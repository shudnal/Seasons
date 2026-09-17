using HarmonyLib;
using UnityEngine;

namespace Seasons.BloodMoon
{
    [HarmonyPatch(typeof(Ragdoll), nameof(Ragdoll.Setup))]
    internal static class BloodMoonExtraRagdollLifetimePatch
    {
        private static void Postfix(Ragdoll __instance)
        {
            if (__instance?.m_nview == null || !__instance.m_nview.IsValid())
                return;

            ZDO zdo = __instance.m_nview.GetZDO();
            if (zdo == null || zdo.GetLong(BloodMoonSpawner.EventMarker, -1L) < 0L)
                return;

            __instance.m_ttl = 2f;
            __instance.m_dropItems = false;
            __instance.CancelInvoke(nameof(Ragdoll.DestroyNow));
            __instance.InvokeRepeating(nameof(Ragdoll.DestroyNow), 2f, 1f);
        }
    }
}
