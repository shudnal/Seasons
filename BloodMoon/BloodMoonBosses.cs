using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static partial class BloodMoonBosses
    {
        internal static readonly int ParkedEventMarker = "Seasons.BloodMoon.ParkedEventId".GetStableHashCode();
        internal static readonly int ParkingSchemaMarker = "Seasons.BloodMoon.ParkingSchema".GetStableHashCode();
        internal static readonly int OriginalPositionMarker = "Seasons.BloodMoon.OriginalPosition".GetStableHashCode();
        internal static readonly int OriginalRotationMarker = "Seasons.BloodMoon.OriginalRotation".GetStableHashCode();
        internal static readonly int OriginalPrefabHashMarker = "Seasons.BloodMoon.OriginalPrefabHash".GetStableHashCode();
        internal static readonly int ParkingTimestampMarker = "Seasons.BloodMoon.ParkingTimestamp".GetStableHashCode();

        private const int LegacyParkingSchema = 1;
        private const int ParkingSchema = 2;
        private const float DiscoveryReportInterval = 1.5f;
        private const float DiscoveryMaxDistance = 160f;
        private const float EncounterWithdrawDistance = 120f;
        private const float ParkingPositionTolerance = 2f;

        private enum ParkingLocation
        {
            ExpectedSlot,
            BeyondWorldEdge,
            InsideWorld,
            Unknown
        }

        private sealed class ParkingRecord
        {
            internal long EventId;
            internal Vector3 OriginalPosition;
            internal Quaternion OriginalRotation;
            internal int OriginalPrefabHash;
            internal long ParkingTimestamp;
        }

        private static readonly Dictionary<ZDOID, double> pendingUntil = new Dictionary<ZDOID, double>();
        private static readonly Dictionary<ZDOID, ParkingRecord> pendingRecords = new Dictionary<ZDOID, ParkingRecord>();
        private static readonly Dictionary<ZDOID, float> nextClientDiscoveryAt = new Dictionary<ZDOID, float>();
        private static readonly Dictionary<ZDOID, string> invalidRecordErrors = new Dictionary<ZDOID, string>();
        private static long clientDiscoveryEventId = -1L;
        private static double nextMarkerScan;

        internal static void Tick(BloodMoonEventState state, double now)
        {
            if (state == null || ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null)
                return;

            if (!state.IsCombatLive || now < nextMarkerScan)
                return;
            nextMarkerScan = now + 2d;

            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values
                .Where(zdo => zdo.GetLong(ParkedEventMarker, -1L) == state.EventId)
                .ToArray())
            {
                if (!TryReadParkingRecord(zdo, out ParkingRecord record, out string error))
                {
                    LogInvalidRecordOnce(zdo, error);
                    continue;
                }

                invalidRecordErrors.Remove(zdo.m_uid);
                if (ClassifyCurrentPosition(zdo) == ParkingLocation.ExpectedSlot)
                    continue;

                Vector3 parked = GetParkingPosition(zdo.m_uid);
                pendingRecords[zdo.m_uid] = record;
                pendingUntil[zdo.m_uid] = now + 5d;
                Reassert(zdo, parked);
                SynchronizeLoadedInstance(zdo, parked, zdo.GetRotation());
            }
        }

        internal static void ReportLoadedBoss(Character boss)
        {
            Player player = Player.m_localPlayer;
            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            if (boss == null || player == null || eventId < 0L || !BloodMoonInteractionRules.IsEventCombatLive ||
                !BloodMoonInteractionRules.IsActiveParticipant(player) || !boss.IsBoss() || boss.IsDead() ||
                boss.m_nview == null || !boss.m_nview.IsValid())
                return;

            if (clientDiscoveryEventId != eventId)
            {
                clientDiscoveryEventId = eventId;
                nextClientDiscoveryAt.Clear();
            }

            if (Utils.DistanceXZ(player.transform.position, boss.transform.position) > DiscoveryMaxDistance ||
                !SameNavigationContext(player.transform.position, boss.transform.position))
                return;

            ZDO zdo = boss.m_nview.GetZDO();
            if (zdo == null || zdo.m_uid.IsNone())
                return;

            float now = Time.realtimeSinceStartup;
            if (nextClientDiscoveryAt.TryGetValue(zdo.m_uid, out float next) && now < next)
                return;
            nextClientDiscoveryAt[zdo.m_uid] = now + DiscoveryReportInterval;

            BloodMoonNetwork.SendBossDiscovery(eventId, player.GetPlayerID(), zdo.m_uid, boss.InInterior(), zdo.GetPrefab());
        }

        internal static void AcceptDiscovery(long sender, long eventId, long playerId, ZDOID bossId, bool observedInterior, int observedPrefabHash)
        {
            BloodMoonController controller = BloodMoonController.Instance;
            BloodMoonEventState state = controller?.State;
            if (state == null || !state.IsCombatLive || state.EventId != eventId || ZDOMan.instance == null || bossId.IsNone())
                return;
            if (!TryValidateDiscoverySender(state, sender, playerId, out Vector3 reporterPosition))
                return;

            ZDO zdo = ZDOMan.instance.GetZDO(bossId);
            if (!IsAliveBossZdo(zdo) || zdo.GetPrefab() != observedPrefabHash)
                return;

            long parkedEvent = zdo.GetLong(ParkedEventMarker, -1L);
            if (parkedEvent >= 0L)
            {
                if (parkedEvent == state.EventId)
                {
                    if (TryReadParkingRecord(zdo, out ParkingRecord record, out string error))
                    {
                        Vector3 parked = GetParkingPosition(zdo.m_uid);
                        pendingRecords[zdo.m_uid] = record;
                        pendingUntil[zdo.m_uid] = seasonState.GetTotalSeconds() + 5d;
                        Reassert(zdo, parked);
                        SynchronizeLoadedInstance(zdo, parked, zdo.GetRotation());
                    }
                    else
                    {
                        LogInvalidRecordOnce(zdo, error);
                    }
                }
                return;
            }

            Vector3 bossPosition = zdo.GetPosition();
            if (Utils.DistanceXZ(reporterPosition, bossPosition) > DiscoveryMaxDistance ||
                !SameNavigationContext(reporterPosition, bossPosition))
                return;

            bool interior = Character.InInterior(bossPosition);
            if (interior != observedInterior)
                LogWarning($"[BloodMoon][event:{eventId}][boss:{bossId}] discovery interior mismatch client={observedInterior} server={interior}; server ZDO position wins.");

            if (interior || !zdo.Persistent)
            {
                WithdrawAffectedPlayers(state, bossPosition, interior ? "interior boss encounter" : "nonpersistent boss encounter");
                return;
            }

            Park(state, zdo, seasonState.GetTotalSeconds());
        }

        internal static void ResetClientState()
        {
            nextClientDiscoveryAt.Clear();
            clientDiscoveryEventId = -1L;
        }

        internal static void ResetRuntimeState()
        {
            pendingUntil.Clear();
            pendingRecords.Clear();
            invalidRecordErrors.Clear();
            nextMarkerScan = 0d;
            ResetClientState();
        }
    }
}
