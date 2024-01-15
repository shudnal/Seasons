﻿using System;
using static Seasons.Seasons;
using HarmonyLib;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using BepInEx;
using static Seasons.SeasonLightings;
using static CharacterDrop;

namespace Seasons
{
    public class SeasonState
    {
        private Season m_season = Season.Spring;
        private int m_day = 0;
        private bool m_seasonIsChanging = false;

        public static readonly Dictionary<Season, SeasonSettings> seasonsSettings = new Dictionary<Season, SeasonSettings>();
        public static SeasonBiomeEnvironments seasonBiomeEnvironments = new SeasonBiomeEnvironments(loadDefaults: true);
        public static List<SeasonEnvironment> seasonEnvironments = SeasonEnvironment.GetDefaultCustomEnvironments();
        public static SeasonRandomEvents seasonRandomEvents = new SeasonRandomEvents(loadDefaults: true);
        public static SeasonLightings seasonLightings = new SeasonLightings(loadDefaults: true);
        public static SeasonStats seasonStats = new SeasonStats(loadDefaults: true);

        private SeasonSettings settings
        {
            get
            {
                if (!seasonsSettings.ContainsKey(m_season))
                    seasonsSettings.Add(m_season, new SeasonSettings(m_season));

                return seasonsSettings[m_season];
            }
        }

        public SeasonState(bool initialize = false)
        {
            if (!initialize)
                return;

            foreach (Season season in Enum.GetValues(typeof(Season)))
                if (!seasonsSettings.ContainsKey(season))
                    seasonsSettings.Add(season, new SeasonSettings(season));

            string folder = Path.Combine(configDirectory, SeasonSettings.defaultsSubdirectory);
            Directory.CreateDirectory(folder);

            LogInfo($"Saving default seasons settings");
            foreach (KeyValuePair<Season, SeasonSettings> seasonSettings in seasonsSettings)
            {
                string filename = Path.Combine(folder, $"{seasonSettings.Key}.json");
                seasonSettings.Value.SaveToJSON(filename);
            }

            SeasonSettings.SaveDefaultEnvironments(folder);
            SeasonSettings.SaveDefaultEvents(folder);
            SeasonSettings.SaveDefaultLightings(folder);
            SeasonSettings.SaveDefaultStats(folder);
        }

        public bool IsActive => EnvMan.instance != null;

        public void UpdateState(int day, bool seasonCanBeChanged)
        {
            int dayInSeason = GetDayInSeason(day);

            int season = (int)m_season;

            if (overrideSeason.Value)
                m_season = seasonOverrided.Value;
            else if (seasonCanBeChanged)
                m_season = GetSeason(day);

            CheckIfSeasonChanged(season);
            CheckIfDayChanged(dayInSeason);
        }

        public Season GetCurrentSeason()
        {
            return m_season;
        }

        public bool GetSeasonIsChanging()
        {
            return showFadeOnSeasonChange.Value && m_seasonIsChanging;
        }

        public int GetCurrentDay()
        {
            return m_day;
        }

        public int GetDaysInSeason()
        {
            return settings.m_daysInSeason;
        }

        public int GetDaysInSeason(Season season)
        {
            return GetSeasonSettings(season).m_daysInSeason;
        }

        public float GetPlantsGrowthMultiplier()
        {
            return GetPlantsGrowthMultiplier(GetCurrentSeason());
        }

        public float GetPlantsGrowthMultiplier(Season season)
        {
            return GetSeasonSettings(season).m_plantsGrowthMultiplier;
        }

        public Season GetNextSeason()
        {
            return GetNextSeason(m_season);
        }

        public Season GetPreviousSeason(Season season)
        {
            return (Season)((seasonsCount + (int)season - 1) % seasonsCount);
        }

        public Season GetNextSeason(Season season)
        {
            return (Season)(((int)season + 1) % seasonsCount);
        }

        public int GetNightLength()
        {
            return settings.m_nightLength;
        }

        public bool OverrideNightLength()
        {
            float nightLength = GetNightLength();
            return nightLength > 0 && nightLength != SeasonSettings.nightLentghDefault;
        }

        public void UpdateSeasonSettings()
        {
            if (!IsActive)
                return;

            foreach (KeyValuePair<int, string> item in seasonsSettingsJSON.Value)
            {
                try
                {
                    if (!String.IsNullOrEmpty(item.Value))
                    {
                        seasonsSettings[(Season)item.Key] = new SeasonSettings((Season)item.Key, JsonConvert.DeserializeObject<SeasonSettingsFile>(item.Value));
                        LogInfo($"Settings updated: {(Season)item.Key}");
                    }
                }
                catch (Exception e)
                {
                    LogWarning($"Error parsing settings: {(Season)item.Key}\n{e}");
                }
            }

            UpdateTorchesFireWarmth();
        }

        public void UpdateSeasonEnvironments()
        {
            if (!IsActive)
                return;

            if (!controlEnvironments.Value)
                return;

            SeasonEnvironment.ClearCachedObjects();

            if (!String.IsNullOrEmpty(customEnvironmentsJSON.Value))
            {
                try
                {
                    seasonEnvironments = JsonConvert.DeserializeObject<List<SeasonEnvironment>>(customEnvironmentsJSON.Value);
                    LogInfo($"Custom environments updated");
                }
                catch (Exception e)
                {
                    LogWarning($"Error parsing custom environments:\n{e}");
                }
            }

            foreach (SeasonEnvironment senv in seasonEnvironments)
                EnvMan.instance.AppendEnvironment(senv.ToEnvSetup());
        }

        public void UpdateBiomeEnvironments()
        {
            if (!IsActive)
                return;

            if (!controlEnvironments.Value)
                return;

            if (!String.IsNullOrEmpty(customBiomeEnvironmentsJSON.Value))
            {
                try
                {
                    seasonBiomeEnvironments = JsonConvert.DeserializeObject<SeasonBiomeEnvironments>(customBiomeEnvironmentsJSON.Value);
                    LogInfo($"Custom biome environments updated");
                }
                catch (Exception e)
                {
                    LogWarning($"Error parsing custom biome environments:\n{e}");
                }
            }

            SeasonBiomeEnvironment biomeEnv = seasonBiomeEnvironments.GetSeasonBiomeEnvironment(GetCurrentSeason());

            EnvMan.instance.m_biomes.Clear();

            foreach (BiomeEnvSetup biomeEnvironmentDefault in biomesDefault)
            {
                try
                {
                    BiomeEnvSetup biomeEnvironment = JsonUtility.FromJson<BiomeEnvSetup>(JsonUtility.ToJson(biomeEnvironmentDefault));

                    foreach (SeasonBiomeEnvironment.EnvironmentReplace replace in biomeEnv.replace)
                        biomeEnvironment.m_environments.DoIf(env => env.m_environment == replace.m_environment, env => env.m_environment = replace.replace_to);

                    foreach (SeasonBiomeEnvironment.EnvironmentAdd add in biomeEnv.add)
                        if (add.m_name == biomeEnvironment.m_name && !biomeEnvironment.m_environments.Any(env => env.m_environment == add.m_environment.m_environment))
                            biomeEnvironment.m_environments.Add(add.m_environment);

                    foreach (SeasonBiomeEnvironment.EnvironmentRemove remove in biomeEnv.remove)
                        biomeEnvironment.m_environments.DoIf(env => biomeEnvironment.m_name == remove.m_name && env.m_environment == remove.m_environment, env => biomeEnvironment.m_environments.Remove(env));

                    EnvMan.instance.AppendBiomeSetup(biomeEnvironment);
                }
                catch (Exception e)
                {
                    LogWarning($"Error appending biome setup {biomeEnvironmentDefault.m_name}:\n{e}");
                }
            }
        }

        public void UpdateCurrentEnvironment()
        {
            if (!controlEnvironments.Value)
                return;

            EnvMan.instance.m_environmentPeriod--;
        }

        public void UpdateRandomEvents()
        {
            if (!IsActive)
                return;

            if (!String.IsNullOrEmpty(customEventsJSON.Value))
            {
                try
                {
                    seasonRandomEvents = JsonConvert.DeserializeObject<SeasonRandomEvents>(customEventsJSON.Value);
                    LogInfo($"Custom events updated");
                }
                catch (Exception e)
                {
                    LogWarning($"Error parsing custom events:\n{e}");
                }
            }
        }

        public void UpdateLightings()
        {
            if (!IsActive)
                return;

            if (!String.IsNullOrEmpty(customLightingsJSON.Value))
            {
                try
                {
                    seasonLightings = JsonConvert.DeserializeObject<SeasonLightings>(customLightingsJSON.Value);
                    LogInfo($"Custom lightings updated");
                }
                catch (Exception e)
                {
                    LogWarning($"Error parsing custom lightings:\n{e}");
                }
            }
        }

        public void UpdateStats()
        {
            if (!IsActive)
                return;

            if (!String.IsNullOrEmpty(customStatsJSON.Value))
            {
                try
                {
                    seasonStats = JsonConvert.DeserializeObject<SeasonStats>(customStatsJSON.Value);
                    LogInfo($"Custom stats updated");
                }
                catch (Exception e)
                {
                    LogWarning($"Error parsing custom stats:\n{e}");
                }
            }

            SE_Season.UpdateSeasonStatusEffectStats();
        }
        public double GetEndOfCurrentSeason()
        {
            return GetStartOfCurrentSeason() + seasonState.GetDaysInSeason() * EnvMan.instance.m_dayLengthSec;
        }

        public double GetStartOfCurrentSeason()
        {
            double startOfDay = ZNet.instance.GetTimeSeconds() - ZNet.instance.GetTimeSeconds() % EnvMan.instance.m_dayLengthSec;
            return startOfDay - (GetCurrentDay() - 1) * EnvMan.instance.m_dayLengthSec;
        }

        public float DayStartFraction()
        {
            return (seasonState.GetNightLength() / 2f) / 100f;
        }

        public bool GetTorchAsFiresource()
        {
            return settings.m_torchAsFiresource;
        }

        public float GetTorchDurabilityDrain()
        {
            return settings.m_torchDurabilityDrain;
        }

        public float GetBeehiveProductionMultiplier()
        {
            return GetBeehiveProductionMultiplier(seasonState.GetCurrentSeason());
        }

        public float GetBeehiveProductionMultiplier(Season season)
        {
            return GetSeasonSettings(season).m_beehiveProductionMultiplier;
        }

        public float GetFoodDrainMultiplier()
        {
            return settings.m_foodDrainMultiplier;
        }

        public float GetStaminaDrainMultiplier()
        {
            return settings.m_staminaDrainMultiplier;
        }

        public float GetFireplaceDrainMultiplier()
        {
            return settings.m_fireplaceDrainMultiplier;
        }

        public float GetSapCollectingSpeedMultiplier()
        {
            return settings.m_sapCollectingSpeedMultiplier;
        }

        public bool GetRainProtection()
        {
            return settings.m_rainProtection;
        }

        public float GetWoodFromTreesMultiplier()
        {
            return settings.m_woodFromTreesMultiplier;
        }

        public float GetMeatFromAnimalsMultiplier()
        {
            return settings.m_meatFromAnimalsMultiplier;
        }

        public float GetWindIntensityMultiplier()
        {
            return settings.m_windIntensityMultiplier;
        }

        public float GetRestedBuffDurationMultiplier()
        {
            return settings.m_restedBuffDurationMultiplier;
        }

        public float GetLivestockProcreationMultiplier()
        {
            return settings.m_livestockProcreationMultiplier;
        }

        public bool GetOverheatIn2WarmClothes()
        {
            return settings.m_overheatIn2WarmClothes;
        }

        private SeasonSettings GetSeasonSettings(Season season)
        {
            return seasonsSettings[season] ?? new SeasonSettings(season);
        }

        private Season GetSeason(int day)
        {
            int dayOfYear = GetDayOfYear(day);
            int days = 0;
            foreach (Season season in Enum.GetValues(typeof(Season)))
            {
                days += GetDaysInSeason(season);
                if (dayOfYear <= days)
                    return season;
            }

            return Season.Winter;
        }

        private int GetDayInSeason(int day)
        {
            int dayOfYear = GetDayOfYear(day);
            int days = 0;
            foreach (Season season in Enum.GetValues(typeof(Season)))
            {
                int daysInSeason = GetDaysInSeason(season);
                if (dayOfYear <= days + daysInSeason)
                    break;
                days += daysInSeason;
            }
            return dayOfYear - days;
        }

        private int GetDayOfYear(int day)
        {
            return day % GetYearLengthInDays();
        }

        public float GetWaterSurfaceFreezeStatus()
        {
            if (GetCurrentSeason() != Season.Winter || waterFreezesAfterDaysOfWinter.Value <= 0)
                return 0f;

            return Mathf.Clamp01((float)GetCurrentDay() / Math.Min(waterFreezesAfterDaysOfWinter.Value, GetDaysInSeason() + 1));
        }

        private void CheckIfSeasonChanged(int season)
        {
            if (season == (int)m_season)
                return;

            if (!showFadeOnSeasonChange.Value || Hud.instance == null || Hud.instance.m_loadingScreen.isActiveAndEnabled || Hud.instance.m_loadingScreen.alpha > 0)
                SeasonChanged();
            else
                Seasons.instance.StartCoroutine(seasonState.SeasonChangedFadeEffect());
        }

        public IEnumerator SeasonChangedFadeEffect()
        {
            m_seasonIsChanging = true;

            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead() || player.IsTeleporting() || Game.instance.IsShuttingDown() || player.IsSleeping())
            {
                SeasonChanged();
                m_seasonIsChanging = false;
                yield break;
            }

            float fadeDuration = fadeOnSeasonChangeDuration.Value / 2;

            Hud.instance.m_loadingScreen.gameObject.SetActive(value: true);
            Hud.instance.m_loadingProgress.SetActive(value: false);
            Hud.instance.m_sleepingProgress.SetActive(value: false);
            Hud.instance.m_teleportingProgress.SetActive(value: false);

            while (Hud.instance.m_loadingScreen.alpha <= 0.99f)
            {
                if (player == null || player.IsDead() || player.IsTeleporting() || Game.instance.IsShuttingDown() || player.IsSleeping())
                {
                    SeasonChanged();
                    m_seasonIsChanging = false;
                    yield break;
                }

                Hud.instance.m_loadingScreen.alpha = Mathf.MoveTowards(Hud.instance.m_loadingScreen.alpha, 1f, Time.fixedDeltaTime / fadeDuration);

                yield return new WaitForFixedUpdate();
            }

            SeasonChanged();

            while (Hud.instance.m_loadingScreen.alpha > 0f)
            {
                if (player == null || player.IsDead() || player.IsTeleporting() || Game.instance.IsShuttingDown() || player.IsSleeping())
                {
                    m_seasonIsChanging = false;
                    yield break;
                }

                Hud.instance.m_loadingScreen.alpha = Mathf.MoveTowards(Hud.instance.m_loadingScreen.alpha, 0f, Time.fixedDeltaTime / fadeDuration);

                yield return new WaitForFixedUpdate();
            }

            Hud.instance.m_loadingScreen.gameObject.SetActive(value: false);
            m_seasonIsChanging = false;
        }

        private void SeasonChanged()
        {
            UpdateBiomeEnvironments();
            UpdateCurrentEnvironment();

            if (UseTextureControllers())
            {
                PrefabVariantController.UpdatePrefabColors();
                TerrainVariantController.UpdateTerrainColors();
                ClutterVariantController.instance.UpdateColors();
                WaterVariantController.UpdateWaterState();
                MinimapVariantController.instance.UpdateColors();
                UpdateTorchesFireWarmth();
                UpdateMinimapBorder();
                
                if (Player.m_localPlayer != null)
                {
                    Player.m_localPlayer.UpdateCurrentSeason();
                    CheckOverheatStatus(Player.m_localPlayer);
                }
            }
        }

        private void CheckIfDayChanged(int dayInSeason)
        {
            if (m_day == dayInSeason)
                return;

            m_day = dayInSeason;
            if (UseTextureControllers())
            {
                ClutterVariantController.instance.UpdateColors();
                WaterVariantController.UpdateWaterState();
            }
        }

        private int GetYearLengthInDays()
        {
            int days = 0;
            foreach (Season season in Enum.GetValues(typeof(Season)))
            {
                days += GetDaysInSeason(season);
            }
            return days;
        }

        public override string ToString()
        {
            return $"{m_season} day:{m_day}";
        }

        public void PatchTorchItemData(ItemDrop.ItemData torch)
        {
            if (torch == null)
                return;

            if (torch.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Torch)
                return;

            if (seasonState.GetTorchAsFiresource() && IsActive && (EnvMan.instance.IsWet() || EnvMan.instance.IsCold() || EnvMan.instance.IsFreezing()))
                torch.m_shared.m_durabilityDrain = seasonState.GetTorchDurabilityDrain();
            else
                torch.m_shared.m_durabilityDrain = 0.0333f;
        }

        public void UpdateTorchesFireWarmth()
        {
            GameObject prefabGoblinTorch = ObjectDB.instance.GetItemPrefab("GoblinTorch");
            if (prefabGoblinTorch != null)
                UpdateTorchFireWarmth(prefabGoblinTorch);

            GameObject prefabTorchMist = ObjectDB.instance.GetItemPrefab("TorchMist");
            if (prefabTorchMist != null)
                UpdateTorchFireWarmth(prefabTorchMist);

            GameObject prefabTorch = ObjectDB.instance.GetItemPrefab(SeasonSettings.itemNameTorch);
            if (prefabTorch != null)
                UpdateTorchFireWarmth(prefabTorch);

            if (Player.m_localPlayer != null)
                PatchTorchesInInventory(Player.m_localPlayer.GetInventory());
        }

        public void PatchTorchesInInventory(Inventory inventory)
        {
            List<ItemDrop.ItemData> items = new List<ItemDrop.ItemData>();
            inventory.GetAllItems(SeasonSettings.itemDropNameTorch, items);

            foreach (ItemDrop.ItemData item in items)
                PatchTorchItemData(item);
        }

        public void UpdateTorchFireWarmth(GameObject prefab, string childName = "FireWarmth")
        {
            Transform fireWarmth = Utils.FindChild(prefab.transform, childName);
            if (fireWarmth == null)
                return;

            EffectArea component = fireWarmth.gameObject.GetComponent<EffectArea>();
            if (component == null)
                return;

            component.m_type = seasonState.GetTorchAsFiresource() ? EffectArea.Type.Heat | EffectArea.Type.Fire : EffectArea.Type.Fire;

            ItemDrop item = prefab.GetComponent<ItemDrop>();
            PatchTorchItemData(item.m_itemData);
        }

        public void UpdateMinimapBorder()
        {
            if (!seasonalMinimapBorderColor.Value || Minimap.instance == null)
                return;

            if (!Minimap.instance.m_smallRoot.TryGetComponent(out UnityEngine.UI.Image image) || image.sprite == null || image.sprite.name != "InputFieldBackground")
                return;

            if (minimapBorderColor == Color.clear)
                minimapBorderColor = image.color;

            switch (GetCurrentSeason())
            {
                case Season.Spring:
                    image.color = new Color(0.44f, 0.56f, 0.03f, minimapBorderColor.a / 2f);
                    break;
                case Season.Summer:
                    image.color = new Color(0.69f, 0.73f, 0.05f, minimapBorderColor.a / 2f);
                    break;
                case Season.Fall:
                    image.color = new Color(0.79f, 0.32f, 0f, minimapBorderColor.a / 2f);
                    break;
                case Season.Winter:
                    image.color = new Color(0.89f, 0.94f, 0.96f, minimapBorderColor.a / 2f);
                    break;
            }
        }

        public void CheckOverheatStatus(Humanoid humanoid)
        {
            bool getOverheat = seasonState.GetOverheatIn2WarmClothes();
            int warmClothesCount = humanoid.GetInventory().GetEquippedItems().Count(itemData => itemData.m_shared.m_damageModifiers.Any(dmod => dmod.m_type == HitData.DamageType.Frost && dmod.m_modifier == HitData.DamageModifier.Resistant));
            bool haveOverheat = humanoid.GetSEMan().HaveStatusEffect(statusEffectOverheatHash);
            if (!getOverheat)
            {
                if (haveOverheat)
                    humanoid.GetSEMan().RemoveStatusEffect(statusEffectOverheatHash);
            }
            else
            {
                if (!haveOverheat && warmClothesCount > 1)
                    humanoid.GetSEMan().AddStatusEffect(statusEffectOverheatHash);
                else if (haveOverheat && warmClothesCount <= 1)
                    humanoid.GetSEMan().RemoveStatusEffect(statusEffectOverheatHash);
            }
        }

        public static void CheckSeasonChange()
        {
            if (seasonState.IsActive)
                seasonState.UpdateState(EnvMan.instance.GetCurrentDay(), seasonCanBeChanged: true);
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.UpdateTriggers))]
    public static class EnvMan_UpdateTriggers_SeasonStateUpdate
    {
        private static void Postfix(EnvMan __instance, float oldDayFraction, float newDayFraction)
        {
            bool seasonCanBeChanged = (oldDayFraction > 0.18f && oldDayFraction < 0.23f && newDayFraction > 0.23f && newDayFraction < 0.3f);
            seasonState.UpdateState(__instance.GetCurrentDay(), seasonCanBeChanged);
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.RescaleDayFraction))]
    public static class EnvMan_RescaleDayFraction_DayNightLength
    {
        public static bool Prefix(float fraction, ref float __result)
        {
            if (!seasonState.OverrideNightLength())
                return true;

            float dayStart = (seasonState.GetNightLength() / 2f) / 100f;
            float nightStart = 1.0f - dayStart;

            if (fraction >= dayStart && fraction <= nightStart)
            {
                float num = (fraction - dayStart) / (nightStart - dayStart);
                fraction = 0.25f + num * 0.5f;
            }
            else if (fraction < 0.5f)
            {
                fraction = fraction / dayStart * 0.25f;
            }
            else
            {
                float num2 = (fraction - nightStart) / dayStart;
                fraction = 0.75f + num2 * 0.25f;
            }

            __result = fraction;
            return false;
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.GetMorningStartSec))]
    public static class EnvMan_GetMorningStartSec_DayNightLength
    {
        public static bool Prefix(EnvMan __instance, int day, ref double __result)
        {
            if (!seasonState.OverrideNightLength())
                return true;

            __result = (float)(day * __instance.m_dayLengthSec) + (float)__instance.m_dayLengthSec * seasonState.DayStartFraction();
            return false;
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.SkipToMorning))]
    public static class EnvMan_SkipToMorning_DayNightLength
    {
        public static bool Prefix(EnvMan __instance, ref bool ___m_skipTime, ref double ___m_skipToTime, ref double ___m_timeSkipSpeed)
        {
            if (!seasonState.OverrideNightLength())
                return true;

            double timeSeconds = ZNet.instance.GetTimeSeconds();
            double time = timeSeconds - (double)((float)__instance.m_dayLengthSec * seasonState.DayStartFraction());
            int day = __instance.GetDay(time);
            double morningStartSec = __instance.GetMorningStartSec(day + 1);
            ___m_skipTime = true;
            ___m_skipToTime = morningStartSec;
            double num = morningStartSec - timeSeconds;
            ___m_timeSkipSpeed = num / 12.0;
            ZLog.Log("Time " + timeSeconds + ", day:" + day + "    nextm:" + morningStartSec + "  skipspeed:" + ___m_timeSkipSpeed);

            return false;
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UpdateEquipment))]
    public static class Humanoid_UpdateEquipment_ToggleTorchesWarmth
    {
        private static void Prefix(Humanoid __instance)
        {
            if (__instance == null || !__instance.IsPlayer())
                return;

            seasonState.PatchTorchItemData(__instance.m_rightItem);
            seasonState.PatchTorchItemData(__instance.m_leftItem);
        }
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
    public static class ObjectDB_Awake_TorchPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            seasonState.UpdateTorchesFireWarmth();
        }
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    public static class ObjectDB_CopyOtherDB_TorchPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            seasonState.UpdateTorchesFireWarmth();
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.AddKnownItem))]
    public static class Player_AddKnownItem_TorchPatch
    {
        private static void Postfix(ref ItemDrop.ItemData item)
        {
            if (item.m_shared.m_name != SeasonSettings.itemDropNameTorch)
                return;

            seasonState.PatchTorchItemData(item);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    public class Player_OnSpawned_TorchPatch
    {
        public static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer)
                return;

            seasonState.PatchTorchesInInventory(__instance.GetInventory());
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.Load))]
    public class Inventory_Load_TorchPatch
    {
        public static void Postfix(Inventory __instance)
        {
            seasonState.PatchTorchesInInventory(__instance);
        }
    }

    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.Start))]
    public static class ItemDrop_Start_TorchPatch
    {
        private static void Postfix(ref ItemDrop __instance)
        {
            if (__instance.GetPrefabName(__instance.name) != SeasonSettings.itemNameTorch)
                return;

            seasonState.PatchTorchItemData(__instance.m_itemData);
        }
    }

    [HarmonyPatch(typeof(SeasonalItemGroup), nameof(SeasonalItemGroup.IsInSeason))]
    public static class SeasonalItemGroup_IsInSeason_SeasonalItems
    {
        private static void Postfix(SeasonalItemGroup __instance, ref bool __result)
        {
            if (!enableSeasonalItems.Value)
                return;

            Season season = seasonState.GetCurrentSeason();
            __result = __result || __instance.name == "Halloween" && season == Season.Fall
                                || __instance.name == "Midsummer" && season == Season.Summer
                                || __instance.name == "Yule" && season == Season.Winter;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    public static class Character_ApplyDamage_PreventDeathFromFreezing
    {
        private static void Prefix(Character __instance, ref HitData hit)
        {
            if (!preventDeathFromFreezing.Value)
                return;

            if (!__instance.IsPlayer())
                return;

            if (__instance != Player.m_localPlayer)
                return;

            if (hit.m_hitType != HitData.HitType.Freezing)
                return;

            Heightmap.Biome biome = (__instance as Player).GetCurrentBiome();
            if (biome == Heightmap.Biome.Mountain || biome == Heightmap.Biome.DeepNorth)
                return;

            if (hit.GetTotalDamage() >= __instance.GetHealth() + 0.1f)
                hit.ApplyModifier(0f);
        }
    }

    [HarmonyPatch(typeof(Pickable), nameof(Pickable.UpdateRespawn))]
    public static class Pickable_UpdateRespawn_PlantsGrowthMultiplier
    {
        private static bool Prefix(Pickable __instance, ref int ___m_respawnTimeMinutes, ZNetView ___m_nview, bool ___m_picked, ref int __state)
        {
            if (seasonState.GetPlantsGrowthMultiplier() == 0f)
                return false;

            if (!___m_nview.IsValid() || !___m_nview.IsOwner() || !___m_picked)
                return true;

            if (__instance.m_itemPrefab == null || !__instance.m_itemPrefab.TryGetComponent(out ItemDrop itemDrop) || itemDrop.m_itemData.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable)
                return true;

            __state = ___m_respawnTimeMinutes;
            ___m_respawnTimeMinutes = Mathf.CeilToInt(___m_respawnTimeMinutes / seasonState.GetPlantsGrowthMultiplier());

            return true;
        }

        private static void Postfix(ref int ___m_respawnTimeMinutes, ref int __state)
        {
            if (__state == 0)
                return;

            ___m_respawnTimeMinutes = __state;
        }
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.TimeSincePlanted))]
    public static class Plant_TimeSincePlanted_PlantsGrowthMultiplier
    {
        private static void Postfix(ref double __result)
        {
            double timeSeconds = ZNet.instance.GetTimeSeconds();
            double seasonStart = seasonState.GetStartOfCurrentSeason();
            Season season = seasonState.GetCurrentSeason();
            double rescaledResult = 0d;

            do
            {
                rescaledResult += (timeSeconds - seasonStart >= __result ? __result : timeSeconds - seasonStart) * seasonState.GetPlantsGrowthMultiplier(season);

                __result -= timeSeconds - seasonStart;
                timeSeconds = seasonStart;
                season = seasonState.GetPreviousSeason(season);
                seasonStart -= seasonState.GetDaysInSeason(season) * EnvMan.instance.m_dayLengthSec;

            } while (__result > 0);

            __result = rescaledResult;
        }
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.GetHoverText))]
    public static class Plant_GetHoverText_Duration
    {
        private static double GetGrowTime(Plant plant)
        {
            double timeSeconds = ZNet.instance.GetTimeSeconds();

            Season season = seasonState.GetCurrentSeason();
            float growthMultiplier = seasonState.GetPlantsGrowthMultiplier(season);

            double secondsToGrow = 0d;
            double secondsToSeasonEnd = seasonState.GetEndOfCurrentSeason() - timeSeconds;
            double secondsLeft = plant.GetGrowTime() - plant.TimeSincePlanted();//growthMultiplier == 0 ? growTime - timeSincePlanted : Math.Max(0, (growTime - timeSincePlanted) / growthMultiplier); 

            do
            {
                double timeInSeasonLeft = growthMultiplier == 0 ? secondsToSeasonEnd : Math.Min(secondsLeft / growthMultiplier, secondsToSeasonEnd);

                secondsToGrow += timeInSeasonLeft;
                secondsLeft -= timeInSeasonLeft * growthMultiplier;

                season = seasonState.GetNextSeason(season);
                growthMultiplier = seasonState.GetPlantsGrowthMultiplier(season);

                secondsToSeasonEnd = seasonState.GetDaysInSeason(season) * EnvMan.instance.m_dayLengthSec;

            } while (secondsLeft > 0);

            return secondsToGrow;
        }

        private static void Postfix(Plant __instance, ref string __result)
        {
            if (hoverPlant.Value == StationHover.Vanilla)
                return;

            if (__result.IsNullOrWhiteSpace())
                return;

            if (__instance.GetStatus() != Plant.Status.Healthy)
                return;

            if (hoverPlant.Value == StationHover.Percentage)
                __result += $"\n{__instance.TimeSincePlanted() / __instance.GetGrowTime():P0}";
            else if (hoverPlant.Value == StationHover.MinutesSeconds)
                __result += $"\n{FromSeconds(GetGrowTime(__instance))}";
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.Start))]
    public static class Minimap_Start_MinimapSeasonalBorderColor
    {
        private static void Postfix()
        {
            if (!seasonState.IsActive)
                return;

            seasonState.UpdateMinimapBorder();
        }
    }

    [HarmonyPatch(typeof(Beehive), nameof(Beehive.Interact))]
    public static class Beehive_Interact_BeesInteractionMessage
    {
        private static void Prefix(ref Beehive __instance, ref string __state)
        {
            __state = __instance.m_happyText;
            if (seasonState.GetBeehiveProductionMultiplier() == 0f)
                __instance.m_happyText = __instance.m_sleepText;
        }

        private static void Postfix(ref Beehive __instance, ref string __state)
        {
            __instance.m_happyText = __state;
        }
    }

    [HarmonyPatch(typeof(Beehive), nameof(Beehive.GetTimeSinceLastUpdate))]
    public static class Beehive_GetTimeSinceLastUpdate_BeesProduction
    {
        private static void Postfix(ref float __result)
        {
            __result *= seasonState.GetBeehiveProductionMultiplier();
        }
    }

    [HarmonyPatch(typeof(Beehive), nameof(Beehive.GetHoverText))]
    public static class Beehive_GetHoverText_Duration
    {
        private static double GetNextProduct(Beehive beehive, float product, int count = 1)
        {
            double timeSeconds = ZNet.instance.GetTimeSeconds();

            Season season = seasonState.GetCurrentSeason();
            float multiplier = seasonState.GetBeehiveProductionMultiplier(season);

            double secondsToProduct = 0d;
            double secondsToSeasonEnd = seasonState.GetEndOfCurrentSeason() - timeSeconds;
            double secondsLeft = beehive.m_secPerUnit * count - product;

            do
            {
                double timeInSeasonLeft = multiplier == 0 ? secondsToSeasonEnd : Math.Min(secondsLeft / multiplier, secondsToSeasonEnd);

                secondsToProduct += timeInSeasonLeft;
                secondsLeft -= timeInSeasonLeft * multiplier;

                season = seasonState.GetNextSeason(season);
                multiplier = seasonState.GetBeehiveProductionMultiplier(season);

                secondsToSeasonEnd = seasonState.GetDaysInSeason(season) * EnvMan.instance.m_dayLengthSec;

            } while (secondsLeft > 0);

            return secondsToProduct;
        }

        private static void Postfix(Beehive __instance, ref string __result)
        {
            if (hoverBeeHive.Value == StationHover.Vanilla)
                return;

            if (__result.IsNullOrWhiteSpace())
                return;

            int honeyLevel = __instance.GetHoneyLevel();

            if (!PrivateArea.CheckAccess(__instance.transform.position, 0f, flash: false) || honeyLevel == __instance.m_maxHoney)
                return;

            float product = __instance.m_nview.GetZDO().GetFloat(ZDOVars.s_product);

            if (hoverBeeHive.Value == StationHover.Percentage)
                __result += $"\n{product / __instance.m_secPerUnit:P0}";
            else if (hoverBeeHive.Value == StationHover.MinutesSeconds)
                __result += $"\n{FromSeconds(GetNextProduct(__instance, product, 1))}";

            if (hoverBeeHiveTotal.Value && honeyLevel < 3)
                if (hoverBeeHive.Value == StationHover.Percentage)
                    __result += $"\n{(product + __instance.m_secPerUnit * honeyLevel) / (__instance.m_secPerUnit * __instance.m_maxHoney):P0}";
                else if (hoverBeeHive.Value == StationHover.MinutesSeconds)
                    __result += $"\n{FromSeconds(GetNextProduct(__instance, product, __instance.m_maxHoney - honeyLevel))}";
        }
    }

    [HarmonyPatch(typeof(Beehive), nameof(Beehive.UpdateBees))]
    public static class Beehive_UpdateBees_BeesSleeping
    {
        private static void Postfix(ref GameObject ___m_beeEffect)
        {
            if (seasonState.GetBeehiveProductionMultiplier() == 0f)
            {
                ___m_beeEffect.SetActive(false);
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.UpdateFood))]
    public static class Player_UpdateFood_FoodDrainMultiplier
    {
        private static void Prefix(Player __instance, float dt, bool forceUpdate)
        {
            if (seasonState.GetFoodDrainMultiplier() == 1.0f)
                return;

            if (__instance == null)
                return;

            if (__instance.InInterior() || __instance.InShelter())
                return;

            if (!(dt + __instance.m_foodUpdateTimer >= 1f || forceUpdate))
                return;

            foreach (Player.Food food in __instance.m_foods)
                food.m_time += 1f - Math.Max(0f, seasonState.GetFoodDrainMultiplier());
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.UseStamina))]
    public static class Player_UseStamina_StaminaDrainMultiplier
    {
        private static void Prefix(Player __instance, ref float v)
        {
            if (__instance == null)
                return;

            if (__instance.InInterior() || __instance.InShelter())
                return;

            v *= Math.Max(0f, seasonState.GetStaminaDrainMultiplier());
        }
    }

    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.GetTimeSinceLastUpdate))]
    static class Fireplace_GetTimeSinceLastUpdate_FireplaceDrainMultiplier
    {
        private static void Postfix(ref double __result)
        {
            __result *= (double)Math.Max(0f, seasonState.GetFireplaceDrainMultiplier());
        }
    }

    [HarmonyPatch(typeof(Smelter), nameof(Smelter.GetDeltaTime))]
    static class Smelter_GetDeltaTime_FireplaceDrainMultiplier_SmeltingSpeedMultiplier
    {
        private static void Postfix(Smelter __instance, ref double __result)
        {
            if (__instance.m_name != "$piece_bathtub")
                return;

            __result *= (double)Math.Max(0f, seasonState.GetFireplaceDrainMultiplier());
        }
    }

    [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.UpdateFuel))]
    static class CookingStation_UpdateFuel_FireplaceDrainMultiplier
    {
        private static void Prefix(ref float dt, ref float __state)
        {
            __state = dt;
            dt *= Math.Max(0f, seasonState.GetFireplaceDrainMultiplier());
        }

        private static void Postfix(ref float dt, float __state)
        {
            dt = __state;
        }
    }

    [HarmonyPatch(typeof(SapCollector), nameof(SapCollector.GetTimeSinceLastUpdate))]
    static class SapCollector_GetTimeSinceLastUpdate_SapCollectingSpeedMultiplier
    {
        private static void Postfix(ref float __result)
        {
            __result *= Math.Max(0f, seasonState.GetSapCollectingSpeedMultiplier());
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateWear))]
    public static class WearNTear_UpdateWear_RainProtection
    {
        private static void Prefix(ZNetView ___m_nview, ref bool ___m_noRoofWear, ref bool __state)
        {
            if (!seasonState.GetRainProtection())
                return;

            if (___m_nview == null || !___m_nview.IsValid())
                return;

            __state = ___m_noRoofWear;

            ___m_noRoofWear = false;
        }

        private static void Postfix(ref bool ___m_noRoofWear, bool __state)
        {
            if (!seasonState.GetRainProtection())
                return;

            if (__state != true) return;

            ___m_noRoofWear = __state;
        }
    }

    [HarmonyPatch(typeof(TreeLog), nameof(TreeLog.Destroy))]
    public static class TreeLog_Destroy_TreeWoodDrop
    {
        public static void ApplyWoodMultiplier(DropTable m_dropWhenDestroyed)
        {
            if (!m_dropWhenDestroyed.m_drops.Any(dd => dd.m_item.name == "Wood" || dd.m_item.name != "FineWood" || dd.m_item.name != "RoundLog" || dd.m_item.name != "ElderBark" || dd.m_item.name != "YggdrasilWood"))
                return;

            m_dropWhenDestroyed.m_dropMax = Mathf.CeilToInt(m_dropWhenDestroyed.m_dropMax * seasonState.GetWoodFromTreesMultiplier());
            if (m_dropWhenDestroyed.m_dropMin < m_dropWhenDestroyed.m_dropMax)
                m_dropWhenDestroyed.m_dropMin = m_dropWhenDestroyed.m_dropMax;
        }

        private static void Prefix(ZNetView ___m_nview, ref DropTable ___m_dropWhenDestroyed)
        {
            if (seasonState.GetWoodFromTreesMultiplier() == 1.0f)
                return;

            if (!___m_nview.IsOwner())
                return;

            if (___m_nview == null || !___m_nview.IsValid())
                return;

            ApplyWoodMultiplier(___m_dropWhenDestroyed);
        }
    }

    [HarmonyPatch(typeof(DropOnDestroyed), nameof(DropOnDestroyed.OnDestroyed))]
    public static class DropOnDestroyed_OnDestroyed_TreeWoodDrop
    {
        private static void Prefix(DropOnDestroyed __instance, ref DropTable ___m_dropWhenDestroyed)
        {
            if (seasonState.GetWoodFromTreesMultiplier() == 1.0f)
                return;

            if (!__instance.TryGetComponent(out Destructible destructible) || destructible.GetDestructibleType() != DestructibleType.Tree)
                return;

            TreeLog_Destroy_TreeWoodDrop.ApplyWoodMultiplier(___m_dropWhenDestroyed);
        }
    }

    [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
    public static class CharacterDrop_GenerateDropList_TreeWoodDrop
    {
        public static void ApplyMeatMultiplier(List<Drop> m_drops)
        {
            foreach (Drop drop in m_drops)
            {
                if (drop.m_prefab.name != "RawMeat" && drop.m_prefab.name != "DeerMeat" && drop.m_prefab.name != "NeckTail" && drop.m_prefab.name != "WolfMeat" &&
                    drop.m_prefab.name != "LoxMeat" && drop.m_prefab.name != "ChickenMeat" && drop.m_prefab.name != "HareMeat" && drop.m_prefab.name != "SerpentMeat")
                    continue;

                drop.m_amountMax = Mathf.CeilToInt(drop.m_amountMax * seasonState.GetMeatFromAnimalsMultiplier());
                if (drop.m_amountMin < drop.m_amountMax)
                    drop.m_amountMin = drop.m_amountMax;
            }
        }

        private static void Prefix(ref List<Drop> ___m_drops)
        {
            if (seasonState.GetMeatFromAnimalsMultiplier() == 1.0f)
                return;

            ApplyMeatMultiplier(___m_drops);
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.GetWindForce))]
    public static class EnvMan_GetWindForce_WindIntensityMultiplier
    {
        private static void Prefix(ref Vector4 ___m_wind, ref float __state)
        {
            if (seasonState.GetWindIntensityMultiplier() == 1.0f)
                return;

            __state = ___m_wind.w;
            ___m_wind.w *= seasonState.GetWindIntensityMultiplier();
        }

        private static void Postfix(ref Vector4 ___m_wind, float __state)
        {
            if (seasonState.GetWindIntensityMultiplier() == 1.0f)
                return;

            ___m_wind.w = __state;
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.GetWindIntensity))]
    public static class EnvMan_GetWindIntensity_WindIntensityMultiplier
    {
        private static void Postfix(ref float __result)
        {
            __result *= seasonState.GetWindIntensityMultiplier();
        }
    }

    [HarmonyPatch(typeof(SE_Rested), nameof(SE_Rested.UpdateTTL))]
    public static class SE_Rested_UpdateTTL_RestedBuffDuration
    {
        private static void Prefix(ref float ___m_baseTTL, ref float ___m_TTLPerComfortLevel, ref Tuple<float, float> __state)
        {
            if (seasonState.GetRestedBuffDurationMultiplier() == 1.0f)
                return;

            __state = new Tuple<float, float> (___m_baseTTL, ___m_TTLPerComfortLevel);
            ___m_baseTTL *= seasonState.GetRestedBuffDurationMultiplier();
            ___m_TTLPerComfortLevel *= seasonState.GetRestedBuffDurationMultiplier();
        }

        private static void Postfix(ref float ___m_baseTTL, ref float ___m_TTLPerComfortLevel, Tuple<float, float> __state)
        {
            if (seasonState.GetRestedBuffDurationMultiplier() == 1.0f)
                return;

            ___m_baseTTL = __state.Item1;
            ___m_TTLPerComfortLevel = __state.Item2;
        }
    }

    [HarmonyPatch(typeof(Procreation), nameof(Procreation.Procreate))]
    public static class Procreation_Procreate_ProcreationMultiplier
    {
        private static void Prefix(ref Procreation __instance, ref Dictionary<string, float> __state)
        {
            if (seasonState.GetLivestockProcreationMultiplier() == 1.0f)
                return;

            __state = new Dictionary<string, float>() {
                { "m_totalCheckRange", __instance.m_totalCheckRange },
                { "m_partnerCheckRange", __instance.m_partnerCheckRange },
                { "m_pregnancyChance", __instance.m_pregnancyChance },
                { "m_pregnancyDuration", __instance.m_pregnancyDuration },
            };

            __instance.m_pregnancyChance *= seasonState.GetLivestockProcreationMultiplier();
            __instance.m_partnerCheckRange *= seasonState.GetLivestockProcreationMultiplier();
            if (seasonState.GetLivestockProcreationMultiplier() != 0f)
            {
                __instance.m_totalCheckRange /= seasonState.GetLivestockProcreationMultiplier();
                __instance.m_pregnancyDuration /= seasonState.GetLivestockProcreationMultiplier();
            }
        }

        private static void Postfix(ref Procreation __instance, ref Dictionary<string, float> __state)
        {
            if (seasonState.GetLivestockProcreationMultiplier() == 1.0f)
                return;

            __instance.m_pregnancyChance *= __state["m_pregnancyChance"];
            __instance.m_totalCheckRange *= __state["m_totalCheckRange"];
            __instance.m_partnerCheckRange *= __state["m_partnerCheckRange"];
            __instance.m_pregnancyDuration *= __state["m_pregnancyDuration"];
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
    public static class Humanoid_EquipItem_OverheatIn2WarmClothes
    {
        private static void Postfix(Humanoid __instance)
        {
            seasonState.CheckOverheatStatus(__instance);
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
    public static class Humanoid_UnequipItem_OverheatIn2WarmClothes
    {
        private static void Postfix(Humanoid __instance)
        {
            seasonState.CheckOverheatStatus(__instance);
        }
    }

    [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.GetPossibleRandomEvents))]
    public static class RandEventSystem_GetPossibleRandomEvents_RandomEventWeights
    {
        private static void Prefix(RandEventSystem __instance, ref List<RandomEvent> __state)
        {
            if (!controlRandomEvents.Value)
                return;

            List<SeasonRandomEvent> randEvents = SeasonState.seasonRandomEvents.GetSeasonEvents(seasonState.GetCurrentSeason());

            __state = new List<RandomEvent>();
            for (int i = 0; i < __instance.m_events.Count; i++)
            {
                RandomEvent randEvent = __instance.m_events[i];
                __state.Add(JsonUtility.FromJson<RandomEvent>(JsonUtility.ToJson(randEvent)));

                SeasonRandomEvent seasonRandEvent = randEvents.Find(re => re.m_name == randEvent.m_name);
                if (seasonRandEvent != null)
                {
                    if (seasonRandEvent.m_biomes != null)
                        randEvent.m_biome = seasonRandEvent.GetBiome();

                    if (seasonRandEvent.m_weight == 0)
                    {
                        randEvent.m_enabled = false;
                    }
                    else if (seasonRandEvent.m_weight > 1)
                    {
                        for (int r = 2; r <= seasonRandEvent.m_weight; r++)
                        {
                            RandEventSystem.instance.m_events.Insert(i, randEvent);
                            i++;
                        }
                    }
                }
            }
        }

        private static void Postfix(ref RandEventSystem __instance, List<RandomEvent> __state)
        {
            if (!controlRandomEvents.Value)
                return;

            __instance.m_events.Clear();
            __instance.m_events.AddRange(__state.ToList());
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.SetEnv))]
    public static class EnvMan_SetEnv_LuminancePatch
    {
        public static Color ChangeColorLuminance(Color color, float luminanceMultiplier)
        {
            HSLColor newColor = new HSLColor(color);
            newColor.l *= luminanceMultiplier;
            return newColor.ToRGBA();
        }

        [HarmonyPriority(Priority.Last)]
        public static void Prefix(EnvSetup env, ref Dictionary<string, Color> __state)
        {
            if (!controlLightings.Value)
                return;

            __state = new Dictionary<string, Color>
                {
                    { "m_ambColorNight", env.m_ambColorNight },
                    { "m_fogColorNight", env.m_fogColorNight },
                    { "m_fogColorSunNight", env.m_fogColorSunNight },
                    { "m_sunColorNight", env.m_sunColorNight },

                    { "m_ambColorDay", env.m_ambColorDay },
                    { "m_fogColorMorning", env.m_fogColorMorning },
                    { "m_fogColorDay", env.m_fogColorDay },
                    { "m_fogColorEvening", env.m_fogColorEvening },
                    { "m_fogColorSunMorning", env.m_fogColorSunMorning },
                    { "m_fogColorSunDay", env.m_fogColorSunDay },
                    { "m_fogColorSunEvening", env.m_fogColorSunEvening },
                    { "m_sunColorMorning", env.m_sunColorMorning },
                    { "m_sunColorDay", env.m_sunColorDay },
                    { "m_sunColorEvening", env.m_sunColorEvening },

                    { "m_lightIntensityDay", new Color(env.m_lightIntensityDay / 100f, 0f, 0f) },
                    { "m_lightIntensityNight", new Color(env.m_lightIntensityNight / 100f, 0f, 0f)},

                    { "m_fogDensityNight", new Color(env.m_fogDensityNight, 0f, 0f) },
                    { "m_fogDensityMorning", new Color(env.m_fogDensityMorning, 0f, 0f) },
                    { "m_fogDensityDay", new Color(env.m_fogDensityDay, 0f, 0f) },
                    { "m_fogDensityEvening", new Color(env.m_fogDensityEvening, 0f, 0f) }
                };

            SeasonLightingSettings lightingSettings = SeasonState.seasonLightings.GetSeasonLighting(seasonState.GetCurrentSeason());

            if (Player.m_localPlayer != null && Player.m_localPlayer.InInterior())
            {
                if (lightingSettings.indoors.luminanceMultiplier != 1.0f)
                {
                    env.m_fogColorMorning = ChangeColorLuminance(env.m_fogColorMorning, lightingSettings.indoors.luminanceMultiplier);
                    env.m_fogColorDay = ChangeColorLuminance(env.m_fogColorDay, lightingSettings.indoors.luminanceMultiplier);
                    env.m_fogColorEvening = ChangeColorLuminance(env.m_fogColorEvening, lightingSettings.indoors.luminanceMultiplier);
                    env.m_fogColorSunMorning = ChangeColorLuminance(env.m_fogColorSunMorning, lightingSettings.indoors.luminanceMultiplier);
                    env.m_fogColorSunDay = ChangeColorLuminance(env.m_fogColorSunDay, lightingSettings.indoors.luminanceMultiplier);
                    env.m_fogColorSunEvening = ChangeColorLuminance(env.m_fogColorSunEvening, lightingSettings.indoors.luminanceMultiplier);
                    env.m_sunColorMorning = ChangeColorLuminance(env.m_sunColorMorning, lightingSettings.indoors.luminanceMultiplier);
                    env.m_sunColorDay = ChangeColorLuminance(env.m_sunColorDay, lightingSettings.indoors.luminanceMultiplier);
                    env.m_sunColorEvening = ChangeColorLuminance(env.m_sunColorEvening, lightingSettings.indoors.luminanceMultiplier);
                    env.m_ambColorNight = ChangeColorLuminance(env.m_ambColorNight, lightingSettings.indoors.luminanceMultiplier);
                    env.m_fogColorNight = ChangeColorLuminance(env.m_fogColorNight, lightingSettings.indoors.luminanceMultiplier);
                    env.m_fogColorSunNight = ChangeColorLuminance(env.m_fogColorSunNight, lightingSettings.indoors.luminanceMultiplier);
                    env.m_sunColorNight = ChangeColorLuminance(env.m_sunColorNight, lightingSettings.indoors.luminanceMultiplier);

                }

                if (lightingSettings.indoors.fogDensityMultiplier != 1.0f)
                {
                    env.m_fogDensityNight *= lightingSettings.indoors.fogDensityMultiplier;
                    env.m_fogDensityMorning *= lightingSettings.indoors.fogDensityMultiplier;
                    env.m_fogDensityEvening *= lightingSettings.indoors.fogDensityMultiplier;
                    env.m_fogDensityDay *= lightingSettings.indoors.fogDensityMultiplier;
                }
            }
            else
            {
                env.m_ambColorNight = ChangeColorLuminance(env.m_ambColorNight, lightingSettings.night.luminanceMultiplier);
                env.m_fogColorNight = ChangeColorLuminance(env.m_fogColorNight, lightingSettings.night.luminanceMultiplier);
                env.m_fogColorSunNight = ChangeColorLuminance(env.m_fogColorSunNight, lightingSettings.night.luminanceMultiplier);
                env.m_sunColorNight = ChangeColorLuminance(env.m_sunColorNight, lightingSettings.night.luminanceMultiplier);

                env.m_fogDensityNight *= lightingSettings.night.fogDensityMultiplier;

                env.m_fogColorMorning = ChangeColorLuminance(env.m_fogColorMorning, lightingSettings.morning.luminanceMultiplier);
                env.m_fogColorSunMorning = ChangeColorLuminance(env.m_fogColorSunMorning, lightingSettings.morning.luminanceMultiplier);
                env.m_sunColorMorning = ChangeColorLuminance(env.m_sunColorMorning, lightingSettings.morning.luminanceMultiplier);

                env.m_fogDensityMorning *= lightingSettings.morning.fogDensityMultiplier;

                env.m_fogColorDay = ChangeColorLuminance(env.m_fogColorDay, lightingSettings.day.luminanceMultiplier);
                env.m_fogColorSunDay = ChangeColorLuminance(env.m_fogColorSunDay, lightingSettings.day.luminanceMultiplier);
                env.m_sunColorDay = ChangeColorLuminance(env.m_sunColorDay, lightingSettings.day.luminanceMultiplier);

                env.m_fogDensityDay *= lightingSettings.day.fogDensityMultiplier;

                env.m_fogColorEvening = ChangeColorLuminance(env.m_fogColorEvening, lightingSettings.evening.luminanceMultiplier);
                env.m_fogColorSunEvening = ChangeColorLuminance(env.m_fogColorSunEvening, lightingSettings.evening.luminanceMultiplier);
                env.m_sunColorEvening = ChangeColorLuminance(env.m_sunColorEvening, lightingSettings.evening.luminanceMultiplier);

                env.m_fogDensityEvening *= lightingSettings.evening.fogDensityMultiplier;
            }

            if (lightingSettings.lightIntensityDayMultiplier != 1.0f)
            {
                env.m_lightIntensityDay *= lightingSettings.lightIntensityDayMultiplier;
            }

            if (lightingSettings.lightIntensityNightMultiplier != 1.0f)
            {
                env.m_lightIntensityNight *= lightingSettings.lightIntensityNightMultiplier;
            }
        }

        [HarmonyPriority(Priority.First)]
        public static void Postfix(EnvSetup env, ref Dictionary<string, Color> __state)
        {
            if (!controlLightings.Value)
                return;

            env.m_ambColorNight = __state["m_ambColorNight"];
            env.m_fogColorNight = __state["m_fogColorNight"];
            env.m_fogColorSunNight = __state["m_fogColorSunNight"];
            env.m_sunColorNight = __state["m_sunColorNight"];

            env.m_fogColorMorning = __state["m_fogColorMorning"];
            env.m_fogColorDay = __state["m_fogColorDay"];
            env.m_fogColorEvening = __state["m_fogColorEvening"];
            env.m_fogColorSunMorning = __state["m_fogColorSunMorning"];
            env.m_fogColorSunDay = __state["m_fogColorSunDay"];
            env.m_fogColorSunEvening = __state["m_fogColorSunEvening"];
            env.m_sunColorMorning = __state["m_sunColorMorning"];
            env.m_sunColorDay = __state["m_sunColorDay"];
            env.m_sunColorEvening = __state["m_sunColorEvening"];

            env.m_fogDensityNight = __state["m_fogDensityNight"].r;
            env.m_fogDensityMorning = __state["m_fogDensityMorning"].r;
            env.m_fogDensityDay = __state["m_fogDensityDay"].r;
            env.m_fogDensityEvening = __state["m_fogDensityEvening"].r;

            env.m_lightIntensityDay = __state["m_lightIntensityDay"].r * 100f;

            env.m_lightIntensityNight = __state["m_lightIntensityNight"].r * 100f;
        }

    }

    [HarmonyPatch(typeof(FootStep), nameof(FootStep.FindBestStepEffect))]
    public static class FootStep_FindBestStepEffect_SnowFootsteps
    {
        private static void Prefix(ref FootStep.GroundMaterial material)
        {
            if (seasonState.GetCurrentSeason() == Season.Winter && (material == FootStep.GroundMaterial.Mud || material == FootStep.GroundMaterial.Grass | material == FootStep.GroundMaterial.GenericGround))
                material = FootStep.GroundMaterial.Snow;
            else if (WaterVariantController.IsFrozen() && material == FootStep.GroundMaterial.Water)
                material = FootStep.GroundMaterial.Snow;
        }
    }

    [HarmonyPatch(typeof(Hud), nameof(Hud.UpdateBlackScreen))]
    public static class Hud_UpdateBlackScreen_BlackScreenFadeOnSeasonChange
    {
        private static bool Prefix()
        {
            return !seasonState.GetSeasonIsChanging();

        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.IsFreezing))]
    public static class EnvMan_IsFreezing_SwimmingInWinterIsFreezing
    {
        private static void Postfix(EnvMan __instance, ref bool __result)
        {
            if (!freezingSwimmingInWinter.Value)
                return;

            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            __result = __result || player.IsSwimming() && seasonState.GetCurrentSeason() == Season.Winter && __instance.IsCold();
        }
    }

    [HarmonyPatch(typeof(Bed), nameof(Bed.Interact))]
    public static class Bed_Interact_PreventSleepingWithTorchFiresource
    {
        private static bool m_interacting = false;

        public static bool IsInteracting() => m_interacting;

        [HarmonyPriority(Priority.First)]
        private static void Prefix(Humanoid human)
        {
            m_interacting = human == Player.m_localPlayer;
        }

        private static void Postfix()
        {
            m_interacting = false;
        }
    }

    [HarmonyPatch(typeof(Bed), nameof(Bed.CheckFire))]
    public static class Bed_CheckFire_PreventSleepingWithTorchFiresource
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Humanoid human)
        {
            if (human == Player.m_localPlayer && seasonState.GetTorchAsFiresource() && 
                (human.GetLeftItem() != null && human.GetLeftItem().m_shared.m_itemType == ItemDrop.ItemData.ItemType.Torch
                 || human.GetRightItem() != null && human.GetRightItem().m_shared.m_itemType == ItemDrop.ItemData.ItemType.Torch))
                human.HideHandItems();
        }
    }


    
}
