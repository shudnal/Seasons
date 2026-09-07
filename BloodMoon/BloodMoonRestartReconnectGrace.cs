using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonRestartReconnectGrace
    {
        private const float GraceSeconds = 30f;
        private static readonly HashSet<long> awaitingReconnect = new HashSet<long>();
        private static long eventId = -1L;
        private static float until;

        internal static void Begin(BloodMoonEventState state)
        {
            Reset();
            if (state == null || !SeasonState.IsActive ||
                state.Phase != BloodMoonEventPhase.Marked && !state.IsCombatLive)
                return;

            eventId = state.EventId;
            // This is a networking grace period, not a calendar deadline. skiptime and
            // wall-clock corrections must neither expire it early nor extend it indefinitely.
            until = Time.realtimeSinceStartup + GraceSeconds;
            foreach (BloodMoonParticipantState participant in state.Participants.Values)
            {
                if (participant.Phase == BloodMoonParticipantPhase.Marked || participant.IsCombatActive)
                    awaitingReconnect.Add(participant.PlayerId);
            }

            if (awaitingReconnect.Count > 0)
                LogInfo($"[BloodMoon][event:{eventId}][recovery] Allowing {GraceSeconds:0}s for {awaitingReconnect.Count} persisted participant(s) to reconnect before Disconnected becomes terminal.");
        }

        internal static void ObserveConnected(BloodMoonController controller, BloodMoonEventState state, double now)
        {
            if (controller == null || state == null || state.EventId != eventId || awaitingReconnect.Count == 0)
                return;

            if (Time.realtimeSinceStartup >= until)
            {
                awaitingReconnect.Clear();
                return;
            }

            foreach (long playerId in awaitingReconnect.ToArray())
            {
                if (!controller.TryGetConnectedPosition(playerId, out _))
                    continue;
                awaitingReconnect.Remove(playerId);
                LogInfo($"[BloodMoon][event:{eventId}][player:{playerId}][recovery] Persisted participant reconnected; restart disconnect grace released for this player.");
            }
        }

        internal static bool ShouldDefer(long currentEventId, long playerId, BloodMoonParticipantExitReason reason, double now)
        {
            if (reason != BloodMoonParticipantExitReason.Disconnected || currentEventId != eventId || Time.realtimeSinceStartup >= until)
                return false;
            return awaitingReconnect.Contains(playerId);
        }

        internal static void Reset()
        {
            eventId = -1L;
            until = 0f;
            awaitingReconnect.Clear();
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

    [HarmonyPatch(typeof(BloodMoonController), "UpdateConnectedParticipants")]
    internal static class BloodMoonRestartReconnectGraceObservePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(BloodMoonController __instance, double now)
        {
            BloodMoonRestartReconnectGrace.ObserveConnected(__instance, __instance?.State, now);
        }
    }

    [HarmonyPatch(typeof(BloodMoonController), "ExitParticipant")]
    internal static class BloodMoonRestartReconnectGraceExitPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BloodMoonController __instance, BloodMoonParticipantState participant, BloodMoonParticipantExitReason reason, double now)
        {
            long currentEventId = __instance?.State?.EventId ?? -1L;
            long playerId = participant?.PlayerId ?? 0L;
            return !BloodMoonRestartReconnectGrace.ShouldDefer(currentEventId, playerId, reason, now);
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
