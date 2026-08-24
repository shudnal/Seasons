using HarmonyLib;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    [HarmonyPatch(typeof(BloodMoonController), "AdvanceToFrozenMorning")]
    internal static class BloodMoonCalendarTimeGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BloodMoonController __instance)
        {
            if (ZNet.m_world == null || !SeasonState.seasonWorldSettings.HasWorldSettings(ZNet.m_world))
                return true;

            BloodMoonEventState state = __instance?.State;
            if (state != null)
                LogInfo($"[BloodMoon][event:{state.EventId}][resolution] Real-time calendar mode keeps Valheim net time unchanged at resolution.");
            return false;
        }
    }
}
