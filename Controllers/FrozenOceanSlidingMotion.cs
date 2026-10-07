using System;
using System.Globalization;
using UnityEngine;
using static Seasons.Seasons;
using static Seasons.ZoneSystemVariantController;

namespace Seasons
{
    public static partial class CharacterExtentions_FrozenOceanSliding
    {
        private sealed class PlayerSlide
        {
            internal Player Player;
            internal bool OnIce;
            internal float TargetSpeed;
        }

        // Only the owning local player has custom momentum. Creatures never enter this state.
        private static PlayerSlide playerSlide;

        internal static void ResetWorldState() => playerSlide = null;

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
                return;
            }
            if (playerSlide == null || playerSlide.Player != player)
                playerSlide = new PlayerSlide { Player = player };
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
            if (!TrySlide(character, out PlayerSlide state) || !state.OnIce || !Finite(speed))
                return speed;
            state.TargetSpeed = Mathf.Max(0f, speed) * Parameter(frozenOceanSlidingSpeedMultiplier.Value, 1.1f, 0.1f, 3f);
            return state.TargetSpeed;
        }

        private static Vector3 BlendWalkingVelocity(Vector3 previous, Vector3 target, float blend, Character character, float dt)
        {
            if (!TrySlide(character, out PlayerSlide state) || !Finite(dt) || dt <= 0f)
                return Vector3.Lerp(previous, target, blend);
            Vector3 actual = Horizontal(state.Player.m_body.linearVelocity);
            if (!Finite(actual))
            {
                ResetWorldState();
                return Vector3.Lerp(previous, target, blend);
            }
            Vector3 result = state.OnIce ? BasicVelocity(state, actual, dt) : actual;
            // UpdateWalking preserves Rigidbody Y separately, before its native ground handling.
            result.y = target.y;
            return result;
        }

        private static Vector3 BasicVelocity(PlayerSlide state, Vector3 actual, float dt)
        {
            float factor = Parameter(frozenOceanSlipperiness.Value, 1.25f, 0.01f, 5f);
            float braking = Parameter(frozenOceanSlidingBraking.Value, 4f, 0.1f, 50f) / factor;
            float deadZone = Parameter(frozenOceanSlidingInputDeadZone.Value, 0.1f, 0.001f, 0.5f);
            Player player = state.Player;
            Vector3 input = player.CanMove() ? Horizontal(player.m_moveDir) : Vector3.zero;
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

        public static string GetSlidingStatus()
        {
            PlayerSlide state = playerSlide;
            if (state == null || !state.Player || !state.Player.m_body)
                return "state=inactive";
            return string.Format(CultureInfo.InvariantCulture, "state=basic grounded={0} speed={1:F2}",
                state.OnIce, Horizontal(state.Player.m_body.linearVelocity).magnitude);
        }
    }
}
