using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seasons
{
    internal sealed partial class SeasonalSnowController
    {
        private readonly Dictionary<Door, float> movingSnowDoors = new Dictionary<Door, float>();
        private readonly List<Door> completedSnowDoors = new List<Door>();
        private float nextDoorCheck;
        private ZNetScene observedSnowGeometryScene;
        private Vector2s observedSnowReferenceZone;
        private SimulationDistance observedSnowSimulationDistance;
        private float observedSnowZoneSize;

        internal void InvalidateSnowArea(Vector3 position, bool geometry)
        {
            if (snowRegions.Count == 0 || Character.InInterior(position))
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
                        QueueRegion(region, reason);
                }
        }

        internal void SnowCoverHintChanged(WearNTear piece)
        {
            if (piece && snowPieces.TryGetValue(piece, out SnowPiece state))
                QueueRefresh(state, SnowRefresh.Geometry);
        }

        internal void SnowSceneObjectsChanged()
        {
            if (!ZNet.instance || !ZoneSystem.instance || snowRegions.Count == 0)
                return;
            Vector2s reference = ZoneSystem.GetZone(ZNet.instance.GetReferencePosition());
            SimulationDistance distance = ZNet.instance.GetSyncedSimulationDistance();
            float zoneSize = ZoneSystem.instance.m_zoneSize;
            bool sameScene = observedSnowGeometryScene == ZNetScene.instance;
            bool activityChanged = !sameScene || reference != observedSnowReferenceZone ||
                !distance.Equals(observedSnowSimulationDistance) || zoneSize != observedSnowZoneSize;
            Bounds previous = new Bounds(ZoneSystem.GetZonePos(observedSnowReferenceZone), Vector3.one *
                (observedSnowSimulationDistance.NearSimulationDistance == 1 ? 2f : 3f) * observedSnowZoneSize);
            Bounds current = new Bounds(ZoneSystem.GetZonePos(reference), Vector3.one *
                (distance.NearSimulationDistance == 1 ? 2f : 3f) * zoneSize);
            // Add/reset notifications invalidate ready regions too. The creation pass
            // retries pending regions without periodically recasting stable geometry.
            foreach (SnowRegion region in regionList)
            {
                if (!region.Ready)
                    region.ReadinessDirty = true;
                if (activityChanged && (!sameScene || RegionTouches(region, previous) || RegionTouches(region, current)))
                    QueueRegion(region, SnowRefresh.Area | SnowRefresh.Heat);
            }
            observedSnowGeometryScene = ZNetScene.instance;
            observedSnowReferenceZone = reference;
            observedSnowSimulationDistance = distance;
            observedSnowZoneSize = zoneSize;
        }

        private static void CaptureSnowGeometry(SnowPiece state)
        {
            Piece piece = state.Piece.m_piece;
            if (!piece)
                piece = state.Piece.GetComponent<Piece>();
            // StaticTarget.GetAllColliders caches only colliders active on its first
            // call. A health/hierarchy revision must also discover newly enabled ones.
            state.Colliders = piece ? piece.GetComponentsInChildren<Collider>(true) : Array.Empty<Collider>();
            state.HaveOrigin = false;
            state.Roof = state.Leaky = false;
            Collider highest = null;
            float top = float.NegativeInfinity;
            foreach (Collider collider in state.Colliders)
            {
                if (!collider)
                    continue;
                if (!collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger)
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
            // A dirty geometry request invalidates positive roof caches too: an opened
            // door can keep the same GameObject reference while moving its collider away.
            // Preserve the Seasons cast and its leaky/self rules, not vanilla HaveRoof.
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

        internal void SnowObjectTransformChanged(ZDO zdo, Vector3 previousPosition, Quaternion previousRotation)
        {
            if (zdo == null || (zdo.GetPosition() == previousPosition && zdo.GetRotation() == previousRotation))
                return;
            SnowPositionChanged(zdo);
            bool tracked = snowIds.TryGetValue(zdo.m_uid, out SnowPiece state) && ReferenceEquals(state.Zdo, zdo);
            if (tracked)
                QueueRefresh(state, SnowRefresh.Geometry | SnowRefresh.Links | SnowRefresh.Area);
            if (!tracked && zdo.Type != ZDO.ObjectType.Solid && zdo.Type != ZDO.ObjectType.Terrain)
                return;
            InvalidateSnowArea(previousPosition, geometry: true);
            InvalidateSnowArea(zdo.GetPosition(), geometry: true);
        }

        internal void HealthGeometryChanged(WearNTear piece, int previous)
        {
            if (!piece || previous == HealthGeometryMask(piece))
                return;
            InvalidateSnowArea(piece.transform.position, geometry: true);
            if (snowPieces.TryGetValue(piece, out SnowPiece state))
            {
                QueueRefresh(state, SnowRefresh.Geometry);
                QueueRuntimeVisual(state, force: true);
            }
        }

        internal void SnowDoorChanged(Door door)
        {
            if (!door || snowRegions.Count == 0)
                return;
            InvalidateSnowArea(door.transform.position, geometry: true);
            movingSnowDoors[door] = Time.time + 8f;
        }

        private void UpdateSnowDoors()
        {
            if (movingSnowDoors.Count == 0 || Time.time < nextDoorCheck)
                return;
            nextDoorCheck = Time.time + 0.25f;
            completedSnowDoors.Clear();
            foreach (KeyValuePair<Door, float> entry in movingSnowDoors)
            {
                Door door = entry.Key;
                if (!door || !door.m_animator || Time.time >= entry.Value ||
                    (!door.m_animator.IsInTransition(0) && door.m_animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f))
                    completedSnowDoors.Add(door);
            }
            foreach (Door door in completedSnowDoors)
            {
                if (door)
                    InvalidateSnowArea(door.transform.position, geometry: true);
                movingSnowDoors.Remove(door);
            }
            completedSnowDoors.Clear();
        }
    }
}
