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
        private enum SlideReason { Surface, Running, Released, Input, Action, Airborne, Slow, Disabled }

        private sealed class PlayerSlide
        {
            internal Player Player;
            internal bool OnIce;
            internal float TargetSpeed;
            internal SlideMode Mode;
            internal SlideReason Reason;
            internal float RunUpTime;
            internal bool RunInput;
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
            state.RunUpTime = 0f;
            state.Interrupted = true;
            ChangeMode(state, SlideMode.Basic, reason);
            // Do not touch Rigidbody velocity or restore a previous launch vector.
        }

        private static bool HasBlockingAction(Player player) => !player.CanMove() || !player.TakeInput() ||
            player.InAttack() || player.InDodge() || player.IsBlocking() || player.IsCrouching() ||
            player.InMinorAction() || player.InEmote() || player.IsEncumbered() || player.m_pushForce.sqrMagnitude > 0f ||
            InventoryGui.IsVisible() || Minimap.IsOpen() || StoreGui.IsVisible() || Hud.IsPieceSelectionVisible() || Hud.InRadial();

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
                playerSlide.RunUpTime = 0f;
                ChangeMode(playerSlide, SlideMode.Basic, SlideReason.Airborne);
                return;
            }
            if (playerSlide == null || playerSlide.Player != player)
                playerSlide = new PlayerSlide
                {
                    Player = player, RunInput = player.m_run,
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
            float baseSpeed = Mathf.Max(0f, speed) * Parameter(frozenOceanSlidingSpeedMultiplier.Value, 1.1f, 0.1f, 3f);
            state.TargetSpeed = baseSpeed;
            Vector3 actual = Horizontal(player.m_body.linearVelocity);
            Vector3 input = Horizontal(player.m_moveDir);
            float deadZone = Parameter(frozenOceanSlidingInputDeadZone.Value, 0.1f, 0.001f, 0.5f);
            bool moving = state.MovementHeld || input.sqrMagnitude > deadZone * deadZone;
            bool blocked = state.Interrupted || HasBlockingAction(player) || !Finite(actual) || !Finite(input);
            state.Interrupted = false;
            bool longGlide = frozenOceanGlidingEnabled.Value;
            float minimumSpeed = Parameter(frozenOceanGlidingMinimumSpeed.Value, 3f, 0.1f, 20f);
            float actualSpeed = actual.magnitude;

            if (state.Mode == SlideMode.Glide)
            {
                if (!longGlide || blocked || moving || actualSpeed < minimumSpeed)
                {
                    state.RunUpTime = 0f;
                    ChangeMode(state, SlideMode.Basic, !longGlide ? SlideReason.Disabled :
                        blocked ? SlideReason.Action : moving ? SlideReason.Input : SlideReason.Slow);
                }
                else
                    return state.TargetSpeed = 0f;
            }

            float requiredTime = Parameter(frozenOceanGlidingRunUpTime.Value, 2f, 0.1f, 20f);
            // Releasing movement is the only entry gesture. Releasing only Run while
            // continuing to hold movement keeps basic inertia and does not animate a glide.
            if (longGlide && !blocked && !moving && state.Mode == SlideMode.RunUp &&
                state.RunUpTime >= requiredTime && actualSpeed >= minimumSpeed)
            {
                Vector3 look = Horizontal(player.GetLookDir());
                state.LookOffset = Finite(look) && look.sqrMagnitude > 0.000001f
                    ? Vector3.SignedAngle(look, actual, Vector3.up) : 0f;
                state.RunUpTime = 0f;
                ChangeMode(state, SlideMode.Glide, SlideReason.Released);
                return state.TargetSpeed = 0f;
            }

            float alignment = Mathf.Cos(Parameter(frozenOceanGlidingRunUpAngle.Value, 25f, 0f, 90f) * Mathf.Deg2Rad);
            bool charging = longGlide && !blocked && moving && player.m_running && baseSpeed > 0f &&
                actualSpeed >= Mathf.Max(minimumSpeed, baseSpeed * Parameter(frozenOceanGlidingRunUpSpeed.Value, 0.85f, 0.1f, 1f)) &&
                Vector3.Dot(actual.normalized, input.normalized) >= alignment;
            if (charging)
            {
                float boostTime = Parameter(frozenOceanGlidingBoostTime.Value, 3f, 0.1f, 30f);
                state.RunUpTime = Mathf.Min(Mathf.Max(requiredTime, boostTime), state.RunUpTime + dt);
                ChangeMode(state, SlideMode.RunUp, SlideReason.Running);
                float bonus = Parameter(frozenOceanGlidingSpeedBonus.Value, 0.25f, 0f, 2f) * Mathf.Clamp01(state.RunUpTime / boostTime);
                state.TargetSpeed = baseSpeed * (1f + bonus);
            }
            else
            {
                state.RunUpTime = 0f;
                if (state.Mode != SlideMode.Basic)
                    ChangeMode(state, SlideMode.Basic, blocked ? SlideReason.Action : SlideReason.Slow);
            }
            return state.TargetSpeed;
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
            float minimum = Parameter(frozenOceanGlidingMinimumSpeed.Value, 3f, 0.1f, 20f);
            if (speed < minimum)
            {
                ChangeMode(state, SlideMode.Basic, SlideReason.Slow);
                return BasicVelocity(state, actual, dt);
            }
            Vector3 direction = actual / speed;
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
            float loss = Parameter(frozenOceanGlidingDeceleration.Value, 0.25f, 0.01f, 10f) * dt +
                Parameter(frozenOceanGlidingTurnLoss.Value, 0.4f, 0f, 10f) * turn;
            speed = Mathf.Max(0f, speed - loss);
            if (speed < minimum)
                ChangeMode(state, SlideMode.Basic, SlideReason.Slow);
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
            return string.Format(CultureInfo.InvariantCulture,
                "state={0} grounded={1} speed={2:F2} runUp={3:F2}/{4:F2} animation={5} transitions={6} reason={7}",
                state.Mode, state.OnIce, Horizontal(state.Player.m_body.linearVelocity).magnitude, state.RunUpTime,
                Parameter(frozenOceanGlidingRunUpTime.Value, 2f, 0.1f, 20f), state.AnimationOwned, state.Transitions, state.Reason);
        }
    }
}
