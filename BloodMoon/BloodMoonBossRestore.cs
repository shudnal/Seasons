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
        internal static void RestoreAll(BloodMoonEventState state)
        {
            if (state == null || ZDOMan.instance == null || ZNetScene.instance == null)
                return;

            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.Where(HasParkingMarker).ToArray())
            {
                if (!TryReadParkingRecord(zdo, out ParkingRecord record, out string error))
                {
                    HandleInvalidRecoveryRecord(zdo, error);
                    continue;
                }
                Restore(zdo, record, state.EventId);
            }

            pendingUntil.Clear();
        }

        internal static bool RestoreNearest(BloodMoonEventState state, Vector3 point)
        {
            if (state == null || ZDOMan.instance == null || ZNetScene.instance == null)
                return false;

            ZDO selected = null;
            ParkingRecord selectedRecord = null;
            float selectedDistance = float.MaxValue;

            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.Where(HasParkingMarker).ToArray())
            {
                if (!TryReadParkingRecord(zdo, out ParkingRecord record, out _))
                    continue;

                float distance = Utils.DistanceXZ(record.OriginalPosition, point);
                if (distance >= selectedDistance)
                    continue;
                selected = zdo;
                selectedRecord = record;
                selectedDistance = distance;
            }

            if (selected == null)
                return false;

            Restore(selected, selectedRecord, state.EventId);
            return true;
        }

        private static void Restore(ZDO zdo, ParkingRecord record, long currentEventId)
        {
            if (zdo == null || record == null || ZDOMan.instance == null)
                return;

            ZDOID id = zdo.m_uid;
            zdo.SetOwner(ZDOMan.GetSessionID());
            ZeroSerializedVelocity(zdo);
            zdo.SetRotation(record.OriginalRotation);
            zdo.SetPosition(record.OriginalPosition);
            SynchronizeLoadedInstance(zdo, record.OriginalPosition, record.OriginalRotation);
            ZDOMan.instance.ForceSendZDO(id);

            ClearParkingMetadata(zdo);
            pendingUntil.Remove(id);
            invalidRecordErrors.Remove(id);
            LogInfo($"[BloodMoon][event:{currentEventId}][boss:{id}] restored event {record.EventId} transform to {record.OriginalPosition}.");
        }

        internal static void Recover(BloodMoonEventState state)
        {
            if (state == null || ZDOMan.instance == null || ZNetScene.instance == null)
                return;

            double now = SeasonState.IsActive ? seasonState.GetTotalSeconds() : 0d;
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.Where(HasParkingMarker).ToArray())
            {
                if (!TryReadParkingRecord(zdo, out ParkingRecord record, out string error))
                {
                    HandleInvalidRecoveryRecord(zdo, error);
                    continue;
                }

                ParkingLocation location = ClassifyCurrentPosition(zdo);
                bool matchingActiveEvent = record.EventId == state.EventId && state.IsCombatLive;
                if (matchingActiveEvent)
                {
                    Vector3 parked = GetParkingPosition(zdo.m_uid);
                    Reassert(zdo, parked);
                    SynchronizeLoadedInstance(zdo, parked, zdo.GetRotation());
                    pendingUntil[zdo.m_uid] = now + 5d;
                    invalidRecordErrors.Remove(zdo.m_uid);
                    if (location == ParkingLocation.InsideWorld)
                        LogWarning($"[BloodMoon.Recovery] Completed interrupted parking transaction for boss {zdo.m_uid} in event {record.EventId}.");
                    continue;
                }

                LogWarning($"[BloodMoon.Recovery] Restoring stale parked boss {zdo.m_uid} from event {record.EventId}; current location={location}.");
                Restore(zdo, record, state.EventId);
            }
        }

        internal static IReadOnlyDictionary<string, BloodMoonBossParkingState> GetParkingDiagnostics()
        {
            Dictionary<string, BloodMoonBossParkingState> result = new Dictionary<string, BloodMoonBossParkingState>(StringComparer.Ordinal);
            if (ZDOMan.instance == null)
                return result;

            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.Where(HasParkingMarker).ToArray())
            {
                bool valid = TryReadParkingRecord(zdo, out ParkingRecord record, out string error);
                ParkingLocation location = ClassifyCurrentPosition(zdo);
                string id = zdo.m_uid.ToString();
                result[id] = new BloodMoonBossParkingState
                {
                    ZdoId = id,
                    EventId = zdo.GetLong(ParkedEventMarker, -1L),
                    OriginalPosition = record?.OriginalPosition ?? Vector3.zero,
                    OriginalRotation = record?.OriginalRotation ?? Quaternion.identity,
                    OriginalOwner = zdo.GetOwner(),
                    OriginalDataRevision = zdo.DataRevision,
                    OriginalOwnerRevision = zdo.OwnerRevision,
                    WasLoaded = ZNetScene.instance?.FindInstance(zdo.m_uid) != null,
                    Restored = location == ParkingLocation.InsideWorld,
                    RecordValid = valid,
                    ValidationError = error ?? string.Empty,
                    Location = location.ToString(),
                    OriginalPrefabHash = record?.OriginalPrefabHash ?? zdo.GetInt(OriginalPrefabHashMarker, 0),
                    CurrentPrefabHash = zdo.GetPrefab()
                };
            }
            return result;
        }
    }
}
