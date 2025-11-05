using System;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    public static class ControlledComponentsExtentions
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
                && Mathf.Abs(pickable.m_nview.GetZDO().GetInt(SeasonsVars.s_cropSurvivedWinterDayHash, 0) - seasonState.GetCurrentWorldDay()) <= seasonState.GetDaysInSeason();
        }

        public static bool IsFreezingToDeath(this Pickable pickable)
        {
            return pickable.m_nview
                && pickable.m_nview.IsValid()
                && seasonState.GetCurrentSeason() == Season.Winter
                && pickable.GetSecondsToFreeze() > 0;
        }

        public static double GetSecondsToFreeze(this Pickable pickable)
        {
            if (pickable.m_nview && pickable.m_nview.IsValid() && ZNet.instance)
            {
                long freezingTime = pickable.m_nview.GetZDO().GetLong(SeasonsVars.s_cropStartedFreezingHash, 0L);
                if (freezingTime <= 0)
                    return 0d;

                float secondsToFreeze = secondsToFreezeForCropInWinter.Value;
                if (secondsToFreeze % 60f == 0)
                    secondsToFreeze -= 2f;

                TimeSpan timeSpan = new DateTime(freezingTime).AddSeconds(secondsToFreeze) - ZNet.instance.GetTime();
                return timeSpan.TotalSeconds;
            }

            return 0d;
        }

        public static bool CheckForPerishInWinter(this Pickable pickable)
        {
            if (!pickable.ShouldBePickedInWinter())
            {
                pickable.SetFreezing(false);
                return false;
            }

            if (secondsToFreezeForCropInWinter.Value > 0)
                pickable.SetFreezing(true);

            if (pickable.IsFreezingToDeath())
                return false;

            pickable.StartCoroutine(PickableSetPickedInWinter(pickable));
            return true;
        }

        public static void SetFreezing(this Pickable pickable, bool freezing)
        {
            if (pickable.m_nview && pickable.m_nview.IsValid() && ZNet.instance && pickable.m_nview.GetZDO() is ZDO zdo)
            {
                if (freezing && zdo.GetLong(SeasonsVars.s_cropStartedFreezingHash, 0L) == 0L && seasonState.GetCurrentSeason() == Season.Winter && seasonState.GetCurrentDay() >= cropsDiesAfterSetDayInWinter.Value)
                    zdo.Set(SeasonsVars.s_cropStartedFreezingHash, ZNet.instance.GetTime().Ticks);
                else if (!freezing)
                    zdo.Set(SeasonsVars.s_cropStartedFreezingHash, 0L);
            }
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
            else if (pickable.GetSecondsToFreeze() is double seconds && seconds != 0d && secondsToFreezeForCropInWinter.Value > 0)
            {
                if (seconds > 0)
                    return $"$seasons_plant_is_freezing\n{FromPercent(seconds / secondsToFreezeForCropInWinter.Value)}";
                else
                    return "$seasons_plant_is_frozen";
            }
            else if (seasonState.GetCurrentDay() > cropsDiesAfterSetDayInWinter.Value)
                return "$seasons_plant_will_perish";
            else
                return "$seasons_plant_is_exposed";
        }

        public static bool ControlPlantGrowth(this MonoBehaviour behaviour) => Seasons.ControlPlantGrowth(behaviour.gameObject);

        public static bool ShouldSurviveWinter(this MonoBehaviour behaviour) => Seasons.PlantWillSurviveWinter(behaviour.gameObject);

        public static bool IsIgnoredPosition(this MonoBehaviour behaviour) => Seasons.IsIgnoredPosition(behaviour.transform.position);

        public static bool IsProtectedPosition(this MonoBehaviour behaviour) => Seasons.IsProtectedPosition(behaviour.transform.position);

        public static bool ProtectedWithHeat(this MonoBehaviour behaviour) => Seasons.ProtectedWithHeat(behaviour.transform.position);

    }
}
