using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using ServerSync;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;
using System.IO;

namespace Seasons
{
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    [BepInIncompatibility("RustyMods.Seasonality")]
    public class Seasons : BaseUnityPlugin
    {
        const string pluginID = "shudnal.Seasons";
        const string pluginName = "Seasons";
        const string pluginVersion = "1.0.0";

        private readonly Harmony harmony = new Harmony(pluginID);

        internal static readonly ConfigSync configSync = new ConfigSync(pluginID) { DisplayName = pluginName, CurrentVersion = pluginVersion, MinimumRequiredVersion = pluginVersion };

        private static ConfigEntry<bool> configLocked;
        private static ConfigEntry<bool> loggingEnabled;
        public static ConfigEntry<CacheFormat> cacheStorageFormat;

        public static ConfigEntry<int> daysInSeason;
        private static ConfigEntry<bool> seasonsOverlap;
        public static ConfigEntry<TimerFormat> seasonsTimerFormat;

        private static ConfigEntry<bool> overrideSeason;
        private static ConfigEntry<Season> seasonOverrided;

        public static ConfigEntry<Color> vegetationSpringColor1;
        public static ConfigEntry<Color> vegetationSpringColor2;
        public static ConfigEntry<Color> vegetationSpringColor3;
        public static ConfigEntry<Color> vegetationSpringColor4;

        public static ConfigEntry<Color> vegetationSummerColor1;
        public static ConfigEntry<Color> vegetationSummerColor2;
        public static ConfigEntry<Color> vegetationSummerColor3;
        public static ConfigEntry<Color> vegetationSummerColor4;

        public static ConfigEntry<Color> vegetationFallColor1;
        public static ConfigEntry<Color> vegetationFallColor2;
        public static ConfigEntry<Color> vegetationFallColor3;
        public static ConfigEntry<Color> vegetationFallColor4;

        public static ConfigEntry<Color> vegetationWinterColor1;
        public static ConfigEntry<Color> vegetationWinterColor2;
        public static ConfigEntry<Color> vegetationWinterColor3;
        public static ConfigEntry<Color> vegetationWinterColor4;

        public static ConfigEntry<Color> grassSpringColor1;
        public static ConfigEntry<Color> grassSpringColor2;
        public static ConfigEntry<Color> grassSpringColor3;
        public static ConfigEntry<Color> grassSpringColor4;

        public static ConfigEntry<Color> grassSummerColor1;
        public static ConfigEntry<Color> grassSummerColor2;
        public static ConfigEntry<Color> grassSummerColor3;
        public static ConfigEntry<Color> grassSummerColor4;

        public static ConfigEntry<Color> grassFallColor1;
        public static ConfigEntry<Color> grassFallColor2;
        public static ConfigEntry<Color> grassFallColor3;
        public static ConfigEntry<Color> grassFallColor4;

        public static ConfigEntry<Color> grassWinterColor1;
        public static ConfigEntry<Color> grassWinterColor2;
        public static ConfigEntry<Color> grassWinterColor3;
        public static ConfigEntry<Color> grassWinterColor4;

        public static ConfigEntry<string> messageSeasonIsComing;
        public static ConfigEntry<string> messageSeasonTooltip;

        public static Seasons instance;
        public static SeasonsState seasonState = new SeasonsState();
        internal const int seasonsCount = 4;
        public const int seasonColorVariants = 4;

        public const string statusEffectSeasonName = "Season";
        public static int statusEffectSeasonHash = statusEffectSeasonName.GetStableHashCode();

        public static Sprite icon;

        public static string configDirectory;

        public static Dictionary<string, PrefabController> prefabControllers = SeasonalTextureVariants.controllers;
        public static Dictionary<int, TextureVariants> texturesVariants = SeasonalTextureVariants.textures;

        public enum Season
        {
            Spring = 0,
            Summer = 1,
            Fall = 2,
            Winter = 3
        }
        
        public enum CacheFormat
        {
            Binary,
            Json,
            SaveBothLoadBinary
        }

        public enum TimerFormat
        {
            None,
            CurrentDay,
            TimeToEnd
        }

        public class SeasonsState
        {
            public float m_spring = 1f;
            public float m_summer = 0f;
            public float m_fall = 0f;
            public float m_winter = 0f;

            private Season m_season = Season.Spring;
            private int m_day = 0;

            public void UpdateState(int day, float dayFraction)
            {
                dayFraction = Mathf.Clamp01(dayFraction);
                int dayInSeason = GetDayInSeason(day);

                for (int i = 0; i < seasonsCount; i++)
                    SetSeasonFactor((Season)i, 0f);

                int season = (int)m_season;

                if (overrideSeason.Value)
                {
                    m_season = seasonOverrided.Value;
                    SetSeasonFactor(m_season, 1f);
                    CheckIfSeasonChanged(season);
                    CheckIfDayChanged(dayInSeason);
                    return;
                }

                m_season = GetSeason(day);
                float fraction = 0f;

                if (seasonsOverlap.Value && day != 0)
                {
                    if (dayInSeason == 0)
                    {
                        fraction = (1f - dayFraction) / 2f;
                        SetSeasonFactor(PreviousSeason(m_season), fraction);
                    }
                    else if (dayInSeason == daysInSeason.Value - 1)
                    {
                        fraction = dayFraction / 2f;
                        SetSeasonFactor(NextSeason(m_season), fraction);
                    }
                }

                SetSeasonFactor(m_season, 1f - fraction);
                CheckIfSeasonChanged(season);
                CheckIfDayChanged(dayInSeason);
            }

            public Season GetCurrentSeason()
            {
                return m_season;
            }

            public int GetCurrentDay()
            {
                return m_day;
            }

            public Season GetNextSeason()
            {
                return NextSeason(m_season);
            }

            private void SetSeasonFactor(Season season, float fraction)
            {
                switch ((int)season)
                {
                    case 0: 
                        m_spring = fraction;
                        break;
                    case 1:
                        m_summer = fraction;
                        break;
                    case 2:
                        m_fall = fraction;
                        break;
                    case 3:
                        m_winter = fraction;
                        break;
                }
            }

            private Season GetSeason(int day)
            {
                return (Season)(day / daysInSeason.Value % seasonsCount);
            }

            private int GetDayInSeason(int day)
            {
                return day % daysInSeason.Value;
            }

            private Season PreviousSeason(Season season)
            {
                return (Season)((seasonsCount + (int)season - 1) % seasonsCount);
            }
            
            private Season NextSeason(Season season)
            {
                return (Season)(((int)season + 1) % seasonsCount);
            }

            private void CheckIfSeasonChanged(int season)
            {
                if (season == (int)m_season)
                    return;

                PrefabVariantController.UpdatePrefabColors();
                TerrainVariantController.UpdateTerrainColors();
                ClutterVariantController.instance.UpdateColors();
            }

            private void CheckIfDayChanged(int dayInSeason)
            {
                if (m_day == dayInSeason)
                    return;

                m_day = dayInSeason;
                ClutterVariantController.instance.UpdateColors();
            }

            public override string ToString()
            {
                return $"{m_season} spring:{m_spring,-5:F4} summer:{m_summer,-5:F4} fall:{m_fall,-5:F4} winter:{m_winter,-5:F4}";
            }
        }

        private void Awake()
        {
            harmony.PatchAll();

            instance = this;

            ConfigInit();
            _ = configSync.AddLockingConfigEntry(configLocked);

            Game.isModded = true;

            LoadAssets();

            Test();
        }

        private void FixedUpdate()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            if (!player.GetSEMan().HaveStatusEffect(statusEffectSeasonHash))
            {
                player.GetSEMan().AddStatusEffect(statusEffectSeasonHash);
            }
        }

        private void OnDestroy()
        {
            Config.Save();
            instance = null;
            harmony?.UnpatchSelf();
        }

        public static void LogInfo(object data)
        {
            if (loggingEnabled.Value)
                instance.Logger.LogInfo(data);
        }

        public void ConfigInit()
        {
            config("General", "NexusID", 0, "Nexus mod ID for updates", false);

            configLocked = config("General", "Lock Configuration", defaultValue: true, "Configuration is locked and can be changed by server admins only.");
            loggingEnabled = config("General", "Logging enabled", defaultValue: false, "Enable logging. [Not Synced with Server]", false);

            daysInSeason = config("Season", "Days in season", defaultValue: 10, "How much ingame days should pass for season to change.");
            seasonsOverlap = config("Season", "Seasons overlap", defaultValue: true, "The seasons will smoothly overlap on the last and first days.");
            seasonsTimerFormat = config("Season", "Timer format", defaultValue: TimerFormat.CurrentDay, "What to show at season buff timer"); 

            overrideSeason = config("Seasons override", "Override", defaultValue: false, "The season will be overrided by set season.");
            seasonOverrided = config("Seasons override", "Season", defaultValue: Season.Spring, "The season to set.");

            vegetationSpringColor1 = config("Seasons - Spring", "Color 1", defaultValue: new Color(0.27f, 0.80f, 0.27f, 0.75f), "Color 1");
            vegetationSpringColor2 = config("Seasons - Spring", "Color 2", defaultValue: new Color(0.69f, 0.84f, 0.15f, 0.75f), "Color 2");
            vegetationSpringColor3 = config("Seasons - Spring", "Color 3", defaultValue: new Color(0.43f, 0.56f, 0.11f, 0.75f), "Color 3");
            vegetationSpringColor4 = config("Seasons - Spring", "Color 4", defaultValue: new Color(0.0f, 1.0f, 0f, 0.0f), "Color 4");

            vegetationSummerColor1 = config("Seasons - Summer", "Color 1", defaultValue: new Color(0.5f, 0.7f, 0.2f, 0.5f), "Color 1");
            vegetationSummerColor2 = config("Seasons - Summer", "Color 2", defaultValue: new Color(0.7f, 0.7f, 0.2f, 0.5f), "Color 2");
            vegetationSummerColor3 = config("Seasons - Summer", "Color 3", defaultValue: new Color(0.5f, 0.5f, 0f, 0.5f), "Color 3");
            vegetationSummerColor4 = config("Seasons - Summer", "Color 4", defaultValue: new Color(0.7f, 0.7f, 0f, 0.2f), "Color 4");

            vegetationFallColor1 = config("Seasons - Fall", "Color 1", defaultValue: new Color(0.8f, 0.5f, 0f, 0.75f), "Color 1");
            vegetationFallColor2 = config("Seasons - Fall", "Color 2", defaultValue: new Color(0.8f, 0.3f, 0f, 0.75f), "Color 2");
            vegetationFallColor3 = config("Seasons - Fall", "Color 3", defaultValue: new Color(0.8f, 0.2f, 0f, 0.75f), "Color 3");
            vegetationFallColor4 = config("Seasons - Fall", "Color 4", defaultValue: new Color(0.9f, 0.5f, 0f, 0.0f), "Color 4");

            vegetationWinterColor1 = config("Seasons - Winter", "Color 1", defaultValue: new Color(1f, 0.98f, 0.98f, 0.7f), "Color 1");
            vegetationWinterColor2 = config("Seasons - Winter", "Color 2", defaultValue: new Color(1f, 1f, 1f, 0.6f), "Color 2");
            vegetationWinterColor3 = config("Seasons - Winter", "Color 3", defaultValue: new Color(0.97f, 0.97f, 1f, 0.75f), "Color 3");
            vegetationWinterColor4 = config("Seasons - Winter", "Color 4", defaultValue: new Color(1f, 1f, 1f, 0.65f), "Color 4");

            grassSpringColor1 = config("Grass - Spring", "Color 1", defaultValue: new Color(0.27f, 0.80f, 0.27f, 0.75f), "Color 1");
            grassSpringColor2 = config("Grass - Spring", "Color 2", defaultValue: new Color(0.69f, 0.84f, 0.15f, 0.75f), "Color 2");
            grassSpringColor3 = config("Grass - Spring", "Color 3", defaultValue: new Color(0.43f, 0.56f, 0.11f, 0.75f), "Color 3");
            grassSpringColor4 = config("Grass - Spring", "Color 4", defaultValue: new Color(0.0f, 1.0f, 0f, 0.0f), "Color 4");

            grassSummerColor1 = config("Grass - Summer", "Color 1", defaultValue: new Color(0.5f, 0.7f, 0.2f, 0.5f), "Color 1");
            grassSummerColor2 = config("Grass - Summer", "Color 2", defaultValue: new Color(0.7f, 0.75f, 0.2f, 0.5f), "Color 2");
            grassSummerColor3 = config("Grass - Summer", "Color 3", defaultValue: new Color(0.5f, 0.5f, 0f, 0.5f), "Color 3");
            grassSummerColor4 = config("Grass - Summer", "Color 4", defaultValue: new Color(0.7f, 0.7f, 0f, 0.2f), "Color 4");

            grassFallColor1 = config("Grass - Fall", "Color 1", defaultValue: new Color(0.8f, 0.6f, 0.2f, 0.5f), "Color 1");
            grassFallColor2 = config("Grass - Fall", "Color 2", defaultValue: new Color(0.8f, 0.5f, 0f, 0.5f), "Color 2");
            grassFallColor3 = config("Grass - Fall", "Color 3", defaultValue: new Color(0.8f, 0.3f, 0f, 0.5f), "Color 3");
            grassFallColor4 = config("Grass - Fall", "Color 4", defaultValue: new Color(0.9f, 0.5f, 0f, 0.0f), "Color 4");

            grassWinterColor1 = config("Grass - Winter", "Color 1", defaultValue: new Color(1f, 0.98f, 0.98f, 0.7f), "Color 1");
            grassWinterColor2 = config("Grass - Winter", "Color 2", defaultValue: new Color(1f, 1f, 1f, 0.6f), "Color 2");
            grassWinterColor3 = config("Grass - Winter", "Color 3", defaultValue: new Color(0.97f, 0.97f, 1f, 0.75f), "Color 3");
            grassWinterColor4 = config("Grass - Winter", "Color 4", defaultValue: new Color(1f, 1f, 1f, 0.65f), "Color 4");

            messageSeasonIsComing = config("Messages", "Next season is coming", defaultValue: "{0} is coming", "Message to be shown on the last day of the season.");
            messageSeasonTooltip = config("Messages", "Season status effect tooltip", defaultValue: "{0} has come", "Message to be shown on the last day of the season."); 

            cacheStorageFormat = config("Test", "Cache format", defaultValue: CacheFormat.Binary, "Cache files format. Binary for fast loading single non humanreadable file. JSON for humanreadable cache.json + textures subfolder.");

            configDirectory = Path.Combine(Paths.ConfigPath, pluginID);
        }

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, ConfigDescription description, bool synchronizedSetting = true)
        {
            ConfigEntry<T> configEntry = Config.Bind(group, name, defaultValue, description);

            SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, string description, bool synchronizedSetting = true) => config(group, name, defaultValue, new ConfigDescription(description), synchronizedSetting);

        private void LoadAssets() 
        {
            Assembly executingAssembly = Assembly.GetExecutingAssembly();
            
            string name = executingAssembly.GetManifestResourceNames().Single(str => str.EndsWith("season.png"));

            Stream resourceStream = executingAssembly.GetManifestResourceStream(name);

            byte[] data = new byte[resourceStream.Length];
            resourceStream.Read(data, 0, data.Length);

            Texture2D tex = new Texture2D(2, 2);
            if (tex.LoadImage(data, true))
                icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.zero);
        }

        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.FixedUpdate))]
        public static class EnvMan_FixedUpdate_SeasonStateUpdate
        {
            private static void Postfix(EnvMan __instance)
            {
                int day = __instance.GetCurrentDay();
                float dayFraction = __instance.GetDayFraction();

                seasonState.UpdateState(day, dayFraction);
            }
        }

        private void Test()
        {
            /*foreach (var item in q)
                if (item.Key.Contains("frac"))
                    ZLog.Log(item.Key);
            }            //SeasonalTextureCache.CreateCache(cacheFolder);*/
            //SwampTree1_Stub
            //-window-mode exclusive -screen-fullscreen -console -exclusivefullscreen
            
        }

        public Color GetSeasonConfigColor(Season season, int pos)
        {
            return GetColorConfig($"vegetation{season}Color{Mathf.Clamp(pos, 1, 4)}");
        }

        public Color GetGrassConfigColor(Season season, int pos)
        {
            return GetColorConfig($"grass{season}Color{Mathf.Clamp(pos, 1, 4)}");
        }

        public Color GetMossConfigColor(Season season, int pos)
        {
            Color grassColor = GetGrassConfigColor(season, (pos + 2) % seasonColorVariants + 1);
            
            if (season != Season.Winter)
                grassColor.a /= 3;

            return grassColor;
        }

        private Color GetColorConfig(string fieldName)
        {
            return (GetType().GetField(fieldName).GetValue(this) as ConfigEntry<Color>).Value;
        }

    }
}
