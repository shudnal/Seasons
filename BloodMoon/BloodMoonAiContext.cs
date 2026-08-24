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
    }

    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.FindEnemy))]
    internal static class BloodMoonBaseAiContextPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(BaseAI __instance, ref Character __result)
        {
            Character enemy = __instance?.m_character;
            if (!BloodMoonInteractionRules.IsBloodEnemy(enemy))
                return;

            if (__result != null && BloodMoonAiContext.SameNavigationContext(enemy, __result))
                return;

            __result = BloodMoonAiContext.FindParticipantTarget(enemy);
        }
    }

    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateTarget))]
    internal static class BloodMoonMonsterAiContextPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(MonsterAI __instance)
        {
            Character enemy = __instance?.m_character;
            if (!BloodMoonInteractionRules.IsBloodEnemy(enemy))
                return;

            if (__instance.m_targetCreature != null && BloodMoonAiContext.SameNavigationContext(enemy, __instance.m_targetCreature) &&
                BloodMoonInteractionRules.CanTarget(enemy, __instance.m_targetCreature))
                return;

            Player target = BloodMoonAiContext.FindParticipantTarget(enemy);
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
