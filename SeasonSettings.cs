using HarmonyLib;
using System;
using static Seasons.Seasons;

namespace Seasons
{
    [Serializable]
    public class SeasonSettingsFile
    {
        public int daysInSeason = 10;
        public int nightLength = SeasonSettings.nightLentghDefault;
    }

    public class SeasonSettings
    {
        public const int nightLentghDefault = 30;

        public int m_daysInSeason = 10;
        public int m_nightLength = nightLentghDefault;

        public SeasonSettings(Season season)
        {
            switch (season)
            {
                case Season.Spring:
                    {
                        break;
                    }
                case Season.Summer:
                    {
                        m_nightLength = 20;
                        break;
                    }
                case Season.Fall:
                    {
                        break;
                    }
                case Season.Winter:
                    {
                        m_nightLength = 40;
                        break;
                    }
            }
        }

        public SeasonSettings(SeasonSettingsFile settings)
        {
            m_daysInSeason = settings.daysInSeason;
            m_nightLength = settings.nightLength;
        }

        public static bool GetSeasonByFilename(string filename, out Season season)
        {
            season = Season.Spring;

            foreach (Season season1 in Enum.GetValues(typeof(Season)))
                if (filename.ToLower() == $"{season1}.json".ToLower())
                {
                    season = season1;
                    return true;
                }

            return false;
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.Awake))]
    public static class EnvMan_Awake_SeasonSettingsConfigWatcher
    {
        private static void Postfix()
        {
            SetupConfigWatcher();
        }
    }

    
}
