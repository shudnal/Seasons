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
                return BloodMoonNetwork.ClientGlobal.BloodBehaviorEnabled &&
                    (phase == BloodMoonEventPhase.Active || phase == BloodMoonEventPhase.AutoCompleting || phase == BloodMoonEventPhase.Resolving);
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
            if (participant == null)
                return false;
            if (Player.m_localPlayer != null && BloodMoonRecovery.IsLocallyExited(Player.m_localPlayer.GetPlayerID()))
                return false;
            return participant.Phase == BloodMoonParticipantPhase.Marked || participant.IsCombatActive;
        }

        internal static bool CanSleep(Player player)
        {
            if (!IsEventMarkedOrLater || player == null)
                return true;
            if (player == Player.m_localPlayer && BloodMoonRecovery.IsLocallyExited(player.GetPlayerID()))
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
            if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId && BloodMoonRecovery.IsLocallyExited(playerId))
                return false;
            return GetParticipant(playerId)?.IsCombatActive == true;
        }

        internal static bool IsParticipantCombatSource(Character character)
        {
            return character is Player player && IsActiveParticipant(player) || BloodMoonSummons.IsBloodSummon(character);
        }

        internal static bool TryGetParticipantSourcePlayerId(Character character, out long playerId)
        {
            playerId = 0L;
            if (character is Player player && IsActiveParticipant(player))
            {
                playerId = player.GetPlayerID();
                return playerId != 0L;
            }
            return BloodMoonSummons.TryGetOwnerPlayerId(character, out playerId);
        }

        internal static bool CanTarget(Character attacker, Character target)
        {
            if (!IsEventCombatLive || attacker == null || target == null)
                return true;

            bool attackerParticipantSource = IsParticipantCombatSource(attacker);
            bool attackerBlood = IsBloodEnemy(attacker);
            bool targetParticipant = target is Player targetPlayer && IsActiveParticipant(targetPlayer);
            bool targetBlood = IsBloodEnemy(target);

            if (attackerParticipantSource)
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
                if (IsParticipantCombatSource(attacker) || IsBloodEnemy(attacker))
                    return false;
                return true;
            }

            // Environmental and other unattributed damage remains vanilla-valid for participants.
            // The same unattributed source must not become a way to damage Blood enemies.
            if (attacker == null)
                return targetParticipant;
            return CanTarget(attacker, target);
        }

        internal static bool CanReceiveProgress(Character target, HitData hit)
        {
            if (!IsEventCombatLive || !IsBloodEnemy(target) || hit == null)
                return false;
            Character attacker = hit.GetAttacker();
            return TryGetParticipantSourcePlayerId(attacker, out long playerId) && CanCreditProgress(playerId);
        }

        internal static bool CanCreditProgress(long playerId)
        {
            BloodMoonParticipantState participant = GetParticipant(playerId);
            return IsEventCombatLive && participant != null && participant.IsCombatActive && IsActiveParticipant(playerId);
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
                if (participant == null || !participant.IsCombatActive || !IsActiveParticipant(player) || player.IsDead() || player.IsTeleporting() || player.InDebugFlyMode() || player.InGhostMode())
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
