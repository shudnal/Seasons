using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Seasons.BloodMoon
{
    [Serializable]
    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class BloodMoonParticipantDetailSnapshot
    {
        [JsonProperty] public int Schema = BloodMoonStateSchema.Current;
        [JsonProperty] public long EventId = -1L;
        [JsonProperty] public int Revision;
        [JsonProperty] public long PlayerId;
        [JsonProperty] public float CombatPoints;
        [JsonProperty] public float DisplayProgress;
        [JsonProperty] public float Contribution;
        [JsonProperty] public float LiveSkillBonusUsed;
        [JsonProperty] public long LastSkillReportSequence;
        [JsonProperty] public double MarkedAt;
        [JsonProperty] public double FightingAt;
        [JsonProperty] public double GoalReachedAt;
        [JsonProperty] public double ExitedAt;
        [JsonProperty] public double ResolvedAt;
    }

    [Serializable]
    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class BloodMoonGlobalSnapshot
    {
        [JsonProperty] public int Schema = BloodMoonStateSchema.Current;
        [JsonProperty] public long EventId = -1;
        [JsonProperty] public BloodMoonEventPhase Phase = BloodMoonEventPhase.Dormant;
        [JsonProperty] public BloodMoonResolutionStep ResolutionStep = BloodMoonResolutionStep.None;
        [JsonProperty] public BloodMoonScheduleSnapshot Schedule;
        [JsonProperty] public int Revision;
        [JsonProperty] public bool BloodBehaviorEnabled;
        [JsonProperty] public bool SpawnsStopped;
        [JsonProperty] public double ServerTime;
    }

    [Serializable]
    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class BloodMoonParticipantRoutingState
    {
        [JsonProperty] public long PlayerId;
        [JsonProperty] public string PlayerName = string.Empty;
        [JsonProperty] public BloodMoonParticipantPhase Phase;
        [JsonProperty] public BloodMoonParticipantExitReason ExitReason;
        [JsonProperty] public bool GoalReached;
        [JsonProperty] public bool AutoCompleted;
        [JsonProperty] public bool JoinedLate;

        internal static BloodMoonParticipantRoutingState FromState(BloodMoonParticipantState source)
        {
            if (source == null)
                return new BloodMoonParticipantRoutingState();

            return new BloodMoonParticipantRoutingState
            {
                PlayerId = source.PlayerId,
                PlayerName = source.PlayerName,
                Phase = source.Phase,
                ExitReason = source.ExitReason,
                GoalReached = source.GoalReached,
                AutoCompleted = source.AutoCompleted,
                JoinedLate = source.JoinedLate
            };
        }

        internal BloodMoonParticipantState ToState()
        {
            return new BloodMoonParticipantState
            {
                PlayerId = PlayerId,
                PlayerName = PlayerName,
                Phase = Phase,
                ExitReason = ExitReason,
                GoalReached = GoalReached,
                AutoCompleted = AutoCompleted,
                JoinedLate = JoinedLate
            };
        }
    }

    [Serializable]
    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class BloodMoonParticipantSnapshot
    {
        [JsonProperty] public int Schema = BloodMoonStateSchema.Current;
        [JsonProperty] public long EventId = -1;
        [JsonProperty] public int Revision;

        [JsonIgnore]
        public List<BloodMoonParticipantState> Participants = new List<BloodMoonParticipantState>();

        [JsonProperty("Participants")]
        private List<BloodMoonParticipantRoutingState> SerializedParticipants
        {
            get
            {
                return (Participants ?? new List<BloodMoonParticipantState>())
                    .Select(BloodMoonParticipantRoutingState.FromState)
                    .ToList();
            }
            set
            {
                Participants = (value ?? new List<BloodMoonParticipantRoutingState>())
                    .Select(participant => participant.ToState())
                    .ToList();
            }
        }
    }
}
