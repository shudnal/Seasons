using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonSkillReports
    {
        internal static void Accept(long sender, long eventId, long playerId, long sequence, Skills.SkillType skill, float baseEquivalent, float liveBonusEquivalent)
        {
            BloodMoonController controller = BloodMoonController.Instance;
            BloodMoonEventState state = controller?.State;
            if (state == null || state.EventId != eventId || !state.IsCombatLive || sequence <= 0L || !IsFinite(baseEquivalent) || !IsFinite(liveBonusEquivalent))
                return;
            if (!state.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant) || !participant.IsCombatActive)
                return;
            if (!ValidateSender(sender, playerId))
                return;

            long previousSequence = participant.LastSkillReportSequence;
            BloodMoonSkills.AcceptServerReport(participant, sequence, skill, baseEquivalent, liveBonusEquivalent);
            if (participant.LastSkillReportSequence == previousSequence)
                return;

            state.UpdatedAt = SeasonState.IsActive ? seasonState.GetTotalSeconds() : state.UpdatedAt;
            state.Revision++;
            BloodMoonPersistence.Save(state);
            BloodMoonNetwork.Publish(state, state.UpdatedAt);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool ValidateSender(long sender, long playerId)
        {
            if (playerId == 0L || ZNet.instance == null || ZRoutedRpc.instance == null || ZDOMan.instance == null)
                return false;

            if (sender == ZRoutedRpc.instance.GetServerPeerID())
                return Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId;

            ZNetPeer peer = ZNet.instance.GetPeer(sender);
            if (peer == null || peer.m_characterID.IsNone())
                return false;
            ZDO playerZdo = ZDOMan.instance.GetZDO(peer.m_characterID);
            return playerZdo != null && playerZdo.GetLong(ZDOVars.s_playerID, 0L) == playerId;
        }
    }
}
