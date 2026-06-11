using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Seasons.SummerHeatUtils;

namespace Seasons
{
    internal struct SummerHeatArmorState
    {
        public static readonly SummerHeatArmorState Empty = new SummerHeatArmorState
        {
            HeadState = "$seasons_status_summer_heat_armor_disabled",
            CloakState = "$seasons_status_summer_heat_armor_disabled",
            ChestState = "$seasons_status_summer_heat_armor_disabled",
            LegsState = "$seasons_status_summer_heat_armor_disabled"
        };

        public float HeatingModifier;
        public float CoolingModifier;
        public string HeadState;
        public string CloakState;
        public string ChestState;
        public string LegsState;
    }

    internal class SummerHeatController : MonoBehaviour
    {
        internal const float EvaluationInterval = 1f;
        internal const float DaytimeHeatCap = 100f;
        internal const float ShadowRayDistance = 100f;
        internal const float StableEpsilon = 0.01f;
        internal const float WetCoolingPerSecond = 5f;
        internal const float ShelterCoolingPerSecond = 2f;
        internal const float BurningHeatPerSecond = 5f;
        internal const float RunningHeatPerSecond = 0.5f;
        internal const float WalkingCoolingPerSecond = 0.5f;
        internal const float StandingCoolingPerSecond = 1f;
        internal const float CoolingFoodHeatPerSecond = 5f;
        internal const float CampFireHeatPerSecond = 0.5f;
        internal const float EncumberedHeatPerSecond = 0.5f;
        internal const float MovementThreshold = 0.1f;
        internal const float NoonPeak = 0.5f;
        internal const float NoonStart = 0.42f;
        internal const float NoonEnd = 0.58f;
        internal const float SecondaryAttackHeat = 1f;
        internal const float PrimaryAttackHeat = 0.5f;
        internal const float DodgeHeat = 0.5f;
        internal const float JumpHeat = 0.25f;
        internal const float BlockHeat = 1f;
        internal const float PerfectBlockHeat = 0.5f;
        internal const float FireDamageHeat = 10f;
        internal const float FrostDamageHeat = -10f;
        private const float BareHeadHairHeatRateBonus = 0.2f;

        private static readonly HashSet<string> s_configuredNonSunnySystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_openHelmetItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_bareHeadHairItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_lightCloakItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_openChestItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_openLegItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string s_configuredNonSunnySystemsValue = string.Empty;
        private static string s_openHelmetItemsValue = string.Empty;
        private static string s_bareHeadHairItemsValue = string.Empty;
        private static string s_lightCloakItemsValue = string.Empty;
        private static string s_openChestItemsValue = string.Empty;
        private static string s_openLegItemsValue = string.Empty;

        private float _evaluationTimer;
        private float _overflowHeat;
        private SummerHeatMode _mode = SummerHeatMode.Stable;
        private SummerHeatState _state;
        private string _currentEnvironmentName = string.Empty;
        private bool _isDaytime = true;
        private bool _hasWetStatus;
        private bool _hasShelterStatus;
        private bool _hasBurningStatus;
        private bool _hasColdStatus;
        private bool _hasCoolingFood;
        private bool _hasCampFireStatus;
        private bool _biomeAllowsSummerHeat = true;
        private Heightmap.Biome _currentBiome = Heightmap.Biome.None;
        private SummerHeatArmorState _armorState = SummerHeatArmorState.Empty;

        internal static SummerHeatController Instance { get; private set; }

        internal Player Player { get; private set; }

        internal SummerHeatState State => _state;

        internal SummerHeatArmorState ArmorState => _armorState;

        private void Awake()
        {
            Player = GetComponent<Player>();
            if (Player != Player.m_localPlayer)
            {
                enabled = false;
                return;
            }

            Instance = this;
            EvaluateState(forceStatusRefresh: true);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            SummerHeatVisuals.UpdateHazeState();
        }

        private void Update()
        {
            if (Player == null || Player != Player.m_localPlayer || Player.IsDead())
                return;

            float dt = Time.deltaTime;
            _evaluationTimer += dt;
            if (_evaluationTimer >= EvaluationInterval)
            {
                _evaluationTimer = 0f;
                EvaluateState(forceStatusRefresh: false);
            }

            UpdateHeat(dt);

            SummerHeatVisuals.UpdateHazeState();
        }

        internal static void EnsureForPlayer(Player player)
        {
            if (player == null || player != Player.m_localPlayer)
                return;

            if (!player.TryGetComponent(out SummerHeatController _))
                player.gameObject.AddComponent<SummerHeatController>();
        }

        internal static HeatZone GetZoneForHeat(float heatPercent, HeatZone previousZone, bool biomeSupported, bool isDaytime, float overflowHeat)
        {
            if (!biomeSupported)
                return HeatZone.Neutral;

            float greenThreshold = GetGreenThreshold(isDaytime);
            float neutralThreshold = GetNeutralThreshold(isDaytime);
            float maxThreshold = GetMaxThreshold(isDaytime);
            float hysteresis = GetZoneHysteresis(isDaytime);
            float greenReturnThreshold = greenThreshold + hysteresis * 0.5f;
            float greenExitThreshold = greenThreshold + hysteresis;
            float redReturnThreshold = neutralThreshold - hysteresis;

            if (overflowHeat > 0f || heatPercent >= maxThreshold)
                return HeatZone.Max;

            return previousZone switch
            {
                HeatZone.Green => heatPercent >= greenExitThreshold ? HeatZone.Neutral : HeatZone.Green,
                HeatZone.Red => heatPercent < redReturnThreshold ? HeatZone.Neutral : HeatZone.Red,
                HeatZone.Max => HeatZone.Red,
                _ => ResolveNeutralZone(heatPercent, greenReturnThreshold, neutralThreshold)
            };
        }

        internal string GetCurrentEnvironmentName() => _currentEnvironmentName;

        internal bool IsDaytime() => _isDaytime;

        internal void RefreshState(bool forceStatusRefresh = true)
        {
            EvaluateState(forceStatusRefresh);
        }

        internal void AddInstantHeat(float amount, bool useConfigGate = false)
        {
            if (!Seasons.summerHeatEnabled.Value || Mathf.Approximately(amount, 0f))
                return;

            if (useConfigGate && !Seasons.summerHeatInstantHeatSources.Value)
                return;

            if (!_biomeAllowsSummerHeat)
                return;

            if (amount > 0f)
            {
                if (!_state.SeasonHeatWindowActive || _hasColdStatus || _hasCoolingFood)
                    return;
            }
            else if (!_state.SeasonHeatWindowActive && _state.TotalHeatPercent <= 0f)
            {
                return;
            }

            float previousTotal = _state.TotalHeatPercent;
            ApplyHeatDelta(amount, GetCurrentHeatCap());
            RefreshDerivedState(previousTotal, forceStatusRefresh: false);
        }

        private static HeatZone ResolveNeutralZone(float heatPercent, float greenReturnThreshold, float neutralThreshold)
        {
            if (heatPercent < greenReturnThreshold)
                return HeatZone.Green;
            if (heatPercent >= neutralThreshold)
                return HeatZone.Red;
            return HeatZone.Neutral;
        }

        private void EvaluateState(bool forceStatusRefresh)
        {
            if (!Seasons.summerHeatEnabled.Value)
            {
                ClearHeatState(forceStatusRefresh);
                return;
            }

            _isDaytime = !EnvMan.IsNight();
            _currentBiome = Player != null ? Player.GetCurrentBiome() : Heightmap.Biome.None;
            _biomeAllowsSummerHeat = AllowsSummerHeatBiome(_currentBiome);

            bool seasonHeatWindowActive = _biomeAllowsSummerHeat && IsSeasonHeatWindowActive();
            EnvSetup currentEnvironment = EnvMan.instance?.m_currentEnv;
            _currentEnvironmentName = GetEnvironmentName(currentEnvironment);

            bool isSunny = seasonHeatWindowActive && IsEnvironmentSunny(currentEnvironment);
            bool isInSun = false;
            bool isInShade = false;

            if (seasonHeatWindowActive && isSunny && _isDaytime)
            {
                isInShade = ComputeShade(Player);
                isInSun = !isInShade;
            }
            else
            {
                isInShade = true;
            }

            SEMan seMan = Player.GetSEMan();
            _hasWetStatus = seMan.HaveStatusEffect(SEMan.s_statusEffectWet);
            _hasShelterStatus = seMan.HaveStatusEffect(SEMan.s_statusEffectShelter);
            _hasBurningStatus = seMan.HaveStatusEffect(SEMan.s_statusEffectBurning);
            _hasCampFireStatus = seMan.HaveStatusEffect(SEMan.s_statusEffectCampFire);
            _hasColdStatus = seMan.HaveStatusEffect(SEMan.s_statusEffectCold)
                || seMan.HaveStatusEffect(SEMan.s_statusEffectFreezing)
                || seMan.HaveStatusEffect(SEMan.s_statusEffectFrost);
            _hasCoolingFood = SeasonState.HasCoolingFood(Player);

            bool isCoolingByWater = IsPlayerCoolingByWater(Player);
            bool isHeating = seasonHeatWindowActive && isSunny && _isDaytime && isInSun && !isCoolingByWater;

            _mode = GetMode(seasonHeatWindowActive, isHeating, isCoolingByWater, isInShade);
            _state.SeasonHeatWindowActive = seasonHeatWindowActive;
            _state.IsSunny = isSunny;
            _state.IsInSun = isInSun;
            _state.IsInShade = isInShade;
            _state.BiomeSupported = _biomeAllowsSummerHeat;

            RefreshArmorState();
            RefreshDerivedState(_state.TotalHeatPercent, forceStatusRefresh);
        }

        private void ClearHeatState(bool forceStatusRefresh)
        {
            float previousTotal = _state.TotalHeatPercent;
            _overflowHeat = 0f;
            _mode = SummerHeatMode.Stable;
            _currentEnvironmentName = string.Empty;
            _isDaytime = !EnvMan.IsNight();
            _hasWetStatus = false;
            _hasShelterStatus = false;
            _hasBurningStatus = false;
            _hasColdStatus = false;
            _hasCoolingFood = false;
            _hasCampFireStatus = false;
            _biomeAllowsSummerHeat = false;
            _state.SetHeat(0f, 0f, 0f, DaytimeHeatCap, HeatZone.Neutral, 0f, 0f, 0f);
            _state.Direction = previousTotal > 0f ? -1 : 0;
            _state.IsCooling = false;
            _state.IsSunny = false;
            _state.IsInSun = false;
            _state.IsInShade = true;
            _state.SeasonHeatWindowActive = false;
            _state.BiomeSupported = false;
            _state.MechanicActive = false;
            _armorState = SummerHeatArmorState.Empty;

            EnsureStatusEffect(shouldHaveEffect: false);

            SummerHeatVisuals.UpdateHazeState();
        }

        private void UpdateHeat(float dt)
        {
            float previousTotal = _state.TotalHeatPercent;

            if (!Seasons.summerHeatEnabled.Value)
            {
                ClearHeatState(forceStatusRefresh: true);
                return;
            }

            if (!_biomeAllowsSummerHeat)
            {
                _overflowHeat = 0f;
                _state.SetHeat(0f, 0f, 0f, DaytimeHeatCap, HeatZone.Neutral, 0f, 0f, 0f);
                _state.Direction = previousTotal > 0f ? -1 : 0;
                _state.IsCooling = previousTotal > 0f;
                _state.MechanicActive = false;
                EnsureStatusEffect(shouldHaveEffect: false);
                return;
            }

            float heatCap = GetCurrentHeatCap();
            float environmentalDelta = 0f;
            float heatPerSecond = DaytimeHeatCap / Mathf.Max(1f, Seasons.summerHeatTimeToMax.Value);

            switch (_mode)
            {
                case SummerHeatMode.Heating:
                    environmentalDelta += heatPerSecond * dt;
                    break;
                case SummerHeatMode.CoolingFast:
                    environmentalDelta -= heatPerSecond * 2.5f * dt;
                    break;
                case SummerHeatMode.CoolingNormal:
                    environmentalDelta -= heatPerSecond * 1.25f * dt;
                    break;
                case SummerHeatMode.CoolingSlow:
                    environmentalDelta -= heatPerSecond * 0.4f * dt;
                    break;
            }

            if (_state.SeasonHeatWindowActive)
            {
                if (_hasBurningStatus)
                    environmentalDelta += BurningHeatPerSecond * dt;

                if (_hasWetStatus)
                    environmentalDelta -= WetCoolingPerSecond * dt;

                if (_hasShelterStatus)
                    environmentalDelta -= ShelterCoolingPerSecond * dt;

                environmentalDelta += GetActivityHeatDelta(Player, dt);
            }

            environmentalDelta = ApplyDynamicRateModifiers(environmentalDelta);
            environmentalDelta = ApplyArmorRateModifiers(environmentalDelta);
            ApplyHeatDelta(environmentalDelta, heatCap);

            if (_hasColdStatus)
            {
                _overflowHeat = 0f;
                ApplyHeatDelta(0f - GetLiveTotalHeat(), heatCap);
            }
            else if (_hasCoolingFood)
            {
                ApplyHeatDelta(GetCoolingDeltaTowards(GetGreenThreshold(_isDaytime), CoolingFoodHeatPerSecond, dt), heatCap);
            }

            if (_state.SeasonHeatWindowActive)
            {
                if (Seasons.summerHeatCampFireAddsHeat.Value && _hasCampFireStatus)
                    ApplyHeatDelta(GetHeatingDeltaTowards(GetGreenThreshold(_isDaytime), CampFireHeatPerSecond, dt), heatCap);

                if (Seasons.summerHeatEncumberedAddsHeat.Value && Player.IsEncumbered())
                    ApplyHeatDelta(GetHeatingDeltaTowards(GetNeutralThreshold(_isDaytime), EncumberedHeatPerSecond, dt), heatCap);
            }

            RefreshDerivedState(previousTotal, forceStatusRefresh: false);
        }

        private void RefreshDerivedState(float previousTotalHeat, bool forceStatusRefresh)
        {
            float heatCap = GetCurrentHeatCap();
            float heat = Mathf.Clamp(_state.HeatPercent, 0f, heatCap);
            float totalHeat = Mathf.Max(0f, heat + _overflowHeat);
            HeatZone zone = GetZoneForHeat(heat, _state.Zone, _biomeAllowsSummerHeat, _isDaytime, _overflowHeat);
            float greenFactor = CalculateGreenFactor(heat);
            float redFactor = CalculateRedFactor(heat);
            float maxFactor = CalculateMaxFactor(heat, totalHeat, heatCap);

            _state.SetHeat(heat, _overflowHeat, totalHeat, DaytimeHeatCap, zone, greenFactor, redFactor, maxFactor);
            float delta = totalHeat - previousTotalHeat;
            _state.Direction = Mathf.Abs(delta) <= StableEpsilon ? 0 : delta > 0f ? 1 : -1;
            _state.IsCooling = _hasColdStatus
                || _hasCoolingFood && totalHeat > GetGreenThreshold(_isDaytime)
                || _hasWetStatus
                || _hasShelterStatus
                || _state.Direction < 0;
            _state.MechanicActive = _state.SeasonHeatWindowActive || totalHeat > 0f;
            _state.BiomeSupported = _biomeAllowsSummerHeat;

            if (forceStatusRefresh || _state.MechanicActive != Player.GetSEMan().HaveStatusEffect(SeasonsVars.s_statusEffectSummerHeatHash))
                EnsureStatusEffect(_state.MechanicActive);
        }

        private void RefreshArmorState()
        {
            _armorState = CalculateArmorState(Player, _state.IsInSun);
        }

        private static SummerHeatArmorState CalculateArmorState(Player player, bool isInDirectSun)
        {
            if (!Seasons.summerHeatArmorHeatEnabled.Value || player == null)
                return SummerHeatArmorState.Empty;

            SummerHeatArmorState state = new SummerHeatArmorState
            {
                HeadState = "$seasons_status_summer_heat_armor_empty",
                CloakState = "$seasons_status_summer_heat_armor_empty",
                ChestState = "$seasons_status_summer_heat_armor_empty",
                LegsState = "$seasons_status_summer_heat_armor_empty"
            };

            ApplyHeadArmor(player, isInDirectSun, ref state);
            ApplyCloakArmor(player, ref state);
            ApplyBodyArmor(player, ItemDrop.ItemData.ItemType.Chest, GetConfiguredItemList(Seasons.summerHeatOpenChestItems, ref s_openChestItemsValue, s_openChestItems), ref state.HeatingModifier, ref state.CoolingModifier, ref state.ChestState);
            ApplyBodyArmor(player, ItemDrop.ItemData.ItemType.Legs, GetConfiguredItemList(Seasons.summerHeatOpenLegItems, ref s_openLegItemsValue, s_openLegItems), ref state.HeatingModifier, ref state.CoolingModifier, ref state.LegsState);

            state.HeatingModifier = Mathf.Clamp(state.HeatingModifier, -0.95f, 3f);
            state.CoolingModifier = Mathf.Clamp(state.CoolingModifier, -0.95f, 3f);
            return state;
        }

        private static void ApplyHeadArmor(Player player, bool isInDirectSun, ref SummerHeatArmorState state)
        {
            ItemDrop.ItemData helmet = GetEquippedItem(player, ItemDrop.ItemData.ItemType.Helmet);
            if (helmet == null)
            {
                bool hasBareHeadHair = IsBareHeadHair(player);
                if (isInDirectSun)
                {
                    state.HeatingModifier += ClampEffect(Seasons.summerHeatUncoveredHeadSunHeating.Value) + (hasBareHeadHair ? BareHeadHairHeatRateBonus : 0f);
                    state.HeadState = hasBareHeadHair ? "$seasons_status_summer_heat_armor_bald_head" : "$seasons_status_summer_heat_armor_uncovered_sun";
                }
                else
                {
                    state.CoolingModifier += ClampEffect(Seasons.summerHeatUncoveredHeadShadeCooling.Value) + (hasBareHeadHair ? BareHeadHairHeatRateBonus : 0f);
                    state.HeadState = hasBareHeadHair ? "$seasons_status_summer_heat_armor_bald_head" : "$seasons_status_summer_heat_armor_uncovered_shade";
                }

                return;
            }

            if (IsConfiguredItem(helmet, GetConfiguredItemList(Seasons.summerHeatOpenHelmetItems, ref s_openHelmetItemsValue, s_openHelmetItems)))
            {
                state.HeatingModifier += ClampEffect(Seasons.summerHeatOpenHelmetHeating.Value);
                state.HeadState = "$seasons_status_summer_heat_armor_open_helmet";
                return;
            }

            state.HeatingModifier += ClampEffect(Seasons.summerHeatClosedHelmetHeating.Value);
            state.CoolingModifier -= ClampEffect(Seasons.summerHeatClosedHelmetCoolingPenalty.Value);
            state.HeadState = "$seasons_status_summer_heat_armor_closed_helmet";
        }

        private static void ApplyCloakArmor(Player player, ref SummerHeatArmorState state)
        {
            ItemDrop.ItemData cloak = GetEquippedItem(player, ItemDrop.ItemData.ItemType.Shoulder);
            if (cloak == null)
            {
                state.HeatingModifier -= ClampEffect(Seasons.summerHeatNoCloakHeatingReduction.Value);
                state.CoolingModifier += ClampEffect(Seasons.summerHeatNoCloakCoolingBonus.Value);
                state.CloakState = "$seasons_status_summer_heat_armor_no_cloak";
                return;
            }

            if (IsConfiguredItem(cloak, GetConfiguredItemList(Seasons.summerHeatLightCloakItems, ref s_lightCloakItemsValue, s_lightCloakItems)))
            {
                state.HeatingModifier -= ClampEffect(Seasons.summerHeatLightCloakHeatingReduction.Value);
                state.CoolingModifier += ClampEffect(Seasons.summerHeatLightCloakCoolingBonus.Value);
                state.CloakState = "$seasons_status_summer_heat_armor_light_cloak";
                return;
            }

            if (IsFrostResistantItem(cloak))
            {
                state.HeatingModifier += ClampEffect(Seasons.summerHeatColdCloakHeating.Value);
                state.CoolingModifier -= ClampEffect(Seasons.summerHeatColdCloakCoolingPenalty.Value);
                state.CloakState = "$seasons_status_summer_heat_armor_cold_cloak";
                return;
            }

            state.HeatingModifier += ClampEffect(Seasons.summerHeatCloakHeating.Value);
            state.CloakState = "$seasons_status_summer_heat_armor_cloak";
        }

        private static void ApplyBodyArmor(Player player, ItemDrop.ItemData.ItemType itemType, HashSet<string> openItems, ref float heatingModifier, ref float coolingModifier, ref string stateKey)
        {
            ItemDrop.ItemData item = GetEquippedItem(player, itemType);
            if (item == null)
            {
                heatingModifier -= ClampEffect(Seasons.summerHeatEmptyArmorSlotHeatingReduction.Value);
                coolingModifier += ClampEffect(Seasons.summerHeatEmptyArmorSlotCoolingBonus.Value);
                stateKey = "$seasons_status_summer_heat_armor_empty";
                return;
            }

            if (IsConfiguredItem(item, openItems))
            {
                heatingModifier -= ClampEffect(Seasons.summerHeatOpenArmorHeatingReduction.Value);
                coolingModifier += ClampEffect(Seasons.summerHeatOpenArmorCoolingBonus.Value);
                stateKey = "$seasons_status_summer_heat_armor_open_armor";
                return;
            }

            if (IsFrostResistantItem(item))
            {
                heatingModifier += ClampEffect(Seasons.summerHeatColdArmorHeating.Value);
                coolingModifier -= ClampEffect(Seasons.summerHeatColdArmorCoolingPenalty.Value);
                stateKey = "$seasons_status_summer_heat_armor_cold_armor";
                return;
            }

            heatingModifier += ClampEffect(Seasons.summerHeatClosedArmorHeating.Value);
            stateKey = "$seasons_status_summer_heat_armor_closed_armor";
        }

        private static ItemDrop.ItemData GetEquippedItem(Player player, ItemDrop.ItemData.ItemType itemType)
        {
            if (player == null || player.GetInventory() is not Inventory inventory)
                return null;

            return inventory.GetEquippedItems().FirstOrDefault(item => item != null && item.m_shared != null && item.m_shared.m_itemType == itemType);
        }

        private static bool IsFrostResistantItem(ItemDrop.ItemData item)
        {
            return item?.m_shared?.m_damageModifiers != null && item.m_shared.m_damageModifiers.Any(SeasonState.IsFrostResistant);
        }

        private static bool IsConfiguredItem(ItemDrop.ItemData item, HashSet<string> configuredItems)
        {
            if (item?.m_shared == null || configuredItems == null || configuredItems.Count == 0)
                return false;

            if (!string.IsNullOrEmpty(item.m_shared.m_name) && configuredItems.Contains(item.m_shared.m_name))
                return true;

            string prefabName = item.m_dropPrefab != null ? item.m_dropPrefab.name : string.Empty;
            return !string.IsNullOrEmpty(prefabName) && (configuredItems.Contains(prefabName) || configuredItems.Contains(prefabName.GetItemName()));
        }

        private static bool IsBareHeadHair(Player player)
        {
            HashSet<string> configuredItems = GetConfiguredItemList(Seasons.summerHeatBareHeadHairItems, ref s_bareHeadHairItemsValue, s_bareHeadHairItems);
            if (player == null || configuredItems.Count == 0)
                return false;

            string hairItem = player.m_hairItem ?? string.Empty;
            if (string.IsNullOrWhiteSpace(hairItem))
                return configuredItems.Contains("none") || configuredItems.Contains("bald") || configuredItems.Contains("balded") || configuredItems.Contains("HairNone") || configuredItems.Contains("HairNone".GetItemName());

            return configuredItems.Contains(hairItem) || configuredItems.Contains(hairItem.GetItemName());
        }

        private static HashSet<string> GetConfiguredItemList(BepInEx.Configuration.ConfigEntry<string> config, ref string cachedValue, HashSet<string> cache)
        {
            string configuredValue = config?.Value ?? string.Empty;
            if (configuredValue == cachedValue)
                return cache;

            cachedValue = configuredValue;
            cache.Clear();

            foreach (string entry in configuredValue.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = entry.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                cache.Add(trimmed);
                cache.Add(trimmed.GetItemName());
            }

            return cache;
        }


        private static float GetActivityHeatDelta(Player player, float dt)
        {
            if (player == null)
                return 0f;

            if (player.IsRunning())
                return RunningHeatPerSecond * dt;

            if (player.IsWalking())
                return 0f - WalkingCoolingPerSecond * dt;

            if (player.m_moveDir.magnitude > MovementThreshold)
                return 0f;

            return 0f - StandingCoolingPerSecond * dt;
        }

        private void ApplyHeatDelta(float delta, float heatCap)
        {
            if (Mathf.Approximately(delta, 0f))
                return;

            float heat = Mathf.Clamp(_state.HeatPercent, 0f, heatCap);
            if (delta > 0f)
            {
                float heatSpace = Mathf.Max(0f, heatCap - heat);
                float heatDelta = Mathf.Min(heatSpace, delta);
                heat += heatDelta;
                float overflowDelta = delta - heatDelta;
                if (overflowDelta > 0f)
                    _overflowHeat = Mathf.Clamp(_overflowHeat + overflowDelta, 0f, ClampPercent(Seasons.summerHeatMaxOverflow.Value));
            }
            else
            {
                float cooling = 0f - delta;
                if (_overflowHeat > 0f)
                {
                    float overflowCooling = Mathf.Min(_overflowHeat, cooling);
                    _overflowHeat -= overflowCooling;
                    cooling -= overflowCooling;
                }

                if (cooling > 0f)
                    heat = Mathf.Max(0f, heat - cooling);
            }

            _state.HeatPercent = Mathf.Clamp(heat, 0f, heatCap);
        }

        private float ApplyDynamicRateModifiers(float delta)
        {
            if (Mathf.Approximately(delta, 0f))
                return 0f;

            float windPercent = Mathf.Clamp01(Seasons.summerHeatWindEffectPercent.Value);
            float windInfluence = Mathf.Lerp(windPercent, 0f - windPercent, Mathf.Clamp01(EnvMan.instance?.GetWindIntensity() ?? 0f));
            float noonInfluence = Mathf.Clamp01(Seasons.summerHeatNoonEffectPercent.Value) * GetNoonInfluence();
            float influence = windInfluence + noonInfluence;
            float multiplier = delta > 0f ? 1f + influence : 1f - influence;
            return delta * Mathf.Max(0.05f, multiplier);
        }

        private float ApplyArmorRateModifiers(float delta)
        {
            if (!Seasons.summerHeatArmorHeatEnabled.Value || Mathf.Approximately(delta, 0f))
                return delta;

            float modifier = delta > 0f ? _armorState.HeatingModifier : _armorState.CoolingModifier;
            if (Mathf.Approximately(modifier, 0f))
                return delta;

            return delta * Mathf.Max(0.05f, 1f + modifier);
        }

        private static float GetNoonInfluence()
        {
            if (EnvMan.instance == null)
                return 0f;

            float dayFraction = EnvMan.instance.m_smoothDayFraction;
            if (dayFraction <= NoonStart || dayFraction >= NoonEnd)
                return 0f;

            float halfRange = (NoonEnd - NoonStart) * 0.5f;
            return Mathf.Clamp01(1f - Mathf.Abs(dayFraction - NoonPeak) / halfRange);
        }

        private float GetCurrentHeatCap()
        {
            float nightFactor = GetNightFactor();
            return _isDaytime ? DaytimeHeatCap : DaytimeHeatCap * nightFactor;
        }

        private static float GetGreenThreshold(bool isDaytime)
        {
            return ScaleHeatPercentForTime(ClampPercent(Seasons.summerHeatGreenThreshold.Value), isDaytime);
        }

        private static float GetNeutralThreshold(bool isDaytime)
        {
            return ScaleHeatPercentForTime(ClampPercent(Mathf.Max(GetGreenThreshold(true) + 1f, Seasons.summerHeatNeutralThreshold.Value)), isDaytime);
        }

        private static float GetMaxThreshold(bool isDaytime)
        {
            return ScaleHeatPercentForTime(ClampPercent(Mathf.Max(GetNeutralThreshold(true) + 1f, Seasons.summerHeatMaxThreshold.Value)), isDaytime);
        }

        private static float GetZoneHysteresis(bool isDaytime)
        {
            return ScaleHeatPercentForTime(ClampPercent(Seasons.summerHeatZoneHysteresis.Value), isDaytime);
        }


        private static float CalculateGreenFactor(float heat)
        {
            bool isDaytime = Instance == null || Instance._isDaytime;
            float greenThreshold = GetGreenThreshold(isDaytime);
            float greenFadeWidth = Mathf.Max(0.1f, ScaleHeatPercentForTime(ClampPercent(Seasons.summerHeatGreenFadeWidth.Value), isDaytime));
            float greenStart = Mathf.Max(0f, greenThreshold - greenFadeWidth);
            float greenEnd = greenThreshold + greenFadeWidth;

            if (heat <= greenStart || heat >= greenEnd)
                return 0f;
            if (heat <= greenThreshold)
                return Mathf.InverseLerp(greenStart, greenThreshold, heat);
            return 1f - Mathf.InverseLerp(greenThreshold, greenEnd, heat);
        }

        private static float CalculateRedFactor(float heat)
        {
            bool isDaytime = Instance == null || Instance._isDaytime;
            float neutralThreshold = GetNeutralThreshold(isDaytime);
            float maxThreshold = GetMaxThreshold(isDaytime);
            float redRampWidth = Mathf.Max(0.1f, ScaleHeatPercentForTime(ClampPercent(Seasons.summerHeatRedRampWidth.Value), isDaytime));
            float redFullThreshold = Mathf.Min(maxThreshold, neutralThreshold + redRampWidth);
            if (heat <= neutralThreshold)
                return 0f;
            if (heat < redFullThreshold)
                return Mathf.InverseLerp(neutralThreshold, redFullThreshold, heat);
            return 1f;
        }

        private static float CalculateMaxFactor(float heat, float totalHeat, float heatCap)
        {
            bool isDaytime = Instance == null || Instance._isDaytime;
            float maxThreshold = GetMaxThreshold(isDaytime);
            if (totalHeat <= maxThreshold)
                return 0f;
            if (totalHeat >= heatCap || Instance != null && Instance._overflowHeat > 0f)
                return 1f;
            return Mathf.InverseLerp(maxThreshold, heatCap, Mathf.Clamp(heat, maxThreshold, heatCap));
        }

        private float GetCoolingDeltaTowards(float target, float unitsPerSecond, float dt)
        {
            float totalHeat = GetLiveTotalHeat();
            if (totalHeat <= target)
                return 0f;

            float maxDelta = Mathf.Max(0f, unitsPerSecond) * dt;
            return 0f - Mathf.Min(maxDelta, totalHeat - target);
        }

        private float GetHeatingDeltaTowards(float target, float unitsPerSecond, float dt)
        {
            float totalHeat = GetLiveTotalHeat();
            if (totalHeat >= target)
                return 0f;

            float maxDelta = Mathf.Max(0f, unitsPerSecond) * dt;
            return Mathf.Min(maxDelta, target - totalHeat);
        }

        private float GetLiveTotalHeat()
        {
            return Mathf.Max(0f, _state.HeatPercent + _overflowHeat);
        }

        private void EnsureStatusEffect(bool shouldHaveEffect)
        {
            SEMan seMan = Player.GetSEMan();
            bool hasEffect = seMan.HaveStatusEffect(SeasonsVars.s_statusEffectSummerHeatHash);
            if (shouldHaveEffect)
            {
                if (!hasEffect)
                    seMan.AddStatusEffect(SeasonsVars.s_statusEffectSummerHeatHash);
            }
            else if (hasEffect)
            {
                seMan.RemoveStatusEffect(SeasonsVars.s_statusEffectSummerHeatHash);
            }
        }

        private static SummerHeatMode GetMode(bool seasonHeatWindowActive, bool isHeating, bool isCoolingByWater, bool isInShade)
        {
            if (isCoolingByWater)
                return SummerHeatMode.CoolingFast;
            if (isHeating)
                return SummerHeatMode.Heating;
            if (seasonHeatWindowActive && isInShade)
                return SummerHeatMode.CoolingNormal;
            return SummerHeatMode.CoolingSlow;
        }

        private bool IsSeasonHeatWindowActive()
        {
            if (!Seasons.summerHeatEnabled.Value || Seasons.seasonState == null)
                return false;

            if (Seasons.seasonState.GetCurrentSeason() != Seasons.Season.Summer)
                return false;

            Vector2 heatDays = Seasons.summerHeatDays.Value;
            int currentDay = Seasons.seasonState.GetCurrentDay();
            int dayFrom = Mathf.RoundToInt(Mathf.Min(heatDays.x, heatDays.y));
            int dayTo = Mathf.RoundToInt(Mathf.Max(heatDays.x, heatDays.y));
            return currentDay >= dayFrom && currentDay <= dayTo;
        }

        private static string GetEnvironmentName(EnvSetup env) => env?.m_name ?? string.Empty;

        private static bool IsPlayerCoolingByWater(Player player)
        {
            if (player == null)
                return false;

            float liquidLevel = Floating.GetLiquidLevel(player.transform.position);
            return player.IsSwimming() || liquidLevel > player.transform.position.y + 0.1f;
        }

        private static bool ComputeShade(Player player)
        {
            if (player == null || EnvMan.instance?.m_dirLight == null || StealthSystem.instance == null)
                return false;

            if (player.InInterior() || player.InShelter())
                return true;

            float factor = 0.5f;
            if (player.InEmote())
                if (player.m_emoteState == "rest")
                    factor = 0.1f;
                else if (player.m_emoteState == "sit")
                    factor = 0.25f;

            Vector3 origin = Vector3.Lerp(player.transform.position, player.GetEyePoint(), factor);

            Vector3 direction = -EnvMan.instance.m_dirLight.transform.forward;
            return Physics.Raycast(origin, direction, ShadowRayDistance, StealthSystem.instance.m_shadowTestMask);
        }

        private static bool IsEnvironmentSunny(EnvSetup env)
        {
            if (env == null)
                return true;

            HashSet<string> configuredSystems = GetConfiguredNonSunnySystems();
            if (configuredSystems.Count == 0)
                return true;

            return !IsInConfiguredWeatherList(env, configuredSystems);
        }

        private static HashSet<string> GetConfiguredNonSunnySystems()
        {
            string configuredValue = Seasons.summerHeatNonSunnyEnvironments.Value ?? string.Empty;
            if (configuredValue == s_configuredNonSunnySystemsValue)
                return s_configuredNonSunnySystems;

            s_configuredNonSunnySystemsValue = configuredValue;
            s_configuredNonSunnySystems.Clear();

            foreach (string entry in configuredValue.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = entry.Trim();
                if (trimmed.Length > 0)
                    s_configuredNonSunnySystems.Add(trimmed);
            }

            return s_configuredNonSunnySystems;
        }

        private static bool IsInConfiguredWeatherList(EnvSetup env, HashSet<string> environmentSystems)
        {
            return env.m_envObject != null && environmentSystems.Contains(env.m_envObject.name)
                || env.m_psystems != null && env.m_psystems.Any(ps => ps != null && ps.name != null && environmentSystems.Contains(ps.name));
        }

        private static bool AllowsSummerHeatBiome(Heightmap.Biome biome) => biome != Heightmap.Biome.AshLands && biome != Heightmap.Biome.DeepNorth && biome != Heightmap.Biome.Mountain;

        internal bool HasCoolingFood() => _hasCoolingFood;

        internal bool HasCampFireHeat() => _state.SeasonHeatWindowActive && Seasons.summerHeatCampFireAddsHeat.Value && _hasCampFireStatus && GetLiveTotalHeat() < GetGreenThreshold(_isDaytime);

        internal bool HasEncumberedHeat() => _state.SeasonHeatWindowActive && Seasons.summerHeatEncumberedAddsHeat.Value && Player.IsEncumbered() && GetLiveTotalHeat() < GetNeutralThreshold(_isDaytime);

        internal static bool IsSecondaryAttack(Humanoid humanoid) => humanoid.m_currentAttackIsSecondary;

        internal static bool WasPerfectBlock(Humanoid humanoid) => humanoid.m_blockTimer > 0f && humanoid.m_blockTimer <= Humanoid.m_perfectBlockInterval;
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class Humanoid_StartAttack_SummerHeat
    {
        private static void Postfix(Humanoid __instance, ref bool __result)
        {
            if (!__result || __instance != Player.m_localPlayer)
                return;

            float attackHeat = SummerHeatController.IsSecondaryAttack(__instance) ? SummerHeatController.SecondaryAttackHeat : SummerHeatController.PrimaryAttackHeat;
            SummerHeatController.Instance?.AddInstantHeat(attackHeat, useConfigGate: true);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.UpdateDodge))]
    internal static class Player_UpdateDodge_SummerHeat
    {
        private static void Prefix(bool ___m_inDodge, ref bool __state)
        {
            __state = ___m_inDodge;
        }

        private static void Postfix(Player __instance, bool ___m_inDodge, bool __state)
        {
            if (!__state && ___m_inDodge && __instance == Player.m_localPlayer)
                SummerHeatController.Instance?.AddInstantHeat(SummerHeatController.DodgeHeat, useConfigGate: true);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Jump))]
    internal static class Character_Jump_SummerHeat
    {
        private static void Postfix(Character __instance)
        {
            if (__instance == Player.m_localPlayer)
                SummerHeatController.Instance?.AddInstantHeat(SummerHeatController.JumpHeat, useConfigGate: true);
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.BlockAttack))]
    internal static class Humanoid_BlockAttack_SummerHeat
    {
        private static void Postfix(Humanoid __instance, bool __result)
        {
            if (!__result || __instance != Player.m_localPlayer)
                return;

            SummerHeatController.Instance?.AddInstantHeat(SummerHeatController.WasPerfectBlock(__instance) ? SummerHeatController.PerfectBlockHeat : SummerHeatController.BlockHeat, useConfigGate: true);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    internal static class Character_ApplyDamage_SummerHeat
    {
        private static void Postfix(Character __instance, ref HitData hit)
        {
            if (__instance != Player.m_localPlayer || hit == null)
                return;

            if (hit.m_damage.m_fire > 0f)
                SummerHeatController.Instance?.AddInstantHeat(SummerHeatController.FireDamageHeat);
            else if (hit.m_damage.m_frost > 0f)
                SummerHeatController.Instance?.AddInstantHeat(SummerHeatController.FrostDamageHeat);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class Player_OnSpawned_SummerHeat
    {
        private static void Postfix(Player __instance)
        {
            SummerHeatController.EnsureForPlayer(__instance);
        }
    }
}
