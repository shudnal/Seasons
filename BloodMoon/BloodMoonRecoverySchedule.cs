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
}
