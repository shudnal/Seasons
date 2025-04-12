using System;
using System.Collections.Generic;
using static Seasons.Seasons;

namespace Seasons
{
    [Serializable]
    public class SeasonClutterSettings
    {
        [Serializable]
        public class SeasonalClutter
        {
            public string clutterName;
            public bool spring;
            public bool summer;
            public bool fall;
            public bool winter;

            public bool GetSeasonState(Season season)
            {
                return season switch
                {
                    Season.Spring => spring,
                    Season.Summer => summer,
                    Season.Fall => fall,
                    Season.Winter => winter,
                    _ => false
                };
            }
        }

        public List<SeasonalClutter> seasonalClutters = new List<SeasonalClutter>();

        public SeasonClutterSettings(bool loadDefaults = false)
        {
            if (!loadDefaults)
                return;

            seasonalClutters.Add(new SeasonalClutter()
            {
                clutterName = ClutterVariantController.c_meadowsFlowersName, 
                spring = true
            });

            seasonalClutters.Add(new SeasonalClutter()
            {
                clutterName = ClutterVariantController.c_forestBloomName,
                spring = true
            });

            seasonalClutters.Add(new SeasonalClutter()
            {
                clutterName = ClutterVariantController.c_swampGrassBloomName,
                spring = true
            });

            seasonalClutters.Add(new SeasonalClutter()
            {
                clutterName = ClutterVariantController.c_meadowsFlowersPrefabName,
                spring = true
            });

            seasonalClutters.Add(new SeasonalClutter()
            {
                clutterName = ClutterVariantController.c_forestBloomPrefabName,
                spring = true
            });

            seasonalClutters.Add(new SeasonalClutter()
            {
                clutterName = ClutterVariantController.c_swampGrassBloomPrefabName,
                spring = true
            });

        }

        public Dictionary<string, bool> GetSeasonalClutterState()
        {
            return GetSeasonalClutterState(seasonState.GetCurrentSeason());
        }

        public Dictionary<string, bool> GetSeasonalClutterState(Season season)
        {
            Dictionary<string, bool> result = new Dictionary<string, bool>();
            foreach (SeasonalClutter clutter in seasonalClutters)
                result.Add(clutter.clutterName, clutter.GetSeasonState(season));

            return result;
        }
    }
}