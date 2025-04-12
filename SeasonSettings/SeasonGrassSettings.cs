using System;
using System.Collections.Generic;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    [Serializable]
    public class SeasonGrassSettings
    {
        [Serializable]
        public class SeasonGrass
        {
            public int m_day = 1;
            public float m_grassPatchSize = 10f;
            public float m_amountScale = 1.5f;
            public float m_scaleMin = 1f;
            public float m_scaleMax = 1f;
        }

        public List<SeasonGrass> Spring = new List<SeasonGrass>();
        public List<SeasonGrass> Summer = new List<SeasonGrass>();
        public List<SeasonGrass> Fall = new List<SeasonGrass>();
        public List<SeasonGrass> Winter = new List<SeasonGrass>();

        public SeasonGrassSettings(bool loadDefaults = false)
        {
            if (!loadDefaults)
                return;

            Winter.Add(new SeasonGrass()
            {
                m_day = 1,
                m_grassPatchSize = 15f,
                m_scaleMax = 0.75f
            });

            Winter.Add(new SeasonGrass()
            {
                m_day = 2,
                m_grassPatchSize = 20f,
                m_scaleMax = 0.5f
            });

            Winter.Add(new SeasonGrass()
            {
                m_day = 3,
                m_grassPatchSize = 25f,
                m_scaleMax = 0.25f
            });

            Winter.Add(new SeasonGrass()
            {
                m_day = 4,
                m_scaleMax = 0f
            });

            Winter.Add(new SeasonGrass()
            {
                m_day = 10,
                m_scaleMax = 0f
            });

            Spring.Add(new SeasonGrass()
            {
                m_day = 1,
                m_grassPatchSize = 20f,
                m_amountScale = 2f,
                m_scaleMin = 0.6f,
                m_scaleMax = 0.6f
            });

            Spring.Add(new SeasonGrass()
            {
                m_day = 3,
                m_scaleMin = 0.7f,
                m_scaleMax = 0.75f
            });

            Spring.Add(new SeasonGrass()
            {
                m_day = 8,
                m_scaleMin = 0.85f,
                m_scaleMax = 0.9f
            });

            Spring.Add(new SeasonGrass()
            {
                m_day = 10,
                m_grassPatchSize = 11f,
                m_scaleMax = 1.1f
            });

            Summer.Add(new SeasonGrass()
            {
                m_day = 1,
                m_scaleMin = 0.9f,
                m_scaleMax = 1.1f,
                m_grassPatchSize = 11f,
            });

            Summer.Add(new SeasonGrass()
            {
                m_day = 6,
                m_scaleMin = 1.1f,
                m_scaleMax = 1.4f,
                m_grassPatchSize = 14f,
                m_amountScale = 1.4f,
            });

            Summer.Add(new SeasonGrass()
            {
                m_day = 9,
                m_scaleMax = 1.1f,
            });

            Fall.Add(new SeasonGrass()
            {
                m_day = 1,
                m_scaleMax = 1.1f,
            });

            Fall.Add(new SeasonGrass()
            {
                m_day = 5,
                m_scaleMax = 1.3f,
            });

            Fall.Add(new SeasonGrass()
            {
                m_day = 10,
                m_grassPatchSize = 12f,
                m_amountScale = 1.2f,
                m_scaleMin = 0.8f,
            });
       }

        public SeasonGrass GetGrassSettings()
        {
            return GetGrassSettings(seasonState.GetCurrentDay());
        }

        public SeasonGrass GetGrassSettings(int day)
        {
            // Grass controlled only after first spring
            if (seasonState.GetCurrentWorldDay() > seasonState.GetDaysInSeason(Season.Spring))
            {
                List<SeasonGrass> seasonDays = GetSeasonGrass(seasonState.GetCurrentSeason());
                for (int i = 0; i < seasonDays.Count; i++)
                {
                    SeasonGrass seasonGrass = seasonDays[i];
                    if (day == seasonGrass.m_day || day <= seasonGrass.m_day && i == 0 || day >= seasonGrass.m_day && i == seasonDays.Count - 1)
                        return seasonGrass;

                    if (seasonGrass.m_day < day)
                        continue;

                    // Duplicate days == bad data, fallback
                    if (seasonDays[i].m_day == seasonDays[i - 1].m_day)
                        break;

                    float target = (float)(day - seasonDays[i - 1].m_day) / (seasonDays[i].m_day - seasonDays[i - 1].m_day);

                    return new SeasonGrass()
                    {
                        m_day = day,
                        m_grassPatchSize = Mathf.Lerp(seasonDays[i - 1].m_grassPatchSize, seasonDays[i].m_grassPatchSize, target),
                        m_amountScale = Mathf.Lerp(seasonDays[i - 1].m_amountScale, seasonDays[i].m_amountScale, target),
                        m_scaleMin = Mathf.Lerp(seasonDays[i - 1].m_scaleMin, seasonDays[i].m_scaleMin, target),
                        m_scaleMax = Mathf.Lerp(seasonDays[i - 1].m_scaleMax, seasonDays[i].m_scaleMax, target),
                    };
                }
            }

            return new SeasonGrass()
            {
                m_day = day,
                m_grassPatchSize = grassDefaultPatchSize.Value,
                m_amountScale = grassDefaultAmountScale.Value,
                m_scaleMin = grassSizeDefaultScaleMin.Value,
                m_scaleMax = grassSizeDefaultScaleMax.Value,
            };
        }

        private List<SeasonGrass> GetSeasonGrass(Season season)
        {
            return season switch
            {
                Season.Spring => Spring,
                Season.Summer => Summer,
                Season.Fall => Fall,
                Season.Winter => Winter,
                _ => new List<SeasonGrass>(),
            };
        }
    }
}