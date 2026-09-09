using HarmonyLib;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Globalization;
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

    internal static class BloodMoonRound5DrainTimeout
    {
        private const float MaximumHoldSeconds = 35f;
        private static long eventId = -1L;
        private static float startedAt;
        private static bool warned;

        internal static void Bound(BloodMoonController controller, ref bool hold)
        {
            if (!hold)
            {
                eventId = -1L;
                startedAt = 0f;
                warned = false;
                return;
            }

            long currentEvent = controller?.State?.EventId ?? -1L;
            if (currentEvent < 0L)
                return;
            if (eventId != currentEvent)
            {
                eventId = currentEvent;
                startedAt = Time.realtimeSinceStartup;
                warned = false;
                return;
            }

            if (Time.realtimeSinceStartup - startedAt < MaximumHoldSeconds)
                return;
            hold = false;
            if (!warned)
            {
                warned = true;
                LogWarning($"[BloodMoon][event:{currentEvent}][resolution] Durable report drain reached its {MaximumHoldSeconds:0}s bound; continuing resolution.");
            }
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

    [HarmonyPatch(typeof(BloodMoonOutcomeDrainGate), nameof(BloodMoonOutcomeDrainGate.ShouldHold))]
    internal static class BloodMoonOutcomeDrainTimeoutPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(BloodMoonController controller, ref bool __result)
        {
            BloodMoonRound5DrainTimeout.Bound(controller, ref __result);
        }
    }

    [HarmonyPatch(typeof(BloodMoonPresentation), nameof(BloodMoonPresentation.OnResolutionComplete))]
    internal static class BloodMoonCompletedTerminalMarkerCleanupPatch
    {
        private const string PendingPrefix = "Seasons.BloodMoon.PendingTerminal.";
        private const string LegacyDefeatedKey = "Seasons.BloodMoon.Defeated";

        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            Player player = Player.m_localPlayer;
            long worldUid = BloodMoonRound5Runtime.CurrentWorldUid;
            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            if (player == null || worldUid == 0L || eventId < 0L || player.GetPlayerID() == 0L)
                return;

            bool changed = player.m_customData.Remove(PendingPrefix + worldUid.ToString(CultureInfo.InvariantCulture) + "." +
                eventId.ToString(CultureInfo.InvariantCulture) + "." + player.GetPlayerID().ToString(CultureInfo.InvariantCulture));

            if (player.m_customData.TryGetValue(LegacyDefeatedKey, out string json) && !string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    JObject source = JObject.Parse(json);
                    if (source.Value<long?>("WorldUid") == worldUid && source.Value<long?>("EventId") == eventId)
                        changed |= player.m_customData.Remove(LegacyDefeatedKey);
                }
                catch
                {
                    // Existing recovery code owns validation/removal of malformed legacy records.
                }
            }

            if (changed)
                BloodMoonRound5Runtime.SaveProfile(player, "completed Blood Moon terminal markers");
        }
    }

    // Profile-backed death replay normally lives on the peer that owned the dying enemy, which is not
    // necessarily the credited participant. During the pre-outcome Resolving drain accept that retained
    // replay from any still-ready ordinary event peer (or the listen-host server peer). Terminal RPCs keep
    // their stricter player-peer binding; this relaxation is specific to enemy-owner death evidence.
    [HarmonyPatch(typeof(BloodMoonEnemyDeathReports), nameof(BloodMoonEnemyDeathReports.TryValidate))]
    internal static class BloodMoonEarlyResolvingOwnerReplayPatch
    {
        [HarmonyPriority(Priority.First + 400)]
        private static bool Prefix(long sender, long eventId, long creditedPlayerId, ZDOID enemyId, ref float serverPoints, ref bool __result)
        {
            BloodMoonController controller = BloodMoonController.Instance;
            BloodMoonEventState state = controller?.State;
            if (state == null || state.EventId != eventId || state.IsCombatLive || !BloodMoonRound5Runtime.IsPreOutcomeDrainOpen(state) ||
                !state.Participants.TryGetValue(creditedPlayerId, out BloodMoonParticipantState participant) || !participant.IsCombatActive ||
                !IsReadyEventSender(sender) ||
                !BloodMoonEnemyDeathDurability.TryGetCurrentProfileReplay(eventId, creditedPlayerId, enemyId, out float replayPoints))
                return true;

            serverPoints = replayPoints;
            __result = replayPoints > 0f && !float.IsNaN(replayPoints) && !float.IsInfinity(replayPoints);
            return false;
        }

        private static bool IsReadyEventSender(long sender)
        {
            if (ZRoutedRpc.instance == null || sender == 0L)
                return false;
            if (sender == ZRoutedRpc.instance.GetServerPeerID())
                return true;
            ZNetPeer peer = ZNet.instance?.GetPeer(sender);
            return peer != null && peer.IsReady();
        }
    }
}