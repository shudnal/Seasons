using HarmonyLib;
using System.Linq;
using UnityEngine;

namespace Seasons.BloodMoon
{
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.FindEnemy))]
    internal static class BloodMoonBaseAiFindEnemyPatch
    {
        private static bool Prefix(BaseAI __instance, ref Character __result)
        {
            if (!BloodMoonInteractionRules.IsBloodEnemy(__instance.m_character))
                return true;

            Player target = BloodMoonInteractionRules.GetLoadedActiveParticipants(preferFighting: true)
                .Where(player => BaseAI.IsEnemy(__instance.m_character, player))
                .OrderBy(player => Utils.DistanceXZ(__instance.transform.position, player.transform.position))
                .FirstOrDefault();
            __result = target;
            return false;
        }
    }

    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsAlerted))]
    internal static class BloodMoonBaseAiAlertedPatch
    {
        private static void Postfix(BaseAI __instance, ref bool __result)
        {
            if (BloodMoonInteractionRules.IsBloodEnemy(__instance.m_character))
                __result = true;
        }
    }

    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.HuntPlayer))]
    internal static class BloodMoonMonsterAiHuntPlayerPatch
    {
        private static void Postfix(MonsterAI __instance, ref bool __result)
        {
            if (BloodMoonInteractionRules.IsBloodEnemy(__instance.m_character) && BloodMoonInteractionRules.GetLoadedActiveParticipants(preferFighting: false).Count > 0)
                __result = true;
        }
    }

    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateTarget))]
    internal static class BloodMoonMonsterAiUpdateTargetPatch
    {
        private static void Prefix(MonsterAI __instance)
        {
            if (!BloodMoonInteractionRules.IsBloodEnemy(__instance.m_character))
                return;
            __instance.m_targetStatic = null;
            if (__instance.m_targetCreature is Player player && !BloodMoonInteractionRules.IsActiveParticipant(player.GetPlayerID()))
                __instance.m_targetCreature = null;
        }

        private static void Postfix(MonsterAI __instance)
        {
            if (!BloodMoonInteractionRules.IsBloodEnemy(__instance.m_character))
                return;

            __instance.m_targetStatic = null;
            if (__instance.m_targetCreature is Player current && BloodMoonInteractionRules.IsActiveParticipant(current.GetPlayerID()))
                return;

            Player target = BloodMoonInteractionRules.GetLoadedActiveParticipants(preferFighting: true)
                .Where(player => BaseAI.IsEnemy(__instance.m_character, player))
                .OrderBy(player => Utils.DistanceXZ(__instance.transform.position, player.transform.position))
                .FirstOrDefault();
            __instance.m_targetCreature = target;
        }
    }

    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.PheromoneFleeCheck))]
    internal static class BloodMoonMonsterAiPheromoneFleePatch
    {
        private static void Postfix(MonsterAI __instance, ref bool __result)
        {
            if (BloodMoonInteractionRules.IsBloodEnemy(__instance.m_character))
                __result = false;
        }
    }
}
