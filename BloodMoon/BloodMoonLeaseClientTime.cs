using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonLeaseClientTime
    {
        private static readonly FieldInfo ClientLeasesField = AccessTools.Field(typeof(BloodMoonSpawner), "clientLeases");
        private static readonly Dictionary<string, float> deadlines = new Dictionary<string, float>();

        internal static void Rebase(BloodMoonSpawnLeaseState lease)
        {
            if (lease == null || ZNet.instance == null || ZNet.instance.IsServer())
                return;

            double advertisedLifetime = Math.Max(2d, BloodMoonConfig.SpawnLeaseSeconds != null ? BloodMoonConfig.SpawnLeaseSeconds.Value : 8f);
            double serverNow = BloodMoonNetwork.ClientGlobal.ServerTime;
            double remaining = advertisedLifetime;

            if (serverNow > 0d)
            {
                remaining = lease.ExpiresAt - serverNow;
                if (double.IsNaN(remaining) || double.IsInfinity(remaining))
                    remaining = advertisedLifetime;
                else
                    remaining = Math.Min(advertisedLifetime, Math.Max(0d, remaining));
            }

            deadlines[MakeDeadlineKey(lease)] = Time.realtimeSinceStartup + (float)remaining;

            // Client expiry is maintained in the monotonic realtimeSinceStartup domain below.
            // Keep the serialized SeasonState-domain value from participating in TickClient's legacy check.
            lease.ExpiresAt = double.MaxValue;
        }

        internal static void PrepareTick()
        {
            if (ZNet.instance == null || ZNet.instance.IsServer())
                return;

            IDictionary leases = ClientLeasesField?.GetValue(null) as IDictionary;
            if (leases == null || leases.Count == 0)
            {
                deadlines.Clear();
                return;
            }

            float now = Time.realtimeSinceStartup;
            float fallbackLifetime = Mathf.Max(2f, BloodMoonConfig.SpawnLeaseSeconds != null ? BloodMoonConfig.SpawnLeaseSeconds.Value : 8f);
            List<object> expiredLeaseKeys = new List<object>();
            HashSet<string> activeDeadlineKeys = new HashSet<string>();

            foreach (DictionaryEntry entry in leases)
            {
                if (entry.Value is not BloodMoonSpawnLeaseState lease)
                    continue;

                string deadlineKey = MakeDeadlineKey(lease);
                activeDeadlineKeys.Add(deadlineKey);

                if (!deadlines.TryGetValue(deadlineKey, out float deadline))
                {
                    deadline = now + fallbackLifetime;
                    deadlines[deadlineKey] = deadline;
                }

                if (now >= deadline)
                {
                    expiredLeaseKeys.Add(entry.Key);
                    deadlines.Remove(deadlineKey);
                    continue;
                }

                lease.ExpiresAt = double.MaxValue;
            }

            foreach (object key in expiredLeaseKeys)
                leases.Remove(key);

            foreach (string deadlineKey in new List<string>(deadlines.Keys))
            {
                if (!activeDeadlineKeys.Contains(deadlineKey))
                    deadlines.Remove(deadlineKey);
            }
        }

        internal static void Reset()
        {
            deadlines.Clear();
        }

        private static string MakeDeadlineKey(BloodMoonSpawnLeaseState lease)
        {
            return lease == null
                ? string.Empty
                : $"{lease.EventId}:{lease.GroupId}:{lease.ZoneX}:{lease.ZoneY}:{lease.OwnerSessionId}:{lease.LeaseRevision}";
        }
    }

    /// <summary>
    /// Keeps already-delivered allowance reserved while a topology-obsolete lease is being revoked.
    /// Each reserved allowance unit is represented by a synthetic pending-spawn token, so the existing
    /// production budget calculation naturally counts it against both server and current-topology group caps.
    /// A replicated extra whose report RPC was lost can consume exactly one matching current or retired token.
    /// </summary>
    internal static class BloodMoonRetiredLeaseReservations
    {
        private const long SyntheticReservationUser = -9223372036854770000L;
        private static readonly FieldInfo PendingReportsField = AccessTools.Field(typeof(BloodMoonSpawner), "pendingSpawnReports");
        private static readonly Dictionary<ZDOID, Reservation> reservations = new Dictionary<ZDOID, Reservation>();
        private static uint nextSyntheticId = 1u;

        private sealed class Reservation
        {
            internal long EventId;
            internal long OriginalGroupId;
            internal int ZoneX;
            internal int ZoneY;
            internal long OwnerPeerId;
            internal int PoolRevision;
            internal float ExpiresAt;
        }

        internal static void RetireObsolete(BloodMoonEventState state)
        {
            if (state?.SpawnLeases == null || state.SpawnLeases.Count == 0)
                return;

            IDictionary pending = PendingReportsField?.GetValue(null) as IDictionary;
            if (pending == null)
                return;

            Prune(pending);
            foreach (BloodMoonSpawnLeaseState lease in state.SpawnLeases.Values.ToArray())
            {
                if (lease == null || lease.Allowance <= 0)
                    continue;

                bool current = lease.EventId == state.EventId &&
                    state.Groups.TryGetValue(lease.GroupId, out BloodMoonGroupState group) &&
                    group.Revision == lease.GroupRevision;
                if (current)
                    continue;

                int deliveredAllowance = Math.Max(0, lease.Allowance);
                for (int i = 0; i < deliveredAllowance; ++i)
                    AddReservationToken(pending, state, lease);

                // Revoke the exact already-delivered revision before any later clamp can erase the
                // evidence that the client may still hold positive allowance for it.
                lease.Allowance = 0;
                BloodMoonNetwork.SendSpawnLease(lease.OwnerPeerId, lease);
            }
        }

        internal static void DiscoverReplicatedExtras(BloodMoonEventState state)
        {
            if (state == null || state.EventId < 0L || state.ExtraEnemyZdos == null || ZDOMan.instance == null)
                return;

            IDictionary pending = PendingReportsField?.GetValue(null) as IDictionary;
            if (pending == null)
                return;
            Prune(pending);

            string expectedPrefabName = BloodMoonSpawner.GetFrozenSpawnPrefabName(state);
            int poolIdentity = BloodMoonSpawner.GetSpawnPoolIdentity(state);
            if (string.IsNullOrEmpty(expectedPrefabName) || poolIdentity == 0)
                return;

            bool changed = false;
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.ToArray())
            {
                if (zdo == null || zdo.GetLong(BloodMoonSpawner.EventMarker, -1L) != state.EventId ||
                    !BloodMoonSpawnReportValidation.IsAllowedExtraEnemyZdo(zdo, state.EventId, expectedPrefabName))
                    continue;

                string idValue = zdo.m_uid.ToString();
                if (state.ExtraEnemyZdos.Contains(idValue))
                    continue;

                long groupId = zdo.GetLong(BloodMoonSpawner.GroupMarker, -1L);
                int zoneX = zdo.GetInt(BloodMoonSpawner.SpawnZoneXMarker, int.MinValue);
                int zoneY = zdo.GetInt(BloodMoonSpawner.SpawnZoneYMarker, int.MinValue);
                if (groupId < 0L || zoneX == int.MinValue || zoneY == int.MinValue)
                    continue;

                long creator = zdo.m_uid.UserID;
                if (!TryConsumeRetired(pending, state.EventId, groupId, zoneX, zoneY, creator, poolIdentity) &&
                    !TryConsumeCurrent(state, groupId, zoneX, zoneY, creator, poolIdentity))
                    continue;

                state.ExtraEnemyZdos.Add(idValue);
                changed = true;
                Seasons.LogInfo($"[BloodMoon][event:{state.EventId}][spawn] Recovered replicated marked extra {zdo.m_uid} by consuming a preserved lease token.");
            }

            if (changed)
                BloodMoonPersistence.Save(state);
        }

        internal static void Clear()
        {
            IDictionary pending = PendingReportsField?.GetValue(null) as IDictionary;
            if (pending != null)
            {
                foreach (ZDOID id in reservations.Keys.ToArray())
                    pending.Remove(id);
            }
            reservations.Clear();
        }

        private static void AddReservationToken(IDictionary pending, BloodMoonEventState state, BloodMoonSpawnLeaseState lease)
        {
            Type pendingType = pending.GetType().GetGenericArguments().ElementAtOrDefault(1);
            if (pendingType == null)
                return;

            object report = Activator.CreateInstance(pendingType, nonPublic: true);
            if (report == null)
                return;

            Vector3 zonePosition = ZoneSystem.GetZonePos(new Vector2i(lease.ZoneX, lease.ZoneY));
            long accountingGroup = ResolveCurrentGroup(state, zonePosition);
            if (accountingGroup < 0L)
                accountingGroup = lease.GroupId;

            ZDOID synthetic = new ZDOID(SyntheticReservationUser, nextSyntheticId++);
            float lifetime = Mathf.Max(2f, BloodMoonConfig.SpawnLeaseSeconds != null ? BloodMoonConfig.SpawnLeaseSeconds.Value : 8f);
            float expiresAt = Time.realtimeSinceStartup + lifetime;

            AccessTools.Field(pendingType, "EventId")?.SetValue(report, lease.EventId);
            AccessTools.Field(pendingType, "GroupId")?.SetValue(report, accountingGroup);
            AccessTools.Field(pendingType, "ZoneX")?.SetValue(report, lease.ZoneX);
            AccessTools.Field(pendingType, "ZoneY")?.SetValue(report, lease.ZoneY);
            AccessTools.Field(pendingType, "SpawnedId")?.SetValue(report, synthetic);
            AccessTools.Field(pendingType, "ExpiresAt")?.SetValue(report, (double)expiresAt);
            pending[synthetic] = report;

            reservations[synthetic] = new Reservation
            {
                EventId = lease.EventId,
                OriginalGroupId = lease.GroupId,
                ZoneX = lease.ZoneX,
                ZoneY = lease.ZoneY,
                OwnerPeerId = lease.OwnerPeerId,
                PoolRevision = lease.PoolRevision,
                ExpiresAt = expiresAt
            };
        }

        private static bool TryConsumeRetired(IDictionary pending, long eventId, long groupId, int zoneX, int zoneY, long creator, int poolIdentity)
        {
            KeyValuePair<ZDOID, Reservation>? match = reservations
                .Where(entry => entry.Value.EventId == eventId && entry.Value.OriginalGroupId == groupId &&
                    entry.Value.ZoneX == zoneX && entry.Value.ZoneY == zoneY && entry.Value.OwnerPeerId == creator &&
                    entry.Value.PoolRevision == poolIdentity && Time.realtimeSinceStartup < entry.Value.ExpiresAt)
                .OrderBy(entry => entry.Value.ExpiresAt)
                .Cast<KeyValuePair<ZDOID, Reservation>?>()
                .FirstOrDefault();

            if (!match.HasValue)
                return false;
            pending.Remove(match.Value.Key);
            reservations.Remove(match.Value.Key);
            return true;
        }

        private static bool TryConsumeCurrent(BloodMoonEventState state, long groupId, int zoneX, int zoneY, long creator, int poolIdentity)
        {
            string key = $"{groupId}:{zoneX}:{zoneY}";
            if (!state.SpawnLeases.TryGetValue(key, out BloodMoonSpawnLeaseState lease) || lease == null ||
                lease.EventId != state.EventId || lease.GroupId != groupId || lease.ZoneX != zoneX || lease.ZoneY != zoneY ||
                lease.OwnerPeerId != creator || lease.PoolRevision != poolIdentity || lease.Allowance <= 0)
                return false;

            if (!state.Groups.TryGetValue(groupId, out BloodMoonGroupState group) || group.Revision != lease.GroupRevision)
                return false;

            lease.Allowance--;
            return true;
        }

        private static long ResolveCurrentGroup(BloodMoonEventState state, Vector3 position)
        {
            if (state?.Groups == null || state.Groups.Count == 0)
                return -1L;
            return state.Groups.Values
                .OrderBy(group => Utils.DistanceXZ(group.Anchor, position))
                .ThenBy(group => group.GroupId)
                .Select(group => group.GroupId)
                .FirstOrDefault();
        }

        private static void Prune(IDictionary pending)
        {
            float now = Time.realtimeSinceStartup;
            foreach (KeyValuePair<ZDOID, Reservation> entry in reservations.ToArray())
            {
                if (now < entry.Value.ExpiresAt && pending.Contains(entry.Key))
                    continue;
                pending.Remove(entry.Key);
                reservations.Remove(entry.Key);
            }
        }
    }

    // Spawn leases are authored in the server SeasonState time domain. On receipt, preserve only
    // the bounded remaining lifetime and move client expiry into Unity's monotonic realtime clock.
    [HarmonyPatch(typeof(BloodMoonSpawner), nameof(BloodMoonSpawner.ReceiveLease))]
    internal static class BloodMoonLeaseClientTimePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(BloodMoonSpawnLeaseState lease)
        {
            BloodMoonLeaseClientTime.Rebase(lease);
        }
    }

    [HarmonyPatch(typeof(BloodMoonSpawner), nameof(BloodMoonSpawner.TickClient))]
    internal static class BloodMoonLeaseClientExpiryPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            BloodMoonLeaseClientTime.PrepareTick();
        }
    }

    [HarmonyPatch(typeof(BloodMoonSpawner), nameof(BloodMoonSpawner.ResetClientState))]
    internal static class BloodMoonLeaseClientResetPatch
    {
        private static void Postfix()
        {
            BloodMoonLeaseClientTime.Reset();
        }
    }

    [HarmonyPatch(typeof(BloodMoonSpawner), nameof(BloodMoonSpawner.UpdateServerLeases))]
    internal static class BloodMoonLeaseRetirementPatch
    {
        [HarmonyPriority(Priority.First + 200)]
        private static void Prefix(BloodMoonEventState state)
        {
            BloodMoonRetiredLeaseReservations.RetireObsolete(state);
        }
    }

    [HarmonyPatch(typeof(BloodMoonLeaseBudgetReconciliationPatch), "DiscoverReplicatedExtras")]
    internal static class BloodMoonLeaseDiscoveryProvenancePatch
    {
        [HarmonyPriority(Priority.First + 200)]
        private static bool Prefix(BloodMoonEventState state)
        {
            BloodMoonRetiredLeaseReservations.DiscoverReplicatedExtras(state);
            return false;
        }
    }

    [HarmonyPatch(typeof(BloodMoonSpawner), nameof(BloodMoonSpawner.StopServerLeases))]
    internal static class BloodMoonRetiredLeaseStopPatch
    {
        private static void Postfix() => BloodMoonRetiredLeaseReservations.Clear();
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    internal static class BloodMoonRetiredLeaseWorldPatch
    {
        private static void Prefix() => BloodMoonRetiredLeaseReservations.Clear();
    }
}
