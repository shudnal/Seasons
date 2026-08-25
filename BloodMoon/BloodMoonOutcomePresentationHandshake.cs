using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonOutcomePresentationHandshake
    {
        private const string RpcName = "Seasons.BloodMoon.OutcomePresentation";
        private const float StartTimeoutSeconds = 12f;
        private const float CompletionTimeoutAfterStartSeconds = 8f;

        private sealed class ServerRecord
        {
            internal bool Started;
            internal bool Completed;
            internal float StartedAtRealtime;
        }

        private static readonly Dictionary<long, ServerRecord> serverRecords = new Dictionary<long, ServerRecord>();
        private static ZRoutedRpc registeredRpc;
        private static long eventId = -1L;
        private static float publishingStartedAtRealtime;

        internal static void RegisterRpc()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(registeredRpc, rpc))
                return;

            registeredRpc = rpc;
            rpc.Register<ZPackage>(RpcName, OnRpc);
        }

        internal static void Begin(long currentEventId)
        {
            eventId = currentEventId;
            publishingStartedAtRealtime = Time.realtimeSinceStartup;
            serverRecords.Clear();
        }

        internal static void NotifyStarted(long worldUid, long currentEventId, long playerId)
        {
            Notify(worldUid, currentEventId, playerId, completed: false);
        }

        internal static void NotifyCompleted(long worldUid, long currentEventId, long playerId)
        {
            Notify(worldUid, currentEventId, playerId, completed: true);
        }

        internal static bool CanRelease(BloodMoonEventState state, out string timeoutDetail)
        {
            timeoutDetail = string.Empty;
            if (state == null || state.ResolutionCancelledBeforeCombat || state.EventId < 0L)
                return true;

            if (eventId != state.EventId)
                Begin(state.EventId);

            BloodMoonController controller = BloodMoonController.Instance;
            if (controller == null)
                return true;

            float now = Time.realtimeSinceStartup;
            float publishingAge = Mathf.Max(0f, now - publishingStartedAtRealtime);
            List<long> timedOut = new List<long>();

            foreach (BloodMoonParticipantState participant in state.Participants.Values)
            {
                if (participant == null || participant.PlayerId == 0L || !IsConnected(controller, participant.PlayerId))
                    continue;

                if (!serverRecords.TryGetValue(participant.PlayerId, out ServerRecord record) || !record.Started)
                {
                    if (publishingAge < StartTimeoutSeconds)
                        return false;
                    timedOut.Add(participant.PlayerId);
                    continue;
                }

                if (record.Completed)
                    continue;

                if (now - record.StartedAtRealtime < CompletionTimeoutAfterStartSeconds)
                    return false;
                timedOut.Add(participant.PlayerId);
            }

            if (timedOut.Count > 0)
                timeoutDetail = string.Join(",", timedOut.OrderBy(id => id));
            return true;
        }

        internal static void ResetRuntime()
        {
            serverRecords.Clear();
            eventId = -1L;
            publishingStartedAtRealtime = 0f;
            registeredRpc = null;
        }

        private static void Notify(long worldUid, long currentEventId, long playerId, bool completed)
        {
            if (worldUid == 0L || currentEventId < 0L || playerId == 0L || ZNet.m_world == null || ZNet.m_world.m_uid != worldUid)
                return;

            RegisterRpc();
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                Player localPlayer = Player.m_localPlayer;
                if (localPlayer != null && localPlayer.GetPlayerID() == playerId)
                    Accept(currentEventId, playerId, completed);
                return;
            }

            if (ZRoutedRpc.instance == null)
                return;

            ZPackage package = new ZPackage();
            package.Write(BloodMoonNetwork.ProtocolVersion);
            package.Write(worldUid);
            package.Write(currentEventId);
            package.Write(playerId);
            package.Write(completed);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcName, package);
        }

        private static void OnRpc(long sender, ZPackage package)
        {
            if (package == null || ZNet.instance == null || !ZNet.instance.IsServer() ||
                package.ReadInt() != BloodMoonNetwork.ProtocolVersion)
                return;

            long worldUid = package.ReadLong();
            long currentEventId = package.ReadLong();
            long playerId = package.ReadLong();
            bool completed = package.ReadBool();
            if (ZNet.m_world == null || ZNet.m_world.m_uid != worldUid || !ValidateSender(sender, playerId))
                return;

            Accept(currentEventId, playerId, completed);
        }

        private static void Accept(long currentEventId, long playerId, bool completed)
        {
            BloodMoonEventState state = BloodMoonController.Instance?.State;
            if (state == null || state.EventId != currentEventId || state.Phase != BloodMoonEventPhase.Resolving ||
                (int)state.ResolutionStep < (int)BloodMoonResolutionStep.PublishingOutcomes ||
                (int)state.ResolutionStep >= (int)BloodMoonResolutionStep.Complete || !state.Participants.ContainsKey(playerId))
                return;

            if (eventId != currentEventId)
                Begin(currentEventId);

            if (!serverRecords.TryGetValue(playerId, out ServerRecord record))
            {
                record = new ServerRecord();
                serverRecords[playerId] = record;
            }

            if (!record.Started)
            {
                record.Started = true;
                record.StartedAtRealtime = Time.realtimeSinceStartup;
            }
            if (completed)
                record.Completed = true;
        }

        private static bool IsConnected(BloodMoonController controller, long playerId)
        {
            if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId)
                return true;
            long peerId = controller.GetPeerForPlayer(playerId);
            return peerId != 0L && ZNet.instance != null && ZNet.instance.GetPeer(peerId) != null;
        }

        private static bool ValidateSender(long sender, long playerId)
        {
            if (playerId == 0L || ZNet.instance == null || ZRoutedRpc.instance == null || ZDOMan.instance == null)
                return false;

            if (sender == ZRoutedRpc.instance.GetServerPeerID())
                return Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId;

            ZNetPeer peer = ZNet.instance.GetPeer(sender);
            if (peer == null || peer.m_characterID.IsNone())
                return false;
            ZDO playerZdo = ZDOMan.instance.GetZDO(peer.m_characterID);
            return playerZdo != null && playerZdo.GetLong(ZDOVars.s_playerID, 0L) == playerId;
        }
    }
}
