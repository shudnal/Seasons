using UnityEngine;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonOfferingAuthority
    {
        private const string RpcRequest = "Seasons.BloodMoon.OfferingRequest";
        private const string RpcRelay = "Seasons.BloodMoon.OfferingRelay";

        [System.ThreadStatic]
        private static int authorizedCompletionDepth;

        private static ZRoutedRpc registeredRpc;

        internal static bool IsAuthorizedCompletion => authorizedCompletionDepth > 0;

        internal static void RegisterRpc()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(registeredRpc, rpc))
                return;

            registeredRpc = rpc;
            rpc.Register<ZPackage>(RpcRequest, OnRequestRpc);
            rpc.Register<ZPackage>(RpcRelay, OnRelayRpc);
        }

        internal static bool ShouldRelay(OfferingBowl bowl)
        {
            return bowl != null && bowl.m_bossPrefab != null && bowl.m_nview != null && bowl.m_nview.IsValid() &&
                BloodMoonNetwork.ClientGlobal.EventId >= 0L && BloodMoonNetwork.ClientGlobal.Phase == BloodMoonEventPhase.Forewarning;
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
            package.Write(BloodMoonNetwork.ClientGlobal.EventId);
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

            long eventId = package.ReadLong();
            ZDOID bowlId = package.ReadZDOID();
            Vector3 point = package.ReadVector3();
            bool removeItemsFromInventory = package.ReadBool();

            BloodMoonEventState state = BloodMoonController.Instance?.State;
            if (state == null || state.EventId != eventId || state.Phase != BloodMoonEventPhase.Forewarning || bowlId.IsNone() || !ValidateRequester(sender))
                return;

            ZDO bowlZdo = ZDOMan.instance.GetZDO(bowlId);
            if (bowlZdo == null || !IsBossOfferingPrefab(bowlZdo))
                return;

            long owner = bowlZdo.GetOwner();
            if (owner == 0L)
                return;

            ZPackage relay = new ZPackage();
            relay.Write(BloodMoonNetwork.ProtocolVersion);
            relay.Write(eventId);
            relay.Write(bowlId);
            relay.Write(sender);
            relay.Write(point);
            relay.Write(removeItemsFromInventory);
            ZRoutedRpc.instance.InvokeRoutedRPC(owner, RpcRelay, relay);
        }

        private static void OnRelayRpc(long sender, ZPackage package)
        {
            if (package == null || ZRoutedRpc.instance == null || ZDOMan.instance == null || ZNetScene.instance == null ||
                sender != ZRoutedRpc.instance.GetServerPeerID() || package.ReadInt() != BloodMoonNetwork.ProtocolVersion)
                return;

            long eventId = package.ReadLong();
            ZDOID bowlId = package.ReadZDOID();
            long requester = package.ReadLong();
            Vector3 point = package.ReadVector3();
            bool removeItemsFromInventory = package.ReadBool();

            if (eventId < 0L || eventId != BloodMoonNetwork.ClientGlobal.EventId || bowlId.IsNone() || requester == 0L)
                return;

            GameObject instance = ZNetScene.instance.FindInstance(bowlId);
            OfferingBowl bowl = instance != null ? instance.GetComponentInChildren<OfferingBowl>(true) : null;
            if (bowl == null || bowl.m_bossPrefab == null || bowl.m_nview == null || !bowl.m_nview.IsValid() || !bowl.m_nview.IsOwner())
                return;

            authorizedCompletionDepth++;
            try
            {
                bowl.RPC_SpawnBoss(requester, point, removeItemsFromInventory);
            }
            finally
            {
                authorizedCompletionDepth--;
            }
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
}
