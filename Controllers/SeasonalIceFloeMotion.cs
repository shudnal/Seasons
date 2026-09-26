using System.Text;
using UnityEngine;

namespace Seasons
{
    // Body mode is independent of the lease. Native ownership keeps dynamic contacts;
    // the ownerless lease still selects the single publisher of kinematic wave motion.
    public partial class IceFloeClimb
    {
        [Header("Kinematic wave following (shared runtime setting)")]
        public static float KinematicResponseSeconds = 0.15f;

        public bool OwnerlessKinematic { get; private set; }
        public long KinematicUpdates, KinematicReplicaUpdates;
        public Vector3 KinematicVelocity, KinematicAngularVelocity;
        private bool motionGravity, motionCollisions, motionSyncKinematic;
        private CollisionDetectionMode motionCollisionMode;
        private RigidbodyInterpolation motionInterpolation;
        private int kinematicFrame = -1;
        private const float ReplicaResponseSeconds = 0.1f;

        private static int sharedMotionSettingsFrame = -1;
        private static SurfaceSettings sharedMotionSettings;
        private static float sharedUnscaledThickness;
        private static float sharedBlendDelta = -1f, sharedBlendResponse;
        private static float sharedMotionBlend, sharedReplicaBlend;
        private ZDO scaleSource;
        private ZDOID scaleSourceId;
        private uint scaleSourceRevision;
        private Vector3 synchronizedLocalScale;
        private bool scaleSourceValid;

        internal static void InvalidateSharedMotionSettings() => sharedMotionSettingsFrame = -1;

        private SurfaceSettings ReadKinematicSettings()
        {
            if (sharedMotionSettingsFrame != Time.frameCount)
            {
                // All runtime coefficients are shared. Validate them once for kinematic
                // work, but NEVER reuse the first floe's scaled displacement thickness.
                sharedMotionSettings = ReadSettings();
                sharedUnscaledThickness = Setting(HullThickness, 1f, 0.1f, 10f);
                sharedMotionSettingsFrame = Time.frameCount;
            }
            SurfaceSettings settings = sharedMotionSettings;
            float scaleY = Setting(Mathf.Abs(Root.lossyScale.y), 1f, 0.01f, 100f);
            settings.Thickness = Mathf.Clamp(sharedUnscaledThickness * scaleY, 0.05f, 100f);
            return settings;
        }

        private static float MotionBlend(float dt, bool replica = false)
        {
            float response = Setting(KinematicResponseSeconds, 0.15f, 0.02f, 1f);
            if (sharedBlendDelta != dt || sharedBlendResponse != response)
            {
                sharedBlendDelta = dt;
                sharedBlendResponse = response;
                sharedMotionBlend = 1f - Mathf.Exp(-dt / response);
                sharedReplicaBlend = 1f - Mathf.Exp(-dt / ReplicaResponseSeconds);
            }
            return replica ? sharedReplicaBlend : sharedMotionBlend;
        }

        private void UpdateOwnerlessBodyMode(ZDO zdo)
        {
            if (Distant)
                return;
            if (zdo.HasOwner())
            {
                RestoreOwnerlessBody();
                return;
            }
            if (OwnerlessKinematic || Body.isKinematic)
                return; // Do not take over an unrelated external kinematic override.
            motionGravity = Body.useGravity;
            motionCollisions = Body.detectCollisions;
            motionCollisionMode = Body.collisionDetectionMode;
            motionInterpolation = Body.interpolation;
            motionSyncKinematic = Sync.m_isKinematicBody;
            KinematicVelocity = Body.linearVelocity;
            KinematicAngularVelocity = Body.angularVelocity;
            StopMotion();
            Body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            Body.detectCollisions = false;
            Body.useGravity = false;
            Body.isKinematic = true;
            Body.interpolation = RigidbodyInterpolation.None;
            Sync.m_isKinematicBody = true;
            OwnerlessKinematic = true;
            scaleSourceValid = false;
            kinematicFrame = -1;
            InvalidatePrediction("Ownerless kinematic motion started");
        }

        private void RestoreOwnerlessBody()
        {
            if (!OwnerlessKinematic)
                return;
            ReleaseBackgroundForecast();
            OwnerlessKinematic = false;
            scaleSourceValid = false;
            scaleSource = null;
            if (Body)
            {
                Body.isKinematic = false;
                Body.collisionDetectionMode = motionCollisionMode;
                Body.detectCollisions = motionCollisions;
                Body.interpolation = motionInterpolation;
                Body.useGravity = motionGravity;
                Body.linearVelocity = Finite(KinematicVelocity) ? KinematicVelocity : Vector3.zero;
                Body.angularVelocity = Finite(KinematicAngularVelocity) ? KinematicAngularVelocity : Vector3.zero;
                Body.WakeUp();
            }
            if (Sync)
            {
                Sync.m_isKinematicBody = motionSyncKinematic;
                Sync.m_lastUpdateFrame = -1;
                Sync.m_positionCached = Sync.m_velocityCached = Vector3.negativeInfinity;
            }
            kinematicFrame = -1;
            diagnosticStepPending = false;
            InvalidatePrediction("Native dynamic body restored");
        }

        // Driven by OwnerSync (normally LateUpdate), once per rendered frame. The
        // FixedUpdate entry does no water/force work for these non-colliding bodies.
        internal void UpdateOwnerlessMotion()
        {
            if (!OwnerlessKinematic || Distant || !WaveValid || kinematicFrame == Time.frameCount ||
                Game.IsPaused() || Time.timeScale <= 0f || !TryAuthorityTime(out double now))
                return;
            kinematicFrame = Time.frameCount;
            if (m_view.GetZDO().HasOwner())
                return; // Never overwrite a newly acquired native owner's pose.
            SynchronizeOwnerlessScale();
            float dt = Time.deltaTime;
            if (!Finite(dt) || dt <= 0f || !Body.isKinematic)
                return;
            LastForceCalls = 0;
            diagnosticStepPending = false;
            UpdatePhysicsMass();
            if (!HasCurrentFallbackLease())
            {
                ReleaseBackgroundForecast();
                UpdateKinematicReplica(dt, now);
                return;
            }
            RecoverInvalidHeight();
            Collider collider = m_floating.m_collider;
            if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy ||
                collider.attachedRigidbody != Body || !ReadHullGeometry(collider, out HullGeometry hull))
            {
                FailKinematicMotion(WaveStatus.NoHullGeometry);
                return;
            }
            SurfaceSettings settings = ReadKinematicSettings();
            backgroundPending = false;
            if (!SeasonalIceFloeWaves.TrySurfaceContext(this, out SeasonalIceFloeWaves.SurfaceContext context) ||
                !TryKinematicSurfaceFrame(context, settings, hull, out SurfaceFrame frame))
            {
                if (backgroundPending)
                {
                    // A queued forecast is not a lost lease or missing water. Keep this
                    // pose without extrapolating; publish zero velocities and renew normally.
                    KinematicVelocity = KinematicAngularVelocity = Vector3.zero;
                    Body.useGravity = false;
                    m_floating.SetSurfaceEffect(false);
                    Status = WaveStatus.KinematicWaiting;
                    RecordFallbackMotionStep();
                }
                else
                    FailKinematicMotion(WaveStatus.NoSurface);
                return;
            }
            RestoreGravity();
            Body.useGravity = false;
            Quaternion beforeRotation = Body.rotation;
            Vector3 beforePosition = Body.position;
            Vector3 beforeCom = Body.worldCenterOfMass;
            Vector3 comOffset = Quaternion.Inverse(beforeRotation) * (beforeCom - beforePosition);
            float blend = MotionBlend(dt);
            Quaternion rotation = Quaternion.Slerp(beforeRotation,
                Quaternion.FromToRotation(beforeRotation * Vector3.up, frame.Normal) * beforeRotation, blend);
            float targetHeight = frame.Height + settings.HeightOffset +
                (0.5f - settings.RestingSubmergence) * hull.Thickness;
            Vector3 hullCenter = hull.Center;
            hullCenter.y = Mathf.Lerp(hull.Center.y, targetHeight, blend);
            // Keep the geometric center's X/Z, not a bottom/off-center pivot, stationary.
            // No speculative horizontal impulses or collision prediction outside native ownership.
            Vector3 position = hullCenter - rotation * hullCenterOffset;
            Vector3 velocity = (position + rotation * comOffset - beforeCom) / dt;
            Vector3 omega = QuaternionVelocity(beforeRotation, rotation, dt);
            if (!Finite(position) || !FiniteRotation(rotation) || !Finite(velocity) || !Finite(omega))
            {
                FailKinematicMotion(WaveStatus.InvalidBody);
                return;
            }
            bool capture = !FreezeDiagnostics && (DiagnosticsEnabled || Time.unscaledTime <= hoverUntil);
            if (capture)
            {
                BeginDiagnostics(dt, frame, settings, context);
                WaveDiagnostics d = Diagnostics;
                d.Kinematic = true;
                if (UsingBackgroundForecast && oceanInputsValid)
                    d.WindIntensity = oceanCurve.Input.Wind.Tilt.Intensity;
                d.Velocity = KinematicVelocity;
                d.AngularVelocity = KinematicAngularVelocity;
                d.SampleVelocity = KinematicVelocity + Vector3.Cross(KinematicAngularVelocity, hull.Center - beforeCom);
                d.HeightError = targetHeight - hull.Center.y;
                d.TargetHullCenterY = targetHeight;
                d.TargetComHeight = beforeCom.y + d.HeightError;
                d.TargetPivotY = position.y;
                d.NominalHullSubmergence = Mathf.Clamp01(0.5f +
                    (frame.Height + settings.HeightOffset - hull.Center.y) / hull.Thickness);
                d.ActualUp = beforeRotation * Vector3.up;
                d.TiltErrorDegrees = Vector3.Angle(d.ActualUp, frame.Normal);
                d.RelativeVerticalVelocity = d.SampleVelocity.y - frame.VerticalVelocity;
                d.Captured = true; // Forces remain zero: this is a pose sample, not a solver sample.
            }
            Body.position = position;
            Body.rotation = rotation;
            KinematicVelocity = velocity;
            KinematicAngularVelocity = omega;
            m_floating.m_waterLevel = frame.Height;
            m_floating.SetSurfaceEffect(false);
            Status = WaveStatus.KinematicFollowing;
            KinematicUpdates++;
            RecordFallbackMotionStep();
        }

        private void FailKinematicMotion(WaveStatus status)
        {
            ReleaseBackgroundForecast();
            Status = status;
            KinematicVelocity = KinematicAngularVelocity = Vector3.zero;
            m_floating.SetSurfaceEffect(false);
            WithdrawSimulationAuthority("Kinematic surface unavailable", LeaseTimeoutSeconds);
        }

        private void SynchronizeOwnerlessScale()
        {
            Sync.m_lastUpdateFrame = Time.frameCount;
            if (!Sync.m_syncScale)
                return;
            ZDO zdo = m_view.GetZDO();
            Vector3 localScale = Root.localScale;
            // Saved scale does not change with every visual pose update. Still notice
            // manual/local scale edits even when no new ZDO revision has arrived.
            if (scaleSourceValid && ReferenceEquals(scaleSource, zdo) && scaleSourceId == zdo.m_uid &&
                scaleSourceRevision == zdo.DataRevision && synchronizedLocalScale.Equals(localScale))
                return;
            Vector3 scale = zdo.GetVec3(ZDOVars.s_scaleHash, Vector3.zero);
            if (scale != Vector3.zero && Finite(scale))
            {
                if (!Root.localScale.Equals(scale))
                    Root.localScale = scale;
            }
            else
            {
                float scalar = zdo.GetFloat(ZDOVars.s_scaleScalarHash, Root.localScale.x);
                if (Finite(scalar) && scalar > 0f && !Root.localScale.Equals(Vector3.one * scalar))
                    Root.localScale = Vector3.one * scalar;
            }
            scaleSource = zdo;
            scaleSourceId = zdo.m_uid;
            scaleSourceRevision = zdo.DataRevision;
            synchronizedLocalScale = Root.localScale;
            scaleSourceValid = true;
        }

        private void UpdateKinematicReplica(float dt, double now)
        {
            ZDO zdo = m_view.GetZDO();
            Vector3 target = zdo.GetPosition();
            Quaternion rotation = zdo.GetRotation();
            if (!Finite(target) || !FiniteRotation(rotation))
            {
                Status = WaveStatus.InvalidBody;
                return;
            }
            long token = zdo.GetLong(simulatorTokenKey);
            if (replicaPoseToken != token || replicaPoseRevision != zdo.DataRevision)
            {
                replicaPoseToken = token;
                replicaPoseRevision = zdo.DataRevision;
                replicaPoseObservedAt = now;
            }
            bool live = IsFallbackReplica();
            Vector3 velocity = live && now - replicaPoseObservedAt <= 0.5
                ? zdo.GetVec3(ZDOVars.s_bodyVelHash, Vector3.zero) : Vector3.zero;
            Vector3 omega = live && now - replicaPoseObservedAt <= 0.5
                ? zdo.GetVec3(ZDOVars.s_bodyAVelHash, Vector3.zero) : Vector3.zero;
            // Only smooth the received pose. No water calculations, local impulses or
            // unlimited extrapolation of a stalled publisher on a replica.
            float blend = MotionBlend(dt, replica: true);
            Body.position = Vector3.Lerp(Body.position, target, blend);
            Body.rotation = Quaternion.Slerp(Body.rotation, rotation, blend);
            Body.useGravity = false;
            KinematicVelocity = Finite(velocity) ? velocity : Vector3.zero;
            KinematicAngularVelocity = Finite(omega) ? omega : Vector3.zero;
            Status = live ? WaveStatus.KinematicReplica : WaveStatus.KinematicWaiting;
            KinematicReplicaUpdates++;
        }

        private static Vector3 QuaternionVelocity(Quaternion from, Quaternion to, float dt)
        {
            Quaternion delta = to * Quaternion.Inverse(from);
            if (delta.w < 0f)
                delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
            Vector3 axis = new Vector3(delta.x, delta.y, delta.z);
            float sine = axis.magnitude;
            return sine > 0.000001f ? axis * (2f * Mathf.Atan2(sine, delta.w) / (sine * dt)) : Vector3.zero;
        }

        private void AppendMotionDiagnostics(StringBuilder b)
        {
            b.Append("\nBody motion=").Append(Distant ? "FarVisual" : OwnerlessKinematic ? "OwnerlessKinematic" : "NativeDynamic");
            b.Append(" hasNativeOwner=").Append(m_view.HasOwner()).Append(" kinematic=").Append(Body.isKinematic);
            b.Append(" collisions=").Append(Body.detectCollisions);
            b.Append(" pose/replica updates=").Append(KinematicUpdates).Append('/').Append(KinematicReplicaUpdates);
        }
    }
}
