using System;
using static Seasons.Seasons;
using HarmonyLib;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace Seasons
{
    public class SeasonState
    {
        private Season m_season = Season.Spring;
        private int m_day = 0;

        public static readonly Dictionary<Season, SeasonSettings> seasonsSettings = new Dictionary<Season, SeasonSettings>();

        private SeasonSettings settings { 
            get
            {
                if (!seasonsSettings.ContainsKey(m_season))
                    seasonsSettings.Add(m_season, new SeasonSettings(m_season));

                return seasonsSettings[m_season];
            }
        }

        public SeasonState()
        {
            foreach (Season season in Enum.GetValues(typeof(Season)))
                seasonsSettings.Add(season, new SeasonSettings(season));
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
            foreach (KeyValuePair<int, string> item in seasonsSettingsJSON.Value)
            {
                seasonsSettings[(Season)item.Key] = new SeasonSettings(JsonConvert.DeserializeObject<SeasonSettingsFile>(item.Value));
                LogInfo($"Settings updated: {(Season)item.Key}");
            }
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

}
