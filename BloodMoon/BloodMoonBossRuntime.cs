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
        internal static void TickPending(BloodMoonEventState state, double now)
        {
            if (state == null || ZDOMan.instance == null || pendingUntil.Count == 0)
                return;

            foreach (KeyValuePair<ZDOID, double> pending in pendingUntil.ToArray())
            {
                if (pending.Value < now)
                {
                    pendingUntil.Remove(pending.Key);
                    pendingRecords.Remove(pending.Key);
                    continue;
                }

                ZDO zdo = ZDOMan.instance.GetZDO(pending.Key);
                if (zdo == null || !pendingRecords.TryGetValue(pending.Key, out ParkingRecord record) ||
                    record.EventId != state.EventId || zdo.GetPrefab() != record.OriginalPrefabHash)
                {
                    pendingUntil.Remove(pending.Key);
                    pendingRecords.Remove(pending.Key);
                    continue;
                }

                WriteParkingRecord(zdo, record);
                Vector3 parked = GetParkingPosition(zdo.m_uid);
                Reassert(zdo, parked);
                SynchronizeLoadedInstance(zdo, parked, zdo.GetRotation());
            }
        }

        private static void WriteParkingRecord(ZDO zdo, ParkingRecord record)
        {
            if (zdo == null || record == null || ZDOMan.instance == null)
                return;

            if (zdo.GetOwner() != ZDOMan.GetSessionID())
                zdo.SetOwner(ZDOMan.GetSessionID());
            zdo.Set(ParkingSchemaMarker, ParkingSchema);
            zdo.Set(OriginalPositionMarker, record.OriginalPosition);
            zdo.Set(OriginalRotationMarker, record.OriginalRotation);
            zdo.Set(OriginalPrefabHashMarker, record.OriginalPrefabHash);
            zdo.Set(ParkingTimestampMarker, record.ParkingTimestamp);
            zdo.Set(ParkedEventMarker, record.EventId);
        }

        private static void Reassert(ZDO zdo, Vector3 position)
        {
            long serverSession = ZDOMan.GetSessionID();
            if (zdo.GetOwner() != serverSession)
                zdo.SetOwner(serverSession);
            ZeroSerializedVelocity(zdo);
            if (Vector3.Distance(zdo.GetPosition(), position) > 0.1f)
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

        private static void SynchronizeLoadedInstance(ZDO zdo, Vector3 position, Quaternion rotation)
        {
            GameObject instance = ZNetScene.instance?.FindInstance(zdo.m_uid);
            if (instance == null)
                return;

            instance.transform.SetPositionAndRotation(position, rotation);
            Rigidbody body = instance.GetComponent<Rigidbody>();
            if (body == null)
                return;

            body.position = position;
            body.rotation = rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }
}
