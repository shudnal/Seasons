using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    internal static class ControlledComponentsExtentions
    {
        public static string Localize(this string text) => Localization.instance.Localize(text);

        public static bool ShouldBePickedInWinter(this Pickable pickable)
        {
            return !pickable.GetPicked()
                && pickable.IsVulnerableToWinter()
                && seasonState.GetCurrentDay() >= cropsDiesAfterSetDayInWinter.Value
                && !pickable.IsProtectedPosition()
                && !pickable.ProtectedWithHeat();
        }

        public static bool IsVulnerableToWinter(this Pickable pickable)
        {
            return seasonState.GetPlantsGrowthMultiplier() == 0f &&
                    seasonState.GetCurrentSeason() == Season.Winter
                    && !pickable.ShouldSurviveWinter();
        }

        public static bool IsIgnored(this Pickable pickable)
        {
            return pickable.m_nview == null ||
                  !pickable.m_nview.IsValid() ||
                  !pickable.m_nview.IsOwner() ||
                  !pickable.ControlPlantGrowth() ||
                  pickable.IsIgnoredPosition();
        }

        public static string GetColdStatus(this Pickable pickable)
        {
            if (pickable.ShouldSurviveWinter())
                return "$se_frostres_name";
            else if (pickable.ProtectedWithHeat())
                return "$se_fire_tooltip";
            else
                return "$piece_plant_toocold";
        }

        public static bool ControlPlantGrowth(this MonoBehaviour behaviour) => Seasons.ControlPlantGrowth(behaviour.gameObject);

        public static bool ShouldSurviveWinter(this MonoBehaviour behaviour) => Seasons.PlantWillSurviveWinter(behaviour.gameObject);

        public static bool IsIgnoredPosition(this MonoBehaviour behaviour) => Seasons.IsIgnoredPosition(behaviour.transform.position);

        public static bool IsProtectedPosition(this MonoBehaviour behaviour) => Seasons.IsProtectedPosition(behaviour.transform.position);

        public static bool ProtectedWithHeat(this MonoBehaviour behaviour) => Seasons.ProtectedWithHeat(behaviour.transform.position);

    }
}
