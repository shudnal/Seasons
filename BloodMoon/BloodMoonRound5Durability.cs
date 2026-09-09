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
        private static bool savingProfile;
        private static float nextSaveWarningAt;

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
            if (controller == null || sender == 0L || playerId == 0L || ZRoutedRpc.instance == null)
                return false;
            if (sender == ZRoutedRpc.instance.GetServerPeerID())
                return Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId;
            return controller.GetPeerForPlayer(playerId) == sender;
        }

        internal static long CurrentWorldUid => ZNet.m_world != null ? ZNet.m_world.m_uid : 0L;

        internal static bool SaveProfile(Player player, string reason)
        {
            Game game = Game.instance;
            PlayerProfile profile = game != null ? game.GetPlayerProfile() : null;
            if (savingProfile || player == null || player != Player.m_localPlayer || profile == null ||
                string.IsNullOrEmpty(profile.m_filename) || profile.m_playerID != player.GetPlayerID())
                return false;

            savingProfile = true;
            try
            {
                // Game.SavePlayerProfile is void, and PlayerProfile.Save can return true after a failed
                // cloud write. Capture current data, use the normal save path, then verify the actual
                // character data through vanilla's disk/cloud loader without creating a live Player.
                profile.SavePlayerData(player);
                game.SavePlayerProfile(setLogoutPoint: false);
                byte[] expected = profile.m_playerData?.ToArray();
                if (expected == null || !ReferenceEquals(game.GetPlayerProfile(), profile))
                    return false;

                PlayerProfile persisted = new PlayerProfile(profile.m_filename, profile.m_fileSource);
                bool verified = persisted.Load() && persisted.m_playerID == player.GetPlayerID() &&
                    persisted.m_playerData != null && expected.SequenceEqual(persisted.m_playerData);
                if (!verified)
                    WarnSave(reason, "saved character data could not be read back unchanged");
                return verified;
            }
            catch (Exception ex)
            {
                WarnSave(reason, ex.Message);
                return false;
            }
            finally
            {
                savingProfile = false;
            }
        }

        private static void WarnSave(string reason, string detail)
        {
            if (Time.realtimeSinceStartup < nextSaveWarningAt)
                return;
            nextSaveWarningAt = Time.realtimeSinceStartup + 10f;
            LogWarning($"[BloodMoon.Durability] Could not confirm {reason}: {detail}. Retention remains pending.");
        }
    }

    internal static class BloodMoonTerminalReliability
    {
        private const string ProfilePrefix = "Seasons.BloodMoon.PendingTerminal.";
        private const string EvidencePrefix = "Seasons.BloodMoon.TerminalEvidence.";
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

        internal static bool BeginWithdrawal(BloodMoonController controller, BloodMoonParticipantState participant,
            ref BloodMoonParticipantExitReason reason)
        {
            if (committingTerminal || reason != BloodMoonParticipantExitReason.Withdrawn || controller?.State == null ||
                participant == null || participant.IsTerminal)
                return true;

            BloodMoonEventState state = controller.State;
            if (state.WorldUid == 0L || state.EventId < 0L || !BloodMoonRound5Runtime.IsPreOutcomeDrainOpen(state))
                return false;

            Player local = Player.m_localPlayer;
            if (local != null && local.GetPlayerID() == participant.PlayerId)
            {
                if (!StoreLocal(local, state.WorldUid, state.EventId, participant.PlayerId, reason, out BloodMoonParticipantExitReason retained))
                    return false;
                reason = retained;
                return true;
            }

            string key = MakeScope(state.WorldUid, state.EventId, participant.PlayerId);
            if (!pendingRetentions.TryGetValue(key, out PendingRetention pending))
            {
                pending = new PendingRetention
                {
                    WorldUid = state.WorldUid,
                    EventId = state.EventId,
                    PlayerId = participant.PlayerId,
                    Reason = reason
                };
                pendingRetentions[key] = pending;
            }
            SendRetention(controller, pending);
            return false;
        }

        internal static void AfterExit(BloodMoonController controller, BloodMoonParticipantState participant, BloodMoonParticipantExitReason reason)
        {
            BloodMoonEventState state = controller?.State;
            if (state == null || participant == null || !participant.IsTerminal || participant.ExitReason != reason || !IsPersonalReason(reason))
                return;
            if (BloodMoonPersistence.Save(state))
                AcknowledgeTerminal(controller, state.WorldUid, state.EventId, participant.PlayerId, reason);
        }

        internal static bool HandleDefeatedPrefix(BloodMoonController controller, long sender, long eventId, long playerId)
        {
            BloodMoonEventState state = controller?.State;
            if (state == null || state.EventId != eventId || !BloodMoonRound5Runtime.ValidateSenderPlayer(controller, sender, playerId))
                return false;
            CommitTerminal(controller, sender, state.WorldUid, eventId, playerId, BloodMoonParticipantExitReason.Defeated);
            return false;
        }

        internal static bool StoreDefeatedBeforeSend(long eventId, long playerId)
        {
            Player player = Player.m_localPlayer;
            long worldUid = BloodMoonRound5Runtime.CurrentWorldUid;
            if (player == null || player.GetPlayerID() != playerId || worldUid == 0L || eventId < 0L)
                return false;

            // Local defeat cannot be undone by an I/O failure. The immediate notification may still let
            // the server durably save it; retained-report/retention ACKs require confirmed profile storage.
            StoreLocal(player, worldUid, eventId, playerId, BloodMoonParticipantExitReason.Defeated, out BloodMoonParticipantExitReason retained);
            if (retained == BloodMoonParticipantExitReason.Defeated)
                return true;
            SendLocalReport(player, eventId, retained);
            return false;
        }

        internal static void StoreWithdrawal(Player player)
        {
            long worldUid = BloodMoonRound5Runtime.CurrentWorldUid;
            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            if (player == null || player != Player.m_localPlayer || worldUid == 0L || eventId < 0L)
                return;
            StoreLocal(player, worldUid, eventId, player.GetPlayerID(), BloodMoonParticipantExitReason.Withdrawn, out _);
        }

        internal static bool HasLocalTerminal(long eventId, long playerId)
        {
            Player player = Player.m_localPlayer;
            return player != null && player.GetPlayerID() == playerId && TryLoadEvidence(player, eventId, out _);
        }

        internal static bool ShouldIgnoreEnrollment(long eventId)
        {
            Player player = Player.m_localPlayer;
            if (player == null || eventId < 0L || !TryLoadEvidence(player, eventId, out BloodMoonParticipantExitReason reason))
                return false;
            SendLocalReport(player, eventId, reason);
            return true;
        }

        internal static void Tick(float dt)
        {
            RegisterRpc();
            TickLocal(dt);
            TickServer();
        }

        internal static bool HasPendingServer(long eventId) => pendingRetentions.Values.Any(item => item.EventId == eventId);

        internal static void Reset()
        {
            pendingRetentions.Clear();
            localRetryTimer = 0f;
            committingTerminal = false;
        }

        internal static void OnResolutionComplete()
        {
            Player player = Player.m_localPlayer;
            long worldUid = BloodMoonRound5Runtime.CurrentWorldUid;
            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            if (player == null || worldUid == 0L || eventId < 0L)
                return;
            CleanupAcknowledgedEvidence(player, worldUid, eventId, player.GetPlayerID());
        }

        private static void TickLocal(float dt)
        {
            Player player = Player.m_localPlayer;
            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            if (player == null || eventId < 0L || !TryLoadPending(player, eventId, out BloodMoonParticipantExitReason reason))
                return;

            // After the capture boundary only an identical already-persisted terminal fact may be ACKed.
            // Keep retrying that acknowledgement; a resolution-complete action is not a storage receipt.
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

                if (participant.IsTerminal && IsPersonalReason(participant.ExitReason))
                {
                    if (BloodMoonPersistence.Save(state))
                        AcknowledgeTerminal(controller, state.WorldUid, state.EventId, participant.PlayerId, participant.ExitReason);
                    continue;
                }
                if (!BloodMoonRound5Runtime.IsPreOutcomeDrainOpen(state))
                {
                    pendingRetentions.Remove(entry.Key);
                    continue;
                }
                SendRetention(controller, pending);
            }
        }

        private static void SendRetention(BloodMoonController controller, PendingRetention pending)
        {
            if (controller == null || pending == null || ZRoutedRpc.instance == null || Time.realtimeSinceStartup < pending.NextSendAt)
                return;
            long peer = controller.GetPeerForPlayer(pending.PlayerId);
            if (peer == 0L)
                return;
            pending.NextSendAt = Time.realtimeSinceStartup + RetrySeconds;
            ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcRetain,
                CreateTerminalPackage(pending.WorldUid, pending.EventId, pending.PlayerId, pending.Reason));
        }

        private static void OnRetainRpc(long sender, ZPackage package)
        {
            if (!ReadTerminalPackage(sender, package, true, out long worldUid, out long eventId, out long playerId,
                out BloodMoonParticipantExitReason requested))
                return;
            Player player = Player.m_localPlayer;
            if (player == null || player.GetPlayerID() != playerId || BloodMoonRound5Runtime.CurrentWorldUid != worldUid ||
                !StoreLocal(player, worldUid, eventId, playerId, requested, out BloodMoonParticipantExitReason retained))
                return;

            // A stale server withdrawal request may race an earlier local defeat. ACK the first retained
            // reason, never the conflicting requested reason, and do not ACK an unverified profile write.
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcRetainAck,
                CreateTerminalPackage(worldUid, eventId, playerId, retained));
        }

        private static void OnRetainAckRpc(long sender, ZPackage package)
        {
            if (!ReadTerminalPackage(sender, package, false, out long worldUid, out long eventId, out long playerId,
                out BloodMoonParticipantExitReason reason))
                return;
            BloodMoonController controller = BloodMoonController.Instance;
            if (!BloodMoonRound5Runtime.ValidateSenderPlayer(controller, sender, playerId) ||
                !pendingRetentions.ContainsKey(MakeScope(worldUid, eventId, playerId)))
                return;
            CommitTerminal(controller, sender, worldUid, eventId, playerId, reason);
        }

        private static void OnReportRpc(long sender, ZPackage package)
        {
            if (!ReadTerminalPackage(sender, package, false, out long worldUid, out long eventId, out long playerId,
                out BloodMoonParticipantExitReason reason))
                return;
            BloodMoonController controller = BloodMoonController.Instance;
            if (BloodMoonRound5Runtime.ValidateSenderPlayer(controller, sender, playerId))
                CommitTerminal(controller, sender, worldUid, eventId, playerId, reason);
        }

        private static void OnAckRpc(long sender, ZPackage package)
        {
            if (!ReadTerminalPackage(sender, package, true, out long worldUid, out long eventId, out long playerId,
                out BloodMoonParticipantExitReason reason))
                return;
            Player player = Player.m_localPlayer;
            if (player != null && player.GetPlayerID() == playerId && BloodMoonRound5Runtime.CurrentWorldUid == worldUid)
                AcknowledgeLocal(player, worldUid, eventId, playerId, reason);
        }

        private static void CommitTerminal(BloodMoonController controller, long sender, long worldUid, long eventId, long playerId,
            BloodMoonParticipantExitReason reason)
        {
            BloodMoonEventState state = controller?.State;
            if (state == null || state.WorldUid != worldUid || state.EventId != eventId || !IsPersonalReason(reason) ||
                !state.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant))
                return;

            if (participant.IsTerminal)
            {
                if (participant.ExitReason == BloodMoonParticipantExitReason.Disconnected && BloodMoonRound5Runtime.IsPreOutcomeDrainOpen(state))
                {
                    participant.ExitReason = reason;
                    state.Revision++;
                    state.UpdatedAt = seasonState.GetTotalSeconds();
                    DispatchTerminalAction(controller, state, participant, reason);
                }
                // A duplicate matching fact can be acknowledged after capture; a different fact must not
                // rewrite the immutable outcome, nor clear the client's conflicting retained evidence.
                if (participant.ExitReason == reason && BloodMoonPersistence.Save(state))
                {
                    BloodMoonNetwork.Publish(state, state.UpdatedAt);
                    AcknowledgeTerminal(controller, worldUid, eventId, playerId, reason);
                }
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

            if (!participant.IsTerminal || participant.ExitReason != reason)
                return;
            DispatchTerminalAction(controller, state, participant, reason);
            if (BloodMoonPersistence.Save(state))
                AcknowledgeTerminal(controller, worldUid, eventId, playerId, reason);
        }

        private static void DispatchTerminalAction(BloodMoonController controller, BloodMoonEventState state, BloodMoonParticipantState participant,
            BloodMoonParticipantExitReason reason)
        {
            string action = reason == BloodMoonParticipantExitReason.Defeated ? "defeated" : "withdrawn";
            Player local = Player.m_localPlayer;
            if (local != null && local.GetPlayerID() == participant.PlayerId)
                BloodMoonController.HandleClientAction(state.EventId, action, string.Empty);
            else
            {
                long peer = controller.GetPeerForPlayer(participant.PlayerId);
                if (peer != 0L)
                    BloodMoonNetwork.SendClientAction(peer, state.EventId, action);
            }
        }

        private static void AcknowledgeTerminal(BloodMoonController controller, long worldUid, long eventId, long playerId,
            BloodMoonParticipantExitReason reason)
        {
            pendingRetentions.Remove(MakeScope(worldUid, eventId, playerId));
            Player local = Player.m_localPlayer;
            if (local != null && local.GetPlayerID() == playerId)
            {
                AcknowledgeLocal(local, worldUid, eventId, playerId, reason);
                return;
            }
            long peer = controller?.GetPeerForPlayer(playerId) ?? 0L;
            if (peer != 0L && ZRoutedRpc.instance != null)
                ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcAck, CreateTerminalPackage(worldUid, eventId, playerId, reason));
        }

        private static void SendLocalReport(Player player, long eventId, BloodMoonParticipantExitReason reason)
        {
            long worldUid = BloodMoonRound5Runtime.CurrentWorldUid;
            if (player == null || ZRoutedRpc.instance == null || worldUid == 0L || !IsPersonalReason(reason) ||
                !BloodMoonRound5Runtime.SaveProfile(player, "retained terminal report"))
                return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcReport,
                CreateTerminalPackage(worldUid, eventId, player.GetPlayerID(), reason));
        }

        private static bool StoreLocal(Player player, long worldUid, long eventId, long playerId, BloodMoonParticipantExitReason requested,
            out BloodMoonParticipantExitReason retained)
        {
            retained = requested;
            if (player == null || worldUid == 0L || worldUid != BloodMoonRound5Runtime.CurrentWorldUid || eventId < 0L ||
                playerId == 0L || player.GetPlayerID() != playerId || !IsPersonalReason(requested))
                return false;

            if (TryLoadEvidence(player, eventId, out BloodMoonParticipantExitReason existing))
                retained = existing;
            string scope = MakeScope(worldUid, eventId, playerId);
            string value = ((int)retained).ToString(CultureInfo.InvariantCulture);
            player.m_customData[EvidencePrefix + scope] = value;
            player.m_customData[ProfilePrefix + scope] = value;
            // Retry persistence even when the same in-memory value was already present after a failure.
            return BloodMoonRound5Runtime.SaveProfile(player, $"pending {retained} terminal marker");
        }

        private static bool TryLoadPending(Player player, long eventId, out BloodMoonParticipantExitReason reason)
        {
            reason = BloodMoonParticipantExitReason.None;
            long worldUid = BloodMoonRound5Runtime.CurrentWorldUid;
            return player != null && worldUid != 0L && eventId >= 0L &&
                TryReadReason(player, ProfilePrefix + MakeScope(worldUid, eventId, player.GetPlayerID()), out reason);
        }

        private static bool TryLoadEvidence(Player player, long eventId, out BloodMoonParticipantExitReason reason)
        {
            reason = BloodMoonParticipantExitReason.None;
            long worldUid = BloodMoonRound5Runtime.CurrentWorldUid;
            if (player == null || worldUid == 0L || eventId < 0L)
                return false;
            string scope = MakeScope(worldUid, eventId, player.GetPlayerID());
            if (TryReadReason(player, EvidencePrefix + scope, out reason) || TryReadReason(player, ProfilePrefix + scope, out reason))
                return true;
            if (!HasLegacyDefeated(player, worldUid, eventId))
                return false;
            reason = BloodMoonParticipantExitReason.Defeated;
            return true;
        }

        private static bool TryReadReason(Player player, string key, out BloodMoonParticipantExitReason reason)
        {
            reason = BloodMoonParticipantExitReason.None;
            if (!player.m_customData.TryGetValue(key, out string value) ||
                !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                return false;
            reason = (BloodMoonParticipantExitReason)parsed;
            return IsPersonalReason(reason);
        }

        private static void AcknowledgeLocal(Player player, long worldUid, long eventId, long playerId, BloodMoonParticipantExitReason reason)
        {
            string scope = MakeScope(worldUid, eventId, playerId);
            string pendingKey = ProfilePrefix + scope;
            if (!TryReadReason(player, pendingKey, out BloodMoonParticipantExitReason pending) || pending != reason)
                return;
            if (TryReadReason(player, EvidencePrefix + scope, out BloodMoonParticipantExitReason first) && first != reason)
                return;
            player.m_customData[EvidencePrefix + scope] = ((int)reason).ToString(CultureInfo.InvariantCulture);
            player.m_customData.Remove(pendingKey);
            BloodMoonRound5Runtime.SaveProfile(player, "acknowledged terminal marker");

            BloodMoonGlobalSnapshot snapshot = BloodMoonNetwork.ClientGlobal;
            if (snapshot.EventId == eventId && (snapshot.Phase == BloodMoonEventPhase.Resolved ||
                snapshot.Phase == BloodMoonEventPhase.Resolving && (int)snapshot.ResolutionStep >= (int)BloodMoonResolutionStep.ReleasingClients))
                CleanupAcknowledgedEvidence(player, worldUid, eventId, playerId);
        }

        private static void CleanupAcknowledgedEvidence(Player player, long worldUid, long eventId, long playerId)
        {
            string scope = MakeScope(worldUid, eventId, playerId);
            if (player.m_customData.ContainsKey(ProfilePrefix + scope))
                return;
            bool changed = player.m_customData.Remove(EvidencePrefix + scope);
            if (HasLegacyDefeated(player, worldUid, eventId))
                changed |= player.m_customData.Remove(LegacyDefeatedKey);
            if (changed)
                BloodMoonRound5Runtime.SaveProfile(player, "completed acknowledged terminal evidence");
        }

        private static bool HasLegacyDefeated(Player player, long worldUid, long eventId)
        {
            if (player == null || !player.m_customData.TryGetValue(LegacyDefeatedKey, out string json) || string.IsNullOrWhiteSpace(json))
                return false;
            try
            {
                JObject source = JObject.Parse(json);
                return source.Value<long?>("WorldUid") == worldUid && source.Value<long?>("EventId") == eventId;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPersonalReason(BloodMoonParticipantExitReason reason)
        {
            return reason == BloodMoonParticipantExitReason.Defeated || reason == BloodMoonParticipantExitReason.Withdrawn;
        }

        private static bool ReadTerminalPackage(long sender, ZPackage package, bool requireServerSender, out long worldUid, out long eventId,
            out long playerId, out BloodMoonParticipantExitReason reason)
        {
            worldUid = 0L;
            eventId = -1L;
            playerId = 0L;
            reason = BloodMoonParticipantExitReason.None;
            if (package == null || ZRoutedRpc.instance == null || package.ReadInt() != BloodMoonNetwork.ProtocolVersion ||
                requireServerSender && sender != ZRoutedRpc.instance.GetServerPeerID())
                return false;
            worldUid = package.ReadLong();
            eventId = package.ReadLong();
            playerId = package.ReadLong();
            reason = (BloodMoonParticipantExitReason)package.ReadInt();
            return worldUid != 0L && eventId >= 0L && playerId != 0L && IsPersonalReason(reason);
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

        private static string MakeScope(long worldUid, long eventId, long playerId)
        {
            return worldUid.ToString(CultureInfo.InvariantCulture) + "." + eventId.ToString(CultureInfo.InvariantCulture) + "." +
                playerId.ToString(CultureInfo.InvariantCulture);
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
            internal long WorldUid;
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

            float replayPoints = BloodMoonCombat.GetPointsForEnemy(enemyId, 0f);
            if (replayPoints <= 0f || float.IsNaN(replayPoints) || float.IsInfinity(replayPoints))
                return false;
            if (!pending.TryGetValue(enemyId, out PendingRetention retention))
            {
                retention = new PendingRetention
                {
                    WorldUid = state.WorldUid,
                    EventId = eventId,
                    PlayerId = playerId,
                    EnemyId = enemyId,
                    Points = replayPoints
                };
                pending[enemyId] = retention;
            }
            Send(controller, retention);
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
                if (state == null || state.WorldUid != item.WorldUid || state.EventId != item.EventId ||
                    !state.Participants.TryGetValue(item.PlayerId, out BloodMoonParticipantState participant) ||
                    state.ReportedEnemyDeaths.Contains(item.EnemyId.ToString()))
                {
                    pending.Remove(entry.Key);
                    continue;
                }
                if (!BloodMoonRound5Runtime.IsPreOutcomeDrainOpen(state))
                    continue;
                // The kill was observed before disconnect. Keep its retention transaction while the
                // same player reconnects; terminal routing alone does not invalidate that past fact.
                if (participant.IsCombatActive || participant.ExitReason == BloodMoonParticipantExitReason.Disconnected)
                    Send(controller, item);
            }
        }

        internal static bool HasPending(long eventId) => pending.Values.Any(item => item.EventId == eventId);

        internal static void Reset() => pending.Clear();

        private static void Send(BloodMoonController controller, PendingRetention item)
        {
            if (controller == null || item == null || ZRoutedRpc.instance == null || Time.realtimeSinceStartup < item.NextSendAt)
                return;
            long peer = controller.GetPeerForPlayer(item.PlayerId);
            if (peer == 0L)
                return;
            item.NextSendAt = Time.realtimeSinceStartup + RetrySeconds;
            ZPackage package = new ZPackage();
            package.Write(BloodMoonNetwork.ProtocolVersion);
            package.Write(item.WorldUid);
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
            string value = playerId.ToString(CultureInfo.InvariantCulture) + "|" + points.ToString("R", CultureInfo.InvariantCulture);
            if (player.m_customData.TryGetValue(key, out string existing) && existing != value)
                return;
            player.m_customData[key] = value;
            if (!BloodMoonRound5Runtime.SaveProfile(player, "server-owned enemy death replay"))
                return;

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
                item.WorldUid != worldUid || item.PlayerId != playerId || Mathf.Abs(item.Points - points) > 0.001f ||
                !BloodMoonRound5Runtime.ValidateSenderPlayer(controller, sender, playerId) || !BloodMoonRound5Runtime.IsPreOutcomeDrainOpen(state))
                return;

            controller.OnEnemyDeathReport(sender, eventId, playerId, enemyId, points);
            // Accepted but not yet durably saved reports remain in the existing death-durability retry
            // transaction, with the independently retained client record available for server restart.
            if (state.ReportedEnemyDeaths.Contains(enemyId.ToString()))
                pending.Remove(enemyId);
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
            Reset();
            if (state == null || state.EventId < 0L ||
                state.Phase != BloodMoonEventPhase.Marked && !state.IsCombatLive &&
                !(state.Phase == BloodMoonEventPhase.Resolving && (int)state.ResolutionStep < (int)BloodMoonResolutionStep.PublishingOutcomes))
                return;

            restartEventId = state.EventId;
            restartUntil = Time.realtimeSinceStartup + RestartReconnectSeconds;
            foreach (BloodMoonParticipantState participant in state.Participants.Values)
            {
                if (participant.Phase == BloodMoonParticipantPhase.Marked || participant.IsCombatActive ||
                    participant.ExitReason == BloodMoonParticipantExitReason.Disconnected)
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
            return BloodMoonTerminalReliability.HasPendingServer(state.EventId) || BloodMoonServerOwnedDeathRetention.HasPending(state.EventId);
        }

        internal static void Reset()
        {
            restartAwaiting.Clear();
            restartEventId = -1L;
            restartUntil = 0f;
            settleUntil = 0f;
            holdEventId = -1L;
            minimumHoldUntil = 0f;
            BloodMoonRound5DrainTimeout.Reset();
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

    [HarmonyPatch(typeof(BloodMoonController), "ExitParticipant")]
    internal static class BloodMoonWithdrawalRetentionPatch
    {
        [HarmonyPriority(Priority.First + 300)]
        private static bool Prefix(BloodMoonController __instance, BloodMoonParticipantState participant, ref BloodMoonParticipantExitReason reason)
        {
            return BloodMoonTerminalReliability.BeginWithdrawal(__instance, participant, ref reason);
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
    }

    [HarmonyPatch(typeof(BloodMoonNetwork), nameof(BloodMoonNetwork.SendDefeated))]
    internal static class BloodMoonDefeatedProfileTransactionPatch
    {
        [HarmonyPriority(Priority.First + 200)]
        private static bool Prefix(long eventId, long playerId)
        {
            return BloodMoonTerminalReliability.StoreDefeatedBeforeSend(eventId, playerId);
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
            return action != "enroll" || !BloodMoonTerminalReliability.ShouldIgnoreEnrollment(eventId);
        }
    }

    [HarmonyPatch(typeof(BloodMoonController), "TickResolution")]
    internal static class BloodMoonOutcomeDrainGatePatch
    {
        [HarmonyPriority(Priority.First + 300)]
        private static bool Prefix(BloodMoonController __instance)
        {
            BloodMoonEventState state = __instance?.State;
            return state == null || state.ResolutionStep != BloodMoonResolutionStep.AdvancingTime || !BloodMoonOutcomeDrainGate.ShouldHold(__instance);
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
