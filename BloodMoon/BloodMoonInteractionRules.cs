using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonInteractionRules
    {
        internal static bool IsEventMarkedOrLater
        {
            get
            {
                BloodMoonEventPhase phase = BloodMoonNetwork.ClientGlobal.Phase;
                return phase == BloodMoonEventPhase.Marked || phase == BloodMoonEventPhase.Active || phase == BloodMoonEventPhase.AutoCompleting || phase == BloodMoonEventPhase.Resolving;
            }
        }

        internal static bool IsEventCombatLive
        {
            get
            {
                BloodMoonEventPhase phase = BloodMoonNetwork.ClientGlobal.Phase;
                return phase == BloodMoonEventPhase.Active || phase == BloodMoonEventPhase.AutoCompleting;
            }
        }

        internal static BloodMoonParticipantState GetLocalParticipant()
        {
            Player player = Player.m_localPlayer;
            if (player == null || BloodMoonNetwork.ClientParticipants.EventId != BloodMoonNetwork.ClientGlobal.EventId)
                return null;
            long playerId = player.GetPlayerID();
            return BloodMoonNetwork.ClientParticipants.Participants?.FirstOrDefault(participant => participant.PlayerId == playerId);
        }

        internal static bool IsLocalParticipantActiveOrMarked()
        {
            BloodMoonParticipantState participant = GetLocalParticipant();
            return participant != null && (participant.Phase == BloodMoonParticipantPhase.Marked || participant.IsCombatActive);
        }

        internal static bool CanSleep(Player player)
        {
            if (!IsEventMarkedOrLater || player == null)
                return true;
            BloodMoonParticipantState participant = GetParticipant(player.GetPlayerID());
            return participant == null || participant.IsTerminal;
        }

        internal static bool CanUseBossOffering(OfferingBowl bowl)
        {
            return bowl == null || bowl.m_bossPrefab == null || !IsEventMarkedOrLater;
        }

        internal static bool IsEligibleExistingMonster(Character character)
        {
            if (character == null || character is Player || character.IsDead() || character.IsBoss() || character.IsTamed())
                return false;
            if (character.GetBaseAI() is not MonsterAI)
                return false;
            Character.Faction faction = character.GetFaction();
            return faction != Character.Faction.Players && faction != Character.Faction.PlayerSpawned && faction != Character.Faction.TrainingDummy;
        }

        internal static bool IsBloodMoonExtra(Character character)
        {
            if (character == null || character.m_nview == null || !character.m_nview.IsValid())
                return false;
            ZDO zdo = character.m_nview.GetZDO();
            return zdo != null && zdo.GetLong(BloodMoonSpawner.EventMarker, -1L) == BloodMoonNetwork.ClientGlobal.EventId;
        }

        internal static bool IsBloodEnemy(Character character)
        {
            if (!IsEventCombatLive || !IsEligibleExistingMonster(character))
                return false;
            if (IsBloodMoonExtra(character))
                return true;

            foreach (Player player in Player.GetAllPlayers())
            {
                BloodMoonParticipantState participant = GetParticipant(player.GetPlayerID());
                if (participant != null && participant.IsCombatActive && BaseAI.IsEnemy(character, player))
                    return true;
            }
            return false;
        }

        internal static bool IsLegalTarget(Character attacker, Character target)
        {
            if (!IsEventCombatLive || attacker == null || target == null)
                return true;

            if (attacker is Player attackerPlayer && IsActiveParticipant(attackerPlayer.GetPlayerID()))
            {
                if (target is Player targetPlayer)
                    return !IsActiveParticipant(targetPlayer.GetPlayerID());
                return IsBloodEnemy(target);
            }

            if (IsBloodEnemy(attacker))
            {
                if (target is Player player)
                    return IsActiveParticipant(player.GetPlayerID());
                return !IsEligibleExistingMonster(target) || !IsBloodEnemy(target);
            }

            if (target is Player protectedParticipant && IsActiveParticipant(protectedParticipant.GetPlayerID()))
                return false;

            return true;
        }

        internal static bool CanCreditProgress(long playerId)
        {
            BloodMoonParticipantState participant = GetParticipant(playerId);
            return IsEventCombatLive && participant != null && participant.IsCombatActive;
        }

        internal static BloodMoonParticipantState GetParticipant(long playerId)
        {
            if (BloodMoonNetwork.ClientParticipants.EventId != BloodMoonNetwork.ClientGlobal.EventId || BloodMoonNetwork.ClientParticipants.Participants == null)
                return null;
            return BloodMoonNetwork.ClientParticipants.Participants.FirstOrDefault(participant => participant.PlayerId == playerId);
        }

        internal static bool IsActiveParticipant(long playerId)
        {
            return GetParticipant(playerId)?.IsCombatActive == true;
        }

        internal static List<Player> GetLoadedActiveParticipants(bool preferFighting)
        {
            List<Player> fighting = new List<Player>();
            List<Player> goalReached = new List<Player>();
            foreach (Player player in Player.GetAllPlayers())
            {
                BloodMoonParticipantState participant = GetParticipant(player.GetPlayerID());
                if (participant == null || !participant.IsCombatActive || player.IsDead())
                    continue;
                if (participant.GoalReached)
                    goalReached.Add(player);
                else
                    fighting.Add(player);
            }
            if (preferFighting && fighting.Count > 0)
                return fighting;
            fighting.AddRange(goalReached);
            return fighting;
        }
    }
}
