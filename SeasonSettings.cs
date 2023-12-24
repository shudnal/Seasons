using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using static Seasons.Seasons;

namespace Seasons
{
    [Serializable]
    public class SeasonSettingsFile
    {
        public int daysInSeason = -1;
        public int nightLength = -1;

        public SeasonSettingsFile(SeasonSettings settings)
        {
            daysInSeason = settings.m_daysInSeason;
            nightLength = settings.m_nightLength;
        }

        public SeasonSettingsFile() 
        {
        }
    }

    public class SeasonSettings
    {
        public const string defaultsSubdirectory = "Default settings";
        public const int nightLentghDefault = 30;

        public int m_daysInSeason = 10;
        public int m_nightLength = nightLentghDefault;

        public SeasonSettings(Season season)
        {
            LoadDefaultSeasonSettings(season);
        }

        public SeasonSettings(Season season, SeasonSettingsFile settings)
        {
            LoadDefaultSeasonSettings(season);

            SeasonSettingsFile defaultFileSettings = new SeasonSettingsFile();

            if (settings.daysInSeason != defaultFileSettings.daysInSeason)
                m_daysInSeason = settings.daysInSeason;

            if (settings.nightLength != defaultFileSettings.nightLength)
                m_nightLength = settings.nightLength;
        }

        public void SaveToJSON(string filename)
        {
            File.WriteAllText("\\\\?\\" + filename, JsonConvert.SerializeObject(new SeasonSettingsFile(this), Formatting.Indented));
        }

        private void LoadDefaultSeasonSettings(Season season)
        {
            switch (season)
            {
                case Season.Spring:
                    {
                        break;
                    }
                case Season.Summer:
                    {
                        m_nightLength = 20;
                        break;
                    }
                case Season.Fall:
                    {
                        break;
                    }
                case Season.Winter:
                    {
                        m_nightLength = 40;
                        break;
                    }
            }
        }

        public static bool GetSeasonByFilename(string filename, out Season season)
        {
            season = Season.Spring;

            foreach (Season season1 in Enum.GetValues(typeof(Season)))
                if (filename.ToLower() == $"{season1}.json".ToLower())
                {
                    season = season1;
                    return true;
                }

            return false;
        }

        public static void SetupConfigWatcher()
        {
            string filter = $"*.json";

            FileSystemWatcher fileSystemWatcher1 = new FileSystemWatcher(configDirectory, filter);
            fileSystemWatcher1.Changed += new FileSystemEventHandler(ReadConfigs);
            fileSystemWatcher1.Created += new FileSystemEventHandler(ReadConfigs);
            fileSystemWatcher1.Renamed += new RenamedEventHandler(ReadConfigs);
            fileSystemWatcher1.IncludeSubdirectories = false;
            fileSystemWatcher1.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            fileSystemWatcher1.EnableRaisingEvents = true;

            ReadConfigs(null, null);
        }

        private static void ReadConfigs(object sender, FileSystemEventArgs eargs)
        {
            Dictionary<int, string> localConfig = new Dictionary<int, string>();

            foreach (FileInfo file in new DirectoryInfo(configDirectory).GetFiles("*.json", SearchOption.TopDirectoryOnly))
            {
                if (!GetSeasonByFilename(file.Name, out Season season))
                    continue;

                try
                {
                    localConfig.Add((int)season, File.ReadAllText(file.FullName));
                }
                catch (Exception e)
                {
                    LogWarning($"Error reading file ({file.FullName})! Error: {e.Message}");
                }
            }

            seasonsSettingsJSON.AssignLocalValue(localConfig);
        }

        public static void UpdateSeasonSettings()
        {
            seasonState.UpdateSeasonSettings();
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
    public static class ZoneSystem_Start_SeasonSettingsConfigWatcher
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            SeasonSettings.SetupConfigWatcher();
        }
    }
}
