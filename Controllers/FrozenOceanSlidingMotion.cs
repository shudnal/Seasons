using System;
using System.Globalization;
using UnityEngine;
using static Seasons.Seasons;
using static Seasons.ZoneSystemVariantController;

namespace Seasons
{
    public static partial class CharacterExtentions_FrozenOceanSliding
    {
        private enum SlideMode { Basic, RunUp, Glide }
        private enum SlideReason { Surface, Running, Released, Input, Action, Airborne, Slow, Disabled, Expired }

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
            internal bool LaunchPending;
            internal bool QuickRechargePending;
            internal Vector3 RechargeHeading;
            internal bool MovementHeld;
            internal bool Interrupted;
            internal bool AnimationOwned;
            internal float LookOffset;
            internal int Transitions;
        }

        // Only the owning local player has custom momentum. Creatures never enter this state.
        private static PlayerSlide playerSlide;

        internal static void ResetWorldState()
        {
            if (playerSlide != null)
                ReleaseAnimation(playerSlide);
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
                state.Transitions++;
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
            state.QuickRechargePending = false;
            state.LaunchPending = false;
            state.GlideTime = 0f;
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

        private static float SteadyRunDuration => Parameter(frozenOceanGlidingRunUpTime.Value, 3f, 0f, 20f);
        private static float ChargeDuration => Parameter(frozenOceanGlidingChargeTime.Value, 2f, 0.1f, 20f);
        private static float GlideBonus => Parameter(frozenOceanGlidingSpeedBonus.Value, 0.1f, 0f, 1f);
        private static float BonusDuration => Parameter(frozenOceanGlidingBonusDuration.Value, 30f, 0.1f, 300f);
        private static float CoastDuration => Parameter(frozenOceanGlidingCoastDuration.Value, 10f, 0.1f, 120f);
        private static float EndSpeedFraction => Parameter(frozenOceanGlidingEndSpeed.Value, 0.9f, 0.1f, 1f);
        private static float RunSpeedFraction => Parameter(frozenOceanGlidingRunUpSpeed.Value, 0.85f, 0.1f, 1f);
        private static float RunAlignment => Mathf.Cos(Parameter(frozenOceanGlidingRunUpAngle.Value, 25f, 0f, 90f) * Mathf.Deg2Rad);
        private static bool IsCharged(PlayerSlide state) => state.RunUpTime >= SteadyRunDuration && state.ChargeTime >= ChargeDuration;

        private static void InterruptForMovement(PlayerSlide state)
        {
            Vector3 actual = Horizontal(state.Player.m_body.linearVelocity);
            bool canRecharge = state.OnIce && state.Player.IsOnIce() && frozenOceanGlidingEnabled.Value &&
                state.Mode == SlideMode.Glide && !state.LaunchPending && state.GlideTime < BonusDuration &&
                Finite(actual) && state.RunSpeed > 0f && actual.magnitude > state.RunSpeed && !HasBlockingAction(state.Player);
            InterruptSlide(state.Player, SlideReason.Input);
            // Consume this permission at the next native speed calculation. Movement alone
            // cancels the animation, but only an actual CheckRun success can renew the charge.
            state.Interrupted = false;
            state.QuickRechargePending = canRecharge;
            state.RechargeHeading = canRecharge ? actual.normalized : Vector3.zero;
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

        private static bool CanOwnSlide(Player player) => SlidingEnabled && player && player == Player.m_localPlayer &&
            player.m_nview && player.m_nview.IsValid() && player.m_nview.IsOwner() && player.m_body &&
            !player.m_iceShoes && !player.m_skating && !player.IsDead() && !player.IsTeleporting() &&
            !player.IsAttached() && !player.IsSwimming() && !player.IsFlying() && !player.IsDebugFlying() && !player.InCutscene();

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
                else if (moving)
                    InterruptForMovement(state);
                else
                    return state.TargetSpeed = 0f;
            }

            if (!longGlide || blocked || !CanReadSlideInput(player))
            {
                ClearPreparation(state);
                state.QuickRechargePending = false;
                ChangeMode(state, SlideMode.Basic, !longGlide ? SlideReason.Disabled :
                    blocked ? SlideReason.Action : SlideReason.Input);
                return state.TargetSpeed;
            }

            // Entry is checked before m_running: releasing movement necessarily makes CheckRun false.
            // A short grace window also tolerates releasing Shift just before the movement keys.
            if (!moving)
            {
                state.QuickRechargePending = false;
                if (state.Mode == SlideMode.RunUp && IsCharged(state) && state.RunSpeed > 0f &&
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
                state.QuickRechargePending = false;
                if (state.Mode == SlideMode.RunUp && IsCharged(state) && aligned &&
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
            if (!charging)
            {
                ClearPreparation(state);
                state.QuickRechargePending = false;
                ChangeMode(state, SlideMode.Basic, SlideReason.Slow);
                return state.TargetSpeed;
            }

            if (state.QuickRechargePending)
            {
                bool sameDirection = Vector3.Dot(state.RechargeHeading, input.normalized) >= RunAlignment;
                state.QuickRechargePending = false;
                if (sameDirection)
                {
                    state.RunHeading = input.normalized;
                    state.RunUpTime = SteadyRunDuration;
                }
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
            state.GlideTime = 0f;
            state.LaunchPending = true;
            ChangeMode(state, SlideMode.Glide, SlideReason.Released);
        }

        private static float GlideSpeedProfile(float seconds)
        {
            if (seconds < BonusDuration)
                return 1f + GlideBonus * (1f - Mathf.Clamp01(seconds / BonusDuration));
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
                    if (state.Mode == SlideMode.Glide && (HasBlockingAction(state.Player) || !frozenOceanGlidingEnabled.Value))
                        InterruptSlide(state.Player, SlideReason.Action);
                    Vector3 result = state.Mode == SlideMode.Glide
                        ? GlideVelocity(state, actual, dt) : BasicVelocity(state, actual, dt);
                    seasonalCorrection = state.OnIce && Finite(result) &&
                        (result.x != actual.x || result.z != actual.z);
                    velocity.x = result.x;
                    velocity.z = result.z;
                }
            }
            // Route the real patched method. The local physics velocity is separate
            // from m_currentVel, which still drives ordinary locomotion animation.
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

        private static Vector3 GlideVelocity(PlayerSlide state, Vector3 actual, float dt)
        {
            float speed = actual.magnitude;
            float duration = BonusDuration + CoastDuration;
            bool launching = state.LaunchPending;
            state.LaunchPending = false;
            if (state.RunSpeed <= 0f || speed <= 0.000001f || state.GlideTime >= duration ||
                (launching ? speed < state.RunSpeed * RunSpeedFraction : speed <= state.RunSpeed * EndSpeedFraction))
            {
                InterruptSlide(state.Player, state.GlideTime >= duration ? SlideReason.Expired : SlideReason.Slow);
                return BasicVelocity(state, actual, dt);
            }
            Vector3 direction = actual / speed;
            if (launching)
                // One earned launch through the existing movement force path, not repeated
                // multiplication. External momentum above this target is never amplified.
                speed = Mathf.Max(speed, state.RunSpeed * (1f + GlideBonus));
            Vector3 look = Horizontal(state.Player.GetLookDir());
            float turn = 0f;
            if (Finite(look) && look.sqrMagnitude > 0.000001f)
            {
                Vector3 desired = Quaternion.AngleAxis(state.LookOffset, Vector3.up) * look.normalized;
                Vector3 steered = Vector3.RotateTowards(direction, desired,
                    Parameter(frozenOceanGlidingTurnSpeed.Value, 25f, 0f, 180f) * Mathf.Deg2Rad * dt, 0f);
                turn = Vector3.Angle(direction, steered) * Mathf.Deg2Rad;
                direction = steered;
            }
            float nextTime = Mathf.Min(duration, state.GlideTime + dt);
            // Subtract the profile's loss from actual velocity. Never restore speed lost to
            // turns, collisions or another movement mod by chasing the nominal profile.
            float loss = state.RunSpeed * Mathf.Max(0f, GlideSpeedProfile(state.GlideTime) - GlideSpeedProfile(nextTime)) +
                Parameter(frozenOceanGlidingTurnLoss.Value, 0.4f, 0f, 10f) * turn;
            state.GlideTime = nextTime;
            speed = Mathf.Max(0f, speed - loss);
            if (nextTime >= duration || speed <= state.RunSpeed * EndSpeedFraction)
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
            PlayerSlide state = playerSlide;
            if (state == null || !state.Player || !state.Player.m_body)
                return "state=inactive";
            float speed = Horizontal(state.Player.m_body.linearVelocity).magnitude;
            return string.Format(CultureInfo.InvariantCulture,
                "state={0} grounded={1} speed={2:F2} runSpeed={3:F2} ratio={4:F1}% steady={5:F2}/{6:F2} charge={7:F2}/{8:F2} ready={9} glide={10:F2}/{11:F2} animation={12} transitions={13} reason={14}",
                state.Mode, state.OnIce, speed, state.RunSpeed, state.RunSpeed > 0f ? 100f * speed / state.RunSpeed : 0f,
                state.RunUpTime, SteadyRunDuration, state.ChargeTime, ChargeDuration, IsCharged(state),
                state.GlideTime, BonusDuration + CoastDuration, state.AnimationOwned, state.Transitions, state.Reason);
        }
    }
}
