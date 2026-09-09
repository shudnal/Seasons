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
        private const string RpcRequesterExpectation = "Seasons.BloodMoon.OfferingRequesterExpectation";
        private const string OfferingRequestMarker = "Seasons.BloodMoon.OfferingRequestId";
        private const string OfferingAuthorityMarker = "Seasons.BloodMoon.OfferingAuthorityId";
        private const string OfferingExecutedRequestMarker = "Seasons.BloodMoon.OfferingExecutedRequestId";
        private const string OfferingExecutedAuthorityMarker = "Seasons.BloodMoon.OfferingExecutedAuthorityId";
        private const string ConsumedMarkerPrefix = "Seasons.BloodMoon.OfferingConsumed.";
        private const float RelayRetrySeconds = 1f;

        private sealed class PendingOffering
        {
            internal long RequestId;
            internal long AuthoritySessionId;
            internal long WorldUid;
            internal ZDOID BowlId;
            internal long Requester;
            internal Vector3 Point;
            internal bool RemoveItemsFromInventory;
            internal float NextRelayAtRealtime;
            internal long LastRelayedOwner;
        }

        private sealed class ExpectedInventoryRemoval
        {
            internal long WorldUid;
            internal long RequestId;
            internal long AuthoritySessionId;
            internal ZDOID BowlId;
        }

        [System.ThreadStatic]
        private static int authorizedCompletionDepth;

        private static readonly Dictionary<ZDOID, PendingOffering> pendingOfferings = new Dictionary<ZDOID, PendingOffering>();
        private static readonly Dictionary<ZDOID, ExpectedInventoryRemoval> expectedInventoryRemovals = new Dictionary<ZDOID, ExpectedInventoryRemoval>();
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
            expectedInventoryRemovals.Clear();
            rpc.Register<ZPackage>(RpcRequest, OnRequestRpc);
            rpc.Register<ZPackage>(RpcRelay, OnRelayRpc);
            rpc.Register<ZPackage>(RpcRelayResult, OnRelayResultRpc);
            rpc.Register<ZPackage>(RpcRequesterExpectation, OnRequesterExpectationRpc);
        }

        internal static void ResetRuntime()
        {
            pendingOfferings.Clear();
            expectedInventoryRemovals.Clear();
            nextRequestId = 0L;
            authorizedCompletionDepth = 0;
        }

        internal static bool ShouldRelay(OfferingBowl bowl)
        {
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
                AuthoritySessionId = ZRoutedRpc.instance.GetServerPeerID(),
                WorldUid = worldUid,
                BowlId = bowlId,
                Requester = sender,
                Point = point,
                RemoveItemsFromInventory = removeItemsFromInventory,
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
                if (!IsCurrentWorld(pending))
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

            pending.NextRelayAtRealtime = Time.realtimeSinceStartup + RelayRetrySeconds;

            if (pending.RemoveItemsFromInventory && !IsPeerAvailable(pending.Requester))
            {
                LogWarning($"[BloodMoon.Offering] Cancelled request {pending.RequestId}: requester session {pending.Requester} disconnected before inventory consumption could complete.");
                pendingOfferings.Remove(pending.BowlId);
                return;
            }

            if (pending.LastRelayedOwner != 0L)
            {
                if (IsPeerAvailable(pending.LastRelayedOwner))
                    return;
                pending.LastRelayedOwner = 0L;
            }

            long owner = bowlZdo.GetOwner();
            if (owner == 0L)
                return;

            pending.LastRelayedOwner = owner;
            ZPackage relay = new ZPackage();
            relay.Write(BloodMoonNetwork.ProtocolVersion);
            relay.Write(pending.RequestId);
            relay.Write(pending.AuthoritySessionId);
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
            long authoritySessionId = package.ReadLong();
            ZDOID bowlId = package.ReadZDOID();
            long requester = package.ReadLong();
            Vector3 point = package.ReadVector3();
            bool removeItemsFromInventory = package.ReadBool();

            if (requestId <= 0L || authoritySessionId == 0L || bowlId.IsNone() || requester == 0L)
                return;

            GameObject instance = ZNetScene.instance.FindInstance(bowlId);
            OfferingBowl bowl = instance != null ? instance.GetComponentInChildren<OfferingBowl>(true) : null;
            if (bowl == null || bowl.m_bossPrefab == null || bowl.m_nview == null || !bowl.m_nview.IsValid() || !bowl.m_nview.IsOwner())
            {
                SendRelayResult(requestId, bowlId, completed: false);
                return;
            }

            if (removeItemsFromInventory && !IsPeerAvailable(requester))
            {
                SendRelayResult(requestId, bowlId, completed: false);
                return;
            }

            ZDO bowlZdo = bowl.m_nview.GetZDO();
            if (bowlZdo == null)
            {
                SendRelayResult(requestId, bowlId, completed: false);
                return;
            }

            // Execution is an at-most-once request-scoped operation. The token is written and force-sent
            // before invoking vanilla so a replacement owner can recognize a completion attempt even if
            // the old owner disappears before its routed result reaches the server. Including the server
            // session prevents request-id reuse after a transport/server restart from colliding with an
            // old bowl marker in the same world.
            if (bowlZdo.GetLong(OfferingExecutedRequestMarker, 0L) == requestId &&
                bowlZdo.GetLong(OfferingExecutedAuthorityMarker, 0L) == authoritySessionId)
            {
                SendRelayResult(requestId, bowlId, completed: true);
                return;
            }

            bowlZdo.Set(OfferingRequestMarker, requestId);
            bowlZdo.Set(OfferingAuthorityMarker, authoritySessionId);
            bowlZdo.Set(OfferingExecutedRequestMarker, requestId);
            bowlZdo.Set(OfferingExecutedAuthorityMarker, authoritySessionId);
            ZDOMan.instance.ForceSendZDO(bowlId);

            if (removeItemsFromInventory)
                SendRequesterExpectation(requester, requestId, authoritySessionId, bowlId);

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

        private static void SendRequesterExpectation(long requester, long requestId, long authoritySessionId, ZDOID bowlId)
        {
            if (ZRoutedRpc.instance == null || requester == 0L || requestId <= 0L || authoritySessionId == 0L || bowlId.IsNone())
                return;

            ZPackage package = new ZPackage();
            package.Write(BloodMoonNetwork.ProtocolVersion);
            package.Write(ZNet.m_world != null ? ZNet.m_world.m_uid : 0L);
            package.Write(requestId);
            package.Write(authoritySessionId);
            package.Write(bowlId);
            ZRoutedRpc.instance.InvokeRoutedRPC(requester, RpcRequesterExpectation, package);
        }

        private static void OnRequesterExpectationRpc(long sender, ZPackage package)
        {
            if (package == null || package.ReadInt() != BloodMoonNetwork.ProtocolVersion || ZNet.m_world == null)
                return;

            long worldUid = package.ReadLong();
            long requestId = package.ReadLong();
            long authoritySessionId = package.ReadLong();
            ZDOID bowlId = package.ReadZDOID();
            if (worldUid == 0L || worldUid != ZNet.m_world.m_uid || requestId <= 0L || authoritySessionId == 0L || bowlId.IsNone())
                return;

            _ = sender;
            expectedInventoryRemovals[bowlId] = new ExpectedInventoryRemoval
            {
                WorldUid = worldUid,
                RequestId = requestId,
                AuthoritySessionId = authoritySessionId,
                BowlId = bowlId
            };
        }

        internal static bool BeginInventoryRemoval(OfferingBowl bowl, out long requestId, out long authoritySessionId)
        {
            requestId = 0L;
            authoritySessionId = 0L;
            if (bowl == null || bowl.m_nview == null || !bowl.m_nview.IsValid() || ZNet.m_world == null)
                return true;

            ZDO bowlZdo = bowl.m_nview.GetZDO();
            if (bowlZdo == null)
                return true;

            ZDOID bowlId = bowlZdo.m_uid;
            long worldUid = ZNet.m_world.m_uid;
            if (!expectedInventoryRemovals.TryGetValue(bowlId, out ExpectedInventoryRemoval expected) || expected.WorldUid != worldUid)
            {
                long markedRequestId = bowlZdo.GetLong(OfferingRequestMarker, 0L);
                long markedAuthorityId = bowlZdo.GetLong(OfferingAuthorityMarker, 0L);
                if (markedRequestId <= 0L || markedAuthorityId == 0L)
                    return true;
                expected = new ExpectedInventoryRemoval
                {
                    WorldUid = worldUid,
                    RequestId = markedRequestId,
                    AuthoritySessionId = markedAuthorityId,
                    BowlId = bowlId
                };
                expectedInventoryRemovals[bowlId] = expected;
            }

            Player player = Player.m_localPlayer;
            if (player == null)
                return false;

            requestId = expected.RequestId;
            authoritySessionId = expected.AuthoritySessionId;
            if (player.m_customData.ContainsKey(GetConsumedMarkerKey(worldUid, bowlId, authoritySessionId, requestId)))
                return false;
            return true;
        }

        internal static void CompleteInventoryRemoval(OfferingBowl bowl, long requestId, long authoritySessionId)
        {
            Player player = Player.m_localPlayer;
            if (player == null || bowl == null || bowl.m_nview == null || !bowl.m_nview.IsValid() || ZNet.m_world == null || requestId <= 0L || authoritySessionId == 0L)
                return;

            ZDO bowlZdo = bowl.m_nview.GetZDO();
            if (bowlZdo == null)
                return;

            player.m_customData[GetConsumedMarkerKey(ZNet.m_world.m_uid, bowlZdo.m_uid, authoritySessionId, requestId)] = "1";
            Game.instance?.SavePlayerProfile(false);
        }

        private static string GetConsumedMarkerKey(long worldUid, ZDOID bowlId, long authoritySessionId, long requestId)
        {
            return $"{ConsumedMarkerPrefix}{worldUid}.{bowlId}.{authoritySessionId}.{requestId}";
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

            if (sender != pending.LastRelayedOwner)
                return;

            if (completed)
            {
                pendingOfferings.Remove(bowlId);
                return;
            }

            pending.LastRelayedOwner = 0L;
            pending.NextRelayAtRealtime = 0f;
        }

        private static bool IsOfferingBlocked(BloodMoonEventState state)
        {
            if (state == null)
                return true;

            if (state.Phase == BloodMoonEventPhase.Forewarning && state.Schedule != null && state.Schedule.IsValid && SeasonState.IsActive)
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

    [HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.RPC_RemoveBossSpawnInventoryItems))]
    internal static class BloodMoonOfferingInventoryRemovalPatch
    {
        private readonly struct RemovalState
        {
            internal readonly long RequestId;
            internal readonly long AuthoritySessionId;

            internal RemovalState(long requestId, long authoritySessionId)
            {
                RequestId = requestId;
                AuthoritySessionId = authoritySessionId;
            }
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(OfferingBowl __instance, out RemovalState __state)
        {
            bool run = BloodMoonOfferingAuthority.BeginInventoryRemoval(__instance, out long requestId, out long authoritySessionId);
            __state = new RemovalState(requestId, authoritySessionId);
            return run;
        }

        private static void Postfix(OfferingBowl __instance, RemovalState __state)
        {
            if (__state.RequestId > 0L && __state.AuthoritySessionId != 0L)
                BloodMoonOfferingAuthority.CompleteInventoryRemoval(__instance, __state.RequestId, __state.AuthoritySessionId);
        }
    }
}
