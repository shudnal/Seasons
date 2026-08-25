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
        internal static bool ParkNearest(BloodMoonEventState state, Vector3 point, double now)
        {
            if (state == null || !state.IsCombatLive || state.EventId < 0L || ZDOMan.instance == null)
                return false;

            ZDO zdo = ZDOMan.instance.m_objectsByID.Values
                .Where(IsAliveBossZdo)
                .Where(item => item.Persistent && !HasParkingMarker(item) && !Character.InInterior(item.GetPosition()))
                .OrderBy(item => Utils.DistanceXZ(item.GetPosition(), point))
                .FirstOrDefault();
            if (zdo == null)
                return false;

            Park(state, zdo, now);
            return true;
        }

        private static void Park(BloodMoonEventState state, ZDO zdo, double now)
        {
            if (state == null || !state.IsCombatLive || state.EventId < 0L || zdo == null ||
                !zdo.Persistent || !IsAliveBossZdo(zdo))
                return;

            long parkedEvent = zdo.GetLong(ParkedEventMarker, -1L);
            if (parkedEvent >= 0L)
            {
                if (parkedEvent == state.EventId)
                {
                    if (TryReadParkingRecord(zdo, out ParkingRecord record, out string error))
                    {
                        Vector3 existingParkingPosition = GetParkingPosition(zdo.m_uid);
                        pendingRecords[zdo.m_uid] = record;
                        pendingUntil[zdo.m_uid] = now + 5d;
                        Reassert(zdo, existingParkingPosition);
                        SynchronizeLoadedInstance(zdo, existingParkingPosition, zdo.GetRotation());
                    }
                    else
                    {
                        LogInvalidRecordOnce(zdo, error);
                    }
                }
                return;
            }

            Vector3 originalPosition = zdo.GetPosition();
            Quaternion originalRotation = zdo.GetRotation();
            int prefabHash = zdo.GetPrefab();
            if (!IsFinite(originalPosition) || !IsFinite(originalRotation))
            {
                LogError($"[BloodMoon][event:{state.EventId}][boss:{zdo.m_uid}] parking rejected because the original transform is not finite.");
                return;
            }

            ParkingRecord record = new ParkingRecord
            {
                EventId = state.EventId,
                OriginalPosition = originalPosition,
                OriginalRotation = originalRotation,
                OriginalPrefabHash = prefabHash,
                ParkingTimestamp = (long)now
            };
            pendingRecords[zdo.m_uid] = record;
            pendingUntil[zdo.m_uid] = now + 5d;
            WriteParkingRecord(zdo, record);

            Vector3 parked = GetParkingPosition(zdo.m_uid);
            Reassert(zdo, parked);
            SynchronizeLoadedInstance(zdo, parked, originalRotation);
            invalidRecordErrors.Remove(zdo.m_uid);
            LogInfo($"[BloodMoon][event:{state.EventId}][boss:{zdo.m_uid}] parked from {originalPosition} to {parked}.");
        }
    }
}
