using System;
using System.Collections.Generic;
using static Seasons.Seasons;

namespace Seasons
{
    [Serializable]
    public class SeasonRandomEvents
    {
        [Serializable]
        public class SeasonRandomEvent
        {
            public string m_name;
            public string m_biomes;
            public int m_weight;

            public SeasonRandomEvent()
            {

            }

            public SeasonRandomEvent(RandomEvent randomEvent)
            {
                m_name = randomEvent.m_name;
                m_weight = 1;
                m_biomes = randomEvent.m_biome.ToString();
            }

            public Heightmap.Biome GetBiome()
            {
                return (Heightmap.Biome)Enum.Parse(typeof(Heightmap.Biome), m_biomes);
            }
        }

        public List<SeasonRandomEvent> Spring = new List<SeasonRandomEvent>();
        public List<SeasonRandomEvent> Summer = new List<SeasonRandomEvent>();
        public List<SeasonRandomEvent> Fall = new List<SeasonRandomEvent>();
        public List<SeasonRandomEvent> Winter = new List<SeasonRandomEvent>();

        public SeasonRandomEvents(bool loadDefaults = false)
        {
            if (!loadDefaults)
                return;

            Spring.Add(new SeasonRandomEvent() 
            {
                m_name = "foresttrolls",
                m_weight = 2
            });
            Spring.Add(new SeasonRandomEvent()
            {
                m_name = "bats",
                m_weight = 0
            });
            Spring.Add(new SeasonRandomEvent()
            {
                m_name = "army_eikthyr",
                m_weight = 2
            });
            Spring.Add(new SeasonRandomEvent()
            {
                m_name = "army_theelder",
                m_weight = 2
            });

            Summer.Add(new SeasonRandomEvent()
            {
                m_name = "bats",
                m_weight = 2
            });
            Summer.Add(new SeasonRandomEvent()
            {
                m_name = "surtlings",
                m_weight = 2
            });
            Summer.Add(new SeasonRandomEvent()
            {
                m_name = "wolves",
                m_weight = 0
            });
            Summer.Add(new SeasonRandomEvent()
            {
                m_name = "army_goblin",
                m_weight = 2
            });

            Fall.Add(new SeasonRandomEvent()
            {
                m_name = "skeletons",
                m_weight = 2
            });
            Fall.Add(new SeasonRandomEvent()
            {
                m_name = "blobs",
                m_weight = 2
            });
            Fall.Add(new SeasonRandomEvent()
            {
                m_name = "army_bonemass",
                m_weight = 2
            });

            Winter.Add(new SeasonRandomEvent()
            {
                m_name = "wolves",
                m_biomes = "Meadows, Swamp, Mountain, BlackForest, Plains, DeepNorth",
                m_weight = 2
            });
            Winter.Add(new SeasonRandomEvent()
            {
                m_name = "army_moder",
                m_weight = 2
            });
            Winter.Add(new SeasonRandomEvent()
            {
                m_name = "skeletons",
                m_weight = 0
            });
            Winter.Add(new SeasonRandomEvent()
            {
                m_name = "foresttrolls",
                m_weight = 0
            });
            Winter.Add(new SeasonRandomEvent()
            {
                m_name = "surtlings",
                m_weight = 0
            });
            Winter.Add(new SeasonRandomEvent()
            {
                m_name = "blobs",
                m_weight = 0
            });
        }

        public List<SeasonRandomEvent> GetSeasonEvents(Season season)
        {
            return season switch
            {
                Season.Spring => Spring,
                Season.Summer => Summer,
                Season.Fall => Fall,
                Season.Winter => Winter,
                _ => new List<SeasonRandomEvent>(),
            };
        }
    }
}