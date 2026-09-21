using System;
using System.Collections.Generic;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    internal sealed partial class SeasonalSnowController
    {
        private const string UseSnowRpc = "Seasons_SnowUse";
        private const int RefreshesPerFrame = 256;
        private const int GeometryPerFrame = 20;
        private const int PublicationsPerFrame = 50;
        private const float PublicationStep = 0.01f;
        private ZNetScene simulationScene;
        private ZNetScene stoppedSnowScene;
        private int simulationFrame = -1;
        private int discoveryCursor = -1;
        private int sourceDiscoveryCursor = -1;
        private int readinessCursor;
        private bool winterRunning;
        private bool endingSnowWinter;
        private bool catchingUp;
        private long winterEpoch;
        private double snowClock;
        private double lastWorldSeconds;
        private double weatherBoundary;
        private int observedShieldRevision;
        private readonly Dictionary<Heightmap.Biome, float> frameWeather = new Dictionary<Heightmap.Biome, float>();

        internal int SnowPieceCount => snowPieces.Count;
        internal int SnowRegionCount => snowRegions.Count;
        internal bool HasSnowRuntime(WearNTear piece) => piece && snowPieces.ContainsKey(piece);

        private bool EnsureSnowScene()
        {
            ZNetScene scene = ZNetScene.instance;
            if (!scene || ReferenceEquals(scene, stoppedSnowScene) || !ZoneSystem.instance || !ZNet.instance)
                return false;
            if (ReferenceEquals(scene, simulationScene))
                return true;
            ResetSnowRuntime();
            simulationScene = scene;
            stoppedSnowScene = null;
            lastWorldSeconds = ZNet.instance.GetTimeSeconds();
            observedShieldRevision = ShieldGenerator.m_instanceChangeID;
            return true;
        }

        internal void RegisterSnow(WearNTear piece)
        {
            if (!piece || !EnsureSnowScene() || endingSnowWinter || !SeasonalSnow.WinterReady ||
                !SeasonalSnow.IsSeasonalSnowPosition(piece) || piece.m_nview.m_ghost ||
                ZNetView.m_forceDisableInit || !piece.gameObject.scene.IsValid())
                return;
            ZDO zdo = piece.m_nview.GetZDO();
            if (zdo == null || !zdo.IsValid())
                return;
            if (snowPieces.TryGetValue(piece, out SnowPiece existing))
            {
                if (existing.Valid)
                    return;
                RetireSnow(existing, releaseVisual: true);
            }
            if (snowIds.TryGetValue(zdo.m_uid, out SnowPiece replaced))
                RetireSnow(replaced, releaseVisual: true);

            SeasonalSnowStorage.MigrateLoaded(piece);
            // Native wear and heavy-snow checks must never see the seasonal amount.
            piece.m_snowBuildup = 0f;
            piece.m_heavySnow = false;
            SnowPiece state = new SnowPiece(piece, zdo);
            AddSnowRegion(state);
            snowPieces.Add(piece, state);
            snowIds.Add(state.Id, state);
            Vector2 range = SeasonalSnow.GetSnowBuildupRange(piece);
            state.Minimum = range.x;
            state.Maximum = range.y;
            state.Biome = SeasonalSnow.GetBiome(piece);
            state.Epoch = SeasonalSnowStorage.CurrentWinterEpoch;
            state.LastHeatTime = snowClock;
            state.WeatherTime = ZNet.instance.GetTimeSeconds();
            SeasonalSnowStorage.Snapshot snapshot = new SeasonalSnowStorage.Snapshot(zdo);
            RememberSnapshot(state, snapshot);
            state.Saved = snapshot.AppliesTo(state.Epoch);
            if (state.Saved)
            {
                state.Snow = Mathf.Min(state.Maximum, snapshot.Value);
                state.AppearanceChosen = true;
            }
            else if (SeasonalSnow.WeatherReady && (state.Owner == 0L || state.View.IsOwner()))
            {
                state.Snow = SeasonalSnow.GetPassiveSeasonalSnowTarget(piece);
                state.AppearanceChosen = true;
            }
            if (ShieldGenerator.IsInsideShieldCached(state.Position, ref piece.m_shieldChangeID))
                state.Snow = 0f;
            Classify(state);
            state.View.Unregister(UseSnowRpc);
            state.View.Register(UseSnowRpc, sender => ReceiveUse(state, sender));
            QueueRuntimeVisual(state, force: true);
            QueueRefresh(state, SnowRefresh.All);
            QueueRegion(state.Region, SnowRefresh.Area);
        }

        private void AddSnowRegion(SnowPiece state)
        {
            Vector2s zone = ZoneSystem.GetZone(state.Position);
            if (!snowRegions.TryGetValue(zone, out SnowRegion region))
            {
                region = new SnowRegion(zone) { ListIndex = regionList.Count };
                snowRegions.Add(zone, region);
                regionList.Add(region);
            }
            state.Region = region;
            state.RegionIndex = region.Pieces.Count;
            region.Pieces.Add(state);
        }

        private void RemoveSnowRegion(SnowPiece state)
        {
            SnowRegion region = state.Region;
            int last = region.Pieces.Count - 1;
            SnowPiece moved = region.Pieces[last];
            region.Pieces[state.RegionIndex] = moved;
            moved.RegionIndex = state.RegionIndex;
            region.Pieces.RemoveAt(last);
            if (region.Queued)
                region.Cursor = Math.Min(region.Cursor, state.RegionIndex);
            if (region.Pieces.Count != 0)
                return;
            snowRegions.Remove(region.Zone);
            int end = regionList.Count - 1;
            SnowRegion movedRegion = regionList[end];
            regionList[region.ListIndex] = movedRegion;
            movedRegion.ListIndex = region.ListIndex;
            regionList.RemoveAt(end);
        }

        private void MoveSnowPiece(SnowPiece state, Vector3 position)
        {
            Vector3 old = state.Position;
            state.Position = position;
            if (state.Region.Zone != ZoneSystem.GetZone(position))
            {
                RemoveFromBucket(state);
                RemoveSnowRegion(state);
                AddSnowRegion(state);
                state.ReadyGeneration = -1;
                Classify(state);
                QueueRegion(state.Region, SnowRefresh.Area);
            }
            state.Biome = WorldGenerator.instance ? WorldGenerator.instance.GetBiome(position) : state.Biome;
            state.GeometryCaptured = false;
            InvalidateSnowArea(old, geometry: true);
            InvalidateSnowArea(position, geometry: true);
        }

        internal float GetSnowValue(WearNTear piece)
        {
            if (!piece)
                return 0f;
            if (!snowPieces.TryGetValue(piece, out SnowPiece state))
            {
                RegisterSnow(piece);
                snowPieces.TryGetValue(piece, out state);
            }
            return state != null && !state.Retired ? state.Snow : 0f;
        }

        internal void SnowPlaced(WearNTear piece)
        {
            RegisterSnow(piece);
            if (!snowPieces.TryGetValue(piece, out SnowPiece state))
                return;
            state.Construction = true;
            state.Saved = state.AppearanceChosen = true;
            state.Snow = 0f;
            state.WeatherTime = ZNet.instance.GetTimeSeconds();
            state.LastHeatTime = snowClock;
            state.Epoch = SeasonalSnowStorage.CurrentWinterEpoch;
            if (SeasonalSnowStorage.CanWrite(state.View, state.Zdo))
            {
                SeasonalSnowStorage.Write(state.Zdo, 0f, state.Epoch, state.WeatherTime);
                RememberSnapshot(state, new SeasonalSnowStorage.Snapshot(state.Zdo));
            }
            Classify(state);
            QueueRuntimeVisual(state, force: true);
            QueueRefresh(state, SnowRefresh.All);
        }

        internal void SnowMeshChanged(WearNTear piece)
        {
            if (!piece)
                return;
            ReleaseVisual(piece, hide: true, restoreNative: false);
            if (snowPieces.TryGetValue(piece, out SnowPiece state))
            {
                state.VisualSnow = float.NaN;
                QueueRefresh(state, SnowRefresh.Rules | SnowRefresh.Geometry | SnowRefresh.Links);
                if (SeasonalSnow.IsSeasonalSnowPosition(piece))
                    QueueRuntimeVisual(state, force: true);
            }
            else
                RegisterSnow(piece);
        }

        internal void ClearSnow(WearNTear piece, ZDO ownedZdo)
        {
            if (!ReferenceEquals(piece, null) && snowPieces.TryGetValue(piece, out SnowPiece state))
                RetireSnow(state, releaseVisual: false);
            if (ownedZdo != null)
                SeasonalSnowStorage.Clear(ownedZdo, clearNative: true);
        }

        internal void ReleaseIgnoredSnow(WearNTear piece)
        {
            if (!piece)
                return;
            if (snowPieces.TryGetValue(piece, out SnowPiece state))
            {
                if (SeasonalSnowStorage.CanWrite(state.View, state.Zdo))
                    SeasonalSnowStorage.Clear(state.Zdo, clearNative: true);
                RetireSnow(state, releaseVisual: false);
            }
            piece.m_snowBuildup = piece.m_nview && piece.m_nview.IsValid()
                ? piece.m_nview.GetZDO().GetFloat(ZDOVars.s_snow) : 0f;
            ReleaseVisual(piece, hide: true, restoreNative: true);
        }

        internal void ForgetSnow(WearNTear piece)
        {
            if (!ReferenceEquals(piece, null) && snowPieces.TryGetValue(piece, out SnowPiece state))
            {
                FlushSnowPiece(state);
                RetireSnow(state, releaseVisual: false);
            }
        }

        private void RetireSnow(SnowPiece state, bool releaseVisual)
        {
            if (state.Retired)
                return;
            state.Retired = true;
            RemoveFromBucket(state);
            RemoveHeatLinks(state);
            interactingPieces.Remove(state);
            snowPieces.Remove(state.Piece);
            if (snowIds.TryGetValue(state.Id, out SnowPiece current) && ReferenceEquals(current, state))
                snowIds.Remove(state.Id);
            if (state.View)
                state.View.Unregister(UseSnowRpc);
            RemoveSnowRegion(state);
            if (releaseVisual)
                ReleaseVisual(state.Piece, hide: true, restoreNative: false);
        }

        internal void RequestSnowRefresh(bool rules)
        {
            if (!EnsureSnowScene())
                return;
            discoveryCursor = 0;
            foreach (SnowRegion region in regionList)
                QueueRegion(region, rules ? SnowRefresh.All : SnowRefresh.Geometry);
        }

        internal void RequestHeatRefresh()
        {
            if (!EnsureSnowScene())
                return;
            foreach (HeatSource source in heatSources)
                if (!source.Retired)
                    ReindexHeatSource(source);
            foreach (SnowRegion region in regionList)
                QueueRegion(region, SnowRefresh.Heat | SnowRefresh.Links);
        }

        internal void SettleBeforeWeatherChange()
        {
            if (!simulationScene || !ZNet.instance || !winterRunning || !SeasonalSnow.WeatherReady)
                return;
            double now = ZNet.instance.GetTimeSeconds();
            if (now >= lastWorldSeconds && now - lastWorldSeconds < 5d)
                IntegrateSnow(now);
            weatherBoundary = now;
        }

        internal void WeatherTimelineChanged()
        {
            if (!EnsureSnowScene())
                return;
            weatherBoundary = ZNet.instance.GetTimeSeconds();
            foreach (SnowRegion region in regionList)
                QueueRegion(region, SnowRefresh.Area | SnowRefresh.Heat);
        }

        internal void RequestSnowCatchUp()
        {
            if (!simulationScene || !ZNet.instance || catchingUp)
                return;
            foreach (SnowRegion region in regionList)
            {
                region.ResumeFromWorld = region.LastReadyWorld;
                region.ReadyGeneration++;
                QueueRegion(region, SnowRefresh.CatchUp);
            }
            catchingUp = true;
        }

        internal void SnowSnapshotReceived(ZDO zdo)
        {
            if (zdo == null || !snowIds.TryGetValue(zdo.m_uid, out SnowPiece state) || !ReferenceEquals(state.Zdo, zdo))
                return;
            QueueRefresh(state, SnowRefresh.Snapshot);
        }

        internal void SnowPositionChanged(ZDO zdo)
        {
            if (zdo != null && snowIds.TryGetValue(zdo.m_uid, out SnowPiece state) &&
                ReferenceEquals(state.Zdo, zdo) && zdo.GetPosition() != state.Position)
                QueueRefresh(state, SnowRefresh.Geometry | SnowRefresh.Links | SnowRefresh.Area);
        }

        internal void BeforeSnowViewReset(ZNetView view)
        {
            ZDO zdo = view ? view.GetZDO() : null;
            if (zdo == null || !snowIds.TryGetValue(zdo.m_uid, out SnowPiece state) || state.View != view)
                return;
            FlushSnowPiece(state);
            RetireSnow(state, releaseVisual: false);
            InvalidateSnowArea(state.Position, geometry: true);
        }

        internal void FlushSnow()
        {
            foreach (SnowPiece state in snowPieces.Values)
                FlushSnowPiece(state);
        }

        private void FlushSnowPiece(SnowPiece state)
        {
            if (!state.Valid || !state.Confirmed || !state.View.IsOwner() || !ZNet.instance)
                return;
            if (!SeasonalSnow.WinterReady || endingSnowWinter)
            {
                SeasonalSnowStorage.Clear(state.Zdo, clearNative: true);
                return;
            }
            if (state.Epoch != SeasonalSnowStorage.CurrentWinterEpoch)
                return;
            // Never mark an unprocessed catch-up interval as consumed just because an
            // object unloads or the save callback runs before its queued refresh.
            bool settled = state.Region.Ready && state.ReadyGeneration == state.Region.ReadyGeneration &&
                double.IsNaN(state.CatchUpFrom) && SeasonalSnow.WeatherReady;
            if (settled)
                IntegratePiece(state, ZNet.instance.GetTimeSeconds(), snowClock);
            double consumed = settled ? state.WeatherTime :
                Math.Max(state.WeatherTime, state.Region.PauseAtWorld);
            SeasonalSnowStorage.Write(state.Zdo, state.Snow, state.Epoch, consumed);
            RememberSnapshot(state, new SeasonalSnowStorage.Snapshot(state.Zdo));
        }

        internal void StopSnowScene(ZNetScene scene)
        {
            if (!ReferenceEquals(scene, simulationScene) && scene != ZNetScene.instance)
                return;
            FlushSnow();
            ResetSnowRuntime();
            stoppedSnowScene = scene;
        }

        internal void ResetSnowRuntime()
        {
            foreach (SnowPiece state in snowPieces.Values)
            {
                state.Retired = true;
                if (state.View)
                    state.View.Unregister(UseSnowRpc);
            }
            snowPieces.Clear();
            snowIds.Clear();
            snowRegions.Clear();
            regionList.Clear();
            regionRefreshes.Clear();
            pieceRefreshes.Clear();
            geometryRefreshes.Clear();
            snowPublications.Clear();
            interactingPieces.Clear();
            expiredInteractions.Clear();
            frameWeather.Clear();
            movingSnowDoors.Clear();
            completedSnowDoors.Clear();
            nextDoorCheck = 0f;
            ResetHeatSources();
            observedStation = null;
            stationSnowPiece = null;
            observedAttachment = null;
            attachedSnowPiece = null;
            simulationScene = null;
            discoveryCursor = sourceDiscoveryCursor = -1;
            readinessCursor = 0;
            simulationFrame = -1;
            winterRunning = endingSnowWinter = catchingUp = false;
            winterEpoch = 0L;
            snowClock = lastWorldSeconds = weatherBoundary = 0d;
        }

        private static void RememberSnapshot(SnowPiece state, SeasonalSnowStorage.Snapshot snapshot)
        {
            state.SnapshotPresent = snapshot.Present;
            state.SnapshotValue = snapshot.Value;
            state.SnapshotBaseline = snapshot.Baseline;
            state.SnapshotTime = snapshot.From;
            state.SnapshotEpoch = snapshot.Epoch;
        }
    }
}
