using HarmonyLib;
using System;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonPreCombatCancellation
    {
        private const string FeatureDisabledReason = "feature disabled";

        [HarmonyPatch(typeof(BloodMoonController), nameof(BloodMoonController.BeginResolution))]
        private static class BeginResolutionPatch
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(BloodMoonController __instance, string reason)
            {
                BloodMoonEventState state = __instance?.State;
                if (state == null || !string.Equals(reason, FeatureDisabledReason, StringComparison.Ordinal))
                    return;
                if (state.Phase != BloodMoonEventPhase.Forewarning && state.Phase != BloodMoonEventPhase.Marked)
                    return;

                state.ResolutionCancelledBeforeCombat = true;
                Seasons.LogInfo($"[BloodMoon][event:{state.EventId}][resolution] Feature disabled before combat; cleanup will not advance time or publish outcomes.");
            }
        }

        [HarmonyPatch(typeof(BloodMoonController), "AdvanceToFrozenMorning")]
        private static class AdvanceTimePatch
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(BloodMoonController __instance)
            {
                return __instance?.State?.ResolutionCancelledBeforeCombat != true;
            }
        }

        [HarmonyPatch(typeof(BloodMoonController), "PublishOutcomes")]
        private static class PublishOutcomesPatch
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(BloodMoonController __instance)
            {
                return __instance?.State?.ResolutionCancelledBeforeCombat != true;
            }
        }
    }
}
