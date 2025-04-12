using System;
using static Seasons.Seasons;

namespace Seasons
{
    [Serializable]
    public class SeasonLightings
    {
        [Serializable]
        public class LightingSettings
        {
            public float luminanceMultiplier = 1.0f;
            public float fogDensityMultiplier = 1.0f;
        }

        [Serializable]
        public class SeasonLightingSettings
        {
            public LightingSettings indoors = new LightingSettings();
            public LightingSettings morning = new LightingSettings();
            public LightingSettings day = new LightingSettings();
            public LightingSettings evening = new LightingSettings();
            public LightingSettings night = new LightingSettings();

            public float lightIntensityDayMultiplier = 1.0f;
            public float lightIntensityNightMultiplier = 1.0f;
        }

        public SeasonLightingSettings Spring = new SeasonLightingSettings();
        public SeasonLightingSettings Summer = new SeasonLightingSettings();
        public SeasonLightingSettings Fall = new SeasonLightingSettings();
        public SeasonLightingSettings Winter = new SeasonLightingSettings();

        public SeasonLightings(bool loadDefaults = false)
        {
            if (!loadDefaults)
                return;

            Summer.indoors.fogDensityMultiplier = 0.9f;

            Summer.morning.luminanceMultiplier = 1.1f;
            Summer.morning.fogDensityMultiplier = 0.9f;

            Summer.evening.luminanceMultiplier = 1.1f;
            Summer.evening.fogDensityMultiplier = 0.9f;
            
            Summer.night.luminanceMultiplier = 1.1f;
            Summer.night.fogDensityMultiplier = 0.9f;

            Summer.lightIntensityNightMultiplier = 0.9f;

            Fall.morning.luminanceMultiplier = 0.95f;
            Fall.morning.fogDensityMultiplier = 1.1f;

            Fall.evening.luminanceMultiplier = 0.95f;
            Fall.evening.fogDensityMultiplier = 1.1f;

            Fall.night.luminanceMultiplier = 0.9f;
            Fall.night.fogDensityMultiplier = 1.3f;

            Fall.lightIntensityNightMultiplier = 1.2f;

            Winter.indoors.luminanceMultiplier = 0.9f;
            Winter.indoors.fogDensityMultiplier = 1.1f;

            Winter.morning.luminanceMultiplier = 0.9f;
            Winter.morning.fogDensityMultiplier = 1.2f;

            Winter.evening.luminanceMultiplier = 0.9f;
            Winter.evening.fogDensityMultiplier = 1.2f;

            Winter.night.luminanceMultiplier = 0.8f;
            Winter.night.fogDensityMultiplier = 1.7f;

            Winter.lightIntensityNightMultiplier = 1.5f;
        }

        public SeasonLightingSettings GetSeasonLighting(Season season)
        {
            return season switch
            {
                Season.Spring => Spring,
                Season.Summer => Summer,
                Season.Fall => Fall,
                Season.Winter => Winter,
                _ => new SeasonLightingSettings(),
            };
        }
    }
}