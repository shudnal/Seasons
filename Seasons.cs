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
using HarmonyLib.Tools;

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

        public static ConfigEntry<TimerFormat> seasonsTimerFormat;

        public static ConfigEntry<bool> overrideSeason;
        public static ConfigEntry<Season> seasonOverrided;

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

        public static ConfigEntry<string> localizationSeasonNameSpring;
        public static ConfigEntry<string> localizationSeasonNameSummer;
        public static ConfigEntry<string> localizationSeasonNameFall;
        public static ConfigEntry<string> localizationSeasonNameWinter;

        public static ConfigEntry<string> localizationSeasonIsComingSpring;
        public static ConfigEntry<string> localizationSeasonIsComingSummer;
        public static ConfigEntry<string> localizationSeasonIsComingFall;
        public static ConfigEntry<string> localizationSeasonIsComingWinter;

        public static ConfigEntry<string> localizationSeasonTooltipSpring;
        public static ConfigEntry<string> localizationSeasonTooltipSummer;
        public static ConfigEntry<string> localizationSeasonTooltipFall;
        public static ConfigEntry<string> localizationSeasonTooltipWinter;

        public static Seasons instance;
        public static SeasonState seasonState;
        internal const int seasonsCount = 4;
        public const int seasonColorVariants = 4;

        public const string statusEffectSeasonName = "Season";
        public static int statusEffectSeasonHash = statusEffectSeasonName.GetStableHashCode();

        public static Sprite iconSpring;
        public static Sprite iconSummer;
        public static Sprite iconFall;
        public static Sprite iconWinter;

        public static string configDirectory;

        public static Dictionary<string, PrefabController> prefabControllers = SeasonalTextureVariants.controllers;
        public static Dictionary<int, TextureVariants> texturesVariants = SeasonalTextureVariants.textures;

        public static readonly CustomSyncedValue<Dictionary<int, string>> seasonsSettingsJSON = new CustomSyncedValue<Dictionary<int, string>>(configSync, "Seasons settings JSON", new Dictionary<int, string>());
        public static readonly CustomSyncedValue<string> customEnvironmentsJSON = new CustomSyncedValue<string>(configSync, "Custom environments JSON", "");
        public static readonly CustomSyncedValue<string> customBiomeEnvironmentsJSON = new CustomSyncedValue<string>(configSync, "Custom biome environments JSON", "");

        public static readonly List<BiomeEnvSetup> biomesDefault = new List<BiomeEnvSetup>();

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
        
        private void Awake()
        {
            harmony.PatchAll();

            instance = this;

            ConfigInit();
            _ = configSync.AddLockingConfigEntry(configLocked);

            seasonsSettingsJSON.ValueChanged += new Action(SeasonSettings.UpdateSeasonSettings);

            Game.isModded = true;

            LoadIcons();

            Test();

            seasonState = new SeasonState();
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
        
        public static void LogWarning(object data)
        {
            if (loggingEnabled.Value)
                instance.Logger.LogWarning(data);
        }

        public void ConfigInit()
        {
            config("General", "NexusID", 0, "Nexus mod ID for updates", false);

            configLocked = config("General", "Lock Configuration", defaultValue: true, "Configuration is locked and can be changed by server admins only.");
            loggingEnabled = config("General", "Logging enabled", defaultValue: false, "Enable logging. [Not Synced with Server]", false);

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

            vegetationWinterColor1 = config("Seasons - Winter", "Color 1", defaultValue: new Color(1f, 0.98f, 0.98f, 0.65f), "Color 1");
            vegetationWinterColor2 = config("Seasons - Winter", "Color 2", defaultValue: new Color(1f, 1f, 1f, 0.6f), "Color 2");
            vegetationWinterColor3 = config("Seasons - Winter", "Color 3", defaultValue: new Color(0.98f, 0.98f, 1f, 0.65f), "Color 3");
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

            grassWinterColor1 = config("Grass - Winter", "Color 1", defaultValue: new Color(1f, 0.98f, 0.98f, 0.65f), "Color 1");
            grassWinterColor2 = config("Grass - Winter", "Color 2", defaultValue: new Color(1f, 1f, 1f, 0.6f), "Color 2");
            grassWinterColor3 = config("Grass - Winter", "Color 3", defaultValue: new Color(0.98f, 0.98f, 1f, 0.65f), "Color 3");
            grassWinterColor4 = config("Grass - Winter", "Color 4", defaultValue: new Color(1f, 1f, 1f, 0.65f), "Color 4");

            localizationSeasonNameSpring = config("Localization", "Season name Spring", defaultValue: "Spring", "Season name");
            localizationSeasonNameSummer = config("Localization", "Season name Summer", defaultValue: "Summer", "Season name");
            localizationSeasonNameFall = config("Localization", "Season name Fall", defaultValue: "Fall", "Season name");
            localizationSeasonNameWinter = config("Localization", "Season name Winter", defaultValue: "Winter", "Season name");

            localizationSeasonIsComingSpring = config("Localization", "Status tooltip - Spring is coming", defaultValue: "Spring is coming", "Message to be shown on the last day of the previous season.");
            localizationSeasonIsComingSummer = config("Localization", "Status tooltip - Summer is coming", defaultValue: "Summer is coming", "Message to be shown on the last day of the previous season.");
            localizationSeasonIsComingFall = config("Localization", "Status tooltip - Fall is coming", defaultValue: "Fall is coming", "Message to be shown on the last day of the previous season.");
            localizationSeasonIsComingWinter = config("Localization", "Status tooltip - Winter is coming", defaultValue: "Winter is coming", "Message to be shown on the last day of the previous season.");

            localizationSeasonTooltipSpring = config("Localization", "Season status effect tooltip - Spring has come", defaultValue: "Spring has come", "Message to be shown on the buff tooltip and almanach.");
            localizationSeasonTooltipSummer = config("Localization", "Season status effect tooltip - Summer has come", defaultValue: "Summer has come", "Message to be shown on the buff tooltip and almanach.");
            localizationSeasonTooltipFall = config("Localization", "Season status effect tooltip - Fall has come", defaultValue: "Fall has come", "Message to be shown on the buff tooltip and almanach.");
            localizationSeasonTooltipWinter = config("Localization", "Season status effect tooltip - Winter has come", defaultValue: "Winter has come", "Message to be shown on the buff tooltip and almanach.");

            cacheStorageFormat = config("Test", "Cache format", defaultValue: CacheFormat.Binary, "Cache files format. Binary for fast loading single non humanreadable file. JSON for humanreadable cache.json + textures subdirectory.");

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

        private void LoadIcons() 
        {
            LoadIcon("season_spring.png",   ref iconSpring);
            LoadIcon("season_summer.png",   ref iconSummer);
            LoadIcon("season_fall.png",     ref iconFall);
            LoadIcon("season_winter.png",   ref iconWinter);
        }

        private void LoadIcon(string filename, ref Sprite icon)
        {
            Assembly executingAssembly = Assembly.GetExecutingAssembly();

            string name = executingAssembly.GetManifestResourceNames().Single(str => str.EndsWith(filename));

            Stream resourceStream = executingAssembly.GetManifestResourceStream(name);

            byte[] data = new byte[resourceStream.Length];
            resourceStream.Read(data, 0, data.Length);

            Texture2D tex = new Texture2D(2, 2);
            if (tex.LoadImage(data, true))
                icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.zero);
        }

        private void Test()
        {
            
        }

        private Color GetColorConfig(string fieldName)
        {
            return (GetType().GetField(fieldName).GetValue(this) as ConfigEntry<Color>).Value;
        }

        private string GetStringConfig(string fieldName)
        {
            return (GetType().GetField(fieldName).GetValue(this) as ConfigEntry<string>).Value;
        }
        
        private Sprite GetSpriteConfig(string fieldName)
        {
            return GetType().GetField(fieldName).GetValue(this) as Sprite;
        }

        public static Color GetSeasonConfigColor(Season season, int pos)
        {
            return instance.GetColorConfig($"vegetation{season}Color{Mathf.Clamp(pos, 1, 4)}");
        }

        public static Color GetGrassConfigColor(Season season, int pos)
        {
            return instance.GetColorConfig($"grass{season}Color{Mathf.Clamp(pos, 1, 4)}");
        }

        public static Color GetMossConfigColor(Season season, int pos)
        {
            Color grassColor = GetGrassConfigColor(season, (pos + 2) % seasonColorVariants + 1);
            
            if (season != Season.Winter)
                grassColor.a /= 3;

            return grassColor;
        }

        public static Color GetCreatureConfigColor(Season season, int pos)
        {
            Color creatureColor = GetSeasonConfigColor(season, pos);

            if (season == Season.Winter)
                creatureColor.a /= 2;
            else
                creatureColor.a /= 3;

            return creatureColor;
        }

        public static string GetSeasonTooltip(Season season)
        {
            return instance.GetStringConfig($"localizationSeasonTooltip{season}");
        }

        public static string GetSeasonName(Season season)
        {
            return instance.GetStringConfig($"localizationSeasonName{season}");
        }

        public static string GetSeasonIsComing(Season season)
        {
            return instance.GetStringConfig($"localizationSeasonIsComing{season}");
        }

        public static Sprite GetSeasonIcon(Season season)
        {
            return instance.GetSpriteConfig($"icon{season}");
        }
    }
}
