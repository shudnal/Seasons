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

        private sealed class Floe
        {
            internal Floating Floating;
            internal ZNetView View;
            internal ZSyncTransform Sync;
            internal Rigidbody Body;
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
                Floating = floating, View = view, Sync = sync, Body = floating.m_body,
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
            if (HasWater(floe))
            {
                floe.Floating.m_waterLevel = floe.CallbackLevel;
                return true;
            }
            Vector3 position = floe.Floating.transform.position;
            if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z))
                return false;
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
            if (!collider.enabled || !collider.gameObject.activeInHierarchy)
                return true; // Keep native buoyancy; health/variant collider enablement is not ours to override.
            Vector3 wind = WaterVolume.s_globalWindAlpha == 0f ? WaterVolume.s_globalWind1 :
                Vector4.Lerp(WaterVolume.s_globalWind1, WaterVolume.s_globalWind2, WaterVolume.s_globalWindAlpha);
            Vector3 side = Vector3.Cross(wind, floating.transform.up);
            Vector3 center = floe.Body.worldCenterOfMass;
            floe.Body.WakeUp();
            AddProbeForce(floe, collider.ClosestPoint(center + wind * 100f), fixedDeltaTime);
            AddProbeForce(floe, collider.ClosestPoint(center - wind * 100f), fixedDeltaTime);
            AddProbeForce(floe, collider.ClosestPoint(center + side * 100f), fixedDeltaTime);
            AddProbeForce(floe, collider.ClosestPoint(center - side * 100f), fixedDeltaTime);
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
