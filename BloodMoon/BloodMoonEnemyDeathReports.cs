using UnityEngine;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonEnemyDeathReports
    {
        internal static bool TryValidate(long sender, long eventId, long creditedPlayerId, ZDOID enemyId, out float serverPoints)
        {
            serverPoints = 0f;
            if (!TryValidateParticipantContext(sender, eventId, creditedPlayerId, enemyId, out BloodMoonController controller, out BloodMoonEventState state))
                return false;

            bool hasServerDeathEvidence = false;
            ZDO enemyZdo = ZDOMan.instance.GetZDO(enemyId);
            if (enemyZdo != null)
            {
                if (!IsEligibleBloodEnemyZdo(state.EventId, enemyZdo) || !IsObservedDead(enemyZdo))
                    return false;
                serverPoints = BloodMoonCombat.GetPointsForEnemy(enemyId, 0f);
                hasServerDeathEvidence = true;
            }
            else if (BloodMoonEnemyDeathPending.TryGetRetainedDeathEvidence(eventId, enemyId, out serverPoints))
            {
                hasServerDeathEvidence = true;
            }

            bool hasConfirmedCredit = BloodMoonDamageCreditAuthority.TryGetConfirmedCredit(eventId, enemyId, out long confirmedPlayerId) &&
                confirmedPlayerId == creditedPlayerId;
            if (hasServerDeathEvidence && hasConfirmedCredit)
                return IsFinitePositive(serverPoints);

            // A server restart after an accepted death but before a successful state write can lose both
            // the dead ZDO and the in-memory matched lethal credit. The unmodified enemy owner keeps the
            // report in its player profile until ACK. Per 16_CLIENT_TRUST_BOUNDARY.md this client-owned fact
            // is trusted for correctness/recovery; hostile fabricated traffic is explicitly out of scope.
            if (!BloodMoonEnemyDeathDurability.TryGetCurrentProfileReplay(eventId, creditedPlayerId, enemyId, out float replayPoints))
                return false;

            if (hasServerDeathEvidence && Mathf.Abs(serverPoints - replayPoints) > 0.001f)
                return false;
            serverPoints = hasServerDeathEvidence ? serverPoints : replayPoints;
            return IsFinitePositive(serverPoints);
        }

        internal static bool TryCapturePendingDeathEvidence(long sender, long eventId, long creditedPlayerId, ZDOID enemyId, out float serverPoints)
        {
            serverPoints = 0f;
            if (!TryValidateParticipantContext(sender, eventId, creditedPlayerId, enemyId, out _, out BloodMoonEventState state))
                return false;

            ZDO enemyZdo = ZDOMan.instance.GetZDO(enemyId);
            if (enemyZdo == null || !IsEligibleBloodEnemyZdo(state.EventId, enemyZdo) || !IsObservedDead(enemyZdo))
                return false;

            serverPoints = BloodMoonCombat.GetPointsForEnemy(enemyId, 0f);
            return IsFinitePositive(serverPoints);
        }

        internal static bool CanPendingValidate(long sender, long eventId, long creditedPlayerId, ZDOID enemyId)
        {
            if (!TryValidateParticipantContext(sender, eventId, creditedPlayerId, enemyId, out _, out BloodMoonEventState state))
                return false;

            if (BloodMoonEnemyDeathPending.TryGetRetainedDeathEvidence(eventId, enemyId, out _))
                return true;

            ZDO enemyZdo = ZDOMan.instance.GetZDO(enemyId);
            return enemyZdo != null && IsEligibleBloodEnemyZdo(state.EventId, enemyZdo);
        }

        internal static bool CanUseRetainedDeathEvidence(long sender, long eventId, long creditedPlayerId, ZDOID enemyId)
        {
            return TryValidateParticipantContext(sender, eventId, creditedPlayerId, enemyId, out _, out _);
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

        private static bool TryValidateParticipantContext(long sender, long eventId, long creditedPlayerId, ZDOID enemyId,
            out BloodMoonController controller, out BloodMoonEventState state)
        {
            controller = BloodMoonController.Instance;
            state = controller?.State;
            if (state == null || state.EventId != eventId || creditedPlayerId == 0L || enemyId.IsNone() || sender == 0L ||
                ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null || ZRoutedRpc.instance == null || ZNetScene.instance == null ||
                !state.Participants.TryGetValue(creditedPlayerId, out BloodMoonParticipantState participant))
                return false;

            bool readySender = sender == ZRoutedRpc.instance.GetServerPeerID() || ZNet.instance.GetPeer(sender)?.IsReady() == true;
            if (!readySender)
                return false;

            bool retainedReplay = BloodMoonEnemyDeathDurability.TryGetCurrentProfileReplay(eventId, creditedPlayerId, enemyId, out _);
            if (retainedReplay && BloodMoonRound5Runtime.IsPreOutcomeDrainOpen(state))
            {
                // This is an already-observed kill retained before disconnect, not a new attack by an
                // exited participant. Replaying it never changes Exited back to Fighting/GoalReached.
                return participant.IsCombatActive || participant.ExitReason == BloodMoonParticipantExitReason.Disconnected;
            }

            return state.IsCombatLive && participant.IsCombatActive && IsConnectedParticipant(controller, creditedPlayerId);
        }

        private static bool IsConnectedParticipant(BloodMoonController controller, long playerId)
        {
            if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId)
                return true;
            long peerId = controller.GetPeerForPlayer(playerId);
            return peerId != 0L && ZNet.instance != null && ZNet.instance.GetPeer(peerId) != null;
        }

        private static bool IsFinitePositive(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
