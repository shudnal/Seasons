using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using static Seasons.ZoneSystemVariantController;

namespace Seasons
{
    /// <summary>The original Floating driver, with four wave probes and a small missing-water safeguard.</summary>
    internal static class SeasonalIceFloeWaves
    {
        private const float BobInterval = 0.25f;
        private const float BobAmplitude = 0.35f;
        private const float RecoveryInterval = 0.5f;
        private const float CenterProbeInterval = 0.5f;
        private const int BobBudget = 16;
        private const float PointRebuildDegrees = 1f;

        private struct Geometry
        {
            internal Vector3 Center, Size, Scale, RelativePosition, CenterOfMass;
            internal Quaternion RelativeRotation;
            internal Mesh Mesh;
            internal Bounds MeshBounds;
            internal int VertexCount;
            internal bool Convex;

            internal bool Matches(Geometry other) => Close(Center, other.Center) && Close(Size, other.Size) &&
                Close(Scale, other.Scale) && Close(RelativePosition, other.RelativePosition) &&
                Close(CenterOfMass, other.CenterOfMass) && Quaternion.Angle(RelativeRotation, other.RelativeRotation) < 0.01f &&
                Mesh == other.Mesh && MeshBounds.Equals(other.MeshBounds) && VertexCount == other.VertexCount && Convex == other.Convex;

            private static bool Close(Vector3 a, Vector3 b) => (a - b).sqrMagnitude <= 0.00000001f;
        }

        private sealed class Floe
        {
            internal Floating Floating;
            internal ZNetView View;
            internal ZSyncTransform Sync;
            internal Rigidbody Body;
            internal Transform Root, ColliderTransform;
            internal Collider Collider;
            internal Vector3[] Points;
            internal Geometry Geometry;
            internal float BuildHeading, BuildWind;
            internal bool PointsReady;
            internal WaterVolume Water, CenterWater;
            internal bool WaterObserved;
            internal float CallbackLevel, CenterLevel = -10000f, NextCenterProbe;
            internal bool HoldingGravity, BodyGravity, SyncGravity;
            internal bool Distant, SyncPosition, SyncVelocity;
            internal bool Recovered, RecoveryPending;
            internal Vector3 Baseline;
            internal long Owner;
            internal float NextBob, NextRecovery;
            internal int Index;
        }

        private static readonly Dictionary<Floating, Floe> floaters = new Dictionary<Floating, Floe>();
        private static readonly Dictionary<ZSyncTransform, Floe> syncs = new Dictionary<ZSyncTransform, Floe>();
        private static readonly List<Floe> bobOrder = new List<Floe>();
        private static int bobCursor;
        internal static float WaterDistance { get; private set; }
        internal static float WaterDistanceSquared { get; private set; }

        internal static void RefreshDistance()
        {
            if (!ZNet.instance || !ZoneSystem.instance)
                return;
            WaterDistance = (float)ZNet.instance.GetSyncedSimulationDistance().NearSimulationDistance
                * ZoneSystem.instance.m_zoneSize;
            WaterDistanceSquared = WaterDistance * WaterDistance;
        }

        internal static void Track(Floating floating)
        {
            if (!floating || floaters.ContainsKey(floating))
                return;
            ZNetView view = floating.m_nview;
            if (!view || !view.IsValid() || view.GetZDO().GetPrefab() != s_iceFloePrefab ||
                !view.GetZDO().GetBool(SeasonsVars.s_iceFloeWatermark) || !floating.m_body)
                return;
            ZSyncTransform sync = floating.GetComponent<ZSyncTransform>();
            if (!sync)
                return;
            Floe floe = new Floe
            {
                Floating = floating, View = view, Sync = sync, Body = floating.m_body, Root = floating.transform,
                CallbackLevel = floating.m_waterLevel,
                Owner = view.GetZDO().GetOwner(), Index = bobOrder.Count, NextBob = Time.time + (floaters.Count % 8) * BobInterval / 8f
            };
            floaters.Add(floating, floe);
            syncs.Add(sync, floe);
            bobOrder.Add(floe);
        }

        internal static void Untrack(Floating floating)
        {
            if (ReferenceEquals(floating, null) || !floaters.TryGetValue(floating, out Floe floe))
                return;
            Restore(floe);
            floaters.Remove(floating);
            syncs.Remove(floe.Sync);
            int last = bobOrder.Count - 1;
            bobOrder[floe.Index] = bobOrder[last];
            bobOrder[floe.Index].Index = floe.Index;
            bobOrder.RemoveAt(last);
        }

        internal static void Reset()
        {
            foreach (Floe floe in floaters.Values)
                Restore(floe);
            floaters.Clear();
            syncs.Clear();
            WaterDistance = WaterDistanceSquared = 0f;
            bobOrder.Clear();
            bobCursor = 0;
        }

        private static bool Valid(Floe floe) => floe.Floating && floe.Body && floe.Sync &&
            floe.View && floe.View.IsValid();

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool WaterLevelValid(float level) => Finite(level) && level > -10000f;
        private static bool ContainsCenter(Floe floe, WaterVolume water) => water && water.isActiveAndEnabled &&
            water.m_collider && water.m_collider.enabled && water.m_collider.bounds.Contains(floe.Floating.transform.position);

        private static bool HasWater(Floe floe) => WaterLevelValid(floe.CallbackLevel) &&
            (!floe.WaterObserved || ContainsCenter(floe, floe.Water));

        private static bool EnsureCenterWater(Floe floe)
        {
            Vector3 position = floe.Floating.transform.position;
            if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z))
                return false;
            if (HasWater(floe))
            {
                floe.Floating.m_waterLevel = floe.CallbackLevel;
                return true;
            }
            // A scaled collider may overlap a neighbor whose last callback does not cover the root.
            // Retry only a near owner's missing center, also allowing a sleeping body to recover.
            if (Time.time >= floe.NextCenterProbe)
            {
                floe.NextCenterProbe = Time.time + CenterProbeInterval;
                floe.CenterWater = null;
                floe.CenterLevel = Floating.GetWaterLevel(position, ref floe.CenterWater);
            }
            if (!WaterLevelValid(floe.CenterLevel) || (!ReferenceEquals(floe.CenterWater, null) && !ContainsCenter(floe, floe.CenterWater)))
                return false;
            // Do not call SetLiquidLevel: its patched callback is the independent observation above.
            floe.Floating.m_waterLevel = floe.CenterLevel;
            return true;
        }

        private static bool BeyondWater(Floe floe)
        {
            if (!GameCamera.instance || !ZoneSystem.instance || WaterDistance <= 0f)
                return false; // A camera-less owner uses actual water availability, not an artificial radius.
            Vector3 delta = GameCamera.instance.transform.position - floe.Floating.transform.position;
            return delta.x * delta.x + delta.z * delta.z > WaterDistanceSquared;
        }

        private static void HoldGravity(Floe floe)
        {
            if (floe.HoldingGravity)
                return;
            floe.HoldingGravity = true;
            floe.BodyGravity = floe.Body.useGravity;
            floe.SyncGravity = floe.Sync.m_useGravity;
            // OwnerSync restores its cached gravity field every late update.
            floe.Sync.m_useGravity = false;
            floe.Body.useGravity = false;
            StopMotion(floe);
        }

        private static void StopMotion(Floe floe)
        {
            if (!floe.Body.isKinematic)
            {
                floe.Body.linearVelocity = Vector3.zero;
                floe.Body.angularVelocity = Vector3.zero;
                floe.Body.Sleep();
            }
        }

        private static void RestoreGravity(Floe floe)
        {
            if (!floe.HoldingGravity)
                return;
            if (floe.Sync)
                floe.Sync.m_useGravity = floe.SyncGravity;
            if (floe.Body)
            {
                // Native ClientSync owns nonowner gravity; a newly authoritative body uses native owner policy.
                floe.Body.useGravity = floe.View && floe.View.IsValid() && floe.View.IsOwner()
                    ? floe.SyncGravity : floe.BodyGravity;
                if (!floe.Body.isKinematic)
                    floe.Body.WakeUp();
            }
            floe.HoldingGravity = false;
        }

        private static void RestoreDistant(Floe floe)
        {
            if (!floe.Distant)
                return;
            if (floe.Body)
                floe.Body.position = floe.Baseline;
            if (floe.Sync)
            {
                floe.Sync.m_syncPosition = floe.SyncPosition;
                floe.Sync.m_syncBodyVelocity = floe.SyncVelocity;
            }
            floe.Distant = false;
        }

        private static void Restore(Floe floe)
        {
            RestoreDistant(floe);
            RestoreGravity(floe);
        }

        private static void ObserveOwner(Floe floe)
        {
            long owner = floe.View.GetZDO().GetOwner();
            if (owner == floe.Owner)
                return;
            // Restore the real pose before native ownership handoff can publish or accept it.
            Restore(floe);
            floe.Owner = owner;
        }

        private static void RecoverInvalidHeight(Floe floe)
        {
            bool adoptingPosition = floe.View.IsOwner() && !floe.Sync.m_wasOwner && floe.Sync.m_syncPosition;
            Vector3 position = adoptingPosition ? floe.View.GetZDO().GetPosition() : floe.Body.position;
            if (floe.Recovered || !floe.View.IsOwner() || !ZoneSystem.instance || Time.time < floe.NextRecovery ||
                (Finite(position.y) && position.y >= -5000f) || !Finite(position.x) || !Finite(position.z))
                return;
            floe.NextRecovery = Time.time + RecoveryInterval;
            Vector3 probe = new Vector3(position.x, ZoneSystem.instance.m_waterLevel, position.z);
            float water = Floating.GetLiquidLevel(probe, type: LiquidType.Water);
            if (!WaterLevelValid(water))
                return;
            Restore(floe);
            position.y = water + floe.Floating.m_waterLevelOffset -
                floe.Floating.transform.TransformVector(floe.Body.centerOfMass).y;
            if (!Finite(position.y))
                return;
            if (!floe.Body.isKinematic)
            {
                floe.Body.linearVelocity = Vector3.zero;
                floe.Body.angularVelocity = Vector3.zero;
            }
            floe.Body.position = position;
            // Native OwnerSync adopts the saved pose after our prefix. Repair that same authoritative
            // pose once, so acquisition cannot overwrite the verified recovery with the invalid height.
            if (adoptingPosition)
            {
                floe.View.GetZDO().SetPosition(position);
                floe.RecoveryPending = true;
            }
            floe.Recovered = true;
        }

        private static void EnterDistant(Floe floe)
        {
            if (floe.Distant)
                return;
            floe.Baseline = floe.Body.position;
            floe.SyncPosition = floe.Sync.m_syncPosition;
            floe.SyncVelocity = floe.Sync.m_syncBodyVelocity;
            floe.Sync.m_syncPosition = false;
            floe.Sync.m_syncBodyVelocity = false;
            floe.Distant = true;
            HoldGravity(floe);
        }

        private static void Bob(Floe floe)
        {
            if (!floe.Distant || Game.IsPaused() || Time.timeScale <= 0f || Time.time < floe.NextBob)
                return;
            Camera camera = GameCamera.instance ? GameCamera.instance.m_camera : null;
            if (!camera || (camera.transform.position - floe.Baseline).sqrMagnitude > camera.farClipPlane * camera.farClipPlane)
                return;
            floe.NextBob = Time.time + BobInterval;
            // Remote baselines follow authoritative changes; cosmetic height never becomes the next baseline.
            if (!floe.View.IsOwner())
                floe.Baseline = floe.View.GetZDO().GetPosition();
            Vector3 position = floe.Baseline;
            position.y += BobAmplitude * Mathf.Sin(WaterVolume.s_waterTime * 0.8f + position.x * 0.013f + position.z * 0.017f);
            if (Finite(position.y))
                floe.Body.position = position;
        }

        private static void UpdateBobs()
        {
            if (Game.IsPaused() || Time.timeScale <= 0f)
                return;
            int visits = Mathf.Min(BobBudget, bobOrder.Count);
            for (int i = 0; i < visits; i++)
            {
                if (bobCursor >= bobOrder.Count)
                    bobCursor = 0;
                Floe floe = bobOrder[bobCursor++];
                if (Valid(floe))
                    Bob(floe);
            }
        }

        private static void AddProbeForce(Floe floe, Vector3 position, float fixedDeltaTime)
        {
            float water = Floating.GetLiquidLevel(position, type: LiquidType.Water);
            if (!WaterLevelValid(water))
                return;
            float depthDelta = position.y - water;
            float forceAmount = 0.5f * Mathf.Clamp01(Mathf.Abs(depthDelta / 4f)) * (fixedDeltaTime * 50f) * Mathf.Abs(depthDelta);
            Vector3 force = depthDelta < 0f ? Vector3.up * (forceAmount * 0.6f) : Vector3.down * forceAmount;
            floe.Body.AddForceAtPosition(force * 0.02f * floe.Body.mass * 0.25f, position, ForceMode.Impulse);
        }

        private static bool ReadGeometry(Floe floe, Collider collider, out Geometry geometry)
        {
            Transform transform = collider.transform;
            geometry = new Geometry
            {
                Scale = transform.lossyScale,
                RelativePosition = floe.Root.InverseTransformPoint(transform.position),
                RelativeRotation = Quaternion.Inverse(floe.Root.rotation) * transform.rotation,
                CenterOfMass = floe.Body.centerOfMass
            };
            if (collider is BoxCollider box)
            {
                geometry.Center = box.center;
                geometry.Size = box.size;
            }
            else if (collider is SphereCollider sphere)
            {
                geometry.Center = sphere.center;
                geometry.Size = new Vector3(sphere.radius, 0f, 0f);
            }
            else if (collider is CapsuleCollider capsule)
            {
                geometry.Center = capsule.center;
                geometry.Size = new Vector3(capsule.radius, capsule.height, capsule.direction);
            }
            else if (collider is MeshCollider mesh)
            {
                geometry.Mesh = mesh.sharedMesh;
                if (!geometry.Mesh)
                    return false;
                // Native floe collider meshes are immutable assets. Do not allocate vertex arrays to
                // detect arbitrary same-bounds in-place mesh edits by another mod.
                geometry.MeshBounds = geometry.Mesh.bounds;
                geometry.VertexCount = geometry.Mesh.vertexCount;
                geometry.Convex = mesh.convex;
            }
            else
                return false;
            return Finite(geometry.Scale.x) && Finite(geometry.Scale.y) && Finite(geometry.Scale.z) &&
                geometry.Scale.x != 0f && geometry.Scale.y != 0f && geometry.Scale.z != 0f;
        }

        private static bool EnsurePoints(Floe floe, Collider collider, Vector3 wind)
        {
            if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy)
            {
                floe.PointsReady = false;
                return false;
            }
            if (!ReadGeometry(floe, collider, out Geometry geometry))
                return floe.PointsReady = false;
            Quaternion rotation = floe.Root.rotation;
            float twist = rotation.y * rotation.y + rotation.w * rotation.w;
            wind.y = 0f;
            // A degenerate heading/wind cannot define new supports; retain only a still-valid geometry.
            bool sameGeometry = floe.PointsReady && floe.Collider == collider && floe.Geometry.Matches(geometry);
            if (!Finite(twist) || twist < 0.000001f || !Finite(wind.sqrMagnitude) || wind.sqrMagnitude < 0.000001f)
                return sameGeometry;
            wind.Normalize();
            // Y twist discards ordinary pitch/roll. Compare to the last build, not the previous tick,
            // so slow cumulative yaw and the 359/0 degree boundary both invalidate at one degree.
            float heading = 2f * Mathf.Atan2(rotation.y, rotation.w) * Mathf.Rad2Deg;
            float windHeading = Mathf.Atan2(wind.x, wind.z) * Mathf.Rad2Deg;
            if (sameGeometry && Mathf.Abs(Mathf.DeltaAngle(floe.BuildHeading, heading)) < PointRebuildDegrees &&
                Mathf.Abs(Mathf.DeltaAngle(floe.BuildWind, windHeading)) < PointRebuildDegrees)
                return true;
            floe.PointsReady = false;
            floe.Collider = collider;
            floe.ColliderTransform = collider.transform;
            if (floe.Points == null)
                floe.Points = new Vector3[4];
            Vector3 center = floe.Body.worldCenterOfMass;
            Vector3 side = Vector3.Cross(wind, floe.Root.up);
            floe.Points[0] = floe.ColliderTransform.InverseTransformPoint(collider.ClosestPoint(center + wind * 100f));
            floe.Points[1] = floe.ColliderTransform.InverseTransformPoint(collider.ClosestPoint(center - wind * 100f));
            floe.Points[2] = floe.ColliderTransform.InverseTransformPoint(collider.ClosestPoint(center + side * 100f));
            floe.Points[3] = floe.ColliderTransform.InverseTransformPoint(collider.ClosestPoint(center - side * 100f));
            for (int i = 0; i < floe.Points.Length; i++)
                if (!Finite(floe.Points[i].x) || !Finite(floe.Points[i].y) || !Finite(floe.Points[i].z))
                    return false;
            floe.Geometry = geometry;
            floe.BuildHeading = heading;
            floe.BuildWind = windHeading;
            return floe.PointsReady = true;
        }

        private static bool BeforeFloating(Floating floating, float fixedDeltaTime)
        {
            if (!floaters.TryGetValue(floating, out Floe floe) || !Valid(floe))
                return true;
            ObserveOwner(floe);
            RecoverInvalidHeight(floe);
            if (BeyondWater(floe) && (!floe.View.IsOwner() || floe.Sync.m_wasOwner))
            {
                EnterDistant(floe);
                return false;
            }
            RestoreDistant(floe);
            if (!floe.View.IsOwner())
            {
                RestoreGravity(floe);
                return true;
            }
            if (!EnsureCenterWater(floe))
            {
                HoldGravity(floe);
                floating.SetSurfaceEffect(false);
                return false;
            }
            RestoreGravity(floe);
            if (floe.Body.isKinematic)
                return false; // Never run native Floating's velocity damping on a kinematic body.
            Collider collider = floating.m_collider;
            if (!collider)
            {
                HoldGravity(floe);
                return false;
            }
            Vector3 wind = EnvMan.instance ? EnvMan.instance.GetWindDir() : Vector3.zero;
            if (EnsurePoints(floe, collider, wind))
            {
                floe.Body.WakeUp();
                for (int i = 0; i < floe.Points.Length; i++)
                    AddProbeForce(floe, floe.ColliderTransform.TransformPoint(floe.Points[i]), fixedDeltaTime);
            }
            return true;
        }

        internal static void PrepareInteraction(Floating floating)
        {
            if (floating && floaters.TryGetValue(floating, out Floe floe) && Valid(floe))
                Restore(floe);
        }

        private static void BeforeSync(ZSyncTransform sync)
        {
            if (!syncs.TryGetValue(sync, out Floe floe) || !Valid(floe))
                return;
            ObserveOwner(floe);
            RecoverInvalidHeight(floe);
            if (floe.Distant && (!BeyondWater(floe) || (floe.View.IsOwner() && !sync.m_wasOwner)))
                RestoreDistant(floe);
            if (!floe.Distant && floe.View.IsOwner())
            {
                bool beyondWater = BeyondWater(floe);
                if ((!beyondWater && EnsureCenterWater(floe)) || (beyondWater && HasWater(floe)))
                    RestoreGravity(floe);
                else
                    HoldGravity(floe);
            }
        }

        [HarmonyPatch(typeof(Floating), nameof(Floating.CustomFixedUpdate))]
        private static class Floating_CustomFixedUpdate_IceFloeRotation
        {
            private static bool Prefix(Floating __instance, float fixedDeltaTime) => BeforeFloating(__instance, fixedDeltaTime);
        }

        [HarmonyPatch(typeof(Floating), nameof(Floating.OnEnable))]
        private static class Floating_OnEnable_IceFloe
        {
            private static void Postfix(Floating __instance)
            {
                IceFloeClimb climb = __instance.GetComponent<IceFloeClimb>();
                if (climb && climb.Started)
                    Track(__instance);
            }
        }

        [HarmonyPatch(typeof(Floating), nameof(Floating.OnDisable))]
        private static class Floating_OnDisable_IceFloe
        {
            private static void Postfix(Floating __instance) => Untrack(__instance);
        }

        [HarmonyPatch(typeof(Floating), nameof(Floating.SetLiquidLevel))]
        private static class Floating_SetLiquidLevel_IceFloe
        {
            private static void Postfix(Floating __instance, float level, LiquidType type, Component liquidObj)
            {
                if (type != LiquidType.Water || !floaters.TryGetValue(__instance, out Floe floe) || !Valid(floe))
                    return;
                floe.CallbackLevel = level;
                floe.Water = liquidObj as WaterVolume;
                floe.WaterObserved = liquidObj is WaterVolume;
                if (!floe.Distant && floe.View.IsOwner() && HasWater(floe))
                    RestoreGravity(floe);
                // Missing/neighbor callbacks are resolved by the next fixed/sync safeguard,
                // which can reuse or refresh the bounded center fallback before holding gravity.
            }
        }

        [HarmonyPatch(typeof(Floating), nameof(Floating.TerrainCheck))]
        private static class Floating_TerrainCheck_IceFloe
        {
            private static bool Prefix(Floating __instance) => !floaters.TryGetValue(__instance, out Floe floe) ||
                (!floe.Distant && !floe.Body.isKinematic);
        }

        [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.OwnerSync))]
        private static class ZSyncTransform_OwnerSync_IceFloe
        {
            private static void Prefix(ZSyncTransform __instance, out bool __state)
            {
                bool acquiring = syncs.TryGetValue(__instance, out Floe floe) && Valid(floe) &&
                    floe.View.IsOwner() && !__instance.m_wasOwner;
                BeforeSync(__instance);
                __state = acquiring && (floe.HoldingGravity || floe.RecoveryPending);
            }

            private static void Postfix(ZSyncTransform __instance, bool __state)
            {
                // Native ownership acquisition restores saved velocities after the prefix safeguard.
                if (__state && syncs.TryGetValue(__instance, out Floe floe) && Valid(floe))
                {
                    StopMotion(floe);
                    floe.RecoveryPending = false;
                }
            }
        }

        [HarmonyPatch(typeof(MonoUpdaters), nameof(MonoUpdaters.LateUpdate))]
        private static class MonoUpdaters_LateUpdate_IceFloeBob
        {
            private static void Postfix() => UpdateBobs();
        }

        [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.ClientSync))]
        private static class ZSyncTransform_ClientSync_IceFloe
        {
            private static void Prefix(ZSyncTransform __instance) => BeforeSync(__instance);
        }

        [HarmonyPatch(typeof(Water), nameof(Water.ApplySettings))]
        private static class Water_ApplySettings_Distance
        {
            private static void Postfix() => RefreshDistance();
        }

        [HarmonyPatch(typeof(Water), nameof(Water.ApplySettingsOnAll))]
        private static class Water_ApplySettingsOnAll_Distance
        {
            private static void Postfix() => RefreshDistance();
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
        private static class ZoneSystem_Start_Waves
        {
            private static void Postfix() => RefreshDistance();
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.OnDestroy))]
        private static class ZoneSystem_OnDestroy_Waves
        {
            private static void Prefix() => Reset();
        }
    }
}
