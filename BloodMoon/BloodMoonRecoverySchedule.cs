using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonRecoverySchedule
    {
        internal static void ReconcileForcedEnd(BloodMoonEventState state)
        {
            if (state == null || state.Phase == BloodMoonEventPhase.Resolving || state.Phase == BloodMoonEventPhase.Resolved ||
                !state.IsEventLive || state.Schedule == null || !state.Schedule.IsValid || !SeasonState.IsActive)
                return;

            double now = seasonState.GetTotalSeconds();
            if (now < state.Schedule.ForcedEndAt)
                return;

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
            state.UpdatedAt = now;
            state.Revision++;
            LogWarning($"[BloodMoon][event:{state.EventId}][resolution] Recovery crossed frozen forced end; entering Resolving before first publish.");
        }
    }
}
