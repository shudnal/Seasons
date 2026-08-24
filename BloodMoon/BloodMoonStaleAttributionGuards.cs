using HarmonyLib;
using UnityEngine;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonStaleAttribution
    {
        private const string EventMarker = "Seasons.BloodMoon.ProjectileEventId";
        private const string SourceTypeMarker = "Seasons.BloodMoon.ProjectileSourceType";

        internal static bool IsStale(Object source, ZNetView nview)
        {
            if (BloodMoonInteractionRules.IsEventCombatLive)
                return false;

            if (source != null && BloodMoonHitAttribution.TryGet(source, nview, out _))
                return true;

            if (nview == null || !nview.IsValid())
                return false;

            ZDO zdo = nview.GetZDO();
            if (zdo == null)
                return false;

            long eventId = zdo.GetLong(EventMarker, -1L);
            int sourceType = zdo.GetInt(SourceTypeMarker, 0);
            return eventId >= 0L && sourceType != (int)BloodMoonCombatSourceType.None;
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    internal static class BloodMoonStaleProjectileGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Projectile __instance)
        {
            return !BloodMoonStaleAttribution.IsStale(__instance, __instance?.m_nview);
        }
    }

    [HarmonyPatch(typeof(Aoe), nameof(Aoe.ShouldHit))]
    internal static class BloodMoonStaleAoeShouldHitGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Aoe __instance, ref bool __result)
        {
            if (!BloodMoonStaleAttribution.IsStale(__instance, __instance?.m_nview))
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Aoe), nameof(Aoe.OnHit))]
    internal static class BloodMoonStaleAoeOnHitGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Aoe __instance)
        {
            return !BloodMoonStaleAttribution.IsStale(__instance, __instance?.m_nview);
        }
    }
}
