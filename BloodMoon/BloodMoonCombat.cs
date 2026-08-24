using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonCombat
    {
        private static readonly Dictionary<ZDOID, long> lastCreditedPlayer = new Dictionary<ZDOID, long>();
        private static readonly HashSet<ZDOID> locallyReportedDeaths = new HashSet<ZDOID>();

        internal static bool CanDirectDamage(Character attacker, Character target, HitData hit, out float multiplier)
        {
            multiplier = 1f;
            if (!BloodMoonInteractionRules.IsEventCombatLive || target == null)
                return true;
            if (!BloodMoonInteractionRules.CanDamage(attacker, target, hit))
                return false;

            if (attacker is Player player && BloodMoonInteractionRules.IsActiveParticipant(player) && BloodMoonInteractionRules.IsBloodEnemy(target))
                multiplier = Mathf.Max(0f, BloodMoonConfig.EnemyIncomingDamageMultiplier.Value);
            else if (BloodMoonInteractionRules.IsBloodEnemy(attacker) && target is Player targetPlayer && BloodMoonInteractionRules.IsActiveParticipant(targetPlayer))
                multiplier = Mathf.Max(0f, BloodMoonConfig.EnemyOutgoingDamageMultiplier.Value);
            return true;
        }

        internal static bool CanAttributedDamage(BloodMoonHitAttributionData attribution, Character target, out float multiplier)
        {
            multiplier = 1f;
            if (!BloodMoonHitAttribution.CanDamage(attribution, target))
                return false;

            if (attribution.SourceType == BloodMoonCombatSourceType.Participant)
                multiplier = Mathf.Max(0f, BloodMoonConfig.EnemyIncomingDamageMultiplier.Value);
            else if (attribution.SourceType == BloodMoonCombatSourceType.BloodEnemy && target is Player)
                multiplier = Mathf.Max(0f, BloodMoonConfig.EnemyOutgoingDamageMultiplier.Value);
            return true;
        }

        internal static void RecordCreditedHit(Character target, Character attacker, BloodMoonHitAttributionData attribution = null)
        {
            if (target == null || target.m_nview == null || !target.m_nview.IsValid() || !BloodMoonInteractionRules.IsBloodEnemy(target))
                return;

            long playerId = 0L;
            if (attribution != null)
            {
                if (BloodMoonHitAttribution.CanCredit(attribution))
                    playerId = attribution.SourcePlayerId;
            }
            else if (attacker is Player player && BloodMoonInteractionRules.CanCreditProgress(player.GetPlayerID()))
            {
                playerId = player.GetPlayerID();
            }

            if (playerId != 0L)
                lastCreditedPlayer[target.GetZDOID()] = playerId;
        }

        internal static bool TryGetCreditedPlayer(Character target, out long playerId)
        {
            playerId = 0L;
            return target != null && lastCreditedPlayer.TryGetValue(target.GetZDOID(), out playerId) && BloodMoonInteractionRules.CanCreditProgress(playerId);
        }

        internal static float GetPointsForEnemy(ZDOID enemyId, float ignoredClientValue)
        {
            ZDO zdo = ZDOMan.instance?.GetZDO(enemyId);
            if (zdo == null)
                return 0f;
            int level = Mathf.Max(1, zdo.GetInt(ZDOVars.s_level, 1));
            return 10f * level;
        }

        internal static bool TryMarkDeathReported(Character character)
        {
            return character != null && locallyReportedDeaths.Add(character.GetZDOID());
        }

        internal static void CleanupDeathTracking(Character character)
        {
            if (character == null)
                return;
            ZDOID id = character.GetZDOID();
            lastCreditedPlayer.Remove(id);
            locallyReportedDeaths.Remove(id);
        }
    }

    internal static class BloodMoonAttackContext
    {
        [System.ThreadStatic]
        internal static Character Attacker;
        [System.ThreadStatic]
        internal static bool AllowedCharacterHit;
        [System.ThreadStatic]
        internal static BloodMoonHitAttributionData Attribution;

        internal static bool IsActive => Attacker != null || Attribution != null;
        internal static bool IsParticipantSource => Attacker is Player player && BloodMoonInteractionRules.IsActiveParticipant(player) || Attribution?.SourceType == BloodMoonCombatSourceType.Participant;
        internal static bool CanCredit => Attribution == null ? IsParticipantSource && AllowedCharacterHit : BloodMoonHitAttribution.CanCredit(Attribution) && AllowedCharacterHit;

        internal static void Begin(Character attacker)
        {
            Attacker = attacker;
            Attribution = null;
            AllowedCharacterHit = false;
        }

        internal static void Begin(BloodMoonHitAttributionData attribution)
        {
            Attacker = null;
            Attribution = attribution;
            AllowedCharacterHit = false;
        }

        internal static void End()
        {
            Attacker = null;
            Attribution = null;
            AllowedCharacterHit = false;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
    internal static class BloodMoonCharacterRpcDamagePatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Character __instance, HitData hit)
        {
            if (hit == null || !__instance.IsOwner())
                return true;

            if (__instance is Player protectedPlayer && !BloodMoonRecovery.ApplyIncomingDamageProtection(protectedPlayer, hit))
            {
                if (BloodMoonConfig.LogHits.Value)
                    Seasons.LogInfo($"[BloodMoon][recovery][player:{protectedPlayer.GetPlayerID()}] blocked incoming damage.");
                return false;
            }

            BloodMoonHitAttributionData attribution = null;
            bool attributed = BloodMoonHitAttribution.TryConsume(__instance, hit, out attribution);
            Character attacker = hit.GetAttacker();
            bool allowed = attributed
                ? BloodMoonCombat.CanAttributedDamage(attribution, __instance, out float attributedMultiplier)
                : BloodMoonCombat.CanDirectDamage(attacker, __instance, hit, out attributedMultiplier);

            if (!allowed)
            {
                if (BloodMoonConfig.LogHits.Value)
                    Seasons.LogInfo($"[BloodMoon][event:{BloodMoonNetwork.ClientGlobal.EventId}][hit] rejected attacker={hit.m_attacker} target={__instance.GetZDOID()} attributed={attributed}.");
                return false;
            }

            if (!Mathf.Approximately(attributedMultiplier, 1f))
                hit.ApplyModifier(attributedMultiplier);
            BloodMoonCombat.RecordCreditedHit(__instance, attacker, attribution);
            return true;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.CheckDeath))]
    internal static class BloodMoonCharacterCheckDeathPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Character __instance)
        {
            if (__instance.IsDead() || __instance.GetHealth() > 0f || !__instance.IsOwner())
                return true;

            if (__instance is Player player && player == Player.m_localPlayer && BloodMoonInteractionRules.IsActiveParticipant(player))
            {
                if (BloodMoonRecovery.TryInterceptDefeat(player))
                {
                    BloodMoonNetwork.SendDefeated(BloodMoonNetwork.ClientGlobal.EventId, player.GetPlayerID());
                    return false;
                }
            }

            if (!BloodMoonInteractionRules.IsBloodEnemy(__instance) || !BloodMoonCombat.TryMarkDeathReported(__instance))
                return true;
            if (BloodMoonCombat.TryGetCreditedPlayer(__instance, out long playerId))
                BloodMoonNetwork.SendEnemyDeath(BloodMoonNetwork.ClientGlobal.EventId, __instance.GetZDOID(), playerId, 0f);
            return true;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.OnDestroy))]
    internal static class BloodMoonCharacterDestroyPatch
    {
        private static void Prefix(Character __instance) => BloodMoonCombat.CleanupDeathTracking(__instance);
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.DoMeleeAttack))]
    [HarmonyPatch(typeof(Attack), nameof(Attack.DoAreaAttack))]
    internal static class BloodMoonDirectAttackContextPatch
    {
        private static void Prefix(Attack __instance)
        {
            Character attacker = __instance.m_character;
            if (BloodMoonInteractionRules.IsEventCombatLive && (attacker is Player player && BloodMoonInteractionRules.IsActiveParticipant(player) || BloodMoonInteractionRules.IsBloodEnemy(attacker)))
                BloodMoonAttackContext.Begin(attacker);
        }

        private static void Postfix()
        {
            if (BloodMoonAttackContext.Attribution == null)
                BloodMoonAttackContext.End();
        }

        private static System.Exception Finalizer(System.Exception __exception)
        {
            if (BloodMoonAttackContext.Attribution == null)
                BloodMoonAttackContext.End();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class BloodMoonEarlyCharacterDamagePatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Character __instance, HitData hit)
        {
            if (!BloodMoonAttackContext.IsActive)
                return true;

            BloodMoonHitAttributionData attribution = BloodMoonAttackContext.Attribution;
            bool allowed = attribution != null
                ? BloodMoonHitAttribution.CanDamage(attribution, __instance)
                : BloodMoonInteractionRules.CanDamage(BloodMoonAttackContext.Attacker, __instance, hit);
            if (!allowed)
                return false;

            if (attribution != null)
                BloodMoonHitAttribution.QueueForTarget(attribution, __instance);
            if (BloodMoonInteractionRules.IsBloodEnemy(__instance))
                BloodMoonAttackContext.AllowedCharacterHit = true;
            return true;
        }
    }

    [HarmonyPatch]
    internal static class BloodMoonWorldDestructibleDamagePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type[] types = { typeof(WearNTear), typeof(Destructible), typeof(MineRock), typeof(MineRock5), typeof(TreeBase), typeof(TreeLog) };
            foreach (Type type in types)
            {
                MethodInfo method = AccessTools.Method(type, "Damage", new[] { typeof(HitData) });
                if (method != null)
                    yield return method;
            }
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(HitData hit)
        {
            if (!BloodMoonInteractionRules.IsEventCombatLive || hit == null)
                return true;
            Character attacker = BloodMoonAttackContext.Attacker ?? hit.GetAttacker();
            if (BloodMoonAttackContext.Attribution != null)
                return false;
            if (attacker is Player player && BloodMoonInteractionRules.IsActiveParticipant(player))
                return false;
            if (BloodMoonInteractionRules.IsBloodEnemy(attacker))
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.RaiseSkill))]
    internal static class BloodMoonAttackSkillCreditGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Character __instance)
        {
            if (!BloodMoonAttackContext.IsActive || __instance is not Player)
                return true;
            return BloodMoonAttackContext.CanCredit;
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    internal static class BloodMoonProjectileOnHitPatch
    {
        private static bool Prefix(Projectile __instance, Collider collider, out bool __state)
        {
            __state = false;
            if (!BloodMoonInteractionRules.IsEventCombatLive || collider == null)
                return true;

            GameObject hitObject = Projectile.FindHitObject(collider);
            Character target = hitObject != null ? hitObject.GetComponent<Character>() : null;
            if (BloodMoonHitAttribution.TryGet(__instance, __instance.m_nview, out BloodMoonHitAttributionData attribution))
            {
                if (target == null || !BloodMoonHitAttribution.CanDamage(attribution, target))
                    return false;
                BloodMoonAttackContext.Begin(attribution);
                BloodMoonAttackContext.AllowedCharacterHit = BloodMoonInteractionRules.IsBloodEnemy(target);
                __state = true;
                return true;
            }

            Character owner = __instance.m_owner;
            if (owner == null)
                return true;
            if (target == null)
                return !(owner is Player player && BloodMoonInteractionRules.IsActiveParticipant(player)) && !BloodMoonInteractionRules.IsBloodEnemy(owner);
            return BloodMoonCombat.CanDirectDamage(owner, target, __instance.m_originalHitData, out _);
        }

        private static void Postfix(bool __state)
        {
            if (__state)
                BloodMoonAttackContext.End();
        }

        private static System.Exception Finalizer(System.Exception __exception, bool __state)
        {
            if (__state)
                BloodMoonAttackContext.End();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Aoe), nameof(Aoe.ShouldHit))]
    internal static class BloodMoonAoeShouldHitPatch
    {
        private static void Postfix(Aoe __instance, Collider collider, ref bool __result)
        {
            if (!__result || !BloodMoonInteractionRules.IsEventCombatLive || collider == null)
                return;

            GameObject hitObject = Projectile.FindHitObject(collider);
            Character target = hitObject != null ? hitObject.GetComponent<Character>() : null;
            if (BloodMoonHitAttribution.TryGet(__instance, __instance.m_nview, out BloodMoonHitAttributionData attribution))
            {
                __result = target != null && BloodMoonHitAttribution.CanDamage(attribution, target);
                return;
            }

            Character owner = __instance.m_owner;
            if (owner == null)
                return;
            if (target == null)
            {
                if (owner is Player player && BloodMoonInteractionRules.IsActiveParticipant(player) || BloodMoonInteractionRules.IsBloodEnemy(owner))
                    __result = false;
                return;
            }
            __result = BloodMoonCombat.CanDirectDamage(owner, target, __instance.m_hitData, out _);
        }
    }

    [HarmonyPatch(typeof(Aoe), nameof(Aoe.OnHit))]
    internal static class BloodMoonAoeOnHitAttributionPatch
    {
        private static void Prefix(Aoe __instance, Collider collider, out bool __state)
        {
            __state = false;
            if (collider == null || !BloodMoonHitAttribution.TryGet(__instance, __instance.m_nview, out BloodMoonHitAttributionData attribution))
                return;
            GameObject hitObject = Projectile.FindHitObject(collider);
            Character target = hitObject != null ? hitObject.GetComponent<Character>() : null;
            if (target == null || !BloodMoonHitAttribution.CanDamage(attribution, target))
                return;
            BloodMoonAttackContext.Begin(attribution);
            BloodMoonAttackContext.AllowedCharacterHit = BloodMoonInteractionRules.IsBloodEnemy(target);
            __state = true;
        }

        private static void Postfix(bool __state)
        {
            if (__state)
                BloodMoonAttackContext.End();
        }

        private static System.Exception Finalizer(System.Exception __exception, bool __state)
        {
            if (__state)
                BloodMoonAttackContext.End();
            return __exception;
        }
    }
}
