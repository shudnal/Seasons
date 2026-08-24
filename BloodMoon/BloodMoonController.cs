using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal sealed class BloodMoonController : MonoBehaviour
    {
        private const float ServerTickInterval = 0.5f;
        private const float GroupTickInterval = 4f;
        private const float OutcomeReplayInterval = 5f;
        private const float FadeAckTimeout = 4f;
        private const float WorldEdgeWithdrawRadius = 10400f;

        internal static BloodMoonController Instance { get; private set; }
        internal BloodMoonEventState State { get; private set; }

        private long loadedWorldUid = long.MinValue;
        private float serverTickTimer;
        private float groupTickTimer;
        private float outcomeReplayTimer;
        private double resolutionStepStartedAt;
        private bool worldSaveSubscribed;
        private bool hadWorld;

        internal static void Attach(GameObject host)
        {
            if (host == null || host.GetComponent<BloodMoonController>() != null)
                return;
            host.AddComponent<BloodMoonController>();
        }

        private void Awake()
        {
            Instance = this;
            BloodMoonNetwork.InitializeValues();
        }

        private void OnDestroy()
        {
            if (worldSaveSubscribed)
                ZNet.WorldSaveStarted -= SaveNow;
            if (State != null && ZNet.instance != null && ZNet.instance.IsServer())
                BloodMoonPersistence.Save(State);
            CleanupClientWorldState();
            Instance = null;
        }

        private void FixedUpdate()
        {
            BloodMoonNetwork.RegisterRpcs();
            BloodMoonPresentation.Tick(Time.fixedDeltaTime);
            BloodMoonSpawner.TickClient(Time.fixedDeltaTime);
            BloodMoonRecovery.TickClientProtection(Time.fixedDeltaTime);
            BloodCraft.TickLocal();

            if (ZNet.instance == null || ZNet.m_world == null || !SeasonState.IsActive)
            {
                if (hadWorld)
                {
                    hadWorld = false;
                    CleanupClientWorldState();
                    State = null;
                    loadedWorldUid = long.MinValue;
                }
                return;
            }

            hadWorld = true;
            EnsureWorldLoaded();
            if (!ZNet.instance.IsServer() || State == null)
                return;

            serverTickTimer -= Time.fixedDeltaTime;
            if (serverTickTimer > 0f)
                return;
            serverTickTimer = ServerTickInterval;

            double now = seasonState.GetTotalSeconds();
            TickServer(now);
        }

        private void EnsureWorldLoaded()
        {
            long worldUid = ZNet.m_world.m_uid;
            if (worldUid == loadedWorldUid && State != null)
                return;

            if (State != null && ZNet.instance.IsServer())
                BloodMoonPersistence.Save(State);

            CleanupClientWorldState();
            loadedWorldUid = worldUid;
            State = ZNet.instance.IsServer() ? BloodMoonPersistence.Load(worldUid) : null;

            if (ZNet.instance.IsServer())
            {
                RecoverServerState();
                PublishState(force: true);
                if (!worldSaveSubscribed)
                {
                    ZNet.WorldSaveStarted += SaveNow;
                    worldSaveSubscribed = true;
                }
            }

            BloodMoonPresentation.OnWorldChanged();
        }

        private void CleanupClientWorldState()
        {
            BloodCraft.CleanupLocal(Player.m_localPlayer);
            BloodMoonEnvironment.ReleaseForcedEnvironment();
            BloodMoonPresentation.CleanupTransientState();
            BloodMoonSpawner.ResetClientState();
            BloodMoonHitAttribution.Reset();
            if (BloodMoonNetwork.ClientGlobal.EventId >= 0L)
                BloodMoonSkills.ResetLocal(BloodMoonNetwork.ClientGlobal.EventId);
        }

        private void TickServer(double now)
        {
            if (!BloodMoonConfig.Enabled.Value)
            {
                if (State.IsEventLive && State.Phase != BloodMoonEventPhase.Resolving)
                    BeginResolution("feature disabled", now);
                if (State.Phase == BloodMoonEventPhase.Resolving)
                    TickResolution(now);
                return;
            }

            EnsureFirstEnabledAt(now);

            if (State.Phase == BloodMoonEventPhase.Resolving)
            {
                TickResolution(now);
                return;
            }

            if (State.Phase == BloodMoonEventPhase.Resolved)
            {
                outcomeReplayTimer -= ServerTickInterval;
                if (outcomeReplayTimer <= 0f)
                {
                    outcomeReplayTimer = OutcomeReplayInterval;
                    ReplayResolvedOutcomes();
                }
            }

            if (State.Phase == BloodMoonEventPhase.Dormant || State.Phase == BloodMoonEventPhase.Resolved || State.Phase == BloodMoonEventPhase.Skipped)
                TryCreateScheduledEvent(now);

            if (State.Schedule == null || State.EventId < 0L || !State.IsEventLive)
                return;

            BloodMoonEventPhase expected = BloodMoonSchedule.GetExpectedPhase(State.Schedule, now);
            AdvanceToExpectedPhase(expected, now);
            if (State.Phase == BloodMoonEventPhase.Resolving)
            {
                TickResolution(now);
                return;
            }

            if (State.Phase == BloodMoonEventPhase.Marked || State.IsCombatLive)
                UpdateConnectedParticipants(now);

            if (!State.IsCombatLive)
                return;

            BloodMoonBosses.Tick(State, now);
            WithdrawPlayersAtWorldEdge(now);
            UpdateAutomaticProgress(now);

            groupTickTimer -= ServerTickInterval;
            if (groupTickTimer <= 0f)
            {
                groupTickTimer = GroupTickInterval;
                BloodMoonGroups.Rebuild(State, now);
                BloodMoonSpawner.UpdateServerLeases(State, now);
            }

            if (CanResolveEarly())
                BeginResolution("all enrolled participants completed or exited", now);
        }

        private void EnsureFirstEnabledAt(double now)
        {
            if (State == null || State.FirstEnabledAt > 0d)
                return;
            State.FirstEnabledAt = now;
            Touch(now, persist: true, publish: false);
            LogInfo($"[BloodMoon.Persistence] First enabled observation recorded at {now:0.###} for world {State.WorldUid}.");
        }

        private void TryCreateScheduledEvent(double now)
        {
            BloodMoonScheduleSnapshot schedule = BloodMoonSchedule.FindCurrentOrNext(now);
            if (schedule == null || schedule.EventWorldDay == State.LastCreatedEventId || schedule.EventWorldDay == State.LastResolvedEventId)
                return;

            BloodMoonEventPhase expected = BloodMoonSchedule.GetExpectedPhase(schedule, now);
            if (expected == BloodMoonEventPhase.Dormant || expected == BloodMoonEventPhase.Resolved)
                return;

            bool noEventHistory = State.EventId < 0L && State.LastCreatedEventId < 0L && State.LastResolvedEventId < 0L;
            bool firstEnabledInsideCurrentWindow = noEventHistory && State.FirstEnabledAt >= schedule.ForewarningAt && State.FirstEnabledAt < schedule.MorningAt;
            if (firstEnabledInsideCurrentWindow)
            {
                double firstEnabledAt = State.FirstEnabledAt;
                State = BloodMoonPersistence.CreateClean(loadedWorldUid);
                State.FirstEnabledAt = firstEnabledAt;
                State.EventId = schedule.EventWorldDay;
                State.LastCreatedEventId = schedule.EventWorldDay;
                State.LastResolvedEventId = schedule.EventWorldDay;
                State.Schedule = schedule;
                State.Phase = BloodMoonEventPhase.Skipped;
                Touch(now, persist: true, publish: true);
                LogInfo($"[BloodMoon][event:{State.EventId}][phase] Skipped first enabled event because enablement occurred inside its forewarning/final-night window.");
                return;
            }

            long previousCreated = State.LastCreatedEventId;
            long previousResolved = State.LastResolvedEventId;
            double firstEnabled = State.FirstEnabledAt;
            State = BloodMoonPersistence.CreateClean(loadedWorldUid);
            State.FirstEnabledAt = firstEnabled;
            State.EventId = schedule.EventWorldDay;
            State.LastCreatedEventId = schedule.EventWorldDay;
            State.LastResolvedEventId = previousResolved;
            State.Schedule = schedule;
            State.Phase = BloodMoonEventPhase.Forewarning;
            State.BloodBehaviorEnabled = false;
            State.SpawnsStopped = false;
            State.EnrollmentFrozen = false;
            State.Revision = 0;
            if (previousCreated > State.EventId)
                LogWarning($"[BloodMoon][event:{State.EventId}][phase] Previous event id {previousCreated} is newer than scheduled event id.");
            Touch(now, persist: true, publish: true);
            BloodMoonPresentation.EnsureEnvironmentRegistered();
            LogInfo($"[BloodMoon][event:{State.EventId}][phase] Created Forewarning schedule for autumn day {schedule.AutumnDay}.");
        }

        private void AdvanceToExpectedPhase(BloodMoonEventPhase expected, double now)
        {
            if (expected == BloodMoonEventPhase.Resolving || expected == BloodMoonEventPhase.Resolved)
            {
                if (State.Phase != BloodMoonEventPhase.Resolving && State.Phase != BloodMoonEventPhase.Resolved)
                    BeginResolution("schedule forced end", now);
                return;
            }

            if (State.Phase == BloodMoonEventPhase.Forewarning &&
                (expected == BloodMoonEventPhase.Marked || expected == BloodMoonEventPhase.Active || expected == BloodMoonEventPhase.AutoCompleting))
                EnterMarked(now);

            if (State.Phase == BloodMoonEventPhase.Marked &&
                (expected == BloodMoonEventPhase.Active || expected == BloodMoonEventPhase.AutoCompleting))
                EnterActive(now);

            if (State.Phase == BloodMoonEventPhase.Active && expected == BloodMoonEventPhase.AutoCompleting)
                EnterAutoCompleting(now);
        }

        private void EnterMarked(double now)
        {
            if (State == null || State.Phase != BloodMoonEventPhase.Forewarning)
                return;

            State.Phase = BloodMoonEventPhase.Marked;
            State.EnrollmentFrozen = false;
            foreach (ConnectedPlayer connected in GetConnectedPlayers())
                Enroll(connected, now, joinedLate: false);
            BloodMoonRandEventSuppression.StopActiveRandomEvent();
            Touch(now, persist: true, publish: true);
            LogInfo($"[BloodMoon][event:{State.EventId}][phase] Entered Marked with {State.Participants.Count} participant(s).");
        }

        private void EnterActive(double now)
        {
            if (State == null || State.Phase != BloodMoonEventPhase.Marked)
                return;

            State.Phase = BloodMoonEventPhase.Active;
            State.BloodBehaviorEnabled = true;
            State.SpawnsStopped = false;
            foreach (BloodMoonParticipantState participant in State.Participants.Values.Where(item => item.Phase == BloodMoonParticipantPhase.Marked))
            {
                participant.Phase = BloodMoonParticipantPhase.Fighting;
                participant.FightingAt = now;
            }
            BloodMoonEnvironment.AcquireForcedEnvironment();
            BloodMoonRandEventSuppression.StopActiveRandomEvent();
            BloodMoonGroups.Rebuild(State, now);
            BloodMoonSpawner.UpdateServerLeases(State, now);
            Touch(now, persist: true, publish: true);
            LogInfo($"[BloodMoon][event:{State.EventId}][phase] Entered Active.");
        }

        private void EnterAutoCompleting(double now)
        {
            if (State == null || State.Phase != BloodMoonEventPhase.Active)
                return;
            State.Phase = BloodMoonEventPhase.AutoCompleting;
            Touch(now, persist: true, publish: true);
            LogInfo($"[BloodMoon][event:{State.EventId}][phase] Entered AutoCompleting.");
        }

        private void UpdateAutomaticProgress(double now)
        {
            if (State.Phase != BloodMoonEventPhase.AutoCompleting || State.Schedule == null)
                return;

            double duration = Math.Max(1d, State.Schedule.ForcedEndAt - State.Schedule.AutoCompleteAt);
            float automaticFloor = Mathf.Clamp01((float)((now - State.Schedule.AutoCompleteAt) / duration)) * 100f;
            bool changed = false;
            foreach (BloodMoonParticipantState participant in State.Participants.Values)
            {
                if (!participant.IsCombatActive || participant.GoalReached)
                    continue;
                float combatProgress = GetCombatProgress(participant);
                float display = Mathf.Max(combatProgress, automaticFloor);
                if (Mathf.Abs(display - participant.DisplayProgress) >= 0.05f)
                {
                    participant.DisplayProgress = display;
                    changed = true;
                }
            }
            if (changed)
                Touch(now, persist: false, publish: true);
        }

        private void CompleteAutomaticProgressAtForcedEnd(double now)
        {
            if (State.Schedule == null || now < State.Schedule.ForcedEndAt)
                return;
            foreach (BloodMoonParticipantState participant in State.Participants.Values)
            {
                if (!participant.IsCombatActive || participant.GoalReached)
                    continue;
                participant.AutoCompleted = true;
                participant.DisplayProgress = 100f;
            }
        }

        private void UpdateConnectedParticipants(double now)
        {
            List<ConnectedPlayer> connected = GetConnectedPlayers();
            HashSet<long> connectedIds = new HashSet<long>(connected.Select(player => player.PlayerId));

            foreach (ConnectedPlayer player in connected)
            {
                if (!State.Participants.ContainsKey(player.PlayerId) && !State.EnrollmentFrozen)
                    Enroll(player, now, joinedLate: State.Phase != BloodMoonEventPhase.Marked);
            }

            foreach (BloodMoonParticipantState participant in State.Participants.Values.ToArray())
            {
                if (participant.Phase != BloodMoonParticipantPhase.Marked && !participant.IsCombatActive)
                    continue;
                if (!connectedIds.Contains(participant.PlayerId))
                    ExitParticipant(participant, BloodMoonParticipantExitReason.Disconnected, now);
            }
        }

        private void Enroll(ConnectedPlayer connected, double now, bool joinedLate)
        {
            if (connected == null || connected.PlayerId == 0L || State.EnrollmentFrozen || State.Participants.ContainsKey(connected.PlayerId))
                return;

            BloodMoonParticipantState participant = new BloodMoonParticipantState
            {
                PlayerId = connected.PlayerId,
                PlayerName = connected.Name,
                Phase = State.Phase == BloodMoonEventPhase.Marked ? BloodMoonParticipantPhase.Marked : BloodMoonParticipantPhase.Fighting,
                JoinedLate = joinedLate,
                MarkedAt = now,
                FightingAt = State.Phase == BloodMoonEventPhase.Marked ? 0d : now
            };
            State.Participants.Add(participant.PlayerId, participant);
            DispatchClientAction(participant.PlayerId, connected.PeerId, "enroll");
            Touch(now, persist: true, publish: true);
            LogInfo($"[BloodMoon][event:{State.EventId}][player:{participant.PlayerId}] Enrolled '{participant.PlayerName}', late={joinedLate}.");
        }

        internal void OnDefeatedReport(long sender, long eventId, long playerId)
        {
            if (!ValidateSender(sender, eventId, playerId, out BloodMoonParticipantState participant) || !participant.IsCombatActive)
                return;
            ExitParticipant(participant, BloodMoonParticipantExitReason.Defeated, seasonState.GetTotalSeconds());
            DispatchClientAction(playerId, GetPeerForPlayer(playerId), "defeated");
        }

        internal void WithdrawLocalOrRequested(long playerId)
        {
            if (State == null || !State.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant) || !participant.IsCombatActive)
                return;
            ExitParticipant(participant, BloodMoonParticipantExitReason.Withdrawn, seasonState.GetTotalSeconds());
            DispatchClientAction(playerId, GetPeerForPlayer(playerId), "withdrawn");
        }

        private void ExitParticipant(BloodMoonParticipantState participant, BloodMoonParticipantExitReason reason, double now)
        {
            if (participant == null || participant.IsTerminal)
                return;
            participant.Phase = BloodMoonParticipantPhase.Exited;
            participant.ExitReason = reason;
            participant.ExitedAt = now;
            BloodMoonGroups.RemovePlayer(State, participant.PlayerId);
            Touch(now, persist: true, publish: true);
            LogInfo($"[BloodMoon][event:{State.EventId}][player:{participant.PlayerId}] Exited: {reason}; goal={participant.GoalReached}; combat={GetCombatProgress(participant):0.##}%.");
        }

        private void WithdrawPlayersAtWorldEdge(double now)
        {
            foreach (BloodMoonParticipantState participant in State.Participants.Values.Where(item => item.IsCombatActive).ToArray())
            {
                if (!TryGetConnectedPosition(participant.PlayerId, out Vector3 position) || Utils.LengthXZ(position) < WorldEdgeWithdrawRadius)
                    continue;
                ExitParticipant(participant, BloodMoonParticipantExitReason.Withdrawn, now);
                DispatchClientAction(participant.PlayerId, GetPeerForPlayer(participant.PlayerId), "withdrawn");
                LogWarning($"[BloodMoon][event:{State.EventId}][player:{participant.PlayerId}] Withdrawn at world-edge safety radius ({Utils.LengthXZ(position):0.#}m).");
            }
        }

        internal void OnEnemyDeathReport(long sender, long eventId, long playerId, ZDOID enemyId, float clientPoints)
        {
            if (!ValidateSender(sender, eventId, playerId, out BloodMoonParticipantState participant) || !participant.IsCombatActive || enemyId.IsNone())
                return;

            string key = enemyId.ToString();
            if (!State.ReportedEnemyDeaths.Add(key))
                return;

            float points = BloodMoonCombat.GetPointsForEnemy(enemyId, clientPoints);
            if (points <= 0f)
                return;

            AwardPoints(participant, points, seasonState.GetTotalSeconds());
        }

        private void AwardPoints(BloodMoonParticipantState source, float points, double now)
        {
            BloodMoonGroupState group = BloodMoonGroups.FindForPlayer(State, source.PlayerId);
            List<BloodMoonParticipantState> recipients = new List<BloodMoonParticipantState>();
            float shareRadius = Mathf.Max(0f, BloodMoonConfig.ProgressShareRadius.Value);

            if (group != null && TryGetConnectedPosition(source.PlayerId, out Vector3 sourcePosition))
            {
                foreach (long id in group.MemberPlayerIds)
                {
                    if (!State.Participants.TryGetValue(id, out BloodMoonParticipantState participant) || !participant.IsCombatActive)
                        continue;
                    if (id == source.PlayerId || TryGetConnectedPosition(id, out Vector3 position) && Utils.DistanceXZ(sourcePosition, position) <= shareRadius)
                        recipients.Add(participant);
                }
            }

            if (recipients.Count == 0)
                recipients.Add(source);

            float share = points / recipients.Count;
            foreach (BloodMoonParticipantState participant in recipients)
            {
                participant.CombatPoints += share;
                if (participant.PlayerId == source.PlayerId)
                    participant.Contribution += points;

                float combatProgress = GetCombatProgress(participant);
                float automaticFloor = GetAutomaticFloor(now);
                participant.DisplayProgress = Mathf.Max(combatProgress, automaticFloor);
                if (!participant.GoalReached && combatProgress >= 100f)
                {
                    participant.GoalReached = true;
                    participant.Phase = BloodMoonParticipantPhase.GoalReached;
                    participant.GoalReachedAt = now;
                    participant.DisplayProgress = 100f;
                    LogInfo($"[BloodMoon][event:{State.EventId}][player:{participant.PlayerId}] GoalReached at {participant.CombatPoints:0.##} combat points.");
                }
            }

            if (group != null)
                group.CombatPoints += points;
            Touch(now, persist: true, publish: true);
        }

        private float GetAutomaticFloor(double now)
        {
            if (State.Phase != BloodMoonEventPhase.AutoCompleting || State.Schedule == null)
                return 0f;
            double duration = Math.Max(1d, State.Schedule.ForcedEndAt - State.Schedule.AutoCompleteAt);
            return Mathf.Clamp01((float)((now - State.Schedule.AutoCompleteAt) / duration)) * 100f;
        }

        private static float GetCombatProgress(BloodMoonParticipantState participant)
        {
            return participant == null ? 0f : Mathf.Clamp(participant.CombatPoints / Mathf.Max(1f, BloodMoonConfig.GoalPoints.Value) * 100f, 0f, 100f);
        }

        private bool CanResolveEarly()
        {
            if (State.Participants.Count == 0)
                return false;
            return State.Participants.Values.All(participant => participant.GoalReached || participant.IsTerminal);
        }

        internal void OnFadeAcknowledged(long sender, long eventId, long playerId)
        {
            if (!ValidateSender(sender, eventId, playerId, out BloodMoonParticipantState participant))
                return;
            participant.FadeAcknowledged = true;
            Touch(seasonState.GetTotalSeconds(), persist: false, publish: true);
        }

        internal void BeginResolution(string reason, double now)
        {
            if (State == null || State.Phase == BloodMoonEventPhase.Resolving || State.Phase == BloodMoonEventPhase.Resolved)
                return;

            CompleteAutomaticProgressAtForcedEnd(now);
            State.Phase = BloodMoonEventPhase.Resolving;
            State.ResolutionStep = BloodMoonResolutionStep.FreezingEnrollment;
            State.EnrollmentFrozen = true;
            resolutionStepStartedAt = now;
            Touch(now, persist: true, publish: true);
            LogInfo($"[BloodMoon][event:{State.EventId}][resolution] Started: {reason}.");
        }

        private void TickResolution(double now)
        {
            switch (State.ResolutionStep)
            {
                case BloodMoonResolutionStep.None:
                case BloodMoonResolutionStep.FreezingEnrollment:
                    State.EnrollmentFrozen = true;
                    SetResolutionStep(BloodMoonResolutionStep.StoppingSpawns, now);
                    break;

                case BloodMoonResolutionStep.StoppingSpawns:
                    State.SpawnsStopped = true;
                    BloodMoonSpawner.StopServerLeases(State);
                    SendFadeToParticipants(begin: true);
                    SetResolutionStep(BloodMoonResolutionStep.AwaitingClientFade, now);
                    break;

                case BloodMoonResolutionStep.AwaitingClientFade:
                    if (AllConnectedParticipantsAcknowledgedFade() || resolutionStepStartedAt <= 0d || now - resolutionStepStartedAt >= FadeAckTimeout)
                        SetResolutionStep(BloodMoonResolutionStep.DisablingBloodBehavior, now);
                    break;

                case BloodMoonResolutionStep.DisablingBloodBehavior:
                    State.BloodBehaviorEnabled = false;
                    SetResolutionStep(BloodMoonResolutionStep.CleaningExtraEnemies, now);
                    break;

                case BloodMoonResolutionStep.CleaningExtraEnemies:
                    BloodMoonSpawner.CleanupExtraEnemies(State);
                    SetResolutionStep(BloodMoonResolutionStep.RestoringBosses, now);
                    break;

                case BloodMoonResolutionStep.RestoringBosses:
                    BloodMoonBosses.RestoreAll(State);
                    SetResolutionStep(BloodMoonResolutionStep.CleaningTemporaryItems, now);
                    break;

                case BloodMoonResolutionStep.CleaningTemporaryItems:
                    BroadcastClientAction("cleanup-craft");
                    BloodCraft.CleanupLocal(Player.m_localPlayer);
                    SetResolutionStep(BloodMoonResolutionStep.RestoringWorldSystems, now);
                    break;

                case BloodMoonResolutionStep.RestoringWorldSystems:
                    BloodMoonEnvironment.ReleaseForcedEnvironment();
                    BloodMoonRandEventSuppression.Release();
                    SetResolutionStep(BloodMoonResolutionStep.AdvancingTime, now);
                    break;

                case BloodMoonResolutionStep.AdvancingTime:
                    AdvanceToFrozenMorning(now);
                    SetResolutionStep(BloodMoonResolutionStep.PublishingOutcomes, now);
                    break;

                case BloodMoonResolutionStep.PublishingOutcomes:
                    PublishOutcomes();
                    SetResolutionStep(BloodMoonResolutionStep.ReleasingClients, now);
                    break;

                case BloodMoonResolutionStep.ReleasingClients:
                    BroadcastClientAction("resolution-complete");
                    SendFadeToParticipants(begin: false);
                    foreach (BloodMoonParticipantState participant in State.Participants.Values)
                    {
                        participant.Phase = BloodMoonParticipantPhase.Resolved;
                        participant.ResolvedAt = now;
                    }
                    SetResolutionStep(BloodMoonResolutionStep.Complete, now);
                    break;

                case BloodMoonResolutionStep.Complete:
                    State.LastResolvedEventId = State.EventId;
                    State.Phase = BloodMoonEventPhase.Resolved;
                    State.BloodBehaviorEnabled = false;
                    State.SpawnsStopped = true;
                    Touch(now, persist: true, publish: true);
                    outcomeReplayTimer = 0f;
                    LogInfo($"[BloodMoon][event:{State.EventId}][resolution] Complete.");
                    break;
            }
        }

        private void SetResolutionStep(BloodMoonResolutionStep step, double now)
        {
            State.ResolutionStep = step;
            resolutionStepStartedAt = now;
            Touch(now, persist: true, publish: true);
            LogInfo($"[BloodMoon][event:{State.EventId}][resolution] Step -> {step}.");
        }

        private void AdvanceToFrozenMorning(double now)
        {
            if (State.Schedule == null || ZNet.instance == null || !ZNet.instance.IsServer())
                return;
            if (now < State.Schedule.MorningAt)
            {
                ZNet.instance.SetNetTime(State.Schedule.MorningAt);
                EnvManPatches.sleepingUpdated = true;
                LogInfo($"[BloodMoon][event:{State.EventId}][resolution] Advanced net time to frozen morning target {State.Schedule.MorningAt:0.###}.");
            }
        }

        private void PublishOutcomes()
        {
            foreach (BloodMoonParticipantState participant in State.Participants.Values)
                DispatchOutcome(participant);
        }

        private void ReplayResolvedOutcomes()
        {
            if (State.Phase != BloodMoonEventPhase.Resolved)
                return;
            HashSet<long> connected = new HashSet<long>(GetConnectedPlayers().Select(player => player.PlayerId));
            foreach (BloodMoonParticipantState participant in State.Participants.Values.Where(participant => connected.Contains(participant.PlayerId)))
                DispatchOutcome(participant);
        }

        private void DispatchOutcome(BloodMoonParticipantState participant)
        {
            if (participant == null)
                return;
            long peer = GetPeerForPlayer(participant.PlayerId);
            string rewardPayload = BloodMoonSkills.SerializeCompletionReward(participant);
            string chronicle = BloodMoonPresentation.BuildChronicle(participant);

            DispatchClientAction(participant.PlayerId, peer, "reward", rewardPayload);
            DispatchClientAction(participant.PlayerId, peer, "chronicle", chronicle);
            DispatchClientAction(participant.PlayerId, peer, "remove-rested");
        }

        private void SendFadeToParticipants(bool begin)
        {
            foreach (BloodMoonParticipantState participant in State.Participants.Values)
            {
                participant.FadeAcknowledged = false;
                if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == participant.PlayerId)
                {
                    BloodMoonPresentation.SetResolutionFade(begin);
                    participant.FadeAcknowledged = true;
                    continue;
                }

                long peer = GetPeerForPlayer(participant.PlayerId);
                if (peer != 0L)
                    BloodMoonNetwork.SendFade(peer, State.EventId, begin);
            }
        }

        private bool AllConnectedParticipantsAcknowledgedFade()
        {
            HashSet<long> connected = new HashSet<long>(GetConnectedPlayers().Select(player => player.PlayerId));
            return State.Participants.Values.Where(participant => connected.Contains(participant.PlayerId)).All(participant => participant.FadeAcknowledged);
        }

        private void RecoverServerState()
        {
            if (State == null)
                return;

            BloodMoonBosses.Recover(State);
            BloodMoonSpawner.Recover(State);

            if (State.Phase == BloodMoonEventPhase.Marked || State.IsCombatLive || State.Phase == BloodMoonEventPhase.Resolving && State.ResolutionStep < BloodMoonResolutionStep.RestoringWorldSystems)
                BloodMoonRandEventSuppression.StopActiveRandomEvent();

            if (State.IsCombatLive)
            {
                State.BloodBehaviorEnabled = true;
                BloodMoonEnvironment.AcquireForcedEnvironment();
            }

            if (State.Phase == BloodMoonEventPhase.Resolving)
            {
                resolutionStepStartedAt = 0d;
                LogWarning($"[BloodMoon][event:{State.EventId}][resolution] Resuming at step {State.ResolutionStep} after state recovery.");
            }
        }

        internal void PublishState(bool force)
        {
            if (State == null || ZNet.instance == null || !ZNet.instance.IsServer())
                return;
            if (force)
                State.Revision++;
            BloodMoonNetwork.Publish(State, seasonState.GetTotalSeconds());
        }

        internal void DebugSetPhase(BloodMoonEventPhase target)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || !SeasonState.IsActive)
                return;

            double now = seasonState.GetTotalSeconds();
            if (State == null || State.EventId < 0L || State.Schedule == null || State.Phase == BloodMoonEventPhase.Resolved || State.Phase == BloodMoonEventPhase.Skipped ||
                target == BloodMoonEventPhase.Forewarning && State.Phase != BloodMoonEventPhase.Forewarning)
            {
                if (State != null && State.IsEventLive)
                    DebugCleanup();

                int day = seasonState.GetCurrentWorldDay();
                double firstEnabled = State?.FirstEnabledAt > 0d ? State.FirstEnabledAt : now;
                long previousResolved = State?.LastResolvedEventId ?? -1L;
                State = BloodMoonPersistence.CreateClean(loadedWorldUid);
                State.FirstEnabledAt = firstEnabled;
                State.EventId = day;
                State.LastCreatedEventId = day;
                State.LastResolvedEventId = previousResolved;
                State.Schedule = CreateDebugSchedule(day, now);
                State.Phase = BloodMoonEventPhase.Forewarning;
                Touch(now, persist: true, publish: true);
            }

            if (target == BloodMoonEventPhase.Forewarning)
                return;
            if (State.Phase == BloodMoonEventPhase.Forewarning)
                EnterMarked(now);
            if (target == BloodMoonEventPhase.Marked)
                return;
            if (State.Phase == BloodMoonEventPhase.Marked)
                EnterActive(now);
        }

        internal void DebugSetProgress(long playerId, float percent)
        {
            if (State == null || !State.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant))
                return;

            float clamped = Mathf.Clamp(percent, 0f, 100f);
            participant.CombatPoints = Mathf.Max(0f, BloodMoonConfig.GoalPoints.Value) * clamped / 100f;
            participant.DisplayProgress = clamped;
            if (clamped >= 100f)
            {
                participant.GoalReached = true;
                participant.Phase = BloodMoonParticipantPhase.GoalReached;
                participant.GoalReachedAt = seasonState.GetTotalSeconds();
            }
            else if (participant.GoalReached)
            {
                participant.CombatPoints = Mathf.Max(participant.CombatPoints, Mathf.Max(0f, BloodMoonConfig.GoalPoints.Value));
                participant.DisplayProgress = 100f;
            }
            Touch(seasonState.GetTotalSeconds(), persist: true, publish: true);
        }

        internal void DebugGoalReached(long playerId) => DebugSetProgress(playerId, 100f);

        internal void DebugCleanup()
        {
            long lastCreated = State?.LastCreatedEventId ?? -1L;
            long lastResolved = State?.LastResolvedEventId ?? -1L;
            double firstEnabled = State?.FirstEnabledAt ?? 0d;
            if (State != null)
            {
                BloodMoonSpawner.CleanupExtraEnemies(State);
                BloodMoonBosses.RestoreAll(State);
                BroadcastClientAction("cleanup-craft");
                if (State.EventId >= 0L)
                {
                    lastCreated = Math.Max(lastCreated, State.EventId);
                    lastResolved = Math.Max(lastResolved, State.EventId);
                }
            }
            BloodCraft.CleanupLocal(Player.m_localPlayer);
            BloodMoonEnvironment.ReleaseForcedEnvironment();
            BloodMoonRandEventSuppression.Release();
            BloodMoonHitAttribution.Reset();
            State = BloodMoonPersistence.CreateClean(loadedWorldUid);
            State.FirstEnabledAt = firstEnabled;
            State.LastCreatedEventId = lastCreated;
            State.LastResolvedEventId = lastResolved;
            BloodMoonPersistence.Save(State);
            PublishState(force: true);
        }

        internal string DumpState()
        {
            if (State == null)
                return "Blood Moon state is not loaded.";
            return $"event={State.EventId} phase={State.Phase} step={State.ResolutionStep} revision={State.Revision} firstEnabled={State.FirstEnabledAt:0.###} participants={State.Participants.Count} groups={State.Groups.Count} leases={State.SpawnLeases.Count} extras={State.ExtraEnemyZdos.Count} bosses={State.ParkedBosses.Count}";
        }

        private bool ValidateSender(long sender, long eventId, long claimedPlayerId, out BloodMoonParticipantState participant)
        {
            participant = null;
            if (State == null || eventId != State.EventId || claimedPlayerId == 0L || !State.Participants.TryGetValue(claimedPlayerId, out participant))
                return false;

            if (ZRoutedRpc.instance != null && sender == ZRoutedRpc.instance.GetServerPeerID())
                return Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == claimedPlayerId;

            ZNetPeer peer = ZNet.instance?.GetPeer(sender);
            if (peer == null || peer.m_characterID.IsNone() || ZDOMan.instance == null)
                return false;
            ZDO playerZdo = ZDOMan.instance.GetZDO(peer.m_characterID);
            return playerZdo != null && playerZdo.GetLong(ZDOVars.s_playerID, 0L) == claimedPlayerId;
        }

        private List<ConnectedPlayer> GetConnectedPlayers()
        {
            List<ConnectedPlayer> result = new List<ConnectedPlayer>();
            if (ZNet.instance == null || ZDOMan.instance == null)
                return result;

            foreach (ZNetPeer peer in ZNet.instance.m_peers)
            {
                if (!peer.IsReady() || peer.m_characterID.IsNone())
                    continue;
                ZDO zdo = ZDOMan.instance.GetZDO(peer.m_characterID);
                if (zdo == null)
                    continue;
                long playerId = zdo.GetLong(ZDOVars.s_playerID, 0L);
                if (playerId == 0L)
                    continue;
                result.Add(new ConnectedPlayer(playerId, zdo.GetString(ZDOVars.s_playerName, peer.m_playerName), peer.m_uid, zdo.GetPosition()));
            }

            if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() != 0L)
                result.Add(new ConnectedPlayer(Player.m_localPlayer.GetPlayerID(), Player.m_localPlayer.GetPlayerName(), 0L, Player.m_localPlayer.transform.position));

            return result.GroupBy(player => player.PlayerId).Select(group => group.First()).ToList();
        }

        internal bool TryGetConnectedPosition(long playerId, out Vector3 position)
        {
            ConnectedPlayer player = GetConnectedPlayers().FirstOrDefault(item => item.PlayerId == playerId);
            if (player != null)
            {
                position = player.Position;
                return true;
            }
            position = Vector3.zero;
            return false;
        }

        internal long GetPeerForPlayer(long playerId)
        {
            ConnectedPlayer player = GetConnectedPlayers().FirstOrDefault(item => item.PlayerId == playerId);
            return player?.PeerId ?? 0L;
        }

        private void DispatchClientAction(long playerId, long peerId, string action, string payload = "")
        {
            if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId)
            {
                HandleClientAction(State?.EventId ?? BloodMoonNetwork.ClientGlobal.EventId, action, payload);
                return;
            }
            if (peerId != 0L && State != null)
                BloodMoonNetwork.SendClientAction(peerId, State.EventId, action, payload);
        }

        private void BroadcastClientAction(string action)
        {
            foreach (ConnectedPlayer player in GetConnectedPlayers())
                DispatchClientAction(player.PlayerId, player.PeerId, action);
        }

        internal static void HandleClientAction(long eventId, string action, string payload)
        {
            if (BloodMoonNetwork.ClientGlobal.EventId >= 0L && eventId != BloodMoonNetwork.ClientGlobal.EventId && action != "cleanup-craft")
                return;

            switch (action)
            {
                case "enroll":
                    BloodMoonPresentation.OnEnrolled();
                    break;
                case "defeated":
                    BloodMoonRecovery.ApplyDefeat(Player.m_localPlayer);
                    BloodCraft.CleanupLocal(Player.m_localPlayer);
                    break;
                case "withdrawn":
                    BloodMoonRecovery.ApplyWithdrawal(Player.m_localPlayer);
                    BloodCraft.CleanupLocal(Player.m_localPlayer);
                    break;
                case "cleanup-craft":
                    BloodCraft.CleanupLocal(Player.m_localPlayer);
                    break;
                case "reward":
                    BloodMoonSkills.ApplySerializedReward(Player.m_localPlayer, payload);
                    break;
                case "chronicle":
                    BloodMoonPresentation.PublishChronicle(payload);
                    break;
                case "remove-rested":
                    BloodMoonRecovery.RemoveRested(Player.m_localPlayer);
                    break;
                case "resolution-complete":
                    BloodMoonPresentation.OnResolutionComplete();
                    break;
            }
        }

        private BloodMoonScheduleSnapshot CreateDebugSchedule(int day, double now)
        {
            double dayLength = Math.Max(1d, seasonState.GetDayLengthInSeconds());
            return new BloodMoonScheduleSnapshot
            {
                EventWorldDay = day,
                AutumnDay = seasonState.GetDayInSeason(day),
                ForewarningAt = now,
                MarkedAt = now,
                ActiveAt = now,
                AutoCompleteAt = now + dayLength * 0.25d,
                ForcedEndAt = now + dayLength * 0.5d,
                MorningAt = now + dayLength * 0.5d + 1d
            };
        }

        private void Touch(double now, bool persist, bool publish)
        {
            if (State == null)
                return;
            State.UpdatedAt = now;
            State.Revision++;
            if (persist)
                BloodMoonPersistence.Save(State);
            if (publish)
                BloodMoonNetwork.Publish(State, now);
        }

        private void SaveNow()
        {
            if (State != null && ZNet.instance != null && ZNet.instance.IsServer())
                BloodMoonPersistence.Save(State);
        }

        internal sealed class ConnectedPlayer
        {
            internal readonly long PlayerId;
            internal readonly string Name;
            internal readonly long PeerId;
            internal readonly Vector3 Position;

            internal ConnectedPlayer(long playerId, string name, long peerId, Vector3 position)
            {
                PlayerId = playerId;
                Name = name ?? string.Empty;
                PeerId = peerId;
                Position = position;
            }
        }
    }
}
