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

        private struct SnowWeatherPassValues
        {
            internal float NaturalGain;
            internal bool HasLiveInterval;
            internal double From, Until;
            internal float Before, After, LiveGain;
        }

        private double snowWeatherPassTime = double.NaN;

        private void BeginSnowWeatherPass(double now)
        {
            frameWeather.Clear();
            snowWeatherPassTime = now;
        }

        private void EndSnowWeatherPass()
        {
            snowWeatherPassTime = double.NaN;
            frameWeather.Clear();
        }

        private float SnowWeatherGainAt(Heightmap.Biome biome, double now)
        {
            if (snowWeatherPassTime != now)
                return SeasonalSnow.GetCumulativeSnowGainAt(biome, now);
            if (!frameWeather.TryGetValue(biome, out SnowWeatherPassValues weather))
            {
                weather.NaturalGain = SeasonalSnow.GetCumulativeSnowGainAt(biome, now);
                frameWeather.Add(biome, weather);
            }
            return weather.NaturalGain;
        }

        private bool ContinuousSnowTime(double now) => now >= lastWorldSeconds &&
            now - lastWorldSeconds <= Math.Max(5d, Time.deltaTime * 10d);

        private void ResetLiveWeather()
        {
            EndSnowWeatherPass();
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
                    BeginSnowWeatherPass(now);
                    try
                    {
                        foreach (SnowPiece state in snowPieces.Values)
                        {
                            IntegratePiece(state, now, snowClock);
                            if (state.MayPublish && state.ReplacedWeatherUntil > 0d)
                                QueuePublication(state);
                        }
                    }
                    finally { EndSnowWeatherPass(); }
                }
            }
            if (!continuous || changed)
            {
                EndSnowWeatherPass();
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
            SnowWeatherPassValues weather = default;
            bool shared = snowWeatherPassTime == now &&
                frameWeather.TryGetValue(state.Biome, out weather);
            float before, after, liveGain;
            if (shared && weather.HasLiveInterval && weather.From == from && weather.Until == until)
            {
                before = weather.Before;
                after = weather.After;
                liveGain = weather.LiveGain;
            }
            else
            {
                before = SeasonalSnow.GetCumulativeSnowGainAt(state.Biome, from);
                after = until == now ? naturalGain : SeasonalSnow.GetCumulativeSnowGainAt(state.Biome, until);
                liveGain = SeasonalSnow.GetLiveSnowGain(liveWeatherBuildup, until - from);
                if (shared)
                {
                    // One replaceable interval per biome in this pass. Distinct piece
                    // boundaries are evaluated individually, never rounded or merged.
                    weather.HasLiveInterval = true;
                    weather.From = from;
                    weather.Until = until;
                    weather.Before = before;
                    weather.After = after;
                    weather.LiveGain = liveGain;
                    frameWeather[state.Biome] = weather;
                }
            }
            if (after > before)
                state.ReplacedWeatherUntil = Math.Max(state.ReplacedWeatherUntil, until);
            // Consume the natural cursor even for a dry override. Retain natural
            // history before activation and outside the observed live interval.
            return Mathf.Max(0f, before - state.WeatherGain) +
                liveGain +
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
