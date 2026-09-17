using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;
using static Seasons.Seasons;
using static Seasons.ZoneSystemVariantController;

namespace Seasons
{
    public static class CharacterExtentions_FrozenOceanSliding
    {
        private const float VanillaSlippingMaximumWaterDepth = 4f;

        private struct SlideStatus
        {
            public Vector3 m_iceSlipVelocity;
            public float m_slip;
        }

        private static readonly Dictionary<Character, SlideStatus> charactersSlides = new Dictionary<Character, SlideStatus>();

        internal static void ResetWorldState()
        {
            charactersSlides.Clear();
            Character_UpdateGroundContact_FrozenOceanSlippery.ResetWorldState();
            Player_UpdateDodge_FrozenOceanSlippery.ResetWorldState();
        }

        public static bool IsOnIce(this Character character)
        {
            if (!IsWaterSurfaceFrozen())
                return false;

            if (!character.IsOnGround())
                return false;

            Collider lastGroundCollider = character.GetLastGroundCollider();
            if (lastGroundCollider == null)
                return false;

            return lastGroundCollider.name == _iceSurfaceName;
        }

        private static bool ShouldUseVanillaIceSlipping(Character character)
        {
            if (!character || character.m_iceShoes || !character.IsOnIce())
                return false;

            // Humanoid.UpdateEquipment derives m_skating from equipped ItemData.m_shared.m_iceSkates.
            if (character.m_skating)
                return true;

            if (!enableVanillaSlippingOnShallowFrozenWater.Value || ZoneSystem.instance == null)
                return false;

            Vector3 groundPosition = character.transform.position;
            ZoneSystem.instance.GetGroundData(
                ref groundPosition,
                out var _,
                out Heightmap.Biome biome,
                out var _,
                out var _);

            if (biome == Heightmap.Biome.Ocean)
                return false;

            return ZoneSystem.instance.m_waterLevel - groundPosition.y <= VanillaSlippingMaximumWaterDepth;
        }

        private static bool ShouldSuppressSeasonsIceSliding(Character character)
        {
            return character && (character.m_iceShoes || character.m_skating || ShouldUseVanillaIceSlipping(character));
        }

        private static bool ResolveVanillaSlipping(bool vanillaSlipping, Character character)
        {
            if (!character || !character.IsOnIce())
                return vanillaSlipping;

            return ShouldUseVanillaIceSlipping(character);
        }

        public static void StartIceSliding(this Character character, Vector3 currentVel, bool checkMagnitude = false, bool checkRunning = true)
        {
            if (!character)
                return;

            if (ShouldSuppressSeasonsIceSliding(character))
            {
                character.StopIceSliding();
                return;
            }

            if (frozenOceanSlipperiness.Value <= 0)
                return;

            if (checkRunning && character.IsRunning())
                return;

            SlideStatus slideStatus = charactersSlides.GetValueSafe(character);
            slideStatus.m_slip = 1f;

            if (checkMagnitude && slideStatus.m_iceSlipVelocity.magnitude > currentVel.magnitude)
                return;

            slideStatus.m_iceSlipVelocity = Vector3.ClampMagnitude(currentVel, 10f);

            charactersSlides[character] = slideStatus;
        }

        public static void UpdateIceSliding(this Character character, ref Vector3 currentVel)
        {
            if (!character)
                return;

            if (ShouldSuppressSeasonsIceSliding(character))
            {
                character.StopIceSliding();
                return;
            }

            if (!charactersSlides.TryGetValue(character, out SlideStatus slideStatus))
                return;

            if (slideStatus.m_slip > 0f && (character.IsOnIce() || !character.IsOnGround()))
            {
                currentVel = Vector3.Lerp(currentVel, slideStatus.m_iceSlipVelocity, slideStatus.m_slip);
                float delta = character.IsOnGround() ? Time.fixedDeltaTime / 2 / Mathf.Abs(frozenOceanSlipperiness.Value) : Time.fixedDeltaTime;
                slideStatus.m_slip = Mathf.MoveTowards(slideStatus.m_slip, 0f, delta);
                charactersSlides[character] = slideStatus;
            }
            else
            {
                character.StopIceSliding();
            }
        }

        public static void StopIceSliding(this Character character)
        {
            if (character)
                charactersSlides.Remove(character);
        }

        [HarmonyPatch(typeof(Character), nameof(Character.OnDestroy))]
        public static class Character_OnDestroy_WaterVariantControllerInit
        {
            private static void Prefix(Character __instance)
            {
                __instance.StopIceSliding();
                Character_UpdateGroundContact_FrozenOceanSlippery.RemoveCharacter(__instance);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        public static class Player_SetControls_FrozenOceanSlippery
        {
            private static void Prefix(Player __instance, bool run)
            {
                if (frozenOceanSlipperiness.Value == 0 || __instance != Player.m_localPlayer || !__instance.IsOnIce())
                    return;

                if (!run && __instance.m_run)
                    __instance.StartIceSliding(__instance.m_currentVel, checkRunning: false);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.SetRun))]
        public static class Character_SetRun_FrozenOceanSlippery
        {
            private static void Prefix(Character __instance, bool run, ZNetView ___m_nview)
            {
                if (frozenOceanSlipperiness.Value == 0 || !__instance.IsOnIce() || !___m_nview.IsValid() || !___m_nview.IsOwner())
                    return;

                if (!run && __instance.m_run)
                    __instance.StartIceSliding(__instance.m_currentVel, checkRunning: false);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.UpdateGroundContact))]
        public static class Character_UpdateGroundContact_FrozenOceanSlippery
        {
            private struct SlideState
            {
                public bool HasValue;
                public float AirAltitude;
                public Vector3 BodyVelocity;
            }

            private static readonly Dictionary<Character, Vector3> m_characterSlideVelocity = new Dictionary<Character, Vector3>(64);

            internal static void ResetWorldState() => m_characterSlideVelocity.Clear();

            public static void RemoveCharacter(Character character) => m_characterSlideVelocity.Remove(character);

            public static void CheckForSlide(Character characterSyncVelocity)
            {
                if (characterSyncVelocity == null || m_characterSlideVelocity.Count == 0)
                    return;

                if (m_characterSlideVelocity.TryGetValue(characterSyncVelocity, out Vector3 bodyVelocity))
                {
                    m_characterSlideVelocity.Remove(characterSyncVelocity);
                    characterSyncVelocity.StartIceSliding(bodyVelocity, checkMagnitude: true);
                }
            }

            private static void Prefix(
                Character __instance,
                ZNetView ___m_nview,
                float ___m_maxAirAltitude,
                ref SlideState __state)
            {
                __state = default;

                if (frozenOceanSlipperiness.Value == 0f || ___m_nview == null || !___m_nview.IsValid() || !___m_nview.IsOwner())
                    return;

                Vector3 position = __instance.transform.position;

                __state.HasValue = true;
                __state.AirAltitude = Mathf.Max(0f, ___m_maxAirAltitude - position.y);
                __state.BodyVelocity = __instance.m_body.linearVelocity;
            }

            private static void Postfix(
                Character __instance,
                float ___m_maxAirAltitude,
                SlideState __state)
            {
                if (!__state.HasValue)
                    return;

                if (__state.AirAltitude <= 1f || frozenOceanSlipperiness.Value <= 0f)
                    return;

                float positionY = __instance.transform.position.y;

                if (___m_maxAirAltitude == positionY && __instance.IsOnIce())
                    m_characterSlideVelocity[__instance] = __state.BodyVelocity;
            }

            [HarmonyPatch(typeof(Character), nameof(Character.SyncVelocity))]
            public static class Character_SyncVelocity_FrozenOceanSlippery
            {
                private static void Prefix(Character __instance)
                {
                    CheckForSlide(__instance);
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.UpdateDodge))]
        public static class Player_UpdateDodge_FrozenOceanSlippery
        {
            private static bool m_initiateSlide;
            private static Vector3 m_bodyVelocity = Vector3.zero;
            private static Player m_slidePlayer;

            internal static void ResetWorldState()
            {
                m_initiateSlide = false;
                m_bodyVelocity = Vector3.zero;
                m_slidePlayer = null;
            }

            [HarmonyPriority(Priority.First)]
            private static void Prefix(Player __instance, bool ___m_inDodge, ref bool __state)
            {
                if (m_slidePlayer != __instance)
                {
                    m_slidePlayer = __instance;
                    m_initiateSlide = false;
                    m_bodyVelocity = Vector3.zero;
                }
                if (m_initiateSlide && !___m_inDodge && __instance.IsOnIce())
                    __instance.StartIceSliding(m_bodyVelocity);

                __state = frozenOceanSlipperiness.Value > 0 && ___m_inDodge && __instance == Player.m_localPlayer;
            }

            private static void Postfix(Player __instance, bool ___m_inDodge, ref bool __state)
            {
                m_initiateSlide = frozenOceanSlipperiness.Value > 0 && ___m_inDodge && __instance == Player.m_localPlayer;
                m_bodyVelocity = __instance.m_queuedDodgeDir * __instance.m_body.linearVelocity.magnitude;

                if (__state && !___m_inDodge && __instance.IsOnIce())
                    __instance.StartIceSliding(m_bodyVelocity);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.ApplyGroundForce))]
        public static class Character_ApplyGroundForce_FrozenOceanSlippery
        {
            private static void Postfix(Character __instance, ZNetView ___m_nview, ref Vector3 vel)
            {
                if (!charactersSlides.ContainsKey(__instance))
                    return;

                if (frozenOceanSlipperiness.Value == 0 || !___m_nview.IsValid() || !___m_nview.IsOwner())
                {
                    __instance.StopIceSliding();
                    return;
                }

                __instance.UpdateIceSliding(ref vel);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.UpdateBodyFriction))]
        public static class Character_UpdateBodyFriction_FrozenOceanSurface
        {
            private static void Postfix(Character __instance, CapsuleCollider ___m_collider)
            {
                if (!__instance.IsOnIce() || __instance.m_iceShoes)
                    return;

                PhysicsMaterial material = ___m_collider.material;
                float friction = ShouldUseVanillaIceSlipping(__instance) ? 0f : 0.1f;
                if (material.staticFriction != friction)
                    material.staticFriction = friction;
                if (material.dynamicFriction != friction)
                    material.dynamicFriction = friction;
                if (material.frictionCombine != PhysicsMaterialCombine.Minimum)
                    material.frictionCombine = PhysicsMaterialCombine.Minimum;
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.UpdateWalking))]
        private static class Character_UpdateWalking_FrozenOceanNativeSlipping
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
                var slippingField = AccessTools.Field(typeof(Character), nameof(Character.m_slipping));
                var resolveMethod = AccessTools.Method(typeof(CharacterExtentions_FrozenOceanSliding), nameof(ResolveVanillaSlipping));

                for (int i = 0; i < codes.Count; ++i)
                {
                    if (codes[i].opcode != OpCodes.Stfld || !Equals(codes[i].operand, slippingField))
                        continue;

                    codes.InsertRange(i, new[]
                    {
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Call, resolveMethod)
                    });
                    return codes;
                }

                LogWarning("Failed to extend Character.UpdateWalking with frozen ocean native slipping.");
                return codes;
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.ApplySlippery))]
        private static class Character_ApplySlippery_FrozenOceanNativeSlipping
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
                var skatingField = AccessTools.Field(typeof(Character), nameof(Character.m_skating));
                var isOnIceMethod = AccessTools.Method(typeof(CharacterExtentions_FrozenOceanSliding), nameof(IsOnIce));
                var shouldUseVanillaMethod = AccessTools.Method(typeof(CharacterExtentions_FrozenOceanSliding), nameof(ShouldUseVanillaIceSlipping));

                for (int i = 1; i < codes.Count; ++i)
                {
                    if (codes[i].opcode != OpCodes.Ldfld || !Equals(codes[i].operand, skatingField) || codes[i - 1].opcode != OpCodes.Ldarg_0)
                        continue;

                    Label vanillaPath = generator.DefineLabel();
                    Label slipperyBody = generator.DefineLabel();
                    codes[0].labels.Add(vanillaPath);
                    codes[i - 1].labels.Add(slipperyBody);
                    codes.InsertRange(0, new[]
                    {
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Call, isOnIceMethod),
                        new CodeInstruction(OpCodes.Brfalse, vanillaPath),
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Call, shouldUseVanillaMethod),
                        new CodeInstruction(OpCodes.Brtrue, slipperyBody),
                        new CodeInstruction(OpCodes.Ret)
                    });
                    return codes;
                }

                LogWarning("Failed to extend Character.ApplySlippery with frozen ocean native slipping.");
                return codes;
            }
        }
    }
}
