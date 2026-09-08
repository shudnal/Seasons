using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

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

            GetPendingCounts(state, out int pendingServer, out Dictionary<long, int> pendingByGroup);
            int serverBudget = Math.Max(0, BloodMoonConfig.ServerExtraEnemyHardCap.Value - state.ExtraEnemyZdos.Count - pendingServer);

            foreach (BloodMoonSpawnLeaseState lease in state.SpawnLeases.Values)
                lease.Allowance = Math.Max(0, lease.Allowance);

            foreach (BloodMoonGroupState group in state.Groups.Values.OrderBy(group => group.GroupId))
            {
                int activeMembers = group.MemberPlayerIds.Count(id =>
                    state.Participants.TryGetValue(id, out BloodMoonParticipantState participant) && participant.IsCombatActive);
                int groupCap = BloodMoonConfig.GetGroupExtraEnemyCap(activeMembers);
                int groupLive = CountLiveExtrasForGroup(state, group.GroupId);
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
                if (lease.EventId == state.EventId && liveGroups.Contains(lease.GroupId))
                    continue;

                // UpdateServerLeases removes non-relevant group keys later in this invocation. Revoke the
                // already-delivered client lease first, using the same revision and zero allowance. The client
                // treats same-revision renewals as a minimum allowance, so reordering cannot resurrect tokens.
                if (lease.Allowance > 0)
                {
                    lease.Allowance = 0;
                    BloodMoonNetwork.SendSpawnLease(lease.OwnerPeerId, lease);
                }
            }
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(BloodMoonEventState state)
        {
            if (state?.SpawnLeases == null || state.SpawnLeases.Count == 0)
                return;

            // The original implementation still groups accepted extras and pending reports by their
            // immutable spawn-time GroupId. A merge/split can replace that group id while those creatures
            // remain alive. Recompute current topology accounting after the original method and clamp every
            // lease again. Sending the lower allowance with the same revision is order-safe because clients
            // keep the minimum allowance observed for one revision.
            GetPendingCounts(state, out int pendingServer, out Dictionary<long, int> pendingByGroup);
            int serverBudget = Math.Max(0, BloodMoonConfig.ServerExtraEnemyHardCap.Value - state.ExtraEnemyZdos.Count - pendingServer);

            foreach (BloodMoonGroupState group in state.Groups.Values.OrderBy(group => group.GroupId))
            {
                int activeMembers = group.MemberPlayerIds.Count(id =>
                    state.Participants.TryGetValue(id, out BloodMoonParticipantState participant) && participant.IsCombatActive);
                int groupCap = BloodMoonConfig.GetGroupExtraEnemyCap(activeMembers);
                int groupLive = CountLiveExtrasForGroup(state, group.GroupId);
                int groupPending = pendingByGroup.TryGetValue(group.GroupId, out int value) ? value : 0;
                int groupBudget = Math.Max(0, groupCap - groupLive - groupPending);

                foreach (BloodMoonSpawnLeaseState lease in state.SpawnLeases.Values
                    .Where(lease => lease.EventId == state.EventId && lease.GroupId == group.GroupId)
                    .OrderBy(lease => lease.ZoneX)
                    .ThenBy(lease => lease.ZoneY)
                    .ToArray())
                {
                    int previous = Math.Max(0, lease.Allowance);
                    int allowed = Math.Max(0, Math.Min(previous, Math.Min(groupBudget, serverBudget)));
                    lease.Allowance = allowed;
                    groupBudget -= allowed;
                    serverBudget -= allowed;
                    if (allowed < previous)
                        BloodMoonNetwork.SendSpawnLease(lease.OwnerPeerId, lease);
                }
            }

            HashSet<long> liveGroups = new HashSet<long>(state.Groups.Keys);
            foreach (BloodMoonSpawnLeaseState lease in state.SpawnLeases.Values.ToArray())
            {
                if (lease.EventId == state.EventId && liveGroups.Contains(lease.GroupId))
                    continue;
                if (lease.Allowance > 0)
                {
                    lease.Allowance = 0;
                    BloodMoonNetwork.SendSpawnLease(lease.OwnerPeerId, lease);
                }
            }

            // The original method has now sent refreshed relevant leases to their owners. Remove
            // exhausted server reservations so a later tick can issue a new revision when capacity
            // becomes available again. Lowering a cap or merging groups never deletes live extras.
            foreach (string key in state.SpawnLeases
                .Where(pair => pair.Value == null || pair.Value.Allowance <= 0)
                .Select(pair => pair.Key)
                .ToList())
            {
                state.SpawnLeases.Remove(key);
            }
        }

        internal static int CountLiveExtrasForGroup(BloodMoonEventState state, long groupId)
        {
            if (state?.Groups == null || state.ExtraEnemyZdos == null || ZDOMan.instance == null)
                return 0;

            int count = 0;
            foreach (string value in state.ExtraEnemyZdos)
            {
                if (!BloodMoonSpawner.TryParseZdoId(value, out ZDOID id))
                    continue;
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                if (zdo == null || zdo.GetLong(BloodMoonSpawner.EventMarker, -1L) != state.EventId)
                    continue;
                if (ResolveAccountingGroup(state, zdo.GetPosition()) == groupId)
                    count++;
            }
            return count;
        }

        private static void GetPendingCounts(BloodMoonEventState state, out int serverCount, out Dictionary<long, int> byGroup)
        {
            serverCount = 0;
            byGroup = new Dictionary<long, int>();
            if (state == null || PendingReportsField?.GetValue(null) is not IDictionary pending)
                return;

            foreach (DictionaryEntry entry in pending)
            {
                object report = entry.Value;
                if (report == null)
                    continue;
                Type type = report.GetType();
                FieldInfo eventField = AccessTools.Field(type, "EventId");
                FieldInfo zoneXField = AccessTools.Field(type, "ZoneX");
                FieldInfo zoneYField = AccessTools.Field(type, "ZoneY");
                FieldInfo spawnedIdField = AccessTools.Field(type, "SpawnedId");
                if (eventField == null || zoneXField == null || zoneYField == null || (long)eventField.GetValue(report) != state.EventId)
                    continue;

                Vector3 position = ZoneSystem.GetZonePos(new Vector2i((int)zoneXField.GetValue(report), (int)zoneYField.GetValue(report)));
                if (spawnedIdField?.GetValue(report) is ZDOID spawnedId && !spawnedId.IsNone() && ZDOMan.instance != null)
                {
                    ZDO zdo = ZDOMan.instance.GetZDO(spawnedId);
                    if (zdo != null)
                        position = zdo.GetPosition();
                }

                long accountingGroup = ResolveAccountingGroup(state, position);
                serverCount++;
                if (accountingGroup >= 0L)
                    byGroup[accountingGroup] = byGroup.TryGetValue(accountingGroup, out int count) ? count + 1 : 1;
            }
        }

        private static long ResolveAccountingGroup(BloodMoonEventState state, Vector3 position)
        {
            if (state?.Groups == null || state.Groups.Count == 0)
                return -1L;

            return state.Groups.Values
                .OrderBy(group => Utils.DistanceXZ(group.Anchor, position))
                .ThenBy(group => group.GroupId)
                .Select(group => group.GroupId)
                .FirstOrDefault();
        }
    }

    [HarmonyPatch(typeof(BloodMoonGroups), nameof(BloodMoonGroups.Rebuild))]
    internal static class BloodMoonGroupExtraCountTopologyPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(BloodMoonEventState state)
        {
            if (state?.Groups == null)
                return;
            foreach (BloodMoonGroupState group in state.Groups.Values)
                group.ExtraEnemyCount = BloodMoonLeaseBudgetReconciliationPatch.CountLiveExtrasForGroup(state, group.GroupId);
        }
    }
}
