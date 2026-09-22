using System;
using UnityEngine;

namespace Seasons
{
    internal sealed partial class SeasonalSnowController
    {
        // One observed live interval, never a persisted or historical weather journal.
        // Owner-zero prediction and foreign snapshots always use the natural timeline.
        private bool liveWeatherObserved;
        private string liveWeatherEnvironment;
        private float liveWeatherBuildup;
        private float liveWeatherRate;
        private double liveWeatherFrom;
        private double liveWeatherUntil;

        private bool ContinuousSnowTime(double now) => now >= lastWorldSeconds &&
            now - lastWorldSeconds <= Math.Max(5d, Time.deltaTime * 10d);

        private void ResetLiveWeather()
        {
            liveWeatherObserved = false;
            liveWeatherEnvironment = null;
            liveWeatherBuildup = liveWeatherRate = 0f;
            liveWeatherFrom = liveWeatherUntil = 0d;
        }

        private void ObserveLiveWeather(double now)
        {
            if (!SeasonalSnow.WeatherReady || Game.IsPaused() || Time.timeScale <= 0f)
            {
                ResetLiveWeather();
                return;
            }
            // Only explicit env/resetenv has this meaning. GetEnvironmentOverride also
            // includes raids, interiors and local zones and must not be used here.
            EnvMan environment = EnvMan.instance;
            string requested = environment ? environment.m_debugEnv : null;
            EnvSetup setup = !String.IsNullOrEmpty(requested) ? environment.GetEnv(requested) : null;
            string name = setup != null ? requested : null;
            float buildup = setup?.m_snowBuildup ?? 0f;
            if (float.IsNaN(buildup) || float.IsInfinity(buildup))
                buildup = 0f;
            buildup = Mathf.Max(0f, buildup);
            float rate = SeasonalSnow.GetLiveSnowGain(buildup, 1d);
            bool continuous = liveWeatherObserved && ContinuousSnowTime(now) && now >= liveWeatherUntil;
            bool changed = !String.Equals(name, liveWeatherEnvironment, StringComparison.Ordinal) ||
                !buildup.Equals(liveWeatherBuildup) || !rate.Equals(liveWeatherRate);
            if (continuous)
            {
                liveWeatherUntil = now;
                if (changed)
                {
                    // Settle the previous source before replacing it. Pending catch-up,
                    // unready pieces and stale ownership are rejected by IntegratePiece.
                    foreach (SnowPiece state in snowPieces.Values)
                    {
                        IntegratePiece(state, now, snowClock);
                        if (state.MayPublish && state.ReplacedWeatherUntil > 0d)
                            QueuePublication(state);
                    }
                }
            }
            if (!continuous || changed)
            {
                liveWeatherEnvironment = name;
                liveWeatherBuildup = buildup;
                liveWeatherRate = rate;
                liveWeatherFrom = liveWeatherUntil = now;
            }
            liveWeatherObserved = true;
        }

        private bool LiveWeatherApplies(SnowPiece state, double now) => liveWeatherObserved &&
            liveWeatherEnvironment != null && state.Valid && state.Owner != 0L && state.Zdo.GetOwner() == state.Owner &&
            state.View.IsOwner() && Math.Min(now, liveWeatherUntil) > Math.Max(state.WeatherTime, liveWeatherFrom);

        private float LiveAccumulationGain(SnowPiece state, double now, float naturalGain)
        {
            if (!LiveWeatherApplies(state, now))
                return Mathf.Max(0f, naturalGain - state.WeatherGain);
            double from = Math.Max(state.WeatherTime, liveWeatherFrom);
            double until = Math.Min(now, liveWeatherUntil);
            float before = SeasonalSnow.GetCumulativeSnowGainAt(state.Biome, from);
            float after = SeasonalSnow.GetCumulativeSnowGainAt(state.Biome, until);
            if (after > before)
                state.ReplacedWeatherUntil = Math.Max(state.ReplacedWeatherUntil, until);
            // Consume the natural cursor even for a dry override. Retain natural
            // history before activation and outside the observed live interval.
            return Mathf.Max(0f, before - state.WeatherGain) +
                SeasonalSnow.GetLiveSnowGain(liveWeatherBuildup, until - from) +
                Mathf.Max(0f, naturalGain - after);
        }

        private string ReadLiveAccumulationSource(SnowPiece state, double now, out float rate)
        {
            rate = 0f;
            if (state == null || !state.Valid || !state.Confirmed || !state.Simulates || !state.Region.Ready ||
                state.ReadyGeneration != state.Region.ReadyGeneration || !double.IsNaN(state.CatchUpFrom) ||
                !SeasonalSnow.WeatherReady || state.Zdo.GetOwner() != state.Owner ||
                (state.Owner != 0L && !state.View.IsOwner()))
                return "inactive";
            bool owned = state.Owner != 0L && state.Zdo.GetOwner() == state.Owner && state.View.IsOwner();
            if (liveWeatherObserved && liveWeatherEnvironment != null && owned)
            {
                if (!state.Melting && state.Snow < state.Maximum)
                    rate = liveWeatherRate;
                return "env " + liveWeatherEnvironment;
            }
            long period = (long)Math.Floor(now / Math.Max(1L, SeasonalSnow.EnvironmentDuration));
            long index = period - SeasonalSnow.FirstEnvironmentPeriod;
            if (!state.Melting && state.Snow < state.Maximum &&
                SeasonalSnow.SeasonalSnowTimelines.TryGetValue(state.Biome, out SeasonalSnow.BiomeSnowTimeline timeline) &&
                index >= 0L && index < timeline.Periods.Length)
                rate = SeasonalSnow.GetLiveSnowGain(timeline.Periods[(int)index].SnowBuildup, 1d);
            return state.Owner == 0L ? "natural (owner-zero prediction)" : "natural";
        }
    }
}
