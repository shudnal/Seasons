using HarmonyLib;

namespace Seasons.BloodMoon
{
    /// <summary>
    /// The server ACK is a contiguous durable high-water mark, not merely the largest sequence seen.
    /// Out-of-order reports stay in the client's profile-backed pending queue until every earlier sequence
    /// has been accepted. This keeps completion contribution and live-bonus accounting intact under normal
    /// routed-RPC reordering and reconnect delivery.
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

            // A supported client only creates reports for skills that were eligible when the raise occurred.
            // If synchronized configuration changes while a durable report is pending, consume that exact
            // sequence without contribution rather than permanently wedging every later report behind it.
            participant.LastSkillReportSequence = sequence;
            return false;
        }
    }
}
