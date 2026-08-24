using HarmonyLib;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonRandEventSuppression
    {
        private static bool ownsSuppression;

        internal static bool IsSuppressed => BloodMoonInteractionRules.IsEventMarkedOrLater;

        internal static void StopActiveRandomEvent()
        {
            if (RandEventSystem.instance == null || ZNet.instance == null || !ZNet.instance.IsServer())
                return;
            ownsSuppression = true;
            if (RandEventSystem.instance.m_randomEvent != null)
            {
                LogInfo($"[BloodMoon.RandEvent] Stopping random event '{RandEventSystem.instance.m_randomEvent.m_name}'.");
                RandEventSystem.instance.ResetRandomEvent();
            }
        }

        internal static void Release()
        {
            ownsSuppression = false;
        }

        internal static bool ShouldBlockRandomEventStart()
        {
            return IsSuppressed || ownsSuppression;
        }
    }

    [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.UpdateRandomEvent))]
    internal static class BloodMoonRandEventUpdatePatch
    {
        private static bool Prefix(RandEventSystem __instance)
        {
            if (!BloodMoonRandEventSuppression.ShouldBlockRandomEventStart())
                return true;
            if (ZNet.instance != null && ZNet.instance.IsServer() && __instance.m_randomEvent != null)
                __instance.ResetRandomEvent();
            return false;
        }
    }

    [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.StartRandomEvent))]
    internal static class BloodMoonRandEventStartPatch
    {
        private static bool Prefix() => !BloodMoonRandEventSuppression.ShouldBlockRandomEventStart();
    }
}
