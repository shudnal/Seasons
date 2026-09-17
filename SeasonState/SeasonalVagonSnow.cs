using BepInEx.Configuration;
using ConditionalConfigSync;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;
using static Seasons.Seasons;
using static Seasons.ZoneSystemVariantController;

namespace Seasons
{
    internal static class SeasonalVagonSnow
    {
        private const float MinimumHeightAboveUnfrozenWater = 1f;
        private const float VanillaSlippingMaximumWaterDepth = 4f;

        private static ConfigEntry<bool> enableVanillaSlippingOnShallowFrozenWater;

        private static void InitializeConfig()
        {
            if (enableVanillaSlippingOnShallowFrozenWater != null || instance == null)
                return;

            enableVanillaSlippingOnShallowFrozenWater = configSync.AddConfigEntry(
                instance.Config,
                "Season - Winter ocean",
                "Enable vanilla slipping on shallow frozen water",
                true,
                new ConfigDescription(
                    "Enable Valheim's stronger native slipping on frozen shallow water up to 4 meters deep outside the Ocean biome. Deeper water and the Ocean biome keep Seasons' smoother sliding. Ice skates always use native slipping; ice shoes disable Seasons sliding."),
                syncMode: ConfigSyncMode.AlwaysServerControlled,
                serverControlledByDefault: true).SourceConfig;
        }

        private static bool ShouldUseSeasonalWinterSnow(Vagon vagon)
        {
            if (!vagon || !SeasonState.IsActive || seasonState.GetCurrentSeason() != Season.Winter || ZoneSystem.instance == null)
                return false;

            bool waterFrozen = IsWaterSurfaceFrozen();
            return waterFrozen ||
                (!waterFrozen && vagon.transform.position.y - ZoneSystem.instance.m_waterLevel > MinimumHeightAboveUnfrozenWater);
        }

        private static bool ShouldUseVanillaIceSlipping(Character character)
        {
            if (!character || character.m_iceShoes || !character.IsOnIce())
                return false;

            // Humanoid.UpdateEquipment derives m_skating from equipped ItemData.m_shared.m_iceSkates.
            if (character.m_skating)
                return true;

            InitializeConfig();
            if (enableVanillaSlippingOnShallowFrozenWater?.Value != true || ZoneSystem.instance == null)
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

            float waterDepth = ZoneSystem.instance.m_waterLevel - groundPosition.y;
            return waterDepth <= VanillaSlippingMaximumWaterDepth;
        }

        private static bool ShouldSuppressSeasonsIceSliding(Character character)
        {
            return character &&
                (character.m_iceShoes || character.m_skating || ShouldUseVanillaIceSlipping(character));
        }

        private static void RestoreNativeGroundedBodyFriction(Character character, CapsuleCollider collider)
        {
            if (!character || !collider)
                return;

            PhysicsMaterial material = collider.material;
            if (!material)
                return;

            material.frictionCombine = PhysicsMaterialCombine.Multiply;
            float friction;
            if (character.m_moveDir.magnitude < 0.1f)
            {
                friction = 0.8f * (1f - character.m_slippage);
                material.frictionCombine = PhysicsMaterialCombine.Maximum;
            }
            else
            {
                friction = 0.4f * (1f - character.m_slippage);
            }

            material.staticFriction = friction;
            material.dynamicFriction = friction;
        }

        [HarmonyPatch(typeof(Vagon), "UpdateSnow")]
        private static class Vagon_UpdateSnow_SeasonalWinterSnow
        {
            [HarmonyPrepare]
            private static bool Prepare()
            {
                InitializeConfig();
                return true;
            }

            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
                var lastBiomeField = AccessTools.Field(typeof(Vagon), "m_lastBiome");
                var conditionMethod = AccessTools.Method(typeof(SeasonalVagonSnow), nameof(ShouldUseSeasonalWinterSnow));

                for (int i = 0; i < codes.Count - 2; ++i)
                {
                    if (codes[i].opcode != OpCodes.Ldfld || !Equals(codes[i].operand, lastBiomeField) ||
                        !codes[i + 1].LoadsConstant((long)Heightmap.Biome.Mountain))
                        continue;

                    CodeInstruction branch = codes[i + 2];
                    if (branch.operand is not Label target)
                        continue;

                    if (branch.opcode == OpCodes.Beq || branch.opcode == OpCodes.Beq_S)
                    {
                        codes.InsertRange(i + 3, new[]
                        {
                            new CodeInstruction(OpCodes.Ldarg_0),
                            new CodeInstruction(OpCodes.Call, conditionMethod),
                            new CodeInstruction(OpCodes.Brtrue, target)
                        });
                        return codes;
                    }

                    if (branch.opcode != OpCodes.Bne_Un && branch.opcode != OpCodes.Bne_Un_S)
                        continue;

                    Label vanillaSnowLabel = generator.DefineLabel();
                    codes[i + 3].labels.Add(vanillaSnowLabel);
                    branch.opcode = OpCodes.Beq;
                    branch.operand = vanillaSnowLabel;
                    codes.InsertRange(i + 3, new[]
                    {
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Call, conditionMethod),
                        new CodeInstruction(OpCodes.Brfalse, target)
                    });
                    return codes;
                }

                LogWarning("Failed to extend Vagon.UpdateSnow with the seasonal winter snow condition.");
                return codes;
            }
        }

        [HarmonyPatch(typeof(CharacterExtentions_FrozenOceanSliding), nameof(CharacterExtentions_FrozenOceanSliding.StartIceSliding))]
        private static class Character_StartIceSliding_RespectNativeIceEquipment
        {
            private static bool Prefix(Character character)
            {
                if (!ShouldSuppressSeasonsIceSliding(character))
                    return true;

                character.StopIceSliding();
                return false;
            }
        }

        [HarmonyPatch(typeof(CharacterExtentions_FrozenOceanSliding), nameof(CharacterExtentions_FrozenOceanSliding.UpdateIceSliding))]
        private static class Character_UpdateIceSliding_RespectNativeIceEquipment
        {
            private static bool Prefix(Character character)
            {
                if (!ShouldSuppressSeasonsIceSliding(character))
                    return true;

                character.StopIceSliding();
                return false;
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.UpdateWalking))]
        private static class Character_UpdateWalking_ShallowFrozenWaterSlipping
        {
            private struct GroundMaterialState
            {
                public PhysicsMaterial Material;
                public float DynamicFriction;
                public bool Applied;
            }

            private static void Prefix(Character __instance, CapsuleCollider ___m_collider, ref GroundMaterialState __state)
            {
                __state = default;
                if (!__instance || !__instance.IsOnIce())
                    return;

                bool useVanillaSlipping = ShouldUseVanillaIceSlipping(__instance);
                if (__instance.m_iceShoes || __instance.m_skating || useVanillaSlipping)
                    __instance.StopIceSliding();

                if (__instance.m_iceShoes)
                {
                    // The existing Seasons ice patch runs after Character.UpdateBodyFriction and applies
                    // the smoother 0.1 friction. Restore the native grounded character friction here so
                    // ice shoes fully opt out of Seasons sliding.
                    RestoreNativeGroundedBodyFriction(__instance, ___m_collider);
                    return;
                }

                if (!useVanillaSlipping)
                    return;

                // Keep the character collider frictionless through the physics step while this surface
                // uses native slippery/skating movement. The shared ice surface itself stays at the
                // smoother Seasons friction so deep-ocean characters are unaffected.
                PhysicsMaterial bodyMaterial = ___m_collider?.material;
                if (bodyMaterial)
                {
                    bodyMaterial.staticFriction = 0f;
                    bodyMaterial.dynamicFriction = 0f;
                    bodyMaterial.frictionCombine = PhysicsMaterialCombine.Minimum;
                }

                Collider groundCollider = __instance.GetLastGroundCollider();
                PhysicsMaterial groundMaterial = groundCollider?.material;
                if (!groundMaterial)
                    return;

                __state.Material = groundMaterial;
                __state.DynamicFriction = groundMaterial.dynamicFriction;
                __state.Applied = true;
                groundMaterial.dynamicFriction = 0f;
            }

            private static void Finalizer(GroundMaterialState __state)
            {
                if (__state.Applied && __state.Material && __state.Material.dynamicFriction == 0f)
                    __state.Material.dynamicFriction = __state.DynamicFriction;
            }
        }
    }
}
