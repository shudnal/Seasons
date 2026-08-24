using System;
using System.Collections.Generic;
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
    internal sealed class BloodMoonScheduleSnapshot
    {
        public int EventWorldDay;
        public int AutumnDay;
        public double ForewarningAt;
        public double MarkedAt;
        public double ActiveAt;
        public double AutoCompleteAt;
        public double ForcedEndAt;
        public double MorningAt;

        public bool IsValid => EventWorldDay >= 0 && ForewarningAt <= MarkedAt && MarkedAt <= ActiveAt && ActiveAt <= AutoCompleteAt && AutoCompleteAt <= ForcedEndAt && ForcedEndAt <= MorningAt;
    }

    [Serializable]
    internal sealed class BloodMoonParticipantState
    {
        public long PlayerId;
        public string PlayerName = string.Empty;
        public BloodMoonParticipantPhase Phase;
        public BloodMoonParticipantExitReason ExitReason;
        public bool GoalReached;
        public bool AutoCompleted;
        public bool JoinedLate;
        public bool FadeAcknowledged;
        public float CombatPoints;
        public float DisplayProgress;
        public float Contribution;
        public double MarkedAt;
        public double FightingAt;
        public double GoalReachedAt;
        public double ExitedAt;
        public double ResolvedAt;
        public long LastSkillReportSequence;
        public Dictionary<int, float> SkillContribution = new Dictionary<int, float>();
        public Dictionary<int, float> LiveSkillBonusEquivalent = new Dictionary<int, float>();

        public bool IsCombatActive => Phase == BloodMoonParticipantPhase.Fighting || Phase == BloodMoonParticipantPhase.GoalReached;
        public bool IsTerminal => Phase == BloodMoonParticipantPhase.Exited || Phase == BloodMoonParticipantPhase.Resolved;
    }

    [Serializable]
    internal sealed class BloodMoonParticipantDetailSnapshot
    {
        public int Schema = BloodMoonStateSchema.Current;
        public long EventId = -1L;
        public int Revision;
        public long PlayerId;
        public float CombatPoints;
        public float DisplayProgress;
        public float Contribution;
        public float LiveSkillBonusUsed;
        public long LastSkillReportSequence;
        public double MarkedAt;
        public double FightingAt;
        public double GoalReachedAt;
        public double ExitedAt;
        public double ResolvedAt;
    }

    [Serializable]
    internal sealed class BloodMoonGroupState
    {
        public long GroupId;
        public int Revision;
        public List<long> MemberPlayerIds = new List<long>();
        public Vector3 Anchor;
        public float CombatPoints;
        public int ExtraEnemyCount;
        public double UpdatedAt;
    }

    [Serializable]
    internal sealed class BloodMoonSpawnLeaseState
    {
        public long EventId;
        public long GroupId;
        public int GroupRevision;
        public int ZoneX;
        public int ZoneY;
        public long OwnerPeerId;
        public long OwnerSessionId;
        public int LeaseRevision;
        public Vector3 Anchor;
        public int Allowance;
        public int GroupCap;
        public int ServerHardCap;
        public int PoolRevision;
        public double ExpiresAt;
    }

    [Serializable]
    internal sealed class BloodMoonBossParkingState
    {
        public string ZdoId = string.Empty;
        public Vector3 OriginalPosition;
        public Quaternion OriginalRotation = Quaternion.identity;
        public long OriginalOwner;
        public uint OriginalDataRevision;
        public ushort OriginalOwnerRevision;
        public bool WasLoaded;
        public bool Restored;
    }

    [Serializable]
    internal sealed class BloodMoonEventState
    {
        public int Schema = BloodMoonStateSchema.Current;
        public long WorldUid;
        public long EventId = -1;
        public BloodMoonEventPhase Phase = BloodMoonEventPhase.Dormant;
        public BloodMoonResolutionStep ResolutionStep = BloodMoonResolutionStep.None;
        public BloodMoonScheduleSnapshot Schedule;
        public long LastCreatedEventId = -1;
        public long LastResolvedEventId = -1;
        public double FirstEnabledAt;
        public int Revision;
        public int GroupSequence;
        public int LeaseSequence;
        public bool EnrollmentFrozen;
        public bool SpawnsStopped;
        public bool BloodBehaviorEnabled;
        public bool ResolutionCancelledBeforeCombat;
        public double UpdatedAt;
        public Dictionary<long, BloodMoonParticipantState> Participants = new Dictionary<long, BloodMoonParticipantState>();
        public Dictionary<long, BloodMoonGroupState> Groups = new Dictionary<long, BloodMoonGroupState>();
        public Dictionary<string, BloodMoonSpawnLeaseState> SpawnLeases = new Dictionary<string, BloodMoonSpawnLeaseState>();
        public HashSet<string> ReportedEnemyDeaths = new HashSet<string>();
        public HashSet<string> ExtraEnemyZdos = new HashSet<string>();
        public Dictionary<string, BloodMoonBossParkingState> ParkedBosses = new Dictionary<string, BloodMoonBossParkingState>();

        public bool IsEventLive => Phase == BloodMoonEventPhase.Forewarning || Phase == BloodMoonEventPhase.Marked || Phase == BloodMoonEventPhase.Active || Phase == BloodMoonEventPhase.AutoCompleting || Phase == BloodMoonEventPhase.Resolving;
        public bool IsCombatLive => Phase == BloodMoonEventPhase.Active || Phase == BloodMoonEventPhase.AutoCompleting;
    }

    [Serializable]
    internal sealed class BloodMoonGlobalSnapshot
    {
        public int Schema = BloodMoonStateSchema.Current;
        public long EventId = -1;
        public BloodMoonEventPhase Phase = BloodMoonEventPhase.Dormant;
        public BloodMoonResolutionStep ResolutionStep = BloodMoonResolutionStep.None;
        public BloodMoonScheduleSnapshot Schedule;
        public int Revision;
        public bool BloodBehaviorEnabled;
        public bool SpawnsStopped;
        public double ServerTime;
    }

    [Serializable]
    internal sealed class BloodMoonParticipantSnapshot
    {
        public int Schema = BloodMoonStateSchema.Current;
        public long EventId = -1;
        public int Revision;
        public List<BloodMoonParticipantState> Participants = new List<BloodMoonParticipantState>();
    }

    internal static class BloodMoonStateSchema
    {
        internal const int Current = 1;
    }
}
