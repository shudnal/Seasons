using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using static Terminal;

namespace Seasons
{
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    [BepInIncompatibility("RustyMods.Seasonality")]
    [BepInIncompatibility("TastyChickenLegs.LongerDays")]
    [BepInDependency(Compatibility.EpicLootCompat.GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Compatibility.MarketplaceCompat.GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Compatibility.EWDCompat.GUID, BepInDependency.DependencyFlags.SoftDependency)]
    public class Seasons : BaseUnityPlugin
    {
        public const string pluginID = "shudnal.Seasons";
        public const string pluginName = "Seasons";
        public const string pluginVersion = "1.8.1";

        private readonly Harmony harmony = new Harmony(pluginID);

        internal static readonly ConfigSync configSync = new ConfigSync(pluginID) { DisplayName = pluginName, CurrentVersion = pluginVersion, MinimumRequiredVersion = pluginVersion };

        private static ConfigEntry<bool> configLocked;
        private static ConfigEntry<bool> loggingEnabled;
        public static ConfigEntry<long> dayLengthSec;
        public static ConfigEntry<bool> enableLoadingTips;

        public static ConfigEntry<CacheFormat> cacheStorageFormat;
        public static ConfigEntry<bool> logTime;
        public static ConfigEntry<bool> logFloes;
        public static ConfigEntry<bool> logControllersTime;
        public static ConfigEntry<bool> plainsSwampBorderFix;
        public static ConfigEntry<bool> frozenKarvePositionFix;
        public static ConfigEntry<float> lastDayTerrainFactor;
        public static ConfigEntry<float> firstDayTerrainFactor;
        public static ConfigEntry<bool> runTextureCachingSync;

        public static ConfigEntry<bool> overrideSeason;
        public static ConfigEntry<Season> seasonOverrided;
        public static ConfigEntry<bool> overrideSeasonDay;
        public static ConfigEntry<int> seasonDayOverrided;

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
        public static ConfigEntry<bool> gettingWetInWinterCausesCold;
        public static ConfigEntry<bool> changeNightLengthGradually;
        public static ConfigEntry<bool> disableTorchWarmthInInterior;
        public static ConfigEntry<bool> summerHeatAddsExtraWarmCloth;
        public static ConfigEntry<bool> gettingWetInMountainsCausesCold;
        public static ConfigEntry<bool> wearing2WarmPiecesPreventsWetCold;
        public static ConfigEntry<bool> mountainInWinterRequires2WarmPieces;
        public static ConfigEntry<float> chanceToProduceACropInWinter;
        public static ConfigEntry<float> secondsToFreezeForCropInWinter;
        public static ConfigEntry<bool> cultivatedGroundTurnsIntoDirtInWinter;

        public static ConfigEntry<bool> enableFrozenWater;
        public static ConfigEntry<Vector2> waterFreezesInWinterDays;
        public static ConfigEntry<bool> enableIceFloes;
        public static ConfigEntry<Vector2> iceFloesInWinterDays;
        public static ConfigEntry<Vector2> amountOfIceFloesInWinterDays;
        public static ConfigEntry<bool> enableNightMusicOnFrozenOcean;
        public static ConfigEntry<float> frozenOceanSlipperiness;
        public static ConfigEntry<bool> placeShipAboveFrozenOcean;
        public static ConfigEntry<bool> placeFloatingContainersAboveFrozenOcean;
        public static ConfigEntry<Vector2> iceFloesScale;
        public static ConfigEntry<float> iceFloesHealth;

        public static ConfigEntry<string> summerHeatCoolingFoods;
        public static ConfigEntry<bool> summerHeatEnabled;
        public static ConfigEntry<Vector2> summerHeatDays;
        public static ConfigEntry<float> summerHeatTimeToMax;
        public static ConfigEntry<float> summerHeatGreenThreshold;
        public static ConfigEntry<float> summerHeatNeutralThreshold;
        public static ConfigEntry<float> summerHeatMaxThreshold;
        public static ConfigEntry<float> summerHeatNightFactor;
        public static ConfigEntry<float> summerHeatZoneHysteresis;
        public static ConfigEntry<float> summerHeatGreenFadeWidth;
        public static ConfigEntry<float> summerHeatRedRampWidth;
        public static ConfigEntry<float> summerHeatMaxOverflow;
        public static ConfigEntry<float> summerHeatDamageTickInterval;
        public static ConfigEntry<float> summerHeatDamageНealthPerTickMinHealthPercentage;
        public static ConfigEntry<float> summerHeatDamageНealthPerTick;
        public static ConfigEntry<HitData.HitType> summerHeatDamageHitType;
        public static ConfigEntry<bool> summerHeatDamageMaxOnly;
        public static ConfigEntry<float> summerHeatStaminaUseMultiplier;
        public static ConfigEntry<float> summerHeatAdrenalineMultiplier;
        public static ConfigEntry<float> summerHeatHealthRegenMultiplier;
        public static ConfigEntry<float> summerHeatStaminaRegenMultiplier;
        public static ConfigEntry<float> summerHeatEitrRegenMultiplier;
        public static ConfigEntry<string> summerHeatNonSunnyEnvironments;
        public static ConfigEntry<SummerHeatStatusEffectDisplay> summerHeatStatusEffectDisplay;
        public static ConfigEntry<bool> summerHeatRavenTechnicalInfo;
        public static ConfigEntry<SummerHeatDisplayMode> summerHeatDisplayMode;
        public static ConfigEntry<SummerHeatBarTagMode> summerHeatBarTagMode;
        public static ConfigEntry<int> summerHeatBarSegments;
        public static ConfigEntry<string> summerHeatBarSymbol;
        public static ConfigEntry<float> summerHeatBarMinBrightness;
        public static ConfigEntry<float> summerHeatBarMaxBrightness;
        public static ConfigEntry<Color> summerHeatBarBonusColor;
        public static ConfigEntry<Color> summerHeatBarNeutralColor;
        public static ConfigEntry<Color> summerHeatBarPenaltyColor;
        public static ConfigEntry<Color> summerHeatBarMaxColor;
        public static ConfigEntry<bool> summerHeatInstantHeatSources;
        public static ConfigEntry<bool> summerHeatCampFireAddsHeat;
        public static ConfigEntry<bool> summerHeatEncumberedAddsHeat;
        public static ConfigEntry<float> summerHeatWindEffectPercent;
        public static ConfigEntry<float> summerHeatNoonEffectPercent;
        public static ConfigEntry<bool> summerHeatArmorHeatEnabled;
        public static ConfigEntry<string> summerHeatOpenHelmetItems;
        public static ConfigEntry<string> summerHeatBareHeadHairItems;
        public static ConfigEntry<string> summerHeatLightCloakItems;
        public static ConfigEntry<string> summerHeatOpenChestItems;
        public static ConfigEntry<string> summerHeatOpenLegItems;
        public static ConfigEntry<float> summerHeatUncoveredHeadSunHeating;
        public static ConfigEntry<float> summerHeatUncoveredHeadShadeCooling;
        public static ConfigEntry<float> summerHeatOpenHelmetHeating;
        public static ConfigEntry<float> summerHeatClosedHelmetHeating;
        public static ConfigEntry<float> summerHeatClosedHelmetCoolingPenalty;
        public static ConfigEntry<float> summerHeatNoCloakHeatingReduction;
        public static ConfigEntry<float> summerHeatNoCloakCoolingBonus;
        public static ConfigEntry<float> summerHeatLightCloakHeatingReduction;
        public static ConfigEntry<float> summerHeatLightCloakCoolingBonus;
        public static ConfigEntry<float> summerHeatCloakHeating;
        public static ConfigEntry<float> summerHeatColdCloakHeating;
        public static ConfigEntry<float> summerHeatColdCloakCoolingPenalty;
        public static ConfigEntry<float> summerHeatEmptyArmorSlotHeatingReduction;
        public static ConfigEntry<float> summerHeatEmptyArmorSlotCoolingBonus;
        public static ConfigEntry<float> summerHeatOpenArmorHeatingReduction;
        public static ConfigEntry<float> summerHeatOpenArmorCoolingBonus;
        public static ConfigEntry<float> summerHeatClosedArmorHeating;
        public static ConfigEntry<float> summerHeatColdArmorHeating;
        public static ConfigEntry<float> summerHeatColdArmorCoolingPenalty;

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
        public static ConfigEntry<StationHover> hoverPickable;
        public static ConfigEntry<bool> seasonalMinimapBorderColor;

        public static ConfigEntry<bool> enableSeasonalGlobalKeys;
        public static ConfigEntry<string> seasonalGlobalKeyFall;
        public static ConfigEntry<string> seasonalGlobalKeySpring;
        public static ConfigEntry<string> seasonalGlobalKeySummer;
        public static ConfigEntry<string> seasonalGlobalKeyWinter;
        public static ConfigEntry<string> seasonalGlobalKeyDay;

        public static Seasons instance;
        public static SeasonState seasonState;
        internal const int seasonsCount = 4;
        public const int seasonColorVariants = 4;

        public static Sprite iconSpring;
        public static Sprite iconSummer;
        public static Sprite iconFall;
        public static Sprite iconWinter;
        public static Sprite iconWarm;

        public static Texture2D Minimap_Summer_ForestTex;
        public static Texture2D Minimap_Fall_ForestTex;
        public static Texture2D Minimap_Winter_ForestTex;

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

        public static Color minimapBorderColor = Color.clear;

        public static WaitForFixedUpdate waitForFixedUpdate = new WaitForFixedUpdate();
        public static WaitForSeconds waitFor1Second = new WaitForSeconds(1f);
        public static WaitForSeconds waitFor5Seconds = new WaitForSeconds(5f);

        internal static HashSet<string> _PlantsToControlGrowth = new HashSet<string>();
        internal static HashSet<string> _PlantsToSurviveWinter = new HashSet<string>();
        internal static HashSet<string> _WoodToControlDrop = new HashSet<string>();
        internal static HashSet<string> _MeatToControlDrop = new HashSet<string>();
        internal static HashSet<string> _GrassToControlSize = new HashSet<string>();

        private static int _instanceChangeIDShieldGeneratorCache;
        private static readonly Dictionary<Vector2, bool> _cachedIgnoredPositions = new Dictionary<Vector2, bool>();
        private static readonly Dictionary<Vector2, bool> _cachedShieldedPositions = new Dictionary<Vector2, bool>();
        private static int _cachedShieldedPositionsChangeID;

        private static readonly Dictionary<string, GameObject> _treeRegrowthPrefabs = new Dictionary<string, GameObject>();

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
            MinutesSeconds,
            Bar
        }

        public enum SummerHeatStatusEffectDisplay
        {
            StatusList,
            RavenMenuOnly,
            None
        }

        public enum SummerHeatDisplayMode
        {
            Bar,
            Percent,
            None
        }

        public enum SummerHeatBarTagMode
        {
            Sup,
            None,
            Sub
        }

        private void Awake()
        {
            instance = this;

            ConfigInit();
            _ = configSync.AddLockingConfigEntry(configLocked);

            Compatibility.EpicLootCompat.CheckForCompatibility();
            Compatibility.MarketplaceCompat.CheckForCompatibility();
            Compatibility.EWDCompat.CheckForCompatibility();
            Compatibility.HoneyPlusCompat.CheckForCompatibility();

            harmony.PatchAll();

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

            if (UseTextureControllers())
                LoadIcons();

            seasonState = new SeasonState();

            StartCoroutine(LocalizationManager.Localizer.Load());
        }

        private void FixedUpdate()
        {
            if (Player.m_localPlayer is Player player && player.IsOwner() && !player.IsDead())
            {
                SummerHeatController.EnsureForPlayer(player);

                if (player.GetSEMan() is SEMan seman && !seman.HaveStatusEffect(SeasonsVars.s_statusEffectSeasonHash))
                    seman.AddStatusEffect(SeasonsVars.s_statusEffectSeasonHash);
            }
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }

        public static void LogInfo(object data)
        {
            if (loggingEnabled.Value)
                instance.Logger.LogInfo(data);
        }
        
        public static void LogWarning(object data)
        {
            instance.Logger.LogWarning(data);
        }

        private ConfigDescription GetDescriptionSeparatedStrings(string description) =>
            Chainloader.PluginInfos.ContainsKey("_shudnal.ConfigurationManager")
                    ? new ConfigDescription(description)
                    : new ConfigDescription(description, null, new CustomConfigs.ConfigurationManagerAttributes { CustomDrawer = CustomConfigs.DrawSeparatedStrings(",") });

        public void ConfigInit()
        {
            config("General", "NexusID", 2654, "Nexus mod ID for updates", false);

            configLocked = config("General", "Lock Configuration", defaultValue: true, "Configuration is locked and can be changed by server admins only.");
            loggingEnabled = config("General", "Logging enabled", defaultValue: false, "Enable logging. [Not Synced with Server]", synchronizedSetting: false);
            dayLengthSec = config("General", "Day length in seconds", defaultValue: 1800L, "Day length in seconds. Vanilla - 1800 seconds. Set to 0 to disable.");
            enableLoadingTips = config("General", "Loading tips enabled", defaultValue: true, "Show seasonal tips on loading screen. [Not Synced with Server]", synchronizedSetting: false);

            enableLoadingTips.SettingChanged += (sender, args) => LoadingTips.UpdateLoadingTips();

            controlEnvironments = config("Season - Control", "Control environments", defaultValue: true, "Enables seasonal weathers");
            controlRandomEvents = config("Season - Control", "Control random events", defaultValue: true, "Enables seasonal random events");
            controlLightings = config("Season - Control", "Control lightings", defaultValue: true, "Enables seasonal lightings change (basically gamma or brightness)");
            controlStats = config("Season - Control", "Control stats", defaultValue: true, "Enables seasonal stats change (status effect)");
            controlMinimap = config("Season - Control", "Control minimap", defaultValue: true, "Enables seasonal minimap colors");
            controlYggdrasil = config("Season - Control", "Control yggdrasil branch and roots", defaultValue: true, "Enables seasonal coloring of yggdrasil branch in the sky and roots on the ground");
            controlTraders = config("Season - Control", "Control trader seasonal items list", defaultValue: true, "Enables seasonal changes of trader additional item availability");
            controlGrass = config("Season - Control", "Control grass", defaultValue: true, "Enables seasonal changes of grass thickness, size and sparseness");
            customTextures = config("Season - Control", "Custom textures", defaultValue: true, "Enables custom textures");

            controlRandomEvents.SettingChanged += (sender, args) => LoadingTips.UpdateLoadingTips();
            controlLightings.SettingChanged += (sender, args) => LoadingTips.UpdateLoadingTips();
            controlStats.SettingChanged += (sender, args) => { SE_Season.UpdateSeasonStatusEffectStats(); LoadingTips.UpdateLoadingTips(); };
            controlGrass.SettingChanged += (sender, args) => { ClutterVariantController.UpdateGrassOnSettingChanged(); LoadingTips.UpdateLoadingTips(); };
            controlTraders.SettingChanged += (sender, args) => LoadingTips.UpdateLoadingTips();
            customTextures.SettingChanged += (sender, args) => CustomTextures.UpdateTexturesOnChange();

            disableBloomInWinter = config("Season", "Disable Bloom in Winter", defaultValue: true, "Force disables Bloom graphics setting while in Winter and restores it in other seasons (it will not change Graphics setting, only disables posteffect)." +
                                                                                                   "\nBloom in Winter is what makes you blind with that much of white. [Not Synced with Server]", synchronizedSetting: false);
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
            chanceToProduceACropInWinter = config("Season", "Crops will have a chance to survive winter", defaultValue: 0.33f, new ConfigDescription("Crops and pickables will have given chance to produce a harvest instead of complete perish.",
                                                                                                                               new AcceptableValueRange<float>(0f, 1f),
                                                                                                                               new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            secondsToFreezeForCropInWinter = config("Season", "Crops will be freezing for seconds until perish", defaultValue: 120f, "After crop is hit by winter it will not perish immediately but will start to gradually freeze to death.");
            cultivatedGroundTurnsIntoDirtInWinter = config("Season", "Cultivated ground turns into regular Dirt in Winter", defaultValue: true, "With the onset of winter, any ground cultivated by player turns into ordinary dirt and has to be recultivated. It happens once per year.");

            cropsToSurviveInWinter = config("Season", "Crops will survive in winter", defaultValue: "Pickable_Carrot,Pickable_Barley,Pickable_Barley_Wild,Pickable_Flax,Pickable_Flax_Wild,Pickable_Thistle,Pickable_Mushroom_Magecap",
                                                                                                GetDescriptionSeparatedStrings("Crops and pickables from the list will not perish after set day in winter"));
            cropsToControlGrowth = config("Season", "Crops to control growth", defaultValue: "Pickable_Barley,Pickable_Barley_Wild,Pickable_Dandelion,Pickable_Flax,Pickable_Flax_Wild,Pickable_SeedCarrot,Pickable_SeedOnion,Pickable_SeedTurnip,Pickable_Thistle,Pickable_Turnip",
                                                                                            GetDescriptionSeparatedStrings("All consumable crops will be added automatically. Set only unconsumable crops here." +
                                                                                            "Crops and pickables from the list will be controlled by growth multiplier in addition to consumable crops"));


            woodListToControlDrop = config("Season", "Wood to control drop", defaultValue: "Wood,FineWood,RoundLog,ElderBark,YggdrasilWood",
                                                                                            GetDescriptionSeparatedStrings("Wood item names to control drop from trees"));
            meatListToControlDrop = config("Season", "Meat to control drop", defaultValue: "RawMeat,DeerMeat,NeckTail,WolfMeat,LoxMeat,ChickenMeat,HareMeat,SerpentMeat",
                                                                                            GetDescriptionSeparatedStrings("Meat item names to control drop from characters"));
            shieldGeneratorProtection = config("Season", "Shield generator protects from weather", defaultValue: true, "If enabled - objects inside shield generator dome will be protected from seasonal effects both positive and negative.");
            shieldGeneratorOnlyWinter = config("Season", "Shield generator protects from Winter only", defaultValue: true, "If enabled - objects inside shield generator dome will be protected from Winter only. If disabled - protection will work through all seasons.");
            gettingWetInWinterCausesCold = config("Season", "Getting Wet in winter causes Cold", defaultValue: true, "If you get Wet status during winter you will get Cold status," +
                                                                                                                     "\nunless you have frost resistance mead or you are near a fire or in shelter");
            changeNightLengthGradually = config("Season", "Change night length gradually", defaultValue: true, "If enabled - night length from seasonal settings will peak at mid season and gradually change to the next season." + 
                                                                                                             "\nIf disabled - it will be fixed value for any day of a season.");
            disableTorchWarmthInInterior = config("Season", "Disable torch warmth in dungeons in winter", defaultValue: true, "If enabled - torch will not provide heat in dungeons.");
            gettingWetInMountainsCausesCold = config("Season", "Getting Wet in Mountains causes Cold", defaultValue: true, "If you get Wet status in Mountains in dungeon you will get Cold status in all seasons," +
                                                                                                                        "\nunless you have frost resistance mead or you are near a fire or in shelter");
            wearing2WarmPiecesPreventsWetCold = config("Season", "Wearing 2 warm armor pieces prevents Cold caused by Wet", defaultValue: true, "If you get Wet status in Mountains or in Winter you will not get Cold status caused by" +
                "\nGetting Wet in winter causes Cold or Getting Wet in Mountains causes Cold configs");
            mountainInWinterRequires2WarmPieces = config("Season", "Mountains in Winter require 2 warm armor pieces", defaultValue: true, "If enabled - you have to wear 2 armor pieces with frost resistance in Winter or get frost resistance mead.");


            cropsDiesAfterSetDayInWinter.SettingChanged += (sender, args) => LoadingTips.UpdateLoadingTips();
            seasonalStatsOutdoorsOnly.SettingChanged += (sender, args) => SE_Season.UpdateSeasonStatusEffectStats();
            freezingSwimmingInWinter.SettingChanged += (sender, args) => LoadingTips.UpdateLoadingTips();
            cropsToSurviveInWinter.SettingChanged += (sender, args) => FillListsToControl();
            cropsToControlGrowth.SettingChanged += (sender, args) => FillListsToControl();
            woodListToControlDrop.SettingChanged += (sender, args) => FillListsToControl();
            meatListToControlDrop.SettingChanged += (sender, args) => FillListsToControl();
            disableBloomInWinter.SettingChanged += (sender, args) => seasonState?.UpdateWinterBloomEffect();
            reduceSnowStormInWinter.SettingChanged += (sender, args) => ZoneSystemVariantController.SnowStormReduceParticlesChanged();

            shieldGeneratorProtection.SettingChanged += (sender, args) => PrefabVariantController.UpdateShieldStateAfterConfigChange();
            shieldGeneratorOnlyWinter.SettingChanged += (sender, args) => PrefabVariantController.UpdateShieldStateAfterConfigChange();


            grassDefaultPatchSize = config("Season - Grass", "Default patch size", defaultValue: 10f, "Default size of grass patch (sparseness or how wide a single grass \"node\" is across the ground)" +
                                                                                                     "Increase to make grass more sparse and decrease to make grass more tight");
            grassDefaultAmountScale = config("Season - Grass", "Default amount scale", defaultValue: 1.5f, "Default amount scale (grass density or how many grass patches created around you at once)");
            grassToControlSize = config("Season - Grass", "List of grass prefabs to control size", defaultValue: "instanced_meadows_grass,instanced_forest_groundcover_brown,instanced_forest_groundcover,instanced_swamp_grass,instanced_heathgrass,grasscross_heath_green,instanced_meadows_grass_short,instanced_heathflowers,instanced_mistlands_grass_short",
                                                                                            GetDescriptionSeparatedStrings("Grass with set prefabs to be hidden in winter and to change size in other seasons"));

            grassSizeDefaultScaleMin = config("Season - Grass", "Default minimum size multiplier", defaultValue: 1f, "Default minimum size of grass will be multiplier by given number");
            grassSizeDefaultScaleMax = config("Season - Grass", "Default maximum size multiplier", defaultValue: 1f, "Default maximum size of grass will be multiplier by given number");

            grassDefaultPatchSize.SettingChanged += (sender, args) => ClutterVariantController.UpdateGrassOnSettingChanged();
            grassDefaultAmountScale.SettingChanged += (sender, args) => ClutterVariantController.UpdateGrassOnSettingChanged();
            grassToControlSize.SettingChanged += (sender, args) => ClutterVariantController.UpdateGrassOnSettingChanged();
            grassSizeDefaultScaleMin.SettingChanged += (sender, args) => ClutterVariantController.UpdateGrassOnSettingChanged();
            grassSizeDefaultScaleMax.SettingChanged += (sender, args) => ClutterVariantController.UpdateGrassOnSettingChanged();

            showCurrentSeasonBuff = config("Season - Buff", "Show current season buff", defaultValue: true, "Show current season buff.");
            seasonsTimerFormat = config("Season - Buff", "Timer format", defaultValue: TimerFormat.CurrentDay, "What to show at season buff timer");
            hideSecondsInTimer = config("Season - Buff", "Hide seconds", defaultValue: true, "Hide seconds at season buff timer");
            showCurrentSeasonInRaven = config("Season - Buff", "Raven menu Show current season", defaultValue: true, "Show current season tooltip in Raven menu");
            seasonsTimerFormatInRaven = config("Season - Buff", "Raven menu Timer format", defaultValue: TimerFormat.CurrentDayAndTimeToEnd, "What to show at season buff timer in Raven menu");
            overrideNewDayMessagesOnSeasonStartEnd = config("Season - Buff", "Show seasonal messages on morning", defaultValue: true, "Show messages \"Season is coming\" on last day and \"Season has come\" on first day of season");

            EventHandler seasonStatusDisplayHandler = (sender, args) =>
            {
                StatusEffectHud.EnsureTimeTextRichText();
                SE_Season.UpdateSeasonStatusEffectStats();
            };
            showCurrentSeasonBuff.SettingChanged += seasonStatusDisplayHandler;
            seasonsTimerFormat.SettingChanged += seasonStatusDisplayHandler;
            hideSecondsInTimer.SettingChanged += seasonStatusDisplayHandler;
            showCurrentSeasonInRaven.SettingChanged += seasonStatusDisplayHandler;
            seasonsTimerFormatInRaven.SettingChanged += seasonStatusDisplayHandler;

            showFadeOnSeasonChange = config("Season - Fade", "Show fade effect on season change", defaultValue: true, "Show black fade loading screen when season is changed.");
            fadeOnSeasonChangeDuration = config("Season - Fade", "Duration of fade effect", defaultValue: 0.5f, "Fade duration");

            hoverBeeHive = config("Season - UI", "Bee Hive Hover", defaultValue: StationHover.Vanilla, "Hover text for bee hive.");
            hoverBeeHiveTotal = config("Season - UI", "Bee Hive Show total", defaultValue: true, "Show total needed time/percent for bee hive.");
            hoverPlant = config("Season - UI", "Plants Hover", defaultValue: StationHover.Vanilla, "Hover text for plants.");
            hoverPickable = config("Season - UI", "Pickables Hover", defaultValue: StationHover.Vanilla, "Hover text for pickables.");
            seasonalMinimapBorderColor = config("Season - UI", "Seasonal colored minimap border", defaultValue: true, "Change minimap border color according to current season.");

            overrideSeason = config("Season - Override", "Override", defaultValue: false, "The season will be overridden by set season.");
            seasonOverrided = config("Season - Override", "Season", defaultValue: Season.Spring, "The season to set.");
            overrideSeasonDay = config("Season - Override", "Day override", defaultValue: false, "The season day will be overridden by set day.");
            seasonDayOverrided = config("Season - Override", "Day", defaultValue: 1, "The season day to set.");
            
            overrideSeason.SettingChanged += (sender, args) => SeasonState.CheckSeasonChange();
            seasonOverrided.SettingChanged += (sender, args) => SeasonState.CheckSeasonChange();
            overrideSeasonDay.SettingChanged += (sender, args) => SeasonState.CheckSeasonChange();
            seasonDayOverrided.SettingChanged += (sender, args) => SeasonState.CheckSeasonChange();

            enableFrozenWater = config("Season - Winter ocean", "Enable frozen water", defaultValue: true, "Enable frozen water in winter");
            waterFreezesInWinterDays = config("Season - Winter ocean", "Freeze the water at given days from to", defaultValue: new Vector2(6f, 9f), "Water will freeze in the first set day of winter and will be unfrozen after second set day");
            enableIceFloes = config("Season - Winter ocean", "Enable ice floes in winter", defaultValue: true, "Enable ice floes in winter");
            iceFloesInWinterDays = config("Season - Winter ocean", "Fill the water with ice floes at given days from to", defaultValue: new Vector2(4f, 10f), "Ice floes will be spawned in the first set day of winter and will be removed after second set day");
            amountOfIceFloesInWinterDays = config("Season - Winter ocean", "Amount of ice floes in one zone", defaultValue: new Vector2(10f, 20f), "Game will take random value between set numbers and will try to spawn that amount of ice floes in one zone (square 64x64)");
            iceFloesScale = config("Season - Winter ocean", "Scale of ice floes", defaultValue: new Vector2(0.75f, 2f), "Size of spawned ice floe random to XYZ axes");
            iceFloesHealth = config("Season - Winter ocean", "Health of ice floes", defaultValue: 20f, "Health of ice floe of average size. Health changes proportionally the volume of an ice floe. Floes respawn is required to apply changes.");
            enableNightMusicOnFrozenOcean = config("Season - Winter ocean", "Enable music while travelling frozen ocean at night", defaultValue: true, "Enables special frozen ocean music");
            frozenOceanSlipperiness = config("Season - Winter ocean", "Frozen ocean surface slipperiness factor", defaultValue: 1f, "Slipperiness factor of the frozen ocean surface");
            placeShipAboveFrozenOcean = config("Season - Winter ocean", "Place ship above frozen ocean surface", defaultValue: false, "Place ship above frozen ocean surface to move them without destroying");
            placeFloatingContainersAboveFrozenOcean = config("Season - Winter ocean", "Place floating containers above frozen ocean surface", defaultValue: false, "Place floating containers above frozen ocean surface");

            enableFrozenWater.SettingChanged += (sender, args) => { ZoneSystemVariantController.UpdateWaterState(); LoadingTips.UpdateLoadingTips(); };
            enableIceFloes.SettingChanged += (sender, args) => ZoneSystemVariantController.UpdateWaterState();
            waterFreezesInWinterDays.SettingChanged += (sender, args) => ZoneSystemVariantController.UpdateWaterState();
            iceFloesInWinterDays.SettingChanged += (sender, args) => ZoneSystemVariantController.UpdateWaterState();
            amountOfIceFloesInWinterDays.SettingChanged += (sender, args) => ZoneSystemVariantController.UpdateWaterState();
            placeShipAboveFrozenOcean.SettingChanged += (sender, args) => ZoneSystemVariantController.UpdateShipsPositions();
            placeFloatingContainersAboveFrozenOcean.SettingChanged += (sender, args) => ZoneSystemVariantController.UpdateFloatingPositions();

            summerHeatCoolingFoods = config("Season - Summer heat", "Cooling foods", defaultValue: "Eyescream,$item_eyescream", GetDescriptionSeparatedStrings("Foods that help cool you down in summer. Use prefab names or localization keys. While a cooling food is active, new heat bursts are blocked. It also protects from the older warm-clothes overheat status when the main Summer Heat mechanic is disabled."));
            summerHeatEnabled = config("Season - Summer heat", "Enabled", defaultValue: true, "Turns the new Summer Heat mechanic on or off. When disabled, the heat meter, status effect, bonuses, penalties and visual heat effects are removed. The older warm-clothes overheat status can still work if 'Warm clothes add heat' is enabled.");
            summerHeatAddsExtraWarmCloth = config("Season - Summer heat", "Warm clothes add heat", defaultValue: true, "Controls the older warm-clothes overheat status. The new Summer Heat mechanic uses the Armor heat settings below, so individual armor pieces can affect heat in a more detailed way.");
            summerHeatDays = config("Season - Summer heat", "Heat days from to", defaultValue: new Vector2(4f, 7f), "Which summer days can become dangerously hot. The first number starts the hot period, the second number ends it.");
            summerHeatTimeToMax = config("Season - Summer heat", "Time to max heat", defaultValue: 180f, "How long it takes to reach 100% heat while standing in direct sun with no shade, water or other cooling help.");
            summerHeatGreenThreshold = config("Season - Summer heat", "Comfortable heat", defaultValue: 25f, new ConfigDescription("Heat percent where the warm-weather bonus is strongest. With the default 25% threshold and 20% bonus range, the bonus starts at 5%, reaches full strength at 25%, then fades out by 45%.", new AcceptableValueRange<float>(0f, 100f)));
            summerHeatNeutralThreshold = config("Season - Summer heat", "Too hot threshold", defaultValue: 60f, new ConfigDescription("Heat percent where penalties begin. With the default 60% threshold and 20% penalty range, negative effects start at 60% and reach full strength at 80%.", new AcceptableValueRange<float>(0f, 100f)));
            summerHeatMaxThreshold = config("Season - Summer heat", "Overheated threshold", defaultValue: 95f, new ConfigDescription("Heat percent where the worst heat state begins. This is where the soft HP cap damage can become active.", new AcceptableValueRange<float>(0f, 100f)));
            summerHeatNightFactor = config("Season - Summer heat", "Night warmth factor", defaultValue: 0.5f, new ConfigDescription("How warm summer nights remain compared to daytime. At 50%, night can only hold about half of the daytime heat: the air is still warm, but without direct sun you should cool down toward a safer level instead of building up to full overheating.", new AcceptableValueRange<float>(0.1f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatZoneHysteresis = config("Season - Summer heat", "State switch buffer", defaultValue: 10f, new ConfigDescription("Small buffer around heat states, in percentage points. It prevents the status from rapidly switching back and forth when your heat is close to a boundary.", new AcceptableValueRange<float>(0f, 100f)));
            summerHeatGreenFadeWidth = config("Season - Summer heat", "Comfortable heat range", defaultValue: 20f, new ConfigDescription("How wide the bonus area is around comfortable heat, in percentage points. With the default 25% threshold and 20% range, the bonus starts at 5%, reaches full strength at 25%, then fades out by 45%.", new AcceptableValueRange<float>(0f, 100f)));
            summerHeatRedRampWidth = config("Season - Summer heat", "Penalty buildup range", defaultValue: 20f, new ConfigDescription("How gradually penalties build after you become too hot, in percentage points. With the default 60% threshold and 20% range, negative effects start at 60% and reach full strength at 80%.", new AcceptableValueRange<float>(0f, 100f)));
            summerHeatMaxOverflow = config("Season - Summer heat", "Overheat buffer", defaultValue: 5f, new ConfigDescription("Small hidden heat reserve above 100%, in percentage points. It makes full overheating take a little time to cool off instead of disappearing instantly.", new AcceptableValueRange<float>(0f, 100f)));
            summerHeatNonSunnyEnvironments = config("Season - Summer heat", "Weather without direct sun", defaultValue: "Rain,LightRain,MistlandsRain,SlimeRain,SnowStorm,Thunder,MistlandsThunder,AshlandsThunder,Ashlands_SeaStorm,Mist,Ashlands_Misty,Ashlands_RainCinder,Ashlands_CinderRain", GetDescriptionSeparatedStrings("Technical weather list. Use internal EnvMan weather object names or particle system names, separated by commas. If the current weather matches one of these names, Summer Heat treats the sky as not sunny and direct sunlight heat stops."));
            summerHeatInstantHeatSources = config("Season - Summer heat", "Actions add heat", defaultValue: true, "If enabled, jumps, attacks, dodges and blocks add small heat bursts during Summer Heat.");
            summerHeatCampFireAddsHeat = config("Season - Summer heat", "Campfire adds heat", defaultValue: true, "If enabled, standing near a campfire can warm you up even during summer.");
            summerHeatEncumberedAddsHeat = config("Season - Summer heat", "Heavy load adds heat", defaultValue: true, "If enabled, carrying too much weight makes you heat up slowly.");
            summerHeatWindEffectPercent = config("Season - Summer heat", "Wind effect", defaultValue: 0.25f, new ConfigDescription("How strongly wind changes heating and cooling. With 25%, still air makes heating faster and cooling slower; strong wind does the opposite.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatNoonEffectPercent = config("Season - Summer heat", "Midday effect", defaultValue: 0.25f, new ConfigDescription("How much stronger the sun feels around midday. With 25%, heat builds faster and cools slower near noon.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));

            summerHeatArmorHeatEnabled = config("Season - Summer heat - Armor heat", "Enabled", defaultValue: true, "Let equipped armor change how quickly you heat up and cool down. This replaces the simple 'two warm pieces add heat' rule inside the new Summer Heat mechanic.");
            summerHeatOpenHelmetItems = config("Season - Summer heat - Armor heat", "Open helmets", defaultValue: "HelmetAshlandsMediumHood,HelmetBerserkerUndead,HelmetBronze,HelmetDverger,HelmetFishingHat,HelmetMidsummerCrown,HelmetTrollLeather,HelmetHat1,HelmetHat2,HelmetHat5,HelmetHat6,HelmetHat7,HelmetHat10", GetDescriptionSeparatedStrings("Helmets that leave enough of the head open to trap less heat. Use prefab names or localization keys, separated by commas. Items in this list heat less than closed helmets."));
            summerHeatBareHeadHairItems = config("Season - Summer heat - Armor heat", "Bare head hairstyles", defaultValue: "HairNone,Hair9,Hair24,Hair14", GetDescriptionSeparatedStrings("Hairstyles that leave the head fully exposed to heat. Use internal hair item names, separated by commas. When no helmet is equipped, listed hairstyles add another 20% heating in direct sun and another 20% cooling outside direct sun. For an empty hair slot, use none, bald, balded or HairNone."));
            summerHeatLightCloakItems = config("Season - Summer heat - Armor heat", "Light cloaks", defaultValue: "", GetDescriptionSeparatedStrings("Cloaks that should behave like light summer clothing. Use prefab names or localization keys, separated by commas. Items in this list reduce heat gain and help cooling a little."));
            summerHeatOpenChestItems = config("Season - Summer heat - Armor heat", "Open chest armor", defaultValue: "ArmorBerserkerChest,ArmorBerserkerUndeadChest", GetDescriptionSeparatedStrings("Chest pieces that leave the body more open. Use prefab names or localization keys, separated by commas. Items in this list heat less than closed armor."));
            summerHeatOpenLegItems = config("Season - Summer heat - Armor heat", "Open leg armor", defaultValue: "ArmorBerserkerLegs,ArmorBerserkerUndeadLegs", GetDescriptionSeparatedStrings("Leg pieces that leave the body more open. Use prefab names or localization keys, separated by commas. Items in this list heat less than closed armor."));
            summerHeatUncoveredHeadSunHeating = config("Season - Summer heat - Armor heat", "Uncovered head sun heating", defaultValue: 0.25f, new ConfigDescription("How much faster you heat up in direct sun with no helmet. At 25%, sun heat becomes 25% stronger.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatUncoveredHeadShadeCooling = config("Season - Summer heat - Armor heat", "Uncovered head shade cooling", defaultValue: 0.2f, new ConfigDescription("How much faster you cool down without a helmet when you are not in direct sun. At 20%, cooling becomes 20% stronger.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatOpenHelmetHeating = config("Season - Summer heat - Armor heat", "Open helmet heating", defaultValue: 0.1f, new ConfigDescription("Extra heat gain from helmets listed as open. At 10%, you heat up 10% faster while wearing one.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatClosedHelmetHeating = config("Season - Summer heat - Armor heat", "Closed helmet heating", defaultValue: 0.2f, new ConfigDescription("Extra heat gain from any helmet not listed as open. At 20%, you heat up 20% faster while wearing one.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatClosedHelmetCoolingPenalty = config("Season - Summer heat - Armor heat", "Closed helmet cooling penalty", defaultValue: 0.1f, new ConfigDescription("How much closed helmets slow cooling. At 10%, cooling becomes 10% weaker while wearing one.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatNoCloakHeatingReduction = config("Season - Summer heat - Armor heat", "No cloak heating reduction", defaultValue: 0.1f, new ConfigDescription("How much slower you heat up without a cloak. At 10%, heat gain becomes 10% weaker.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatNoCloakCoolingBonus = config("Season - Summer heat - Armor heat", "No cloak cooling bonus", defaultValue: 0.15f, new ConfigDescription("How much faster you cool down without a cloak. At 15%, cooling becomes 15% stronger.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatLightCloakHeatingReduction = config("Season - Summer heat - Armor heat", "Light cloak heating reduction", defaultValue: 0.05f, new ConfigDescription("How much slower you heat up with a cloak listed as light. At 5%, heat gain becomes 5% weaker.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatLightCloakCoolingBonus = config("Season - Summer heat - Armor heat", "Light cloak cooling bonus", defaultValue: 0.1f, new ConfigDescription("How much faster you cool down with a cloak listed as light. At 10%, cooling becomes 10% stronger.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatCloakHeating = config("Season - Summer heat - Armor heat", "Cloak heating", defaultValue: 0.15f, new ConfigDescription("Extra heat gain from ordinary cloaks. At 15%, you heat up 15% faster while wearing one.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatColdCloakHeating = config("Season - Summer heat - Armor heat", "Cold cloak heating", defaultValue: 0.3f, new ConfigDescription("Extra heat gain from cloaks with frost resistance. At 30%, you heat up 30% faster while wearing one.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatColdCloakCoolingPenalty = config("Season - Summer heat - Armor heat", "Cold cloak cooling penalty", defaultValue: 0.1f, new ConfigDescription("How much frost-resistant cloaks slow cooling. At 10%, cooling becomes 10% weaker.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatEmptyArmorSlotHeatingReduction = config("Season - Summer heat - Armor heat", "Empty body slot heating reduction", defaultValue: 0.1f, new ConfigDescription("How much slower you heat up for each empty chest or leg armor slot. At 10%, each empty slot reduces heat gain by 10%.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatEmptyArmorSlotCoolingBonus = config("Season - Summer heat - Armor heat", "Empty body slot cooling bonus", defaultValue: 0.15f, new ConfigDescription("How much faster you cool down for each empty chest or leg armor slot. At 15%, each empty slot improves cooling by 15%.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatOpenArmorHeatingReduction = config("Season - Summer heat - Armor heat", "Open armor heating reduction", defaultValue: 0.05f, new ConfigDescription("How much slower you heat up with chest or leg armor listed as open. At 5%, each matching item reduces heat gain by 5%.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatOpenArmorCoolingBonus = config("Season - Summer heat - Armor heat", "Open armor cooling bonus", defaultValue: 0.05f, new ConfigDescription("How much faster you cool down with chest or leg armor listed as open. At 5%, each matching item improves cooling by 5%.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatClosedArmorHeating = config("Season - Summer heat - Armor heat", "Closed armor heating", defaultValue: 0.1f, new ConfigDescription("Extra heat gain from ordinary chest and leg armor. At 10%, each closed item makes you heat up 10% faster.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatColdArmorHeating = config("Season - Summer heat - Armor heat", "Cold armor heating", defaultValue: 0.25f, new ConfigDescription("Extra heat gain from chest and leg armor with frost resistance. At 25%, each warm item makes you heat up 25% faster.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatColdArmorCoolingPenalty = config("Season - Summer heat - Armor heat", "Cold armor cooling penalty", defaultValue: 0.1f, new ConfigDescription("How much frost-resistant chest and leg armor slows cooling. At 10%, each warm item makes cooling 10% weaker.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));

            summerHeatDamageTickInterval = config("Season - Summer heat - Damage in red zone", "Damage tick interval", defaultValue: 2f, "Seconds between damage ticks while heat is forcing your health down.");
            summerHeatDamageНealthPerTickMinHealthPercentage = config("Season - Summer heat - Damage in red zone", "Soft HP cap percentage", defaultValue: 0.8f, new ConfigDescription("Lowest health percentage that Summer Heat can push you toward. You will take constant damage when your HP is higher than this percent. This damage only lowers current HP toward the set mark; it does not reduce your real maximum HP.", new AcceptableValueRange<float>(0.05f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatDamageНealthPerTick = config("Season - Summer heat - Damage in red zone", "Damage per tick", defaultValue: 2f, "How much damage is dealt each tick while Summer Heat is pushing your HP down toward the soft cap.");
            summerHeatDamageHitType = config("Season - Summer heat - Damage in red zone", "Damage hit type", defaultValue: HitData.HitType.Self, "How the game should mark this damage. Most players can leave this unchanged.");
            summerHeatDamageMaxOnly = config("Season - Summer heat - Damage in red zone", "Damage only when overheated", defaultValue: true, "If enabled, HP cap damage waits until the status reaches Overheated. If disabled, the damage can start as soon as the top heat damage ramp begins near the end of the red zone.");

            summerHeatStaminaUseMultiplier = config("Season - Summer heat - Multipliers", "Stamina use effect", defaultValue: 0.2f, new ConfigDescription("How strongly heat changes running stamina cost. With 20%, red heat can make running cost up to 20% more, while comfortable heat can make it cost up to 20% less.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatAdrenalineMultiplier = config("Season - Summer heat - Multipliers", "Adrenaline effect", defaultValue: 0.15f, new ConfigDescription("How strongly heat changes adrenaline use. With 15%, red heat can cost up to 15% more, while comfortable heat can cost up to 15% less.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatHealthRegenMultiplier = config("Season - Summer heat - Multipliers", "Health regen effect", defaultValue: 0.15f, new ConfigDescription("How strongly heat changes health regeneration. With 15%, red heat can reduce regeneration by up to 15%, while comfortable heat can increase it by up to 15%.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatStaminaRegenMultiplier = config("Season - Summer heat - Multipliers", "Stamina regen effect", defaultValue: 0.15f, new ConfigDescription("How strongly heat changes stamina regeneration. With 15%, red heat can reduce regeneration by up to 15%, while comfortable heat can increase it by up to 15%.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatEitrRegenMultiplier = config("Season - Summer heat - Multipliers", "Eitr regen effect", defaultValue: 0.1f, new ConfigDescription("How strongly heat changes eitr regeneration. With 10%, red heat can reduce regeneration by up to 10%, while comfortable heat can increase it by up to 10%.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));

            summerHeatStatusEffectDisplay = config("Season - Summer heat - Status", "Status effect visibility", defaultValue: SummerHeatStatusEffectDisplay.StatusList, "Where to show the Summer Heat status effect. StatusList shows it normally, RavenMenuOnly hides it from the status list but keeps it in the Raven active effects menu, None hides it everywhere.");
            summerHeatRavenTechnicalInfo = config("Season - Summer heat - Status", "Raven menu technical info", defaultValue: false, "Show extra heat numbers in the Raven active effects menu. Useful for server admins while tuning the mechanic.");
            summerHeatDisplayMode = config("Season - Summer heat - Status", "Value display mode", defaultValue: SummerHeatDisplayMode.Bar, "How the status icon shows current heat: bar, percent, or nothing.");
            summerHeatBarTagMode = config("Season - Summer heat - Status", "Bar vertical tag", defaultValue: SummerHeatBarTagMode.Sup, "Optional rich-text tag around the heat bar. Sup makes compact raised blocks, Sub lowers them, None draws the bar without vertical adjustment.");
            summerHeatBarSegments = config("Season - Summer heat - Status", "Bar segments", defaultValue: 12, new ConfigDescription("Number of blocks in the heat bar.", new AcceptableValueRange<int>(1, 32)));
            summerHeatBarSymbol = config("Season - Summer heat - Status", "Bar symbol", defaultValue: "▄", "Character used for each heat bar block. Use one character, for example ▄, ▀, ■ or ▬.");
            summerHeatBarMinBrightness = config("Season - Summer heat - Status", "Bar minimum brightness", defaultValue: 0.2f, new ConfigDescription("Brightness of empty bar blocks. Higher values make the empty part easier to see.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatBarMaxBrightness = config("Season - Summer heat - Status", "Bar maximum brightness", defaultValue: 1f, new ConfigDescription("Brightness of filled bar blocks. Lower values make the whole bar less bright.", new AcceptableValueRange<float>(0f, 1f), new CustomConfigs.ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            summerHeatBarBonusColor = config("Season - Summer heat - Status", "Bar color bonus", defaultValue: new Color(0.49804f, 0.72941f, 0.03529f, 1f), "Color used when current heat gives bonuses.");
            summerHeatBarNeutralColor = config("Season - Summer heat - Status", "Bar color neutral", defaultValue: new Color(0.84314f, 0.72941f, 0.03529f, 1f), "Color used when current heat is safe but gives no bonus.");
            summerHeatBarPenaltyColor = config("Season - Summer heat - Status", "Bar color penalty", defaultValue: new Color(0.69020f, 0.34902f, 0.03529f, 1f), "Color used when current heat applies penalties.");
            summerHeatBarMaxColor = config("Season - Summer heat - Status", "Bar color overheat", defaultValue: new Color(0.72941f, 0.03529f, 0.03529f, 1f), "Color used when current heat reaches the most dangerous state.");

            EventHandler summerHeatRefreshHandler = (sender, args) =>
            {
                seasonState?.CheckOverheatStatus(Player.m_localPlayer);
                SummerHeatController.Instance?.RefreshState();
                SummerHeatVisuals.UpdateHazeState();
                LoadingTips.UpdateLoadingTips();
            };
            summerHeatEnabled.SettingChanged += summerHeatRefreshHandler;
            summerHeatAddsExtraWarmCloth.SettingChanged += summerHeatRefreshHandler;
            summerHeatCoolingFoods.SettingChanged += summerHeatRefreshHandler;
            summerHeatDays.SettingChanged += summerHeatRefreshHandler;
            summerHeatTimeToMax.SettingChanged += summerHeatRefreshHandler;
            summerHeatGreenThreshold.SettingChanged += summerHeatRefreshHandler;
            summerHeatNeutralThreshold.SettingChanged += summerHeatRefreshHandler;
            summerHeatMaxThreshold.SettingChanged += summerHeatRefreshHandler;
            summerHeatNightFactor.SettingChanged += summerHeatRefreshHandler;
            summerHeatZoneHysteresis.SettingChanged += summerHeatRefreshHandler;
            summerHeatGreenFadeWidth.SettingChanged += summerHeatRefreshHandler;
            summerHeatRedRampWidth.SettingChanged += summerHeatRefreshHandler;
            summerHeatMaxOverflow.SettingChanged += summerHeatRefreshHandler;
            summerHeatNonSunnyEnvironments.SettingChanged += summerHeatRefreshHandler;
            summerHeatDamageTickInterval.SettingChanged += summerHeatRefreshHandler;
            summerHeatDamageНealthPerTickMinHealthPercentage.SettingChanged += summerHeatRefreshHandler;
            summerHeatDamageНealthPerTick.SettingChanged += summerHeatRefreshHandler;
            summerHeatDamageHitType.SettingChanged += summerHeatRefreshHandler;
            summerHeatDamageMaxOnly.SettingChanged += summerHeatRefreshHandler;
            summerHeatStaminaUseMultiplier.SettingChanged += summerHeatRefreshHandler;
            summerHeatAdrenalineMultiplier.SettingChanged += summerHeatRefreshHandler;
            summerHeatHealthRegenMultiplier.SettingChanged += summerHeatRefreshHandler;
            summerHeatStaminaRegenMultiplier.SettingChanged += summerHeatRefreshHandler;
            summerHeatEitrRegenMultiplier.SettingChanged += summerHeatRefreshHandler;
            summerHeatInstantHeatSources.SettingChanged += summerHeatRefreshHandler;
            summerHeatCampFireAddsHeat.SettingChanged += summerHeatRefreshHandler;
            summerHeatEncumberedAddsHeat.SettingChanged += summerHeatRefreshHandler;
            summerHeatWindEffectPercent.SettingChanged += summerHeatRefreshHandler;
            summerHeatNoonEffectPercent.SettingChanged += summerHeatRefreshHandler;
            summerHeatArmorHeatEnabled.SettingChanged += summerHeatRefreshHandler;
            summerHeatOpenHelmetItems.SettingChanged += summerHeatRefreshHandler;
            summerHeatBareHeadHairItems.SettingChanged += summerHeatRefreshHandler;
            summerHeatLightCloakItems.SettingChanged += summerHeatRefreshHandler;
            summerHeatOpenChestItems.SettingChanged += summerHeatRefreshHandler;
            summerHeatOpenLegItems.SettingChanged += summerHeatRefreshHandler;
            summerHeatUncoveredHeadSunHeating.SettingChanged += summerHeatRefreshHandler;
            summerHeatUncoveredHeadShadeCooling.SettingChanged += summerHeatRefreshHandler;
            summerHeatOpenHelmetHeating.SettingChanged += summerHeatRefreshHandler;
            summerHeatClosedHelmetHeating.SettingChanged += summerHeatRefreshHandler;
            summerHeatClosedHelmetCoolingPenalty.SettingChanged += summerHeatRefreshHandler;
            summerHeatNoCloakHeatingReduction.SettingChanged += summerHeatRefreshHandler;
            summerHeatNoCloakCoolingBonus.SettingChanged += summerHeatRefreshHandler;
            summerHeatLightCloakHeatingReduction.SettingChanged += summerHeatRefreshHandler;
            summerHeatLightCloakCoolingBonus.SettingChanged += summerHeatRefreshHandler;
            summerHeatCloakHeating.SettingChanged += summerHeatRefreshHandler;
            summerHeatColdCloakHeating.SettingChanged += summerHeatRefreshHandler;
            summerHeatColdCloakCoolingPenalty.SettingChanged += summerHeatRefreshHandler;
            summerHeatEmptyArmorSlotHeatingReduction.SettingChanged += summerHeatRefreshHandler;
            summerHeatEmptyArmorSlotCoolingBonus.SettingChanged += summerHeatRefreshHandler;
            summerHeatOpenArmorHeatingReduction.SettingChanged += summerHeatRefreshHandler;
            summerHeatOpenArmorCoolingBonus.SettingChanged += summerHeatRefreshHandler;
            summerHeatClosedArmorHeating.SettingChanged += summerHeatRefreshHandler;
            summerHeatColdArmorHeating.SettingChanged += summerHeatRefreshHandler;
            summerHeatColdArmorCoolingPenalty.SettingChanged += summerHeatRefreshHandler;

            EventHandler summerHeatStatusDisplayHandler = (sender, args) => StatusEffectHud.EnsureTimeTextRichText();
            summerHeatStatusEffectDisplay.SettingChanged += summerHeatStatusDisplayHandler;
            summerHeatRavenTechnicalInfo.SettingChanged += summerHeatStatusDisplayHandler;
            summerHeatDisplayMode.SettingChanged += summerHeatStatusDisplayHandler;
            summerHeatBarTagMode.SettingChanged += summerHeatStatusDisplayHandler;
            summerHeatBarSegments.SettingChanged += summerHeatStatusDisplayHandler;
            summerHeatBarSymbol.SettingChanged += summerHeatStatusDisplayHandler;
            summerHeatBarMinBrightness.SettingChanged += summerHeatStatusDisplayHandler;
            summerHeatBarMaxBrightness.SettingChanged += summerHeatStatusDisplayHandler;
            summerHeatBarBonusColor.SettingChanged += summerHeatStatusDisplayHandler;
            summerHeatBarNeutralColor.SettingChanged += summerHeatStatusDisplayHandler;
            summerHeatBarPenaltyColor.SettingChanged += summerHeatStatusDisplayHandler;
            summerHeatBarMaxColor.SettingChanged += summerHeatStatusDisplayHandler;

            enableSeasonalGlobalKeys = config("Seasons - Global keys", "Enable setting seasonal Global Keys", defaultValue: false, "Enables setting seasonal global key");
            seasonalGlobalKeyFall = config("Seasons - Global keys", "Fall", defaultValue: "Season_Fall", "Seasonal global key for autumn. You can set config value like \"Season Fall\" space separated and it will be treated as key value pair.");
            seasonalGlobalKeySpring = config("Seasons - Global keys", "Spring", defaultValue: "Season_Spring", "Seasonal global key for spring. You can set config value like \"Season Spring\" space separated and it will be treated as key value pair.");
            seasonalGlobalKeySummer = config("Seasons - Global keys", "Summer", defaultValue: "Season_Summer", "Seasonal global key for summer. You can set config value like \"Season Summer\" space separated and it will be treated as key value pair.");
            seasonalGlobalKeyWinter = config("Seasons - Global keys", "Winter", defaultValue: "Season_Winter", "Seasonal global key for winter. You can set config value like \"Season Winter\" space separated and it will be treated as key value pair.");
            seasonalGlobalKeyDay = config("Seasons - Global keys", "Day number", defaultValue: "SeasonDay_{0}", "Seasonal global key for current day number. You can set config value like \"SeasonDay {0}\" space separated and it will be treated as key value pair.");

            enableSeasonalGlobalKeys.SettingChanged += (sender, args) => seasonState?.UpdateGlobalKeys();
            seasonalGlobalKeyFall.SettingChanged += (sender, args) => seasonState?.UpdateGlobalKeys();
            seasonalGlobalKeySpring.SettingChanged += (sender, args) => seasonState?.UpdateGlobalKeys();
            seasonalGlobalKeySummer.SettingChanged += (sender, args) => seasonState?.UpdateGlobalKeys();
            seasonalGlobalKeyWinter.SettingChanged += (sender, args) => seasonState?.UpdateGlobalKeys();
            seasonalGlobalKeyDay.SettingChanged += (sender, args) => seasonState?.UpdateGlobalKeys();

            cacheStorageFormat = config("Test", "Cache format", defaultValue: CacheFormat.Binary, "Cache files format. Binary for fast loading single non humanreadable file. JSON for humanreadable cache.json + textures subdirectory.");
            logTime = config("Test", "Log time", defaultValue: false, "Log time info on state update");
            logFloes = config("Test", "Log ice floes", defaultValue: false, "Log ice floes spawning/destroying");
            logControllersTime = config("Test", "Log prefab caching time", defaultValue: false, "Log elapsed time of prefabs caching process in descending order");
            plainsSwampBorderFix = config("Test", "Plains Swamp border fix", defaultValue: true, "Fix clipping into ground on Plains - Swamp border");
            frozenKarvePositionFix = config("Test", "Fix position for frozen Karve", defaultValue: false, "Make Karve storage always available if frozen. If Karve is below certain level it will be pushed to the surface.");
            lastDayTerrainFactor = config("Test", "Last day terrain factor", defaultValue: 0.0f, "Last day");
            firstDayTerrainFactor = config("Test", "First day terrain factor", defaultValue: 0.0f, "First day");
            runTextureCachingSync = config("Test", "Run texture caching without indicator", defaultValue: false, "It is significantly faster than running with loading indicator but lacks visual progress");

            plainsSwampBorderFix.SettingChanged += (sender, args) => ZoneSystemVariantController.UpdateTerrainColors();
            lastDayTerrainFactor.SettingChanged += (sender, args) => ZoneSystemVariantController.UpdateTerrainColors();
            firstDayTerrainFactor.SettingChanged += (sender, args) => ZoneSystemVariantController.UpdateTerrainColors();

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
            LoadIcon("valheim_warm.png",    ref iconWarm);

            Minimap_Summer_ForestTex = new Texture2D(512, 512, TextureFormat.RGBA32, false);
            LoadTexture("Minimap_Summer_ForestTex.png", ref Minimap_Summer_ForestTex);
            Minimap_Summer_ForestTex.wrapMode = TextureWrapMode.Repeat;
            Minimap_Summer_ForestTex.filterMode = FilterMode.Bilinear;

            Minimap_Fall_ForestTex = new Texture2D(512, 512, TextureFormat.RGBA32, false);
            LoadTexture("Minimap_Fall_ForestTex.png", ref Minimap_Fall_ForestTex);
            Minimap_Fall_ForestTex.wrapMode = TextureWrapMode.Repeat;
            Minimap_Fall_ForestTex.filterMode = FilterMode.Bilinear;

            Minimap_Winter_ForestTex = new Texture2D(512, 512, TextureFormat.RGBA32, false);
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
            string name = executingAssembly.GetManifestResourceNames().FirstOrDefault(str => str.EndsWith(filename));
            if (name == null)
                return false;

            using Stream resourceStream = executingAssembly.GetManifestResourceStream(name);
            if (resourceStream == null)
                return false;

            byte[] data = new byte[resourceStream.Length];
            resourceStream.Read(data, 0, data.Length);

            tex.name = Path.GetFileNameWithoutExtension(filename);
            return tex.LoadImage(data, true);
        }
        
        private Sprite GetSpriteConfig(string fieldName) => GetType().GetField(fieldName).GetValue(this) as Sprite;

        public static string GetSeasonTooltip(Season season) => $"$seasons_season_{season.ToString().ToLower()}_has_come";

        public static string GetSeasonName(Season season) => $"$seasons_season_{season.ToString().ToLower()}_name";

        public static string GetSeasonIsComing(Season season) => $"$seasons_season_{season.ToString().ToLower()}_is_coming";

        public static Sprite GetSeasonIcon(Season season) => showCurrentSeasonBuff.Value ? instance.GetSpriteConfig($"icon{season}") : null;

        public static string FromSeconds(double seconds)
        {
            if (seconds <= 0)
                return "$hud_ready".Localize();

            TimeSpan ts = TimeSpan.FromSeconds(seconds);
            return ts.ToString(ts.Hours > 0 ? @"h\:mm\:ss" : @"m\:ss");
        }

        public static string FromPercent(double percent) => "<sup><alpha=#ff>▀▀▀▀▀▀▀▀▀▀<alpha=#ff></sup>".Insert(Mathf.Clamp(Mathf.RoundToInt((float)percent * 10), 0, 10) + 16, "<alpha=#33>");

        public static bool UseTextureControllers()
        {
            return (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) && (ZNet.instance?.IsDedicated() != true);
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

            if (ZNetScene.instance?.m_prefabs == null)
                return;

            Dictionary<GameObject, GameObject> stubs = new Dictionary<GameObject, GameObject>();

            // At first fill all grown state pickables and stubs prefabs
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab?.TryGetComponent(out Pickable pickable) == true && pickable.m_itemPrefab != null && 
                    pickable.m_itemPrefab.TryGetComponent(out ItemDrop itemDrop) && itemDrop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable)
                    _PlantsToControlGrowth.Add(pickable.gameObject.name.ToLower());

                if (prefab?.TryGetComponent(out TreeBase tree) == true && tree.m_stubPrefab != null &&
                    tree.m_stubPrefab.TryGetComponent(out Destructible destructible) && IsTree(destructible))
                    stubs.Add(prefab, tree.m_stubPrefab);
            }

            // Add Plant that will later have Pickable in grown state and a stub of grown prefab
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab?.TryGetComponent(out Plant plant) == true && plant.m_grownPrefabs != null)
                {
                    if (plant.m_grownPrefabs.Any(prefab => ControlPlantGrowth(prefab)))
                        _PlantsToControlGrowth.Add(plant.gameObject.name.ToLower());

                    if (plant.m_tolerateCold || plant.m_grownPrefabs.Any(prefab => PlantWillSurviveWinter(prefab)))
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
            _cachedShieldedPositions.Clear();
            _cachedShieldedPositionsChangeID = ShieldGenerator.m_instanceChangeID;
        }

        public static bool IsIgnoredPosition(Vector3 position)
        {
            if (Character.InInterior(position))
                return true;

            if (WorldGenerator.instance == null)
                return true;

            Vector2 pos = new(position.x, position.z);
            if (_cachedIgnoredPositions.TryGetValue(pos, out bool ignored))
                return ignored;

            if (_cachedIgnoredPositions.Count > 15000)
                InvalidatePositionsCache();

            Heightmap.Biome biome = WorldGenerator.instance.GetBiome(position);

            ignored = biome == Heightmap.Biome.AshLands || 
                      biome == Heightmap.Biome.DeepNorth ||
                      biome == Heightmap.Biome.Mountain && WorldGenerator.instance.GetBaseHeight(position.x, position.z, menuTerrain: false) > WorldGenerator.mountainBaseHeightMin + 0.05f;
            
            _cachedIgnoredPositions[pos] = ignored;
            return ignored;
        }

        public static bool IsShieldProtectionActive()
        {
            return shieldGeneratorProtection.Value && (!shieldGeneratorOnlyWinter.Value || seasonState.GetCurrentSeason() == Season.Winter);
        }

        public static bool IsShieldedPosition(Vector3 position)
        {
            if (!IsShieldProtectionActive())
                return false;

            int shieldChangeID = ShieldGenerator.m_instanceChangeID;
            if (_cachedShieldedPositionsChangeID != shieldChangeID)
            {
                _cachedShieldedPositions.Clear();
                _cachedShieldedPositionsChangeID = shieldChangeID;
            }

            Vector2 pos = new(position.x, position.z);
            if (_cachedShieldedPositions.TryGetValue(pos, out bool shielded))
                return shielded;

            if (_cachedShieldedPositions.Count > 15000)
                _cachedShieldedPositions.Clear();

            shielded = ShieldGenerator.IsInsideShieldCached(position, ref _instanceChangeIDShieldGeneratorCache);
            _cachedShieldedPositions[pos] = shielded;

            return shielded;
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

        public static IEnumerator PickableSetPickedInWinter(Pickable pickable)
        {
            yield return waitFor1Second;

            if (!pickable.ShouldBePickedInWinter())
                yield break;

            if (!pickable.m_nview || !pickable.m_nview.IsValid())
                yield break;

            if (UnityEngine.Random.Range(0f, 1f) < chanceToProduceACropInWinter.Value)
                pickable.m_nview.GetZDO().Set(SeasonsVars.s_cropSurvivedWinterDayHash, seasonState.GetCurrentWorldDay());
            else
                pickable.m_nview.InvokeRPC(ZNetView.Everybody, "RPC_SetPicked", true);
        }

        public static IEnumerator ReplantTree(GameObject prefab, Vector3 position, Quaternion rotation, float scale)
        {
            yield return waitFor5Seconds;

            if (ZoneSystem.instance.IsBlocked(position))
                yield break;

            if ((bool)EffectArea.IsPointInsideArea(position, EffectArea.Type.PlayerBase))
                yield break;

            GameObject result = Instantiate(prefab, position, rotation);

            yield return waitForFixedUpdate;

            if (result != null && result.TryGetComponent(out ZNetView m_nview) && m_nview.IsValid())
            {
                m_nview.GetZDO().Set(SeasonsVars.s_treeRegrowthHaveGrowSpace, true);

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
