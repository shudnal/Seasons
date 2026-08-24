using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonBosses
    {
        internal static readonly int ParkedEventMarker = "Seasons.BloodMoon.ParkedEventId".GetStableHashCode();
        internal static readonly int ParkingSchemaMarker = "Seasons.BloodMoon.ParkingSchema".GetStableHashCode();
        internal static readonly int OriginalPositionMarker = "Seasons.BloodMoon.OriginalPosition".GetStableHashCode();
        internal static readonly int OriginalPrefabHashMarker = "Seasons.BloodMoon.OriginalPrefabHash".GetStableHashCode();
        internal static readonly int ParkingTimestampMarker = "Seasons.BloodMoon.ParkingTimestamp".GetStableHashCode();

        private const int ParkingSchema = 1;
        private const float DiscoveryReportInterval = 1.5f;
        private const float DiscoveryMaxDistance = 160f;
        private const float EncounterWithdrawDistance = 120f;

        private static readonly Dictionary<ZDOID, double> pendingUntil = new Dictionary<ZDOID, double>();
        private static readonly Dictionary<ZDOID, float> nextClientDiscoveryAt = new Dictionary<ZDOID, float>();
        private static long clientDiscoveryEventId = -1L;
        private static double nextMarkerScan;

        internal static void Tick(BloodMoonEventState state, double now)
        {
            if (state == null || ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null)
                return;

            ReassertPending(state, now);
            if (!state.IsCombatLive || now < nextMarkerScan)
                return;
            nextMarkerScan = now + 2d;

            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values
                .Where(zdo => zdo.GetLong(ParkedEventMarker, -1L) == state.EventId)
                .ToArray())
            {
                EnsureTransactionFromMarker(state, zdo);
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

            if (Utils.DistanceXZ(player.transform.position, boss.transform.position) > DiscoveryMaxDistance)
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
                    EnsureTransactionFromMarker(state, zdo);
                return;
            }

            Vector3 bossPosition = zdo.GetPosition();
            if (Utils.DistanceXZ(reporterPosition, bossPosition) > DiscoveryMaxDistance)
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
            nextMarkerScan = 0d;
            ResetClientState();
        }

        internal static bool ParkNearest(BloodMoonEventState state, Vector3 point, double now)
        {
            if (state == null || ZDOMan.instance == null)
                return false;

            ZDO zdo = ZDOMan.instance.m_objectsByID.Values
                .Where(IsAliveBossZdo)
                .Where(item => item.Persistent && !Character.InInterior(item.GetPosition()))
                .OrderBy(item => Utils.DistanceXZ(item.GetPosition(), point))
                .FirstOrDefault();
            if (zdo == null)
                return false;

            Park(state, zdo, now);
            return true;
        }

        private static void Park(BloodMoonEventState state, ZDO zdo, double now)
        {
            string id = zdo.m_uid.ToString();
            if (state.ParkedBosses.ContainsKey(id) || zdo.GetLong(ParkedEventMarker, -1L) == state.EventId)
                return;

            Vector3 originalPosition = zdo.GetPosition();
            BloodMoonBossParkingState transaction = new BloodMoonBossParkingState
            {
                ZdoId = id,
                OriginalPosition = originalPosition,
                OriginalRotation = zdo.GetRotation(),
                OriginalOwner = zdo.GetOwner(),
                OriginalDataRevision = zdo.DataRevision,
                OriginalOwnerRevision = zdo.OwnerRevision,
                WasLoaded = ZNetScene.instance?.FindInstance(zdo.m_uid) != null
            };

            zdo.Set(ParkingSchemaMarker, ParkingSchema);
            zdo.Set(OriginalPositionMarker, originalPosition);
            zdo.Set(OriginalPrefabHashMarker, zdo.GetPrefab());
            zdo.Set(ParkingTimestampMarker, (long)now);
            zdo.Set(ParkedEventMarker, state.EventId);
            state.ParkedBosses[id] = transaction;
            BloodMoonPersistence.Save(state);

            Vector3 parked = GetParkingPosition(zdo.m_uid);
            Reassert(zdo, parked);
            pendingUntil[zdo.m_uid] = now + 5d;
            SynchronizeLoadedInstance(zdo, parked);
            LogInfo($"[BloodMoon][event:{state.EventId}][boss:{zdo.m_uid}] parked from {originalPosition} to {parked}.");
        }

        internal static void RestoreAll(BloodMoonEventState state)
        {
            if (state == null || ZDOMan.instance == null)
                return;

            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.Where(HasParkingMarker).ToArray())
            {
                BloodMoonBossParkingState transaction = ResolveTransaction(state, zdo);
                if (transaction != null)
                    Restore(state, transaction);
            }

            state.ParkedBosses.Clear();
            pendingUntil.Clear();
            BloodMoonPersistence.Save(state);
        }

        internal static bool RestoreNearest(BloodMoonEventState state, Vector3 point)
        {
            if (state == null || ZDOMan.instance == null)
                return false;

            ZDO zdo = ZDOMan.instance.m_objectsByID.Values
                .Where(HasParkingMarker)
                .OrderBy(item => TryReadOriginalPosition(item, out Vector3 position) ? Utils.DistanceXZ(position, point) : float.MaxValue)
                .FirstOrDefault();
            if (zdo == null)
                return false;

            BloodMoonBossParkingState transaction = ResolveTransaction(state, zdo);
            if (transaction == null)
                return false;
            Restore(state, transaction);
            state.ParkedBosses.Remove(transaction.ZdoId);
            BloodMoonPersistence.Save(state);
            return true;
        }

        private static void Restore(BloodMoonEventState state, BloodMoonBossParkingState transaction)
        {
            if (!BloodMoonSpawner.TryParseZdoId(transaction.ZdoId, out ZDOID id))
                return;
            ZDO zdo = ZDOMan.instance.GetZDO(id);
            if (zdo == null)
            {
                transaction.Restored = true;
                return;
            }

            Vector3 originalPosition = transaction.OriginalPosition;
            if (TryReadOriginalPosition(zdo, out Vector3 durablePosition))
                originalPosition = durablePosition;

            zdo.SetOwner(ZDOMan.GetSessionID());
            ZeroSerializedVelocity(zdo);
            zdo.SetPosition(originalPosition);
            SynchronizeLoadedInstance(zdo, originalPosition);
            ZDOMan.instance.ForceSendZDO(id);

            zdo.Set(ParkedEventMarker, -1L);
            zdo.Set(ParkingSchemaMarker, 0);
            zdo.Set(OriginalPositionMarker, Vector3.zero);
            zdo.Set(OriginalPrefabHashMarker, 0);
            zdo.Set(ParkingTimestampMarker, 0L);
            ZDOMan.instance.ForceSendZDO(id);

            transaction.Restored = true;
            pendingUntil.Remove(id);
            LogInfo($"[BloodMoon][event:{state.EventId}][boss:{id}] restored to {originalPosition}.");
        }

        internal static void Recover(BloodMoonEventState state)
        {
            if (state == null || ZDOMan.instance == null)
                return;

            double now = SeasonState.IsActive ? seasonState.GetTotalSeconds() : 0d;
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.Where(HasParkingMarker).ToArray())
            {
                long markedEventId = zdo.GetLong(ParkedEventMarker, -1L);
                BloodMoonBossParkingState transaction = ResolveTransaction(state, zdo);
                if (transaction == null)
                {
                    LogError($"[BloodMoon.Recovery] Cannot restore parked boss {zdo.m_uid}: durable original position is missing.");
                    continue;
                }

                bool matchingActiveEvent = markedEventId == state.EventId && state.IsCombatLive;
                if (matchingActiveEvent)
                {
                    state.ParkedBosses[transaction.ZdoId] = transaction;
                    pendingUntil[zdo.m_uid] = now + 5d;
                    Reassert(zdo, GetParkingPosition(zdo.m_uid));
                }
                else
                {
                    LogWarning($"[BloodMoon.Recovery] Restoring stale parked boss {zdo.m_uid} from event {markedEventId}.");
                    Restore(state, transaction);
                    state.ParkedBosses.Remove(transaction.ZdoId);
                }
            }

            BloodMoonPersistence.Save(state);
        }

        private static bool TryValidateDiscoverySender(BloodMoonEventState state, long sender, long playerId, out Vector3 position)
        {
            position = Vector3.zero;
            if (state == null || playerId == 0L || ZNet.instance == null || ZRoutedRpc.instance == null || ZDOMan.instance == null ||
                !state.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant) || !participant.IsCombatActive)
                return false;

            if (sender == ZRoutedRpc.instance.GetServerPeerID())
            {
                Player player = Player.m_localPlayer;
                if (player == null || player.GetPlayerID() != playerId)
                    return false;
                position = player.transform.position;
                return true;
            }

            ZNetPeer peer = ZNet.instance.GetPeer(sender);
            if (peer == null || peer.m_characterID.IsNone())
                return false;
            ZDO playerZdo = ZDOMan.instance.GetZDO(peer.m_characterID);
            if (playerZdo == null || playerZdo.GetLong(ZDOVars.s_playerID, 0L) != playerId)
                return false;
            position = playerZdo.GetPosition();
            return true;
        }

        private static BloodMoonBossParkingState ResolveTransaction(BloodMoonEventState state, ZDO zdo)
        {
            string id = zdo.m_uid.ToString();
            if (state.ParkedBosses.TryGetValue(id, out BloodMoonBossParkingState transaction))
                return transaction;
            return EnsureTransactionFromMarker(state, zdo);
        }

        private static BloodMoonBossParkingState EnsureTransactionFromMarker(BloodMoonEventState state, ZDO zdo)
        {
            if (!TryReadOriginalPosition(zdo, out Vector3 originalPosition))
                return null;

            BloodMoonBossParkingState transaction = new BloodMoonBossParkingState
            {
                ZdoId = zdo.m_uid.ToString(),
                OriginalPosition = originalPosition,
                OriginalRotation = zdo.GetRotation(),
                OriginalOwner = 0L,
                OriginalDataRevision = zdo.DataRevision,
                OriginalOwnerRevision = zdo.OwnerRevision,
                WasLoaded = ZNetScene.instance?.FindInstance(zdo.m_uid) != null
            };
            state.ParkedBosses[transaction.ZdoId] = transaction;
            return transaction;
        }

        private static void ReassertPending(BloodMoonEventState state, double now)
        {
            foreach (KeyValuePair<ZDOID, double> pending in pendingUntil.ToArray())
            {
                if (pending.Value < now)
                {
                    pendingUntil.Remove(pending.Key);
                    continue;
                }

                ZDO zdo = ZDOMan.instance.GetZDO(pending.Key);
                if (zdo == null || zdo.GetLong(ParkedEventMarker, -1L) != state.EventId)
                {
                    pendingUntil.Remove(pending.Key);
                    continue;
                }

                Vector3 parked = GetParkingPosition(zdo.m_uid);
                Reassert(zdo, parked);
                SynchronizeLoadedInstance(zdo, parked);
            }
        }

        private static void Reassert(ZDO zdo, Vector3 position)
        {
            long serverSession = ZDOMan.GetSessionID();
            if (zdo.GetOwner() != serverSession)
                zdo.SetOwner(serverSession);
            ZeroSerializedVelocity(zdo);
            if (Utils.DistanceXZ(zdo.GetPosition(), position) > 0.1f || Mathf.Abs(zdo.GetPosition().y - position.y) > 0.1f)
                zdo.SetPosition(position);
            ZDOMan.instance.ForceSendZDO(zdo.m_uid);
        }

        private static void ZeroSerializedVelocity(ZDO zdo)
        {
            if (zdo == null)
                return;
            zdo.Set(ZDOVars.s_velHash, Vector3.zero);
            zdo.Set(ZDOVars.s_bodyVelHash, Vector3.zero);
            zdo.Set(ZDOVars.s_bodyAVelHash, Vector3.zero);
        }

        private static void SynchronizeLoadedInstance(ZDO zdo, Vector3 position)
        {
            GameObject instance = ZNetScene.instance?.FindInstance(zdo.m_uid);
            if (instance == null)
                return;
            instance.transform.position = position;
            Rigidbody body = instance.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = position;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        private static bool IsAliveBossZdo(ZDO zdo)
        {
            if (zdo == null || ZNetScene.instance == null)
                return false;
            GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            Character character = prefab != null ? prefab.GetComponent<Character>() : null;
            return character != null && character.IsBoss() && zdo.GetFloat(ZDOVars.s_health, 1f) > 0f;
        }

        private static bool HasParkingMarker(ZDO zdo)
        {
            return zdo != null && zdo.GetLong(ParkedEventMarker, -1L) >= 0L;
        }

        private static bool TryReadOriginalPosition(ZDO zdo, out Vector3 position)
        {
            position = Vector3.zero;
            return zdo != null && zdo.GetInt(ParkingSchemaMarker, 0) == ParkingSchema && zdo.GetVec3(OriginalPositionMarker, out position);
        }

        private static Vector3 GetParkingPosition(ZDOID id)
        {
            float offset = Mathf.Abs(id.GetHashCode() % 10000);
            return new Vector3(600000f + offset, 200f, 600000f + offset * 0.37f);
        }

        private static void WithdrawAffectedPlayers(BloodMoonEventState state, Vector3 bossPosition, string reason)
        {
            if (BloodMoonController.Instance == null)
                return;
            foreach (BloodMoonParticipantState participant in state.Participants.Values.Where(item => item.IsCombatActive).ToArray())
            {
                if (!BloodMoonController.Instance.TryGetConnectedPosition(participant.PlayerId, out Vector3 position) || Utils.DistanceXZ(position, bossPosition) > EncounterWithdrawDistance)
                    continue;
                BloodMoonController.Instance.WithdrawLocalOrRequested(participant.PlayerId);
                LogWarning($"[BloodMoon][event:{state.EventId}][boss] withdrew player {participant.PlayerId}: {reason}.");
            }
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
    internal static class BloodMoonBossDiscoveryPatch
    {
        private static void Postfix(Character __instance)
        {
            BloodMoonBosses.ReportLoadedBoss(__instance);
        }
    }
}
