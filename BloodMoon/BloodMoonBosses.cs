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
        private static readonly Dictionary<ZDOID, double> pendingUntil = new Dictionary<ZDOID, double>();
        private static double nextScan;

        internal static void Tick(BloodMoonEventState state, double now)
        {
            if (state == null || ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null || ZNetScene.instance == null)
                return;

            ReassertPending(state, now);
            if (!state.IsCombatLive || now < nextScan)
                return;
            nextScan = now + 2d;

            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.ToArray())
            {
                if (!IsAliveBossZdo(zdo))
                    continue;

                long parkedEventId = zdo.GetLong(ParkedEventMarker, -1L);
                if (parkedEventId >= 0L)
                {
                    if (parkedEventId == state.EventId)
                        EnsureTransactionFromMarker(state, zdo);
                    continue;
                }

                Vector3 position = zdo.GetPosition();
                bool interior = Character.InInterior(position);
                if (interior || !zdo.Persistent)
                {
                    WithdrawAffectedPlayers(state, position, interior ? "interior boss encounter" : "nonpersistent boss encounter");
                    continue;
                }

                Park(state, zdo, now);
            }
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
            if (Utils.DistanceXZ(zdo.GetPosition(), position) > 0.1f || Mathf.Abs(zdo.GetPosition().y - position.y) > 0.1f)
                zdo.SetPosition(position);
            ZDOMan.instance.ForceSendZDO(zdo.m_uid);
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
                if (!BloodMoonController.Instance.TryGetConnectedPosition(participant.PlayerId, out Vector3 position) || Utils.DistanceXZ(position, bossPosition) > 120f)
                    continue;
                BloodMoonController.Instance.WithdrawLocalOrRequested(participant.PlayerId);
                LogWarning($"[BloodMoon][event:{state.EventId}][boss] withdrew player {participant.PlayerId}: {reason}.");
            }
        }
    }
}
