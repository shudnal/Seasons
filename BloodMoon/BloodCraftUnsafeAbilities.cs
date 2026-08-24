using HarmonyLib;
using System.Collections.Generic;

namespace Seasons.BloodMoon
{
    internal static class BloodCraftUnsafeAbilities
    {
        internal static bool UsesPersistentWorldSpawner(ItemDrop.ItemData item)
        {
            return item?.m_shared != null &&
                (AttackUsesPersistentWorldSpawner(item.m_shared.m_attack) || AttackUsesPersistentWorldSpawner(item.m_shared.m_secondaryAttack));
        }

        private static bool AttackUsesPersistentWorldSpawner(Attack attack)
        {
            return attack?.m_attackProjectile != null && attack.m_attackProjectile.GetComponent<TriggerSpawnAbility>() != null;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.GetAvailableRecipes))]
        [HarmonyPriority(Priority.Last)]
        private static class PlayerGetAvailableRecipesSafetyPatch
        {
            private static void Postfix(ref List<Recipe> available)
            {
                available?.RemoveAll(recipe => BloodCraft.IsBloodRecipe(recipe) && UsesPersistentWorldSpawner(recipe.m_item?.m_itemData));
            }
        }

        [HarmonyPatch(typeof(TriggerSpawnAbility), nameof(TriggerSpawnAbility.Setup))]
        private static class TriggerSpawnAbilitySafetyPatch
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(TriggerSpawnAbility __instance, Character owner, ItemDrop.ItemData item)
            {
                if (!BloodCraft.HasMarker(item))
                    return true;

                if (owner is Player player && player == Player.m_localPlayer)
                    player.Message(MessageHud.MessageType.Center, "Blood Craft items cannot trigger persistent world spawners.");
                if (__instance != null)
                    UnityEngine.Object.Destroy(__instance.gameObject);
                return false;
            }
        }
    }
}
