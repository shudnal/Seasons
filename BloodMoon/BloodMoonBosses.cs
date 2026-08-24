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
        private static readonly Dictionary<ZDOID, double> pendingUntil = new Dictionary<ZDOID, double>();
        private static double nextScan;

        internal static void Tick(BloodMoonEventState state, double now)
        {
            if (state == null || ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null)
                return;

            ReassertPending(state, now);
            if (!state.IsCombatLive || now < nextScan)
                return;
            nextScan = now + 2d;

            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.ToArray())
            {
                if (!IsBossZdo(zdo) || zdo.GetFloat(ZDOVars.s_health, 1f) <= 0f)
                    continue;
                string id = zdo.m_uid.ToString();
                if (state.ParkedBosses.ContainsKey(id) || zdo.GetLong(ParkedEventMarker, -1L) == state.EventId)
                    continue;

                if (!zdo.Persistent)
                {
                    WithdrawAffectedPlayers(state, zdo.GetPosition());
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
                .Where(IsBossZdo)
                .OrderBy(item => Utils.DistanceXZ(item.GetPosition(), point))
                .FirstOrDefault();
            if (zdo == null || !zdo.Persistent)
                return false;
            Park(state, zdo, now);
            return true;
        }

        private static void Park(BloodMoonEventState state, ZDO zdo, double now)
        {
            string id = zdo.m_uid.ToString();
            if (state.ParkedBosses.ContainsKey(id))
                return;

            BloodMoonBossParkingState transaction = new BloodMoonBossParkingState
            {
                ZdoId = id,
                OriginalPosition = zdo.GetPosition(),
                OriginalRotation = zdo.GetRotation(),
                OriginalOwner = zdo.GetOwner(),
                OriginalDataRevision = zdo.DataRevision,
                OriginalOwnerRevision = zdo.OwnerRevision,
                WasLoaded = ZNetScene.instance?.FindInstance(zdo.m_uid) != null
            };

            // Marker and sidecar state are durable before ownership/position mutation.
            zdo.Set(ParkedEventMarker, state.EventId);
            state.ParkedBosses[id] = transaction;
            BloodMoonPersistence.Save(state);

            Vector3 parked = GetParkingPosition(zdo.m_uid);
            Reassert(zdo, parked);
            pendingUntil[zdo.m_uid] = now + 5d;
            SynchronizeLoadedInstance(zdo, parked);
            LogInfo($"[BloodMoon.Boss] Parked {zdo.m_uid} from {transaction.OriginalPosition} to {parked}.");
        }

        internal static void RestoreAll(BloodMoonEventState state)
        {
            if (state == null || ZDOMan.instance == null)
                return;
            foreach (BloodMoonBossParkingState transaction in state.ParkedBosses.Values.ToArray())
                Restore(state, transaction);
            state.ParkedBosses.Clear();
            pendingUntil.Clear();
            BloodMoonPersistence.Save(state);
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

            zdo.SetOwner(ZDOMan.GetSessionID());
            zdo.SetPosition(transaction.OriginalPosition);
            zdo.SetRotation(transaction.OriginalRotation);
            ZDOMan.instance.ForceSendZDO(id);
            SynchronizeLoadedInstance(zdo, transaction.OriginalPosition);
            if (transaction.OriginalOwner != 0L)
                zdo.SetOwner(transaction.OriginalOwner);
            zdo.Set(ParkedEventMarker, -1L);
            transaction.Restored = true;
            LogInfo($"[BloodMoon.Boss] Restored {id} to {transaction.OriginalPosition}.");
        }

        internal static void Recover(BloodMoonEventState state)
        {
            if (state == null || ZDOMan.instance == null)
                return;

            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.Where(item => item.GetLong(ParkedEventMarker, -1L) == state.EventId).ToArray())
            {
                string id = zdo.m_uid.ToString();
                if (!state.ParkedBosses.TryGetValue(id, out BloodMoonBossParkingState transaction))
                {
                    LogError($"[BloodMoon.Recovery] Parked boss marker {id} has no transaction record; leaving it untouched for manual recovery.");
                    continue;
                }
                if (state.Phase == BloodMoonEventPhase.Resolving || state.Phase == BloodMoonEventPhase.Resolved || state.Phase == BloodMoonEventPhase.Dormant)
                    Restore(state, transaction);
                else
                    pendingUntil[zdo.m_uid] = seasonState.GetTotalSeconds() + 5d;
            }
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

        private static bool IsBossZdo(ZDO zdo)
        {
            if (zdo == null || ZNetScene.instance == null)
                return false;
            GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            Character character = prefab != null ? prefab.GetComponent<Character>() : null;
            return character != null && character.IsBoss();
        }

        private static Vector3 GetParkingPosition(ZDOID id)
        {
            float offset = Mathf.Abs(id.GetHashCode() % 10000);
            return new Vector3(600000f + offset, 200f, 600000f + offset * 0.37f);
        }

        private static void WithdrawAffectedPlayers(BloodMoonEventState state, Vector3 bossPosition)
        {
            if (BloodMoonController.Instance == null)
                return;
            foreach (BloodMoonParticipantState participant in state.Participants.Values.Where(item => item.IsCombatActive).ToArray())
            {
                if (!BloodMoonController.Instance.TryGetConnectedPosition(participant.PlayerId, out Vector3 position) || Utils.DistanceXZ(position, bossPosition) > 120f)
                    continue;
                BloodMoonController.Instance.WithdrawLocalOrRequested(participant.PlayerId);
                LogWarning($"[BloodMoon.Boss] Withdrew {participant.PlayerName} because nearby boss is nonpersistent/unparkable.");
            }
        }
    }
}
