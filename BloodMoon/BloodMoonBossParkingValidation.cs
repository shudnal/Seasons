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
        private static void HandleInvalidRecoveryRecord(ZDO zdo, string error)
        {
            ParkingLocation location = ClassifyCurrentPosition(zdo);
            if (location == ParkingLocation.InsideWorld && TryResolveBossPrefab(zdo, out _))
            {
                LogWarning($"[BloodMoon.Recovery] Clearing incomplete parking marker for boss {zdo.m_uid} already inside the world: {error}.");
                ClearParkingMetadata(zdo);
                return;
            }

            LogInvalidRecordOnce(zdo, $"{error}; current location={location}. Boss was preserved for manual recovery");
        }

        private static void ClearParkingMetadata(ZDO zdo)
        {
            if (zdo == null)
                return;

            zdo.Set(ParkingSchemaMarker, 0);
            zdo.Set(OriginalPositionMarker, Vector3.zero);
            zdo.Set(OriginalRotationMarker, Quaternion.identity);
            zdo.Set(OriginalPrefabHashMarker, 0);
            zdo.Set(ParkingTimestampMarker, 0L);
            ZDOMan.instance?.ForceSendZDO(zdo.m_uid);

            // The active marker is committed last so an interrupted cleanup remains detectable.
            zdo.Set(ParkedEventMarker, -1L);
            ZDOMan.instance?.ForceSendZDO(zdo.m_uid);
            pendingUntil.Remove(zdo.m_uid);
            pendingRecords.Remove(zdo.m_uid);
            invalidRecordErrors.Remove(zdo.m_uid);
        }

        private static void LogInvalidRecordOnce(ZDO zdo, string error)
        {
            if (zdo == null)
                return;
            error ??= "unknown parking record error";
            if (invalidRecordErrors.TryGetValue(zdo.m_uid, out string previous) &&
                string.Equals(previous, error, StringComparison.Ordinal))
                return;

            invalidRecordErrors[zdo.m_uid] = error;
            LogError($"[BloodMoon.Recovery] Invalid parking record on ZDO {zdo.m_uid}: {error}.");
        }

        private static bool IsAliveBossZdo(ZDO zdo)
        {
            return TryResolveBossPrefab(zdo, out _) && zdo.GetFloat(ZDOVars.s_health, 1f) > 0f;
        }

        private static bool TryResolveBossPrefab(ZDO zdo, out Character character)
        {
            character = null;
            if (zdo == null || ZNetScene.instance == null)
                return false;

            GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            character = prefab != null ? prefab.GetComponent<Character>() : null;
            return character != null && character.IsBoss();
        }

        private static bool HasParkingMarker(ZDO zdo)
        {
            return zdo != null && zdo.GetLong(ParkedEventMarker, -1L) >= 0L;
        }

        private static ParkingLocation ClassifyCurrentPosition(ZDO zdo)
        {
            if (zdo == null)
                return ParkingLocation.Unknown;

            Vector3 position = zdo.GetPosition();
            if (!IsFinite(position))
                return ParkingLocation.Unknown;
            if (Vector3.Distance(position, GetParkingPosition(zdo.m_uid)) <= ParkingPositionTolerance)
                return ParkingLocation.ExpectedSlot;
            if (BloodMoonWorldEdge.TryIsBeyondWorldEdge(position, out bool beyond))
                return beyond ? ParkingLocation.BeyondWorldEdge : ParkingLocation.InsideWorld;

            // The deterministic parking sector is above 600 km. A position below the midpoint
            // cannot be one of our parking slots and is safe to classify during early world load.
            return Utils.LengthXZ(position) < 300000f ? ParkingLocation.InsideWorld : ParkingLocation.Unknown;
        }

        private static Vector3 GetParkingPosition(ZDOID id)
        {
            float offset = (float)Math.Abs((long)id.GetHashCode() % 10000L);
            return new Vector3(600000f + offset, 200f, 600000f + offset * 0.37f);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool SameNavigationContext(Vector3 first, Vector3 second)
        {
            bool firstInterior = Character.InInterior(first);
            bool secondInterior = Character.InInterior(second);
            if (firstInterior != secondInterior)
                return false;
            if (!firstInterior)
                return true;

            Location firstLocation = Location.GetLocation(first);
            Location secondLocation = Location.GetLocation(second);
            if (firstLocation != null || secondLocation != null)
                return firstLocation != null && object.ReferenceEquals(firstLocation, secondLocation);

            return ZoneSystem.GetZone(first) == ZoneSystem.GetZone(second);
        }

        private static void WithdrawAffectedPlayers(BloodMoonEventState state, Vector3 bossPosition, string reason)
        {
            if (BloodMoonController.Instance == null)
                return;
            foreach (BloodMoonParticipantState participant in state.Participants.Values.Where(item => item.IsCombatActive).ToArray())
            {
                if (!BloodMoonController.Instance.TryGetConnectedPosition(participant.PlayerId, out Vector3 position) ||
                    Utils.DistanceXZ(position, bossPosition) > EncounterWithdrawDistance || !SameNavigationContext(position, bossPosition))
                    continue;
                BloodMoonController.Instance.WithdrawLocalOrRequested(participant.PlayerId);
                LogWarning($"[BloodMoon][event:{state.EventId}][boss] withdrew player {participant.PlayerId}: {reason}.");
            }
        }
    }
}
