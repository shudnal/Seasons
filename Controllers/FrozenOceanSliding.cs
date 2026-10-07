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
                bool gliding = playerSlide != null && ReferenceEquals(playerSlide.Player, __instance) && playerSlide.Mode == SlideMode.Glide;
                float friction = ShouldUseVanillaIceSlipping(__instance) ? 0f : gliding
                    ? Parameter(frozenOceanGlidingFriction.Value, 0f, 0f, 1f)
                    : Parameter(frozenOceanSlidingFriction.Value, 0.05f, 0f, 1f);
                PhysicsMaterial material = ___m_collider.material;
                if (material.staticFriction != friction)
                    material.staticFriction = friction;
                if (material.dynamicFriction != friction)
                    material.dynamicFriction = friction;
                if (material.frictionCombine != PhysicsMaterialCombine.Minimum)
                    material.frictionCombine = PhysicsMaterialCombine.Minimum;
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.UpdateMotion))]
        private static class Character_UpdateMotion_ReleaseSeasonalSlide
        {
            [HarmonyPrefix]
            private static void Prefix(Character __instance)
            {
                if (playerSlide != null && ReferenceEquals(playerSlide.Player, __instance) &&
                    (!CanOwnSlide(playerSlide.Player) || !IsWaterSurfaceFrozen()))
                    ResetWorldState();
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        private static class Player_SetControls_FrozenOceanSliding
        {
            [HarmonyPrefix]
            private static void Prefix(Player __instance, Vector3 movedir, bool attack, bool attackHold,
                bool secondaryAttack, bool secondaryAttackHold, bool block, bool blockHold,
                bool jump, bool crouch, bool run, bool autoRun, bool dodge)
            {
                PlayerSlide state = playerSlide;
                if (state == null || !ReferenceEquals(state.Player, __instance))
                    return;
                bool action = attack || attackHold || secondaryAttack || secondaryAttackHold || block || blockHold ||
                    jump || crouch || dodge || autoRun;
                if (state.Mode == SlideMode.Glide)
                {
                    float deadZone = Parameter(frozenOceanSlidingInputDeadZone.Value, 0.1f, 0.001f, 0.5f);
                    // Held-state reads do not consume the native button-down events. Also
                    // recognize opposing keys whose combined movement vector would be zero.
                    bool movement = !Finite(movedir) || movedir.sqrMagnitude > deadZone * deadZone ||
                        ZInput.GetButton("Forward") || ZInput.GetButton("Backward") || ZInput.GetButton("Left") || ZInput.GetButton("Right");
                    action |= movement || (run && !state.RunInput) || ZInput.GetButton("Use") || ZInput.GetButton("JoyUse");
                }
                state.RunInput = run;
                if (action)
                    InterruptSlide(__instance, SlideReason.Input);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Interact))]
        private static class Player_Interact_StopLongGlide
        {
            [HarmonyPrefix]
            private static void Prefix(Player __instance) => InterruptSlide(__instance, SlideReason.Action);
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Jump))]
        private static class Character_Jump_StopLongGlide
        {
            [HarmonyPrefix]
            private static void Prefix(Character __instance) => InterruptSlide(__instance, SlideReason.Action);
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        private static class Humanoid_EquipItem_StopLongGlide
        {
            [HarmonyPrefix]
            private static void Prefix(Humanoid __instance, ItemDrop.ItemData item)
            {
                if (item != null && !item.m_equipped)
                    InterruptSlide(__instance, SlideReason.Action);
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
        private static class Humanoid_UnequipItem_StopLongGlide
        {
            [HarmonyPrefix]
            private static void Prefix(Humanoid __instance, ItemDrop.ItemData item)
            {
                if (item != null && item.m_equipped)
                    InterruptSlide(__instance, SlideReason.Action);
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
        private static class Humanoid_UseItem_StopLongGlide
        {
            [HarmonyPrefix]
            private static void Prefix(Humanoid __instance) => InterruptSlide(__instance, SlideReason.Action);
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
                var slippingAnimation = AccessTools.Field(typeof(Character), nameof(Character.s_slipping));
                var setBool = AccessTools.Method(typeof(ZSyncAnimation), nameof(ZSyncAnimation.SetBool), new[] { typeof(int), typeof(bool) });
                int animationFieldIndex = code.FindIndex(instruction => instruction.LoadsField(slippingAnimation));
                int animationIndex = animationFieldIndex < 0 ? -1 : code.FindIndex(animationFieldIndex, instruction => instruction.Calls(setBool));
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
                    speedIndex - canMoveIndex < 20 && blendIndex > speedIndex && blendMatches == 1 &&
                    animationFieldIndex > blendIndex && animationIndex > animationFieldIndex;
                if (!walkingHookReady)
                {
                    LogWarning("Seasonal ice sliding is disabled: Character.UpdateWalking integration points were not recognized.");
                    return code;
                }

                // Supply one final value to the existing synchronized animation write.
                InsertBefore(code, animationIndex, new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CharacterExtentions_FrozenOceanSliding), nameof(ResolveSlippingAnimation))));
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
