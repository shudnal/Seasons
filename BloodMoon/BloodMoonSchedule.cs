using System;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonSchedule
    {
        internal static BloodMoonScheduleSnapshot TryCreateForWorldDay(int worldDay)
        {
            if (!SeasonState.IsActive || seasonState.GetSeason(worldDay) != Season.Fall)
                return null;

            int dayInSeason = seasonState.GetDayInSeason(worldDay);
            int eventAutumnDay = Math.Min(Math.Max(BloodMoonConfig.AutumnDay.Value, 4), seasonState.GetDaysInSeason(Season.Fall));
            if (dayInSeason != eventAutumnDay)
                return null;

            double dayLength = seasonState.GetDayLengthInSeconds();
            double dayStart = worldDay * dayLength;
            return new BloodMoonScheduleSnapshot
            {
                EventWorldDay = worldDay,
                AutumnDay = eventAutumnDay,
                ForewarningAt = ToAbsolute(dayStart, dayLength, BloodMoonConfig.ForewarningHour - 72f),
                MarkedAt = ToAbsolute(dayStart, dayLength, BloodMoonConfig.MarkedHour),
                ActiveAt = ToAbsolute(dayStart, dayLength, BloodMoonConfig.ActiveHour),
                AutoCompleteAt = ToAbsolute(dayStart, dayLength, BloodMoonConfig.AutoCompleteHour),
                ForcedEndAt = ToAbsolute(dayStart, dayLength, BloodMoonConfig.ForcedEndHour),
                MorningAt = ToAbsolute(dayStart, dayLength, BloodMoonConfig.MorningHour)
            };
        }

        internal static BloodMoonScheduleSnapshot FindCurrentOrNext(double now)
        {
            if (!SeasonState.IsActive)
                return null;

            int currentWorldDay = seasonState.GetWorldDay(now);
            int searchRadius = Math.Max(16, seasonState.GetDaysInSeason(Season.Fall) * 4 + 4);
            BloodMoonScheduleSnapshot best = null;

            for (int day = Math.Max(0, currentWorldDay - 4); day <= currentWorldDay + searchRadius; ++day)
            {
                BloodMoonScheduleSnapshot candidate = TryCreateForWorldDay(day);
                if (candidate == null || candidate.MorningAt < now)
                    continue;
                if (best == null || candidate.ForewarningAt < best.ForewarningAt)
                    best = candidate;
            }

            return best;
        }

        internal static BloodMoonEventPhase GetExpectedPhase(BloodMoonScheduleSnapshot schedule, double now)
        {
            if (schedule == null || !schedule.IsValid || now < schedule.ForewarningAt)
                return BloodMoonEventPhase.Dormant;
            if (now < schedule.MarkedAt)
                return BloodMoonEventPhase.Forewarning;
            if (now < schedule.ActiveAt)
                return BloodMoonEventPhase.Marked;
            if (now < schedule.AutoCompleteAt)
                return BloodMoonEventPhase.Active;
            if (now < schedule.ForcedEndAt)
                return BloodMoonEventPhase.AutoCompleting;
            if (now < schedule.MorningAt)
                return BloodMoonEventPhase.Resolving;
            return BloodMoonEventPhase.Resolved;
        }

        private static double ToAbsolute(double eventDayStart, double dayLength, float hour)
        {
            return eventDayStart + dayLength * (hour / 24d);
        }
    }
}
