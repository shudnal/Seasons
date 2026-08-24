using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal sealed class BloodMoonController : MonoBehaviour
    {
        internal static BloodMoonController Instance { get; private set; }
        internal BloodMoonEventState State { get; private set; }

        private long loadedWorldUid;
        private float serverTickTimer;
        private float groupTickTimer;
        private double resolutionStepStartedAt;
        private bool worldSaveSubscribed;

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
            BloodMoonEnvironment.ReleaseForcedEnvironment();
            BloodMoonPresentation.Cleanup();
            BloodMoonSpawner.ResetClientState();
            Instance = null;
        }

        private void FixedUpdate()
        {
            BloodMoonNetwork.RegisterRpcs();
            BloodMoonPresentation.Tick(Time.fixedDeltaTime);
            BloodMoonSpawner.TickClient(Time.fixedDeltaTime);
            BloodMoonRecovery.TickClientProtection(Time.fixedDeltaTime);

            if (ZNet.instance == null || ZNet.m_world == null || !SeasonState.IsActive)
                return;

            EnsureWorldLoaded();
            if (!ZNet.instance.IsServer() || State == null)
                return;

            serverTickTimer -= Time.fixedDeltaTime;
            if (serverTickTimer > 0f)
                return;
            serverTickTimer = 0.5f;

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

        private void TickServer(double now)
        {
            if (!BloodMoonConfig.Enabled.Value)
            {
                if (State.IsEventLive && State.Phase != BloodMoonEventPhase.Resolving)
                    BeginResolution("disabled", now);
                if (State.Phase == BloodMoonEventPhase.Resolving)
                    TickResolution(now);
                return;
            }

            if (State.Phase == BloodMoonEventPhase.Resolving)
            {
                TickResolution(now);
                return;
            }

            if (State.Phase == BloodMoonEventPhase.Dormant || State.Phase == BloodMoonEventPhase.Resolved || State.Phase == BloodMoonEventPhase.Skipped)
                TryCreateScheduledEvent(now);

            if (State.Schedule == null || State.EventId < 0)
                return;

            BloodMoonEventPhase expected = BloodMoonSchedule.GetExpectedPhase(State.Schedule, now);
            AdvanceToExpectedPhase(expected, now);

            if (State.Phase == BloodMoonEventPhase.Marked || State.Phase == BloodMoonEventPhase.Active || State.Phase == BloodMoonEventPhase.AutoCompleting)
                UpdateConnectedParticipants(now);

            if (State.IsCombatLive)
            {
                groupTickTimer -= 0.5f;
                if (groupTickTimer <= 0f)
                {
                    groupTickTimer = 4f;
                    BloodMoonGroups.Rebuild(State, now);
                    BloodMoonSpawner.UpdateServerLeases(State, now);
                }

                if (State.Participants.Count > 0 && State.Participants.Values.All(participant => participant.IsTerminal))
                    BeginResolution("all participants terminal", now);
            }
        }

        private void TryCreateScheduledEvent(double now)
        {
            BloodMoonScheduleSnapshot schedule = BloodMoonSchedule.FindCurrentOrNext(now);
            if (schedule == null || schedule.EventWorldDay == State.LastResolvedEventId || schedule.EventWorldDay == State.LastCreatedEventId)
                return;

            BloodMoonEventPhase expected = BloodMoonSchedule.GetExpectedPhase(schedule, now);
            if (expected == BloodMoonEventPhase.Dormant || expected == BloodMoonEventPhase.Resolved)
                return;

            State = BloodMoonPersistence.CreateClean(loadedWorldUid);
            State.EventId = schedule.EventWorldDay;
            State.LastCreatedEventId = schedule.EventWorldDay;
            State.Schedule = schedule;
            State.Phase = BloodMoonEventPhase.Forewarning;
            State.BloodBehaviorEnabled = false;
            Touch(now, persist: true, publish: true);
            LogInfo($"[BloodMoon.Event] Created event {State.EventId}; phase Forewarning.");
            BloodMoonPresentation.EnsureEnvironmentRegistered();
        }

        private void AdvanceToExpectedPhase(BloodMoonEventPhase expected, double now)
        {
            if (expected == BloodMoonEventPhase.Resolved && State.Phase != BloodMoonEventPhase.Resolving)
            {
                BeginResolution("schedule forced end", now);
                return;
            }

            if (expected >= BloodMoonEventPhase.Marked && State.Phase == BloodMoonEventPhase.Forewarning)
                EnterMarked(now);
            if (expected >= BloodMoonEventPhase.Active && State.Phase == BloodMoonEventPhase.Marked)
                EnterActive(now);
            if (expected >= BloodMoonEventPhase.AutoCompleting && State.Phase == BloodMoonEventPhase.Active)
                EnterAutoCompleting(now);
            if (State.Schedule != null && now >= State.Schedule.ForcedEndAt && State.Phase != BloodMoonEventPhase.Resolving)
                BeginResolution("forced end", now);
        }

        private void EnterMarked(double now)
        {
            State.Phase = BloodMoonEventPhase.Marked;
            State.EnrollmentFrozen = false;
            foreach (ConnectedPlayer connected in GetConnectedPlayers())
                Enroll(connected, now, joinedLate: false);
            BloodMoonRandEventSuppression.StopActiveRandomEvent();
            Touch(now, persist: true, publish: true);
            LogInfo($"[BloodMoon.Event] Event {State.EventId} entered Marked with {State.Participants.Count} participant(s).");
        }

        private void EnterActive(double now)
        {
            State.Phase = BloodMoonEventPhase.Active;
            State.BloodBehaviorEnabled = true;
            foreach (BloodMoonParticipantState participant in State.Participants.Values.Where(item => item.Phase == BloodMoonParticipantPhase.Marked))
            {
                participant.Phase = BloodMoonParticipantPhase.Fighting;
                participant.FightingAt = now;
            }
            BloodMoonEnvironment.AcquireForcedEnvironment();
            Touch(now, persist: true, publish: true);
            LogInfo($"[BloodMoon.Event] Event {State.EventId} entered Active.");
        }

        private void EnterAutoCompleting(double now)
        {
            State.Phase = BloodMoonEventPhase.AutoCompleting;
            foreach (BloodMoonParticipantState participant in State.Participants.Values)
            {
                if (!participant.IsCombatActive || participant.GoalReached)
                    continue;
                participant.AutoCompleted = true;
                participant.DisplayProgress = 100f;
            }
            Touch(now, persist: true, publish: true);
            LogInfo($"[BloodMoon.Event] Event {State.EventId} entered AutoCompleting.");
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

            foreach (BloodMoonParticipantState participant in State.Participants.Values)
            {
                if (!participant.IsCombatActive && participant.Phase != BloodMoonParticipantPhase.Marked)
                    continue;
                if (!connectedIds.Contains(participant.PlayerId))
                    ExitParticipant(participant, BloodMoonParticipantExitReason.Disconnected, now);
            }
        }

        private void Enroll(ConnectedPlayer connected, double now, bool joinedLate)
        {
            if (connected.PlayerId == 0L || State.Participants.ContainsKey(connected.PlayerId))
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
            SendClientAction(connected.PeerId, "enroll");
            Touch(now, persist: true, publish: true);
            LogInfo($"[BloodMoon.Participant] Enrolled {participant.PlayerName} ({participant.PlayerId}), late={joinedLate}.");
        }

        internal void OnDefeatedReport(long sender, long eventId, long playerId)
        {
            if (!ValidateSender(sender, eventId, playerId, out BloodMoonParticipantState participant) || !participant.IsCombatActive)
                return;
            ExitParticipant(participant, BloodMoonParticipantExitReason.Defeated, seasonState.GetTotalSeconds());
            SendClientAction(sender, "defeated");
        }

        internal void WithdrawLocalOrRequested(long playerId)
        {
            if (State == null || !State.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant) || !participant.IsCombatActive)
                return;
            ExitParticipant(participant, BloodMoonParticipantExitReason.Withdrawn, seasonState.GetTotalSeconds());
            long peer = GetPeerForPlayer(playerId);
            SendClientAction(peer, "withdrawn");
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
            LogInfo($"[BloodMoon.Participant] {participant.PlayerName} ({participant.PlayerId}) exited: {reason}.");
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
            IEnumerable<BloodMoonParticipantState> recipients = group == null
                ? new[] { source }
                : group.MemberPlayerIds.Where(State.Participants.ContainsKey).Select(id => State.Participants[id]).Where(item => item.IsCombatActive);

            List<BloodMoonParticipantState> recipientList = recipients.ToList();
            if (recipientList.Count == 0)
                recipientList.Add(source);
            float share = points / recipientList.Count;

            foreach (BloodMoonParticipantState participant in recipientList)
            {
                participant.CombatPoints += share;
                participant.Contribution += participant.PlayerId == source.PlayerId ? points : 0f;
                participant.DisplayProgress = Mathf.Clamp(participant.CombatPoints / Mathf.Max(1f, BloodMoonConfig.GoalPoints.Value) * 100f, 0f, 100f);
                if (!participant.GoalReached && participant.DisplayProgress >= 100f)
                {
                    participant.GoalReached = true;
                    participant.Phase = BloodMoonParticipantPhase.GoalReached;
                    participant.GoalReachedAt = now;
                    LogInfo($"[BloodMoon.Progress] {participant.PlayerName} reached the goal with {participant.CombatPoints:0.##} points.");
                }
            }

            if (group != null)
                group.CombatPoints += points;
            Touch(now, persist: true, publish: true);
        }

        internal void OnSpawnReport(long sender, long eventId, long playerId, long groupId, int groupRevision, int leaseRevision, ZDOID spawnedId)
        {
            if (!ValidateSender(sender, eventId, playerId, out _) || spawnedId.IsNone())
                return;
            BloodMoonSpawner.AcceptSpawnReport(State, sender, groupId, groupRevision, leaseRevision, spawnedId, seasonState.GetTotalSeconds());
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
            State.Phase = BloodMoonEventPhase.Resolving;
            State.ResolutionStep = BloodMoonResolutionStep.FreezingEnrollment;
            State.EnrollmentFrozen = true;
            resolutionStepStartedAt = now;
            Touch(now, persist: true, publish: true);
            LogInfo($"[BloodMoon.Resolution] Event {State.EventId} started resolution: {reason}.");
        }

        private void TickResolution(double now)
        {
            switch (State.ResolutionStep)
            {
                case BloodMoonResolutionStep.FreezingEnrollment:
                    SetResolutionStep(BloodMoonResolutionStep.StoppingSpawns, now);
                    break;
                case BloodMoonResolutionStep.StoppingSpawns:
                    State.SpawnsStopped = true;
                    BloodMoonSpawner.StopServerLeases(State);
                    SendFadeToParticipants(begin: true);
                    SetResolutionStep(BloodMoonResolutionStep.AwaitingClientFade, now);
                    break;
                case BloodMoonResolutionStep.AwaitingClientFade:
                    if (AllConnectedParticipantsAcknowledgedFade() || now - resolutionStepStartedAt >= 4d)
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
                    AdvanceToMorning();
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
                        if (participant.Phase != BloodMoonParticipantPhase.Exited)
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
                    LogInfo($"[BloodMoon.Resolution] Event {State.EventId} resolved.");
                    break;
            }
        }

        private void SetResolutionStep(BloodMoonResolutionStep step, double now)
        {
            State.ResolutionStep = step;
            resolutionStepStartedAt = now;
            Touch(now, persist: true, publish: true);
            LogInfo($"[BloodMoon.Resolution] Step -> {step}.");
        }

        private void PublishOutcomes()
        {
            foreach (BloodMoonParticipantState participant in State.Participants.Values)
            {
                long peer = GetPeerForPlayer(participant.PlayerId);
                string payload = BloodMoonSkills.SerializeCompletionReward(participant);
                if (peer != 0L)
                {
                    BloodMoonNetwork.SendClientAction(peer, State.EventId, "reward", payload);
                    BloodMoonNetwork.SendClientAction(peer, State.EventId, "chronicle", BloodMoonPresentation.BuildChronicle(participant));
                    BloodMoonNetwork.SendClientAction(peer, State.EventId, "remove-rested");
                }
                if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == participant.PlayerId)
                {
                    BloodMoonSkills.ApplySerializedReward(Player.m_localPlayer, payload);
                    BloodMoonPresentation.PublishChronicle(BloodMoonPresentation.BuildChronicle(participant));
                    BloodMoonRecovery.RemoveRested(Player.m_localPlayer);
                }
            }
        }

        private void AdvanceToMorning()
        {
            if (EnvMan.instance != null && State.Schedule != null && seasonState.GetTotalSeconds() < State.Schedule.MorningAt)
                EnvMan.instance.SkipToMorning();
        }

        private void SendFadeToParticipants(bool begin)
        {
            foreach (BloodMoonParticipantState participant in State.Participants.Values)
            {
                participant.FadeAcknowledged = false;
                long peer = GetPeerForPlayer(participant.PlayerId);
                if (peer != 0L)
                    BloodMoonNetwork.SendFade(peer, State.EventId, begin);
            }
            if (Player.m_localPlayer != null && State.Participants.ContainsKey(Player.m_localPlayer.GetPlayerID()))
                BloodMoonPresentation.SetResolutionFade(begin);
        }

        private bool AllConnectedParticipantsAcknowledgedFade()
        {
            HashSet<long> connected = new HashSet<long>(GetConnectedPlayers().Select(player => player.PlayerId));
            return State.Participants.Values.Where(participant => connected.Contains(participant.PlayerId)).All(participant => participant.FadeAcknowledged || Player.m_localPlayer != null && participant.PlayerId == Player.m_localPlayer.GetPlayerID());
        }

        private void RecoverServerState()
        {
            if (State == null)
                return;
            BloodMoonBosses.Recover(State);
            BloodMoonSpawner.Recover(State);
            if (State.Phase == BloodMoonEventPhase.Resolving)
                LogWarning($"[BloodMoon.Recovery] Resuming event {State.EventId} at resolution step {State.ResolutionStep}.");
            else if (State.IsCombatLive)
            {
                State.BloodBehaviorEnabled = true;
                BloodMoonEnvironment.AcquireForcedEnvironment();
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
            if (ZNet.instance == null || !ZNet.instance.IsServer())
                return;
            double now = seasonState.GetTotalSeconds();
            if (State == null || State.EventId < 0 || State.Schedule == null)
            {
                int day = seasonState.GetCurrentWorldDay();
                State = BloodMoonPersistence.CreateClean(loadedWorldUid);
                State.EventId = day;
                State.LastCreatedEventId = day;
                State.Schedule = BloodMoonSchedule.TryCreateForWorldDay(day) ?? CreateDebugSchedule(day, now);
                State.Phase = BloodMoonEventPhase.Forewarning;
            }
            if (target == BloodMoonEventPhase.Forewarning)
            {
                State.Phase = BloodMoonEventPhase.Forewarning;
                State.BloodBehaviorEnabled = false;
            }
            else
            {
                if (State.Phase == BloodMoonEventPhase.Forewarning)
                    EnterMarked(now);
                if (target == BloodMoonEventPhase.Active && State.Phase == BloodMoonEventPhase.Marked)
                    EnterActive(now);
            }
            Touch(now, persist: true, publish: true);
        }

        internal void DebugSetProgress(long playerId, float percent)
        {
            if (State == null || !State.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant))
                return;
            participant.DisplayProgress = Mathf.Clamp(percent, 0f, 100f);
            participant.CombatPoints = BloodMoonConfig.GoalPoints.Value * participant.DisplayProgress / 100f;
            if (participant.DisplayProgress >= 100f)
            {
                participant.GoalReached = true;
                participant.Phase = BloodMoonParticipantPhase.GoalReached;
            }
            Touch(seasonState.GetTotalSeconds(), persist: true, publish: true);
        }

        internal void DebugGoalReached(long playerId) => DebugSetProgress(playerId, 100f);

        internal void DebugCleanup()
        {
            BloodMoonSpawner.CleanupExtraEnemies(State);
            BloodMoonBosses.RestoreAll(State);
            BroadcastClientAction("cleanup-craft");
            BloodCraft.CleanupLocal(Player.m_localPlayer);
            BloodMoonEnvironment.ReleaseForcedEnvironment();
            State = BloodMoonPersistence.CreateClean(loadedWorldUid);
            BloodMoonPersistence.Save(State);
            PublishState(force: true);
        }

        internal string DumpState()
        {
            if (State == null)
                return "Blood Moon state is not loaded.";
            return $"event={State.EventId} phase={State.Phase} step={State.ResolutionStep} revision={State.Revision} participants={State.Participants.Count} groups={State.Groups.Count} leases={State.SpawnLeases.Count} extras={State.ExtraEnemyZdos.Count} bosses={State.ParkedBosses.Count}";
        }

        private bool ValidateSender(long sender, long eventId, long claimedPlayerId, out BloodMoonParticipantState participant)
        {
            participant = null;
            if (State == null || eventId != State.EventId || claimedPlayerId == 0L || !State.Participants.TryGetValue(claimedPlayerId, out participant))
                return false;

            if (sender == ZRoutedRpc.instance.GetServerPeerID())
                return Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == claimedPlayerId;

            ZNetPeer peer = ZNet.instance.GetPeer(sender);
            if (peer == null || peer.m_characterID.IsNone())
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
                result.Add(new ConnectedPlayer(Player.m_localPlayer.GetPlayerID(), Player.m_localPlayer.GetPlayerName(), ZRoutedRpc.instance.GetServerPeerID(), Player.m_localPlayer.transform.position));
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

        private void SendClientAction(long peerId, string action)
        {
            if (peerId != 0L && peerId != ZRoutedRpc.instance.GetServerPeerID())
                BloodMoonNetwork.SendClientAction(peerId, State.EventId, action);
            if (Player.m_localPlayer != null && State.Participants.ContainsKey(Player.m_localPlayer.GetPlayerID()))
                HandleClientAction(State.EventId, action, string.Empty);
        }

        private void BroadcastClientAction(string action)
        {
            foreach (ConnectedPlayer player in GetConnectedPlayers())
                SendClientAction(player.PeerId, action);
        }

        internal static void HandleClientAction(long eventId, string action, string payload)
        {
            if (BloodMoonNetwork.ClientGlobal.EventId >= 0 && eventId != BloodMoonNetwork.ClientGlobal.EventId && action != "cleanup-craft")
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
            double dayLength = seasonState.GetDayLengthInSeconds();
            return new BloodMoonScheduleSnapshot
            {
                EventWorldDay = day,
                AutumnDay = seasonState.GetDayInSeason(day),
                ForewarningAt = now,
                MarkedAt = now,
                ActiveAt = now,
                AutoCompleteAt = now + dayLength,
                ForcedEndAt = now + dayLength * 2d,
                MorningAt = now + dayLength * 2d + 1d
            };
        }

        private void Touch(double now, bool persist, bool publish)
        {
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
