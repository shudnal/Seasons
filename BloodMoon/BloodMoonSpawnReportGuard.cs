using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonSpawnReportValidation
    {
        private static readonly FieldInfo PendingReportsField = AccessTools.Field(typeof(BloodMoonSpawner), "pendingSpawnReports");

        internal static IDictionary GetPendingReports()
        {
            return PendingReportsField?.GetValue(null) as IDictionary;
        }

        internal static bool TryReadPendingReport(object report, out long eventId, out long groupId, out ZDOID spawnedId)
        {
            eventId = -1L;
            groupId = -1L;
            spawnedId = ZDOID.None;
            if (report == null)
                return false;

            Type type = report.GetType();
            FieldInfo eventField = AccessTools.Field(type, "EventId");
            FieldInfo groupField = AccessTools.Field(type, "GroupId");
            FieldInfo spawnedField = AccessTools.Field(type, "SpawnedId");
            if (eventField == null || groupField == null || spawnedField == null)
                return false;

            eventId = (long)eventField.GetValue(report);
            groupId = (long)groupField.GetValue(report);
            spawnedId = (ZDOID)spawnedField.GetValue(report);
            return true;
        }

        internal static bool IsAllowedExtraEnemyZdo(ZDO zdo, long eventId)
        {
            if (zdo == null || eventId < 0L || zdo.GetLong(BloodMoonSpawner.EventMarker, -1L) != eventId || ZNetScene.instance == null)
                return false;

            string configuredPrefab = BloodMoonConfig.TestEnemyPrefab.Value?.Trim();
            if (string.IsNullOrEmpty(configuredPrefab))
                return false;

            GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            return prefab != null && prefab.GetComponent<MonsterAI>() != null &&
                string.Equals(prefab.name, configuredPrefab, StringComparison.Ordinal);
        }
    }

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
                    if (BloodMoonSpawnReportValidation.IsAllowedExtraEnemyZdo(zdo, entry.Value.EventId))
                    {
                        LogWarning($"[BloodMoon][event:{entry.Value.EventId}][spawn] Removing rejected marked spawn {entry.Key} after delayed replication.");
                        ZDOMan.instance.DestroyZDO(zdo);
                    }
                    else if (zdo.GetLong(BloodMoonSpawner.EventMarker, -1L) == entry.Value.EventId)
                    {
                        LogWarning($"[BloodMoon][event:{entry.Value.EventId}][spawn] Refusing cleanup for rejected ZDO {entry.Key}: prefab is not an allowed Blood Moon extra enemy.");
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

            ZDO zdo = ZDOMan.instance?.GetZDO(spawnedId);
            if (zdo != null)
            {
                long markedEvent = zdo.GetLong(BloodMoonSpawner.EventMarker, -1L);
                if (markedEvent != eventId)
                {
                    LogWarning($"[BloodMoon][event:{eventId}][spawn] Ignoring report for unmarked or foreign ZDO {spawnedId}; marker={markedEvent}.");
                    return false;
                }

                if (zdo.GetLong(BloodMoonSpawner.GroupMarker, -1L) != groupId || zdo.GetSector() != new Vector2i(zoneX, zoneY))
                    return Reject(spawnedId, eventId, now);

                if (!BloodMoonSpawnReportValidation.IsAllowedExtraEnemyZdo(zdo, eventId))
                {
                    LogWarning($"[BloodMoon][event:{eventId}][spawn] Rejected ZDO {spawnedId}: prefab is not the configured Blood Moon extra enemy.");
                    return false;
                }
            }

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
            IDictionary pending = BloodMoonSpawnReportValidation.GetPendingReports();
            if (pending == null)
                return;

            foreach (DictionaryEntry entry in pending)
            {
                if (!BloodMoonSpawnReportValidation.TryReadPendingReport(entry.Value, out long pendingEvent, out long pendingGroup, out _) || pendingEvent != eventId)
                    continue;
                serverCount++;
                if (pendingGroup == groupId)
                    groupCount++;
            }
        }
    }

    [HarmonyPatch(typeof(BloodMoonSpawner), "ValidateSpawnedZdo")]
    internal static class BloodMoonDeferredSpawnValidationPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ZDO zdo, ref bool __result)
        {
            if (!__result || zdo == null)
                return;

            long eventId = zdo.GetLong(BloodMoonSpawner.EventMarker, -1L);
            __result = BloodMoonSpawnReportValidation.IsAllowedExtraEnemyZdo(zdo, eventId);
            if (!__result)
                LogWarning($"[BloodMoon][event:{eventId}][spawn] Deferred report rejected for ZDO {zdo.m_uid}: prefab is not the configured Blood Moon extra enemy.");
        }
    }

    [HarmonyPatch(typeof(BloodMoonSpawner), "DestroyZdo")]
    internal static class BloodMoonSpawnDestroyGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ZDOID id)
        {
            ZDO zdo = ZDOMan.instance?.GetZDO(id);
            if (zdo == null)
                return true;

            long eventId = zdo.GetLong(BloodMoonSpawner.EventMarker, -1L);
            if (BloodMoonSpawnReportValidation.IsAllowedExtraEnemyZdo(zdo, eventId))
                return true;

            if (eventId >= 0L)
                LogWarning($"[BloodMoon][event:{eventId}][spawn] Refusing to destroy marked ZDO {id}: prefab is not an allowed Blood Moon extra enemy.");
            return false;
        }
    }

    [HarmonyPatch(typeof(BloodMoonSpawner), nameof(BloodMoonSpawner.CleanupExtraEnemies))]
    internal static class BloodMoonSpawnCleanupAuthorityPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BloodMoonEventState state)
        {
            if (state == null || ZDOMan.instance == null)
                return false;

            double now = SeasonState.IsActive ? seasonState.GetTotalSeconds() : 0d;
            foreach (string idValue in new List<string>(state.ExtraEnemyZdos))
            {
                if (!BloodMoonSpawner.TryParseZdoId(idValue, out ZDOID id))
                    continue;
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                if (BloodMoonSpawnReportValidation.IsAllowedExtraEnemyZdo(zdo, state.EventId))
                    ZDOMan.instance.DestroyZDO(zdo);
                else if (zdo != null)
                    LogWarning($"[BloodMoon][event:{state.EventId}][spawn] Skipping cleanup for authoritative id {id}: prefab validation failed.");
            }

            IDictionary pending = BloodMoonSpawnReportValidation.GetPendingReports();
            if (pending != null)
            {
                foreach (DictionaryEntry entry in pending)
                {
                    if (!BloodMoonSpawnReportValidation.TryReadPendingReport(entry.Value, out long pendingEvent, out _, out ZDOID pendingId) ||
                        pendingEvent != state.EventId || pendingId.IsNone())
                        continue;
                    BloodMoonRejectedSpawnCleanup.Queue(pendingId, pendingEvent, now);
                }
                pending.Clear();
            }

            BloodMoonRejectedSpawnCleanup.Process(now);
            state.ExtraEnemyZdos.Clear();
            BloodMoonSpawner.StopServerLeases(state);
            return false;
        }
    }

    [HarmonyPatch(typeof(BloodMoonSpawner), nameof(BloodMoonSpawner.Recover))]
    internal static class BloodMoonSpawnRecoveryValidationPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(BloodMoonEventState state)
        {
            if (state == null || ZDOMan.instance == null || state.ExtraEnemyZdos.Count == 0)
                return;

            bool changed = false;
            foreach (string idValue in new List<string>(state.ExtraEnemyZdos))
            {
                if (!BloodMoonSpawner.TryParseZdoId(idValue, out ZDOID id) ||
                    !BloodMoonSpawnReportValidation.IsAllowedExtraEnemyZdo(ZDOMan.instance.GetZDO(id), state.EventId))
                {
                    state.ExtraEnemyZdos.Remove(idValue);
                    changed = true;
                }
            }

            if (changed)
                BloodMoonPersistence.Save(state);
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
