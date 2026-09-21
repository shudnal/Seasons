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
            bool weatherReady = SeasonalSnow.WeatherReady;
            if (snowWeatherReady != weatherReady)
            {
                if (snowWeatherReady)
                    RequestSnowCatchUp();
                else
                    foreach (SnowRegion region in regionList)
                        QueueRegion(region, SnowRefresh.Area | SnowRefresh.CatchUp);
                snowWeatherReady = weatherReady;
            }
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
            PrepareSnowVisuals();
            foreach (SnowRegion region in regionList)
                if (region.Ready && weatherReady)
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
                    PauseSnowRegion(region, region.PauseAtWorld);
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
                if ((state.Refresh & (SnowRefresh.Geometry | SnowRefresh.Links)) != 0 ||
                    (state.Region.Ready && state.GeometryRevision != state.Region.GeometryRevision) ||
                    (!state.Confirmed && state.Region.Ready))
                {
                    geometryRefreshes.Enqueue(state);
                    continue;
                }
                RefreshSnowPiece(state);
            }
            if (sourceDiscoveryCursor >= 0 || HasPendingHeatGeometry)
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
                if (visuals.ContainsKey(state.Piece))
                    QueueVisual(state.Piece, 0f, disabled: false, force: true);
                RetireSnow(state, releaseVisual: false);
                return;
            }
            long epoch = SeasonalSnowStorage.CurrentWinterEpoch;
            bool newEpoch = state.Epoch != epoch;
            bool resumed = state.ReadyGeneration != state.Region.ReadyGeneration;
            if (state.Confirmed && !resumed && !newEpoch && double.IsNaN(state.CatchUpFrom) && SeasonalSnow.WeatherReady)
                IntegratePiece(state, now, snowClock);
            if (newEpoch)
            {
                state.Epoch = epoch;
                state.Confirmed = state.Saved = state.Simulates = state.AppearanceChosen = false;
                state.Construction = SeasonalSnowStorage.IsCurrentWinterPlacement(state.Zdo);
                state.AllowOwnerlessPublication = state.AllowInitialPublication = false;
                state.CatchUpFrom = double.NaN;
            }
            if ((reasons & SnowRefresh.Rules) != 0 || newEpoch)
            {
                Vector2 range = SeasonalSnow.GetSnowBuildupRange(state.Piece);
                state.Minimum = range.x;
                state.Maximum = range.y;
                state.Biome = SeasonalSnow.GetBiome(state.Piece);
            }

            long previousOwner = state.Owner;
            long owner = state.Zdo.GetOwner();
            bool ownershipChanged = owner != previousOwner;
            bool localOwner = owner != 0L && state.View.IsOwner();
            bool localCalculation = owner == 0L || localOwner;
            state.Owner = owner;
            if (ownershipChanged && localCalculation)
                SeasonalSnowStorage.MigrateLoaded(state.Piece);
            SeasonalSnowStorage.Snapshot snapshot = new SeasonalSnowStorage.Snapshot(state.Zdo);
            bool snapshotChanged = !SnapshotUnchanged(state, snapshot);
            bool acceptSnapshot = (newEpoch || !state.Confirmed || snapshotChanged) && snapshot.AppliesTo(epoch);
            float target = newEpoch ? 0f : state.Snow;
            if (acceptSnapshot)
            {
                target = snapshot.Value;
                state.Saved = state.AppearanceChosen = true;
            }
            else if (newEpoch)
                state.Saved = false;
            RememberSnapshot(state, snapshot);
            bool previouslyPublished = state.MayPublish;
            state.MayPublish = localOwner && !simulationScene.OutsideActiveArea(state.Position);
            bool shielded = ShieldGenerator.IsInsideShieldCached(state.Position, ref state.Piece.m_shieldChangeID);
            bool shieldChanged = shielded != state.Shielded;
            state.Shielded = shielded;
            if (!state.Region.Ready || !SeasonalSnow.WeatherReady)
            {
                state.Simulates = false;
                if (!state.Confirmed && !state.Saved && (!state.AppearanceChosen || shieldChanged) &&
                    localCalculation && SeasonalSnow.WeatherReady)
                {
                    target = state.Construction ? 0f :
                        state.Minimum + SeasonalSnow.GetCumulativeSnowGainAt(state.Biome, now);
                    state.AppearanceChosen = true;
                }
                if (acceptSnapshot && state.Confirmed)
                {
                    state.WeatherTime = Math.Min(now, SeasonalSnowStorage.FromTimestamp(snapshot.From));
                    state.CatchUpFrom = state.WeatherTime;
                }
                state.Snow = shielded ? 0f : Mathf.Clamp(target, 0f, state.Maximum);
                if (state.AppearanceChosen || newEpoch || shielded)
                    QueueRuntimeVisual(state, force: acceptSnapshot || newEpoch || shieldChanged);
                Classify(state);
                return;
            }
            if (!state.Confirmed && !localCalculation && !state.Saved)
                return;

            bool geometryChanged = !state.GeometryCaptured || state.GeometryRevision != state.Region.GeometryRevision ||
                (reasons & SnowRefresh.Geometry) != 0;
            if (geometryChanged)
            {
                CaptureSnowGeometry(state);
                state.GeometryCaptured = true;
                state.Covered = HasSnowCover(state);
                state.GeometryRevision = state.Region.GeometryRevision;
            }
            if (!state.Confirmed || (reasons & SnowRefresh.Links) != 0)
                RebuildHeatLinks(state);
            else if (geometryChanged || (reasons & SnowRefresh.Heat) != 0)
                RecalculateHeat(state);

            bool confirmation = !state.Confirmed;
            bool catchUp = resumed || !double.IsNaN(state.CatchUpFrom) ||
                (ownershipChanged && previousOwner != 0L && localCalculation) ||
                (acceptSnapshot && state.Confirmed);
            double from = acceptSnapshot ? SeasonalSnowStorage.FromTimestamp(snapshot.From) :
                !double.IsNaN(state.CatchUpFrom) ? state.CatchUpFrom : state.WeatherTime;
            if (confirmation)
            {
                if (!state.Saved)
                {
                    from = state.Construction ? SeasonalSnowStorage.PlacementTime(state.Zdo) : now;
                    target = state.Construction || state.Covered ? 0f :
                        state.Minimum + SeasonalSnow.GetCumulativeSnowGainAt(state.Biome, now);
                    if (state.Construction && localCalculation && !state.Covered)
                        target += SeasonalSnow.GainBetween(state.Biome, from, now);
                }
                else if (localCalculation && !state.Covered && snapshot.From > 0L)
                {
                    // Legacy baselines may predate a larger saved value. Never reinsert
                    // the discovery minimum, including when the saved value is zero.
                    target = Mathf.Max(target, snapshot.Baseline + SeasonalSnow.GainBetween(state.Biome, from, now));
                }
                state.Confirmed = state.AppearanceChosen = true;
                state.AllowOwnerlessPublication = owner == 0L;
                state.AllowInitialPublication = localOwner;
            }
            else if (catchUp && localCalculation && !state.Covered && !state.Shielded)
                target += SeasonalSnow.GainBetween(state.Biome, from, now);

            if (state.Shielded)
                target = 0f;
            state.Snow = Mathf.Clamp(target, 0f, state.Maximum);
            state.WeatherTime = localCalculation ? now : acceptSnapshot ? Math.Min(now, from) : state.WeatherTime;
            state.WeatherGain = SeasonalSnow.GetCumulativeSnowGainAt(state.Biome, state.WeatherTime);
            state.LastHeatTime = snowClock;
            state.CatchUpFrom = double.NaN;
            state.ReadyGeneration = state.Region.ReadyGeneration;
            state.Simulates = localCalculation;
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
            if (confirmation || ownershipChanged || (!previouslyPublished && state.MayPublish) || wasMelting != state.Melting || resumed ||
                (reasons & (SnowRefresh.Rules | SnowRefresh.CatchUp)) != 0)
                QueuePublication(state, force: true);
        }
        private void IntegrateSnow(double now)
        {
            frameWeather.Clear();
            snowingBiomes.Clear();
            foreach (Heightmap.Biome biome in SeasonalSnow.SeasonalSnowTimelines.Keys)
            {
                frameWeather[biome] = SeasonalSnow.GetCumulativeSnowGainAt(biome, now);
                if (SeasonalSnow.GainBetween(biome, lastWorldSeconds, now) > 0f)
                    snowingBiomes.Add(biome);
            }
            foreach (SnowRegion region in regionList)
            {
                if (!region.Ready)
                    continue;
                foreach (List<SnowPiece> group in region.Buckets[(int)SnowBucket.MeltingSnow].Values)
                    IntegrateBucket(group, now, onlySnowing: false);
                if (snowingBiomes.Count != 0)
                    foreach (KeyValuePair<Heightmap.Biome, List<SnowPiece>> group in region.Buckets[(int)SnowBucket.AccumulationOpen])
                        if (snowingBiomes.Contains(group.Key))
                            IntegrateBucket(group.Value, now, onlySnowing: true);
            }
        }

        private void IntegrateBucket(List<SnowPiece> list, double now, bool onlySnowing)
        {
            int index = 0;
            while (index < list.Count)
            {
                SnowPiece state = list[index];
                frameWeather.TryGetValue(state.Biome, out float gain);
                if (onlySnowing ? gain > state.WeatherGain : state.MeltRate > 0f)
                    IntegratePiece(state, now, snowClock, gain);
                if (index < list.Count && ReferenceEquals(list[index], state))
                    index++;
            }
        }

        private void IntegratePiece(SnowPiece state, double now, double clock, float? cumulativeGain = null)
        {
            if (!state.Confirmed || !state.Simulates || !state.Region.Ready ||
                state.ReadyGeneration != state.Region.ReadyGeneration || state.Epoch != winterEpoch ||
                !double.IsNaN(state.CatchUpFrom))
                return;
            float previous = state.Snow;
            float gain = cumulativeGain ?? SeasonalSnow.GetCumulativeSnowGainAt(state.Biome, now);
            if (state.Melting)
                state.Snow = Mathf.Max(0f, state.Snow - state.MeltRate * (float)Math.Max(0d, clock - state.LastHeatTime));
            else
                state.Snow = Mathf.Min(state.Maximum, state.Snow + Mathf.Max(0f, gain - state.WeatherGain));
            state.LastHeatTime = clock;
            state.WeatherTime = now;
            state.WeatherGain = gain;
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
            if (snowHeadless || state.Retired)
                return;
            state.ForceVisual |= force;
            if (state.VisualQueued)
                return;
            state.VisualQueued = true;
            snowVisualChanges.Enqueue(state);
        }

        internal bool RequestSnowVisual(WearNTear piece, bool force)
        {
            RegisterSnow(piece);
            if (!snowPieces.TryGetValue(piece, out SnowPiece state))
                return false;
            QueueRuntimeVisual(state, force);
            return true;
        }

        internal bool TryGetRuntimeSnow(WearNTear piece, out float value)
        {
            value = 0f;
            if (!piece || !snowPieces.TryGetValue(piece, out SnowPiece state) || !state.Valid)
                return false;
            value = endingSnowWinter || !SeasonalSnow.WinterReady ? 0f : state.Snow;
            return true;
        }

        private void PrepareSnowVisuals()
        {
            int remaining = VisualsPerFrame;
            while (remaining-- > 0 && snowVisualChanges.Count != 0)
            {
                SnowPiece state = snowVisualChanges.Dequeue();
                state.VisualQueued = false;
                bool force = state.ForceVisual;
                state.ForceVisual = false;
                if (state.Valid)
                    QueueVisual(state.Piece, state.Snow, disabled: false, force: force);
            }
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
                    (!state.View.IsOwner() && !(state.AllowOwnerlessPublication && state.Zdo.GetOwner() == 0L)) ||
                    (state.View.IsOwner() && !state.AllowInitialPublication && simulationScene.OutsideActiveArea(state.Position)))
                    continue;
                if (!force && Mathf.Abs(state.Snow - state.SnapshotValue) < PublicationStep)
                    continue;
                if (!SnapshotUnchanged(state, new SeasonalSnowStorage.Snapshot(state.Zdo)))
                {
                    QueueRefresh(state, SnowRefresh.Snapshot);
                    continue;
                }
                SeasonalSnowStorage.Write(state.Zdo, state.Snow, state.Epoch, state.WeatherTime);
                state.AllowOwnerlessPublication = state.AllowInitialPublication = false;
                RememberSnapshot(state, new SeasonalSnowStorage.Snapshot(state.Zdo));
            }
        }

        private void EndSnowWinter()
        {
            // Stop arithmetic immediately, but spread persistence and retirement over
            // frames independently of the high-priority visual hide queue.
            if (!endingSnowWinter)
                foreach (SnowPiece state in snowPieces.Values)
                {
                    state.Snow = 0f;
                    state.Simulates = false;
                }
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
                if (state.Piece && visuals.ContainsKey(state.Piece))
                    QueueVisual(state.Piece, 0f, disabled: false, force: true);
                RetireSnow(state, releaseVisual: false);
            }
            if (regionList.Count != 0)
                return;
            regionRefreshes.Clear();
            pieceRefreshes.Clear();
            geometryRefreshes.Clear();
            snowPublications.Clear();
            snowVisualChanges.Clear();
            movingSnowDoors.Clear();
            completedSnowDoors.Clear();
            endingSnowWinter = false;
        }
    }
}
