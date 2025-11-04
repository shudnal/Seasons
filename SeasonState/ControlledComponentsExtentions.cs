using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    internal static class ControlledComponentsExtentions
    {
        public static string Localize(this string text) => Localization.instance.Localize(text);

        public static bool ShouldBePickedInWinter(this Pickable pickable)
        {
            return pickable.CanBePicked()
                && !pickable.GetPicked()
                && pickable.IsVulnerableToWinter()
                && seasonState.GetCurrentDay() >= cropsDiesAfterSetDayInWinter.Value
                && !pickable.IsProtectedPosition()
                && !pickable.ProtectedWithHeat();
        }

        public static bool IsVulnerableToWinter(this Pickable pickable)
        {
            return seasonState.GetPlantsGrowthMultiplier() == 0f &&
                    seasonState.GetCurrentSeason() == Season.Winter
                    && !pickable.ShouldSurviveWinter()
                    && !pickable.SurvivedCurrentWinter();
        }

        public static bool SurvivedCurrentWinter(this Pickable pickable)
        {
            return pickable.m_nview 
                && pickable.m_nview.IsValid() 
                && seasonState.GetCurrentSeason() == Season.Winter
                && Mathf.Abs(pickable.m_nview.GetZDO().GetInt(SeasonState.cropSurvivedWinterDayHash, 0) - seasonState.GetCurrentWorldDay()) <= seasonState.GetDaysInSeason();
        }

        public static bool IsIgnored(this Pickable pickable)
        {
            return pickable.m_nview == null ||
                  !pickable.m_nview.IsValid() ||
                  pickable.m_nview.HasOwner() && !pickable.m_nview.IsOwner() ||
                  !pickable.ControlPlantGrowth() ||
                  pickable.IsIgnoredPosition();
        }

        public static string GetColdStatus(this Pickable pickable)
        {
            if (pickable.ShouldSurviveWinter())
                return "$seasons_plant_frost_resistant";
            else if (pickable.ProtectedWithHeat())
                return "$seasons_plant_heat_protected";
            else if (pickable.SurvivedCurrentWinter())
                return "$seasons_plant_survived_winter";
            else
                return "$seasons_plant_will_perish";
        }

        public static bool ControlPlantGrowth(this MonoBehaviour behaviour) => Seasons.ControlPlantGrowth(behaviour.gameObject);

        public static bool ShouldSurviveWinter(this MonoBehaviour behaviour) => Seasons.PlantWillSurviveWinter(behaviour.gameObject);

        public static bool IsIgnoredPosition(this MonoBehaviour behaviour) => Seasons.IsIgnoredPosition(behaviour.transform.position);

        public static bool IsProtectedPosition(this MonoBehaviour behaviour) => Seasons.IsProtectedPosition(behaviour.transform.position);

        public static bool ProtectedWithHeat(this MonoBehaviour behaviour) => Seasons.ProtectedWithHeat(behaviour.transform.position);

    }
}
