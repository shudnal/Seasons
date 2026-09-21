using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    internal sealed partial class SeasonalSnowController
    {
        internal void UpdateSnowSimulation(ZNetScene scene)
        {
            if (!EnsureSnowScene() || scene != simulationScene || simulationFrame == Time.frameCount ||
                Game.IsPaused() || Time.timeScale <= 0f)
                return;
            simulationFrame = Time.frameCount;
            double now = ZNet.instance.GetTimeSeconds();
            if (endingSnowWinter || !SeasonalSnow.WinterReady)
            {
                if (winterRunning || endingSnowWinter || snowPieces.Count != 0)
                    EndSnowWinter();
                lastWorldSeconds = now;
                return;
            }
            long epoch = SeasonalSnowStorage.CurrentWinterEpoch;
            if (!winterRunning || winterEpoch != epoch)
            {
                winterRunning = true;
                winterEpoch = epoch;
                discoveryCursor = sourceDiscoveryCursor = 0;
                foreach (SnowRegion region in regionList)
                    QueueRegion(region, SnowRefresh.All);
            }
            if (now < lastWorldSeconds || now - lastWorldSeconds > Math.Max(5d, Time.deltaTime * 10d))
                RequestSnowCatchUp();
            snowClock += Math.Max(0f, Time.deltaTime);
            DiscoverSnowInstances();
            UpdateHeatSources();
            UpdateSnowDoors();
            ExpireSnowInteractions();
            if (observedShieldRevision != ShieldGenerator.m_instanceChangeID)
            {
                observedShieldRevision = ShieldGenerator.m_instanceChangeID;
                foreach (SnowRegion region in regionList)
                    QueueRegion(region, SnowRefresh.Heat);
            }
            ObserveSnowReadiness();
            ExpandRegionRefreshes();
            ProcessSnowRefreshes();
            if (SeasonalSnow.WeatherReady)
                IntegrateSnow(now);
            PublishSnowChanges();
            foreach (SnowRegion region in regionList)
                if (region.Ready)
                {
                    region.LastReadyWorld = now;
                    region.LastReadyTime = snowClock;
                }
            lastWorldSeconds = now;
            catchingUp = false;
        }

        private void DiscoverSnowInstances()
        {
            if (discoveryCursor >= 0)
            {
                List<WearNTear> pieces = WearNTear.GetAllInstances();
                int left = 64;
                while (left-- > 0 && discoveryCursor < pieces.Count)
                    RegisterSnow(pieces[discoveryCursor++]);
                if (discoveryCursor >= pieces.Count)
                    discoveryCursor = -1;
            }
            if (sourceDiscoveryCursor >= 0)
            {
                List<EffectArea> areas = EffectArea.s_allAreas;
                int left = 8;
                while (left-- > 0 && sourceDiscoveryCursor < areas.Count)
                    RegisterHeatArea(areas[sourceDiscoveryCursor++]);
                if (sourceDiscoveryCursor >= areas.Count)
                    sourceDiscoveryCursor = -1;
            }
        }

        private void ObserveSnowReadiness()
        {
            int candidates = Math.Min(16, regionList.Count);
            int checks = 2;
            while (candidates-- > 0 && regionList.Count != 0)
            {
                readinessCursor %= regionList.Count;
                SnowRegion region = regionList[readinessCursor++];
                if (!region.ReadinessDirty && (region.Ready || Time.time < region.NextReadinessCheck))
                    continue;
                region.NextReadinessCheck = Time.time + 0.5f;
                bool ready = false;
                if (ZoneSystem.instance.IsZoneLoaded(region.Zone))
                {
                    if (checks-- <= 0)
                        break;
                    ready = simulationScene.IsAreaReady(region.Center);
                }
                region.ReadinessDirty = false;
                if (region.Ready == ready)
                    continue;
                if (!ready)
                {
                    region.PauseAtWorld = region.LastReadyWorld;
                    region.PauseAtTime = region.LastReadyTime;
                }
                else
                    region.ResumeFromWorld = region.PauseAtWorld;
                region.Ready = ready;
                region.ReadyGeneration++;
                QueueRegion(region, SnowRefresh.Geometry | SnowRefresh.Links | SnowRefresh.Area);
            }
        }

        private void ExpandRegionRefreshes()
        {
            int left = 128;
            while (left-- > 0 && regionRefreshes.Count != 0)
            {
                SnowRegion region = regionRefreshes.Peek();
                if (region.Cursor < region.Pieces.Count)
                {
                    SnowPiece state = region.Pieces[region.Cursor++];
                    if ((region.Scanning & SnowRefresh.CatchUp) != 0)
                        state.CatchUpFrom = Math.Max(state.WeatherTime, region.ResumeFromWorld);
                    QueueRefresh(state, region.Scanning);
                    continue;
                }
                regionRefreshes.Dequeue();
                region.Queued = false;
                region.Scanning = SnowRefresh.None;
                if (region.Pieces.Count != 0 && region.Pending != SnowRefresh.None)
                    QueueRegion(region, SnowRefresh.None);
            }
        }

        private void ProcessSnowRefreshes()
        {
            int cheap = RefreshesPerFrame;
            while (cheap-- > 0 && pieceRefreshes.Count != 0)
            {
                SnowPiece state = pieceRefreshes.Dequeue();
                if (state.Retired)
                    continue;
                if ((state.Refresh & (SnowRefresh.Geometry | SnowRefresh.Rules | SnowRefresh.Links)) != 0 ||
                    (!state.Confirmed && state.Region.Ready))
                {
                    geometryRefreshes.Enqueue(state);
                    continue;
                }
                RefreshSnowPiece(state);
            }
            if (sourceDiscoveryCursor >= 0)
                return;
            long start = Stopwatch.GetTimestamp();
            int costly = GeometryPerFrame;
            while (costly-- > 0 && geometryRefreshes.Count != 0)
            {
                SnowPiece state = geometryRefreshes.Dequeue();
                if (!state.Retired)
                    RefreshSnowPiece(state);
                if ((Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency >= 0.001d)
                    break;
            }
        }

        private void RefreshSnowPiece(SnowPiece state)
        {
            SnowRefresh reasons = state.Refresh;
            state.Refresh = SnowRefresh.None;
            state.RefreshQueued = false;
            if (!state.Valid)
            {
                RetireSnow(state, releaseVisual: true);
                return;
            }
            double now = ZNet.instance.GetTimeSeconds();
            Vector3 position = state.Transform.position;
            if (position != state.Position)
            {
                IntegratePiece(state, now, snowClock);
                MoveSnowPiece(state, position);
                reasons |= SnowRefresh.Geometry | SnowRefresh.Links | SnowRefresh.Rules;
            }
            if ((reasons & SnowRefresh.Rules) != 0 && !SeasonalSnow.IsSeasonalSnowPosition(state.Piece))
            {
                if (SeasonalSnowStorage.CanWrite(state.View, state.Zdo))
                    SeasonalSnowStorage.Clear(state.Zdo, clearNative: true);
                QueueVisual(state.Piece, 0f, disabled: false, force: true);
                RetireSnow(state, releaseVisual: false);
                return;
            }
            long epoch = SeasonalSnowStorage.CurrentWinterEpoch;
            bool newEpoch = state.Epoch != epoch;
            if (newEpoch)
            {
                state.Epoch = epoch;
                state.Confirmed = state.Construction = state.Saved = state.Simulates = state.AppearanceChosen = false;
                state.AllowOwnerlessPublication = false;
                state.CatchUpFrom = double.NaN;
            }
            bool resumed = state.ReadyGeneration != state.Region.ReadyGeneration;
            if (state.Confirmed && !resumed && !newEpoch && double.IsNaN(state.CatchUpFrom))
                IntegratePiece(state, now, snowClock);
            if ((reasons & SnowRefresh.Rules) != 0 || newEpoch)
            {
                Vector2 range = SeasonalSnow.GetSnowBuildupRange(state.Piece);
                state.Minimum = range.x;
                state.Maximum = range.y;
                state.Snow = Mathf.Min(state.Maximum, state.Snow);
                state.Biome = SeasonalSnow.GetBiome(state.Piece);
            }

            long previousOwner = state.Owner;
            long owner = state.Zdo.GetOwner();
            bool ownershipChanged = owner != previousOwner;
            state.Owner = owner;
            SeasonalSnowStorage.Snapshot snapshot = new SeasonalSnowStorage.Snapshot(state.Zdo);
            bool snapshotChanged = snapshot.Present != state.SnapshotPresent || snapshot.Epoch != state.SnapshotEpoch ||
                snapshot.From != state.SnapshotTime || !snapshot.Value.Equals(state.SnapshotValue) ||
                !snapshot.Baseline.Equals(state.SnapshotBaseline);
            if (newEpoch || !state.Confirmed || snapshotChanged)
            {
                state.Saved = snapshot.AppliesTo(epoch);
                if (state.Saved)
                {
                    state.Snow = Mathf.Min(state.Maximum, snapshot.Value);
                    state.AppearanceChosen = true;
                }
                if (snapshotChanged && state.Confirmed)
                {
                    state.WeatherTime = Math.Min(now, SeasonalSnowStorage.FromTimestamp(snapshot.From));
                    state.LastHeatTime = snowClock;
                }
                RememberSnapshot(state, snapshot);
            }
            state.MayPublish = owner != 0L && state.View.IsOwner();
            if (!state.Region.Ready || !SeasonalSnow.WeatherReady)
            {
                state.Simulates = false;
                // Keep saved-first appearance before surrounding geometry is ready.
                if (!state.Confirmed && state.Saved)
                    QueueRuntimeVisual(state, force: true);
                else if (!state.Confirmed && !state.AppearanceChosen && !state.Construction &&
                    (owner == 0L || state.MayPublish) && SeasonalSnow.WeatherReady)
                {
                    state.Snow = Mathf.Min(state.Maximum, state.Minimum + SeasonalSnow.GetCumulativeSnowGainAt(state.Biome, now));
                    state.AppearanceChosen = true;
                    QueueRuntimeVisual(state, force: true);
                }
                Classify(state);
                return;
            }
            if (!state.Confirmed && owner != 0L && !state.MayPublish && !state.Saved)
                return;

            bool geometryChanged = !state.GeometryCaptured || (reasons & SnowRefresh.Geometry) != 0 || resumed;
            if (geometryChanged)
            {
                CaptureSnowGeometry(state);
                state.GeometryCaptured = true;
                state.Covered = HasSnowCover(state);
            }
            state.Shielded = ShieldGenerator.IsInsideShieldCached(state.Position, ref state.Piece.m_shieldChangeID);
            if (!state.Confirmed || resumed || (reasons & (SnowRefresh.Links | SnowRefresh.Rules)) != 0)
                RebuildHeatLinks(state);
            else if (geometryChanged || (reasons & SnowRefresh.Heat) != 0)
                RecalculateHeat(state);

            bool confirmation = !state.Confirmed;
            bool accountedGap = false;
            if (confirmation)
            {
                float target = state.Snow;
                if (state.Shielded || (!state.Saved && state.Covered))
                    target = 0f;
                else if (!state.Saved)
                    target = state.Construction ? 0f : state.Minimum + SeasonalSnow.GetCumulativeSnowGainAt(state.Biome, now);
                else if (!state.Covered && (owner == 0L || state.MayPublish) && snapshot.CurrentWinter && snapshot.From > 0L)
                {
                    float baseline = snapshot.Epoch == SeasonalSnowStorage.MissingEpoch && !state.Construction
                        ? Mathf.Max(state.Minimum, snapshot.Baseline) : snapshot.Baseline;
                    target = Mathf.Max(target, baseline + SeasonalSnow.GainBetween(state.Biome,
                        SeasonalSnowStorage.FromTimestamp(snapshot.From), now));
                }
                state.Snow = Mathf.Clamp(target, 0f, state.Maximum);
                state.Confirmed = state.AppearanceChosen = true;
                state.AllowOwnerlessPublication = owner == 0L;
                state.WeatherTime = now;
                state.LastHeatTime = snowClock;
                accountedGap = true;
            }
            else if (resumed || !double.IsNaN(state.CatchUpFrom))
            {
                double from = !double.IsNaN(state.CatchUpFrom) ? state.CatchUpFrom :
                    Math.Max(state.WeatherTime, state.Region.ResumeFromWorld);
                if (!state.Covered && !state.Shielded && (owner == 0L || state.MayPublish))
                    state.Snow = Mathf.Min(state.Maximum, state.Snow + SeasonalSnow.GainBetween(state.Biome, from, now));
                state.WeatherTime = now;
                state.LastHeatTime = snowClock;
                accountedGap = true;
            }
            // A true foreign owner supplied snapshots while we did not simulate. A local
            // ownerless calculation, in contrast, must continue without replaying its gap.
            if (!accountedGap && ownershipChanged && previousOwner != 0L && state.MayPublish &&
                state.Saved && snapshot.From > 0L && !state.Covered && !state.Shielded)
            {
                state.Snow = Mathf.Min(state.Maximum, Mathf.Max(state.Snow, snapshot.Baseline +
                    SeasonalSnow.GainBetween(state.Biome, SeasonalSnowStorage.FromTimestamp(snapshot.From), now)));
                state.WeatherTime = now;
                state.LastHeatTime = snowClock;
            }
            if (state.Shielded)
                state.Snow = 0f;
            state.CatchUpFrom = double.NaN;
            state.ReadyGeneration = state.Region.ReadyGeneration;
            state.Simulates = owner == 0L || state.MayPublish;
            bool wasMelting = state.Melting;
            bool interactive = state.InteractiveUntil > Time.time && !simulationScene.OutsideActiveArea(state.Position) &&
                seasonalSnowInteractiveObjectMeltMultiplier.Value > 0f;
            state.Melting = state.Covered || state.HeatRate > 0f || interactive || state.Shielded;
            state.MeltRate = state.Shielded ? 0f :
                (state.Covered ? UnitMeltRate * Mathf.Max(0f, seasonalSnowCoveredPieceMeltMultiplier.Value) : 0f) +
                (interactive ? InteractiveMeltRate * Mathf.Max(0f, seasonalSnowInteractiveObjectMeltMultiplier.Value) : state.HeatRate);
            if (float.IsNaN(state.MeltRate) || float.IsInfinity(state.MeltRate))
                state.MeltRate = 0f;
            Classify(state);
            QueueRuntimeVisual(state, force: confirmation || resumed || snapshotChanged || state.Shielded);
            if (confirmation || ownershipChanged || wasMelting != state.Melting || resumed ||
                (reasons & (SnowRefresh.Rules | SnowRefresh.CatchUp)) != 0)
                QueuePublication(state, force: true);
        }

        private void IntegrateSnow(double now)
        {
            frameWeather.Clear();
            foreach (Heightmap.Biome biome in SeasonalSnow.SeasonalSnowTimelines.Keys)
                frameWeather[biome] = SeasonalSnow.GainBetween(biome, Math.Max(lastWorldSeconds, weatherBoundary), now);
            bool anySnow = false;
            foreach (float gain in frameWeather.Values)
                anySnow |= gain > 0f;
            foreach (SnowRegion region in regionList)
            {
                if (!region.Ready)
                    continue;
                IntegrateBucket(region.Buckets[(int)SnowBucket.MeltingSnow], now, onlySnowing: false);
                if (anySnow)
                    IntegrateBucket(region.Buckets[(int)SnowBucket.AccumulationOpen], now, onlySnowing: true);
            }
        }

        private void IntegrateBucket(List<SnowPiece> list, double now, bool onlySnowing)
        {
            int index = 0;
            while (index < list.Count)
            {
                SnowPiece state = list[index];
                if (onlySnowing ? frameWeather.TryGetValue(state.Biome, out float gain) && gain > 0f : state.MeltRate > 0f)
                    IntegratePiece(state, now, snowClock);
                if (index < list.Count && ReferenceEquals(list[index], state))
                    index++;
            }
        }

        private void IntegratePiece(SnowPiece state, double now, double clock)
        {
            if (!state.Confirmed || !state.Simulates || !state.Region.Ready ||
                state.ReadyGeneration != state.Region.ReadyGeneration || state.Epoch != winterEpoch ||
                !double.IsNaN(state.CatchUpFrom))
                return;
            float previous = state.Snow;
            if (state.Melting)
                state.Snow = Mathf.Max(0f, state.Snow - state.MeltRate * (float)Math.Max(0d, clock - state.LastHeatTime));
            else
                state.Snow = Mathf.Min(state.Maximum, state.Snow + SeasonalSnow.GainBetween(state.Biome,
                    Math.Max(state.WeatherTime, weatherBoundary), now));
            state.LastHeatTime = clock;
            state.WeatherTime = now;
            if (previous.Equals(state.Snow))
                return;
            Classify(state);
            QueueRuntimeVisual(state, force: state.Snow <= 0f || state.Snow >= state.Maximum);
            if (state.MayPublish && (Mathf.Abs(state.Snow - state.SnapshotValue) >= PublicationStep ||
                state.Snow <= 0f || state.Snow >= state.Maximum))
                QueuePublication(state, force: state.Snow <= 0f || state.Snow >= state.Maximum);
        }

        private void QueueRuntimeVisual(SnowPiece state, bool force)
        {
            if (ZNet.instance && ZNet.instance.IsDedicated())
                return;
            float previous = state.VisualSnow;
            bool visible = state.Snow > VisibilityThreshold;
            if (!force && !float.IsNaN(previous) && visible == (previous > VisibilityThreshold) &&
                Mathf.Abs(state.Snow - previous) / (1f - VisibilityThreshold) < VisualStep)
                return;
            state.VisualSnow = state.Snow;
            QueueVisual(state.Piece, state.Snow, disabled: false, force: force);
        }

        private void PublishSnowChanges()
        {
            int remaining = PublicationsPerFrame;
            while (remaining-- > 0 && snowPublications.Count != 0)
            {
                SnowPiece state = snowPublications.Dequeue();
                state.PublishQueued = false;
                bool force = state.ForcePublish;
                state.ForcePublish = false;
                if (!state.Valid || !state.Confirmed || !state.Region.Ready || state.Epoch != winterEpoch ||
                    state.ReadyGeneration != state.Region.ReadyGeneration || !double.IsNaN(state.CatchUpFrom) ||
                    (!state.View.IsOwner() && !(state.AllowOwnerlessPublication && state.Zdo.GetOwner() == 0L)))
                    continue;
                if (!force && Mathf.Abs(state.Snow - state.SnapshotValue) < PublicationStep)
                    continue;
                double consumed = Math.Max(state.WeatherTime, state.Region.LastReadyWorld);
                SeasonalSnowStorage.Write(state.Zdo, state.Snow, state.Epoch, consumed);
                state.AllowOwnerlessPublication = false;
                RememberSnapshot(state, new SeasonalSnowStorage.Snapshot(state.Zdo));
            }
        }

        private void EndSnowWinter()
        {
            // Stop arithmetic immediately, but spread persistence and retirement over
            // frames independently of the high-priority visual hide queue.
            winterRunning = false;
            endingSnowWinter = true;
            discoveryCursor = sourceDiscoveryCursor = -1;
            int remaining = RefreshesPerFrame;
            while (remaining-- > 0 && regionList.Count != 0)
            {
                SnowRegion region = regionList[regionList.Count - 1];
                SnowPiece state = region.Pieces[region.Pieces.Count - 1];
                if (state.Valid && SeasonalSnowStorage.CanWrite(state.View, state.Zdo))
                    SeasonalSnowStorage.Clear(state.Zdo, clearNative: true);
                if (state.Piece)
                    QueueVisual(state.Piece, 0f, disabled: false, force: true);
                RetireSnow(state, releaseVisual: false);
            }
            if (regionList.Count != 0)
                return;
            regionRefreshes.Clear();
            pieceRefreshes.Clear();
            geometryRefreshes.Clear();
            snowPublications.Clear();
            movingSnowDoors.Clear();
            completedSnowDoors.Clear();
            endingSnowWinter = false;
        }
    }
}
