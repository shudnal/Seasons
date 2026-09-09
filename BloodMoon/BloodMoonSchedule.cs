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
            int annualDays = 0;
            foreach (Season season in Enum.GetValues(typeof(Season)))
                annualDays += seasonState.GetDaysInSeason(season);
            int searchRadius = Math.Max(16, annualDays + 4);
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
            BloodMoonEventPhase expected;
            if (schedule == null || !schedule.IsValid || now < schedule.ForewarningAt)
                expected = BloodMoonEventPhase.Dormant;
            else if (now < schedule.MarkedAt)
                expected = BloodMoonEventPhase.Forewarning;
            else if (now < schedule.ActiveAt)
                expected = BloodMoonEventPhase.Marked;
            else if (now < schedule.AutoCompleteAt)
                expected = BloodMoonEventPhase.Active;
            else if (now < schedule.ForcedEndAt)
                expected = BloodMoonEventPhase.AutoCompleting;
            else if (now < schedule.MorningAt)
                expected = BloodMoonEventPhase.Resolving;
            else
                expected = BloodMoonEventPhase.Resolved;

            // A large forward skip may land beyond ForcedEnd while the persisted event is still in
            // Forewarning/Marked/Active. The controller can traverse Marked -> Active -> AutoCompleting
            // in one call, preserving enrollment, suppression and Active initialization side effects.
            // Let that happen before the following server tick starts the normal resolution pipeline.
            if (expected == BloodMoonEventPhase.Resolving || expected == BloodMoonEventPhase.Resolved)
            {
                BloodMoonEventState state = BloodMoonController.Instance?.State;
                if (state != null && state.EventId == schedule.EventWorldDay &&
                    (state.Phase == BloodMoonEventPhase.Forewarning || state.Phase == BloodMoonEventPhase.Marked || state.Phase == BloodMoonEventPhase.Active))
                    return BloodMoonEventPhase.AutoCompleting;
            }

            return expected;
        }

        private static double ToAbsolute(double eventDayStart, double dayLength, float hour)
        {
            return eventDayStart + dayLength * (hour / 24d);
        }
    }
}