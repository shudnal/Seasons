using HarmonyLib;
using System.Linq;

namespace Seasons.BloodMoon
{
    [HarmonyPatch(typeof(BloodMoonNetwork), "CreateDetailSnapshot")]
    internal static class BloodMoonResyncSkillBaselinePatch
    {
        private static void Postfix(BloodMoonParticipantState participant, ref BloodMoonParticipantDetailSnapshot __result)
        {
            if (participant == null || __result == null)
                return;

            __result.LiveSkillBonusUsed = participant.LiveSkillBonusEquivalent?.Values.Sum() ?? 0f;
            __result.LastSkillReportSequence = participant.LastSkillReportSequence;
        }
    }
}
