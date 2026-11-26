using HarmonyLib;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    public static partial class CharacterExtentions_FrozenOceanSliding
    {
        private static bool cadenceHookReady;
        private static bool fovHookReady;
        private const float FeedbackTransitionTime = 0.25f;
        private static GameCamera fovCamera;
        private static Player fovPlayer;
        private static float glideFovBlend;
        private static float glideFovBlendVelocity;
        private static float glideFovLimit;

        private static float ChargeFeedback(PlayerSlide state) => IsPreparing(state)
            ? Mathf.SmoothStep(0f, 1f, state.ChargeTime / ChargeDuration) : 0f;

        private static float GetTargetGlideFovBlend()
        {
            if (!frozenOceanGlidingFovEnabled.Value || !frozenOceanGlidingEnabled.Value ||
                !TrySlide(Player.m_localPlayer, out PlayerSlide state) || !state.OnIce || !state.Player.IsOnIce() ||
                HasBlockingAction(state.Player) || state.RunSpeed <= 0f)
                return 0f;

            float bonus = state.Mode == SlideMode.Glide ? state.PeakFraction - 1f : GlideBonus;
            if (bonus <= 0f)
                return 0f;
            Vector3 actual = Horizontal(state.Player.m_body.linearVelocity);
            if (!Finite(actual))
                return 0f;

            float actualBonus = actual.magnitude / state.RunSpeed - 1f;
            if (state.Mode == SlideMode.Glide)
            {
                // Keep the charged cue through the scheduled acceleration, while
                // allowing actual speed losses to reduce it even before peak speed.
                if (state.GlideTime < state.LaunchDuration)
                    actualBonus += state.PeakFraction - GlideSpeedProfile(state, state.GlideTime);
                return Mathf.Clamp01(actualBonus / bonus);
            }

            return IsPreparing(state)
                ? Mathf.Max(Mathf.Clamp01(actualBonus / bonus), ChargeFeedback(state))
                : 0f;
        }

        private static void UpdateGlideFov(GameCamera camera, float dt)
        {
            if (!fovHookReady || camera != GameCamera.instance)
                return;

            Player player = Player.m_localPlayer;
            if (fovCamera != camera || fovPlayer != player)
            {
                fovCamera = camera;
                fovPlayer = player;
                glideFovBlend = glideFovBlendVelocity = 0f;
            }

            // A normal interruption fades the camera even if playerSlide was released.
            // Never carry this visual state into a different life or free-fly camera.
            if (!player || player.IsDead() || player.IsTeleporting() || camera.m_freeFly)
            {
                glideFovBlend = glideFovBlendVelocity = 0f;
                return;
            }

            float target = GetTargetGlideFovBlend();
            if (glideFovBlend == 0f && glideFovBlendVelocity == 0f && target == 0f)
                return;

            if (Finite(dt) && dt > 0f)
            {
                glideFovBlend = Mathf.Clamp01(Mathf.SmoothDamp(glideFovBlend, target, ref glideFovBlendVelocity,
                    FeedbackTransitionTime, Mathf.Infinity, dt));
                if (Mathf.Abs(glideFovBlend - target) < 0.0001f && Mathf.Abs(glideFovBlendVelocity) < 0.001f)
                {
                    glideFovBlend = target;
                    glideFovBlendVelocity = 0f;
                }
            }

            // Configured FOV is horizontal, Unity's fieldOfView is vertical.
            glideFovLimit = Camera.HorizontalToVerticalFieldOfView(
                Parameter(frozenOceanGlidingMaxFov.Value, 105f, 60f, 150f), camera.m_camera.aspect);
        }

        private static float GetGlideFovOffset() =>
            fovCamera && fovCamera == GameCamera.instance
                ? Mathf.Max(0f, glideFovLimit - fovCamera.m_fov) * glideFovBlend
                : 0f;

        private static float ResolveGlideFov(float baseFov, GameCamera camera)
        {
            if (camera != fovCamera || camera.m_freeFly || !Finite(baseFov) || glideFovBlend <= 0f)
                return baseFov;
            // Use the incoming base FOV on both camera writes. Never narrow it.
            return Mathf.Lerp(baseFov, Mathf.Max(baseFov, glideFovLimit), glideFovBlend);
        }

        private static void UpdateRunCadence(PlayerSlide state, float dt)
        {
            float target = IsPreparing(state) && state.Player.m_running && !HasBlockingAction(state.Player)
                ? ChargeFeedback(state) : 0f;
            state.CadenceFeedback = Mathf.MoveTowards(state.CadenceFeedback, target, dt / FeedbackTransitionTime);
        }

        private static float ResolveRunCadence(float value, Character character)
        {
            if (!Finite(value) || !TrySlide(character, out PlayerSlide state) || !state.OnIce ||
                state.CadenceFeedback <= 0f || !character.IsOnIce())
                return value;
            // Fade the extra locomotion cadence on release or cancellation. Native
            // ZSyncAnimation smoothing and Animator state transitions remain intact.
            float result = value * (1f + state.CadenceFeedback * Parameter(frozenOceanGlidingAnimationBonus.Value, 0.25f, 0f, 1f));
            return Finite(result) ? result : value;
        }

        private const float SlidingDiagnosticsInterval = 0.1f;
        private static Player slidingDiagnosticsPlayer;
        private static string slidingDiagnosticsText;
        private static float slidingDiagnosticsAt;
        private static Player lastSlidingExitPlayer;
        private static SlideMode lastSlidingExitMode;
        private static SlideReason lastSlidingExitReason;
        private static float lastSlidingExitAt;

        internal static void ClearSlidingDiagnostics()
        {
            ClearPhysicsDiagnostics();
            slidingDiagnosticsPlayer = lastSlidingExitPlayer = null;
            slidingDiagnosticsText = null;
            slidingDiagnosticsAt = 0f;
            if (playerSlide != null)
                playerSlide.DiagnosticTurnDegrees = playerSlide.DiagnosticTurnLoss = 0f;
        }

        private const float PhysicsDeviationThreshold = 0.02f;
        private const int PhysicsTraceBefore = 25;
        private const int PhysicsTraceAfter = 25;

        private struct PhysicsMotionSample
        {
            internal bool Grounded, Ice, Running, Slipping, HasSlide, OnIce, MovementHeld, RunHeld;
            internal SlideMode Mode;
            internal SlideReason Reason;
            internal int GroundId, Transitions;
            internal float RunUp, Charge, GlideTime, GlideElapsed, RunSpeed, TargetSpeed;
            internal Vector3 GroundNormal, Input, CurrentVelocity;
        }

        private struct PhysicsStepSample
        {
            internal bool Valid, Grounded, Ice, SeasonalCorrection, ForceApplied, TargetRecorded, ForceChecked;
            internal SlideMode Mode;
            internal float Time, Dt, NextTime, Threshold;
            internal int Frame, NextFrame;
            internal Vector2s FromZone, ToZone;
            internal PhysicsMotionSample StartMotion, EndMotion;
            internal Vector3 StartPosition, PrePhysicsPosition, Position, Before, AtTarget, Target, AtForce,
                Requested, Applied, BeforePhysics, Queued, After;
            internal Vector3 Impulse, GroundNormal, ContactNormal;
            internal int GroundId, ContactId, ContactPairs, IcePairs, ContactPoints;

            internal Vector3 Expected => BeforePhysics + Queued;
            internal Vector3 Residual => Horizontal(After - Expected);
            internal float SpeedError => Horizontal(After).magnitude - Horizontal(Expected).magnitude;
        }

        private static Player physicsDiagnosticsPlayer;
        private static PhysicsContactDiagnostics physicsContactDiagnostics;
        private static bool physicsDiagnosticsRecording;
        private static bool physicsDiagnosticsPending;
        private static PhysicsStepSample physicsStep, lastPhysicsStep, lastZoneStep, lastDeviationStep;
        private static PhysicsStepSample[] physicsHistory, physicsCrossingTrace;
        private static int physicsHistoryNext, physicsHistoryCount, physicsTraceCount, physicsTraceCrossingIndex;
        private static BepInEx.Logging.ManualLogSource physicsTraceLog;

        private static void ResetPhysicsTrace()
        {
            physicsHistoryNext = physicsHistoryCount = physicsTraceCount = physicsTraceCrossingIndex = 0;
        }

        private static void ClearPhysicsDiagnostics()
        {
            physicsDiagnosticsRecording = physicsDiagnosticsPending = false;
            physicsDiagnosticsPlayer = null;
            physicsStep = lastPhysicsStep = lastZoneStep = lastDeviationStep = default;
            ResetPhysicsTrace();
            if (physicsContactDiagnostics)
                UnityEngine.Object.Destroy(physicsContactDiagnostics);
            physicsContactDiagnostics = null;
        }

        private static bool RecordingPhysics(Character character) =>
            physicsDiagnosticsRecording && ReferenceEquals(physicsDiagnosticsPlayer, character);

        private static void RecordPhysicsTarget(Character character, Vector3 velocity, bool seasonalCorrection)
        {
            if (!RecordingPhysics(character))
                return;
            physicsStep.TargetRecorded = true;
            physicsStep.AtTarget = character.m_body.linearVelocity;
            physicsStep.Target = velocity;
            physicsStep.SeasonalCorrection = seasonalCorrection;
        }

        private static float RecordPhysicsForceRequest(float threshold, Vector3 correction, Character character)
        {
            if (RecordingPhysics(character))
            {
                physicsStep.ForceChecked = true;
                physicsStep.AtForce = character.m_body.linearVelocity;
                physicsStep.Grounded = character.IsOnGround();
                physicsStep.Ice = character.IsOnIce();
                physicsStep.Mode = playerSlide != null && ReferenceEquals(playerSlide.Player, character)
                    ? playerSlide.Mode : SlideMode.Basic;
                physicsStep.Requested = correction;
                physicsStep.Threshold = threshold;
                Collider ground = character.GetLastGroundCollider();
                physicsStep.GroundId = ground ? ground.GetInstanceID() : 0;
                physicsStep.GroundNormal = character.m_lastGroundNormal;
            }
            return threshold;
        }

        private static void RecordPhysicsForceApplied(Vector3 correction, Character character)
        {
            if (!RecordingPhysics(character))
                return;
            physicsStep.ForceApplied = true;
            physicsStep.Applied = correction;
        }

        // The original Character update is left intact. The following fixed update
        // observes the previous simulation, not a render-frame velocity estimate.
        [HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
        private static class Character_CustomFixedUpdate_PhysicsDiagnostics
        {
            [HarmonyPrefix, HarmonyPriority(Priority.First)]
            private static void Prefix(Character __instance, float dt)
            {
                if (__instance != Player.m_localPlayer || showSlidingDiagnosticsOnHover?.Value != true || !walkingForceHookReady)
                    return;
                Player player = (Player)__instance;
                Rigidbody body = player.m_body;
                if (!body || body.isKinematic || !player.m_nview || !player.m_nview.IsValid() ||
                    !player.m_nview.IsOwner() || player.IsDead() || player.IsTeleporting() || player.IsDebugFlying())
                {
                    physicsDiagnosticsRecording = physicsDiagnosticsPending = false;
                    ResetPhysicsTrace();
                    return;
                }

                if (!ReferenceEquals(physicsDiagnosticsPlayer, player))
                {
                    ClearPhysicsDiagnostics();
                    physicsDiagnosticsPlayer = player;
                    physicsContactDiagnostics = player.gameObject.AddComponent<PhysicsContactDiagnostics>();
                }

                if (physicsDiagnosticsPending)
                {
                    physicsStep.After = body.linearVelocity;
                    physicsStep.Position = body.position;
                    physicsStep.ToZone = ZoneSystem.GetZone(physicsStep.Position);
                    physicsStep.NextTime = Time.fixedTime;
                    physicsStep.NextFrame = Time.frameCount;
                    lastPhysicsStep = physicsStep;
                    if (physicsStep.FromZone != physicsStep.ToZone)
                        lastZoneStep = physicsStep;
                    if ((physicsStep.Grounded || physicsStep.StartMotion.Grounded) &&
                        Mathf.Abs(physicsStep.SpeedError) >= PhysicsDeviationThreshold)
                        lastDeviationStep = physicsStep;
                    RecordCrossingTrace(physicsStep);
                }

                physicsStep = new PhysicsStepSample
                {
                    Valid = true,
                    Time = Time.fixedTime,
                    Dt = dt,
                    Frame = Time.frameCount,
                    FromZone = ZoneSystem.GetZone(body.position),
                    StartPosition = body.position,
                    Before = body.linearVelocity,
                    StartMotion = ReadPhysicsMotion(player),
                    ContactNormal = Vector3.up
                };
                physicsDiagnosticsPending = false;
                physicsDiagnosticsRecording = true;
            }

            [HarmonyPostfix, HarmonyPriority(Priority.Last)]
            private static void Postfix(Character __instance)
            {
                if (!RecordingPhysics(__instance))
                    return;
                Rigidbody body = __instance.m_body;
                physicsDiagnosticsRecording = false;
                if (!body || body.isKinematic)
                {
                    ResetPhysicsTrace();
                    return;
                }
                // Retain steps that did not reach the walking target or force guard.
                physicsStep.EndMotion = ReadPhysicsMotion((Player)__instance);
                if (!physicsStep.ForceChecked)
                {
                    physicsStep.Grounded = physicsStep.EndMotion.Grounded;
                    physicsStep.Ice = physicsStep.EndMotion.Ice;
                    physicsStep.Mode = physicsStep.EndMotion.Mode;
                    physicsStep.GroundId = physicsStep.EndMotion.GroundId;
                    physicsStep.GroundNormal = physicsStep.EndMotion.GroundNormal;
                }
                physicsStep.PrePhysicsPosition = body.position;
                physicsStep.BeforePhysics = body.linearVelocity;
                // Unity reports the accumulated force in ForceMode.Force units.
                physicsStep.Queued = body.GetAccumulatedForce(physicsStep.Dt) * (physicsStep.Dt / body.mass);
                physicsDiagnosticsPending = true;
            }
        }

        private static PhysicsMotionSample ReadPhysicsMotion(Player player)
        {
            PlayerSlide state = playerSlide != null && ReferenceEquals(playerSlide.Player, player) ? playerSlide : null;
            Collider ground = player.GetLastGroundCollider();
            return new PhysicsMotionSample
            {
                Grounded = player.IsOnGround(),
                Ice = player.IsOnIce(),
                Running = player.m_running,
                Slipping = player.m_slipping,
                GroundId = ground ? ground.GetInstanceID() : 0,
                GroundNormal = player.m_lastGroundNormal,
                Input = player.m_moveDir,
                CurrentVelocity = player.m_currentVel,
                HasSlide = state != null,
                OnIce = state?.OnIce ?? false,
                Mode = state?.Mode ?? SlideMode.Basic,
                Reason = state?.Reason ?? SlideReason.Surface,
                MovementHeld = state?.MovementHeld ?? false,
                RunHeld = state?.RunKeyHeld ?? false,
                Transitions = state?.Transitions ?? 0,
                RunUp = state?.RunUpTime ?? 0f,
                Charge = state?.ChargeTime ?? 0f,
                GlideTime = state?.GlideTime ?? 0f,
                GlideElapsed = state?.GlideElapsed ?? 0f,
                RunSpeed = state?.RunSpeed ?? 0f,
                TargetSpeed = state?.TargetSpeed ?? 0f
            };
        }

        private static void RecordCrossingTrace(PhysicsStepSample sample)
        {
            physicsHistory ??= new PhysicsStepSample[PhysicsTraceBefore];
            bool crossing = sample.FromZone != sample.ToZone &&
                (sample.Ice || sample.StartMotion.Ice || sample.EndMotion.Ice ||
                 sample.StartMotion.OnIce || sample.EndMotion.OnIce || sample.IcePairs > 0);
            if (physicsTraceCount == 0 && crossing)
            {
                physicsCrossingTrace ??= new PhysicsStepSample[PhysicsTraceBefore + 1 + PhysicsTraceAfter];
                int oldest = (physicsHistoryNext - physicsHistoryCount + PhysicsTraceBefore) % PhysicsTraceBefore;
                for (int i = 0; i < physicsHistoryCount; ++i)
                    physicsCrossingTrace[i] = physicsHistory[(oldest + i) % PhysicsTraceBefore];
                physicsTraceCrossingIndex = physicsTraceCount = physicsHistoryCount;
            }

            // A second crossing inside the same window is marked in the same trace.
            if (physicsTraceCount > 0 || crossing)
            {
                physicsCrossingTrace[physicsTraceCount++] = sample;
                if (physicsTraceCount == physicsTraceCrossingIndex + 1 + PhysicsTraceAfter)
                {
                    WriteCrossingTrace();
                    physicsTraceCount = 0;
                }
            }

            physicsHistory[physicsHistoryNext] = sample;
            physicsHistoryNext = (physicsHistoryNext + 1) % PhysicsTraceBefore;
            if (physicsHistoryCount < PhysicsTraceBefore)
                ++physicsHistoryCount;
        }

        private static string DiagnosticVector(Vector3 value) =>
            string.Format(CultureInfo.InvariantCulture, "({0:F3},{1:F3},{2:F3})", value.x, value.y, value.z);

        private static void AppendTraceMotion(StringBuilder text, string label, PhysicsMotionSample motion)
        {
            SlidingDiagnosticLine(text,
                "  {0}: state={1} reason={2} onIce={3} transitions={4} ground={5} ice={6} collider={7} normal={8} run={9} slip={10} held={11}/{12} input={13} currentVel={14} steady={15:F3} charge={16:F3} glide={17:F3}/{18:F3} runRef={19:F3} targetSpeed={20:F3}",
                label, motion.HasSlide ? motion.Mode.ToString() : "None", motion.HasSlide ? motion.Reason.ToString() : "-",
                motion.OnIce, motion.Transitions, motion.Grounded, motion.Ice, motion.GroundId, DiagnosticVector(motion.GroundNormal),
                motion.Running, motion.Slipping, motion.MovementHeld, motion.RunHeld, DiagnosticVector(motion.Input),
                DiagnosticVector(motion.CurrentVelocity), motion.RunUp, motion.Charge, motion.GlideTime, motion.GlideElapsed,
                motion.RunSpeed, motion.TargetSpeed);
        }

        private static void WriteCrossingTrace()
        {
            var text = new StringBuilder(32768);
            SlidingDiagnosticLine(text, "Sliding crossing trace: {0} steps before, {1} after. XYZ vectors; speeds in m/s.",
                physicsTraceCrossingIndex, PhysicsTraceAfter);
            for (int i = 0; i < physicsTraceCount; ++i)
            {
                PhysicsStepSample sample = physicsCrossingTrace[i];
                SlidingDiagnosticLine(text,
                    "[{0:+0;-0;0}] {1} zone=({2},{3})->({4},{5}) t={6:F3}->{7:F3} dt={8:F3} frame={9}->{10}",
                    i - physicsTraceCrossingIndex, sample.FromZone != sample.ToZone ? "CROSS" : "STEP",
                    sample.FromZone.x, sample.FromZone.y, sample.ToZone.x, sample.ToZone.y,
                    sample.Time, sample.NextTime, sample.Dt, sample.Frame, sample.NextFrame);
                SlidingDiagnosticLine(text, "  pos={0} prePhysics={1} next={2}",
                    DiagnosticVector(sample.StartPosition), DiagnosticVector(sample.PrePhysicsPosition), DiagnosticVector(sample.Position));
                SlidingDiagnosticLine(text,
                    "  speed={0:F3}->{1:F3}->{2:F3} expected={3:F3} err={4:+0.000;-0.000;0.000} v={5} prePhysics={6} queued={7} next={8} residualXZ={9}",
                    Horizontal(sample.Before).magnitude, Horizontal(sample.BeforePhysics).magnitude, Horizontal(sample.After).magnitude,
                    Horizontal(sample.Expected).magnitude, sample.SpeedError, DiagnosticVector(sample.Before),
                    DiagnosticVector(sample.BeforePhysics), DiagnosticVector(sample.Queued), DiagnosticVector(sample.After),
                    DiagnosticXZ(sample.Residual));
                SlidingDiagnosticLine(text,
                    "  targetRecorded={0} atTarget={1} target={2} seasonal={3} forceChecked={4} atForce={5} request={6} guard={7} applied={8} sent={9}",
                    sample.TargetRecorded, sample.TargetRecorded ? DiagnosticVector(sample.AtTarget) : "-",
                    sample.TargetRecorded ? DiagnosticVector(sample.Target) : "-", sample.SeasonalCorrection,
                    sample.ForceChecked, sample.ForceChecked ? DiagnosticVector(sample.AtForce) : "-",
                    sample.ForceChecked ? DiagnosticVector(sample.Requested) : "-",
                    sample.ForceChecked ? sample.Threshold.ToString("F3", CultureInfo.InvariantCulture) : "-",
                    sample.ForceApplied, sample.ForceApplied ? DiagnosticVector(sample.Applied) : "-");
                SlidingDiagnosticLine(text, "  contact: impulse={0} pairs={1} icePairs={2} points={3} collider={4} normal={5}",
                    DiagnosticVector(sample.Impulse), sample.ContactPairs, sample.IcePairs, sample.ContactPoints,
                    sample.ContactId, DiagnosticVector(sample.ContactNormal));
                AppendTraceMotion(text, "start", sample.StartMotion);
                AppendTraceMotion(text, "end", sample.EndMotion);
            }
            text.Append("End sliding crossing trace.");
            // The explicit diagnostic switch is sufficient; general Seasons logging may be disabled.
            physicsTraceLog ??= BepInEx.Logging.Logger.CreateLogSource("Seasons Sliding");
            physicsTraceLog.LogInfo(text.ToString());
        }

        // Listen only on the local player while the hover diagnostics are enabled.
        // Enter matters at seams; Stay alone misses the first contact with a tile.
        private sealed class PhysicsContactDiagnostics : MonoBehaviour
        {
            private void OnCollisionEnter(Collision collision) => RecordContact(collision);
            private void OnCollisionStay(Collision collision) => RecordContact(collision);

            private void RecordContact(Collision collision)
            {
                if (!physicsDiagnosticsPending || !ReferenceEquals(physicsContactDiagnostics, this))
                    return;
                Collider collider = collision.collider;
                if (!collider)
                    return;
                physicsStep.Impulse += collision.impulse;
                physicsStep.ContactPairs++;
                if (collider.name == ZoneSystemVariantController._iceSurfaceName)
                    physicsStep.IcePairs++;
                int count = collision.contactCount;
                for (int i = 0; i < count; ++i)
                {
                    Vector3 normal = collision.GetContact(i).normal;
                    if (physicsStep.ContactPoints == 0 ||
                        Horizontal(normal).sqrMagnitude > Horizontal(physicsStep.ContactNormal).sqrMagnitude)
                    {
                        physicsStep.ContactNormal = normal;
                        physicsStep.ContactId = collider.GetInstanceID();
                    }
                    physicsStep.ContactPoints++;
                }
            }
        }

        private static string DiagnosticXZ(Vector3 value) =>
            string.Format(CultureInfo.InvariantCulture, "({0:F3},{1:F3})", value.x, value.z);

        private static void AppendPhysicsStep(StringBuilder text, string label, PhysicsStepSample sample)
        {
            if (!sample.Valid)
            {
                SlidingDiagnosticLine(text, "{0}: -", label);
                return;
            }

            SlidingDiagnosticLine(text, "{0}: {1} ({2},{3})->({4},{5}) | t={6:F2} | dt={7:F3}",
                label, sample.Ice ? sample.Mode.ToString() : "Native", sample.FromZone.x, sample.FromZone.y,
                sample.ToZone.x, sample.ToZone.y, sample.Time, sample.Dt);
            SlidingDiagnosticLine(text, "V: {0:F3} / target {1:F3} / pre {2:F3} / exp {3:F3} / got {4:F3} | err {5:+0.000;-0.000;0.000}",
                Horizontal(sample.Before).magnitude, sample.TargetRecorded ? Horizontal(sample.Target).magnitude : float.NaN, Horizontal(sample.BeforePhysics).magnitude,
                Horizontal(sample.Expected).magnitude, Horizontal(sample.After).magnitude, sample.SpeedError);
            SlidingDiagnosticLine(text, "dV X/Z: req {0} sent {1} queued {2} | guard {3} | force {4} / seasonal {5}",
                sample.ForceChecked ? DiagnosticXZ(sample.Requested) : "-", DiagnosticXZ(sample.Applied),
                DiagnosticXZ(sample.Queued), sample.ForceChecked ? sample.Threshold.ToString("F3", CultureInfo.InvariantCulture) : "-",
                sample.ForceApplied, sample.SeasonalCorrection);
            SlidingDiagnosticLine(text, "Residual {0} | J X/Z {1} Ns | pairs {2} (ice {3}) | ground {4} / contact {5}",
                DiagnosticXZ(sample.Residual), DiagnosticXZ(sample.Impulse), sample.ContactPairs, sample.IcePairs,
                sample.GroundId, sample.ContactId);
            SlidingDiagnosticLine(text, "Normal G ({0:F3},{1:F3},{2:F3}) C ({3:F3},{4:F3},{5:F3}) | X/Z {6:F2},{7:F2}",
                sample.GroundNormal.x, sample.GroundNormal.y, sample.GroundNormal.z, sample.ContactNormal.x,
                sample.ContactNormal.y, sample.ContactNormal.z, sample.Position.x, sample.Position.z);
        }

        private static void AppendPhysicsDiagnostics(StringBuilder text, Player player)
        {
            if (!walkingForceHookReady)
            {
                text.AppendLine("Physics diagnostics: force hook unavailable");
                return;
            }
            if (!ReferenceEquals(physicsDiagnosticsPlayer, player))
                return;

            AppendPhysicsStep(text, "Step", lastPhysicsStep);
            if (!lastZoneStep.Valid || !lastPhysicsStep.Valid || lastZoneStep.Time != lastPhysicsStep.Time)
                AppendPhysicsStep(text, "Last crossing", lastZoneStep);
            if (!lastDeviationStep.Valid ||
                (lastDeviationStep.Time != lastPhysicsStep.Time &&
                 (!lastZoneStep.Valid || lastDeviationStep.Time != lastZoneStep.Time)))
                AppendPhysicsStep(text, "Last deviation", lastDeviationStep);
        }

        private static void RecordSlidingExit(PlayerSlide state, SlideReason reason)
        {
            if (showSlidingDiagnosticsOnHover?.Value != true || state.Mode == SlideMode.Basic)
                return;

            // The next walking update can overwrite Reason or recreate the state.
            // Keep the last exit visible while the next preparation is already running.
            lastSlidingExitPlayer = state.Player;
            lastSlidingExitMode = state.Mode;
            lastSlidingExitReason = reason;
            lastSlidingExitAt = Time.unscaledTime;
            slidingDiagnosticsAt = 0f;
        }

        private static string DiagnosticAngle(Vector3 from, Vector3 to) =>
            from.sqrMagnitude > 0.000001f && to.sqrMagnitude > 0.000001f
                ? Vector3.Angle(from, to).ToString("F0", CultureInfo.InvariantCulture) + "°"
                : "-";

        private static void SlidingDiagnosticLine(StringBuilder text, string format, params object[] args) =>
            text.AppendFormat(CultureInfo.InvariantCulture, format, args).Append('\n');

        private static string ReadSlidingDiagnostics(Player player, float now)
        {
            var text = new StringBuilder(768);
            SlidingDiagnosticLine(text, "<color=#ABDCEF>Seasons - Sliding diagnostics</color> | t={0:F1}s", now);
            Vector3 position = player.transform.position;
            Vector2s zone = ZoneSystem.GetZone(position);
            SlidingDiagnosticLine(text, "Zone: ({0}, {1}) | X/Z: {2:F2}, {3:F2}", zone.x, zone.y, position.x, position.z);

            PlayerSlide state = playerSlide != null && ReferenceEquals(playerSlide.Player, player) ? playerSlide : null;
            Vector3 velocity = Horizontal(player.m_body.linearVelocity);
            Vector3 input = Horizontal(player.m_moveDir);
            float speed = velocity.magnitude;
            bool ice = player.IsOnIce();

            if (!HooksReady)
                text.AppendLine("Mode: Hooks unavailable");
            else if (!ice)
                text.AppendLine(player.IsOnGround() ? "Mode: Off seasonal ice" : "Mode: Airborne");
            else if (!SlidingEnabled)
                text.AppendLine("Mode: Disabled");
            else if (player.m_iceShoes)
                text.AppendLine("Mode: Ice shoes");
            else if (player.m_skating || frozenOceanUseVanillaSlippingPlayers.Value)
                text.AppendLine("Mode: Native slipping");
            else if (state == null)
                text.AppendLine("Mode: Inactive");
            else if (IsPreparing(state))
                SlidingDiagnosticLine(text, "Mode: {0} / {1}", state.Mode, IsCharged(state) ? "Ready" :
                    state.RunUpTime < SteadyRunDuration ? "Steady" : "Charge");
            else if (state.Mode == SlideMode.Glide)
            {
                float progress = state.GlideTime;
                if (progress < state.LaunchDuration)
                    SlidingDiagnosticLine(text, "Mode: Glide / Acceleration {0:F2}/{1:F2}s", progress, state.LaunchDuration);
                else if (progress < state.LaunchDuration + BonusDuration)
                    SlidingDiagnosticLine(text, "Mode: Glide / Bonus {0:F2}/{1:F2}", progress - state.LaunchDuration, BonusDuration);
                else
                    SlidingDiagnosticLine(text, "Mode: Glide / Coast {0:F2}/{1:F2}", progress - state.LaunchDuration - BonusDuration, CoastDuration);
            }
            else
                SlidingDiagnosticLine(text, "Mode: Basic / {0}", state.Reason);

            if (state != null && state.RunSpeed > 0f)
                SlidingDiagnosticLine(text, "Speed: {0:F2} m/s | Run ref: {1:F2} ({2:F1}%)", speed, state.RunSpeed, 100f * speed / state.RunSpeed);
            else
                SlidingDiagnosticLine(text, "Speed: {0:F2} m/s | Run ref: -", speed);

            if (state != null)
            {
                if (state.Mode == SlideMode.Glide)
                {
                    float profile = GlideSpeedProfile(state, state.GlideTime);
                    float nominal = state.RunSpeed * profile;
                    SlidingDiagnosticLine(text, "Profile: {0:F2} m/s ({1:F1}%) | Diff: {2:+0.00;-0.00;0.00}", nominal, profile * 100f, speed - nominal);
                    SlidingDiagnosticLine(text, "Glide: {0:F2}s | Profile time: {1:F2}/{2:F2}", state.GlideElapsed, state.GlideTime,
                        state.LaunchDuration + BonusDuration + CoastDuration);
                    SlidingDiagnosticLine(text, "Peak: {0:F1}% | End: {1:F0}% | Entry wind: {2:+0.00;-0.00;0.00}",
                        state.PeakFraction * 100f, EndSpeedFraction * 100f, state.LaunchWindAlong);
                    SlidingDiagnosticLine(text, "Wind: {0:F2} | Along: {1:+0.00;-0.00;0.00} | Side: {2:+0.00;-0.00;0.00}",
                        state.DiagnosticWindIntensity, state.DiagnosticWindAlong, state.DiagnosticWindSide);
                    SlidingDiagnosticLine(text, "Wind decay: x{0:F2} | Drift: {1:+0.0;-0.0;0.0}°",
                        state.GlideTime < state.LaunchDuration ? 1f : state.DiagnosticWindDecay,
                        Mathf.Atan(Parameter(frozenOceanGlidingWindLateral.Value, 0.1f, 0f, 0.5f) * state.DiagnosticWindSide) * Mathf.Rad2Deg);
                    SlidingDiagnosticLine(text, "Observed turns: {0:F1}° | Speed loss: {1:F3} m/s", state.DiagnosticTurnDegrees, state.DiagnosticTurnLoss);
                }
                else
                {
                    SlidingDiagnosticLine(text, "Steady: {0:F2}/{1:F2}s | Charge: {2:F2}/{3:F2}s", state.RunUpTime, SteadyRunDuration, state.ChargeTime, ChargeDuration);
                    SlidingDiagnosticLine(text, "Target: {0:F2} m/s | Release grace: {1:F2}/{2:F2}s",
                        state.TargetSpeed, state.ReleaseTime, Parameter(frozenOceanGlidingReleaseGrace.Value, 0.25f, 0f, 2f));
                    SlidingDiagnosticLine(text, "Alignment: V/I {0} | V/H {1} | I/H {2} (max {3:F0}°)",
                        DiagnosticAngle(velocity, input), DiagnosticAngle(velocity, state.RunHeading), DiagnosticAngle(input, state.RunHeading),
                        Parameter(frozenOceanGlidingRunUpAngle.Value, 45f, 0f, 90f));
                }
            }

            SlidingDiagnosticLine(text, "Input: {0:F2} | Run: {1} | Ground: {2} | Ice: {3}", input.magnitude, player.m_running, player.IsOnGround(), ice);
            SlidingDiagnosticLine(text, "Held: {0} | Run held: {1} | Stamina: {2:F1}", state?.MovementHeld ?? false, state?.RunKeyHeld ?? false, player.GetStamina());
            SlidingDiagnosticLine(text, "Blocked: {0} | Input allowed: {1}", HasBlockingAction(player), CanReadSlideInput(player));

            GameCamera camera = GameCamera.instance;
            if (camera && camera.m_camera)
            {
                float aspect = camera.m_camera.aspect;
                float actualFov = Camera.VerticalToHorizontalFieldOfView(camera.m_camera.fieldOfView, aspect);
                float baseFov = Camera.VerticalToHorizontalFieldOfView(camera.m_fov, aspect);
                SlidingDiagnosticLine(text, "FOV H: {0:F1}° | Base: {1:F1}° | Max: {2:F1}°", actualFov, baseFov,
                    Parameter(frozenOceanGlidingMaxFov.Value, 105f, 60f, 150f));
            }

            float cadence = cadenceHookReady && state != null && state.OnIce && ice
                ? 100f * state.CadenceFeedback * Parameter(frozenOceanGlidingAnimationBonus.Value, 0.25f, 0f, 1f) : 0f;
            SlidingDiagnosticLine(text, "Cadence: +{0:F1}% | Slip anim: {1}", cadence,
                player.m_animator && player.m_animator.GetBool(Character.s_slipping));
            SlidingDiagnosticLine(text, "Hooks: motion={0} cadence={1} FOV={2}", HooksReady, cadenceHookReady, fovHookReady);
            if (ReferenceEquals(lastSlidingExitPlayer, player))
                SlidingDiagnosticLine(text, "Last exit: {0} / {1} | {2:F1}s ago", lastSlidingExitMode, lastSlidingExitReason, now - lastSlidingExitAt);
            else
                text.AppendLine("Last exit: -");

            AppendPhysicsDiagnostics(text, player);
            return text.ToString().TrimEnd('\n');
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.UpdateCrosshair))]
        private static class Hud_UpdateCrosshair_SlidingDiagnostics
        {
            [HarmonyPostfix, HarmonyPriority(Priority.Last)]
            private static void Postfix(Hud __instance, Player player)
            {
                // Native UpdateCrosshair rewrites the hover text even without a target.
                if (showSlidingDiagnosticsOnHover?.Value != true || player != Player.m_localPlayer || !player || !player.m_body ||
                    !__instance.m_hoverName || (TextViewer.instance && TextViewer.instance.IsVisible()))
                    return;

                float now = Time.unscaledTime;
                if (slidingDiagnosticsPlayer != player || slidingDiagnosticsText == null || now >= slidingDiagnosticsAt)
                {
                    slidingDiagnosticsPlayer = player;
                    if (lastSlidingExitPlayer != player)
                        lastSlidingExitPlayer = null;
                    slidingDiagnosticsText = ReadSlidingDiagnostics(player, now);
                    slidingDiagnosticsAt = now + SlidingDiagnosticsInterval;
                }

                string hover = __instance.m_hoverName.text;
                __instance.m_hoverName.text = string.IsNullOrEmpty(hover)
                    ? slidingDiagnosticsText : hover + "\n\n" + slidingDiagnosticsText;
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.UpdateWalking))]
        private static class Character_UpdateWalking_GlideCadence
        {
            [HarmonyTranspiler, HarmonyPriority(Priority.Last)]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = new List<CodeInstruction>(instructions);
                var forward = AccessTools.Field(typeof(Character), nameof(Character.s_forwardSpeed));
                var sideways = AccessTools.Field(typeof(Character), nameof(Character.s_sidewaySpeed));
                var setFloat = AccessTools.Method(typeof(ZSyncAnimation), nameof(ZSyncAnimation.SetFloat),
                    new[] { typeof(int), typeof(float) });
                var writes = new List<int>();
                int forwardCount = 0;
                int sidewaysCount = 0;
                for (int i = 0; i + 2 < code.Count; i++)
                {
                    bool isForward = code[i].LoadsField(forward);
                    bool isSideways = code[i].LoadsField(sideways);
                    if ((!isForward && !isSideways) || LocalIndex(code[i + 1]) < 0 || !code[i + 2].Calls(setFloat))
                        continue;
                    if (isForward) forwardCount++;
                    if (isSideways) sidewaysCount++;
                    writes.Add(i + 2);
                }
                cadenceHookReady = forwardCount == 1 && sidewaysCount == 1;
                if (!cadenceHookReady)
                {
                    LogWarning("Seasonal glide cadence cue is unavailable: locomotion animation writes were changed or ambiguous. Movement is unchanged.");
                    return code;
                }
                var resolve = AccessTools.Method(typeof(CharacterExtentions_FrozenOceanSliding), nameof(ResolveRunCadence));
                for (int i = writes.Count - 1; i >= 0; i--)
                    // Only the synchronized locomotion parameters change. No Animator.speed
                    // override and no boosted value fed back into the physics velocity.
                    InsertBefore(code, writes[i], new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Call, resolve));
                return code;
            }
        }

        [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateCamera))]
        private static class GameCamera_UpdateCamera_GlideFov
        {
            // Advance the visual transition once per native camera update.
            private static void Prefix(GameCamera __instance, float dt) => UpdateGlideFov(__instance, dt);

            [HarmonyTranspiler, HarmonyPriority(Priority.Last)]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = new List<CodeInstruction>(instructions);
                var main = AccessTools.Field(typeof(GameCamera), nameof(GameCamera.m_camera));
                var sky = AccessTools.Field(typeof(GameCamera), nameof(GameCamera.m_skyCamera));
                var fov = AccessTools.Field(typeof(GameCamera), nameof(GameCamera.m_fov));
                var setter = AccessTools.PropertySetter(typeof(Camera), nameof(Camera.fieldOfView));
                var writes = new List<int>();
                int mainCount = 0;
                int skyCount = 0;
                for (int i = 4; i < code.Count; i++)
                {
                    if (!code[i].Calls(setter) || code[i - 4].opcode != OpCodes.Ldarg_0 ||
                        code[i - 2].opcode != OpCodes.Ldarg_0 || !code[i - 1].LoadsField(fov))
                        continue;
                    bool isMain = code[i - 3].LoadsField(main);
                    bool isSky = code[i - 3].LoadsField(sky);
                    if (!isMain && !isSky)
                        continue;
                    if (isMain) mainCount++;
                    if (isSky) skyCount++;
                    writes.Add(i);
                }
                // Require both cameras and the non-accumulating m_fov source. Do not guess
                // when another camera transpiler has replaced this path or its source.
                fovHookReady = mainCount == 1 && skyCount == 1;
                if (!fovHookReady)
                {
                    LogWarning("Seasonal glide FOV cue is unavailable: camera FOV writes were changed or ambiguous. Camera behavior is unchanged.");
                    return code;
                }
                var resolve = AccessTools.Method(typeof(CharacterExtentions_FrozenOceanSliding), nameof(ResolveGlideFov));
                for (int i = writes.Count - 1; i >= 0; i--)
                    InsertBefore(code, writes[i], new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Call, resolve));
                return code;
            }
        }
    }
}
