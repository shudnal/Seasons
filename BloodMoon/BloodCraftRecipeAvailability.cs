using HarmonyLib;
using System.Collections.Generic;
using System.Linq;

namespace Seasons.BloodMoon
{
    internal static class BloodCraftRecipeAvailability
    {
        [HarmonyPatch(typeof(BloodCraft), "IsEligibleKnownRecipe")]
        private static class SourceRecipePatch
        {
            private static void Postfix(Recipe recipe, ref bool __result)
            {
                if (__result && (recipe == null || !recipe.m_enabled))
                    __result = false;
            }
        }

        [HarmonyPatch(typeof(BloodCraft), nameof(BloodCraft.AppendAvailableRecipes))]
        private static class UpgradeRecipePatch
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Player player, List<Recipe> available)
            {
                if (player == null || available == null)
                    return;

                HashSet<string> temporaryNames = new HashSet<string>(player.GetInventory().GetAllItems()
                    .Where(BloodCraft.IsTemporary)
                    .Select(item => item.m_shared?.m_name)
                    .Where(name => !string.IsNullOrEmpty(name)));

                available.RemoveAll(recipe => recipe != null && !recipe.m_enabled &&
                    (BloodCraft.IsBloodRecipe(recipe) ||
                     recipe.m_item?.m_itemData?.m_shared != null && temporaryNames.Contains(recipe.m_item.m_itemData.m_shared.m_name)));
            }
        }
    }
}
