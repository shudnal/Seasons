using UnityEngine;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonEnemyDeathReports
    {
        internal static bool TryValidate(long sender, long eventId, long creditedPlayerId, ZDOID enemyId, out long creditedPlayerSender)
        {
            creditedPlayerSender = 0L;
            BloodMoonController controller = BloodMoonController.Instance;
            BloodMoonEventState state = controller?.State;
            if (state == null || !state.IsCombatLive || state.EventId != eventId || creditedPlayerId == 0L || enemyId.IsNone() ||
                ZDOMan.instance == null || ZRoutedRpc.instance == null || ZNetScene.instance == null)
                return false;

            if (!state.Participants.TryGetValue(creditedPlayerId, out BloodMoonParticipantState participant) || !participant.IsCombatActive)
                return false;

            ZDO enemyZdo = ZDOMan.instance.GetZDO(enemyId);
            if (enemyZdo == null || enemyZdo.GetOwner() != sender || !IsEligibleBloodEnemyZdo(state, enemyZdo))
                return false;

            if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == creditedPlayerId)
            {
                creditedPlayerSender = ZRoutedRpc.instance.GetServerPeerID();
                return true;
            }

            long peerId = controller.GetPeerForPlayer(creditedPlayerId);
            if (peerId == 0L || ZNet.instance == null || ZNet.instance.GetPeer(peerId) == null)
                return false;
            creditedPlayerSender = peerId;
            return true;
        }

        internal static bool IsEligibleBloodEnemyZdo(BloodMoonEventState state, ZDO zdo)
        {
            if (state == null || zdo == null || ZNetScene.instance == null)
                return false;

            GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            Character character = prefab != null ? prefab.GetComponent<Character>() : null;
            MonsterAI monsterAi = prefab != null ? prefab.GetComponent<MonsterAI>() : null;
            if (character == null || monsterAi == null || character.IsBoss() || zdo.GetBool(ZDOVars.s_tamed, false))
                return false;

            Character.Faction faction = character.GetFaction();
            if (faction == Character.Faction.Players || faction == Character.Faction.PlayerSpawned || faction == Character.Faction.TrainingDummy || faction == Character.Faction.Boss)
                return false;

            if (zdo.GetLong(BloodMoonSpawner.EventMarker, -1L) == state.EventId)
                return true;

            // This is BaseAI.IsEnemy(monster, Player) reduced to data available on a dedicated server.
            // For an untamed MonsterAI all eligible factions are hostile to Players except neutral Dverger,
            // which becomes hostile only while its persistent aggravated flag is set.
            if (faction == Character.Faction.Dverger)
                return zdo.GetBool(ZDOVars.s_aggravated, false);

            return true;
        }
    }
}
