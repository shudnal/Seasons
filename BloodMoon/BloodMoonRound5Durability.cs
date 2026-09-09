using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonRound5Runtime
    {
        internal static bool IsPreOutcomeDrainOpen(BloodMoonEventState state)
        {
            if (state == null)
                return false;
            if (state.IsCombatLive)
                return true;
            return state.Phase == BloodMoonEventPhase.Resolving &&
                (int)state.ResolutionStep < (int)BloodMoonResolutionStep.PublishingOutcomes;
        }

        internal static bool ValidateSenderPlayer(BloodMoonController controller, long sender, long playerId)
        {
            if (controller == null || playerId == 0L || ZRoutedRpc.instance == null)
                return false;

            if (sender == ZRoutedRpc.instance.GetServerPeerID())
                return Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId;

            return controller.GetPeerForPlayer(playerId) == sender;
        }

        internal static long CurrentWorldUid => ZNet.m_world != null ? ZNet.m_world.m_uid : 0L;

        internal static void SaveProfile(Player player, string reason)
        {
            if (player == null || player != Player.m_localPlayer || Game.instance == null)
                return;
            try
            {
                Game.instance.SavePlayerProfile(setLogoutPoint: false);
            }
            catch (Exception ex)
            {
                LogWarning($"[BloodMoon.Durability] Could not persist {reason}: {ex.Message}");
            }
        }
    }

    internal static class BloodMoonTerminalReliability
    {
        private const string ProfilePrefix = "Seasons.BloodMoon.PendingTerminal.";
        private const string LegacyDefeatedKey = "Seasons.BloodMoon.Defeated";
        private const string RpcRetain = "Seasons.BloodMoon.TerminalRetain";
        private const string RpcRetainAck = "Seasons.BloodMoon.TerminalRetainAck";
        private const string RpcReport = "Seasons.BloodMoon.TerminalReport";
        private const string RpcAck = "Seasons.BloodMoon.TerminalAck";
        private const float RetrySeconds = 1f;

        private sealed class PendingRetention
        {
            internal long WorldUid;
            internal long EventId;
            internal long PlayerId;
            internal BloodMoonParticipantExitReason Reason;
            internal float NextSendAt;
        }

        private static readonly Dictionary<string, PendingRetention> pendingRetentions = new Dictionary<string, PendingRetention>(StringComparer.Ordinal);
        private static readonly MethodInfo ExitParticipantMethod = AccessTools.Method(typeof(BloodMoonController), "ExitParticipant");
        private static ZRoutedRpc registeredRpc;
        private static float localRetryTimer;
        [ThreadStatic] private static bool committingTerminal;

        internal static void RegisterRpc()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(registeredRpc, rpc))
                return;
            registeredRpc = rpc;
            localRetryTimer = 0f;
            rpc.Register<ZPackage>(RpcRetain, OnRetainRpc);
            rpc.Register<ZPackage>(RpcRetainAck, OnRetainAckRpc);
            rpc.Register<ZPackage>(RpcReport, OnReportRpc);
            rpc.Register<ZPackage>(RpcAck, OnAckRpc);
        }

        internal static bool BeginWithdrawal(BloodMoonController controller, BloodMoonParticipantState participant, BloodMoonParticipantExitReason reason)
        {
            if (committingTerminal || reason != BloodMoonParticipantExitReason.Withdrawn || controller?.State == null ||
                participant == null || participant.IsTerminal)
                return true;

            BloodMoonEventState state = controller.State;
            long worldUid = state.WorldUid;
            if (worldUid == 0L || state.EventId < 0L)
                return true;

            Player local = Player.m_localPlayer;
            if (local != null && local.GetPlayerID() == participant.PlayerId)
            {
                StoreLocal(local, worldUid, state.EventId, participant.PlayerId, reason);
                return true;
            }

            long peer = controller.GetPeerForPlayer(participant.PlayerId);
            if (peer == 0L)
                return true;

            string key = MakeServerKey(worldUid, state.EventId, participant.PlayerId, reason);
            if (!pendingRetentions.TryGetValue(key, out PendingRetention pending))
            {
                pending = new PendingRetention
                {
                    WorldUid = worldUid,
                    EventId = state.EventId,
                    PlayerId = participant.PlayerId,
                    Reason = reason,
                    NextSendAt = 0f
                };
                pendingRetentions[key] = pending;
            }
            SendRetention(controller, pending, force: true);
            return false;
        }

        internal static void AfterExit(BloodMoonController controller, BloodMoonParticipantState participant, BloodMoonParticipantExitReason reason)
        {
            BloodMoonEventState state = controller?.State;
            if (state == null || participant == null || !participant.IsTerminal || participant.ExitReason != reason ||
                reason != BloodMoonParticipantExitReason.Withdrawn && reason != BloodMoonParticipantExitReason.Defeated)
                return;

            if (!BloodMoonPersistence.Save(state))
                return;
            AcknowledgeTerminal(controller, state.WorldUid, state.EventId, participant.PlayerId, reason);
        }

        internal static bool HandleDefeatedPrefix(BloodMoonController controller, long sender, long eventId, long playerId)
        {
            BloodMoonEventState state = controller?.State;
            if (state == null || state.EventId != eventId || !state.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant) ||
                !BloodMoonRound5Runtime.ValidateSenderPlayer(controller, sender, playerId))
                return true;

            if (participant.IsTerminal)
            {
                if (participant.ExitReason == BloodMoonParticipantExitReason.Disconnected)
                    participant.ExitReason = BloodMoonParticipantExitReason.Defeated;
                if (participant.ExitReason == BloodMoonParticipantExitReason.Defeated && BloodMoonPersistence.Save(state))
                    SendTerminalAck(sender, state.WorldUid, eventId, playerId, BloodMoonParticipantExitReason.Defeated);
                return false;
            }

            if (!participant.IsCombatActive || state.IsCombatLive)
                return true;
            if (!BloodMoonRound5Runtime.IsPreOutcomeDrainOpen(state))
                return false;

            CommitTerminal(controller, sender, state.WorldUid, eventId, playerId, BloodMoonParticipantExitReason.Defeated);
            return false;
        }

        internal static void StoreDefeatedBeforeSend(long eventId, long playerId)
        {
            Player player = Player.m_localPlayer;
            long worldUid = BloodMoonRound5Runtime.CurrentWorldUid;
            if (player == null || player.GetPlayerID() != playerId || worldUid == 0L || eventId < 0L)
                return;
            StoreLocal(player, worldUid, eventId, playerId, BloodMoonParticipantExitReason.Defeated);
        }

        internal static void StoreWithdrawal(Player player)
        {
            long worldUid = BloodMoonRound5Runtime.CurrentWorldUid;
            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            if (player == null || player != Player.m_localPlayer || worldUid == 0L || eventId < 0L)
                return;
            StoreLocal(player, worldUid, eventId, player.GetPlayerID(), BloodMoonParticipantExitReason.Withdrawn);
        }

        internal static bool ShouldIgnoreEnrollment(long eventId)
        {
            Player player = Player.m_localPlayer;
            if (player == null || eventId < 0L)
                return false;

            if (TryLoadLocal(player, eventId, out BloodMoonParticipantExitReason reason))
            {
                SendLocalReport(player, eventId, reason);
                return true;
            }

            if (HasLegacyDefeated(player, eventId))
            {
                StoreLocal(player, BloodMoonRound5Runtime.CurrentWorldUid, eventId, player.GetPlayerID(), BloodMoonParticipantExitReason.Defeated);
                SendLocalReport(player, eventId, BloodMoonParticipantExitReason.Defeated);
                return true;
            }
            return false;
        }

        internal static void Tick(float dt)
        {
            RegisterRpc();
            TickLocal(dt);
            TickServer();
        }

        internal static bool HasPendingServer(long eventId)
        {
            return pendingRetentions.Values.Any(item => item.EventId == eventId);
        }

        internal static void Reset()
        {
            pendingRetentions.Clear();
            localRetryTimer = 0f;
            committingTerminal = false;
        }

        private static void TickLocal(float dt)
        {
            Player player = Player.m_localPlayer;
            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            if (player == null || eventId < 0L || !TryLoadLocal(player, eventId, out BloodMoonParticipantExitReason reason))
                return;

            BloodMoonEventPhase phase = BloodMoonNetwork.ClientGlobal.Phase;
            bool drainOpen = phase == BloodMoonEventPhase.Active || phase == BloodMoonEventPhase.AutoCompleting ||
                phase == BloodMoonEventPhase.Resolving &&
                (int)BloodMoonNetwork.ClientGlobal.ResolutionStep < (int)BloodMoonResolutionStep.PublishingOutcomes;
            if (!drainOpen)
                return;

            localRetryTimer -= Mathf.Max(0f, dt);
            if (localRetryTimer > 0f)
                return;
            localRetryTimer = RetrySeconds;
            SendLocalReport(player, eventId, reason);
        }

        private static void TickServer()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || pendingRetentions.Count == 0)
                return;

            BloodMoonController controller = BloodMoonController.Instance;
            BloodMoonEventState state = controller?.State;
            foreach (KeyValuePair<string, PendingRetention> entry in pendingRetentions.ToArray())
            {
                PendingRetention pending = entry.Value;
                if (state == null || state.WorldUid != pending.WorldUid || state.EventId != pending.EventId ||
                    !state.Participants.TryGetValue(pending.PlayerId, out BloodMoonParticipantState participant))
                {
                    pendingRetentions.Remove(entry.Key);
                    continue;
                }

                if (participant.IsTerminal)
                {
                    pendingRetentions.Remove(entry.Key);
                    if (BloodMoonPersistence.Save(state))
                        AcknowledgeTerminal(controller, pending.WorldUid, pending.EventId, pending.PlayerId, pending.Reason);
                    continue;
                }

                SendRetention(controller, pending, force: false);
            }
        }

        private static void SendRetention(BloodMoonController controller, PendingRetention pending, bool force)
        {
            if (controller == null || pending == null || ZRoutedRpc.instance == null)
                return;
            float now = Time.realtimeSinceStartup;
            if (!force && now < pending.NextSendAt)
                return;
            long peer = controller.GetPeerForPlayer(pending.PlayerId);
            if (peer == 0L)
                return;
            pending.NextSendAt = now + RetrySeconds;

            ZPackage package = new ZPackage();
            package.Write(BloodMoonNetwork.ProtocolVersion);
            package.Write(pending.WorldUid);
            package.Write(pending.EventId);
            package.Write(pending.PlayerId);
            package.Write((int)pending.Reason);
            ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcRetain, package);
        }

        private static void OnRetainRpc(long sender, ZPackage package)
        {
            if (!ReadTerminalPackage(sender, package, requireServerSender: true, out long worldUid, out long eventId, out long playerId,
                    out BloodMoonParticipantExitReason reason))
                return;

            Player player = Player.m_localPlayer;
            if (player == null || player.GetPlayerID() != playerId || BloodMoonRound5Runtime.CurrentWorldUid != worldUid)
                return;
            StoreLocal(player, worldUid, eventId, playerId, reason);

            ZPackage ack = CreateTerminalPackage(worldUid, eventId, playerId, reason);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcRetainAck, ack);
        }

        private static void OnRetainAckRpc(long sender, ZPackage package)
        {
            if (!ReadTerminalPackage(sender, package, requireServerSender: false, out long worldUid, out long eventId, out long playerId,
                    out BloodMoonParticipantExitReason reason))
                return;

            BloodMoonController controller = BloodMoonController.Instance;
            if (!BloodMoonRound5Runtime.ValidateSenderPlayer(controller, sender, playerId))
                return;
            string key = MakeServerKey(worldUid, eventId, playerId, reason);
            if (!pendingRetentions.Remove(key))
                return;
            CommitTerminal(controller, sender, worldUid, eventId, playerId, reason);
        }

        private static void OnReportRpc(long sender, ZPackage package)
        {
            if (!ReadTerminalPackage(sender, package, requireServerSender: false, out long worldUid, out long eventId, out long playerId,
                    out BloodMoonParticipantExitReason reason))
                return;
            BloodMoonController controller = BloodMoonController.Instance;
            if (!BloodMoonRound5Runtime.ValidateSenderPlayer(controller, sender, playerId))
                return;
            CommitTerminal(controller, sender, worldUid, eventId, playerId, reason);
        }

        private static void OnAckRpc(long sender, ZPackage package)
        {
            if (!ReadTerminalPackage(sender, package, requireServerSender: true, out long worldUid, out long eventId, out long playerId,
                    out BloodMoonParticipantExitReason reason))
                return;
            Player player = Player.m_localPlayer;
            if (player == null || player.GetPlayerID() != playerId || BloodMoonRound5Runtime.CurrentWorldUid != worldUid)
                return;
            ClearLocal(player, worldUid, eventId, playerId);
        }

        private static void CommitTerminal(BloodMoonController controller, long sender, long worldUid, long eventId, long playerId,
            BloodMoonParticipantExitReason reason)
        {
            BloodMoonEventState state = controller?.State;
            if (state == null || state.WorldUid != worldUid || state.EventId != eventId ||
                reason != BloodMoonParticipantExitReason.Defeated && reason != BloodMoonParticipantExitReason.Withdrawn ||
                !state.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant))
                return;

            if (participant.IsTerminal)
            {
                if (participant.ExitReason == BloodMoonParticipantExitReason.Disconnected)
                    participant.ExitReason = reason;
                if (participant.ExitReason == reason && BloodMoonPersistence.Save(state))
                    SendTerminalAck(sender, worldUid, eventId, playerId, reason);
                return;
            }

            if (!participant.IsCombatActive || !BloodMoonRound5Runtime.IsPreOutcomeDrainOpen(state) || ExitParticipantMethod == null)
                return;

            committingTerminal = true;
            try
            {
                ExitParticipantMethod.Invoke(controller, new object[] { participant, reason, seasonState.GetTotalSeconds() });
            }
            finally
            {
                committingTerminal = false;
            }

            DispatchTerminalAction(controller, state, participant, reason);
            if (participant.IsTerminal && participant.ExitReason == reason && BloodMoonPersistence.Save(state))
                SendTerminalAck(sender, worldUid, eventId, playerId, reason);
        }

        private static void DispatchTerminalAction(BloodMoonController controller, BloodMoonEventState state, BloodMoonParticipantState participant,
            BloodMoonParticipantExitReason reason)
        {
            if (controller == null || state == null || participant == null)
                return;
            string action = reason == BloodMoonParticipantExitReason.Defeated ? "defeated" : "withdrawn";
            Player local = Player.m_localPlayer;
            if (local != null && local.GetPlayerID() == participant.PlayerId)
            {
                BloodMoonController.HandleClientAction(state.EventId, action, string.Empty);
                return;
            }
            long peer = controller.GetPeerForPlayer(participant.PlayerId);
            if (peer != 0L)
                BloodMoonNetwork.SendClientAction(peer, state.EventId, action);
        }

        private static void AcknowledgeTerminal(BloodMoonController controller, long worldUid, long eventId, long playerId,
            BloodMoonParticipantExitReason reason)
        {
            Player local = Player.m_localPlayer;
            if (local != null && local.GetPlayerID() == playerId)
            {
                ClearLocal(local, worldUid, eventId, playerId);
                return;
            }
            long peer = controller?.GetPeerForPlayer(playerId) ?? 0L;
            if (peer != 0L)
                SendTerminalAck(peer, worldUid, eventId, playerId, reason);
        }

        private static void SendTerminalAck(long peer, long worldUid, long eventId, long playerId, BloodMoonParticipantExitReason reason)
        {
            if (peer == 0L || ZRoutedRpc.instance == null || ZNet.instance == null || !ZNet.instance.IsServer())
                return;
            ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcAck, CreateTerminalPackage(worldUid, eventId, playerId, reason));
        }

        private static void SendLocalReport(Player player, long eventId, BloodMoonParticipantExitReason reason)
        {
            if (player == null || ZRoutedRpc.instance == null)
                return;
            long worldUid = BloodMoonRound5Runtime.CurrentWorldUid;
            if (worldUid == 0L)
                return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcReport,
                CreateTerminalPackage(worldUid, eventId, player.GetPlayerID(), reason));
        }

        private static void StoreLocal(Player player, long worldUid, long eventId, long playerId, BloodMoonParticipantExitReason reason)
        {
            if (player == null || worldUid == 0L || eventId < 0L || playerId == 0L ||
                reason != BloodMoonParticipantExitReason.Defeated && reason != BloodMoonParticipantExitReason.Withdrawn)
                return;
            string key = MakeProfileKey(worldUid, eventId, playerId);
            string value = ((int)reason).ToString(CultureInfo.InvariantCulture);
            if (player.m_customData.TryGetValue(key, out string existing) && existing == value)
                return;
            player.m_customData[key] = value;
            BloodMoonRound5Runtime.SaveProfile(player, $"pending {reason} terminal marker");
        }

        private static bool TryLoadLocal(Player player, long eventId, out BloodMoonParticipantExitReason reason)
        {
            reason = BloodMoonParticipantExitReason.None;
            long worldUid = BloodMoonRound5Runtime.CurrentWorldUid;
            if (player == null || worldUid == 0L || eventId < 0L ||
                !player.m_customData.TryGetValue(MakeProfileKey(worldUid, eventId, player.GetPlayerID()), out string raw) ||
                !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                return false;
            reason = (BloodMoonParticipantExitReason)value;
            return reason == BloodMoonParticipantExitReason.Defeated || reason == BloodMoonParticipantExitReason.Withdrawn;
        }

        private static void ClearLocal(Player player, long worldUid, long eventId, long playerId)
        {
            if (player == null || worldUid == 0L || eventId < 0L || playerId == 0L)
                return;
            if (player.m_customData.Remove(MakeProfileKey(worldUid, eventId, playerId)))
                BloodMoonRound5Runtime.SaveProfile(player, "acknowledged Blood Moon terminal marker");
        }

        private static bool HasLegacyDefeated(Player player, long eventId)
        {
            if (player == null || !player.m_customData.TryGetValue(LegacyDefeatedKey, out string json) || string.IsNullOrWhiteSpace(json))
                return false;
            try
            {
                JObject source = JObject.Parse(json);
                return source.Value<long?>("WorldUid") == BloodMoonRound5Runtime.CurrentWorldUid && source.Value<long?>("EventId") == eventId;
            }
            catch
            {
                return false;
            }
        }

        private static bool ReadTerminalPackage(long sender, ZPackage package, bool requireServerSender, out long worldUid, out long eventId,
            out long playerId, out BloodMoonParticipantExitReason reason)
        {
            worldUid = 0L;
            eventId = -1L;
            playerId = 0L;
            reason = BloodMoonParticipantExitReason.None;
            if (package == null || ZRoutedRpc.instance == null || package.ReadInt() != BloodMoonNetwork.ProtocolVersion)
                return false;
            if (requireServerSender && sender != ZRoutedRpc.instance.GetServerPeerID())
                return false;
            worldUid = package.ReadLong();
            eventId = package.ReadLong();
            playerId = package.ReadLong();
            reason = (BloodMoonParticipantExitReason)package.ReadInt();
            return worldUid != 0L && eventId >= 0L && playerId != 0L &&
                (reason == BloodMoonParticipantExitReason.Defeated || reason == BloodMoonParticipantExitReason.Withdrawn);
        }

        private static ZPackage CreateTerminalPackage(long worldUid, long eventId, long playerId, BloodMoonParticipantExitReason reason)
        {
            ZPackage package = new ZPackage();
            package.Write(BloodMoonNetwork.ProtocolVersion);
            package.Write(worldUid);
            package.Write(eventId);
            package.Write(playerId);
            package.Write((int)reason);
            return package;
        }

        private static string MakeProfileKey(long worldUid, long eventId, long playerId)
        {
            return ProfilePrefix + worldUid.ToString(CultureInfo.InvariantCulture) + "." + eventId.ToString(CultureInfo.InvariantCulture) + "." +
                playerId.ToString(CultureInfo.InvariantCulture);
        }

        private static string MakeServerKey(long worldUid, long eventId, long playerId, BloodMoonParticipantExitReason reason)
        {
            return worldUid.ToString(CultureInfo.InvariantCulture) + ":" + eventId.ToString(CultureInfo.InvariantCulture) + ":" +
                playerId.ToString(CultureInfo.InvariantCulture) + ":" + ((int)reason).ToString(CultureInfo.InvariantCulture);
        }
    }

    internal static class BloodMoonServerOwnedDeathRetention
    {
        private const string ProfilePrefix = "Seasons.BloodMoon.PendingEnemyDeath.";
        private const string RpcRetain = "Seasons.BloodMoon.ServerOwnedDeathRetain";
        private const string RpcRetainAck = "Seasons.BloodMoon.ServerOwnedDeathRetainAck";
        private const float RetrySeconds = 1f;

        private sealed class PendingRetention
        {
            internal long EventId;
            internal long PlayerId;
            internal ZDOID EnemyId;
            internal float Points;
            internal float NextSendAt;
        }

        private static readonly Dictionary<ZDOID, PendingRetention> pending = new Dictionary<ZDOID, PendingRetention>();
        private static ZRoutedRpc registeredRpc;

        internal static void RegisterRpc()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(registeredRpc, rpc))
                return;
            registeredRpc = rpc;
            rpc.Register<ZPackage>(RpcRetain, OnRetainRpc);
            rpc.Register<ZPackage>(RpcRetainAck, OnRetainAckRpc);
        }

        internal static bool InterceptServerSend(long eventId, ZDOID enemyId, long playerId, float points)
        {
            if (points != 0f || ZNet.instance == null || !ZNet.instance.IsServer() || Player.m_localPlayer != null ||
                enemyId.IsNone() || eventId < 0L || playerId == 0L)
                return false;

            BloodMoonController controller = BloodMoonController.Instance;
            BloodMoonEventState state = controller?.State;
            if (state == null || state.EventId != eventId || !state.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant) ||
                !participant.IsCombatActive)
                return false;

            long peer = controller.GetPeerForPlayer(playerId);
            if (peer == 0L)
                return false;

            float replayPoints = BloodMoonCombat.GetPointsForEnemy(enemyId, 0f);
            if (replayPoints <= 0f || float.IsNaN(replayPoints) || float.IsInfinity(replayPoints))
                return false;

            pending[enemyId] = new PendingRetention
            {
                EventId = eventId,
                PlayerId = playerId,
                EnemyId = enemyId,
                Points = replayPoints,
                NextSendAt = 0f
            };
            Send(controller, pending[enemyId], force: true);
            return true;
        }

        internal static void Tick()
        {
            RegisterRpc();
            if (ZNet.instance == null || !ZNet.instance.IsServer() || pending.Count == 0)
                return;
            BloodMoonController controller = BloodMoonController.Instance;
            BloodMoonEventState state = controller?.State;
            foreach (KeyValuePair<ZDOID, PendingRetention> entry in pending.ToArray())
            {
                PendingRetention item = entry.Value;
                if (state == null || state.EventId != item.EventId || !state.Participants.TryGetValue(item.PlayerId, out BloodMoonParticipantState participant) ||
                    !participant.IsCombatActive)
                {
                    pending.Remove(entry.Key);
                    continue;
                }
                Send(controller, item, force: false);
            }
        }

        internal static bool HasPending(long eventId) => pending.Values.Any(item => item.EventId == eventId);

        internal static void Reset() => pending.Clear();

        private static void Send(BloodMoonController controller, PendingRetention item, bool force)
        {
            if (controller == null || item == null || ZRoutedRpc.instance == null)
                return;
            float now = Time.realtimeSinceStartup;
            if (!force && now < item.NextSendAt)
                return;
            long peer = controller.GetPeerForPlayer(item.PlayerId);
            if (peer == 0L)
                return;
            item.NextSendAt = now + RetrySeconds;

            ZPackage package = new ZPackage();
            package.Write(BloodMoonNetwork.ProtocolVersion);
            package.Write(BloodMoonRound5Runtime.CurrentWorldUid);
            package.Write(item.EventId);
            package.Write(item.PlayerId);
            package.Write(item.EnemyId);
            package.Write(item.Points);
            ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcRetain, package);
        }

        private static void OnRetainRpc(long sender, ZPackage package)
        {
            if (package == null || ZRoutedRpc.instance == null || sender != ZRoutedRpc.instance.GetServerPeerID() ||
                package.ReadInt() != BloodMoonNetwork.ProtocolVersion)
                return;
            long worldUid = package.ReadLong();
            long eventId = package.ReadLong();
            long playerId = package.ReadLong();
            ZDOID enemyId = package.ReadZDOID();
            float points = package.ReadSingle();
            Player player = Player.m_localPlayer;
            if (player == null || player.GetPlayerID() != playerId || BloodMoonRound5Runtime.CurrentWorldUid != worldUid || eventId < 0L ||
                enemyId.IsNone() || points <= 0f || float.IsNaN(points) || float.IsInfinity(points))
                return;

            string key = MakeProfileKey(worldUid, eventId, enemyId);
            player.m_customData[key] = playerId.ToString(CultureInfo.InvariantCulture) + "|" + points.ToString("R", CultureInfo.InvariantCulture);
            BloodMoonRound5Runtime.SaveProfile(player, "server-owned enemy death replay");

            ZPackage ack = new ZPackage();
            ack.Write(BloodMoonNetwork.ProtocolVersion);
            ack.Write(worldUid);
            ack.Write(eventId);
            ack.Write(playerId);
            ack.Write(enemyId);
            ack.Write(points);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcRetainAck, ack);
        }

        private static void OnRetainAckRpc(long sender, ZPackage package)
        {
            if (package == null || ZNet.instance == null || !ZNet.instance.IsServer() || package.ReadInt() != BloodMoonNetwork.ProtocolVersion)
                return;
            long worldUid = package.ReadLong();
            long eventId = package.ReadLong();
            long playerId = package.ReadLong();
            ZDOID enemyId = package.ReadZDOID();
            float points = package.ReadSingle();
            BloodMoonController controller = BloodMoonController.Instance;
            BloodMoonEventState state = controller?.State;
            if (state == null || state.WorldUid != worldUid || state.EventId != eventId || !pending.TryGetValue(enemyId, out PendingRetention item) ||
                item.PlayerId != playerId || Mathf.Abs(item.Points - points) > 0.001f ||
                !BloodMoonRound5Runtime.ValidateSenderPlayer(controller, sender, playerId))
                return;

            pending.Remove(enemyId);
            controller.OnEnemyDeathReport(sender, eventId, playerId, enemyId, points);
        }

        private static string MakeProfileKey(long worldUid, long eventId, ZDOID enemyId)
        {
            return ProfilePrefix + worldUid.ToString(CultureInfo.InvariantCulture) + "." + eventId.ToString(CultureInfo.InvariantCulture) + "." + enemyId;
        }
    }

    internal static class BloodMoonOutcomeDrainGate
    {
        private const float MinimumDrainSeconds = 5f;
        private const float RestartReconnectSeconds = 30f;
        private static readonly HashSet<long> restartAwaiting = new HashSet<long>();
        private static long restartEventId = -1L;
        private static float restartUntil;
        private static float settleUntil;
        private static long holdEventId = -1L;
        private static float minimumHoldUntil;

        internal static void BeginRestart(BloodMoonEventState state)
        {
            restartAwaiting.Clear();
            restartEventId = -1L;
            restartUntil = 0f;
            settleUntil = 0f;
            if (state == null || state.EventId < 0L ||
                state.Phase != BloodMoonEventPhase.Marked && !state.IsCombatLive &&
                !(state.Phase == BloodMoonEventPhase.Resolving && (int)state.ResolutionStep < (int)BloodMoonResolutionStep.PublishingOutcomes))
                return;

            restartEventId = state.EventId;
            restartUntil = Time.realtimeSinceStartup + RestartReconnectSeconds;
            foreach (BloodMoonParticipantState participant in state.Participants.Values)
            {
                if (participant.Phase == BloodMoonParticipantPhase.Marked || participant.IsCombatActive)
                    restartAwaiting.Add(participant.PlayerId);
            }
        }

        internal static bool ShouldHold(BloodMoonController controller)
        {
            BloodMoonEventState state = controller?.State;
            if (state == null || state.ResolutionCancelledBeforeCombat || state.EventId < 0L)
                return false;

            float now = Time.realtimeSinceStartup;
            if (holdEventId != state.EventId)
            {
                holdEventId = state.EventId;
                minimumHoldUntil = now + MinimumDrainSeconds;
            }

            if (restartEventId == state.EventId && restartAwaiting.Count > 0)
            {
                foreach (long playerId in restartAwaiting.ToArray())
                {
                    bool connected = controller.GetPeerForPlayer(playerId) != 0L ||
                        Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId;
                    if (!connected)
                        continue;
                    restartAwaiting.Remove(playerId);
                    settleUntil = Math.Max(settleUntil, now + MinimumDrainSeconds);
                }
                if (restartAwaiting.Count > 0 && now < restartUntil)
                    return true;
                if (now >= restartUntil)
                    restartAwaiting.Clear();
            }

            if (now < minimumHoldUntil || now < settleUntil)
                return true;
            if (BloodMoonTerminalReliability.HasPendingServer(state.EventId) || BloodMoonServerOwnedDeathRetention.HasPending(state.EventId))
                return true;
            return false;
        }

        internal static void Reset()
        {
            restartAwaiting.Clear();
            restartEventId = -1L;
            restartUntil = 0f;
            settleUntil = 0f;
            holdEventId = -1L;
            minimumHoldUntil = 0f;
        }
    }

    internal static class BloodMoonSkillScopedReliability
    {
        private const string RecordPrefix = "Seasons.BloodMoon.LiveSkillReports.";
        private const float RetrySeconds = 2f;
        private const int MaxReportsPerRetry = 8;
        private static readonly FieldInfo LocalSequenceField = AccessTools.Field(typeof(BloodMoonSkills), "localReportSequence");
        private static readonly FieldInfo LocalBonusField = AccessTools.Field(typeof(BloodMoonSkills), "localLiveBonusUsed");
        private static float retryTimer;

        [Serializable]
        [JsonObject(MemberSerialization.OptIn)]
        private sealed class PendingReport
        {
            [JsonProperty] public long Sequence;
            [JsonProperty] public int Skill;
            [JsonProperty] public float BaseEquivalent;
            [JsonProperty] public float LiveBonusEquivalent;
        }

        [Serializable]
        [JsonObject(MemberSerialization.OptIn)]
        private sealed class Record
        {
            [JsonProperty] public long WorldUid;
            [JsonProperty] public long EventId;
            [JsonProperty] public long PlayerId;
            [JsonProperty] public long LastAckSequence;
            [JsonProperty] public long LastSequence;
            [JsonProperty] public float LiveBonusUsed;
            [JsonProperty] public List<PendingReport> Pending = new List<PendingReport>();
        }

        internal static void TrackOutgoing(long eventId, long playerId, long sequence, Skills.SkillType skill, float baseEquivalent, float liveBonusEquivalent)
        {
            Player player = Player.m_localPlayer;
            long worldUid = BloodMoonRound5Runtime.CurrentWorldUid;
            if (player == null || player.GetPlayerID() != playerId || worldUid == 0L || eventId < 0L || sequence <= 0L)
                return;
            Record record = Load(player, worldUid, eventId, playerId) ?? new Record { WorldUid = worldUid, EventId = eventId, PlayerId = playerId };
            ReconcileServerBaseline(record);
            record.LastSequence = Math.Max(record.LastSequence, sequence);
            record.LiveBonusUsed = Math.Max(record.LiveBonusUsed, GetRuntimeLiveBonusUsed(eventId));
            if (!record.Pending.Any(item => item.Sequence == sequence))
            {
                record.Pending.Add(new PendingReport
                {
                    Sequence = sequence,
                    Skill = (int)skill,
                    BaseEquivalent = baseEquivalent,
                    LiveBonusEquivalent = liveBonusEquivalent
                });
            }
            Save(player, record);
        }

        internal static void TickLocal(float dt)
        {
            Player player = Player.m_localPlayer;
            long worldUid = BloodMoonRound5Runtime.CurrentWorldUid;
            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            if (player == null || worldUid == 0L || eventId < 0L)
                return;
            Record record = Load(player, worldUid, eventId, player.GetPlayerID());
            if (record == null)
                return;
            if (ReconcileServerBaseline(record))
                Save(player, record);
            SyncRuntime(record);

            BloodMoonEventPhase phase = BloodMoonNetwork.ClientGlobal.Phase;
            bool drainOpen = phase == BloodMoonEventPhase.Active || phase == BloodMoonEventPhase.AutoCompleting ||
                phase == BloodMoonEventPhase.Resolving &&
                (int)BloodMoonNetwork.ClientGlobal.ResolutionStep < (int)BloodMoonResolutionStep.PublishingOutcomes;
            if (!drainOpen || record.Pending.Count == 0)
                return;

            retryTimer -= Mathf.Max(0f, dt);
            if (retryTimer > 0f)
                return;
            retryTimer = RetrySeconds;
            foreach (PendingReport report in record.Pending.Where(item => item.Sequence > record.LastAckSequence).OrderBy(item => item.Sequence)
                .Take(MaxReportsPerRetry).ToArray())
            {
                BloodMoonNetwork.SendSkillGain(eventId, record.PlayerId, report.Sequence, (Skills.SkillType)report.Skill,
                    report.BaseEquivalent, report.LiveBonusEquivalent);
            }
        }

        internal static float GetPersistedLiveBonusUsed(long eventId)
        {
            Player player = Player.m_localPlayer;
            long worldUid = BloodMoonRound5Runtime.CurrentWorldUid;
            Record record = player != null && worldUid != 0L ? Load(player, worldUid, eventId, player.GetPlayerID()) : null;
            return record != null ? Mathf.Max(0f, record.LiveBonusUsed) : 0f;
        }

        internal static void SyncRuntimeFromProfile(Player player, long eventId)
        {
            long worldUid = BloodMoonRound5Runtime.CurrentWorldUid;
            if (player == null || worldUid == 0L || eventId < 0L)
                return;
            Record record = Load(player, worldUid, eventId, player.GetPlayerID());
            if (record == null)
                return;
            if (ReconcileServerBaseline(record))
                Save(player, record);
            SyncRuntime(record);
        }

        internal static void HandleAck(long sender, ZPackage package)
        {
            if (package == null || ZRoutedRpc.instance == null || sender != ZRoutedRpc.instance.GetServerPeerID() ||
                package.ReadInt() != BloodMoonNetwork.ProtocolVersion)
                return;
            long eventId = package.ReadLong();
            long playerId = package.ReadLong();
            long sequence = package.ReadLong();
            Player player = Player.m_localPlayer;
            long worldUid = BloodMoonRound5Runtime.CurrentWorldUid;
            if (player == null || player.GetPlayerID() != playerId || worldUid == 0L || sequence < 0L)
                return;
            Record record = Load(player, worldUid, eventId, playerId);
            if (record == null)
                return;
            long acknowledged = Math.Max(record.LastAckSequence, sequence);
            record.LastAckSequence = acknowledged;
            record.LastSequence = Math.Max(record.LastSequence, acknowledged);
            record.Pending.RemoveAll(item => item.Sequence <= acknowledged);
            SyncRuntime(record);
            Save(player, record);
        }

        private static bool ReconcileServerBaseline(Record record)
        {
            BloodMoonParticipantDetailSnapshot detail = BloodMoonParticipantDetails.ClientOwn;
            if (record == null || detail == null || detail.EventId != record.EventId || detail.PlayerId != record.PlayerId)
                return false;
            bool changed = false;
            long acknowledged = Math.Max(record.LastAckSequence, Math.Max(0L, detail.LastSkillReportSequence));
            if (acknowledged != record.LastAckSequence)
            {
                record.LastAckSequence = acknowledged;
                changed = true;
            }
            if (record.Pending.RemoveAll(item => item.Sequence <= acknowledged) > 0)
                changed = true;
            long sequence = Math.Max(record.LastSequence, Math.Max(0L, detail.LastSkillReportSequence));
            if (sequence != record.LastSequence)
            {
                record.LastSequence = sequence;
                changed = true;
            }
            float used = Math.Max(record.LiveBonusUsed, Mathf.Max(0f, detail.LiveSkillBonusUsed));
            if (Mathf.Abs(used - record.LiveBonusUsed) > 0.0001f)
            {
                record.LiveBonusUsed = used;
                changed = true;
            }
            return changed;
        }

        private static void SyncRuntime(Record record)
        {
            if (record == null)
                return;
            if (LocalSequenceField != null)
            {
                long current = (long)LocalSequenceField.GetValue(null);
                if (record.LastSequence > current)
                    LocalSequenceField.SetValue(null, record.LastSequence);
            }
            if (LocalBonusField?.GetValue(null) is Dictionary<long, float> bonusByEvent)
            {
                float current = bonusByEvent.TryGetValue(record.EventId, out float value) ? value : 0f;
                bonusByEvent[record.EventId] = Mathf.Max(current, Mathf.Max(0f, record.LiveBonusUsed));
            }
        }

        private static float GetRuntimeLiveBonusUsed(long eventId)
        {
            if (LocalBonusField?.GetValue(null) is Dictionary<long, float> bonusByEvent && bonusByEvent.TryGetValue(eventId, out float value))
                return Mathf.Max(0f, value);
            return 0f;
        }

        private static Record Load(Player player, long worldUid, long eventId, long playerId)
        {
            if (player == null || worldUid == 0L || eventId < 0L || playerId == 0L)
                return null;
            string key = MakeKey(worldUid, eventId, playerId);
            if (!player.m_customData.TryGetValue(key, out string json) || string.IsNullOrWhiteSpace(json))
                return null;
            try
            {
                Record record = JsonConvert.DeserializeObject<Record>(json);
                if (record == null || record.WorldUid != worldUid || record.EventId != eventId || record.PlayerId != playerId)
                    return null;
                record.Pending ??= new List<PendingReport>();
                return record;
            }
            catch (Exception ex)
            {
                LogWarning($"[BloodMoon.Skill] Invalid scoped live skill record removed: {ex.Message}");
                player.m_customData.Remove(key);
                return null;
            }
        }

        private static void Save(Player player, Record record)
        {
            if (player == null || record == null || record.WorldUid == 0L || record.EventId < 0L || record.PlayerId == 0L)
                return;
            record.Pending = (record.Pending ?? new List<PendingReport>())
                .Where(item => item != null && item.Sequence > record.LastAckSequence)
                .GroupBy(item => item.Sequence)
                .Select(group => group.First())
                .OrderBy(item => item.Sequence)
                .ToList();
            player.m_customData[MakeKey(record.WorldUid, record.EventId, record.PlayerId)] = JsonConvert.SerializeObject(record);
        }

        private static string MakeKey(long worldUid, long eventId, long playerId)
        {
            return RecordPrefix + worldUid.ToString(CultureInfo.InvariantCulture) + "." + eventId.ToString(CultureInfo.InvariantCulture) + "." +
                playerId.ToString(CultureInfo.InvariantCulture);
        }
    }

    [HarmonyPatch(typeof(BloodMoonNetwork), nameof(BloodMoonNetwork.SendEnemyDeath))]
    internal static class BloodMoonServerOwnedDeathRetentionPatch
    {
        [HarmonyPriority(Priority.First + 200)]
        private static bool Prefix(long eventId, ZDOID enemyId, long creditedPlayerId, float points)
        {
            return !BloodMoonServerOwnedDeathRetention.InterceptServerSend(eventId, enemyId, creditedPlayerId, points);
        }
    }

    [HarmonyPatch(typeof(BloodMoonEnemyDeathReports), nameof(BloodMoonEnemyDeathReports.TryValidate))]
    internal static class BloodMoonEarlyResolvingDeathReplayPatch
    {
        [HarmonyPriority(Priority.First + 200)]
        private static bool Prefix(long sender, long eventId, long creditedPlayerId, ZDOID enemyId, ref float serverPoints, ref bool __result)
        {
            BloodMoonController controller = BloodMoonController.Instance;
            BloodMoonEventState state = controller?.State;
            if (state == null || state.EventId != eventId || state.IsCombatLive || !BloodMoonRound5Runtime.IsPreOutcomeDrainOpen(state) ||
                !state.Participants.TryGetValue(creditedPlayerId, out BloodMoonParticipantState participant) || !participant.IsCombatActive ||
                !BloodMoonRound5Runtime.ValidateSenderPlayer(controller, sender, creditedPlayerId) ||
                !BloodMoonEnemyDeathDurability.TryGetCurrentProfileReplay(eventId, creditedPlayerId, enemyId, out float replayPoints))
                return true;

            serverPoints = replayPoints;
            __result = replayPoints > 0f && !float.IsNaN(replayPoints) && !float.IsInfinity(replayPoints);
            return false;
        }
    }

    [HarmonyPatch(typeof(BloodMoonController), "ExitParticipant")]
    internal static class BloodMoonWithdrawalRetentionPatch
    {
        [HarmonyPriority(Priority.First + 300)]
        private static bool Prefix(BloodMoonController __instance, BloodMoonParticipantState participant, BloodMoonParticipantExitReason reason)
        {
            return BloodMoonTerminalReliability.BeginWithdrawal(__instance, participant, reason);
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(BloodMoonController __instance, BloodMoonParticipantState participant, BloodMoonParticipantExitReason reason)
        {
            BloodMoonTerminalReliability.AfterExit(__instance, participant, reason);
        }
    }

    [HarmonyPatch(typeof(BloodMoonController), nameof(BloodMoonController.OnDefeatedReport))]
    internal static class BloodMoonEarlyResolvingDefeatPatch
    {
        [HarmonyPriority(Priority.First + 300)]
        private static bool Prefix(BloodMoonController __instance, long sender, long eventId, long playerId)
        {
            return BloodMoonTerminalReliability.HandleDefeatedPrefix(__instance, sender, eventId, playerId);
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(BloodMoonController __instance, long sender, long eventId, long playerId)
        {
            BloodMoonEventState state = __instance?.State;
            if (state != null && state.EventId == eventId && state.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant) &&
                participant.IsTerminal && participant.ExitReason == BloodMoonParticipantExitReason.Defeated &&
                BloodMoonRound5Runtime.ValidateSenderPlayer(__instance, sender, playerId) && BloodMoonPersistence.Save(state))
            {
                // The pending marker will be cleared by the custom terminal ACK path on the next retry.
                // Do not mutate the legacy Defeated marker here; it remains the local terminal-state source.
            }
        }
    }

    [HarmonyPatch(typeof(BloodMoonNetwork), nameof(BloodMoonNetwork.SendDefeated))]
    internal static class BloodMoonDefeatedProfileTransactionPatch
    {
        [HarmonyPriority(Priority.First + 200)]
        private static void Prefix(long eventId, long playerId)
        {
            BloodMoonTerminalReliability.StoreDefeatedBeforeSend(eventId, playerId);
        }
    }

    [HarmonyPatch(typeof(BloodMoonRecovery), nameof(BloodMoonRecovery.ApplyWithdrawal))]
    internal static class BloodMoonWithdrawalProfileTransactionPatch
    {
        [HarmonyPriority(Priority.First + 200)]
        private static void Prefix(Player player)
        {
            BloodMoonTerminalReliability.StoreWithdrawal(player);
        }
    }

    [HarmonyPatch(typeof(BloodMoonLateJoinEnrollment), nameof(BloodMoonLateJoinEnrollment.PrepareLocal))]
    internal static class BloodMoonDuplicatePrepareTerminalGuardPatch
    {
        [HarmonyPriority(Priority.First + 300)]
        private static bool Prefix(long eventId)
        {
            return !BloodMoonTerminalReliability.ShouldIgnoreEnrollment(eventId);
        }
    }

    [HarmonyPatch(typeof(BloodMoonController), nameof(BloodMoonController.HandleClientAction))]
    internal static class BloodMoonDelayedEnrollTerminalGuardPatch
    {
        [HarmonyPriority(Priority.First + 300)]
        private static bool Prefix(long eventId, string action)
        {
            if (action != "enroll")
                return true;
            return !BloodMoonTerminalReliability.ShouldIgnoreEnrollment(eventId);
        }
    }

    [HarmonyPatch(typeof(BloodMoonController), "TickResolution")]
    internal static class BloodMoonOutcomeDrainGatePatch
    {
        [HarmonyPriority(Priority.First + 300)]
        private static bool Prefix(BloodMoonController __instance)
        {
            BloodMoonEventState state = __instance?.State;
            if (state == null || state.ResolutionStep != BloodMoonResolutionStep.AdvancingTime)
                return true;
            return !BloodMoonOutcomeDrainGate.ShouldHold(__instance);
        }
    }

    [HarmonyPatch(typeof(BloodMoonController), "RecoverServerState")]
    internal static class BloodMoonOutcomeDrainRecoveryPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(BloodMoonController __instance)
        {
            BloodMoonOutcomeDrainGate.BeginRestart(__instance?.State);
        }
    }

    [HarmonyPatch(typeof(BloodMoonSkillReportReliability), nameof(BloodMoonSkillReportReliability.TrackOutgoing))]
    internal static class BloodMoonScopedSkillTrackPatch
    {
        [HarmonyPriority(Priority.First + 300)]
        private static bool Prefix(long eventId, long playerId, long sequence, Skills.SkillType skill, float baseEquivalent, float liveBonusEquivalent)
        {
            BloodMoonSkillScopedReliability.TrackOutgoing(eventId, playerId, sequence, skill, baseEquivalent, liveBonusEquivalent);
            return false;
        }
    }

    [HarmonyPatch(typeof(BloodMoonSkillReportReliability), nameof(BloodMoonSkillReportReliability.TickLocal))]
    internal static class BloodMoonScopedSkillTickPatch
    {
        [HarmonyPriority(Priority.First + 300)]
        private static bool Prefix(float dt)
        {
            BloodMoonSkillScopedReliability.TickLocal(dt);
            return false;
        }
    }

    [HarmonyPatch(typeof(BloodMoonSkillReportReliability), nameof(BloodMoonSkillReportReliability.GetPersistedLiveBonusUsed))]
    internal static class BloodMoonScopedSkillBonusPatch
    {
        [HarmonyPriority(Priority.First + 300)]
        private static bool Prefix(long eventId, ref float __result)
        {
            __result = BloodMoonSkillScopedReliability.GetPersistedLiveBonusUsed(eventId);
            return false;
        }
    }

    [HarmonyPatch(typeof(BloodMoonSkillReportReliability), nameof(BloodMoonSkillReportReliability.SyncRuntimeFromProfile))]
    internal static class BloodMoonScopedSkillSyncPatch
    {
        [HarmonyPriority(Priority.First + 300)]
        private static bool Prefix(Player player, long eventId)
        {
            BloodMoonSkillScopedReliability.SyncRuntimeFromProfile(player, eventId);
            return false;
        }
    }

    [HarmonyPatch(typeof(BloodMoonSkillReportReliability), "OnAckRpc")]
    internal static class BloodMoonScopedSkillAckPatch
    {
        [HarmonyPriority(Priority.First + 300)]
        private static bool Prefix(long sender, ZPackage package)
        {
            BloodMoonSkillScopedReliability.HandleAck(sender, package);
            return false;
        }
    }

    [HarmonyPatch(typeof(BloodMoonController), "FixedUpdate")]
    internal static class BloodMoonRound5TickPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            BloodMoonTerminalReliability.Tick(Time.fixedDeltaTime);
            BloodMoonServerOwnedDeathRetention.Tick();
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    internal static class BloodMoonRound5CleanupPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            BloodMoonTerminalReliability.Reset();
            BloodMoonServerOwnedDeathRetention.Reset();
            BloodMoonOutcomeDrainGate.Reset();
        }
    }
}