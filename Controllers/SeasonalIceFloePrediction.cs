using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;

namespace Seasons
{
    public partial class IceFloeClimb
    {
        public enum FloeSurfaceMode { FullRate, Predicted, FarVisual }

        [Header("Distance prediction (shared runtime settings)")]
        public static bool EnableWavePrediction = true;
        [Tooltip("Use complete water height for heave while keeping the tilt plane free of short ripples.")]
        public static bool UseFullWaterHeight = true;
        public static float MinimumPredictionSeconds = 0.1f;
        public static float MaximumPredictionSeconds = 2f;
        [Tooltip("Maximum time between forecast knots. A two-second forecast is a curve, not a straight line across a whole wave.")]
        public static float PredictionKnotSeconds = 0.25f;
        public static float PredictionPositionTolerance = 0.5f;
        public static float DistantBobAmplitude = 0.08f;
        public static float DistantBobPeriod = 6f;

        [Header("Live sampling diagnostics")]
        public FloeSurfaceMode SurfaceMode;
        public bool InActivePhysicsArea;
        public float PredictionSeconds, PredictionAge, DistanceToCamera;
        public long DirectSurfaceFrames, PredictionBuilds, PredictionHits, SurfaceHeightQueries;
        public string PredictionInvalidation = "Not initialized";

        public enum ForecastRebuildCause
        {
            None, Empty, Geometry, Clock, WaterContext, Wind, Settings, ProbeSpacing,
            Authority, HorizontalMotion, Completed, ShorterHorizon, ExternalReset
        }

        [Header("Prediction cache diagnostics")]
        public ForecastRebuildCause LastForecastRebuildCause;
        public long GeometryCacheBuilds, ScaleNoiseReuses, ForecastKnotsSampled, PredictionDirectFallbacks;

        // Main-thread counters only. Reading them never queries water or instruments every
        // floe. The aggregate remains useful while detailed per-object capture is disabled.
        public sealed class FloePredictionCounters
        {
            public long DirectFrames, ForecastBuilds, ForecastHits, SampledKnots;
            public long HullRebuilds, ScaleNoiseReuses, SameFrameFallbacks;
            public long GeometryResets, ClockResets, WaterResets, WindResets, SettingsResets;
            public long AuthorityResets, MotionResets, ScheduledBuilds, OtherBuilds;
        }

        public static FloePredictionCounters PredictionPerformance { get; private set; } = new FloePredictionCounters();

        public static void ResetPredictionPerformanceCounters() => PredictionPerformance = new FloePredictionCounters();

        private static void CountForecastBuild(ForecastRebuildCause cause)
        {
            FloePredictionCounters totals = PredictionPerformance;
            totals.ForecastBuilds++;
            switch (cause)
            {
                case ForecastRebuildCause.Geometry: totals.GeometryResets++; break;
                case ForecastRebuildCause.Clock: totals.ClockResets++; break;
                case ForecastRebuildCause.WaterContext: totals.WaterResets++; break;
                case ForecastRebuildCause.Wind: totals.WindResets++; break;
                case ForecastRebuildCause.Settings:
                case ForecastRebuildCause.ProbeSpacing: totals.SettingsResets++; break;
                case ForecastRebuildCause.Authority: totals.AuthorityResets++; break;
                case ForecastRebuildCause.HorizontalMotion: totals.MotionResets++; break;
                case ForecastRebuildCause.Completed:
                case ForecastRebuildCause.ShorterHorizon: totals.ScheduledBuilds++; break;
                default: totals.OtherBuilds++; break;
            }
        }

        private struct SurfaceKnot
        {
            internal Vector4 Heights, Rates;
            internal float WaterHeight, WaterRate;
        }

        // Allocated only on first prediction; 2 seconds plus one derivative segment at
        // minimum knot spacing 0.1 seconds. Reused across wind, scale and owner changes.
        private SurfaceKnot[] forecast;
        private int forecastCount;
        private float forecastStep, forecastHorizon;
        private double forecastStart;
        private float forecastWaveTime, forecastWindIntensity, forecastWeight, forecastMaxTilt;
        private Vector3 forecastOrigin, forecastVelocity, forecastWind, forecastSide;
        private Vector2 forecastRadii;
        private SeasonalIceFloeWaves.SurfaceContext forecastContext;
        private uint forecastGeometryRevision;
        private int forecastBuildFrame = -1;
        private ForecastRebuildCause pendingForecastCause = ForecastRebuildCause.Empty;
        private long forecastOwner, forecastToken;
        private bool forecastValid, forecastFullHeight;
        private Vector4 forecastWaterWind1, forecastWaterWind2;
        private float forecastWaterBlend, forecastSpacing;

        private int stateFrame = -1;
        private ushort stateOwnerRevision;
        private long stateOwner;
        private bool statePaused, stateFallbackEnabled;
        private uint authorityDataRevision;
        private int authorityFrame = -1;

        private Vector3 hullCenterOffset, hullX, hullY, hullZ, hullScale;
        private Collider hullCollider;
        private Transform hullParent;
        private Vector3 hullLocalScale;
        private uint hullGeometryRevision;
        private bool hullCached;
        private HullShape hullShape;
        private float hullThickness;

        private bool distantKinematic, distantCollisions;
        private CollisionDetectionMode distantCollisionMode;
        private Quaternion distantRotation, distantFlatRotation;
        private Vector3 distantVelocity, distantAngularVelocity;
        private long distantOwner;
        private ushort distantOwnerRevision;

        private void InitializePrediction()
        {
            stateFrame = authorityFrame = forecastBuildFrame = -1;
            forecastValid = hullCached = false;
            forecastCount = 0;
            hullGeometryRevision = 0;
            hullParent = null;
            pendingForecastCause = ForecastRebuildCause.Empty;
            PredictionSeconds = PredictionAge = 0f;
            SurfaceMode = FloeSurfaceMode.FullRate;
        }

        private void InvalidatePrediction(string reason, ForecastRebuildCause cause = ForecastRebuildCause.ExternalReset)
        {
            forecastValid = false;
            PredictionAge = 0f;
            PredictionInvalidation = reason;
            pendingForecastCause = cause;
        }

        // Geometry is immutable for the known ice prefab except scale. Do not rebuild
        // a transform chain or query collider bounds on every physics step.
        public void RebuildHullGeometry()
        {
            hullCached = false;
            InvalidatePrediction("Geometry rebuild requested", ForecastRebuildCause.Geometry);
        }

        // lossyScale is decomposed from a floating-point world matrix. Rotation can
        // perturb its least significant bits without any actual scale edit. Compare to
        // the last accepted scale (not the last observation) so real cumulative changes
        // still invalidate the cache. Explicit local-scale edits/reparenting remain exact.
        private static bool SameWorldScale(Vector3 a, Vector3 b) =>
            SameScaleComponent(a.x, b.x) && SameScaleComponent(a.y, b.y) && SameScaleComponent(a.z, b.z);

        private static bool SameScaleComponent(float a, float b) =>
            Finite(a) && Finite(b) && Mathf.Abs(a - b) <= 0.00001f + 0.0001f * Mathf.Max(Mathf.Abs(a), Mathf.Abs(b));

        private bool ReadHullGeometry(Collider collider, out HullGeometry hull)
        {
            hull = default;
            Vector3 scale = Root.lossyScale;
            Vector3 localScale = Root.localScale;
            Transform parent = Root.parent;
            if (!Finite(scale) || !Finite(localScale))
                return false;
            if (!hullCached || hullCollider != collider || hullParent != parent ||
                !hullLocalScale.Equals(localScale) || !SameWorldScale(hullScale, scale))
            {
                if (!TryHullGeometry(collider, out HullGeometry original))
                    return false;
                Bounds bounds = collider is BoxCollider box ? new Bounds(box.center, box.size) :
                    ((MeshCollider)collider).sharedMesh.bounds;
                Transform shape = collider.transform;
                Quaternion undo = Quaternion.Inverse(Root.rotation);
                hullCenterOffset = undo * (shape.TransformPoint(bounds.center) - Root.position);
                hullX = undo * shape.TransformVector(Vector3.right * bounds.extents.x);
                hullY = undo * shape.TransformVector(Vector3.up * bounds.extents.y);
                hullZ = undo * shape.TransformVector(Vector3.forward * bounds.extents.z);
                hullThickness = original.Thickness;
                hullShape = original.Shape;
                hullScale = scale;
                hullLocalScale = localScale;
                hullParent = parent;
                hullCollider = collider;
                hullCached = true;
                hullGeometryRevision++;
                GeometryCacheBuilds++;
                PredictionPerformance.HullRebuilds++;
                InvalidatePrediction("Geometry or scale changed", ForecastRebuildCause.Geometry);
            }
            else if (!hullScale.Equals(scale))
            {
                ScaleNoiseReuses++;
                PredictionPerformance.ScaleNoiseReuses++;
            }
            Quaternion rotation = Body.rotation;
            hull.Shape = hullShape;
            hull.Center = Body.position + rotation * hullCenterOffset;
            hull.Thickness = hullThickness;
            Vector3 x = rotation * hullX, y = rotation * hullY, z = rotation * hullZ;
            float halfHeight = Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y);
            hull.BottomY = hull.Center.y - halfHeight;
            hull.TopY = hull.Center.y + halfHeight;
            return Finite(hull.Center) && Finite(halfHeight);
        }

        private void RefreshFloeState()
        {
            ZDO zdo = m_view.GetZDO();
            bool paused = Game.IsPaused() || Time.timeScale <= 0f;
            int frame = Time.frameCount;
            long owner = zdo.GetOwner();
            if (stateFrame == frame && stateOwner == owner && stateOwnerRevision == zdo.OwnerRevision &&
                statePaused == paused && stateFallbackEnabled == EnableFallbackSimulation)
            {
                // Foreign writes can arrive between callbacks. Do not postpone their
                // authority implications until the next rendered frame.
                if (!Distant && authorityDataRevision != zdo.DataRevision)
                    RefreshAuthority(zdo);
                return;
            }
            stateFrame = frame;
            stateOwner = owner;
            stateOwnerRevision = zdo.OwnerRevision;
            statePaused = paused;
            stateFallbackEnabled = EnableFallbackSimulation;
            ObserveOwner();
            InActivePhysicsArea = ZNet.instance && ZoneSystem.instance &&
                ZNetScene.InActiveArea(Body.position, ZNet.instance.GetReferencePosition());
            Camera camera = GameCamera.instance ? GameCamera.instance.m_camera : null;
            DistanceToCamera = camera ? Utils.DistanceXZ(camera.transform.position, Body.position) : 0f;
            if (BeyondWater())
            {
                EnterDistant();
                SurfaceMode = FloeSurfaceMode.FarVisual;
                PredictionSeconds = 0f;
                AuthorityMode = FloeAuthorityMode.VisualOnly;
                AuthorityReason = "Local bob outside visible waves; no pose publication";
                return;
            }
            if (Distant)
            {
                RestoreDistant();
                RestoreGravity();
            }
            SurfaceMode = InActivePhysicsArea || !EnableWavePrediction ? FloeSurfaceMode.FullRate : FloeSurfaceMode.Predicted;
            PredictionSeconds = 0f;
            if (SurfaceMode == FloeSurfaceMode.Predicted && ZoneSystem.instance && ZNet.instance)
            {
                float zoneSize = ZoneSystem.instance.m_zoneSize;
                float halfActive = zoneSize * (ZNet.instance.GetSyncedSimulationDistance().NearSimulationDistance == 1 ? 1f : 1.5f);
                Vector3 zoneCenter = ZoneSystem.GetZonePos(ZoneSystem.GetZone(ZNet.instance.GetReferencePosition()));
                Vector3 delta = Body.position - zoneCenter;
                float outside = Mathf.Max(0f, Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.z)) - halfActive);
                float fraction = Mathf.Clamp01(outside / Mathf.Max(zoneSize, SeasonalIceFloeWaves.WaterDistance - halfActive));
                float maximum = Setting(MaximumPredictionSeconds, 2f, 0.1f, 2f);
                float minimum = Setting(MinimumPredictionSeconds, 0.1f, 0.05f, maximum);
                PredictionSeconds = Mathf.Lerp(minimum, maximum, fraction);
            }
            RefreshAuthority(zdo, force: true);
        }

        private void RefreshAuthority(ZDO zdo, bool force = false)
        {
            if (!force && authorityFrame == Time.frameCount && authorityDataRevision == zdo.DataRevision &&
                stateOwner == zdo.GetOwner() && stateOwnerRevision == zdo.OwnerRevision &&
                statePaused == (Game.IsPaused() || Time.timeScale <= 0f) &&
                stateFallbackEnabled == EnableFallbackSimulation)
                return;
            UpdateSimulationAuthority();
            authorityFrame = Time.frameCount;
            authorityDataRevision = zdo.DataRevision;
        }

        private static float Mean(Vector4 value) => (value.x + value.y + value.z + value.w) * 0.25f;

        private bool SampleKnot(SeasonalIceFloeWaves.SurfaceContext context, SurfaceSettings settings,
            Vector3 origin, Vector3 velocity, Vector3 wind, Vector3 side, Vector2 radii, float ahead, out SurfaceKnot knot)
        {
            knot = default;
            ForecastKnotsSampled++;
            PredictionPerformance.SampledKnots++;
            for (int i = 0; i < 4; i++)
            {
                Vector3 offset = i < 2 ? wind * (i == 0 ? radii.x : -radii.x) : side * (i == 2 ? radii.y : -radii.y);
                Vector3 point = origin + velocity * ahead + offset;
                SurfaceHeightQueries += 2;
                if (!SeasonalIceFloeWaves.TryPhysicsSurface(context, point, ahead, settings.SecondarySwellWeight, out float height) ||
                    !SeasonalIceFloeWaves.TryPhysicsSurface(context, point + velocity * SurfaceDerivativeStep,
                        ahead + SurfaceDerivativeStep, settings.SecondarySwellWeight, out float next))
                    return false;
                knot.Heights[i] = height;
                knot.Rates[i] = (next - height) / SurfaceDerivativeStep;
            }
            if (UseFullWaterHeight)
            {
                SurfaceHeightQueries += 2;
                if (!SeasonalIceFloeWaves.TryWaterlineSurface(context, origin + velocity * ahead, ahead, out knot.WaterHeight) ||
                    !SeasonalIceFloeWaves.TryWaterlineSurface(context, origin + velocity * (ahead + SurfaceDerivativeStep),
                        ahead + SurfaceDerivativeStep, out float next))
                    return false;
                knot.WaterRate = (next - knot.WaterHeight) / SurfaceDerivativeStep;
            }
            else
            {
                knot.WaterHeight = Mean(knot.Heights);
                knot.WaterRate = Mean(knot.Rates);
            }
            return true;
        }

        private bool TryDirectSurfaceFrame(SeasonalIceFloeWaves.SurfaceContext context, SurfaceSettings settings,
            HullGeometry hull, out SurfaceFrame frame)
        {
            DirectSurfaceFrames++;
            PredictionPerformance.DirectFrames++;
            SurfaceHeightQueries += 8;
            if (!TrySurfaceFrame(context, settings, hull, out frame))
                return false;
            if (UseFullWaterHeight)
            {
                SurfaceHeightQueries += 2;
                Vector3 travel = frame.SampleVelocity * SurfaceDerivativeStep;
                travel.y = 0f;
                if (!SeasonalIceFloeWaves.TryWaterlineSurface(context, hull.Center, 0f, out frame.Height) ||
                    !SeasonalIceFloeWaves.TryWaterlineSurface(context, hull.Center + travel, SurfaceDerivativeStep, out float next))
                    return false;
                frame.VerticalVelocity = (next - frame.Height) / SurfaceDerivativeStep;
            }
            return WaterValid(frame.Height) && Finite(frame.VerticalVelocity);
        }

        private ForecastRebuildCause ForecastChange(SeasonalIceFloeWaves.SurfaceContext context, SurfaceSettings settings,
            double elapsed, Vector3 wind, Vector2 radii, Vector3 drift, float tolerance)
        {
            if (!forecastValid)
                return pendingForecastCause == ForecastRebuildCause.None ? ForecastRebuildCause.Empty : pendingForecastCause;
            if (forecastGeometryRevision != hullGeometryRevision)
                return ForecastRebuildCause.Geometry;
            if (!(elapsed >= 0.0) || !(Mathf.Abs(SeasonalIceFloeWaves.WaveTime - forecastWaveTime - (float)elapsed) < 0.1f))
                return ForecastRebuildCause.Clock;
            if (forecastContext.Water != context.Water || forecastContext.WaterLevel != context.WaterLevel ||
                forecastContext.Offset != context.Offset || forecastContext.UseWaves != context.UseWaves ||
                forecastContext.HasWorldEdge != context.HasWorldEdge)
                return ForecastRebuildCause.WaterContext;
            if (!(Vector3.Dot(wind, forecastWind) > 0.99996f) ||
                !(Mathf.Abs(SeasonalIceFloeWaves.WindIntensity - forecastWindIntensity) < 0.01f) ||
                (UseFullWaterHeight && (!((forecastWaterWind1 - SeasonalIceFloeWaves.WaterWind1).sqrMagnitude < 0.0001f) ||
                    !((forecastWaterWind2 - SeasonalIceFloeWaves.WaterWind2).sqrMagnitude < 0.0001f) ||
                    !(Mathf.Abs(forecastWaterBlend - SeasonalIceFloeWaves.WaterWindBlend) < 0.01f))))
                return ForecastRebuildCause.Wind;
            if (forecastWeight != settings.SecondarySwellWeight || forecastMaxTilt != settings.MaxSurfaceTilt ||
                forecastFullHeight != UseFullWaterHeight ||
                forecastSpacing != Setting(PredictionKnotSeconds, 0.25f, 0.1f, 0.25f))
                return ForecastRebuildCause.Settings;
            if (!((forecastRadii - radii).sqrMagnitude < 0.0001f))
                return ForecastRebuildCause.ProbeSpacing;
            if (forecastOwner != PhysicsOwner || forecastToken != PhysicsAuthorityToken)
                return ForecastRebuildCause.Authority;
            if (!(drift.sqrMagnitude <= tolerance * tolerance))
                return ForecastRebuildCause.HorizontalMotion;
            if (elapsed >= forecastHorizon)
                return ForecastRebuildCause.Completed;
            if (PredictionSeconds < forecastHorizon * 0.75f)
                return ForecastRebuildCause.ShorterHorizon;
            return ForecastRebuildCause.None;
        }

        private bool TryScheduledSurfaceFrame(SeasonalIceFloeWaves.SurfaceContext context, SurfaceSettings settings,
            HullGeometry hull, out SurfaceFrame frame)
        {
            // Native wave phase wraps daily and is not a periodic continuation of every
            // term. Never interpolate a forecast across that discontinuity.
            bool phaseBoundary = SeasonalIceFloeWaves.WaveTime > 86400f - 2.1f;
            if (SurfaceMode == FloeSurfaceMode.FullRate || phaseBoundary)
            {
                InvalidatePrediction(phaseBoundary ? "Native wave phase boundary" : "Full-rate active area",
                    phaseBoundary ? ForecastRebuildCause.Clock : ForecastRebuildCause.ExternalReset);
                return TryDirectSurfaceFrame(context, settings, hull, out frame);
            }
            frame = default;
            if (!TryAuthorityTime(out double now))
                return false;
            Vector3 velocity = Body.GetPointVelocity(hull.Center);
            if (!Finite(velocity))
                return false;
            Vector3 horizontalVelocity = velocity;
            horizontalVelocity.y = 0f;
            Vector3 wind = SeasonalIceFloeWaves.WindDirection;
            if (wind.sqrMagnitude < 0.000001f)
                wind = Vector3.forward;
            Vector3 side = Vector3.Cross(wind, Vector3.up);
            Vector2 radii = ProbeRadii(settings, wind, side);
            double elapsed = now - forecastStart;
            Vector3 drift = hull.Center - (forecastOrigin + forecastVelocity * (float)elapsed);
            drift.y = 0f;
            float tolerance = Setting(PredictionPositionTolerance, 0.5f, 0.1f, 2f);
            ForecastRebuildCause cause = ForecastChange(context, settings, elapsed, wind, radii, drift, tolerance);
            if (cause != ForecastRebuildCause.None)
            {
                bool compatible = forecastValid &&
                    (cause == ForecastRebuildCause.Completed || cause == ForecastRebuildCause.ShorterHorizon);
                // A changing input during multiple catch-up fixed steps must never rebuild
                // a whole multi-knot horizon repeatedly in one rendered frame. Use one exact
                // current sample instead; do not freeze forces or reuse incompatible water.
                if (forecastBuildFrame == Time.frameCount)
                {
                    PredictionDirectFallbacks++;
                    PredictionPerformance.SameFrameFallbacks++;
                    if (!compatible)
                        InvalidatePrediction("Prediction rebuild deferred; using current surface", cause);
                    return TryDirectSurfaceFrame(context, settings, hull, out frame);
                }
                // Count attempts too. A failed sample must not retry the expensive window
                // repeatedly before the next rendered frame.
                forecastBuildFrame = Time.frameCount;
                LastForecastRebuildCause = cause;
                CountForecastBuild(cause);
                // A short first horizon staggers the expensive refreshes by floe/peer.
                // Successive horizons start from the present, never replay a catch-up loop.
                float horizon = Mathf.Max(0.05f, PredictionSeconds);
                if (!compatible)
                    horizon *= 0.5f + 0.5f * (float)electionPhase;
                float spacing = Setting(PredictionKnotSeconds, 0.25f, 0.1f, 0.25f);
                int count = Mathf.CeilToInt((horizon + SurfaceDerivativeStep) / spacing) + 1;
                forecast ??= new SurfaceKnot[24];
                count = Mathf.Min(count, forecast.Length);
                float step = (horizon + SurfaceDerivativeStep) / (count - 1);
                SurfaceKnot continuation = default;
                bool continuous = compatible && elapsed <= forecastHorizon + SurfaceDerivativeStep;
                if (continuous)
                    EvaluateForecast((float)elapsed, out continuation);
                forecastValid = false;
                for (int i = 0; i < count; i++)
                {
                    if (i == 0 && continuous)
                        forecast[i] = continuation;
                    else if (!SampleKnot(context, settings, hull.Center, horizontalVelocity, wind, side, radii, i * step, out forecast[i]))
                        return false;
                }
                forecastStart = now;
                forecastCount = count;
                forecastStep = step;
                forecastHorizon = horizon;
                forecastWaveTime = SeasonalIceFloeWaves.WaveTime;
                forecastOrigin = hull.Center;
                forecastVelocity = horizontalVelocity;
                forecastWind = wind;
                forecastSide = side;
                forecastRadii = radii;
                forecastContext = context;
                forecastWeight = settings.SecondarySwellWeight;
                forecastFullHeight = UseFullWaterHeight;
                forecastWaterWind1 = SeasonalIceFloeWaves.WaterWind1;
                forecastWaterWind2 = SeasonalIceFloeWaves.WaterWind2;
                forecastWaterBlend = SeasonalIceFloeWaves.WaterWindBlend;
                forecastSpacing = spacing;
                forecastMaxTilt = settings.MaxSurfaceTilt;
                forecastWindIntensity = SeasonalIceFloeWaves.WindIntensity;
                forecastGeometryRevision = hullGeometryRevision;
                forecastOwner = PhysicsOwner;
                forecastToken = PhysicsAuthorityToken;
                PredictionInvalidation = compatible ? "Forecast completed" : "Forecast rebuilt; see cause";
                pendingForecastCause = ForecastRebuildCause.None;
                forecastValid = true;
                PredictionBuilds++;
                elapsed = 0.0;
            }
            else
            {
                PredictionHits++;
                PredictionPerformance.ForecastHits++;
            }
            PredictionAge = (float)elapsed;
            frame.Center = Body.worldCenterOfMass;
            frame.Hull = hull;
            frame.SampleVelocity = velocity;
            frame.Wind = forecastWind;
            frame.Side = forecastSide;
            frame.AlongRadius = forecastRadii.x;
            frame.AcrossRadius = forecastRadii.y;
            EvaluateForecast((float)elapsed, out SurfaceKnot current);
            EvaluateForecast((float)elapsed + SurfaceDerivativeStep, out SurfaceKnot following);
            frame.Heights = current.Heights;
            frame.NextHeights = following.Heights;
            frame.Height = current.WaterHeight;
            frame.VerticalVelocity = current.WaterRate;
            frame.Normal = PlaneNormal(frame.Heights, frame.Wind, frame.Side, forecastRadii, settings.MaxSurfaceTilt);
            frame.NextNormal = PlaneNormal(frame.NextHeights, frame.Wind, frame.Side, forecastRadii, settings.MaxSurfaceTilt);
            return WaterValid(frame.Height) && Finite(frame.VerticalVelocity) && Finite(frame.Normal) && Finite(frame.NextNormal);
        }

        private void EvaluateForecast(float time, out SurfaceKnot result)
        {
            int index = Mathf.Clamp(Mathf.FloorToInt(time / forecastStep), 0, forecastCount - 2);
            float t = Mathf.Clamp01((time - index * forecastStep) / forecastStep);
            float t2 = t * t, t3 = t2 * t;
            SurfaceKnot a = forecast[index], b = forecast[index + 1];
            // Cubic Hermite interpolation, with its own analytic derivative. Damping and
            // height therefore follow the same curve, not an unrelated velocity lerp.
            result = default;
            result.Heights = (2f * t3 - 3f * t2 + 1f) * a.Heights + (t3 - 2f * t2 + t) * forecastStep * a.Rates +
                (-2f * t3 + 3f * t2) * b.Heights + (t3 - t2) * forecastStep * b.Rates;
            result.Rates = ((6f * t2 - 6f * t) * a.Heights + (-6f * t2 + 6f * t) * b.Heights) / forecastStep +
                (3f * t2 - 4f * t + 1f) * a.Rates + (3f * t2 - 2f * t) * b.Rates;
            result.WaterHeight = (2f * t3 - 3f * t2 + 1f) * a.WaterHeight + (t3 - 2f * t2 + t) * forecastStep * a.WaterRate +
                (-2f * t3 + 3f * t2) * b.WaterHeight + (t3 - t2) * forecastStep * b.WaterRate;
            result.WaterRate = ((6f * t2 - 6f * t) * a.WaterHeight + (-6f * t2 + 6f * t) * b.WaterHeight) / forecastStep +
                (3f * t2 - 4f * t + 1f) * a.WaterRate + (3f * t2 - 2f * t) * b.WaterRate;
        }

        private void AppendPredictionDiagnostics(StringBuilder b)
        {
            b.Append("\nSurface mode=").Append(SurfaceMode).Append(" activeArea=").Append(InActivePhysicsArea);
            b.Append(" distance=").Append(Number(DistanceToCamera)).Append(" waveRadius=").Append(Number(SeasonalIceFloeWaves.WaterDistance));
            b.Append("\nPrediction interval/age=").Append(Number(PredictionSeconds)).Append('/').Append(Number(PredictionAge));
            b.Append(" knots=").Append(forecastValid ? forecastCount : 0).Append(" direct/builds/hits=");
            b.Append(DirectSurfaceFrames).Append('/').Append(PredictionBuilds).Append('/').Append(PredictionHits);
            b.Append(" heightQueries=").Append(SurfaceHeightQueries);
            b.Append("\nPrediction reason=").Append(PredictionInvalidation).Append(" fullWaterHeight=").Append(UseFullWaterHeight);
            b.Append(" nativeWaterThrottling=").Append(SeasonalIceFloeWaves.NativeWaterThrottlingActive);
            b.Append("\nCache geometry/noise/knots/fallbacks=").Append(GeometryCacheBuilds).Append('/');
            b.Append(ScaleNoiseReuses).Append('/').Append(ForecastKnotsSampled).Append('/').Append(PredictionDirectFallbacks);
            b.Append(" lastBuildCause=").Append(LastForecastRebuildCause);
            FloePredictionCounters totals = PredictionPerformance;
            b.Append("\nAll floes since reset: direct/builds/hits/knots=").Append(totals.DirectFrames).Append('/');
            b.Append(totals.ForecastBuilds).Append('/').Append(totals.ForecastHits).Append('/').Append(totals.SampledKnots);
            b.Append(" hull/noise/fallbacks=").Append(totals.HullRebuilds).Append('/');
            b.Append(totals.ScaleNoiseReuses).Append('/').Append(totals.SameFrameFallbacks);
            b.Append("\nBuild causes geometry/clock/water/wind/settings/authority/motion/scheduled/other=");
            b.Append(totals.GeometryResets).Append('/').Append(totals.ClockResets).Append('/').Append(totals.WaterResets).Append('/');
            b.Append(totals.WindResets).Append('/').Append(totals.SettingsResets).Append('/').Append(totals.AuthorityResets).Append('/');
            b.Append(totals.MotionResets).Append('/').Append(totals.ScheduledBuilds).Append('/').Append(totals.OtherBuilds);
        }
    }
}

namespace Seasons
{
    internal static partial class SeasonalIceFloeWaves
    {
        internal static bool NativeWaterThrottlingActive { get; private set; }

        private static float FloeWaterObservation(WaterVolume water, Vector3 position, float waveFactor, IWaterInteractable target)
        {
            if (target is Floating floating && floaters.TryGetValue(floating, out IceFloeClimb controller) &&
                controller.WaveValid && controller.SurfaceMode != IceFloeClimb.FloeSurfaceMode.FullRate)
            {
                // Retain registration, Increment/Decrement and SetLiquidLevel callbacks.
                // Only replace this duplicate wave query. Physics supplies its own current
                // interpolated level before impact/surface effects; replicas need no waves.
                float level = water.transform.position.y + water.m_surfaceOffset;
                if (water.m_forceDepth < 0f && Utils.LengthXZ(position) > 10500f)
                    level -= 100f;
                return level;
            }
            return water.GetWaterSurface(position, waveFactor);
        }

        [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.UpdateFloaters))]
        private static class WaterVolume_UpdateFloaters_FloeSampling
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
            {
                List<CodeInstruction> code = new List<CodeInstruction>(instructions);
                MethodInfo sample = AccessTools.Method(typeof(WaterVolume), nameof(WaterVolume.GetWaterSurface),
                    new[] { typeof(Vector3), typeof(float) });
                int local = -1;
                MethodBody body = __originalMethod.GetMethodBody();
                if (body != null)
                {
                    foreach (LocalVariableInfo variable in body.LocalVariables)
                    {
                        if (variable.LocalType != typeof(IWaterInteractable))
                            continue;
                        if (local >= 0)
                        {
                            local = -1;
                            break;
                        }
                        local = variable.LocalIndex;
                    }
                }
                int call = -1, count = 0;
                for (int i = 0; i < code.Count; i++)
                {
                    if (sample != null && code[i].Calls(sample))
                    {
                        call = i;
                        count++;
                    }
                }
                if (local < 0 || count != 1 || code[call].blocks.Count != 0)
                {
                    Seasons.LogWarning("Floe water observation throttling was not applied: unexpected WaterVolume.UpdateFloaters IL. Native observations remain enabled.");
                    NativeWaterThrottlingActive = false;
                    return code;
                }
                CodeInstruction argument = new CodeInstruction(OpCodes.Ldloc, local);
                argument.labels.AddRange(code[call].labels);
                code[call].labels.Clear();
                code.Insert(call, argument);
                code[call + 1] = new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(SeasonalIceFloeWaves), nameof(FloeWaterObservation)));
                NativeWaterThrottlingActive = true;
                return code;
            }
        }
    }
}
