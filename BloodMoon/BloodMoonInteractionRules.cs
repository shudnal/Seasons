using System.Collections.Generic;
using System.Linq;

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
                return BloodMoonNetwork.ClientGlobal.BloodBehaviorEnabled && (phase == BloodMoonEventPhase.Active || phase == BloodMoonEventPhase.AutoCompleting);
            }
        }

        internal static BloodMoonParticipantState GetLocalParticipant()
        {
            Player player = Player.m_localPlayer;
            if (player == null || BloodMoonNetwork.ClientParticipants.EventId != BloodMoonNetwork.ClientGlobal.EventId)
                return null;
            return GetParticipant(player.GetPlayerID());
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
            if (character.m_nview == null || !character.m_nview.IsValid() || character.GetBaseAI() is not MonsterAI)
                return false;
            Character.Faction faction = character.GetFaction();
            return faction != Character.Faction.Players && faction != Character.Faction.PlayerSpawned && faction != Character.Faction.TrainingDummy;
        }

        internal static bool IsBloodMoonSpawned(Character character)
        {
            if (character == null || character.m_nview == null || !character.m_nview.IsValid())
                return false;
            ZDO zdo = character.m_nview.GetZDO();
            return zdo != null && zdo.GetLong(BloodMoonSpawner.EventMarker, -1L) == BloodMoonNetwork.ClientGlobal.EventId;
        }

        internal static bool IsBloodMoonExtra(Character character) => IsBloodMoonSpawned(character);

        internal static bool IsBloodEnemy(Character character)
        {
            if (!IsEventCombatLive || !IsEligibleExistingMonster(character))
                return false;
            if (IsBloodMoonSpawned(character))
                return true;

            foreach (Player player in GetLoadedActiveParticipants(preferFighting: false))
            {
                if (BaseAI.IsEnemy(character, player))
                    return true;
            }
            return false;
        }

        internal static bool IsActiveParticipant(Player player)
        {
            return player != null && IsActiveParticipant(player.GetPlayerID());
        }

        internal static bool IsActiveParticipant(long playerId)
        {
            return GetParticipant(playerId)?.IsCombatActive == true;
        }

        internal static bool CanTarget(Character attacker, Character target)
        {
            if (!IsEventCombatLive || attacker == null || target == null)
                return true;

            bool attackerParticipant = attacker is Player attackerPlayer && IsActiveParticipant(attackerPlayer);
            bool attackerBlood = IsBloodEnemy(attacker);
            bool targetParticipant = target is Player targetPlayer && IsActiveParticipant(targetPlayer);
            bool targetBlood = IsBloodEnemy(target);

            if (attackerParticipant)
                return targetBlood;
            if (attackerBlood)
                return targetParticipant;
            if (targetBlood || targetParticipant)
                return false;
            return true;
        }

        internal static bool CanDamage(Character attacker, Character target, HitData hit)
        {
            if (!IsEventCombatLive || target == null)
                return true;

            bool targetParticipant = target is Player targetPlayer && IsActiveParticipant(targetPlayer);
            bool targetBlood = IsBloodEnemy(target);
            if (!targetParticipant && !targetBlood)
            {
                if (attacker is Player participant && IsActiveParticipant(participant))
                    return false;
                if (IsBloodEnemy(attacker))
                    return false;
                return true;
            }

            if (attacker == null)
                return false;
            return CanTarget(attacker, target);
        }

        internal static bool CanReceiveProgress(Character target, HitData hit)
        {
            if (!IsEventCombatLive || !IsBloodEnemy(target) || hit == null)
                return false;
            return hit.GetAttacker() is Player player && IsActiveParticipant(player);
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
            BloodMoonParticipantState routing = BloodMoonNetwork.ClientParticipants.Participants.FirstOrDefault(participant => participant.PlayerId == playerId);
            return BloodMoonParticipantDetails.MergeOwnDetail(routing);
        }

        internal static List<Player> GetLoadedActiveParticipants(bool preferFighting)
        {
            List<Player> fighting = new List<Player>();
            List<Player> goalReached = new List<Player>();
            foreach (Player player in Player.GetAllPlayers())
            {
                BloodMoonParticipantState participant = GetParticipant(player.GetPlayerID());
                if (participant == null || !participant.IsCombatActive || player.IsDead() || player.InDebugFlyMode() || player.InGhostMode())
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
