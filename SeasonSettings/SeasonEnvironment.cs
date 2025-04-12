using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEngine;

namespace Seasons
{
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
                            env.m_ambientLoop = usedAudioClips.GetValueSafe(m_ambientLoop) ?? CustomMusic.audioClips.GetValueSafe(m_ambientLoop);
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
                    m_name = "SwampRain Fall",
                    m_cloneFrom = "SwampRain",
                    m_isWet = true,
                    m_isCold = true,
                    m_isColdAtNight = true,
                    m_alwaysDark = false,
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
}