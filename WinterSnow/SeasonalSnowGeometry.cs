using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seasons
{
    internal sealed partial class SeasonalSnowController
    {
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

        private static void CaptureSnowGeometry(SnowPiece state)
        {
            // Rebuild only on registration or a relevant hierarchy/geometry change.
            Piece piece = state.Piece.m_piece;
            List<Collider> colliders = piece ? piece.GetAllColliders() : null;
            state.Colliders = colliders != null ? colliders.ToArray() : Array.Empty<Collider>();
            state.HaveOrigin = false;
            state.Roof = state.Leaky = false;
            Collider highest = null;
            float top = float.NegativeInfinity;
            foreach (Collider collider in state.Colliders)
            {
                if (!collider)
                    continue;
                state.Roof |= collider.CompareTag("roof");
                state.Leaky |= collider.CompareTag("leaky");
                if (!collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger)
                    continue;
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
            // Preserve the custom Seasons probe, not vanilla HaveRoof's leaky filtering.
            if (state.Piece.m_roof)
                return true;
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

        internal void HealthGeometryChanged(WearNTear piece, int previous)
        {
            if (!piece || previous == HealthGeometryMask(piece))
                return;
            InvalidateSnowArea(piece.transform.position, geometry: true);
            if (snowPieces.TryGetValue(piece, out SnowPiece state))
            {
                QueueRefresh(state, SnowRefresh.Geometry);
                QueueVisual(piece, state.Snow, disabled: false, force: true);
            }
        }
    }
}
