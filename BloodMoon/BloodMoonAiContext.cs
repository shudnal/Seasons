using HarmonyLib;
using System.Linq;
using UnityEngine;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonAiContext
    {
        internal static bool SameNavigationContext(Character first, Character second)
        {
            if (first == null || second == null)
                return false;

            bool firstInterior = Character.InInterior(first.transform.position);
            bool secondInterior = Character.InInterior(second.transform.position);
            if (firstInterior != secondInterior)
                return false;
            if (!firstInterior)
                return true;

            Location firstLocation = Location.GetLocation(first.transform.position);
            Location secondLocation = Location.GetLocation(second.transform.position);
            if (firstLocation != null || secondLocation != null)
                return firstLocation != null && ReferenceEquals(firstLocation, secondLocation);

            return ZoneSystem.GetZone(first.transform.position) == ZoneSystem.GetZone(second.transform.position);
        }

        internal static Player FindParticipantTarget(Character enemy)
        {
            if (enemy == null)
                return null;

            float huntRange = Mathf.Max(0f, BloodMoonConfig.EnemyHuntRange.Value);
            return BloodMoonInteractionRules.GetLoadedActiveParticipants(preferFighting: false)
                .Where(player => SameNavigationContext(enemy, player))
                .Where(player => BaseAI.IsEnemy(enemy, player))
                .Where(player => Utils.DistanceXZ(enemy.transform.position, player.transform.position) <= huntRange)
                .OrderBy(player => BloodMoonInteractionRules.GetParticipant(player.GetPlayerID())?.GoalReached == true ? 1 : 0)
                .ThenBy(player => Utils.DistanceXZ(enemy.transform.position, player.transform.position))
                .FirstOrDefault();
        }

        internal static Character FindBloodEnemyTarget(BaseAI ai)
        {
            Character summon = ai?.m_character;
            if (summon == null)
                return null;

            return Character.GetAllCharacters()
                .Where(character => character != null && !character.IsDead() && !character.m_aiSkipTarget)
                .Where(BloodMoonInteractionRules.IsBloodEnemy)
                .Where(character => SameNavigationContext(summon, character))
                .Where(character => BaseAI.IsEnemy(summon, character) && ai.CanSenseTarget(character))
                .OrderBy(character => Utils.DistanceXZ(summon.transform.position, character.transform.position))
                .FirstOrDefault();
        }
    }

    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.FindEnemy))]
    internal static class BloodMoonBaseAiContextPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(BaseAI __instance, ref Character __result)
        {
            Character source = __instance?.m_character;
            bool bloodEnemy = BloodMoonInteractionRules.IsBloodEnemy(source);
            bool participantSummon = BloodMoonSummons.IsBloodSummon(source);
            if (!bloodEnemy && !participantSummon)
                return;

            if (__result != null && BloodMoonAiContext.SameNavigationContext(source, __result) &&
                (bloodEnemy ? BloodMoonInteractionRules.CanTarget(source, __result) : BloodMoonInteractionRules.IsBloodEnemy(__result)))
                return;

            __result = bloodEnemy
                ? BloodMoonAiContext.FindParticipantTarget(source)
                : BloodMoonAiContext.FindBloodEnemyTarget(__instance);
        }
    }

    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateTarget))]
    internal static class BloodMoonMonsterAiContextPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(MonsterAI __instance)
        {
            Character source = __instance?.m_character;
            bool bloodEnemy = BloodMoonInteractionRules.IsBloodEnemy(source);
            bool participantSummon = BloodMoonSummons.IsBloodSummon(source);
            if (!bloodEnemy && !participantSummon)
                return;

            bool currentValid = __instance.m_targetCreature != null && BloodMoonAiContext.SameNavigationContext(source, __instance.m_targetCreature) &&
                (bloodEnemy ? BloodMoonInteractionRules.CanTarget(source, __instance.m_targetCreature) : BloodMoonInteractionRules.IsBloodEnemy(__instance.m_targetCreature));
            if (currentValid)
                return;

            Character target = bloodEnemy
                ? BloodMoonAiContext.FindParticipantTarget(source)
                : BloodMoonAiContext.FindBloodEnemyTarget(__instance);
            __instance.m_targetCreature = target;
            __instance.m_targetStatic = null;
            if (target != null)
            {
                __instance.m_lastKnownTargetPos = target.transform.position;
                __instance.m_beenAtLastPos = false;
                __instance.m_timeSinceSensedTargetCreature = 0f;
            }
        }
    }
}
