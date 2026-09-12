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

        public float? m_snowBuildup;

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

        public SeasonGradient m_auroraColors;

        public float? m_auroraIntensityNight;

        public float? m_auroraIntensityMorning;

        public float? m_auroraIntensityDay;

        public float? m_auroraIntensityEvening;

        public float? m_cloudOpacityNight;

        public float? m_cloudOpacityMorning;

        public float? m_cloudOpacityDay;

        public float? m_cloudOpacityEvening;

        public string m_envObject;

        public string m_psystems;

        public bool m_psystemsOutsideOnly;

        public float m_rainCloudAlpha;

        public string m_ambientOcclusionColor;

        public float? m_aoIntensityNight;

        public float? m_aoIntensityMorning;

        public float? m_aoIntensityDay;

        public float? m_aoIntensityEvening;

        public string m_ambientLoop;

        public float m_ambientVol;

        public string m_ambientList;

        public string m_musicMorning;

        public string m_musicEvening;

        public string m_musicDay;

        public string m_musicNight;

        [Serializable]
        public class SeasonGradient
        {
            public SeasonGradientColorKey[] colorKeys;

            public SeasonGradientAlphaKey[] alphaKeys;

            public string mode;

            public SeasonGradient()
            {

            }

            public SeasonGradient(Gradient gradient)
            {
                if (gradient == null)
                    return;

                colorKeys = gradient.colorKeys.Select(key => new SeasonGradientColorKey(key)).ToArray();
                alphaKeys = gradient.alphaKeys.Select(key => new SeasonGradientAlphaKey(key)).ToArray();
                mode = gradient.mode.ToString();
            }

            public bool TryCreateGradient(Gradient original, out Gradient gradient, out string error)
            {
                gradient = new Gradient();
                error = "";

                GradientColorKey[] sourceColorKeys = original?.colorKeys;
                GradientAlphaKey[] sourceAlphaKeys = original?.alphaKeys;

                GradientColorKey[] resolvedColorKeys = sourceColorKeys;
                if (colorKeys != null)
                {
                    if (colorKeys.Length < 2)
                    {
                        error = "Aurora gradient must contain at least two color keys.";
                        return false;
                    }

                    resolvedColorKeys = new GradientColorKey[colorKeys.Length];
                    for (int i = 0; i < colorKeys.Length; i++)
                    {
                        if (colorKeys[i] == null || !ColorUtility.TryParseHtmlString(colorKeys[i].color, out Color color))
                        {
                            error = $"Aurora gradient color key {i} has invalid color \"{colorKeys[i]?.color}\".";
                            return false;
                        }

                        resolvedColorKeys[i] = new GradientColorKey(color, colorKeys[i].time);
                    }
                }

                GradientAlphaKey[] resolvedAlphaKeys = sourceAlphaKeys;
                if (alphaKeys != null)
                {
                    if (alphaKeys.Length < 2)
                    {
                        error = "Aurora gradient must contain at least two alpha keys.";
                        return false;
                    }

                    resolvedAlphaKeys = new GradientAlphaKey[alphaKeys.Length];
                    for (int i = 0; i < alphaKeys.Length; i++)
                    {
                        if (alphaKeys[i] == null)
                        {
                            error = $"Aurora gradient alpha key {i} is null.";
                            return false;
                        }

                        resolvedAlphaKeys[i] = new GradientAlphaKey(alphaKeys[i].alpha, alphaKeys[i].time);
                    }
                }

                if (resolvedColorKeys == null || resolvedColorKeys.Length < 2 || resolvedAlphaKeys == null || resolvedAlphaKeys.Length < 2)
                {
                    error = "Aurora gradient needs at least two color keys and two alpha keys.";
                    return false;
                }

                gradient.SetKeys(resolvedColorKeys, resolvedAlphaKeys);

                if (!String.IsNullOrWhiteSpace(mode))
                {
                    if (!Enum.TryParse(mode, true, out GradientMode gradientMode))
                    {
                        error = $"Aurora gradient mode \"{mode}\" is invalid.";
                        return false;
                    }

                    gradient.mode = gradientMode;
                }
                else if (original != null)
                {
                    gradient.mode = original.mode;
                }

                return true;
            }
        }

        [Serializable]
        public class SeasonGradientColorKey
        {
            public string color;

            public float time;

            public SeasonGradientColorKey()
            {

            }

            public SeasonGradientColorKey(GradientColorKey key)
            {
                color = $"#{ColorUtility.ToHtmlStringRGBA(key.color)}";
                time = key.time;
            }
        }

        [Serializable]
        public class SeasonGradientAlphaKey
        {
            public float alpha;

            public float time;

            public SeasonGradientAlphaKey()
            {

            }

            public SeasonGradientAlphaKey(GradientAlphaKey key)
            {
                alpha = key.alpha;
                time = key.time;
            }
        }

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
                    case "m_auroraColors":
                        {
                            if (env.m_auroraColors != null)
                                m_auroraColors = new SeasonGradient(env.m_auroraColors);
                            continue;
                        }
                    case "m_snowBuildup":
                        m_snowBuildup = env.m_snowBuildup;
                        continue;
                    case "m_auroraIntensityNight":
                        m_auroraIntensityNight = env.m_auroraIntensityNight;
                        continue;
                    case "m_auroraIntensityMorning":
                        m_auroraIntensityMorning = env.m_auroraIntensityMorning;
                        continue;
                    case "m_auroraIntensityDay":
                        m_auroraIntensityDay = env.m_auroraIntensityDay;
                        continue;
                    case "m_auroraIntensityEvening":
                        m_auroraIntensityEvening = env.m_auroraIntensityEvening;
                        continue;
                    case "m_cloudOpacityNight":
                        m_cloudOpacityNight = env.m_cloudOpacityNight;
                        continue;
                    case "m_cloudOpacityMorning":
                        m_cloudOpacityMorning = env.m_cloudOpacityMorning;
                        continue;
                    case "m_cloudOpacityDay":
                        m_cloudOpacityDay = env.m_cloudOpacityDay;
                        continue;
                    case "m_cloudOpacityEvening":
                        m_cloudOpacityEvening = env.m_cloudOpacityEvening;
                        continue;
                    case "m_aoIntensityNight":
                        m_aoIntensityNight = env.m_aoIntensityNight;
                        continue;
                    case "m_aoIntensityMorning":
                        m_aoIntensityMorning = env.m_aoIntensityMorning;
                        continue;
                    case "m_aoIntensityDay":
                        m_aoIntensityDay = env.m_aoIntensityDay;
                        continue;
                    case "m_aoIntensityEvening":
                        m_aoIntensityEvening = env.m_aoIntensityEvening;
                        continue;
                }

                object value = property.GetValue(env);
                if (property.FieldType == typeof(Color))
                    value = $"#{ColorUtility.ToHtmlStringRGBA((Color)value)}";

                field.SetValue(this, value);
            }
        }

        public EnvSetup ToEnvSetup()
        {
            EnvSetup original = EnvMan.instance.m_environments.Find(e => e.m_name == m_cloneFrom);

            if (original == null)
            {
                Seasons.LogWarning($"Environment \"{m_name}\" clone source \"{m_cloneFrom}\" was not found. Falling back to \"Clear\".");

                original = EnvMan.instance.GetEnv("Clear") ?? EnvMan.instance.m_environments.FirstOrDefault();
            }

            if (original == null)
                throw new InvalidOperationException($"No environment is available to clone for \"{m_name}\".");

            SeasonEnvironment defaultSettings = new SeasonEnvironment();

            EnvSetup env = original.Clone();

            foreach (FieldInfo property in env.GetType().GetFields())
            {
                FieldInfo field = GetType().GetField(property.Name);
                if (field == null)
                    continue;

                object fieldValue = field.GetValue(this);
                if (fieldValue == null)
                    continue;

                bool isNullable = Nullable.GetUnderlyingType(field.FieldType) != null;
                if (!isNullable && field.FieldType != typeof(bool) && fieldValue.Equals(field.GetValue(defaultSettings)))
                    continue;

                switch (property.Name)
                {
                    case "m_envObject":
                        {
                            if (usedObjects.TryGetValue(m_envObject, out GameObject envObject) && envObject != null)
                            {
                                env.m_envObject = envObject;
                            }
                            else
                            {
                                Seasons.LogWarning($"Environment object \"{m_envObject}\" was not found for environment \"{m_name}\".");
                            }

                            continue;
                        }
                    case "m_psystems":
                        {
                            List<GameObject> particleSystems = new List<GameObject>();

                            foreach (string particleSystemName in m_psystems.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                string name = particleSystemName.Trim();

                                if (usedObjects.TryGetValue(name, out GameObject particleSystem) && particleSystem != null)
                                {
                                    particleSystems.Add(particleSystem);
                                    continue;
                                }

                                Seasons.LogWarning($"Particle system \"{name}\" was not found for environment \"{m_name}\".");
                            }

                            env.m_psystems = particleSystems.ToArray();
                            continue;
                        }
                    case "m_ambientLoop":
                        {
                            env.m_ambientLoop = usedAudioClips.GetValueSafe(m_ambientLoop) ?? CustomMusic.audioClips.GetValueSafe(m_ambientLoop);

                            if (env.m_ambientLoop == null)
                                Seasons.LogWarning($"Ambient loop \"{m_ambientLoop}\" was not found for environment \"{m_name}\".");

                            continue;
                        }
                    case "m_auroraColors":
                        {
                            if (m_auroraColors.TryCreateGradient(env.m_auroraColors, out Gradient gradient, out string error))
                                env.m_auroraColors = gradient;
                            else
                                Seasons.LogWarning($"Invalid aurora gradient for environment \"{m_name}\": {error}");

                            continue;
                        }
                }

                if (property.FieldType == typeof(Color))
                {
                    if (ColorUtility.TryParseHtmlString(fieldValue.ToString(), out Color color))
                        property.SetValue(env, color);
                    else
                        Seasons.LogWarning($"Invalid color \"{fieldValue}\" for property \"{property.Name}\" in environment \"{m_name}\".");
                    continue;
                }

                property.SetValue(env, fieldValue);
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
                    m_snowBuildup = 0.2f
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
                    m_ambientLoop = "Amb_DeepNorth_Loop_01",
                    m_snowBuildup = 0.1f
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
                    m_snowBuildup = 0.3f
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
                    m_ambientLoop = "Amb_DeepNorth_Loop_01",
                    m_snowBuildup = 0.1f
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
                    m_ambientLoop = "Amb_DeepNorth_Loop_01",
                    m_snowBuildup = 0.1f
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
                    m_snowBuildup = 0.2f
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
                    m_ambientLoop = "Amb_DeepNorth_Loop_01",
                    m_snowBuildup = 0.1f
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

        private static readonly Dictionary<string, GameObject> usedObjects = new Dictionary<string, GameObject>();

        private static readonly Dictionary<string, AudioClip> usedAudioClips = new Dictionary<string, AudioClip>();

        public static void ClearCachedObjects()
        {
            usedObjects.Clear();
            usedAudioClips.Clear();
        }

        public static void RebuildCachedObjects()
        {
            ClearCachedObjects();
            AddCachedObjectsFromCurrentEnvironments();
        }

        public static void AddCachedObjectsFromCurrentEnvironments()
        {
            if (EnvMan.instance == null || EnvMan.instance.m_environments == null)
                return;

            foreach (EnvSetup env in EnvMan.instance.m_environments)
                AddCachedObjects(env);
        }

        public static void AddCachedObjects(EnvSetup env)
        {
            if (env == null)
                return;

            AddCachedObject(env.m_envObject);

            if (env.m_psystems != null)
            {
                foreach (GameObject psystem in env.m_psystems)
                    AddCachedObject(psystem);
            }

            AddCachedAudioClip(env.m_ambientLoop);
        }

        private static void AddCachedObject(GameObject obj)
        {
            if (obj == null || String.IsNullOrWhiteSpace(obj.name) || usedObjects.ContainsKey(obj.name))
                return;

            usedObjects.Add(obj.name, obj);
        }

        private static void AddCachedAudioClip(AudioClip clip)
        {
            if (clip == null || String.IsNullOrWhiteSpace(clip.name) || usedAudioClips.ContainsKey(clip.name))
                return;

            usedAudioClips.Add(clip.name, clip);
        }
    }
}