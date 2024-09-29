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
using System.Collections;

namespace Seasons
{
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    [BepInIncompatibility("RustyMods.Seasonality")]
    public class Seasons : BaseUnityPlugin
    {
        public const string pluginID = "shudnal.Seasons";
        public const string pluginName = "Seasons";
        public const string pluginVersion = "1.3.15";

        private readonly Harmony harmony = new Harmony(pluginID);

        internal static readonly ConfigSync configSync = new ConfigSync(pluginID) { DisplayName = pluginName, CurrentVersion = pluginVersion, MinimumRequiredVersion = pluginVersion };

        private static ConfigEntry<bool> configLocked;
        private static ConfigEntry<bool> loggingEnabled;
        public static ConfigEntry<long> dayLengthSec;

        public static ConfigEntry<CacheFormat> cacheStorageFormat;
        public static ConfigEntry<bool> logTime;
        public static ConfigEntry<bool> logFloes;
        public static ConfigEntry<bool> plainsSwampBorderFix;

        public static ConfigEntry<bool> overrideSeason;
        public static ConfigEntry<Season> seasonOverrided;

        public static ConfigEntry<bool> controlEnvironments;
        public static ConfigEntry<bool> controlRandomEvents;
        public static ConfigEntry<bool> controlLightings;
        public static ConfigEntry<bool> controlStats;
        public static ConfigEntry<bool> controlMinimap;
        public static ConfigEntry<bool> controlYggdrasil;
        public static ConfigEntry<bool> controlTraders;
        public static ConfigEntry<bool> controlGrass;
        public static ConfigEntry<bool> customTextures;

        public static ConfigEntry<bool> showCurrentSeasonBuff;
        public static ConfigEntry<TimerFormat> seasonsTimerFormat;
        public static ConfigEntry<bool> hideSecondsInTimer;
        public static ConfigEntry<bool> showCurrentSeasonInRaven;
        public static ConfigEntry<TimerFormat> seasonsTimerFormatInRaven;
        public static ConfigEntry<bool> overrideNewDayMessagesOnSeasonStartEnd;

        public static ConfigEntry<bool> disableBloomInWinter;
        public static ConfigEntry<Vector2> reduceSnowStormInWinter;
        public static ConfigEntry<bool> enableSeasonalItems;
        public static ConfigEntry<bool> preventDeathFromFreezing;
        public static ConfigEntry<bool> freezingSwimmingInWinter;
        public static ConfigEntry<bool> seasonalStatsOutdoorsOnly;
        public static ConfigEntry<bool> changeSeasonOnlyAfterSleep;
        public static ConfigEntry<int> cropsDiesAfterSetDayInWinter;
        public static ConfigEntry<string> cropsToSurviveInWinter;
        public static ConfigEntry<string> cropsToControlGrowth;
        public static ConfigEntry<string> woodListToControlDrop;
        public static ConfigEntry<string> meatListToControlDrop;
        public static ConfigEntry<bool> shieldGeneratorProtection;
        public static ConfigEntry<bool> shieldGeneratorOnlyWinter;
        public static ConfigEntry<bool> fireHeatProtectsFromPerish;

        public static ConfigEntry<bool> enableFrozenWater;
        public static ConfigEntry<Vector2> waterFreezesInWinterDays;
        public static ConfigEntry<bool> enableIceFloes;
        public static ConfigEntry<Vector2> iceFloesInWinterDays;
        public static ConfigEntry<Vector2> amountOfIceFloesInWinterDays;
        public static ConfigEntry<bool> enableNightMusicOnFrozenOcean;
        public static ConfigEntry<float> frozenOceanSlipperiness;
        public static ConfigEntry<bool> placeShipAboveFrozenOcean;
        public static ConfigEntry<Vector2> iceFloesScale;

        public static ConfigEntry<float> grassDefaultPatchSize;
        public static ConfigEntry<float> grassDefaultAmountScale;
        public static ConfigEntry<string> grassToControlSize;
        public static ConfigEntry<float> grassSizeDefaultScaleMin;
        public static ConfigEntry<float> grassSizeDefaultScaleMax;

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
        public static string cacheDirectory;

        public static SeasonalTextureVariants texturesVariants = new SeasonalTextureVariants();

        public static readonly CustomSyncedValue<int> currentSeasonDay = new CustomSyncedValue<int>(configSync, "Current season and day", 1, Priority.First);

        public static readonly CustomSyncedValue<string> customEnvironmentsJSON = new CustomSyncedValue<string>(configSync, "Custom environments JSON", "", Priority.HigherThanNormal);
        public static readonly CustomSyncedValue<string> customBiomeEnvironmentsJSON = new CustomSyncedValue<string>(configSync, "Custom biome environments JSON", "", Priority.Normal);
        public static readonly CustomSyncedValue<string> customEventsJSON = new CustomSyncedValue<string>(configSync, "Custom events JSON", "", Priority.Normal);
        public static readonly CustomSyncedValue<string> customLightingsJSON = new CustomSyncedValue<string>(configSync, "Custom lightings JSON", "", Priority.Normal);
        public static readonly CustomSyncedValue<string> customStatsJSON = new CustomSyncedValue<string>(configSync, "Custom stats JSON", "", Priority.Normal);
        public static readonly CustomSyncedValue<string> customTraderItemsJSON = new CustomSyncedValue<string>(configSync, "Custom traders JSON", "", Priority.Normal);
        public static readonly CustomSyncedValue<string> customWorldSettingsJSON = new CustomSyncedValue<string>(configSync, "Custom world settings JSON", "", Priority.Normal);
        public static readonly CustomSyncedValue<string> customGrassSettingsJSON = new CustomSyncedValue<string>(configSync, "Custom grass settings JSON", "", Priority.Normal);
        public static readonly CustomSyncedValue<string> customClutterSettingsJSON = new CustomSyncedValue<string>(configSync, "Custom clutter settings JSON", "", Priority.Normal);
        public static readonly CustomSyncedValue<string> customBiomeSettingsJSON = new CustomSyncedValue<string>(configSync, "Custom biome settings JSON", "", Priority.Normal);

        public static readonly CustomSyncedValue<Dictionary<int, string>> seasonsSettingsJSON = new CustomSyncedValue<Dictionary<int, string>>(configSync, "Seasons settings JSON", new Dictionary<int, string>(), Priority.LowerThanNormal);

        public static readonly CustomSyncedValue<string> customMaterialSettingsJSON = new CustomSyncedValue<string>(configSync, "Custom material settings JSON", "", Priority.Low);
        public static readonly CustomSyncedValue<string> customColorSettingsJSON = new CustomSyncedValue<string>(configSync, "Custom color settings JSON", "", Priority.Low);
        public static readonly CustomSyncedValue<string> customColorReplacementJSON = new CustomSyncedValue<string>(configSync, "Custom color replacements JSON", "", Priority.Low);
        public static readonly CustomSyncedValue<string> customColorPositionsJSON = new CustomSyncedValue<string>(configSync, "Custom color positions JSON", "", Priority.Low);

        public static readonly CustomSyncedValue<uint> cacheRevision = new CustomSyncedValue<uint>(configSync, "Cache revision", 0, Priority.VeryLow);

        public static readonly List<string> biomesDefault = new List<string>();
        public static Color minimapBorderColor = Color.clear;

        public static WaitForFixedUpdate waitForFixedUpdate = new WaitForFixedUpdate();

        private static HashSet<string> _PlantsToControlGrowth = new HashSet<string>();
        private static HashSet<string> _PlantsToSurviveWinter = new HashSet<string>();
        private static HashSet<string> _WoodToControlDrop = new HashSet<string>();
        private static HashSet<string> _MeatToControlDrop = new HashSet<string>();
        private static HashSet<string> _GrassToControlSize = new HashSet<string>();

        private static int _instanceChangeIDShieldGeneratorCache;
        private static readonly Dictionary<Vector2, bool> _cachedIgnoredPositions = new Dictionary<Vector2, bool>();

        private static readonly Dictionary<string, GameObject> _treeRegrowthPrefabs = new Dictionary<string, GameObject>();
        public static int _treeRegrowthHaveGrowSpace = "Seasons_HaveGrowSpace".GetStableHashCode();

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

            currentSeasonDay.ValueChanged += new Action(SeasonState.OnSeasonDayChange);

            customBiomeSettingsJSON.ValueChanged += new Action(SeasonState.UpdateBiomeSettings);
            customClutterSettingsJSON.ValueChanged += new Action(SeasonState.UpdateClutterSettings);
            customGrassSettingsJSON.ValueChanged += new Action(SeasonState.UpdateGrassSettings);
            customTraderItemsJSON.ValueChanged += new Action(SeasonState.UpdateTraderItems);
            customStatsJSON.ValueChanged += new Action(SeasonState.UpdateStats);
            customLightingsJSON.ValueChanged += new Action(SeasonState.UpdateLightings);
            customEventsJSON.ValueChanged += new Action(SeasonState.UpdateRandomEvents);
            customBiomeEnvironmentsJSON.ValueChanged += new Action(SeasonState.UpdateBiomeEnvironments);
            customEnvironmentsJSON.ValueChanged += new Action(SeasonState.UpdateSeasonEnvironments);
            customWorldSettingsJSON.ValueChanged += new Action(SeasonState.UpdateWorldSettings);
            seasonsSettingsJSON.ValueChanged += new Action(SeasonState.UpdateSeasonSettings);

            cacheRevision.ValueChanged += new Action(SeasonalTexturePrefabCache.OnCacheRevisionChange);

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
            dayLengthSec = config("General", "Day length in seconds", defaultValue: 1800L, "Day length in seconds. Vanilla - 1800 seconds.");

            controlEnvironments = config("Season - Control", "Control environments", defaultValue: true, "Enables seasonal weathers");
            controlRandomEvents = config("Season - Control", "Control random events", defaultValue: true, "Enables seasonal random events");
            controlLightings = config("Season - Control", "Control lightings", defaultValue: true, "Enables seasonal lightings change (basically gamma or brightness)");
            controlStats = config("Season - Control", "Control stats", defaultValue: true, "Enables seasonal stats change (status effect)");
            controlMinimap = config("Season - Control", "Control minimap", defaultValue: true, "Enables seasonal minimap colors");
            controlYggdrasil = config("Season - Control", "Control yggdrasil branch and roots", defaultValue: true, "Enables seasonal coloring of yggdrasil branch in the sky and roots on the ground");
            controlTraders = config("Season - Control", "Control trader seasonal items list", defaultValue: true, "Enables seasonal changes of trader additional item availability");
            controlGrass = config("Season - Control", "Control grass", defaultValue: true, "Enables seasonal changes of grass thickness, size and sparseness");
            customTextures = config("Season - Control", "Custom textures", defaultValue: true, "Enables custom textures");

            controlStats.SettingChanged += (sender, args) => SE_Season.UpdateSeasonStatusEffectStats();
            controlGrass.SettingChanged += (sender, args) => ClutterVariantController.Instance.UpdateGrass();
            customTextures.SettingChanged += (sender, args) => CustomTextures.UpdateTexturesOnChange();

            disableBloomInWinter = config("Season", "Disable Bloom in Winter", defaultValue: true, "Force disables Bloom graphics setting while in Winter and restores it in other seasons (it will not change Graphics setting, only disables posteffect)." +
                                                                                                   "\nBloom in Winter is what makes you blind with that much of white.");
            reduceSnowStormInWinter = config("Season", "Reduce SnowStorm particles in Winter", defaultValue: new Vector2(250, 1000), "Reduce SnowStorm particles emission rate and maximum amount. Vanilla values is 500:2000" +
                                                                                                   "\nFirst parameter is emission rate and second is max particles amount." +
                                                                                                   "\nHelps fps in Winter. Doesn't affect Mountains, Ashlands and DeepNorth." +
                                                                                                   "\nSet to 0:0 to return Vanilla behaviour.");
            enableSeasonalItems = config("Season", "Enable seasonal items", defaultValue: true, "Enables seasonal (Halloween, Midsummer, Yule) items in the corresponding season");
            preventDeathFromFreezing = config("Season", "Prevent death from freezing", defaultValue: true, "Prevents death from freezing when not in mountains or deep north");
            seasonalStatsOutdoorsOnly = config("Season", "Seasonal stats works only outdoors", defaultValue: true, "Make seasonal stats works only outdoors");
            freezingSwimmingInWinter = config("Season", "Get freezing when swimming in cold water in winter", defaultValue: true, "Swimming in cold water during winter will get you freezing debuff");
            changeSeasonOnlyAfterSleep = config("Season", "Change season only after sleep", defaultValue: false, "Season can be changed regular way only after sleep");
            cropsDiesAfterSetDayInWinter = config("Season", "Crops will die after set day in winter", defaultValue: 3, "Crops and pickables will perish after set day in winter");
            fireHeatProtectsFromPerish = config("Season", "Crops will survive if protected by fire", defaultValue: true, "Crops and pickables will not perish in winter if there are fire source nearby");
            cropsToSurviveInWinter = config("Season", "Crops will survive in winter", defaultValue: "Pickable_Carrot, Pickable_Barley, Pickable_Barley_Wild, Pickable_Flax, Pickable_Flax_Wild, Pickable_Thistle, Pickable_Mushroom_Magecap", "Crops and pickables from the list will not perish after set day in winter");
            cropsToControlGrowth = config("Season", "Crops to control growth", defaultValue: "Pickable_Barley, Pickable_Barley_Wild, Pickable_Dandelion, Pickable_Flax, Pickable_Flax_Wild, Pickable_SeedCarrot, Pickable_SeedOnion, Pickable_SeedTurnip, Pickable_Thistle, Pickable_Turnip", "All consumable crops will be added automatically. Set only unconsumable crops here." +
                                                                                                                                                                                                                                                                                              "Crops and pickables from the list will be controlled by growth multiplier in addition to consumable crops");
            woodListToControlDrop = config("Season", "Wood to control drop", defaultValue: "Wood, FineWood, RoundLog, ElderBark, YggdrasilWood", "Wood item names to control drop from trees");
            meatListToControlDrop = config("Season", "Meat to control drop", defaultValue: "RawMeat, DeerMeat, NeckTail, WolfMeat, LoxMeat, ChickenMeat, HareMeat, SerpentMeat", "Meat item names to control drop from characters");
            shieldGeneratorProtection = config("Season", "Shield generator protects from weather", defaultValue: true, "If enabled - objects inside shield generator dome will be protected from seasonal effects both positive and negative.");
            shieldGeneratorOnlyWinter = config("Season", "Shield generator protects from Winter only", defaultValue: true, "If enabled - objects inside shield generator dome will be protected from Winter only. If disabled - protection will work through all seasons.");

            seasonalStatsOutdoorsOnly.SettingChanged += (sender, args) => SE_Season.UpdateSeasonStatusEffectStats();
            cropsToSurviveInWinter.SettingChanged += (sender, args) => FillListsToControl();
            cropsToControlGrowth.SettingChanged += (sender, args) => FillListsToControl();
            woodListToControlDrop.SettingChanged += (sender, args) => FillListsToControl();
            meatListToControlDrop.SettingChanged += (sender, args) => FillListsToControl();
            disableBloomInWinter.SettingChanged += (sender, args) => seasonState.UpdateWinterBloomEffect();
            reduceSnowStormInWinter.SettingChanged += (sender, args) => ZoneSystemVariantController.SnowStormReduceParticlesChanged();
            

            grassDefaultPatchSize = config("Season - Grass", "Default patch size", defaultValue: 10f, "Default size of grass patch (sparseness or how wide a single grass \"node\" is across the ground)" +
                                                                                                     "Increase to make grass more sparse and decrease to make grass more tight");
            grassDefaultAmountScale = config("Season - Grass", "Default amount scale", defaultValue: 1.5f, "Default amount scale (grass density or how many grass patches created around you at once)");
            grassToControlSize = config("Season - Grass", "List of grass prefabs to control size", defaultValue: "instanced_meadows_grass, instanced_forest_groundcover_brown, instanced_forest_groundcover, instanced_swamp_grass, instanced_heathgrass, grasscross_heath_green, instanced_meadows_grass_short, instanced_heathflowers, instanced_mistlands_grass_short", "Grass with set prefabs to be hidden in winter and to change size in other seasons");

            grassSizeDefaultScaleMin = config("Season - Grass", "Default minimum size multiplier", defaultValue: 1f, "Default minimum size of grass will be multiplier by given number");
            grassSizeDefaultScaleMax = config("Season - Grass", "Default maximum size multiplier", defaultValue: 1f, "Default maximum size of grass will be multiplier by given number");

            grassDefaultPatchSize.SettingChanged += (sender, args) => ClutterVariantController.Instance.UpdateGrass();
            grassDefaultAmountScale.SettingChanged += (sender, args) => ClutterVariantController.Instance.UpdateGrass();
            grassToControlSize.SettingChanged += (sender, args) => ClutterVariantController.Instance.UpdateGrass();
            grassSizeDefaultScaleMin.SettingChanged += (sender, args) => ClutterVariantController.Instance.UpdateGrass();
            grassSizeDefaultScaleMax.SettingChanged += (sender, args) => ClutterVariantController.Instance.UpdateGrass();

            showCurrentSeasonBuff = config("Season - Buff", "Show current season buff", defaultValue: true, "Show current season buff.");
            seasonsTimerFormat = config("Season - Buff", "Timer format", defaultValue: TimerFormat.CurrentDay, "What to show at season buff timer");
            hideSecondsInTimer = config("Season - Buff", "Hide seconds", defaultValue: true, "Hide seconds at season buff timer");
            showCurrentSeasonInRaven = config("Season - Buff", "Raven menu Show current season", defaultValue: true, "Show current season tooltip in Raven menu");
            seasonsTimerFormatInRaven = config("Season - Buff", "Raven menu Timer format", defaultValue: TimerFormat.CurrentDayAndTimeToEnd, "What to show at season buff timer in Raven menu");
            overrideNewDayMessagesOnSeasonStartEnd = config("Season - Buff", "Show seasonal messages on morning", defaultValue: true, "Show messages \"Season is coming\" on last day and \"Season has come\" on first day of season");

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
            iceFloesScale = config("Season - Winter ocean", "Scale of ice floes", defaultValue: new Vector2(0.75f, 2f), "Size of spawned ice floe random to XYZ axes");
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
            plainsSwampBorderFix = config("Test", "Plains Swamp border fix", defaultValue: true, "Fix clipping into ground on Plains - Swamp border");

            plainsSwampBorderFix.SettingChanged += (sender, args) => ZoneSystemVariantController.UpdateTerrainColors();

            configDirectory = Path.Combine(Paths.ConfigPath, pluginID);
            cacheDirectory = Path.Combine(Paths.CachePath, pluginID);

            TerminalCommandsInit();
        }

        public void TerminalCommandsInit()
        {
            new ConsoleCommand("resetseasonscache", "Rebuild Seasons texture cache", delegate (ConsoleEventArgs args)
            {
                if (!SeasonState.IsActive)
                {
                    args.Context.AddString($"Start the game before rebuilding cache");
                    return false;
                }

                StartCacheRebuild();

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

        internal static void LoadIcon(string filename, ref Sprite icon)
        {
            Texture2D tex = new Texture2D(2, 2);
            if (LoadTexture(filename, ref tex))
                icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.zero);
        }

        internal static bool LoadTexture(string filename, ref Texture2D tex)
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

            tex.name = Path.GetFileNameWithoutExtension(filename);

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

        public static void FillListsToControl()
        {
            _PlantsToControlGrowth = ConfigToHashSet(cropsToControlGrowth.Value);
            _PlantsToSurviveWinter = ConfigToHashSet(cropsToSurviveInWinter.Value);

            _WoodToControlDrop = ConfigToHashSet(woodListToControlDrop.Value);
            _MeatToControlDrop = ConfigToHashSet(meatListToControlDrop.Value);

            _GrassToControlSize = ConfigToHashSet(grassToControlSize.Value);
            _GrassToControlSize.Add(ClutterVariantController.c_meadowsFlowersPrefabName.ToLower());
            _GrassToControlSize.Add(ClutterVariantController.c_forestBloomPrefabName.ToLower());
            _GrassToControlSize.Add(ClutterVariantController.c_swampGrassBloomName.ToLower());

            _treeRegrowthPrefabs.Clear();

            Dictionary<GameObject, GameObject> stubs = new Dictionary<GameObject, GameObject>();

            // At first fill all grown state pickables and stubs prefabs
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab.TryGetComponent(out Pickable pickable) && pickable.m_itemPrefab != null && 
                    pickable.m_itemPrefab.TryGetComponent(out ItemDrop itemDrop) && itemDrop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable)
                    _PlantsToControlGrowth.Add(pickable.gameObject.name.ToLower());

                if (prefab.TryGetComponent(out TreeBase tree) && tree.m_stubPrefab != null &&
                    tree.m_stubPrefab.TryGetComponent(out Destructible destructible) && IsTree(destructible))
                    stubs.Add(prefab, tree.m_stubPrefab);
            }

            // Add Plant that will later have Pickable in grown state and a stub of grown prefab
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab.TryGetComponent(out Plant plant) && plant.m_grownPrefabs != null)
                {
                    if (plant.m_grownPrefabs.Any(prefab => ControlPlantGrowth(prefab)))
                        _PlantsToControlGrowth.Add(plant.gameObject.name.ToLower());

                    if (plant.m_grownPrefabs.Any(prefab => PlantWillSurviveWinter(prefab)))
                        _PlantsToSurviveWinter.Add(plant.gameObject.name.ToLower());

                    foreach (GameObject grown in plant.m_grownPrefabs.Where(grown => stubs.ContainsKey(grown)))
                    {
                        string stubName = stubs[grown].name;
                        if (!_treeRegrowthPrefabs.ContainsKey(stubName))
                            _treeRegrowthPrefabs.Add(stubName, prefab);
                    }
                }
            }

            static HashSet<string> ConfigToHashSet(string configString)
            {
                return new HashSet<string>(configString.Split(',').Select(p => p.Trim().ToLower()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList());
            }

            static bool IsTree(Destructible destructible)
            {
                try
                {
                    return destructible.GetDestructibleType() == DestructibleType.Tree;
                }
                catch
                {
                    return destructible.m_destructibleType == DestructibleType.Tree;
                }
            }
        }

        public static bool ControlPlantGrowth(GameObject gameObject)
        {
            return _PlantsToControlGrowth.Contains(PrefabVariantController.GetPrefabName(gameObject).ToLower());
        }

        public static bool PlantWillSurviveWinter(GameObject gameObject)
        {
            return _PlantsToSurviveWinter.Contains(PrefabVariantController.GetPrefabName(gameObject).ToLower());
        }

        public static bool ControlWoodDrop(GameObject gameObject)
        {
            return _WoodToControlDrop.Contains(PrefabVariantController.GetPrefabName(gameObject).ToLower());
        }

        public static bool ControlMeatDrop(GameObject gameObject)
        {
            return _MeatToControlDrop.Contains(PrefabVariantController.GetPrefabName(gameObject).ToLower());
        }

        public static GameObject TreeToRegrowth(GameObject gameObject)
        {
            return _treeRegrowthPrefabs.GetValueSafe(PrefabVariantController.GetPrefabName(gameObject));
        }

        public static bool ControlGrassSize(GameObject gameObject)
        {
            return _GrassToControlSize.Contains(PrefabVariantController.GetPrefabName(gameObject).ToLower());
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

            Vector2 pos = new Vector2(position.x, position.z);
            if (_cachedIgnoredPositions.TryGetValue(pos, out bool ignored))
                return ignored;

            if (_cachedIgnoredPositions.Count > 15000)
                InvalidatePositionsCache();

            ignored = WorldGenerator.IsAshlands(position.x, position.z) || 
                      WorldGenerator.IsDeepnorth(position.x, position.z) || 
                      WorldGenerator.instance.GetBaseHeight(position.x, position.z, menuTerrain: false) > WorldGenerator.mountainBaseHeightMin + 0.05f;
            
            _cachedIgnoredPositions[pos] = ignored;
            return ignored;
        }

        public static bool IsShieldProtectionActive()
        {
            return shieldGeneratorProtection.Value && (!shieldGeneratorOnlyWinter.Value || seasonState.GetCurrentSeason() == Season.Winter);
        }

        public static bool IsShieldedPosition(Vector3 position)
        {
            return IsShieldProtectionActive() && ShieldGenerator.IsInsideShieldCached(position, ref _instanceChangeIDShieldGeneratorCache);
        }

        public static bool IsProtectedPosition(Vector3 position)
        {
            return IsIgnoredPosition(position) || IsShieldedPosition(position);
        }

        public static bool ProtectedWithHeat(Vector3 position)
        {
            return fireHeatProtectsFromPerish.Value && EffectArea.IsPointInsideArea(position, EffectArea.Type.Heat);
        }

        public static void StartCacheRebuild()
        {
            if (SeasonState.IsActive)
                instance.StartCoroutine(texturesVariants.RebuildCache());
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

        public static IEnumerator PickableSetPicked(Pickable pickable)
        {
            yield return waitForFixedUpdate;

            pickable.m_nview?.InvokeRPC(ZNetView.Everybody, "RPC_SetPicked", true);
        }

        public static IEnumerator ReplantTree(GameObject prefab, Vector3 position, Quaternion rotation, float scale)
        {
            yield return new WaitForSeconds(5f);

            if (ZoneSystem.instance.IsBlocked(position))
                yield break;

            if ((bool)EffectArea.IsPointInsideArea(position, EffectArea.Type.PlayerBase))
                yield break;

            GameObject result = Instantiate(prefab, position, rotation);

            yield return waitForFixedUpdate;

            if (result != null && result.TryGetComponent(out ZNetView m_nview) && m_nview.IsValid())
            {
                m_nview.GetZDO().Set(_treeRegrowthHaveGrowSpace, true);

                if (scale != 0f && result.TryGetComponent(out Plant plant))
                {
                    plant.m_minScale = scale;
                    plant.m_maxScale = scale;
                }
            }

            LogInfo($"Replanted {prefab}");
        }
    }
}
