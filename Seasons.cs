using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using ServerSync;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;
using System.IO;
using UnityEngine.Rendering;

namespace Seasons
{
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    [BepInIncompatibility("RustyMods.Seasonality")]
    public class Seasons : BaseUnityPlugin
    {
        const string pluginID = "shudnal.Seasons";
        const string pluginName = "Seasons";
        const string pluginVersion = "1.0.7";

        private readonly Harmony harmony = new Harmony(pluginID);

        internal static readonly ConfigSync configSync = new ConfigSync(pluginID) { DisplayName = pluginName, CurrentVersion = pluginVersion, MinimumRequiredVersion = pluginVersion };

        private static ConfigEntry<bool> configLocked;
        private static ConfigEntry<bool> loggingEnabled;

        public static ConfigEntry<CacheFormat> cacheStorageFormat;
        public static ConfigEntry<bool> logTime;

        public static ConfigEntry<bool> overrideSeason;
        public static ConfigEntry<Season> seasonOverrided;

        public static ConfigEntry<bool> controlEnvironments;
        public static ConfigEntry<bool> controlRandomEvents;
        public static ConfigEntry<bool> controlLightings;
        public static ConfigEntry<bool> controlStats;
        public static ConfigEntry<bool> controlMinimap;
        public static ConfigEntry<bool> controlYggdrasil;
        public static ConfigEntry<bool> controlTraders;

        public static ConfigEntry<bool> showCurrentSeasonBuff;
        public static ConfigEntry<TimerFormat> seasonsTimerFormat;
        public static ConfigEntry<bool> hideSecondsInTimer;

        public static ConfigEntry<bool> enableSeasonalItems;
        public static ConfigEntry<bool> preventDeathFromFreezing;
        public static ConfigEntry<bool> freezingSwimmingInWinter;
        public static ConfigEntry<bool> seasonalStatsOutdoorsOnly;
        public static ConfigEntry<bool> changeSeasonOnlyAfterSleep;
        public static ConfigEntry<bool> hideGrassInWinter;

        public static ConfigEntry<int> waterFreezesAfterDaysOfWinter;
        public static ConfigEntry<bool> enableNightMusicOnFrozenOcean;
        public static ConfigEntry<float> frozenOceanSlipperiness;
        public static ConfigEntry<bool> placeShipAboveFrozenOcean;

        public static ConfigEntry<bool> showFadeOnSeasonChange;
        public static ConfigEntry<float> fadeOnSeasonChangeDuration;

        public static ConfigEntry<StationHover> hoverBeeHive;
        public static ConfigEntry<bool> hoverBeeHiveTotal;
        public static ConfigEntry<StationHover> hoverPlant;
        public static ConfigEntry<bool> seasonalMinimapBorderColor;

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

        public static ConfigEntry<bool> enableSeasonalGlobalKeys;
        public static ConfigEntry<string> seasonalGlobalKeyFall;
        public static ConfigEntry<string> seasonalGlobalKeySpring;
        public static ConfigEntry<string> seasonalGlobalKeySummer;
        public static ConfigEntry<string> seasonalGlobalKeyWinter;

        public static Seasons instance;
        public static SeasonState seasonState;
        internal const int seasonsCount = 4;
        public const int seasonColorVariants = 4;

        public const string statusEffectSeasonName = "Season";
        public static int statusEffectSeasonHash = statusEffectSeasonName.GetStableHashCode();

        public const string statusEffectOverheatName = "Overheat";
        public static int statusEffectOverheatHash = statusEffectOverheatName.GetStableHashCode();

        public static Sprite iconSpring;
        public static Sprite iconSummer;
        public static Sprite iconFall;
        public static Sprite iconWinter;

        public static Texture2D Minimap_Summer_ForestTex = new Texture2D(512, 512, TextureFormat.RGBA32, false);
        public static Texture2D Minimap_Fall_ForestTex = new Texture2D(512, 512, TextureFormat.RGBA32, false);
        public static Texture2D Minimap_Winter_ForestTex = new Texture2D(512, 512, TextureFormat.RGBA32, false);

        public static string configDirectory;

        public static Dictionary<string, PrefabController> prefabControllers = SeasonalTextureVariants.controllers;
        public static Dictionary<int, TextureVariants> texturesVariants = SeasonalTextureVariants.textures;
       
        public static readonly CustomSyncedValue<int> currentSeason = new CustomSyncedValue<int>(configSync, "Current season");
        public static readonly CustomSyncedValue<int> currentDay = new CustomSyncedValue<int>(configSync, "Current day");

        public static readonly CustomSyncedValue<Dictionary<int, string>> seasonsSettingsJSON = new CustomSyncedValue<Dictionary<int, string>>(configSync, "Seasons settings JSON", new Dictionary<int, string>());
        public static readonly CustomSyncedValue<string> customEnvironmentsJSON = new CustomSyncedValue<string>(configSync, "Custom environments JSON", "");
        public static readonly CustomSyncedValue<string> customBiomeEnvironmentsJSON = new CustomSyncedValue<string>(configSync, "Custom biome environments JSON", "");
        public static readonly CustomSyncedValue<string> customEventsJSON = new CustomSyncedValue<string>(configSync, "Custom events JSON", "");
        public static readonly CustomSyncedValue<string> customLightingsJSON = new CustomSyncedValue<string>(configSync, "Custom lightings JSON", "");
        public static readonly CustomSyncedValue<string> customStatsJSON = new CustomSyncedValue<string>(configSync, "Custom stats JSON", "");
        public static readonly CustomSyncedValue<string> customTraderItemsJSON = new CustomSyncedValue<string>(configSync, "Custom traders JSON", "");
        public static readonly CustomSyncedValue<string> customWorldSettingsJSON = new CustomSyncedValue<string>(configSync, "Custom world settings JSON", "");

        public static readonly List<BiomeEnvSetup> biomesDefault = new List<BiomeEnvSetup>();
        public static readonly List<RandomEvent> eventsDefault = new List<RandomEvent>();
        public static Color minimapBorderColor = Color.clear;

        public static WaitForFixedUpdate waitForFixedUpdate = new WaitForFixedUpdate();

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
        
        public enum StationHover
        {
            Vanilla,
            Percentage,
            MinutesSeconds
        }

        private void Awake()
        {
            harmony.PatchAll();

            instance = this;

            ConfigInit();
            _ = configSync.AddLockingConfigEntry(configLocked);

            currentSeason.ValueChanged += new Action(SeasonState.OnSeasonChange);
            currentDay.ValueChanged += new Action(SeasonState.OnDayChange);
            seasonsSettingsJSON.ValueChanged += new Action(SeasonSettings.UpdateSeasonSettings);

            Game.isModded = true;

            LoadIcons();

            seasonState = new SeasonState();
        }

        private void FixedUpdate()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            if (!player.GetSEMan().HaveStatusEffect(statusEffectSeasonHash))
                player.GetSEMan().AddStatusEffect(statusEffectSeasonHash);
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
            config("General", "NexusID", 2654, "Nexus mod ID for updates", false);

            configLocked = config("General", "Lock Configuration", defaultValue: true, "Configuration is locked and can be changed by server admins only.");
            loggingEnabled = config("General", "Logging enabled", defaultValue: false, "Enable logging. [Not Synced with Server]", false);

            controlEnvironments = config("Season - Control", "Control environments", defaultValue: true, "Enables seasonal weathers");
            controlRandomEvents = config("Season - Control", "Control random events", defaultValue: true, "Enables seasonal random events");
            controlLightings = config("Season - Control", "Control lightings", defaultValue: true, "Enables seasonal lightings change (basically gamma or brightness)");
            controlStats = config("Season - Control", "Control stats", defaultValue: true, "Enables seasonal stats change (status effect)");
            controlMinimap = config("Season - Control", "Control minimap", defaultValue: true, "Enables seasonal minimap colors");
            controlYggdrasil = config("Season - Control", "Control yggdrasil branch and roots", defaultValue: true, "Enables seasonal coloring of yggdrasil branch in the sky and roots on the ground");
            controlTraders = config("Season - Control", "Control trader seasonal items list", defaultValue: true, "Enables seasonal changes of trader additional item availability");

            controlStats.SettingChanged += (sender, args) => SE_Season.UpdateSeasonStatusEffectStats();

            enableSeasonalItems = config("Season", "Enable seasonal items", defaultValue: true, "Enables seasonal (Halloween, Midsummer, Yule) items in the corresponding season");
            preventDeathFromFreezing = config("Season", "Prevent death from freezing", defaultValue: true, "Prevents death from freezing when not in mountains or deep north");
            seasonalStatsOutdoorsOnly = config("Season", "Seasonal stats works only outdoors", defaultValue: true, "Make seasonal stats works only outdoors");
            freezingSwimmingInWinter = config("Season", "Get freezing when swimming in cold water in winter", defaultValue: true, "Swimming in cold water during winter will get you freezing debuff");
            changeSeasonOnlyAfterSleep = config("Season", "Change season only after sleep", defaultValue: false, "Season can be changed regular way only after sleep");
            hideGrassInWinter = config("Season", "Hide grass in winter", defaultValue: false, "Hide grass in winter");

            seasonalStatsOutdoorsOnly.SettingChanged += (sender, args) => SE_Season.UpdateSeasonStatusEffectStats();
            hideGrassInWinter.SettingChanged += (sender, args) => ClutterVariantController.instance.UpdateColors();

            showCurrentSeasonBuff = config("Season - Buff", "Show current season buff", defaultValue: true, "Show current season buff.");
            seasonsTimerFormat = config("Season - Buff", "Timer format", defaultValue: TimerFormat.CurrentDay, "What to show at season buff timer");
            hideSecondsInTimer = config("Season - Buff", "Hide seconds", defaultValue: true, "Hide seconds at season buff timer");

            showCurrentSeasonBuff.SettingChanged += (sender, args) => SE_Season.UpdateSeasonStatusEffectStats();

            showFadeOnSeasonChange = config("Season - Fade", "Show fade effect on season change", defaultValue: true, "Show black fade loading screen when season is changed.");
            fadeOnSeasonChangeDuration = config("Season - Fade", "Duration of fade effect", defaultValue: 0.5f, "Fade duration");

            hoverBeeHive = Config.Bind("Season - UI", "Bee Hive Hover", defaultValue: StationHover.Vanilla, "Hover text for bee hive.");
            hoverBeeHiveTotal = Config.Bind("Season - UI", "Bee Hive Show total", defaultValue: true, "Show total needed time/percent for bee hive.");
            hoverPlant = Config.Bind("Season - UI", "Plants Hover", defaultValue: StationHover.Vanilla, "Hover text for plants.");
            seasonalMinimapBorderColor = Config.Bind("Season - UI", "Seasonal colored minimap border", defaultValue: true, "Change minimap border color according to current season");

            overrideSeason = config("Season - Override", "Override", defaultValue: false, "The season will be overrided by set season.");
            seasonOverrided = config("Season - Override", "Season", defaultValue: Season.Spring, "The season to set.");

            overrideSeason.SettingChanged += (sender, args) => SeasonState.CheckSeasonChange();
            seasonOverrided.SettingChanged += (sender, args) => SeasonState.CheckSeasonChange();

            waterFreezesAfterDaysOfWinter = config("Season - Winter ocean", "Freeze water in set day of winter", defaultValue: 6, "Water will freeze in the set day of winter");
            enableNightMusicOnFrozenOcean = config("Season - Winter ocean", "Enable music while travelling frozen ocean at night", defaultValue: true, "Enables special frozen ocean music");
            frozenOceanSlipperiness = config("Season - Winter ocean", "Frozen ocean surface slipperiness factor", defaultValue: 1f, "Slipperiness factor of the frozen ocean surface");
            placeShipAboveFrozenOcean = config("Season - Winter ocean", "Place ship above frozen ocean surface", defaultValue: false, "Place ship above frozen ocean surface to prevent unpredictable collisions");

            waterFreezesAfterDaysOfWinter.SettingChanged += (sender, args) => ZoneSystemVariantController.UpdateWaterState();
            placeShipAboveFrozenOcean.SettingChanged += (sender, args) => ZoneSystemVariantController.UpdateShipsPositions();

            vegetationSpringColor1 = config("Seasons - Color - Main - Spring", "Color 1", defaultValue: new Color(0.27f, 0.80f, 0.27f, 0.75f), "Color 1");
            vegetationSpringColor2 = config("Seasons - Color - Main - Spring", "Color 2", defaultValue: new Color(0.69f, 0.84f, 0.15f, 0.75f), "Color 2");
            vegetationSpringColor3 = config("Seasons - Color - Main - Spring", "Color 3", defaultValue: new Color(0.43f, 0.56f, 0.11f, 0.75f), "Color 3");
            vegetationSpringColor4 = config("Seasons - Color - Main - Spring", "Color 4", defaultValue: new Color(0.0f, 1.0f, 0f, 0.0f), "Color 4");

            vegetationSummerColor1 = config("Seasons - Color - Main - Summer", "Color 1", defaultValue: new Color(0.5f, 0.7f, 0.2f, 0.5f), "Color 1");
            vegetationSummerColor2 = config("Seasons - Color - Main - Summer", "Color 2", defaultValue: new Color(0.7f, 0.7f, 0.2f, 0.5f), "Color 2");
            vegetationSummerColor3 = config("Seasons - Color - Main - Summer", "Color 3", defaultValue: new Color(0.5f, 0.5f, 0f, 0.5f), "Color 3");
            vegetationSummerColor4 = config("Seasons - Color - Main - Summer", "Color 4", defaultValue: new Color(0.7f, 0.7f, 0f, 0.2f), "Color 4");

            vegetationFallColor1 = config("Seasons - Color - Main - Fall", "Color 1", defaultValue: new Color(0.8f, 0.5f, 0f, 0.75f), "Color 1");
            vegetationFallColor2 = config("Seasons - Color - Main - Fall", "Color 2", defaultValue: new Color(0.8f, 0.3f, 0f, 0.75f), "Color 2");
            vegetationFallColor3 = config("Seasons - Color - Main - Fall", "Color 3", defaultValue: new Color(0.8f, 0.2f, 0f, 0.75f), "Color 3");
            vegetationFallColor4 = config("Seasons - Color - Main - Fall", "Color 4", defaultValue: new Color(0.9f, 0.5f, 0f, 0.0f), "Color 4");

            vegetationWinterColor1 = config("Seasons - Color - Main - Winter", "Color 1", defaultValue: new Color(1f, 0.98f, 0.98f, 0.65f), "Color 1");
            vegetationWinterColor2 = config("Seasons - Color - Main - Winter", "Color 2", defaultValue: new Color(1f, 1f, 1f, 0.6f), "Color 2");
            vegetationWinterColor3 = config("Seasons - Color - Main - Winter", "Color 3", defaultValue: new Color(0.98f, 0.98f, 1f, 0.65f), "Color 3");
            vegetationWinterColor4 = config("Seasons - Color - Main - Winter", "Color 4", defaultValue: new Color(1f, 1f, 1f, 0.65f), "Color 4");

            grassSpringColor1 = config("Seasons - Color - Grass - Spring", "Color 1", defaultValue: new Color(0.27f, 0.80f, 0.27f, 0.75f), "Color 1");
            grassSpringColor2 = config("Seasons - Color - Grass - Spring", "Color 2", defaultValue: new Color(0.69f, 0.84f, 0.15f, 0.75f), "Color 2");
            grassSpringColor3 = config("Seasons - Color - Grass - Spring", "Color 3", defaultValue: new Color(0.43f, 0.56f, 0.11f, 0.75f), "Color 3");
            grassSpringColor4 = config("Seasons - Color - Grass - Spring", "Color 4", defaultValue: new Color(0.0f, 1.0f, 0f, 0.0f), "Color 4");

            grassSummerColor1 = config("Seasons - Color - Grass - Summer", "Color 1", defaultValue: new Color(0.5f, 0.7f, 0.2f, 0.5f), "Color 1");
            grassSummerColor2 = config("Seasons - Color - Grass - Summer", "Color 2", defaultValue: new Color(0.7f, 0.75f, 0.2f, 0.5f), "Color 2");
            grassSummerColor3 = config("Seasons - Color - Grass - Summer", "Color 3", defaultValue: new Color(0.5f, 0.5f, 0f, 0.5f), "Color 3");
            grassSummerColor4 = config("Seasons - Color - Grass - Summer", "Color 4", defaultValue: new Color(0.7f, 0.7f, 0f, 0.2f), "Color 4");

            grassFallColor1 = config("Seasons - Color - Grass - Fall", "Color 1", defaultValue: new Color(0.8f, 0.6f, 0.2f, 0.5f), "Color 1");
            grassFallColor2 = config("Seasons - Color - Grass - Fall", "Color 2", defaultValue: new Color(0.8f, 0.5f, 0f, 0.5f), "Color 2");
            grassFallColor3 = config("Seasons - Color - Grass - Fall", "Color 3", defaultValue: new Color(0.8f, 0.3f, 0f, 0.5f), "Color 3");
            grassFallColor4 = config("Seasons - Color - Grass - Fall", "Color 4", defaultValue: new Color(0.9f, 0.5f, 0f, 0.0f), "Color 4");

            grassWinterColor1 = config("Seasons - Color - Grass - Winter", "Color 1", defaultValue: new Color(1f, 0.98f, 0.98f, 0.65f), "Color 1");
            grassWinterColor2 = config("Seasons - Color - Grass - Winter", "Color 2", defaultValue: new Color(1f, 1f, 1f, 0.6f), "Color 2");
            grassWinterColor3 = config("Seasons - Color - Grass - Winter", "Color 3", defaultValue: new Color(0.98f, 0.98f, 1f, 0.65f), "Color 3");
            grassWinterColor4 = config("Seasons - Color - Grass - Winter", "Color 4", defaultValue: new Color(1f, 1f, 1f, 0.65f), "Color 4");

            enableSeasonalGlobalKeys = config("Seasons - Global keys", "Enable setting seasonal Global Keys", defaultValue: false, "Enables setting seasonal global key");
            seasonalGlobalKeyFall = config("Seasons - Global keys", "Fall", defaultValue: "Season_Fall", "Seasonal global key for automn");
            seasonalGlobalKeySpring = config("Seasons - Global keys", "Spring", defaultValue: "Season_Spring", "Seasonal global key for spring");
            seasonalGlobalKeySummer = config("Seasons - Global keys", "Summer", defaultValue: "Season_Summer", "Seasonal global key for summer");
            seasonalGlobalKeyWinter = config("Seasons - Global keys", "Winter", defaultValue: "Season_Winter", "Seasonal global key for winter");

            localizationSeasonNameSpring = config("Seasons - Localization", "Season name Spring", defaultValue: "Spring", "Season name");
            localizationSeasonNameSummer = config("Seasons - Localization", "Season name Summer", defaultValue: "Summer", "Season name");
            localizationSeasonNameFall = config("Seasons - Localization", "Season name Fall", defaultValue: "Fall", "Season name");
            localizationSeasonNameWinter = config("Seasons - Localization", "Season name Winter", defaultValue: "Winter", "Season name");

            localizationSeasonIsComingSpring = config("Seasons - Localization", "Status tooltip - Spring is coming", defaultValue: "Spring is coming", "Message to be shown on the last day of the previous season.");
            localizationSeasonIsComingSummer = config("Seasons - Localization", "Status tooltip - Summer is coming", defaultValue: "Summer is coming", "Message to be shown on the last day of the previous season.");
            localizationSeasonIsComingFall = config("Seasons - Localization", "Status tooltip - Fall is coming", defaultValue: "Fall is coming", "Message to be shown on the last day of the previous season.");
            localizationSeasonIsComingWinter = config("Seasons - Localization", "Status tooltip - Winter is coming", defaultValue: "Winter is coming", "Message to be shown on the last day of the previous season.");

            localizationSeasonTooltipSpring = config("Seasons - Localization", "Season status effect tooltip - Spring has come", defaultValue: "Spring has come", "Message to be shown on the buff tooltip and almanach.");
            localizationSeasonTooltipSummer = config("Seasons - Localization", "Season status effect tooltip - Summer has come", defaultValue: "Summer has come", "Message to be shown on the buff tooltip and almanach.");
            localizationSeasonTooltipFall = config("Seasons - Localization", "Season status effect tooltip - Fall has come", defaultValue: "Fall has come", "Message to be shown on the buff tooltip and almanach.");
            localizationSeasonTooltipWinter = config("Seasons - Localization", "Season status effect tooltip - Winter has come", defaultValue: "Winter has come", "Message to be shown on the buff tooltip and almanach.");

            cacheStorageFormat = config("Test", "Cache format", defaultValue: CacheFormat.Binary, "Cache files format. Binary for fast loading single non humanreadable file. JSON for humanreadable cache.json + textures subdirectory.");
            logTime = config("Test", "Log time", defaultValue: false, "Log time info on state update");

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

            LoadTexture("Minimap_Summer_ForestTex.png", ref Minimap_Summer_ForestTex);
            Minimap_Summer_ForestTex.wrapMode = TextureWrapMode.Repeat;
            Minimap_Summer_ForestTex.filterMode = FilterMode.Bilinear;

            LoadTexture("Minimap_Fall_ForestTex.png", ref Minimap_Fall_ForestTex);
            Minimap_Fall_ForestTex.wrapMode = TextureWrapMode.Repeat;
            Minimap_Fall_ForestTex.filterMode = FilterMode.Bilinear;

            LoadTexture("Minimap_Winter_ForestTex.png", ref Minimap_Winter_ForestTex);
            Minimap_Winter_ForestTex.wrapMode = TextureWrapMode.Repeat;
            Minimap_Winter_ForestTex.filterMode = FilterMode.Bilinear;
        }

        private void LoadIcon(string filename, ref Sprite icon)
        {
            Texture2D tex = new Texture2D(2, 2);
            if (LoadTexture(filename, ref tex))
                icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.zero);
        }

        private bool LoadTexture(string filename, ref Texture2D tex)
        {
            string fileInConfigFolder = Path.Combine(configDirectory, filename);
            if (File.Exists(fileInConfigFolder))
                return tex.LoadImage(File.ReadAllBytes(fileInConfigFolder));
            
            Assembly executingAssembly = Assembly.GetExecutingAssembly();

            string name = executingAssembly.GetManifestResourceNames().Single(str => str.EndsWith(filename));

            Stream resourceStream = executingAssembly.GetManifestResourceStream(name);

            byte[] data = new byte[resourceStream.Length];
            resourceStream.Read(data, 0, data.Length);

            return tex.LoadImage(data, true);
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
            return showCurrentSeasonBuff.Value ? instance.GetSpriteConfig($"icon{season}") : null;
        }

        public static string FromSeconds(double seconds)
        {
            TimeSpan ts = TimeSpan.FromSeconds(seconds);
            return ts.ToString(ts.Hours > 0 ? @"h\:mm\:ss" : @"m\:ss");
        }

        public static bool UseTextureControllers()
        {
            return SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
        }
    }
}
