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
            // A summer load must not initialize registries, discover heat, inspect
            // area readiness or touch ZDOs. Only an unfinished winter cleanup runs.
            if (!SeasonalSnow.WinterReady && !winterRunning && !endingSnowWinter && snowPieces.Count == 0)
                return;
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
            ExpireSnowInteractions();
            if (observedShieldRevision != ShieldGenerator.m_instanceChangeID)
            {
                observedShieldRevision = ShieldGenerator.m_instanceChangeID;
                foreach (SnowRegion region in regionList)
                    QueueRegion(region, SnowRefresh.Heat);
            }
            ObserveSnowReadiness();
            ExpandRegionRefreshes();
            ProcessSnowRefreshes(now);
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
            long start = Stopwatch.GetTimestamp();
            while (candidates-- > 0 && regionList.Count != 0)
            {
                readinessCursor %= regionList.Count;
                SnowRegion region = regionList[readinessCursor++];
                if (region.Ready ? !region.ReadinessDirty : Time.time < region.NextReadinessCheck)
                    continue;
                // Creation notifications must not bypass the retry interval after a
                // failed check. A previously ready dirty region still gets prompt validation.
                bool ready = false;
                if (ZoneSystem.instance.IsZoneLoaded(region.Zone))
                {
                    if (checks <= 0 || (Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency >= 0.001d)
                        break;
                    checks--;
                    ready = simulationScene.IsAreaReady(region.Center);
                }
                // Only a completed check advances the retry clock or consumes dirtiness.
                region.NextReadinessCheck = Time.time + 0.5f;
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
                // The check just established readiness. Do not dirty it again merely
                // to notify pieces about that result.
                QueueRegion(region, SnowRefresh.Geometry | SnowRefresh.Links);
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

        private void ProcessSnowRefreshes(double now)
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
                RefreshSnowPiece(state, now);
            }
            if (sourceDiscoveryCursor >= 0 || HasPendingHeatGeometry)
                return;
            long start = Stopwatch.GetTimestamp();
            int costly = GeometryPerFrame;
            while (costly-- > 0 && geometryRefreshes.Count != 0)
            {
                SnowPiece state = geometryRefreshes.Dequeue();
                if (!state.Retired)
                    RefreshSnowPiece(state, now);
                if ((Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency >= 0.001d)
                    break;
            }
        }

        private void RetireInvalidSnowRule(SnowPiece state)
        {
            if (SeasonalSnowStorage.CanWrite(state.View, state.Zdo))
                SeasonalSnowStorage.Clear(state.Zdo, clearNative: true);
            if (visuals.ContainsKey(state.Piece))
                QueueVisual(state.Piece, 0f, disabled: false, force: true);
            RetireSnow(state, releaseVisual: false);
        }

        private void RefreshSnowPiece(SnowPiece state, double now)
        {
            SnowRefresh reasons = state.Refresh;
            state.Refresh = SnowRefresh.None;
            state.RefreshQueued = false;
            if (!state.Valid)
            {
                RetireSnow(state, releaseVisual: true);
                return;
            }
            Vector3 position = state.Transform.position;
            if (position != state.Position)
            {
                IntegratePiece(state, now, snowClock);
                MoveSnowPiece(state, position);
                reasons |= SnowRefresh.Geometry | SnowRefresh.Links | SnowRefresh.Rules;
            }
            long epoch = SeasonalSnowStorage.CurrentWinterEpoch;
            bool newEpoch = state.Epoch != epoch;
            if (((reasons & SnowRefresh.Rules) != 0 || newEpoch) &&
                !TryGetSeasonalSnowBiome(state.Piece, out Heightmap.Biome ruleBiome))
            {
                RetireInvalidSnowRule(state);
                return;
            }
            else if ((reasons & SnowRefresh.Rules) == 0 && !newEpoch)
                ruleBiome = state.Biome;

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
                state.Biome = ruleBiome;
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
                // A neighboring collider invalidates the cover result, not this
                // piece's cached colliders and local ray origin.
                if (!state.GeometryCaptured || (reasons & SnowRefresh.Rules) != 0)
                {
                    CaptureSnowGeometry(state);
                    state.GeometryCaptured = true;
                }
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
                    // Master stored an initial baseline of zero; the initial minimum
                    // was implicit in GetSnowTarget, not included in that baseline.
                    // Preserve an explicit zero and never apply this rule to new snapshots.
                    float baseline = snapshot.Baseline;
                    if (snapshot.Epoch == SeasonalSnowStorage.MissingEpoch && snapshot.CurrentWinter &&
                        snapshot.From <= SeasonalSnowStorage.ToTimestamp(SeasonalSnow.TimelineStartSeconds) && target > 0f)
                        baseline = Mathf.Max(baseline, state.Minimum);
                    target = Mathf.Max(target, baseline + SeasonalSnow.GainBetween(state.Biome, from, now));
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
                QueuePublication(state, force: false);
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
                foreach (KeyValuePair<Heightmap.Biome, List<SnowPiece>> group in region.Buckets[(int)SnowBucket.MeltingSnow])
                {
                    frameWeather.TryGetValue(group.Key, out float gain);
                    IntegrateBucket(group.Value, now, gain, onlySnowing: false);
                }
                if (snowingBiomes.Count != 0)
                    foreach (KeyValuePair<Heightmap.Biome, List<SnowPiece>> group in region.Buckets[(int)SnowBucket.AccumulationOpen])
                        if (snowingBiomes.Contains(group.Key))
                        {
                            frameWeather.TryGetValue(group.Key, out float gain);
                            IntegrateBucket(group.Value, now, gain, onlySnowing: true);
                        }
            }
        }

        private void IntegrateBucket(List<SnowPiece> list, double now, float gain, bool onlySnowing)
        {
            int index = 0;
            while (index < list.Count)
            {
                SnowPiece state = list[index];
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
            if (!visuals.ContainsKey(state.Piece) && state.Snow <= VisibilityThreshold)
            {
                state.ForceVisual = false;
                HideCapRoots(state.Piece);
                return;
            }
            if (visuals.TryGetValue(state.Piece, out VisualState visual) && !visual.Disabled && visual.Matches(state.Piece))
            {
                bool prioritizeHide = SetVisualTarget(visual, state.Snow, disabled: false);
                if (visual.Queued)
                {
                    // The renderer queue already reads the latest runtime value.
                    // Do not spend a second preparation slot on the same piece.
                    if (prioritizeHide)
                        EnqueueVisual(visual, prioritize: true);
                    state.ForceVisual = false;
                    return;
                }
                if (visual.HasApplied && visual.Target == visual.Applied &&
                    Mathf.Abs(visual.TargetLevel - visual.AppliedLevel) + 0.000001f < VisualStep &&
                    (!state.ForceVisual || visual.TargetLevel.Equals(visual.AppliedLevel)))
                {
                    state.ForceVisual = false;
                    return;
                }
            }
            state.VisualQueued = true;
            snowVisualChanges.Enqueue(state);
        }

        internal bool RequestSnowVisual(WearNTear piece, bool force)
        {
            if (!piece)
                return false;
            if (!snowPieces.TryGetValue(piece, out SnowPiece state) || !state.Valid)
            {
                RegisterSnow(piece);
                if (!snowPieces.TryGetValue(piece, out state) || !state.Valid)
                    return false;
            }
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

        private static bool NeedsSnowPublication(SnowPiece state, double consumed, bool exactValue)
        {
            if (!state.SnapshotPresent || state.SnapshotEpoch != state.Epoch ||
                state.SnapshotEpoch == SeasonalSnowStorage.MissingEpoch ||
                !state.SnapshotBaseline.Equals(state.SnapshotValue))
                return true;

            float delta = Mathf.Abs(state.Snow - state.SnapshotValue);
            if (delta > 0f && (exactValue || delta >= PublicationStep ||
                state.Snow <= 0f || state.Snow >= state.Maximum))
                return true;

            double persisted = SeasonalSnowStorage.FromTimestamp(state.SnapshotTime);
            return consumed > persisted && (state.Melting || state.Shielded || state.Snow >= state.Maximum) &&
                SeasonalSnow.GainBetween(state.Biome, persisted, consumed) > 0f;
        }

        private static void RememberWrittenSnapshot(SnowPiece state, double consumed)
        {
            float value = SeasonalSnowStorage.Sanitize(state.Snow);
            state.SnapshotPresent = true;
            state.SnapshotValue = value;
            state.SnapshotBaseline = value;
            state.SnapshotTime = SeasonalSnowStorage.ToTimestamp(consumed);
            state.SnapshotEpoch = state.Epoch;
        }

        private void PublishSnowChanges()
        {
            int remaining = PublicationsPerFrame;
            while (remaining-- > 0 && snowPublications.Count != 0)
            {
                SnowPiece state = snowPublications.Dequeue();
                state.PublishQueued = false;
                bool exactValue = state.ForcePublish;
                state.ForcePublish = false;
                if (!state.Valid || !state.Confirmed || !state.Region.Ready || state.Epoch != winterEpoch ||
                    state.ReadyGeneration != state.Region.ReadyGeneration || !double.IsNaN(state.CatchUpFrom) ||
                    (!state.View.IsOwner() && !(state.AllowOwnerlessPublication && state.Zdo.GetOwner() == 0L)) ||
                    (state.View.IsOwner() && !state.AllowInitialPublication && simulationScene.OutsideActiveArea(state.Position)))
                    continue;
                SeasonalSnowStorage.Snapshot current = new SeasonalSnowStorage.Snapshot(state.Zdo);
                if (!SnapshotUnchanged(state, current))
                {
                    QueueRefresh(state, SnowRefresh.Snapshot);
                    continue;
                }
                if (!NeedsSnowPublication(state, state.WeatherTime, exactValue))
                {
                    state.AllowOwnerlessPublication = state.AllowInitialPublication = false;
                    continue;
                }
                SeasonalSnowStorage.Write(state.Zdo, state.Snow, state.Epoch, state.WeatherTime);
                state.AllowOwnerlessPublication = state.AllowInitialPublication = false;
                RememberWrittenSnapshot(state, state.WeatherTime);
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
            ResetSnowInteractions();
            ResetHeatSources();
            ResetSnowGeometry();
            frameWeather.Clear();
            snowingBiomes.Clear();
            endingSnowWinter = false;
        }
    }
}
