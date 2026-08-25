using UnityEngine;

namespace Seasons.BloodMoon
{
    // Runtime-only projection built from current ZDO markers for diagnostics. It is never persisted.
    internal sealed class BloodMoonBossParkingDiagnostic
    {
        public string ZdoId = string.Empty;
        public long EventId = -1L;
        public bool BossValid;
        public bool RecordValid;
        public string ValidationError = string.Empty;
        public string Location = string.Empty;
        public Vector3 OriginalPosition;
        public Quaternion OriginalRotation = Quaternion.identity;
        public int OriginalPrefabHash;
        public int CurrentPrefabHash;
        public long CurrentOwner;
        public uint DataRevision;
        public ushort OwnerRevision;
        public bool Loaded;
    }
}
