using BepInEx.Configuration;
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
        private const float SnowRpcStep = 0.005f;
        private const float NearbyHeatDistance = 3f;
        private const float HeatMeltMultiplier = 3f;
        private const float BaseSnowMeltSpeedMultiplier = 2f;
        private const float DefaultSnowMeltSpeedMultiplier = 1f;
        private const float DefaultCraftingStationMeltMultiplier = 5f;
        private const float DefaultLeakyPieceMeltMultiplier = 2f;
        private const float DefaultRoofPieceMeltMultiplier = 0f;
        private const double SnowTimeStampScale = 1000d;

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

        private struct UpdateCoverState
        {
            public bool refreshExpected;
        }

        private sealed class SnowCoverageState
        {
            public float nextRoofCheckTime;
            public bool haveRoof;
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

        private sealed class PieceMeltSourceState
        {
            public readonly EffectArea[] heatAreas;
            public readonly bool leaky;
            public readonly bool roof;
            public readonly bool craftingStation;

            public PieceMeltSourceState(WearNTear instance)
            {
                heatAreas = instance ? instance.GetComponentsInChildren<EffectArea>(true) : Array.Empty<EffectArea>();

                Piece piece = instance ? instance.m_piece : null;
                if (!piece && instance)
                    piece = instance.GetComponent<Piece>();

                List<Collider> colliders = piece?.GetAllColliders();
                leaky = colliders != null
                    && colliders.Any(collider => collider && collider.CompareTag("leaky"));
                roof = colliders != null
                    && colliders.Any(collider => collider && collider.CompareTag("roof"));

                craftingStation = instance
                    && (instance.GetComponent<CraftingStation>()
                        || instance.GetComponentInParent<CraftingStation>()
                        || instance.GetComponentInChildren<CraftingStation>(true));
            }
        }

        private sealed class StationMeltState
        {
            public float lastPokeTime = -1f;
            public float targetSnow;
            public float lastSentSnow;
            public long owner;
            public bool initialized;
        }

        private static ConditionalWeakTable<WearNTear, SnowCoverageState> SeasonalSnowCoverageChecks =
            new ConditionalWeakTable<WearNTear, SnowCoverageState>();
        private static ConditionalWeakTable<WearNTear, PieceMeltSourceState> SeasonalSnowMeltSources =
            new ConditionalWeakTable<WearNTear, PieceMeltSourceState>();
        private static ConditionalWeakTable<CraftingStation, StationMeltState> CraftingStationMeltStates =
            new ConditionalWeakTable<CraftingStation, StationMeltState>();

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
        private static ConfigEntry<float> snowMeltSpeedMultiplier;
        private static ConfigEntry<float> craftingStationMeltMultiplier;
        private static ConfigEntry<float> leakyPieceMeltMultiplier;
        private static ConfigEntry<float> roofPieceMeltMultiplier;

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
        private static float SnowMeltSpeedMultiplier =>
            BaseSnowMeltSpeedMultiplier * Mathf.Max(0f, snowMeltSpeedMultiplier?.Value ?? DefaultSnowMeltSpeedMultiplier);
        private static float CraftingStationMeltMultiplier =>
            Mathf.Max(0f, craftingStationMeltMultiplier?.Value ?? DefaultCraftingStationMeltMultiplier);
        private static float LeakyPieceMeltMultiplier =>
            Mathf.Max(0f, leakyPieceMeltMultiplier?.Value ?? DefaultLeakyPieceMeltMultiplier);
        private static float RoofPieceMeltMultiplier =>
            Mathf.Max(0f, roofPieceMeltMultiplier?.Value ?? DefaultRoofPieceMeltMultiplier);

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

        private static void EnsureSnowMeltConfigs()
        {
            if (Seasons.instance == null)
                return;

            if (snowMeltSpeedMultiplier == null)
            {
                snowMeltSpeedMultiplier = Seasons.configSync.AddConfigEntry(
                    Seasons.instance.Config,
                    "Season - Winter snow",
                    "Snow melt speed multiplier - all sources",
                    DefaultSnowMeltSpeedMultiplier,
                    new ConfigDescription("Multiplier for the current seasonal snow melting speed from all sources. 1 uses the current default rate; 0 disables gradual melting."),
                    syncMode: ConditionalConfigSync.ConfigSyncMode.AlwaysServerControlled,
                    serverControlledByDefault: true).SourceConfig;
            }

            if (craftingStationMeltMultiplier == null)
            {
                craftingStationMeltMultiplier = Seasons.configSync.AddConfigEntry(
                    Seasons.instance.Config,
                    "Season - Winter snow",
                    "Snow melt speed multiplier - crafting stations",
                    DefaultCraftingStationMeltMultiplier,
                    new ConfigDescription("Source multiplier for real-time seasonal snow melting while the local player is using a crafting station. Crafting stations ignore the leaky-piece multiplier."),
                    syncMode: ConditionalConfigSync.ConfigSyncMode.AlwaysServerControlled,
                    serverControlledByDefault: true).SourceConfig;
            }

            if (leakyPieceMeltMultiplier == null)
            {
                leakyPieceMeltMultiplier = Seasons.configSync.AddConfigEntry(
                    Seasons.instance.Config,
                    "Season - Winter snow",
                    "Snow melt speed multiplier - leaky pieces",
                    DefaultLeakyPieceMeltMultiplier,
                    new ConfigDescription("Additional multiplier for seasonal snow melting on pieces with at least one active non-trigger collider tagged 'leaky'. Crafting stations ignore this multiplier."),
                    syncMode: ConditionalConfigSync.ConfigSyncMode.AlwaysServerControlled,
                    serverControlledByDefault: true).SourceConfig;
            }

            if (roofPieceMeltMultiplier == null)
            {
                roofPieceMeltMultiplier = Seasons.configSync.AddConfigEntry(
                    Seasons.instance.Config,
                    "Season - Winter snow",
                    "Snow melt speed multiplier - roof pieces",
                    DefaultRoofPieceMeltMultiplier,
                    new ConfigDescription("Additional multiplier for gradual Heat melting on pieces with at least one active non-trigger collider tagged 'roof'. This multiplier takes precedence over the leaky-piece multiplier. Crafting stations ignore it."),
                    syncMode: ConditionalConfigSync.ConfigSyncMode.AlwaysServerControlled,
                    serverControlledByDefault: true).SourceConfig;
            }
        }

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
            SeasonalSnowCoverageChecks = new ConditionalWeakTable<WearNTear, SnowCoverageState>();
            SeasonalSnowMeltSources = new ConditionalWeakTable<WearNTear, PieceMeltSourceState>();
            CraftingStationMeltStates = new ConditionalWeakTable<CraftingStation, StationMeltState>();
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
                setup = EnvMan.instance.GetEnv(environment.m_environment) ?? setup;

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

        private static Heightmap.Biome GetBiome(Vector3 position)
        {
            Heightmap.Biome biome = Heightmap.FindBiome(position);
            if (biome == Heightmap.Biome.None && WorldGenerator.instance != null)
                biome = WorldGenerator.instance.GetBiome(position);
            return biome;
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

        private static bool CanTrackSeasonalSnow(WearNTear instance)
        {
            return instance
                && instance.m_nview != null
                && instance.m_nview.IsValid()
                && instance.m_nview.GetZDO() != null
                && SeasonState.IsActive
                && seasonState.GetCurrentSeason() == Season.Winter
                && IsSeasonalSnowPosition(instance);
        }

        private static bool IsSeasonalSnowSurface(WearNTear instance)
        {
            return instance
                && SupportsSeasonalSnow(instance)
                && !IsDeepNorth(instance)
                && !IsIgnoredPosition(instance.transform.position);
        }

        private static bool HasSeasonalSnowMarker(WearNTear instance)
        {
            return instance
                && instance.m_nview != null
                && instance.m_nview.IsValid()
                && instance.m_nview.GetZDO()?.GetBool(SeasonsVars.s_seasonalSnowWatermark) == true;
        }

        private static void RemoveBoolWithRevision(ZDO zdo, int hash)
        {
            if (zdo != null && zdo.RemoveBool(hash))
                zdo.IncreaseDataRevision();
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

        private static void SetSnowBuildup(WearNTear instance, float value, bool markSeasonalSnow, bool allowBelowMinimum = false)
        {
            if (!instance)
                return;

            Vector2 range = GetSnowBuildupRange(instance);
            value = markSeasonalSnow && !allowBelowMinimum
                ? ClampSnowBuildup(value, range)
                : Mathf.Clamp(value, 0f, range.y);
            instance.m_snowBuildup = value;

            if (CanOwnSnowState(instance))
            {
                ZDO zdo = instance.m_nview.GetZDO();
                zdo.Set(ZDOVars.s_snow, value);

                if (markSeasonalSnow && value > SnowChangeEpsilon)
                    zdo.Set(SeasonsVars.s_seasonalSnowWatermark, true);
                else if (value <= SnowChangeEpsilon)
                    RemoveBoolWithRevision(zdo, SeasonsVars.s_seasonalSnowWatermark);
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

            if (GetMeltMultiplier(instance) > 0f)
                return 0f;

            return GetPredictedSnowBuildup(
                biome,
                ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : environmentManager.m_totalSeconds) * SnowAccumulationSpeed;
        }

        private static long ToSnowTimeStamp(double seconds)
        {
            return (long)Math.Floor(Math.Max(0d, seconds) * SnowTimeStampScale);
        }

        private static double FromSnowTimeStamp(long value)
        {
            return Math.Max(0d, value) / SnowTimeStampScale;
        }

        private static long GetCurrentSnowTimeStamp()
        {
            return ZNet.instance == null ? 0L : ToSnowTimeStamp(ZNet.instance.GetTimeSeconds());
        }

        private static bool IsWinterStateReady()
        {
            return SeasonState.IsActive
                && seasonState.GetCurrentDay() > 0
                && seasonState.GetCurrentSeason() == Season.Winter;
        }

        private static bool HasCurrentWinterState(WearNTear instance)
        {
            return CanOwnSnowState(instance)
                && instance.m_nview.GetZDO().GetBool(SeasonsVars.s_seasonalSnowWinter);
        }

        private static bool EnsureCurrentWinterState(WearNTear instance)
        {
            if (!CanOwnSnowState(instance) || !IsWinterStateReady())
                return false;

            ZDO zdo = instance.m_nview.GetZDO();
            if (!zdo.GetBool(SeasonsVars.s_seasonalSnowWinter))
            {
                zdo.Set(SeasonsVars.s_seasonalSnowWinter, true);
                if (zdo.RemoveLong(SeasonsVars.s_seasonalSnowWinter))
                    zdo.IncreaseDataRevision();
                zdo.Set(SeasonsVars.s_seasonalSnowFrom, ToSnowTimeStamp(seasonalSnowTimelineStartSeconds));
                zdo.Set(SeasonsVars.s_seasonalSnowBaseline, 0f);
                RemoveBoolWithRevision(zdo, SeasonsVars.s_seasonalSnowMeltedBelowMinimum);
            }

            return true;
        }

        private static void SetAccumulationBaselineNow(WearNTear instance, bool meltedBelowMinimum)
        {
            if (!EnsureCurrentWinterState(instance))
                return;

            ZDO zdo = instance.m_nview.GetZDO();
            zdo.Set(SeasonsVars.s_seasonalSnowFrom, GetCurrentSnowTimeStamp());
            zdo.Set(SeasonsVars.s_seasonalSnowBaseline, Mathf.Max(0f, instance.m_snowBuildup));

            if (meltedBelowMinimum)
                zdo.Set(SeasonsVars.s_seasonalSnowMeltedBelowMinimum, true);
            else
                RemoveBoolWithRevision(zdo, SeasonsVars.s_seasonalSnowMeltedBelowMinimum);
        }

        private static float GetCumulativeSnowGainAt(Heightmap.Biome biome, double seconds)
        {
            if (!SeasonalSnowTimelines.TryGetValue(biome, out BiomeSnowTimeline timeline) ||
                timeline.cumulativeSnowGain.Length == 0)
                return 0f;

            if (seconds <= seasonalSnowTimelineStartSeconds)
                return 0f;

            if (seconds >= seasonalSnowTimelineEndSeconds)
                return timeline.cumulativeSnowGain[timeline.cumulativeSnowGain.Length - 1];

            long duration = Math.Max(1L, seasonalSnowEnvironmentDuration);
            long environmentPeriod = (long)Math.Floor(seconds / duration);
            long indexLong = environmentPeriod - seasonalSnowFirstEnvironmentPeriod;
            if (indexLong < 0L)
                return 0f;

            if (indexLong >= timeline.cumulativeSnowGain.Length)
                return timeline.cumulativeSnowGain[timeline.cumulativeSnowGain.Length - 1];

            int index = (int)indexLong;
            float previousGain = index > 0 ? timeline.cumulativeSnowGain[index - 1] : 0f;
            double periodStart = environmentPeriod * (double)duration;
            double overlapStart = Math.Max(periodStart, seasonalSnowTimelineStartSeconds);
            double overlapEnd = Math.Min(seconds, seasonalSnowTimelineEndSeconds);
            float partial = GetPredictedSnowGain(
                timeline.snowBuildup[index],
                Math.Max(0d, overlapEnd - overlapStart));

            return Mathf.Max(0f, previousGain + partial);
        }

        private static float GetPersonalSnowGain(WearNTear instance)
        {
            if (!CanOwnSnowState(instance) || ZNet.instance == null)
                return 0f;

            ZDO zdo = instance.m_nview.GetZDO();
            long fromStamp = zdo.GetLong(
                SeasonsVars.s_seasonalSnowFrom,
                ToSnowTimeStamp(seasonalSnowTimelineStartSeconds));

            double from = FromSnowTimeStamp(fromStamp);
            double now = ZNet.instance.GetTimeSeconds();
            Heightmap.Biome biome = GetBiome(instance);

            return Mathf.Max(0f, GetCumulativeSnowGainAt(biome, now) - GetCumulativeSnowGainAt(biome, from));
        }

        private static bool IsPreWinterPiece(WearNTear instance)
        {
            if (!CanOwnSnowState(instance))
                return false;

            long from = instance.m_nview.GetZDO().GetLong(SeasonsVars.s_seasonalSnowFrom, long.MaxValue);
            return from <= ToSnowTimeStamp(seasonalSnowTimelineStartSeconds);
        }

        private static float GetPersonalSnowTarget(WearNTear instance, bool requireGain)
        {
            Vector2 range = GetSnowBuildupRange(instance);
            float gain = GetPersonalSnowGain(instance);
            bool preWinterPiece = IsPreWinterPiece(instance);
            float baseline = CanOwnSnowState(instance)
                ? Mathf.Max(0f, instance.m_nview.GetZDO().GetFloat(SeasonsVars.s_seasonalSnowBaseline, 0f))
                : 0f;

            if (requireGain && gain <= SnowChangeEpsilon && !preWinterPiece)
                return baseline;

            if (preWinterPiece)
                return Mathf.Clamp(Mathf.Max(baseline, range.x) + gain, 0f, range.y);

            if (baseline + SnowChangeEpsilon < range.x)
            {
                if (gain <= SnowChangeEpsilon)
                    return baseline;
                baseline = range.x;
            }

            return Mathf.Clamp(baseline + gain, 0f, range.y);
        }

        private static bool IsActiveHeatArea(EffectArea area)
        {
            return area
                && area.enabled
                && area.gameObject.activeInHierarchy
                && area.m_isHeatType
                && area.m_collider != null
                && area.m_collider.enabled
                && area.m_collider.gameObject.activeInHierarchy;
        }

        private static bool IsInsideHeatArea(Vector3 position)
        {
            return IsActiveHeatArea(EffectArea.IsPointInsideArea(position, EffectArea.Type.Heat));
        }

        private static PieceMeltSourceState GetMeltSourceState(WearNTear instance)
        {
            return instance ? SeasonalSnowMeltSources.GetValue(instance, key => new PieceMeltSourceState(key)) : null;
        }

        private static bool HasActiveInternalHeatArea(WearNTear instance)
        {
            PieceMeltSourceState state = GetMeltSourceState(instance);
            if (state == null)
                return false;

            foreach (EffectArea area in state.heatAreas)
                if (IsActiveHeatArea(area))
                    return true;

            return false;
        }

        private static float GetPieceSnowMeltMultiplier(WearNTear instance)
        {
            PieceMeltSourceState state = GetMeltSourceState(instance);
            if (state == null || state.craftingStation)
                return 1f;

            if (state.roof)
                return RoofPieceMeltMultiplier;

            return state.leaky ? LeakyPieceMeltMultiplier : 1f;
        }

        private static bool IsNearHeatArea(Vector3 position)
        {
            foreach (EffectArea area in EffectArea.GetAllAreas())
            {
                if (!IsActiveHeatArea(area) || (area.m_type & EffectArea.Type.Heat) == 0)
                    continue;

                if (Vector3.Distance(position, area.transform.position) <= NearbyHeatDistance)
                    return true;
            }

            return false;
        }

        private static bool IsSnowBlocked(WearNTear instance, bool forceRefresh = false)
        {
            if (!instance)
                return true;

            bool shielded = ShieldGenerator.IsInsideShieldCached(instance.transform.position, ref instance.m_shieldChangeID);
            SnowCoverageState state = SeasonalSnowCoverageChecks.GetValue(instance, _ => new SnowCoverageState());

            if (forceRefresh || Time.time >= state.nextRoofCheckTime)
            {
                state.nextRoofCheckTime = Time.time + 5f;
                state.haveRoof = instance.HaveRoof();
                instance.m_haveRoof = state.haveRoof;
            }

            return shielded || state.haveRoof;
        }

        private static float GetMeltMultiplier(WearNTear instance, bool? blocked = null)
        {
            if (!instance || !SeasonState.IsActive || seasonState.GetCurrentSeason() != Season.Winter ||
                SnowMeltSpeedMultiplier <= 0f)
                return 0f;

            Vector3 position = instance.transform.position;
            float multiplier = 0f;

            if (IsInsideHeatArea(position) && (blocked ?? IsSnowBlocked(instance)))
                multiplier = HeatMeltMultiplier;

            if (HasActiveInternalHeatArea(instance))
                multiplier = Mathf.Max(multiplier, HeatMeltMultiplier);

            if (IsNearHeatArea(position))
                multiplier = Mathf.Max(multiplier, HeatMeltMultiplier);

            return multiplier * GetPieceSnowMeltMultiplier(instance);
        }

        private static void TryApplyPassiveSeasonalSnow(WearNTear instance)
        {
            if (!SeasonState.IsActive || !CanOwnSnowState(instance) || !SupportsSeasonalSnow(instance))
                return;

            if (ZNetScene.instance == null || !ZNetScene.instance.IsAreaReady(instance.transform.position))
                return;

            if (IsDeepNorth(instance) || !IsSeasonalSnowPosition(instance))
                return;

            bool hadCurrentWinterState = HasCurrentWinterState(instance);
            if (!EnsureCurrentWinterState(instance))
                return;

            if (GetMeltMultiplier(instance) > 0f)
            {
                SetAccumulationBaselineNow(
                    instance,
                    instance.m_nview.GetZDO().GetBool(SeasonsVars.s_seasonalSnowMeltedBelowMinimum));
                return;
            }

            if (IsSnowBlocked(instance))
            {
                if (instance.m_snowBuildup > SnowChangeEpsilon)
                    SetSnowBuildup(instance, 0f, markSeasonalSnow: false);

                SetAccumulationBaselineNow(instance, meltedBelowMinimum: false);
                return;
            }

            float gain = GetPersonalSnowGain(instance);
            if (hadCurrentWinterState && gain <= SnowChangeEpsilon)
                return;

            float target = GetPersonalSnowTarget(instance, requireGain: hadCurrentWinterState);
            if (instance.m_snowBuildup + SnowChangeEpsilon < target)
            {
                SetSnowBuildup(instance, target, markSeasonalSnow: target > SnowChangeEpsilon);
                RemoveBoolWithRevision(instance.m_nview.GetZDO(), SeasonsVars.s_seasonalSnowMeltedBelowMinimum);
            }
        }

        private static bool HasTrackedSeasonalSnowState(ZDO zdo)
        {
            return zdo != null &&
                (zdo.GetBool(SeasonsVars.s_seasonalSnowWatermark)
                || zdo.GetBool(SeasonsVars.s_seasonalSnowWinter)
                || zdo.GetLong(SeasonsVars.s_seasonalSnowWinter, 0L) != 0L);
        }

        private static bool TryClearInvalidSeasonalSnow(WearNTear instance)
        {
            if (!SeasonState.IsActive || seasonState.GetCurrentDay() <= 0 ||
                !CanOwnSnowState(instance) || IsDeepNorth(instance))
                return false;

            ZDO zdo = instance.m_nview.GetZDO();
            if (!HasTrackedSeasonalSnowState(zdo))
                return false;

            if (seasonState.GetCurrentSeason() == Season.Winter && IsSeasonalSnowPosition(instance))
                return false;

            ClearTrackedInstance(instance);
            return true;
        }

        private static void TryInitializeSeasonalSnowOnStart(WearNTear instance)
        {
            if (instance)
                SeasonalSnowCoverageChecks.Remove(instance);
        }

        private static bool TryClearCoveredSeasonalSnow(WearNTear instance)
        {
            if (!CanOwnSnowState(instance) || IsDeepNorth(instance) || instance.m_snowBuildup <= SnowChangeEpsilon)
                return false;

            if (!IsSeasonalSnowPosition(instance))
                return TryClearInvalidSeasonalSnow(instance);

            bool blocked = IsSnowBlocked(instance);
            if (!blocked)
                return false;

            if (GetMeltMultiplier(instance, blocked: true) > 0f)
            {
                SetAccumulationBaselineNow(
                    instance,
                    instance.m_nview.GetZDO().GetBool(SeasonsVars.s_seasonalSnowMeltedBelowMinimum));
                return false;
            }

            SetSnowBuildup(instance, 0f, markSeasonalSnow: false);
            SetAccumulationBaselineNow(instance, meltedBelowMinimum: false);
            return true;
        }

        private static void ApplyRealtimeHeatMelt(WearNTear instance)
        {
            if (!CanOwnSnowState(instance) || instance.m_snowBuildup <= SnowChangeEpsilon ||
                !SeasonState.IsActive || seasonState.GetCurrentSeason() != Season.Winter ||
                !IsSeasonalSnowPosition(instance) || IsDeepNorth(instance) || Game.instance == null ||
                ZNetScene.instance == null || ZNetScene.instance.OutsideActiveArea(instance.transform.position))
                return;

            if (!EnsureCurrentWinterState(instance))
                return;

            float meltMultiplier = GetMeltMultiplier(instance);
            if (meltMultiplier <= 0f)
                return;

            Vector2 range = GetSnowBuildupRange(instance);
            float melt = Time.deltaTime
                * Game.instance.m_snowBuildupSpeed
                * SnowAccumulationSpeed
                * SnowMeltSpeedMultiplier
                * meltMultiplier;
            float value = Mathf.Max(0f, instance.m_snowBuildup - melt);
            bool belowMinimum = value + SnowChangeEpsilon < range.x;

            SetSnowBuildup(
                instance,
                value,
                markSeasonalSnow: value > SnowChangeEpsilon,
                allowBelowMinimum: true);
            SetAccumulationBaselineNow(instance, belowMinimum);
        }

        private static void MarkPlacedDuringWinter(WearNTear instance)
        {
            if (!CanOwnSnowState(instance) || !SupportsSeasonalSnow(instance) || IsDeepNorth(instance) ||
                !IsSeasonalSnowPosition(instance))
                return;

            if (!EnsureCurrentWinterState(instance))
                return;

            ZDO zdo = instance.m_nview.GetZDO();
            zdo.Set(SeasonsVars.s_seasonalSnowFrom, GetCurrentSnowTimeStamp());
            zdo.Set(SeasonsVars.s_seasonalSnowBaseline, 0f);
            RemoveBoolWithRevision(zdo, SeasonsVars.s_seasonalSnowMeltedBelowMinimum);
            SetSnowBuildup(instance, 0f, markSeasonalSnow: false);
        }

        private static void RecordSnowDecrease(WearNTear instance, float persistedSnowBefore)
        {
            if (!CanTrackSeasonalSnow(instance) || !instance.m_nview.IsOwner() || ZNet.instance == null)
                return;

            ZDO zdo = instance.m_nview.GetZDO();
            float current = Mathf.Max(0f, zdo.GetFloat(ZDOVars.s_snow, instance.m_snowBuildup));
            if (current >= persistedSnowBefore - SnowChangeEpsilon)
                return;

            if (!EnsureCurrentWinterState(instance))
                return;

            instance.m_snowBuildup = current;
            zdo.Set(SeasonsVars.s_seasonalSnowFrom, GetCurrentSnowTimeStamp());
            zdo.Set(SeasonsVars.s_seasonalSnowBaseline, current);

            if (current > SnowChangeEpsilon)
                zdo.Set(SeasonsVars.s_seasonalSnowWatermark, true);
            else
                RemoveBoolWithRevision(zdo, SeasonsVars.s_seasonalSnowWatermark);

            Vector2 range = GetSnowBuildupRange(instance);
            if (current + SnowChangeEpsilon < range.x)
                zdo.Set(SeasonsVars.s_seasonalSnowMeltedBelowMinimum, true);
            else
                RemoveBoolWithRevision(zdo, SeasonsVars.s_seasonalSnowMeltedBelowMinimum);

            instance.UpdateSnowVisual();
        }

        private static WearNTear GetStationWearNTear(CraftingStation station)
        {
            if (!station)
                return null;

            WearNTear wearNTear = station.GetComponent<WearNTear>();
            if (!wearNTear)
                wearNTear = station.GetComponentInParent<WearNTear>();
            if (!wearNTear)
                wearNTear = station.GetComponentInChildren<WearNTear>(true);

            return wearNTear;
        }

        private static void ResetStationMeltState(CraftingStation station)
        {
            if (!station || !CraftingStationMeltStates.TryGetValue(station, out StationMeltState state))
                return;

            state.lastPokeTime = -1f;
            state.targetSnow = 0f;
            state.lastSentSnow = 0f;
            state.owner = 0L;
            state.initialized = false;
        }

        private static void UpdateCraftingStationMelt(CraftingStation station)
        {
            Player player = Player.m_localPlayer;
            if (!player || player.GetCurrentCraftingStation() != station ||
                !SeasonState.IsActive || seasonState.GetCurrentSeason() != Season.Winter || Game.instance == null)
            {
                ResetStationMeltState(station);
                return;
            }

            WearNTear wearNTear = GetStationWearNTear(station);
            if (!CanTrackSeasonalSnow(wearNTear))
            {
                ResetStationMeltState(station);
                return;
            }

            ZDO zdo = wearNTear.m_nview.GetZDO();
            float maximum = GetSnowBuildupRange(wearNTear).y;
            float currentSnow = Mathf.Clamp(zdo.GetFloat(ZDOVars.s_snow, wearNTear.m_snowBuildup), 0f, maximum);
            if (currentSnow <= SnowChangeEpsilon)
            {
                wearNTear.m_snowBuildup = 0f;
                wearNTear.UpdateSnowVisual();
                ResetStationMeltState(station);
                return;
            }

            float heatMeltMultiplier = GetMeltMultiplier(wearNTear);
            float multiplier = Mathf.Max(0f, CraftingStationMeltMultiplier - heatMeltMultiplier);
            if (multiplier <= 0f)
            {
                ResetStationMeltState(station);
                return;
            }

            long owner = zdo.GetOwner();
            StationMeltState state = CraftingStationMeltStates.GetValue(station, _ => new StationMeltState());
            float now = Time.time;

            bool restart = !state.initialized
                || state.owner != owner
                || state.lastPokeTime < 0f
                || now <= state.lastPokeTime
                || now - state.lastPokeTime > 0.25f;

            if (restart)
            {
                state.initialized = true;
                state.owner = owner;
                state.lastPokeTime = now;
                state.targetSnow = currentSnow;
                state.lastSentSnow = currentSnow;
                wearNTear.m_snowBuildup = currentSnow;
                wearNTear.UpdateSnowVisual();
                return;
            }

            float deltaTime = now - state.lastPokeTime;
            state.lastPokeTime = now;

            state.targetSnow = Mathf.Min(state.targetSnow, currentSnow);
            state.targetSnow = Mathf.Max(
                0f,
                state.targetSnow - GetPredictedSnowGain(1f, deltaTime)
                    * SnowMeltSpeedMultiplier
                    * multiplier);

            wearNTear.m_snowBuildup = state.targetSnow;
            wearNTear.UpdateSnowVisual();

            bool sendZero = state.targetSnow <= SnowChangeEpsilon && state.lastSentSnow > SnowChangeEpsilon;
            bool sendStep = state.lastSentSnow - state.targetSnow + SnowChangeEpsilon >= SnowRpcStep;
            if (!sendZero && !sendStep)
                return;

            float target = sendZero ? 0f : state.targetSnow;
            state.targetSnow = target;
            state.lastSentSnow = target;
            wearNTear.m_nview.InvokeRPC("RPC_SetSnow", target);
        }

        private static bool RemoveRuntimeState(ZDO zdo)
        {
            if (zdo == null)
                return false;

            bool removed = false;
            removed |= zdo.RemoveBool(SeasonsVars.s_seasonalSnowWatermark);
            removed |= zdo.RemoveBool(SeasonsVars.s_seasonalSnowWinter);
            removed |= zdo.RemoveLong(SeasonsVars.s_seasonalSnowWinter);
            removed |= zdo.RemoveLong(SeasonsVars.s_seasonalSnowFrom);
            removed |= zdo.RemoveFloat(SeasonsVars.s_seasonalSnowBaseline);
            removed |= zdo.RemoveBool(SeasonsVars.s_seasonalSnowMeltedBelowMinimum);
            if (removed)
                zdo.IncreaseDataRevision();
            return removed;
        }

        private static void ClearTrackedInstance(WearNTear instance)
        {
            if (!CanOwnSnowState(instance) || IsDeepNorth(instance))
                return;

            ZDO zdo = instance.m_nview.GetZDO();
            if (!HasTrackedSeasonalSnowState(zdo))
                return;

            instance.m_snowBuildup = 0f;
            zdo.Set(ZDOVars.s_snow, 0f);
            RemoveRuntimeState(zdo);
            instance.UpdateSnowVisual();
        }

        private static void ClampLoadedSnowMaximum()
        {
            if (!IsWinterStateReady())
                return;

            foreach (WearNTear wearNTear in WearNTear.GetAllInstances().ToArray())
            {
                if (!CanOwnSnowState(wearNTear) || IsDeepNorth(wearNTear) || !SupportsSeasonalSnow(wearNTear) ||
                    IsIgnoredPosition(wearNTear.transform.position) || !HasTrackedSeasonalSnowState(wearNTear.m_nview.GetZDO()))
                    continue;

                float maximum = GetSnowBuildupRange(wearNTear).y;
                if (wearNTear.m_snowBuildup <= maximum + SnowChangeEpsilon)
                    continue;

                float value = Mathf.Clamp(wearNTear.m_snowBuildup, 0f, maximum);
                SetSnowBuildup(
                    wearNTear,
                    value,
                    markSeasonalSnow: value > SnowChangeEpsilon,
                    allowBelowMinimum: true);
                SetAccumulationBaselineNow(
                    wearNTear,
                    value + SnowChangeEpsilon < GetSnowBuildupRange(wearNTear).x);
            }
        }

        private static void RebaseLoadedSnowAccumulation()
        {
            if (!IsWinterStateReady())
                return;

            foreach (WearNTear wearNTear in WearNTear.GetAllInstances().ToArray())
            {
                if (!CanOwnSnowState(wearNTear) || !HasCurrentWinterState(wearNTear) ||
                    IsDeepNorth(wearNTear) || !IsSeasonalSnowPosition(wearNTear))
                    continue;

                ZDO zdo = wearNTear.m_nview.GetZDO();
                SetAccumulationBaselineNow(
                    wearNTear,
                    zdo.GetBool(SeasonsVars.s_seasonalSnowMeltedBelowMinimum));
            }
        }

        public static void OnEnabledConfigChanged()
        {
            SeasonalSnowCoverageChecks = new ConditionalWeakTable<WearNTear, SnowCoverageState>();

            if (!Enabled)
            {
                SeasonalSnowTimelines.Clear();
                ClearLoadedSeasonalSnow();
                ClearServerSeasonalSnowZDOs();
                return;
            }

            RefreshWeatherTimeline();
            UpdateLoadedSnowCover();
        }

        public static void OnSnowRangeConfigChanged()
        {
            ClampLoadedSnowMaximum();
        }

        public static void OnReducedSnowPrefabsConfigChanged()
        {
            RebuildReducedSnowBuildupPrefabs();
            ClampLoadedSnowMaximum();
        }

        public static void OnAccumulationSpeedConfigChanged()
        {
            RebaseLoadedSnowAccumulation();
            RefreshWeatherTimeline();
        }

        public static void OnSnowClippingFixConfigChanged()
        {
            RebuildSnowMeshLocalYFixes();
        }

        public static void UpdateLoadedSnowCover()
        {
            if (!SeasonState.IsActive)
                return;

            foreach (WearNTear wearNTear in WearNTear.GetAllInstances().ToArray())
            {
                if (!wearNTear || !SupportsSeasonalSnow(wearNTear))
                    continue;

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
                    TryApplyPassiveSeasonalSnow(wearNTear);

                    float maximum = GetSnowBuildupRange(wearNTear).y;
                    if (HasSeasonalSnowMarker(wearNTear) && wearNTear.m_snowBuildup > maximum + SnowChangeEpsilon)
                    {
                        SetSnowBuildup(
                            wearNTear,
                            maximum,
                            markSeasonalSnow: true,
                            allowBelowMinimum: true);
                        SetAccumulationBaselineNow(
                            wearNTear,
                            maximum + SnowChangeEpsilon < GetSnowBuildupRange(wearNTear).x);
                        continue;
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

                SeasonalSnowCoverageChecks.Remove(wearNTear);
                SeasonalSnowMeltSources.Remove(wearNTear);

                if (!CanOwnSnowState(wearNTear) || IsDeepNorth(wearNTear))
                    continue;

                ClearTrackedInstance(wearNTear);
            }

            CraftingStationMeltStates = new ConditionalWeakTable<CraftingStation, StationMeltState>();
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
                if (zdo == null || !zdo.IsValid() || !SeasonalSnowPrefabs.Contains(zdo.GetPrefab()) || IsDeepNorth(zdo.GetPosition()))
                    continue;

                if (zdo.HasOwner() && !zdo.IsOwner())
                    continue;

                if (!HasTrackedSeasonalSnowState(zdo))
                    continue;

                zdo.Set(ZDOVars.s_snow, 0f);
                RemoveRuntimeState(zdo);
                cleared++;
            }

            if (cleared > 0)
                LogInfo($"Cleared seasonal snow state from {cleared} ZDO(s)");
        }

        public static void UpdateSeasonState()
        {
            if (!SeasonState.IsActive || seasonState.GetCurrentDay() <= 0)
                return;

            if (seasonState.GetCurrentSeason() != Season.Winter)
            {
                ClearLoadedSeasonalSnow();
                ClearServerSeasonalSnowZDOs();
            }

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

        private static bool IsFireplaceSnowMeltSupportedPosition(Vector3 position)
        {
            Heightmap.Biome biome = GetBiome(position);
            return biome == Heightmap.Biome.DeepNorth
                || (biome != Heightmap.Biome.None && biome != Heightmap.Biome.AshLands);
        }

        private static bool ShouldRunFireplaceSnowMelt(Fireplace fireplace)
        {
            if (!fireplace || fireplace.m_nview == null || !fireplace.m_nview.IsValid() || fireplace.m_nview.GetZDO() == null)
                return false;

            Vector3 position = fireplace.transform.position;
            Heightmap.Biome biome = GetBiome(position);
            if (biome == Heightmap.Biome.DeepNorth)
                return true;

            return SeasonState.IsActive
                && Enabled
                && controlEnvironments.Value
                && seasonState.GetCurrentSeason() == Season.Winter
                && biome != Heightmap.Biome.None
                && biome != Heightmap.Biome.AshLands
                && !IsIgnoredPosition(position);
        }

        private static void EnsureFireplaceSnowMeltSchedule(Fireplace fireplace)
        {
            if (!fireplace || !fireplace.m_snowMelter || fireplace.m_snowMelterInterval <= 0f ||
                fireplace.m_nview == null || !fireplace.m_nview.IsValid() || fireplace.m_nview.GetZDO() == null ||
                !IsFireplaceSnowMeltSupportedPosition(fireplace.transform.position) ||
                fireplace.IsInvoking(nameof(Fireplace.UpdateSnowMelt)))
                return;

            fireplace.UpdateSnowMelt();
            fireplace.InvokeRepeating(
                nameof(Fireplace.UpdateSnowMelt),
                UnityEngine.Random.Range(fireplace.m_snowMelterInterval, fireplace.m_snowMelterInterval * 2f),
                fireplace.m_snowMelterInterval);
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
                    if ((instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt) ||
                        !Equals(instruction.operand, UpdateBiomeMethod))
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
                __state.trackSeasonalSnow = CanOwnSnowState(__instance) && IsSeasonalSnowPosition(__instance) && !IsDeepNorth(__instance);

                if (__state.trackSeasonalSnow && __instance.m_addPreSnow)
                {
                    __instance.m_addPreSnow = false;
                    ZDO zdo = __instance.m_nview.GetZDO();
                    if (zdo != null && zdo.GetBool(ZDOVars.s_preSnow))
                        zdo.Set(ZDOVars.s_preSnow, false);
                }
            }

            [HarmonyPostfix]
            [HarmonyPriority(HarmonyLib.Priority.Last)]
            private static void Postfix(WearNTear __instance, UpdateWearState __state)
            {
                if (!CanOwnSnowState(__instance))
                    return;

                if (TryClearInvalidSeasonalSnow(__instance))
                    return;

                bool seasonalSnowPosition = IsSeasonalSnowPosition(__instance) && !IsDeepNorth(__instance);
                if (!seasonalSnowPosition)
                    return;

                bool melting = GetMeltMultiplier(__instance) > 0f;
                if (__state.trackSeasonalSnow && !melting && __instance.m_snowBuildup > __state.snowBefore + SnowChangeEpsilon)
                {
                    MarkSeasonalSnow(__instance);
                    RemoveBoolWithRevision(__instance.m_nview.GetZDO(), SeasonsVars.s_seasonalSnowMeltedBelowMinimum);
                }

                if (TryClearCoveredSeasonalSnow(__instance))
                    return;

                TryApplyPassiveSeasonalSnow(__instance);

                float maximum = GetSnowBuildupRange(__instance).y;
                if (!melting && HasSeasonalSnowMarker(__instance) && __instance.m_snowBuildup > maximum + SnowChangeEpsilon)
                {
                    SetSnowBuildup(
                        __instance,
                        maximum,
                        markSeasonalSnow: true,
                        allowBelowMinimum: true);
                    SetAccumulationBaselineNow(
                        __instance,
                        maximum + SnowChangeEpsilon < GetSnowBuildupRange(__instance).x);
                }

                ApplyRealtimeHeatMelt(__instance);
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

            [HarmonyPrefix]
            private static void Prefix(WearNTear __instance, float dt, ref UpdateCoverState __state)
            {
                __state.refreshExpected = __instance && __instance.m_updateCoverTimer + dt > WearNTear.c_UpdateCoverFrequency;
            }

            [HarmonyPostfix]
            private static void Postfix(WearNTear __instance, UpdateCoverState __state)
            {
                if (!__instance)
                    return;

                if (__state.refreshExpected && IsSeasonalSnowPosition(__instance))
                {
                    SnowCoverageState state = SeasonalSnowCoverageChecks.GetValue(__instance, _ => new SnowCoverageState());
                    state.haveRoof = __instance.m_haveRoof;
                    state.nextRoofCheckTime = Time.time + 5f;
                }

                TryClearCoveredSeasonalSnow(__instance);
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

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.OnPlaced))]
        private static class WearNTear_OnPlaced_SeasonalSnow
        {
            [HarmonyPostfix]
            private static void Postfix(WearNTear __instance)
            {
                MarkPlacedDuringWinter(__instance);
            }
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.RPC_SetSnow))]
        private static class WearNTear_RPC_SetSnow_SeasonalSnow
        {
            [HarmonyPrefix]
            private static bool Prefix(WearNTear __instance, ref float value, ref float __state)
            {
                __state = __instance ? __instance.m_snowBuildup : 0f;
                if (!__instance || __instance.m_nview == null || !__instance.m_nview.IsValid())
                    return true;

                ZDO zdo = __instance.m_nview.GetZDO();
                if (zdo == null)
                    return true;

                __state = zdo.GetFloat(ZDOVars.s_snow, __state);
                if (!__instance.m_nview.IsOwner())
                    return true;

                bool synchronizedSeasonState = SeasonState.IsActive && seasonState.GetCurrentDay() > 0;
                if (synchronizedSeasonState && IsSeasonalSnowSurface(__instance) &&
                    (!Enabled || !controlEnvironments.Value || seasonState.GetCurrentSeason() != Season.Winter))
                {
                    __instance.m_snowBuildup = __state;
                    __instance.UpdateSnowVisual();
                    return false;
                }

                if (!CanTrackSeasonalSnow(__instance))
                    return true;

                if (Single.IsNaN(value) || Single.IsInfinity(value))
                {
                    __instance.m_snowBuildup = __state;
                    __instance.UpdateSnowVisual();
                    return false;
                }

                value = Mathf.Clamp(value, 0f, GetSnowBuildupRange(__instance).y);
                if (value > __state + SnowChangeEpsilon)
                {
                    __instance.m_snowBuildup = __state;
                    __instance.UpdateSnowVisual();
                    return false;
                }

                return true;
            }

            [HarmonyPostfix]
            private static void Postfix(WearNTear __instance, float __state)
            {
                RecordSnowDecrease(__instance, __state);
            }
        }

        [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.PokeInUse))]
        private static class CraftingStation_PokeInUse_SeasonalSnow
        {
            [HarmonyPrepare]
            private static bool Prepare()
            {
                EnsureSnowMeltConfigs();
                return true;
            }

            [HarmonyPostfix]
            private static void Postfix(CraftingStation __instance)
            {
                UpdateCraftingStationMelt(__instance);
            }
        }

        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Awake))]
        private static class Fireplace_Awake_SeasonalSnowMelt
        {
            [HarmonyPostfix]
            private static void Postfix(Fireplace __instance)
            {
                EnsureFireplaceSnowMeltSchedule(__instance);
            }
        }

        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.UpdateSnowMelt))]
        private static class Fireplace_UpdateSnowMelt_SeasonalSnow
        {
            [HarmonyPrefix]
            private static bool Prefix(Fireplace __instance)
            {
                return ShouldRunFireplaceSnowMelt(__instance);
            }
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.OnDestroy))]
        private static class WearNTear_OnDestroy_SeasonalSnow
        {
            [HarmonyPrefix]
            private static void Prefix(WearNTear __instance)
            {
                if (!__instance)
                    return;

                SeasonalSnowCoverageChecks.Remove(__instance);
                SeasonalSnowMeltSources.Remove(__instance);
            }
        }
    }
}
