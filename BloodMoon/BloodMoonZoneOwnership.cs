using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonZoneOwnership
    {
        internal sealed class ZoneClaim
        {
            internal long EventId;
            internal Vector2i Zone;
            internal long PeerId;
            internal long PlayerId;
            internal double LastSeen;
        }

        private const double ClaimLifetimeSeconds = 5d;
        private const float RelevantZoneDistance = 180f;
        private static readonly Dictionary<Vector2i, ZoneClaim> serverClaims = new Dictionary<Vector2i, ZoneClaim>();
        private static float localReportTimer;

        internal static void TickClient(float dt)
        {
            if (!BloodMoonInteractionRules.IsEventCombatLive || Player.m_localPlayer == null || ZRoutedRpc.instance == null)
            {
                localReportTimer = 0f;
                return;
            }

            localReportTimer -= dt;
            if (localReportTimer > 0f)
                return;
            localReportTimer = 2f;

            HashSet<Vector2i> reported = new HashSet<Vector2i>();
            foreach (SpawnSystem spawnSystem in SpawnSystem.m_instances)
            {
                if (spawnSystem == null || spawnSystem.m_nview == null || !spawnSystem.m_nview.IsValid() || !spawnSystem.m_nview.IsOwner())
                    continue;
                Vector2i zone = ZoneSystem.GetZone(spawnSystem.transform.position);
                if (reported.Add(zone))
                    BloodMoonNetwork.SendZoneClaim(BloodMoonNetwork.ClientGlobal.EventId, zone);
            }
        }

        internal static void AcceptClaim(long sender, long eventId, long playerId, int zoneX, int zoneY)
        {
            BloodMoonEventState state = BloodMoonController.Instance?.State;
            if (state == null || !state.IsCombatLive || state.EventId != eventId || !ValidatePeerPlayer(sender, playerId))
                return;

            Vector2i zone = new Vector2i(zoneX, zoneY);
            serverClaims[zone] = new ZoneClaim
            {
                EventId = eventId,
                Zone = zone,
                PeerId = sender,
                PlayerId = playerId,
                LastSeen = seasonState.GetTotalSeconds()
            };
        }

        internal static List<ZoneClaim> GetRelevantClaims(BloodMoonEventState state, BloodMoonGroupState group, double now)
        {
            if (state == null || group == null)
                return new List<ZoneClaim>();

            Prune(state.EventId, now);
            BloodMoonController controller = BloodMoonController.Instance;
            if (controller == null)
                return new List<ZoneClaim>();

            List<Vector3> memberPositions = new List<Vector3>();
            foreach (long playerId in group.MemberPlayerIds)
            {
                if (controller.TryGetConnectedPosition(playerId, out Vector3 position))
                    memberPositions.Add(position);
            }
            if (memberPositions.Count == 0)
                return new List<ZoneClaim>();

            return serverClaims.Values
                .Where(claim => claim.EventId == state.EventId && memberPositions.Any(position => Utils.DistanceXZ(position, ZoneSystem.GetZonePos(claim.Zone)) <= RelevantZoneDistance))
                .OrderBy(claim => claim.Zone.x)
                .ThenBy(claim => claim.Zone.y)
                .ToList();
        }

        internal static void Reset()
        {
            serverClaims.Clear();
            localReportTimer = 0f;
        }

        internal static string Dump(BloodMoonEventState state)
        {
            if (state == null)
                return "No Blood Moon state.";
            double now = SeasonState.IsActive ? seasonState.GetTotalSeconds() : 0d;
            Prune(state.EventId, now);
            if (serverClaims.Count == 0)
                return "No active Blood Moon zone claims.";
            return string.Join("\n", serverClaims.Values
                .OrderBy(claim => claim.Zone.x)
                .ThenBy(claim => claim.Zone.y)
                .Select(claim => $"zone={claim.Zone} peer={claim.PeerId} player={claim.PlayerId} age={Math.Max(0d, now - claim.LastSeen):0.0}s"));
        }

        private static void Prune(long eventId, double now)
        {
            long serverPeerId = ZRoutedRpc.instance != null ? ZRoutedRpc.instance.GetServerPeerID() : 0L;
            foreach (Vector2i zone in serverClaims
                .Where(pair => pair.Value.EventId != eventId || now - pair.Value.LastSeen > ClaimLifetimeSeconds ||
                    pair.Value.PeerId != serverPeerId && (ZNet.instance == null || ZNet.instance.GetPeer(pair.Value.PeerId) == null))
                .Select(pair => pair.Key)
                .ToList())
            {
                serverClaims.Remove(zone);
            }
        }

        private static bool ValidatePeerPlayer(long sender, long playerId)
        {
            if (playerId == 0L || ZNet.instance == null || ZDOMan.instance == null || ZRoutedRpc.instance == null)
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
