using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonCombat
    {
        private static readonly Dictionary<ZDOID, long> lastCreditedPlayer = new Dictionary<ZDOID, long>();
        private static readonly HashSet<ZDOID> locallyReportedDeaths = new HashSet<ZDOID>();

        internal static bool CanDamage(Character attacker, Character target, HitData hit, out float multiplier)
        {
            multiplier = 1f;
            if (!BloodMoonInteractionRules.IsEventCombatLive || target == null)
                return true;

            bool targetParticipant = target is Player targetPlayer && BloodMoonInteractionRules.IsActiveParticipant(targetPlayer.GetPlayerID());
            bool targetBlood = BloodMoonInteractionRules.IsBloodEnemy(target);
            bool attackerParticipant = attacker is Player attackerPlayer && BloodMoonInteractionRules.IsActiveParticipant(attackerPlayer.GetPlayerID());
            bool attackerBlood = BloodMoonInteractionRules.IsBloodEnemy(attacker);

            if (!targetParticipant && !targetBlood && !attackerParticipant && !attackerBlood)
                return true;
            if (attackerParticipant && targetBlood)
            {
                multiplier = Mathf.Max(0f, BloodMoonConfig.EnemyIncomingDamageMultiplier.Value);
                return true;
            }
            if (attackerBlood && targetParticipant)
            {
                multiplier = Mathf.Max(0f, BloodMoonConfig.EnemyOutgoingDamageMultiplier.Value);
                multiplier *= BloodMoonRecovery.GetIncomingDamageMultiplier(targetPlayer);
                return true;
            }
            return false;
        }

        internal static void RecordCreditedHit(Character target, Character attacker)
        {
            if (target == null || attacker is not Player player || target.m_nview == null || !target.m_nview.IsValid())
                return;
            if (!BloodMoonInteractionRules.CanCreditProgress(player.GetPlayerID()) || !BloodMoonInteractionRules.IsBloodEnemy(target))
                return;
            lastCreditedPlayer[target.GetZDOID()] = player.GetPlayerID();
            BloodMoonSkills.RecordCombatSkillFromHit(player, target);
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

    [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
    internal static class BloodMoonCharacterRpcDamagePatch
    {
        private static bool Prefix(Character __instance, HitData hit)
        {
            if (hit == null || !__instance.IsOwner())
                return true;
            Character attacker = hit.GetAttacker();
            if (!BloodMoonCombat.CanDamage(attacker, __instance, hit, out float multiplier))
                return false;
            if (!Mathf.Approximately(multiplier, 1f))
                hit.ApplyModifier(multiplier);
            BloodMoonCombat.RecordCreditedHit(__instance, attacker);
            return true;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.CheckDeath))]
    internal static class BloodMoonCharacterCheckDeathPatch
    {
        private static bool Prefix(Character __instance)
        {
            if (__instance.IsDead() || __instance.GetHealth() > 0f || !__instance.IsOwner())
                return true;

            if (__instance is Player player && player == Player.m_localPlayer && BloodMoonInteractionRules.IsActiveParticipant(player.GetPlayerID()))
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

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    internal static class BloodMoonProjectileOnHitPatch
    {
        private static bool Prefix(Projectile __instance, Collider collider)
        {
            if (!BloodMoonInteractionRules.IsEventCombatLive || collider == null || __instance.m_owner == null)
                return true;
            GameObject hitObject = Projectile.FindHitObject(collider);
            Character target = hitObject != null ? hitObject.GetComponent<Character>() : null;
            if (target == null)
                return __instance.m_owner is not Player player || !BloodMoonInteractionRules.IsActiveParticipant(player.GetPlayerID());
            return BloodMoonCombat.CanDamage(__instance.m_owner, target, __instance.m_originalHitData, out _);
        }
    }

    [HarmonyPatch(typeof(Aoe), nameof(Aoe.ShouldHit))]
    internal static class BloodMoonAoeShouldHitPatch
    {
        private static void Postfix(Aoe __instance, Collider collider, ref bool __result)
        {
            if (!__result || !BloodMoonInteractionRules.IsEventCombatLive || __instance.m_owner == null || collider == null)
                return;
            GameObject hitObject = Projectile.FindHitObject(collider);
            Character target = hitObject != null ? hitObject.GetComponent<Character>() : null;
            if (target == null)
            {
                if (__instance.m_owner is Player player && BloodMoonInteractionRules.IsActiveParticipant(player.GetPlayerID()))
                    __result = false;
                return;
            }
            __result = BloodMoonCombat.CanDamage(__instance.m_owner, target, __instance.m_hitData, out _);
        }
    }
}
