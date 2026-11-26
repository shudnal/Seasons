using System;
using System.Collections.Generic;
using static Seasons.Seasons;

namespace Seasons
{
    internal static class SeasonFuzzyTimer
    {
        internal const string DefaultStages = "$seasons_timer_fuzzy_arrives,$seasons_timer_fuzzy_reigns,$seasons_timer_fuzzy_fades";

        private static readonly string[] s_defaultLabels = DefaultStages.Split(',');
        private static string[] s_labels = s_defaultLabels;
        private static string s_text;
        private static double s_validFrom;
        private static double s_validUntil;

        internal static void Invalidate() => s_text = null;

        internal static void Configure(string value)
        {
            List<string> labels = new List<string>();
            foreach (string entry in (value ?? string.Empty).Split(','))
            {
                string label = entry.Trim();
                if (label.Length != 0)
                    labels.Add(label);
            }
            s_labels = labels.Count == 0 ? s_defaultLabels : labels.ToArray();
            Invalidate();
        }

        internal static string GetText()
        {
            if (!SeasonState.IsActive || ZNet.instance == null)
                return string.Empty;

            if (s_labels.Length == 1)
                return s_text ?? (s_text = Localization.instance.Localize(s_labels[0]));

            double now = seasonState.GetTotalSeconds();
            if (s_text != null && now >= s_validFrom && now < s_validUntil)
                return s_text;

            double duration = seasonState.GetSecondsInSeason();
            double remaining = seasonState.GetTimeToCurrentSeasonEnd(now);
            double start = now + remaining - duration;
            double progress = Math.Clamp(1d - remaining / duration, 0d, 1d);
            int index = Math.Min(s_labels.Length - 1, (int)Math.Floor(progress * s_labels.Length));

            // Compare the actual boundaries too: division can round to the adjacent index.
            if (index > 0 && now < Boundary(start, duration, index))
                index--;
            else if (index < s_labels.Length - 1 && now >= Boundary(start, duration, index + 1))
                index++;

            // Clamp the first/last labels until the calendar changes, including delayed transitions.
            // Both bounds are checked so a backward time jump can select an earlier stage.
            s_validFrom = index == 0 ? double.NegativeInfinity : Boundary(start, duration, index);
            s_validUntil = index == s_labels.Length - 1 ? double.PositiveInfinity : Boundary(start, duration, index + 1);
            s_text = Localization.instance.Localize(s_labels[index]);
            return s_text;
        }

        // Use the actual stage's start boundary, not rounded season-day indices.
        // One day can contain multiple stages; only the stage current this morning
        // is relevant. The first day begins a new season even with a single label.
        internal static string GetMorningText(bool onlyWhenPeriodChanges)
        {
            string text = GetText();
            if (!onlyWhenPeriodChanges || string.IsNullOrEmpty(text))
                return text;

            if (seasonState.GetCurrentDay() == 1)
                return text;

            if (s_labels.Length == 1)
                return string.Empty;

            double now = seasonState.GetTotalSeconds();
            double dayLength = seasonState.GetDayLengthInSeconds();
            double previousMorning = now - dayLength;
            if (controlNightLength.Value)
            {
                // Morning falls at a different fraction of the day when night length
                // varies. Compare against yesterday's actual morning, not the same
                // clock fraction as today's morning.
                int today = seasonState.GetWorldDay(now);
                int yesterday = today - 1;
                float todayMorning = seasonState.DayStartFraction(seasonState.GetSeason(today), seasonState.GetDayInSeason(today));
                float previousDayMorning = seasonState.DayStartFraction(seasonState.GetSeason(yesterday), seasonState.GetDayInSeason(yesterday));
                previousMorning += (previousDayMorning - todayMorning) * dayLength;
            }
            return s_validFrom > previousMorning ? text : string.Empty;
        }

        private static double Boundary(double start, double duration, int index) =>
            start + duration * ((double)index / s_labels.Length);
    }
}
