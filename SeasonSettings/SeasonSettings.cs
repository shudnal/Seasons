using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using static Seasons.Seasons;
using ServerSync;

namespace Seasons
{
    public class SeasonSettings
    {
        public const string defaultsSubdirectory = "Default settings";
        public const string customEnvironmentsFileName = "Custom environments.json";
        public const string customBiomeEnvironmentsFileName = "Custom Biome Environments.json";
        public const string customEventsFileName = "Custom events.json";
        public const string customLightingsFileName = "Custom lightings.json";
        public const string customStatsFileName = "Custom stats.json";
        public const string customTraderItemsFileName = "Custom trader items.json";
        public const string customWorldSettingsFileName = "Custom world settings.json";
        public const string customGrassSettingsFileName = "Custom grass settings.json";
        public const string customClutterSettingsFileName = "Custom clutter settings.json";
        public const string customBiomesSettingsFileName = "Custom biome settings.json";
        public const int nightLentghDefault = 30;
        public const string itemDropNameTorch = "$item_torch";
        public const string itemNameTorch = "Torch";

        public int m_daysInSeason = 10;
        public int m_nightLength = nightLentghDefault;
        public bool m_torchAsFiresource = false;
        public float m_torchDurabilityDrain = 0.1f;
        public float m_plantsGrowthMultiplier = 1.0f;
        public float m_beehiveProductionMultiplier = 1.0f;
        public float m_foodDrainMultiplier = 1.0f;
        public float m_staminaDrainMultiplier = 1.0f;
        public float m_fireplaceDrainMultiplier = 1.0f;
        public float m_sapCollectingSpeedMultiplier = 1.0f;
        public bool m_rainProtection = false;
        public float m_woodFromTreesMultiplier = 1.0f;
        public float m_windIntensityMultiplier = 1.0f;
        public float m_restedBuffDurationMultiplier = 1.0f;
        public float m_livestockProcreationMultiplier = 1.0f;
        public bool m_overheatIn2WarmClothes = false;
        public float m_meatFromAnimalsMultiplier = 1.0f;
        public float m_treesRegrowthChance = 0.0f;

        internal static FileSystemWatcher configWatcher;

        public SeasonSettings(Season season)
        {
            LoadDefaultSeasonSettings(season);
        }

        public SeasonSettings(Season season, SeasonSettingsFile settings)
        {
            LoadDefaultSeasonSettings(season);

            foreach (FieldInfo fieldSettings in settings.GetType().GetFields())
            {
                object value = fieldSettings.GetValue(settings);
                if (value != null)
                {
                    FieldInfo fieldSeason = typeof(SeasonSettings).GetField($"m_{fieldSettings.Name}");
                    fieldSeason?.SetValue(this, value);
                }
            }
        }

        public void SaveToJSON(string filename)
        {
            File.WriteAllText(filename, JsonConvert.SerializeObject(new SeasonSettingsFile(this), Formatting.Indented));
        }

        private void LoadDefaultSeasonSettings(Season season)
        {
            switch (season)
            {
                case Season.Spring:
                    {
                        m_plantsGrowthMultiplier = 2.0f;
                        m_beehiveProductionMultiplier = 0.5f;
                        m_fireplaceDrainMultiplier = 0.75f;
                        m_sapCollectingSpeedMultiplier = 2.0f;
                        m_woodFromTreesMultiplier = 0.75f;
                        m_windIntensityMultiplier = 0.9f;
                        m_restedBuffDurationMultiplier = 1.25f;
                        m_livestockProcreationMultiplier = 1.5f;
                        m_meatFromAnimalsMultiplier = 0.5f;
                        m_treesRegrowthChance = 0.9f;
                        break;
                    }
                case Season.Summer:
                    {
                        m_plantsGrowthMultiplier = 1.5f;
                        m_beehiveProductionMultiplier = 2f;
                        m_foodDrainMultiplier = 0.75f;
                        m_nightLength = 15;
                        m_staminaDrainMultiplier = 0.8f;
                        m_fireplaceDrainMultiplier = 0.25f;
                        m_sapCollectingSpeedMultiplier = 1.25f;
                        m_woodFromTreesMultiplier = 0.75f;
                        m_windIntensityMultiplier = 1.1f;
                        m_restedBuffDurationMultiplier = 1.5f;
                        m_livestockProcreationMultiplier = 1.25f;
                        m_overheatIn2WarmClothes = true;
                        m_meatFromAnimalsMultiplier = 0.75f;
                        m_treesRegrowthChance = 0.75f;
                        break;
                    }
                case Season.Fall:
                    {
                        m_plantsGrowthMultiplier = 0.5f;
                        m_beehiveProductionMultiplier = 1.5f;
                        m_fireplaceDrainMultiplier = 1f;
                        m_torchAsFiresource = true;
                        m_sapCollectingSpeedMultiplier = 0.5f;
                        m_woodFromTreesMultiplier = 1.25f;
                        m_windIntensityMultiplier = 1.2f;
                        m_restedBuffDurationMultiplier = 0.85f;
                        m_livestockProcreationMultiplier = 0.75f;
                        m_meatFromAnimalsMultiplier = 1.25f;
                        m_treesRegrowthChance = 0.25f;
                        break;
                    }
                case Season.Winter:
                    {
                        m_plantsGrowthMultiplier = 0f;
                        m_beehiveProductionMultiplier = 0f;
                        m_foodDrainMultiplier = 1.25f;
                        m_nightLength = 45;
                        m_torchAsFiresource = true;
                        m_staminaDrainMultiplier = 1.2f;
                        m_fireplaceDrainMultiplier = 2f;
                        m_sapCollectingSpeedMultiplier = 0.25f;
                        m_rainProtection = true;
                        m_woodFromTreesMultiplier = 1.5f;
                        m_windIntensityMultiplier = 0.9f;
                        m_restedBuffDurationMultiplier = 0.75f;
                        m_livestockProcreationMultiplier = 0.5f;
                        m_meatFromAnimalsMultiplier = 1.5f;
                        m_treesRegrowthChance = 0f;
                        break;
                    }
            }
        }

        public static bool TryGetSeasonByFilename(string filename, out Season season)
        {
            season = Season.Spring;

            foreach (Season season1 in Enum.GetValues(typeof(Season)))
                if (filename.Equals(SeasonState.GetSeasonalFileName(season1), StringComparison.OrdinalIgnoreCase))
                {
                    season = season1;
                    return true;
                }

            return false;
        }

        public static void SetupConfigWatcher(bool enabled)
        {
            if (enabled)
                ReadInitialConfigs();

            if (configWatcher == null)
            {
                configWatcher = new FileSystemWatcher(configDirectory, $"*.json");
                configWatcher.Changed += new FileSystemEventHandler(ReadConfigs);
                configWatcher.Created += new FileSystemEventHandler(ReadConfigs);
                configWatcher.Renamed += new RenamedEventHandler(ReadConfigs);
                configWatcher.Deleted += new FileSystemEventHandler(ReadConfigs);
                configWatcher.IncludeSubdirectories = false;
                configWatcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            }

            configWatcher.EnableRaisingEvents = enabled;
        }

        private static void ReadInitialConfigs()
        {
            // Order matters as it defines settings apply order
            foreach (string filename in new List<string>()
            {
                customEnvironmentsFileName,
                customBiomeEnvironmentsFileName,
                customEventsFileName,
                customLightingsFileName,
                customStatsFileName,
                customTraderItemsFileName,
                customWorldSettingsFileName,
                customGrassSettingsFileName,
                customClutterSettingsFileName,
                customBiomesSettingsFileName
            })
            {
                ReadConfigFile(filename, Path.Combine(configDirectory, filename), initial: true);
            };

            ReadSeasonsSettings(initial: true);
        }

        internal static Dictionary<int, string> GetSeasonalSettings()
        {
            Dictionary<int, string> localConfig = new Dictionary<int, string>();

            foreach (FileInfo file in new DirectoryInfo(configDirectory).GetFiles("*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (TryGetSeasonByFilename(file.Name, out Season season))
                        localConfig.Add((int)season, File.ReadAllText(file.FullName));
                }
                catch (Exception e)
                {
                    LogWarning($"Error reading file ({file.FullName})! Error: {e.Message}");
                }
            }

            return localConfig;
        }

        private static void ReadSeasonsSettings(bool initial = false)
        {
            if (initial)
                seasonsSettingsJSON.AssignValueSafe(GetSeasonalSettings);
            else
                seasonsSettingsJSON.AssignValueIfChanged(GetSeasonalSettings);
        }

        private static void ReadConfigs(object sender, FileSystemEventArgs eargs)
        {
            ReadConfigFile(eargs.Name, eargs.FullPath);
            if (eargs is RenamedEventArgs)
            {
                if (GetSyncedValueToAssign((eargs as RenamedEventArgs).OldName, out CustomSyncedValue<string> syncedValue, out string logMessage))
                {
                    syncedValue.AssignValueIfChanged("");
                    LogInfo(logMessage + " defaults");
                }
                else if (TryGetSeasonByFilename(eargs.Name, out _))
                {
                    ReadSeasonsSettings();
                }
            }
        }

        private static void ReadConfigFile(string filename, string fullname, bool initial = false)
        {
            if (!GetSyncedValueToAssign(filename, out CustomSyncedValue<string> syncedValue, out string logMessage))
            {
                if (TryGetSeasonByFilename(filename, out _))
                    ReadSeasonsSettings();
                return;
            }

            string content;
            try
            {
                content = File.ReadAllText(fullname);
            }
            catch (Exception e)
            {
                if (!initial) 
                    LogWarning($"Error reading file ({fullname})! Error: {e.Message}");
                
                content = "";
                logMessage += " defaults";
            }

            if (initial)
                syncedValue.AssignValueSafe(content);
            else
                syncedValue.AssignValueIfChanged(content);

            LogInfo(logMessage);
        }

        private static bool GetSyncedValueToAssign(string filename, out CustomSyncedValue<string> customSyncedValue, out string logMessage)
        {
            if (filename.Equals(customEnvironmentsFileName, StringComparison.OrdinalIgnoreCase))
            {
                customSyncedValue = customEnvironmentsJSON;
                logMessage = "Custom environments file loaded";
            }

            else if (filename.Equals(customBiomeEnvironmentsFileName, StringComparison.OrdinalIgnoreCase))
            {
                customSyncedValue = customBiomeEnvironmentsJSON;
                logMessage = "Custom biome environments file loaded";
            }

            else if (filename.Equals(customEventsFileName, StringComparison.OrdinalIgnoreCase))
            {
                customSyncedValue = customEventsJSON;
                logMessage = "Custom events file loaded";
            }

            else if (filename.Equals(customLightingsFileName, StringComparison.OrdinalIgnoreCase))
            {
                customSyncedValue = customLightingsJSON;
                logMessage = "Custom lightings file loaded";
            }

            else if (filename.Equals(customStatsFileName, StringComparison.OrdinalIgnoreCase))
            {
                customSyncedValue = customStatsJSON;
                logMessage = "Custom stats file loaded";
            }

            else if (filename.Equals(customTraderItemsFileName, StringComparison.OrdinalIgnoreCase))
            {
                customSyncedValue = customTraderItemsJSON;
                logMessage = "Custom trader items file loaded";
            }

            else if (filename.Equals(customWorldSettingsFileName, StringComparison.OrdinalIgnoreCase))
            {
                customSyncedValue = customWorldSettingsJSON;
                logMessage = "Custom world settings file loaded";
            }

            else if (filename.Equals(customGrassSettingsFileName, StringComparison.OrdinalIgnoreCase))
            {
                customSyncedValue = customGrassSettingsJSON;
                logMessage = "Custom grass settings file loaded";
            }

            else if (filename.Equals(customClutterSettingsFileName, StringComparison.OrdinalIgnoreCase))
            {
                customSyncedValue = customClutterSettingsJSON;
                logMessage = "Custom clutter settings file loaded";
            }

            else if (filename.Equals(customBiomesSettingsFileName, StringComparison.OrdinalIgnoreCase))
            {
                customSyncedValue = customBiomeSettingsJSON;
                logMessage = "Custom biomes settings file loaded";
            }
            else
            {
                customSyncedValue = null;
                logMessage = "";
            }

            return customSyncedValue != null;
        }

        public static void SaveDefaultEnvironments(string folder)
        {
            List<SeasonEnvironment> list = new List<SeasonEnvironment>();
            EnvMan.instance.m_environments.Do(env => list.Add(new SeasonEnvironment(env)));

            LogInfo($"Saving default environments settings");
            File.WriteAllText(Path.Combine(folder, "Default environments.json"), JsonConvert.SerializeObject(list, Formatting.Indented));

            JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
            {
                DefaultValueHandling = DefaultValueHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore,
            };

            LogInfo($"Saving default custom environments settings");
            File.WriteAllText(Path.Combine(folder, customEnvironmentsFileName), JsonConvert.SerializeObject(SeasonEnvironment.GetDefaultCustomEnvironments(), Formatting.Indented, jsonSerializerSettings));

            LogInfo($"Saving default biome environments settings");
            File.WriteAllText(Path.Combine(folder, "Default biome environments.json"), JsonConvert.SerializeObject(EnvMan.instance.m_biomes.ToList(), Formatting.Indented));

            LogInfo($"Saving default custom biome environments settings");
            File.WriteAllText(Path.Combine(folder, customBiomeEnvironmentsFileName), JsonConvert.SerializeObject(new SeasonBiomeEnvironments(loadDefaults: true), Formatting.Indented));
        }

        public static void SaveDefaultEvents(string folder)
        {
            List<SeasonRandomEvents.SeasonRandomEvent> list = new List<SeasonRandomEvents.SeasonRandomEvent>();
            RandEventSystem.instance.m_events.DoIf(randevent => randevent.m_random, randevent => list.Add(new SeasonRandomEvents.SeasonRandomEvent(randevent)));

            JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
            };

            LogInfo($"Saving default events settings");
            File.WriteAllText(Path.Combine(folder, "Default events.json"), JsonConvert.SerializeObject(list, Formatting.Indented, jsonSerializerSettings));

            LogInfo($"Saving default custom events settings");
            File.WriteAllText(Path.Combine(folder, customEventsFileName), JsonConvert.SerializeObject(new SeasonRandomEvents(loadDefaults: true), Formatting.Indented, jsonSerializerSettings));
        }

        public static void SaveDefaultLightings(string folder)
        {
            LogInfo($"Saving default custom ligthing settings");
            File.WriteAllText(Path.Combine(folder, customLightingsFileName), JsonConvert.SerializeObject(new SeasonLightings(loadDefaults: true), Formatting.Indented));
        }

        public static void SaveDefaultStats(string folder)
        {
            LogInfo($"Saving default custom stats settings");
            File.WriteAllText(Path.Combine(folder, customStatsFileName), JsonConvert.SerializeObject(new SeasonStats(loadDefaults: true), Formatting.Indented));
        }

        public static void SaveDefaultTraderItems(string folder)
        {
            LogInfo($"Saving default custom trader items settings");
            File.WriteAllText(Path.Combine(folder, customTraderItemsFileName), JsonConvert.SerializeObject(new SeasonTraderItems(loadDefaults: true), Formatting.Indented));
        }

        public static void SaveDefaultWorldSettings(string folder)
        {
            LogInfo($"Saving default custom world settings");
            File.WriteAllText(Path.Combine(folder, customWorldSettingsFileName), JsonConvert.SerializeObject(new SeasonWorldSettings(loadDefaults: true), Formatting.Indented));
        }

        public static void SaveDefaultGrassSettings(string folder)
        {
            LogInfo($"Saving default custom grass settings");
            File.WriteAllText(Path.Combine(folder, customGrassSettingsFileName), JsonConvert.SerializeObject(new SeasonGrassSettings(loadDefaults: true), Formatting.Indented));
        }

        public static void SaveDefaultClutterSettings(string folder)
        {
            LogInfo($"Saving default custom clutter settings");
            File.WriteAllText(Path.Combine(folder, customClutterSettingsFileName), JsonConvert.SerializeObject(new SeasonClutterSettings(loadDefaults: true), Formatting.Indented));
        }

        public static void SaveDefaultBiomesSettings(string folder)
        {
            LogInfo($"Saving default custom biomes settings");
            File.WriteAllText(Path.Combine(folder, customBiomesSettingsFileName), JsonConvert.SerializeObject(new SeasonBiomeSettings(loadDefaults: true), Formatting.Indented, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
            }));
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
    public static class ZoneSystem_Start_InitSeasonStateAndConfigWatcher
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(new string[1] { "expand_world_data" })]
        private static void Postfix()
        {
            seasonState = new SeasonState(initialize: true);
            SeasonSettings.SetupConfigWatcher(enabled: true);
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.OnDestroy))]
    public static class ZoneSystem_OnDestroy_DisableConfigWatcher
    {
        private static void Postfix()
        {
            SeasonSettings.SetupConfigWatcher(enabled: false);
            SeasonState.ResetCurrentSeasonDay();
        }
    }
}