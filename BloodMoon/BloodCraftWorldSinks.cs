using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Seasons.BloodMoon
{
    internal static class BloodCraftWorldSinks
    {
        private static bool RejectTemporary(Humanoid user, ItemDrop.ItemData item, ref bool result)
        {
            if (!BloodCraft.HasMarker(item))
                return true;

            if (user is Player player)
                player.Message(MessageHud.MessageType.Center, "Blood Craft items cannot be placed into world objects.");
            result = false;
            return false;
        }

        private static ItemDrop.ItemData FindPermanent(Inventory inventory, System.Func<ItemDrop.ItemData, bool> predicate)
        {
            return inventory?.m_inventory.FirstOrDefault(item => item != null && !BloodCraft.HasMarker(item) && predicate(item));
        }

        [HarmonyPatch]
        private static class DirectWorldConsumerPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                MethodBase[] methods =
                {
                    AccessTools.Method(typeof(ItemStand), nameof(ItemStand.UseItem), new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) }),
                    AccessTools.Method(typeof(ArmorStand), nameof(ArmorStand.UseItem), new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) }),
                    AccessTools.Method(typeof(Fermenter), nameof(Fermenter.UseItem), new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) }),
                    AccessTools.Method(typeof(Fermenter), nameof(Fermenter.AddItem), new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) }),
                    AccessTools.Method(typeof(CookingStation), nameof(CookingStation.UseItem), new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) }),
                    AccessTools.Method(typeof(CookingStation), nameof(CookingStation.OnUseItem), new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) }),
                    AccessTools.Method(typeof(CookingStation), nameof(CookingStation.CookItem), new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) }),
                    AccessTools.Method(typeof(CookingStation), nameof(CookingStation.OnAddFoodSwitch), new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) }),
                    AccessTools.Method(typeof(CookingStation), nameof(CookingStation.OnAddFuelSwitch), new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) }),
                    AccessTools.Method(typeof(Smelter), nameof(Smelter.OnAddOre), new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) }),
                    AccessTools.Method(typeof(Smelter), nameof(Smelter.OnAddFuel), new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) }),
                    AccessTools.Method(typeof(Turret), nameof(Turret.UseItem), new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) }),
                    AccessTools.Method(typeof(Catapult), nameof(Catapult.OnLoadPointUse), new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData) })
                };

                return methods.Where(method => method != null);
            }

            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Humanoid user, ItemDrop.ItemData item, ref bool __result)
            {
                return RejectTemporary(user, item, ref __result);
            }
        }

        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.DropItem), new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(Vector3), typeof(Quaternion) })]
        private static class WorldDropPatch
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(ItemDrop.ItemData item, ref ItemDrop __result)
            {
                if (!BloodCraft.HasMarker(item))
                    return true;

                __result = null;
                return false;
            }
        }

        [HarmonyPatch(typeof(Fermenter), nameof(Fermenter.FindCookableItem))]
        private static class FermenterFindCookableItemPatch
        {
            private static void Postfix(Fermenter __instance, Inventory inventory, ref ItemDrop.ItemData __result)
            {
                if (BloodCraft.HasMarker(__result))
                    __result = FindPermanent(inventory, __instance.IsItemAllowed);
            }
        }

        [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.FindCookableItem))]
        private static class CookingStationFindCookableItemPatch
        {
            private static void Postfix(CookingStation __instance, Inventory inventory, ref ItemDrop.ItemData __result)
            {
                if (BloodCraft.HasMarker(__result))
                    __result = FindPermanent(inventory, __instance.IsItemAllowed);
            }
        }

        [HarmonyPatch(typeof(Smelter), nameof(Smelter.FindCookableItem))]
        private static class SmelterFindCookableItemPatch
        {
            private static void Postfix(Smelter __instance, Inventory inventory, ref ItemDrop.ItemData __result)
            {
                if (BloodCraft.HasMarker(__result))
                    __result = FindPermanent(inventory, __instance.IsItemAllowed);
            }
        }

        [HarmonyPatch(typeof(Turret), nameof(Turret.FindAmmoItem))]
        private static class TurretFindAmmoItemPatch
        {
            private static void Postfix(Turret __instance, Inventory inventory, bool onlyCurrentlyLoadableType, ref ItemDrop.ItemData __result)
            {
                if (!BloodCraft.HasMarker(__result))
                    return;

                string currentAmmo = onlyCurrentlyLoadableType && __instance.HasAmmo() ? __instance.GetAmmoType() : string.Empty;
                __result = FindPermanent(inventory, item =>
                    item.m_dropPrefab != null && __instance.IsItemAllowed(item.m_dropPrefab.name) &&
                    (string.IsNullOrEmpty(currentAmmo) || item.m_dropPrefab.name == currentAmmo));
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.GetSellableItem))]
        private static class StoreGuiSellableItemPatch
        {
            private static void Postfix(StoreGui __instance, ref ItemDrop.ItemData __result)
            {
                if (!BloodCraft.HasMarker(__result))
                    return;

                __result = __instance.m_tempItems.FirstOrDefault(item =>
                    item != null && !BloodCraft.HasMarker(item) && item.m_shared.m_name != __instance.m_coinPrefab.m_itemData.m_shared.m_name);
            }
        }
    }
}
