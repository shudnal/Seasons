using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    // Shared eligibility, configuration and deterministic per-biome weather history.
    // SeasonalSnowController is the only seasonal buildup producer.
    public static class SeasonalSnow
    {
        public static readonly Vector2 DefaultSnowBuildup = new Vector2(0.51f, 1f);
        public static readonly Vector2 DefaultReducedSnowBuildup = new Vector2(0.3f, 0.6f);
        public const float SeasonalSnowHardMaximum = 1f;
        private const float WeatherGainPerSecond = 0.01f;
        private static float timelineAccumulationSpeed;
        private static float timelineBuildupSpeed;
        public readonly struct SnowPeriod
        {
            public readonly string EnvironmentName;
            public readonly float SnowBuildup;
            public readonly float CumulativeSnowGain;

            public SnowPeriod(string environmentName, float snowBuildup, float cumulativeSnowGain)
            {
                EnvironmentName = environmentName ?? "<none>";
                SnowBuildup = snowBuildup;
                CumulativeSnowGain = cumulativeSnowGain;
            }

            public override string ToString() => String.Format(CultureInfo.InvariantCulture,
                "{0} | buildup={1:G9} | cumulative={2:G9}", EnvironmentName, SnowBuildup, CumulativeSnowGain);
        }

        public sealed class BiomeSnowTimeline
        {
            public readonly SnowPeriod[] Periods;

            public BiomeSnowTimeline(int periods)
            {
                Periods = new SnowPeriod[periods];
            }
        }

        public static readonly HashSet<int> SeasonalSnowPrefabs = new HashSet<int>();
        public static readonly HashSet<int> ReducedSnowBuildupPrefabs = new HashSet<int>();
        public static readonly HashSet<string> ReducedSnowBuildupPrefabNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<Heightmap.Biome, BiomeSnowTimeline> SeasonalSnowTimelines =
            new Dictionary<Heightmap.Biome, BiomeSnowTimeline>();
        public static readonly Dictionary<Heightmap.Biome, List<EnvEntry>> SeasonalSnowEnvironments =
            new Dictionary<Heightmap.Biome, List<EnvEntry>>();

        private static bool seasonalSnowPrefabsInitialized;
        private static double seasonalSnowTimelineStartSeconds;
        private static double seasonalSnowTimelineEndSeconds;
        private static long seasonalSnowFirstEnvironmentPeriod;
        private static long seasonalSnowEnvironmentDuration = 1L;
        private static bool collectingBiomeEnvironments;

        public static double TimelineStartSeconds => seasonalSnowTimelineStartSeconds;
        public static double TimelineEndSeconds => seasonalSnowTimelineEndSeconds;
        public static long FirstEnvironmentPeriod => seasonalSnowFirstEnvironmentPeriod;
        public static long EnvironmentDuration => seasonalSnowEnvironmentDuration;
        public static bool Enabled => enableSeasonalSnow?.Value ?? true;
        public static float SnowAccumulationSpeed => Mathf.Max(0f, seasonalSnowAccumulationSpeed?.Value ?? 1f);
        public static float MinimumSnowBuildup => GetSnowBuildupRange().x;
        public static float MaximumSnowBuildup => GetSnowBuildupRange().y;
        public static float ReducedMinimumSnowBuildup => GetReducedSnowBuildupRange().x;
        public static float ReducedMaximumSnowBuildup => GetReducedSnowBuildupRange().y;
        internal static bool WinterReady => SeasonState.IsActive && Enabled && controlEnvironments.Value &&
            seasonState.GetCurrentDay() > 0 && seasonState.GetCurrentSeason() == Season.Winter;
        internal static bool WeatherReady => WinterReady && SeasonalSnowTimelines.Count != 0 && !collectingBiomeEnvironments;
        public static void InitializePrefabs()
        {
            SeasonalSnowPrefabs.Clear();
            seasonalSnowPrefabsInitialized = false;

            if (ZNetScene.instance?.m_prefabs == null)
                return;

            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (!prefab)
                    continue;

                WearNTear wearNTear = prefab.GetComponent<WearNTear>();
                if (SeasonalSnowMeshSettings.IsPrefabIgnored(prefab.name) || SeasonalSnowMeshSettings.IsPrefabDisabled(prefab.name) ||
                    !wearNTear || (!wearNTear.m_snow && !SeasonalSnowMeshSettings.HasSnowCopy(prefab.name)))
                    continue;

                int prefabHash = ZNetScene.instance.GetPrefabHash(prefab);
                SeasonalSnowPrefabs.Add(prefabHash);
            }

            seasonalSnowPrefabsInitialized = true;
            RebuildReducedSnowBuildupPrefabs();
            SeasonalSnowMeshSettings.ApplyToLoadedInstances(updateCopies: true);
            SeasonalSnowController.Instance.RequestSnowRefresh(rules: true);
            LogInfo($"Seasonal snow support initialized for {SeasonalSnowPrefabs.Count} prefab(s)");
        }

        public static void Reset()
        {
            SeasonalSnowController.Instance.ResetSnowRuntime();
            SeasonalSnowPrefabs.Clear();
            ReducedSnowBuildupPrefabs.Clear();
            ReducedSnowBuildupPrefabNames.Clear();
            SeasonalSnowMeshSettings.Reset();
            SeasonalSnowTimelines.Clear();
            SeasonalSnowEnvironments.Clear();
            seasonalSnowPrefabsInitialized = false;
            seasonalSnowTimelineStartSeconds = 0d;
            seasonalSnowTimelineEndSeconds = 0d;
            seasonalSnowFirstEnvironmentPeriod = 0L;
            seasonalSnowEnvironmentDuration = 1L;
            collectingBiomeEnvironments = false;
        }

        public static void BeginBiomeEnvironmentUpdate()
        {
            SeasonalSnowController.Instance.SettleBeforeWeatherChange();
            SeasonalSnowEnvironments.Clear();
            SeasonalSnowTimelines.Clear();
            collectingBiomeEnvironments = true;
        }

        public static void EndBiomeEnvironmentUpdate()
        {
            collectingBiomeEnvironments = false;
            RefreshWeatherTimeline();
        }

        public static void RegisterBiomeEnvironments(Heightmap.Biome biome, IEnumerable<EnvEntry> environments)
        {
            if (biome == Heightmap.Biome.None || environments == null)
                return;

            List<EnvEntry> snapshot = environments
                .Where(environment => environment != null)
                .Select(CloneEnvironmentEntry)
                .ToList();

            if (snapshot.Count == 0)
                return;

            bool changed = !SeasonalSnowEnvironments.TryGetValue(biome, out List<EnvEntry> previous)
                || !EnvironmentListsEqual(previous, snapshot);

            SeasonalSnowEnvironments[biome] = snapshot;

            if (changed && !collectingBiomeEnvironments &&
                SeasonState.IsActive && seasonState.GetCurrentSeason() == Season.Winter)
            {
                RefreshWeatherTimeline();
            }
        }

        private static EnvEntry CloneEnvironmentEntry(EnvEntry source)
        {
            EnvEntry clone = new EnvEntry
            {
                m_environment = source.m_environment,
                m_weight = source.m_weight,
                m_ashlandsOverride = source.m_ashlandsOverride,
                m_deepnorthOverride = source.m_deepnorthOverride,
                m_env = source.m_env
            };

            if (EnvMan.instance != null && !String.IsNullOrWhiteSpace(clone.m_environment))
                clone.m_env = EnvMan.instance.GetEnv(clone.m_environment) ?? clone.m_env;

            return clone;
        }

        private static bool EnvironmentListsEqual(List<EnvEntry> first, List<EnvEntry> second)
        {
            if (ReferenceEquals(first, second))
                return true;

            if (first == null || second == null || first.Count != second.Count)
                return false;

            for (int i = 0; i < first.Count; ++i)
            {
                EnvEntry a = first[i];
                EnvEntry b = second[i];

                if (!String.Equals(a.m_environment, b.m_environment, StringComparison.Ordinal) ||
                    !a.m_weight.Equals(b.m_weight) ||
                    a.m_ashlandsOverride != b.m_ashlandsOverride ||
                    a.m_deepnorthOverride != b.m_deepnorthOverride ||
                    !GetEnvironmentSnowBuildup(a).Equals(GetEnvironmentSnowBuildup(b)))
                    return false;
            }

            return true;
        }

        private static float GetEnvironmentSnowBuildup(EnvEntry environment)
        {
            if (environment == null)
                return 0f;

            EnvSetup setup = environment.m_env;
            if (setup == null && EnvMan.instance != null && !String.IsNullOrWhiteSpace(environment.m_environment))
                setup = EnvMan.instance.GetEnv(environment.m_environment) ?? setup;

            return setup?.m_snowBuildup ?? 0f;
        }

        public static void RebuildReducedSnowBuildupPrefabs()
        {
            ReducedSnowBuildupPrefabs.Clear();
            ReducedSnowBuildupPrefabNames.Clear();

            foreach (KeyValuePair<string, SeasonSnow.PieceSnow> entry in SeasonalSnowSettings.Current.pieces)
            {
                if (entry.Value.buildup != SeasonSnow.SnowBuildup.Reduced)
                    continue;

                ReducedSnowBuildupPrefabNames.Add(entry.Key);
                ReducedSnowBuildupPrefabs.Add(entry.Key.GetStableHashCode());
            }

            if (ZNetScene.instance?.m_prefabs == null || ReducedSnowBuildupPrefabNames.Count == 0)
                return;

            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (!prefab || !ReducedSnowBuildupPrefabNames.Contains(prefab.name))
                    continue;

                ReducedSnowBuildupPrefabs.Add(ZNetScene.instance.GetPrefabHash(prefab));
            }
        }

        public static bool UsesReducedSnowBuildup(WearNTear instance)
        {
            if (!instance)
                return false;

            if (instance.m_nview != null && instance.m_nview.IsValid())
            {
                ZDO zdo = instance.m_nview.GetZDO();
                if (zdo != null && ReducedSnowBuildupPrefabs.Contains(zdo.GetPrefab()))
                    return true;
            }

            return ReducedSnowBuildupPrefabNames.Contains(Utils.GetPrefabName(instance.gameObject));
        }

        public static bool SupportsSeasonalSnow(WearNTear instance)
        {
            if (!instance || !instance.m_snow || instance.m_nview == null || !instance.m_nview.IsValid() ||
                SeasonalSnowMeshSettings.IsSnowDisabled(instance) || SeasonalSnowMeshSettings.IsSnowIgnored(instance))
                return false;

            ZDO zdo = instance.m_nview.GetZDO();
            if (zdo == null)
                return false;

            return !seasonalSnowPrefabsInitialized || SeasonalSnowPrefabs.Contains(zdo.GetPrefab());
        }

        public static bool IsSeasonalSnowPosition(WearNTear instance)
        {
            return SeasonState.IsActive
                && Enabled
                && controlEnvironments.Value
                && seasonState.GetCurrentSeason() == Season.Winter
                && SupportsSeasonalSnow(instance)
                && !IsIgnoredPosition(instance.transform.position);
        }

        internal static Heightmap.Biome GetBiome(WearNTear instance)
        {
            if (!instance)
                return Heightmap.Biome.None;

            if (instance.m_biome == Heightmap.Biome.None)
                instance.UpdateBiome();

            if (instance.m_biome != Heightmap.Biome.None)
                return instance.m_biome;

            return WorldGenerator.instance == null
                ? Heightmap.Biome.None
                : WorldGenerator.instance.GetBiome(instance.transform.position);
        }

        private static Heightmap.Biome GetBiome(Vector3 position)
        {
            Heightmap.Biome biome = Heightmap.FindBiome(position);
            if (biome == Heightmap.Biome.None && WorldGenerator.instance != null)
                biome = WorldGenerator.instance.GetBiome(position);
            return biome;
        }

        private static Vector2 NormalizeSnowBuildupRange(Vector2 configured)
        {
            float first = Mathf.Clamp01(configured.x);
            float second = Mathf.Clamp01(configured.y);
            float minimum = Mathf.Min(first, second);
            float maximum = Mathf.Min(Mathf.Max(first, second), SeasonalSnowHardMaximum);

            if (minimum > maximum)
                minimum = maximum;

            return new Vector2(minimum, maximum);
        }

        public static Vector2 GetSnowBuildupRange()
        {
            return NormalizeSnowBuildupRange(seasonalSnowBuildup?.Value ?? DefaultSnowBuildup);
        }

        public static Vector2 GetReducedSnowBuildupRange()
        {
            return NormalizeSnowBuildupRange(reducedSeasonalSnowBuildup?.Value ?? DefaultReducedSnowBuildup);
        }

        public static Vector2 GetSnowBuildupRange(WearNTear instance)
        {
            return UsesReducedSnowBuildup(instance) ? GetReducedSnowBuildupRange() : GetSnowBuildupRange();
        }

        private static float ClampSnowBuildup(float value, Vector2 range)
        {
            return Mathf.Clamp(value, 0f, range.y);
        }

        public static float ClampSnowBuildup(float value)
        {
            return ClampSnowBuildup(value, GetSnowBuildupRange());
        }

        public static float ClampSnowBuildup(WearNTear instance, float value)
        {
            return ClampSnowBuildup(value, GetSnowBuildupRange(instance));
        }

        private static EnvSetup PredictEnvironment(List<EnvEntry> environments, long environmentPeriod)
        {
            if (EnvMan.instance == null || environments == null || environments.Count == 0)
                return null;

            UnityEngine.Random.State state = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState((int)environmentPeriod);
                return EnvMan.instance.SelectWeightedEnvironment(environments);
            }
            finally
            {
                UnityEngine.Random.state = state;
            }
        }

        private static float GetPredictedSnowGain(float snowBuildup, double seconds)
        {
            if (snowBuildup <= 0f || seconds <= 0d || Game.instance == null)
                return 0f;

            float elapsed = (float)seconds;
            return snowBuildup * elapsed * WeatherGainPerSecond * timelineBuildupSpeed * timelineAccumulationSpeed;
        }

        internal static float GetLiveSnowGain(float snowBuildup, double seconds) =>
            GetPredictedSnowGain(snowBuildup, seconds);

        public static void RefreshWeatherTimeline()
        {
            SeasonalSnowController.Instance.SettleBeforeWeatherChange();
            try
            {
                timelineAccumulationSpeed = SnowAccumulationSpeed;
                timelineBuildupSpeed = Game.instance ? Game.instance.m_snowBuildupSpeed : 0f;
                SeasonalSnowTimelines.Clear();
                seasonalSnowTimelineStartSeconds = 0d;
                seasonalSnowTimelineEndSeconds = 0d;
                seasonalSnowFirstEnvironmentPeriod = 0L;
                seasonalSnowEnvironmentDuration = 1L;

                if (!SeasonState.IsActive || !Enabled || seasonState.GetCurrentSeason() != Season.Winter ||
                    EnvMan.instance == null || ZNet.instance == null || Game.instance == null ||
                    SeasonalSnowEnvironments.Count == 0)
                    return;

                seasonalSnowEnvironmentDuration = Math.Max(1L, EnvMan.instance.m_environmentDuration);

                double seasonElapsed = Math.Max(0d,
                    seasonState.GetTotalSeconds() - seasonState.GetStartOfCurrentSeason());

                seasonalSnowTimelineStartSeconds = Math.Max(0d,
                    ZNet.instance.GetTimeSeconds() - seasonElapsed);
                seasonalSnowTimelineEndSeconds = seasonalSnowTimelineStartSeconds
                    + seasonState.GetSecondsInSeason(Season.Winter);

                seasonalSnowFirstEnvironmentPeriod =
                    (long)Math.Floor(seasonalSnowTimelineStartSeconds / seasonalSnowEnvironmentDuration);

                long lastEnvironmentPeriodExclusive =
                    (long)Math.Ceiling(seasonalSnowTimelineEndSeconds / seasonalSnowEnvironmentDuration);
                long periodCountLong = lastEnvironmentPeriodExclusive - seasonalSnowFirstEnvironmentPeriod;

                if (periodCountLong <= 0L || periodCountLong > 100000L)
                {
                    LogWarning($"Unable to build seasonal snow weather timeline: invalid period count {periodCountLong}");
                    return;
                }

                int periodCount = (int)periodCountLong;

                foreach (KeyValuePair<Heightmap.Biome, List<EnvEntry>> biomeEnvironments in SeasonalSnowEnvironments.ToArray())
                {
                    Heightmap.Biome biome = biomeEnvironments.Key;
                    if (biome == Heightmap.Biome.None || biome == Heightmap.Biome.DeepNorth || biome == Heightmap.Biome.AshLands)
                        continue;

                    List<EnvEntry> environments = biomeEnvironments.Value;
                    if (environments == null || environments.Count == 0)
                        continue;

                    BiomeSnowTimeline timeline = new BiomeSnowTimeline(periodCount);
                    float cumulativeGain = 0f;

                    UnityEngine.Random.State randomState = UnityEngine.Random.state;
                    try
                    {
                        for (int i = 0; i < periodCount; ++i)
                        {
                            long environmentPeriod = seasonalSnowFirstEnvironmentPeriod + i;
                            UnityEngine.Random.InitState((int)environmentPeriod);
                            EnvSetup environment = EnvMan.instance.SelectWeightedEnvironment(environments);
                            float snowBuildup = Mathf.Max(0f, environment?.m_snowBuildup ?? 0f);

                            double periodStart = environmentPeriod * (double)seasonalSnowEnvironmentDuration;
                            double periodEnd = periodStart + seasonalSnowEnvironmentDuration;
                            double overlapStart = Math.Max(periodStart, seasonalSnowTimelineStartSeconds);
                            double overlapEnd = Math.Min(periodEnd, seasonalSnowTimelineEndSeconds);

                            cumulativeGain += GetPredictedSnowGain(
                                snowBuildup, Math.Max(0d, overlapEnd - overlapStart));

                            timeline.Periods[i] = new SnowPeriod(environment?.m_name, snowBuildup, Mathf.Max(0f, cumulativeGain));
                        }
                    }
                    finally
                    {
                        UnityEngine.Random.state = randomState;
                    }

                    SeasonalSnowTimelines[biome] = timeline;
                }

                LogInfo($"Seasonal snow weather timeline initialized for {SeasonalSnowTimelines.Count} biome(s), {periodCount} weather period(s)");
            }
            finally
            {
                SeasonalSnowController.Instance.WeatherTimelineChanged();
            }
        }
        private static float PredictSnowBuildup(Heightmap.Biome biome, long environmentPeriod)
        {
            if (!SeasonalSnowEnvironments.TryGetValue(biome, out List<EnvEntry> environments) ||
                environments == null || environments.Count == 0)
                return 0f;

            EnvSetup environment = PredictEnvironment(environments, environmentPeriod);
            return Mathf.Max(0f, environment?.m_snowBuildup ?? 0f);
        }

        public static float GetPredictedSnowBuildup(Heightmap.Biome biome, double seconds)
        {
            long environmentDuration = EnvMan.instance != null
                ? Math.Max(1L, EnvMan.instance.m_environmentDuration)
                : Math.Max(1L, seasonalSnowEnvironmentDuration);
            long environmentPeriod = (long)seconds / environmentDuration;
            long indexLong = environmentPeriod - seasonalSnowFirstEnvironmentPeriod;

            if (environmentDuration == seasonalSnowEnvironmentDuration &&
                SeasonalSnowTimelines.TryGetValue(biome, out BiomeSnowTimeline timeline) &&
                indexLong >= 0L && indexLong < timeline.Periods.Length)
                return timeline.Periods[(int)indexLong].SnowBuildup;

            return PredictSnowBuildup(biome, environmentPeriod);
        }

        private static float GetPassiveSeasonalSnowGain(Heightmap.Biome biome)
        {
            if (ZNet.instance == null)
                return 0f;

            if (!SeasonalSnowTimelines.TryGetValue(biome, out BiomeSnowTimeline timeline) ||
                timeline.Periods.Length == 0)
                return 0f;

            double seconds = ZNet.instance.GetTimeSeconds();
            if (seconds <= seasonalSnowTimelineStartSeconds)
                return 0f;

            if (seconds >= seasonalSnowTimelineEndSeconds)
                return timeline.Periods[timeline.Periods.Length - 1].CumulativeSnowGain;

            long environmentPeriod = (long)seconds / seasonalSnowEnvironmentDuration;
            long indexLong = environmentPeriod - seasonalSnowFirstEnvironmentPeriod;
            if (indexLong < 0L)
                return 0f;

            if (indexLong >= timeline.Periods.Length)
                return timeline.Periods[timeline.Periods.Length - 1].CumulativeSnowGain;

            int index = (int)indexLong;
            float previousGain = index > 0 ? timeline.Periods[index - 1].CumulativeSnowGain : 0f;
            double periodStart = environmentPeriod * (double)seasonalSnowEnvironmentDuration;
            double overlapStart = Math.Max(periodStart, seasonalSnowTimelineStartSeconds);
            double overlapEnd = Math.Min(seconds, seasonalSnowTimelineEndSeconds);
            float partial = GetPredictedSnowGain(
                timeline.Periods[index].SnowBuildup,
                Math.Max(0d, overlapEnd - overlapStart));

            return Mathf.Max(0f, previousGain + partial);
        }

        public static float GetPassiveSeasonalSnowTarget(Heightmap.Biome biome)
        {
            Vector2 range = GetSnowBuildupRange();
            return ClampSnowBuildup(range.x + GetPassiveSeasonalSnowGain(biome), range);
        }

        public static float GetPassiveSeasonalSnowTarget(WearNTear instance)
        {
            if (!instance || SeasonalSnowMeshSettings.IsSnowDisabled(instance) || SeasonalSnowMeshSettings.IsSnowIgnored(instance))
                return 0f;

            Heightmap.Biome biome = GetBiome(instance);
            Vector2 range = GetSnowBuildupRange(instance);
            return ClampSnowBuildup(range.x + GetPassiveSeasonalSnowGain(biome), range);
        }

        internal static float GetCumulativeSnowGainAt(Heightmap.Biome biome, double seconds)
        {
            if (!SeasonalSnowTimelines.TryGetValue(biome, out BiomeSnowTimeline timeline) ||
                timeline.Periods.Length == 0)
                return 0f;

            if (seconds <= seasonalSnowTimelineStartSeconds)
                return 0f;

            if (seconds >= seasonalSnowTimelineEndSeconds)
                return timeline.Periods[timeline.Periods.Length - 1].CumulativeSnowGain;

            long duration = Math.Max(1L, seasonalSnowEnvironmentDuration);
            long environmentPeriod = (long)Math.Floor(seconds / duration);
            long indexLong = environmentPeriod - seasonalSnowFirstEnvironmentPeriod;
            if (indexLong < 0L)
                return 0f;

            if (indexLong >= timeline.Periods.Length)
                return timeline.Periods[timeline.Periods.Length - 1].CumulativeSnowGain;

            int index = (int)indexLong;
            float previousGain = index > 0 ? timeline.Periods[index - 1].CumulativeSnowGain : 0f;
            double periodStart = environmentPeriod * (double)duration;
            double overlapStart = Math.Max(periodStart, seasonalSnowTimelineStartSeconds);
            double overlapEnd = Math.Min(seconds, seasonalSnowTimelineEndSeconds);
            float partial = GetPredictedSnowGain(
                timeline.Periods[index].SnowBuildup,
                Math.Max(0d, overlapEnd - overlapStart));

            return Mathf.Max(0f, previousGain + partial);
        }

        internal static float GainBetween(Heightmap.Biome biome, double from, double until) =>
            until <= from ? 0f : Mathf.Max(0f, GetCumulativeSnowGainAt(biome, until) - GetCumulativeSnowGainAt(biome, from));

        internal static void CaptureInitialSnowVisual(WearNTear instance) =>
            SeasonalSnowController.Instance.RegisterSnow(instance);

        internal static void ClearInactiveLoadedSnow(WearNTear instance)
        {
            // Check stored metadata before eligibility and biome lookup: most summer
            // instances have no seasonal data and must not initialize any snow state.
            if (!SeasonState.IsActive || seasonState.GetCurrentDay() <= 0 || WinterReady ||
                !instance || !instance.m_nview || !instance.m_nview.IsValid())
                return;
            ZDO zdo = instance.m_nview.GetZDO();
            if (!SeasonalSnowStorage.HasSavedValue(zdo) && !SeasonalSnowStorage.HasLegacyState(zdo))
                return;
            if (!SupportsSeasonalSnow(instance) || GetBiome(instance) == Heightmap.Biome.DeepNorth)
                return;
            SeasonalSnowController.Instance.ClearSnow(instance,
                SeasonalSnowStorage.CanWrite(instance.m_nview, zdo) ? zdo : null);
            instance.m_snowBuildup = 0f;
            instance.m_addPreSnow = false;
            instance.m_heavySnow = false;
            // Start may run after native Awake already displayed legacy snow. Hide
            // the existing roots without binding renderers or allocating materials.
            HideInactiveSnowRoot(instance.m_snow);
            HideInactiveSnowRoot(instance.m_snowWorn);
            HideInactiveSnowRoot(instance.m_snowBroken);
        }

        private static void HideInactiveSnowRoot(MeshRenderer renderer)
        {
            if (renderer && renderer.gameObject.activeSelf)
                renderer.gameObject.SetActive(false);
        }

        internal static void ClearInstanceSnowState(WearNTear instance, ZDO ownedZdo) =>
            SeasonalSnowController.Instance.ClearSnow(instance, ownedZdo);

        internal static void ReleaseIgnoredSnowState(WearNTear instance) =>
            SeasonalSnowController.Instance.ReleaseIgnoredSnow(instance);

        internal static void OnSnowMeshChanged(WearNTear instance) =>
            SeasonalSnowController.Instance.SnowMeshChanged(instance);

        public static void ReconcileLoadedSnowAfterTimeSkip() =>
            SeasonalSnowController.Instance.RequestSnowCatchUp();

        public static void OnEnabledConfigChanged()
        {
            RefreshWeatherTimeline();
            SeasonalSnowController.Instance.RequestSnowRefresh(rules: true);
        }

        public static void OnSnowRangeConfigChanged() =>
            SeasonalSnowController.Instance.RequestSnowRefresh(rules: true);

        public static void OnAccumulationSpeedConfigChanged() => RefreshWeatherTimeline();

        public static void OnHeatConfigChanged(bool rebuildLinks = false, bool reindexSources = false) =>
            SeasonalSnowController.Instance.RequestHeatRefresh(rebuildLinks, reindexSources);

        public static void UpdateLoadedSnowCover() =>
            SeasonalSnowController.Instance.RequestSnowRefresh(rules: false);

        public static void UpdateSeasonState() =>
            SeasonalSnowController.Instance.RequestSnowRefresh(rules: true);
    }
}
