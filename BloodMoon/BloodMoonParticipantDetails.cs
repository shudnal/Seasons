using Newtonsoft.Json;
using System;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonParticipantDetails
    {
        internal static BloodMoonParticipantDetailSnapshot ClientOwn { get; private set; } = new BloodMoonParticipantDetailSnapshot();

        internal static string Serialize(BloodMoonEventState state, BloodMoonParticipantState participant)
        {
            if (state == null || participant == null)
                return string.Empty;

            return JsonConvert.SerializeObject(new BloodMoonParticipantDetailSnapshot
            {
                EventId = state.EventId,
                Revision = state.Revision,
                PlayerId = participant.PlayerId,
                CombatPoints = participant.CombatPoints,
                DisplayProgress = participant.DisplayProgress,
                Contribution = participant.Contribution,
                MarkedAt = participant.MarkedAt,
                FightingAt = participant.FightingAt,
                GoalReachedAt = participant.GoalReachedAt,
                ExitedAt = participant.ExitedAt,
                ResolvedAt = participant.ResolvedAt
            });
        }

        internal static void Apply(long eventId, string payload)
        {
            if (eventId < 0L || string.IsNullOrWhiteSpace(payload) || Player.m_localPlayer == null)
                return;

            BloodMoonParticipantDetailSnapshot snapshot;
            try
            {
                snapshot = JsonConvert.DeserializeObject<BloodMoonParticipantDetailSnapshot>(payload);
            }
            catch (Exception ex)
            {
                Seasons.LogWarning($"[BloodMoon.Sync] Invalid participant detail snapshot: {ex.Message}");
                return;
            }

            if (snapshot == null || snapshot.Schema != BloodMoonStateSchema.Current || snapshot.EventId != eventId ||
                snapshot.PlayerId != Player.m_localPlayer.GetPlayerID())
                return;
            if (ClientOwn.EventId == snapshot.EventId && ClientOwn.Revision > snapshot.Revision)
                return;

            ClientOwn = snapshot;
            BloodMoonStatus.UpdateLocal();
        }

        internal static BloodMoonParticipantState MergeOwnDetail(BloodMoonParticipantState routing)
        {
            if (routing == null || Player.m_localPlayer == null || routing.PlayerId != Player.m_localPlayer.GetPlayerID() ||
                ClientOwn.EventId != BloodMoonNetwork.ClientGlobal.EventId || ClientOwn.PlayerId != routing.PlayerId)
                return routing;

            return new BloodMoonParticipantState
            {
                PlayerId = routing.PlayerId,
                PlayerName = routing.PlayerName,
                Phase = routing.Phase,
                ExitReason = routing.ExitReason,
                GoalReached = routing.GoalReached,
                AutoCompleted = routing.AutoCompleted,
                JoinedLate = routing.JoinedLate,
                CombatPoints = ClientOwn.CombatPoints,
                DisplayProgress = ClientOwn.DisplayProgress,
                Contribution = ClientOwn.Contribution,
                MarkedAt = ClientOwn.MarkedAt,
                FightingAt = ClientOwn.FightingAt,
                GoalReachedAt = ClientOwn.GoalReachedAt,
                ExitedAt = ClientOwn.ExitedAt,
                ResolvedAt = ClientOwn.ResolvedAt
            };
        }

        internal static void Reset()
        {
            ClientOwn = new BloodMoonParticipantDetailSnapshot();
        }
    }
}
