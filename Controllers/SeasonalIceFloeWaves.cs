using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using static Seasons.ZoneSystemVariantController;

namespace Seasons
{
    // Shared inputs and native callback routing only. Per-floe state and decisions live on IceFloeClimb.
    internal static class SeasonalIceFloeWaves
    {
        internal struct SurfaceContext
        {
            internal float WaterLevel, Offset;
            internal bool UseWaves, HasWorldEdge;
        }

        private static readonly Dictionary<Floating, IceFloeClimb> floaters = new Dictionary<Floating, IceFloeClimb>();
        private static readonly Dictionary<ZSyncTransform, IceFloeClimb> syncs = new Dictionary<ZSyncTransform, IceFloeClimb>();
        private static readonly List<IceFloeClimb> bobOrder = new List<IceFloeClimb>();
        private static int bobCursor;
        private static WaterVolume oceanPrefab;
        private static int snapshotCycle = -1, snapshotFrame = -1;
        internal static Vector3 WindDirection { get; private set; }
        internal static float WindHeading { get; private set; }
        internal static float WindIntensity { get; private set; }
        internal static float WaveTime { get; private set; }
        private static Vector4 effectiveWind;
        private static bool snapshotValid;
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
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        internal static bool WaterLevelValid(float level) => Finite(level) && level > -10000f;

        internal static void RefreshDistance()
        {
            if (!ZNet.instance || !ZoneSystem.instance)
                return;
            WaterDistance = (float)ZNet.instance.GetSyncedSimulationDistance().NearSimulationDistance * ZoneSystem.instance.m_zoneSize;
            WaterDistanceSquared = WaterDistance * WaterDistance;
        }

        internal static void Track(Floating floating)
        {
            if (!floating || !floating.isActiveAndEnabled || floaters.ContainsKey(floating))
                return;
            IceFloeClimb controller = floating.GetComponent<IceFloeClimb>();
            if (!controller || !controller.Started || !controller.isActiveAndEnabled || !controller.InitializeWaves(floating))
                return;
            controller.WaveIndex = bobOrder.Count;
            floaters.Add(floating, controller);
            syncs.Add(controller.Sync, controller);
            bobOrder.Add(controller);
        }

        internal static void Untrack(Floating floating)
        {
            if (ReferenceEquals(floating, null) || !floaters.TryGetValue(floating, out IceFloeClimb controller))
                return;
            controller.ReleaseWaves();
            floaters.Remove(floating);
            syncs.Remove(controller.Sync);
            int last = bobOrder.Count - 1;
            bobOrder[controller.WaveIndex] = bobOrder[last];
            bobOrder[controller.WaveIndex].WaveIndex = controller.WaveIndex;
            bobOrder.RemoveAt(last);
            controller.WaveIndex = -1;
        }

        internal static void Reset()
        {
            foreach (IceFloeClimb controller in floaters.Values)
                if (controller)
                    controller.ReleaseWaves();
            floaters.Clear();
            syncs.Clear();
            bobOrder.Clear();
            bobCursor = 0;
            WaterDistance = WaterDistanceSquared = 0f;
            oceanPrefab = null;
            snapshotCycle = snapshotFrame = -1;
            snapshotValid = false;
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
            Vector3 direction = EnvMan.instance.GetWindDir();
            direction.y = 0f;
            WindIntensity = EnvMan.instance.GetWindIntensity();
            WaveTime = (float)ZNet.instance.GetWrappedDayTimeSeconds();
            if (!Finite(direction) || !Finite(WindIntensity) || !Finite(WaveTime))
                return false;
            direction.Normalize();
            WindDirection = direction;
            WindHeading = direction.sqrMagnitude > 0f ? Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg : float.NaN;
            // Preserve the agreed effective-wind approximation, including patched EnvMan accessors.
            effectiveWind = new Vector4(direction.x, 0f, direction.z, WindIntensity);
            return snapshotValid = true;
        }

        internal static bool TrySurfaceContext(IceFloeClimb controller, out SurfaceContext context)
        {
            context = default;
            if (!Snapshot())
                return false;
            WaterVolume water = controller.ContainsCenter(controller.Water) ? controller.Water : null;
            context.WaterLevel = ZoneSystem.instance.m_waterLevel;
            context.Offset = water ? water.m_surfaceOffset : oceanPrefab.m_surfaceOffset - (IsWaterSurfaceFrozen() ? _winterWaterSurfaceOffset : 0f);
            context.UseWaves = water ? water.m_useGlobalWind : oceanPrefab.m_useGlobalWind && !IsWaterSurfaceFrozen();
            context.HasWorldEdge = (water ? water.m_forceDepth : oceanPrefab.m_forceDepth) < 0f;
            return true;
        }

        internal static bool TrySurface(IceFloeClimb controller, Vector3 position, out float surface)
        {
            surface = -10000f;
            return TrySurfaceContext(controller, out SurfaceContext context) && TrySurface(context, position, out surface);
        }

        internal static bool TrySurface(SurfaceContext context, Vector3 position, out float surface)
        {
            surface = -10000f;
            if (!Finite(position))
                return false;
            float wave = 0f;
            if (context.UseWaves)
            {
                float big = 1f - (float)WorldGenerator.DeepNorthWaveFade(position.x, position.z);
                if (WaterVolume.s_createWaveTangents != null)
                    wave = oceanPrefab.CalcWave(position, 1f, effectiveWind, WaveTime, 1f, big);
                else
                {
                    // Preserve native CreateWave fallback before native tangent storage exists.
                    for (int i = 0; i < waves.Length; i++)
                    {
                        Vector4 parameters = waves[i];
                        Vector2 direction = i == 0 ? new Vector2(WindDirection.x, WindDirection.z) : WaterVolume.s_createWaveDirections[i];
                        wave += oceanPrefab.CreateWave(position, WaveTime / 20f, parameters.x, parameters.y,
                            parameters.z * (i < 6 ? big : 1f), direction, new Vector2(-direction.y, direction.x), parameters.w);
                    }
                    wave *= WindIntensity;
                }
            }
            surface = context.WaterLevel + context.Offset + wave;
            if (context.HasWorldEdge && Utils.LengthXZ(position) > 10500f)
                surface -= 100f;
            return WaterLevelValid(surface);
        }

        internal static void PrepareInteraction(Floating floating)
        {
            if (floating && floaters.TryGetValue(floating, out IceFloeClimb controller) && controller.WaveValid)
                controller.RestoreWaves();
        }

        [HarmonyPatch(typeof(Floating), nameof(Floating.CustomFixedUpdate))]
        private static class Floating_CustomFixedUpdate_IceFloeRotation
        {
            private static bool Prefix(Floating __instance, float fixedDeltaTime) =>
                !floaters.TryGetValue(__instance, out IceFloeClimb controller) || !controller.WaveValid ||
                controller.BeforeFloating(fixedDeltaTime);
        }

        [HarmonyPatch(typeof(Floating), nameof(Floating.OnEnable))]
        private static class Floating_OnEnable_IceFloe
        {
            private static void Postfix(Floating __instance) => Track(__instance);
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
                if (type == LiquidType.Water && floaters.TryGetValue(__instance, out IceFloeClimb controller) && controller.WaveValid)
                    controller.ObserveWater(level, liquidObj);
            }
        }

        [HarmonyPatch(typeof(Floating), nameof(Floating.TerrainCheck))]
        private static class Floating_TerrainCheck_IceFloe
        {
            private static bool Prefix(Floating __instance) => !floaters.TryGetValue(__instance, out IceFloeClimb controller) ||
                !controller.WaveValid || (!controller.Distant && !controller.Body.isKinematic);
        }

        [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.OwnerSync))]
        private static class ZSyncTransform_OwnerSync_IceFloe
        {
            private static void Prefix(ZSyncTransform __instance, out bool __state)
            {
                __state = false;
                if (!syncs.TryGetValue(__instance, out IceFloeClimb controller) || !controller.WaveValid)
                    return;
                bool acquiring = controller.m_view.IsOwner() && !__instance.m_wasOwner;
                controller.BeforeSync();
                __state = acquiring && (controller.HoldingGravity || controller.RecoveryPending);
            }

            private static void Postfix(ZSyncTransform __instance, bool __state)
            {
                if (__state && syncs.TryGetValue(__instance, out IceFloeClimb controller) && controller.WaveValid)
                {
                    controller.StopMotion();
                    controller.RecoveryPending = false;
                }
            }
        }

        [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.ClientSync))]
        private static class ZSyncTransform_ClientSync_IceFloe
        {
            private static void Prefix(ZSyncTransform __instance)
            {
                if (syncs.TryGetValue(__instance, out IceFloeClimb controller) && controller.WaveValid)
                    controller.BeforeSync();
            }
        }

        [HarmonyPatch(typeof(MonoUpdaters), nameof(MonoUpdaters.LateUpdate))]
        private static class MonoUpdaters_LateUpdate_IceFloeBob
        {
            private static void Postfix()
            {
                if (Game.IsPaused() || Time.timeScale <= 0f)
                    return;
                int visits = Mathf.Min(16, bobOrder.Count);
                for (int i = 0; i < visits; i++)
                {
                    if (bobCursor >= bobOrder.Count)
                        bobCursor = 0;
                    IceFloeClimb controller = bobOrder[bobCursor++];
                    if (controller.WaveValid)
                        controller.UpdateBobTarget();
                }
            }
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.UpdateCrosshair))]
        private static class Hud_UpdateCrosshair_FloeDiagnostics
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Hud __instance, Player player)
            {
                if (!player || !__instance.m_hoverName || (TextViewer.instance && TextViewer.instance.IsVisible()))
                    return;
                GameObject target = player.GetHoverObject();
                if (!target)
                    return;
                IceFloeClimb controller = target.GetComponentInParent<IceFloeClimb>();
                if (!controller || !controller.ShowDiagnosticsInHover ||
                    target.GetComponentInParent<Hoverable>() is IceFloeClimb)
                    return; // The component's native GetHoverText already appended the same block.
                string text = __instance.m_hoverName.text ?? "";
                controller.AppendHoverDiagnostics(ref text);
                __instance.m_hoverName.text = text;
            }
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

    // Deliberately component-owned during physics diagnosis. No second per-object driver or FixedUpdate.
    public partial class IceFloeClimb
    {
        public enum PointSampling { Cached, FreshClosestPoint, LegacyClosestPoint }
        public enum WaterSampling { Mathematical, NativeLiquidLevel }
        public enum WaveStatus { Unregistered, Ready, Paused, Distant, NonOwner, NoWater, Kinematic, NoCollider, NoPoints, NoSurface, NoValidProbes, ForcesSubmitted }

        [Header("Local diagnostic controls (not saved or synchronized)")]
        public bool ShowDiagnosticsInHover = true;
        public bool DiagnosticsEnabled;
        public bool FreezeDiagnostics;
        public PointSampling PointMode = PointSampling.Cached;
        public WaterSampling WaterMode = WaterSampling.Mathematical;

        [Header("Live floe state")]
        public Rigidbody Body;
        public ZSyncTransform Sync;
        public Transform Root, ColliderTransform;
        public WaterVolume Water;
        public bool Registered, WaterObserved, PointsReady, Distant, HoldingGravity;
        public float CallbackLevel = -10000f;
        // Runtime buffers must not inherit serialized empty arrays from the prefab.
        [NonSerialized] public Vector3[] Points;
        public float BuildHeading, BuildWind;
        public int CacheBuildCount;
        public WaveStatus Status = WaveStatus.Unregistered;
        public int LastRunFrame = -1, LastForceCalls;
        public float LastRunFixedTime;
        public long TotalForceCalls;
        public bool Recovered, RecoveryPending;
        public bool BodyGravity, SyncGravity, SyncPosition, SyncVelocity;
        public Vector3 Baseline;
        public long Owner;
        public float NextRecovery, TargetY;
        public int BobFrame = -1;
        internal int WaveIndex = -1;

        [Serializable]
        public struct ProbeDiagnostics
        {
            public Vector3 LocalPoint, CachedWorld, FreshWorld, LegacyWorld;
            public float BuildRoundTripError, CacheError, CacheErrorXZ, CacheErrorY, LegacyError, OutsideDistance;
            public float CachedMathWater, CachedNativeWater, FreshMathWater, FreshNativeWater, LegacyNativeWater;
            public Vector3 AppliedWorld, AppliedImpulse, AngularImpulse;
            public bool Applied, AppliedWaterValid;
            public float AppliedWater;
        }

        [Serializable]
        public sealed class WaveDiagnostics
        {
            public bool Captured;
            public int Frame = -1, ForceCalls, NativeValidCount;
            public PointSampling PointMode;
            public WaterSampling WaterMode;
            public float FixedTime, FixedDelta, WaveTime, WindIntensity;
            public Vector3 Wind, LegacyWind, CenterOfMass, EulerAngles, AngularVelocity, Velocity, Inertia;
            public Quaternion InertiaRotation;
            public float Mass, AngularDamping, BalanceFraction, Damping, CenterWater;
            public Vector3 CachedMathAngularImpulse, FreshMathAngularImpulse, CachedNativeAngularImpulse, FreshNativeAngularImpulse, LegacyNativeAngularImpulse;
            public Vector3 SubmittedAngularImpulse, EngineAngularImpulse, EngineImpulse;
            public float MaxRoundTripError, MaxCacheErrorXZ, MaxCacheErrorY, MaxOutsideDistance;
            public ProbeDiagnostics[] Probes = new ProbeDiagnostics[4];
        }

        [Header("Last captured physics call (before native Floating / physics simulation)")]
        [NonSerialized] public WaveDiagnostics Diagnostics;
        private readonly float[] buildRoundTripErrors = new float[4];
        private float hoverUntil = -1f, nextHoverText;
        private string hoverText = "";
        private StringBuilder hoverBuilder;
        private const string HoverBlockStart = "\n\n<size=70%><color=#88CCEE>Floe physics</color>";

        internal bool WaveValid => this && Registered && m_floating && Body && Sync && m_view && m_view.IsValid();
        private static bool Finite(float value) => SeasonalIceFloeWaves.Finite(value);
        private static bool Finite(Vector3 value) => SeasonalIceFloeWaves.Finite(value);
        private static bool WaterValid(float value) => SeasonalIceFloeWaves.WaterLevelValid(value);

        internal bool InitializeWaves(Floating floating)
        {
            ZNetView view = floating.m_nview;
            if (!view || !view.IsValid() || view.GetZDO().GetPrefab() != s_iceFloePrefab ||
                !view.GetZDO().GetBool(SeasonsVars.s_iceFloeWatermark) || !floating.m_body)
                return false;
            ZSyncTransform sync = floating.GetComponent<ZSyncTransform>();
            if (!sync)
                return false;
            m_floating = floating;
            m_view = view;
            Body = floating.m_body;
            Sync = sync;
            Root = floating.transform;
            Owner = view.GetZDO().GetOwner();
            CallbackLevel = -10000f; // Never adopt our previous fallback as a new water observation.
            Water = null;
            WaterObserved = PointsReady = Distant = HoldingGravity = Recovered = RecoveryPending = false;
            NextRecovery = 0f;
            BobFrame = -1;
            LastForceCalls = 0;
            Status = WaveStatus.Ready;
            return Registered = true;
        }

        internal void ReleaseWaves()
        {
            RestoreWaves();
            Registered = PointsReady = WaterObserved = false;
            Water = null;
            CallbackLevel = -10000f;
            Status = WaveStatus.Unregistered;
            LastForceCalls = 0;
        }

        internal bool ContainsCenter(WaterVolume water) => water && water.isActiveAndEnabled &&
            water.m_collider && water.m_collider.enabled && water.m_collider.bounds.Contains(Root.position);
        private bool HasWater() => WaterObserved && WaterValid(CallbackLevel) && ContainsCenter(Water);

        private bool EnsureCenterWater()
        {
            Vector3 position = Root.position;
            if (!Finite(position))
                return false;
            if (HasWater())
            {
                m_floating.m_waterLevel = CallbackLevel;
                return true;
            }
            if (!SeasonalIceFloeWaves.TrySurface(this, position, out float surface))
                return false;
            m_floating.m_waterLevel = surface;
            return true;
        }

        internal void ObserveWater(float level, Component liquidObj)
        {
            CallbackLevel = level;
            Water = liquidObj as WaterVolume;
            WaterObserved = liquidObj is WaterVolume;
            if (!Distant && m_view.IsOwner() && HasWater())
                RestoreGravity();
        }

        private bool BeyondWater()
        {
            if (!GameCamera.instance || !ZoneSystem.instance || !ZNet.instance || ZNet.instance.IsDedicated() || SeasonalIceFloeWaves.WaterDistance <= 0f)
                return false;
            Vector3 delta = GameCamera.instance.transform.position - Root.position;
            return delta.x * delta.x + delta.z * delta.z > SeasonalIceFloeWaves.WaterDistanceSquared;
        }

        private void HoldGravity()
        {
            if (HoldingGravity)
                return;
            HoldingGravity = true;
            BodyGravity = Body.useGravity;
            SyncGravity = Sync.m_useGravity;
            Sync.m_useGravity = false;
            Body.useGravity = false;
            StopMotion();
        }

        internal void StopMotion()
        {
            if (!Body.isKinematic)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                Body.Sleep();
            }
        }

        private void RestoreGravity()
        {
            if (!HoldingGravity)
                return;
            if (Sync)
                Sync.m_useGravity = SyncGravity;
            if (Body)
            {
                Body.useGravity = m_view && m_view.IsValid() && m_view.IsOwner() ? SyncGravity : BodyGravity;
                if (!Body.isKinematic)
                    Body.WakeUp();
            }
            HoldingGravity = false;
        }

        private void RestoreDistant()
        {
            if (!Distant)
                return;
            if (Body)
            {
                bool sameOwner = m_view && m_view.IsValid() && m_view.IsOwner() &&
                    m_view.GetZDO().GetOwner() == Owner && Sync && Sync.m_wasOwner;
                Vector3 position = !sameOwner && m_view && m_view.IsValid() ? m_view.GetZDO().GetPosition() : Baseline;
                if (sameOwner)
                {
                    position.x = Body.position.x;
                    position.z = Body.position.z;
                }
                Body.position = position;
            }
            if (Sync)
            {
                Sync.m_syncPosition = SyncPosition;
                Sync.m_syncBodyVelocity = SyncVelocity;
            }
            Distant = false;
        }

        internal void RestoreWaves()
        {
            RestoreDistant();
            RestoreGravity();
        }

        private void ObserveOwner()
        {
            long owner = m_view.GetZDO().GetOwner();
            if (owner == Owner)
                return;
            RestoreWaves();
            Owner = owner;
        }

        private void RecoverInvalidHeight()
        {
            bool adoptingPosition = m_view.IsOwner() && !Sync.m_wasOwner && Sync.m_syncPosition;
            Vector3 position = adoptingPosition ? m_view.GetZDO().GetPosition() : Body.position;
            if (Recovered || !m_view.IsOwner() || !ZoneSystem.instance || Time.time < NextRecovery ||
                (Finite(position.y) && position.y >= -5000f) || !Finite(position.x) || !Finite(position.z))
                return;
            NextRecovery = Time.time + 0.5f;
            Vector3 probe = new Vector3(position.x, ZoneSystem.instance.m_waterLevel, position.z);
            if (!SeasonalIceFloeWaves.TrySurface(this, probe, out float water))
                return;
            RestoreWaves();
            position.y = water + m_floating.m_waterLevelOffset - Root.TransformVector(Body.centerOfMass).y;
            if (!Finite(position.y))
                return;
            if (!Body.isKinematic)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
            Body.position = position;
            m_view.GetZDO().SetPosition(position);
            RecoveryPending = adoptingPosition;
            Recovered = true;
        }

        private void EnterDistant()
        {
            if (Distant)
                return;
            Baseline = Body.position;
            TargetY = Baseline.y;
            SyncPosition = Sync.m_syncPosition;
            SyncVelocity = Sync.m_syncBodyVelocity;
            Sync.m_syncPosition = false;
            Sync.m_syncBodyVelocity = false;
            Distant = true;
            HoldGravity();
        }

        internal void UpdateBobTarget()
        {
            if (!Distant || Game.IsPaused() || Time.timeScale <= 0f)
                return;
            Camera camera = GameCamera.instance ? GameCamera.instance.m_camera : null;
            if (!camera || (camera.transform.position - Baseline).sqrMagnitude > camera.farClipPlane * camera.farClipPlane)
                return;
            if (!m_view.IsOwner())
                Baseline = m_view.GetZDO().GetPosition();
            if (SeasonalIceFloeWaves.TrySurface(this, Baseline, out float surface))
            {
                float water = ZoneSystem.instance.m_waterLevel;
                float displacement = surface - water;
                float target = water + m_floating.m_waterLevelOffset + displacement / (1f + Mathf.Abs(displacement));
                if (Finite(target))
                    TargetY = target;
            }
        }

        private void ApplyBob()
        {
            if (!Distant || BobFrame == Time.frameCount || Game.IsPaused() || Time.timeScale <= 0f)
                return;
            BobFrame = Time.frameCount;
            if (!m_view.IsOwner())
                Baseline = m_view.GetZDO().GetPosition();
            else
            {
                Baseline.x = Body.position.x;
                Baseline.z = Body.position.z;
            }
            Vector3 position = Baseline;
            position.y = Mathf.Lerp(Body.position.y, TargetY, 1f - Mathf.Exp(-Time.deltaTime / 0.15f));
            if (Finite(position.y))
                Body.position = position;
        }

        internal void BeforeSync()
        {
            ObserveOwner();
            RecoverInvalidHeight();
            if (Distant && (!BeyondWater() || (m_view.IsOwner() && !Sync.m_wasOwner)))
                RestoreDistant();
            if (!Distant && m_view.IsOwner())
            {
                if (EnsureCenterWater())
                    RestoreGravity();
                else
                    HoldGravity();
            }
            ApplyBob();
        }

        private Vector3 FreshPoint(Collider collider, Vector3 center, Vector3 wind, int index)
        {
            Vector3 side = Vector3.Cross(wind, Root.up);
            Vector3 direction = index == 0 ? wind : index == 1 ? -wind : index == 2 ? side : -side;
            return collider.ClosestPoint(center + direction * 100f);
        }

        private bool EnsurePoints(Collider collider)
        {
            if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy)
                return false;
            // Validate before the cached fast path: a non-null array can still be empty
            // after prefab cloning or have been resized in a runtime inspector.
            if (Points == null || Points.Length != 4)
            {
                Points = new Vector3[4];
                PointsReady = false;
            }
            Vector3 forward = Root.forward;
            float heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float windHeading = SeasonalIceFloeWaves.WindHeading;
            if (!Finite(heading) || !Finite(windHeading) || forward.x * forward.x + forward.z * forward.z < 0.000001f)
                return PointsReady;
            if (PointsReady && Mathf.Abs(Mathf.DeltaAngle(BuildHeading, heading)) < 1f &&
                Mathf.Abs(Mathf.DeltaAngle(BuildWind, windHeading)) < 1f)
                return true;
            PointsReady = false;
            ColliderTransform = collider.transform;
            Vector3 center = Body.worldCenterOfMass;
            for (int i = 0; i < 4; i++)
            {
                Vector3 original = FreshPoint(collider, center, SeasonalIceFloeWaves.WindDirection, i);
                Points[i] = ColliderTransform.InverseTransformPoint(original);
                if (!Finite(Points[i]))
                    return false;
                buildRoundTripErrors[i] = Vector3.Distance(original, ColliderTransform.TransformPoint(Points[i]));
            }
            BuildHeading = heading;
            BuildWind = windHeading;
            CacheBuildCount++;
            return PointsReady = true;
        }

        private Vector3 WaveImpulse(Vector3 position, float water, float dt)
        {
            float depthDelta = position.y - water;
            float forceAmount = 0.5f * Mathf.Clamp01(Mathf.Abs(depthDelta / 4f)) * (dt * 50f) * Mathf.Abs(depthDelta);
            Vector3 force = depthDelta < 0f ? Vector3.up * (forceAmount * 0.6f) : Vector3.down * forceAmount;
            return force * 0.02f * Body.mass * 0.25f;
        }

        private static Vector3 LegacyWindDirection()
        {
            // Reproduce 1.8.2's direction selection through EnvMan, without reading wind intensity fields.
            EnvMan.instance.GetWindData(out Vector4 first, out Vector4 second, out float alpha);
            return alpha == 0f ? (Vector3)first : (Vector3)Vector4.Lerp(first, second, alpha);
        }

        internal bool BeforeFloating(float dt)
        {
            LastRunFrame = Time.frameCount;
            LastRunFixedTime = Time.fixedTime;
            LastForceCalls = 0;
            if (Game.IsPaused() || Time.timeScale <= 0f)
            {
                Status = WaveStatus.Paused;
                return false;
            }
            ObserveOwner();
            RecoverInvalidHeight();
            if (BeyondWater() && (!m_view.IsOwner() || Sync.m_wasOwner))
            {
                EnterDistant();
                Status = WaveStatus.Distant;
                return false;
            }
            RestoreDistant();
            if (!m_view.IsOwner())
            {
                RestoreGravity();
                Status = WaveStatus.NonOwner;
                return true;
            }
            if (!EnsureCenterWater())
            {
                HoldGravity();
                m_floating.SetSurfaceEffect(false);
                Status = WaveStatus.NoWater;
                return false;
            }
            RestoreGravity();
            if (Body.isKinematic)
            {
                Status = WaveStatus.Kinematic;
                return false;
            }
            Collider collider = m_floating.m_collider;
            if (!collider)
            {
                HoldGravity();
                Status = WaveStatus.NoCollider;
                return false;
            }
            if (!SeasonalIceFloeWaves.TrySurfaceContext(this, out SeasonalIceFloeWaves.SurfaceContext context))
            {
                Status = WaveStatus.NoSurface;
                return true;
            }
            if (!EnsurePoints(collider))
            {
                Status = WaveStatus.NoPoints;
                return true;
            }
            bool capture = !FreezeDiagnostics && (DiagnosticsEnabled || Time.unscaledTime <= hoverUntil);
            Vector3 legacyWind = capture || PointMode == PointSampling.LegacyClosestPoint ? LegacyWindDirection() : Vector3.zero;
            Vector3 center = Body.worldCenterOfMass;
            if (capture)
                BeginDiagnostics(dt, center, legacyWind);
            Vector3 torqueBefore = capture ? Body.GetAccumulatedTorque(dt) : Vector3.zero;
            Vector3 forceBefore = capture ? Body.GetAccumulatedForce(dt) : Vector3.zero;
            Body.WakeUp();
            for (int i = 0; i < 4; i++)
            {
                Vector3 cached = ColliderTransform.TransformPoint(Points[i]);
                Vector3 fresh = capture || PointMode == PointSampling.FreshClosestPoint
                    ? FreshPoint(collider, center, SeasonalIceFloeWaves.WindDirection, i) : cached;
                Vector3 legacy = capture || PointMode == PointSampling.LegacyClosestPoint
                    ? FreshPoint(collider, center, legacyWind, i) : cached;
                Vector3 position = PointMode == PointSampling.FreshClosestPoint ? fresh :
                    PointMode == PointSampling.LegacyClosestPoint ? legacy : cached;
                float water;
                bool valid;
                if (WaterMode == WaterSampling.NativeLiquidLevel)
                {
                    water = Floating.GetLiquidLevel(position);
                    valid = WaterValid(water);
                }
                else
                    valid = SeasonalIceFloeWaves.TrySurface(context, position, out water);
                Vector3 impulse = valid ? WaveImpulse(position, water, dt) : Vector3.zero;
                valid &= Finite(position) && Finite(impulse);
                if (valid)
                {
                    Body.AddForceAtPosition(impulse, position, ForceMode.Impulse);
                    LastForceCalls++;
                    TotalForceCalls++;
                }
                if (capture)
                    CaptureProbe(i, collider, context, cached, fresh, legacy, position, water, impulse, valid, dt, center);
            }
            Status = LastForceCalls == 0 ? WaveStatus.NoValidProbes : WaveStatus.ForcesSubmitted;
            if (capture)
            {
                Diagnostics.ForceCalls = LastForceCalls;
                // Native accumulator deltas, not estimates from the submitted argument vectors.
                // Contacts, native Floating and the next physics simulation are outside this bracket.
                Diagnostics.EngineAngularImpulse = (Body.GetAccumulatedTorque(dt) - torqueBefore) * dt;
                Diagnostics.EngineImpulse = (Body.GetAccumulatedForce(dt) - forceBefore) * dt;
                Diagnostics.Captured = true;
            }
            return true;
        }

        private void BeginDiagnostics(float dt, Vector3 center, Vector3 legacyWind)
        {
            Diagnostics ??= new WaveDiagnostics();
            WaveDiagnostics d = Diagnostics;
            d.Captured = false;
            if (d.Probes == null || d.Probes.Length != 4)
                d.Probes = new ProbeDiagnostics[4];
            d.Frame = Time.frameCount;
            d.PointMode = PointMode;
            d.WaterMode = WaterMode;
            d.NativeValidCount = 0;
            d.FixedTime = Time.fixedTime;
            d.FixedDelta = dt;
            d.WaveTime = SeasonalIceFloeWaves.WaveTime;
            d.Wind = SeasonalIceFloeWaves.WindDirection;
            d.WindIntensity = SeasonalIceFloeWaves.WindIntensity;
            d.LegacyWind = legacyWind;
            d.CenterOfMass = center;
            d.EulerAngles = Root.eulerAngles;
            d.AngularVelocity = Body.angularVelocity;
            d.Velocity = Body.linearVelocity;
            d.Inertia = Body.inertiaTensor;
            d.InertiaRotation = Body.inertiaTensorRotation;
            d.Mass = Body.mass;
            d.AngularDamping = Body.angularDamping;
            d.BalanceFraction = m_floating.m_balanceForceFraction;
            d.Damping = m_floating.m_damping;
            d.CenterWater = m_floating.m_waterLevel;
            d.CachedMathAngularImpulse = d.FreshMathAngularImpulse = d.CachedNativeAngularImpulse =
                d.FreshNativeAngularImpulse = d.LegacyNativeAngularImpulse = d.SubmittedAngularImpulse = Vector3.zero;
            d.MaxRoundTripError = d.MaxCacheErrorXZ = d.MaxCacheErrorY = d.MaxOutsideDistance = 0f;
        }

        private Vector3 ExpectedAngular(Vector3 point, float water, float dt, Vector3 center) =>
            WaterValid(water) ? Vector3.Cross(point - center, WaveImpulse(point, water, dt)) : Vector3.zero;

        private void CaptureProbe(int index, Collider collider, SeasonalIceFloeWaves.SurfaceContext context,
            Vector3 cached, Vector3 fresh, Vector3 legacy, Vector3 applied, float water, Vector3 impulse, bool submitted, float dt, Vector3 center)
        {
            WaveDiagnostics d = Diagnostics;
            ProbeDiagnostics p = default;
            p.LocalPoint = Points[index];
            p.CachedWorld = cached;
            p.FreshWorld = fresh;
            p.LegacyWorld = legacy;
            p.BuildRoundTripError = buildRoundTripErrors[index];
            Vector3 error = cached - fresh;
            p.CacheError = error.magnitude;
            p.CacheErrorXZ = Mathf.Sqrt(error.x * error.x + error.z * error.z);
            p.CacheErrorY = error.y;
            p.LegacyError = Vector3.Distance(cached, legacy);
            // Zero means inside/on the collider, not proof of the same support point.
            p.OutsideDistance = Vector3.Distance(cached, collider.ClosestPoint(cached));
            SeasonalIceFloeWaves.TrySurface(context, cached, out p.CachedMathWater);
            SeasonalIceFloeWaves.TrySurface(context, fresh, out p.FreshMathWater);
            p.CachedNativeWater = Floating.GetLiquidLevel(cached);
            p.FreshNativeWater = Floating.GetLiquidLevel(fresh);
            p.LegacyNativeWater = Floating.GetLiquidLevel(legacy);
            if (WaterValid(p.LegacyNativeWater))
                d.NativeValidCount++;
            p.AppliedWorld = applied;
            p.AppliedWater = water;
            p.AppliedWaterValid = WaterValid(water);
            p.Applied = submitted;
            p.AppliedImpulse = submitted ? impulse : Vector3.zero;
            p.AngularImpulse = Vector3.Cross(applied - center, p.AppliedImpulse);
            d.Probes[index] = p;
            d.CachedMathAngularImpulse += ExpectedAngular(cached, p.CachedMathWater, dt, center);
            d.FreshMathAngularImpulse += ExpectedAngular(fresh, p.FreshMathWater, dt, center);
            d.CachedNativeAngularImpulse += ExpectedAngular(cached, p.CachedNativeWater, dt, center);
            d.FreshNativeAngularImpulse += ExpectedAngular(fresh, p.FreshNativeWater, dt, center);
            d.LegacyNativeAngularImpulse += ExpectedAngular(legacy, p.LegacyNativeWater, dt, center);
            d.SubmittedAngularImpulse += p.AngularImpulse;
            d.MaxRoundTripError = Mathf.Max(d.MaxRoundTripError, p.BuildRoundTripError);
            d.MaxCacheErrorXZ = Mathf.Max(d.MaxCacheErrorXZ, p.CacheErrorXZ);
            d.MaxCacheErrorY = Mathf.Max(d.MaxCacheErrorY, Mathf.Abs(p.CacheErrorY));
            d.MaxOutsideDistance = Mathf.Max(d.MaxOutsideDistance, p.OutsideDistance);
        }

        public void RebuildWavePoints() => PointsReady = false;
        public void ClearWaveDiagnostics()
        {
            Diagnostics = null;
            CacheBuildCount = 0;
            TotalForceCalls = 0;
            nextHoverText = 0f;
        }

        private static string Number(float value, string format = "F3") => value.ToString(format, CultureInfo.InvariantCulture);
        private static string Vector(Vector3 value) => "(" + Number(value.x) + ", " + Number(value.y) + ", " + Number(value.z) + ")";

        partial void AppendWaveDiagnostics(ref string text)
        {
            if (!ShowDiagnosticsInHover || !m_view || !m_view.IsValid() ||
                !m_view.GetZDO().GetBool(SeasonsVars.s_iceFloeWatermark))
                return;
            // Hover only requests a snapshot in the real physics callback; it never runs forces or rebuilds points.
            hoverUntil = Time.unscaledTime + 0.5f;
            if (Time.unscaledTime >= nextHoverText)
            {
                nextHoverText = Time.unscaledTime + 0.2f;
                StringBuilder b = hoverBuilder ??= new StringBuilder(1536);
                b.Clear();
                b.Append(HoverBlockStart).Append(' ').Append(Status).Append(" | ").Append(PointMode).Append('/').Append(WaterMode);
                b.Append("\nOwner=").Append(m_view.GetZDO().GetOwner()).Append(" local=").Append(m_view.IsOwner());
                b.Append(" distant=").Append(Distant).Append(" gravityHold=").Append(HoldingGravity);
                b.Append("\nForce calls=").Append(LastForceCalls).Append("/4 total=").Append(TotalForceCalls);
                b.Append(" cache=").Append(PointsReady).Append(" builds=").Append(CacheBuildCount);
                WaveDiagnostics d = Diagnostics;
                if (d == null || !d.Captured || d.Probes == null || d.Probes.Length != 4)
                    b.Append("\nWaiting for an active physics sample. Inspector: DiagnosticsEnabled pins capture.");
                else
                {
                    b.Append("\nSample frame=").Append(d.Frame).Append(" fixed=").Append(Number(d.FixedTime, "F1"));
                    b.Append(" age=").Append(Number(Mathf.Max(0f, Time.fixedTime - d.FixedTime), "F1")).Append("s frozen=").Append(FreezeDiagnostics);
                    b.Append("\nSample mode=").Append(d.PointMode).Append('/').Append(d.WaterMode).Append(" native water=").Append(d.NativeValidCount).Append("/4");
                    b.Append("\nWind=").Append(Number(d.WindIntensity, "F2")).Append(" angles=").Append(Vector(d.EulerAngles));
                    b.Append(" angularVelocity=").Append(Vector(d.AngularVelocity));
                    for (int i = 0; i < d.Probes.Length; i++)
                    {
                        ProbeDiagnostics p = d.Probes[i];
                        b.Append("\nP").Append(i).Append(" roundtrip=").Append(Number(p.BuildRoundTripError, "F5"));
                        b.Append(" cacheXZ/Y=").Append(Number(p.CacheErrorXZ)).Append('/').Append(Number(p.CacheErrorY));
                        b.Append(" old=").Append(Number(p.LegacyError)).Append(" outside=").Append(Number(p.OutsideDistance));
                        b.Append(" Jy=").Append(Number(p.AppliedImpulse.y, "F5"));
                    }
                    b.Append("\nAngular J submitted=").Append(Vector(d.SubmittedAngularImpulse));
                    b.Append(" engine=").Append(Vector(d.EngineAngularImpulse));
                    b.Append("\nExpected cached/math=").Append(Vector(d.CachedMathAngularImpulse));
                    b.Append(" fresh/math=").Append(Vector(d.FreshMathAngularImpulse));
                    b.Append("\nExpected cached/native=").Append(Vector(d.CachedNativeAngularImpulse));
                    b.Append(" legacy/native=").Append(Vector(d.LegacyNativeAngularImpulse));
                }
                b.Append("</size>");
                hoverText = b.ToString();
            }
            int previous = text.IndexOf(HoverBlockStart, StringComparison.Ordinal);
            if (previous >= 0)
            {
                int end = text.IndexOf("</size>", previous + HoverBlockStart.Length, StringComparison.Ordinal);
                if (end >= 0)
                    text = text.Remove(previous, end + 7 - previous);
            }
            text += hoverText;
        }
    }
}
