using HarmonyLib;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonRestartReconnectGrace
    {
        private const double GraceSeconds = 30d;
        private static long eventId = -1L;
        private static double until;

        internal static void Begin(BloodMoonEventState state)
        {
            if (state == null || !SeasonState.IsActive ||
                state.Phase != BloodMoonEventPhase.Marked && !state.IsCombatLive)
            {
                Reset();
                return;
            }

            eventId = state.EventId;
            until = seasonState.GetTotalSeconds() + GraceSeconds;
            LogInfo($"[BloodMoon][event:{eventId}][recovery] Allowing {GraceSeconds:0}s for persisted participants to reconnect before Disconnected becomes terminal.");
        }

        internal static bool ShouldDefer(long currentEventId, BloodMoonParticipantExitReason reason, double now)
        {
            return reason == BloodMoonParticipantExitReason.Disconnected && currentEventId == eventId && now < until;
        }

        internal static void Reset()
        {
            eventId = -1L;
            until = 0d;
        }
    }

    [HarmonyPatch(typeof(BloodMoonController), "RecoverServerState")]
    internal static class BloodMoonRestartReconnectGraceRecoveryPatch
    {
        private static void Postfix(BloodMoonController __instance)
        {
            BloodMoonRestartReconnectGrace.Begin(__instance?.State);
        }
    }

    [HarmonyPatch(typeof(BloodMoonController), "ExitParticipant")]
    internal static class BloodMoonRestartReconnectGraceExitPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BloodMoonController __instance, BloodMoonParticipantExitReason reason, double now)
        {
            long eventId = __instance?.State?.EventId ?? -1L;
            return !BloodMoonRestartReconnectGrace.ShouldDefer(eventId, reason, now);
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    internal static class BloodMoonRestartReconnectGraceCleanupPatch
    {
        private static void Prefix()
        {
            BloodMoonRestartReconnectGrace.Reset();
        }
    }
}
