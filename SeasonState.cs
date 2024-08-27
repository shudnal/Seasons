using System;
using static Seasons.Seasons;
using HarmonyLib;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using BepInEx;
using System.Reflection;

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

            biomesDefault.Clear();

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

        public int GetCurrentWorldDay()
        {
            return (int)(GetTotalSeconds() / GetDayLengthInSeconds());
        }

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

            if (!CheckIfSeasonChanged(currentSeason, setSeason, m_dayInSeasonGlobal, worldDay))
                CheckIfDayChanged(m_dayInSeasonGlobal, worldDay, forceSeasonChange);
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
            return Math.Max(5, m_isUsingIngameDays ? (EnvMan.instance == null ? 1800L : EnvMan.instance.m_dayLengthSec) : seasonWorldSettings.GetDayLengthSeconds(GetCurrentWorld()));
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
            return GetNightLength(GetSeason(GetCurrentWorldDay()));
        }

        public int GetNightLength(Season season)
        {
            return GetSeasonSettings(season).m_nightLength;
        }

        private void UpdateBiomesSetup()
        {
            SeasonBiomeEnvironment biomeEnv = seasonBiomeEnvironments.GetSeasonBiomeEnvironment(seasonState.GetCurrentSeason());

            if (biomesDefault.Count == 0)
                biomesDefault.AddRange(EnvMan.instance.m_biomes.Select(biome => JsonUtility.ToJson(biome)));

            EnvMan.instance.m_biomes.Clear();

            foreach (string biomeEnvironmentDefault in biomesDefault)
            {
                try
                {
                    BiomeEnvSetup biomeEnvironment = JsonUtility.FromJson<BiomeEnvSetup>(biomeEnvironmentDefault);

                    foreach (SeasonBiomeEnvironment.EnvironmentReplace replace in biomeEnv.replace)
                        biomeEnvironment.m_environments.DoIf(env => env.m_environment == replace.m_environment, env => env.m_environment = replace.replace_to);

                    foreach (SeasonBiomeEnvironment.EnvironmentAdd add in biomeEnv.add)
                        if (add.m_name == biomeEnvironment.m_name && !biomeEnvironment.m_environments.Any(env => env.m_environment == add.m_environment.m_environment))
                            biomeEnvironment.m_environments.Add(add.m_environment);

                    foreach (SeasonBiomeEnvironment.EnvironmentRemove remove in biomeEnv.remove)
                        biomeEnvironment.m_environments.DoIf(env => biomeEnvironment.m_name == remove.m_name && env.m_environment == remove.m_environment, env => biomeEnvironment.m_environments.Remove(env));

                    EnvMan.instance.AppendBiomeSetup(biomeEnvironment);
                }
                catch (Exception e)
                {
                    LogWarning($"Error appending biome setup:\n{biomeEnvironmentDefault}\n{e}");
                }
            }

            UpdateCurrentEnvironment();
        }

        public static void UpdateSeasonSettings()
        {
            if (!IsActive)
                return;

            seasonsSettings.Clear();
            foreach (KeyValuePair<int, string> item in seasonsSettingsJSON.Value)
            {
                try
                {
                    if (!String.IsNullOrEmpty(item.Value))
                    {
                        seasonsSettings[(Season)item.Key] = new SeasonSettings((Season)item.Key, JsonConvert.DeserializeObject<SeasonSettingsFile>(item.Value));
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

            CheckSeasonChange();
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
            if (!enableSeasonalGlobalKeys.Value)
                return;

            foreach (Season season in Enum.GetValues(typeof(Season)))
            {
                string globalKey = GetSeasonalGlobalKey(season);
                if (globalKey.IsNullOrWhiteSpace())
                    continue;

                if (season == GetCurrentSeason())
                    ZoneSystem.instance.SetGlobalKey(globalKey);
                else
                    ZoneSystem.instance.RemoveGlobalKey(globalKey);
            }
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
            return DayStartFraction(GetSeason(GetCurrentWorldDay()));
        }

        public float DayStartFraction(Season season)
        {
            return (seasonState.GetNightLength(season) / 2f) / 100f;
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
            return settings.m_fireplaceDrainMultiplier;
        }

        public float GetSapCollectingSpeedMultiplier()
        {
            return settings.m_sapCollectingSpeedMultiplier;
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

        private SeasonSettings GetSeasonSettings(Season season)
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

        private int GetDayInSeason(int day)
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

        private int GetDayOfYear(int day)
        {
            int yearLength = GetYearLengthInDays();
            int dayOfYear = day % yearLength;
            return dayOfYear == day ? dayOfYear : (dayOfYear == 0 ? yearLength : dayOfYear);
        }

        public float GetWaterSurfaceFreezeStatus()
        {
            if (!enableFrozenWater.Value)
                return 0f;

            if (Player.m_localPlayer?.GetCurrentBiome() == Heightmap.Biome.AshLands)
                return 0f;

            int currentDay = GetCurrentDay();
            int daysInSeason = GetDaysInSeason();
            int firstDay = Mathf.Clamp((int)waterFreezesInWinterDays.Value.x, 0, daysInSeason + 1);
            int lastDay = Mathf.Clamp((int)waterFreezesInWinterDays.Value.y, 0, daysInSeason + 1);

            if (currentDay == 0 || GetCurrentSeason() != Season.Winter || lastDay == 0 || lastDay > daysInSeason)
                return 0f;

            return currentDay > lastDay ? Mathf.Clamp01((float)(daysInSeason - currentDay) / Math.Max(daysInSeason - lastDay, 1)) : Mathf.Clamp01((float)currentDay / Math.Max(firstDay, 1));
        }

        private bool CheckIfSeasonChanged(int currentSeason, Season setSeason, int dayInSeason, int worldDay)
        {
            if (currentSeason == (int)setSeason)
                return false;

            m_worldDay = worldDay;

            SetCurrentSeasonDay(setSeason, dayInSeason);

            return true;
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
                PrefabVariantController.UpdatePrefabColors();
                ZoneSystemVariantController.UpdateTerrainColors();
                ClutterVariantController.Instance?.UpdateColors();

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

        private void CheckIfDayChanged(int dayInSeason, int worldDay, bool forceSeasonChange)
        {
            if (m_day == dayInSeason && m_worldDay == worldDay)
                return;

            m_worldDay = worldDay;

            if (dayInSeason > m_day || forceSeasonChange)
            {
                SetCurrentDay(dayInSeason);
            }
        }

        private int GetYearLengthInDays()
        {
            int days = 0;
            foreach (Season season in Enum.GetValues(typeof(Season)))
            {
                days += GetDaysInSeason(season);
            }
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

            if (seasonState.GetTorchAsFiresource() && IsActive && (EnvMan.IsWet() || EnvMan.IsCold() || EnvMan.IsFreezing()))
                torch.m_shared.m_durabilityDrain = seasonState.GetTorchDurabilityDrain();
            else
                torch.m_shared.m_durabilityDrain = 0.0333f;
        }

        public void UpdateTorchesFireWarmth()
        {
            GameObject prefabGoblinTorch = ObjectDB.instance.GetItemPrefab("GoblinTorch");
            if (prefabGoblinTorch != null)
                UpdateTorchFireWarmth(prefabGoblinTorch);

            GameObject prefabTorchMist = ObjectDB.instance.GetItemPrefab("TorchMist");
            if (prefabTorchMist != null)
                UpdateTorchFireWarmth(prefabTorchMist);

            GameObject prefabTorch = ObjectDB.instance.GetItemPrefab(SeasonSettings.itemNameTorch);
            if (prefabTorch != null)
                UpdateTorchFireWarmth(prefabTorch);

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

        public void UpdateTorchFireWarmth(GameObject prefab, string childName = "FireWarmth")
        {
            Transform fireWarmth = Utils.FindChild(prefab.transform, childName);
            if (fireWarmth == null)
                return;

            EffectArea component = fireWarmth.gameObject.GetComponent<EffectArea>();
            if (component == null)
                return;

            component.m_type = seasonState.GetTorchAsFiresource() ? EffectArea.Type.Heat | EffectArea.Type.Fire : EffectArea.Type.Fire;

            ItemDrop item = prefab.GetComponent<ItemDrop>();
            PatchTorchItemData(item.m_itemData);
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

        public void CheckOverheatStatus(Humanoid humanoid)
        {
            bool getOverheat = seasonState.GetOverheatIn2WarmClothes();
            int warmClothesCount = humanoid.GetInventory().GetEquippedItems().Count(itemData => itemData.m_shared.m_damageModifiers.Any(dmod => dmod.m_type == HitData.DamageType.Frost && dmod.m_modifier == HitData.DamageModifier.Resistant));
            bool haveOverheat = humanoid.GetSEMan().HaveStatusEffect(statusEffectOverheatHash);
            if (!getOverheat)
            {
                if (haveOverheat)
                    humanoid.GetSEMan().RemoveStatusEffect(statusEffectOverheatHash);
            }
            else
            {
                if (!haveOverheat && warmClothesCount > 1)
                    humanoid.GetSEMan().AddStatusEffect(statusEffectOverheatHash);
                else if (haveOverheat && warmClothesCount <= 1)
                    humanoid.GetSEMan().RemoveStatusEffect(statusEffectOverheatHash);
            }
        }

        private void SetCurrentSeasonDay(Season season, int day)
        {
            UpdateCurrentSeasonDay((int)season * 10000 + day);
        }

        private void SetCurrentDay(int day)
        {
            UpdateCurrentSeasonDay((int)(_pendingSeasonChange == 0 ? m_season : GetPendingSeasonDay().Item1) * 10000 + day);
        }

        private void UpdateCurrentSeasonDay(int newValue)
        {
            if (_pendingSeasonChange == newValue)
                return;

            _pendingSeasonChange = newValue;

            currentSeasonDay.AssignValueSafe(GetPendingSeasonDayChange);

            LogInfo($"Season update pending: {m_season} -> {GetPendingSeasonDay().Item1}{(overrideSeason.Value ? "(override)" : "")}, Day: {m_day} -> {GetPendingSeasonDay().Item2}");
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

        private static void StartClutterUpdate()
        {
            if (UseTextureControllers())
            {
                ClutterVariantController.Instance?.StartCoroutine(ClutterVariantController.Instance.UpdateDayState());
            }
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

    [HarmonyPatch]
    public static class EnvMan_DayLength
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(EnvMan), nameof(EnvMan.Awake));
            yield return AccessTools.Method(typeof(EnvMan), nameof(EnvMan.FixedUpdate));
            yield return AccessTools.Method(typeof(EnvMan), nameof(EnvMan.GetCurrentDay));
            yield return AccessTools.Method(typeof(EnvMan), nameof(EnvMan.GetDay));
            yield return AccessTools.Method(typeof(EnvMan), nameof(EnvMan.GetMorningStartSec));
            yield return AccessTools.Method(typeof(EnvMan), nameof(EnvMan.SkipToMorning));
        }

        [HarmonyPriority(Priority.First)]
        private static void Prefix(ref long ___m_dayLengthSec)
        {
            if (dayLengthSec.Value != 0L && ___m_dayLengthSec != dayLengthSec.Value)
                ___m_dayLengthSec = dayLengthSec.Value;
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.UpdateTriggers))]
    public static class EnvMan_UpdateTriggers_SeasonStateUpdate
    {
        private static void Postfix(float oldDayFraction, float newDayFraction)
        {
            float fraction = SeasonState.GetDayFractionForSeasonChange();

            bool timeForSeasonToChange = oldDayFraction > 0.16f && oldDayFraction <= fraction && newDayFraction >= fraction && newDayFraction < 0.3f;
            seasonState.UpdateState(timeForSeasonToChange);
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.RescaleDayFraction))]
    public static class EnvMan_RescaleDayFraction_DayNightLength
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(float fraction, ref float __result)
        {
            float dayStart = seasonState.DayStartFraction();
            if (dayStart == EnvMan.c_MorningL)
                return true;

            float nightStart = 1.0f - dayStart;

            if (dayStart <= fraction && fraction <= nightStart)
            {
                float num = (fraction - dayStart) / (nightStart - dayStart);
                fraction = 0.25f + num * 0.5f;
            }
            else if (fraction < 0.5f)
            {
                fraction = fraction / dayStart * 0.25f;
            }
            else
            {
                float num2 = (fraction - nightStart) / dayStart;
                fraction = 0.75f + num2 * 0.25f;
            }

            __result = fraction;
            return false;
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.GetMorningStartSec))]
    public static class EnvMan_GetMorningStartSec_DayNightLength
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(EnvMan __instance, int day, ref double __result)
        {
            __result = (day * __instance.m_dayLengthSec) + (double)(__instance.m_dayLengthSec * seasonState.DayStartFraction(seasonState.GetSeason(day)));
            return false;
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.SkipToMorning))]
    public static class EnvMan_SkipToMorning_DayNightLength
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(EnvMan __instance, ref bool ___m_skipTime, ref double ___m_skipToTime, ref double ___m_timeSkipSpeed)
        {
            float dayStart = seasonState.DayStartFraction();
            if (dayStart == EnvMan.c_MorningL)
                return true;

            double timeSeconds = ZNet.instance.GetTimeSeconds();
            double startOfMorning = timeSeconds - timeSeconds % __instance.m_dayLengthSec + __instance.m_dayLengthSec * dayStart;
            
            int day = __instance.GetDay(startOfMorning);
            double morningStartSec = __instance.GetMorningStartSec(day + 1);

            ___m_skipTime = true;
            ___m_skipToTime = morningStartSec;

            ___m_timeSkipSpeed = (morningStartSec - timeSeconds) / 12.0;

            LogInfo($"Time: {timeSeconds,-10:F2} Day: {day} Next morning: {morningStartSec,-10:F2} Skipspeed: {___m_timeSkipSpeed,-5:F2}");

            return false;
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UpdateEquipment))]
    public static class Humanoid_UpdateEquipment_ToggleTorchesWarmth
    {
        private static void Prefix(Humanoid __instance)
        {
            if (__instance == null || !__instance.IsPlayer())
                return;

            seasonState.PatchTorchItemData(__instance.m_rightItem);
            seasonState.PatchTorchItemData(__instance.m_leftItem);
        }
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
    public static class ObjectDB_Awake_TorchPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            seasonState.UpdateTorchesFireWarmth();
        }
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    public static class ObjectDB_CopyOtherDB_TorchPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            seasonState.UpdateTorchesFireWarmth();
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.AddKnownItem))]
    public static class Player_AddKnownItem_TorchPatch
    {
        private static void Postfix(ref ItemDrop.ItemData item)
        {
            if (item.m_shared.m_name != SeasonSettings.itemDropNameTorch)
                return;

            seasonState.PatchTorchItemData(item);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    public class Player_OnSpawned_TorchPatch
    {
        public static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer)
                return;

            seasonState.PatchTorchesInInventory(__instance.GetInventory());
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.Load))]
    public class Inventory_Load_TorchPatch
    {
        public static void Postfix(Inventory __instance)
        {
            seasonState.PatchTorchesInInventory(__instance);
        }
    }

    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.Start))]
    public static class ItemDrop_Start_TorchPatch
    {
        private static void Postfix(ref ItemDrop __instance)
        {
            if (__instance.GetPrefabName(__instance.name) != SeasonSettings.itemNameTorch)
                return;

            seasonState.PatchTorchItemData(__instance.m_itemData);
        }
    }

    [HarmonyPatch(typeof(SeasonalItemGroup), nameof(SeasonalItemGroup.IsInSeason))]
    public static class SeasonalItemGroup_IsInSeason_SeasonalItems
    {
        private static void Postfix(SeasonalItemGroup __instance, ref bool __result)
        {
            if (!enableSeasonalItems.Value)
                return;

            Season season = seasonState.GetCurrentSeason();
            __result = __instance.name == "Halloween" && season == Season.Fall
                    || __instance.name == "Midsummer" && season == Season.Summer
                    || __instance.name == "Yule" && season == Season.Winter;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    public static class Character_ApplyDamage_PreventDeathFromFreezing
    {
        private static bool Prefix(Character __instance, ref HitData hit)
        {
            if (!preventDeathFromFreezing.Value)
                return true;

            if (!__instance.IsPlayer())
                return true;

            if (__instance != Player.m_localPlayer)
                return true;

            if (hit.m_hitType != HitData.HitType.Freezing)
                return true;

            Heightmap.Biome biome = (__instance as Player).GetCurrentBiome();
            if (biome == Heightmap.Biome.Mountain || biome == Heightmap.Biome.DeepNorth)
                return true;
            
            return __instance.GetHealth() >= 5;
        }
    }

    [HarmonyPatch(typeof(Pickable), nameof(Pickable.Awake))]
    public static class Pickable_Awake_PlantsGrowthMultiplier
    {
        public static bool ShouldBePicked(Pickable pickable)
        {
            return !pickable.GetPicked() &&
                    seasonState.GetPlantsGrowthMultiplier() == 0f &&
                    seasonState.GetCurrentSeason() == Season.Winter &&
                    seasonState.GetCurrentDay() >= cropsDiesAfterSetDayInWinter.Value
                    && !PlantWillSurviveWinter(pickable.gameObject)
                    && !IsProtectedPosition(pickable.transform.position);
        }

        public static bool IsIgnored(Pickable pickable)
        {
            return pickable.m_nview == null || 
                  !pickable.m_nview.IsValid() || 
                  !pickable.m_nview.IsOwner() || 
                  !ControlPlantGrowth(pickable.gameObject) || 
                  IsIgnoredPosition(pickable.transform.position);
        }

        private static void Postfix(Pickable __instance)
        {
            if (IsIgnored(__instance))
                return;

            if (ShouldBePicked(__instance))
                __instance.StartCoroutine(PickableSetPicked(__instance));
        }
    }

    [HarmonyPatch(typeof(Pickable), nameof(Pickable.UpdateRespawn))]
    public static class Pickable_UpdateRespawn_PlantsGrowthMultiplier
    {
        private static bool Prefix(Pickable __instance, ref float ___m_respawnTimeMinutes, ref float __state)
        {
            if (Pickable_Awake_PlantsGrowthMultiplier.IsIgnored(__instance))
                return true;

            if (Pickable_Awake_PlantsGrowthMultiplier.ShouldBePicked(__instance))
            {
                __instance.SetPicked(true);
                return false;
            }

            if (IsProtectedPosition(__instance.transform.position))
                return true;

            if (seasonState.GetPlantsGrowthMultiplier() == 0f)
                return false;

            __state = ___m_respawnTimeMinutes;
            ___m_respawnTimeMinutes /= seasonState.GetPlantsGrowthMultiplier();

            return true;
        }

        private static void Postfix(ref float ___m_respawnTimeMinutes, ref float __state)
        {
            if (__state == 0f)
                return;

            ___m_respawnTimeMinutes = __state; 
        }
    }

    [HarmonyPatch(typeof(Vine), nameof(Vine.UpdateGrow))]
    public static class Vine_UpdateGrow_VinesGrowthWinterStop
    {
        private static bool Prefix(Vine __instance, ref Tuple<float, float> __state)
        {
            if (IsProtectedPosition(__instance.transform.position) || __instance.m_initialGrowItterations > 0 || __instance.IsDoneGrowing)
                return true;

            float multiplier = seasonState.GetPlantsGrowthMultiplier();
            if (multiplier == 0f)
                return false;

            __state = Tuple.Create(__instance.m_growTime, __instance.m_growTimePerBranch);

            __instance.m_growTime *= multiplier;
            __instance.m_growTimePerBranch *= multiplier;

            return true;
        }

        private static void Postfix(Vine __instance, Tuple<float, float> __state)
        {
            if (__state == null)
                return;

            __instance.m_growTime = __state.Item1;
            __instance.m_growTimePerBranch = __state.Item2;
        }
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.UpdateHealth))]
    public static class Pickable_UpdateHealth_PlantsPerishInWinter
    {
        private static void Postfix(Plant __instance, ref Plant.Status ___m_status)
        {
            if (IsProtectedPosition(__instance.transform.position))
                return;

            if (___m_status == Plant.Status.Healthy && seasonState.GetPlantsGrowthMultiplier() == 0f && seasonState.GetCurrentSeason() == Season.Winter 
                                                    && seasonState.GetCurrentDay() >= cropsDiesAfterSetDayInWinter.Value && !PlantWillSurviveWinter(__instance.gameObject))
                ___m_status = Plant.Status.WrongBiome;
        }
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.TimeSincePlanted))]
    public static class Plant_TimeSincePlanted_PlantsGrowthMultiplier
    {
        private static void Postfix(Plant __instance, ref double __result)
        {
            if (IsProtectedPosition(__instance.transform.position))
                return;

            double timeSeconds = seasonState.GetTotalSeconds();
            double seasonStart = seasonState.GetStartOfCurrentSeason();
            Season season = seasonState.GetCurrentSeason();
            double rescaledResult = 0d;

            do
            {
                rescaledResult += (timeSeconds - seasonStart >= __result ? __result : timeSeconds - seasonStart) * seasonState.GetPlantsGrowthMultiplier(season);

                __result -= timeSeconds - seasonStart;
                timeSeconds = seasonStart;
                season = seasonState.GetPreviousSeason(season);
                seasonStart -= seasonState.GetDaysInSeason(season) * seasonState.GetDayLengthInSeconds();

            } while (__result > 0);

            __result = rescaledResult;
        }
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.GetHoverText))]
    public static class Plant_GetHoverText_Duration
    {
        private static double GetGrowTime(Plant plant)
        {
            double secondsLeft = plant.GetGrowTime() - plant.TimeSincePlanted();

            if (IsProtectedPosition(plant.transform.position))
                return secondsLeft;

            Season season = seasonState.GetCurrentSeason();
            float growthMultiplier = seasonState.GetPlantsGrowthMultiplier(season);

            double secondsToGrow = 0d;
            double secondsToSeasonEnd = seasonState.GetTimeToCurrentSeasonEnd();
            do
            {
                double timeInSeasonLeft = growthMultiplier == 0 ? secondsToSeasonEnd : Math.Min(secondsLeft / growthMultiplier, secondsToSeasonEnd);

                secondsToGrow += timeInSeasonLeft;
                secondsLeft -= timeInSeasonLeft * growthMultiplier;

                season = seasonState.GetNextSeason(season);
                growthMultiplier = seasonState.GetPlantsGrowthMultiplier(season);

                secondsToSeasonEnd = seasonState.GetDaysInSeason(season) * seasonState.GetDayLengthInSeconds();

            } while (secondsLeft > 0);

            return secondsToGrow;
        }

        private static void Postfix(Plant __instance, ref string __result)
        {
            if (hoverPlant.Value == StationHover.Vanilla)
                return;

            if (__result.IsNullOrWhiteSpace())
                return;

            if (__instance.GetStatus() != Plant.Status.Healthy)
                return;

            if (hoverPlant.Value == StationHover.Percentage)
                __result += $"\n{__instance.TimeSincePlanted() / __instance.GetGrowTime():P0}";
            else if (hoverPlant.Value == StationHover.MinutesSeconds)
                __result += $"\n{FromSeconds(GetGrowTime(__instance))}";
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.Start))]
    public static class Minimap_Start_MinimapSeasonalBorderColor
    {
        private static void Postfix()
        {
            if (!SeasonState.IsActive)
                return;

            seasonState.UpdateMinimapBorder();
        }
    }

    [HarmonyPatch(typeof(Beehive), nameof(Beehive.Interact))]
    public static class Beehive_Interact_BeesInteractionMessage
    {
        private static void Prefix(Beehive __instance, ref string __state)
        {
            if (IsProtectedPosition(__instance.transform.position))
                return;

            __state = __instance.m_happyText;
            if (seasonState.GetBeehiveProductionMultiplier() == 0f)
                __instance.m_happyText = __instance.m_sleepText;
        }

        private static void Postfix(Beehive __instance, ref string __state)
        {
            if (IsProtectedPosition(__instance.transform.position))
                return;

            __instance.m_happyText = __state;
        }
    }

    [HarmonyPatch(typeof(Beehive), nameof(Beehive.GetTimeSinceLastUpdate))]
    public static class Beehive_GetTimeSinceLastUpdate_BeesProduction
    {
        private static void Postfix(Beehive __instance, ref float __result)
        {
            if (IsProtectedPosition(__instance.transform.position))
                return;

            __result *= seasonState.GetBeehiveProductionMultiplier();
        }
    }

    [HarmonyPatch(typeof(Beehive), nameof(Beehive.GetHoverText))]
    public static class Beehive_GetHoverText_Duration
    {
        private static double GetNextProduct(Beehive beehive, float product, int count = 1)
        {
            double secondsLeft = beehive.m_secPerUnit * count - product;
            if (IsProtectedPosition(beehive.transform.position))
                return secondsLeft;

            Season season = seasonState.GetCurrentSeason();
            float multiplier = seasonState.GetBeehiveProductionMultiplier(season);

            double secondsToProduct = 0d;
            double secondsToSeasonEnd = seasonState.GetTimeToCurrentSeasonEnd();

            do
            {
                double timeInSeasonLeft = multiplier == 0 ? secondsToSeasonEnd : Math.Min(secondsLeft / multiplier, secondsToSeasonEnd);

                secondsToProduct += timeInSeasonLeft;
                secondsLeft -= timeInSeasonLeft * multiplier;

                season = seasonState.GetNextSeason(season);
                multiplier = seasonState.GetBeehiveProductionMultiplier(season);

                secondsToSeasonEnd = seasonState.GetDaysInSeason(season) * seasonState.GetDayLengthInSeconds();

            } while (secondsLeft > 0);

            return secondsToProduct;
        }

        private static void Postfix(Beehive __instance, ref string __result)
        {
            if (hoverBeeHive.Value == StationHover.Vanilla)
                return;

            if (__result.IsNullOrWhiteSpace())
                return;

            int honeyLevel = __instance.GetHoneyLevel();

            if (!PrivateArea.CheckAccess(__instance.transform.position, 0f, flash: false) || honeyLevel == __instance.m_maxHoney)
                return;

            float product = __instance.m_nview.GetZDO().GetFloat(ZDOVars.s_product);

            if (hoverBeeHive.Value == StationHover.Percentage)
                __result += $"\n{product / __instance.m_secPerUnit:P0}";
            else if (hoverBeeHive.Value == StationHover.MinutesSeconds)
                __result += $"\n{FromSeconds(GetNextProduct(__instance, product, 1))}";

            if (hoverBeeHiveTotal.Value && honeyLevel < 3)
                if (hoverBeeHive.Value == StationHover.Percentage)
                    __result += $"\n{(product + __instance.m_secPerUnit * honeyLevel) / (__instance.m_secPerUnit * __instance.m_maxHoney):P0}";
                else if (hoverBeeHive.Value == StationHover.MinutesSeconds)
                    __result += $"\n{FromSeconds(GetNextProduct(__instance, product, __instance.m_maxHoney - honeyLevel))}";
        }
    }

    [HarmonyPatch(typeof(Beehive), nameof(Beehive.UpdateBees))]
    public static class Beehive_UpdateBees_BeesSleeping
    {
        private static void Postfix(Beehive __instance, ref GameObject ___m_beeEffect)
        {
            if (IsProtectedPosition(__instance.transform.position))
                return;

            if (seasonState.GetBeehiveProductionMultiplier() == 0f)
            {
                ___m_beeEffect.SetActive(false);
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.UpdateFood))]
    public static class Player_UpdateFood_FoodDrainMultiplier
    {
        private static void Prefix(Player __instance, float dt, bool forceUpdate)
        {
            if (seasonState.GetFoodDrainMultiplier() == 1.0f)
                return;

            if (__instance == null)
                return;

            if (__instance.InInterior() || __instance.InShelter())
                return;

            if (!(dt + __instance.m_foodUpdateTimer >= 1f || forceUpdate))
                return;

            foreach (Player.Food food in __instance.m_foods)
                food.m_time += 1f - Math.Max(0f, seasonState.GetFoodDrainMultiplier());
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.UseStamina))]
    public static class Player_UseStamina_StaminaDrainMultiplier
    {
        private static void Prefix(Player __instance, ref float v)
        {
            if (__instance == null)
                return;

            if (__instance.InInterior() || __instance.InShelter())
                return;

            v *= Math.Max(0f, seasonState.GetStaminaDrainMultiplier());
        }
    }

    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.GetTimeSinceLastUpdate))]
    static class Fireplace_GetTimeSinceLastUpdate_FireplaceDrainMultiplier
    {
        private static void Postfix(Fireplace __instance, ref double __result)
        {
            if (IsProtectedPosition(__instance.transform.position))
                return;

            __result *= (double)Math.Max(0f, seasonState.GetFireplaceDrainMultiplier());
        }
    }

    [HarmonyPatch(typeof(Smelter), nameof(Smelter.GetDeltaTime))]
    static class Smelter_GetDeltaTime_FireplaceDrainMultiplier_SmeltingSpeedMultiplier
    {
        private static void Postfix(Smelter __instance, ref double __result)
        {
            if (__instance.m_name != "$piece_bathtub")
                return;

            if (IsProtectedPosition(__instance.transform.position))
                return;

            __result *= (double)Math.Max(0f, seasonState.GetFireplaceDrainMultiplier());
        }
    }

    [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.UpdateFuel))]
    static class CookingStation_UpdateFuel_FireplaceDrainMultiplier
    {
        private static void Prefix(CookingStation __instance, ref float dt, ref float __state)
        {
            if (IsProtectedPosition(__instance.transform.position))
                return;

            __state = dt;
            dt *= Math.Max(0f, seasonState.GetFireplaceDrainMultiplier());
        }

        private static void Postfix(CookingStation __instance, ref float dt, float __state)
        {
            if (IsProtectedPosition(__instance.transform.position))
                return;

            dt = __state;
        }
    }

    [HarmonyPatch(typeof(SapCollector), nameof(SapCollector.GetTimeSinceLastUpdate))]
    static class SapCollector_GetTimeSinceLastUpdate_SapCollectingSpeedMultiplier
    {
        private static void Postfix(SapCollector __instance, ref float __result)
        {
            if (IsProtectedPosition(__instance.transform.position))
                return;

            __result *= Math.Max(0f, seasonState.GetSapCollectingSpeedMultiplier());
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateWear))]
    public static class WearNTear_UpdateWear_RainProtection
    {
        private static void Prefix(WearNTear __instance, ZNetView ___m_nview, ref bool ___m_noRoofWear, ref bool __state)
        {
            if (!seasonState.GetRainProtection())
                return;

            if (___m_nview == null || !___m_nview.IsValid())
                return;

            if (IsProtectedPosition(__instance.transform.position))
                return;

            __state = ___m_noRoofWear;

            ___m_noRoofWear = false;
        }

        private static void Postfix(ref bool ___m_noRoofWear, bool __state)
        {
            if (!seasonState.GetRainProtection())
                return;

            if (__state != true) return;

            ___m_noRoofWear = __state;
        }
    }

    [HarmonyPatch(typeof(TreeLog), nameof(TreeLog.Destroy))]
    public static class TreeLog_Destroy_TreeWoodDrop
    {
        public static void ApplyWoodMultiplier(DropTable m_dropWhenDestroyed)
        {
            if (!m_dropWhenDestroyed.m_drops.Any(dd => ControlWoodDrop(dd.m_item)))
                return;

            m_dropWhenDestroyed.m_dropMax = Mathf.CeilToInt(m_dropWhenDestroyed.m_dropMax * seasonState.GetWoodFromTreesMultiplier());
            if (m_dropWhenDestroyed.m_dropMin < m_dropWhenDestroyed.m_dropMax)
                m_dropWhenDestroyed.m_dropMin = m_dropWhenDestroyed.m_dropMax;
        }

        private static void Prefix(TreeLog __instance, ZNetView ___m_nview, ref DropTable ___m_dropWhenDestroyed)
        {
            if (seasonState.GetWoodFromTreesMultiplier() == 1.0f)
                return;

            if (___m_nview == null || !___m_nview.IsValid() || !___m_nview.IsOwner())
                return;

            if (IsProtectedPosition(__instance.transform.position))
                return;

            ApplyWoodMultiplier(___m_dropWhenDestroyed);
        }
    }

    [HarmonyPatch(typeof(Destructible), nameof(Destructible.Destroy))]
    public static class Destructible_Destroy_TreeRegrowth
    {
        private static void Prefix(Destructible __instance, ZNetView ___m_nview)
        {
            if (UnityEngine.Random.Range(0.0f, 1.0f) > seasonState.GetTreesReqrowthChance())
                return;

            if (___m_nview == null || !___m_nview.IsValid() || !___m_nview.IsOwner())
                return;

            if (__instance.GetDestructibleType() != DestructibleType.Tree)
                return;

            if (TreeToRegrowth(__instance.gameObject) == null)
                return;

            if (IsProtectedPosition(__instance.transform.position))
                return;

            if ((bool)EffectArea.IsPointInsideArea(__instance.transform.position, EffectArea.Type.PlayerBase))
                return;

            GameObject plant = TreeToRegrowth(__instance.gameObject);

            float scale = ___m_nview.GetZDO().GetFloat(ZDOVars.s_scaleScalarHash);

            instance.StartCoroutine(ReplantTree(plant, __instance.transform.position, __instance.transform.rotation, scale));
        }
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.HaveGrowSpace))]
    public static class Plant_HaveGrowSpace_TreeRegrowth
    {
        private static bool Prefix(ZNetView ___m_nview, ref bool __result)
        {
            if (___m_nview == null || !___m_nview.IsValid() || !___m_nview.IsOwner())
                return true;

            __result = __result || ___m_nview.GetZDO().GetBool(_treeRegrowthHaveGrowSpace, false);
            return !__result;
        }
    }

    [HarmonyPatch(typeof(DropOnDestroyed), nameof(DropOnDestroyed.OnDestroyed))]
    public static class DropOnDestroyed_OnDestroyed_TreeWoodDrop
    {
        private static void Prefix(DropOnDestroyed __instance, ref DropTable ___m_dropWhenDestroyed)
        {
            if (seasonState.GetWoodFromTreesMultiplier() == 1.0f)
                return;

            if (!__instance.TryGetComponent(out Destructible destructible) || destructible.GetDestructibleType() != DestructibleType.Tree)
                return;

            if (IsProtectedPosition(__instance.transform.position))
                return;

            TreeLog_Destroy_TreeWoodDrop.ApplyWoodMultiplier(___m_dropWhenDestroyed);
        }
    }

    [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
    public static class CharacterDrop_GenerateDropList_MeatDrop
    {
        public static void ApplyMeatMultiplier(List<CharacterDrop.Drop> m_drops)
        {
            foreach (CharacterDrop.Drop drop in m_drops)
            {
                if (drop.m_prefab == null || !ControlMeatDrop(drop.m_prefab))
                    continue;

                drop.m_amountMax = Mathf.CeilToInt(drop.m_amountMax * seasonState.GetMeatFromAnimalsMultiplier());
                if (drop.m_amountMin < drop.m_amountMax)
                    drop.m_amountMin = drop.m_amountMax;
            }
        }

        private static void Prefix(CharacterDrop __instance, ref List<CharacterDrop.Drop> ___m_drops)
        {
            if (seasonState.GetMeatFromAnimalsMultiplier() == 1.0f)
                return;

            if (IsProtectedPosition(__instance.transform.position))
                return;

            ApplyMeatMultiplier(___m_drops);
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.GetWindForce))]
    public static class EnvMan_GetWindForce_WindIntensityMultiplier
    {
        private static void Prefix(ref Vector4 ___m_wind, ref float __state)
        {
            if (seasonState.GetWindIntensityMultiplier() == 1.0f)
                return;

            __state = ___m_wind.w;
            ___m_wind.w *= seasonState.GetWindIntensityMultiplier();
        }

        private static void Postfix(ref Vector4 ___m_wind, float __state)
        {
            if (seasonState.GetWindIntensityMultiplier() == 1.0f)
                return;

            ___m_wind.w = __state;
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.GetWindIntensity))]
    public static class EnvMan_GetWindIntensity_WindIntensityMultiplier
    {
        private static void Postfix(ref float __result)
        {
            __result *= seasonState.GetWindIntensityMultiplier();
        }
    }

    [HarmonyPatch(typeof(SE_Rested), nameof(SE_Rested.UpdateTTL))]
    public static class SE_Rested_UpdateTTL_RestedBuffDuration
    {
        private static void Prefix(ref float ___m_baseTTL, ref float ___m_TTLPerComfortLevel, ref Tuple<float, float> __state)
        {
            if (seasonState.GetRestedBuffDurationMultiplier() == 1.0f)
                return;

            __state = new Tuple<float, float> (___m_baseTTL, ___m_TTLPerComfortLevel);
            ___m_baseTTL *= seasonState.GetRestedBuffDurationMultiplier();
            ___m_TTLPerComfortLevel *= seasonState.GetRestedBuffDurationMultiplier();
        }

        private static void Postfix(ref float ___m_baseTTL, ref float ___m_TTLPerComfortLevel, Tuple<float, float> __state)
        {
            if (seasonState.GetRestedBuffDurationMultiplier() == 1.0f)
                return;

            ___m_baseTTL = __state.Item1;
            ___m_TTLPerComfortLevel = __state.Item2;
        }
    }

    [HarmonyPatch(typeof(Procreation), nameof(Procreation.Procreate))]
    public static class Procreation_Procreate_ProcreationMultiplier
    {
        private class ProcreateState
        {
            public float m_totalCheckRange;
            public float m_partnerCheckRange;
            public float m_pregnancyChance;
            public float m_pregnancyDuration;
        }

        private static readonly ProcreateState _procreateState = new ProcreateState();

        private static void Prefix(ref Procreation __instance)
        {
            if (seasonState.GetLivestockProcreationMultiplier() == 1.0f)
                return;

            _procreateState.m_totalCheckRange = __instance.m_totalCheckRange;
            _procreateState.m_partnerCheckRange = __instance.m_partnerCheckRange;
            _procreateState.m_pregnancyChance = __instance.m_pregnancyChance;
            _procreateState.m_pregnancyDuration = __instance.m_pregnancyDuration;

            __instance.m_pregnancyChance *= seasonState.GetLivestockProcreationMultiplier();
            __instance.m_partnerCheckRange *= seasonState.GetLivestockProcreationMultiplier();
            if (seasonState.GetLivestockProcreationMultiplier() != 0f)
            {
                __instance.m_totalCheckRange /= seasonState.GetLivestockProcreationMultiplier();
                __instance.m_pregnancyDuration /= seasonState.GetLivestockProcreationMultiplier();
            }
        }

        private static void Postfix(ref Procreation __instance)
        {
            if (seasonState.GetLivestockProcreationMultiplier() == 1.0f)
                return;

            __instance.m_pregnancyChance = _procreateState.m_pregnancyChance;
            __instance.m_totalCheckRange = _procreateState.m_totalCheckRange;
            __instance.m_partnerCheckRange = _procreateState.m_partnerCheckRange;
            __instance.m_pregnancyDuration = _procreateState.m_pregnancyDuration;
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
    public static class Humanoid_EquipItem_OverheatIn2WarmClothes
    {
        private static void Postfix(Humanoid __instance)
        {
            seasonState.CheckOverheatStatus(__instance);
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
    public static class Humanoid_UnequipItem_OverheatIn2WarmClothes
    {
        private static void Postfix(Humanoid __instance)
        {
            seasonState.CheckOverheatStatus(__instance);
        }
    }

    [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.GetPossibleRandomEvents))]
    public static class RandEventSystem_GetPossibleRandomEvents_RandomEventWeights
    {
        private static void Prefix(RandEventSystem __instance, ref List<RandomEvent> __state)
        {
            if (!controlRandomEvents.Value)
                return;

            List<SeasonRandomEvents.SeasonRandomEvent> randEvents = SeasonState.seasonRandomEvents.GetSeasonEvents(seasonState.GetCurrentSeason());

            __state = new List<RandomEvent>();
            for (int i = 0; i < __instance.m_events.Count; i++)
            {
                RandomEvent randEvent = __instance.m_events[i];
                __state.Add(JsonUtility.FromJson<RandomEvent>(JsonUtility.ToJson(randEvent)));

                SeasonRandomEvents.SeasonRandomEvent seasonRandEvent = randEvents.Find(re => re.m_name == randEvent.m_name);
                if (seasonRandEvent != null)
                {
                    if (seasonRandEvent.m_biomes != null)
                    {
                        randEvent.m_biome = seasonRandEvent.GetBiome();
                        randEvent.m_spawn.ForEach(spawn => spawn.m_biome |= randEvent.m_biome);
                    }

                    if (seasonRandEvent.m_weight == 0)
                    {
                        randEvent.m_enabled = false;
                    }
                    else if (seasonRandEvent.m_weight > 1)
                    {
                        for (int r = 2; r <= seasonRandEvent.m_weight; r++)
                        {
                            RandEventSystem.instance.m_events.Insert(i, randEvent);
                            i++;
                        }
                    }
                }
            }
        }

        private static void Postfix(ref RandEventSystem __instance, List<RandomEvent> __state)
        {
            if (!controlRandomEvents.Value)
                return;

            __instance.m_events.Clear();
            __instance.m_events.AddRange(__state.ToList());
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.SetEnv))]
    public static class EnvMan_SetEnv_LuminancePatch
    {
        private class LightState
        {
            public Color m_ambColorNight;
            public Color m_fogColorNight;
            public Color m_fogColorSunNight;
            public Color m_sunColorNight;

            public Color m_ambColorDay;
            public Color m_fogColorMorning;
            public Color m_fogColorDay;
            public Color m_fogColorEvening;
            public Color m_fogColorSunMorning;
            public Color m_fogColorSunDay;
            public Color m_fogColorSunEvening;
            public Color m_sunColorMorning;
            public Color m_sunColorDay;
            public Color m_sunColorEvening;

            public float m_lightIntensityDay;
            public float m_lightIntensityNight;

            public float m_fogDensityNight;
            public float m_fogDensityMorning;
            public float m_fogDensityDay;
            public float m_fogDensityEvening;
        }

        private static readonly LightState _lightState = new LightState();

        private static Color ChangeColorLuminance(Color color, float luminanceMultiplier)
        {
            HSLColor newColor = new HSLColor(color);
            newColor.l *= luminanceMultiplier;
            return newColor.ToRGBA();
        }

        private static void SaveLightState(EnvSetup env)
        {
            _lightState.m_ambColorNight = env.m_ambColorNight;
            _lightState.m_sunColorNight = env.m_sunColorNight;
            _lightState.m_fogColorNight = env.m_fogColorNight;
            _lightState.m_fogColorSunNight = env.m_fogColorSunNight;

            _lightState.m_ambColorDay = env.m_ambColorDay;
            _lightState.m_sunColorDay = env.m_sunColorDay;
            _lightState.m_fogColorDay = env.m_fogColorDay;
            _lightState.m_fogColorSunDay = env.m_fogColorSunDay;

            _lightState.m_sunColorMorning = env.m_sunColorMorning;
            _lightState.m_fogColorMorning = env.m_fogColorMorning;
            _lightState.m_fogColorSunMorning = env.m_fogColorSunMorning;

            _lightState.m_sunColorEvening = env.m_sunColorEvening;
            _lightState.m_fogColorEvening = env.m_fogColorEvening;
            _lightState.m_fogColorSunEvening = env.m_fogColorSunEvening;

            _lightState.m_lightIntensityDay = env.m_lightIntensityDay;
            _lightState.m_lightIntensityNight = env.m_lightIntensityNight;

            _lightState.m_fogDensityNight = env.m_fogDensityNight;
            _lightState.m_fogDensityMorning = env.m_fogDensityMorning;
            _lightState.m_fogDensityDay = env.m_fogDensityDay;
            _lightState.m_fogDensityEvening = env.m_fogDensityEvening;
        }

        private static void ChangeEnvColor(EnvSetup env, SeasonLightings.SeasonLightingSettings lightingSettings, bool indoors = false)
        {
            env.m_ambColorNight = ChangeColorLuminance(env.m_ambColorNight, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.night.luminanceMultiplier);
            env.m_fogColorNight = ChangeColorLuminance(env.m_fogColorNight, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.night.luminanceMultiplier);
            env.m_fogColorSunNight = ChangeColorLuminance(env.m_fogColorSunNight, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.night.luminanceMultiplier);
            env.m_sunColorNight = ChangeColorLuminance(env.m_sunColorNight, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.night.luminanceMultiplier);

            env.m_fogColorMorning = ChangeColorLuminance(env.m_fogColorMorning, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.morning.luminanceMultiplier);
            env.m_fogColorSunMorning = ChangeColorLuminance(env.m_fogColorSunMorning, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.morning.luminanceMultiplier);
            env.m_sunColorMorning = ChangeColorLuminance(env.m_sunColorMorning, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.morning.luminanceMultiplier);

            env.m_ambColorDay = ChangeColorLuminance(env.m_ambColorDay, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.day.luminanceMultiplier);
            env.m_fogColorDay = ChangeColorLuminance(env.m_fogColorDay, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.day.luminanceMultiplier);
            env.m_fogColorSunDay = ChangeColorLuminance(env.m_fogColorSunDay, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.day.luminanceMultiplier);
            env.m_sunColorDay = ChangeColorLuminance(env.m_sunColorDay, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.day.luminanceMultiplier);

            env.m_fogColorEvening = ChangeColorLuminance(env.m_fogColorEvening, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.evening.luminanceMultiplier);
            env.m_fogColorSunEvening = ChangeColorLuminance(env.m_fogColorSunEvening, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.evening.luminanceMultiplier);
            env.m_sunColorEvening = ChangeColorLuminance(env.m_sunColorEvening, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.evening.luminanceMultiplier);

            env.m_fogDensityNight *= indoors ? lightingSettings.indoors.fogDensityMultiplier : lightingSettings.night.fogDensityMultiplier;
            env.m_fogDensityMorning *= indoors ? lightingSettings.indoors.fogDensityMultiplier : lightingSettings.morning.fogDensityMultiplier;
            env.m_fogDensityDay *= indoors ? lightingSettings.indoors.fogDensityMultiplier : lightingSettings.day.fogDensityMultiplier;
            env.m_fogDensityEvening *= indoors ? lightingSettings.indoors.fogDensityMultiplier : lightingSettings.evening.fogDensityMultiplier;

            env.m_lightIntensityDay *= lightingSettings.lightIntensityDayMultiplier;
            env.m_lightIntensityNight *= lightingSettings.lightIntensityNightMultiplier;
        }

        public static void ChangeLightState(EnvSetup env)
        {
            SaveLightState(env);

            SeasonLightings.SeasonLightingSettings lightingSettings = SeasonState.seasonLightings.GetSeasonLighting(seasonState.GetCurrentSeason());

            ChangeEnvColor(env, lightingSettings, indoors: Player.m_localPlayer != null && Player.m_localPlayer.InInterior());
        }

        public static void ResetLightState(EnvSetup env)
        {
            env.m_ambColorNight = _lightState.m_ambColorNight;
            env.m_sunColorNight = _lightState.m_sunColorNight;
            env.m_fogColorNight = _lightState.m_fogColorNight;
            env.m_fogColorSunNight = _lightState.m_fogColorSunNight;

            env.m_ambColorDay = _lightState.m_ambColorDay;
            env.m_sunColorDay = _lightState.m_sunColorDay;
            env.m_fogColorDay = _lightState.m_fogColorDay;
            env.m_fogColorSunDay = _lightState.m_fogColorSunDay;

            env.m_sunColorMorning = _lightState.m_sunColorMorning;
            env.m_fogColorMorning = _lightState.m_fogColorMorning;
            env.m_fogColorSunMorning = _lightState.m_fogColorSunMorning;

            env.m_sunColorEvening = _lightState.m_sunColorEvening;
            env.m_fogColorEvening = _lightState.m_fogColorEvening;
            env.m_fogColorSunEvening = _lightState.m_fogColorSunEvening;

            env.m_fogDensityNight = _lightState.m_fogDensityNight;
            env.m_fogDensityMorning = _lightState.m_fogDensityMorning;
            env.m_fogDensityDay = _lightState.m_fogDensityDay;
            env.m_fogDensityEvening = _lightState.m_fogDensityEvening;

            env.m_lightIntensityDay = _lightState.m_lightIntensityDay;
            env.m_lightIntensityNight = _lightState.m_lightIntensityNight;
        }

        [HarmonyPriority(Priority.Last)]
        [HarmonyBefore(new string[1] { "shudnal.GammaOfNightLights" })]
        public static void Prefix(EnvSetup env)
        {
            if (!controlLightings.Value || !UseTextureControllers())
                return;

            ChangeLightState(env);
        }

        [HarmonyPriority(Priority.First)]
        [HarmonyAfter(new string[1] { "shudnal.GammaOfNightLights" })]
        public static void Postfix(EnvSetup env)
        {
            if (!controlLightings.Value || !UseTextureControllers())
                return;

            ResetLightState(env);
        }
    }

    [HarmonyPatch(typeof(FootStep), nameof(FootStep.FindBestStepEffect))]
    public static class FootStep_FindBestStepEffect_SnowFootsteps
    {
        private static void Prefix(FootStep __instance, ref FootStep.GroundMaterial material)
        {
            if (IsShieldProtectionActive() && __instance.m_character?.GetLastGroundCollider() != null && ZoneSystemVariantController.IsProtectedHeightmap(__instance.m_character.GetLastGroundCollider().GetComponent<Heightmap>()))
                return;

            if (seasonState.GetCurrentSeason() == Season.Winter && (material == FootStep.GroundMaterial.Mud || material == FootStep.GroundMaterial.Grass || material == FootStep.GroundMaterial.GenericGround))
                material = FootStep.GroundMaterial.Snow;
            else if (ZoneSystemVariantController.IsWaterSurfaceFrozen() && material == FootStep.GroundMaterial.Water)
                material = FootStep.GroundMaterial.Snow;
        }
    }

    [HarmonyPatch(typeof(Hud), nameof(Hud.UpdateBlackScreen))]
    public static class Hud_UpdateBlackScreen_BlackScreenFadeOnSeasonChange
    {
        private static bool Prefix()
        {
            return !seasonState.GetSeasonIsChanging();
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.IsFreezing))]
    public static class EnvMan_IsFreezing_SwimmingInWinterIsFreezing
    {
        private static void Postfix(ref bool __result)
        {
            if (!freezingSwimmingInWinter.Value)
                return;

            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            __result = __result || player.IsSwimming() && seasonState.GetCurrentSeason() == Season.Winter && EnvMan.IsCold();
        }
    }

    [HarmonyPatch(typeof(Bed), nameof(Bed.CheckFire))]
    public static class Bed_CheckFire_PreventSleepingWithTorchFiresource
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Humanoid human)
        {
            if (human == Player.m_localPlayer && seasonState.GetTorchAsFiresource() && 
                (human.GetLeftItem() != null && human.GetLeftItem().m_shared.m_itemType == ItemDrop.ItemData.ItemType.Torch
                 || human.GetRightItem() != null && human.GetRightItem().m_shared.m_itemType == ItemDrop.ItemData.ItemType.Torch))
                human.HideHandItems();
        }
    }

    [HarmonyPatch(typeof(Trader), nameof(Trader.GetAvailableItems))]
    public static class Trader_GetAvailableItems_SeasonalTraderItems
    {
        [HarmonyPriority(Priority.First)]
        static void Postfix(Trader __instance, ref List<Trader.TradeItem> __result)
        {
            if (controlTraders.Value)
                SeasonState.seasonTraderItems.AddSeasonalTraderItems(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.UpdateSleeping))]
    public static class Game_UpdateSleeping_ForceUpdateState
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(bool ___m_sleeping, ref bool __state)
        {
            __state = ___m_sleeping;
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(bool ___m_sleeping, bool __state)
        {
            if (!___m_sleeping && __state)
                SeasonState.CheckSeasonChange();
        }
    }

    [HarmonyPatch(typeof(Terminal), nameof(Terminal.TryRunCommand))]
    public static class Terminal_TryRunCommand_ForceUpdateState
    {
        private static void Postfix(string text)
        {
            if (text.IndexOf("skiptime") > -1 && SeasonState.IsActive && ZNet.instance && ZNet.instance.IsServer())
            {
                LogInfo("Force update season state after skiptime");
                SeasonState.CheckSeasonChange();
            }
        }
    }

    [HarmonyPatch(typeof(Settings), nameof(Settings.SaveTabSettings))]
    public static class Settings_SaveTabSettings_ForceUpdateState
    {
        private static void Postfix()
        {
            seasonState?.UpdateWinterBloomEffect();
        }
    }
}
