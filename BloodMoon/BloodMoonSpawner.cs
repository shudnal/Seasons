using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonSpawner
    {
        internal static readonly int EventMarker = "Seasons.BloodMoon.SpawnedEventId".GetStableHashCode();
        internal static readonly int GroupMarker = "Seasons.BloodMoon.GroupId".GetStableHashCode();
        internal static readonly int RoleMarker = "Seasons.BloodMoon.Role".GetStableHashCode();

        private static readonly Dictionary<string, BloodMoonSpawnLeaseState> clientLeases = new Dictionary<string, BloodMoonSpawnLeaseState>();
        private static float clientSpawnTimer;

        internal static void UpdateServerLeases(BloodMoonEventState state, double now)
        {
            if (state == null || state.SpawnsStopped || !state.IsCombatLive || BloodMoonController.Instance == null)
                return;

            PruneMissingExtras(state);
            int serverRemaining = Mathf.Max(0, BloodMoonConfig.ServerExtraEnemyHardCap.Value - state.ExtraEnemyZdos.Count);
            HashSet<string> activeKeys = new HashSet<string>();

            foreach (BloodMoonGroupState group in state.Groups.Values)
            {
                int groupExisting = state.ExtraEnemyZdos.Count(id => GetMarkedGroupId(id) == group.GroupId);
                int groupRemaining = Mathf.Max(0, BloodMoonConfig.GroupExtraEnemyCap.Value - groupExisting);
                if (groupRemaining <= 0 || serverRemaining <= 0)
                    continue;

                List<(Vector2i Zone, long Peer)> zones = new List<(Vector2i, long)>();
                foreach (long playerId in group.MemberPlayerIds)
                {
                    if (!BloodMoonController.Instance.TryGetConnectedPosition(playerId, out Vector3 position))
                        continue;
                    long peer = BloodMoonController.Instance.GetPeerForPlayer(playerId);
                    if (peer == 0L)
                        continue;
                    Vector2i zone = ZoneSystem.GetZone(position);
                    if (!zones.Any(entry => entry.Zone == zone))
                        zones.Add((zone, peer));
                }

                if (zones.Count == 0)
                    continue;

                int budget = Mathf.Min(groupRemaining, serverRemaining);
                int perZone = Mathf.Max(1, Mathf.CeilToInt(budget / (float)zones.Count));
                foreach ((Vector2i zone, long peer) in zones)
                {
                    if (budget <= 0 || serverRemaining <= 0)
                        break;
                    string key = MakeLeaseKey(group.GroupId, zone.x, zone.y);
                    activeKeys.Add(key);
                    int allowance = Mathf.Min(perZone, budget, serverRemaining);
                    if (!state.SpawnLeases.TryGetValue(key, out BloodMoonSpawnLeaseState lease) || lease.ExpiresAt <= now || lease.OwnerPeerId != peer || lease.GroupRevision != group.Revision)
                    {
                        lease = new BloodMoonSpawnLeaseState
                        {
                            EventId = state.EventId,
                            GroupId = group.GroupId,
                            GroupRevision = group.Revision,
                            ZoneX = zone.x,
                            ZoneY = zone.y,
                            OwnerPeerId = peer,
                            OwnerSessionId = peer,
                            LeaseRevision = ++state.LeaseSequence,
                            Anchor = group.Anchor,
                            Allowance = allowance,
                            GroupCap = BloodMoonConfig.GroupExtraEnemyCap.Value,
                            ServerHardCap = BloodMoonConfig.ServerExtraEnemyHardCap.Value,
                            PoolRevision = 1,
                            ExpiresAt = now + Mathf.Max(2f, BloodMoonConfig.SpawnLeaseSeconds.Value)
                        };
                        state.SpawnLeases[key] = lease;
                    }
                    else
                    {
                        lease.Anchor = group.Anchor;
                        lease.Allowance = allowance;
                        lease.ExpiresAt = now + Mathf.Max(2f, BloodMoonConfig.SpawnLeaseSeconds.Value);
                    }
                    BloodMoonNetwork.SendSpawnLease(peer, lease);
                    budget -= allowance;
                    serverRemaining -= allowance;
                }
            }

            foreach (string key in state.SpawnLeases.Keys.Where(key => !activeKeys.Contains(key) || state.SpawnLeases[key].ExpiresAt + 30d < now).ToList())
                state.SpawnLeases.Remove(key);
        }

        internal static void ReceiveLease(BloodMoonSpawnLeaseState lease)
        {
            if (lease == null || lease.EventId != BloodMoonNetwork.ClientGlobal.EventId || lease.OwnerSessionId != ZDOMan.GetSessionID())
                return;
            string key = MakeLeaseKey(lease.GroupId, lease.ZoneX, lease.ZoneY);
            if (clientLeases.TryGetValue(key, out BloodMoonSpawnLeaseState current) && current.LeaseRevision > lease.LeaseRevision)
                return;
            clientLeases[key] = lease;
        }

        internal static void TickClient(float dt)
        {
            if (!BloodMoonInteractionRules.IsEventCombatLive || Player.m_localPlayer == null || ZDOMan.instance == null)
                return;
            clientSpawnTimer -= dt;
            if (clientSpawnTimer > 0f)
                return;
            clientSpawnTimer = 1f;

            double now = SeasonState.IsActive ? seasonState.GetTotalSeconds() : 0d;
            foreach (KeyValuePair<string, BloodMoonSpawnLeaseState> entry in clientLeases.ToArray())
            {
                BloodMoonSpawnLeaseState lease = entry.Value;
                if (lease.EventId != BloodMoonNetwork.ClientGlobal.EventId || lease.ExpiresAt <= now || lease.Allowance <= 0)
                {
                    clientLeases.Remove(entry.Key);
                    continue;
                }
                Vector2i zone = new Vector2i(lease.ZoneX, lease.ZoneY);
                if (!OwnsLoadedZone(zone))
                    continue;
                TrySpawnFromLease(lease, zone);
            }
        }

        private static bool OwnsLoadedZone(Vector2i zone)
        {
            foreach (SpawnSystem spawnSystem in SpawnSystem.m_instances)
            {
                if (spawnSystem == null || spawnSystem.m_nview == null || !spawnSystem.m_nview.IsValid() || !spawnSystem.m_nview.IsOwner())
                    continue;
                if (ZoneSystem.GetZone(spawnSystem.transform.position) == zone)
                    return true;
            }
            return false;
        }

        private static void TrySpawnFromLease(BloodMoonSpawnLeaseState lease, Vector2i zone)
        {
            string prefabName = BloodMoonConfig.TestEnemyPrefab.Value?.Trim();
            GameObject prefab = string.IsNullOrEmpty(prefabName) || ZNetScene.instance == null ? null : ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null || prefab.GetComponent<MonsterAI>() == null)
                return;

            for (int attempt = 0; attempt < 12; ++attempt)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(40f, 75f);
                Vector3 point = lease.Anchor + new Vector3(offset.x, 0f, offset.y);
                if (ZoneSystem.GetZone(point) != zone || !ZoneSystem.instance.FindFloor(point, out float height))
                    continue;
                point.y = height + 0.5f;
                if (!IsSpawnPointAllowed(point))
                    continue;
                GameObject spawned = UnityEngine.Object.Instantiate(prefab, point, Quaternion.identity);
                ZNetView nview = spawned.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid())
                {
                    UnityEngine.Object.Destroy(spawned);
                    return;
                }
                ZDO zdo = nview.GetZDO();
                zdo.Set(EventMarker, lease.EventId);
                zdo.Set(GroupMarker, lease.GroupId);
                zdo.Set(RoleMarker, (int)(Player.m_localPlayer.InInterior() ? BloodMoonExtraEnemyRole.Interior : BloodMoonExtraEnemyRole.Surface));
                MonsterAI ai = spawned.GetComponent<MonsterAI>();
                if (ai != null)
                {
                    ai.m_eventCreature = false;
                    ai.m_despawnInDay = false;
                }
                lease.Allowance--;
                BloodMoonNetwork.SendSpawnReport(lease.EventId, lease.GroupId, lease.GroupRevision, lease.LeaseRevision, zdo.m_uid);
                LogInfo($"[BloodMoon.Spawn] Spawned {prefab.name} {zdo.m_uid} for group {lease.GroupId} zone {zone}.");
                return;
            }

            // Interior fallback uses authored CreatureSpawner positions but never calls CreatureSpawner.Spawn().
            foreach (CreatureSpawner spawner in CreatureSpawner.m_creatureSpawners)
            {
                if (spawner == null || spawner.m_nview == null || !spawner.m_nview.IsOwner() || ZoneSystem.GetZone(spawner.transform.position) != zone)
                    continue;
                Vector3 point = spawner.transform.position;
                if (!IsSpawnPointAllowed(point))
                    continue;
                GameObject spawned = UnityEngine.Object.Instantiate(prefab, point, Quaternion.identity);
                ZNetView nview = spawned.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid())
                {
                    UnityEngine.Object.Destroy(spawned);
                    return;
                }
                ZDO zdo = nview.GetZDO();
                zdo.Set(EventMarker, lease.EventId);
                zdo.Set(GroupMarker, lease.GroupId);
                zdo.Set(RoleMarker, (int)BloodMoonExtraEnemyRole.Interior);
                lease.Allowance--;
                BloodMoonNetwork.SendSpawnReport(lease.EventId, lease.GroupId, lease.GroupRevision, lease.LeaseRevision, zdo.m_uid);
                return;
            }
        }

        private static bool IsSpawnPointAllowed(Vector3 point)
        {
            if (Player.m_localPlayer == null)
                return false;
            float distance = Utils.DistanceXZ(Player.m_localPlayer.transform.position, point);
            if (distance < 35f || distance > 90f)
                return false;
            if (EffectArea.IsPointInsideArea(point, EffectArea.Type.PlayerBase) || EffectArea.IsPointInsideArea(point, EffectArea.Type.NoMonsters))
                return false;
            Camera camera = Utils.GetMainCamera();
            if (camera != null && GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), new Bounds(point, Vector3.one * 2f)))
                return false;
            return true;
        }

        internal static void AcceptSpawnReport(BloodMoonEventState state, long sender, long groupId, int groupRevision, int leaseRevision, ZDOID spawnedId, double now)
        {
            if (state == null || state.SpawnsStopped || spawnedId.IsNone())
                return;
            string leaseKey = state.SpawnLeases.Keys.FirstOrDefault(key => state.SpawnLeases[key].GroupId == groupId && state.SpawnLeases[key].LeaseRevision == leaseRevision);
            if (leaseKey == null)
                return;
            BloodMoonSpawnLeaseState lease = state.SpawnLeases[leaseKey];
            if (lease.OwnerPeerId != sender || lease.EventId != state.EventId || lease.GroupRevision != groupRevision || lease.ExpiresAt < now)
                return;
            if (!state.Groups.TryGetValue(groupId, out BloodMoonGroupState group) || group.Revision != groupRevision)
                return;
            if (state.ExtraEnemyZdos.Count >= BloodMoonConfig.ServerExtraEnemyHardCap.Value || state.ExtraEnemyZdos.Count(id => GetMarkedGroupId(id) == groupId) >= BloodMoonConfig.GroupExtraEnemyCap.Value)
            {
                DestroyZdo(spawnedId);
                return;
            }
            ZDO zdo = ZDOMan.instance.GetZDO(spawnedId);
            if (zdo == null || zdo.GetLong(EventMarker, -1L) != state.EventId || zdo.GetLong(GroupMarker, -1L) != groupId)
                return;
            state.ExtraEnemyZdos.Add(spawnedId.ToString());
            BloodMoonPersistence.Save(state);
        }

        internal static void StopServerLeases(BloodMoonEventState state)
        {
            if (state == null)
                return;
            state.SpawnLeases.Clear();
            clientLeases.Clear();
        }

        internal static void CleanupExtraEnemies(BloodMoonEventState state)
        {
            if (state == null || ZDOMan.instance == null)
                return;
            foreach (string text in state.ExtraEnemyZdos.ToArray())
            {
                if (TryParseZdoId(text, out ZDOID id))
                    DestroyZdo(id);
            }
            state.ExtraEnemyZdos.Clear();
            state.SpawnLeases.Clear();
        }

        internal static void Recover(BloodMoonEventState state)
        {
            if (state == null)
                return;
            PruneMissingExtras(state);
            if (!state.IsCombatLive)
                CleanupExtraEnemies(state);
        }

        internal static void ResetClientState()
        {
            clientLeases.Clear();
            clientSpawnTimer = 0f;
        }

        internal static long GetMarkedGroupId(string zdoId)
        {
            if (!TryParseZdoId(zdoId, out ZDOID id) || ZDOMan.instance == null)
                return -1L;
            return ZDOMan.instance.GetZDO(id)?.GetLong(GroupMarker, -1L) ?? -1L;
        }

        private static void PruneMissingExtras(BloodMoonEventState state)
        {
            if (ZDOMan.instance == null)
                return;
            foreach (string id in state.ExtraEnemyZdos.Where(id => !TryParseZdoId(id, out ZDOID zdoId) || ZDOMan.instance.GetZDO(zdoId) == null).ToList())
                state.ExtraEnemyZdos.Remove(id);
        }

        private static string MakeLeaseKey(long groupId, int x, int y) => $"{groupId}:{x}:{y}";

        internal static bool TryParseZdoId(string value, out ZDOID id)
        {
            id = ZDOID.None;
            if (string.IsNullOrEmpty(value))
                return false;
            string[] parts = value.Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[0], out long userId) || !uint.TryParse(parts[1], out uint objectId))
                return false;
            id = new ZDOID(userId, objectId);
            return true;
        }

        private static void DestroyZdo(ZDOID id)
        {
            ZDO zdo = ZDOMan.instance?.GetZDO(id);
            if (zdo != null)
                ZDOMan.instance.DestroyZDO(zdo);
        }
    }

    [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
    internal static class BloodMoonExtraDropPatch
    {
        private static bool Prefix(CharacterDrop __instance, ref List<KeyValuePair<GameObject, int>> __result)
        {
            Character character = __instance.GetComponent<Character>();
            if (!BloodMoonInteractionRules.IsBloodMoonExtra(character))
                return true;
            __result = new List<KeyValuePair<GameObject, int>>();
            return false;
        }
    }

    [HarmonyPatch(typeof(Ragdoll), nameof(Ragdoll.Setup))]
    internal static class BloodMoonExtraRagdollPatch
    {
        private static void Postfix(Ragdoll __instance)
        {
            ZNetView nview = __instance.GetComponent<ZNetView>();
            if (nview?.GetZDO() != null && nview.GetZDO().GetLong(EventMarker, -1L) == BloodMoonNetwork.ClientGlobal.EventId)
                __instance.m_ttl = Mathf.Min(__instance.m_ttl, 2f);
        }
    }
}
