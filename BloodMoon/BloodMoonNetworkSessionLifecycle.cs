using HarmonyLib;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonNetworkSessionLifecycle
    {
        internal static void ResetClientSnapshots()
        {
            BloodMoonNetwork.ResetSession();
            BloodMoonLateJoinEnrollment.Reset();
        }
    }

    internal static class BloodMoonLateJoinEnrollment
    {
        private const string RpcPrepare = "Seasons.BloodMoon.EnrollmentPrepare";
        private const string RpcReady = "Seasons.BloodMoon.EnrollmentReady";

        private static ZRoutedRpc registeredRpc;
        private static long protectedEventId = -1L;
        private static bool loggedProtection;

        internal static void RegisterRpc()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(registeredRpc, rpc))
                return;
            registeredRpc = rpc;
            rpc.Register<ZPackage>(RpcPrepare, OnPrepareRpc);
            rpc.Register<ZPackage>(RpcReady, OnReadyRpc);
        }

        internal static void PrepareLocal(long eventId)
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.GetPlayerID() == 0L || eventId < 0L)
                return;

            protectedEventId = eventId;
            loggedProtection = false;
            BloodMoonRecovery.ResetEvent(eventId);
            BloodMoonPresentation.OnEnrolled();

            if (ZRoutedRpc.instance == null)
                return;
            ZPackage package = new ZPackage();
            package.Write(BloodMoonNetwork.ProtocolVersion);
            package.Write(eventId);
            package.Write(player.GetPlayerID());
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcReady, package);
        }

        internal static void SendPrepare(BloodMoonController controller, BloodMoonParticipantState participant)
        {
            if (controller == null || participant == null || participant.Phase != BloodMoonParticipantPhase.Marked || !participant.JoinedLate ||
                controller.State == null)
                return;

            Player local = Player.m_localPlayer;
            if (local != null && local.GetPlayerID() == participant.PlayerId)
            {
                PrepareLocal(controller.State.EventId);
                return;
            }

            long peer = controller.GetPeerForPlayer(participant.PlayerId);
            if (peer == 0L || ZNet.instance == null || !ZNet.instance.IsServer() || ZRoutedRpc.instance == null)
                return;

            // Do not route this through ClientAction: that path intentionally waits for a matching CCS
            // snapshot. The purpose of this prepare RPC is to install the narrow death guard before the
            // server is allowed to publish this late joiner as Fighting.
            ZPackage package = new ZPackage();
            package.Write(BloodMoonNetwork.ProtocolVersion);
            package.Write(controller.State.EventId);
            package.Write(participant.PlayerId);
            ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcPrepare, package);
        }

        internal static bool ProtectPendingDeath(Player player)
        {
            if (player == null || player != Player.m_localPlayer)
                return false;

            long globalEventId = BloodMoonNetwork.ClientGlobal.EventId;
            if (protectedEventId >= 0L)
            {
                // Event ids are monotonic world-day high-water marks. A genuinely newer event proves that
                // an old prepare guard is stale; a lower/stale snapshot must not cancel protection while the
                // matching event snapshot is still in flight.
                if (globalEventId > protectedEventId)
                    ClearProtection();
                else if (globalEventId == protectedEventId)
                {
                    BloodMoonParticipantState participant = BloodMoonInteractionRules.GetLocalParticipant();
                    if (participant != null && (participant.IsCombatActive || participant.IsTerminal))
                    {
                        ClearProtection();
                        return false;
                    }

                    BloodMoonEventPhase phase = BloodMoonNetwork.ClientGlobal.Phase;
                    if (phase == BloodMoonEventPhase.Resolving || phase == BloodMoonEventPhase.Resolved || phase == BloodMoonEventPhase.Skipped ||
                        phase == BloodMoonEventPhase.Dormant)
                    {
                        ClearProtection();
                        return false;
                    }
                }

                if (protectedEventId >= 0L)
                    return true;
            }

            // A late joiner can receive the already-active global snapshot before the server's targeted
            // prepare RPC or participant routing snapshot. All connected players are enrollable during
            // Active/AutoCompleting, so absence (or staged Marked state) here is transport convergence, not
            // permission to take the vanilla tombstone/skill-loss death path.
            BloodMoonEventPhase globalPhase = BloodMoonNetwork.ClientGlobal.Phase;
            if (globalEventId < 0L || globalPhase != BloodMoonEventPhase.Active && globalPhase != BloodMoonEventPhase.AutoCompleting ||
                BloodMoonRecovery.IsLocallyExited(player.GetPlayerID()))
                return false;

            BloodMoonParticipantState localParticipant = BloodMoonInteractionRules.GetLocalParticipant();
            return localParticipant == null || localParticipant.Phase == BloodMoonParticipantPhase.Marked;
        }

        internal static void LogProtection(Player player)
        {
            if (loggedProtection || player == null)
                return;
            loggedProtection = true;
            long eventId = protectedEventId >= 0L ? protectedEventId : BloodMoonNetwork.ClientGlobal.EventId;
            LogWarning($"[BloodMoon][event:{eventId}][player:{player.GetPlayerID()}] Prevented vanilla death while late-join enrollment/combat snapshots were converging.");
        }

        internal static void Reset()
        {
            ClearProtection();
        }

        private static void ClearProtection()
        {
            protectedEventId = -1L;
            loggedProtection = false;
        }

        private static void OnPrepareRpc(long sender, ZPackage package)
        {
            if (package == null || ZRoutedRpc.instance == null || sender != ZRoutedRpc.instance.GetServerPeerID() ||
                package.ReadInt() != BloodMoonNetwork.ProtocolVersion)
                return;

            long eventId = package.ReadLong();
            long playerId = package.ReadLong();
            Player player = Player.m_localPlayer;
            if (player == null || player.GetPlayerID() != playerId)
                return;
            PrepareLocal(eventId);
        }

        private static void OnReadyRpc(long sender, ZPackage package)
        {
            if (package == null || ZNet.instance == null || !ZNet.instance.IsServer() || ZRoutedRpc.instance == null ||
                package.ReadInt() != BloodMoonNetwork.ProtocolVersion)
                return;

            long eventId = package.ReadLong();
            long playerId = package.ReadLong();
            BloodMoonController controller = BloodMoonController.Instance;
            BloodMoonEventState state = controller?.State;
            if (state == null || !state.IsCombatLive || state.EventId != eventId || playerId == 0L ||
                !state.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant) ||
                participant.Phase != BloodMoonParticipantPhase.Marked || !participant.JoinedLate || !ValidateSender(controller, sender, playerId))
                return;

            double now = seasonState.GetTotalSeconds();
            int oldRevision = state.Revision;
            double oldUpdated = state.UpdatedAt;
            participant.Phase = BloodMoonParticipantPhase.Fighting;
            participant.FightingAt = now;
            state.UpdatedAt = now;
            state.Revision++;

            if (!BloodMoonPersistence.Save(state))
            {
                participant.Phase = BloodMoonParticipantPhase.Marked;
                participant.FightingAt = 0d;
                state.Revision = oldRevision;
                state.UpdatedAt = oldUpdated;
                return;
            }

            BloodMoonNetwork.Publish(state, now);
            LogInfo($"[BloodMoon][event:{eventId}][player:{playerId}] Late-join enrollment acknowledged; participant entered Fighting.");
        }

        private static bool ValidateSender(BloodMoonController controller, long sender, long playerId)
        {
            if (controller == null || ZRoutedRpc.instance == null)
                return false;
            if (sender == ZRoutedRpc.instance.GetServerPeerID())
                return Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId;
            return controller.GetPeerForPlayer(playerId) == sender;
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    internal static class BloodMoonNetworkSessionResetPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            BloodMoonNetworkSessionLifecycle.ResetClientSnapshots();
        }
    }

    [HarmonyPatch(typeof(BloodMoonNetwork), nameof(BloodMoonNetwork.RegisterRpcs))]
    internal static class BloodMoonLateJoinRpcRegistrationPatch
    {
        private static void Postfix() => BloodMoonLateJoinEnrollment.RegisterRpc();
    }

    [HarmonyPatch(typeof(BloodMoonController), "Enroll")]
    internal static class BloodMoonLateJoinEnrollPatch
    {
        [HarmonyPriority(Priority.First + 100)]
        private static bool Prefix(BloodMoonController __instance, BloodMoonController.ConnectedPlayer connected, double now, bool joinedLate)
        {
            BloodMoonEventState state = __instance?.State;
            if (!joinedLate || state == null || !state.IsCombatLive)
                return true;
            if (connected == null || connected.PlayerId == 0L || state.EnrollmentFrozen || state.Participants.ContainsKey(connected.PlayerId))
                return false;

            BloodMoonParticipantState participant = new BloodMoonParticipantState
            {
                PlayerId = connected.PlayerId,
                PlayerName = connected.Name,
                Phase = BloodMoonParticipantPhase.Marked,
                JoinedLate = true,
                MarkedAt = now,
                FightingAt = 0d
            };
            state.Participants.Add(participant.PlayerId, participant);
            int oldRevision = state.Revision;
            double oldUpdated = state.UpdatedAt;
            state.UpdatedAt = now;
            state.Revision++;

            if (!BloodMoonPersistence.Save(state))
            {
                state.Participants.Remove(participant.PlayerId);
                state.Revision = oldRevision;
                state.UpdatedAt = oldUpdated;
                return false;
            }

            BloodMoonNetwork.Publish(state, now);
            BloodMoonLateJoinEnrollment.SendPrepare(__instance, participant);
            LogInfo($"[BloodMoon][event:{state.EventId}][player:{participant.PlayerId}] Late join staged as Marked pending client combat-ready acknowledgement.");
            return false;
        }
    }

    [HarmonyPatch(typeof(BloodMoonController), "UpdateConnectedParticipants")]
    internal static class BloodMoonLateJoinPrepareRetryPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(BloodMoonController __instance)
        {
            BloodMoonEventState state = __instance?.State;
            if (state == null || !state.IsCombatLive)
                return;
            foreach (BloodMoonParticipantState participant in state.Participants.Values)
            {
                if (participant.JoinedLate && participant.Phase == BloodMoonParticipantPhase.Marked)
                    BloodMoonLateJoinEnrollment.SendPrepare(__instance, participant);
            }
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.CheckDeath))]
    internal static class BloodMoonLateJoinDeathGuardPatch
    {
        [HarmonyPriority(Priority.First + 200)]
        private static bool Prefix(Character __instance)
        {
            if (__instance is not Player player || player != Player.m_localPlayer || !player.IsOwner() || player.IsDead() || player.GetHealth() > 0f ||
                !BloodMoonLateJoinEnrollment.ProtectPendingDeath(player))
                return true;

            player.SetHealth(1f);
            BloodMoonLateJoinEnrollment.LogProtection(player);
            return false;
        }
    }
}
