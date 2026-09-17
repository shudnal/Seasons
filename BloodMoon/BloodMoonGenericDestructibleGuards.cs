using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Seasons.BloodMoon
{
    /// <summary>
    /// Additional non-vanilla IDestructible implementations that are not
    /// already covered by explicit Blood Moon patches.
    /// </summary>
    internal static class BloodMoonGenericDestructiblePatchTargets
    {
        private static readonly HashSet<Type> ExistingCoveredTypes = new HashSet<Type>
        {
            typeof(WearNTear),
            typeof(Destructible),
            typeof(MineRock),
            typeof(MineRock5),
            typeof(TreeBase),
            typeof(TreeLog),
            typeof(HitArea),
            typeof(Raven)
        };

        internal static readonly MethodBase[] Methods =
            BloodMoonDestructibleTargets
                .EnumerateWorldDamageMethods()
                .Where(method =>
                    method?.DeclaringType != null &&
                    !ExistingCoveredTypes.Contains(method.DeclaringType))
                .Distinct()
                .ToArray();
    }

    [HarmonyPatch]
    internal static class BloodMoonGenericWorldDestructibleDamagePatch
    {
        /// <summary>
        /// Harmony considers a patch class without resolved originals invalid.
        /// Skip the optional compatibility patch when no additional
        /// IDestructible implementations are loaded.
        /// </summary>
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return BloodMoonGenericDestructiblePatchTargets.Methods.Length > 0;
        }

        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return BloodMoonGenericDestructiblePatchTargets.Methods;
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(HitData hit)
        {
            if (!BloodMoonInteractionRules.IsEventCombatLive || hit == null)
                return true;

            Character attacker =
                BloodMoonAttackContext.Attacker ?? hit.GetAttacker();

            if (BloodMoonAttackContext.Attribution != null)
                return false;

            return
                !BloodMoonInteractionRules.IsParticipantCombatSource(attacker) &&
                !BloodMoonInteractionRules.IsBloodEnemy(attacker);
        }
    }

    [HarmonyPatch]
    internal static class BloodMoonGenericStaleProjectileDamageGuardPatch
    {
        /// <summary>
        /// This is also an optional compatibility patch and may legitimately
        /// have no targets in a vanilla-only setup.
        /// </summary>
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return BloodMoonGenericDestructiblePatchTargets.Methods.Length > 0;
        }

        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return BloodMoonGenericDestructiblePatchTargets.Methods;
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            // BloodMoonStaleProjectileGuardPatch owns this invocation-scoped flag while
            // vanilla Projectile.OnHit is allowed to finish its physical collision lifecycle.
            // Generic modded IDestructible targets must obey the same stale-damage block as
            // the explicitly patched vanilla destructibles.
            return !BloodMoonStaleAttribution.BlockingProjectileDamage;
        }
    }
}