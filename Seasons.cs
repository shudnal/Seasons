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
using static Terminal;
using System.Diagnostics;
using System.Collections;

namespace Seasons
{
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    [BepInIncompatibility("RustyMods.Seasonality")]
    [BepInDependency("shudnal.GammaOfNightLights", BepInDependency.DependencyFlags.SoftDependency)]
    public class Seasons : BaseUnityPlugin
    {
        const string pluginID = "shudnal.Seasons";
        const string pluginName = "Seasons";
        const string pluginVersion = "1.1.1";

        private readonly Harmony harmony = new Harmony(pluginID);

        internal static readonly ConfigSync configSync = new ConfigSync(pluginID) { DisplayName = pluginName, CurrentVersion = pluginVersion, MinimumRequiredVersion = pluginVersion };

        private static ConfigEntry<bool> configLocked;
        private static ConfigEntry<bool> loggingEnabled;
        public static ConfigEntry<long> dayLengthSec;

        public static ConfigEntry<CacheFormat> cacheStorageFormat;
        public static ConfigEntry<bool> logTime;
        public static ConfigEntry<bool> logFloes;
        public static ConfigEntry<bool> rebuildCache;

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
        public static ConfigEntry<bool> showCurrentSeasonInRaven;
        public static ConfigEntry<TimerFormatRaven> seasonsTimerFormatInRaven;

        public static ConfigEntry<bool> enableSeasonalItems;
        public static ConfigEntry<bool> preventDeathFromFreezing;
        public static ConfigEntry<bool> freezingSwimmingInWinter;
        public static ConfigEntry<bool> seasonalStatsOutdoorsOnly;
        public static ConfigEntry<bool> changeSeasonOnlyAfterSleep;
        public static ConfigEntry<bool> hideGrassInWinter;
        public static ConfigEntry<Vector2> hideGrassInWinterDays;
        public static ConfigEntry<string> hideGrassListInWinter;
        public static ConfigEntry<int> cropsDiesAfterSetDayInWinter;
        public static ConfigEntry<string> cropsToSurviveInWinter;

        public static ConfigEntry<bool> enableFrozenWater;
        public static ConfigEntry<Vector2> waterFreezesInWinterDays;
        public static ConfigEntry<bool> enableIceFloes;
        public static ConfigEntry<Vector2> iceFloesInWinterDays;
        public static ConfigEntry<Vector2> amountOfIceFloesInWinterDays;
        public static ConfigEntry<bool> enableNightMusicOnFrozenOcean;
        public static ConfigEntry<float> frozenOceanSlipperiness;
        public static ConfigEntry<bool> placeShipAboveFrozenOcean;

        public static ConfigEntry<bool> showFadeOnSeasonChange;
        public static ConfigEntry<float> fadeOnSeasonChangeDuration;

        public static ConfigEntry<StationHover> hoverBeeHive;
        public static ConfigEntry<bool> hoverBeeHiveTotal;
        public static ConfigEntry<StationHover> hoverPlant;
        public static ConfigEntry<bool> seasonalMinimapBorderColor;

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
        public static bool haveGammaOfNightLights;

        /*public static Dictionary<string, PrefabController> prefabControllers = SeasonalTextureVariants.controllers;
        public static Dictionary<int, TextureVariants> texturesVariants = SeasonalTextureVariants.textures;*/

        public static SeasonalTextureVariants texturesVariants = new SeasonalTextureVariants();

        public static readonly CustomSyncedValue<int> currentDay = new CustomSyncedValue<int>(configSync, "Current day", 1, Priority.First);
        public static readonly CustomSyncedValue<int> currentSeason = new CustomSyncedValue<int>(configSync, "Current season", 1, Priority.VeryHigh);

        public static readonly CustomSyncedValue<Dictionary<int, string>> seasonsSettingsJSON = new CustomSyncedValue<Dictionary<int, string>>(configSync, "Seasons settings JSON", new Dictionary<int, string>(), Priority.LowerThanNormal);
        public static readonly CustomSyncedValue<string> customEnvironmentsJSON = new CustomSyncedValue<string>(configSync, "Custom environments JSON", "", Priority.Normal);
        public static readonly CustomSyncedValue<string> customBiomeEnvironmentsJSON = new CustomSyncedValue<string>(configSync, "Custom biome environments JSON", "", Priority.Normal);
        public static readonly CustomSyncedValue<string> customEventsJSON = new CustomSyncedValue<string>(configSync, "Custom events JSON", "", Priority.Normal);
        public static readonly CustomSyncedValue<string> customLightingsJSON = new CustomSyncedValue<string>(configSync, "Custom lightings JSON", "", Priority.Normal);
        public static readonly CustomSyncedValue<string> customStatsJSON = new CustomSyncedValue<string>(configSync, "Custom stats JSON", "", Priority.Normal);
        public static readonly CustomSyncedValue<string> customTraderItemsJSON = new CustomSyncedValue<string>(configSync, "Custom traders JSON", "", Priority.Normal);
        public static readonly CustomSyncedValue<string> customWorldSettingsJSON = new CustomSyncedValue<string>(configSync, "Custom world settings JSON", "", Priority.Normal);

        public static readonly CustomSyncedValue<string> customMaterialSettingsJSON = new CustomSyncedValue<string>(configSync, "Custom material settings JSON", "", Priority.Low);
        public static readonly CustomSyncedValue<string> customColorSettingsJSON = new CustomSyncedValue<string>(configSync, "Custom color settings JSON", "", Priority.Low);
        public static readonly CustomSyncedValue<string> customColorReplacementJSON = new CustomSyncedValue<string>(configSync, "Custom color replacements JSON", "", Priority.Low);
        public static readonly CustomSyncedValue<string> customColorPositionsJSON = new CustomSyncedValue<string>(configSync, "Custom color positions JSON", "", Priority.Low);

        public static readonly List<BiomeEnvSetup> biomesDefault = new List<BiomeEnvSetup>();
        public static readonly List<RandomEvent> eventsDefault = new List<RandomEvent>();
        public static Color minimapBorderColor = Color.clear;

        public static WaitForFixedUpdate waitForFixedUpdate = new WaitForFixedUpdate();

        private static HashSet<string> _PlantsToControlGrowth;
        private static HashSet<string> _PlantsToSurviveWinter;
        private static readonly Dictionary<Vector3, bool> _cachedIgnoredPositions = new Dictionary<Vector3, bool>();

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

        public enum TimerFormatRaven
        {
            None,
            CurrentDay,
            TimeToEnd,
            CurrentDayAndTimeToEnd,
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

            haveGammaOfNightLights = GetComponent("GammaOfNightLights") != null;
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
            dayLengthSec = config("General", "Day length in seconds", defaultValue: 1800L, "Day length in seconds. Vanilla - 1800 seconds.");

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
            hideGrassInWinter = config("Season", "Hide grass in winter", defaultValue: true, "Hide grass in winter");
            hideGrassInWinterDays = config("Season", "Hide grass in winter day from to", defaultValue: new Vector2(3f, 10f), "Hide grass in winter");
            hideGrassListInWinter = config("Season", "Hide grass in set list in winter", defaultValue: "grasscross_meadows, grasscross_forest_brown, grasscross_forest, grasscross_swamp, grasscross_heath, grasscross_meadows_short, grasscross_heath_flower, grasscross_mistlands_short", "Hide set grass in winter");
            cropsDiesAfterSetDayInWinter = config("Season", "Crops will die after set day in winter", defaultValue: 3, "Crops and pickables will perish after set day in winter");
            cropsToSurviveInWinter = config("Season", "Crops will survive in winter", defaultValue: "Pickable_Carrot, Pickable_Barley, Pickable_Barley_Wild, Pickable_Flax, Pickable_Flax_Wild, Pickable_Thistle, Pickable_Mushroom_Magecap", "Crops and pickables from the list will not perish after set day in winter");

            seasonalStatsOutdoorsOnly.SettingChanged += (sender, args) => SE_Season.UpdateSeasonStatusEffectStats();
            hideGrassInWinter.SettingChanged += (sender, args) => ClutterVariantController.instance.UpdateColors();
            hideGrassInWinterDays.SettingChanged += (sender, args) => ClutterVariantController.instance.UpdateColors();
            hideGrassListInWinter.SettingChanged += (sender, args) => ClutterVariantController.instance.UpdateColors();
            cropsToSurviveInWinter.SettingChanged += (sender, args) => FillPickablesListToControlGrowth();

            showCurrentSeasonBuff = config("Season - Buff", "Show current season buff", defaultValue: true, "Show current season buff.");
            seasonsTimerFormat = config("Season - Buff", "Timer format", defaultValue: TimerFormat.CurrentDay, "What to show at season buff timer");
            hideSecondsInTimer = config("Season - Buff", "Hide seconds", defaultValue: true, "Hide seconds at season buff timer");
            showCurrentSeasonInRaven = config("Season - Buff", "Raven menu Show current season", defaultValue: true, "Show current season tooltip in Raven menu");
            seasonsTimerFormatInRaven = config("Season - Buff", "Raven menu Timer format", defaultValue: TimerFormatRaven.CurrentDayAndTimeToEnd, "What to show at season buff timer in Raven menu");

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

            enableFrozenWater = config("Season - Winter ocean", "Enable frozen water", defaultValue: true, "Enable frozen water in winter");
            waterFreezesInWinterDays = config("Season - Winter ocean", "Freeze the water at given days from to", defaultValue: new Vector2(6f, 9f), "Water will freeze in the first set day of winter and will be unfrozen after second set day");
            enableIceFloes = config("Season - Winter ocean", "Enable ice floes in winter", defaultValue: true, "Enable ice floes in winter");
            iceFloesInWinterDays = config("Season - Winter ocean", "Fill the water with ice floes at given days from to", defaultValue: new Vector2(4f, 10f), "Ice floes will be spawned in the first set day of winter and will be removed after second set day");
            amountOfIceFloesInWinterDays = config("Season - Winter ocean", "Amount of ice floes in one zone", defaultValue: new Vector2(10f, 20f), "Game will take random value between set numbers and will try to spawn that amount of ice floes in one zone (square 64x64)");
            enableNightMusicOnFrozenOcean = config("Season - Winter ocean", "Enable music while travelling frozen ocean at night", defaultValue: true, "Enables special frozen ocean music");
            frozenOceanSlipperiness = config("Season - Winter ocean", "Frozen ocean surface slipperiness factor", defaultValue: 1f, "Slipperiness factor of the frozen ocean surface");
            placeShipAboveFrozenOcean = config("Season - Winter ocean", "Place ship above frozen ocean surface", defaultValue: false, "Place ship above frozen ocean surface to move them without destroying");

            enableFrozenWater.SettingChanged += (sender, args) => ZoneSystemVariantController.UpdateWaterState();
            enableIceFloes.SettingChanged += (sender, args) => ZoneSystemVariantController.UpdateWaterState();
            waterFreezesInWinterDays.SettingChanged += (sender, args) => ZoneSystemVariantController.UpdateWaterState();
            iceFloesInWinterDays.SettingChanged += (sender, args) => ZoneSystemVariantController.UpdateWaterState();
            amountOfIceFloesInWinterDays.SettingChanged += (sender, args) => ZoneSystemVariantController.UpdateWaterState();
            placeShipAboveFrozenOcean.SettingChanged += (sender, args) => ZoneSystemVariantController.UpdateShipsPositions();

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

            localizationSeasonTooltipSpring = config("Seasons - Localization", "Season status effect tooltip - Spring has come", defaultValue: "Spring has come", "Message to be shown on the buff tooltip and Raven menu.");
            localizationSeasonTooltipSummer = config("Seasons - Localization", "Season status effect tooltip - Summer has come", defaultValue: "Summer has come", "Message to be shown on the buff tooltip and Raven menu.");
            localizationSeasonTooltipFall = config("Seasons - Localization", "Season status effect tooltip - Fall has come", defaultValue: "Fall has come", "Message to be shown on the buff tooltip and Raven menu.");
            localizationSeasonTooltipWinter = config("Seasons - Localization", "Season status effect tooltip - Winter has come", defaultValue: "Winter has come", "Message to be shown on the buff tooltip and Raven menu.");

            cacheStorageFormat = config("Test", "Cache format", defaultValue: CacheFormat.Binary, "Cache files format. Binary for fast loading single non humanreadable file. JSON for humanreadable cache.json + textures subdirectory.");
            logTime = config("Test", "Log time", defaultValue: false, "Log time info on state update");
            logFloes = config("Test", "Log ice floes", defaultValue: false, "Log ice floes spawning/destroying");
            rebuildCache = config("Test", "Rebuild cache", defaultValue: false, "Start cache rebuilding process", false);

            rebuildCache.SettingChanged += (sender, args) => StartCacheRebuild();

            configDirectory = Path.Combine(Paths.ConfigPath, pluginID);

            new ConsoleCommand("resetseasonscache", "Rebuild Seasons texture cache", delegate (ConsoleEventArgs args)
            {
                if (!seasonState.IsActive)
                {
                    args.Context.AddString($"Start the game before rebuilding cache");
                    return false;
                }

                StartCacheRebuild(fromConfig: false);

                args.Context.AddString($"Texture cache rebuilding process started");
                return true;
            });
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
            {
                LogInfo($"Loaded image: {fileInConfigFolder}");
                return tex.LoadImage(File.ReadAllBytes(fileInConfigFolder));
            }
            
            Assembly executingAssembly = Assembly.GetExecutingAssembly();

            string name = executingAssembly.GetManifestResourceNames().Single(str => str.EndsWith(filename));

            Stream resourceStream = executingAssembly.GetManifestResourceStream(name);

            byte[] data = new byte[resourceStream.Length];
            resourceStream.Read(data, 0, data.Length);

            return tex.LoadImage(data, true);
        }

        private string GetStringConfig(string fieldName)
        {
            return (GetType().GetField(fieldName).GetValue(this) as ConfigEntry<string>).Value;
        }
        
        private Sprite GetSpriteConfig(string fieldName)
        {
            return GetType().GetField(fieldName).GetValue(this) as Sprite;
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

        public static void FillPickablesListToControlGrowth()
        {
            _PlantsToControlGrowth = new HashSet<string>
            {
                "Pickable_Barley",
                "Pickable_Barley_Wild",
                "Pickable_Dandelion",
                "Pickable_Flax",
                "Pickable_Flax_Wild",
                "Pickable_SeedCarrot",
                "Pickable_SeedOnion",
                "Pickable_SeedTurnip",
                "Pickable_Thistle",
                "Pickable_Turnip",
            };

            _PlantsToSurviveWinter = new HashSet<string>(cropsToSurviveInWinter.Value.Split(',').Select(p => p.Trim().ToLower()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList());

            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab.TryGetComponent(out Pickable pickable) && pickable.m_itemPrefab != null && 
                    pickable.m_itemPrefab.TryGetComponent(out ItemDrop itemDrop) && itemDrop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable)
                    _PlantsToControlGrowth.Add(pickable.gameObject.name);
            }

            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab.TryGetComponent(out Plant plant) && plant.m_grownPrefabs != null)
                {
                    if (plant.m_grownPrefabs.Any(prefab => ControlPlantGrowth(prefab)))
                        _PlantsToControlGrowth.Add(plant.gameObject.name);

                    if (plant.m_grownPrefabs.Any(prefab => PlantWillSurviveWinter(prefab)))
                        _PlantsToSurviveWinter.Add(plant.gameObject.name.ToLower());
                }
            }
        }

        public static bool ControlPlantGrowth(GameObject gameObject)
        {
            return _PlantsToControlGrowth.Contains(PrefabVariantController.GetPrefabName(gameObject));
        }

        public static bool PlantWillSurviveWinter(GameObject gameObject)
        {
            return _PlantsToSurviveWinter.Contains(PrefabVariantController.GetPrefabName(gameObject).ToLower());
        }

        public static void InvalidatePositionsCache()
        {
            _cachedIgnoredPositions.Clear();
        }

        public static bool IsIgnoredPosition(Vector3 position)
        {
            if (position.y > 3000f)
                return true;

            if (WorldGenerator.instance == null)
                return true;

            if (_cachedIgnoredPositions.TryGetValue(position, out bool ignored))
                return ignored;

            float baseHeight = WorldGenerator.instance.GetBaseHeight(position.x, position.z, menuTerrain: false);

            if (baseHeight > WorldGenerator.mountainBaseHeightMin + 0.05f)
            {
                _cachedIgnoredPositions[position] = true;
                return true;
            }

            Heightmap.Biome biome = WorldGenerator.instance.GetBiome(position);

            ignored = biome == Heightmap.Biome.DeepNorth || biome == Heightmap.Biome.AshLands;
            
            _cachedIgnoredPositions[position] = ignored;
            return ignored;
        }
    
        public static void StartCacheRebuild(bool fromConfig = true)
        {
            if (fromConfig && !rebuildCache.Value)
                return;

            rebuildCache.Value = false;

            if (seasonState.IsActive)
                instance.StartCoroutine(instance.RebuildCache());
        }

        public IEnumerator RebuildCache()
        {
            SeasonalTextureVariants newTexturesVariants = new SeasonalTextureVariants();

            SeasonalTexturePrefabCache.SetCurrentTextureVariants(newTexturesVariants);

            PrefabVariantController.instance.RevertPrefabsState();
            ClutterVariantController.instance.RevertColors();

            yield return SeasonalTexturePrefabCache.FillWithGameData();

            if (newTexturesVariants.Initialized())
            {
                Stopwatch stopwatch = Stopwatch.StartNew();

                texturesVariants.controllers.Clear();
                texturesVariants.textures.Clear();
                texturesVariants.controllers.Copy(newTexturesVariants.controllers);
                texturesVariants.textures.Copy(newTexturesVariants.textures);

                if (Directory.Exists(CachedData.CacheDirectory()))
                    Directory.Delete(CachedData.CacheDirectory(), recursive: true);

                yield return texturesVariants.SaveCacheOnDisk();

                SeasonalTexturePrefabCache.SetCurrentTextureVariants(texturesVariants);

                ClutterVariantController.Reinitialize();
                PrefabVariantController.ReinitializePrefabVariants();

                LogInfo($"Colors reinitialized in {stopwatch.Elapsed.TotalSeconds,-4:F2} seconds");
            }

            yield return null;

            SeasonalTexturePrefabCache.SetCurrentTextureVariants(texturesVariants);

            PrefabVariantController.UpdatePrefabColors();
            ClutterVariantController.instance.UpdateColors();
        }

        public static void StartCoroutineSync(IEnumerator routine)
        {
            while (routine.MoveNext())
            {
                if (routine.Current != null)
                {
                    IEnumerator func;
                    try
                    {
                        func = (IEnumerator)routine.Current;
                    }
                    catch (InvalidCastException)
                    {
                        continue;
                    }
                    StartCoroutineSync(func);
                }
            }
        }

    }
}
