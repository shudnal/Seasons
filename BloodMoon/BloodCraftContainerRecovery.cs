using HarmonyLib;
using System.Linq;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    [HarmonyPatch(typeof(Container), nameof(Container.Load))]
    internal static class BloodCraftContainerRecoveryPatch
    {
        private static void Postfix(Container __instance)
        {
            if (__instance?.m_inventory == null || __instance.m_nview == null || !__instance.m_nview.IsValid() || !__instance.m_nview.IsOwner())
                return;

            var stale = __instance.m_inventory.GetAllItems().Where(BloodCraft.HasMarker).ToList();
            if (stale.Count == 0)
                return;

            bool wasLoading = __instance.m_loading;
            __instance.m_loading = true;
            try
            {
                foreach (ItemDrop.ItemData item in stale)
                    __instance.m_inventory.RemoveItem(item);
            }
            finally
            {
                __instance.m_loading = wasLoading;
            }

            __instance.Save();
            LogWarning($"[BloodMoon.Craft] Removed {stale.Count} stale temporary item stack(s) from container {__instance.GetComponent<ZNetView>()?.GetZDO()?.m_uid}.");
        }
    }
}
