using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Seasons.BloodMoon
{
    internal enum BloodMoonEventPhase
    {
        Dormant,
        Forewarning,
        Marked,
        Active,
        AutoCompleting,
        Resolving,
        Resolved,
        Skipped
    }

    internal enum BloodMoonParticipantPhase
    {
        None,
        Marked,
        Fighting,
        GoalReached,
        Exited,
        Resolved
    }

    internal enum BloodMoonParticipantExitReason
    {
        None,
        Defeated,
        Withdrawn,
        Disconnected
    }

    internal enum BloodMoonResolutionStep
    {
        None,
        FreezingEnrollment,
        StoppingSpawns,
        AwaitingClientFade,
        DisablingBloodBehavior,
        CleaningExtraEnemies,
        RestoringBosses,
        CleaningTemporaryItems,
        RestoringWorldSystems,
        AdvancingTime,
        PublishingOutcomes,
        ReleasingClients,
        Complete
    }

    internal enum BloodMoonRewardMode
    {
        None,
        CompletionOnly,
        LimitedTripleGainOnly,
        Hybrid
    }

    internal enum BloodMoonExtraEnemyRole
    {
        Surface,
        Interior
    }

    [Serializable]
    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class BloodMoonScheduleSnapshot
    {
        [JsonProperty] public int EventWorldDay;
        [JsonProperty] public int AutumnDay;
        [JsonProperty] public double ForewarningAt;
        [JsonProperty] public double MarkedAt;
        [JsonProperty] public double ActiveAt;
        [JsonProperty] public double AutoCompleteAt;
        [JsonProperty] public double ForcedEndAt;
        [JsonProperty] public double MorningAt;

        public bool IsValid => EventWorldDay >= 0 && ForewarningAt <= MarkedAt && MarkedAt <= ActiveAt &&
            ActiveAt <= AutoCompleteAt && AutoCompleteAt <= ForcedEndAt && ForcedEndAt <= MorningAt;
    }

    [Serializable]
    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class BloodMoonParticipantState
    {
        [JsonProperty] public long PlayerId;
        [JsonProperty] public string PlayerName = string.Empty;
        [JsonProperty] public BloodMoonParticipantPhase Phase;
        [JsonProperty] public BloodMoonParticipantExitReason ExitReason;
        [JsonProperty] public bool GoalReached;
        [JsonProperty] public bool AutoCompleted;
        [JsonProperty] public bool JoinedLate;
        [JsonProperty] public bool FadeAcknowledged;
        [JsonProperty] public float CombatPoints;
        [JsonProperty] public float DisplayProgress;
        [JsonProperty] public float Contribution;
        [JsonProperty] public double MarkedAt;
        [JsonProperty] public double FightingAt;
        [JsonProperty] public double GoalReachedAt;
        [JsonProperty] public double ExitedAt;
        [JsonProperty] public double ResolvedAt;
        [JsonProperty] public long LastSkillReportSequence;
        [JsonProperty] public Dictionary<int, float> SkillContribution = new Dictionary<int, float>();
        [JsonProperty] public Dictionary<int, float> LiveSkillBonusEquivalent = new Dictionary<int, float>();

        // The ledger remains the single durable source; snapshots project its total without
        // introducing a second independently persisted budget counter.
        [JsonIgnore]
        public float LiveSkillBonusUsed => LiveSkillBonusEquivalent?.Values.Sum() ?? 0f;

        public bool IsCombatActive => Phase == BloodMoonParticipantPhase.Fighting || Phase == BloodMoonParticipantPhase.GoalReached;
        public bool IsTerminal => Phase == BloodMoonParticipantPhase.Exited || Phase == BloodMoonParticipantPhase.Resolved;
    }

    [Serializable]
    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class BloodMoonGroupState
    {
        [JsonProperty] public long GroupId;
        [JsonProperty] public int Revision;
        [JsonProperty] public List<long> MemberPlayerIds = new List<long>();
        [JsonProperty] public Vector3 Anchor;
        [JsonProperty] public float CombatPoints;
        [JsonProperty] public int ExtraEnemyCount;
        [JsonProperty] public double UpdatedAt;
    }

    [Serializable]
    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class BloodMoonSpawnLeaseState
    {
        [JsonProperty] public long EventId;
        [JsonProperty] public long GroupId;
        [JsonProperty] public int GroupRevision;
        [JsonProperty] public int ZoneX;
        [JsonProperty] public int ZoneY;
        [JsonProperty] public long OwnerPeerId;
        [JsonProperty] public long OwnerSessionId;
        [JsonProperty] public int LeaseRevision;
        [JsonProperty] public Vector3 Anchor;
        [JsonProperty] public int Allowance;
        [JsonProperty] public int GroupCap;
        [JsonProperty] public int ServerHardCap;
        [JsonProperty] public int PoolRevision;
        [JsonProperty] public double ExpiresAt;
    }

    [Serializable]
    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class BloodMoonEventState
    {
        [JsonProperty] public int Schema = BloodMoonStateSchema.Current;
        [JsonProperty] public long WorldUid;
        [JsonProperty] public long PersistenceGeneration;
        [JsonProperty] public long EventId = -1;
        [JsonProperty] public BloodMoonEventPhase Phase = BloodMoonEventPhase.Dormant;
        [JsonProperty] public BloodMoonResolutionStep ResolutionStep = BloodMoonResolutionStep.None;
        [JsonProperty] public BloodMoonScheduleSnapshot Schedule;
        [JsonProperty] public long LastCreatedEventId = -1;
        [JsonProperty] public long LastResolvedEventId = -1;
        [JsonProperty] public double FirstEnabledAt;
        [JsonProperty] public int Revision;
        [JsonProperty] public int GroupSequence;
        [JsonProperty] public int LeaseSequence;
        [JsonProperty] public bool EnrollmentFrozen;
        [JsonProperty] public bool SpawnsStopped;
        [JsonProperty] public bool BloodBehaviorEnabled;
        [JsonProperty] public bool ResolutionCancelledBeforeCombat;
        [JsonProperty] public bool SpawnPoolFrozen;
        [JsonProperty] public string ExtraEnemyPrefab = string.Empty;
        [JsonProperty] public double UpdatedAt;
        [JsonProperty] public Dictionary<long, BloodMoonParticipantState> Participants = new Dictionary<long, BloodMoonParticipantState>();
        [JsonProperty] public Dictionary<long, BloodMoonGroupState> Groups = new Dictionary<long, BloodMoonGroupState>();
        [JsonProperty] public Dictionary<string, BloodMoonSpawnLeaseState> SpawnLeases = new Dictionary<string, BloodMoonSpawnLeaseState>();
        [JsonProperty] public HashSet<string> ReportedEnemyDeaths = new HashSet<string>();
        [JsonProperty] public HashSet<string> ExtraEnemyZdos = new HashSet<string>();

        // Compatibility-only runtime projection for existing status output. It is rebuilt from ZDO markers
        // on every access and is excluded from the durable JSON contract.
        [JsonIgnore]
        public IReadOnlyDictionary<string, BloodMoonBossParkingDiagnostic> ParkedBosses => BloodMoonBosses.GetParkingDiagnostics();

        public bool IsEventLive => Phase == BloodMoonEventPhase.Forewarning || Phase == BloodMoonEventPhase.Marked ||
            Phase == BloodMoonEventPhase.Active || Phase == BloodMoonEventPhase.AutoCompleting ||
            Phase == BloodMoonEventPhase.Resolving;
        public bool IsCombatLive => Phase == BloodMoonEventPhase.Active || Phase == BloodMoonEventPhase.AutoCompleting;
    }

    internal static class BloodMoonStateSchema
    {
        internal const int Current = 1;
    }
}
