using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    public static class SeasonalSnow
    {

        public static readonly Vector2 DefaultSnowBuildup = new Vector2(0.4f, 0.99f);
        public static readonly Vector2 DefaultReducedSnowBuildup = new Vector2(0.3f, 0.6f);
        public const float PredictedWearUpdateDelta = 0.01f;
        public const float SeasonalSnowHardMaximum = 0.99f;
        private const float SnowChangeEpsilon = 0.0001f;

        public sealed class BiomeSnowTimeline
        {
            public readonly float[] snowBuildup;
            public readonly float[] cumulativeSnowGain;

            public BiomeSnowTimeline(int periods)
            {
                snowBuildup = new float[periods];
                cumulativeSnowGain = new float[periods];
            }
        }

        private struct UpdateWearState
        {
            public bool trackSeasonalSnow;
            public float snowBefore;
        }

        private sealed class SnowCoverageState
        {
            public float nextCheckTime;
        }

        private sealed class SnowMeshLocalYState
        {
            public readonly bool hasSnow;
            public readonly float snow;
            public readonly bool hasSnowWorn;
            public readonly float snowWorn;
            public readonly bool hasSnowBroken;
            public readonly float snowBroken;

            public SnowMeshLocalYState(WearNTear instance)
            {
                hasSnow = instance != null && instance.m_snow;
                snow = hasSnow ? instance.m_snow.transform.localPosition.y : 0f;
                hasSnowWorn = instance != null && instance.m_snowWorn;
                snowWorn = hasSnowWorn ? instance.m_snowWorn.transform.localPosition.y : 0f;
                hasSnowBroken = instance != null && instance.m_snowBroken;
                snowBroken = hasSnowBroken ? instance.m_snowBroken.transform.localPosition.y : 0f;
            }
        }

        private static ConditionalWeakTable<WearNTear, object> SeasonalSnowInitialized =
            new ConditionalWeakTable<WearNTear, object>();
        private static ConditionalWeakTable<WearNTear, SnowCoverageState> SeasonalSnowCoverageChecks =
            new ConditionalWeakTable<WearNTear, SnowCoverageState>();

        public static readonly HashSet<int> SeasonalSnowPrefabs = new HashSet<int>();
        public static readonly HashSet<int> ReducedSnowBuildupPrefabs = new HashSet<int>();
        public static readonly HashSet<string> ReducedSnowBuildupPrefabNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, float> SnowMeshLocalYByPrefabName =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, float> SnowMeshLocalYByPrefab = new Dictionary<int, float>();
        private static readonly Dictionary<int, SnowMeshLocalYState> OriginalSnowMeshLocalYByPrefab =
            new Dictionary<int, SnowMeshLocalYState>();
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

        private static readonly MethodInfo UpdateBiomeMethod =
            AccessTools.Method(typeof(WearNTear), nameof(WearNTear.UpdateBiome));

        private static readonly FieldInfo BiomeField =
            AccessTools.Field(typeof(WearNTear), nameof(WearNTear.m_biome));

        private static readonly MethodInfo IsSeasonalSnowPositionMethod =
            AccessTools.Method(typeof(SeasonalSnow), nameof(IsSeasonalSnowPosition));

        private static readonly MethodInfo GetSnowBuildupMethod =
            AccessTools.Method(typeof(EnvMan), nameof(EnvMan.GetSnowBuildup));

        private static readonly MethodInfo GetSeasonalSnowBuildupMethod =
            AccessTools.Method(typeof(SeasonalSnow), nameof(GetSeasonalSnowBuildup));

        public static void InitializePrefabs()
        {
            SeasonalSnowPrefabs.Clear();
            SnowMeshLocalYByPrefab.Clear();
            OriginalSnowMeshLocalYByPrefab.Clear();
            seasonalSnowPrefabsInitialized = false;
            ParseSnowMeshLocalYConfig();

            if (ZNetScene.instance?.m_prefabs == null)
                return;

            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (!prefab)
                    continue;

                WearNTear wearNTear = prefab.GetComponent<WearNTear>();
                if (!wearNTear || !wearNTear.m_snow)
                    continue;

                int prefabHash = ZNetScene.instance.GetPrefabHash(prefab);
                SeasonalSnowPrefabs.Add(prefabHash);
                OriginalSnowMeshLocalYByPrefab[prefabHash] = new SnowMeshLocalYState(wearNTear);

                if (SnowMeshLocalYByPrefabName.TryGetValue(prefab.name, out float localY))
                    SnowMeshLocalYByPrefab[prefabHash] = localY;

                ApplySnowMeshLocalY(wearNTear, prefabHash, prefab.name);
            }

            seasonalSnowPrefabsInitialized = true;
            RebuildReducedSnowBuildupPrefabs();
            LogInfo($"Seasonal snow support initialized for {SeasonalSnowPrefabs.Count} prefab(s)");
        }

        public static void Reset()
        {
            SeasonalSnowInitialized = new ConditionalWeakTable<WearNTear, object>();
            SeasonalSnowCoverageChecks = new ConditionalWeakTable<WearNTear, SnowCoverageState>();
            SeasonalSnowPrefabs.Clear();
            ReducedSnowBuildupPrefabs.Clear();
            ReducedSnowBuildupPrefabNames.Clear();
            SnowMeshLocalYByPrefabName.Clear();
            SnowMeshLocalYByPrefab.Clear();
            OriginalSnowMeshLocalYByPrefab.Clear();
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
                setup = EnvMan.instance.GetEnv(environment.m_environment);

            return setup?.m_snowBuildup ?? 0f;
        }

        public static void RebuildReducedSnowBuildupPrefabs()
        {
            ReducedSnowBuildupPrefabs.Clear();
            ReducedSnowBuildupPrefabNames.Clear();

            string value = reducedSeasonalSnowPrefabs?.Value ?? String.Empty;
            foreach (string rawName in value.Split(','))
            {
                string prefabName = rawName.Trim();
                if (String.IsNullOrWhiteSpace(prefabName))
                    continue;

                ReducedSnowBuildupPrefabNames.Add(prefabName);
                ReducedSnowBuildupPrefabs.Add(prefabName.GetStableHashCode());
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

        private static void ParseSnowMeshLocalYConfig()
        {
            SnowMeshLocalYByPrefabName.Clear();

            string value = seasonalSnowClippingFixes?.Value ?? String.Empty;
            foreach (string rawEntry in value.Split(';'))
            {
                string entry = rawEntry.Trim();
                if (String.IsNullOrWhiteSpace(entry))
                    continue;

                int separator = entry.LastIndexOf(':');
                if (separator <= 0 || separator >= entry.Length - 1)
                {
                    LogWarning($"Invalid seasonal snow clipping fix entry '{entry}'. Expected prefab:localY.");
                    continue;
                }

                string prefabName = entry.Substring(0, separator).Trim();
                string localYText = entry.Substring(separator + 1).Trim();
                if (String.IsNullOrWhiteSpace(prefabName) ||
                    !Single.TryParse(localYText, NumberStyles.Float, CultureInfo.InvariantCulture, out float localY))
                {
                    LogWarning($"Invalid seasonal snow clipping fix entry '{entry}'. Expected prefab:localY.");
                    continue;
                }

                SnowMeshLocalYByPrefabName[prefabName] = localY;
            }
        }

        private static void SetSnowRendererLocalY(MeshRenderer renderer, float localY)
        {
            if (!renderer)
                return;

            Vector3 localPosition = renderer.transform.localPosition;
            if (Mathf.Approximately(localPosition.y, localY))
                return;

            localPosition.y = localY;
            renderer.transform.localPosition = localPosition;
        }

        private static void ApplySnowMeshLocalY(WearNTear instance, int prefabHash, string prefabName)
        {
            if (!instance || !OriginalSnowMeshLocalYByPrefab.TryGetValue(prefabHash, out SnowMeshLocalYState original))
                return;

            bool hasOverride = SnowMeshLocalYByPrefab.TryGetValue(prefabHash, out float configuredY) ||
                (!String.IsNullOrWhiteSpace(prefabName) && SnowMeshLocalYByPrefabName.TryGetValue(prefabName, out configuredY));

            if (original.hasSnow)
                SetSnowRendererLocalY(instance.m_snow, hasOverride ? configuredY : original.snow);
            if (original.hasSnowWorn)
                SetSnowRendererLocalY(instance.m_snowWorn, hasOverride ? configuredY : original.snowWorn);
            if (original.hasSnowBroken)
                SetSnowRendererLocalY(instance.m_snowBroken, hasOverride ? configuredY : original.snowBroken);
        }

        private static void ApplySnowMeshLocalY(WearNTear instance)
        {
            if (!instance || instance.m_nview == null || !instance.m_nview.IsValid())
                return;

            ZDO zdo = instance.m_nview.GetZDO();
            if (zdo == null)
                return;

            ApplySnowMeshLocalY(instance, zdo.GetPrefab(), Utils.GetPrefabName(instance.gameObject));
        }

        public static void RebuildSnowMeshLocalYFixes()
        {
            ParseSnowMeshLocalYConfig();
            SnowMeshLocalYByPrefab.Clear();

            if (ZNetScene.instance?.m_prefabs != null)
            {
                foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                {
                    if (!prefab)
                        continue;

                    WearNTear wearNTear = prefab.GetComponent<WearNTear>();
                    if (!wearNTear || !wearNTear.m_snow)
                        continue;

                    int prefabHash = ZNetScene.instance.GetPrefabHash(prefab);
                    if (!OriginalSnowMeshLocalYByPrefab.ContainsKey(prefabHash))
                        OriginalSnowMeshLocalYByPrefab[prefabHash] = new SnowMeshLocalYState(wearNTear);

                    if (SnowMeshLocalYByPrefabName.TryGetValue(prefab.name, out float localY))
                        SnowMeshLocalYByPrefab[prefabHash] = localY;

                    ApplySnowMeshLocalY(wearNTear, prefabHash, prefab.name);
                }
            }

            foreach (WearNTear wearNTear in WearNTear.GetAllInstances().ToArray())
                ApplySnowMeshLocalY(wearNTear);
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
            if (!instance || !instance.m_snow || instance.m_nview == null || !instance.m_nview.IsValid())
                return false;

            ZDO zdo = instance.m_nview.GetZDO();
            if (zdo == null)
                return false;

            // WearNTear instances can Awake before ZoneSystem.Start has finished the
            // prefab scan. The renderer check is an exact local fallback until the
            // shared prefab hash set becomes available.
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

        private static Heightmap.Biome GetBiome(WearNTear instance)
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

        private static bool IsDeepNorth(WearNTear instance) => GetBiome(instance) == Heightmap.Biome.DeepNorth;

        private static bool IsDeepNorth(Vector3 position)
        {
            return WorldGenerator.instance != null
                && WorldGenerator.instance.GetBiome(position) == Heightmap.Biome.DeepNorth;
        }

        private static bool CanOwnSnowState(WearNTear instance)
        {
            return instance
                && instance.m_nview != null
                && instance.m_nview.IsValid()
                && instance.m_nview.IsOwner()
                && instance.m_nview.GetZDO() != null;
        }

        private static bool HasSeasonalSnowMarker(WearNTear instance)
        {
            return instance
                && instance.m_nview != null
                && instance.m_nview.IsValid()
                && instance.m_nview.GetZDO()?.GetBool(SeasonsVars.s_seasonalSnowWatermark) == true;
        }

        private static void MarkSeasonalSnow(WearNTear instance)
        {
            if (!CanOwnSnowState(instance))
                return;

            instance.m_nview.GetZDO().Set(SeasonsVars.s_seasonalSnowWatermark, true);
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
            return Mathf.Clamp(value, range.x, range.y);
        }

        public static float ClampSnowBuildup(float value)
        {
            return ClampSnowBuildup(value, GetSnowBuildupRange());
        }

        public static float ClampSnowBuildup(WearNTear instance, float value)
        {
            return ClampSnowBuildup(value, GetSnowBuildupRange(instance));
        }

        private static void SetSnowBuildup(WearNTear instance, float value, bool markSeasonalSnow)
        {
            if (!instance)
                return;

            value = markSeasonalSnow ? ClampSnowBuildup(instance, value) : Mathf.Clamp01(value);
            instance.m_snowBuildup = value;

            if (CanOwnSnowState(instance))
            {
                ZDO zdo = instance.m_nview.GetZDO();
                zdo.Set(ZDOVars.s_snow, value);

                if (markSeasonalSnow && value > 0f)
                    zdo.Set(SeasonsVars.s_seasonalSnowWatermark, true);
                else if (value <= 0f)
                    zdo.RemoveBool(SeasonsVars.s_seasonalSnowWatermark);
            }

            instance.UpdateSnowVisual();
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

            // WearNTearUpdater targets one UpdateWear pass per second. Vanilla adds
            // snowBuildup * Time.deltaTime * m_snowBuildupSpeed on each pass. Use a
            // deterministic frame-delta approximation so the predicted history is the
            // same on every peer. Keep it isolated for in-game REPL calibration.
            float wearUpdates = (float)(seconds / WearNTearUpdater.c_WearNTearTime);
            return snowBuildup * wearUpdates * PredictedWearUpdateDelta * Game.instance.m_snowBuildupSpeed * SnowAccumulationSpeed;
        }

        public static void RefreshWeatherTimeline()
        {
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
                        timeline.snowBuildup[i] = snowBuildup;

                        double periodStart = environmentPeriod * (double)seasonalSnowEnvironmentDuration;
                        double periodEnd = periodStart + seasonalSnowEnvironmentDuration;
                        double overlapStart = Math.Max(periodStart, seasonalSnowTimelineStartSeconds);
                        double overlapEnd = Math.Min(periodEnd, seasonalSnowTimelineEndSeconds);

                        cumulativeGain += GetPredictedSnowGain(
                            snowBuildup, Math.Max(0d, overlapEnd - overlapStart));

                        timeline.cumulativeSnowGain[i] = Mathf.Max(0f, cumulativeGain);
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
                indexLong >= 0L && indexLong < timeline.snowBuildup.Length)
                return timeline.snowBuildup[(int)indexLong];

            return PredictSnowBuildup(biome, environmentPeriod);
        }

        private static float GetPassiveSeasonalSnowGain(Heightmap.Biome biome)
        {
            if (ZNet.instance == null)
                return 0f;

            if (!SeasonalSnowTimelines.TryGetValue(biome, out BiomeSnowTimeline timeline) ||
                timeline.cumulativeSnowGain.Length == 0)
                return 0f;

            double seconds = ZNet.instance.GetTimeSeconds();
            if (seconds <= seasonalSnowTimelineStartSeconds)
                return 0f;

            if (seconds >= seasonalSnowTimelineEndSeconds)
                return timeline.cumulativeSnowGain[timeline.cumulativeSnowGain.Length - 1];

            long environmentPeriod = (long)seconds / seasonalSnowEnvironmentDuration;
            long indexLong = environmentPeriod - seasonalSnowFirstEnvironmentPeriod;
            if (indexLong < 0L)
                return 0f;

            if (indexLong >= timeline.cumulativeSnowGain.Length)
                return timeline.cumulativeSnowGain[timeline.cumulativeSnowGain.Length - 1];

            int index = (int)indexLong;
            float previousGain = index > 0 ? timeline.cumulativeSnowGain[index - 1] : 0f;
            double periodStart = environmentPeriod * (double)seasonalSnowEnvironmentDuration;
            double overlapStart = Math.Max(periodStart, seasonalSnowTimelineStartSeconds);
            double overlapEnd = Math.Min(seconds, seasonalSnowTimelineEndSeconds);
            float partial = GetPredictedSnowGain(
                timeline.snowBuildup[index],
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
            if (!instance)
                return 0f;

            Heightmap.Biome biome = GetBiome(instance);
            Vector2 range = GetSnowBuildupRange(instance);
            return ClampSnowBuildup(range.x + GetPassiveSeasonalSnowGain(biome), range);
        }

        private static float GetSeasonalSnowBuildup(EnvMan environmentManager, WearNTear instance)
        {
            if (environmentManager == null)
                return 0f;

            if (!IsSeasonalSnowPosition(instance))
                return environmentManager.GetSnowBuildup();

            Heightmap.Biome biome = GetBiome(instance);
            if (biome == Heightmap.Biome.None || biome == Heightmap.Biome.DeepNorth)
                return environmentManager.GetSnowBuildup();

            return GetPredictedSnowBuildup(
                biome,
                ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : environmentManager.m_totalSeconds) * SnowAccumulationSpeed;
        }

        private static bool IsSeasonalSnowInitialized(WearNTear instance)
        {
            return SeasonalSnowInitialized.TryGetValue(instance, out _);
        }

        private static void MarkSeasonalSnowInitialized(WearNTear instance)
        {
            SeasonalSnowInitialized.GetValue(instance, _ => new object());
        }

        private static void TryApplyPassiveSeasonalSnow(WearNTear instance, bool forceTarget = false)
        {
            if (!SeasonState.IsActive || !CanOwnSnowState(instance) || !SupportsSeasonalSnow(instance) ||
                (!forceTarget && IsSeasonalSnowInitialized(instance)))
                return;

            if (ZNetScene.instance == null || !ZNetScene.instance.IsAreaReady(instance.transform.position))
                return;

            // Only evaluate historical snowfall after all surrounding networked pieces
            // are present, so CanHaveSnow can reliably see roofs. Mark the instance even
            // when covered/shielded: past snowfall must not appear later if protection is removed.
            MarkSeasonalSnowInitialized(instance);

            Heightmap.Biome biome = GetBiome(instance);
            if (biome == Heightmap.Biome.DeepNorth || !IsSeasonalSnowPosition(instance))
                return;

            if (!instance.CanHaveSnow(forceCover: true))
                return;

            float target = GetPassiveSeasonalSnowTarget(instance);
            if (forceTarget ? !Mathf.Approximately(instance.m_snowBuildup, target) : instance.m_snowBuildup < target)
                SetSnowBuildup(instance, target, markSeasonalSnow: true);
        }

        private static bool TryClearInvalidSeasonalSnow(WearNTear instance)
        {
            if (!CanOwnSnowState(instance) || !HasSeasonalSnowMarker(instance) || IsDeepNorth(instance))
                return false;

            if (IsSeasonalSnowPosition(instance))
                return false;

            SetSnowBuildup(instance, 0f, markSeasonalSnow: false);
            return true;
        }

        private static bool IsSnowBlocked(WearNTear instance)
        {
            if (!instance)
                return true;

            SnowCoverageState state = SeasonalSnowCoverageChecks.GetValue(instance, _ => new SnowCoverageState());
            if (Time.time < state.nextCheckTime)
                return false;

            state.nextCheckTime = Time.time + 5f;

            // This mirrors the old seasonal recolor coverage check: refresh roof state on
            // our own cadence instead of waiting for WearNTear's much slower cover timer.
            bool haveRoof = instance.HaveRoof();
            instance.m_haveRoof = haveRoof;
            if (haveRoof)
                return true;

            return ShieldGenerator.IsInsideShieldCached(instance.transform.position, ref instance.m_shieldChangeID);
        }

        private static void TryInitializeSeasonalSnowOnStart(WearNTear instance)
        {
            if (instance)
                SeasonalSnowCoverageChecks.Remove(instance);

            if (!CanOwnSnowState(instance) || !SupportsSeasonalSnow(instance) || IsDeepNorth(instance) ||
                !IsSeasonalSnowPosition(instance) || instance.m_snowBuildup > SnowChangeEpsilon)
                return;

            // Start is early enough to avoid visibly popping snow in when approaching a structure.
            // Deliberately ignore roof state here because surrounding pieces may not be loaded yet.
            // Shields do not depend on roof geometry, so respect an already active shield when available.
            if (ShieldGenerator.IsInsideShieldCached(instance.transform.position, ref instance.m_shieldChangeID))
                return;

            float target = GetPassiveSeasonalSnowTarget(instance);
            if (target <= SnowChangeEpsilon)
                return;

            SetSnowBuildup(instance, target, markSeasonalSnow: true);
            MarkSeasonalSnowInitialized(instance);
        }

        private static bool TryClearCoveredSeasonalSnow(WearNTear instance)
        {
            if (!CanOwnSnowState(instance) || !HasSeasonalSnowMarker(instance) || IsDeepNorth(instance) ||
                instance.m_snowBuildup <= SnowChangeEpsilon)
                return false;

            if (!IsSeasonalSnowPosition(instance))
                return TryClearInvalidSeasonalSnow(instance);

            if (!IsSnowBlocked(instance))
                return false;

            // Once a piece becomes covered, do not restore historical snowfall if it is
            // uncovered later during the same load. From that point it accumulates only live snow.
            MarkSeasonalSnowInitialized(instance);
            SetSnowBuildup(instance, 0f, markSeasonalSnow: false);
            return true;
        }

        public static void OnEnabledConfigChanged()
        {
            SeasonalSnowInitialized = new ConditionalWeakTable<WearNTear, object>();
            SeasonalSnowCoverageChecks = new ConditionalWeakTable<WearNTear, SnowCoverageState>();

            if (!Enabled)
            {
                SeasonalSnowTimelines.Clear();
                ClearServerSeasonalSnowZDOs();
                ClearLoadedSeasonalSnow();
                return;
            }

            RefreshWeatherTimeline();
            UpdateLoadedSnowCover(forceTarget: true);
        }

        public static void OnSnowRangeConfigChanged()
        {
            UpdateLoadedSnowCover(forceTarget: true);
        }

        public static void OnReducedSnowPrefabsConfigChanged()
        {
            RebuildReducedSnowBuildupPrefabs();
            UpdateLoadedSnowCover(forceTarget: true);
        }

        public static void OnAccumulationSpeedConfigChanged()
        {
            RefreshWeatherTimeline();
            UpdateLoadedSnowCover(forceTarget: true);
        }

        public static void OnSnowClippingFixConfigChanged()
        {
            RebuildSnowMeshLocalYFixes();
        }

        public static void UpdateLoadedSnowCover(bool forceTarget = false)
        {
            if (!SeasonState.IsActive)
                return;

            foreach (WearNTear wearNTear in WearNTear.GetAllInstances().ToArray())
            {
                if (!wearNTear || !SupportsSeasonalSnow(wearNTear))
                    continue;

                SeasonalSnowInitialized.Remove(wearNTear);
                SeasonalSnowCoverageChecks.Remove(wearNTear);

                if (!CanOwnSnowState(wearNTear))
                {
                    wearNTear.UpdateSnowVisual();
                    continue;
                }

                if (IsDeepNorth(wearNTear))
                {
                    wearNTear.UpdateSnowVisual();
                    continue;
                }

                if (TryClearInvalidSeasonalSnow(wearNTear))
                    continue;

                if (TryClearCoveredSeasonalSnow(wearNTear))
                    continue;

                if (IsSeasonalSnowPosition(wearNTear))
                {
                    TryApplyPassiveSeasonalSnow(wearNTear, forceTarget);

                    if (HasSeasonalSnowMarker(wearNTear) && wearNTear.m_snowBuildup > 0f)
                    {
                        float clamped = ClampSnowBuildup(wearNTear, wearNTear.m_snowBuildup);
                        if (!Mathf.Approximately(wearNTear.m_snowBuildup, clamped))
                        {
                            SetSnowBuildup(wearNTear, clamped, markSeasonalSnow: true);
                            continue;
                        }
                    }
                }

                wearNTear.UpdateSnowVisual();
            }
        }

        private static void ClearLoadedSeasonalSnow()
        {
            foreach (WearNTear wearNTear in WearNTear.GetAllInstances().ToArray())
            {
                if (!wearNTear)
                    continue;

                SeasonalSnowInitialized.Remove(wearNTear);
                SeasonalSnowCoverageChecks.Remove(wearNTear);

                if (!CanOwnSnowState(wearNTear) || !HasSeasonalSnowMarker(wearNTear) || IsDeepNorth(wearNTear))
                    continue;

                SetSnowBuildup(wearNTear, 0f, markSeasonalSnow: false);
            }
        }

        private static void ClearServerSeasonalSnowZDOs()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null || WorldGenerator.instance == null)
                return;

            if (!seasonalSnowPrefabsInitialized)
                InitializePrefabs();

            int cleared = 0;
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.ToArray())
            {
                if (zdo == null || !zdo.IsValid() || !SeasonalSnowPrefabs.Contains(zdo.GetPrefab()))
                    continue;

                if (!zdo.GetBool(SeasonsVars.s_seasonalSnowWatermark) || IsDeepNorth(zdo.GetPosition()))
                    continue;

                zdo.Set(ZDOVars.s_snow, 0f);
                zdo.RemoveBool(SeasonsVars.s_seasonalSnowWatermark);
                cleared++;
            }

            if (cleared > 0)
                LogInfo($"Cleared seasonal snow state from {cleared} ZDO(s)");
        }

        public static void UpdateSeasonState()
        {
            // Every season starts with a clean Seasons-owned snow state. Deep North is
            // deliberately untouched because its snow is native game state.
            ClearServerSeasonalSnowZDOs();
            ClearLoadedSeasonalSnow();

            SeasonalSnowInitialized = new ConditionalWeakTable<WearNTear, object>();
            SeasonalSnowCoverageChecks = new ConditionalWeakTable<WearNTear, SnowCoverageState>();
        }

        private static bool TryAddSeasonalSnowCondition(
            List<CodeInstruction> codes,
            int startIndex,
            ILGenerator generator)
        {
            for (int i = startIndex; i < codes.Count - 2; ++i)
            {
                if (codes[i].opcode != OpCodes.Ldfld || !Equals(codes[i].operand, BiomeField))
                    continue;

                if (!codes[i + 1].LoadsConstant((long)(int)Heightmap.Biome.DeepNorth))
                    continue;

                int branchIndex = i + 2;
                CodeInstruction branch = codes[branchIndex];

                if (!(branch.operand is Label target))
                    continue;

                if (branch.opcode == OpCodes.Beq || branch.opcode == OpCodes.Beq_S)
                {
                    codes.InsertRange(branchIndex + 1, new[]
                    {
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Call, IsSeasonalSnowPositionMethod),
                        new CodeInstruction(OpCodes.Brtrue, target)
                    });

                    return true;
                }

                if (branch.opcode == OpCodes.Bne_Un || branch.opcode == OpCodes.Bne_Un_S)
                {
                    if (branchIndex + 1 >= codes.Count)
                        continue;

                    Label bodyLabel = generator.DefineLabel();

                    codes[branchIndex + 1].labels.Add(bodyLabel);

                    branch.opcode = OpCodes.Beq;
                    branch.operand = bodyLabel;

                    codes.InsertRange(branchIndex + 1, new[]
                    {
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Call, IsSeasonalSnowPositionMethod),
                        new CodeInstruction(OpCodes.Brfalse, target)
                    });

                    return true;
                }
            }

            return false;
        }

        private static bool TryReplaceSnowBuildupLookup(List<CodeInstruction> codes)
        {
            for (int i = 0; i < codes.Count; ++i)
            {
                CodeInstruction instruction = codes[i];
                if ((instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt) ||
                    !Equals(instruction.operand, GetSnowBuildupMethod))
                    continue;

                CodeInstruction loadInstance = new CodeInstruction(OpCodes.Ldarg_0);
                if (instruction.labels.Count > 0)
                {
                    loadInstance.labels.AddRange(instruction.labels);
                    instruction.labels.Clear();
                }

                codes.Insert(i, loadInstance);
                instruction.opcode = OpCodes.Call;
                instruction.operand = GetSeasonalSnowBuildupMethod;
                return true;
            }

            return false;
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateWear))]
        private static class WearNTear_UpdateWear_SeasonalSnow
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions,
                ILGenerator generator)
            {
                List<CodeInstruction> codes = instructions.ToList();
                bool snowConditionPatched = false;

                for (int i = 0; i < codes.Count; ++i)
                {
                    CodeInstruction instruction = codes[i];

                    if ((instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt)
                        || !Equals(instruction.operand, UpdateBiomeMethod))
                        continue;

                    snowConditionPatched = TryAddSeasonalSnowCondition(codes, i + 1, generator);
                    break;
                }

                if (!snowConditionPatched)
                    LogWarning("Failed to patch WearNTear.UpdateWear seasonal snow condition.");

                if (!TryReplaceSnowBuildupLookup(codes))
                    LogWarning("Failed to patch WearNTear.UpdateWear seasonal snow buildup lookup.");

                return codes;
            }

            [HarmonyPrefix]
            private static void Prefix(WearNTear __instance, ref UpdateWearState __state)
            {
                __state.snowBefore = __instance.m_snowBuildup;
                __state.trackSeasonalSnow = CanOwnSnowState(__instance) && IsSeasonalSnowPosition(__instance);
            }

            [HarmonyPostfix]
            private static void Postfix(WearNTear __instance, UpdateWearState __state)
            {
                if (!CanOwnSnowState(__instance))
                    return;

                if (TryClearInvalidSeasonalSnow(__instance))
                    return;

                bool seasonalSnowPosition = IsSeasonalSnowPosition(__instance) && !IsDeepNorth(__instance);
                if (!seasonalSnowPosition)
                    return;

                if (__state.trackSeasonalSnow && __instance.m_snowBuildup > __state.snowBefore + SnowChangeEpsilon)
                    MarkSeasonalSnow(__instance);

                if (TryClearCoveredSeasonalSnow(__instance))
                    return;

                TryApplyPassiveSeasonalSnow(__instance);

                if (HasSeasonalSnowMarker(__instance) && __instance.m_snowBuildup > 0f)
                {
                    float clamped = ClampSnowBuildup(__instance, __instance.m_snowBuildup);
                    if (!Mathf.Approximately(__instance.m_snowBuildup, clamped))
                        SetSnowBuildup(__instance, clamped, markSeasonalSnow: true);
                }
            }
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateCover))]
        private static class WearNTear_UpdateCover_SeasonalSnow
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions,
                ILGenerator generator)
            {
                List<CodeInstruction> codes = instructions.ToList();

                if (!TryAddSeasonalSnowCondition(codes, 0, generator))
                    LogWarning("Failed to patch WearNTear.UpdateCover seasonal snow condition.");

                return codes;
            }

            [HarmonyPostfix]
            private static void Postfix(WearNTear __instance)
            {
                TryClearCoveredSeasonalSnow(__instance);
            }
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Awake))]
        private static class WearNTear_Awake_SeasonalSnow
        {
            [HarmonyPostfix]
            private static void Postfix(WearNTear __instance)
            {
                if (!CanOwnSnowState(__instance) || !HasSeasonalSnowMarker(__instance))
                    return;

                if (IsDeepNorth(__instance))
                    return;

                if (!IsSeasonalSnowPosition(__instance))
                {
                    SetSnowBuildup(__instance, 0f, markSeasonalSnow: false);
                    return;
                }

                float clamped = ClampSnowBuildup(__instance, __instance.m_snowBuildup);
                if (!Mathf.Approximately(__instance.m_snowBuildup, clamped))
                    SetSnowBuildup(__instance, clamped, markSeasonalSnow: true);
            }
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Start))]
        private static class WearNTear_Start_SeasonalSnow
        {
            [HarmonyPostfix]
            private static void Postfix(WearNTear __instance)
            {
                ApplySnowMeshLocalY(__instance);
                TryInitializeSeasonalSnowOnStart(__instance);
            }
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.OnDestroy))]
        private static class WearNTear_OnDestroy_SeasonalSnow
        {
            [HarmonyPrefix]
            private static void Prefix(WearNTear __instance)
            {
                if (__instance)
                {
                    SeasonalSnowInitialized.Remove(__instance);
                    SeasonalSnowCoverageChecks.Remove(__instance);
                }
            }
        }

    }
}
