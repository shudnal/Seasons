using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seasons
{
    internal sealed partial class SeasonalSnowController
    {
        private enum SnowBucket : byte
        {
            AccumulationFull,
            AccumulationOpen,
            MeltingEmpty,
            MeltingSnow
        }

        [Flags]
        private enum SnowRefresh : byte
        {
            None = 0,
            Area = 1,
            Geometry = 2,
            Heat = 4,
            Snapshot = 8,
            Rules = 16,
            Links = 32,
            CatchUp = 64,
            All = Area | Geometry | Heat | Snapshot | Rules | Links
        }

        private sealed class SnowPiece
        {
            internal readonly WearNTear Piece;
            internal readonly ZNetView View;
            internal readonly ZDO Zdo;
            internal readonly ZDOID Id;
            internal readonly Transform Transform;
            internal SnowRegion Region;
            internal Vector3 Position;
            internal Heightmap.Biome Biome;
            internal int RegionIndex;
            internal int BucketIndex = -1;
            internal SnowBucket Bucket;
            internal Heightmap.Biome BucketBiome;
            internal SnowRefresh Refresh;
            internal bool RefreshQueued;
            internal bool PublishQueued;
            internal bool ForcePublish;
            internal bool VisualQueued;
            internal bool ForceVisual;
            internal bool Retired;
            internal bool Confirmed;
            internal bool Construction;
            internal bool Saved;
            internal bool AppearanceChosen;
            internal bool MayPublish;
            internal bool AllowOwnerlessPublication;
            internal bool AllowInitialPublication;
            internal bool Covered;
            internal bool Shielded;
            internal bool Roof;
            internal bool Leaky;
            internal bool HaveOrigin;
            internal bool GeometryCaptured;
            internal int GeometryRevision = -1;
            internal bool Simulates;
            internal bool Melting;
            internal Vector3 LocalOrigin;
            internal Collider[] Colliders;
            internal long Epoch;
            internal long Owner;
            internal long SnapshotEpoch;
            internal long SnapshotTime;
            internal float SnapshotValue;
            internal float SnapshotBaseline;
            internal bool SnapshotPresent;
            internal float Snow;
            internal float Minimum;
            internal float Maximum;
            internal float HeatRate;
            internal float MeltRate;
            internal float InteractiveUntil;
            internal float NextUseSignal;
            internal double LastHeatTime;
            internal double WeatherTime;
            internal float WeatherGain;
            internal double ReplacedWeatherUntil;
            internal double CatchUpFrom = double.NaN;
            internal int ReadyGeneration;
            internal List<HeatLink> HeatLinks;

            internal SnowPiece(WearNTear piece, ZDO zdo)
            {
                Piece = piece;
                View = piece.m_nview;
                Zdo = zdo;
                Id = zdo.m_uid;
                Transform = piece.transform;
                Position = Transform.position;
                Owner = zdo.GetOwner();
            }

            internal bool Valid => !Retired && Piece && View && View.GetZDO() == Zdo && Zdo.m_uid == Id;
        }

        private sealed class SnowRegion
        {
            internal readonly Vector2s Zone;
            internal readonly Vector3 Center;
            internal readonly List<SnowPiece> Pieces = new List<SnowPiece>();
            internal readonly Dictionary<Heightmap.Biome, List<SnowPiece>>[] Buckets =
            {
                new Dictionary<Heightmap.Biome, List<SnowPiece>>(), new Dictionary<Heightmap.Biome, List<SnowPiece>>(),
                new Dictionary<Heightmap.Biome, List<SnowPiece>>(), new Dictionary<Heightmap.Biome, List<SnowPiece>>()
            };
            internal bool Ready;
            internal bool ReadinessDirty = true;
            internal bool Queued;
            internal int Cursor;
            internal SnowRefresh Pending;
            internal SnowRefresh Scanning;
            internal int ReadyGeneration;
            internal int GeometryRevision;
            internal double LastReadyWorld;
            internal double LastReadyTime;
            internal double ResumeFromWorld;
            internal double PauseAtWorld;
            internal double PauseAtTime;
            internal float NextReadinessCheck;
            internal int ListIndex;

            internal SnowRegion(Vector2s zone)
            {
                Zone = zone;
                Center = ZoneSystem.GetZonePos(zone);
            }
        }

        private readonly Dictionary<WearNTear, SnowPiece> snowPieces = new Dictionary<WearNTear, SnowPiece>();
        private readonly Dictionary<ZDOID, SnowPiece> snowIds = new Dictionary<ZDOID, SnowPiece>();
        private readonly Dictionary<Vector2s, SnowRegion> snowRegions = new Dictionary<Vector2s, SnowRegion>();
        private readonly List<SnowRegion> regionList = new List<SnowRegion>();
        private readonly Queue<SnowRegion> regionRefreshes = new Queue<SnowRegion>();
        private readonly Queue<SnowPiece> pieceRefreshes = new Queue<SnowPiece>();
        private readonly Queue<SnowPiece> geometryRefreshes = new Queue<SnowPiece>();
        private readonly Queue<SnowPiece> snowPublications = new Queue<SnowPiece>();
        private readonly Queue<SnowPiece> snowVisualChanges = new Queue<SnowPiece>();
        private readonly HashSet<SnowPiece> interactingPieces = new HashSet<SnowPiece>();
        private readonly List<SnowPiece> expiredInteractions = new List<SnowPiece>();

        private static void RemoveFromBucket(SnowPiece state)
        {
            if (state.BucketIndex < 0)
                return;
            List<SnowPiece> list = state.Region.Buckets[(int)state.Bucket][state.BucketBiome];
            int last = list.Count - 1;
            SnowPiece moved = list[last];
            list[state.BucketIndex] = moved;
            moved.BucketIndex = state.BucketIndex;
            list.RemoveAt(last);
            state.BucketIndex = -1;
        }

        private static void Classify(SnowPiece state)
        {
            SnowBucket bucket = state.Melting
                ? (state.Snow <= 0f ? SnowBucket.MeltingEmpty : SnowBucket.MeltingSnow)
                : (state.Snow >= state.Maximum ? SnowBucket.AccumulationFull : SnowBucket.AccumulationOpen);
            if (state.BucketIndex >= 0 && state.Bucket == bucket && state.BucketBiome == state.Biome)
                return;
            bool newMembership = state.BucketIndex < 0 || state.BucketBiome != state.Biome;
            RemoveFromBucket(state);
            state.Bucket = bucket;
            state.BucketBiome = state.Biome;
            Dictionary<Heightmap.Biome, List<SnowPiece>> groups = state.Region.Buckets[(int)bucket];
            if (newMembership)
            {
                // Allocate all terminal destinations on registration/biome changes,
                // never when ordinary arithmetic first reaches a numeric bound.
                foreach (Dictionary<Heightmap.Biome, List<SnowPiece>> group in state.Region.Buckets)
                {
                    if (!group.TryGetValue(state.Biome, out List<SnowPiece> destination))
                        group.Add(state.Biome, destination = new List<SnowPiece>());
                    if (destination.Capacity < state.Region.Pieces.Count)
                        destination.Capacity = Math.Max(state.Region.Pieces.Count, Math.Max(4, destination.Capacity * 2));
                }
            }
            List<SnowPiece> list = groups[state.Biome];
            state.BucketIndex = list.Count;
            list.Add(state);
        }

        private void QueueRefresh(SnowPiece state, SnowRefresh reason)
        {
            if (state.Retired)
                return;
            state.Refresh |= reason;
            if (state.RefreshQueued)
                return;
            state.RefreshQueued = true;
            pieceRefreshes.Enqueue(state);
        }

        private void QueueRegion(SnowRegion region, SnowRefresh reason)
        {
            bool geometry = (reason & SnowRefresh.Geometry) != 0;
            if (geometry)
            {
                // One generation is enough for every geometry event coalesced into the
                // same current or pending regional pass. Events arriving during an
                // active geometry scan schedule exactly one follow-up generation so
                // receivers already visited by that scan are refreshed again.
                bool pendingGeometry = (region.Pending & SnowRefresh.Geometry) != 0;
                if (!region.Queued || !pendingGeometry)
                    region.GeometryRevision++;
            }
            region.Pending |= reason;
            if ((reason & SnowRefresh.Area) != 0)
                region.ReadinessDirty = true;
            if (region.Queued)
                return;
            region.Queued = true;
            region.Cursor = 0;
            region.Scanning = region.Pending;
            region.Pending = SnowRefresh.None;
            regionRefreshes.Enqueue(region);
        }

        private void QueuePublication(SnowPiece state, bool force = false)
        {
            state.ForcePublish |= force;
            if (state.PublishQueued || state.Retired)
                return;
            state.PublishQueued = true;
            snowPublications.Enqueue(state);
        }
    }
}
