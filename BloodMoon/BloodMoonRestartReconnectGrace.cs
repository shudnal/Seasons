using HarmonyLib;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
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

    internal static class BloodMoonRound8ServerOwnedDeathRetention
    {
        private static readonly FieldInfo PendingField = AccessTools.Field(typeof(BloodMoonServerOwnedDeathRetention), "pending");
        private static readonly MethodInfo SendMethod = AccessTools.Method(typeof(BloodMoonServerOwnedDeathRetention), "Send");

        internal static void RetryPersonalTerminalTransactions()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || PendingField == null || SendMethod == null ||
                PendingField.GetValue(null) is not IDictionary pending || pending.Count == 0)
                return;

            BloodMoonController controller = BloodMoonController.Instance;
            BloodMoonEventState state = controller?.State;
            if (state == null || !BloodMoonRound5Runtime.IsPreOutcomeDrainOpen(state))
                return;

            try
            {
                foreach (object item in pending.Values.Cast<object>().ToArray())
                {
                    if (item == null)
                        continue;
                    Type type = item.GetType();
                    long worldUid = (long)(AccessTools.Field(type, "WorldUid")?.GetValue(item) ?? 0L);
                    long retainedEventId = (long)(AccessTools.Field(type, "EventId")?.GetValue(item) ?? -1L);
                    long playerId = (long)(AccessTools.Field(type, "PlayerId")?.GetValue(item) ?? 0L);
                    if (worldUid != state.WorldUid || retainedEventId != state.EventId || playerId == 0L ||
                        !state.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant) || !participant.IsTerminal ||
                        participant.ExitReason != BloodMoonParticipantExitReason.Defeated && participant.ExitReason != BloodMoonParticipantExitReason.Withdrawn)
                        continue;

                    // The pending entry was created only while this participant was combat-active. A later
                    // personal terminal transition does not turn that already-observed kill into a post-exit hit.
                    SendMethod.Invoke(null, new[] { (object)controller, item });
                }
            }
            catch (Exception ex)
            {
                LogWarning($"[BloodMoon.Durability] Could not retry a terminal server-owned death retention: {ex.Message}");
            }
        }
    }

    internal static class BloodMoonRound8RestartReadiness
    {
        private sealed class RestagedParticipant
        {
            internal BloodMoonParticipantPhase Phase;
            internal double FightingAt;
        }

        private static readonly Dictionary<long, RestagedParticipant> restaged = new Dictionary<long, RestagedParticipant>();
        private static long worldUid;
        private static long eventId = -1L;

        internal static void StageRecoveredCombatParticipants(BloodMoonEventState state)
        {
            Reset();
            if (state == null || !state.IsCombatLive)
                return;

            worldUid = state.WorldUid;
            eventId = state.EventId;
            bool changed = false;
            foreach (BloodMoonParticipantState participant in state.Participants.Values)
            {
                if (participant == null || !participant.IsCombatActive)
                    continue;
                restaged[participant.PlayerId] = new RestagedParticipant
                {
                    Phase = participant.Phase,
                    FightingAt = participant.FightingAt
                };
                participant.Phase = BloodMoonParticipantPhase.Marked;
                changed = true;
            }

            if (!changed)
                return;

            state.UpdatedAt = SeasonState.IsActive ? seasonState.GetTotalSeconds() : state.UpdatedAt;
            state.Revision++;
            BloodMoonPersistence.Save(state);
            LogInfo($"[BloodMoon][event:{eventId}][recovery] Re-staged {restaged.Count} persisted combat participant(s) as Marked until prepare/ready convergence.");
        }

        internal static void RestoreReadyPhaseBeforeSave(BloodMoonEventState state)
        {
            if (state == null || state.WorldUid != worldUid || state.EventId != eventId || restaged.Count == 0)
                return;

            foreach (KeyValuePair<long, RestagedParticipant> entry in restaged.ToArray())
            {
                if (!state.Participants.TryGetValue(entry.Key, out BloodMoonParticipantState participant) || participant == null)
                    continue;
                if (participant.Phase != BloodMoonParticipantPhase.Fighting)
                    continue;

                participant.FightingAt = entry.Value.FightingAt;
                if (entry.Value.Phase == BloodMoonParticipantPhase.GoalReached && participant.GoalReached)
                    participant.Phase = BloodMoonParticipantPhase.GoalReached;
            }
        }

        internal static void ObserveSave(BloodMoonEventState state, bool succeeded)
        {
            if (!succeeded || state == null || state.WorldUid != worldUid || state.EventId != eventId || restaged.Count == 0)
                return;

            foreach (long playerId in restaged.Keys.ToArray())
            {
                if (!state.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant) || participant == null ||
                    participant.IsTerminal || participant.Phase == BloodMoonParticipantPhase.Fighting || participant.Phase == BloodMoonParticipantPhase.GoalReached)
                    restaged.Remove(playerId);
            }
            if (restaged.Count == 0)
                Reset();
        }

        internal static void Reset()
        {
            restaged.Clear();
            worldUid = 0L;
            eventId = -1L;
        }
    }

    internal static class BloodMoonRound8RecoveryRealtime
    {
        private static long worldUid;
        private static long playerId;
        private static float lastRealtime = -1f;

        internal static void ReplaceTickDelta(ref float dt)
        {
            long currentWorldUid = ZNet.m_world?.m_uid ?? 0L;
            long currentPlayerId = Player.m_localPlayer?.GetPlayerID() ?? 0L;
            float now = Time.realtimeSinceStartup;
            if (currentWorldUid != 0L && currentPlayerId != 0L && currentWorldUid == worldUid && currentPlayerId == playerId && lastRealtime >= 0f)
                dt = Mathf.Max(0f, now - lastRealtime);

            worldUid = currentWorldUid;
            playerId = currentPlayerId;
            lastRealtime = now;
        }

        internal static void Reset()
        {
            worldUid = 0L;
            playerId = 0L;
            lastRealtime = -1f;
        }
    }

    internal static class BloodMoonRound8RecoveryScope
    {
        private const string LegacyKey = "Seasons.BloodMoon.Recovery";
        private const string ScopedPrefix = "Seasons.BloodMoon.Recovery.";
        private static long preparedPlayerId;
        private static string preparedScopedKey;

        internal static void BeforeEnsureLoaded(Player player)
        {
            preparedPlayerId = player?.GetPlayerID() ?? 0L;
            preparedScopedKey = null;
            long currentWorldUid = ZNet.m_world?.m_uid ?? 0L;
            if (player == null || currentWorldUid == 0L)
                return;

            if (player.m_customData.TryGetValue(LegacyKey, out string activeJson) && TryReadScope(activeJson, out long storedWorldUid, out long storedEventId, out _))
            {
                string storedKey = MakeKey(storedWorldUid, storedEventId);
                player.m_customData[storedKey] = activeJson;
                if (storedWorldUid == currentWorldUid)
                    preparedScopedKey = storedKey;
                else
                    player.m_customData.Remove(LegacyKey);
            }

            if (player.m_customData.ContainsKey(LegacyKey))
                return;

            KeyValuePair<string, string>? newest = null;
            long newestTicks = long.MinValue;
            string prefix = ScopedPrefix + currentWorldUid.ToString(CultureInfo.InvariantCulture) + ".";
            foreach (KeyValuePair<string, string> entry in player.m_customData.Where(item => item.Key.StartsWith(prefix, StringComparison.Ordinal)))
            {
                if (!TryReadScope(entry.Value, out long storedWorldUid, out _, out long savedTicks) || storedWorldUid != currentWorldUid || savedTicks <= newestTicks)
                    continue;
                newest = entry;
                newestTicks = savedTicks;
            }

            if (!newest.HasValue)
                return;
            player.m_customData[LegacyKey] = newest.Value.Value;
            preparedScopedKey = newest.Value.Key;
        }

        internal static void AfterEnsureLoaded(Player player)
        {
            if (player == null || preparedPlayerId == 0L || player.GetPlayerID() != preparedPlayerId)
            {
                ClearPrepared();
                return;
            }

            if (player.m_customData.TryGetValue(LegacyKey, out string activeJson) && TryReadScope(activeJson, out long world, out long evt, out _))
                player.m_customData[MakeKey(world, evt)] = activeJson;
            else if (!string.IsNullOrEmpty(preparedScopedKey))
                player.m_customData.Remove(preparedScopedKey);
            ClearPrepared();
        }

        internal static void AfterPersist(Player player)
        {
            if (player == null || !player.m_customData.TryGetValue(LegacyKey, out string json) ||
                !TryReadScope(json, out long worldUid, out long eventId, out _))
                return;
            player.m_customData[MakeKey(worldUid, eventId)] = json;
        }

        internal static void AfterEndRecovery(Player player)
        {
            long currentWorldUid = ZNet.m_world?.m_uid ?? 0L;
            if (player == null || currentWorldUid == 0L)
                return;
            string prefix = ScopedPrefix + currentWorldUid.ToString(CultureInfo.InvariantCulture) + ".";
            foreach (string key in player.m_customData.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
                player.m_customData.Remove(key);
        }

        internal static void ResetRuntime()
        {
            ClearPrepared();
        }

        private static bool TryReadScope(string json, out long worldUid, out long eventId, out long savedTicks)
        {
            worldUid = 0L;
            eventId = -1L;
            savedTicks = 0L;
            if (string.IsNullOrWhiteSpace(json))
                return false;
            try
            {
                JObject record = JObject.Parse(json);
                worldUid = record.Value<long?>("WorldUid") ?? 0L;
                eventId = record.Value<long?>("EventId") ?? -1L;
                savedTicks = record.Value<long?>("SavedUtcTicks") ?? 0L;
                return worldUid != 0L && eventId >= 0L;
            }
            catch
            {
                return false;
            }
        }

        private static string MakeKey(long worldUid, long eventId)
        {
            return ScopedPrefix + worldUid.ToString(CultureInfo.InvariantCulture) + "." + eventId.ToString(CultureInfo.InvariantCulture);
        }

        private static void ClearPrepared()
        {
            preparedPlayerId = 0L;
            preparedScopedKey = null;
        }
    }

    internal static class BloodMoonRound8CrossedEnrollmentSettlement
    {
        private const float MinimumSettleSeconds = 1f;
        private const float MaximumSettleSeconds = 5f;
        private static long worldUid;
        private static long eventId = -1L;
        private static float minimumUntil;
        private static float maximumUntil;

        internal static void ObserveSelectedSchedule(BloodMoonScheduleSnapshot schedule, double now)
        {
            if (schedule == null || schedule.MorningAt > now || !BloodMoonObservedServerClock.IsObservedCrossing(schedule, now))
                return;
            BloodMoonEventState state = BloodMoonController.Instance?.State;
            if (state == null || state.WorldUid == 0L)
                return;

            worldUid = state.WorldUid;
            eventId = schedule.EventWorldDay;
            minimumUntil = Time.realtimeSinceStartup + MinimumSettleSeconds;
            maximumUntil = Time.realtimeSinceStartup + MaximumSettleSeconds;
        }

        internal static bool ShouldDelayResolution(BloodMoonController controller, string reason, double now)
        {
            BloodMoonEventState state = controller?.State;
            if (state == null || state.WorldUid != worldUid || state.EventId != eventId || eventId < 0L)
                return false;
            if (!string.Equals(reason, "schedule forced end", StringComparison.Ordinal) || state.Phase != BloodMoonEventPhase.AutoCompleting ||
                state.Schedule == null || now < state.Schedule.MorningAt)
            {
                Reset();
                return false;
            }

            float realtime = Time.realtimeSinceStartup;
            List<BloodMoonParticipantState> connectedMarked = state.Participants.Values
                .Where(participant => participant != null && participant.Phase == BloodMoonParticipantPhase.Marked && IsConnected(controller, participant.PlayerId))
                .ToList();

            if (realtime < minimumUntil || connectedMarked.Count > 0 && realtime < maximumUntil)
                return true;

            // A compatible peer that still has not ACKed by the bounded deadline must not make resolution
            // publish a zero-progress Marked outcome. Promote only inside this resolution call; BeginResolution
            // immediately changes the event to Resolving before anything can publish an exposed Fighting state,
            // and CompleteAutomaticProgressAtForcedEnd then grants the normal Survived-until-dawn display result.
            foreach (BloodMoonParticipantState participant in connectedMarked)
            {
                participant.Phase = participant.GoalReached ? BloodMoonParticipantPhase.GoalReached : BloodMoonParticipantPhase.Fighting;
                if (participant.FightingAt <= 0d)
                    participant.FightingAt = now;
            }
            if (connectedMarked.Count > 0)
                LogWarning($"[BloodMoon][event:{eventId}][recovery] Crossed-event readiness settle reached {MaximumSettleSeconds:0}s; completing {connectedMarked.Count} still-connected staged participant(s) inside the resolution transition.");

            Reset();
            return false;
        }

        internal static void Reset()
        {
            worldUid = 0L;
            eventId = -1L;
            minimumUntil = 0f;
            maximumUntil = 0f;
        }

        private static bool IsConnected(BloodMoonController controller, long playerId)
        {
            if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId)
                return true;
            return controller != null && controller.GetPeerForPlayer(playerId) != 0L;
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

    [HarmonyPatch(typeof(BloodMoonRestartReconnectGrace), nameof(BloodMoonRestartReconnectGrace.Begin))]
    internal static class BloodMoonRound8RestartReadinessStagePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(BloodMoonEventState state)
        {
            BloodMoonRound8RestartReadiness.StageRecoveredCombatParticipants(state);
        }
    }

    [HarmonyPatch(typeof(BloodMoonPersistence), nameof(BloodMoonPersistence.Save))]
    internal static class BloodMoonRound8RestartReadinessSavePatch
    {
        [HarmonyPriority(Priority.First + 350)]
        private static void Prefix(BloodMoonEventState state)
        {
            BloodMoonRound8RestartReadiness.RestoreReadyPhaseBeforeSave(state);
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(BloodMoonEventState state, bool __result)
        {
            BloodMoonRound8RestartReadiness.ObserveSave(state, __result);
        }
    }

    [HarmonyPatch(typeof(BloodMoonServerOwnedDeathRetention), nameof(BloodMoonServerOwnedDeathRetention.Tick))]
    internal static class BloodMoonRound8ServerOwnedTerminalRetryPatch
    {
        [HarmonyPriority(Priority.First + 350)]
        private static void Prefix()
        {
            BloodMoonRound8ServerOwnedDeathRetention.RetryPersonalTerminalTransactions();
        }
    }

    [HarmonyPatch(typeof(BloodMoonRecovery), nameof(BloodMoonRecovery.TickClientProtection))]
    internal static class BloodMoonRound8RecoveryRealtimePatch
    {
        [HarmonyPriority(Priority.First + 350)]
        private static void Prefix(ref float dt)
        {
            BloodMoonRound8RecoveryRealtime.ReplaceTickDelta(ref dt);
        }
    }

    [HarmonyPatch(typeof(BloodMoonRecovery), "EnsureLoaded")]
    internal static class BloodMoonRound8RecoveryScopeLoadPatch
    {
        [HarmonyPriority(Priority.First + 350)]
        private static void Prefix(Player player)
        {
            BloodMoonRound8RecoveryScope.BeforeEnsureLoaded(player);
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Player player)
        {
            BloodMoonRound8RecoveryScope.AfterEnsureLoaded(player);
        }
    }

    [HarmonyPatch(typeof(BloodMoonRecovery), "Persist")]
    internal static class BloodMoonRound8RecoveryScopePersistPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Player player)
        {
            BloodMoonRound8RecoveryScope.AfterPersist(player);
        }
    }

    [HarmonyPatch(typeof(BloodMoonRecovery), "EndRecovery")]
    internal static class BloodMoonRound8RecoveryScopeEndPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Player player)
        {
            BloodMoonRound8RecoveryScope.AfterEndRecovery(player);
        }
    }

    [HarmonyPatch(typeof(BloodMoonSchedule), nameof(BloodMoonSchedule.FindCurrentOrNext))]
    internal static class BloodMoonRound8CrossedScheduleSettlementArmPatch
    {
        [HarmonyPriority(Priority.Last - 100)]
        private static void Postfix(double now, BloodMoonScheduleSnapshot __result)
        {
            BloodMoonRound8CrossedEnrollmentSettlement.ObserveSelectedSchedule(__result, now);
        }
    }

    [HarmonyPatch(typeof(BloodMoonController), nameof(BloodMoonController.BeginResolution))]
    internal static class BloodMoonRound8CrossedScheduleSettlementPatch
    {
        [HarmonyPriority(Priority.First + 450)]
        private static bool Prefix(BloodMoonController __instance, string reason, double now)
        {
            return !BloodMoonRound8CrossedEnrollmentSettlement.ShouldDelayResolution(__instance, reason, now);
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
            BloodMoonRound8RestartReadiness.Reset();
            BloodMoonRound8RecoveryRealtime.Reset();
            BloodMoonRound8RecoveryScope.ResetRuntime();
            BloodMoonRound8CrossedEnrollmentSettlement.Reset();
        }
    }
}
