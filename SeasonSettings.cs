using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;
using static Seasons.Seasons;
using static TerrainOp;
using System.Security.Policy;
using System.Runtime.ConstrainedExecution;
using static Utils;

namespace Seasons
{
    [Serializable]
    public class SeasonSettingsFile
    {
        public int daysInSeason;
        public int nightLength;
        public bool torchAsFiresource;
        public float torchDurabilityDrain;

        public SeasonSettingsFile(SeasonSettings settings)
        {
            daysInSeason = settings.m_daysInSeason;
            nightLength = settings.m_nightLength;
            torchAsFiresource = settings.m_torchAsFiresource;
            torchDurabilityDrain = settings.m_torchDurabilityDrain;
        }

        public SeasonSettingsFile()
        {
        }
    }

    [Serializable]
    public class SeasonBiomeEnvironment
    {
        public class EnvironmentAdd
        {
            public string m_name;
            public EnvEntry m_environment;

            public EnvironmentAdd(string name, EnvEntry environment)
            {
                m_name = name;
                m_environment = environment;
            }
        }

        [Serializable]
        public class EnvironmentRemove
        {
            public string m_name;
            public string m_environment;

            public EnvironmentRemove(string name, string environment)
            {
                m_name = name;
                m_environment = environment;
            }
        }

        [Serializable]
        public class EnvironmentReplace
        {
            public string m_environment;
            public string replace_to;

            public EnvironmentReplace(string environment, string replaceTo)
            {
                m_environment = environment;
                replace_to = replaceTo;
            }
        }

        [Serializable]
        public class EnvironmentReplacePair
        {
            public string m_environment;
            public string replace_to;
        }

        public List<EnvironmentAdd> add = new List<EnvironmentAdd>();

        public List<EnvironmentRemove> remove = new List<EnvironmentRemove>();

        public List<EnvironmentReplace> replace = new List<EnvironmentReplace>();
    }

    [Serializable]
    public class SeasonEnvironment
    {
        public string m_cloneFrom = "";

        public string m_name = "";

        public bool m_default;

        public bool m_isWet;

        public bool m_isFreezing;

        public bool m_isFreezingAtNight;

        public bool m_isCold;

        public bool m_isColdAtNight;

        public bool m_alwaysDark;

        public string m_ambColorNight;

        public string m_ambColorDay;

        public string m_fogColorNight;

        public string m_fogColorMorning;

        public string m_fogColorDay;

        public string m_fogColorEvening;

        public string m_fogColorSunNight;

        public string m_fogColorSunMorning;

        public string m_fogColorSunDay;

        public string m_fogColorSunEvening;

        public float m_fogDensityNight;

        public float m_fogDensityMorning;

        public float m_fogDensityDay;

        public float m_fogDensityEvening;

        public string m_sunColorNight;

        public string m_sunColorMorning;

        public string m_sunColorDay;

        public string m_sunColorEvening;

        public float m_lightIntensityDay;

        public float m_lightIntensityNight;

        public float m_sunAngle;

        public float m_windMin;

        public float m_windMax;

        public string m_envObject;

        public string m_psystems;

        public bool m_psystemsOutsideOnly;

        public float m_rainCloudAlpha;

        public string m_ambientLoop;

        public float m_ambientVol;

        public string m_ambientList;

        public string m_musicMorning;

        public string m_musicEvening;

        public string m_musicDay;

        public string m_musicNight;

        public SeasonEnvironment()
        {

        }

        public SeasonEnvironment(EnvSetup env)
        {
            foreach (FieldInfo property in env.GetType().GetFields())
            {
                FieldInfo field = GetType().GetField(property.Name);
                if (field == null)
                    continue;

                switch (property.Name)
                {
                    case "m_envObject":
                        {
                            if (env.m_envObject != null)
                                m_envObject = env.m_envObject.name;
                            continue;
                        }
                    case "m_psystems":
                        {
                            if (env.m_psystems != null)
                                m_psystems = env.m_psystems.Select(ps => ps.name).Join(null, ",");
                            continue;
                        }
                    case "m_ambientLoop":
                        {
                            if (env.m_ambientLoop != null)
                                m_ambientLoop = env.m_ambientLoop.name;
                            continue;
                        }
                }
                field.SetValue(this, property.FieldType == typeof(Color) ? $"#{ColorUtility.ToHtmlStringRGBA((Color)property.GetValue(env))}" : property.GetValue(env));
            }
        }

        public EnvSetup ToEnvSetup()
        {
            EnvSetup original = EnvMan.instance.m_environments.Find(e => e.m_name == m_cloneFrom) ?? EnvMan.instance.m_environments[0];

            SeasonEnvironment defaultSettings = new SeasonEnvironment();

            EnvSetup env = original.Clone();

            foreach (FieldInfo property in env.GetType().GetFields())
            {
                FieldInfo field = GetType().GetField(property.Name);
                if (field == null)
                    continue;

                if ((field.GetValue(this) == null) || (field.FieldType != typeof(bool) && field.GetValue(this).Equals(field.GetValue(defaultSettings))))
                    continue;

                switch (property.Name)
                {
                    case "m_envObject":
                        {
                            env.m_envObject = usedObjects.GetValueSafe(m_envObject);
                            continue;
                        }
                    case "m_psystems":
                        {
                            env.m_psystems = m_psystems.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Where(ps => usedObjects.ContainsKey(ps)).Select(ps => usedObjects.GetValueSafe(ps)).ToArray();
                            continue;
                        }
                    case "m_ambientLoop":
                        {
                            env.m_ambientLoop = usedAudioClips.GetValueSafe(m_ambientLoop);
                            continue;
                        }
                }

                property.SetValue(env, property.FieldType == typeof(Color) && ColorUtility.TryParseHtmlString(field.GetValue(this).ToString(), out Color color) ? color : field.GetValue(this));
            }

            return env;
        }

        public static List<SeasonEnvironment> GetDefaultCustomEnvironments()
        {
            return new List<SeasonEnvironment>()
            {
                new SeasonEnvironment
                {
                    m_name = "Clear Winter",
                    m_cloneFrom = "Clear",
                    m_isCold = true,
                    m_isColdAtNight = true
                },
                new SeasonEnvironment
                {
                    m_name = "Clear Summer",
                    m_cloneFrom = "Clear",
                    m_isCold = false,
                    m_isColdAtNight = false
                },
                new SeasonEnvironment
                {
                    m_name = "Misty Winter",
                    m_cloneFrom = "Misty",
                    m_isCold = true,
                    m_isColdAtNight = true
                },
                new SeasonEnvironment
                {
                    m_name = "Misty Summer",
                    m_cloneFrom = "Misty",
                    m_isCold = false,
                    m_isColdAtNight = false
                },
                new SeasonEnvironment
                {
                    m_name = "DeepForest Mist Winter",
                    m_cloneFrom = "DeepForest Mist",
                    m_isCold = true,
                    m_isColdAtNight = true
                },
                new SeasonEnvironment
                {
                    m_name = "DeepForest Mist Summer",
                    m_cloneFrom = "DeepForest Mist",
                    m_isCold = false,
                    m_isColdAtNight = false
                },
                new SeasonEnvironment
                {
                    m_name = "Rain Winter",
                    m_cloneFrom = "Rain",
                    m_isWet = true,
                    m_isFreezingAtNight = true,
                    m_isCold = true,
                    m_isColdAtNight = true,
                    m_alwaysDark = true,
                    m_psystems = "GroundMist,Snow,FogClouds,OceanMist",
                    m_ambientLoop = "Wind_ColdLoop3",
                },
                new SeasonEnvironment
                {
                    m_name = "LightRain Winter",
                    m_cloneFrom = "LightRain",
                    m_isWet = true,
                    m_isCold = true,
                    m_isColdAtNight = true,
                    m_alwaysDark = true,
                    m_psystems = "GroundMist,Snow,FogClouds",
                    m_ambientLoop = "SW008_Wendland_Autumn_Wind_In_Reeds_Medium_Distance_Leaves_Only",
                },
                new SeasonEnvironment
                {
                    m_name = "ThunderStorm Winter",
                    m_cloneFrom = "ThunderStorm",
                    m_isWet = true,
                    m_isCold = true,
                    m_isFreezing = true,
                    m_isFreezingAtNight = true,
                    m_isColdAtNight = true,
                    m_alwaysDark = true,
                    m_psystems = "SnowStorm,GroundMist,FogClouds,OceanMist",
                    m_ambientLoop = "Wind_BlowingLoop3",
                },
                new SeasonEnvironment
                {
                    m_name = "ThunderStorm Fall",
                    m_cloneFrom = "ThunderStorm",
                    m_isWet = true,
                    m_isCold = true,
                    m_isColdAtNight = true,
                    m_alwaysDark = true,
                },
                new SeasonEnvironment
                {
                    m_name = "SwampRain Winter",
                    m_cloneFrom = "SwampRain",
                    m_isWet = true,
                    m_isCold = true,
                    m_isColdAtNight = true,
                    m_alwaysDark = true,
                    m_psystems = "Snow,GroundMist",
                    m_ambientLoop = "SW008_Wendland_Autumn_Wind_In_Reeds_Medium_Distance_Leaves_Only",
                },
                new SeasonEnvironment
                {
                    m_name = "Mistlands_clear Winter",
                    m_cloneFrom = "Mistlands_clear",
                    m_isCold = true,
                    m_isColdAtNight = true,
                },
                new SeasonEnvironment
                {
                    m_name = "Mistlands_clear Summer",
                    m_cloneFrom = "Mistlands_clear",
                    m_isCold = false,
                    m_isColdAtNight = false,
                },
                new SeasonEnvironment
                {
                    m_name = "Mistlands_rain Winter",
                    m_cloneFrom = "Mistlands_rain",
                    m_isWet = true,
                    m_isCold = true,
                    m_isColdAtNight = true,
                    m_isFreezingAtNight = true,
                    m_alwaysDark = true,
                    m_psystems = "Snow,GroundMist",
                    m_ambientLoop = "Wind_ColdLoop3",
                },
                new SeasonEnvironment
                {
                    m_name = "Mistlands_thunder Winter",
                    m_cloneFrom = "Mistlands_thunder",
                    m_isWet = true,
                    m_isCold = true,
                    m_isColdAtNight = true,
                    m_isFreezing = true,
                    m_isFreezingAtNight = true,
                    m_alwaysDark = true,
                    m_psystems = "SnowStorm,MistlandsThunder,GroundMist",
                    m_ambientLoop = "Wind_BlowingLoop3",
                },
                new SeasonEnvironment
                {
                    m_name = "Darklands_dark Winter",
                    m_cloneFrom = "Darklands_dark",
                    m_isCold = true,
                    m_isColdAtNight = true,
                    m_isFreezingAtNight = true,
                    m_alwaysDark = false,
                    m_psystems = "Snow,Darklands,GroundMist",
                    m_ambientLoop = "Wind_ColdLoop3",
                },
                new SeasonEnvironment
                {
                    m_name = "Heath clear Winter",
                    m_cloneFrom = "Heath clear",
                    m_isCold = true,
                    m_isColdAtNight = true
                },
                new SeasonEnvironment
                {
                    m_name = "Heath clear Summer",
                    m_cloneFrom = "Heath clear",
                    m_isColdAtNight = false
                },
                new SeasonEnvironment
                {
                    m_name = "Swamp Summer",
                    m_cloneFrom = "Darklands_dark",
                    m_isCold = false,
                    m_isColdAtNight = false,
                    m_alwaysDark = true,
                    m_psystems = "LightRain,GroundMist",
                    m_ambientLoop = "SW008_Wendland_Autumn_Wind_In_Reeds_Medium_Distance_Leaves_Only",
                },
                new SeasonEnvironment
                {
                    m_name = "SwampRain Summer",
                    m_cloneFrom = "SwampRain",
                    m_isWet = false,
                    m_isColdAtNight = false,
                    m_alwaysDark = true,
                    m_psystems = "GroundMist",
                    m_ambientLoop = "SW008_Wendland_Autumn_Wind_In_Reeds_Medium_Distance_Leaves_Only",
                }
            };
        }

        public static Dictionary<string, GameObject> usedObjects
        {
            get
            {
                if (_usedObjects.Count > 0 || EnvMan.instance == null)
                    return _usedObjects;

                foreach (EnvSetup env in EnvMan.instance.m_environments)
                {
                    if (env.m_envObject != null && !_usedObjects.ContainsKey(env.m_envObject.name))
                        _usedObjects.Add(env.m_envObject.name, env.m_envObject);

                    env.m_psystems?.Where(ps => !_usedObjects.ContainsKey(ps.name)).Do(ps => _usedObjects.Add(ps.name, ps));
                }

                return _usedObjects;
            }
        }

        private static Dictionary<string, GameObject> _usedObjects = new Dictionary<string, GameObject>();

        public static Dictionary<string, AudioClip> usedAudioClips
        {
            get
            {
                if (_usedAudioClips.Count > 0 || EnvMan.instance == null)
                    return _usedAudioClips;

                foreach (EnvSetup env in EnvMan.instance.m_environments)
                    if (env.m_ambientLoop != null && !_usedAudioClips.ContainsKey(env.m_ambientLoop.name))
                        _usedAudioClips.Add(env.m_ambientLoop.name, env.m_ambientLoop);

                return _usedAudioClips;
            }
        }

        private static Dictionary<string, AudioClip> _usedAudioClips = new Dictionary<string, AudioClip>();
    }

    [Serializable]
    public class SeasonBiomeEnvironments
    {
        public SeasonBiomeEnvironment Spring = new SeasonBiomeEnvironment();
        public SeasonBiomeEnvironment Summer = new SeasonBiomeEnvironment();
        public SeasonBiomeEnvironment Fall = new SeasonBiomeEnvironment();
        public SeasonBiomeEnvironment Winter = new SeasonBiomeEnvironment();

        public SeasonBiomeEnvironments()
        {
            Summer.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Clear", "Clear Summer"));
            Summer.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Misty", "Misty Summer"));
            Summer.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("DeepForest Mist", "DeepForest Mist Summer"));
            Summer.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Mistlands_clear", "Mistlands_clear Summer"));
            Summer.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Heath clear", "Heath clear Summer"));

            Summer.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Meadows", new EnvEntry { m_environment = "Heath clear", m_weight = 2.0f }));

            Summer.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Black forest", new EnvEntry { m_environment = "Light Rain", m_weight = 0.1f }));
            Summer.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Black forest", new EnvEntry { m_environment = "Clear", m_weight = 0.2f }));

            Summer.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Swamp", new EnvEntry { m_environment = "SwampRain Summer", m_weight = 0.1f }));
            Summer.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Swamp", new EnvEntry { m_environment = "Swamp Summer", m_weight = 0.1f }));

            Summer.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Mountain", new EnvEntry { m_environment = "Twilight_Clear", m_weight = 1.0f }));
            Summer.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Mountain", new EnvEntry { m_environment = "Twilight_Snow", m_weight = 1.0f }));

            Summer.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Plains", new EnvEntry { m_environment = "ThunderStorm", m_weight = 0.1f }));

            Summer.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Ocean", new EnvEntry { m_environment = "Heath clear", m_weight = 1.0f }));

            Summer.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Mistlands", new EnvEntry { m_environment = "Heath clear", m_weight = 0.5f }));

            Fall.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("ThunderStorm", "ThunderStorm Fall"));

            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Meadows", new EnvEntry { m_environment = "DeepForest Mist", m_weight = 0.2f }));
            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Meadows", new EnvEntry { m_environment = "SwampRain", m_weight = 0.2f }));

            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Black forest", new EnvEntry { m_environment = "LightRain", m_weight = 0.1f }));
            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Black forest", new EnvEntry { m_environment = "SwampRain", m_weight = 0.1f }));

            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Swamp", new EnvEntry { m_environment = "ThunderStorm", m_weight = 0.1f }));

            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Mountain", new EnvEntry { m_environment = "Twilight_SnowStorm", m_weight = 0.5f }));

            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Plains", new EnvEntry { m_environment = "Rain", m_weight = 0.4f }));
            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Plains", new EnvEntry { m_environment = "ThunderStorm", m_weight = 0.2f }));
            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Plains", new EnvEntry { m_environment = "SwampRain", m_weight = 0.1f }));

            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Ocean", new EnvEntry { m_environment = "SwampRain", m_weight = 0.1f }));
            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Ocean", new EnvEntry { m_environment = "DeepForest Mist", m_weight = 0.1f }));

            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Mistlands", new EnvEntry { m_environment = "SwampRain", m_weight = 0.1f }));
            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Mistlands", new EnvEntry { m_environment = "DeepForest Mist", m_weight = 0.1f }));

            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Rain", "Rain Winter"));
            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("LightRain", "LightRain Winter"));
            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("ThunderStorm", "ThunderStorm Winter"));
            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Clear", "Clear Winter"));
            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Misty", "Misty Winter"));
            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("DeepForest Mist", "DeepForest Mist Winter"));
            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("SwampRain", "SwampRain Winter"));
            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Mistlands_clear", "Mistlands_clear Winter"));
            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Mistlands_rain", "Mistlands_rain Winter"));
            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Mistlands_thunder", "Mistlands_thunder Winter"));
            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Heath clear", "Heath clear Winter"));

            Winter.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Mountain", new EnvEntry { m_environment = "Twilight_SnowStorm", m_weight = 1.0f }));
            Winter.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Plains", new EnvEntry { m_environment = "Snow", m_weight = 0.5f }));
            Winter.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Ocean", new EnvEntry { m_environment = "Darklands_dark Winter", m_weight = 0.1f }));
            Winter.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Mistlands", new EnvEntry { m_environment = "Twilight_Snow", m_weight = 0.1f }));
            Winter.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Mistlands", new EnvEntry { m_environment = "Twilight_SnowStorm", m_weight = 0.1f }));
        }
    }

    public class SeasonSettings
    {
        public const string defaultsSubdirectory = "Default settings";
        public const string customEnvironmentsFileName = "Custom environments.json";
        public const string customBiomeEnvironmentsFileName = "Custom Biome Environments.json";
        public const int nightLentghDefault = 30;

        public int m_daysInSeason = 10;
        public int m_nightLength = nightLentghDefault;
        public bool m_torchAsFiresource = false;
        public float m_torchDurabilityDrain = 0.5f; 

        public SeasonSettings(Season season)
        {
            LoadDefaultSeasonSettings(season);
        }

        public SeasonSettings(Season season, SeasonSettingsFile settings)
        {
            LoadDefaultSeasonSettings(season);

            SeasonSettingsFile defaultFileSettings = new SeasonSettingsFile();

            foreach (FieldInfo property in settings.GetType().GetFields())
            {
                FieldInfo field = GetType().GetField($"m_{property.Name}");
                if (field == null || (field.FieldType != typeof(bool) && property.GetValue(settings).Equals(property.GetValue(defaultFileSettings))))
                    continue;

                field.SetValue(this, property.GetValue(settings));
            }
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
                        m_torchAsFiresource = true;
                        break;
                    }
                case Season.Winter:
                    {
                        m_nightLength = 40;
                        m_torchAsFiresource = true;
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
                if (GetSeasonByFilename(file.Name, out Season season))
                {
                    try
                    {
                        localConfig.Add((int)season, File.ReadAllText(file.FullName));
                    }
                    catch (Exception e)
                    {
                        LogWarning($"Error reading file ({file.FullName})! Error: {e.Message}");
                    }
                };

                if (file.Name == customEnvironmentsFileName)
                    try
                    {
                        customEnvironmentsJSON.AssignLocalValue(File.ReadAllText(file.FullName));
                    }
                    catch (Exception e)
                    {
                        LogWarning($"Error reading file ({file.FullName})! Error: {e.Message}");
                    }


                if (file.Name == customBiomeEnvironmentsFileName)
                    try
                    {
                        customBiomeEnvironmentsJSON.AssignLocalValue(File.ReadAllText(file.FullName));
                    }
                    catch (Exception e)
                    {
                        LogWarning($"Error reading file ({file.FullName})! Error: {e.Message}");
                    }
            };

            seasonsSettingsJSON.AssignLocalValue(localConfig);
        }

        public static void UpdateSeasonSettings()
        {
            seasonState.UpdateSeasonSettings();
            seasonState.UpdateSeasonEnvironments();
            seasonState.UpdateBiomeEnvironments();
        }

        public static void SaveDefaultEnvironments(string folder)
        {
            List<SeasonEnvironment> list = new List<SeasonEnvironment>();
            EnvMan.instance.m_environments.Do(env => list.Add(new SeasonEnvironment(env)));

            LogInfo($"Saving default environments settings");
            File.WriteAllText(Path.Combine(folder, "Environments.json"), JsonConvert.SerializeObject(list, Formatting.Indented));

            JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
            {
                DefaultValueHandling = DefaultValueHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore,
            };

            LogInfo($"Saving default custom environments settings");
            File.WriteAllText(Path.Combine(folder, customEnvironmentsFileName), JsonConvert.SerializeObject(SeasonEnvironment.GetDefaultCustomEnvironments(), Formatting.Indented, jsonSerializerSettings));

            if (biomesDefault.Count == 0)
                biomesDefault.AddRange(EnvMan.instance.m_biomes.ToList());

            LogInfo($"Saving default biome environments settings");
            File.WriteAllText(Path.Combine(folder, "Default Biome Environments.json"), JsonConvert.SerializeObject(biomesDefault, Formatting.Indented));

            LogInfo($"Saving default custom biome environments settings");
            File.WriteAllText(Path.Combine(folder, customBiomeEnvironmentsFileName), JsonConvert.SerializeObject(new SeasonBiomeEnvironments(), Formatting.Indented));
        }

    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
    public static class ZoneSystem_Start_SeasonSettingsConfigWatcher
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            seasonState = new SeasonState(initialize: true);
            SeasonSettings.SetupConfigWatcher();
        }
    }

}
