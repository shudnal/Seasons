using HarmonyLib;

namespace Seasons.BloodMoon
{
    [HarmonyPatch(typeof(BloodMoonController), nameof(BloodMoonController.OnDefeatedReport))]
    internal static class BloodMoonDefeatReportGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BloodMoonController __instance)
        {
            return __instance?.State != null && __instance.State.IsCombatLive;
        }
    }
}
