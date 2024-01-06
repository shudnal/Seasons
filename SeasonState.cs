using System;
using static Seasons.Seasons;
using HarmonyLib;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

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

        public Season GetNextSeason()
        {
            return NextSeason(m_season);
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

            foreach (BiomeEnvSetup biomeEnvironment in biomesDefault)
            {
                foreach (SeasonBiomeEnvironment.EnvironmentReplace replace in biomeEnv.replace)
                    biomeEnvironment.m_environments.DoIf(env => env.m_environment == replace.m_environment, env => env.m_environment = replace.replace_to);

                foreach (SeasonBiomeEnvironment.EnvironmentAdd add in biomeEnv.add)
                    if (add.m_name == biomeEnvironment.m_name && !biomeEnvironment.m_environments.Any(env => env.m_environment == add.m_environment.m_environment))
                        biomeEnvironment.m_environments.Add(add.m_environment);

                foreach (SeasonBiomeEnvironment.EnvironmentRemove remove in biomeEnv.remove)
                    biomeEnvironment.m_environments.DoIf(env => biomeEnvironment.m_name == remove.m_name && env.m_environment == remove.m_environment, env => biomeEnvironment.m_environments.Remove(env));

                try
                {
                    EnvMan.instance.AppendBiomeSetup(biomeEnvironment);
                }
                catch (Exception e)
                {
                    LogWarning($"Error appending biome setup {biomeEnvironment.m_name}:\n{e}");
                }
            }
        }

        public void UpdateCurrentEnvironment()
        {
            EnvMan.instance.m_environmentPeriod--;
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

        private int GetDaysInSeason(Season season)
        {
            return (seasonsSettings[season] ?? new SeasonSettings(season)).m_daysInSeason;
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
            UpdateBiomeEnvironments();
            UpdateCurrentEnvironment();
            UpdateTorchesFireWarmth();

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

            float dayStart = (seasonState.GetNightLength() / 2f) / 100f;

            __result = (float)(day * __instance.m_dayLengthSec) + (float)__instance.m_dayLengthSec * dayStart;
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

            float dayStart = (seasonState.GetNightLength() / 2f) / 100f;

            double timeSeconds = ZNet.instance.GetTimeSeconds();
            double time = timeSeconds - (double)((float)__instance.m_dayLengthSec * dayStart);
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
    
}
