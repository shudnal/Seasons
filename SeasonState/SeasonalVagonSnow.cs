using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
using static Seasons.Seasons;
using static Seasons.ZoneSystemVariantController;

namespace Seasons
{
    internal static class SeasonalVagonSnow
    {
        private const float MinimumHeightAboveUnfrozenWater = 1f;

        private static bool ShouldUseSeasonalWinterSnow(Vagon vagon)
        {
            if (!vagon || !SeasonState.IsActive || seasonState.GetCurrentSeason() != Season.Winter || ZoneSystem.instance == null)
                return false;

            bool waterFrozen = IsWaterSurfaceFrozen();
            return waterFrozen ||
                (!waterFrozen && vagon.transform.position.y - ZoneSystem.instance.m_waterLevel > MinimumHeightAboveUnfrozenWater);
        }

        [HarmonyPatch(typeof(Vagon), "UpdateSnow")]
        private static class Vagon_UpdateSnow_SeasonalWinterSnow
        {
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
    }
}
