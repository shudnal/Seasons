using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static Seasons.Seasons;
using UnityEngine;

namespace Seasons
{
    internal class SeasonStatePatches
    {
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
                __result = __instance.name == "Halloween" && season == Season.Fall
                        || __instance.name == "Midsummer" && season == Season.Summer
                        || __instance.name == "Yule" && season == Season.Winter;
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
        public static class Character_ApplyDamage_PreventDeathFromFreezing
        {
            private static bool Prefix(Character __instance, ref HitData hit)
            {
                if (!preventDeathFromFreezing.Value)
                    return true;

                if (!__instance.IsPlayer())
                    return true;

                if (__instance != Player.m_localPlayer)
                    return true;

                if (hit.m_hitType != HitData.HitType.Freezing)
                    return true;

                Heightmap.Biome biome = (__instance as Player).GetCurrentBiome();
                if (biome == Heightmap.Biome.Mountain || biome == Heightmap.Biome.DeepNorth)
                    return true;

                return __instance.GetHealth() >= 5;
            }
        }

        [HarmonyPatch(typeof(Pickable), nameof(Pickable.Awake))]
        public static class Pickable_Awake_PlantsGrowthMultiplier
        {
            public static bool ShouldBePicked(Pickable pickable)
            {
                return !pickable.GetPicked()
                    && IsVulnerableToWinter(pickable)
                    && seasonState.GetCurrentDay() >= cropsDiesAfterSetDayInWinter.Value
                    && !IsProtectedPosition(pickable.transform.position);
            }

            public static bool IsVulnerableToWinter(Pickable pickable)
            {
                return seasonState.GetPlantsGrowthMultiplier() == 0f &&
                        seasonState.GetCurrentSeason() == Season.Winter
                        && !PlantWillSurviveWinter(pickable.gameObject);
            }

            public static bool IsIgnored(Pickable pickable)
            {
                return pickable.m_nview == null ||
                      !pickable.m_nview.IsValid() ||
                      !pickable.m_nview.IsOwner() ||
                      !ControlPlantGrowth(pickable.gameObject) ||
                      IsIgnoredPosition(pickable.transform.position);
            }

            private static void Postfix(Pickable __instance)
            {
                if (IsIgnored(__instance))
                    return;

                if (ShouldBePicked(__instance) && !ProtectedWithHeat(__instance.transform.position))
                    __instance.StartCoroutine(PickableSetPicked(__instance));
            }
        }

        [HarmonyPatch(typeof(Pickable), nameof(Pickable.UpdateRespawn))]
        public static class Pickable_UpdateRespawn_PlantsGrowthMultiplier
        {
            private static bool Prefix(Pickable __instance, ref float ___m_respawnTimeMinutes, ref float __state)
            {
                if (Pickable_Awake_PlantsGrowthMultiplier.IsIgnored(__instance))
                    return true;

                if (Pickable_Awake_PlantsGrowthMultiplier.ShouldBePicked(__instance) && !ProtectedWithHeat(__instance.transform.position))
                {
                    __instance.SetPicked(true);
                    return false;
                }

                if (IsProtectedPosition(__instance.transform.position))
                    return true;

                if (seasonState.GetPlantsGrowthMultiplier() == 0f)
                    return false;

                __state = ___m_respawnTimeMinutes;

                ___m_respawnTimeMinutes = (float)seasonState.GetSecondsToRespawnPickable(__instance) / 60f;

                return true;
            }

            private static void Postfix(ref float ___m_respawnTimeMinutes, ref float __state)
            {
                if (__state == 0f)
                    return;

                ___m_respawnTimeMinutes = __state;
            }
        }

        [HarmonyPatch(typeof(Pickable), nameof(Pickable.GetHoverText))]
        public static class Pickable_GetHoverText_FireWarmthPerishProtection
        {
            private static string GetPickableStatus(Pickable __instance)
            {
                if (!Pickable_Awake_PlantsGrowthMultiplier.IsVulnerableToWinter(__instance))
                    return "$se_frostres_name";
                else if (ProtectedWithHeat(__instance.transform.position))
                    return "$se_fire_tooltip";
                else
                    return "$piece_plant_toocold";
            }

            private static void Postfix(Pickable __instance, ref string __result)
            {
                if (hoverPickable.Value != StationHover.Vanilla)
                {
                    if (__instance.m_picked && __instance.m_enabled > 0 && __instance.m_nview != null && __instance.m_nview.IsValid())
                    {
                        long pickedTime = __instance.m_nview.GetZDO().GetLong(ZDOVars.s_pickedTime, 0L);
                        if (pickedTime > 1)
                        {
                            if (string.IsNullOrWhiteSpace(__result))
                                __result = Localization.instance.Localize(__instance.GetHoverName());

                            TimeSpan timeSpan = ZNet.instance.GetTime() - new DateTime(pickedTime);
                            double respawnTimeSeconds = seasonState.GetSecondsToRespawnPickable(__instance);

                            if (hoverPickable.Value == StationHover.Percentage)
                                __result += $"\n{timeSpan.TotalSeconds / respawnTimeSeconds:P0}";
                            else if (hoverPickable.Value == StationHover.Bar)
                                __result += $"\n{FromPercent(timeSpan.TotalSeconds / respawnTimeSeconds)}";
                            else if (hoverPickable.Value == StationHover.MinutesSeconds)
                                __result += $"\n{FromSeconds(respawnTimeSeconds - timeSpan.TotalSeconds)}";
                        }
                    }
                }

                if (Pickable_Awake_PlantsGrowthMultiplier.IsIgnored(__instance) || seasonState.GetCurrentSeason() != Season.Winter)
                    return;

                if (string.IsNullOrWhiteSpace(__result))
                    __result = Localization.instance.Localize(__instance.GetHoverName());

                __result += Localization.instance.Localize($"\n<color=#ADD8E6>{GetPickableStatus(__instance)}</color>");
            }
        }

        [HarmonyPatch(typeof(Vine), nameof(Vine.UpdateGrow))]
        public static class Vine_UpdateGrow_VinesGrowthWinterStop
        {
            private static bool Prefix(Vine __instance, ref Tuple<float, float> __state)
            {
                if (IsProtectedPosition(__instance.transform.position) || __instance.m_initialGrowItterations > 0 || __instance.IsDoneGrowing)
                    return true;

                float multiplier = seasonState.GetPlantsGrowthMultiplier();
                if (multiplier == 0f)
                    return false;

                __state = Tuple.Create(__instance.m_growTime, __instance.m_growTimePerBranch);

                __instance.m_growTime *= multiplier;
                __instance.m_growTimePerBranch *= multiplier;

                return true;
            }

            private static void Postfix(Vine __instance, Tuple<float, float> __state)
            {
                if (__state == null)
                    return;

                __instance.m_growTime = __state.Item1;
                __instance.m_growTimePerBranch = __state.Item2;
            }
        }

        [HarmonyPatch(typeof(Plant), nameof(Plant.UpdateHealth))]
        public static class Pickable_UpdateHealth_PlantsPerishInWinter
        {
            private static void Postfix(Plant __instance, ref Plant.Status ___m_status)
            {
                if (IsProtectedPosition(__instance.transform.position))
                    return;

                if (___m_status == Plant.Status.Healthy && seasonState.GetPlantsGrowthMultiplier() == 0f && seasonState.GetCurrentSeason() == Season.Winter
                                                        && !PlantWillSurviveWinter(__instance.gameObject) && !ProtectedWithHeat(__instance.transform.position))
                    ___m_status = Plant.Status.TooCold;
            }
        }

        [HarmonyPatch(typeof(Plant), nameof(Plant.TimeSincePlanted))]
        public static class Plant_TimeSincePlanted_PlantsGrowthMultiplier
        {
            private static void Postfix(Plant __instance, ref double __result)
            {
                if (IsProtectedPosition(__instance.transform.position))
                    return;

                double timeSeconds = seasonState.GetTotalSeconds();
                double seasonStart = seasonState.GetStartOfCurrentSeason();
                Season season = seasonState.GetCurrentSeason();
                double rescaledResult = 0d;

                do
                {
                    rescaledResult += (timeSeconds - seasonStart >= __result ? __result : timeSeconds - seasonStart) * seasonState.GetPlantsGrowthMultiplier(season);

                    __result -= timeSeconds - seasonStart;
                    timeSeconds = seasonStart;
                    season = seasonState.GetPreviousSeason(season);
                    seasonStart -= seasonState.GetDaysInSeason(season) * seasonState.GetDayLengthInSeconds();

                } while (__result > 0);

                __result = rescaledResult;
            }
        }

        [HarmonyPatch(typeof(Plant), nameof(Plant.GetHoverText))]
        public static class Plant_GetHoverText_Duration
        {
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
                else if (hoverPlant.Value == StationHover.Bar)
                    __result += $"\n{FromPercent(__instance.TimeSincePlanted() / __instance.GetGrowTime())}";
                else if (hoverPlant.Value == StationHover.MinutesSeconds)
                    __result += $"\n{FromSeconds(seasonState.GetSecondsToGrowPlant(__instance))}";
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.Start))]
        public static class Minimap_Start_MinimapSeasonalBorderColor
        {
            private static void Postfix()
            {
                if (!SeasonState.IsActive)
                    return;

                seasonState.UpdateMinimapBorder();
            }
        }

        [HarmonyPatch(typeof(Beehive), nameof(Beehive.Interact))]
        public static class Beehive_Interact_BeesInteractionMessage
        {
            private static void Prefix(Beehive __instance, ref string __state)
            {
                if (IsProtectedPosition(__instance.transform.position))
                    return;

                __state = __instance.m_happyText;
                if (seasonState.GetBeehiveProductionMultiplier() == 0f)
                    __instance.m_happyText = __instance.m_sleepText;
            }

            private static void Postfix(Beehive __instance, ref string __state)
            {
                if (IsProtectedPosition(__instance.transform.position))
                    return;

                __instance.m_happyText = __state;
            }
        }

        [HarmonyPatch(typeof(Beehive), nameof(Beehive.GetTimeSinceLastUpdate))]
        public static class Beehive_GetTimeSinceLastUpdate_BeesProduction
        {
            private static void Postfix(Beehive __instance, ref float __result)
            {
                if (IsProtectedPosition(__instance.transform.position))
                    return;

                __result *= seasonState.GetBeehiveProductionMultiplier();
            }
        }

        [HarmonyPatch(typeof(Beehive), nameof(Beehive.GetHoverText))]
        public static class Beehive_GetHoverText_Duration
        {
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
                else if (hoverBeeHive.Value == StationHover.Bar)
                    __result += $"\n{FromPercent(product / __instance.m_secPerUnit)}";
                else if (hoverBeeHive.Value == StationHover.MinutesSeconds)
                    __result += $"\n{FromSeconds(seasonState.GetSecondsToMakeHoney(__instance, 1, product))}";

                if (hoverBeeHiveTotal.Value && honeyLevel < 3)
                    if (hoverBeeHive.Value == StationHover.Percentage)
                        __result += $"\n{(product + __instance.m_secPerUnit * honeyLevel) / (__instance.m_secPerUnit * __instance.m_maxHoney):P0}";
                    else if (hoverBeeHive.Value == StationHover.Bar)
                        __result += $"\n{FromPercent((product + __instance.m_secPerUnit * honeyLevel) / (__instance.m_secPerUnit * __instance.m_maxHoney))}";
                    else if (hoverBeeHive.Value == StationHover.MinutesSeconds)
                        __result += $"\n{FromSeconds(seasonState.GetSecondsToMakeHoney(__instance, __instance.m_maxHoney - honeyLevel, product))}";
            }
        }

        [HarmonyPatch(typeof(Beehive), nameof(Beehive.UpdateBees))]
        public static class Beehive_UpdateBees_BeesSleeping
        {
            private static void Postfix(Beehive __instance, ref GameObject ___m_beeEffect)
            {
                if (IsProtectedPosition(__instance.transform.position))
                    return;

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
                if (IsProtectedPosition(__instance.transform.position))
                    return;

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

                if (IsProtectedPosition(__instance.transform.position))
                    return;

                __result *= (double)Math.Max(0f, seasonState.GetFireplaceDrainMultiplier());
            }
        }

        [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.UpdateFuel))]
        static class CookingStation_UpdateFuel_FireplaceDrainMultiplier
        {
            private static void Prefix(CookingStation __instance, ref float dt, ref float __state)
            {
                if (IsProtectedPosition(__instance.transform.position))
                    return;

                __state = dt;
                dt *= Math.Max(0f, seasonState.GetFireplaceDrainMultiplier());
            }

            private static void Postfix(CookingStation __instance, ref float dt, float __state)
            {
                if (IsProtectedPosition(__instance.transform.position))
                    return;

                dt = __state;
            }
        }

        [HarmonyPatch(typeof(SapCollector), nameof(SapCollector.GetTimeSinceLastUpdate))]
        static class SapCollector_GetTimeSinceLastUpdate_SapCollectingSpeedMultiplier
        {
            private static void Postfix(SapCollector __instance, ref float __result)
            {
                if (IsProtectedPosition(__instance.transform.position))
                    return;

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

                if (IsProtectedPosition(__instance.transform.position))
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
                if (!m_dropWhenDestroyed.m_drops.Any(dd => ControlWoodDrop(dd.m_item)))
                    return;

                m_dropWhenDestroyed.m_dropMax = Mathf.CeilToInt(m_dropWhenDestroyed.m_dropMax * seasonState.GetWoodFromTreesMultiplier());
                if (m_dropWhenDestroyed.m_dropMin < m_dropWhenDestroyed.m_dropMax)
                    m_dropWhenDestroyed.m_dropMin = m_dropWhenDestroyed.m_dropMax;
            }

            private static void Prefix(TreeLog __instance, ZNetView ___m_nview, ref DropTable ___m_dropWhenDestroyed)
            {
                if (seasonState.GetWoodFromTreesMultiplier() == 1.0f)
                    return;

                if (___m_nview == null || !___m_nview.IsValid() || !___m_nview.IsOwner())
                    return;

                if (IsProtectedPosition(__instance.transform.position))
                    return;

                ApplyWoodMultiplier(___m_dropWhenDestroyed);
            }
        }

        [HarmonyPatch(typeof(Destructible), nameof(Destructible.Destroy))]
        public static class Destructible_Destroy_TreeRegrowth
        {
            private static void Prefix(Destructible __instance, ZNetView ___m_nview)
            {
                if (UnityEngine.Random.Range(0.0f, 1.0f) > seasonState.GetTreesReqrowthChance())
                    return;

                if (___m_nview == null || !___m_nview.IsValid() || !___m_nview.IsOwner())
                    return;

                if (__instance.GetDestructibleType() != DestructibleType.Tree)
                    return;

                if (TreeToRegrowth(__instance.gameObject) is not GameObject plant)
                    return;

                if (IsProtectedPosition(__instance.transform.position))
                    return;

                if ((bool)EffectArea.IsPointInsideArea(__instance.transform.position, EffectArea.Type.PlayerBase))
                    return;

                float scale = ___m_nview.GetZDO().GetFloat(ZDOVars.s_scaleScalarHash);

                instance.StartCoroutine(ReplantTree(plant, __instance.transform.position, __instance.transform.rotation, scale));
            }
        }

        [HarmonyPatch(typeof(Plant), nameof(Plant.HaveGrowSpace))]
        public static class Plant_HaveGrowSpace_TreeRegrowth
        {
            private static bool Prefix(ZNetView ___m_nview, ref bool __result)
            {
                if (___m_nview == null || !___m_nview.IsValid() || !___m_nview.IsOwner())
                    return true;

                __result = __result || ___m_nview.GetZDO().GetBool(_treeRegrowthHaveGrowSpace, false);
                return !__result;
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

                if (IsProtectedPosition(__instance.transform.position))
                    return;

                TreeLog_Destroy_TreeWoodDrop.ApplyWoodMultiplier(___m_dropWhenDestroyed);
            }
        }

        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
        public static class CharacterDrop_GenerateDropList_MeatDrop
        {
            public static void ApplyMeatMultiplier(List<CharacterDrop.Drop> m_drops)
            {
                foreach (CharacterDrop.Drop drop in m_drops)
                {
                    if (drop.m_prefab == null || !ControlMeatDrop(drop.m_prefab))
                        continue;

                    drop.m_amountMax = Mathf.CeilToInt(drop.m_amountMax * seasonState.GetMeatFromAnimalsMultiplier());
                    if (drop.m_amountMin < drop.m_amountMax)
                        drop.m_amountMin = drop.m_amountMax;
                }
            }

            private static void Prefix(CharacterDrop __instance, ref List<CharacterDrop.Drop> ___m_drops)
            {
                if (seasonState.GetMeatFromAnimalsMultiplier() == 1.0f)
                    return;

                if (IsProtectedPosition(__instance.transform.position))
                    return;

                ApplyMeatMultiplier(___m_drops);
            }
        }

        [HarmonyPatch(typeof(SE_Rested), nameof(SE_Rested.UpdateTTL))]
        public static class SE_Rested_UpdateTTL_RestedBuffDuration
        {
            private static void Prefix(ref float ___m_baseTTL, ref float ___m_TTLPerComfortLevel, ref Tuple<float, float> __state)
            {
                if (seasonState.GetRestedBuffDurationMultiplier() == 1.0f)
                    return;

                __state = new Tuple<float, float>(___m_baseTTL, ___m_TTLPerComfortLevel);
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
            private class ProcreateState
            {
                public float m_totalCheckRange;
                public float m_partnerCheckRange;
                public float m_pregnancyChance;
                public float m_pregnancyDuration;
            }

            private static readonly ProcreateState _procreateState = new ProcreateState();

            private static void Prefix(ref Procreation __instance)
            {
                if (seasonState.GetLivestockProcreationMultiplier() == 1.0f)
                    return;

                _procreateState.m_totalCheckRange = __instance.m_totalCheckRange;
                _procreateState.m_partnerCheckRange = __instance.m_partnerCheckRange;
                _procreateState.m_pregnancyChance = __instance.m_pregnancyChance;
                _procreateState.m_pregnancyDuration = __instance.m_pregnancyDuration;

                __instance.m_pregnancyChance *= seasonState.GetLivestockProcreationMultiplier();
                __instance.m_partnerCheckRange *= seasonState.GetLivestockProcreationMultiplier();
                if (seasonState.GetLivestockProcreationMultiplier() != 0f)
                {
                    __instance.m_totalCheckRange /= seasonState.GetLivestockProcreationMultiplier();
                    __instance.m_pregnancyDuration /= seasonState.GetLivestockProcreationMultiplier();
                }
            }

            private static void Postfix(ref Procreation __instance)
            {
                if (seasonState.GetLivestockProcreationMultiplier() == 1.0f)
                    return;

                __instance.m_pregnancyChance = _procreateState.m_pregnancyChance;
                __instance.m_totalCheckRange = _procreateState.m_totalCheckRange;
                __instance.m_partnerCheckRange = _procreateState.m_partnerCheckRange;
                __instance.m_pregnancyDuration = _procreateState.m_pregnancyDuration;
            }
        }

        [HarmonyPatch]
        public static class Player_Food_OverheatIn2WarmClothesExcludeEyescream
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Player), nameof(Player.UpdateFood));
                yield return AccessTools.Method(typeof(Player), nameof(Player.ClearFood));
                yield return AccessTools.Method(typeof(Player), nameof(Player.EatFood));
                yield return AccessTools.Method(typeof(Player), nameof(Player.RemoveOneFood));
            }

            private static void Prefix(Player __instance, ref int __state)
            {
                if (__instance == Player.m_localPlayer)
                    __state = __instance.GetFoods().Count;
            }

            private static void Postfix(Player __instance, int __state)
            {
                if (__instance == Player.m_localPlayer && __state != __instance.GetFoods().Count)
                    seasonState.CheckOverheatStatus(__instance);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.UpdateEnvStatusEffects))]
        public static class Player_UpdateEnvStatusEffects_ColdStatus
        {
            public static bool isCalled = false;

            private static void Prefix(Player __instance)
            {
                isCalled = gettingWetInWinterCausesCold.Value && seasonState.GetCurrentSeason() == Season.Winter && EnvMan.IsCold() && __instance.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet);
            }

            private static void Postfix()
            {
                isCalled = false;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.ApplyArmorDamageMods))]
        public static class Player_ApplyArmorDamageMods_ColdStatusWhenWet
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(ref HitData.DamageModifiers mods)
            {
                if (!Player_UpdateEnvStatusEffects_ColdStatus.isCalled)
                    return;

                // Method is called in Winter when player is wet and environment is cold
                HitData.DamageModifier modifier = mods.GetModifier(HitData.DamageType.Frost);
                if (modifier == HitData.DamageModifier.Resistant || modifier == HitData.DamageModifier.VeryResistant || modifier == HitData.DamageModifier.SlightlyResistant)
                    mods.m_frost = HitData.DamageModifier.Normal;
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        public static class Humanoid_EquipItem_OverheatIn2WarmClothes
        {
            private static void Postfix(Humanoid __instance)
            {
                if (__instance.IsPlayer())
                    seasonState.CheckOverheatStatus(__instance as Player);
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
        public static class Humanoid_UnequipItem_OverheatIn2WarmClothes
        {
            private static void Postfix(Humanoid __instance)
            {
                if (__instance.IsPlayer())
                    seasonState.CheckOverheatStatus(__instance as Player);
            }
        }

        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.GetPossibleRandomEvents))]
        public static class RandEventSystem_GetPossibleRandomEvents_RandomEventWeights
        {
            private static void Prefix(RandEventSystem __instance, ref List<RandomEvent> __state)
            {
                if (!controlRandomEvents.Value)
                    return;

                List<SeasonRandomEvents.SeasonRandomEvent> randEvents = SeasonState.seasonRandomEvents.GetSeasonEvents(seasonState.GetCurrentSeason());

                __state = new List<RandomEvent>();
                for (int i = 0; i < __instance.m_events.Count; i++)
                {
                    RandomEvent randEvent = __instance.m_events[i];
                    __state.Add(JsonUtility.FromJson<RandomEvent>(JsonUtility.ToJson(randEvent)));

                    SeasonRandomEvents.SeasonRandomEvent seasonRandEvent = randEvents.Find(re => re.m_name == randEvent.m_name);
                    if (seasonRandEvent != null)
                    {
                        if (seasonRandEvent.m_biomes != null)
                        {
                            randEvent.m_biome = seasonRandEvent.GetBiome();
                            randEvent.m_spawn.ForEach(spawn => spawn.m_biome |= randEvent.m_biome);
                        }

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

        [HarmonyPatch(typeof(FootStep), nameof(FootStep.FindBestStepEffect))]
        public static class FootStep_FindBestStepEffect_SnowFootsteps
        {
            private static void Prefix(FootStep __instance, ref FootStep.GroundMaterial material)
            {
                if (IsShieldProtectionActive() && __instance.m_character?.GetLastGroundCollider() != null && ZoneSystemVariantController.IsProtectedHeightmap(__instance.m_character.GetLastGroundCollider().GetComponent<Heightmap>()))
                    return;

                if (seasonState.GetCurrentSeason() == Season.Winter && (material == FootStep.GroundMaterial.Mud || material == FootStep.GroundMaterial.Grass || material == FootStep.GroundMaterial.GenericGround))
                    material = FootStep.GroundMaterial.Snow;
                else if (ZoneSystemVariantController.IsWaterSurfaceFrozen() && material == FootStep.GroundMaterial.Water)
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

        [HarmonyPatch(typeof(Trader), nameof(Trader.GetAvailableItems))]
        public static class Trader_GetAvailableItems_SeasonalTraderItems
        {
            [HarmonyPriority(Priority.First)]
            static void Postfix(Trader __instance, ref List<Trader.TradeItem> __result)
            {
                if (controlTraders.Value)
                    SeasonState.seasonTraderItems.AddSeasonalTraderItems(__instance, __result);
            }
        }

        [HarmonyPatch(typeof(Game), nameof(Game.UpdateSleeping))]
        public static class Game_UpdateSleeping_ForceUpdateState
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(bool ___m_sleeping, ref bool __state)
            {
                __state = ___m_sleeping;
            }

            [HarmonyPriority(Priority.Last)]
            private static void Postfix(bool ___m_sleeping, bool __state)
            {
                if (!___m_sleeping && __state)
                    EnvManPatches.sleepingUpdated = true;
            }
        }

        [HarmonyPatch(typeof(Terminal), nameof(Terminal.TryRunCommand))]
        public static class Terminal_TryRunCommand_ForceUpdateState
        {
            private static void Postfix(string text)
            {
                if (text.IndexOf("skiptime") > -1 && SeasonState.IsActive && ZNet.instance && ZNet.instance.IsServer())
                    EnvManPatches.skiptimeUsed = true;
            }
        }

        [HarmonyPatch(typeof(Settings), nameof(Settings.SaveTabSettings))]
        public static class Settings_SaveTabSettings_ForceUpdateState
        {
            private static void Postfix()
            {
                seasonState?.UpdateWinterBloomEffect();
            }
        }
    }
}
