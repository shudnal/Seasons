using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
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

        private static float ChargeFeedback(PlayerSlide state) => state.Mode == SlideMode.RunUp
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

            return state.Mode == SlideMode.RunUp
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
            float target = state.Mode == SlideMode.RunUp && state.Player.m_running && !HasBlockingAction(state.Player)
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
