using HarmonyLib;
using System;
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

            if (BloodMoonInteractionRules.IsParticipantCombatSource(attacker) && BloodMoonInteractionRules.IsBloodEnemy(target))
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

            if (attribution.SourceType == BloodMoonCombatSourceType.Participant || attribution.SourceType == BloodMoonCombatSourceType.ParticipantSummon)
                multiplier = Mathf.Max(0f, BloodMoonConfig.EnemyIncomingDamageMultiplier.Value);
            else if (attribution.SourceType == BloodMoonCombatSourceType.BloodEnemy && target is Player)
                multiplier = Mathf.Max(0f, BloodMoonConfig.EnemyOutgoingDamageMultiplier.Value);
            return true;
        }

        internal static void RecordCreditedPlayer(Character target, long playerId)
        {
            if (target == null || target.m_nview == null || !target.m_nview.IsValid() || !BloodMoonInteractionRules.IsBloodEnemy(target) ||
                playerId == 0L || !BloodMoonInteractionRules.CanCreditProgress(playerId))
                return;
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
        [ThreadStatic]
        internal static Character Attacker;
        [ThreadStatic]
        internal static bool AllowedCharacterHit;
        [ThreadStatic]
        internal static BloodMoonHitAttributionData Attribution;

        internal static bool IsActive => Attacker != null || Attribution != null;
        internal static bool IsParticipantSource => BloodMoonInteractionRules.IsParticipantCombatSource(Attacker) ||
            Attribution?.SourceType == BloodMoonCombatSourceType.Participant || Attribution?.SourceType == BloodMoonCombatSourceType.ParticipantSummon;
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

    internal struct BloodMoonDamageObservation
    {
        internal bool Track;
        internal float BeforeHealth;
        internal long EventId;
        internal long CreditPlayerId;
        internal long LifestealPlayerId;
    }

    internal struct BloodMoonProjectileHitState
    {
        internal bool ContextStarted;
        internal bool RestoreHealthReturn;
        internal float HealthReturn;
    }

    [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
    internal static class BloodMoonCharacterRpcDamagePatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Character __instance, HitData hit, out BloodMoonDamageObservation __state)
        {
            __state = default;
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

            if (__instance is Player targetPlayer)
            {
                bool fromBloodEnemy = attributed
                    ? attribution?.SourceType == BloodMoonCombatSourceType.BloodEnemy
                    : BloodMoonInteractionRules.IsBloodEnemy(attacker);
                if (fromBloodEnemy)
                {
                    float bloodlustIncoming = BloodMoonBloodlust.GetIncomingMultiplier(targetPlayer);
                    if (!Mathf.Approximately(bloodlustIncoming, 1f))
                        hit.ApplyModifier(bloodlustIncoming);
                }
            }

            if (BloodMoonInteractionRules.IsBloodEnemy(__instance))
            {
                long creditPlayerId = GetCreditPlayerId(attacker, attribution);
                long lifestealPlayerId = TryGetLifestealPlayerId(attacker, attribution, out long sourcePlayerId) ? sourcePlayerId : 0L;
                if (creditPlayerId != 0L || lifestealPlayerId != 0L)
                {
                    __state.Track = true;
                    __state.BeforeHealth = Mathf.Max(0f, __instance.GetHealth());
                    __state.EventId = BloodMoonNetwork.ClientGlobal.EventId;
                    __state.CreditPlayerId = creditPlayerId;
                    __state.LifestealPlayerId = lifestealPlayerId;
                }
            }
            return true;
        }

        private static void Postfix(Character __instance, BloodMoonDamageObservation __state)
        {
            if (!__state.Track || __instance == null || !__instance.IsOwner())
                return;

            float actualDamage = Mathf.Max(0f, __state.BeforeHealth - Mathf.Max(0f, __instance.GetHealth()));
            if (actualDamage <= 0f)
                return;

            if (__state.CreditPlayerId != 0L)
                BloodMoonCombat.RecordCreditedPlayer(__instance, __state.CreditPlayerId);
            if (__state.LifestealPlayerId != 0L)
                BloodMoonBloodlust.ReportActualDamage(__instance, __state.EventId, __state.LifestealPlayerId, actualDamage);
        }

        private static long GetCreditPlayerId(Character attacker, BloodMoonHitAttributionData attribution)
        {
            if (attribution != null)
                return BloodMoonHitAttribution.CanCredit(attribution) ? attribution.SourcePlayerId : 0L;
            return BloodMoonInteractionRules.TryGetParticipantSourcePlayerId(attacker, out long playerId) && BloodMoonInteractionRules.CanCreditProgress(playerId)
                ? playerId
                : 0L;
        }

        private static bool TryGetLifestealPlayerId(Character attacker, BloodMoonHitAttributionData attribution, out long playerId)
        {
            playerId = 0L;
            if (attribution != null)
            {
                if (attribution.SourceType != BloodMoonCombatSourceType.Participant || !BloodMoonHitAttribution.CanCredit(attribution))
                    return false;
                playerId = attribution.SourcePlayerId;
                return playerId != 0L;
            }

            if (attacker is not Player player || !BloodMoonInteractionRules.IsActiveParticipant(player))
                return false;
            playerId = player.GetPlayerID();
            return playerId != 0L;
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

    [HarmonyPatch]
    internal static class BloodMoonDirectAttackContextPatch
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

        private static void Prefix(Attack __instance)
        {
            Character attacker = __instance.m_character;
            if (BloodMoonInteractionRules.IsEventCombatLive &&
                (BloodMoonInteractionRules.IsParticipantCombatSource(attacker) || BloodMoonInteractionRules.IsBloodEnemy(attacker)))
                BloodMoonAttackContext.Begin(attacker);
        }

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
            {
                BloodMoonAttackContext.AllowedCharacterHit = true;
                Player sourcePlayer = null;
                if (attribution?.SourceType == BloodMoonCombatSourceType.Participant && Player.m_localPlayer != null &&
                    attribution.SourcePlayerId == Player.m_localPlayer.GetPlayerID())
                    sourcePlayer = Player.m_localPlayer;
                else if (attribution == null && BloodMoonAttackContext.Attacker is Player player)
                    sourcePlayer = player;

                if (sourcePlayer != null)
                {
                    float bloodlustOutgoing = BloodMoonBloodlust.GetOutgoingMultiplier(sourcePlayer);
                    if (!Mathf.Approximately(bloodlustOutgoing, 1f))
                        hit.ApplyModifier(bloodlustOutgoing);
                }
            }
            return true;
        }
    }

    [HarmonyPatch]
    internal static class BloodMoonWorldDestructibleDamagePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type[] types = { typeof(WearNTear), typeof(Destructible), typeof(MineRock), typeof(MineRock5), typeof(TreeBase), typeof(TreeLog), typeof(HitArea), typeof(Raven) };
            foreach (Type type in types)
            {
                MethodInfo method = AccessTools.Method(type, "Damage", new[] { typeof(HitData) });
                if (method != null)
                    yield return method;
                else
                    Seasons.LogWarning($"[BloodMoon.Combat] Could not resolve {type.Name}.Damage(HitData); Blood Moon world-damage protection for that type is unavailable.");
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
            if (BloodMoonInteractionRules.IsParticipantCombatSource(attacker) || BloodMoonInteractionRules.IsBloodEnemy(attacker))
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.RaiseSkill))]
    internal static class BloodMoonAttackSkillCreditGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Player __instance)
        {
            if (!BloodMoonAttackContext.IsActive)
                return true;
            return BloodMoonAttackContext.CanCredit;
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    internal static class BloodMoonProjectileOnHitPatch
    {
        private static bool Prefix(Projectile __instance, Collider collider, out BloodMoonProjectileHitState __state)
        {
            __state = default;
            if (!BloodMoonInteractionRules.IsEventCombatLive || collider == null)
                return true;

            GameObject hitObject = Projectile.FindHitObject(collider);
            Character target = hitObject != null ? hitObject.GetComponent<Character>() : null;
            if (BloodMoonHitAttribution.TryGet(__instance, __instance.m_nview, out BloodMoonHitAttributionData attribution))
            {
                if (target != null && !BloodMoonHitAttribution.CanDamage(attribution, target))
                    return false;

                BloodMoonAttackContext.Begin(attribution);
                BloodMoonAttackContext.AllowedCharacterHit = target != null && BloodMoonInteractionRules.IsBloodEnemy(target);
                __state.ContextStarted = true;
                SuppressWorldHealthReturn(__instance, target, ref __state);
                return true;
            }

            Character owner = __instance.m_owner;
            if (owner == null)
                return true;
            bool bloodSource = BloodMoonInteractionRules.IsParticipantCombatSource(owner) || BloodMoonInteractionRules.IsBloodEnemy(owner);
            if (!bloodSource)
                return true;
            if (target != null && !BloodMoonCombat.CanDirectDamage(owner, target, __instance.m_originalHitData, out _))
                return false;

            BloodMoonAttackContext.Begin(owner);
            BloodMoonAttackContext.AllowedCharacterHit = target != null && BloodMoonInteractionRules.IsBloodEnemy(target);
            __state.ContextStarted = true;
            SuppressWorldHealthReturn(__instance, target, ref __state);
            return true;
        }

        private static void Postfix(Projectile __instance, BloodMoonProjectileHitState __state)
        {
            Restore(__instance, __state);
        }

        private static Exception Finalizer(Exception __exception, Projectile __instance, BloodMoonProjectileHitState __state)
        {
            Restore(__instance, __state);
            return __exception;
        }

        private static void SuppressWorldHealthReturn(Projectile projectile, Character target, ref BloodMoonProjectileHitState state)
        {
            if (projectile == null || target != null || Mathf.Approximately(projectile.m_healthReturn, 0f))
                return;
            state.RestoreHealthReturn = true;
            state.HealthReturn = projectile.m_healthReturn;
            projectile.m_healthReturn = 0f;
        }

        private static void Restore(Projectile projectile, BloodMoonProjectileHitState state)
        {
            if (projectile != null && state.RestoreHealthReturn)
                projectile.m_healthReturn = state.HealthReturn;
            if (state.ContextStarted)
                BloodMoonAttackContext.End();
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
                if (BloodMoonInteractionRules.IsParticipantCombatSource(owner) || BloodMoonInteractionRules.IsBloodEnemy(owner))
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

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
                BloodMoonAttackContext.End();
            return __exception;
        }
    }
}
