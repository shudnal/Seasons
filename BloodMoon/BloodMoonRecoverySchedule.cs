using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonRecoverySchedule
    {
        internal static void ReconcileLoadedState(BloodMoonEventState state)
        {
            if (state == null || state.Phase == BloodMoonEventPhase.Resolving || state.Phase == BloodMoonEventPhase.Resolved ||
                !state.IsEventLive || state.Schedule == null || !state.Schedule.IsValid || !SeasonState.IsActive)
                return;

            double now = seasonState.GetTotalSeconds();
            BloodMoonEventPhase expected = BloodMoonSchedule.GetExpectedPhase(state.Schedule, now);
            bool changed = false;

            if (expected == BloodMoonEventPhase.Resolving || expected == BloodMoonEventPhase.Resolved)
            {
                foreach (BloodMoonParticipantState participant in state.Participants.Values)
                {
                    if (!participant.IsCombatActive || participant.GoalReached)
                        continue;
                    participant.AutoCompleted = true;
                    participant.DisplayProgress = 100f;
                }

                state.Phase = BloodMoonEventPhase.Resolving;
                state.ResolutionStep = BloodMoonResolutionStep.FreezingEnrollment;
                state.EnrollmentFrozen = true;
                changed = true;
                LogWarning($"[BloodMoon][event:{state.EventId}][resolution] Recovery crossed frozen forced end; entering Resolving before first publish.");
            }
            else if (expected == BloodMoonEventPhase.AutoCompleting &&
                (state.Phase == BloodMoonEventPhase.Forewarning || state.Phase == BloodMoonEventPhase.Marked || state.Phase == BloodMoonEventPhase.Active))
            {
                EnterRecoveredCombatPhase(state, BloodMoonEventPhase.AutoCompleting, now);
                changed = true;
            }
            else if (expected == BloodMoonEventPhase.Active &&
                (state.Phase == BloodMoonEventPhase.Forewarning || state.Phase == BloodMoonEventPhase.Marked))
            {
                EnterRecoveredCombatPhase(state, BloodMoonEventPhase.Active, now);
                changed = true;
            }
            else if (expected == BloodMoonEventPhase.Marked && state.Phase == BloodMoonEventPhase.Forewarning)
            {
                state.Phase = BloodMoonEventPhase.Marked;
                state.EnrollmentFrozen = false;
                state.BloodBehaviorEnabled = false;
                state.SpawnsStopped = false;
                changed = true;
            }

            if (!changed)
                return;

            state.UpdatedAt = now;
            state.Revision++;
            LogInfo($"[BloodMoon][event:{state.EventId}][phase] Recovered frozen schedule to {state.Phase} before first publish.");
        }

        // Compatibility wrapper for state files loaded by the immediately preceding implementation commit.
        internal static void ReconcileForcedEnd(BloodMoonEventState state) => ReconcileLoadedState(state);

        private static void EnterRecoveredCombatPhase(BloodMoonEventState state, BloodMoonEventPhase phase, double now)
        {
            state.Phase = phase;
            state.EnrollmentFrozen = false;
            state.BloodBehaviorEnabled = true;
            state.SpawnsStopped = false;
            foreach (BloodMoonParticipantState participant in state.Participants.Values)
            {
                if (participant.Phase != BloodMoonParticipantPhase.Marked)
                    continue;
                participant.Phase = BloodMoonParticipantPhase.Fighting;
                if (participant.FightingAt <= 0d)
                    participant.FightingAt = now;
            }
        }
    }
}
