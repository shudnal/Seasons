using HarmonyLib;
using System.Collections.Generic;
using System.Linq;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonSkillDurableBaseline
    {
        private readonly struct Key
        {
            internal readonly long EventId;
            internal readonly long PlayerId;

            internal Key(long eventId, long playerId)
            {
                EventId = eventId;
                PlayerId = playerId;
            }

            public override bool Equals(object obj) => obj is Key other && other.EventId == EventId && other.PlayerId == PlayerId;
            public override int GetHashCode() => EventId.GetHashCode() * 397 ^ PlayerId.GetHashCode();
        }

        private sealed class Baseline
        {
            internal long Sequence;
            internal float LiveBonusUsed;
        }

        private static readonly Dictionary<Key, Baseline> durable = new Dictionary<Key, Baseline>();

        internal static void Capture(BloodMoonEventState state)
        {
            if (state == null || state.EventId < 0L || state.Participants == null)
                return;

            foreach (BloodMoonParticipantState participant in state.Participants.Values)
            {
                if (participant == null || participant.PlayerId == 0L)
                    continue;
                durable[new Key(state.EventId, participant.PlayerId)] = new Baseline
                {
                    Sequence = System.Math.Max(0L, participant.LastSkillReportSequence),
                    LiveBonusUsed = participant.LiveSkillBonusEquivalent?.Values.Sum() ?? 0f
                };
            }
        }

        internal static void Clamp(long eventId, long playerId, BloodMoonParticipantDetailSnapshot snapshot)
        {
            if (snapshot == null || eventId < 0L || playerId == 0L)
                return;

            if (durable.TryGetValue(new Key(eventId, playerId), out Baseline baseline))
            {
                snapshot.LastSkillReportSequence = baseline.Sequence;
                snapshot.LiveSkillBonusUsed = baseline.LiveBonusUsed;
                return;
            }

            // No successful persistence of this event/player has been observed in this process. Never
            // advertise an in-memory skill high-water as an ACK-equivalent private baseline.
            snapshot.LastSkillReportSequence = 0L;
            snapshot.LiveSkillBonusUsed = 0f;
        }

        internal static void Reset() => durable.Clear();
    }

    [HarmonyPatch(typeof(BloodMoonNetwork), "CreateDetailSnapshot")]
    internal static class BloodMoonResyncSkillBaselinePatch
    {
        private static void Postfix(BloodMoonParticipantState participant, ref BloodMoonParticipantDetailSnapshot __result)
        {
            if (participant == null || __result == null)
                return;
            BloodMoonSkillDurableBaseline.Clamp(__result.EventId, participant.PlayerId, __result);
        }
    }

    [HarmonyPatch(typeof(BloodMoonParticipantDetails), nameof(BloodMoonParticipantDetails.Serialize))]
    internal static class BloodMoonPublishedSkillBaselinePatch
    {
        private static void Postfix(BloodMoonEventState state, BloodMoonParticipantState participant, ref string __result)
        {
            if (state == null || participant == null || string.IsNullOrEmpty(__result))
                return;
            try
            {
                BloodMoonParticipantDetailSnapshot snapshot = JsonConvert.DeserializeObject<BloodMoonParticipantDetailSnapshot>(__result);
                if (snapshot == null)
                    return;
                BloodMoonSkillDurableBaseline.Clamp(state.EventId, participant.PlayerId, snapshot);
                __result = JsonConvert.SerializeObject(snapshot);
            }
            catch
            {
                // Leave the normal serializer error path responsible for diagnostics.
            }
        }
    }

    [HarmonyPatch(typeof(BloodMoonPersistence), nameof(BloodMoonPersistence.Save))]
    internal static class BloodMoonDurableSkillSavePatch
    {
        private static void Postfix(BloodMoonEventState state, bool __result)
        {
            if (__result)
                BloodMoonSkillDurableBaseline.Capture(state);
        }
    }

    [HarmonyPatch(typeof(BloodMoonPersistence), nameof(BloodMoonPersistence.Load))]
    internal static class BloodMoonDurableSkillLoadPatch
    {
        private static void Postfix(ref BloodMoonEventState __result)
        {
            BloodMoonSkillDurableBaseline.Capture(__result);
        }
    }

    /// <summary>
    /// SkillGainAck is a contiguous durable high-water mark. Out-of-order reports stay pending until
    /// every preceding sequence has been accepted or deliberately consumed.
    /// </summary>
    [HarmonyPatch(typeof(BloodMoonSkills), nameof(BloodMoonSkills.AcceptServerReport))]
    internal static class BloodMoonSkillReportContiguousSequencePatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BloodMoonParticipantState participant, long sequence, Skills.SkillType skill)
        {
            if (participant == null || sequence <= participant.LastSkillReportSequence)
                return false;

            long expected = participant.LastSkillReportSequence + 1L;
            if (sequence != expected)
                return false;

            if (BloodMoonSkills.IsEligible(skill))
                return true;

            // A supported client created the report while this skill was eligible. If synchronized
            // configuration changed while it was pending, consume this exact sequence without contribution
            // rather than permanently wedging all later durable reports behind it.
            participant.LastSkillReportSequence = sequence;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    internal static class BloodMoonDurableSkillWorldResetPatch
    {
        private static void Prefix() => BloodMoonSkillDurableBaseline.Reset();
    }
}
