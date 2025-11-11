using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        private static readonly Dictionary<Heightmap.Biome, string> biomesDefault = new Dictionary<Heightmap.Biome, string>();
        private static readonly List<ItemDrop.ItemData> _itemDataList = new List<ItemDrop.ItemData>();
        private static int _pendingSeasonChange = 0;

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

            foreach (Season season in Enum.GetValues(typeof(Season)))
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
                LogInfo($"Current: {m_season, -6} {m_day} {m_worldDay} New: {newSeason, -6} {m_dayInSeasonGlobal} {worldDay} Time: {EnvMan.instance.GetDayFraction(),-6:F4} TotalSeconds: {GetTotalSeconds(), -10:F2} TimeToChange:{timeForSeasonToChange, -5} SleepCheck:{sleepCheck,-5} Force:{forceSeasonChange, -5} ToPast:{timeForSeasonToChange && !forceSeasonChange && !sleepCheck && m_isUsingIngameDays && changeSeasonOnlyAfterSleep.Value && GetCurrentDay() == GetDaysInSeason() && m_dayInSeasonGlobal != GetCurrentDay(), -5}");

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
            if (previousBiome == Heightmap.Biome.None || previousBiome == currentBiome)
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

        private World GetCurrentWorld()
        {
            return ZNet.m_world ?? (WorldGenerator.instance?.m_world);
        }

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
                int daysInPreviousSeason = GetDaysInSeason(season);

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
            ZoneSystem.instance.gameObject.AddComponent<PrefabVariantController>();
            PrefabVariantController.AddControllerToPrefabs();
            ClutterVariantController.Initialize();
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
            SeasonBiomeEnvironments.SeasonBiomeEnvironment biomeEnv = seasonBiomeEnvironments.GetSeasonBiomeEnvironment(seasonState.GetCurrentSeason());

            RefreshBiomesDefault(forceUpdate:false);

            EnvMan.instance.m_biomes.Clear();

            biomesDefault.Do(kvp => ChangeBiomeEnvironment(kvp.Value));

            UpdateCurrentEnvironment();

            void ChangeBiomeEnvironment(string biomeEnvironmentDefault)
            {
                try
                {
                    BiomeEnvSetup biomeEnvironment = JsonUtility.FromJson<BiomeEnvSetup>(biomeEnvironmentDefault);

                    foreach (SeasonBiomeEnvironments.SeasonBiomeEnvironment.EnvironmentReplace replace in biomeEnv.replace)
                        biomeEnvironment.m_environments.DoIf(env => env.m_environment == replace.m_environment, env => env.m_environment = replace.replace_to);

                    foreach (SeasonBiomeEnvironments.SeasonBiomeEnvironment.EnvironmentAdd add in biomeEnv.add)
                        if (add.m_name == biomeEnvironment.m_name && !biomeEnvironment.m_environments.Any(env => env.m_environment == add.m_environment.m_environment))
                            biomeEnvironment.m_environments.Add(add.m_environment);

                    foreach (SeasonBiomeEnvironments.SeasonBiomeEnvironment.EnvironmentRemove remove in biomeEnv.remove)
                        biomeEnvironment.m_environments.RemoveAll(env => biomeEnvironment.m_name == remove.m_name && env.m_environment == remove.m_environment);

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

            seasonState.UpdateTorchesFireWarmth();

            LoadingTips.UpdateLoadingTips();

            EnvManPatches.settingsUpdated = true;
        }

        public static void UpdateSeasonEnvironments()
        {
            if (!IsActive)
                return;

            if (!controlEnvironments.Value)
                return;

            SeasonEnvironment.ClearCachedObjects();

            foreach (SeasonEnvironment senv in seasonEnvironments)
            {
                EnvSetup env2 = EnvMan.instance.GetEnv(senv.m_name);
                if (env2 != null)
                    EnvMan.instance.m_environments.Remove(env2);
            }

            CustomMusic.CheckMusicList();

            if (!String.IsNullOrEmpty(customEnvironmentsJSON.Value))
            {
                try
                {
                    seasonEnvironments = JsonConvert.DeserializeObject<List<SeasonEnvironment>>(customEnvironmentsJSON.Value);
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
                LogInfo($"Custom environments loaded defaults");
            }

            foreach (SeasonEnvironment senv in seasonEnvironments)
            {
                EnvSetup env2 = EnvMan.instance.GetEnv(senv.m_name);
                if (env2 != null)
                    EnvMan.instance.m_environments.Remove(env2);

                EnvMan.instance.AppendEnvironment(senv.ToEnvSetup());
            }

            UpdateCurrentEnvironment();
        }

        private static void UpdateCurrentEnvironment()
        {
            EnvMan.instance.m_environmentPeriod = -1L;
        }

        public static void UpdateBiomeEnvironments()
        {
            if (!IsActive)
                return;

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
            if (!IsActive)
                return;

            foreach (Season season in Enum.GetValues(typeof(Season)))
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
            int day = GetCurrentWorldDay();
            return DayStartFraction(GetSeason(day), GetDayInSeason(day));
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
            foreach (Season season in Enum.GetValues(typeof(Season)))
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
            foreach (Season season in Enum.GetValues(typeof(Season)))
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

            double pickedTimeSeconds = TimeSpan.FromTicks(pickable.m_nview.GetZDO().GetLong(ZDOVars.s_pickedTime, 0L)).TotalSeconds;
            int worldDay = GetWorldDay(pickedTimeSeconds);
            Season season = GetSeason(worldDay);

            double startOfDay = pickedTimeSeconds - pickedTimeSeconds % GetDayLengthInSeconds();
            double seasonStart = startOfDay - (GetDayInSeason(worldDay) - (worldDay == GetCurrentWorldDay() && IsPendingSeasonChange() ? 0 : 1)) * GetDayLengthInSeconds();
            double seasonEnd = seasonStart + GetSecondsInSeason(season);

            float dayStartFractionLength = DayStartFraction() * (1f - ((0.25f - GetDayFractionForSeasonChange()) * DayStartFraction() / 0.25f)) * GetDayLengthInSeconds();

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
            Season season = GetCurrentSeason();
            float multiplier = getMultiplier.Invoke(season);

            double seconds = 0d;
            double secondsToSeasonEnd = GetTimeToCurrentSeasonEnd();

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
            if (player == null || player.IsDead() || player.IsTeleporting() || Game.instance.IsShuttingDown() || player.IsSleeping())
            {
                OnSeasonChange();
                m_seasonIsChanging = false;
                yield break;
            }

            float fadeDuration = fadeOnSeasonChangeDuration.Value / 2;

            Hud.instance.m_loadingScreen.gameObject.SetActive(value: true);
            Hud.instance.m_loadingProgress.SetActive(value: false);
            Hud.instance.m_sleepingProgress.SetActive(value: false);
            Hud.instance.m_teleportingProgress.SetActive(value: false);

            while (Hud.instance.m_loadingScreen.alpha <= 0.99f)
            {
                if (player == null || player.IsDead() || player.IsTeleporting() || Game.instance.IsShuttingDown() || player.IsSleeping())
                {
                    OnSeasonChange();
                    m_seasonIsChanging = false;
                    yield break;
                }

                Hud.instance.m_loadingScreen.alpha = Mathf.MoveTowards(Hud.instance.m_loadingScreen.alpha, 1f, Time.fixedDeltaTime / fadeDuration);

                yield return waitForFixedUpdate;
            }

            OnSeasonChange();

            while (Hud.instance.m_loadingScreen.alpha > 0f)
            {
                if (player == null || player.IsDead() || player.IsTeleporting() || Game.instance.IsShuttingDown() || player.IsSleeping())
                {
                    m_seasonIsChanging = false;
                    yield break;
                }

                Hud.instance.m_loadingScreen.alpha = Mathf.MoveTowards(Hud.instance.m_loadingScreen.alpha, 0f, Time.fixedDeltaTime / fadeDuration);

                yield return waitForFixedUpdate;
            }

            Hud.instance.m_loadingScreen.gameObject.SetActive(value: false);
            m_seasonIsChanging = false;
        }

        private void OnSeasonChange()
        {
            UpdateBiomesSetup();
            UpdateGlobalKeys();
            UpdateWinterBloomEffect();
            ZoneSystemVariantController.UpdateWaterState();
            UpdateCurrentEnvironment();

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
            if (IsActive && UseTextureControllers())
                CameraEffects.instance.SetBloom((!disableBloomInWinter.Value || GetCurrentSeason() != Season.Winter) && PlatformPrefs.GetInt("Bloom", 1) == 1);
        }

        public int GetYearLengthInDays()
        {
            int days = 0;
            foreach (Season season in Enum.GetValues(typeof(Season)))
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
            GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabName);
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
            PatchTorchItemData(item.m_itemData);

            if (Player.m_localPlayer != null)
            {
                if (Player.m_localPlayer.m_visEquipment.m_rightItem == prefabName && (Player.m_localPlayer.m_visEquipment.m_rightItemInstance?.GetComponentInChildren<EffectArea>(includeInactive: true) is EffectArea rightEffect))
                {
                    rightEffect.m_type = component.m_type;
                    rightEffect.m_isHeatType = component.m_isHeatType;
                }

                if (Player.m_localPlayer.m_visEquipment.m_leftItem == prefabName && (Player.m_localPlayer.m_visEquipment.m_leftItemInstance?.GetComponentInChildren<EffectArea>(includeInactive: true) is EffectArea leftEffect))
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
                    image.color = new Color(0.69f, 0.73f, 0.05f, minimapBorderColor.a / 2f);
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

            bool getOverheat = seasonState.GetOverheatIn2WarmClothes() && !IsCold() && !player.GetFoods().Any(food => food.m_item.m_shared.m_name == "$item_eyescream");
            bool haveOverheat = player.GetSEMan().HaveStatusEffect(SeasonsVars.s_statusEffectOverheatHash);
            if (!getOverheat)
            {
                if (haveOverheat)
                    player.GetSEMan().RemoveStatusEffect(SeasonsVars.s_statusEffectOverheatHash);
            }
            else
            {
                int warmClothCount = GetWarmClothesCount(player);
                if (summerHeatAddsExtraWarmCloth.Value && player == Player.m_localPlayer && seasonState.GetCurrentSeason() == Season.Summer)
                    warmClothCount++;

                if (!haveOverheat && warmClothCount > 1)
                    player.GetSEMan().AddStatusEffect(SeasonsVars.s_statusEffectOverheatHash);
                else if (haveOverheat && warmClothCount <= 1)
                    player.GetSEMan().RemoveStatusEffect(SeasonsVars.s_statusEffectOverheatHash);
            }
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
            if (_pendingSeasonChange == newValue)
                return;

            _pendingSeasonChange = newValue;

            currentSeasonDay.AssignValueSafe(GetPendingSeasonDayChange);

            LogInfo($"Season update pending: {m_season} -> {GetPendingSeasonDay().Item1}{(overrideSeason.Value ? "(override)" : "")}, Day: {m_day} -> {GetPendingSeasonDay().Item2}{(overrideSeasonDay.Value ? "(override)" : "")}");
        }

        internal static float GetDayFractionForSeasonChange()
        {
            return changeSeasonOnlyAfterSleep.Value ? 0.2498f : 0.24f;
        }

        internal static int GetPendingSeasonDayChange()
        {
            return _pendingSeasonChange;
        }

        public static void CheckSeasonChange()
        {
            if (IsActive)
                seasonState.UpdateState(forceSeasonChange: true);
        }

        public static void ResetCurrentSeasonDay()
        {
            _pendingSeasonChange = 0;
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
    }
}
