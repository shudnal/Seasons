using ConditionalConfigSync;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonNetwork
    {
        internal const int ProtocolVersion = 2;
        private const int SyncPriorityGlobal = Priority.VeryLow + 4;
        private const int SyncPriorityParticipants = Priority.VeryLow + 3;
        private const int MaxPendingClientActions = 32;

        private const string RpcDefeated = "Seasons.BloodMoon.Defeated";
        private const string RpcEnemyDeath = "Seasons.BloodMoon.EnemyDeath";
        private const string RpcSkillGain = "Seasons.BloodMoon.SkillGain";
        private const string RpcBossDiscovery = "Seasons.BloodMoon.BossDiscovery";
        private const string RpcZoneClaim = "Seasons.BloodMoon.ZoneClaim";
        private const string RpcSpawnLease = "Seasons.BloodMoon.SpawnLease";
        private const string RpcSpawnReport = "Seasons.BloodMoon.SpawnReport";
        private const string RpcFade = "Seasons.BloodMoon.Fade";
        private const string RpcFadeAck = "Seasons.BloodMoon.FadeAck";
        private const string RpcResync = "Seasons.BloodMoon.Resync";
        private const string RpcClientAction = "Seasons.BloodMoon.ClientAction";
        private const string ParticipantDetailAction = "participant-detail";
        private const string ResyncStateAction = "resync-state";

        [Serializable]
        private sealed class ResyncEnvelope
        {
            public int Protocol = ProtocolVersion;
            public BloodMoonGlobalSnapshot Global;
            public BloodMoonParticipantSnapshot Participants;
            public BloodMoonParticipantDetailSnapshot OwnDetail;
        }

        private sealed class PendingClientAction
        {
            internal long EventId;
            internal string Action;
            internal string Payload;
        }

        internal static readonly CustomSyncedValue<string> GlobalStateJson = new CustomSyncedValue<string>(configSync, "Blood Moon global state", "", SyncPriorityGlobal);
        internal static readonly CustomSyncedValue<string> ParticipantStateJson = new CustomSyncedValue<string>(configSync, "Blood Moon participant state", "", SyncPriorityParticipants);

        private static readonly List<PendingClientAction> pendingClientActions = new List<PendingClientAction>();
        private static bool valuesInitialized;
        private static ZRoutedRpc registeredRpc;
        private static string lastGlobalSignature = string.Empty;
        private static string lastParticipantSignature = string.Empty;
        private static int globalSnapshotRevision;
        private static int participantSnapshotRevision;

        internal static BloodMoonGlobalSnapshot ClientGlobal { get; private set; } = new BloodMoonGlobalSnapshot();
        internal static BloodMoonParticipantSnapshot ClientParticipants { get; private set; } = new BloodMoonParticipantSnapshot();

        internal static void InitializeValues()
        {
            if (valuesInitialized)
                return;
            valuesInitialized = true;
            GlobalStateJson.ValueChanged += OnGlobalStateChanged;
            ParticipantStateJson.ValueChanged += OnParticipantStateChanged;
            OnGlobalStateChanged();
            OnParticipantStateChanged();
        }

        internal static void RegisterRpcs()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(registeredRpc, rpc))
                return;

            registeredRpc = rpc;
            pendingClientActions.Clear();
            BloodMoonParticipantDetails.Reset();
            rpc.Register<ZPackage>(RpcDefeated, OnDefeated);
            rpc.Register<ZPackage>(RpcEnemyDeath, OnEnemyDeath);
            rpc.Register<ZPackage>(RpcSkillGain, OnSkillGain);
            rpc.Register<ZPackage>(RpcBossDiscovery, OnBossDiscovery);
            rpc.Register<ZPackage>(RpcZoneClaim, OnZoneClaim);
            rpc.Register<ZPackage>(RpcSpawnLease, OnSpawnLease);
            rpc.Register<ZPackage>(RpcSpawnReport, OnSpawnReport);
            rpc.Register<ZPackage>(RpcFade, OnFade);
            rpc.Register<ZPackage>(RpcFadeAck, OnFadeAck);
            rpc.Register<ZPackage>(RpcResync, OnResync);
            rpc.Register<ZPackage>(RpcClientAction, OnClientAction);
            BloodMoonHitAttribution.RegisterRpc(rpc);
            LogInfo("[BloodMoon.Sync] Routed RPC handlers registered.");
        }

        internal static void Publish(BloodMoonEventState state, double serverTime)
        {
            if (state == null || ZNet.instance == null || !ZNet.instance.IsServer())
                return;

            PublishGlobalSnapshot(state, serverTime);
            PublishRoutingSnapshot(state);
            PublishParticipantDetails(state);
        }

        private static void PublishGlobalSnapshot(BloodMoonEventState state, double serverTime)
        {
            BloodMoonGlobalSnapshot snapshot = BuildGlobalSnapshot(state, 0, 0d);
            string signature = JsonConvert.SerializeObject(snapshot);
            if (string.Equals(signature, lastGlobalSignature, StringComparison.Ordinal))
                return;

            lastGlobalSignature = signature;
            snapshot.Revision = ++globalSnapshotRevision;
            snapshot.ServerTime = serverTime;
            GlobalStateJson.AssignValueSafeIfChanged(JsonConvert.SerializeObject(snapshot));
        }

        private static void PublishRoutingSnapshot(BloodMoonEventState state)
        {
            BloodMoonParticipantSnapshot snapshot = BuildRoutingSnapshot(state, 0);
            string signature = JsonConvert.SerializeObject(snapshot);
            if (string.Equals(signature, lastParticipantSignature, StringComparison.Ordinal))
                return;

            lastParticipantSignature = signature;
            snapshot.Revision = ++participantSnapshotRevision;
            ParticipantStateJson.AssignValueSafeIfChanged(JsonConvert.SerializeObject(snapshot));
        }

        private static BloodMoonGlobalSnapshot BuildGlobalSnapshot(BloodMoonEventState state, int revision, double serverTime)
        {
            return new BloodMoonGlobalSnapshot
            {
                EventId = state.EventId,
                Phase = state.Phase,
                ResolutionStep = state.ResolutionStep,
                Schedule = state.Schedule,
                Revision = revision,
                BloodBehaviorEnabled = state.BloodBehaviorEnabled,
                SpawnsStopped = state.SpawnsStopped,
                ServerTime = serverTime
            };
        }

        private static BloodMoonParticipantSnapshot BuildRoutingSnapshot(BloodMoonEventState state, int revision)
        {
            return new BloodMoonParticipantSnapshot
            {
                EventId = state.EventId,
                Revision = revision,
                Participants = state.Participants.Values
                    .OrderBy(participant => participant.PlayerId)
                    .Select(CreatePublicParticipant)
                    .ToList()
            };
        }

        private static BloodMoonParticipantState CreatePublicParticipant(BloodMoonParticipantState source)
        {
            return new BloodMoonParticipantState
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

        private static void PublishParticipantDetails(BloodMoonEventState state)
        {
            BloodMoonController controller = BloodMoonController.Instance;
            if (controller == null)
                return;

            foreach (BloodMoonParticipantState participant in state.Participants.Values)
            {
                string payload = BloodMoonParticipantDetails.Serialize(state, participant);
                if (string.IsNullOrEmpty(payload))
                    continue;

                if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == participant.PlayerId)
                {
                    BloodMoonParticipantDetails.Apply(state.EventId, payload);
                    continue;
                }

                long peerId = controller.GetPeerForPlayer(participant.PlayerId);
                if (peerId != 0L)
                    SendClientAction(peerId, state.EventId, ParticipantDetailAction, payload);
            }
        }

        internal static void SendDefeated(long eventId, long playerId)
        {
            SendToServer(RpcDefeated, CreateHeader(eventId, playerId));
        }

        internal static void SendEnemyDeath(long eventId, ZDOID enemyId, long creditedPlayerId, float points)
        {
            ZPackage pkg = CreateHeader(eventId, creditedPlayerId);
            pkg.Write(enemyId);
            pkg.Write(points);
            SendToServer(RpcEnemyDeath, pkg);
        }

        internal static void SendSkillGain(long eventId, long playerId, long sequence, Skills.SkillType skill, float baseEquivalent, float liveBonusEquivalent)
        {
            ZPackage pkg = CreateHeader(eventId, playerId);
            pkg.Write(sequence);
            pkg.Write((int)skill);
            pkg.Write(baseEquivalent);
            pkg.Write(liveBonusEquivalent);
            SendToServer(RpcSkillGain, pkg);
        }

        internal static void SendBossDiscovery(long eventId, long playerId, ZDOID bossId, bool observedInterior, int observedPrefabHash)
        {
            ZPackage pkg = CreateHeader(eventId, playerId);
            pkg.Write(bossId);
            pkg.Write(observedInterior);
            pkg.Write(observedPrefabHash);
            SendToServer(RpcBossDiscovery, pkg);
        }

        internal static void SendZoneClaim(long eventId, Vector2i zone)
        {
            ZPackage pkg = CreateHeader(eventId, GetLocalPlayerId());
            pkg.Write(zone.x);
            pkg.Write(zone.y);
            SendToServer(RpcZoneClaim, pkg);
        }

        internal static void SendSpawnReport(long eventId, long groupId, int groupRevision, int zoneX, int zoneY, int leaseRevision, ZDOID spawnedId)
        {
            ZPackage pkg = CreateHeader(eventId, GetLocalPlayerId());
            pkg.Write(groupId);
            pkg.Write(groupRevision);
            pkg.Write(zoneX);
            pkg.Write(zoneY);
            pkg.Write(leaseRevision);
            pkg.Write(spawnedId);
            SendToServer(RpcSpawnReport, pkg);
        }

        internal static void SendSpawnLease(long peerId, BloodMoonSpawnLeaseState lease)
        {
            if (!CanSendFromServer() || lease == null)
                return;
            ZPackage pkg = CreateHeader(lease.EventId, 0L);
            pkg.Write(lease.GroupId);
            pkg.Write(lease.GroupRevision);
            pkg.Write(lease.ZoneX);
            pkg.Write(lease.ZoneY);
            pkg.Write(lease.OwnerSessionId);
            pkg.Write(lease.LeaseRevision);
            pkg.Write(lease.Anchor);
            pkg.Write(lease.Allowance);
            pkg.Write(lease.GroupCap);
            pkg.Write(lease.ServerHardCap);
            pkg.Write(lease.PoolRevision);
            pkg.Write(lease.ExpiresAt);
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, RpcSpawnLease, pkg);
        }

        internal static void SendFade(long peerId, long eventId, bool begin)
        {
            if (!CanSendFromServer())
                return;
            ZPackage pkg = CreateHeader(eventId, 0L);
            pkg.Write(begin);
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, RpcFade, pkg);
        }

        internal static void SendClientAction(long peerId, long eventId, string action, string payload = "")
        {
            if (!CanSendFromServer())
                return;
            ZPackage pkg = CreateHeader(eventId, 0L);
            pkg.Write(action ?? string.Empty);
            pkg.Write(payload ?? string.Empty);
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, RpcClientAction, pkg);
        }

        internal static void RequestResync()
        {
            SendToServer(RpcResync, CreateHeader(ClientGlobal.EventId, GetLocalPlayerId()));
        }

        private static ZPackage CreateHeader(long eventId, long playerId)
        {
            ZPackage pkg = new ZPackage();
            pkg.Write(ProtocolVersion);
            pkg.Write(eventId);
            pkg.Write(playerId);
            return pkg;
        }

        private static bool ReadHeader(ZPackage pkg, out long eventId, out long playerId)
        {
            eventId = -1L;
            playerId = 0L;
            if (pkg == null || pkg.ReadInt() != ProtocolVersion)
                return false;
            eventId = pkg.ReadLong();
            playerId = pkg.ReadLong();
            return true;
        }

        private static void SendToServer(string method, ZPackage pkg)
        {
            if (ZRoutedRpc.instance == null)
                return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), method, pkg);
        }

        private static bool CanSendFromServer()
        {
            return ZNet.instance != null && ZNet.instance.IsServer() && ZRoutedRpc.instance != null;
        }

        private static bool IsFromServer(long sender)
        {
            return ZRoutedRpc.instance != null && sender == ZRoutedRpc.instance.GetServerPeerID();
        }

        private static long GetLocalPlayerId()
        {
            return Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;
        }

        private static void OnDefeated(long sender, ZPackage pkg)
        {
            if (!ReadHeader(pkg, out long eventId, out long playerId) || ZNet.instance == null || !ZNet.instance.IsServer())
                return;
            BloodMoonController.Instance?.OnDefeatedReport(sender, eventId, playerId);
        }

        private static void OnEnemyDeath(long sender, ZPackage pkg)
        {
            if (!ReadHeader(pkg, out long eventId, out long playerId) || ZNet.instance == null || !ZNet.instance.IsServer())
                return;
            ZDOID enemyId = pkg.ReadZDOID();
            float points = pkg.ReadSingle();
            BloodMoonController.Instance?.OnEnemyDeathReport(sender, eventId, playerId, enemyId, points);
        }

        private static void OnSkillGain(long sender, ZPackage pkg)
        {
            if (!ReadHeader(pkg, out long eventId, out long playerId) || ZNet.instance == null || !ZNet.instance.IsServer())
                return;
            long sequence = pkg.ReadLong();
            Skills.SkillType skill = (Skills.SkillType)pkg.ReadInt();
            float baseEquivalent = pkg.ReadSingle();
            float liveBonusEquivalent = pkg.ReadSingle();
            BloodMoonSkillReports.Accept(sender, eventId, playerId, sequence, skill, baseEquivalent, liveBonusEquivalent);
        }

        private static void OnBossDiscovery(long sender, ZPackage pkg)
        {
            if (!ReadHeader(pkg, out long eventId, out long playerId) || ZNet.instance == null || !ZNet.instance.IsServer())
                return;
            ZDOID bossId = pkg.ReadZDOID();
            bool observedInterior = pkg.ReadBool();
            int observedPrefabHash = pkg.ReadInt();
            BloodMoonBosses.AcceptDiscovery(sender, eventId, playerId, bossId, observedInterior, observedPrefabHash);
        }

        private static void OnZoneClaim(long sender, ZPackage pkg)
        {
            if (!ReadHeader(pkg, out long eventId, out long playerId) || ZNet.instance == null || !ZNet.instance.IsServer())
                return;
            BloodMoonZoneOwnership.AcceptClaim(sender, eventId, playerId, pkg.ReadInt(), pkg.ReadInt());
        }

        private static void OnSpawnLease(long sender, ZPackage pkg)
        {
            // ZRoutedRpc delivers a server-to-self call synchronously on a listen host. The same
            // deserialized lease path is required there; being the server does not make the local
            // zone-owner role disappear.
            if (!ReadHeader(pkg, out long eventId, out _) || ZNet.instance == null || !IsFromServer(sender) || ZDOMan.instance == null)
                return;
            BloodMoonSpawnLeaseState lease = new BloodMoonSpawnLeaseState
            {
                EventId = eventId,
                GroupId = pkg.ReadLong(),
                GroupRevision = pkg.ReadInt(),
                ZoneX = pkg.ReadInt(),
                ZoneY = pkg.ReadInt(),
                OwnerPeerId = ZDOMan.GetSessionID(),
                OwnerSessionId = pkg.ReadLong(),
                LeaseRevision = pkg.ReadInt(),
                Anchor = pkg.ReadVector3(),
                Allowance = pkg.ReadInt(),
                GroupCap = pkg.ReadInt(),
                ServerHardCap = pkg.ReadInt(),
                PoolRevision = pkg.ReadInt(),
                ExpiresAt = pkg.ReadDouble()
            };
            BloodMoonSpawner.ReceiveLease(lease);
        }

        private static void OnSpawnReport(long sender, ZPackage pkg)
        {
            if (!ReadHeader(pkg, out long eventId, out _) || ZNet.instance == null || !ZNet.instance.IsServer())
                return;
            long groupId = pkg.ReadLong();
            int groupRevision = pkg.ReadInt();
            int zoneX = pkg.ReadInt();
            int zoneY = pkg.ReadInt();
            int leaseRevision = pkg.ReadInt();
            ZDOID spawnedId = pkg.ReadZDOID();
            BloodMoonEventState state = BloodMoonController.Instance?.State;
            if (state == null || state.EventId != eventId)
                return;
            BloodMoonSpawner.AcceptSpawnReport(state, sender, groupId, groupRevision, zoneX, zoneY, leaseRevision, spawnedId, seasonState.GetTotalSeconds());
        }

        private static void OnFade(long sender, ZPackage pkg)
        {
            if (!ReadHeader(pkg, out long eventId, out _) || ZNet.instance == null || ZNet.instance.IsServer() || !IsFromServer(sender))
                return;
            bool begin = pkg.ReadBool();
            BloodMoonPresentation.SetResolutionFade(begin);
            ZPackage ack = CreateHeader(eventId, GetLocalPlayerId());
            SendToServer(RpcFadeAck, ack);
        }

        private static void OnFadeAck(long sender, ZPackage pkg)
        {
            if (!ReadHeader(pkg, out long eventId, out long playerId) || ZNet.instance == null || !ZNet.instance.IsServer())
                return;
            BloodMoonController.Instance?.OnFadeAcknowledged(sender, eventId, playerId);
        }

        private static void OnResync(long sender, ZPackage pkg)
        {
            if (!ReadHeader(pkg, out _, out long playerId) || ZNet.instance == null || !ZNet.instance.IsServer())
                return;

            BloodMoonEventState state = BloodMoonController.Instance?.State;
            if (state == null)
                return;

            BloodMoonParticipantState own = null;
            if (TryValidateSenderPlayer(sender, playerId) && state.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant))
                own = participant;

            ResyncEnvelope envelope = new ResyncEnvelope
            {
                Global = ReadServerGlobalSnapshot(state),
                Participants = ReadServerRoutingSnapshot(state),
                OwnDetail = own == null ? null : CreateDetailSnapshot(state, own)
            };
            string payload = JsonConvert.SerializeObject(envelope);

            if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId)
                ApplyResyncEnvelope(payload);
            else if (sender != 0L)
                SendClientAction(sender, state.EventId, ResyncStateAction, payload);
        }

        private static BloodMoonGlobalSnapshot ReadServerGlobalSnapshot(BloodMoonEventState state)
        {
            if (!string.IsNullOrEmpty(GlobalStateJson.Value))
            {
                try
                {
                    BloodMoonGlobalSnapshot snapshot = JsonConvert.DeserializeObject<BloodMoonGlobalSnapshot>(GlobalStateJson.Value);
                    if (snapshot != null && snapshot.EventId == state.EventId)
                        return snapshot;
                }
                catch
                {
                }
            }
            return BuildGlobalSnapshot(state, Math.Max(1, globalSnapshotRevision), seasonState.GetTotalSeconds());
        }

        private static BloodMoonParticipantSnapshot ReadServerRoutingSnapshot(BloodMoonEventState state)
        {
            if (!string.IsNullOrEmpty(ParticipantStateJson.Value))
            {
                try
                {
                    BloodMoonParticipantSnapshot snapshot = JsonConvert.DeserializeObject<BloodMoonParticipantSnapshot>(ParticipantStateJson.Value);
                    if (snapshot != null && snapshot.EventId == state.EventId)
                        return snapshot;
                }
                catch
                {
                }
            }
            return BuildRoutingSnapshot(state, Math.Max(1, participantSnapshotRevision));
        }

        private static BloodMoonParticipantDetailSnapshot CreateDetailSnapshot(BloodMoonEventState state, BloodMoonParticipantState participant)
        {
            return new BloodMoonParticipantDetailSnapshot
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
            };
        }

        private static bool TryValidateSenderPlayer(long sender, long playerId)
        {
            if (playerId == 0L || ZNet.instance == null || ZRoutedRpc.instance == null || ZDOMan.instance == null)
                return false;
            if (sender == ZRoutedRpc.instance.GetServerPeerID())
                return Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId;

            ZNetPeer peer = ZNet.instance.GetPeer(sender);
            if (peer == null || peer.m_characterID.IsNone())
                return false;
            ZDO zdo = ZDOMan.instance.GetZDO(peer.m_characterID);
            return zdo != null && zdo.GetLong(ZDOVars.s_playerID, 0L) == playerId;
        }

        private static void OnClientAction(long sender, ZPackage pkg)
        {
            if (!ReadHeader(pkg, out long eventId, out _) || ZNet.instance == null || ZNet.instance.IsServer() || !IsFromServer(sender))
                return;
            string action = pkg.ReadString();
            string payload = pkg.ReadString();

            if (action == ResyncStateAction)
            {
                ApplyResyncEnvelope(payload);
                return;
            }

            if (ClientGlobal.EventId == eventId)
            {
                DispatchClientAction(eventId, action, payload);
                return;
            }

            QueueClientAction(eventId, action, payload);
        }

        private static void ApplyResyncEnvelope(string payload)
        {
            ResyncEnvelope envelope;
            try
            {
                envelope = JsonConvert.DeserializeObject<ResyncEnvelope>(payload);
            }
            catch (Exception ex)
            {
                LogWarning($"[BloodMoon.Sync] Invalid resync payload: {ex.Message}");
                return;
            }

            if (envelope == null || envelope.Protocol != ProtocolVersion || envelope.Global == null || envelope.Participants == null ||
                envelope.Global.Schema != BloodMoonStateSchema.Current || envelope.Participants.Schema != BloodMoonStateSchema.Current ||
                envelope.Global.EventId != envelope.Participants.EventId)
                return;

            long eventId = envelope.Global.EventId;
            BloodMoonParticipantDetails.Reset();
            ClientGlobal = envelope.Global;
            ClientParticipants = envelope.Participants;
            if (envelope.OwnDetail != null)
                BloodMoonParticipantDetails.Apply(eventId, JsonConvert.SerializeObject(envelope.OwnDetail));

            pendingClientActions.RemoveAll(item => item.EventId != eventId);
            BloodMoonPresentation.OnGlobalSnapshot(ClientGlobal);
            BloodMoonStatus.UpdateLocal();
            FlushClientActions(eventId);
            LogInfo($"[BloodMoon.Sync] Applied explicit resync for event {eventId}, global revision {ClientGlobal.Revision}, routing revision {ClientParticipants.Revision}.");
        }

        private static void DispatchClientAction(long eventId, string action, string payload)
        {
            if (action == ParticipantDetailAction)
            {
                BloodMoonParticipantDetails.Apply(eventId, payload);
                return;
            }
            BloodMoonController.HandleClientAction(eventId, action, payload);
        }

        private static void QueueClientAction(long eventId, string action, string payload)
        {
            if (eventId < 0L || string.IsNullOrEmpty(action))
                return;
            if (pendingClientActions.Any(item => item.EventId == eventId && item.Action == action && item.Payload == payload))
                return;
            if (pendingClientActions.Count >= MaxPendingClientActions)
                pendingClientActions.RemoveAt(0);
            pendingClientActions.Add(new PendingClientAction
            {
                EventId = eventId,
                Action = action,
                Payload = payload ?? string.Empty
            });
        }

        private static void FlushClientActions(long eventId)
        {
            if (eventId < 0L || pendingClientActions.Count == 0)
                return;
            foreach (PendingClientAction item in pendingClientActions.Where(item => item.EventId == eventId).ToArray())
            {
                pendingClientActions.Remove(item);
                DispatchClientAction(item.EventId, item.Action, item.Payload);
            }
        }

        private static void OnGlobalStateChanged()
        {
            try
            {
                BloodMoonGlobalSnapshot snapshot = string.IsNullOrEmpty(GlobalStateJson.Value)
                    ? new BloodMoonGlobalSnapshot()
                    : JsonConvert.DeserializeObject<BloodMoonGlobalSnapshot>(GlobalStateJson.Value) ?? new BloodMoonGlobalSnapshot();
                if (snapshot.EventId == ClientGlobal.EventId && snapshot.Revision < ClientGlobal.Revision)
                    return;
                if (snapshot.EventId != ClientGlobal.EventId)
                    BloodMoonParticipantDetails.Reset();
                ClientGlobal = snapshot;
                BloodMoonPresentation.OnGlobalSnapshot(ClientGlobal);
                FlushClientActions(ClientGlobal.EventId);
            }
            catch (Exception ex)
            {
                LogError($"[BloodMoon.Sync] Invalid global snapshot: {ex}");
            }
        }

        private static void OnParticipantStateChanged()
        {
            try
            {
                BloodMoonParticipantSnapshot snapshot = string.IsNullOrEmpty(ParticipantStateJson.Value)
                    ? new BloodMoonParticipantSnapshot()
                    : JsonConvert.DeserializeObject<BloodMoonParticipantSnapshot>(ParticipantStateJson.Value) ?? new BloodMoonParticipantSnapshot();
                if (snapshot.EventId == ClientParticipants.EventId && snapshot.Revision < ClientParticipants.Revision)
                    return;
                ClientParticipants = snapshot;
                BloodMoonStatus.UpdateLocal();
            }
            catch (Exception ex)
            {
                LogError($"[BloodMoon.Sync] Invalid participant snapshot: {ex}");
            }
        }
    }
}
