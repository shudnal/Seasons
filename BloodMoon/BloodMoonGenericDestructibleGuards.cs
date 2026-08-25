using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Seasons.BloodMoon
{
    [HarmonyPatch]
    internal static class BloodMoonGenericWorldDestructibleDamagePatch
    {
        private static readonly HashSet<Type> ExistingCoveredTypes = new HashSet<Type>
        {
            typeof(WearNTear), typeof(Destructible), typeof(MineRock), typeof(MineRock5),
            typeof(TreeBase), typeof(TreeLog), typeof(HitArea), typeof(Raven)
        };

        private static IEnumerable<MethodBase> TargetMethods()
        {
            return BloodMoonDestructibleTargets.EnumerateWorldDamageMethods()
                .Where(method => method?.DeclaringType != null && !ExistingCoveredTypes.Contains(method.DeclaringType));
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(HitData hit)
        {
            if (!BloodMoonInteractionRules.IsEventCombatLive || hit == null)
                return true;

            Character attacker = BloodMoonAttackContext.Attacker ?? hit.GetAttacker();
            if (BloodMoonAttackContext.Attribution != null)
                return false;
            return !BloodMoonInteractionRules.IsParticipantCombatSource(attacker) && !BloodMoonInteractionRules.IsBloodEnemy(attacker);
        }
    }

    [HarmonyPatch]
    internal static class BloodMoonGenericStaleProjectileDamageGuardPatch
    {
        private static readonly HashSet<Type> ExistingCoveredTypes = new HashSet<Type>
        {
            typeof(WearNTear), typeof(Destructible), typeof(MineRock), typeof(MineRock5),
            typeof(TreeBase), typeof(TreeLog), typeof(HitArea), typeof(Raven)
        };

        private static IEnumerable<MethodBase> TargetMethods()
        {
            return BloodMoonDestructibleTargets.EnumerateWorldDamageMethods()
                .Where(method => method?.DeclaringType != null && !ExistingCoveredTypes.Contains(method.DeclaringType));
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            return !BloodMoonStaleAttribution.BlockingProjectileDamage;
        }
    }
}
