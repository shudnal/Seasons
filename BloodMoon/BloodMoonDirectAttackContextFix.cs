using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Seasons.BloodMoon
{
    [HarmonyPatch]
    internal static class BloodMoonDirectAttackContextFixPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            MethodInfo melee = AccessTools.Method(typeof(Attack), nameof(Attack.DoMeleeAttack));
            MethodInfo area = AccessTools.Method(typeof(Attack), nameof(Attack.DoAreaAttack));
            if (melee != null)
                yield return melee;
            if (area != null)
                yield return area;
        }

        [HarmonyPriority(Priority.First)]
        private static void Prefix(Attack __instance)
        {
            Character attacker = __instance?.m_character;
            if (!BloodMoonInteractionRules.IsEventCombatLive || attacker == null)
                return;
            if (BloodMoonInteractionRules.IsParticipantCombatSource(attacker) || BloodMoonInteractionRules.IsBloodEnemy(attacker))
                BloodMoonAttackContext.Begin(attacker);
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            if (BloodMoonAttackContext.Attribution == null)
                BloodMoonAttackContext.End();
        }

        private static Exception Finalizer(Exception __exception)
        {
            if (BloodMoonAttackContext.Attribution == null)
                BloodMoonAttackContext.End();
            return __exception;
        }
    }
}
