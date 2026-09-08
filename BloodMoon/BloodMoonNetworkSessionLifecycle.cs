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
        private const string RpcReady = "Seasons.BloodMoon.EnrollmentReady";
        internal const string PrepareAction = "prepare-enrollment";

        private static ZRoutedRpc registeredRpc;
        private static long protectedEventId = -1L;
        private static bool loggedProtection;

        internal static void RegisterRpc()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(registeredRpc, rpc))
                return;
            registeredRpc = rpc;
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
            if (controller == null || participant == null || participant.Phase != BloodMoonParticipantPhase.Marked || !participant.JoinedLate)
                return;

            Player local = Player.m_localPlayer;
            if (local != null && local.GetPlayerID() == participant.PlayerId)
            {
                PrepareLocal(controller.State?.EventId ?? -1L);
                return;
            }

            long peer = controller.GetPeerForPlayer(participant.PlayerId);
            if (peer != 0L && controller.State != null)
                BloodMoonNetwork.SendClientAction(peer, controller.State.EventId, PrepareAction);
        }

        internal static bool ProtectPendingDeath(Player player)
        {
            if (player == null || player != Player.m_localPlayer || protectedEventId < 0L)
                return false;

            if (BloodMoonNetwork.ClientGlobal.EventId != protectedEventId)
            {
                protectedEventId = -1L;
                return false;
            }

            BloodMoonParticipantState participant = BloodMoonInteractionRules.GetLocalParticipant();
            if (participant != null && (participant.IsCombatActive || participant.IsTerminal))
            {
                protectedEventId = -1L;
                return false;
            }

            BloodMoonEventPhase phase = BloodMoonNetwork.ClientGlobal.Phase;
            return phase == BloodMoonEventPhase.Active || phase == BloodMoonEventPhase.AutoCompleting;
        }

        internal static void LogProtection(Player player)
        {
            if (loggedProtection || player == null)
                return;
            loggedProtection = true;
            LogWarning($"[BloodMoon][event:{protectedEventId}][player:{player.GetPlayerID()}] Prevented vanilla death while late-join Fighting enrollment was awaiting its routing snapshot.");
        }

        internal static void Reset()
        {
            protectedEventId = -1L;
            loggedProtection = false;
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

    [HarmonyPatch(typeof(BloodMoonController), nameof(BloodMoonController.HandleClientAction))]
    internal static class BloodMoonLateJoinPrepareActionPatch
    {
        [HarmonyPriority(Priority.First + 100)]
        private static bool Prefix(long eventId, string action)
        {
            if (action != BloodMoonLateJoinEnrollment.PrepareAction)
                return true;
            BloodMoonLateJoinEnrollment.PrepareLocal(eventId);
            return false;
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

            // The server has not made this client-visible participant Fighting yet. Preserve ordinary
            // pre-enrollment health semantics as much as possible while preventing the forbidden vanilla
            // tombstone/respawn path in the narrow ACK -> routing-snapshot window.
            player.SetHealth(1f);
            BloodMoonLateJoinEnrollment.LogProtection(player);
            return false;
        }
    }
}
