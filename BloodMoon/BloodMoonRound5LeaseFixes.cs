using HarmonyLib;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonRound5LeaseFixes
    {
        private static readonly FieldInfo PendingReportsField = AccessTools.Field(typeof(BloodMoonSpawner), "pendingSpawnReports");
        private static readonly MethodInfo AddReservationTokenMethod = AccessTools.Method(typeof(BloodMoonRetiredLeaseReservations), "AddReservationToken");
        private static readonly MethodInfo TryConsumeRetiredMethod = AccessTools.Method(typeof(BloodMoonRetiredLeaseReservations), "TryConsumeRetired");
        private static readonly MethodInfo TryConsumeCurrentMethod = AccessTools.Method(typeof(BloodMoonRetiredLeaseReservations), "TryConsumeCurrent");
        private static readonly MethodInfo ValidateSpawnSystemOwnershipMethod = AccessTools.Method(typeof(BloodMoonZoneOwnership), "ValidateSpawnSystemOwnership");

        internal static void RetireOwnershipMigrations(BloodMoonEventState state)
        {
            if (state?.SpawnLeases == null || state.SpawnLeases.Count == 0 || AddReservationTokenMethod == null ||
                ValidateSpawnSystemOwnershipMethod == null)
                return;

            IDictionary pending = PendingReportsField?.GetValue(null) as IDictionary;
            if (pending == null)
                return;

            foreach (BloodMoonSpawnLeaseState lease in state.SpawnLeases.Values.ToArray())
            {
                if (lease == null || lease.Allowance <= 0 || lease.EventId != state.EventId ||
                    !state.Groups.TryGetValue(lease.GroupId, out BloodMoonGroupState group) || group.Revision != lease.GroupRevision)
                    continue;

                bool stillOwnsZone;
                try
                {
                    stillOwnsZone = (bool)ValidateSpawnSystemOwnershipMethod.Invoke(null,
                        new object[] { lease.OwnerPeerId, new Vector2i(lease.ZoneX, lease.ZoneY) });
                }
                catch (Exception ex)
                {
                    LogWarning($"[BloodMoon.Spawn] Could not verify lease owner for zone {lease.ZoneX},{lease.ZoneY}: {ex.Message}");
                    continue;
                }

                if (stillOwnsZone)
                    continue;

                int deliveredAllowance = Math.Max(0, lease.Allowance);
                for (int i = 0; i < deliveredAllowance; ++i)
                    AddReservationTokenMethod.Invoke(null, new object[] { pending, state, lease });

                // The group/revision is still current, but the zone owner is not. Retain the already-delivered
                // currency as reservation tokens, then revoke this exact lease revision before the ordinary
                // lease pass can reclaim its capacity for the replacement owner.
                lease.Allowance = 0;
                BloodMoonNetwork.SendSpawnLease(lease.OwnerPeerId, lease);
                LogInfo($"[BloodMoon][event:{state.EventId}][zone:{lease.ZoneX},{lease.ZoneY}][lease:{lease.LeaseRevision}] " +
                    $"Retired {deliveredAllowance} delivered token(s) after zone ownership migrated from peer {lease.OwnerPeerId}.");
            }
        }

        internal static void DiscoverReplicatedExtras(BloodMoonEventState state)
        {
            if (state == null || state.EventId < 0L || state.ExtraEnemyZdos == null || ZDOMan.instance == null ||
                TryConsumeRetiredMethod == null || TryConsumeCurrentMethod == null)
                return;

            IDictionary pending = PendingReportsField?.GetValue(null) as IDictionary;
            if (pending == null)
                return;

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

                // A normal report has already spent one lease token before entering pendingSpawnReports.
                // Discovery must wait for that report's metadata-validation path instead of spending a
                // second current/retired token for the same ZDO.
                if (pending.Contains(zdo.m_uid))
                    continue;

                long groupId = zdo.GetLong(BloodMoonSpawner.GroupMarker, -1L);
                int zoneX = zdo.GetInt(BloodMoonSpawner.SpawnZoneXMarker, int.MinValue);
                int zoneY = zdo.GetInt(BloodMoonSpawner.SpawnZoneYMarker, int.MinValue);
                if (groupId < 0L || zoneX == int.MinValue || zoneY == int.MinValue)
                    continue;

                long creator = zdo.m_uid.UserID;
                bool consumed;
                try
                {
                    consumed = (bool)TryConsumeRetiredMethod.Invoke(null,
                        new object[] { pending, state.EventId, groupId, zoneX, zoneY, creator, poolIdentity });
                    if (!consumed)
                    {
                        consumed = (bool)TryConsumeCurrentMethod.Invoke(null,
                            new object[] { state, groupId, zoneX, zoneY, creator, poolIdentity });
                    }
                }
                catch (Exception ex)
                {
                    LogWarning($"[BloodMoon.Spawn] Could not validate replicated extra {zdo.m_uid} against lease provenance: {ex.Message}");
                    continue;
                }

                if (!consumed)
                    continue;

                state.ExtraEnemyZdos.Add(idValue);
                changed = true;
                LogInfo($"[BloodMoon][event:{state.EventId}][spawn] Recovered replicated marked extra {zdo.m_uid} by consuming one matching lease token.");
            }

            if (changed)
                BloodMoonPersistence.Save(state);
        }
    }

    [HarmonyPatch(typeof(BloodMoonRetiredLeaseReservations), nameof(BloodMoonRetiredLeaseReservations.RetireObsolete))]
    internal static class BloodMoonZoneOwnerLeaseRetirementPatch
    {
        [HarmonyPriority(Priority.First + 300)]
        private static void Prefix(BloodMoonEventState state)
        {
            BloodMoonRound5LeaseFixes.RetireOwnershipMigrations(state);
        }
    }

    [HarmonyPatch(typeof(BloodMoonRetiredLeaseReservations), nameof(BloodMoonRetiredLeaseReservations.DiscoverReplicatedExtras))]
    internal static class BloodMoonPendingSpawnDiscoveryTokenPatch
    {
        [HarmonyPriority(Priority.First + 300)]
        private static bool Prefix(BloodMoonEventState state)
        {
            BloodMoonRound5LeaseFixes.DiscoverReplicatedExtras(state);
            return false;
        }
    }
}