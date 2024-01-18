using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    [Serializable]
    public class SeasonSettingsFile
    {
        public int daysInSeason;
        public int nightLength;
        public bool torchAsFiresource;
        public float torchDurabilityDrain;
        public float plantsGrowthMultiplier;
        public float beehiveProductionMultiplier;
        public float foodDrainMultiplier;
        public float staminaDrainMultiplier;
        public float fireplaceDrainMultiplier;
        public float sapCollectingSpeedMultiplier;
        public bool rainProtection;
        public float woodFromTreesMultiplier;
        public float windIntensityMultiplier;
        public float restedBuffDurationMultiplier;
        public float livestockProcreationMultiplier;
        public bool overheatIn2WarmClothes;
        public float meatFromAnimalsMultiplier;

        public SeasonSettingsFile(SeasonSettings settings)
        {
            daysInSeason = settings.m_daysInSeason;
            nightLength = settings.m_nightLength;
            torchAsFiresource = settings.m_torchAsFiresource;
            torchDurabilityDrain = settings.m_torchDurabilityDrain;
            plantsGrowthMultiplier = settings.m_plantsGrowthMultiplier;
            beehiveProductionMultiplier = settings.m_beehiveProductionMultiplier;
            foodDrainMultiplier = settings.m_foodDrainMultiplier;
            staminaDrainMultiplier = settings.m_staminaDrainMultiplier;
            fireplaceDrainMultiplier = settings.m_fireplaceDrainMultiplier;
            sapCollectingSpeedMultiplier = settings.m_sapCollectingSpeedMultiplier;
            rainProtection = settings.m_rainProtection;
            woodFromTreesMultiplier = settings.m_woodFromTreesMultiplier;
            windIntensityMultiplier = settings.m_windIntensityMultiplier;
            restedBuffDurationMultiplier = settings.m_restedBuffDurationMultiplier;
            livestockProcreationMultiplier = settings.m_livestockProcreationMultiplier;
            overheatIn2WarmClothes = settings.m_overheatIn2WarmClothes;
            meatFromAnimalsMultiplier = settings.m_meatFromAnimalsMultiplier;
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

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
        public bool m_isWet;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
        public bool m_isFreezing;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
        public bool m_isFreezingAtNight;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
        public bool m_isCold;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
        public bool m_isColdAtNight;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
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
                    m_psystems = "SnowStorm",
                    m_ambientLoop = "Wind_BlowingLoop3",
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
                    m_ambientLoop = "Wind_ColdLoop3",
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
                    m_psystems = "SnowStorm",
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
                    m_ambientLoop = "Wind_ColdLoop3",
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
                    m_psystems = "SnowStorm,MistlandsThunder",
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

        private static readonly Dictionary<string, GameObject> _usedObjects = new Dictionary<string, GameObject>();

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

        private static readonly Dictionary<string, AudioClip> _usedAudioClips = new Dictionary<string, AudioClip>();
        
        public static void ClearCachedObjects()
        {
            _usedObjects.Clear();
            _usedAudioClips.Clear();
        }
    }

    [Serializable]
    public class SeasonBiomeEnvironments
    {
        public SeasonBiomeEnvironment Spring = new SeasonBiomeEnvironment();
        public SeasonBiomeEnvironment Summer = new SeasonBiomeEnvironment();
        public SeasonBiomeEnvironment Fall = new SeasonBiomeEnvironment();
        public SeasonBiomeEnvironment Winter = new SeasonBiomeEnvironment();

        public SeasonBiomeEnvironments(bool loadDefaults = false)
        {
            if (!loadDefaults)
                return;

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

        public SeasonBiomeEnvironment GetSeasonBiomeEnvironment(Season season)
        {
            return season switch
            {
                Season.Spring => Spring,
                Season.Summer => Summer,
                Season.Fall => Fall,
                Season.Winter => Winter,
                _ => new SeasonBiomeEnvironment(),
            };
        }
    }

    [Serializable]
    public class SeasonRandomEvent
    {
        public string m_name;
        public string m_biomes;
        public int m_weight;

        public SeasonRandomEvent()
        {

        }

        public SeasonRandomEvent(RandomEvent randomEvent)
        {
            m_name = randomEvent.m_name;
            m_weight = 1;
            m_biomes = randomEvent.m_biome.ToString();
        }

        public Heightmap.Biome GetBiome()
        {
            return (Heightmap.Biome)Enum.Parse(typeof(Heightmap.Biome), m_biomes);
        }
    }

    [Serializable]
    public class SeasonRandomEvents
    {
        public List<SeasonRandomEvent> Spring = new List<SeasonRandomEvent>();
        public List<SeasonRandomEvent> Summer = new List<SeasonRandomEvent>();
        public List<SeasonRandomEvent> Fall = new List<SeasonRandomEvent>();
        public List<SeasonRandomEvent> Winter = new List<SeasonRandomEvent>();

        public SeasonRandomEvents(bool loadDefaults = false)
        {
            if (!loadDefaults)
                return;

            Spring.Add(new SeasonRandomEvent() 
            {
                m_name = "foresttrolls",
                m_weight = 2
            });
            Spring.Add(new SeasonRandomEvent()
            {
                m_name = "bats",
                m_weight = 0
            });
            Spring.Add(new SeasonRandomEvent()
            {
                m_name = "army_eikthyr",
                m_weight = 2
            });
            Spring.Add(new SeasonRandomEvent()
            {
                m_name = "army_theelder",
                m_weight = 2
            });

            Summer.Add(new SeasonRandomEvent()
            {
                m_name = "bats",
                m_weight = 2
            });
            Summer.Add(new SeasonRandomEvent()
            {
                m_name = "surtlings",
                m_weight = 2
            });
            Summer.Add(new SeasonRandomEvent()
            {
                m_name = "wolves",
                m_weight = 0
            });
            Summer.Add(new SeasonRandomEvent()
            {
                m_name = "army_goblin",
                m_weight = 2
            });

            Fall.Add(new SeasonRandomEvent()
            {
                m_name = "skeletons",
                m_weight = 2
            });
            Fall.Add(new SeasonRandomEvent()
            {
                m_name = "blobs",
                m_weight = 2
            });
            Fall.Add(new SeasonRandomEvent()
            {
                m_name = "army_bonemass",
                m_weight = 2
            });

            Winter.Add(new SeasonRandomEvent()
            {
                m_name = "wolves",
                m_biomes = "Meadows, Swamp, Mountain, BlackForest, Plains, DeepNorth",
                m_weight = 2
            });
            Winter.Add(new SeasonRandomEvent()
            {
                m_name = "army_moder",
                m_weight = 2
            });
            Winter.Add(new SeasonRandomEvent()
            {
                m_name = "skeletons",
                m_weight = 0
            });
            Winter.Add(new SeasonRandomEvent()
            {
                m_name = "foresttrolls",
                m_weight = 0
            });
            Winter.Add(new SeasonRandomEvent()
            {
                m_name = "surtlings",
                m_weight = 0
            });
            Winter.Add(new SeasonRandomEvent()
            {
                m_name = "blobs",
                m_weight = 0
            });
        }

        public List<SeasonRandomEvent> GetSeasonEvents(Season season)
        {
            return season switch
            {
                Season.Spring => Spring,
                Season.Summer => Summer,
                Season.Fall => Fall,
                Season.Winter => Winter,
                _ => new List<SeasonRandomEvent>(),
            };
        }
    }

    [Serializable]
    public class SeasonLightings
    {
        [Serializable]
        public class LightingSettings
        {
            public float luminanceMultiplier = 1.0f;
            public float fogDensityMultiplier = 1.0f;
        }

        [Serializable]
        public class SeasonLightingSettings
        {
            public LightingSettings indoors = new LightingSettings();
            public LightingSettings morning = new LightingSettings();
            public LightingSettings day = new LightingSettings();
            public LightingSettings evening = new LightingSettings();
            public LightingSettings night = new LightingSettings();

            public float lightIntensityDayMultiplier = 1.0f;
            public float lightIntensityNightMultiplier = 1.0f;
        }

        public SeasonLightingSettings Spring = new SeasonLightingSettings();
        public SeasonLightingSettings Summer = new SeasonLightingSettings();
        public SeasonLightingSettings Fall = new SeasonLightingSettings();
        public SeasonLightingSettings Winter = new SeasonLightingSettings();

        public SeasonLightings(bool loadDefaults = false)
        {
            if (!loadDefaults)
                return;

            Summer.indoors.fogDensityMultiplier = 0.9f;

            Summer.morning.luminanceMultiplier = 1.1f;
            Summer.morning.fogDensityMultiplier = 0.9f;

            Summer.evening.luminanceMultiplier = 1.1f;
            Summer.evening.fogDensityMultiplier = 0.9f;
            
            Summer.night.luminanceMultiplier = 1.1f;
            Summer.night.fogDensityMultiplier = 0.9f;

            Summer.lightIntensityNightMultiplier = 0.9f;

            Fall.morning.luminanceMultiplier = 0.95f;
            Fall.morning.fogDensityMultiplier = 1.1f;

            Fall.evening.luminanceMultiplier = 0.95f;
            Fall.evening.fogDensityMultiplier = 1.1f;

            Fall.night.luminanceMultiplier = 0.9f;
            Fall.night.fogDensityMultiplier = 1.3f;

            Fall.lightIntensityNightMultiplier = 1.2f;

            Winter.indoors.luminanceMultiplier = 0.9f;
            Winter.indoors.fogDensityMultiplier = 1.1f;

            Winter.morning.luminanceMultiplier = 0.9f;
            Winter.morning.fogDensityMultiplier = 1.2f;

            Winter.evening.luminanceMultiplier = 0.9f;
            Winter.evening.fogDensityMultiplier = 1.2f;

            Winter.night.luminanceMultiplier = 0.8f;
            Winter.night.fogDensityMultiplier = 1.7f;

            Winter.lightIntensityNightMultiplier = 1.5f;
        }

        public SeasonLightingSettings GetSeasonLighting(Season season)
        {
            return season switch
            {
                Season.Spring => Spring,
                Season.Summer => Summer,
                Season.Fall => Fall,
                Season.Winter => Winter,
                _ => new SeasonLightingSettings(),
            };
        }
    }

    [Serializable]
    public class SeasonStats
    {
        [Serializable]
        public class Stats
        {
            [Header("__SE_Stats__")]
            [Header("HP per tick")]
            public float m_tickInterval;

            public float m_healthPerTickMinHealthPercentage;

            public float m_healthPerTick;

            public string m_healthHitType = "";
            
            [Header("Stamina")]
            public float m_staminaDrainPerSec;

            public float m_runStaminaDrainModifier;

            public float m_jumpStaminaUseModifier;

            [Header("Regen modifiers")]
            public float m_healthRegenMultiplier = 1f;

            public float m_staminaRegenMultiplier = 1f;

            public float m_eitrRegenMultiplier = 1f;

            [Header("Skills modifiers")]
            public Dictionary<Skills.SkillType, float> m_raiseSkills = new Dictionary<Skills.SkillType, float>();
            public Dictionary<Skills.SkillType, float> m_skillLevels = new Dictionary<Skills.SkillType, float>();
            public Dictionary<Skills.SkillType, float> m_modifyAttackSkills = new Dictionary<Skills.SkillType, float>();

            [Header("Hit modifier")]
            public Dictionary<string, string> m_damageModifiers = new Dictionary<string, string>();

            [Header("Sneak")]
            public float m_noiseModifier;

            public float m_stealthModifier;

            [Header("Carry weight")]
            public float m_addMaxCarryWeight;

            [Header("Speed")]
            public float m_speedModifier;

            [Header("Fall")]
            public float m_maxMaxFallSpeed;

            public float m_fallDamageModifier;

            public void SetStatusEffectStats(SE_Season statusEffect)
            {
                foreach (FieldInfo property in GetType().GetFields())
                {
                    FieldInfo field = statusEffect.GetType().GetField(property.Name);
                    if (field == null)
                        continue;
                        
                    field.SetValue(statusEffect, property.GetValue(this));
                }

                statusEffect.m_mods.Clear();
                foreach (KeyValuePair<string, string> damageMod in m_damageModifiers)
                    if (Enum.TryParse(damageMod.Key, out HitData.DamageType m_type) && Enum.TryParse(damageMod.Value, out HitData.DamageModifier m_modifier))
                        statusEffect.m_mods.Add(new HitData.DamageModPair() { m_type = m_type, m_modifier = m_modifier });

                statusEffect.m_hitType = HitData.HitType.Undefined;
                if (Enum.TryParse(m_healthHitType, out HitData.HitType hitType))
                    statusEffect.m_hitType = hitType;
            }
        }

        public Stats Spring = new Stats();
        public Stats Summer = new Stats();
        public Stats Fall = new Stats();
        public Stats Winter = new Stats();

        public SeasonStats(bool loadDefaults = false)
        {
            if (!loadDefaults)
                return;

            Spring.m_tickInterval = 5f;
            Spring.m_healthPerTick = 1f;
            Spring.m_damageModifiers.Add(HitData.DamageType.Poison.ToString(), HitData.DamageModifier.Resistant.ToString());

            Spring.m_raiseSkills.Add(Skills.SkillType.Jump, 1.2f);
            Spring.m_raiseSkills.Add(Skills.SkillType.Sneak, 1.2f);
            Spring.m_raiseSkills.Add(Skills.SkillType.Run, 1.2f);
            Spring.m_raiseSkills.Add(Skills.SkillType.Swim, 1.2f);
            Spring.m_skillLevels.Add(Skills.SkillType.Jump, 15f);
            Spring.m_skillLevels.Add(Skills.SkillType.Sneak, 15f);
            Spring.m_skillLevels.Add(Skills.SkillType.Run, 15f);
            Spring.m_skillLevels.Add(Skills.SkillType.Swim, 15f);

            Summer.m_runStaminaDrainModifier = -0.1f;
            Summer.m_jumpStaminaUseModifier = -0.1f;
            Summer.m_healthRegenMultiplier = 1.1f;
            Summer.m_noiseModifier = -0.2f;
            Summer.m_stealthModifier = 0.2f;
            Summer.m_speedModifier = 0.05f;

            Summer.m_raiseSkills.Add(Skills.SkillType.Jump, 1.1f);
            Summer.m_raiseSkills.Add(Skills.SkillType.Sneak, 1.1f);
            Summer.m_raiseSkills.Add(Skills.SkillType.Run, 1.1f);
            Summer.m_raiseSkills.Add(Skills.SkillType.Swim, 1.1f);
            Summer.m_skillLevels.Add(Skills.SkillType.Jump, 10f);
            Summer.m_skillLevels.Add(Skills.SkillType.Sneak, 10f);
            Summer.m_skillLevels.Add(Skills.SkillType.Run, 10f);
            Summer.m_skillLevels.Add(Skills.SkillType.Swim, 10f);

            Fall.m_eitrRegenMultiplier = 1.1f;
            Fall.m_raiseSkills.Add(Skills.SkillType.WoodCutting, 1.2f);
            Fall.m_raiseSkills.Add(Skills.SkillType.Fishing, 1.2f);
            Fall.m_raiseSkills.Add(Skills.SkillType.Pickaxes, 1.2f);
            Fall.m_skillLevels.Add(Skills.SkillType.WoodCutting, 15f);
            Fall.m_skillLevels.Add(Skills.SkillType.Fishing, 15f);
            Fall.m_skillLevels.Add(Skills.SkillType.Pickaxes, 15f);

            Winter.m_staminaRegenMultiplier = 1.1f;
            Winter.m_noiseModifier = 0.2f;
            Winter.m_stealthModifier = -0.2f;
            Winter.m_speedModifier = -0.05f;
            Winter.m_fallDamageModifier = -0.3f;

            Winter.m_damageModifiers.Add(HitData.DamageType.Fire.ToString(), HitData.DamageModifier.Resistant.ToString());

            Winter.m_raiseSkills.Add(Skills.SkillType.WoodCutting, 1.1f);
            Winter.m_raiseSkills.Add(Skills.SkillType.Fishing, 1.1f);
            Winter.m_raiseSkills.Add(Skills.SkillType.Pickaxes, 1.1f);
            Winter.m_skillLevels.Add(Skills.SkillType.WoodCutting, 10f);
            Winter.m_skillLevels.Add(Skills.SkillType.Fishing, 10f);
            Winter.m_skillLevels.Add(Skills.SkillType.Pickaxes, 10f);
        }

        public Stats GetSeasonStats()
        {
            return GetSeasonStats(seasonState.GetCurrentSeason());
        }

        private Stats GetSeasonStats(Season season)
        {
            return season switch
            {
                Season.Spring => Spring,
                Season.Summer => Summer,
                Season.Fall => Fall,
                Season.Winter => Winter,
                _ => new Stats(),
            };
        }
    }

    [Serializable]
    public class SeasonTraderItems 
    {
        [Serializable]
        public class TradeableItem
        {
            public string prefab;
            public int stack = 1;
            public int price = 1;
            public string requiredGlobalKey = "";

            public override string ToString()
            {
                return $"{prefab}x{stack}, {price} coins {(!requiredGlobalKey.IsNullOrWhiteSpace() ? $", {requiredGlobalKey}" : "")}";
            }
        }

        public Dictionary<string, List<TradeableItem>> Spring = new Dictionary<string, List<TradeableItem>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<TradeableItem>> Summer = new Dictionary<string, List<TradeableItem>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<TradeableItem>> Fall = new Dictionary<string, List<TradeableItem>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<TradeableItem>> Winter = new Dictionary<string, List<TradeableItem>>(StringComparer.OrdinalIgnoreCase);

        public SeasonTraderItems(bool loadDefaults = false)
        {
            if (!loadDefaults)
                return;

            Spring.Add("haldor", new List<TradeableItem>
                             {
                                 new TradeableItem { prefab = "Honey", price = 200, stack = 10, requiredGlobalKey = "defeated_eikthyr" },
                                 new TradeableItem { prefab = "RawMeat", price = 150, stack = 10, requiredGlobalKey = "defeated_eikthyr" },
                                 new TradeableItem { prefab = "NeckTail", price = 150, stack = 10, requiredGlobalKey = "defeated_eikthyr" },
                                 new TradeableItem { prefab = "DeerMeat", price = 200, stack = 10, requiredGlobalKey = "defeated_gdking" },
                                 new TradeableItem { prefab = "WolfMeat", price = 350, stack = 10, requiredGlobalKey = "defeated_dragon" },
                                 new TradeableItem { prefab = "LoxMeat", price = 500, stack = 5, requiredGlobalKey = "defeated_goblinking" },
                                 new TradeableItem { prefab = "HareMeat", price = 500, stack = 10, requiredGlobalKey = "defeated_queen" },
                                 new TradeableItem { prefab = "SerpentMeat", price = 500, stack = 5, requiredGlobalKey = "defeated_bonemass" }
                             });

            Fall.Add("haldor", new List<TradeableItem>
                             {
                                 new TradeableItem { prefab = "Raspberry", price = 150, stack = 10, requiredGlobalKey = "defeated_eikthyr" },
                                 new TradeableItem { prefab = "Blueberries", price = 200, stack = 10, requiredGlobalKey = "defeated_eikthyr" },
                                 new TradeableItem { prefab = "Carrot", price = 300, stack = 10, requiredGlobalKey = "defeated_gdking" },
                                 new TradeableItem { prefab = "Turnip", price = 350, stack = 10, requiredGlobalKey = "defeated_bonemass" },
                                 new TradeableItem { prefab = "Onion", price = 400, stack = 10, requiredGlobalKey = "defeated_dragon" },
                                 new TradeableItem { prefab = "Barley", price = 500, stack = 10, requiredGlobalKey = "defeated_goblinking" },
                                 new TradeableItem { prefab = "Flax", price = 500, stack = 10, requiredGlobalKey = "defeated_goblinking" },
                                 new TradeableItem { prefab = "Cloudberry", price = 300, stack = 10, requiredGlobalKey = "defeated_goblinking" },
                             });

            Winter.Add("haldor", new List<TradeableItem>
                             {
                                 new TradeableItem { prefab = "Honey", price = 300, stack = 10, requiredGlobalKey = "defeated_eikthyr" },
                                 new TradeableItem { prefab = "Acorn", price = 100, stack = 1, requiredGlobalKey = "defeated_gdking" },
                                 new TradeableItem { prefab = "BeechSeeds", price = 50, stack = 10, requiredGlobalKey = "defeated_eikthyr" },
                                 new TradeableItem { prefab = "BirchSeeds", price = 150, stack = 10, requiredGlobalKey = "defeated_gdking" },
                                 new TradeableItem { prefab = "FirCone", price = 150, stack = 10, requiredGlobalKey = "defeated_gdking" },
                                 new TradeableItem { prefab = "PineCone", price = 150, stack = 10, requiredGlobalKey = "defeated_gdking" },
                                 new TradeableItem { prefab = "CarrotSeeds", price = 50, stack = 10, requiredGlobalKey = "defeated_gdking" },
                                 new TradeableItem { prefab = "TurnipSeeds", price = 80, stack = 10, requiredGlobalKey = "defeated_bonemass" },
                                 new TradeableItem { prefab = "OnionSeeds", price = 100, stack = 10, requiredGlobalKey = "defeated_dragon" },
                                 new TradeableItem { prefab = "SerpentMeat", price = 500, stack = 5, requiredGlobalKey = "defeated_bonemass" },
                                 new TradeableItem { prefab = "SerpentScale", price = 300, stack = 5, requiredGlobalKey = "defeated_bonemass" },
                                 new TradeableItem { prefab = "Bloodbag", price = 500, stack = 10, requiredGlobalKey = "killed_surtling" },
                             });

            Summer.Add("hildir", new List<TradeableItem>
                             {
                                 new TradeableItem { prefab = "HelmetMidsummerCrown", price = 100, stack = 1},
                             });

            Fall.Add("hildir", new List<TradeableItem>
                             {
                                 new TradeableItem { prefab = "HelmetPointyHat", price = 300, stack = 1},
                             });

            Winter.Add("hildir", new List<TradeableItem>
                             {
                                 new TradeableItem { prefab = "HelmetYule", price = 100, stack = 1},
                             });
        }

        public void AddSeasonalTraderItems(Trader trader, List<Trader.TradeItem> itemList)
        {
            foreach (TradeableItem item in GetCurrentSeasonalTraderItems(trader))
            {
                if (string.IsNullOrEmpty(item.requiredGlobalKey) || ZoneSystem.instance.GetGlobalKey(item.requiredGlobalKey))
                {
                    GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(item.prefab);

                    if (itemPrefab == null)
                        continue;

                    ItemDrop prefab = itemPrefab.GetComponent<ItemDrop>();
                    if (prefab == null)
                        continue;

                    if (itemList.Exists(x => x.m_prefab == prefab))
                    {
                        Trader.TradeItem itemTrader = itemList.First(x => x.m_prefab == prefab);
                        itemTrader.m_price = item.price;
                        itemTrader.m_stack = item.stack;
                        itemTrader.m_requiredGlobalKey = item.requiredGlobalKey;
                    }
                    else
                    {
                        itemList.Add(new Trader.TradeItem
                        {
                            m_prefab = prefab,
                            m_price = item.price,
                            m_stack = item.stack,
                            m_requiredGlobalKey = item.requiredGlobalKey
                        });
                    }
                }
            }
        }

        private List<TradeableItem> GetCurrentSeasonalTraderItems(Trader trader)
        {
            List<string> traderNames = new List<string>
            {
                trader.name,
                Utils.GetPrefabName(trader.gameObject),
                trader.m_name,
                trader.m_name.ToLower().Replace("$npc_", ""),
                Localization.instance.Localize(trader.m_name),
            };

            Season season = seasonState.GetCurrentSeason();

            foreach (string traderName in traderNames)
            {
                List<TradeableItem> list = GetSeasonItems(traderName, season);
                if (list != null)
                    return list;
            }

            return new List<TradeableItem>();
        }

        private Dictionary<string, List<TradeableItem>> GetSeasonList(Season season)
        {
            return season switch
            {
                Season.Spring => Spring,
                Season.Summer => Summer,
                Season.Fall => Fall,
                Season.Winter => Winter,
                _ => new Dictionary<string, List<TradeableItem>>()
            };
        }

        private List<TradeableItem> GetSeasonItems(string trader, Season season)
        {
            Dictionary<string, List<TradeableItem>> list = GetSeasonList(season);
            if (list.ContainsKey(trader))
                return list[trader];

            return null;
        }
    }

    public class SeasonSettings
    {
        public const string defaultsSubdirectory = "Default settings";
        public const string customEnvironmentsFileName = "Custom environments.json";
        public const string customBiomeEnvironmentsFileName = "Custom Biome Environments.json";
        public const string customEventsFileName = "Custom events.json";
        public const string customLightingsFileName = "Custom lightings.json";
        public const string customStatsFileName = "Custom stats.json";
        public const string customTraderItemsFileName = "Custom trader items.json"; 
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

                if (file.Name == customEventsFileName)
                    try
                    {
                        customEventsJSON.AssignLocalValue(File.ReadAllText(file.FullName));
                    }
                    catch (Exception e)
                    {
                        LogWarning($"Error reading file ({file.FullName})! Error: {e.Message}");
                    }

                if (file.Name == customLightingsFileName)
                    try
                    {
                        customLightingsJSON.AssignLocalValue(File.ReadAllText(file.FullName));
                    }
                    catch (Exception e)
                    {
                        LogWarning($"Error reading file ({file.FullName})! Error: {e.Message}");
                    }

                if (file.Name == customStatsFileName)
                    try
                    {
                        customStatsJSON.AssignLocalValue(File.ReadAllText(file.FullName));
                    }
                    catch (Exception e)
                    {
                        LogWarning($"Error reading file ({file.FullName})! Error: {e.Message}");
                    }

                if (file.Name == customTraderItemsFileName)
                    try
                    {
                        customStatsJSON.AssignLocalValue(File.ReadAllText(file.FullName));
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
            seasonState.UpdateCurrentEnvironment();
            seasonState.UpdateRandomEvents();
            seasonState.UpdateLightings();
            seasonState.UpdateStats();
            seasonState.UpdateTraderItems();

            SeasonState.CheckSeasonChange();
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

            if (biomesDefault.Count == 0)
                biomesDefault.AddRange(EnvMan.instance.m_biomes.ToList());

            LogInfo($"Saving default biome environments settings");
            File.WriteAllText(Path.Combine(folder, "Default biome environments.json"), JsonConvert.SerializeObject(biomesDefault, Formatting.Indented));

            LogInfo($"Saving default custom biome environments settings");
            File.WriteAllText(Path.Combine(folder, customBiomeEnvironmentsFileName), JsonConvert.SerializeObject(new SeasonBiomeEnvironments(loadDefaults: true), Formatting.Indented));
        }

        public static void SaveDefaultEvents(string folder)
        {
            if (eventsDefault.Count == 0)
                eventsDefault.AddRange(RandEventSystem.instance.m_events.ToList());

            List<SeasonRandomEvent> list = new List<SeasonRandomEvent>();
            eventsDefault.DoIf(randevent => randevent.m_random, randevent => list.Add(new SeasonRandomEvent(randevent)));

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
