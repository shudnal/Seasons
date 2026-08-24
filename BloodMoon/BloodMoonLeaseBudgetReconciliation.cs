using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Seasons.BloodMoon
{
    [HarmonyPatch(typeof(BloodMoonSpawner), nameof(BloodMoonSpawner.UpdateServerLeases))]
    internal static class BloodMoonLeaseBudgetReconciliationPatch
    {
        private static readonly FieldInfo PendingReportsField = AccessTools.Field(typeof(BloodMoonSpawner), "pendingSpawnReports");

        [HarmonyPriority(Priority.First)]
        private static void Prefix(BloodMoonEventState state)
        {
            if (state == null || state.SpawnLeases == null || state.SpawnLeases.Count == 0)
                return;

            GetPendingCounts(state.EventId, out int pendingServer, out Dictionary<long, int> pendingByGroup);
            int serverBudget = Math.Max(0, BloodMoonConfig.ServerExtraEnemyHardCap.Value - state.ExtraEnemyZdos.Count - pendingServer);

            foreach (BloodMoonSpawnLeaseState lease in state.SpawnLeases.Values)
                lease.Allowance = Math.Max(0, lease.Allowance);

            foreach (BloodMoonGroupState group in state.Groups.Values.OrderBy(group => group.GroupId))
            {
                int activeMembers = group.MemberPlayerIds.Count(id =>
                    state.Participants.TryGetValue(id, out BloodMoonParticipantState participant) && participant.IsCombatActive);
                int groupCap = BloodMoonConfig.GetGroupExtraEnemyCap(activeMembers);
                int groupLive = state.ExtraEnemyZdos.Count(id => BloodMoonSpawner.GetMarkedGroupId(id) == group.GroupId);
                int groupPending = pendingByGroup.TryGetValue(group.GroupId, out int value) ? value : 0;
                int groupBudget = Math.Max(0, groupCap - groupLive - groupPending);

                foreach (BloodMoonSpawnLeaseState lease in state.SpawnLeases.Values
                    .Where(lease => lease.EventId == state.EventId && lease.GroupId == group.GroupId)
                    .OrderBy(lease => lease.ZoneX)
                    .ThenBy(lease => lease.ZoneY))
                {
                    int allowed = Math.Min(lease.Allowance, Math.Min(groupBudget, serverBudget));
                    lease.Allowance = Math.Max(0, allowed);
                    groupBudget -= lease.Allowance;
                    serverBudget -= lease.Allowance;
                }
            }

            HashSet<long> liveGroups = new HashSet<long>(state.Groups.Keys);
            foreach (BloodMoonSpawnLeaseState lease in state.SpawnLeases.Values)
            {
                if (lease.EventId != state.EventId || !liveGroups.Contains(lease.GroupId))
                    lease.Allowance = 0;
            }
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(BloodMoonEventState state)
        {
            if (state?.SpawnLeases == null || state.SpawnLeases.Count == 0)
                return;

            // The original method has now sent refreshed relevant leases to their owners. Remove
            // exhausted server reservations so a later tick can issue a new revision when capacity
            // becomes available again. Lowering a cap never deletes already-live extras.
            foreach (string key in state.SpawnLeases
                .Where(pair => pair.Value == null || pair.Value.Allowance <= 0)
                .Select(pair => pair.Key)
                .ToList())
            {
                state.SpawnLeases.Remove(key);
            }
        }

        private static void GetPendingCounts(long eventId, out int serverCount, out Dictionary<long, int> byGroup)
        {
            serverCount = 0;
            byGroup = new Dictionary<long, int>();
            if (PendingReportsField?.GetValue(null) is not IDictionary pending)
                return;

            foreach (DictionaryEntry entry in pending)
            {
                object report = entry.Value;
                if (report == null)
                    continue;
                Type type = report.GetType();
                FieldInfo eventField = AccessTools.Field(type, "EventId");
                FieldInfo groupField = AccessTools.Field(type, "GroupId");
                if (eventField == null || groupField == null || (long)eventField.GetValue(report) != eventId)
                    continue;

                long groupId = (long)groupField.GetValue(report);
                serverCount++;
                byGroup[groupId] = byGroup.TryGetValue(groupId, out int count) ? count + 1 : 1;
            }
        }
    }
}
