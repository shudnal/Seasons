using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using static Seasons.Seasons;

namespace Seasons
{
    public static class LoadingTips
    {
        private static readonly List<string> summerHeatCombinedTips = new List<string>();

        [HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
        public static class Hud_Awake_LoadingTips
        {
            private static void Postfix() => UpdateLoadingTips();
        }

        public static void UpdateLoadingTips()
        {
            if (!UseTextureControllers() || Hud.instance == null || !SeasonState.IsActive)
                return;

            UpdateTipBasedOnValue("$seasons_loadscreen_tip_ice", enableFrozenWater.Value);
            UpdateTipBasedOnValue("$seasons_loadscreen_tip_torch", seasonState.GetSeasonSettings(Season.Winter).m_torchAsFiresource);
            UpdateTipBasedOnValue("$seasons_loadscreen_tip_harvests", seasonState.GetSeasonSettings(Season.Spring).m_plantsGrowthMultiplier != 1 || seasonState.GetSeasonSettings(Season.Summer).m_plantsGrowthMultiplier != 1);
            UpdateTipBasedOnValue("$seasons_loadscreen_tip_nights", seasonState.GetSeasonSettings(Season.Winter).m_nightLength > SeasonSettings.nightLentghDefault || controlLightings.Value);
            UpdateTipBasedOnValue("$seasons_loadscreen_tip_overheat", !summerHeatEnabled.Value && summerHeatAddsExtraWarmCloth.Value && seasonState.GetSeasonSettings(Season.Summer).m_overheatIn2WarmClothes);
            UpdateTipBasedOnValue("$seasons_loadscreen_tip_summer_heat", summerHeatEnabled.Value);
            UpdateTipBasedOnValue("$seasons_loadscreen_tip_summer_heat_cold_food", summerHeatEnabled.Value && !string.IsNullOrWhiteSpace(summerHeatCoolingFoods.Value));
            UpdateTipBasedOnValue("$seasons_loadscreen_tip_summer_heat_risk", summerHeatEnabled.Value);
            UpdateTipBasedOnValue("$seasons_loadscreen_tip_firewood", seasonState.GetSeasonSettings(Season.Winter).m_fireplaceDrainMultiplier > 1f);
            UpdateTipBasedOnValue("$seasons_loadscreen_tip_perish", cropsDiesAfterSetDayInWinter.Value != 0);
            UpdateTipBasedOnValue("$seasons_loadscreen_tip_traders", controlTraders.Value);
            UpdateTipBasedOnValue("$seasons_loadscreen_tip_stats", controlStats.Value);
            UpdateTipBasedOnValue("$seasons_loadscreen_tip_wolves", controlRandomEvents.Value);
            UpdateTipBasedOnValue("$seasons_loadscreen_tip_swimming", freezingSwimmingInWinter.Value);
            UpdateTipBasedOnValue("$seasons_loadscreen_tip_clutter", controlGrass.Value);
            UpdateSummerHeatCombinedTips();

            Hud.instance.m_haveSetupLoadScreen = false;

            LogInfo("Loading tips updated.");
        }

        private static void UpdateTipBasedOnValue(string tip, bool value)
        {
            if (enableLoadingTips.Value && value && !Hud.instance.m_loadingTips.Contains(tip))
                Hud.instance.m_loadingTips.Add(tip);
            else if ((!enableLoadingTips.Value || !value) && Hud.instance.m_loadingTips.Contains(tip))
                Hud.instance.m_loadingTips.Remove(tip);
        }

        private static void UpdateSummerHeatCombinedTips()
        {
            foreach (string tip in summerHeatCombinedTips)
                Hud.instance.m_loadingTips.Remove(tip);
            summerHeatCombinedTips.Clear();

            if (!enableLoadingTips.Value || !summerHeatEnabled.Value)
                return;

            AddSummerHeatCombinedTips(BuildSummerHeatOutfitTipParts());
            AddSummerHeatCombinedTips(BuildSummerHeatBehaviorTipParts());
        }

        private static IEnumerable<string> BuildSummerHeatOutfitTipParts()
        {
            bool armorHeatEnabled = summerHeatArmorHeatEnabled.Value;
            bool hasOutfitSpecificRules = HasText(summerHeatOpenHelmetItems.Value) || HasText(summerHeatOpenChestItems.Value) || HasText(summerHeatOpenLegItems.Value) || HasText(summerHeatLightCloakItems.Value) || HasColdWeatherArmorHeatRules();

            if (armorHeatEnabled)
                yield return "$seasons_loadscreen_tip_summer_heat_clothing".Localize();
            if (armorHeatEnabled && hasOutfitSpecificRules)
                yield return "$seasons_loadscreen_tip_summer_heat_cold_clothing".Localize();
            if (armorHeatEnabled && HasText(summerHeatBareHeadHairItems.Value))
                yield return "$seasons_loadscreen_tip_summer_heat_hairstyle".Localize();
        }

        private static IEnumerable<string> BuildSummerHeatBehaviorTipParts()
        {
            if (summerHeatInstantHeatSources.Value || summerHeatEncumberedAddsHeat.Value)
                yield return "$seasons_loadscreen_tip_summer_heat_activity".Localize();
            if (summerHeatCampFireAddsHeat.Value)
                yield return "$seasons_loadscreen_tip_summer_heat_campfire".Localize();
            if (summerHeatNoonEffectPercent.Value > 0f || summerHeatNightFactor.Value < 1f)
                yield return "$seasons_loadscreen_tip_summer_heat_day_night".Localize();
        }

        private static void AddSummerHeatCombinedTips(IEnumerable<string> parts)
        {
            List<string> filteredParts = parts.Where(part => !string.IsNullOrWhiteSpace(part)).ToList();
            if (filteredParts.Count == 0)
                return;

            string prefix = "$seasons_loadscreen_tip_summer_heat_prefix".Localize();
            for (int i = 0; i < filteredParts.Count; i += 3)
            {
                string tip = $"{prefix} {string.Join(" ", filteredParts.Skip(i).Take(3))}";
                summerHeatCombinedTips.Add(tip);
                if (!Hud.instance.m_loadingTips.Contains(tip))
                    Hud.instance.m_loadingTips.Add(tip);
            }
        }

        private static bool HasText(string value) => !string.IsNullOrWhiteSpace(value);

        private static bool HasColdWeatherArmorHeatRules()
        {
            return summerHeatColdArmorHeating.Value > 0f
                || summerHeatColdArmorCoolingPenalty.Value > 0f
                || summerHeatColdCloakHeating.Value > 0f
                || summerHeatColdCloakCoolingPenalty.Value > 0f;
        }
    }
}
