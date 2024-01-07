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
using static MeleeWeaponTrail;

namespace Seasons
{
    public class SeasonState
    {
        private Season m_season = Season.Spring;
        private int m_day = 0;

        public static readonly Dictionary<Season, SeasonSettings> seasonsSettings = new Dictionary<Season, SeasonSettings>();
        public static SeasonBiomeEnvironments seasonBiomeEnvironments = new SeasonBiomeEnvironments();
        public static List<SeasonEnvironment> seasonEnvironments = SeasonEnvironment.GetDefaultCustomEnvironments();

        private SeasonSettings settings { 
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
        }

        public void UpdateSeasonEnvironments()
        {
            if (!IsActive)
                return;

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

            SeasonBiomeEnvironment biomeEnv = new SeasonBiomeEnvironment();
            switch (GetCurrentSeason())
            {
                case Season.Spring:
                    {
                        biomeEnv = seasonBiomeEnvironments.Spring;
                        break;
                    }
                case Season.Summer:
                    {
                        biomeEnv = seasonBiomeEnvironments.Summer;
                        break;
                    }
                case Season.Fall:
                    {
                        biomeEnv = seasonBiomeEnvironments.Fall;
                        break;
                    }
                case Season.Winter:
                    {
                        biomeEnv = seasonBiomeEnvironments.Winter;
                        break;
                    }
            }

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
            EnvMan.instance.m_environmentPeriod--;
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

        public float GetBeehiveProductionMultiplier()
        {
            return GetBeehiveProductionMultiplier(seasonState.GetCurrentSeason());
        }

        public float GetBeehiveProductionMultiplier(Season season)
        {
            return GetSeasonSettings(season).m_beehiveProductionMultiplier;
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

        private void CheckIfSeasonChanged(int season)
        {
            if (season == (int)m_season)
                return;

            PrefabVariantController.UpdatePrefabColors();
            TerrainVariantController.UpdateTerrainColors();
            ClutterVariantController.instance.UpdateColors();
            UpdateBiomeEnvironments();
            UpdateCurrentEnvironment();
            UpdateTorchesFireWarmth();
            UpdateMinimapBorder();

            if (Player.m_localPlayer != null)
            {
                Player.m_localPlayer.UpdateCurrentSeason();
            }
        }

        private void CheckIfDayChanged(int dayInSeason)
        {
            if (m_day == dayInSeason)
                return;

            m_day = dayInSeason;
            ClutterVariantController.instance.UpdateColors();
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

            if (seasonState.settings.m_torchAsFiresource && (EnvMan.instance.IsWet() || EnvMan.instance.IsCold() || EnvMan.instance.IsFreezing()))
                torch.m_shared.m_durabilityDrain = seasonState.settings.m_torchDurabilityDrain;
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
                seasonState.PatchTorchItemData(item);
        }

        public void UpdateTorchFireWarmth(GameObject prefab, string childName = "FireWarmth")
        {
            Transform fireWarmth = Utils.FindChild(prefab.transform, childName);
            if (fireWarmth == null)
                return;

            EffectArea component = fireWarmth.gameObject.GetComponent<EffectArea>();
            if (component == null)
                return;

            component.m_type = seasonState.settings.m_torchAsFiresource ? EffectArea.Type.Heat | EffectArea.Type.Fire : EffectArea.Type.Fire;
            
            ItemDrop item = prefab.GetComponent<ItemDrop>();
            PatchTorchItemData(item.m_itemData);
        }

        public void UpdateMinimapBorder()
        {
            if (!seasonalMinimapBorderColor.Value || Minimap.instance == null)
                return;

            if (!Minimap.instance.m_smallRoot.TryGetComponent(out UnityEngine.UI.Image image) || image.sprite == null || image.sprite.name != "InputFieldBackground")
                return;

            switch (GetCurrentSeason())
            {
                case Season.Spring:
                    image.color = new Color(0.44f, 0.56f, 0.03f, image.color.a);
                    break;
                case Season.Summer:
                    image.color = new Color(0.69f, 0.73f, 0.05f, image.color.a);
                    break;
                case Season.Fall:
                    image.color = new Color(0.79f, 0.32f, 0f, image.color.a);
                    break;
                case Season.Winter:
                    image.color = new Color(0.89f, 0.94f, 0.96f, image.color.a);
                    break;
            }
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
        public static bool Prefix(EnvMan __instance, float fraction, ref float __result)
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

        private static void Postfix(Plant __instance, ZNetView ___m_nview, ref string __result)
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

}
