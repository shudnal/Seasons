using System;
using System.Collections.Generic;
using static Seasons.Seasons;

namespace Seasons
{
    [Serializable]
    public class SeasonBiomeEnvironments
    {
        [Serializable]
        public class SeasonBiomeEnvironment
        {
            public class EnvironmentAdd
            {
                public string m_name;
                public EnvEntry m_environment;

                public EnvironmentAdd(string name, EnvEntry environment)
                {
                    m_name = name;
                    m_environment = environment;
                }
            }

            [Serializable]
            public class EnvironmentRemove
            {
                public string m_name;
                public string m_environment;

                public EnvironmentRemove(string name, string environment)
                {
                    m_name = name;
                    m_environment = environment;
                }
            }

            [Serializable]
            public class EnvironmentReplace
            {
                public string m_environment;
                public string replace_to;

                public EnvironmentReplace(string environment, string replaceTo)
                {
                    m_environment = environment;
                    replace_to = replaceTo;
                }
            }

            [Serializable]
            public class EnvironmentReplacePair
            {
                public string m_environment;
                public string replace_to;
            }

            public List<EnvironmentAdd> add = new List<EnvironmentAdd>();

            public List<EnvironmentRemove> remove = new List<EnvironmentRemove>();

            public List<EnvironmentReplace> replace = new List<EnvironmentReplace>();
        }

        public SeasonBiomeEnvironment Spring = new SeasonBiomeEnvironment();
        public SeasonBiomeEnvironment Summer = new SeasonBiomeEnvironment();
        public SeasonBiomeEnvironment Fall = new SeasonBiomeEnvironment();
        public SeasonBiomeEnvironment Winter = new SeasonBiomeEnvironment();

        public SeasonBiomeEnvironments(bool loadDefaults = false)
        {
            if (!loadDefaults)
                return;

            Summer.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Clear", "Clear Summer"));
            Summer.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Misty", "Misty Summer"));
            Summer.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("DeepForest Mist", "DeepForest Mist Summer"));
            Summer.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Mistlands_clear", "Mistlands_clear Summer"));
            Summer.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Heath clear", "Heath clear Summer"));

            Summer.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Meadows", new EnvEntry { m_environment = "Heath clear", m_weight = 2.0f }));

            Summer.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Black forest", new EnvEntry { m_environment = "LightRain", m_weight = 0.1f }));
            Summer.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Black forest", new EnvEntry { m_environment = "Clear", m_weight = 0.2f }));

            Summer.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Swamp", new EnvEntry { m_environment = "SwampRain Summer", m_weight = 0.1f }));
            Summer.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Swamp", new EnvEntry { m_environment = "Swamp Summer", m_weight = 0.1f }));

            Summer.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Mountain", new EnvEntry { m_environment = "Twilight_Clear", m_weight = 1.0f }));
            Summer.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Mountain", new EnvEntry { m_environment = "Twilight_Snow", m_weight = 1.0f }));

            Summer.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Plains", new EnvEntry { m_environment = "ThunderStorm", m_weight = 0.1f }));

            Summer.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Ocean", new EnvEntry { m_environment = "Heath clear", m_weight = 1.0f }));

            Summer.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Mistlands", new EnvEntry { m_environment = "Heath clear", m_weight = 0.5f }));

            Fall.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("ThunderStorm", "ThunderStorm Fall"));

            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Meadows", new EnvEntry { m_environment = "DeepForest Mist", m_weight = 0.2f }));
            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Meadows", new EnvEntry { m_environment = "SwampRain Fall", m_weight = 0.2f }));

            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Black forest", new EnvEntry { m_environment = "LightRain", m_weight = 0.1f }));
            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Black forest", new EnvEntry { m_environment = "SwampRain Fall", m_weight = 0.1f }));

            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Swamp", new EnvEntry { m_environment = "ThunderStorm", m_weight = 0.1f }));

            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Mountain", new EnvEntry { m_environment = "Twilight_SnowStorm", m_weight = 0.5f }));

            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Plains", new EnvEntry { m_environment = "Rain", m_weight = 0.4f }));
            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Plains", new EnvEntry { m_environment = "ThunderStorm", m_weight = 0.2f }));
            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Plains", new EnvEntry { m_environment = "SwampRain Fall", m_weight = 0.1f }));

            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Ocean", new EnvEntry { m_environment = "SwampRain Fall", m_weight = 0.1f }));
            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Ocean", new EnvEntry { m_environment = "DeepForest Mist", m_weight = 0.1f }));

            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Mistlands", new EnvEntry { m_environment = "SwampRain Fall", m_weight = 0.1f }));
            Fall.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Mistlands", new EnvEntry { m_environment = "DeepForest Mist", m_weight = 0.1f }));

            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Rain", "Rain Winter"));
            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("LightRain", "LightRain Winter"));
            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("ThunderStorm", "ThunderStorm Winter"));
            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Clear", "Clear Winter"));
            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Misty", "Misty Winter"));
            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("DeepForest Mist", "DeepForest Mist Winter"));
            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("SwampRain", "SwampRain Winter"));
            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Mistlands_clear", "Mistlands_clear Winter"));
            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Mistlands_rain", "Mistlands_rain Winter"));
            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Mistlands_thunder", "Mistlands_thunder Winter"));
            Winter.replace.Add(new SeasonBiomeEnvironment.EnvironmentReplace("Heath clear", "Heath clear Winter"));

            Winter.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Mountain", new EnvEntry { m_environment = "Twilight_SnowStorm", m_weight = 1.0f }));
            Winter.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Plains", new EnvEntry { m_environment = "Snow", m_weight = 0.5f }));
            Winter.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Ocean", new EnvEntry { m_environment = "Darklands_dark Winter", m_weight = 0.1f }));
            Winter.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Mistlands", new EnvEntry { m_environment = "Twilight_Snow", m_weight = 0.1f }));
            Winter.add.Add(new SeasonBiomeEnvironment.EnvironmentAdd("Mistlands", new EnvEntry { m_environment = "Twilight_SnowStorm", m_weight = 0.1f }));
        }

        public SeasonBiomeEnvironment GetSeasonBiomeEnvironment(Season season)
        {
            return season switch
            {
                Season.Spring => Spring,
                Season.Summer => Summer,
                Season.Fall => Fall,
                Season.Winter => Winter,
                _ => new SeasonBiomeEnvironment(),
            };
        }
    }
}