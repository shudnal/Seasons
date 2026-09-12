using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    public class SeasonStatePatches
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

                seasonState.UpdateTorchesFireWarmth();
            }
        }

        [HarmonyPatch]
        public static class VisEquipment_HandEquipped_TorchWarmth
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(VisEquipment), nameof(VisEquipment.SetRightHandEquipped), new[] { typeof(int), typeof(int) });
                yield return AccessTools.Method(typeof(VisEquipment), nameof(VisEquipment.SetLeftHandEquipped), new[] { typeof(int), typeof(int), typeof(int) });
            }

            private static void Postfix(VisEquipment __instance, bool __result)
            {
                if (__result && Player.m_localPlayer != null && Player.m_localPlayer.m_visEquipment == __instance)
                    seasonState.UpdateTorchesFireWarmth();
            }
        }

        [HarmonyPatch]
        public class Inventory_Load_TorchPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Inventory), nameof(Inventory.Load), new[] { typeof(ZPackage) });
                yield return AccessTools.Method(typeof(Inventory), nameof(Inventory.Load), new[] { typeof(ZPackage), typeof(bool) });
            }

            public static void Postfix(Inventory __instance)
            {
                if (!__instance.m_temoraryInventory)
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
                switch (__instance.name)
                {
                    case "Halloween": __result = season == Season.Fall; break;
                    case "Midsummer": __result = season == Season.Summer; break;
                    case "Yule": __result = season == Season.Winter; break;
                }
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

                float health = __instance.GetHealth();
                if (health < 5f)
                    return false;

                float damage = hit.GetTotalDamage() * Game.m_localDamgeTakenRate;
                if (damage >= health)
                    hit.ApplyModifier((health - 1f) / damage);

                return true;
            }
        }

        [HarmonyPatch(typeof(Pickable), nameof(Pickable.Awake))]
        public static class Pickable_Awake_PlantsGrowthMultiplier
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Pickable __instance)
            {
                if (__instance.m_nview?.IsValid() == true && !__instance.m_nview.HasOwner())
                    __instance.m_nview.ClaimOwnership();

                if (__instance.IsIgnored())
                    return;

                if (!__instance.CheckForPerishInWinter() && !__instance.IsInvoking("UpdateRespawn"))
                    __instance.InvokeRepeating("UpdateRespawn", UnityEngine.Random.Range(1f, 5f), repeatRate: 60f);
            }
        }

        [HarmonyPatch(typeof(Pickable), nameof(Pickable.UpdateRespawn))]
        public static class Pickable_UpdateRespawn_PlantsGrowthMultiplier
        {
            private static bool Prefix(Pickable __instance, ref float ___m_respawnTimeMinutes, ref float __state)
            {
                __state = 0f;
                if (__instance.IsIgnored())
                    return true;

                if (__instance.CheckForPerishInWinter())
                    return false;

                if (seasonState.GetPlantsGrowthMultiplier() == 0f)
                    return false;

                if (___m_respawnTimeMinutes == 0)
                    return false;

                __state = ___m_respawnTimeMinutes;

                ___m_respawnTimeMinutes = (float)seasonState.GetSecondsToRespawnPickable(__instance) / 60f;

                return true;
            }

            private static void Finalizer(ref float ___m_respawnTimeMinutes, float __state)
            {
                if (__state == 0f)
                    return;

                ___m_respawnTimeMinutes = __state;
            }
        }

        [HarmonyPatch(typeof(Pickable), nameof(Pickable.SetPicked))]
        public static class Pickable_SetPicked_FreezingTime
        {
            private static void Prefix(Pickable __instance, bool picked)
            {
                if (!__instance.IsIgnored() && picked)
                    __instance.SetFreezing(false);
            }
        }

        [HarmonyPatch(typeof(Pickable), nameof(Pickable.GetHoverText))]
        public static class Pickable_GetHoverText_FireWarmthPerishProtection
        {
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
                                __result = __instance.GetHoverName().Localize();

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

                if (__instance.IsIgnored() || seasonState.GetCurrentSeason() != Season.Winter || !__instance.CanBePicked())
                    return;

                if (string.IsNullOrWhiteSpace(__result))
                    __result = __instance.GetHoverName().Localize();

                __result += $"\n<color=#ADD8E6>{__instance.GetColdStatus().Localize()}</color>";
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
                if (multiplier <= 0f)
                    return false;

                __state = Tuple.Create(__instance.m_growTime, __instance.m_growTimePerBranch);
                __instance.m_growTime /= multiplier;
                __instance.m_growTimePerBranch /= multiplier;

                return true;
            }

            private static void Finalizer(Vine __instance, Tuple<float, float> __state)
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
            private static void Prefix(Plant __instance, ref double timeSincePlanted, ref bool __state)
            {
                if (__state = IsProtectedPosition(__instance.transform.position))
                    return;

                if (timeSincePlanted == 0d && seasonState.GetPlantsGrowthMultiplier() == 0f && seasonState.GetCurrentSeason() == Season.Winter)
                    timeSincePlanted = 11d;
            }

            private static void Postfix(Plant __instance, ref Plant.Status ___m_status, bool __state)
            {
                if (__state)
                    return;

                if (___m_status == Plant.Status.Healthy && seasonState.GetPlantsGrowthMultiplier() == 0f && seasonState.GetCurrentSeason() == Season.Winter
                                                        && !__instance.ShouldSurviveWinter() && !__instance.ProtectedWithHeat())
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
            private static void Prefix(Beehive __instance, ref Tuple<string> __state)
            {
                if (IsProtectedPosition(__instance.transform.position))
                    return;

                __state = Tuple.Create(__instance.m_happyText);
                if (seasonState.GetBeehiveProductionMultiplier() == 0f)
                    __instance.m_happyText = __instance.m_sleepText;
            }

            private static void Finalizer(Beehive __instance, Tuple<string> __state)
            {
                if (__state == null)
                    return;

                __instance.m_happyText = __state.Item1;
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

                if (seasonState.GetBeehiveProductionMultiplier() == 0f && ___m_beeEffect != null)
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

                if (!(dt * Game.m_foodRate + __instance.m_foodUpdateTimer >= 1f || forceUpdate))
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
            private static void Prefix(CookingStation __instance, ref float dt)
            {
                if (IsProtectedPosition(__instance.transform.position))
                    return;

                dt *= Math.Max(0f, seasonState.GetFireplaceDrainMultiplier());
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
            private static void Prefix(WearNTear __instance, ZNetView ___m_nview, ref bool ___m_noRoofWear, ref bool? __state)
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

            private static void Finalizer(ref bool ___m_noRoofWear, bool? __state)
            {
                if (__state.HasValue)
                    ___m_noRoofWear = __state.Value;
            }
        }

        [HarmonyPatch(typeof(TreeLog), nameof(TreeLog.Destroy))]
        public static class TreeLog_Destroy_TreeWoodDrop
        {
            public static void ApplyWoodMultiplier(ref DropTable table, ref DropTable original)
            {
                if (table == null || !table.m_drops.Any(dd => dd.m_item != null && ControlWoodDrop(dd.m_item)))
                    return;

                original = table;
                table = table.Clone();
                float multiplier = Mathf.Max(0f, seasonState.GetWoodFromTreesMultiplier());
                table.m_dropMin = Mathf.CeilToInt(table.m_dropMin * multiplier);
                table.m_dropMax = Mathf.Max(table.m_dropMin, Mathf.CeilToInt(table.m_dropMax * multiplier));
            }

            private static void Prefix(TreeLog __instance, ZNetView ___m_nview, ref DropTable ___m_dropWhenDestroyed, ref DropTable __state)
            {
                if (seasonState.GetWoodFromTreesMultiplier() == 1.0f)
                    return;

                if (___m_nview == null || !___m_nview.IsValid() || !___m_nview.IsOwner())
                    return;

                if (IsProtectedPosition(__instance.transform.position))
                    return;

                ApplyWoodMultiplier(ref ___m_dropWhenDestroyed, ref __state);
            }

            private static void Finalizer(ref DropTable ___m_dropWhenDestroyed, DropTable __state)
            {
                if (__state != null)
                    ___m_dropWhenDestroyed = __state;
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

                __result = __result || ___m_nview.GetZDO().GetBool(SeasonsVars.s_treeRegrowthHaveGrowSpace, false);
                return !__result;
            }
        }

        [HarmonyPatch(typeof(DropOnDestroyed), nameof(DropOnDestroyed.OnDestroyed))]
        public static class DropOnDestroyed_OnDestroyed_TreeWoodDrop
        {
            private static void Prefix(DropOnDestroyed __instance, ref DropTable ___m_dropWhenDestroyed, ref DropTable __state)
            {
                if (seasonState.GetWoodFromTreesMultiplier() == 1.0f)
                    return;

                if (!__instance.TryGetComponent(out Destructible destructible) || destructible.GetDestructibleType() != DestructibleType.Tree)
                    return;

                if (IsProtectedPosition(__instance.transform.position))
                    return;

                TreeLog_Destroy_TreeWoodDrop.ApplyWoodMultiplier(ref ___m_dropWhenDestroyed, ref __state);
            }

            private static void Finalizer(ref DropTable ___m_dropWhenDestroyed, DropTable __state)
            {
                if (__state != null)
                    ___m_dropWhenDestroyed = __state;
            }
        }

        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
        public static class CharacterDrop_GenerateDropList_MeatDrop
        {
            private static void Prefix(CharacterDrop __instance, ref List<CharacterDrop.Drop> ___m_drops, ref List<CharacterDrop.Drop> __state)
            {
                float multiplier = Mathf.Max(0f, seasonState.GetMeatFromAnimalsMultiplier());
                if (multiplier == 1f)
                    return;

                if (IsProtectedPosition(__instance.transform.position))
                    return;

                __state = ___m_drops;
                ___m_drops = __state.Select(drop => new CharacterDrop.Drop
                {
                    m_prefab = drop.m_prefab,
                    m_amountMin = drop.m_amountMin,
                    m_amountMax = drop.m_amountMax,
                    m_chance = drop.m_chance,
                    m_onePerPlayer = drop.m_onePerPlayer,
                    m_levelMultiplier = drop.m_levelMultiplier,
                    m_dontScale = drop.m_dontScale
                }).ToList();
                foreach (CharacterDrop.Drop drop in ___m_drops)
                {
                    if (drop.m_prefab == null || !ControlMeatDrop(drop.m_prefab))
                        continue;

                    drop.m_amountMin = Mathf.CeilToInt(drop.m_amountMin * multiplier);
                    drop.m_amountMax = Mathf.Max(drop.m_amountMin, Mathf.CeilToInt(drop.m_amountMax * multiplier));
                }
            }

            private static void Finalizer(ref List<CharacterDrop.Drop> ___m_drops, List<CharacterDrop.Drop> __state)
            {
                if (__state != null)
                    ___m_drops = __state;
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

            private static void Finalizer(ref float ___m_baseTTL, ref float ___m_TTLPerComfortLevel, Tuple<float, float> __state)
            {
                if (__state == null)
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

            private static bool Prefix(Procreation __instance, ref ProcreateState __state)
            {
                float multiplier = Mathf.Max(0f, seasonState.GetLivestockProcreationMultiplier());
                if (multiplier == 1f)
                    return true;
                if (multiplier == 0f)
                    return false;

                __state = new ProcreateState
                {
                    m_totalCheckRange = __instance.m_totalCheckRange,
                    m_partnerCheckRange = __instance.m_partnerCheckRange,
                    m_pregnancyChance = __instance.m_pregnancyChance,
                    m_pregnancyDuration = __instance.m_pregnancyDuration
                };

                // Native Procreate skips when the random value is below this threshold.
                __instance.m_pregnancyChance = 1f - Mathf.Clamp01((1f - __instance.m_pregnancyChance) * multiplier);
                __instance.m_partnerCheckRange *= multiplier;
                __instance.m_totalCheckRange /= multiplier;
                __instance.m_pregnancyDuration /= multiplier;
                return true;
            }

            private static void Finalizer(Procreation __instance, ProcreateState __state)
            {
                if (__state == null)
                    return;

                __instance.m_pregnancyChance = __state.m_pregnancyChance;
                __instance.m_totalCheckRange = __state.m_totalCheckRange;
                __instance.m_partnerCheckRange = __state.m_partnerCheckRange;
                __instance.m_pregnancyDuration = __state.m_pregnancyDuration;
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

            private static void Prefix(Player __instance, ref bool __state)
            {
                if (__instance == Player.m_localPlayer)
                    __state = SeasonState.HasCoolingFood(__instance);
            }

            private static void Postfix(Player __instance, bool __state)
            {
                if (__instance == Player.m_localPlayer && __state != SeasonState.HasCoolingFood(__instance))
                    seasonState.CheckOverheatStatus(__instance);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.UpdateEnvStatusEffects))]
        public static class Player_UpdateEnvStatusEffects_ColdStatus
        {
            private static Player coldStatusPlayer;
            private static bool removeFrostResistanceFromArmor;
            private static readonly int s_wetStatusHash = SEMan.s_statusEffectWet;

            public static bool ShouldRemoveFrostResistance(Player player) => coldStatusPlayer == player && removeFrostResistanceFromArmor;

            private static void Prefix(Player __instance, ref Tuple<Player, bool> __state)
            {
                __state = Tuple.Create(coldStatusPlayer, removeFrostResistanceFromArmor);
                coldStatusPlayer = __instance;
                removeFrostResistanceFromArmor = false;
                int warmPieces = SeasonState.GetWarmClothesCount(__instance);

                if (__instance.GetCurrentBiome() == Heightmap.Biome.Mountain ? gettingWetInMountainsCausesCold.Value : gettingWetInWinterCausesCold.Value && seasonState.GetCurrentSeason() == Season.Winter)
                {
                    bool isWetInColdEnv = (EnvMan.IsCold() || EnvMan.IsFreezing()) && __instance.GetSEMan().HaveStatusEffect(s_wetStatusHash);

                    bool isProtectedFromCold = wearing2WarmPiecesPreventsWetCold.Value && warmPieces > 1;

                    removeFrostResistanceFromArmor = isWetInColdEnv && (!isProtectedFromCold || __instance.IsSwimming());
                }

                if (mountainInWinterRequires2WarmPieces.Value && __instance.GetCurrentBiome() == Heightmap.Biome.Mountain && seasonState.GetCurrentSeason() == Season.Winter && warmPieces < 2)
                    removeFrostResistanceFromArmor = true;
            }

            private static void Finalizer(Tuple<Player, bool> __state)
            {
                if (__state == null)
                    return;

                coldStatusPlayer = __state.Item1;
                removeFrostResistanceFromArmor = __state.Item2;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.ApplyArmorDamageMods))]
        public static class Player_ApplyArmorDamageMods_ColdStatusWhenWet
        {
            public static bool IsFrostResistant(HitData.DamageModifiers mods)
            {
                HitData.DamageModifier modifier = mods.GetModifier(HitData.DamageType.Frost);
                return modifier == HitData.DamageModifier.Resistant || modifier == HitData.DamageModifier.VeryResistant || modifier == HitData.DamageModifier.SlightlyResistant;
            }

            private static void Prefix(HitData.DamageModifiers mods, ref bool __state) => __state = IsFrostResistant(mods); // Check for innate frost resistance

            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Player __instance, ref HitData.DamageModifiers mods, bool __state)
            {
                if (!__state && Player_UpdateEnvStatusEffects_ColdStatus.ShouldRemoveFrostResistance(__instance) && IsFrostResistant(mods))
                    mods.m_frost = HitData.DamageModifier.Normal;

                // This is called if player is not innately frost resistant and:
                // - when player is wet and environment is cold in Winter
                // - when player is wet and in Mountains
                // - when player is in Mountains in Winter and wears less than 2 clothes
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

                __state = __instance.m_events;
                List<RandomEvent> events = new List<RandomEvent>();
                foreach (RandomEvent original in __state)
                {
                    RandomEvent randEvent = original.Clone();
                    SeasonRandomEvents.SeasonRandomEvent seasonRandEvent = randEvents.Find(re => re.m_name == randEvent.m_name);
                    if (seasonRandEvent != null)
                    {
                        SeasonState.seasonRandomEvents.ApplySeasonalBiomes(randEvent);

                        if (seasonRandEvent.m_weight == 0)
                        {
                            randEvent.m_enabled = false;
                        }
                        else if (seasonRandEvent.m_weight > 1)
                        {
                            for (int r = 2; r <= seasonRandEvent.m_weight; r++)
                            {
                                events.Add(randEvent);
                            }
                        }
                    }
                    events.Add(randEvent);
                }
                __instance.m_events = events;
            }

            private static void Finalizer(RandEventSystem __instance, List<RandomEvent> __state)
            {
                if (__state != null)
                    __instance.m_events = __state;
            }
        }

        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.GetValidEventPoints))]
        public static class RandEventSystem_GetValidEventPoints_SeasonalBiomes
        {
            private static bool Prefix(ref RandomEvent ev, ref List<Vector3> __result)
            {
                if (!controlRandomEvents.Value)
                    return true;

                string ev_name = ev.m_name;

                SeasonRandomEvents.SeasonRandomEvent settings = SeasonState.seasonRandomEvents.GetSeasonEvents(seasonState.GetCurrentSeason()).Find(item => item.m_name == ev_name);
                if (settings?.m_weight == 0)
                {
                    __result = new List<Vector3>();
                    return false;
                }

                ev = ev.Clone();
                SeasonState.seasonRandomEvents.ApplySeasonalBiomes(ev);
                return true;
            }
        }

        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.SetRandomEvent))]
        public static class RandEventSystem_SetRandomEvent_SeasonalBiomes
        {
            private static void Prefix(ref RandomEvent ev)
            {
                if (!controlRandomEvents.Value || ev == null)
                    return;

                // Clients reconstruct active events by name instead of receiving spawn data.
                ev = ev.Clone();
                SeasonState.seasonRandomEvents.ApplySeasonalBiomes(ev);
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
            private static void Prefix(Player human, ref List<Tuple<EffectArea, EffectArea.Type, bool>> __state)
            {
                if (human == null || human != Player.m_localPlayer || !seasonState.GetTorchAsFiresource())
                    return;

                bool leftTorch = human.GetLeftItem()?.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Torch;
                bool rightTorch = human.GetRightItem()?.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Torch;
                if (!leftTorch && !rightTorch)
                    return;

                __state = new List<Tuple<EffectArea, EffectArea.Type, bool>>();
                if (human.m_visEquipment != null)
                {
                    if (leftTorch)
                        SuppressHeat(human.m_visEquipment.m_leftItemInstance, __state);
                    if (rightTorch)
                        SuppressHeat(human.m_visEquipment.m_rightItemInstance, __state);
                }

                // Hiding items updates equipment hashes before the visual heat areas are removed.
                human.HideHandItems();
            }

            private static void SuppressHeat(GameObject visual, List<Tuple<EffectArea, EffectArea.Type, bool>> state)
            {
                if (visual == null)
                    return;

                foreach (EffectArea area in visual.GetComponentsInChildren<EffectArea>(true))
                {
                    if (state.Any(entry => entry.Item1 == area))
                        continue;

                    state.Add(Tuple.Create(area, area.m_type, area.m_isHeatType));
                    area.m_type &= ~EffectArea.Type.Heat;
                    area.m_isHeatType = false;
                }
            }

            private static void Finalizer(List<Tuple<EffectArea, EffectArea.Type, bool>> __state)
            {
                if (__state == null)
                    return;

                foreach (var entry in __state)
                {
                    if (entry.Item1 == null)
                        continue;

                    entry.Item1.m_type = entry.Item2;
                    entry.Item1.m_isHeatType = entry.Item3;
                }
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

        [HarmonyPatch(typeof(CameraEffects), nameof(CameraEffects.SetBloom))]
        public static class CameraEffects_SetBloom_WinterOverride
        {
            private static void Prefix(ref bool enabled)
            {
                if (SeasonState.IsActive && UseTextureControllers() && disableBloomInWinter.Value && seasonState.GetCurrentSeason() == Season.Winter)
                    enabled = false;
            }
        }
    }
}
