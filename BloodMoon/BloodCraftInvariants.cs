using HarmonyLib;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodCraftInvariants
    {
        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.DropItem), new System.Type[]
        {
            typeof(ItemDrop.ItemData), typeof(int), typeof(Vector3), typeof(Quaternion)
        })]
        private static class ItemDropDropItemPatch
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(ItemDrop.ItemData item, ref ItemDrop __result)
            {
                if (!BloodCraft.HasMarker(item))
                    return true;

                __result = null;
                LogWarning("[BloodMoon.Craft] Blocked creation of a temporary Blood Craft world item.");
                return false;
            }
        }

        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.Awake))]
        private static class ItemDropAwakePatch
        {
            private static void Postfix(ItemDrop __instance)
            {
                if (__instance == null || !BloodCraft.HasMarker(__instance.m_itemData))
                    return;
                ZNetView nview = __instance.m_nview != null ? __instance.m_nview : __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid() || !nview.IsOwner())
                    return;
                LogWarning($"[BloodMoon.Craft] Removing temporary world item found during Awake: {__instance.gameObject.name}.");
                ZNetScene.instance?.Destroy(__instance.gameObject);
            }
        }
    }
}
