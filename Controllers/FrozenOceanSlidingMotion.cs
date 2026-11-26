using System;
using System.Globalization;
using UnityEngine;
using static Seasons.Seasons;
using static Seasons.ZoneSystemVariantController;

namespace Seasons
{
    public static partial class CharacterExtentions_FrozenOceanSliding
    {
        private enum SlideMode { Basic, RunUp, Recharge, Glide }
        private enum SlideReason { Surface, Running, Released, Input, Action, Airborne, Slow, Disabled, Expired, Reset }

        private sealed class PlayerSlide
        {
            internal Player Player;
            internal bool OnIce;
            internal float TargetSpeed;
            internal SlideMode Mode;
            internal SlideReason Reason;
            internal float RunUpTime;
            internal float ChargeTime;
            internal float ReleaseTime;
            internal Vector3 RunHeading;
            internal float RunSpeed;
            internal float GlideTime;
            internal float GlideElapsed;
            internal float LaunchDuration;
            internal float LaunchStartFraction;
            internal float PeakFraction;
            internal float CadenceFeedback;
            internal bool RunKeyHeld;
            internal bool GlideRunReleased;
            internal bool MovementHeld;
            internal bool Interrupted;
            internal bool AnimationOwned;
            internal float LookOffset;
            internal int Transitions;
            internal float DiagnosticTurnDegrees;
            internal float DiagnosticTurnLoss;
            internal float LaunchWindAlong;
            internal float DiagnosticWindIntensity;
            internal float DiagnosticWindAlong;
            internal float DiagnosticWindSide;
            internal float DiagnosticWindDecay;
        }

        // Only the owning local player keeps persistent slide state. Creatures use stateless inertia.
        private static PlayerSlide playerSlide;

        internal static void ResetWorldState()
        {
            if (playerSlide != null)
            {
                RecordSlidingExit(playerSlide, SlideReason.Reset);
                ReleaseAnimation(playerSlide);
            }
            playerSlide = null;
        }

        private static void ReleaseAnimation(PlayerSlide state)
        {
            if (!state.AnimationOwned)
                return;
            state.AnimationOwned = false;
            Player player = state.Player;
            if (player && player.m_zanim && player.m_nview && player.m_nview.IsValid() && player.m_nview.IsOwner())
                player.m_zanim.SetBool(Character.s_slipping, false);
        }

        private static void ChangeMode(PlayerSlide state, SlideMode mode, SlideReason reason)
        {
            if (state.Mode != mode)
            {
                if (mode == SlideMode.Basic)
                    RecordSlidingExit(state, reason);
                state.Transitions++;
            }
            state.Mode = mode;
            state.Reason = reason;
            if (mode != SlideMode.Glide)
                ReleaseAnimation(state);
        }

        private static void InterruptSlide(Character character, SlideReason reason)
        {
            PlayerSlide state = playerSlide;
            if (state == null || !ReferenceEquals(state.Player, character))
                return;
            ClearPreparation(state);
            state.GlideTime = state.GlideElapsed = 0f;
            state.Interrupted = true;
            ChangeMode(state, SlideMode.Basic, reason);
            // Do not touch Rigidbody velocity or restore a previous launch vector.
        }

        private static void ClearPreparation(PlayerSlide state)
        {
            state.RunUpTime = 0f;
            state.ChargeTime = 0f;
            state.ReleaseTime = 0f;
            state.RunHeading = Vector3.zero;
        }

        private static float SteadyRunDuration => Parameter(frozenOceanGlidingRunUpTime.Value, 2f, 0f, 20f);
        private static float ChargeDuration => Parameter(frozenOceanGlidingChargeTime.Value, 2f, 0.1f, 20f);
        private static float GlideBonus => Parameter(frozenOceanGlidingSpeedBonus.Value, 0.2f, 0f, 1f);
        private static float LaunchDuration => Parameter(frozenOceanGlidingAccelerationDuration.Value, 2f, 0.1f, 20f);
        private static float BonusDuration => Parameter(frozenOceanGlidingBonusDuration.Value, 30f, 0.1f, 300f);
        private static float CoastDuration => Parameter(frozenOceanGlidingCoastDuration.Value, 10f, 0.1f, 120f);
        private static float EndSpeedFraction => Parameter(frozenOceanGlidingEndSpeed.Value, 0.9f, 0.1f, 1f);
        private static float RunSpeedFraction => Parameter(frozenOceanGlidingRunUpSpeed.Value, 0.85f, 0.1f, 1f);
        private static float RunAlignment => Mathf.Cos(Parameter(frozenOceanGlidingRunUpAngle.Value, 45f, 0f, 90f) * Mathf.Deg2Rad);
        private static bool IsCharged(PlayerSlide state) => state.RunUpTime >= SteadyRunDuration && state.ChargeTime >= ChargeDuration;

        private static bool IsPreparing(PlayerSlide state) => state.Mode == SlideMode.RunUp || state.Mode == SlideMode.Recharge;

        private static void BeginRecharge(PlayerSlide state)
        {
            ClearPreparation(state);
            state.RunUpTime = SteadyRunDuration;
            state.GlideTime = state.GlideElapsed = 0f;
            ChangeMode(state, SlideMode.Recharge, SlideReason.Running);
        }

        private static bool HasBlockingAction(Player player) => !player.CanMove() ||
            player.InAttack() || player.InDodge() || player.IsBlocking() || player.IsCrouching() ||
            player.InMinorAction() || player.InEmote() || player.IsEncumbered() || player.m_pushForce.sqrMagnitude > 0f;

        // Losing input focus blocks run-up and entry, not an already active passive glide.
        private static bool CanReadSlideInput(Player player) => player.TakeInput() &&
            !Hud.IsPieceSelectionVisible() && !Hud.InRadial();

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.sqrMagnitude);
        private static float Parameter(float value, float fallback, float minimum, float maximum) =>
            Mathf.Clamp(Finite(value) ? value : fallback, minimum, maximum);
        private static Vector3 Horizontal(Vector3 value) => new Vector3(value.x, 0f, value.z);

        private static bool CanOwnSlide(Player player) => SlidingEnabled && !frozenOceanUseVanillaSlippingPlayers.Value &&
            player && player == Player.m_localPlayer &&
            player.m_nview && player.m_nview.IsValid() && player.m_nview.IsOwner() && player.m_body &&
            !player.m_iceShoes && !player.m_skating && !player.IsDead() && !player.IsTeleporting() &&
            !player.IsAttached() && !player.IsSwimming() && !player.IsFlying() && !player.IsDebugFlying() && !player.InCutscene();

        // Creature movement has no persistent slide state or extra AI update.
        // Only the network owner supplies the short ice correction to native walking.
        private static bool CanUseCreatureSlide(Character character) =>
            SlidingEnabled && !frozenOceanUseVanillaSlippingCreatures.Value &&
            character && !(character is Player) && !character.m_iceShoes && !character.m_skating &&
            !character.IsDead() && character.m_body && character.m_nview &&
            character.m_nview.IsValid() && character.m_nview.IsOwner() && character.IsOnIce();

        private static void PreparePlayerSurface(Character character, bool onIce)
        {
            if (!(character is Player player) || player != Player.m_localPlayer)
                return;
            if (!CanOwnSlide(player) || !IsWaterSurfaceFrozen())
            {
                ResetWorldState();
                return;
            }
            if (!onIce)
            {
                // A jump keeps its actual horizontal momentum, not a cached launch velocity.
                // Landing on any other surface releases the entire seasonal state.
                if (playerSlide == null || playerSlide.Player != player || player.IsOnGround())
                {
                    ResetWorldState();
                    return;
                }
                playerSlide.OnIce = false;
                InterruptSlide(player, SlideReason.Airborne);
                return;
            }
            if (playerSlide == null || playerSlide.Player != player)
                playerSlide = new PlayerSlide
                {
                    Player = player,
                    MovementHeld = player.m_moveDir.sqrMagnitude > 0f, Reason = SlideReason.Surface
                };
            playerSlide.OnIce = true;
            playerSlide.TargetSpeed = 0f;
        }

        private static bool TrySlide(Character character, out PlayerSlide state)
        {
            state = playerSlide;
            return state != null && ReferenceEquals(state.Player, character) && CanOwnSlide(state.Player);
        }

        private static float ResolveWalkingSpeed(float speed, Character character, float dt)
        {
            if (!TrySlide(character, out PlayerSlide state) || !state.OnIce || !Finite(speed) || !Finite(dt) || dt <= 0f)
                return speed;
            Player player = state.Player;
            // Capture the already modified native speed, never call CheckRun or speed modifiers twice.
            // Legacy Movement speed multiplier values above one are intentionally capped.
            state.TargetSpeed = Mathf.Max(0f, speed) * Parameter(frozenOceanSlidingSpeedMultiplier.Value, 1f, 0.1f, 1f);
            Vector3 actual = Horizontal(player.m_body.linearVelocity);
            Vector3 input = Horizontal(player.m_moveDir);
            float deadZone = Parameter(frozenOceanSlidingInputDeadZone.Value, 0.1f, 0.001f, 0.5f);
            bool moving = state.MovementHeld || input.sqrMagnitude > deadZone * deadZone;
            bool blocked = state.Interrupted || HasBlockingAction(player) || !Finite(actual) || !Finite(input);
            state.Interrupted = false;
            bool longGlide = frozenOceanGlidingEnabled.Value;
            float actualSpeed = actual.magnitude;

            if (state.Mode == SlideMode.Glide)
            {
                if (!longGlide || blocked)
                    InterruptSlide(player, !longGlide ? SlideReason.Disabled : SlideReason.Action);
                else if (!moving)
                    return state.TargetSpeed = 0f;
                else if (player.m_running && CanReadSlideInput(player) && input.sqrMagnitude > deadZone * deadZone)
                    BeginRecharge(state);
                else
                {
                    InterruptSlide(player, SlideReason.Input);
                    return state.TargetSpeed;
                }
            }

            if (!longGlide || blocked || !CanReadSlideInput(player))
            {
                ClearPreparation(state);
                ChangeMode(state, SlideMode.Basic, !longGlide ? SlideReason.Disabled :
                    blocked ? SlideReason.Action : SlideReason.Input);
                return state.TargetSpeed;
            }

            // Entry is checked before m_running: releasing movement necessarily makes CheckRun false.
            // A short grace window also tolerates releasing Shift just before the movement keys.
            if (!moving)
            {
                if (IsPreparing(state) && IsCharged(state) &&
                    (state.Mode != SlideMode.Recharge || !state.RunKeyHeld) && state.RunSpeed > 0f &&
                    actualSpeed >= state.RunSpeed * RunSpeedFraction &&
                    Vector3.Dot(actual.normalized, state.RunHeading) >= RunAlignment)
                {
                    BeginGlide(state, actual);
                    return state.TargetSpeed = 0f;
                }
                ClearPreparation(state);
                ChangeMode(state, SlideMode.Basic, SlideReason.Released);
                return state.TargetSpeed;
            }

            bool aligned = input.sqrMagnitude > deadZone * deadZone && actualSpeed > 0f &&
                Vector3.Dot(actual.normalized, input.normalized) >= RunAlignment;
            if (!player.m_running)
            {
                if (IsPreparing(state) && IsCharged(state) &&
                    (state.Mode != SlideMode.Recharge || !state.RunKeyHeld) && aligned &&
                    actualSpeed >= state.RunSpeed * RunSpeedFraction &&
                    Vector3.Dot(state.RunHeading, input.normalized) >= RunAlignment)
                {
                    state.ReleaseTime += dt;
                    if (state.ReleaseTime <= Parameter(frozenOceanGlidingReleaseGrace.Value, 0.25f, 0f, 2f))
                        return state.TargetSpeed;
                }
                ClearPreparation(state);
                ChangeMode(state, SlideMode.Basic, SlideReason.Slow);
                return state.TargetSpeed;
            }

            state.RunSpeed = state.TargetSpeed;
            bool charging = aligned && state.RunSpeed > 0f && actualSpeed >= state.RunSpeed * RunSpeedFraction;
            if (state.Mode == SlideMode.Recharge)
            {
                // Powered steering may change the heading. Only aligned, sufficiently
                // fast native running earns the charge; never restart the steady-run stage.
                state.RunHeading = input.normalized;
                state.RunUpTime = SteadyRunDuration;
                state.ChargeTime = charging ? Mathf.Min(ChargeDuration, state.ChargeTime + dt) : 0f;
                state.ReleaseTime = 0f;
                return state.TargetSpeed;
            }
            if (!charging)
            {
                ClearPreparation(state);
                ChangeMode(state, SlideMode.Basic, SlideReason.Slow);
                return state.TargetSpeed;
            }

            if (state.RunHeading.sqrMagnitude == 0f || Vector3.Dot(state.RunHeading, input.normalized) < RunAlignment ||
                Vector3.Dot(state.RunHeading, actual.normalized) < RunAlignment)
            {
                ClearPreparation(state);
                state.RunHeading = input.normalized;
            }
            state.ReleaseTime = 0f;
            float warmup = SteadyRunDuration;
            float elapsed = state.RunUpTime + dt;
            state.RunUpTime = Mathf.Min(warmup, elapsed);
            if (elapsed > warmup)
                state.ChargeTime = Mathf.Min(ChargeDuration, state.ChargeTime + elapsed - warmup);
            ChangeMode(state, SlideMode.RunUp, SlideReason.Running);
            // Only feedback ramps up here. Driven movement never exceeds the native running target.
            return state.TargetSpeed;
        }

        private static void BeginGlide(PlayerSlide state, Vector3 actual)
        {
            Vector3 look = Horizontal(state.Player.GetLookDir());
            state.LookOffset = Finite(look) && look.sqrMagnitude > 0.000001f
                ? Vector3.SignedAngle(look, actual, Vector3.up) : 0f;
            ClearPreparation(state);
            state.GlideTime = state.GlideElapsed = 0f;
            // A Run key still held from preparation is not a new recharge gesture.
            state.GlideRunReleased = !state.RunKeyHeld;
            // Capture the earned acceleration once. Live tuning cannot restart it.
            state.LaunchDuration = LaunchDuration;
            Vector3 wind = Horizontal(EnvMan.instance.GetWindForce());
            state.LaunchWindAlong = Mathf.Clamp(Vector3.Dot(actual.normalized, wind), -1f, 1f);
            float windBonus = Parameter(frozenOceanGlidingWindSpeed.Value, 0.05f, 0f, 0.2f) * state.LaunchWindAlong;
            // Freeze the earned peak for this launch; a wind change must not replay acceleration.
            state.PeakFraction = 1f + Mathf.Max(0f, GlideBonus + windBonus);
            state.LaunchStartFraction = Mathf.Min(actual.magnitude / state.RunSpeed, state.PeakFraction);
            state.DiagnosticTurnDegrees = state.DiagnosticTurnLoss = 0f;
            state.DiagnosticWindIntensity = wind.magnitude;
            state.DiagnosticWindAlong = state.LaunchWindAlong;
            state.DiagnosticWindSide = 0f;
            state.DiagnosticWindDecay = 1f;
            ChangeMode(state, SlideMode.Glide, SlideReason.Released);
        }

        private static float GlideSpeedProfile(PlayerSlide state, float seconds)
        {
            if (seconds < state.LaunchDuration)
                return Mathf.SmoothStep(state.LaunchStartFraction, state.PeakFraction, seconds / state.LaunchDuration);

            seconds -= state.LaunchDuration;
            if (seconds < BonusDuration)
                return Mathf.Lerp(state.PeakFraction, 1f, seconds / BonusDuration);
            return Mathf.Lerp(1f, EndSpeedFraction, Mathf.Clamp01((seconds - BonusDuration) / CoastDuration));
        }

        private static bool ApplyWalkingInertiaAndRootMotion(Character character, ref Vector3 velocity, float dt)
        {
            bool seasonalCorrection = false;
            // Leave the native airborne target and air control intact after jumping off ice.
            // A later root-motion or movement patch must receive that same unmodified target.
            if (TrySlide(character, out PlayerSlide state) && state.OnIce && character.IsOnIce() && Finite(dt) && dt > 0f)
            {
                Vector3 actual = Horizontal(state.Player.m_body.linearVelocity);
                if (!Finite(actual))
                    ResetWorldState();
                else
                {
                    // CanMove may bypass the native speed branch. Immobilizing actions
                    // must still cancel the glide, without restoring a launch velocity.
                    if ((state.Mode == SlideMode.Glide || state.Mode == SlideMode.Recharge) &&
                        (HasBlockingAction(state.Player) || !frozenOceanGlidingEnabled.Value))
                        InterruptSlide(state.Player, SlideReason.Action);
                    Vector3 result = state.Mode == SlideMode.Glide
                        ? GlideVelocity(state, actual, dt)
                        : state.Mode == SlideMode.Recharge && state.Player.m_running
                            ? RechargeVelocity(state, actual, dt) : BasicVelocity(state, actual, dt);
                    seasonalCorrection = state.OnIce && Finite(result) &&
                        (result.x != actual.x || result.z != actual.z);
                    velocity.x = result.x;
                    velocity.z = result.z;
                    UpdateRunCadence(state, dt);
                }
            }
            else if (CanUseCreatureSlide(character) && Finite(dt) && dt > 0f)
            {
                Vector3 actual = Horizontal(character.m_body.linearVelocity);
                Vector3 desired = Horizontal(velocity);
                if (Finite(actual) && Finite(desired))
                {
                    Vector3 result = CreatureVelocity(character, actual, desired, dt);
                    if (Finite(result))
                    {
                        velocity.x = result.x;
                        velocity.z = result.z;
                    }
                }
            }
            // Route the real patched method. The local physics velocity is separate
            // from m_currentVel, which still drives ordinary locomotion animation.
            RecordPhysicsTarget(character, velocity, seasonalCorrection);
            character.ApplyRootMotion(ref velocity);
            return seasonalCorrection;
        }

        private static float ResolveMovementThreshold(float threshold, bool seasonalCorrection, Character character)
        {
            // Respect thresholds changed by other mods, including an already removed guard.
            // Eligibility alone is not enough: our grounded solver must have contributed
            // a finite horizontal correction during this particular UpdateWalking call.
            if (threshold != 0.01f || !seasonalCorrection || !TrySlide(character, out PlayerSlide state) ||
                !state.OnIce || !character.IsOnIce() || character.m_slipping)
                return threshold;
            // Keep the original strict comparison: zero and NaN still cannot apply a force.
            return 0f;
        }

        private static Vector3 BasicVelocity(PlayerSlide state, Vector3 actual, float dt)
        {
            float factor = Parameter(frozenOceanSlipperiness.Value, 1.25f, 0.01f, 5f);
            float braking = Parameter(frozenOceanSlidingBraking.Value, 4f, 0.1f, 50f) / factor;
            float deadZone = Parameter(frozenOceanSlidingInputDeadZone.Value, 0.1f, 0.001f, 0.5f);
            Player player = state.Player;
            Vector3 input = player.CanMove() ? Horizontal(player.m_moveDir) : Vector3.zero;
            if (!Finite(input) || !Finite(state.TargetSpeed))
                return actual;
            float inputLength = input.magnitude;
            float speed = actual.magnitude;
            if (inputLength <= deadZone || state.TargetSpeed <= 0f)
            {
                float deceleration = speed >= Parameter(frozenOceanSlidingMinimumSpeed.Value, 1f, 0.01f, 10f)
                    ? Parameter(frozenOceanSlidingCoasting.Value, 1.5f, 0.01f, 20f) / factor : braking;
                return Vector3.MoveTowards(actual, Vector3.zero, deceleration * dt);
            }

            Vector3 direction = input / inputLength;
            float targetSpeed = state.TargetSpeed * (player.m_running ? 1f : Mathf.Min(inputLength, 1f));
            float forward = Vector3.Dot(actual, direction);
            Vector3 lateral = actual - direction * forward;
            float acceleration = forward < 0f || forward > targetSpeed ? braking :
                Parameter(frozenOceanSlidingAcceleration.Value, 12f, 0.1f, 50f);
            forward = Mathf.MoveTowards(forward, targetSpeed, acceleration * dt);
            lateral = Vector3.MoveTowards(lateral, Vector3.zero,
                Parameter(frozenOceanSlidingSteering.Value, 5f, 0.1f, 50f) / factor * dt);
            // Independent forward/lateral control must not generate unbounded strafe acceleration.
            // External impulses may exceed the running cap, but cannot grow through steering.
            return Vector3.ClampMagnitude(direction * forward + lateral, Mathf.Max(speed, targetSpeed));
        }

        private static Vector3 RechargeVelocity(PlayerSlide state, Vector3 actual, float dt)
        {
            Vector3 input = Horizontal(state.Player.m_moveDir);
            float speed = actual.magnitude;
            // Preserve glide momentum while running, but do not create a second
            // speed bonus before the next earned launch. Normal running can recover
            // speed below its native target; excess external momentum settles to the peak.
            float factor = Parameter(frozenOceanSlipperiness.Value, 1.25f, 0.01f, 5f);
            // Keep momentum earned with the previous glide's tailwind, without
            // reading or applying wind during powered movement.
            float peak = Mathf.Max(1f + GlideBonus, state.PeakFraction);
            float target = Mathf.Clamp(speed, state.TargetSpeed, state.TargetSpeed * peak);
            float acceleration = speed > target
                ? Parameter(frozenOceanSlidingBraking.Value, 4f, 0.1f, 50f) / factor
                : Parameter(frozenOceanSlidingAcceleration.Value, 12f, 0.1f, 50f);
            float nextSpeed = Mathf.MoveTowards(speed, target, acceleration * dt);
            if (speed <= 0.000001f)
                return input.normalized * nextSpeed;

            // Rotate the existing momentum rather than damping its lateral component.
            float turn = Parameter(frozenOceanSlidingSteering.Value, 5f, 0.1f, 50f) / factor;
            Vector3 direction = Vector3.RotateTowards(actual / speed, input.normalized,
                turn * dt / Mathf.Max(speed, 1f), 0f);
            return direction * nextSpeed;
        }

        private static Vector3 CreatureVelocity(Character character, Vector3 actual, Vector3 desired, float dt)
        {
            // Do not prolong attacks, immobilization or external pushback.
            if (!character.CanMove() || character.InAttack() || character.InDodge() ||
                character.IsAttached() || character.m_pushForce.sqrMagnitude > 0f)
                return desired;

            float factor = Parameter(frozenOceanSlipperiness.Value, 1.25f, 0.01f, 5f);
            float braking = Parameter(frozenOceanSlidingBraking.Value, 4f, 0.1f, 50f) / factor;
            float deadZone = Parameter(frozenOceanSlidingInputDeadZone.Value, 0.1f, 0.001f, 0.5f);
            Vector3 input = Horizontal(character.m_moveDir);
            if (!Finite(input))
                return desired;

            // Creatures coast at the basic braking rate, not the longer player coasting
            // rate. This bounds their travel after AI stops requesting movement.
            if (input.sqrMagnitude <= deadZone * deadZone)
                return Vector3.MoveTowards(actual, Vector3.zero, braking * dt);

            float targetSpeed = desired.magnitude;
            if (targetSpeed <= 0.000001f)
                return desired;
            // The AI supplies heading directly; its native velocity supplies the speed cap.
            Vector3 direction = input.normalized;
            float forward = Vector3.Dot(actual, direction);
            Vector3 lateral = actual - direction * forward;
            float acceleration = forward < 0f || forward > targetSpeed ? braking :
                Parameter(frozenOceanSlidingAcceleration.Value, 12f, 0.1f, 50f);
            forward = Mathf.MoveTowards(forward, targetSpeed, acceleration * dt);
            lateral = Vector3.MoveTowards(lateral, Vector3.zero,
                Parameter(frozenOceanSlidingSteering.Value, 5f, 0.1f, 50f) / factor * dt);
            // No new impulse: target native movement and actual velocity set the speed cap.
            return Vector3.ClampMagnitude(direction * forward + lateral, Mathf.Max(actual.magnitude, targetSpeed));
        }

        private static Vector3 GlideVelocity(PlayerSlide state, Vector3 actual, float dt)
        {
            float speed = actual.magnitude;
            float duration = state.LaunchDuration + BonusDuration + CoastDuration;
            float launchMinimum = state.RunSpeed * Mathf.Min(RunSpeedFraction, EndSpeedFraction);
            float endMinimum = state.RunSpeed * EndSpeedFraction;
            if (state.RunSpeed <= 0f || speed <= 0.000001f || state.GlideTime >= duration ||
                (state.GlideTime < state.LaunchDuration ? speed < launchMinimum : speed <= endMinimum))
            {
                InterruptSlide(state.Player, state.GlideTime >= duration ? SlideReason.Expired : SlideReason.Slow);
                return BasicVelocity(state, actual, dt);
            }
            Vector3 direction = actual / speed;
            Vector3 wind = Horizontal(EnvMan.instance.GetWindForce());
            Vector3 look = Horizontal(state.Player.GetLookDir());
            float turn = 0f;
            float windSide = 0f;
            if (Finite(look) && look.sqrMagnitude > 0.000001f)
            {
                Vector3 desired = Quaternion.AngleAxis(state.LookOffset, Vector3.up) * look.normalized;
                Vector3 right = Vector3.Cross(Vector3.up, desired);
                windSide = Mathf.Clamp(Vector3.Dot(wind, right), -1f, 1f);
                float drift = Parameter(frozenOceanGlidingWindLateral.Value, 0.1f, 0f, 0.5f) * windSide;
                if (drift != 0f)
                    desired = (desired + right * drift).normalized;
                Vector3 steered = Vector3.RotateTowards(direction, desired,
                    Parameter(frozenOceanGlidingTurnSpeed.Value, 25f, 0f, 180f) * Mathf.Deg2Rad * dt, 0f);
                turn = Vector3.Angle(direction, steered) * Mathf.Deg2Rad;
                direction = steered;
            }

            float windAlong = Mathf.Clamp(Vector3.Dot(wind, direction), -1f, 1f);
            float windDecay = 1f - Parameter(frozenOceanGlidingWindDeceleration.Value, 0.25f, 0f, 0.75f) * windAlong;
            // Acceleration keeps its real duration. Only the two slowing phases
            // advance at the wind-adjusted rate, which is always strictly positive.
            float launchStep = Mathf.Min(dt, Mathf.Max(0f, state.LaunchDuration - state.GlideTime));
            float nextTime = Mathf.Min(duration, state.GlideTime + launchStep + (dt - launchStep) * windDecay);
            // Apply only the scheduled change to actual speed, not a target velocity.
            // The finite launch gain never restores losses or multiplies existing momentum.
            float speedDelta = state.RunSpeed * (GlideSpeedProfile(state, nextTime) - GlideSpeedProfile(state, state.GlideTime));
            if (speedDelta > 0f)
                speedDelta = Mathf.Min(speedDelta, Mathf.Max(0f, state.RunSpeed * state.PeakFraction - speed));
            float turnLoss = Parameter(frozenOceanGlidingTurnLoss.Value, 0.2f, 0f, 10f) * turn;
            if (showSlidingDiagnosticsOnHover.Value)
            {
                state.DiagnosticTurnDegrees += turn * Mathf.Rad2Deg;
                state.DiagnosticTurnLoss += turnLoss;
                state.DiagnosticWindIntensity = wind.magnitude;
                state.DiagnosticWindAlong = windAlong;
                state.DiagnosticWindSide = windSide;
                state.DiagnosticWindDecay = windDecay;
            }
            state.GlideElapsed += dt;
            state.GlideTime = nextTime;
            speed = Mathf.Max(0f, speed + speedDelta - turnLoss);
            if (nextTime >= duration || (nextTime < state.LaunchDuration ? speed < launchMinimum : speed <= endMinimum))
                InterruptSlide(state.Player, nextTime >= duration ? SlideReason.Expired : SlideReason.Slow);
            return direction * speed;
        }

        private static bool ResolveSlippingAnimation(bool vanillaAnimation, Character character)
        {
            if (!TrySlide(character, out PlayerSlide state))
                return vanillaAnimation;
            bool animated = state.OnIce && state.Mode == SlideMode.Glide;
            state.AnimationOwned = animated;
            if (animated)
            {
                // Native UpdateRotation has already run. Face the actual glide trajectory
                // without changing look yaw (the steering input) or enabling m_slipping.
                Vector3 velocity = Horizontal(state.Player.m_body.linearVelocity);
                if (Finite(velocity) && velocity.sqrMagnitude > 0.000001f)
                    state.Player.transform.rotation = Quaternion.LookRotation(velocity, Vector3.up);
            }
            return animated;
        }

        public static string GetSlidingStatus()
        {
            if (SlidingEnabled && frozenOceanUseVanillaSlippingPlayers.Value && Player.m_localPlayer &&
                !Player.m_localPlayer.m_iceShoes && Player.m_localPlayer.IsOnIce())
                return "state=vanilla";
            PlayerSlide state = playerSlide;
            if (state == null || !state.Player || !state.Player.m_body)
                return "state=inactive";
            float speed = Horizontal(state.Player.m_body.linearVelocity).magnitude;
            return string.Format(CultureInfo.InvariantCulture,
                "state={0} grounded={1} speed={2:F2} runSpeed={3:F2} ratio={4:F1}% steady={5:F2}/{6:F2} charge={7:F2}/{8:F2} ready={9} glide={10:F2}/{11:F2} animation={12} transitions={13} reason={14} fovOffset={15:F2} cadenceHook={16} fovHook={17} glideElapsed={18:F2}",
                state.Mode, state.OnIce, speed, state.RunSpeed, state.RunSpeed > 0f ? 100f * speed / state.RunSpeed : 0f,
                state.RunUpTime, SteadyRunDuration, state.ChargeTime, ChargeDuration, IsCharged(state),
                state.GlideTime, (state.Mode == SlideMode.Glide ? state.LaunchDuration : LaunchDuration) + BonusDuration + CoastDuration, state.AnimationOwned, state.Transitions, state.Reason,
                GetGlideFovOffset(), cadenceHookReady, fovHookReady, state.GlideElapsed);
        }
    }
}
