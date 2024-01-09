using System;
using static Seasons.Seasons;
using HarmonyLib;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using BepInEx;

namespace Seasons
{
    public class SeasonState
    {
        private Season m_season = Season.Spring;
        private int m_day = 0;

        public static readonly Dictionary<Season, SeasonSettings> seasonsSettings = new Dictionary<Season, SeasonSettings>();
        public static SeasonBiomeEnvironments seasonBiomeEnvironments = new SeasonBiomeEnvironments();
        public static List<SeasonEnvironment> seasonEnvironments = SeasonEnvironment.GetDefaultCustomEnvironments();
        public static SeasonRandomEvents seasonRandomEvents = new SeasonRandomEvents();

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

        public void UpdateEventEnvironments()
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
                CheckOverheatStatus(Player.m_localPlayer);
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

            if (seasonState.settings.m_torchAsFiresource && IsActive && (EnvMan.instance.IsWet() || EnvMan.instance.IsCold() || EnvMan.instance.IsFreezing()))
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
        private static void Postfix(Fireplace __instance, ref double __result)
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
        private static void Prefix(Smelter __instance, ref float dt, ref float __state)
        {
            __state = dt;
            dt *= Math.Max(0f, seasonState.GetFireplaceDrainMultiplier());
        }

        private static void Postfix(Smelter __instance, ref float dt, float __state)
        {
            dt = __state;
        }
    }

    [HarmonyPatch(typeof(SapCollector), nameof(SapCollector.GetTimeSinceLastUpdate))]
    static class SapCollector_GetTimeSinceLastUpdate_SapCollectingSpeedMultiplier
    {
        private static void Postfix(SapCollector __instance, ref float __result)
        {
            __result *= Math.Max(0f, seasonState.GetSapCollectingSpeedMultiplier());
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateWear))]
    public static class WearNTear_UpdateWear_RainProtection
    {
        private static void Prefix(WearNTear __instance, ZNetView ___m_nview, ref bool ___m_noRoofWear, ref bool __state)
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

    [HarmonyPatch(typeof(TreeBase), nameof(TreeBase.RPC_Damage))]
    public static class TreeBase_RPC_Damage_TreeWoodDrop
    {
        private static void Prefix(TreeBase __instance, ZNetView ___m_nview, ref float __state)
        {
            if (seasonState.GetWoodFromTreesMultiplier() == 1.0f)
                return;

            if (!___m_nview.IsOwner())
                return;

            if (___m_nview == null || !___m_nview.IsValid())
                return;

            __state = Game.m_resourceRate;
            Game.m_resourceRate *= seasonState.GetWoodFromTreesMultiplier();
        }

        private static void Postfix(float __state)
        {
            if (seasonState.GetWoodFromTreesMultiplier() == 1.0f)
                return;

            if (__state != 0f) return;

            Game.m_resourceRate = __state;
        }
    }

    [HarmonyPatch(typeof(TreeLog), nameof(TreeLog.Destroy))]
    public static class TreeLog_Destroy_TreeWoodDrop
    {
        private static void Prefix(TreeLog __instance, ZNetView ___m_nview, ref float __state)
        {
            if (seasonState.GetWoodFromTreesMultiplier() == 1.0f)
                return;

            if (!___m_nview.IsOwner())
                return;

            if (___m_nview == null || !___m_nview.IsValid())
                return;

            __state = Game.m_resourceRate;
            Game.m_resourceRate *= seasonState.GetWoodFromTreesMultiplier();
        }

        private static void Postfix(float __state)
        {
            if (seasonState.GetWoodFromTreesMultiplier() == 1.0f)
                return;

            if (__state != 0f) return;

            Game.m_resourceRate = __state;
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
            List<SeasonRandomEvent> randEvents = new List<SeasonRandomEvent>();
            switch (seasonState.GetCurrentSeason())
            {
                case Season.Spring:
                    {
                        randEvents = SeasonState.seasonRandomEvents.Spring;
                        break;
                    }
                case Season.Summer:
                    {
                        randEvents = SeasonState.seasonRandomEvents.Summer;
                        break;
                    }
                case Season.Fall:
                    {
                        randEvents = SeasonState.seasonRandomEvents.Fall;
                        break;
                    }
                case Season.Winter:
                    {
                        randEvents = SeasonState.seasonRandomEvents.Winter;
                        break;
                    }
            }

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
            __instance.m_events.Clear();
            __instance.m_events.AddRange(__state.ToList());
        }
    }
}
