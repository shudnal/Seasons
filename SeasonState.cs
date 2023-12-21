using System;
using static Seasons.Seasons;
using UnityEngine;
using HarmonyLib;
using Newtonsoft.Json;
using System.IO;
using System.Collections.Generic;

namespace Seasons
{
    [Serializable]
    public class SeasonSettingsFile
    {
        public int m_daysInSeason = 10;
        public int m_nightLength = SeasonSettings.nightLentghDefault;
    }

    public class SeasonSettings
    {
        public const int nightLentghDefault = 30;

        public int m_daysInSeason = 10;
        public int m_nightLength = nightLentghDefault;

        public SeasonSettings(Season season) 
        {
            /*string filename = $"{season}.json";

            config new DirectoryInfo(configDirectory);


            File.WriteAllText(filename, JsonConvert.SerializeObject(controllers, Formatting.Indented));*/
        }
    }

    public class SeasonState
    {
        private Season m_season = Season.Spring;
        private int m_day = 0;

        private SeasonSettings settings { 
            get
            {
                if (seasonsSettings.Value.TryGetValue(GetCurrentSeason(), out SeasonSettings settings))
                    return settings;

                return new SeasonSettings(m_season);
            }
        }

        public bool IsActive => EnvMan.instance != null;

        public void UpdateState(int day, float dayFraction)
        {
            float fraction = Mathf.Clamp01(dayFraction);

            int dayInSeason = GetDayInSeason(day);

            int season = (int)m_season;

            m_season = overrideSeason.Value ? seasonOverrided.Value : GetSeason(day);

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

        }

        private Season GetSeason(int day)
        {
            return (Season)(day / daysInSeason.Value % seasonsCount);
        }

        private int GetDayInSeason(int day)
        {
            return day % daysInSeason.Value;
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
        }

        private void CheckIfDayChanged(int dayInSeason)
        {
            if (m_day == dayInSeason)
                return;

            m_day = dayInSeason;
            ClutterVariantController.instance.UpdateColors();
        }

        public override string ToString()
        {
            return $"{m_season} day:{m_day}";
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.FixedUpdate))]
    public static class EnvMan_FixedUpdate_SeasonStateUpdate
    {
        private static void Postfix(EnvMan __instance)
        {
            int day = __instance.GetCurrentDay();
            float dayFraction = __instance.GetDayFraction();

            seasonState.UpdateState(day, dayFraction);
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
            ZLog.Log((object)("Time " + timeSeconds + ", day:" + day + "    nextm:" + morningStartSec + "  skipspeed:" + ___m_timeSkipSpeed));

            return false;
        }
    }

}
