using HarmonyLib;
using System.Linq;
using static Seasons.Seasons;

namespace Seasons
{
    public static class LoadingTips
    {
        private static string summerHeatCombinedTip = string.Empty;

        [HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
        public static class Hud_Awake_LoadingTips
        {
            private static void Postfix() => UpdateLoadingTips();
        }

        public static void UpdateLoadingTips()
        {
            if (Hud.instance == null)
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
            UpdateSummerHeatCombinedTip();

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

        private static void UpdateSummerHeatCombinedTip()
        {
            if (!string.IsNullOrEmpty(summerHeatCombinedTip) && Hud.instance.m_loadingTips.Contains(summerHeatCombinedTip))
                Hud.instance.m_loadingTips.Remove(summerHeatCombinedTip);

            string[] parts =
            {
                summerHeatEnabled.Value && summerHeatInstantHeatSources.Value ? "$seasons_loadscreen_tip_summer_heat_activity".Localize() : string.Empty,
                summerHeatEnabled.Value && summerHeatCampFireAddsHeat.Value ? "$seasons_loadscreen_tip_summer_heat_campfire".Localize() : string.Empty
            };

            summerHeatCombinedTip = enableLoadingTips.Value ? string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part))) : string.Empty;
            if (!string.IsNullOrEmpty(summerHeatCombinedTip) && !Hud.instance.m_loadingTips.Contains(summerHeatCombinedTip))
                Hud.instance.m_loadingTips.Add(summerHeatCombinedTip);
        }
    }
}