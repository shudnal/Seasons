using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using static Seasons.ZoneSystemVariantController;

namespace Seasons
{
    /// <summary>The original Floating driver, with four wave probes and a small missing-water safeguard.</summary>
    internal static class SeasonalIceFloeWaves
    {
        private const float RecoveryInterval = 0.5f;
        private const int BobBudget = 16;
        private const float BobSmoothingSeconds = 0.15f;
        private const float PointRebuildDegrees = 1f;

        // Prepared for one synchronous set of probes, never retained across physics/sync calls.
        private struct SurfaceContext
        {
            internal float WaterLevel, Offset;
            internal bool UseWaves, HasWorldEdge;
        }

        private sealed class Floe
        {
            internal Floating Floating;
            internal ZNetView View;
            internal ZSyncTransform Sync;
            internal Rigidbody Body;
            internal Transform Root, ColliderTransform;
            internal Vector3[] Points;
            internal float BuildHeading, BuildWind;
            internal bool PointsReady;
            internal WaterVolume Water;
            internal bool WaterObserved;
            internal float CallbackLevel = -10000f;
            internal bool HoldingGravity, BodyGravity, SyncGravity;
            internal bool Distant, SyncPosition, SyncVelocity;
            internal bool Recovered, RecoveryPending;
            internal Vector3 Baseline;
            internal long Owner;
            internal float NextRecovery;
            internal float TargetY;
            internal int BobFrame = -1;
            internal int Index;
        }

        private static readonly Dictionary<Floating, Floe> floaters = new Dictionary<Floating, Floe>();
        private static readonly Dictionary<ZSyncTransform, Floe> syncs = new Dictionary<ZSyncTransform, Floe>();
        private static readonly List<Floe> bobOrder = new List<Floe>();
        private static int bobCursor;
        private static WaterVolume oceanPrefab;
        private static int snapshotCycle = -1, snapshotFrame = -1;
        private static Vector3 windDirection;
        private static Vector4 effectiveWind;
        private static float waveTime, windIntensity, windHeading;
        private static bool snapshotValid;
        // CalcWave coefficients from game 1.0.15, only used before WaterVolume.Awake initializes
        // native tangent storage. Even this fallback calls native CreateWave, not copied wave math.
        private static readonly Vector4[] waves =
        {
            new Vector4(10f, 0.04f, 8f, 0.5f), new Vector4(14.123f, 0.08f, 6f, 0.5f),
            new Vector4(22.312f, 0.1f, 4f, 0.5f), new Vector4(31.42f, 0.2f, 2f, 0.5f),
            new Vector4(35.42f, 0.4f, 1f, 0.5f), new Vector4(38.1223f, 1f, 0.8f, 0.7f),
            new Vector4(41.1223f, 1.2f, 0.6f, 0.8f), new Vector4(51.5123f, 1.3f, 0.4f, 0.9f),
            new Vector4(54.2f, 1.3f, 0.3f, 0.9f), new Vector4(56.123f, 1.5f, 0.2f, 0.9f)
        };
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
                // Floating retains m_waterLevel across disable/re-enable, including our own
                // last fallback. Only a new SetLiquidLevel callback can establish live water.
                Owner = view.GetZDO().GetOwner(), Index = bobOrder.Count
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
            oceanPrefab = null;
            snapshotCycle = snapshotFrame = -1;
            snapshotValid = false;
        }

        private static bool Valid(Floe floe) => floe.Floating && floe.Body && floe.Sync &&
            floe.View && floe.View.IsValid();

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool WaterLevelValid(float level) => Finite(level) && level > -10000f;
        private static bool ContainsCenter(Floe floe, WaterVolume water) => water && water.isActiveAndEnabled &&
            water.m_collider && water.m_collider.enabled && water.m_collider.bounds.Contains(floe.Floating.transform.position);

        private static bool HasWater(Floe floe) => floe.WaterObserved && WaterLevelValid(floe.CallbackLevel) &&
            ContainsCenter(floe, floe.Water);

        private static bool EnsureCenterWater(Floe floe)
        {
            Vector3 position = floe.Root.position;
            if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z))
                return false;
            if (HasWater(floe))
            {
                floe.Floating.m_waterLevel = floe.CallbackLevel;
                return true;
            }
            if (!TrySurface(floe, position, out float surface))
                return false;
            // Do not call SetLiquidLevel: its patched callback is the independent observation above.
            // Native Floating adds m_waterLevelOffset itself, exactly once.
            floe.Floating.m_waterLevel = surface;
            return true;
        }

        private static bool Snapshot()
        {
            if (snapshotCycle == MonoUpdaters.UpdateCount && snapshotFrame == Time.frameCount)
                return snapshotValid;
            snapshotCycle = MonoUpdaters.UpdateCount;
            snapshotFrame = Time.frameCount;
            snapshotValid = false;
            if (!EnvMan.instance || !ZNet.instance || !ZoneSystem.instance)
                return false;
            if (!oceanPrefab)
            {
                Transform water = ZoneSystem.instance.m_zonePrefab?.transform.Find("Water");
                oceanPrefab = water ? water.GetComponentInChildren<WaterVolume>(true) : null;
            }
            if (!oceanPrefab)
                return false;
            windDirection = EnvMan.instance.GetWindDir();
            windDirection.y = 0f;
            windIntensity = EnvMan.instance.GetWindIntensity();
            waveTime = (float)ZNet.instance.GetWrappedDayTimeSeconds();
            if (!Finite(windDirection.x) || !Finite(windDirection.z) || !Finite(windIntensity) || !Finite(waveTime))
                return false;
            windDirection.Normalize();
            windHeading = windDirection.sqrMagnitude > 0f
                ? Mathf.Atan2(windDirection.x, windDirection.z) * Mathf.Rad2Deg : float.NaN;
            // Use the public effective direction/intensity once per cycle. This approximates native
            // two-wave wind-transition blending with one effective wind. GetWindIntensity already
            // includes Seasons' multiplier and other accessor patches; never apply it a second time.
            effectiveWind = new Vector4(windDirection.x, 0f, windDirection.z, windIntensity);
            return snapshotValid = true;
        }

        private static bool TrySurfaceContext(Floe floe, out SurfaceContext context)
        {
            context = default;
            if (!Snapshot())
                return false;
            // Resolve the shared surface policy once for all four probes in this call.
            // Their positions and wave heights remain independent and are never cached here.
            WaterVolume water = ContainsCenter(floe, floe.Water) ? floe.Water : null;
            context.WaterLevel = ZoneSystem.instance.m_waterLevel;
            context.Offset = water ? water.m_surfaceOffset : oceanPrefab.m_surfaceOffset - (IsWaterSurfaceFrozen() ? _winterWaterSurfaceOffset : 0f);
            context.UseWaves = water ? water.m_useGlobalWind : oceanPrefab.m_useGlobalWind && !IsWaterSurfaceFrozen();
            context.HasWorldEdge = (water ? water.m_forceDepth : oceanPrefab.m_forceDepth) < 0f;
            return true;
        }

        private static bool TrySurface(Floe floe, Vector3 position, out float surface)
        {
            surface = -10000f;
            return TrySurfaceContext(floe, out SurfaceContext context) && TrySurface(context, position, out surface);
        }

        private static bool TrySurface(SurfaceContext context, Vector3 position, out float surface)
        {
            surface = -10000f;
            if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z))
                return false;
            // The prefab is an existing native component used solely as a math receiver. Its ocean
            // defaults apply without loaded volumes; never borrow another volume's depth or height.
            float wave = 0f;
            if (context.UseWaves)
            {
                float big = 1f - (float)WorldGenerator.DeepNorthWaveFade(position.x, position.z);
                // Normalized depth 1 is the intentional Ocean approximation, including coastal floes.
                if (WaterVolume.s_createWaveTangents != null)
                    wave = oceanPrefab.CalcWave(position, 1f, effectiveWind, waveTime, 1f, big);
                else
                {
                    for (int i = 0; i < waves.Length; i++)
                    {
                        Vector4 parameters = waves[i];
                        Vector2 direction = i == 0 ? new Vector2(windDirection.x, windDirection.z) : WaterVolume.s_createWaveDirections[i];
                        wave += oceanPrefab.CreateWave(position, waveTime / 20f, parameters.x, parameters.y,
                            parameters.z * (i < 6 ? big : 1f), direction, new Vector2(-direction.y, direction.x), parameters.w);
                    }
                    wave *= windIntensity;
                }
            }
            surface = context.WaterLevel + context.Offset + wave;
            if (context.HasWorldEdge && Utils.LengthXZ(position) > 10500f)
                surface -= 100f;
            return WaterLevelValid(surface);
        }

        private static bool BeyondWater(Floe floe)
        {
            if (!GameCamera.instance || !ZoneSystem.instance || !ZNet.instance || ZNet.instance.IsDedicated() || WaterDistance <= 0f)
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
            {
                // A remote snapshot can arrive after the last bob, including ownership acquisition.
                // Only a continuously authoritative owner may restore its cached physical pose.
                bool sameOwner = floe.View && floe.View.IsValid() && floe.View.IsOwner() &&
                    floe.View.GetZDO().GetOwner() == floe.Owner && floe.Sync && floe.Sync.m_wasOwner;
                Vector3 position = !sameOwner && floe.View && floe.View.IsValid()
                    ? floe.View.GetZDO().GetPosition() : floe.Baseline;
                if (sameOwner)
                {
                    position.x = floe.Body.position.x;
                    position.z = floe.Body.position.z;
                }
                floe.Body.position = position;
            }
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
            if (!TrySurface(floe, probe, out float water))
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
            // Publish only this verified recovery, including an existing distant owner whose normal
            // position sync is disabled. Acquisition must not adopt the old invalid saved height.
            floe.View.GetZDO().SetPosition(position);
            floe.RecoveryPending = adoptingPosition;
            floe.Recovered = true;
        }

        private static void EnterDistant(Floe floe)
        {
            if (floe.Distant)
                return;
            floe.Baseline = floe.Body.position;
            floe.TargetY = floe.Baseline.y;
            floe.SyncPosition = floe.Sync.m_syncPosition;
            floe.SyncVelocity = floe.Sync.m_syncBodyVelocity;
            floe.Sync.m_syncPosition = false;
            floe.Sync.m_syncBodyVelocity = false;
            floe.Distant = true;
            HoldGravity(floe);
        }

        private static void Bob(Floe floe)
        {
            if (!floe.Distant || Game.IsPaused() || Time.timeScale <= 0f)
                return;
            Camera camera = GameCamera.instance ? GameCamera.instance.m_camera : null;
            if (!camera || (camera.transform.position - floe.Baseline).sqrMagnitude > camera.farClipPlane * camera.farClipPlane)
                return;
            // Remote baselines follow authoritative changes; cosmetic height never becomes the next baseline.
            if (!floe.View.IsOwner())
                floe.Baseline = floe.View.GetZDO().GetPosition();
            Vector3 position = floe.Baseline;
            if (TrySurface(floe, position, out float surface))
            {
                float waterLevel = ZoneSystem.instance.m_waterLevel;
                float displacement = surface - waterLevel;
                float target = waterLevel + floe.Floating.m_waterLevelOffset + Dampen(displacement);
                if (Finite(target))
                    floe.TargetY = target;
            }
        }

        private static float Dampen(float value) => value / (1f + Mathf.Abs(value));

        private static void ApplyBob(Floe floe)
        {
            if (!floe.Distant || floe.BobFrame == Time.frameCount || Game.IsPaused() || Time.timeScale <= 0f)
                return;
            floe.BobFrame = Time.frameCount;
            // The bounded sampler changes only the target. Cheap interpolation in the existing sync
            // callback avoids visible height steps when many floes share the sampling budget.
            if (!floe.View.IsOwner())
                floe.Baseline = floe.View.GetZDO().GetPosition();
            else
            {
                // Preserve real horizontal movement; the cosmetic operation changes height only.
                floe.Baseline.x = floe.Body.position.x;
                floe.Baseline.z = floe.Body.position.z;
            }
            Vector3 position = floe.Baseline;
            position.y = Mathf.Lerp(floe.Body.position.y, floe.TargetY, 1f - Mathf.Exp(-Time.deltaTime / BobSmoothingSeconds));
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

        private static void AddProbeForce(Floe floe, SurfaceContext context, Vector3 position, float fixedDeltaTime)
        {
            if (!TrySurface(context, position, out float water))
                return;
            float depthDelta = position.y - water;
            float forceAmount = 0.5f * Mathf.Clamp01(Mathf.Abs(depthDelta / 4f)) * (fixedDeltaTime * 50f) * Mathf.Abs(depthDelta);
            Vector3 force = depthDelta < 0f ? Vector3.up * (forceAmount * 0.6f) : Vector3.down * forceAmount;
            floe.Body.AddForceAtPosition(force * 0.02f * floe.Body.mass * 0.25f, position, ForceMode.Impulse);
        }

        private static bool EnsurePoints(Floe floe, Collider collider)
        {
            if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy)
                return false;
            Vector3 forward = floe.Root.forward;
            float heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            if (!Finite(heading) || !Finite(windHeading) || forward.x * forward.x + forward.z * forward.z < 0.000001f)
                return floe.PointsReady;
            // Floe geometry is fixed after placement. Only horizontal floe/wind headings
            // invalidate supports; compare with the last build so small changes accumulate.
            if (floe.PointsReady && Mathf.Abs(Mathf.DeltaAngle(floe.BuildHeading, heading)) < PointRebuildDegrees &&
                Mathf.Abs(Mathf.DeltaAngle(floe.BuildWind, windHeading)) < PointRebuildDegrees)
                return true;
            floe.PointsReady = false;
            floe.ColliderTransform = collider.transform;
            if (floe.Points == null)
                floe.Points = new Vector3[4];
            Vector3 center = floe.Body.worldCenterOfMass;
            Vector3 side = Vector3.Cross(windDirection, floe.Root.up);
            floe.Points[0] = floe.ColliderTransform.InverseTransformPoint(collider.ClosestPoint(center + windDirection * 100f));
            floe.Points[1] = floe.ColliderTransform.InverseTransformPoint(collider.ClosestPoint(center - windDirection * 100f));
            floe.Points[2] = floe.ColliderTransform.InverseTransformPoint(collider.ClosestPoint(center + side * 100f));
            floe.Points[3] = floe.ColliderTransform.InverseTransformPoint(collider.ClosestPoint(center - side * 100f));
            for (int i = 0; i < floe.Points.Length; i++)
                if (!Finite(floe.Points[i].x) || !Finite(floe.Points[i].y) || !Finite(floe.Points[i].z))
                    return false;
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
            if (TrySurfaceContext(floe, out SurfaceContext context) && EnsurePoints(floe, collider))
            {
                floe.Body.WakeUp();
                for (int i = 0; i < floe.Points.Length; i++)
                    AddProbeForce(floe, context, floe.ColliderTransform.TransformPoint(floe.Points[i]), fixedDeltaTime);
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
                if (EnsureCenterWater(floe))
                    RestoreGravity(floe);
                else
                    HoldGravity(floe);
            }
            ApplyBob(floe);
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
                // which uses the mathematical ocean surface before holding gravity.
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
