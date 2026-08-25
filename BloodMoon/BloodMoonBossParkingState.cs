using UnityEngine;

namespace Seasons.BloodMoon
{
    // Runtime-only projection used by existing diagnostics. No instance of this type is persisted.
    internal sealed class BloodMoonBossParkingState
    {
        public string ZdoId = string.Empty;
        public long EventId = -1L;
        public Vector3 OriginalPosition;
        public Quaternion OriginalRotation = Quaternion.identity;
        public long OriginalOwner;
        public uint OriginalDataRevision;
        public ushort OriginalOwnerRevision;
        public bool WasLoaded;
        public bool Restored;
        public bool RecordValid;
        public string ValidationError = string.Empty;
        public string Location = string.Empty;
        public int OriginalPrefabHash;
        public int CurrentPrefabHash;
    }
}
