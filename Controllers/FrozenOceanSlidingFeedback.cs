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

        private static float ChargeFeedback(PlayerSlide state) => state.Mode == SlideMode.RunUp
            ? Mathf.Clamp01(state.ChargeTime / ChargeDuration) : 0f;

        private static float GetGlideFovOffset()
        {
            if (!frozenOceanGlidingFovEnabled.Value || !frozenOceanGlidingEnabled.Value ||
                !TrySlide(Player.m_localPlayer, out PlayerSlide state) || !state.OnIce || !state.Player.IsOnIce() ||
                HasBlockingAction(state.Player))
                return 0f;
            float bonus = 0f;
            Vector3 actual = Horizontal(state.Player.m_body.linearVelocity);
            if (state.RunSpeed > 0f && Finite(actual))
            {
                float actualBonus = Mathf.Clamp(actual.magnitude / state.RunSpeed - 1f, 0f, GlideBonus);
                if (state.Mode == SlideMode.Glide)
                    bonus = actualBonus;
                else if (state.Mode == SlideMode.RunUp)
                    // Keep any remaining physical bonus visible while the next charge builds.
                    bonus = Mathf.Max(actualBonus, GlideBonus * ChargeFeedback(state));
            }
            return bonus * 100f * Parameter(frozenOceanGlidingFovScale.Value, 1f, 0f, 3f);
        }

        private static float ResolveGlideFov(float baseFov, GameCamera camera)
        {
            if (!camera || camera != GameCamera.instance || camera.m_freeFly || !Finite(baseFov))
                return baseFov;
            float offset = GetGlideFovOffset();
            // This is the incoming camera value for this write, not last frame's fieldOfView.
            // Never mutate m_fov, m_fovBase or m_fovTarget, and never narrow the base view.
            return offset > 0f ? baseFov + Mathf.Min(offset, Mathf.Max(0f, 179f - baseFov)) : baseFov;
        }

        private static float ResolveRunCadence(float value, Character character)
        {
            if (!frozenOceanGlidingEnabled.Value || !Finite(value) || !TrySlide(character, out PlayerSlide state) ||
                !state.OnIce || !character.IsOnIce() || !character.m_running || HasBlockingAction(state.Player))
                return value;
            float result = value * (1f + ChargeFeedback(state) * Parameter(frozenOceanGlidingAnimationBonus.Value, 0.175f, 0f, 1f));
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
