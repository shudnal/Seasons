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
        internal const int ProtocolVersion = 1;
        private const int SyncPriorityGlobal = Priority.VeryLow + 4;
        private const int SyncPriorityParticipants = Priority.VeryLow + 3;

        private const string RpcDefeated = "Seasons.BloodMoon.Defeated";
        private const string RpcEnemyDeath = "Seasons.BloodMoon.EnemyDeath";
        private const string RpcSkillGain = "Seasons.BloodMoon.SkillGain";
        private const string RpcZoneClaim = "Seasons.BloodMoon.ZoneClaim";
        private const string RpcSpawnLease = "Seasons.BloodMoon.SpawnLease";
        private const string RpcSpawnReport = "Seasons.BloodMoon.SpawnReport";
        private const string RpcFade = "Seasons.BloodMoon.Fade";
        private const string RpcFadeAck = "Seasons.BloodMoon.FadeAck";
        private const string RpcResync = "Seasons.BloodMoon.Resync";
        private const string RpcClientAction = "Seasons.BloodMoon.ClientAction";

        internal static readonly CustomSyncedValue<string> GlobalStateJson = new CustomSyncedValue<string>(configSync, "Blood Moon global state", "", SyncPriorityGlobal);
        internal static readonly CustomSyncedValue<string> ParticipantStateJson = new CustomSyncedValue<string>(configSync, "Blood Moon participant state", "", SyncPriorityParticipants);

        private static bool valuesInitialized;
        private static ZRoutedRpc registeredRpc;

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
            rpc.Register<ZPackage>(RpcDefeated, OnDefeated);
            rpc.Register<ZPackage>(RpcEnemyDeath, OnEnemyDeath);
            rpc.Register<ZPackage>(RpcSkillGain, OnSkillGain);
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

            BloodMoonGlobalSnapshot global = new BloodMoonGlobalSnapshot
            {
                EventId = state.EventId,
                Phase = state.Phase,
                ResolutionStep = state.ResolutionStep,
                Schedule = state.Schedule,
                Revision = state.Revision,
                BloodBehaviorEnabled = state.BloodBehaviorEnabled,
                SpawnsStopped = state.SpawnsStopped,
                ServerTime = serverTime
            };
            BloodMoonParticipantSnapshot participants = new BloodMoonParticipantSnapshot
            {
                EventId = state.EventId,
                Revision = state.Revision,
                Participants = state.Participants.Values
                    .OrderBy(participant => participant.PlayerId)
                    .Select(CreatePublicParticipant)
                    .ToList()
            };

            GlobalStateJson.AssignValueSafeIfChanged(JsonConvert.SerializeObject(global));
            ParticipantStateJson.AssignValueSafeIfChanged(JsonConvert.SerializeObject(participants));
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
                JoinedLate = source.JoinedLate,
                CombatPoints = source.CombatPoints,
                DisplayProgress = source.DisplayProgress,
                MarkedAt = source.MarkedAt,
                FightingAt = source.FightingAt,
                GoalReachedAt = source.GoalReachedAt,
                ExitedAt = source.ExitedAt,
                ResolvedAt = source.ResolvedAt
            };
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

        private static void OnZoneClaim(long sender, ZPackage pkg)
        {
            if (!ReadHeader(pkg, out long eventId, out long playerId) || ZNet.instance == null || !ZNet.instance.IsServer())
                return;
            BloodMoonZoneOwnership.AcceptClaim(sender, eventId, playerId, pkg.ReadInt(), pkg.ReadInt());
        }

        private static void OnSpawnLease(long sender, ZPackage pkg)
        {
            if (!ReadHeader(pkg, out long eventId, out _) || ZNet.instance == null || ZNet.instance.IsServer() || !IsFromServer(sender) || ZDOMan.instance == null)
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
            if (!ReadHeader(pkg, out _, out _) || ZNet.instance == null || !ZNet.instance.IsServer())
                return;
            BloodMoonController.Instance?.PublishState(force: true);
        }

        private static void OnClientAction(long sender, ZPackage pkg)
        {
            if (!ReadHeader(pkg, out long eventId, out _) || ZNet.instance == null || ZNet.instance.IsServer() || !IsFromServer(sender))
                return;
            string action = pkg.ReadString();
            string payload = pkg.ReadString();
            BloodMoonController.HandleClientAction(eventId, action, payload);
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
                ClientGlobal = snapshot;
                BloodMoonPresentation.OnGlobalSnapshot(ClientGlobal);
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
            }
            catch (Exception ex)
            {
                LogError($"[BloodMoon.Sync] Invalid participant snapshot: {ex}");
            }
        }
    }
}
