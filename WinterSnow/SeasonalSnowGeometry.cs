using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seasons
{
    internal sealed partial class SeasonalSnowController
    {
        private readonly Dictionary<int, bool> snowCoverPrefabs = new Dictionary<int, bool>();
        private ZNetScene observedSnowGeometryScene;
        private Vector2s observedSnowReferenceZone;
        private SimulationDistance observedSnowSimulationDistance;
        private float observedSnowZoneSize;

        internal bool ObservesSnowGeometry => SeasonalSnow.WinterReady && snowRegions.Count != 0;

        internal void InvalidateSnowArea(Vector3 position, bool geometry, bool readyOnly = false)
        {
            if (!ObservesSnowGeometry || Character.InInterior(position))
                return;
            Vector2s center = ZoneSystem.GetZone(position);
            SnowRefresh reason = SnowRefresh.Area | (geometry ? SnowRefresh.Geometry : SnowRefresh.None);
            for (int x = -1; x <= 1; ++x)
                for (int y = -1; y <= 1; ++y)
                {
                    int zx = center.x + x;
                    int zy = center.y + y;
                    if (zx < short.MinValue || zx > short.MaxValue || zy < short.MinValue || zy > short.MaxValue)
                        continue;
                    if (snowRegions.TryGetValue(new Vector2s(zx, zy), out SnowRegion region))
                    {
                        if (geometry)
                        {
                            // Streaming objects that arrive before area readiness are
                            // covered by the single confirmation pass after readiness.
                            if (!readyOnly || region.Ready)
                                QueueRegion(region, reason);
                        }
                        else
                            region.ReadinessDirty = true;
                    }
                }
        }

        internal void SnowCoverHintChanged(WearNTear piece)
        {
            if (ObservesSnowGeometry && piece && snowPieces.TryGetValue(piece, out SnowPiece state))
                QueueRefresh(state, SnowRefresh.Geometry);
        }

        internal void SnowSceneObjectsChanged()
        {
            if (!ObservesSnowGeometry || !ZNet.instance || !ZoneSystem.instance)
                return;
            Vector2s reference = ZoneSystem.GetZone(ZNet.instance.GetReferencePosition());
            SimulationDistance distance = ZNet.instance.GetSyncedSimulationDistance();
            float zoneSize = ZoneSystem.instance.m_zoneSize;
            bool sameScene = observedSnowGeometryScene == ZNetScene.instance;
            bool activityChanged = !sameScene || reference != observedSnowReferenceZone ||
                !distance.Equals(observedSnowSimulationDistance) || zoneSize != observedSnowZoneSize;
            if (!activityChanged)
                return;

            Bounds previous = new Bounds(ZoneSystem.GetZonePos(observedSnowReferenceZone), Vector3.one *
                (observedSnowSimulationDistance.NearSimulationDistance == 1 ? 2f : 3f) * observedSnowZoneSize);
            Bounds current = new Bounds(ZoneSystem.GetZonePos(reference), Vector3.one *
                (distance.NearSimulationDistance == 1 ? 2f : 3f) * zoneSize);
            foreach (SnowRegion region in regionList)
                if (!sameScene || RegionTouches(region, previous) || RegionTouches(region, current))
                    QueueRegion(region, SnowRefresh.Area | SnowRefresh.Heat);

            observedSnowGeometryScene = ZNetScene.instance;
            observedSnowReferenceZone = reference;
            observedSnowSimulationDistance = distance;
            observedSnowZoneSize = zoneSize;
        }

        internal bool CanAffectSnowCover(ZDO zdo)
        {
            if (!ObservesSnowGeometry || zdo == null)
                return false;
            int hash = zdo.GetPrefab();
            if (snowCoverPrefabs.TryGetValue(hash, out bool result))
                return result;
            GameObject prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(hash) : null;
            // Do not cache a missing prefab during scene initialization.
            if (!prefab)
                return zdo.Type == ZDO.ObjectType.Solid || zdo.Type == ZDO.ObjectType.Terrain;
            ZSyncTransform motion = prefab.GetComponent<ZSyncTransform>();
            bool isDynamic = prefab.GetComponent<Character>() || prefab.GetComponent<ItemDrop>() ||
                prefab.GetComponent<Ship>() || prefab.GetComponent<Vagon>() || prefab.GetComponent<Projectile>() ||
                (motion && (motion.m_syncPosition || motion.m_characterParentSync));
            if (!isDynamic)
            {
                if (WearNTear.s_rayMask == 0)
                    WearNTear.s_rayMask = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small", "terrain");
                foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
                    if (collider && !collider.isTrigger &&
                        (WearNTear.s_rayMask & (1 << collider.gameObject.layer)) != 0)
                    {
                        result = true;
                        break;
                    }
            }
            snowCoverPrefabs[hash] = result;
            return result;
        }

        internal bool ShouldObserveSnowTransform(ZDO zdo)
        {
            if (!ObservesSnowGeometry || zdo == null)
                return false;
            if (snowIds.TryGetValue(zdo.m_uid, out SnowPiece state) && ReferenceEquals(state.Zdo, zdo))
                return true;
            return snowCoverPrefabs.TryGetValue(zdo.GetPrefab(), out bool result)
                ? result
                : CanAffectSnowCover(zdo);
        }

        internal void SnowObjectAdded(ZDO zdo)
        {
            if (CanAffectSnowCover(zdo))
                InvalidateSnowArea(zdo.GetPosition(), geometry: true, readyOnly: true);
        }

        private static void CaptureSnowGeometry(SnowPiece state)
        {
            Collider[] colliders = state.Colliders;
            if (colliders == null)
            {
                // WearNTear may already have collected the complete inactive-inclusive
                // hierarchy for support. Otherwise perform this discovery once and
                // retain the references across cover invalidations.
                colliders = state.Piece.m_colliders ?? state.Piece.GetComponentsInChildren<Collider>(true);
                state.Colliders = colliders;
            }
            state.HaveOrigin = false;
            state.Roof = state.Leaky = false;
            Collider highest = null;
            float top = float.NegativeInfinity;
            foreach (Collider collider in colliders)
            {
                if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger)
                    continue;
                state.Roof |= collider.CompareTag("roof");
                state.Leaky |= collider.CompareTag("leaky");
                float height = collider.bounds.max.y;
                if (height <= top)
                    continue;
                top = height;
                highest = collider;
            }
            if (!highest)
                return;
            Bounds bounds = highest.bounds;
            Vector3 origin = new Vector3(bounds.center.x, bounds.max.y + 0.05f, bounds.center.z);
            if (highest.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, bounds.size.y + 0.1f))
            {
                state.LocalOrigin = state.Transform.InverseTransformPoint(hit.point);
                state.HaveOrigin = true;
            }
        }

        private static bool HasSnowCover(SnowPiece state)
        {
            // Preserve the Seasons cast and self-filter, not vanilla HaveRoof.
            if (WearNTear.s_rayMask == 0)
                WearNTear.s_rayMask = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small", "terrain");
            Vector3 origin = state.HaveOrigin
                ? state.Transform.TransformPoint(state.LocalOrigin) + Vector3.up * 0.4f
                : state.Position + new Vector3(0f, state.Piece.m_roofCheckOffset, 0f);
            int count = Physics.SphereCastNonAlloc(origin, 0.1f, Vector3.up,
                WearNTear.s_raycastHits, 100f, WearNTear.s_rayMask);
            for (int i = 0; i < count; ++i)
            {
                Collider collider = WearNTear.s_raycastHits[i].collider;
                if (!collider)
                    continue;
                Transform hit = collider.transform;
                if (hit == state.Transform || hit.IsAncestor(state.Transform))
                    continue;
                return true;
            }
            return false;
        }

        internal static int HealthGeometryMask(WearNTear piece) =>
            (piece.m_new && piece.m_new.activeSelf ? 1 : 0) |
            (piece.m_worn && piece.m_worn.activeSelf ? 2 : 0) |
            (piece.m_broken && piece.m_broken.activeSelf ? 4 : 0);

        internal void SnowObjectTransformChanged(ZDO zdo, Vector3 previousPosition, Vector3 previousRotation)
        {
            if (!ObservesSnowGeometry || zdo == null ||
                (zdo.GetPosition() == previousPosition && zdo.m_rotation == previousRotation))
                return;
            SnowPositionChanged(zdo);
            bool tracked = snowIds.TryGetValue(zdo.m_uid, out SnowPiece state) && ReferenceEquals(state.Zdo, zdo);
            if (tracked)
            {
                state.GeometryCaptured = false;
                QueueRefresh(state, SnowRefresh.Geometry | SnowRefresh.Links | SnowRefresh.Area);
            }
            if (!tracked && !CanAffectSnowCover(zdo))
                return;
            InvalidateSnowArea(previousPosition, geometry: true, readyOnly: true);
            InvalidateSnowArea(zdo.GetPosition(), geometry: true, readyOnly: true);
        }

        internal void HealthGeometryChanged(WearNTear piece, int previous)
        {
            if (!ObservesSnowGeometry || !piece || previous == HealthGeometryMask(piece))
                return;
            InvalidateSnowArea(piece.transform.position, geometry: true);
            if (snowPieces.TryGetValue(piece, out SnowPiece state))
            {
                state.GeometryCaptured = false;
                QueueRefresh(state, SnowRefresh.Geometry);
                QueueRuntimeVisual(state, force: true);
            }
        }

        internal void BeforeSnowReferencePositionChanged(Vector3 position)
        {
            if (!ObservesSnowGeometry || !ZNet.instance || !simulationScene || !ZoneSystem.instance)
                return;
            Vector2s previous = ZoneSystem.GetZone(ZNet.instance.GetReferencePosition());
            Vector2s next = ZoneSystem.GetZone(position);
            if (previous == next)
                return;

            SimulationDistance distance = ZNet.instance.GetSyncedSimulationDistance();
            float zoneSize = ZoneSystem.instance.m_zoneSize;
            Bounds previousArea = new Bounds(ZoneSystem.GetZonePos(previous), Vector3.one *
                (distance.NearSimulationDistance == 1 ? 2f : 3f) * zoneSize);
            // Persist terminal buckets while still owning the departing area. Restrict
            // exact per-piece checks to regions that could overlap the previous area.
            foreach (SnowRegion region in regionList)
            {
                if (!RegionTouches(region, previousArea))
                    continue;
                foreach (SnowPiece state in region.Pieces)
                    if (state.Valid && state.Confirmed && state.View.IsOwner() &&
                        ZNetScene.InActiveArea(state.Position, previous) && !ZNetScene.InActiveArea(state.Position, next))
                        FlushSnowPiece(state);
            }
        }

        internal void BeforeSnowOwnerChanged(ZDO zdo, long owner)
        {
            if (!ObservesSnowGeometry || zdo == null || !zdo.IsOwner() || zdo.GetOwner() == owner)
                return;
            if (snowIds.TryGetValue(zdo.m_uid, out SnowPiece state) && ReferenceEquals(state.Zdo, zdo))
                FlushSnowPiece(state);
        }

        private void ResetSnowGeometry()
        {
            snowCoverPrefabs.Clear();
            observedSnowGeometryScene = null;
            observedSnowReferenceZone = default;
            observedSnowSimulationDistance = default;
            observedSnowZoneSize = 0f;
        }
    }
}
