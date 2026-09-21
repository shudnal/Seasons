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

            internal void Capture()
            {
                Matrix = Transform.localToWorldMatrix;
                Inverse = Transform.worldToLocalMatrix;
                Position = Transform.position;
                Bounds local;
                if (Collider is BoxCollider box)
                    local = new Bounds(box.center, box.size);
                else if (Collider is SphereCollider sphere)
                    local = new Bounds(sphere.center, Vector3.one * sphere.radius * 2f);
                else if (Collider is CapsuleCollider capsule)
                {
                    Vector3 size = Vector3.one * capsule.radius * 2f;
                    size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
                    local = new Bounds(capsule.center, size);
                }
                else if (Collider is MeshCollider mesh && mesh.sharedMesh)
                    local = mesh.sharedMesh.bounds;
                else
                {
                    Bounds = Collider.bounds;
                    return;
                }
                Vector3 x = Matrix.MultiplyVector(new Vector3(local.extents.x, 0f, 0f));
                Vector3 y = Matrix.MultiplyVector(new Vector3(0f, local.extents.y, 0f));
                Vector3 z = Matrix.MultiplyVector(new Vector3(0f, 0f, local.extents.z));
                Bounds = new Bounds(Matrix.MultiplyPoint3x4(local.center), 2f * new Vector3(
                    Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                    Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                    Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)));
            }

            internal bool Contains(Vector3 point)
            {
                if (Collider is BoxCollider box)
                {
                    Vector3 p = Inverse.MultiplyPoint3x4(point) - box.center;
                    Vector3 half = box.size * 0.5f;
                    return Mathf.Abs(p.x) <= half.x && Mathf.Abs(p.y) <= half.y && Mathf.Abs(p.z) <= half.z;
                }
                Vector3 scale = Transform.lossyScale;
                scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                if (Collider is SphereCollider sphere)
                {
                    float radius = sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                    return (point - Matrix.MultiplyPoint3x4(sphere.center)).sqrMagnitude <= radius * radius;
                }
                if (Collider is CapsuleCollider capsule)
                {
                    int axis = capsule.direction;
                    float radius = capsule.radius * Mathf.Max(scale[(axis + 1) % 3], scale[(axis + 2) % 3]);
                    float half = Mathf.Max(0f, capsule.height * scale[axis] * 0.5f - radius);
                    Vector3 direction = Vector3.zero;
                    direction[axis] = 1f;
                    direction = Matrix.MultiplyVector(direction).normalized;
                    Vector3 offset = point - Matrix.MultiplyPoint3x4(capsule.center);
                    Vector3 nearest = direction * Mathf.Clamp(Vector3.Dot(offset, direction), -half, half);
                    return (offset - nearest).sqrMagnitude <= radius * radius;
                }
                // Uncommon shapes use one narrow query while links are rebuilt, not per tick.
                return Active && Bounds.Contains(point) && (Collider.ClosestPoint(point) - point).sqrMagnitude < 0.000001f;
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
            internal readonly List<Vector2Int> Cells = new List<Vector2Int>();
            internal Bounds Bounds;
            internal bool Wide;
            internal bool Queued;
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
        private readonly Dictionary<Vector2Int, HashSet<HeatSource>> heatCells = new Dictionary<Vector2Int, HashSet<HeatSource>>();
        private readonly Dictionary<WearNTear, HashSet<HeatSource>> selfHeaters = new Dictionary<WearNTear, HashSet<HeatSource>>();
        private readonly List<HeatSource> heatSources = new List<HeatSource>();
        private readonly HashSet<HeatSource> wideHeaters = new HashSet<HeatSource>();
        private readonly Queue<HeatSource> changedHeaters = new Queue<HeatSource>();
        private readonly HashSet<HeatSource> heatCandidates = new HashSet<HeatSource>();
        private int heatPollCursor;

        private static Vector2Int HeatCell(Vector3 p) => new Vector2Int(
            Mathf.FloorToInt(p.x / HeatCellSize), Mathf.FloorToInt(p.z / HeatCellSize));

        internal void RegisterHeatArea(EffectArea area)
        {
            if (!area || heatAreas.ContainsKey(area) || (area.m_type & EffectArea.Type.Heat) == 0 ||
                !area.gameObject.scene.IsValid() || !area.m_collider)
                return;
            // Classification and component discovery happen only when a source appears.
            WearNTear piece = area.GetComponentInParent<WearNTear>();
            if ((piece && !piece.m_staticPosition) || area.GetComponentInParent<Character>() ||
                area.GetComponentInParent<Ship>() || area.GetComponentInParent<Vagon>() ||
                area.GetComponentInParent<ItemDrop>() || area.GetComponentInParent<Rigidbody>())
                return;
            Fireplace fireplace = area.GetComponentInParent<Fireplace>();
            Smelter smelter = area.GetComponentInParent<Smelter>();
            UnityEngine.Object owner = fireplace ? (UnityEngine.Object)fireplace : smelter ? smelter : area;
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
            shape.Capture();
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
            {
                RegisterHeatArea(area);
                return;
            }
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
                return;
            source.Queued = true;
            source.Cursor = 0;
            changedHeaters.Enqueue(source);
        }

        private void ReindexHeatSource(HeatSource source)
        {
            foreach (Vector2Int cell in source.Cells)
                if (heatCells.TryGetValue(cell, out HashSet<HeatSource> sources))
                {
                    sources.Remove(source);
                    if (sources.Count == 0)
                        heatCells.Remove(cell);
                }
            source.Cells.Clear();
            wideHeaters.Remove(source);
            Bounds old = source.Bounds;
            bool initialized = false;
            float distance = Mathf.Max(1f, seasonalSnowHeatSourceCheckDistance.Value);
            foreach (HeatArea area in source.Areas)
            {
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
                Vector2Int min = HeatCell(source.Bounds.min);
                Vector2Int max = HeatCell(source.Bounds.max);
                long count = ((long)max.x - min.x + 1L) * ((long)max.y - min.y + 1L);
                source.Wide = count > 4096L;
                if (source.Wide)
                    wideHeaters.Add(source);
                else
                    for (int x = min.x; x <= max.x; ++x)
                        for (int y = min.y; y <= max.y; ++y)
                        {
                            Vector2Int cell = new Vector2Int(x, y);
                            if (!heatCells.TryGetValue(cell, out HashSet<HeatSource> sources))
                                heatCells.Add(cell, sources = new HashSet<HeatSource>());
                            sources.Add(source);
                            source.Cells.Add(cell);
                        }
            }
            // A topology change is rare and touches regions, never a full piece scan here.
            foreach (SnowRegion region in snowRegions.Values)
                if (RegionTouches(region, old) || (initialized && RegionTouches(region, source.Bounds)))
                    QueueRegion(region, SnowRefresh.Heat | SnowRefresh.Rules);
            if (source.Piece && snowPieces.TryGetValue(source.Piece, out SnowPiece self))
                QueueRefresh(self, SnowRefresh.Heat | SnowRefresh.Rules);
        }

        private static bool RegionTouches(SnowRegion region, Bounds bounds) =>
            Mathf.Abs(region.Center.x - bounds.center.x) <= 32f + bounds.extents.x &&
            Mathf.Abs(region.Center.z - bounds.center.z) <= 32f + bounds.extents.z;

        private void PollHeatSource(HeatSource source, bool force = false)
        {
            if (source.Retired || (!force && Time.time < source.NextPoll))
                return;
            source.NextPoll = Time.time + 0.5f;
            bool burning = source.Fireplace
                ? source.Fireplace.m_nview && source.Fireplace.m_nview.IsValid() && source.Fireplace.IsBurning()
                : source.Smelter
                    ? source.Smelter.m_nview && source.Smelter.m_nview.IsValid() && source.Smelter.IsActive()
                    : true;
            bool changed = force;
            bool moved = false;
            foreach (HeatArea shape in source.Areas)
            {
                bool active = burning && shape.Area && shape.Area.isActiveAndEnabled &&
                    shape.Collider && shape.Collider.enabled && shape.Collider.gameObject.activeInHierarchy;
                changed |= shape.Active != active;
                shape.Active = active;
                moved |= shape.Transform && shape.Transform.localToWorldMatrix != shape.Matrix;
            }
            if (moved)
                ReindexHeatSource(source);
            if (changed)
                QueueHeater(source);
        }

        private void UpdateHeatSources()
        {
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
                        (self ? Mathf.Max(1f, seasonalSnowSelfHeatMultiplier.Value) : 1f);
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
            state.HeatRate = UnitMeltRate * Mathf.Max(0f, seasonalSnowHeatSourceMeltMultiplier.Value) *
                Mathf.Max(0f, piece) * weight;
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
            heatCandidates.Clear();
            heatPollCursor = 0;
        }
    }
}
