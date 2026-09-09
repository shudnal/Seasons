using HarmonyLib;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonRecoverySchedule
    {
        internal static bool ReconcileLoadedState(BloodMoonEventState state)
        {
            if (state == null || state.Phase == BloodMoonEventPhase.Resolving || state.Phase == BloodMoonEventPhase.Resolved ||
                !state.IsEventLive || state.Schedule == null || !state.Schedule.IsValid || !SeasonState.IsActive)
                return false;

            double now = seasonState.GetTotalSeconds();
            BloodMoonEventPhase expected = BloodMoonSchedule.GetExpectedPhase(state.Schedule, now);

            // Persistence loading happens before BloodMoonController.State receives this object. Do not
            // mutate a recovered Forewarning/Marked/Active state directly here: doing so would bypass
            // EnterMarked/EnterActive side effects such as enrollment, suppression, environment setup and
            // group/spawn initialization. The first normal server tick runs after State assignment and uses
            // BloodMoonSchedule.GetExpectedPhase + AdvanceToExpectedPhase, including the AutoCompleting bridge
            // for a large forward jump beyond ForcedEnd. Backward clock changes intentionally do nothing.
            if ((int)expected > (int)state.Phase)
                LogInfo($"[BloodMoon][event:{state.EventId}][phase] Recovery observed forward schedule target {expected}; deferring catch-up to normal controller transitions.");

            return false;
        }

        // Compatibility wrapper for code paths introduced by the immediately preceding implementation commit.
        internal static void ReconcileForcedEnd(BloodMoonEventState state) => ReconcileLoadedState(state);
    }

    /// <summary>
    /// A completely crossed Dormant event is catch-up work only when this running server actually observed
    /// the clock jump across that event window. Server downtime or a period with Blood Moon disabled must not
    /// retroactively replay a missed annual event on the first later tick.
    /// </summary>
    internal static class BloodMoonObservedServerClock
    {
        private static BloodMoonEventState observedState;
        private static long observedWorldUid;
        private static double lastObservedNow = double.NaN;
        private static double tickPreviousNow = double.NaN;
        private static BloodMoonEventState tickState;
        private static bool inTick;

        internal static void Begin(BloodMoonController controller, double now)
        {
            BloodMoonEventState state = controller?.State;
            long worldUid = state?.WorldUid ?? 0L;
            tickState = state;
            tickPreviousNow = ReferenceEquals(observedState, state) && observedWorldUid == worldUid ? lastObservedNow : double.NaN;
            inTick = true;
        }

        internal static void End(BloodMoonController controller, double now)
        {
            BloodMoonEventState state = controller?.State;
            observedState = state;
            observedWorldUid = state?.WorldUid ?? 0L;
            lastObservedNow = now;
            tickState = null;
            tickPreviousNow = double.NaN;
            inTick = false;
        }

        internal static bool IsObservedCrossing(BloodMoonScheduleSnapshot schedule, double now)
        {
            BloodMoonEventState state = BloodMoonController.Instance?.State;
            return inTick && schedule != null && ReferenceEquals(tickState, state) && !double.IsNaN(tickPreviousNow) &&
                tickPreviousNow < schedule.ForewarningAt && now >= schedule.MorningAt;
        }

        internal static void Reset()
        {
            observedState = null;
            observedWorldUid = 0L;
            lastObservedNow = double.NaN;
            tickState = null;
            tickPreviousNow = double.NaN;
            inTick = false;
        }
    }

    [HarmonyPatch(typeof(BloodMoonController), "TickServer")]
    internal static class BloodMoonObservedServerClockPatch
    {
        [HarmonyPriority(Priority.First + 500)]
        private static void Prefix(BloodMoonController __instance, double now)
        {
            BloodMoonObservedServerClock.Begin(__instance, now);
        }

        [HarmonyPriority(Priority.Last - 500)]
        private static void Postfix(BloodMoonController __instance, double now)
        {
            BloodMoonObservedServerClock.End(__instance, now);
        }

        private static System.Exception Finalizer(System.Exception __exception, BloodMoonController __instance, double now)
        {
            BloodMoonObservedServerClock.End(__instance, now);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(BloodMoonRound7Conformance), "FindMostRecentCrossedSchedule")]
    internal static class BloodMoonObservedCrossedScheduleGuardPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(double now, ref BloodMoonScheduleSnapshot __result)
        {
            if (__result != null && !BloodMoonObservedServerClock.IsObservedCrossing(__result, now))
                __result = null;
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    internal static class BloodMoonObservedServerClockResetPatch
    {
        private static void Prefix() => BloodMoonObservedServerClock.Reset();
    }
}
