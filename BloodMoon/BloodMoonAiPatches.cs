using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonAiRuntimeContext
    {
        [System.ThreadStatic]
        internal static int BloodAiDepth;

        internal static bool IgnoreNoMonsterAreas => BloodAiDepth > 0;
    }

    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.FindEnemy))]
    internal static class BloodMoonBaseAiFindEnemyPatch
    {
        private static bool Prefix(BaseAI __instance, ref Character __result)
        {
            if (!BloodMoonInteractionRules.IsBloodEnemy(__instance.m_character))
                return true;

            float huntRange = Mathf.Max(0f, BloodMoonConfig.EnemyHuntRange.Value);
            Player target = BloodMoonInteractionRules.GetLoadedActiveParticipants(preferFighting: true)
                .Where(player => BaseAI.IsEnemy(__instance.m_character, player))
                .Where(player => Utils.DistanceXZ(__instance.transform.position, player.transform.position) <= huntRange)
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

    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.SetAlerted))]
    internal static class BloodMoonBaseAiSetAlertedPatch
    {
        private static bool Prefix(BaseAI __instance)
        {
            // Blood alert state is virtual. Do not persist s_alert on existing or extra enemies.
            return !BloodMoonInteractionRules.IsBloodEnemy(__instance.m_character);
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

    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.DespawnInDay))]
    internal static class BloodMoonMonsterAiDespawnInDayPatch
    {
        private static void Postfix(MonsterAI __instance, ref bool __result)
        {
            if (BloodMoonInteractionRules.IsBloodEnemy(__instance.m_character))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.IsEventCreature))]
    internal static class BloodMoonMonsterAiEventCreaturePatch
    {
        private static void Postfix(MonsterAI __instance, ref bool __result)
        {
            if (BloodMoonInteractionRules.IsBloodEnemy(__instance.m_character))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetRunSpeedFactor))]
    internal static class BloodMoonCharacterRunSpeedPatch
    {
        private static void Postfix(Character __instance, ref float __result)
        {
            if (BloodMoonInteractionRules.IsBloodEnemy(__instance))
                __result *= Mathf.Max(0f, BloodMoonConfig.EnemyMovementSpeedMultiplier.Value);
        }
    }

    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateTarget))]
    internal static class BloodMoonMonsterAiUpdateTargetPatch
    {
        private sealed class TargetState
        {
            internal bool Active;
            internal bool Restored;
            internal bool AttackPlayerObjects;
        }

        private static void Prefix(MonsterAI __instance, ref float dt, out TargetState __state)
        {
            __state = new TargetState();
            if (!BloodMoonInteractionRules.IsBloodEnemy(__instance.m_character))
                return;

            __state.Active = true;
            __state.AttackPlayerObjects = __instance.m_attackPlayerObjects;
            __instance.m_attackPlayerObjects = false;
            __instance.m_targetStatic = null;

            float intervalMultiplier = Mathf.Max(0.05f, BloodMoonConfig.EnemyTargetUpdateIntervalMultiplier.Value);
            dt /= intervalMultiplier;

            if (__instance.m_targetCreature != null && !BloodMoonInteractionRules.CanTarget(__instance.m_character, __instance.m_targetCreature))
                __instance.m_targetCreature = null;
        }

        private static void Postfix(MonsterAI __instance, TargetState __state)
        {
            if (__state == null || !__state.Active)
                return;

            Restore(__instance, __state);
            __instance.m_targetStatic = null;

            if (__instance.m_targetCreature is Player current && BloodMoonInteractionRules.IsActiveParticipant(current))
            {
                __instance.m_lastKnownTargetPos = current.transform.position;
                return;
            }

            float huntRange = Mathf.Max(0f, BloodMoonConfig.EnemyHuntRange.Value);
            Player target = BloodMoonInteractionRules.GetLoadedActiveParticipants(preferFighting: true)
                .Where(player => BaseAI.IsEnemy(__instance.m_character, player))
                .Where(player => Utils.DistanceXZ(__instance.transform.position, player.transform.position) <= huntRange)
                .OrderBy(player => Utils.DistanceXZ(__instance.transform.position, player.transform.position))
                .FirstOrDefault();
            __instance.m_targetCreature = target;
            if (target != null)
            {
                __instance.m_lastKnownTargetPos = target.transform.position;
                __instance.m_beenAtLastPos = false;
                __instance.m_timeSinceSensedTargetCreature = 0f;
            }
        }

        private static System.Exception Finalizer(System.Exception __exception, MonsterAI __instance, TargetState __state)
        {
            Restore(__instance, __state);
            return __exception;
        }

        private static void Restore(MonsterAI ai, TargetState state)
        {
            if (ai == null || state == null || !state.Active || state.Restored)
                return;
            state.Restored = true;
            ai.m_attackPlayerObjects = state.AttackPlayerObjects;
        }
    }

    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
    internal static class BloodMoonMonsterAiUpdateAiPatch
    {
        private sealed class RuntimeState
        {
            internal bool Active;
            internal bool Restored;
            internal bool FleeIfHurtWhenTargetCantBeReached;
            internal bool FleeIfNotAlerted;
            internal float FleeIfLowHealth;
            internal bool AvoidFire;
            internal bool AfraidOfFire;
            internal List<ItemDrop> ConsumeItems;
            internal float MaxChaseDistance;
        }

        private static void Prefix(MonsterAI __instance, out RuntimeState __state)
        {
            __state = new RuntimeState();
            if (!BloodMoonInteractionRules.IsBloodEnemy(__instance.m_character))
                return;

            __state.Active = true;
            __state.FleeIfHurtWhenTargetCantBeReached = __instance.m_fleeIfHurtWhenTargetCantBeReached;
            __state.FleeIfNotAlerted = __instance.m_fleeIfNotAlerted;
            __state.FleeIfLowHealth = __instance.m_fleeIfLowHealth;
            __state.AvoidFire = __instance.m_avoidFire;
            __state.AfraidOfFire = __instance.m_afraidOfFire;
            __state.ConsumeItems = __instance.m_consumeItems;
            __state.MaxChaseDistance = __instance.m_maxChaseDistance;

            __instance.m_fleeIfHurtWhenTargetCantBeReached = false;
            __instance.m_fleeIfNotAlerted = false;
            __instance.m_fleeIfLowHealth = 0f;
            __instance.m_avoidFire = false;
            __instance.m_afraidOfFire = false;
            __instance.m_consumeItems = null;
            __instance.m_maxChaseDistance = 0f;
            __instance.m_targetStatic = null;
            BloodMoonAiRuntimeContext.BloodAiDepth++;
        }

        private static void Postfix(MonsterAI __instance, RuntimeState __state)
        {
            Restore(__instance, __state);
            if (__state != null && __state.Active)
                __instance.m_targetStatic = null;
        }

        private static System.Exception Finalizer(System.Exception __exception, RuntimeState __state, MonsterAI __instance)
        {
            Restore(__instance, __state);
            return __exception;
        }

        private static void Restore(MonsterAI ai, RuntimeState state)
        {
            if (ai == null || state == null || !state.Active || state.Restored)
                return;
            state.Restored = true;
            BloodMoonAiRuntimeContext.BloodAiDepth = Mathf.Max(0, BloodMoonAiRuntimeContext.BloodAiDepth - 1);
            ai.m_fleeIfHurtWhenTargetCantBeReached = state.FleeIfHurtWhenTargetCantBeReached;
            ai.m_fleeIfNotAlerted = state.FleeIfNotAlerted;
            ai.m_fleeIfLowHealth = state.FleeIfLowHealth;
            ai.m_avoidFire = state.AvoidFire;
            ai.m_afraidOfFire = state.AfraidOfFire;
            ai.m_consumeItems = state.ConsumeItems;
            ai.m_maxChaseDistance = state.MaxChaseDistance;
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

    [HarmonyPatch(typeof(EffectArea), nameof(EffectArea.IsPointInsideNoMonsterArea))]
    internal static class BloodMoonNoMonsterAreaInsidePatch
    {
        private static void Postfix(ref EffectArea __result)
        {
            if (BloodMoonAiRuntimeContext.IgnoreNoMonsterAreas)
                __result = null;
        }
    }

    [HarmonyPatch(typeof(EffectArea), nameof(EffectArea.IsPointCloseToNoMonsterArea))]
    internal static class BloodMoonNoMonsterAreaClosePatch
    {
        private static void Postfix(ref EffectArea __result)
        {
            if (BloodMoonAiRuntimeContext.IgnoreNoMonsterAreas)
                __result = null;
        }
    }
}
