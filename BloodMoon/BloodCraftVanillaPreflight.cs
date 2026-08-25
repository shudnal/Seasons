using HarmonyLib;
using System;

namespace Seasons.BloodMoon
{
    internal static class BloodCraftVanillaPreflight
    {
        private static bool IsBloodCraftSelection(Recipe recipe)
        {
            InventoryGui gui = InventoryGui.instance;
            if (BloodCraft.IsBloodRecipe(recipe))
                return true;
            return gui != null && gui.m_selectedRecipe.Recipe == recipe && BloodCraft.IsTemporary(gui.m_selectedRecipe.ItemData);
        }

        [HarmonyPatch(typeof(Recipe), nameof(Recipe.GetAmount))]
        private static class RecipeGetAmountPatch
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Recipe __instance, int craftMultiplier, ref int need, ref ItemDrop.ItemData singleReqItem, ref int __result)
            {
                if (!IsBloodCraftSelection(__instance))
                    return true;

                need = 0;
                singleReqItem = null;
                __result = Math.Max(1, __instance.m_amount) * Math.Max(1, craftMultiplier);
                return false;
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirementList))]
        private static class InventoryGuiSetupRequirementListPatch
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(InventoryGui __instance)
            {
                if (__instance == null || !BloodCraft.IsTemporary(__instance.m_selectedRecipe.ItemData))
                    return true;

                foreach (GameObject requirement in __instance.m_recipeRequirementList)
                {
                    if (requirement != null)
                        InventoryGui.HideRequirement(requirement.transform);
                }
                return false;
            }
        }
    }
}
