using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    public class SeasonState
    {
        private Season m_season = Season.Spring;
        private int m_day = 0;
        private int m_worldDay = 0;
        private int m_dayInSeasonGlobal = 0;
        private bool m_seasonIsChanging = false;
        private bool m_isUsingIngameDays = true;

        public static readonly Dictionary<Season, SeasonSettings> seasonsSettings = new Dictionary<Season, SeasonSettings>();
        public static List<SeasonEnvironment> seasonEnvironments = SeasonEnvironment.GetDefaultCustomEnvironments();
        public static SeasonBiomeEnvironments seasonBiomeEnvironments = new SeasonBiomeEnvironments(loadDefaults: true);
        public static SeasonRandomEvents seasonRandomEvents = new SeasonRandomEvents(loadDefaults: true);
        public static SeasonLightings seasonLightings = new SeasonLightings(loadDefaults: true);
        public static SeasonStats seasonStats = new SeasonStats(loadDefaults: true);
        public static SeasonTraderItems seasonTraderItems = new SeasonTraderItems(loadDefaults: true);
        public static SeasonWorldSettings seasonWorldSettings = new SeasonWorldSettings();
        public static SeasonGrassSettings seasonGrassSettings = new SeasonGrassSettings(loadDefaults: true);
        public static SeasonClutterSettings seasonClutterSettings = new SeasonClutterSettings(loadDefaults: true);
        public static SeasonBiomeSettings seasonBiomeSettings = new SeasonBiomeSettings(loadDefaults: true);

        private static readonly Season[] _seasons = (Season[])Enum.GetValues(typeof(Season));
        private static readonly Dictionary<Heightmap.Biome, string> biomesDefault = new Dictionary<Heightmap.Biome, string>();
        private static readonly Dictionary<string, EnvSetup> replacedEnvironmentDefaults = new Dictionary<string, EnvSetup>(StringComparer.Ordinal);
        private static readonly Dictionary<string, EnvSetup> appliedSeasonEnvironmentObjects = new Dictionary<string, EnvSetup>(StringComparer.Ordinal);
        private static readonly HashSet<Texture2D> generatedEnvironmentTextures = new HashSet<Texture2D>();
        private static readonly List<ItemDrop.ItemData> _itemDataList = new List<ItemDrop.ItemData>();
        private static readonly HashSet<string> _coolingFoodNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static int _pendingSeasonChange = 0;
        private static string _coolingFoodNamesValue = string.Empty;
        private SeasonSettings settings
        {
            get
            {
                if (!seasonsSettings.ContainsKey(m_season))
                    seasonsSettings.Add(m_season, new SeasonSettings(m_season));

                return seasonsSettings[m_season];
            }
        }

        public SeasonState(bool initialize = false)
        {
            if (!initialize)
                return;

            ClearBiomesDefault();
            ResetEnvironmentStateTracking();

            foreach (Season season in _seasons)
                if (!seasonsSettings.ContainsKey(season))
                    seasonsSettings.Add(season, new SeasonSettings(season));

            string folder = Path.Combine(configDirectory, SeasonSettings.defaultsSubdirectory);
            Directory.CreateDirectory(folder);

            LogInfo($"Saving default seasons settings");
            foreach (KeyValuePair<Season, SeasonSettings> seasonSettings in seasonsSettings)
            {
                string filename = Path.Combine(folder, GetSeasonalFileName(seasonSettings.Key));
                seasonSettings.Value.SaveToJSON(filename);
            }

            SeasonSettings.SaveDefaultEnvironments(folder);
            SeasonSettings.SaveDefaultEvents(folder);
            SeasonSettings.SaveDefaultLightings(folder);
            SeasonSettings.SaveDefaultStats(folder);
            SeasonSettings.SaveDefaultTraderItems(folder);
            SeasonSettings.SaveDefaultWorldSettings(folder);
            SeasonSettings.SaveDefaultGrassSettings(folder);
            SeasonSettings.SaveDefaultClutterSettings(folder);
            SeasonSettings.SaveDefaultBiomesSettings(folder);

            UpdateUsingOfIngameDays();
        }

        public static string GetSeasonalFileName(Season season) => $"{season}.json";

        public static bool IsActive => seasonState != null && EnvMan.instance != null;

        public static long GetDayLengthInSecondsEnvMan() => EnvMan.instance == null ? (dayLengthSec.Value != 0L ? dayLengthSec.Value : 1800L) : EnvMan.instance.m_dayLengthSec;

        public int GetWorldDay(double seconds) => (int)(seconds / GetDayLengthInSeconds());

        public int GetCurrentWorldDay() => GetWorldDay(GetTotalSeconds());

        public void UpdateState(bool timeForSeasonToChange = false, bool forceSeasonChange = false)
        {
            if (!IsActive || !ZNet.instance.IsServer())
                return;

            int worldDay = GetCurrentWorldDay();
            m_dayInSeasonGlobal = GetDayInSeason(worldDay);
            Season newSeason = GetSeason(worldDay);

            int currentSeason = (int)m_season;

            forceSeasonChange = forceSeasonChange || !m_isUsingIngameDays || newSeason == GetPreviousSeason(m_season) || Math.Abs(m_worldDay - worldDay) > 1;

            bool sleepCheck = forceSeasonChange
                            || !changeSeasonOnlyAfterSleep.Value
                            || Game.instance.m_sleeping;

            if (logTime.Value)
                LogInfo($"Current: {m_season,-6} {m_day} {m_worldDay} New: {newSeason,-6} {m_dayInSeasonGlobal} {worldDay} Time: {EnvMan.instance.GetDayFraction(),-6:F4} TotalSeconds: {GetTotalSeconds(),-10:F2} TimeToChange:{timeForSeasonToChange,-5} SleepCheck:{sleepCheck,-5} Force:{forceSeasonChange,-5} ToPast:{timeForSeasonToChange && !forceSeasonChange && !sleepCheck && m_isUsingIngameDays && changeSeasonOnlyAfterSleep.Value && GetCurrentDay() == GetDaysInSeason() && m_dayInSeasonGlobal != GetCurrentDay(),-5}");

            Season setSeason = m_season;
            if (overrideSeason.Value)
                setSeason = seasonOverrided.Value;
            else if (newSeason != GetCurrentSeason() && (timeForSeasonToChange || forceSeasonChange))
            {
                if (timeForSeasonToChange && !forceSeasonChange && !sleepCheck && m_isUsingIngameDays && changeSeasonOnlyAfterSleep.Value && GetCurrentDay() == GetDaysInSeason() && m_dayInSeasonGlobal != GetCurrentDay())
                {
                    double timeSeconds = ZNet.instance.GetTimeSeconds() - EnvMan.instance.m_dayLengthSec;

                    ZNet.instance.SetNetTime(Math.Max(timeSeconds, 0));
                    ZNet.instance.SendNetTime();

                    EnvMan.instance.m_skipTime = false;
                    EnvMan.instance.m_totalSeconds = ZNet.instance.GetTimeSeconds();

                    worldDay = GetCurrentWorldDay();
                    m_dayInSeasonGlobal = GetDayInSeason(worldDay);
                    newSeason = GetSeason(worldDay);
                }

                setSeason = newSeason;
            }

            if (overrideSeasonDay.Value)
                m_dayInSeasonGlobal = Math.Clamp(seasonDayOverrided.Value, 1, GetDaysInSeason(setSeason));

            if (!CheckIfSeasonChanged(currentSeason, setSeason, m_dayInSeasonGlobal, worldDay))
                CheckIfDayChanged(m_dayInSeasonGlobal, worldDay, forceSeasonChange);
        }

        public void OnBiomeChange(Heightmap.Biome previousBiome, Heightmap.Biome currentBiome)
        {
            if (previousBiome == currentBiome)
                return;

            if (GetCurrentSeason() == Season.Winter && (previousBiome == Heightmap.Biome.AshLands || currentBiome == Heightmap.Biome.AshLands))
                ZoneSystemVariantController.UpdateWaterState();

            if (GetTorchAsFiresource() && (TorchHeatInBiome(previousBiome) != TorchHeatInBiome(currentBiome)))
                UpdateTorchesFireWarmth();
        }

        public void OnInteriorChanged(bool inInterior)
        {
            if (disableTorchWarmthInInterior.Value)
                UpdateTorchesFireWarmth();
        }

        private World GetCurrentWorld() => ZNet.m_world ?? (WorldGenerator.instance?.m_world);

        private void UpdateUsingOfIngameDays()
        {
            bool previous = m_isUsingIngameDays;
            m_isUsingIngameDays = !seasonWorldSettings.HasWorldSettings(GetCurrentWorld());

            if (m_isUsingIngameDays != previous)
                UpdateState(forceSeasonChange: true);
        }

        private DateTime GetStartTimeUTC()
        {
            return seasonWorldSettings.GetStartTimeUTC(GetCurrentWorld());
        }

        public double GetTotalSeconds()
        {
            return m_isUsingIngameDays ? ZNet.instance.GetTimeSeconds() : DateTime.UtcNow.Subtract(GetStartTimeUTC()).TotalSeconds;
        }

        public long GetDayLengthInSeconds()
        {
            return Math.Max(5, m_isUsingIngameDays ? GetDayLengthInSecondsEnvMan() : seasonWorldSettings.GetDayLengthSeconds(GetCurrentWorld()));
        }

        public Season GetCurrentSeason()
        {
            return m_season;
        }

        public bool GetSeasonIsChanging()
        {
            return showFadeOnSeasonChange.Value && m_seasonIsChanging;
        }

        public int GetCurrentDay()
        {
            return m_day;
        }

        public int GetDaysInSeason()
        {
            return Math.Max(1, settings.m_daysInSeason);
        }

        public int GetDaysInSeason(Season season)
        {
            return Math.Max(1, GetSeasonSettings(season).m_daysInSeason);
        }

        public long GetSecondsInSeason()
        {
            return GetDaysInSeason() * GetDayLengthInSeconds();
        }

        public long GetSecondsInSeason(Season season)
        {
            return GetDaysInSeason(season) * GetDayLengthInSeconds();
        }

        public float GetPlantsGrowthMultiplier()
        {
            return GetPlantsGrowthMultiplier(GetCurrentSeason());
        }

        public float GetPlantsGrowthMultiplier(Season season)
        {
            return GetSeasonSettings(season).m_plantsGrowthMultiplier;
        }

        public Season GetPreviousSeason()
        {
            return GetPreviousSeason(m_season);
        }

        public Season GetNextSeason()
        {
            return GetNextSeason(m_season);
        }

        public Season GetPreviousSeason(Season season)
        {
            return (Season)((seasonsCount + (int)season - 1) % seasonsCount);
        }

        public Season GetNextSeason(Season season)
        {
            return (Season)(((int)season + 1) % seasonsCount);
        }

        public int GetNightLength()
        {
            int day = GetCurrentWorldDay();
            return GetNightLength(GetSeason(day), GetDayInSeason(day));
        }

        public int GetNightLength(Season season, int dayInSeason)
        {
            int currentNightLength = GetSeasonSettings(season).m_nightLength;
            if (!changeNightLengthGradually.Value)
                return currentNightLength;

            int daysInSeason = GetDaysInSeason(season);

            float currentPeakDay = daysInSeason / 2f;
            int lastPeakDay = Mathf.CeilToInt(currentPeakDay);
            int firstPeakDay = Mathf.FloorToInt(currentPeakDay);

            if (dayInSeason == firstPeakDay || dayInSeason == lastPeakDay)
                return currentNightLength;
            else if (dayInSeason < firstPeakDay)
            {
                Season previous = GetPreviousSeason(season);
                int daysInPreviousSeason = GetDaysInSeason(previous);

                lastPeakDay = Mathf.CeilToInt(daysInPreviousSeason / 2f);
                int daysInPrevious = daysInPreviousSeason - lastPeakDay;

                int previousNightLength = GetSeasonSettings(previous).m_nightLength;

                return Mathf.RoundToInt(Mathf.Lerp(previousNightLength, currentNightLength, (float)(dayInSeason + daysInPrevious) / (firstPeakDay + daysInPrevious)));
            }
            else if (dayInSeason > lastPeakDay)
            {
                Season next = GetNextSeason(season);
                int daysInNextSeason = GetDaysInSeason(next);

                firstPeakDay = Mathf.FloorToInt(daysInNextSeason / 2f);
                int daysLeft = daysInSeason - lastPeakDay;

                int nextNightLength = GetSeasonSettings(next).m_nightLength;

                return Mathf.RoundToInt(Mathf.Lerp(currentNightLength, nextNightLength, (float)(dayInSeason - lastPeakDay) / (firstPeakDay + daysLeft)));
            }

            return currentNightLength;
        }

        public static void InitializeTextureControllers()
        {
            if (!ZoneSystem.instance.TryGetComponent<PrefabVariantController>(out _))
                ZoneSystem.instance.gameObject.AddComponent<PrefabVariantController>();
            PrefabVariantController.AddControllerToPrefabs();
            ClutterVariantController.Initialize();
            if (!ZoneSystem.instance.TryGetComponent<ZoneSystemVariantController>(out _))
                ZoneSystem.instance.gameObject.AddComponent<ZoneSystemVariantController>().Initialize(ZoneSystem.instance);
            FillListsToControl();
            InvalidatePositionsCache();
            CustomTextures.SetupConfigWatcher();
            CustomMusic.SetupConfigWatcher();
        }

        public static void ClearBiomesDefault() => biomesDefault.Clear();

        public static void RefreshBiomesDefault(bool forceUpdate)
        {
            if (forceUpdate)
                ClearBiomesDefault();

            if (!EnvMan.instance)
                return;

            foreach (BiomeEnvSetup biome in EnvMan.instance.m_biomes)
            {
                if (biomesDefault.TryGetValue(biome.m_biome, out string biomeJSON))
                {
                    if (forceUpdate)
                    {
                        // Combine several entries just in case
                        BiomeEnvSetup biomeEnvironment = JsonUtility.FromJson<BiomeEnvSetup>(biomeJSON);
                        biomeEnvironment.m_environments.AddRange(biome.m_environments);
                        biomesDefault[biome.m_biome] = JsonUtility.ToJson(biomeEnvironment);
                    }
                }
                else
                {
                    biomesDefault[biome.m_biome] = JsonUtility.ToJson(biome);
                }
            }
        }

        private void UpdateBiomesSetup()
        {
            RefreshBiomesDefault(forceUpdate: false);

            SeasonalSnow.BeginBiomeEnvironmentUpdate();
            try
            {
                foreach (string biomeEnvironmentDefault in biomesDefault.Values)
                    RegisterSeasonalSnowBiomeEnvironment(biomeEnvironmentDefault);

                if (Compatibility.EWDCompat.ShouldApplySeasonalRulesToAvailableEnvironments())
                {
                    RefreshBiomeEnvironmentReferences();
                    UpdateCurrentEnvironment();
                    return;
                }

                EnvMan.instance.m_biomes.Clear();

                biomesDefault.Do(kvp => ChangeBiomeEnvironment(kvp.Value));

                RefreshBiomeEnvironmentReferences();
                UpdateCurrentEnvironment();

                Compatibility.EWDCompat.OnSeasonsBiomeSetupApplied();
            }
            finally
            {
                SeasonalSnow.EndBiomeEnvironmentUpdate();
            }

            void RegisterSeasonalSnowBiomeEnvironment(string biomeEnvironmentDefault)
            {
                try
                {
                    BiomeEnvSetup biomeEnvironment =
                        JsonUtility.FromJson<BiomeEnvSetup>(biomeEnvironmentDefault);

                    List<EnvEntry> environments =
                        ApplySeasonBiomeEnvironmentRules(biomeEnvironment.m_biome, biomeEnvironment.m_environments);
                    SeasonalSnow.RegisterBiomeEnvironments(biomeEnvironment.m_biome, environments);
                }
                catch (Exception e)
                {
                    LogWarning($"Error preparing seasonal snow biome setup:\n{biomeEnvironmentDefault}\n{e}");
                }
            }

            void ChangeBiomeEnvironment(string biomeEnvironmentDefault)
            {
                try
                {
                    BiomeEnvSetup biomeEnvironment =
                        JsonUtility.FromJson<BiomeEnvSetup>(biomeEnvironmentDefault);

                    EnvMan.instance.AppendBiomeSetup(biomeEnvironment);
                }
                catch (Exception e)
                {
                    LogWarning($"Error appending biome setup:\n{biomeEnvironmentDefault}\n{e}");
                }
            }
        }

        public static void UpdateSeasonSettings()
        {
            if (!IsActive)
                return;

            JsonSerializerSettings jsonSettings = new()
            {
                DefaultValueHandling = DefaultValueHandling.Ignore
            };

            seasonsSettings.Clear();
            foreach (KeyValuePair<int, string> item in seasonsSettingsJSON.Value)
            {
                try
                {
                    if (!String.IsNullOrEmpty(item.Value))
                    {
                        seasonsSettings[(Season)item.Key] = new SeasonSettings((Season)item.Key, JsonConvert.DeserializeObject<SeasonSettingsFile>(item.Value, jsonSettings));
                        LogInfo($"Settings updated: {(Season)item.Key}");
                    }
                }
                catch (Exception e)
                {
                    LogWarning($"Error parsing settings: {(Season)item.Key}\n{e}");
                }
            }

            seasonState.UpdateUsingOfIngameDays();
            SeasonalSnow.RefreshWeatherTimeline();

            seasonState.UpdateTorchesFireWarmth();

            LoadingTips.UpdateLoadingTips();

            EnvManPatches.settingsUpdated = true;
        }

        public static void UpdateSeasonEnvironments()
        {
            UpdateSeasonEnvironments(rebuildBiomeSetup: true);
        }

        private static void UpdateSeasonEnvironments(bool rebuildBiomeSetup)
        {
            if (!IsActive || EnvMan.instance == null)
                return;

            unresolvedSeasonEnvironmentRules.Clear();

            if (!controlEnvironments.Value)
            {
                RestoreEnvironmentControlState();
                return;
            }

            RemoveAppliedSeasonEnvironments();

            CustomMusic.CheckMusicList();

            SeasonEnvironment.RebuildCachedObjects();

            if (!String.IsNullOrEmpty(customEnvironmentsJSON.Value))
            {
                try
                {
                    seasonEnvironments = JsonConvert.DeserializeObject<List<SeasonEnvironment>>(customEnvironmentsJSON.Value);
                    seasonEnvironments = SortCustomEnvironmentsByCloneDependencies(seasonEnvironments);
                    LogInfo($"Custom environments updated");
                }
                catch (Exception e)
                {
                    LogWarning($"Error parsing custom environments:\n{e}");
                }
            }
            else
            {
                seasonEnvironments = SeasonEnvironment.GetDefaultCustomEnvironments();
                seasonEnvironments = SortCustomEnvironmentsByCloneDependencies(seasonEnvironments);
                LogInfo($"Custom environments loaded defaults");
            }

            SeasonEnvironment.AddCachedObjectsFromCurrentEnvironments();

            foreach (SeasonEnvironment senv in seasonEnvironments)
            {
                if (senv == null || String.IsNullOrWhiteSpace(senv.m_name))
                    continue;

                EnvSetup existingEnvironment = EnvMan.instance.GetEnv(senv.m_name);
                if (existingEnvironment != null)
                {
                    replacedEnvironmentDefaults[senv.m_name] = existingEnvironment;
                    EnvMan.instance.m_environments.Remove(existingEnvironment);
                }

                EnvSetup environment = senv.ToEnvSetup();
                EnvMan.instance.AppendEnvironment(environment);
                generatedEnvironmentTextures.Add(environment.m_auroraGradientTexture);
                appliedSeasonEnvironmentObjects[senv.m_name] = environment;

                SeasonEnvironment.AddCachedObjects(environment);
            }

            if (rebuildBiomeSetup)
                seasonState.UpdateBiomesSetup();
            else
                UpdateCurrentEnvironment();
        }

        public static void UpdateEnvironmentControlState()
        {
            if (IsActive)
            {
                if (controlEnvironments.Value)
                {
                    UpdateSeasonEnvironments(rebuildBiomeSetup: false);
                    UpdateBiomeEnvironments();
                }
                else
                {
                    RestoreEnvironmentControlState();
                }
            }

            LoadingTips.UpdateLoadingTips();
        }

        public static void PrepareForExternalEnvironmentUpdate()
        {
            if (EnvMan.instance == null || appliedSeasonEnvironmentObjects.Count == 0)
                return;

            bool appliedEnvironmentStillPresent = appliedSeasonEnvironmentObjects.Values
                .Any(environment => environment != null && EnvMan.instance.m_environments.Contains(environment));

            if (!appliedEnvironmentStillPresent)
                ResetEnvironmentStateTracking();
        }

        public static void ResetEnvironmentStateTracking()
        {
            replacedEnvironmentDefaults.Clear();
            appliedSeasonEnvironmentObjects.Clear();
        }

        private static void RestoreEnvironmentControlState()
        {
            unresolvedSeasonEnvironmentRules.Clear();
            RemoveAppliedSeasonEnvironments();
            seasonState.UpdateBiomesSetup();
        }

        private static void RemoveAppliedSeasonEnvironments()
        {
            if (EnvMan.instance == null)
            {
                ResetEnvironmentStateTracking();
                return;
            }

            foreach (EnvSetup environment in appliedSeasonEnvironmentObjects.Values)
            {
                if (environment != null)
                    EnvMan.instance.m_environments.Remove(environment);
            }

            foreach (KeyValuePair<string, EnvSetup> defaultEnvironment in replacedEnvironmentDefaults)
            {
                if (defaultEnvironment.Value != null && EnvMan.instance.GetEnv(defaultEnvironment.Key) == null)
                    // The saved native entry was already initialized, including its gradient texture.
                    EnvMan.instance.m_environments.Add(defaultEnvironment.Value);
            }

            ResetEnvironmentStateTracking();
        }

        internal static void ReleaseUnusedEnvironmentTextures()
        {
            if (generatedEnvironmentTextures.Count == 0)
                return;

            EnvMan environmentManager = EnvMan.instance;
            foreach (Texture2D texture in generatedEnvironmentTextures.ToArray())
            {
                if (texture && environmentManager != null
                    && (environmentManager.m_currentEnv?.m_auroraGradientTexture == texture
                        || environmentManager.m_prevEnv?.m_auroraGradientTexture == texture
                        || environmentManager.m_nextEnv?.m_auroraGradientTexture == texture
                        || environmentManager.m_environments.Any(environment => environment.m_auroraGradientTexture == texture)))
                    continue;

                if (texture)
                    UnityEngine.Object.Destroy(texture);
                generatedEnvironmentTextures.Remove(texture);
            }
        }

        private static void RefreshBiomeEnvironmentReferences()
        {
            if (EnvMan.instance?.m_biomes == null)
                return;

            HashSet<string> unresolvedEnvironments = new HashSet<string>();

            foreach (BiomeEnvSetup biomeEnvironment in EnvMan.instance.m_biomes)
            {
                if (biomeEnvironment == null)
                    continue;

                EnvMan.instance.InitializeBiomeEnvSetup(biomeEnvironment);

                foreach (EnvEntry environment in biomeEnvironment.m_environments)
                {
                    if (environment != null && environment.m_env == null && !String.IsNullOrWhiteSpace(environment.m_environment))
                        unresolvedEnvironments.Add(environment.m_environment);
                }
            }

            if (unresolvedEnvironments.Count > 0)
                LogWarning($"Unresolved biome environment references: {String.Join(", ", unresolvedEnvironments.OrderBy(name => name))}");
        }

        public static void ReapplyEnvironmentStateAfterWorldInitialization()
        {
            if (!IsActive)
                return;

            UpdateSeasonEnvironments(rebuildBiomeSetup: false);
            UpdateBiomeEnvironments();

            if (currentSeasonDay.Value > 0)
                OnSeasonDayChange();
        }

        private static void UpdateCurrentEnvironment()
        {
            EnvMan.instance.m_environmentPeriod = -1L;
        }

        public static void UpdateBiomeEnvironments()
        {
            if (!IsActive)
                return;

            unresolvedSeasonEnvironmentRules.Clear();

            if (!controlEnvironments.Value)
                return;

            if (!String.IsNullOrEmpty(customBiomeEnvironmentsJSON.Value))
            {
                try
                {
                    seasonBiomeEnvironments = JsonConvert.DeserializeObject<SeasonBiomeEnvironments>(customBiomeEnvironmentsJSON.Value);
                    LogInfo($"Custom biome environments updated");
                }
                catch (Exception e)
                {
                    LogWarning($"Error parsing custom biome environments:\n{e}");
                }
            }
            else
            {
                seasonBiomeEnvironments = new SeasonBiomeEnvironments(loadDefaults: true);
                LogInfo($"Custom biome environments loaded defaults");
            }

            seasonState.UpdateBiomesSetup();
        }

        public static void UpdateRandomEvents()
        {
            if (!IsActive)
                return;

            if (!String.IsNullOrEmpty(customEventsJSON.Value))
            {
                try
                {
                    seasonRandomEvents = JsonConvert.DeserializeObject<SeasonRandomEvents>(customEventsJSON.Value);
                    LogInfo($"Custom events updated");
                }
                catch (Exception e)
                {
                    LogWarning($"Error parsing custom events:\n{e}");
                }
            }
            else
            {
                seasonRandomEvents = new SeasonRandomEvents(loadDefaults: true);
                LogInfo($"Custom events loaded defaults");
            }
        }

        public static void UpdateLightings()
        {
            if (!IsActive)
                return;

            if (!String.IsNullOrEmpty(customLightingsJSON.Value))
            {
                try
                {
                    seasonLightings = JsonConvert.DeserializeObject<SeasonLightings>(customLightingsJSON.Value);
                    LogInfo($"Custom lightings updated");
                }
                catch (Exception e)
                {
                    LogWarning($"Error parsing custom lightings:\n{e}");
                }
            }
            else
            {
                seasonLightings = new SeasonLightings(loadDefaults: true);
                LogInfo($"Custom lightings loaded defaults");
            }
        }

        public static void UpdateStats()
        {
            if (!IsActive)
                return;

            if (!String.IsNullOrEmpty(customStatsJSON.Value))
            {
                try
                {
                    seasonStats = JsonConvert.DeserializeObject<SeasonStats>(customStatsJSON.Value);
                    LogInfo($"Custom stats updated");
                }
                catch (Exception e)
                {
                    LogWarning($"Error parsing custom stats:\n{e}");
                }
            }
            else
            {
                seasonStats = new SeasonStats(loadDefaults: true);
                LogInfo($"Custom stats loaded defaults");
            }

            SE_Season.UpdateSeasonStatusEffectStats();
        }

        public static void UpdateTraderItems()
        {
            if (!IsActive)
                return;

            if (!String.IsNullOrEmpty(customTraderItemsJSON.Value))
            {
                try
                {
                    seasonTraderItems = JsonConvert.DeserializeObject<SeasonTraderItems>(customTraderItemsJSON.Value);
                    LogInfo($"Custom trader items updated");
                }
                catch (Exception e)
                {
                    LogWarning($"Error parsing custom trader items:\n{e}");
                }
            }
            else
            {
                seasonTraderItems = new SeasonTraderItems(loadDefaults: true);
                LogInfo($"Custom trader items loaded defaults");
            }
        }

        public static void UpdateWorldSettings()
        {
            if (!IsActive)
                return;

            if (!String.IsNullOrEmpty(customWorldSettingsJSON.Value))
            {
                try
                {
                    seasonWorldSettings = JsonConvert.DeserializeObject<SeasonWorldSettings>(customWorldSettingsJSON.Value);
                    LogInfo($"Custom world settings updated");
                }
                catch (Exception e)
                {
                    LogWarning($"Error parsing world settings items:\n{e}");
                }
            }
            else
            {
                seasonWorldSettings = new SeasonWorldSettings();
                LogInfo($"Custom world settings loaded defaults");
            }

            seasonState.UpdateUsingOfIngameDays();
        }

        public static void UpdateGrassSettings()
        {
            if (!IsActive)
                return;

            if (!String.IsNullOrEmpty(customGrassSettingsJSON.Value))
            {
                try
                {
                    seasonGrassSettings = JsonConvert.DeserializeObject<SeasonGrassSettings>(customGrassSettingsJSON.Value);
                    LogInfo($"Custom grass settings updated");
                }
                catch (Exception e)
                {
                    LogWarning($"Error parsing custom grass settings:\n{e}");
                }
            }
            else
            {
                seasonGrassSettings = new SeasonGrassSettings(loadDefaults: true);
                LogInfo($"Custom grass settings loaded defaults");
            }

            StartClutterUpdate();
        }

        public static void UpdateClutterSettings()
        {
            if (!IsActive)
                return;

            if (!String.IsNullOrEmpty(customClutterSettingsJSON.Value))
            {
                try
                {
                    seasonClutterSettings = JsonConvert.DeserializeObject<SeasonClutterSettings>(customClutterSettingsJSON.Value);
                    LogInfo($"Custom clutter settings updated");
                }
                catch (Exception e)
                {
                    LogWarning($"Error parsing custom clutter settings:\n{e}");
                }
            }
            else
            {
                seasonClutterSettings = new SeasonClutterSettings(loadDefaults: true);
                LogInfo($"Custom clutter settings loaded defaults");
            }

            StartClutterUpdate();
        }

        public static void UpdateBiomeSettings()
        {
            if (!IsActive)
                return;

            if (!String.IsNullOrEmpty(customBiomeSettingsJSON.Value))
            {
                try
                {
                    seasonBiomeSettings = JsonConvert.DeserializeObject<SeasonBiomeSettings>(customBiomeSettingsJSON.Value);
                    LogInfo($"Custom biomes settings updated");
                }
                catch (Exception e)
                {
                    LogWarning($"Error parsing custom biomes settings:\n{e}");
                }
            }
            else
            {
                seasonBiomeSettings = new SeasonBiomeSettings(loadDefaults: true);
                LogInfo($"Custom biomes settings loaded defaults");
            }

            ZoneSystemVariantController.UpdateTerrainColors();
        }

        public void UpdateGlobalKeys()
        {
            if (!IsActive || !ZoneSystem.instance || !ZNet.instance || !ZNet.instance.IsServer())
                return;

            foreach (Season season in _seasons)
                ZoneSystem.instance.RemoveGlobalKey(GetSeasonalGlobalKey(season));

            string globalKey = GetSeasonalGlobalKey(GetCurrentSeason());

            if (enableSeasonalGlobalKeys.Value)
                ZoneSystem.instance.SetGlobalKey(globalKey);

            for (int i = 0; i <= seasonState.GetYearLengthInDays(); i++)
                ZoneSystem.instance.RemoveGlobalKey(GetSeasonalDayGlobalKey(i));

            if (enableSeasonalGlobalKeys.Value && !(globalKey = GetSeasonalDayGlobalKey(seasonState.GetCurrentDay())).IsNullOrWhiteSpace())
                ZoneSystem.instance.SetGlobalKey(globalKey);
        }

        public string GetSeasonalGlobalKey(Season season)
        {
            return season switch
            {
                Season.Spring => seasonalGlobalKeySpring.Value,
                Season.Summer => seasonalGlobalKeySummer.Value,
                Season.Fall => seasonalGlobalKeyFall.Value,
                Season.Winter => seasonalGlobalKeyWinter.Value,
                _ => seasonalGlobalKeySpring.Value
            };
        }

        public string GetSeasonalDayGlobalKey(int day)
        {
            return string.Format(seasonalGlobalKeyDay.Value, day.ToString());
        }

        public double GetTimeToCurrentSeasonEnd()
        {
            return GetEndOfCurrentSeason() + (seasonState.DayStartFraction() * (1f - ((0.25f - GetDayFractionForSeasonChange()) * seasonState.DayStartFraction() / 0.25f)) * seasonState.GetDayLengthInSeconds()) - seasonState.GetTotalSeconds();
        }

        public double GetEndOfCurrentSeason()
        {
            return GetStartOfCurrentSeason() + seasonState.GetSecondsInSeason();
        }

        public double GetStartOfCurrentSeason()
        {
            double startOfDay = GetTotalSeconds() - GetTotalSeconds() % GetDayLengthInSeconds();
            return startOfDay - (GetCurrentDay() - (IsPendingSeasonChange() ? 0 : 1)) * GetDayLengthInSeconds();
        }

        public bool IsPendingSeasonChange()
        {
            return 0 < m_dayInSeasonGlobal && m_dayInSeasonGlobal < GetCurrentDay();
        }

        public float DayStartFraction()
        {
            int worldDay = GetCurrentWorldDay();
            return DayStartFraction(GetSeason(worldDay), GetDayInSeason(worldDay));
        }

        public float DayStartFraction(Season season, int dayInSeason)
        {
            return (seasonState.GetNightLength(season, dayInSeason) / 2f) / 100f;
        }

        public bool GetTorchAsFiresource()
        {
            return settings.m_torchAsFiresource;
        }

        public float GetTorchDurabilityDrain()
        {
            return settings.m_torchDurabilityDrain;
        }

        public float GetBeehiveProductionMultiplier()
        {
            return GetBeehiveProductionMultiplier(seasonState.GetCurrentSeason());
        }

        public float GetBeehiveProductionMultiplier(Season season)
        {
            return GetSeasonSettings(season).m_beehiveProductionMultiplier;
        }

        public float GetFoodDrainMultiplier()
        {
            return settings.m_foodDrainMultiplier;
        }

        public float GetStaminaDrainMultiplier()
        {
            return settings.m_staminaDrainMultiplier;
        }

        public float GetFireplaceDrainMultiplier()
        {
            return GetFireplaceDrainMultiplier(seasonState.GetCurrentSeason());
        }

        public float GetFireplaceDrainMultiplier(Season season)
        {
            return GetSeasonSettings(season).m_fireplaceDrainMultiplier;
        }

        public float GetSapCollectingSpeedMultiplier()
        {
            return GetSapCollectingSpeedMultiplier(seasonState.GetCurrentSeason());
        }

        public float GetSapCollectingSpeedMultiplier(Season season)
        {
            return GetSeasonSettings(season).m_sapCollectingSpeedMultiplier;
        }

        public bool GetRainProtection()
        {
            return settings.m_rainProtection;
        }

        public float GetWoodFromTreesMultiplier()
        {
            return settings.m_woodFromTreesMultiplier;
        }

        public float GetMeatFromAnimalsMultiplier()
        {
            return settings.m_meatFromAnimalsMultiplier;
        }

        public float GetWindIntensityMultiplier()
        {
            return settings.m_windIntensityMultiplier;
        }

        public float GetRestedBuffDurationMultiplier()
        {
            return settings.m_restedBuffDurationMultiplier;
        }

        public float GetLivestockProcreationMultiplier()
        {
            return settings.m_livestockProcreationMultiplier;
        }

        public bool GetOverheatIn2WarmClothes()
        {
            return settings.m_overheatIn2WarmClothes;
        }

        public float GetTreesReqrowthChance()
        {
            return settings.m_treesRegrowthChance;
        }

        public SeasonSettings GetSeasonSettings(Season season)
        {
            return seasonsSettings.ContainsKey(season) ? seasonsSettings[season] : new SeasonSettings(season);
        }

        public Season GetSeason(int day)
        {
            int dayOfYear = GetDayOfYear(day);
            int days = 0;
            foreach (Season season in _seasons)
            {
                days += GetDaysInSeason(season);
                if (dayOfYear <= days)
                    return season;
            }

            return Season.Winter;
        }

        public int GetDayInSeason(int day)
        {
            int dayOfYear = GetDayOfYear(day);
            int days = 0;
            int daysInSeason = 0;
            foreach (Season season in _seasons)
            {
                daysInSeason = GetDaysInSeason(season);
                if (dayOfYear <= days + daysInSeason)
                    return dayOfYear - days;
                days += daysInSeason;
            }
            return dayOfYear >= days ? daysInSeason : dayOfYear - days;
        }

        public int GetDayOfYear(int day)
        {
            int yearLength = GetYearLengthInDays();
            int dayOfYear = day % yearLength;
            return dayOfYear == day ? dayOfYear : (dayOfYear == 0 ? yearLength : dayOfYear);
        }

        public float GetWaterSurfaceFreezeStatus()
        {
            if (!enableFrozenWater.Value)
                return 0f;

            if (Player.m_localPlayer)
            {
                if (Player.m_localPlayer?.GetCurrentBiome() == Heightmap.Biome.AshLands)
                    return 0f;

                if (ZoneSystemVariantController.IsBeyondWorldEdge(Player.m_localPlayer.transform.position))
                    return 0f;
            }

            int currentDay = GetCurrentDay();
            int daysInSeason = GetDaysInSeason();
            int firstDay = Mathf.Clamp((int)waterFreezesInWinterDays.Value.x, 0, daysInSeason + 1);
            int lastDay = Mathf.Clamp((int)waterFreezesInWinterDays.Value.y, 0, daysInSeason + 1);

            if (currentDay == 0 || GetCurrentSeason() != Season.Winter || lastDay == 0 || lastDay > daysInSeason)
                return 0f;

            return currentDay > lastDay ? Mathf.Clamp01((float)(daysInSeason - currentDay) / Math.Max(daysInSeason - lastDay, 1)) : Mathf.Clamp01((float)currentDay / Math.Max(firstDay, 1));
        }

        public double GetSecondsToMakeHoney(Beehive beehive, int amount = 1, float product = -1f)
        {
            if (!beehive.m_nview.IsValid())
                return 0f;

            if (product == -1f)
                product = beehive.m_nview.GetZDO().GetFloat(ZDOVars.s_product);

            double secondsLeft = beehive.m_secPerUnit * amount - product;
            if (IsProtectedPosition(beehive.transform.position))
                return secondsLeft;

            return GetSecondsLeftWithSeasonalMultiplier(secondsLeft, GetBeehiveProductionMultiplier);
        }

        public double GetSecondsToGrowPlant(Plant plant)
        {
            if (!plant.m_nview.IsValid())
                return 0d;

            double secondsLeft = plant.GetGrowTime() - plant.TimeSincePlanted();

            if (IsProtectedPosition(plant.transform.position))
                return secondsLeft;

            return GetSecondsLeftWithSeasonalMultiplier(secondsLeft, GetPlantsGrowthMultiplier);
        }

        public double GetSecondsToRespawnPickable(Pickable pickable)
        {
            if (!pickable.m_nview.IsValid())
                return 0d;

            double secondsLeft = pickable.m_respawnTimeMinutes * 60;

            if (IsProtectedPosition(pickable.transform.position) || secondsLeft <= 0d)
                return secondsLeft;

            if (!_seasons.Any(season => GetPlantsGrowthMultiplier(season) > 0f))
                return double.PositiveInfinity;

            double pickedTimeSeconds = TimeSpan.FromTicks(pickable.m_nview.GetZDO().GetLong(ZDOVars.s_pickedTime, 0L)).TotalSeconds;
            int worldDay = GetWorldDay(pickedTimeSeconds);
            Season season = GetSeason(worldDay);

            double startOfDay = pickedTimeSeconds - pickedTimeSeconds % GetDayLengthInSeconds();
            double seasonStart = startOfDay - (GetDayInSeason(worldDay) - (worldDay == GetCurrentWorldDay() && IsPendingSeasonChange() ? 0 : 1)) * GetDayLengthInSeconds();
            double seasonEnd = seasonStart + GetSecondsInSeason(season);

            float dayStartFraction = DayStartFraction();
            float dayStartFractionLength = dayStartFraction * (1f - ((0.25f - GetDayFractionForSeasonChange()) * dayStartFraction / 0.25f)) * GetDayLengthInSeconds();

            double secondsToSeasonEnd = seasonEnd + dayStartFractionLength - pickedTimeSeconds;
            double secondsToGrow = 0d;
            float growthMultiplier = GetPlantsGrowthMultiplier(season);
            do
            {
                double timeInSeasonLeft = growthMultiplier == 0 ? secondsToSeasonEnd : Math.Min(secondsLeft / growthMultiplier, secondsToSeasonEnd);

                secondsToGrow += timeInSeasonLeft;
                secondsLeft -= timeInSeasonLeft * growthMultiplier;

                season = GetNextSeason(season);
                growthMultiplier = GetPlantsGrowthMultiplier(season);

                secondsToSeasonEnd = GetDaysInSeason(season) * GetDayLengthInSeconds();

            } while (secondsLeft > 0);

            return secondsToGrow;
        }

        public double GetSecondsToBurnFire(Fireplace fireplace)
        {
            if (!fireplace.m_nview.IsValid())
                return 0d;

            double secondsLeft = fireplace.m_nview.GetZDO().GetFloat(ZDOVars.s_fuel) * fireplace.m_secPerFuel;
            if (secondsLeft == 0 || IsProtectedPosition(fireplace.transform.position))
                return secondsLeft;

            return GetSecondsLeftWithSeasonalMultiplier(secondsLeft, GetFireplaceDrainMultiplier);
        }

        public double GetSecondsToBurnFire(Smelter smelter)
        {
            if (!smelter.m_nview.IsValid() || smelter.m_fuelPerProduct == 0)
                return 0d;

            double secondsLeft = smelter.GetFuel() * smelter.m_secPerProduct / smelter.m_fuelPerProduct;
            if (secondsLeft == 0 || IsProtectedPosition(smelter.transform.position))
                return secondsLeft;

            return GetSecondsLeftWithSeasonalMultiplier(secondsLeft, GetFireplaceDrainMultiplier);
        }

        public double GetSecondsToBurnFire(CookingStation cookingStation)
        {
            if (!cookingStation.m_nview.IsValid())
                return 0d;

            double secondsLeft = cookingStation.GetFuel() * cookingStation.m_secPerFuel;
            if (secondsLeft == 0 || IsProtectedPosition(cookingStation.transform.position))
                return secondsLeft;

            return GetSecondsLeftWithSeasonalMultiplier(secondsLeft, GetFireplaceDrainMultiplier);
        }

        public double GetSecondsToFillSap(SapCollector sapCollector)
        {
            if (!sapCollector.m_nview.IsValid())
                return 0d;

            double secondsLeft = (sapCollector.m_maxLevel - sapCollector.GetLevel()) * sapCollector.m_secPerUnit - sapCollector.m_nview.GetZDO().GetFloat(ZDOVars.s_product);
            if (secondsLeft == 0 || IsProtectedPosition(sapCollector.transform.position))
                return secondsLeft;

            return GetSecondsLeftWithSeasonalMultiplier(secondsLeft, GetSapCollectingSpeedMultiplier);
        }

        private double GetSecondsLeftWithSeasonalMultiplier(double secondsLeft, Func<Season, float> getMultiplier)
        {
            if (secondsLeft <= 0d)
                return 0d;
            if (!_seasons.Any(season => getMultiplier(season) > 0f))
                return double.PositiveInfinity;

            Season season = GetCurrentSeason();
            float multiplier = getMultiplier.Invoke(season);

            double seconds = 0d;
            double secondsToSeasonEnd = Math.Max(0d, GetTimeToCurrentSeasonEnd());

            do
            {
                double timeInSeasonLeft = multiplier == 0 ? secondsToSeasonEnd : Math.Min(secondsLeft / multiplier, secondsToSeasonEnd);

                seconds += timeInSeasonLeft;
                secondsLeft -= timeInSeasonLeft * multiplier;

                season = GetNextSeason(season);
                multiplier = getMultiplier.Invoke(season);

                secondsToSeasonEnd = GetDaysInSeason(season) * GetDayLengthInSeconds();

            } while (secondsLeft > 0);

            return seconds;
        }

        private bool CheckIfSeasonChanged(int currentSeason, Season setSeason, int dayInSeason, int worldDay)
        {
            if (currentSeason == (int)setSeason)
                return false;

            m_worldDay = worldDay;

            SetCurrentSeasonDay(setSeason, dayInSeason);

            return true;
        }

        private void CheckIfDayChanged(int dayInSeason, int worldDay, bool forceSeasonChange)
        {
            if (m_day == dayInSeason && m_worldDay == worldDay)
                return;

            m_worldDay = worldDay;

            if (dayInSeason > m_day || forceSeasonChange)
                SetCurrentDay(dayInSeason, forceSeasonChange);
        }

        public void StartSeasonChange()
        {
            if (!showFadeOnSeasonChange.Value || Hud.instance == null || Hud.instance.m_loadingScreen.isActiveAndEnabled || Hud.instance.m_loadingScreen.alpha > 0)
                OnSeasonChange();
            else
                Seasons.instance.StartCoroutine(seasonState.SeasonChangedFadeEffect());
        }

        public IEnumerator SeasonChangedFadeEffect()
        {
            m_seasonIsChanging = true;
            Player player = Player.m_localPlayer;
            Hud hud = Hud.instance;
            Game game = Game.instance;
            EnvMan environment = EnvMan.instance;

            bool SameWorld() => game != null && Game.instance == game && environment != null && EnvMan.instance == environment
                && ReferenceEquals(seasonState, this) && !game.IsShuttingDown();
            bool CanFade() => SameWorld() && hud != null && Hud.instance == hud && player != null
                && Player.m_localPlayer == player && !player.IsDead() && !player.IsTeleporting() && !player.IsSleeping();

            try
            {
                if (!CanFade())
                {
                    if (SameWorld())
                        OnSeasonChange();
                    yield break;
                }

                float fadeDuration = Mathf.Max(0.01f, fadeOnSeasonChangeDuration.Value / 2f);
                hud.m_loadingScreen.gameObject.SetActive(value: true);
                hud.m_loadingProgress.SetActive(value: false);
                hud.m_sleepingProgress.SetActive(value: false);
                hud.m_teleportingProgress.SetActive(value: false);

                while (CanFade() && hud.m_loadingScreen.alpha <= 0.99f)
                {
                    hud.m_loadingScreen.alpha = Mathf.MoveTowards(hud.m_loadingScreen.alpha, 1f, Time.fixedDeltaTime / fadeDuration);
                    yield return waitForFixedUpdate;
                }

                if (SameWorld())
                    OnSeasonChange();

                while (CanFade() && hud.m_loadingScreen.alpha > 0f)
                {
                    hud.m_loadingScreen.alpha = Mathf.MoveTowards(hud.m_loadingScreen.alpha, 0f, Time.fixedDeltaTime / fadeDuration);
                    yield return waitForFixedUpdate;
                }

                if (CanFade())
                    hud.m_loadingScreen.gameObject.SetActive(value: false);
            }
            finally
            {
                m_seasonIsChanging = false;
            }
        }

        private void OnSeasonChange()
        {
            UpdateBiomesSetup();
            UpdateGlobalKeys();
            UpdateWinterBloomEffect();
            ZoneSystemVariantController.UpdateWaterState();
            UpdateCurrentEnvironment();
            SeasonalSnow.UpdateSeasonState();
            SeasonalSnow.UpdateLoadedSnowCover();

            if (UseTextureControllers())
            {
                ClutterVariantController.UpdateShieldActiveState();
                ClutterVariantController.Instance?.UpdateColors();

                PrefabVariantController.UpdatePrefabColors();
                ZoneSystemVariantController.UpdateTerrainColors();

                UpdateTorchesFireWarmth();

                if (MinimapVariantController.instance != null)
                {
                    MinimapVariantController.instance.UpdateColors();
                    UpdateMinimapBorder();
                }

                if (Player.m_localPlayer != null)
                {
                    Player.m_localPlayer.UpdateCurrentSeason();
                    CheckOverheatStatus(Player.m_localPlayer);
                }
            }
        }

        public void UpdateWinterBloomEffect()
        {
            if (CameraEffects.instance != null && GraphicsSettingsManager.Instance != null)
                CameraEffects.instance.SetBloom(GraphicsSettingsManager.Instance.ActiveSettings.m_bloom);
        }

        public int GetYearLengthInDays()
        {
            int days = 0;
            foreach (Season season in _seasons)
                days += GetDaysInSeason(season);
            return days;
        }

        public override string ToString()
        {
            return $"{m_season} day:{m_day}";
        }

        public void PatchTorchItemData(ItemDrop.ItemData torch)
        {
            if (torch == null)
                return;

            if (torch.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Torch)
                return;

            if (seasonState.GetTorchAsFiresource() && IsActive && (EnvMan.IsWet() || IsCold()))
                torch.m_shared.m_durabilityDrain = seasonState.GetTorchDurabilityDrain();
            else
                torch.m_shared.m_durabilityDrain = 0.0333f;
        }

        public void UpdateTorchesFireWarmth()
        {
            UpdateTorchFireWarmth("GoblinTorch");
            UpdateTorchFireWarmth(SeasonSettings.itemNameTorch);

            if (Player.m_localPlayer != null)
                PatchTorchesInInventory(Player.m_localPlayer.GetInventory());
        }

        public void PatchTorchesInInventory(Inventory inventory)
        {
            _itemDataList.Clear();
            inventory.GetAllItems(SeasonSettings.itemDropNameTorch, _itemDataList);

            foreach (ItemDrop.ItemData item in _itemDataList)
                PatchTorchItemData(item);
        }

        public void UpdateTorchFireWarmth(string prefabName)
        {
            GameObject prefab = ObjectDB.instance?.GetItemPrefab(prefabName);
            if (prefab == null)
                return;

            EffectArea component = prefab.GetComponentInChildren<EffectArea>(includeInactive: true);
            if (component == null)
                return;

            bool heatEnabled =
                seasonState.GetTorchAsFiresource() &&
                (
                    !Player.m_localPlayer ||
                    (
                        TorchHeatInBiome(Player.m_localPlayer.GetCurrentBiome()) &&
                        (
                            !disableTorchWarmthInInterior.Value ||
                            !Player.m_localPlayer.InInterior()
                        )
                    )
                );

            component.m_type = heatEnabled ? EffectArea.Type.Heat | EffectArea.Type.Fire : EffectArea.Type.Fire;
            component.m_isHeatType = component.m_type.HasFlag(EffectArea.Type.Heat);

            ItemDrop item = prefab.GetComponent<ItemDrop>();
            PatchTorchItemData(item?.m_itemData);

            if (Player.m_localPlayer != null && Player.m_localPlayer.m_visEquipment != null)
            {
                if (Player.m_localPlayer.m_visEquipment.m_rightItem == prefabName.GetStableHashCode() && (Player.m_localPlayer.m_visEquipment.m_rightItemInstance?.GetComponentInChildren<EffectArea>(includeInactive: true) is EffectArea rightEffect))
                {
                    rightEffect.m_type = component.m_type;
                    rightEffect.m_isHeatType = component.m_isHeatType;
                }

                if (Player.m_localPlayer.m_visEquipment.m_leftItem == prefabName.GetStableHashCode() && (Player.m_localPlayer.m_visEquipment.m_leftItemInstance?.GetComponentInChildren<EffectArea>(includeInactive: true) is EffectArea leftEffect))
                {
                    leftEffect.m_type = component.m_type;
                    leftEffect.m_isHeatType = component.m_isHeatType;
                }
            }
        }

        public void UpdateMinimapBorder()
        {
            if (!seasonalMinimapBorderColor.Value || Minimap.instance == null)
                return;

            if (!Minimap.instance.m_smallRoot.TryGetComponent(out UnityEngine.UI.Image image) || image.sprite == null || image.sprite.name != "InputFieldBackground")
                return;

            if (minimapBorderColor == Color.clear)
                minimapBorderColor = image.color;

            switch (GetCurrentSeason())
            {
                case Season.Spring:
                    image.color = new Color(0.44f, 0.56f, 0.03f, minimapBorderColor.a / 2f);
                    break;
                case Season.Summer:
                    image.color = new Color(0.82f, 0.72f, 0.04f, minimapBorderColor.a / 2f);
                    break;
                case Season.Fall:
                    image.color = new Color(0.79f, 0.32f, 0f, minimapBorderColor.a / 2f);
                    break;
                case Season.Winter:
                    image.color = new Color(0.89f, 0.94f, 0.96f, minimapBorderColor.a / 2f);
                    break;
            }
        }

        public void CheckOverheatStatus(Player player)
        {
            if (player == null || player.m_isLoading || player.m_nview == null || !player.m_nview.IsValid())
                return;

            bool haveOverheat = player.GetSEMan().HaveStatusEffect(SeasonsVars.s_statusEffectOverheatHash);
            bool useLegacyWarm = !summerHeatEnabled.Value
                && summerHeatAddsExtraWarmCloth.Value
                && player == Player.m_localPlayer
                && seasonState.GetCurrentSeason() == Season.Summer;

            bool getOverheat = useLegacyWarm
                && seasonState.GetOverheatIn2WarmClothes()
                && !IsCold()
                && !HasCoolingFood(player);

            if (!getOverheat)
            {
                if (haveOverheat)
                    player.GetSEMan().RemoveStatusEffect(SeasonsVars.s_statusEffectOverheatHash);

                return;
            }

            int warmClothCount = GetWarmClothesCount(player);
            if (!haveOverheat && warmClothCount > 1)
                player.GetSEMan().AddStatusEffect(SeasonsVars.s_statusEffectOverheatHash);
            else if (haveOverheat && warmClothCount <= 1)
                player.GetSEMan().RemoveStatusEffect(SeasonsVars.s_statusEffectOverheatHash);
        }

        public static int GetWarmClothesCount(Player player)
        {
            if (player == null || player.GetInventory() is not Inventory inventory)
                return 0;

            return inventory.GetEquippedItems().Count(itemData => itemData.m_shared.m_damageModifiers.Any(IsFrostResistant));
        }

        public static bool IsFrostResistant(HitData.DamageModPair damageMod)
        {
            return damageMod.m_type == HitData.DamageType.Frost &&
                   (damageMod.m_modifier == HitData.DamageModifier.SlightlyResistant || damageMod.m_modifier == HitData.DamageModifier.Resistant || damageMod.m_modifier == HitData.DamageModifier.VeryResistant || damageMod.m_modifier == HitData.DamageModifier.Immune);
        }

        public static bool HasCoolingFood(Player player)
        {
            return player != null && player.GetFoods().Any(food => IsCoolingFood(food.m_item));
        }

        public static bool IsCoolingFood(ItemDrop.ItemData item)
        {
            if (item == null)
                return false;

            return GetCoolingFoodNames().Contains(item.m_shared?.m_name);
        }

        private static HashSet<string> GetCoolingFoodNames()
        {
            string configuredFoods = summerHeatCoolingFoods?.Value ?? string.Empty;
            if (_coolingFoodNamesValue == configuredFoods)
                return _coolingFoodNames;

            _coolingFoodNamesValue = configuredFoods;
            _coolingFoodNames.Clear();

            foreach (string value in configuredFoods.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string normalized = value.GetItemName();
                if (!string.IsNullOrEmpty(normalized))
                    _coolingFoodNames.Add(normalized);
            }

            return _coolingFoodNames;
        }

        public static bool IsCold() => EnvMan.IsFreezing() || EnvMan.IsCold();

        private void SetCurrentSeasonDay(Season season, int day)
        {
            UpdateCurrentSeasonDay((int)season * 10000 + day);
        }

        private void SetCurrentDay(int day, bool forceSeasonChange)
        {
            SetCurrentSeasonDay(_pendingSeasonChange == 0 || forceSeasonChange ? m_season : GetPendingSeasonDay().Item1, day);
        }

        private void UpdateCurrentSeasonDay(int newValue)
        {
            if (cacheRevision.Value == 0)
            {
                LogInfo("Season update pending prevented: cache revision 0");
                return;
            }

            if (_pendingSeasonChange == newValue)
                return;

            _pendingSeasonChange = newValue;

            currentSeasonDay.AssignValueSafeAndNotify(() =>
            {
                Season pendingSeason = (Season)((newValue / 10000) % seasonsCount);
                int pendingDay = newValue % 10000;

                LogInfo(
                    $"Season update pending: {m_season} -> {pendingSeason}{(overrideSeason.Value ? "(override)" : "")}, " +
                    $"Day: {m_day} -> {pendingDay}{(overrideSeasonDay.Value ? "(override)" : "")}, " +
                    $"World Day: {m_worldDay}");

                return newValue;
            });
        }

        internal static float GetDayFractionForSeasonChange()
        {
            return changeSeasonOnlyAfterSleep.Value ? 0.2498f : 0.24f;
        }

        public static void CheckSeasonChange()
        {
            if (IsActive)
                seasonState.UpdateState(forceSeasonChange: true);
        }

        public static void ResetCurrentSeasonDay()
        {
            _pendingSeasonChange = 0;
            cacheRevision.AssignValueSafe(0u);
            currentSeasonDay.AssignValueSafe(0);
        }

        public static void OnSeasonDayChange()
        {
            if (!IsActive)
                return;

            _pendingSeasonChange = 0;
            Tuple<Season, int> seasonDay = GetSyncedCurrentSeasonDay();

            bool dayChanged = seasonState.m_day != seasonDay.Item2;
            bool seasonChanged = seasonState.m_season != seasonDay.Item1;

            seasonState.m_season = seasonDay.Item1;
            seasonState.m_day = seasonDay.Item2;

            if (seasonChanged || dayChanged)
                LogInfo($"Season: {seasonState.m_season}, day: {seasonState.m_day}");

            if (seasonChanged)
                seasonState.StartSeasonChange();
            else if (dayChanged)
                OnDayChange();
        }

        private static void OnDayChange()
        {
            StartClutterUpdate();
            ZoneSystemVariantController.UpdateWaterState();
            seasonState.UpdateGlobalKeys();
            seasonState.UpdateWinterBloomEffect();
            UpdateCurrentEnvironment();
            SeasonalSnow.UpdateLoadedSnowCover();
        }

        internal static bool TorchHeatInBiome(Heightmap.Biome biome) => biome != Heightmap.Biome.Mountain && biome != Heightmap.Biome.DeepNorth && biome != Heightmap.Biome.AshLands;

        private static void StartClutterUpdate()
        {
            if (UseTextureControllers())
                ClutterVariantController.Instance?.StartCoroutine(ClutterVariantController.Instance.UpdateDayState());
        }

        public static Tuple<Season, int> GetSyncedCurrentSeasonDay()
        {
            return Tuple.Create((Season)((currentSeasonDay.Value / 10000) % 4), currentSeasonDay.Value % 10000);
        }

        public static Tuple<Season, int> GetPendingSeasonDay()
        {
            return Tuple.Create((Season)((_pendingSeasonChange / 10000) % 4), _pendingSeasonChange % 10000);
        }

        internal static List<EnvEntry> ApplySeasonBiomeEnvironmentRules(Heightmap.Biome biome, List<EnvEntry> environments)
        {
            if (!IsActive || !controlEnvironments.Value || environments == null)
                return environments;

            SeasonBiomeEnvironments.SeasonBiomeEnvironment biomeEnv = seasonBiomeEnvironments.GetSeasonBiomeEnvironment(seasonState.GetCurrentSeason());
            return ApplySeasonBiomeEnvironmentRules(biomeEnv, biome, environments);
        }

        private static List<EnvEntry> ApplySeasonBiomeEnvironmentRules(SeasonBiomeEnvironments.SeasonBiomeEnvironment biomeEnv, BiomeEnvSetup biomeEnvironment, List<EnvEntry> environments)
        {
            return ApplySeasonBiomeEnvironmentRules(
                biomeEnv,
                environments,
                configuredName => BiomeNameMatches(configuredName, biomeEnvironment),
                preserveSourceEntries: false);
        }

        private static List<EnvEntry> ApplySeasonBiomeEnvironmentRules(SeasonBiomeEnvironments.SeasonBiomeEnvironment biomeEnv, Heightmap.Biome biome, List<EnvEntry> environments)
        {
            return ApplySeasonBiomeEnvironmentRules(
                biomeEnv,
                environments,
                configuredName => BiomeNameMatches(configuredName, biome),
                preserveSourceEntries: true);
        }

        private static List<EnvEntry> ApplySeasonBiomeEnvironmentRules(
            SeasonBiomeEnvironments.SeasonBiomeEnvironment biomeEnv,
            List<EnvEntry> environments,
            Func<string, bool> biomeMatches,
            bool preserveSourceEntries)
        {
            List<EnvEntry> result = environments == null
                ? new List<EnvEntry>()
                : environments
                    .Where(environment => environment != null)
                    .Select(CloneEnvEntry)
                    .ToList();

            RefreshEnvironmentReferences(result);

            if (biomeEnv == null)
                return result;

            foreach (SeasonBiomeEnvironments.SeasonBiomeEnvironment.EnvironmentAdd add in biomeEnv.add)
            {
                if (add == null || add.m_environment == null || !biomeMatches(add.m_name))
                    continue;

                EnvEntry addedEnvironment = CloneEnvEntry(add.m_environment);
                if (TryResolveSeasonEnvironmentReference(addedEnvironment, $"add rule for biome {add.m_name}"))
                    result.Add(addedEnvironment);
            }

            foreach (SeasonBiomeEnvironments.SeasonBiomeEnvironment.EnvironmentReplace replace in biomeEnv.replace)
            {
                if (replace == null || string.IsNullOrWhiteSpace(replace.m_environment) || string.IsNullOrWhiteSpace(replace.replace_to))
                    continue;

                for (int i = 0; i < result.Count; i++)
                {
                    EnvEntry sourceEnvironment = result[i];
                    if (!String.Equals(sourceEnvironment.m_environment, replace.m_environment, StringComparison.Ordinal))
                        continue;

                    EnvEntry replacementEnvironment = CloneEnvEntry(sourceEnvironment);
                    replacementEnvironment.m_environment = replace.replace_to;

                    if (TryResolveSeasonEnvironmentReference(replacementEnvironment, $"replace rule {replace.m_environment} -> {replace.replace_to}"))
                        result[i] = replacementEnvironment;
                }
            }

            foreach (SeasonBiomeEnvironments.SeasonBiomeEnvironment.EnvironmentRemove remove in biomeEnv.remove)
            {
                if (remove == null || string.IsNullOrWhiteSpace(remove.m_environment) || !biomeMatches(remove.m_name))
                    continue;

                result.RemoveAll(environment => String.Equals(environment.m_environment, remove.m_environment, StringComparison.Ordinal));
            }

            return result;
        }

        private static readonly FieldInfo[] envEntryFields = typeof(EnvEntry).GetFields(BindingFlags.Instance | BindingFlags.Public);
        private static readonly HashSet<string> unresolvedSeasonEnvironmentRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static EnvEntry CloneEnvEntry(EnvEntry source)
        {
            EnvEntry clone = new EnvEntry();

            if (source == null)
                return clone;

            foreach (FieldInfo field in envEntryFields)
            {
                if (field.Name == nameof(EnvEntry.m_env))
                    continue;

                field.SetValue(clone, field.GetValue(source));
            }

            clone.m_env = null;
            return clone;
        }

        private static void RefreshEnvironmentReferences(IEnumerable<EnvEntry> environments)
        {
            if (EnvMan.instance == null || environments == null)
                return;

            foreach (EnvEntry environment in environments)
            {
                if (environment == null || String.IsNullOrWhiteSpace(environment.m_environment))
                    continue;

                environment.m_env = EnvMan.instance.GetEnv(environment.m_environment);
            }
        }

        private static bool TryResolveSeasonEnvironmentReference(EnvEntry environment, string ruleDescription)
        {
            if (environment == null || String.IsNullOrWhiteSpace(environment.m_environment) || EnvMan.instance == null)
                return false;

            environment.m_env = EnvMan.instance.GetEnv(environment.m_environment);
            if (environment.m_env != null)
                return true;

            string warningKey = $"{ruleDescription}|{environment.m_environment}";
            if (unresolvedSeasonEnvironmentRules.Add(warningKey))
                LogWarning($"Seasonal biome environment {ruleDescription} references missing environment {environment.m_environment}; the rule was skipped.");

            return false;
        }

        private static bool BiomeNameMatches(string configuredName, BiomeEnvSetup biomeEnvironment)
        {
            if (string.IsNullOrWhiteSpace(configuredName) || biomeEnvironment == null)
                return false;

            if (!string.IsNullOrWhiteSpace(biomeEnvironment.m_name) &&
                string.Equals(
                    NormalizeBiomeName(configuredName),
                    NormalizeBiomeName(biomeEnvironment.m_name),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return BiomeNameMatches(configuredName, biomeEnvironment.m_biome);
        }

        private static bool BiomeNameMatches(string configuredName, Heightmap.Biome biome)
        {
            if (string.IsNullOrWhiteSpace(configuredName))
                return false;

            string normalizedConfiguredName = NormalizeBiomeName(configuredName);
            if (string.Equals(
                normalizedConfiguredName,
                NormalizeBiomeName(biome.ToString()),
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return Compatibility.EWDCompat.TryGetBiomeDisplayName(biome, out string displayName) &&
                string.Equals(
                    normalizedConfiguredName,
                    NormalizeBiomeName(displayName),
                    StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeBiomeName(string biomeName)
        {
            if (String.IsNullOrWhiteSpace(biomeName))
                return String.Empty;

            return biomeName
                .Replace(" ", "")
                .Replace("_", "")
                .Replace("-", "");
        }

        private static List<SeasonEnvironment> SortCustomEnvironmentsByCloneDependencies(List<SeasonEnvironment> environments)
        {
            if (environments == null || environments.Count == 0)
                return new List<SeasonEnvironment>();

            Dictionary<string, int> lastIndexByName = new Dictionary<string, int>();
            HashSet<string> duplicateNames = new HashSet<string>();

            for (int i = 0; i < environments.Count; i++)
            {
                SeasonEnvironment environment = environments[i];

                if (environment == null || String.IsNullOrWhiteSpace(environment.m_name))
                {
                    LogWarning("Custom environment with empty m_name was skipped.");
                    continue;
                }

                if (lastIndexByName.ContainsKey(environment.m_name))
                    duplicateNames.Add(environment.m_name);

                lastIndexByName[environment.m_name] = i;
            }

            foreach (string duplicateName in duplicateNames)
                LogWarning($"Duplicate custom environment name \"{duplicateName}\". The last definition will be used.");

            List<SeasonEnvironment> uniqueEnvironments = new List<SeasonEnvironment>();
            Dictionary<string, SeasonEnvironment> customByName = new Dictionary<string, SeasonEnvironment>();

            for (int i = 0; i < environments.Count; i++)
            {
                SeasonEnvironment environment = environments[i];

                if (environment == null || String.IsNullOrWhiteSpace(environment.m_name))
                    continue;

                if (lastIndexByName[environment.m_name] != i)
                    continue;

                uniqueEnvironments.Add(environment);
                customByName[environment.m_name] = environment;
            }

            HashSet<string> existingEnvironmentNames = EnvMan.instance.m_environments
                .Where(environment => environment != null && !String.IsNullOrWhiteSpace(environment.m_name))
                .Select(environment => environment.m_name)
                .ToHashSet();

            List<SeasonEnvironment> sorted = new List<SeasonEnvironment>();
            HashSet<string> resolvedCustomNames = new HashSet<string>();
            HashSet<string> addedNames = new HashSet<string>();

            bool progress;

            do
            {
                progress = false;

                foreach (SeasonEnvironment environment in uniqueEnvironments)
                {
                    if (addedNames.Contains(environment.m_name))
                        continue;

                    string cloneFrom = environment.m_cloneFrom;

                    bool cloneSourceResolved =
                        String.IsNullOrWhiteSpace(cloneFrom) ||
                        existingEnvironmentNames.Contains(cloneFrom) ||
                        resolvedCustomNames.Contains(cloneFrom);

                    if (!cloneSourceResolved)
                        continue;

                    sorted.Add(environment);
                    addedNames.Add(environment.m_name);
                    resolvedCustomNames.Add(environment.m_name);
                    progress = true;
                }
            }
            while (progress);

            foreach (SeasonEnvironment environment in uniqueEnvironments)
            {
                if (addedNames.Contains(environment.m_name))
                    continue;

                if (customByName.ContainsKey(environment.m_cloneFrom))
                {
                    LogWarning(
                        $"Custom environment \"{environment.m_name}\" has unresolved clone dependency \"{environment.m_cloneFrom}\". " +
                        "This is probably a circular clone dependency.");
                }
                else
                {
                    LogWarning(
                        $"Custom environment \"{environment.m_name}\" clone source \"{environment.m_cloneFrom}\" was not found.");
                }

                sorted.Add(environment);
                addedNames.Add(environment.m_name);
            }

            return sorted;
        }
    }
}
