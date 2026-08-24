using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonRejectedSpawnCleanup
    {
        private const double WatchLifetimeSeconds = 30d;

        private sealed class Watch
        {
            internal long EventId;
            internal double ExpiresAt;
        }

        private static readonly Dictionary<ZDOID, Watch> watches = new Dictionary<ZDOID, Watch>();

        internal static void Queue(ZDOID id, long eventId, double now)
        {
            if (id.IsNone() || eventId < 0L)
                return;

            watches[id] = new Watch
            {
                EventId = eventId,
                ExpiresAt = now + WatchLifetimeSeconds
            };
        }

        internal static void Process(double now)
        {
            if (ZDOMan.instance == null || watches.Count == 0)
                return;

            foreach (KeyValuePair<ZDOID, Watch> entry in new List<KeyValuePair<ZDOID, Watch>>(watches))
            {
                ZDO zdo = ZDOMan.instance.GetZDO(entry.Key);
                if (zdo != null)
                {
                    if (zdo.GetLong(BloodMoonSpawner.EventMarker, -1L) == entry.Value.EventId)
                    {
                        LogWarning($"[BloodMoon][event:{entry.Value.EventId}][spawn] Removing rejected marked spawn {entry.Key} after delayed replication.");
                        ZDOMan.instance.DestroyZDO(zdo);
                    }
                    watches.Remove(entry.Key);
                    continue;
                }

                if (now >= entry.Value.ExpiresAt)
                    watches.Remove(entry.Key);
            }
        }

        internal static void Reset()
        {
            watches.Clear();
        }
    }

    [HarmonyPatch(typeof(BloodMoonSpawner), nameof(BloodMoonSpawner.AcceptSpawnReport))]
    internal static class BloodMoonSpawnReportAuthorityGuardPatch
    {
        private static readonly FieldInfo PendingReportsField = AccessTools.Field(typeof(BloodMoonSpawner), "pendingSpawnReports");

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BloodMoonEventState state, long sender, long groupId, int groupRevision, int zoneX, int zoneY,
            int leaseRevision, ZDOID spawnedId, double now)
        {
            if (state == null || spawnedId.IsNone())
                return false;

            long eventId = state.EventId;
            if (eventId < 0L || state.ExtraEnemyZdos.Contains(spawnedId.ToString()))
                return false;

            // The ZDOID user/session component is immutable after creation. Only the peer that created
            // an object may report it, so a rejected report cannot be used to schedule another peer's
            // valid event extra for delayed cleanup.
            if (spawnedId.UserID != sender)
            {
                LogWarning($"[BloodMoon][event:{eventId}][spawn] Rejected report for ZDO {spawnedId} from non-creator peer {sender}.");
                return false;
            }

            if (state.SpawnsStopped || !state.IsCombatLive)
                return Reject(spawnedId, eventId, now);

            string leaseKey = $"{groupId}:{zoneX}:{zoneY}";
            if (!state.SpawnLeases.TryGetValue(leaseKey, out BloodMoonSpawnLeaseState lease))
                return Reject(spawnedId, eventId, now);

            if (lease.OwnerPeerId != sender || lease.OwnerSessionId != sender || lease.EventId != eventId ||
                lease.GroupRevision != groupRevision || lease.LeaseRevision != leaseRevision || lease.ExpiresAt < now || lease.Allowance <= 0)
                return Reject(spawnedId, eventId, now);

            if (!state.Groups.TryGetValue(groupId, out BloodMoonGroupState group) || group.Revision != groupRevision)
                return Reject(spawnedId, eventId, now);

            int hardCap = Math.Max(0, BloodMoonConfig.ServerExtraEnemyHardCap.Value);
            int groupCap = BloodMoonConfig.GetGroupExtraEnemyCap(group.MemberPlayerIds.Count(id =>
                state.Participants.TryGetValue(id, out BloodMoonParticipantState participant) && participant.IsCombatActive));
            GetPendingCounts(eventId, groupId, out int pendingServer, out int pendingGroup);
            int serverExisting = state.ExtraEnemyZdos.Count;
            int groupExisting = 0;
            foreach (string id in state.ExtraEnemyZdos)
            {
                if (BloodMoonSpawner.GetMarkedGroupId(id) == groupId)
                    groupExisting++;
            }

            if (serverExisting + pendingServer >= hardCap || groupExisting + pendingGroup >= groupCap)
            {
                lease.Allowance = Math.Max(0, lease.Allowance - 1);
                BloodMoonPersistence.Save(state);
                return Reject(spawnedId, eventId, now);
            }

            ZDO zdo = ZDOMan.instance?.GetZDO(spawnedId);
            if (zdo == null)
                return true;

            long markedEvent = zdo.GetLong(BloodMoonSpawner.EventMarker, -1L);
            if (markedEvent != eventId)
            {
                LogWarning($"[BloodMoon][event:{eventId}][spawn] Ignoring report for unmarked or foreign ZDO {spawnedId}; marker={markedEvent}.");
                return false;
            }

            if (zdo.GetLong(BloodMoonSpawner.GroupMarker, -1L) != groupId || zdo.GetSector() != new Vector2i(zoneX, zoneY))
                return Reject(spawnedId, eventId, now);

            return true;
        }

        private static bool Reject(ZDOID spawnedId, long eventId, double now)
        {
            BloodMoonRejectedSpawnCleanup.Queue(spawnedId, eventId, now);
            BloodMoonRejectedSpawnCleanup.Process(now);
            return false;
        }

        private static void GetPendingCounts(long eventId, long groupId, out int serverCount, out int groupCount)
        {
            serverCount = 0;
            groupCount = 0;
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
                serverCount++;
                if ((long)groupField.GetValue(report) == groupId)
                    groupCount++;
            }
        }
    }

    [HarmonyPatch(typeof(BloodMoonSpawner), nameof(BloodMoonSpawner.TickClient))]
    internal static class BloodMoonRejectedSpawnCleanupTickPatch
    {
        private static void Postfix()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || !SeasonState.IsActive)
                return;
            BloodMoonRejectedSpawnCleanup.Process(seasonState.GetTotalSeconds());
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    internal static class BloodMoonRejectedSpawnCleanupWorldPatch
    {
        private static void Prefix()
        {
            BloodMoonRejectedSpawnCleanup.Reset();
        }
    }
}
