using System;
using System.Collections.Generic;

namespace Seasons
{
    [Serializable]
    public class SeasonWorldSettings
    {
        [Serializable]
        public class SeasonWorld
        {
            public string startTimeUTC = "";
            public long dayLengthSeconds = 0L;

            public SeasonWorld(DateTime timeUTC, long seconds)
            {
                startTimeUTC = timeUTC.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
                dayLengthSeconds = seconds;
            }
        }

        public Dictionary<string, SeasonWorld> worlds = new Dictionary<string, SeasonWorld>();

        public SeasonWorldSettings(bool loadDefaults = false)
        {
            if (!loadDefaults)
                return;

            worlds.Add("ExampleSeasonsWorld", new SeasonWorld(DateTime.UtcNow, 86400L));
        }

        public DateTime GetStartTimeUTC(World world)
        {
            if (HasWorldSettings(world) && DateTime.TryParse(GetWorldSettings(world).startTimeUTC, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime result))
                return DateTime.Compare(result, new DateTime(2023, 1, 1, 0, 0, 0)) < 0 ? new DateTime(2023, 1, 1, 0, 0, 0) : result;

            return DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(ZNet.instance.GetTimeSeconds()));
        }

        public long GetDayLengthSeconds(World world)
        {
            if (!HasWorldSettings(world))
                return SeasonState.GetDayLengthInSecondsEnvMan();

            return Math.Max(GetWorldSettings(world).dayLengthSeconds, 5);
        }

        public bool HasWorldSettings(World world)
        {
            return world != null && worlds.ContainsKey(world.m_name);
        }

        public SeasonWorld GetWorldSettings(World world)
        {
            if (!HasWorldSettings(world))
                return null;
           
            return worlds[world.m_name];
        }
    }
}