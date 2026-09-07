using UnityEngine;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonEnemyDeathReports
    {
        internal static bool TryValidate(long sender, long eventId, long creditedPlayerId, ZDOID enemyId, out float serverPoints)
        {
            serverPoints = 0f;
            BloodMoonController controller = BloodMoonController.Instance;
            BloodMoonEventState state = controller?.State;
            if (state == null || !state.IsCombatLive || state.EventId != eventId || creditedPlayerId == 0L || enemyId.IsNone() ||
                ZDOMan.instance == null || ZRoutedRpc.instance == null || ZNetScene.instance == null)
                return false;

            if (!state.Participants.TryGetValue(creditedPlayerId, out BloodMoonParticipantState participant) || !participant.IsCombatActive || !IsConnectedParticipant(controller, creditedPlayerId))
                return false;

            ZDO enemyZdo = ZDOMan.instance.GetZDO(enemyId);
            // Ownership can migrate after the owner observes death but before its routed report reaches
            // the server. Event identity, observed dead state and matched damage credit remain stable
            // validation inputs; receive-time ownership does not.
            if (enemyZdo == null || !IsEligibleBloodEnemyZdo(state.EventId, enemyZdo) || !IsObservedDead(enemyZdo))
                return false;

            if (!BloodMoonDamageCreditAuthority.TryGetConfirmedCredit(eventId, enemyId, out long confirmedPlayerId) || confirmedPlayerId != creditedPlayerId)
                return false;

            serverPoints = BloodMoonCombat.GetPointsForEnemy(enemyId, 0f);
            return serverPoints > 0f && !float.IsNaN(serverPoints) && !float.IsInfinity(serverPoints);
        }

        internal static bool CanPendingValidate(long sender, long eventId, long creditedPlayerId, ZDOID enemyId)
        {
            BloodMoonController controller = BloodMoonController.Instance;
            BloodMoonEventState state = controller?.State;
            if (state == null || !state.IsCombatLive || state.EventId != eventId || creditedPlayerId == 0L || enemyId.IsNone() ||
                ZDOMan.instance == null || ZRoutedRpc.instance == null || ZNetScene.instance == null)
                return false;
            if (!state.Participants.TryGetValue(creditedPlayerId, out BloodMoonParticipantState participant) || !participant.IsCombatActive || !IsConnectedParticipant(controller, creditedPlayerId))
                return false;

            ZDO enemyZdo = ZDOMan.instance.GetZDO(enemyId);
            return enemyZdo != null && IsEligibleBloodEnemyZdo(state.EventId, enemyZdo);
        }

        internal static bool IsObservedDead(ZDO zdo)
        {
            return zdo != null && zdo.GetFloat(ZDOVars.s_health, float.MaxValue) <= 0f;
        }

        internal static bool IsEligibleBloodEnemyZdo(BloodMoonEventState state, ZDO zdo)
        {
            return state != null && IsEligibleBloodEnemyZdo(state.EventId, zdo);
        }

        internal static bool IsEligibleBloodEnemyZdo(long eventId, ZDO zdo)
        {
            if (eventId < 0L || zdo == null || ZNetScene.instance == null)
                return false;

            GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            Character character = prefab != null ? prefab.GetComponent<Character>() : null;
            MonsterAI monsterAi = prefab != null ? prefab.GetComponent<MonsterAI>() : null;
            if (character == null || monsterAi == null || character.IsBoss() || zdo.GetBool(ZDOVars.s_tamed, false))
                return false;

            Character.Faction faction = character.GetFaction();
            if (faction == Character.Faction.Players || faction == Character.Faction.PlayerSpawned || faction == Character.Faction.TrainingDummy || faction == Character.Faction.Boss)
                return false;

            if (zdo.GetLong(BloodMoonSpawner.EventMarker, -1L) == eventId)
                return true;

            // BaseAI.IsEnemy(monster, Player) reduced to dedicated-server data for an untamed MonsterAI.
            // Dverger are neutral to Players unless aggravated; the other eligible MonsterAI factions are hostile.
            if (faction == Character.Faction.Dverger)
                return zdo.GetBool(ZDOVars.s_aggravated, false);

            return true;
        }

        private static bool IsConnectedParticipant(BloodMoonController controller, long playerId)
        {
            if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId)
                return true;
            long peerId = controller.GetPeerForPlayer(playerId);
            return peerId != 0L && ZNet.instance != null && ZNet.instance.GetPeer(peerId) != null;
        }
    }
}