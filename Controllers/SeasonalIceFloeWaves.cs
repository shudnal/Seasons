using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using static Seasons.ZoneSystemVariantController;

namespace Seasons
{
    // Shared wave inputs and native callback routing. Per-floe physics belongs to IceFloeClimb.
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
            // Keep the effective-wind approximation and patched accessors; never multiply
            // seasonal wind intensity again or substitute WaterVolume's cached intensity.
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

        // Retained full-spectrum sampler for distant bobbing, recovery and water diagnostics.
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

        internal static bool TryPhysicsSurface(SurfaceContext context, Vector3 position, float timeOffset,
            float secondarySwellWeight, out float surface)
        {
            surface = -10000f;
            if (!Finite(position) || !Finite(timeOffset))
                return false;
            float wave = 0f;
            if (context.UseWaves && !IsWaterSurfaceFrozen())
            {
                // WaterVolume.CalcWave at assemblies_combined d1374bfd: only term 0 uses
                // the effective wind direction. Retain BOTH TrochSin factors of CreateWave;
                // its slow transverse envelope is not the separate short-wave spectrum.
                // Terms 1..4 are optional fixed-direction swells. Terms 5..9 are excluded.
                float big = 1f - (float)WorldGenerator.DeepNorthWaveFade(position.x, position.z);
                int count = secondarySwellWeight > 0f ? 5 : 1;
                for (int i = 0; i < count; i++)
                {
                    Vector4 parameters = waves[i];
                    Vector2 direction = i == 0 ? new Vector2(WindDirection.x, WindDirection.z) : WaterVolume.s_createWaveDirections[i];
                    float height = parameters.z * big * (i == 0 ? 1f : secondarySwellWeight);
                    wave += oceanPrefab.CreateWave(position, (WaveTime + timeOffset) / 20f,
                        parameters.x, parameters.y, height, direction, new Vector2(-direction.y, direction.x), parameters.w);
                }
                wave *= WindIntensity; // Normalized depth 1 remains the accepted Ocean approximation.
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
            private static bool Prefix(Floating __instance, float fixedDeltaTime)
            {
                if (!floaters.TryGetValue(__instance, out IceFloeClimb controller) || !controller.WaveValid)
                    return true;
                controller.SimulatePhysics(fixedDeltaTime);
                return false; // Floating retains its lifecycle and water callbacks, not a second force driver.
            }
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
                if (!syncs.TryGetValue(__instance, out IceFloeClimb controller) || !controller.WaveValid)
                    return;
                if (__state)
                {
                    controller.StopMotion();
                    controller.RecoveryPending = false;
                }
                controller.PublishFallbackPose();
            }
        }
        [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.ClientSync))]
        private static class ZSyncTransform_ClientSync_IceFloe
        {
            private static bool Prefix(ZSyncTransform __instance, out bool __state)
            {
                __state = false;
                if (!syncs.TryGetValue(__instance, out IceFloeClimb controller) || !controller.WaveValid)
                    return true;
                controller.BeforeSync();
                if (controller.SuppressFallbackClientSync())
                    return false;
                __state = __instance.m_lastUpdateFrame != Time.frameCount && controller.IsFallbackReplica();
                return true;
            }

            private static void Postfix(ZSyncTransform __instance, bool __state)
            {
                if (__state && syncs.TryGetValue(__instance, out IceFloeClimb controller) && controller.WaveValid)
                    controller.RestoreFallbackReplicaVelocity();
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
                if (!controller || !controller.ShowDiagnosticsInHover || target.GetComponentInParent<Hoverable>() is IceFloeClimb)
                    return;
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

    public partial class IceFloeClimb
    {
        public enum WaveStatus { Unregistered, Ready, Paused, Distant, NonOwner, NoWater, Kinematic, NoCollider, NoSurface, InvalidBody, Dry, PhysicsDisabled, ForcesSubmitted, NoHullGeometry }

        [Header("Local diagnostics (not saved or synchronized)")]
        public bool ShowDiagnosticsInHover = true;
        public bool DiagnosticsEnabled;
        public bool FreezeDiagnostics;

        // RUE can edit these static fields once for every seasonal floe on this peer.
        // New instances read the same values; only explicit reset or plugin reload clears edits.
        [Header("Surface sampling (shared runtime settings, this peer only)")]
        [Tooltip("Half-distance in metres along/across wind. Four mathematical samples, not force application points.")]
        public static float ProbeDistance = 2f;
        public static bool ScaleProbeDistance;
        [Tooltip("0: only the native wind-directed wave. 1: also include native fixed-direction terms 1..4. Short waves are always excluded.")]
        [Range(0f, 1f)] public static float SecondarySwellWeight = 1f;

        [Header("Displacement and water resistance (shared runtime settings, this peer only)")]
        public static bool ApplyBuoyancy = true;
        public static bool ApplyVerticalWaterDamping = true;
        public static bool ApplyHorizontalWaterDamping = true;
        [Tooltip("Actual Rigidbody mass relative to the existing saved floe mass. Restored on release; never written to ZDO.")]
        public static float MassMultiplier = 4f;
        [Tooltip("Effective displacement thickness in metres at scale Y=1. This is a slab approximation, not a mesh volume calculation.")]
        public static float HullThickness = 1f;
        [Tooltip("Effective ice/water density ratio. 0.9 caps static lift at weight/0.9. This controls effective displacement, not the geometric waterline.")]
        [Range(0.5f, 0.99f)] public static float RelativeDensity = 0.9f;
        [Tooltip("Additional world-space correction of the collider waterline. Positive raises the floe. Floating.m_waterLevelOffset is not added.")]
        public static float HeightOffset;
        [Tooltip("Nominal fraction of the collider thickness below the sampled plane at rest. 0.5 places its center on the plane, independently of the root pivot and COM.")]
        [Range(0.1f, 0.95f)] public static float RestingSubmergence = 0.5f;
        [Tooltip("Damping ratio relative to moving water. 1 is the small-motion critical-damping reference, not an absolute velocity brake.")]
        public static float VerticalDampingRatio = 1f;
        [Tooltip("Limit on vertical water-drag acceleration, m/s^2. Buoyancy is separately limited by displaced volume.")]
        public static float MaxWaterDragAcceleration = 6f;
        [Tooltip("Horizontal water drag rate, 1/s. It does not change Rigidbody.linearDamping.")]
        public static float HorizontalDamping = 0.15f;

        [Header("Dynamic tilt control (shared runtime settings, this peer only)")]
        public static bool ApplySurfaceAlignment = true;
        public static bool ApplyTiltDamping = true;
        public static bool ApplyYawDamping = true;
        [Tooltip("Tilt response frequency in Hz. The controller submits torque through the actual world-space inertia tensor.")]
        public static float TiltFrequency = 0.65f;
        public static float TiltDampingRatio = 1f;
        [Tooltip("Maximum commanded tilt acceleration, radians/s^2. This is not a rotation constraint.")]
        public static float MaxTiltAcceleration = 1.5f;
        [Tooltip("Maximum target surface inclination in degrees. Collisions may still tilt the actual body further.")]
        public static float MaxSurfaceTilt = 45f;
        [Tooltip("Water drag around the floe's own up axis, 1/s. No target heading is imposed.")]
        public static float YawDamping = 0.15f;

        [Header("Live floe state")]
        public Rigidbody Body;
        public ZSyncTransform Sync;
        public Transform Root;
        public WaterVolume Water;
        public bool Registered, WaterObserved, Distant, HoldingGravity;
        public float CallbackLevel = -10000f;
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
        [NonSerialized] public float SourceMass;
        private float appliedMass, appliedMassMultiplier;
        internal int WaveIndex = -1;

        [Serializable]
        public struct SurfaceSettings
        {
            public float ProbeDistance, SecondarySwellWeight, MassMultiplier, Thickness, Density, HeightOffset, RestingSubmergence;
            public float VerticalDampingRatio, MaxWaterDragAcceleration, HorizontalDamping;
            public float TiltFrequency, TiltDampingRatio, MaxTiltAcceleration, MaxSurfaceTilt, YawDamping;
            public bool ScaleProbes, Buoyancy, VerticalDamping, HorizontalDrag, Alignment, TiltDamping, YawDrag;
        }

        public enum HullShape { Box, MeshBounds }

        private struct HullGeometry
        {
            internal HullShape Shape;
            internal Vector3 Center;
            internal float Thickness, BottomY, TopY;
        }

        private struct SurfaceFrame
        {
            internal HullGeometry Hull;
            internal Vector3 Center, SampleVelocity, Wind, Side, Normal, NextNormal;
            internal Vector4 Heights, NextHeights;
            internal float AlongRadius, AcrossRadius, Height, VerticalVelocity;
        }

        [Serializable]
        public sealed class WaveDiagnostics
        {
            public bool Captured, UseGravity, Sleeping, FloatingBodyMatches, SyncBodyMatches;
            public int Frame, BodyId, ForceCalls;
            public long Owner, AuthorityToken;
            public float FixedTime, FixedDelta, WaveTime, WindIntensity, Mass;
            public float FullSurfaceHeight, NativeSurfaceHeight, PlaneHeight, TargetComHeight, HeightError;
            public HullShape HullShape;
            public Vector3 SampleCenter, SampleVelocity;
            public float PivotY, TargetPivotY, TargetHullCenterY, ColliderThickness, ColliderBottomY, ColliderTopY;
            public float NominalHullSubmergence, NativeWaterLevelOffset;
            public float SubmergedFraction, WetWeight, WaterVerticalVelocity, RelativeVerticalVelocity, TiltErrorDegrees;
            public float AlongRadius, AcrossRadius, LinearDamping, AngularDamping, MaxAngularVelocity;
            public Vector3 CenterOfMass, Wind, ActualUp, TargetNormal, TargetAngularVelocity, Inertia;
            public Vector3 Velocity, AngularVelocity, BuoyancyForce, VerticalDragForce, HorizontalDragForce;
            public Vector3 AngularAcceleration, SubmittedForce, SubmittedTorque, EngineImpulse, EngineAngularImpulse;
            public Vector4 Heights, NextHeights;
            public Quaternion BodyRotation, InertiaRotation;
            public RigidbodyConstraints Constraints;
            public SurfaceSettings Settings;
        }

        [Serializable]
        public sealed class PhysicsStepDiagnostics
        {
            public bool Captured;
            public int SourceFrame, ObservedFrame;
            public long Owner, AuthorityToken;
            public float SourceFixedTime, ObservedFixedTime, RotationChangeDegrees, HeightChange;
            public Vector3 SourceForce, SourceTorque, BeforeVelocity, ObservedVelocity, BeforeOmega, ObservedOmega;
            public SurfaceSettings Settings;
        }

        [Header("Last captured controller call (before the solver)")]
        [NonSerialized] public WaveDiagnostics Diagnostics;
        [Header("Previous captured call observed at the next physics callback")]
        [NonSerialized] public PhysicsStepDiagnostics LastPhysicsStep;
        private bool diagnosticStepPending;
        private float hoverUntil = -1f, nextHoverText;
        private string hoverText = "";
        private StringBuilder hoverBuilder;
        private const string HoverBlockStart = "\n\n<size=70%><color=#88CCEE>Floe physics</color>";
        private const float SurfaceDerivativeStep = 0.05f;

        internal bool WaveValid => this && Registered && m_floating && Body && Sync && Root && m_view && m_view.IsValid();
        private static bool Finite(float value) => SeasonalIceFloeWaves.Finite(value);
        private static bool Finite(Vector3 value) => SeasonalIceFloeWaves.Finite(value);
        private static bool WaterValid(float value) => SeasonalIceFloeWaves.WaterLevelValid(value);
        private static float Setting(float value, float fallback, float min, float max) =>
            Mathf.Clamp(Finite(value) ? value : fallback, min, max);

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
            CallbackLevel = -10000f;
            Water = null;
            WaterObserved = Distant = HoldingGravity = Recovered = RecoveryPending = false;
            NextRecovery = 0f;
            BobFrame = -1;
            LastForceCalls = 0;
            Status = WaveStatus.Ready;
            diagnosticStepPending = false;
            SourceMass = appliedMass = Body.mass;
            appliedMassMultiplier = 1f;
            UpdatePhysicsMass();
            InitializeSimulationAuthority();
            return Registered = true;
        }

        private void UpdatePhysicsMass()
        {
            float multiplier = Setting(MassMultiplier, 4f, 0.25f, 20f);
            if (multiplier == appliedMassMultiplier)
                return;
            // Do not fight another mod writing mass every frame. An explicit new multiplier
            // may adopt its new baseline; release only restores a value still owned by us.
            if (!Body.mass.Equals(appliedMass))
                SourceMass = Body.mass;
            float mass = SourceMass * multiplier;
            if (!Finite(mass) || mass <= 0f)
                return;
            Body.mass = mass;
            appliedMass = Body.mass;
            appliedMassMultiplier = multiplier;
            diagnosticStepPending = false;
        }

        internal void ReleaseWaves()
        {
            WithdrawSimulationAuthority("Component released");
            RestoreWaves();
            if (Body && Body.mass.Equals(appliedMass) && Finite(SourceMass) && SourceMass > 0f)
                Body.mass = SourceMass;
            Registered = WaterObserved = false;
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
            if (!Distant && HasPhysicsAuthority && HasWater())
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
            if (FallbackSimulator)
                WithdrawSimulationAuthority("Physics safety hold", 3.0);
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
            diagnosticStepPending = false;
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
                Body.useGravity = HasPhysicsAuthority ? SyncGravity : BodyGravity;
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
            diagnosticStepPending = false;
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
            if (Recovered || !HasPhysicsAuthority || !ZoneSystem.instance || Time.time < NextRecovery ||
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
            ZDO zdo = m_view.GetZDO();
            uint revision = zdo.DataRevision;
            zdo.SetPosition(position);
            if (zdo.DataRevision == revision)
                zdo.IncreaseDataRevision(); // Ownerless recovery also needs publication.
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
            if (Game.IsPaused() || Time.timeScale <= 0f)
                diagnosticStepPending = false;
            ObserveOwner();
            UpdateSimulationAuthority();
            RecoverInvalidHeight();
            if (Distant && (!BeyondWater() || (m_view.IsOwner() && !Sync.m_wasOwner)))
                RestoreDistant();
            if (!Distant && HasPhysicsAuthority)
            {
                if (EnsureCenterWater())
                    RestoreGravity();
                else
                    HoldGravity();
            }
            ApplyBob();
        }

        private SurfaceSettings ReadSettings()
        {
            float scaleY = Setting(Mathf.Abs(Root.lossyScale.y), 1f, 0.01f, 100f);
            return new SurfaceSettings
            {
                ProbeDistance = Setting(ProbeDistance, 2f, 0.25f, 20f),
                ScaleProbes = ScaleProbeDistance,
                SecondarySwellWeight = Setting(SecondarySwellWeight, 1f, 0f, 1f),
                MassMultiplier = Setting(MassMultiplier, 4f, 0.25f, 20f),
                Thickness = Mathf.Clamp(Setting(HullThickness, 1f, 0.1f, 10f) * scaleY, 0.05f, 100f),
                Density = Setting(RelativeDensity, 0.9f, 0.5f, 0.99f),
                HeightOffset = Setting(HeightOffset, 0f, -10f, 10f),
                RestingSubmergence = Setting(RestingSubmergence, 0.5f, 0.1f, 0.95f),
                VerticalDampingRatio = Setting(VerticalDampingRatio, 1f, 0f, 5f),
                MaxWaterDragAcceleration = Setting(MaxWaterDragAcceleration, 6f, 0f, 50f),
                HorizontalDamping = Setting(HorizontalDamping, 0.15f, 0f, 10f),
                TiltFrequency = Setting(TiltFrequency, 0.65f, 0f, 3f),
                TiltDampingRatio = Setting(TiltDampingRatio, 1f, 0f, 5f),
                MaxTiltAcceleration = Setting(MaxTiltAcceleration, 1.5f, 0f, 20f),
                MaxSurfaceTilt = Setting(MaxSurfaceTilt, 45f, 0f, 80f),
                YawDamping = Setting(YawDamping, 0.15f, 0f, 10f),
                Buoyancy = ApplyBuoyancy, VerticalDamping = ApplyVerticalWaterDamping,
                HorizontalDrag = ApplyHorizontalWaterDamping, Alignment = ApplySurfaceAlignment,
                TiltDamping = ApplyTiltDamping, YawDrag = ApplyYawDamping
            };
        }

        private bool TryHullGeometry(Collider collider, out HullGeometry hull)
        {
            hull = default;
            Bounds bounds;
            if (collider is BoxCollider box)
            {
                bounds = new Bounds(box.center, box.size);
                hull.Shape = HullShape.Box;
            }
            else if (collider is MeshCollider mesh && mesh.sharedMesh)
            {
                bounds = mesh.sharedMesh.bounds;
                hull.Shape = HullShape.MeshBounds;
            }
            else
                return false;

            // The datum is the collider, not the bottom-positioned prefab pivot or an
            // assumed center of mass. Read only this collider's local bounds, no mesh scan.
            if (!Finite(bounds.center) || !Finite(bounds.extents) || bounds.extents.y <= 0f)
                return false;
            Transform shape = collider.transform;
            Quaternion undoRootRotation = Quaternion.Inverse(Root.rotation);
            Vector3 centerOffset = undoRootRotation * (shape.TransformPoint(bounds.center) - Root.position);
            Vector3 x = undoRootRotation * shape.TransformVector(Vector3.right * bounds.extents.x);
            Vector3 y = undoRootRotation * shape.TransformVector(Vector3.up * bounds.extents.y);
            Vector3 z = undoRootRotation * shape.TransformVector(Vector3.forward * bounds.extents.z);
            hull.Center = Body.position + Body.rotation * centerOffset;
            // Intrinsic thickness follows scale and child transforms, not the growing
            // world-axis AABB height when the wide floe tilts. Never apply scale twice.
            hull.Thickness = 2f * (Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y));
            x = Body.rotation * x;
            y = Body.rotation * y;
            z = Body.rotation * z;
            float worldHalfHeight = Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y);
            hull.BottomY = hull.Center.y - worldHalfHeight;
            hull.TopY = hull.Center.y + worldHalfHeight;
            return Finite(hull.Center) && Finite(hull.Thickness) && hull.Thickness > 0.0001f &&
                Finite(hull.BottomY) && Finite(hull.TopY);
        }

        private Vector2 ProbeRadii(SurfaceSettings settings, Vector3 wind, Vector3 side)
        {
            if (!settings.ScaleProbes)
                return Vector2.one * settings.ProbeDistance;
            Vector3 forward = Body.rotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.000001f)
            {
                Vector3 rightFallback = Body.rotation * Vector3.right;
                rightFallback.y = 0f;
                forward = Vector3.Cross(rightFallback.normalized, Vector3.up);
            }
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 scale = Root.lossyScale;
            float sx = Setting(Mathf.Abs(scale.x), 1f, 0.01f, 100f);
            float sz = Setting(Mathf.Abs(scale.z), 1f, 0.01f, 100f);
            float wr = Vector3.Dot(wind, right) * sx, wf = Vector3.Dot(wind, forward) * sz;
            float sr = Vector3.Dot(side, right) * sx, sf = Vector3.Dot(side, forward) * sz;
            // Project the fixed local X/Z scale into the wind frame; ignore pitch/roll.
            return new Vector2(
                Mathf.Clamp(settings.ProbeDistance * Mathf.Sqrt(wr * wr + wf * wf), 0.25f, 100f),
                Mathf.Clamp(settings.ProbeDistance * Mathf.Sqrt(sr * sr + sf * sf), 0.25f, 100f));
        }

        private static Vector3 PlaneNormal(Vector4 heights, Vector3 wind, Vector3 side, Vector2 radii, float maxTilt)
        {
            float alongSlope = (heights.x - heights.y) / (2f * radii.x);
            float acrossSlope = (heights.z - heights.w) / (2f * radii.y);
            Vector3 gradient = alongSlope * wind + acrossSlope * side;
            gradient = Vector3.ClampMagnitude(gradient, Mathf.Tan(maxTilt * Mathf.Deg2Rad));
            return (Vector3.up - gradient).normalized;
        }

        private bool TrySurfaceFrame(SeasonalIceFloeWaves.SurfaceContext context, SurfaceSettings settings,
            HullGeometry hull, out SurfaceFrame frame)
        {
            frame = default;
            frame.Center = Body.worldCenterOfMass;
            frame.Hull = hull;
            frame.SampleVelocity = Body.GetPointVelocity(hull.Center);
            if (!Finite(frame.SampleVelocity))
                return false;
            frame.Wind = SeasonalIceFloeWaves.WindDirection;
            if (frame.Wind.sqrMagnitude < 0.000001f)
                frame.Wind = Vector3.forward; // Deterministic calm-water axes; no point cache to invalidate.
            frame.Side = Vector3.Cross(frame.Wind, Vector3.up);
            Vector2 radii = ProbeRadii(settings, frame.Wind, frame.Side);
            frame.AlongRadius = radii.x;
            frame.AcrossRadius = radii.y;
            Vector3 travel = frame.SampleVelocity * SurfaceDerivativeStep;
            travel.y = 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 offset = i < 2 ? frame.Wind * (i == 0 ? radii.x : -radii.x) :
                    frame.Side * (i == 2 ? radii.y : -radii.y);
                Vector3 point = hull.Center + offset;
                if (!SeasonalIceFloeWaves.TryPhysicsSurface(context, point, 0f, settings.SecondarySwellWeight, out float now) ||
                    !SeasonalIceFloeWaves.TryPhysicsSurface(context, point + travel, SurfaceDerivativeStep,
                        settings.SecondarySwellWeight, out float next))
                    return false;
                frame.Heights[i] = now;
                frame.NextHeights[i] = next;
            }
            // Four samples are generally not coplanar. On this symmetric cross, their mean
            // height and two central differences define the least-squares plane at the center.
            frame.Height = (frame.Heights.x + frame.Heights.y + frame.Heights.z + frame.Heights.w) * 0.25f;
            float nextHeight = (frame.NextHeights.x + frame.NextHeights.y + frame.NextHeights.z + frame.NextHeights.w) * 0.25f;
            frame.VerticalVelocity = (nextHeight - frame.Height) / SurfaceDerivativeStep;
            frame.Normal = PlaneNormal(frame.Heights, frame.Wind, frame.Side, radii, settings.MaxSurfaceTilt);
            frame.NextNormal = PlaneNormal(frame.NextHeights, frame.Wind, frame.Side, radii, settings.MaxSurfaceTilt);
            // Differencing at the same time snapshot avoids cross-frame owner/wind/time-reset
            // derivative spikes. The horizontal travel term includes moving across a wave.
            return WaterValid(frame.Height) && Finite(frame.VerticalVelocity) && Finite(frame.Normal) && Finite(frame.NextNormal);
        }

        private static Vector3 RotationError(Vector3 from, Vector3 to, Vector3 fallbackAxis)
        {
            Vector3 axis = Vector3.Cross(from, to);
            float sine = axis.magnitude;
            float cosine = Mathf.Clamp(Vector3.Dot(from, to), -1f, 1f);
            if (sine > 0.000001f)
                return axis * (Mathf.Atan2(sine, cosine) / sine);
            // Cross alone vanishes for upside-down floes. Choose a deterministic body axis
            // so a recovered/inverted floe is not an uncorrectable equilibrium.
            return cosine < 0f ? Vector3.ProjectOnPlane(fallbackAxis, from).normalized * Mathf.PI : Vector3.zero;
        }

        internal void SimulatePhysics(float dt)
        {
            LastRunFrame = Time.frameCount;
            LastRunFixedTime = Time.fixedTime;
            LastForceCalls = 0;
            ObserveOwner();
            UpdateSimulationAuthority();
            if (Game.IsPaused() || Time.timeScale <= 0f)
            {
                diagnosticStepPending = false;
                Status = WaveStatus.Paused;
                return;
            }
            UpdatePhysicsMass();
            ObserveNextPhysicsStep();
            RecoverInvalidHeight();
            if (BeyondWater() && (!m_view.IsOwner() || Sync.m_wasOwner))
            {
                EnterDistant();
                Status = WaveStatus.Distant;
                return;
            }
            RestoreDistant();
            if (!HasPhysicsAuthority)
            {
                RestoreGravity();
                Status = WaveStatus.NonOwner;
                return;
            }
            if (!EnsureCenterWater())
            {
                HoldGravity();
                m_floating.SetSurfaceEffect(false);
                Status = WaveStatus.NoWater;
                return;
            }
            RestoreGravity();
            if (FallbackSimulator)
                Body.useGravity = Sync.m_useGravity;
            if (Body.isKinematic)
            {
                Status = WaveStatus.Kinematic;
                return;
            }
            Collider collider = m_floating.m_collider;
            if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy)
            {
                HoldGravity();
                Status = WaveStatus.NoCollider;
                return;
            }
            if (!Finite(dt) || dt <= 0f || !Finite(Body.mass) || Body.mass <= 0f ||
                !Finite(Body.worldCenterOfMass) || !Finite(Body.linearVelocity) || !Finite(Body.angularVelocity) ||
                !Finite(Body.inertiaTensor) || !Finite(Physics.gravity) ||
                m_floating.m_body != Body || collider.attachedRigidbody != Body)
            {
                if (FallbackSimulator)
                    WithdrawSimulationAuthority("Invalid physics body", 3.0);
                Status = WaveStatus.InvalidBody;
                return;
            }
            if (!TryHullGeometry(collider, out HullGeometry hull))
            {
                HoldGravity();
                m_floating.SetSurfaceEffect(false);
                Status = WaveStatus.NoHullGeometry;
                return;
            }
            SurfaceSettings settings = ReadSettings();
            if (!SeasonalIceFloeWaves.TrySurfaceContext(this, out SeasonalIceFloeWaves.SurfaceContext context) ||
                !TrySurfaceFrame(context, settings, hull, out SurfaceFrame frame))
            {
                HoldGravity();
                m_floating.SetSurfaceEffect(false);
                Status = WaveStatus.NoSurface;
                return;
            }
            bool capture = !FreezeDiagnostics && (DiagnosticsEnabled || Time.unscaledTime <= hoverUntil);
            if (capture)
                BeginDiagnostics(dt, frame, settings, context);
            Vector3 beforeForce = capture ? Body.GetAccumulatedForce(dt) : Vector3.zero;
            Vector3 beforeTorque = capture ? Body.GetAccumulatedTorque(dt) : Vector3.zero;
            ApplySurfacePhysics(dt, frame, settings, capture);
            RecordFallbackPhysicsStep();
            if (capture)
            {
                Diagnostics.ForceCalls = LastForceCalls;
                Diagnostics.EngineImpulse = (Body.GetAccumulatedForce(dt) - beforeForce) * dt;
                Diagnostics.EngineAngularImpulse = (Body.GetAccumulatedTorque(dt) - beforeTorque) * dt;
                Diagnostics.Captured = true;
                diagnosticStepPending = true;
            }
        }

        private void ApplySurfacePhysics(float dt, SurfaceFrame frame, SurfaceSettings settings, bool capture)
        {
            float gravity = Mathf.Max(0f, -Physics.gravity.y);
            // Separate the geometric waterline from the effective displacement model.
            // At 0.5 the collider center lies on the sampled plane. For a bottom pivot,
            // its resting root is therefore half the scaled collider height below water.
            // Do not also add Floating.m_waterLevelOffset or another half-height shift.
            float targetHullHeight = frame.Height + settings.HeightOffset +
                (0.5f - settings.RestingSubmergence) * frame.Hull.Thickness;
            float heightError = targetHullHeight - frame.Hull.Center.y;
            float targetHeight = frame.Center.y + heightError;
            float submerged = Mathf.Clamp01(settings.Density + heightError / settings.Thickness);
            float wet = Mathf.Clamp01(submerged / settings.Density);
            float relativeVelocity = frame.SampleVelocity.y - frame.VerticalVelocity;

            // Effective slab volume V=m/(rho_water*density); at the chosen geometric
            // waterline fraction=density, so lift balances weight. Density still limits
            // reserve lift; it no longer imposes a visual 90% collider immersion.
            // HullThickness remains the virtual slab thickness, not exact submerged mesh volume.
            float buoyancyAcceleration = settings.Buoyancy ? gravity * submerged / settings.Density : 0f;
            float dragAcceleration = 0f;
            if (settings.VerticalDamping && wet > 0f)
            {
                float rate = 2f * settings.VerticalDampingRatio * Mathf.Sqrt(gravity / (settings.Density * settings.Thickness)) * wet;
                float externalGravity = Body.useGravity ? Physics.gravity.y : 0f;
                // Implicit linear drag against moving water, including the velocity change
                // from buoyancy/gravity during this step. Stable drag does not overwrite
                // collision impulses, cancel all velocity, or turn into a second lift spring.
                float predictedRelative = relativeVelocity + (buoyancyAcceleration + externalGravity) * dt;
                dragAcceleration = Mathf.Clamp(-rate * predictedRelative / (1f + rate * dt),
                    -settings.MaxWaterDragAcceleration, settings.MaxWaterDragAcceleration);
            }
            Vector3 horizontalAcceleration = Vector3.zero;
            if (settings.HorizontalDrag && wet > 0f)
            {
                Vector3 horizontal = Body.linearVelocity;
                horizontal.y = 0f;
                float rate = settings.HorizontalDamping * wet;
                horizontalAcceleration = -horizontal * (rate / (1f + rate * dt));
            }
            Vector3 buoyancyForce = Vector3.up * (Body.mass * buoyancyAcceleration);
            Vector3 dragForce = Vector3.up * (Body.mass * dragAcceleration);
            Vector3 horizontalForce = horizontalAcceleration * Body.mass;
            Vector3 force = buoyancyForce + dragForce + horizontalForce;

            Vector3 up = Body.rotation * Vector3.up;
            Vector3 error = RotationError(up, frame.Normal, Body.rotation * Vector3.right);
            Vector3 targetOmega = RotationError(frame.Normal, frame.NextNormal, frame.Wind) / SurfaceDerivativeStep;
            Vector3 relativeOmega = Vector3.ProjectOnPlane(targetOmega - Body.angularVelocity, up);
            float frequency = 2f * Mathf.PI * settings.TiltFrequency;
            float stiffness = settings.Alignment ? frequency * frequency * wet : 0f;
            float damping = settings.TiltDamping ? 2f * settings.TiltDampingRatio * frequency * wet : 0f;
            // Implicit PD in radians. Only the normal is targeted; heading is not locked.
            Vector3 acceleration = (stiffness * error + (damping + stiffness * dt) * relativeOmega) /
                (1f + damping * dt + stiffness * dt * dt);
            acceleration = Vector3.ClampMagnitude(acceleration, settings.MaxTiltAcceleration);
            if (settings.YawDrag && wet > 0f)
            {
                float rate = settings.YawDamping * wet;
                acceleration -= up * (Vector3.Dot(Body.angularVelocity, up) * rate / (1f + rate * dt));
            }
            // Convert desired angular acceleration into a real world-space torque. Do not
            // divide by scalar mass or multiply dt twice. External contact response retains
            // the body's actual mass/inertia and is resolved together with these forces.
            Quaternion inertiaFrame = Body.rotation * Body.inertiaTensorRotation;
            Vector3 torque = inertiaFrame * Vector3.Scale(Body.inertiaTensor, Quaternion.Inverse(inertiaFrame) * acceleration);
            if (!Finite(force) || !Finite(torque))
            {
                Status = WaveStatus.InvalidBody;
                return;
            }
            if (force.sqrMagnitude > 0f)
            {
                Body.AddForce(force, ForceMode.Force); // Applied at the center of mass, no extra balance moment.
                LastForceCalls++;
                TotalForceCalls++;
            }
            if (torque.sqrMagnitude > 0f)
            {
                Body.AddTorque(torque, ForceMode.Force);
                LastForceCalls++;
                TotalForceCalls++;
            }
            // Keep native impact/surface presentation only; no native buoyancy or velocity writes.
            m_floating.UpdateImpactEffect();
            m_floating.SetSurfaceEffect(submerged > 0f);
            Status = submerged <= 0f ? WaveStatus.Dry : LastForceCalls == 0 ? WaveStatus.PhysicsDisabled : WaveStatus.ForcesSubmitted;
            if (capture)
            {
                WaveDiagnostics d = Diagnostics;
                d.TargetComHeight = targetHeight;
                d.TargetHullCenterY = targetHullHeight;
                d.TargetPivotY = Body.position.y + heightError;
                d.HeightError = heightError;
                d.NominalHullSubmergence = Mathf.Clamp01(0.5f +
                    (frame.Height + settings.HeightOffset - frame.Hull.Center.y) / frame.Hull.Thickness);
                d.SubmergedFraction = submerged;
                d.WetWeight = wet;
                d.RelativeVerticalVelocity = relativeVelocity;
                d.TiltErrorDegrees = error.magnitude * Mathf.Rad2Deg;
                d.ActualUp = up;
                d.TargetAngularVelocity = targetOmega;
                d.BuoyancyForce = buoyancyForce;
                d.VerticalDragForce = dragForce;
                d.HorizontalDragForce = horizontalForce;
                d.AngularAcceleration = acceleration;
                d.SubmittedForce = force;
                d.SubmittedTorque = torque;
            }
        }

        private void BeginDiagnostics(float dt, SurfaceFrame frame, SurfaceSettings settings, SeasonalIceFloeWaves.SurfaceContext context)
        {
            Diagnostics ??= new WaveDiagnostics();
            WaveDiagnostics d = Diagnostics;
            d.Captured = false;
            d.Frame = Time.frameCount;
            d.BodyId = Body.GetInstanceID();
            d.Owner = PhysicsOwner;
            d.AuthorityToken = PhysicsAuthorityToken;
            d.FixedTime = Time.fixedTime;
            d.FixedDelta = dt;
            d.Settings = settings;
            d.ForceCalls = 0;
            d.WaveTime = SeasonalIceFloeWaves.WaveTime;
            d.WindIntensity = SeasonalIceFloeWaves.WindIntensity;
            d.Mass = Body.mass;
            d.UseGravity = Body.useGravity;
            d.Sleeping = Body.IsSleeping();
            d.FloatingBodyMatches = m_floating.m_body == Body;
            d.SyncBodyMatches = Sync.m_body == Body;
            d.Inertia = Body.inertiaTensor;
            d.InertiaRotation = Body.inertiaTensorRotation;
            d.Constraints = Body.constraints;
            d.LinearDamping = Body.linearDamping;
            d.AngularDamping = Body.angularDamping;
            d.MaxAngularVelocity = Body.maxAngularVelocity;
            d.BodyRotation = Body.rotation;
            d.CenterOfMass = frame.Center;
            d.SampleCenter = frame.Hull.Center;
            d.SampleVelocity = frame.SampleVelocity;
            d.HullShape = frame.Hull.Shape;
            d.ColliderThickness = frame.Hull.Thickness;
            d.ColliderBottomY = frame.Hull.BottomY;
            d.ColliderTopY = frame.Hull.TopY;
            d.PivotY = Body.position.y;
            d.NativeWaterLevelOffset = m_floating.m_waterLevelOffset; // Observed only, not used in near forces.
            d.TargetPivotY = d.TargetHullCenterY = d.NominalHullSubmergence = 0f;
            d.Velocity = Body.linearVelocity;
            d.AngularVelocity = Body.angularVelocity;
            d.Wind = frame.Wind;
            d.TargetNormal = frame.Normal;
            d.PlaneHeight = frame.Height;
            d.WaterVerticalVelocity = frame.VerticalVelocity;
            d.Heights = frame.Heights;
            d.NextHeights = frame.NextHeights;
            d.AlongRadius = frame.AlongRadius;
            d.AcrossRadius = frame.AcrossRadius;
            SeasonalIceFloeWaves.TrySurface(context, frame.Hull.Center, out d.FullSurfaceHeight);
            d.NativeSurfaceHeight = Floating.GetLiquidLevel(frame.Hull.Center);
            d.TargetComHeight = d.HeightError = d.SubmergedFraction = d.WetWeight = d.RelativeVerticalVelocity = d.TiltErrorDegrees = 0f;
            d.ActualUp = d.TargetAngularVelocity = d.BuoyancyForce = d.VerticalDragForce = d.HorizontalDragForce =
                d.AngularAcceleration = d.SubmittedForce = d.SubmittedTorque = d.EngineImpulse = d.EngineAngularImpulse = Vector3.zero;
        }

        private void ObserveNextPhysicsStep()
        {
            bool pending = diagnosticStepPending;
            diagnosticStepPending = false;
            WaveDiagnostics d = Diagnostics;
            if (!pending || FreezeDiagnostics || d == null || !d.Captured || (!DiagnosticsEnabled && Time.unscaledTime > hoverUntil))
                return;
            LastPhysicsStep ??= new PhysicsStepDiagnostics();
            LastPhysicsStep.Captured = false;
            float elapsed = Time.fixedTime - d.FixedTime;
            if (Distant || HoldingGravity || Body.isKinematic || BeyondWater() || !HasPhysicsAuthority ||
                PhysicsOwner != d.Owner || PhysicsAuthorityToken != d.AuthorityToken || Body.GetInstanceID() != d.BodyId || elapsed <= 0f ||
                elapsed > Mathf.Max(d.FixedDelta, Time.fixedDeltaTime) * 1.5f + 0.001f)
                return;
            PhysicsStepDiagnostics step = LastPhysicsStep;
            step.SourceFrame = d.Frame;
            step.ObservedFrame = Time.frameCount;
            step.SourceFixedTime = d.FixedTime;
            step.ObservedFixedTime = Time.fixedTime;
            step.Owner = d.Owner;
            step.AuthorityToken = d.AuthorityToken;
            step.Settings = d.Settings;
            step.SourceForce = d.SubmittedForce;
            step.SourceTorque = d.SubmittedTorque;
            step.BeforeVelocity = d.Velocity;
            step.ObservedVelocity = Body.linearVelocity;
            step.BeforeOmega = d.AngularVelocity;
            step.ObservedOmega = Body.angularVelocity;
            step.HeightChange = Body.worldCenterOfMass.y - d.CenterOfMass.y;
            Quaternion delta = Body.rotation * Quaternion.Inverse(d.BodyRotation);
            float sine = Mathf.Sqrt(delta.x * delta.x + delta.y * delta.y + delta.z * delta.z);
            step.RotationChangeDegrees = 2f * Mathf.Atan2(sine, Mathf.Abs(delta.w)) * Mathf.Rad2Deg;
            step.Captured = true; // Includes contacts, engine damping, sync and other scripts between callbacks.
        }

        // Resets shared controls for ALL floes on this peer; does not reset their poses.
        public static void ResetSurfacePhysicsSettings()
        {
            ProbeDistance = 2f;
            ScaleProbeDistance = false;
            SecondarySwellWeight = 1f;
            ApplyBuoyancy = ApplyVerticalWaterDamping = ApplyHorizontalWaterDamping = true;
            MassMultiplier = 4f;
            HullThickness = 1f;
            RelativeDensity = 0.9f;
            HeightOffset = 0f;
            RestingSubmergence = 0.5f;
            VerticalDampingRatio = 1f;
            MaxWaterDragAcceleration = 6f;
            HorizontalDamping = 0.15f;
            ApplySurfaceAlignment = ApplyTiltDamping = ApplyYawDamping = true;
            TiltFrequency = 0.65f;
            TiltDampingRatio = 1f;
            MaxTiltAcceleration = 1.5f;
            MaxSurfaceTilt = 45f;
            YawDamping = 0.15f;
            // Apply mass through the normal lifecycle on the next callback, not inside the inspector.
        }

        public void ClearWaveDiagnostics()
        {
            Diagnostics = null;
            LastPhysicsStep = null;
            diagnosticStepPending = false;
            TotalForceCalls = 0;
            nextHoverText = 0f;
        }

        private static string Number(float value, string format = "F3") => value.ToString(format, CultureInfo.InvariantCulture);
        private static string Vector(Vector3 value, string format = "F3") =>
            "(" + Number(value.x, format) + ", " + Number(value.y, format) + ", " + Number(value.z, format) + ")";
        private static string Heights(Vector4 value) =>
            "(" + Number(value.x) + ", " + Number(value.y) + ", " + Number(value.z) + ", " + Number(value.w) + ")";

        private static void AppendSwitches(StringBuilder b, SurfaceSettings settings)
        {
            b.Append("buoyancy=").Append(settings.Buoyancy).Append(" drag V/H=");
            b.Append(settings.VerticalDamping).Append('/').Append(settings.HorizontalDrag);
            b.Append(" tilt/damp/yaw=").Append(settings.Alignment).Append('/').Append(settings.TiltDamping).Append('/').Append(settings.YawDrag);
        }

        partial void AppendWaveDiagnostics(ref string text)
        {
            if (!ShowDiagnosticsInHover || !m_view || !m_view.IsValid() || !m_view.GetZDO().GetBool(SeasonsVars.s_iceFloeWatermark))
                return;
            hoverUntil = Time.unscaledTime + 0.5f;
            if (Time.unscaledTime >= nextHoverText)
            {
                nextHoverText = Time.unscaledTime + 0.2f;
                StringBuilder b = hoverBuilder ??= new StringBuilder(3072);
                b.Clear();
                b.Append(HoverBlockStart).Append(' ').Append(Status).Append(" | Wind surface / dynamic forces | shared settings");
                b.Append("\nOwner=").Append(m_view.GetZDO().GetOwner()).Append(" local=").Append(m_view.IsOwner());
                b.Append(" distant=").Append(Distant).Append(" gravityHold=").Append(HoldingGravity);
                AppendAuthorityDiagnostics(b);
                b.Append("\nForce/torque calls=").Append(LastForceCalls).Append("/2 total=").Append(TotalForceCalls);
                WaveDiagnostics d = Diagnostics;
                if (d == null || !d.Captured)
                    b.Append("\nWaiting for an active physics sample. DiagnosticsEnabled pins capture.");
                else
                {
                    b.Append("\nSample frame=").Append(d.Frame).Append(" fixed=").Append(Number(d.FixedTime));
                    b.Append(" simulator=").Append(d.Owner).Append(" token=").Append(d.AuthorityToken);
                    b.Append(" age=").Append(Number(Mathf.Max(0f, Time.fixedTime - d.FixedTime), "F1")).Append("s frozen=").Append(FreezeDiagnostics);
                    b.Append("\nSample switches: ");
                    AppendSwitches(b, d.Settings);
                    b.Append("\nWind=").Append(Vector(d.Wind)).Append(" intensity=").Append(Number(d.WindIntensity));
                    b.Append(" secondary swell=").Append(Number(d.Settings.SecondarySwellWeight));
                    b.Append("\nWater plane/full/native=").Append(Number(d.PlaneHeight)).Append('/');
                    b.Append(Number(d.FullSurfaceHeight)).Append('/').Append(Number(d.NativeSurfaceHeight));
                    b.Append(" gap plane/native=").Append(WaterValid(d.NativeSurfaceHeight)
                        ? Number(d.PlaneHeight - d.NativeSurfaceHeight) : "invalid");
                    b.Append("\nProbe heights +W/-W/+S/-S=").Append(Heights(d.Heights));
                    b.Append(" radii=").Append(Number(d.AlongRadius)).Append('/').Append(Number(d.AcrossRadius));
                    b.Append("\nNormal=").Append(Vector(d.TargetNormal)).Append(" up=").Append(Vector(d.ActualUp));
                    b.Append(" tilt error=").Append(Number(d.TiltErrorDegrees)).Append(" deg");
                    b.Append("\nCOM target/actual=").Append(Number(d.TargetComHeight)).Append('/').Append(Number(d.CenterOfMass.y));
                    b.Append(" error=").Append(Number(d.HeightError)).Append(" displacement=").Append(Number(d.SubmergedFraction));
                    b.Append("\nY pivot/hull/COM=").Append(Number(d.PivotY)).Append('/');
                    b.Append(Number(d.SampleCenter.y)).Append('/').Append(Number(d.CenterOfMass.y));
                    b.Append("\nHull=").Append(d.HullShape).Append(" thickness=").Append(Number(d.ColliderThickness));
                    b.Append(" bottom/top=").Append(Number(d.ColliderBottomY)).Append('/').Append(Number(d.ColliderTopY));
                    b.Append("\nWaterline target/actual fraction=").Append(Number(d.Settings.RestingSubmergence));
                    b.Append('/').Append(Number(d.NominalHullSubmergence));
                    b.Append(" heightOffset=").Append(Number(d.Settings.HeightOffset));
                    b.Append("\nTarget Y pivot/hull=").Append(Number(d.TargetPivotY)).Append('/').Append(Number(d.TargetHullCenterY));
                    b.Append(" nativeOffset(unused)=").Append(Number(d.NativeWaterLevelOffset));
                    b.Append("\nVelocity Y water/hull/relative=").Append(Number(d.WaterVerticalVelocity)).Append('/');
                    b.Append(Number(d.SampleVelocity.y)).Append('/').Append(Number(d.RelativeVerticalVelocity));
                    b.Append("\nBody mass=").Append(Number(d.Mass)).Append(" inertia=").Append(Vector(d.Inertia));
                    b.Append(" gravity=").Append(d.UseGravity).Append(" constraints=").Append(d.Constraints);
                    b.Append("\nBody damp L/A=").Append(Number(d.LinearDamping)).Append('/').Append(Number(d.AngularDamping));
                    b.Append(" maxOmega=").Append(Number(d.MaxAngularVelocity)).Append(" same Floating/Sync=");
                    b.Append(d.FloatingBodyMatches).Append('/').Append(d.SyncBodyMatches);
                    b.Append("\nDensity/effectiveThickness=").Append(Number(d.Settings.Density)).Append('/').Append(Number(d.Settings.Thickness));
                    b.Append(" heave damping=").Append(Number(d.Settings.VerticalDampingRatio));
                    b.Append(" tilt Hz/damping=").Append(Number(d.Settings.TiltFrequency)).Append('/').Append(Number(d.Settings.TiltDampingRatio));
                    b.Append("\nForces N: buoyancy=").Append(Number(d.BuoyancyForce.y)).Append(" vertical drag=").Append(Number(d.VerticalDragForce.y));
                    b.Append(" horizontal=").Append(Vector(d.HorizontalDragForce));
                    b.Append("\nTorque Nm=").Append(Vector(d.SubmittedTorque)).Append(" alpha=").Append(Vector(d.AngularAcceleration));
                    b.Append("\nOmega actual/target=").Append(Vector(d.AngularVelocity, "F5")).Append('/').Append(Vector(d.TargetAngularVelocity, "F5"));
                    b.Append("\nImpulse expected/engine=").Append(Vector(d.SubmittedForce * d.FixedDelta)).Append('/').Append(Vector(d.EngineImpulse));
                    b.Append("\nAngular J expected/engine=").Append(Vector(d.SubmittedTorque * d.FixedDelta)).Append('/').Append(Vector(d.EngineAngularImpulse));
                }
                PhysicsStepDiagnostics step = LastPhysicsStep;
                if (step != null && step.Captured)
                {
                    b.Append("\nPrevious step fixed=").Append(Number(step.SourceFixedTime)).Append(" -> ").Append(Number(step.ObservedFixedTime));
                    b.Append(" simulator=").Append(step.Owner).Append(" token=").Append(step.AuthorityToken);
                    b.Append(" age=").Append(Number(Mathf.Max(0f, Time.fixedTime - step.ObservedFixedTime), "F1")).Append("s");
                    b.Append("\nPrevious switches: ");
                    AppendSwitches(b, step.Settings);
                    b.Append("\nPrevious Y delta=").Append(Number(step.HeightChange, "F5"));
                    b.Append(" rotation delta=").Append(Number(step.RotationChangeDegrees, "F5")).Append(" deg");
                    b.Append("\nPrevious velocity Y=").Append(Number(step.BeforeVelocity.y, "F5")).Append(" -> ").Append(Number(step.ObservedVelocity.y, "F5"));
                    b.Append(" omega=").Append(Vector(step.BeforeOmega, "F5")).Append(" -> ").Append(Vector(step.ObservedOmega, "F5"));
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
