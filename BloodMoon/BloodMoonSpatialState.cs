using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonSpatialState
    {
        private const string RpcName = "Seasons.BloodMoon.SpatialState";
        private const float HeartbeatSeconds = 2f;
        private const double ServerRecordLifetimeSeconds = 5d;

        private sealed class ServerRecord
        {
            internal long EventId;
            internal bool Suspended;
            internal double LastSeen;
        }

        private static readonly Dictionary<long, ServerRecord> serverRecords = new Dictionary<long, ServerRecord>();
        private static ZRoutedRpc registeredRpc;
        private static long localEventId = -1L;
        private static bool localLastSuspended;
        private static bool localSent;
        private static float localHeartbeat;

        internal static void RegisterRpc()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(registeredRpc, rpc))
                return;
            registeredRpc = rpc;
            serverRecords.Clear();
            rpc.Register<ZPackage>(RpcName, OnRpc);
        }

        internal static void TickLocal(Player player, float dt)
        {
            RegisterRpc();
            if (player == null || player != Player.m_localPlayer || ZRoutedRpc.instance == null)
                return;

            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            bool combatLive = BloodMoonInteractionRules.IsEventCombatLive;
            if (!combatLive || eventId < 0L)
            {
                localEventId = eventId;
                localLastSuspended = false;
                localSent = false;
                localHeartbeat = 0f;
                return;
            }

            bool suspended = player.IsTeleporting();
            if (localEventId != eventId)
            {
                localEventId = eventId;
                localSent = false;
                localHeartbeat = 0f;
            }

            localHeartbeat -= Mathf.Max(0f, dt);
            bool stateChanged = !localSent || localLastSuspended != suspended;
            if (!stateChanged && localHeartbeat > 0f)
                return;

            ZPackage pkg = new ZPackage();
            pkg.Write(BloodMoonNetwork.ProtocolVersion);
            pkg.Write(eventId);
            pkg.Write(player.GetPlayerID());
            pkg.Write(suspended);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcName, pkg);
            localSent = true;
            localLastSuspended = suspended;
            localHeartbeat = HeartbeatSeconds;
        }

        internal static bool IsAnchorSuspended(long eventId, long playerId)
        {
            if (eventId < 0L || playerId == 0L)
                return false;

            if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId &&
                BloodMoonNetwork.ClientGlobal.EventId == eventId && Player.m_localPlayer.IsTeleporting())
                return true;

            if (!serverRecords.TryGetValue(playerId, out ServerRecord record) || record.EventId != eventId || !record.Suspended)
                return false;

            double now = SeasonState.IsActive ? seasonState.GetTotalSeconds() : record.LastSeen;
            if (now - record.LastSeen <= ServerRecordLifetimeSeconds)
                return true;

            serverRecords.Remove(playerId);
            return false;
        }

        internal static void Reset()
        {
            serverRecords.Clear();
            localEventId = -1L;
            localLastSuspended = false;
            localSent = false;
            localHeartbeat = 0f;
        }

        private static void OnRpc(long sender, ZPackage pkg)
        {
            if (pkg == null || ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null ||
                pkg.ReadInt() != BloodMoonNetwork.ProtocolVersion)
                return;

            long eventId = pkg.ReadLong();
            long playerId = pkg.ReadLong();
            bool suspended = pkg.ReadBool();
            BloodMoonEventState state = BloodMoonController.Instance?.State;
            if (state == null || !state.IsCombatLive || state.EventId != eventId || playerId == 0L || !ValidateSender(sender, playerId))
                return;

            serverRecords[playerId] = new ServerRecord
            {
                EventId = eventId,
                Suspended = suspended,
                LastSeen = SeasonState.IsActive ? seasonState.GetTotalSeconds() : 0d
            };
        }

        private static bool ValidateSender(long sender, long playerId)
        {
            if (ZRoutedRpc.instance == null || ZNet.instance == null || ZDOMan.instance == null)
                return false;

            if (sender == ZRoutedRpc.instance.GetServerPeerID())
                return Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId;

            ZNetPeer peer = ZNet.instance.GetPeer(sender);
            if (peer == null || peer.m_characterID.IsNone())
                return false;
            ZDO zdo = ZDOMan.instance.GetZDO(peer.m_characterID);
            return zdo != null && zdo.GetLong(ZDOVars.s_playerID, 0L) == playerId;
        }
    }

    [HarmonyPatch(typeof(BloodMoonNetwork), nameof(BloodMoonNetwork.RegisterRpcs))]
    internal static class BloodMoonSpatialRpcRegistrationPatch
    {
        private static void Postfix()
        {
            BloodMoonSpatialState.RegisterRpc();
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.CustomFixedUpdate))]
    internal static class BloodMoonSpatialLocalStatePatch
    {
        private static void Postfix(Player __instance, float dt)
        {
            BloodMoonSpatialState.TickLocal(__instance, dt);
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    internal static class BloodMoonSpatialWorldCleanupPatch
    {
        private static void Prefix()
        {
            BloodMoonSpatialState.Reset();
        }
    }
}
