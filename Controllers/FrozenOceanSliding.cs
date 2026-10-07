using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;
using static Seasons.Seasons;
using static Seasons.ZoneSystemVariantController;

namespace Seasons
{
    // Seasonal IceSurface is a separate movement domain. Never seed vanilla momentum from it.
    public static partial class CharacterExtentions_FrozenOceanSliding
    {
        private static bool walkingHookReady;
        private static bool slipperyHookReady;
        private static bool HooksReady => walkingHookReady && slipperyHookReady;
        private static bool SlidingEnabled => HooksReady && frozenOceanSlidingEnabled.Value && frozenOceanSlipperiness.Value > 0f;

        public static bool IsOnIce(this Character character)
        {
            if (!character || !IsWaterSurfaceFrozen() || !character.IsOnGround())
                return false;
            Collider ground = character.GetLastGroundCollider();
            return ground && ground.name == _iceSurfaceName;
        }

        private static bool ShouldUseVanillaIceSlipping(Character character) =>
            SlidingEnabled && character && !character.m_iceShoes &&
            (character.m_skating || !(character is Player));

        private static bool ResolveVanillaSlipping(bool vanillaSlipping, Character character)
        {
            bool onIce = character.IsOnIce();
            PreparePlayerSurface(character, onIce);
            // Native ice, terrain and floating floes retain their own logic and state.
            return onIce && HooksReady ? ShouldUseVanillaIceSlipping(character) : vanillaSlipping;
        }

        [HarmonyPatch(typeof(Character), nameof(Character.OnDestroy))]
        private static class Character_OnDestroy_FrozenOceanSliding
        {
            [HarmonyPrefix]
            private static void Prefix(Character __instance)
            {
                if (playerSlide != null && ReferenceEquals(playerSlide.Player, __instance))
                    ResetWorldState();
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.UpdateBodyFriction))]
        private static class Character_UpdateBodyFriction_FrozenOceanSurface
        {
            [HarmonyPostfix]
            private static void Postfix(Character __instance, CapsuleCollider ___m_collider)
            {
                if (!SlidingEnabled || !__instance.IsOnIce() || __instance.m_iceShoes)
                    return;
                float friction = ShouldUseVanillaIceSlipping(__instance) ? 0f :
                    Parameter(frozenOceanSlidingFriction.Value, 0.05f, 0f, 1f);
                PhysicsMaterial material = ___m_collider.material;
                if (material.staticFriction != friction)
                    material.staticFriction = friction;
                if (material.dynamicFriction != friction)
                    material.dynamicFriction = friction;
                if (material.frictionCombine != PhysicsMaterialCombine.Minimum)
                    material.frictionCombine = PhysicsMaterialCombine.Minimum;
            }
        }

        private static void InsertBefore(List<CodeInstruction> code, int index, params CodeInstruction[] added)
        {
            // Incoming branches must execute the injected code, not jump past it.
            added[0].labels.AddRange(code[index].labels);
            added[0].blocks.AddRange(code[index].blocks);
            code[index].labels.Clear();
            code[index].blocks.Clear();
            code.InsertRange(index, added);
        }

        [HarmonyPatch(typeof(Character), nameof(Character.UpdateWalking))]
        private static class Character_UpdateWalking_FrozenOceanSliding
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = new List<CodeInstruction>(instructions);
                var slipping = AccessTools.Field(typeof(Character), nameof(Character.m_slipping));
                var currentVelocity = AccessTools.Field(typeof(Character), nameof(Character.m_currentVel));
                var canMove = AccessTools.Method(typeof(Character), nameof(Character.CanMove));
                var multiply = AccessTools.Method(typeof(Vector3), "op_Multiply", new[] { typeof(Vector3), typeof(float) });
                var lerp = AccessTools.Method(typeof(Vector3), nameof(Vector3.Lerp));
                int slippingIndex = code.FindIndex(instruction => instruction.StoresField(slipping));
                int canMoveIndex = code.FindIndex(instruction => instruction.Calls(canMove));
                int speedIndex = canMoveIndex < 0 ? -1 : code.FindIndex(canMoveIndex, instruction => instruction.Calls(multiply));
                int blendIndex = -1;
                int blendMatches = 0;
                for (int i = 0; i + 1 < code.Count; ++i)
                    if (code[i].Calls(lerp) && code[i + 1].StoresField(currentVelocity))
                    {
                        blendIndex = i;
                        blendMatches++;
                    }
                walkingHookReady = slippingIndex >= 0 && canMoveIndex > slippingIndex && speedIndex > canMoveIndex &&
                    speedIndex - canMoveIndex < 20 && blendIndex > speedIndex && blendMatches == 1;
                if (!walkingHookReady)
                {
                    LogWarning("Seasonal ice sliding is disabled: Character.UpdateWalking integration points were not recognized.");
                    return code;
                }

                // Replace only the native walking blend. Root motion, pushback, ground forces,
                // air control and the actual Rigidbody force application remain downstream.
                code[blendIndex].operand = AccessTools.Method(typeof(CharacterExtentions_FrozenOceanSliding), nameof(BlendWalkingVelocity));
                InsertBefore(code, blendIndex, new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldarg_1));
                InsertBefore(code, speedIndex, new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CharacterExtentions_FrozenOceanSliding), nameof(ResolveWalkingSpeed))));
                InsertBefore(code, slippingIndex, new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CharacterExtentions_FrozenOceanSliding), nameof(ResolveVanillaSlipping))));
                return code;
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.ApplySlippery))]
        private static class Character_ApplySlippery_FrozenOceanNativeSlipping
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                var code = new List<CodeInstruction>(instructions);
                var skating = AccessTools.Field(typeof(Character), nameof(Character.m_skating));
                int body = -1;
                for (int i = 1; i < code.Count; ++i)
                    if (code[i].LoadsField(skating) && code[i - 1].opcode == OpCodes.Ldarg_0)
                    {
                        body = i - 1;
                        break;
                    }
                slipperyHookReady = body >= 0;
                if (!slipperyHookReady)
                {
                    LogWarning("Seasonal ice sliding is disabled: Character.ApplySlippery integration point was not recognized.");
                    return code;
                }
                Label native = generator.DefineLabel();
                Label slippery = generator.DefineLabel();
                code[0].labels.Add(native);
                code[body].labels.Add(slippery);
                code.InsertRange(0, new[]
                {
                    new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(CharacterExtentions_FrozenOceanSliding), nameof(HooksReady))),
                    new CodeInstruction(OpCodes.Brfalse, native),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CharacterExtentions_FrozenOceanSliding), nameof(IsOnIce))),
                    new CodeInstruction(OpCodes.Brfalse, native),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CharacterExtentions_FrozenOceanSliding), nameof(ShouldUseVanillaIceSlipping))),
                    new CodeInstruction(OpCodes.Brtrue, slippery),
                    new CodeInstruction(OpCodes.Ret)
                });
                return code;
            }
        }
    }
}
