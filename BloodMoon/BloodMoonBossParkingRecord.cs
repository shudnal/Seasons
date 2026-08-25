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
        private static bool TryReadParkingRecord(ZDO zdo, out ParkingRecord record, out string error)
        {
            record = null;
            error = string.Empty;
            if (zdo == null || !HasParkingMarker(zdo))
            {
                error = "parking marker is missing";
                return false;
            }

            if (!TryResolveBossPrefab(zdo, out _))
            {
                error = "current prefab is not a registered boss";
                return false;
            }

            if (!zdo.Persistent)
            {
                error = "marked boss ZDO is not persistent";
                return false;
            }

            int schema = zdo.GetInt(ParkingSchemaMarker, 0);
            if (schema == LegacyParkingSchema)
            {
                if (!TryUpgradeLegacyRecord(zdo, out error))
                    return false;
                schema = zdo.GetInt(ParkingSchemaMarker, 0);
            }
            if (schema != ParkingSchema)
            {
                error = $"unsupported parking schema {schema}";
                return false;
            }

            if (!zdo.GetVec3(OriginalPositionMarker, out Vector3 originalPosition))
            {
                error = "original position is missing";
                return false;
            }
            if (!zdo.GetQuaternion(OriginalRotationMarker, out Quaternion originalRotation))
            {
                error = "original rotation is missing";
                return false;
            }

            int originalPrefabHash = zdo.GetInt(OriginalPrefabHashMarker, int.MinValue);
            if (originalPrefabHash == int.MinValue)
            {
                error = "original prefab hash is missing";
                return false;
            }
            if (originalPrefabHash != zdo.GetPrefab())
            {
                error = $"prefab marker {originalPrefabHash} does not match current prefab {zdo.GetPrefab()}";
                return false;
            }
            if (!IsFinite(originalPosition))
            {
                error = "original position is not finite";
                return false;
            }
            if (!IsFinite(originalRotation))
            {
                error = "original rotation is not finite";
                return false;
            }
            if (BloodMoonWorldEdge.TryIsBeyondWorldEdge(originalPosition, out bool originalBeyond))
            {
                if (originalBeyond)
                {
                    error = "original position is outside the world boundary";
                    return false;
                }
            }
            else if (Utils.LengthXZ(originalPosition) >= 300000f)
            {
                error = "original position cannot be validated while the world boundary is unavailable";
                return false;
            }

            record = new ParkingRecord
            {
                EventId = zdo.GetLong(ParkedEventMarker, -1L),
                OriginalPosition = originalPosition,
                OriginalRotation = originalRotation,
                OriginalPrefabHash = originalPrefabHash
            };
            return true;
        }

        private static bool TryUpgradeLegacyRecord(ZDO zdo, out string error)
        {
            error = string.Empty;
            if (!zdo.GetVec3(OriginalPositionMarker, out Vector3 originalPosition) || !IsFinite(originalPosition))
            {
                error = "legacy record has no finite original position";
                return false;
            }

            int originalPrefabHash = zdo.GetInt(OriginalPrefabHashMarker, int.MinValue);
            if (originalPrefabHash == int.MinValue || originalPrefabHash != zdo.GetPrefab())
            {
                error = "legacy record prefab marker is missing or mismatched";
                return false;
            }

            Quaternion originalRotation = zdo.GetRotation();
            if (!IsFinite(originalRotation))
            {
                error = "legacy record current rotation is not finite";
                return false;
            }

            // Schema 1 never changed boss rotation while parking, so the current ZDO rotation is
            // the durable original rotation for an in-place upgrade.
            zdo.Set(OriginalRotationMarker, originalRotation);
            zdo.Set(ParkingSchemaMarker, ParkingSchema);
            ZDOMan.instance?.ForceSendZDO(zdo.m_uid);
            LogInfo($"[BloodMoon.Recovery] Upgraded legacy parking record for boss {zdo.m_uid} from schema {LegacyParkingSchema} to {ParkingSchema}.");
            return true;
        }
    }
}
