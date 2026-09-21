using System;
using System.Collections.Generic;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    internal sealed partial class SeasonalSnowController
    {
        private const float HeatCellSize = 8f;
        private const float UnitMeltRate = 0.0018f;
        private const float InteractiveMeltRate = 0.002f;

        private readonly struct SnowHeatCell : IEquatable<SnowHeatCell>
        {
            private readonly ZoneSystem.SectorIndex sector;
            private readonly int local;

            internal SnowHeatCell(Vector3 position)
            {
                Vector2s zone = ZoneSystem.GetZone(position);
                Vector3 center = ZoneSystem.GetZonePos(zone);
                sector = ZoneSystem.GetSectorIndex(position);
                int x = Mathf.Clamp(Mathf.FloorToInt((position.x - center.x + 32f) / HeatCellSize), 0, 7);
                int z = Mathf.Clamp(Mathf.FloorToInt((position.z - center.z + 32f) / HeatCellSize), 0, 7);
                local = x + z * 8;
            }

            public bool Equals(SnowHeatCell other) => sector == other.sector && local == other.local;
            public override bool Equals(object other) => other is SnowHeatCell cell && Equals(cell);
            public override int GetHashCode() => unchecked(sector.GetHashCode() * 397 ^ local);
        }

        private sealed class HeatArea
        {
            internal EffectArea Area;
            internal Collider Collider;
            internal Transform Transform;
            internal Matrix4x4 Matrix;
            internal Matrix4x4 Inverse;
            internal Vector3 Position;
            internal Bounds Bounds;
            internal bool Active;
            private byte shape;
            private Vector3 center;
            private Vector3 half;
            private Vector3 direction;
            private float radius;
            private float segmentHalf;

            internal bool NeedsActiveGeometry => shape == 0;

            internal void Capture()
            {
                Matrix = Transform.localToWorldMatrix;
                Inverse = Transform.worldToLocalMatrix;
                Position = Transform.position;
                Vector3 scale = Transform.lossyScale;
                scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                if (Collider is BoxCollider box)
                {
                    shape = 1;
                    center = box.center;
                    half = box.size * 0.5f;
                    Vector3 x = Matrix.MultiplyVector(new Vector3(half.x, 0f, 0f));
                    Vector3 y = Matrix.MultiplyVector(new Vector3(0f, half.y, 0f));
                    Vector3 z = Matrix.MultiplyVector(new Vector3(0f, 0f, half.z));
                    Bounds = new Bounds(Matrix.MultiplyPoint3x4(center), 2f * new Vector3(
                        Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                        Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                        Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)));
                }
                else if (Collider is SphereCollider sphere)
                {
                    shape = 2;
                    center = Matrix.MultiplyPoint3x4(sphere.center);
                    radius = sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                    // Unity scales a sphere by the largest axis, not into an ellipsoid.
                    Bounds = new Bounds(center, Vector3.one * radius * 2f);
                }
                else if (Collider is CapsuleCollider capsule)
                {
                    shape = 3;
                    int axis = capsule.direction;
                    radius = capsule.radius * Mathf.Max(scale[(axis + 1) % 3], scale[(axis + 2) % 3]);
                    segmentHalf = Mathf.Max(0f, capsule.height * scale[axis] * 0.5f - radius);
                    direction = Vector3.zero;
                    direction[axis] = 1f;
                    direction = Matrix.MultiplyVector(direction).normalized;
                    center = Matrix.MultiplyPoint3x4(capsule.center);
                    Bounds = new Bounds(center, 2f * (Vector3.one * radius + segmentHalf *
                        new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z))));
                }
                else
                {
                    shape = 0;
                    Bounds = Collider.bounds;
                }
            }

            internal bool Contains(Vector3 point)
            {
                if (shape == 1)
                {
                    Vector3 p = Inverse.MultiplyPoint3x4(point) - center;
                    return Mathf.Abs(p.x) <= half.x && Mathf.Abs(p.y) <= half.y && Mathf.Abs(p.z) <= half.z;
                }
                if (shape == 2)
                    return (point - center).sqrMagnitude <= radius * radius;
                if (shape == 3)
                {
                    Vector3 offset = point - center;
                    Vector3 nearest = direction * Mathf.Clamp(Vector3.Dot(offset, direction), -segmentHalf, segmentHalf);
                    return (offset - nearest).sqrMagnitude <= radius * radius;
                }
                // Uncommon shapes use a narrow query only while links are rebuilt.
                return Active && Collider && Bounds.Contains(point) &&
                    (Collider.ClosestPoint(point) - point).sqrMagnitude < 0.000001f;
            }
        }

        private sealed class HeatSource
        {
            internal UnityEngine.Object Owner;
            internal Fireplace Fireplace;
            internal Smelter Smelter;
            internal WearNTear Piece;
            internal readonly List<HeatArea> Areas = new List<HeatArea>();
            internal readonly List<HeatLink> Links = new List<HeatLink>();
            internal readonly List<SnowHeatCell> Cells = new List<SnowHeatCell>();
            internal Bounds Bounds;
            internal bool HasBounds;
            internal bool GeometryQueued;
            internal bool Wide;
            internal bool Queued;
            internal bool Pending;
            internal bool Retired;
            internal int Cursor;
            internal float NextPoll;
        }

        private sealed class HeatLink
        {
            internal HeatSource Source;
            internal SnowPiece Piece;
            internal int SourceIndex;
            internal HeatArea[] Areas;
            internal float[] Weights;

            internal float Contribution()
            {
                if (Source.Retired)
                    return 0f;
                float weight = 0f;
                for (int i = 0; i < Areas.Length; ++i)
                    if (Areas[i].Active)
                        weight = Mathf.Max(weight, Weights[i]);
                return weight;
            }
        }

        private readonly Dictionary<EffectArea, HeatSource> heatAreas = new Dictionary<EffectArea, HeatSource>();
        private readonly Dictionary<UnityEngine.Object, HeatSource> heatOwners = new Dictionary<UnityEngine.Object, HeatSource>();
        private readonly Dictionary<SnowHeatCell, HashSet<HeatSource>> heatCells = new Dictionary<SnowHeatCell, HashSet<HeatSource>>();
        private readonly Dictionary<WearNTear, HashSet<HeatSource>> selfHeaters = new Dictionary<WearNTear, HashSet<HeatSource>>();
        private readonly List<HeatSource> heatSources = new List<HeatSource>();
        private readonly HashSet<HeatSource> wideHeaters = new HashSet<HeatSource>();
        private readonly Queue<HeatSource> changedHeaters = new Queue<HeatSource>();
        private readonly Queue<HeatSource> heatGeometry = new Queue<HeatSource>();
        private readonly HashSet<HeatSource> heatCandidates = new HashSet<HeatSource>();
        private int heatPollCursor;

        private bool HasPendingHeatGeometry => heatGeometry.Count != 0;

        private static SnowHeatCell HeatCell(Vector3 p) => new SnowHeatCell(p);

        internal void RegisterHeatArea(EffectArea area)
        {
            if (!area || !EnsureSnowScene() || heatAreas.ContainsKey(area) ||
                (area.m_type & EffectArea.Type.Heat) == 0 || !area.gameObject.scene.IsValid() || !area.m_collider)
                return;
            WearNTear piece = area.GetComponentInParent<WearNTear>();
            ZSyncTransform motion = area.GetComponentInParent<ZSyncTransform>();
            if ((piece && !piece.m_staticPosition) || area.GetComponentInParent<Character>() ||
                area.GetComponentInParent<Ship>() || area.GetComponentInParent<Vagon>() ||
                area.GetComponentInParent<ItemDrop>() ||
                (motion && (motion.m_syncPosition || motion.m_syncRotation || motion.m_characterParentSync)))
                return;
            // A static building piece is an explicit stationary anchor. An otherwise
            // unclassified physics hierarchy stays mobile even while asleep/kinematic.
            Rigidbody body = area.GetComponentInParent<Rigidbody>();
            if (body && (!piece || body.transform != piece.transform))
                return;
            Fireplace fireplace = area.GetComponentInParent<Fireplace>();
            Smelter smelter = area.GetComponentInParent<Smelter>();
            ZNetView view = area.GetComponentInParent<ZNetView>();
            UnityEngine.Object owner = fireplace ? (UnityEngine.Object)fireplace :
                smelter ? smelter : piece ? piece : view ? view : area.transform.root;
            if (!heatOwners.TryGetValue(owner, out HeatSource source))
            {
                source = new HeatSource { Owner = owner, Fireplace = fireplace, Smelter = smelter, Piece = piece };
                heatOwners.Add(owner, source);
                heatSources.Add(source);
                if (piece)
                {
                    if (!selfHeaters.TryGetValue(piece, out HashSet<HeatSource> own))
                        selfHeaters.Add(piece, own = new HashSet<HeatSource>());
                    own.Add(source);
                }
            }
            HeatArea shape = new HeatArea { Area = area, Collider = area.m_collider, Transform = area.transform };
            source.Areas.Add(shape);
            heatAreas.Add(area, source);
            ReindexHeatSource(source);
            PollHeatSource(source, force: true);
        }

        internal void HeatAreaChanged(EffectArea area)
        {
            if (!area)
                return;
            if (!heatAreas.TryGetValue(area, out HeatSource source))
                RegisterHeatArea(area);
            else
                source.NextPoll = 0f;
        }

        internal void RemoveHeatArea(EffectArea area)
        {
            if (ReferenceEquals(area, null) || !heatAreas.TryGetValue(area, out HeatSource source))
                return;
            heatAreas.Remove(area);
            for (int i = source.Areas.Count - 1; i >= 0; --i)
                if (ReferenceEquals(source.Areas[i].Area, area))
                {
                    source.Areas[i].Active = false;
                    source.Areas.RemoveAt(i);
                }
            if (source.Areas.Count == 0)
            {
                source.Retired = true;
                heatOwners.Remove(source.Owner);
                if (source.Piece && selfHeaters.TryGetValue(source.Piece, out HashSet<HeatSource> own))
                {
                    own.Remove(source);
                    if (own.Count == 0)
                        selfHeaters.Remove(source.Piece);
                }
            }
            ReindexHeatSource(source);
            QueueHeater(source);
        }

        private void QueueHeater(HeatSource source)
        {
            if (source.Queued)
            {
                // Receivers already visited by this pass must see a later toggle too.
                source.Pending = true;
                return;
            }
            source.Queued = true;
            source.Cursor = 0;
            changedHeaters.Enqueue(source);
        }

        private void ReindexHeatSource(HeatSource source)
        {
            if (source.GeometryQueued)
                return;
            source.GeometryQueued = true;
            heatGeometry.Enqueue(source);
        }

        private void ReindexHeatSourceNow(HeatSource source)
        {
            foreach (SnowHeatCell cell in source.Cells)
                if (heatCells.TryGetValue(cell, out HashSet<HeatSource> sources))
                {
                    sources.Remove(source);
                    if (sources.Count == 0)
                        heatCells.Remove(cell);
                }
            source.Cells.Clear();
            wideHeaters.Remove(source);
            Bounds old = source.Bounds;
            bool hadBounds = source.HasBounds;
            bool initialized = false;
            float distance = Mathf.Max(1f, seasonalSnowHeatSourceCheckDistance.Value);
            foreach (HeatArea area in source.Areas)
            {
                if (!area.Area || !area.Collider || !area.Transform)
                    continue;
                area.Capture();
                Bounds bounds = area.Bounds;
                bounds.Encapsulate(new Bounds(area.Position, Vector3.one * distance * 2f));
                if (!initialized)
                    source.Bounds = bounds;
                else
                    source.Bounds.Encapsulate(bounds);
                initialized = true;
            }
            if (initialized && !source.Retired)
            {
                Vector2Int min = new Vector2Int(Mathf.FloorToInt(source.Bounds.min.x / HeatCellSize),
                    Mathf.FloorToInt(source.Bounds.min.z / HeatCellSize));
                Vector2Int max = new Vector2Int(Mathf.FloorToInt(source.Bounds.max.x / HeatCellSize),
                    Mathf.FloorToInt(source.Bounds.max.z / HeatCellSize));
                long count = ((long)max.x - min.x + 1L) * ((long)max.y - min.y + 1L);
                // Keep a single topology item small even for a huge modded source.
                source.Wide = count > 256L || count <= 0L;
                if (source.Wide)
                    wideHeaters.Add(source);
                else
                    for (int x = min.x; x <= max.x; ++x)
                        for (int y = min.y; y <= max.y; ++y)
                        {
                            SnowHeatCell cell = HeatCell(new Vector3((x + 0.5f) * HeatCellSize, 0f, (y + 0.5f) * HeatCellSize));
                            if (!heatCells.TryGetValue(cell, out HashSet<HeatSource> sources))
                                heatCells.Add(cell, sources = new HashSet<HeatSource>());
                            sources.Add(source);
                            source.Cells.Add(cell);
                        }
            }
            source.HasBounds = initialized && !source.Retired;
            if (hadBounds)
                QueueHeatRegions(old);
            if (source.HasBounds)
                QueueHeatRegions(source.Bounds);
            if (source.Piece && snowPieces.TryGetValue(source.Piece, out SnowPiece self))
                QueueRefresh(self, SnowRefresh.Heat | SnowRefresh.Links);
        }

        private void QueueHeatRegions(Bounds bounds)
        {
            Vector2s min = ZoneSystem.GetZone(bounds.min);
            Vector2s max = ZoneSystem.GetZone(bounds.max);
            long count = ((long)max.x - min.x + 1L) * ((long)max.y - min.y + 1L);
            if (count > 4096L || count <= 0L)
            {
                // A giant modded volume can cover more cells than there are loaded
                // regions. The fallback scans region metadata, never all receivers.
                foreach (SnowRegion region in regionList)
                    if (RegionTouches(region, bounds))
                        QueueRegion(region, SnowRefresh.Heat | SnowRefresh.Links);
                return;
            }
            for (int x = min.x; x <= max.x; ++x)
                for (int z = min.y; z <= max.y; ++z)
                    if (snowRegions.TryGetValue(new Vector2s(x, z), out SnowRegion region))
                        QueueRegion(region, SnowRefresh.Heat | SnowRefresh.Links);
        }

        private static bool RegionTouches(SnowRegion region, Bounds bounds) =>
            Mathf.Abs(region.Center.x - bounds.center.x) <= 32f + bounds.extents.x &&
            Mathf.Abs(region.Center.z - bounds.center.z) <= 32f + bounds.extents.z;

        private void PollHeatSource(HeatSource source, bool force = false)
        {
            if (source.Retired || (!force && Time.time < source.NextPoll))
                return;
            source.NextPoll = Time.time + 0.5f;
            bool burning = source.Owner && (source.Fireplace
                ? source.Fireplace.m_nview && source.Fireplace.m_nview.IsValid() && source.Fireplace.IsBurning()
                : source.Smelter
                    ? source.Smelter.m_nview && source.Smelter.m_nview.IsValid() && source.Smelter.IsActive()
                    : true);
            bool changed = force;
            bool moved = false;
            foreach (HeatArea shape in source.Areas)
            {
                bool active = burning && shape.Area && shape.Area.isActiveAndEnabled &&
                    shape.Collider && shape.Collider.enabled && shape.Collider.gameObject.activeInHierarchy;
                bool activityChanged = shape.Active != active;
                changed |= activityChanged;
                shape.Active = active;
                moved |= shape.Transform && shape.Transform.localToWorldMatrix != shape.Matrix;
                moved |= activityChanged && shape.NeedsActiveGeometry;
            }
            if (moved)
                ReindexHeatSource(source);
            if (changed)
                QueueHeater(source);
        }

        private void UpdateHeatSources()
        {
            long geometryStart = System.Diagnostics.Stopwatch.GetTimestamp();
            int geometryLeft = 4;
            while (geometryLeft-- > 0 && heatGeometry.Count != 0 &&
                (System.Diagnostics.Stopwatch.GetTimestamp() - geometryStart) /
                    (double)System.Diagnostics.Stopwatch.Frequency < 0.0005d)
            {
                HeatSource source = heatGeometry.Dequeue();
                source.GeometryQueued = false;
                ReindexHeatSourceNow(source);
            }
            int polls = Math.Min(8, heatSources.Count);
            while (polls-- > 0 && heatSources.Count != 0)
            {
                heatPollCursor %= heatSources.Count;
                HeatSource source = heatSources[heatPollCursor];
                if (source.Retired)
                {
                    heatSources[heatPollCursor] = heatSources[heatSources.Count - 1];
                    heatSources.RemoveAt(heatSources.Count - 1);
                }
                else
                {
                    heatPollCursor++;
                    PollHeatSource(source);
                }
            }
            int remaining = 128;
            while (remaining-- > 0 && changedHeaters.Count != 0)
            {
                HeatSource source = changedHeaters.Peek();
                if (source.Cursor >= source.Links.Count)
                {
                    changedHeaters.Dequeue();
                    source.Queued = false;
                    if (source.Pending)
                    {
                        source.Pending = false;
                        QueueHeater(source);
                    }
                    continue;
                }
                QueueRefresh(source.Links[source.Cursor++].Piece, SnowRefresh.Heat);
            }
        }

        private static float DistanceWeight(float distance)
        {
            Vector2 weights = seasonalSnowHeatDistanceMultipliers.Value;
            float maximum = Mathf.Max(1f, seasonalSnowHeatSourceCheckDistance.Value);
            float t = maximum <= 1.0001f ? 0f : Mathf.InverseLerp(1f, maximum, distance);
            return Mathf.Lerp(Mathf.Max(0f, weights.x), Mathf.Max(0f, weights.y), t);
        }

        private void RebuildHeatLinks(SnowPiece state)
        {
            RemoveHeatLinks(state);
            heatCandidates.Clear();
            if (heatCells.TryGetValue(HeatCell(state.Position), out HashSet<HeatSource> nearby))
                heatCandidates.UnionWith(nearby);
            heatCandidates.UnionWith(wideHeaters);
            if (selfHeaters.TryGetValue(state.Piece, out HashSet<HeatSource> own))
                heatCandidates.UnionWith(own);
            float maximum = Mathf.Max(1f, seasonalSnowHeatSourceCheckDistance.Value);
            foreach (HeatSource source in heatCandidates)
            {
                if (source.Retired)
                    continue;
                HeatLink link = new HeatLink
                {
                    Source = source, Piece = state, SourceIndex = source.Links.Count,
                    Areas = source.Areas.ToArray(), Weights = new float[source.Areas.Count]
                };
                bool any = false;
                for (int i = 0; i < link.Areas.Length; ++i)
                {
                    HeatArea area = link.Areas[i];
                    bool self = source.Piece == state.Piece;
                    float distance = Vector3.Distance(state.Position, area.Position);
                    if (!self && distance > maximum && !area.Contains(state.Position))
                        continue;
                    link.Weights[i] = DistanceWeight(Mathf.Min(distance, maximum)) *
                        (self ? Mathf.Max(0f, seasonalSnowSelfHeatMultiplier.Value) : 1f);
                    any |= link.Weights[i] > 0f;
                }
                if (!any)
                    continue;
                state.HeatLinks.Add(source, link);
                source.Links.Add(link);
            }
            heatCandidates.Clear();
            RecalculateHeat(state);
        }

        private static void RemoveHeatLinks(SnowPiece state)
        {
            foreach (HeatLink link in state.HeatLinks.Values)
            {
                List<HeatLink> links = link.Source.Links;
                int last = links.Count - 1;
                HeatLink moved = links[last];
                links[link.SourceIndex] = moved;
                moved.SourceIndex = link.SourceIndex;
                links.RemoveAt(last);
                if (link.Source.Queued)
                    link.Source.Cursor = Math.Min(link.Source.Cursor, link.SourceIndex);
            }
            state.HeatLinks.Clear();
        }

        private static void RecalculateHeat(SnowPiece state)
        {
            float weight = 0f;
            foreach (HeatLink link in state.HeatLinks.Values)
                weight += link.Contribution();
            float piece = state.Roof ? seasonalSnowRoofPieceMeltMultiplier.Value :
                state.Leaky ? seasonalSnowLeakyPieceMeltMultiplier.Value : 1f;
            float rate = UnitMeltRate * Mathf.Max(0f, seasonalSnowHeatSourceMeltMultiplier.Value) *
                Mathf.Max(0f, piece) * weight;
            state.HeatRate = float.IsNaN(rate) || float.IsInfinity(rate) ? 0f : rate;
        }

        private void ResetHeatSources()
        {
            heatAreas.Clear();
            heatOwners.Clear();
            heatCells.Clear();
            selfHeaters.Clear();
            heatSources.Clear();
            wideHeaters.Clear();
            changedHeaters.Clear();
            heatGeometry.Clear();
            heatCandidates.Clear();
            heatPollCursor = 0;
        }
    }
}
