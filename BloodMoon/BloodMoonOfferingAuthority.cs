using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonOfferingAuthority
    {
        private const string RpcRequest = "Seasons.BloodMoon.OfferingRequest";
        private const string RpcRelay = "Seasons.BloodMoon.OfferingRelay";
        private const string RpcRelayResult = "Seasons.BloodMoon.OfferingRelayResult";
        private const float RelayRetrySeconds = 1f;
        private const float RequestLifetimeSeconds = 15f;

        private sealed class PendingOffering
        {
            internal long RequestId;
            internal long WorldUid;
            internal ZDOID BowlId;
            internal long Requester;
            internal Vector3 Point;
            internal bool RemoveItemsFromInventory;
            internal float ExpiresAtRealtime;
            internal float NextRelayAtRealtime;
            internal long LastRelayedOwner;
        }

        [System.ThreadStatic]
        private static int authorizedCompletionDepth;

        private static readonly Dictionary<ZDOID, PendingOffering> pendingOfferings = new Dictionary<ZDOID, PendingOffering>();
        private static ZRoutedRpc registeredRpc;
        private static long nextRequestId;

        internal static bool IsAuthorizedCompletion => authorizedCompletionDepth > 0;

        internal static void RegisterRpc()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(registeredRpc, rpc))
                return;

            registeredRpc = rpc;
            pendingOfferings.Clear();
            rpc.Register<ZPackage>(RpcRequest, OnRequestRpc);
            rpc.Register<ZPackage>(RpcRelay, OnRelayRpc);
            rpc.Register<ZPackage>(RpcRelayResult, OnRelayResultRpc);
        }

        internal static bool ShouldRelay(OfferingBowl bowl)
        {
            // Every normal boss-producing bowl initiation goes through the server. A late joiner can
            // temporarily have a stale Dormant/Forewarning snapshot, so the client cannot safely decide
            // whether the authoritative 18:00 cutoff has already passed.
            return bowl != null && bowl.m_bossPrefab != null && bowl.m_nview != null && bowl.m_nview.IsValid();
        }

        internal static bool TryRequest(OfferingBowl bowl, Vector3 point, bool removeItemsFromInventory)
        {
            if (!ShouldRelay(bowl) || ZRoutedRpc.instance == null)
                return false;

            ZDO zdo = bowl.m_nview.GetZDO();
            if (zdo == null)
                return false;

            RegisterRpc();
            ZPackage package = new ZPackage();
            package.Write(BloodMoonNetwork.ProtocolVersion);
            package.Write(zdo.m_uid);
            package.Write(point);
            package.Write(removeItemsFromInventory);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcRequest, package);
            return true;
        }

        private static void OnRequestRpc(long sender, ZPackage package)
        {
            if (package == null || ZNet.instance == null || !ZNet.instance.IsServer() || ZRoutedRpc.instance == null || ZDOMan.instance == null ||
                ZNetScene.instance == null || package.ReadInt() != BloodMoonNetwork.ProtocolVersion)
                return;

            ZDOID bowlId = package.ReadZDOID();
            Vector3 point = package.ReadVector3();
            bool removeItemsFromInventory = package.ReadBool();

            BloodMoonEventState state = BloodMoonController.Instance?.State;
            if (state == null || bowlId.IsNone() || !ValidateRequester(sender) || IsOfferingBlocked(state))
                return;

            ZDO bowlZdo = ZDOMan.instance.GetZDO(bowlId);
            if (bowlZdo == null || !IsBossOfferingPrefab(bowlZdo))
                return;

            long worldUid = ZNet.m_world != null ? ZNet.m_world.m_uid : 0L;
            if (pendingOfferings.TryGetValue(bowlId, out PendingOffering existingPending) && existingPending.WorldUid != worldUid)
                pendingOfferings.Remove(bowlId);
            if (pendingOfferings.ContainsKey(bowlId))
                return;

            PendingOffering pending = new PendingOffering
            {
                RequestId = ++nextRequestId,
                WorldUid = worldUid,
                BowlId = bowlId,
                Requester = sender,
                Point = point,
                RemoveItemsFromInventory = removeItemsFromInventory,
                ExpiresAtRealtime = Time.realtimeSinceStartup + RequestLifetimeSeconds,
                NextRelayAtRealtime = 0f,
                LastRelayedOwner = 0L
            };
            pendingOfferings[bowlId] = pending;

            TryRelay(pending);
            BloodMoonController.Instance.StartCoroutine(RelayUntilSettled(pending.BowlId, pending.RequestId));
        }

        private static IEnumerator RelayUntilSettled(ZDOID bowlId, long requestId)
        {
            WaitForSecondsRealtime retryDelay = new WaitForSecondsRealtime(RelayRetrySeconds);
            while (pendingOfferings.TryGetValue(bowlId, out PendingOffering pending) && pending.RequestId == requestId)
            {
                if (!IsCurrentWorld(pending) || Time.realtimeSinceStartup >= pending.ExpiresAtRealtime)
                {
                    pendingOfferings.Remove(bowlId);
                    yield break;
                }

                if (Time.realtimeSinceStartup >= pending.NextRelayAtRealtime)
                    TryRelay(pending);

                yield return retryDelay;
            }
        }

        private static void TryRelay(PendingOffering pending)
        {
            if (pending == null || ZRoutedRpc.instance == null || ZDOMan.instance == null || ZNetScene.instance == null)
                return;

            ZDO bowlZdo = ZDOMan.instance.GetZDO(pending.BowlId);
            if (bowlZdo == null || !IsBossOfferingPrefab(bowlZdo))
            {
                pendingOfferings.Remove(pending.BowlId);
                return;
            }

            long owner = bowlZdo.GetOwner();
            pending.NextRelayAtRealtime = Time.realtimeSinceStartup + RelayRetrySeconds;
            if (owner == 0L)
                return;

            // Do not hammer the same live owner while its ACK is in flight. Ownership migration changes
            // this value and permits the accepted pre-cutoff request to be handed to the new owner.
            if (pending.LastRelayedOwner == owner && IsPeerAvailable(owner))
                return;

            pending.LastRelayedOwner = owner;
            ZPackage relay = new ZPackage();
            relay.Write(BloodMoonNetwork.ProtocolVersion);
            relay.Write(pending.RequestId);
            relay.Write(pending.BowlId);
            relay.Write(pending.Requester);
            relay.Write(pending.Point);
            relay.Write(pending.RemoveItemsFromInventory);
            ZRoutedRpc.instance.InvokeRoutedRPC(owner, RpcRelay, relay);
        }

        private static void OnRelayRpc(long sender, ZPackage package)
        {
            if (package == null || ZRoutedRpc.instance == null || ZDOMan.instance == null || ZNetScene.instance == null ||
                sender != ZRoutedRpc.instance.GetServerPeerID() || package.ReadInt() != BloodMoonNetwork.ProtocolVersion)
                return;

            long requestId = package.ReadLong();
            ZDOID bowlId = package.ReadZDOID();
            long requester = package.ReadLong();
            Vector3 point = package.ReadVector3();
            bool removeItemsFromInventory = package.ReadBool();

            if (requestId <= 0L || bowlId.IsNone() || requester == 0L)
                return;

            GameObject instance = ZNetScene.instance.FindInstance(bowlId);
            OfferingBowl bowl = instance != null ? instance.GetComponentInChildren<OfferingBowl>(true) : null;
            if (bowl == null || bowl.m_bossPrefab == null || bowl.m_nview == null || !bowl.m_nview.IsValid() || !bowl.m_nview.IsOwner())
            {
                SendRelayResult(requestId, bowlId, completed: false);
                return;
            }

            bool completed = false;
            authorizedCompletionDepth++;
            try
            {
                bowl.RPC_SpawnBoss(requester, point, removeItemsFromInventory);
                completed = true;
            }
            finally
            {
                authorizedCompletionDepth--;
                SendRelayResult(requestId, bowlId, completed);
            }
        }

        private static void SendRelayResult(long requestId, ZDOID bowlId, bool completed)
        {
            if (ZRoutedRpc.instance == null)
                return;
            ZPackage package = new ZPackage();
            package.Write(BloodMoonNetwork.ProtocolVersion);
            package.Write(requestId);
            package.Write(bowlId);
            package.Write(completed);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcRelayResult, package);
        }

        private static void OnRelayResultRpc(long sender, ZPackage package)
        {
            if (package == null || ZNet.instance == null || !ZNet.instance.IsServer() || package.ReadInt() != BloodMoonNetwork.ProtocolVersion)
                return;

            long requestId = package.ReadLong();
            ZDOID bowlId = package.ReadZDOID();
            bool completed = package.ReadBool();
            if (!pendingOfferings.TryGetValue(bowlId, out PendingOffering pending) || pending.RequestId != requestId)
                return;

            if (completed)
            {
                pendingOfferings.Remove(bowlId);
                return;
            }

            // A stale negative ACK from an owner we have already moved past must not wake another relay.
            if (sender != pending.LastRelayedOwner)
                return;

            // A relay that reached an owner after ownership already migrated is explicitly retryable.
            // The next pass resolves the owner from the authoritative ZDO again without re-checking the
            // Blood Moon phase; the request was accepted before the cutoff and keeps that authorization.
            pending.LastRelayedOwner = 0L;
            pending.NextRelayAtRealtime = 0f;
        }

        private static bool IsOfferingBlocked(BloodMoonEventState state)
        {
            if (state == null)
                return true;

            // The server state machine ticks discretely. Use the frozen schedule as well so an offering
            // received just after 18:00 cannot slip through before the next Forewarning -> Marked tick.
            if (state.Schedule != null && state.Schedule.IsValid && SeasonState.IsActive)
            {
                double now = seasonState.GetTotalSeconds();
                if (now >= state.Schedule.MarkedAt && now < state.Schedule.MorningAt)
                    return true;
            }

            return state.Phase == BloodMoonEventPhase.Marked || state.Phase == BloodMoonEventPhase.Active ||
                state.Phase == BloodMoonEventPhase.AutoCompleting || state.Phase == BloodMoonEventPhase.Resolving;
        }

        private static bool IsCurrentWorld(PendingOffering pending)
        {
            return pending != null && ZNet.m_world != null && pending.WorldUid == ZNet.m_world.m_uid;
        }

        private static bool IsPeerAvailable(long peerId)
        {
            if (ZRoutedRpc.instance == null || ZNet.instance == null || peerId == 0L)
                return false;
            if (peerId == ZRoutedRpc.instance.GetServerPeerID())
                return true;
            return ZNet.instance.GetPeer(peerId) != null;
        }

        private static bool ValidateRequester(long sender)
        {
            if (ZRoutedRpc.instance == null || ZNet.instance == null)
                return false;
            if (sender == ZRoutedRpc.instance.GetServerPeerID())
                return Player.m_localPlayer != null;
            return ZNet.instance.GetPeer(sender) != null;
        }

        private static bool IsBossOfferingPrefab(ZDO bowlZdo)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(bowlZdo.GetPrefab());
            OfferingBowl bowl = prefab != null ? prefab.GetComponentInChildren<OfferingBowl>(true) : null;
            return bowl != null && bowl.m_bossPrefab != null;
        }
    }

    [HarmonyPatch(typeof(BloodMoonNetwork), nameof(BloodMoonNetwork.RegisterRpcs))]
    internal static class BloodMoonOfferingAuthorityRegistrationPatch
    {
        private static void Postfix()
        {
            BloodMoonOfferingAuthority.RegisterRpc();
        }
    }
}
