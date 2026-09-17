using HarmonyLib;
using System.Linq;

namespace Seasons.BloodMoon
{
    internal static class BloodCraftGraceGuards
    {
        internal static bool CanUse(Humanoid humanoid, ItemDrop.ItemData item)
        {
            if (!BloodCraft.HasMarker(item))
                return true;
            return humanoid is Player player && player == Player.m_localPlayer && BloodCraft.IsValidFor(player, item);
        }

        internal static void QuarantineEquippedItems()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            foreach (ItemDrop.ItemData item in player.GetInventory().GetEquippedItems().ToArray())
            {
                if (BloodCraft.HasMarker(item) && !BloodCraft.IsValidFor(player, item))
                    player.UnequipItem(item, triggerEquipEffects: false);
            }
        }

        internal static ItemDrop.ItemData FindUsableAmmo(Inventory inventory, string ammoName, string matchPrefabName)
        {
            Player player = Player.m_localPlayer;
            if (player == null || inventory == null || inventory != player.GetInventory())
                return null;

            return inventory.m_inventory
                .Where(item => item != null &&
                    (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo ||
                     item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.AmmoNonEquipable ||
                     item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable) &&
                    item.m_shared.m_ammoType == ammoName &&
                    (matchPrefabName == null || item.m_dropPrefab.name == matchPrefabName) &&
                    CanUse(player, item))
                .OrderBy(item => item.m_gridPos.y * inventory.m_width + item.m_gridPos.x)
                .FirstOrDefault();
        }
    }

    [HarmonyPatch(typeof(BloodCraft), nameof(BloodCraft.TickLocal))]
    internal static class BloodCraftPendingValidationQuarantinePatch
    {
        private static void Postfix()
        {
            BloodCraftGraceGuards.QuarantineEquippedItems();
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
    internal static class BloodCraftEquipGraceGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item, ref bool __result)
        {
            if (BloodCraftGraceGuards.CanUse(__instance, item))
                return true;
            if (__instance is Player player && player == Player.m_localPlayer)
                player.Message(MessageHud.MessageType.Center, "Blood Craft item is waiting for event validation.");
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
    internal static class BloodCraftUseGraceGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item)
        {
            if (BloodCraftGraceGuards.CanUse(__instance, item))
                return true;
            if (__instance is Player player && player == Player.m_localPlayer)
                player.Message(MessageHud.MessageType.Center, "Blood Craft item is waiting for event validation.");
            return false;
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetAmmoItem))]
    internal static class BloodCraftAmmoGraceGuardPatch
    {
        private static void Postfix(Inventory __instance, string ammoName, string matchPrefabName, ref ItemDrop.ItemData __result)
        {
            if (__result == null || !BloodCraft.HasMarker(__result) ||
                Player.m_localPlayer != null && BloodCraft.IsValidFor(Player.m_localPlayer, __result))
                return;
            __result = BloodCraftGraceGuards.FindUsableAmmo(__instance, ammoName, matchPrefabName);
        }
    }
}
