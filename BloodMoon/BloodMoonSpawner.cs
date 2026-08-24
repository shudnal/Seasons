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

        private const double PendingReportLifetimeSeconds = 30d;
        private const double PendingCleanupLifetimeSeconds = 30d;

        private sealed class PendingSpawnReport
        {
            internal long EventId;
            internal long GroupId;
            internal int ZoneX;
            internal int ZoneY;
            internal ZDOID SpawnedId;
            internal double ExpiresAt;
        }

        private static readonly Dictionary<string, BloodMoonSpawnLeaseState> clientLeases = new Dictionary<string, BloodMoonSpawnLeaseState>();
        private static readonly Dictionary<ZDOID, PendingSpawnReport> pendingSpawnReports = new Dictionary<ZDOID, PendingSpawnReport>();
        private static readonly Dictionary<ZDOID, double> pendingCleanupZdos = new Dictionary<ZDOID, double>();
        private static float clientSpawnTimer;
        private static float serverMaintenanceTimer;

        internal static void UpdateServerLeases(BloodMoonEventState state, double now)
        {
            if (state == null || state.SpawnsStopped || !state.IsCombatLive || BloodMoonController.Instance == null)
                return;

            ProcessPendingReports(state, now);
            PruneMissingExtras(state);
            PruneServerLeases(state, now);

            int hardCap = Math.Max(0, BloodMoonConfig.ServerExtraEnemyHardCap.Value);
            int pendingServer = pendingSpawnReports.Values.Count(report => report.EventId == state.EventId);
            int reservedServer = state.SpawnLeases.Values.Sum(lease => Math.Max(0, lease.Allowance));
            int serverAvailable = Math.Max(0, hardCap - state.ExtraEnemyZdos.Count - pendingServer - reservedServer);
            HashSet<string> relevantLeaseKeys = new HashSet<string>();

            foreach (BloodMoonGroupState group in state.Groups.Values.OrderBy(group => group.GroupId))
            {
                int activeMembers = group.MemberPlayerIds.Count(id => state.Participants.TryGetValue(id, out BloodMoonParticipantState participant) && participant.IsCombatActive);
                int groupCap = BloodMoonConfig.GetGroupExtraEnemyCap(activeMembers);
                int groupExisting = state.ExtraEnemyZdos.Count(id => GetMarkedGroupId(id) == group.GroupId);
                int groupPending = pendingSpawnReports.Values.Count(report => report.EventId == state.EventId && report.GroupId == group.GroupId);
                int groupReserved = state.SpawnLeases.Values.Where(lease => lease.GroupId == group.GroupId).Sum(lease => Math.Max(0, lease.Allowance));
                int groupAvailable = Math.Max(0, groupCap - groupExisting - groupPending - groupReserved);

                List<BloodMoonZoneOwnership.ZoneClaim> claims = BloodMoonZoneOwnership.GetRelevantClaims(state, group, now);
                if (claims.Count == 0)
                    continue;

                for (int claimIndex = 0; claimIndex < claims.Count; ++claimIndex)
                {
                    BloodMoonZoneOwnership.ZoneClaim claim = claims[claimIndex];
                    string key = MakeLeaseKey(group.GroupId, claim.Zone.x, claim.Zone.y);
                    relevantLeaseKeys.Add(key);

                    if (state.SpawnLeases.TryGetValue(key, out BloodMoonSpawnLeaseState existing))
                    {
                        if (existing.OwnerPeerId != claim.PeerId || existing.OwnerSessionId != claim.PeerId || existing.GroupRevision != group.Revision)
                        {
                            serverAvailable += Math.Max(0, existing.Allowance);
                            groupAvailable += Math.Max(0, existing.Allowance);
                            state.SpawnLeases.Remove(key);
                        }
                        else
                        {
                            existing.Anchor = group.Anchor;
                            existing.GroupCap = groupCap;
                            existing.ServerHardCap = hardCap;
                            existing.ExpiresAt = now + Math.Max(2f, BloodMoonConfig.SpawnLeaseSeconds.Value);
                            BloodMoonNetwork.SendSpawnLease(claim.PeerId, existing);
                            continue;
                        }
                    }

                    if (groupAvailable <= 0 || serverAvailable <= 0)
                        continue;

                    int remainingClaims = Math.Max(1, claims.Count - claimIndex);
                    int allowance = Math.Max(1, Mathf.CeilToInt(Math.Min(groupAvailable, serverAvailable) / (float)remainingClaims));
                    allowance = Math.Min(allowance, Math.Min(groupAvailable, serverAvailable));

                    BloodMoonSpawnLeaseState lease = new BloodMoonSpawnLeaseState
                    {
                        EventId = state.EventId,
                        GroupId = group.GroupId,
                        GroupRevision = group.Revision,
                        ZoneX = claim.Zone.x,
                        ZoneY = claim.Zone.y,
                        OwnerPeerId = claim.PeerId,
                        OwnerSessionId = claim.PeerId,
                        LeaseRevision = ++state.LeaseSequence,
                        Anchor = group.Anchor,
                        Allowance = allowance,
                        GroupCap = groupCap,
                        ServerHardCap = hardCap,
                        PoolRevision = 1,
                        ExpiresAt = now + Math.Max(2f, BloodMoonConfig.SpawnLeaseSeconds.Value)
                    };
                    state.SpawnLeases[key] = lease;
                    groupAvailable -= allowance;
                    serverAvailable -= allowance;
                    BloodMoonNetwork.SendSpawnLease(claim.PeerId, lease);
                }
            }

            foreach (string key in state.SpawnLeases.Keys.Where(key => !relevantLeaseKeys.Contains(key)).ToList())
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
            TickServerMaintenance(dt);
            BloodMoonZoneOwnership.TickClient(dt);
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

        private static void TickServerMaintenance(float dt)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null || !SeasonState.IsActive)
                return;
            serverMaintenanceTimer -= Mathf.Max(0f, dt);
            if (serverMaintenanceTimer > 0f)
                return;
            serverMaintenanceTimer = 0.5f;

            double now = seasonState.GetTotalSeconds();
            BloodMoonEventState state = BloodMoonController.Instance?.State;
            if (state != null && state.IsCombatLive)
                ProcessPendingReports(state, now);
            ProcessPendingCleanup(now);
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
            GameObject prefab = ResolveSpawnPrefab();
            if (prefab == null)
                return;

            List<Player> targets = BloodMoonInteractionRules.GetLoadedActiveParticipants(preferFighting: false)
                .Where(player => Utils.DistanceXZ(player.transform.position, ZoneSystem.GetZonePos(zone)) <= 180f)
                .ToList();
            if (targets.Count == 0)
                return;

            bool interior = targets.Any(player => player.InInterior());
            bool spawned = interior
                ? TrySpawnInterior(prefab, lease, zone, targets)
                : TrySpawnSurface(prefab, lease, zone, targets);

            if (spawned)
                lease.Allowance--;
        }

        private static GameObject ResolveSpawnPrefab()
        {
            string prefabName = BloodMoonConfig.TestEnemyPrefab.Value?.Trim();
            GameObject prefab = string.IsNullOrEmpty(prefabName) || ZNetScene.instance == null ? null : ZNetScene.instance.GetPrefab(prefabName);
            return prefab != null && prefab.GetComponent<MonsterAI>() != null ? prefab : null;
        }

        private static bool TrySpawnSurface(GameObject prefab, BloodMoonSpawnLeaseState lease, Vector2i zone, List<Player> targets)
        {
            for (int attempt = 0; attempt < 16; ++attempt)
            {
                Player target = targets[UnityEngine.Random.Range(0, targets.Count)];
                Vector2 direction = UnityEngine.Random.insideUnitCircle.normalized;
                if (direction == Vector2.zero)
                    direction = Vector2.right;
                Vector3 point = target.transform.position + new Vector3(direction.x, 0f, direction.y) * UnityEngine.Random.Range(40f, 75f);
                if (ZoneSystem.GetZone(point) != zone || !ZoneSystem.instance.FindFloor(point, out float height))
                    continue;
                point.y = height + 0.5f;
                if (Character.InInterior(point) || !IsSurfaceSpawnPointAllowed(point, targets) || !HasPath(prefab, point, target.transform.position))
                    continue;
                return SpawnMarked(prefab, point, lease, zone, BloodMoonExtraEnemyRole.Surface);
            }
            return false;
        }

        private static bool TrySpawnInterior(GameObject prefab, BloodMoonSpawnLeaseState lease, Vector2i zone, List<Player> targets)
        {
            List<Player> interiorTargets = targets.Where(player => player.InInterior()).ToList();
            if (interiorTargets.Count == 0)
                return false;

            List<CreatureSpawner> candidates = CreatureSpawner.m_creatureSpawners
                .Where(spawner => spawner != null && spawner.m_nview != null && spawner.m_nview.IsValid() && spawner.m_nview.IsOwner())
                .Where(spawner => ZoneSystem.GetZone(spawner.transform.position) == zone && Character.InInterior(spawner.transform.position))
                .OrderBy(spawner => Vector3.Distance(spawner.transform.position, lease.Anchor))
                .ToList();

            foreach (CreatureSpawner spawner in candidates)
            {
                Vector3 point = spawner.transform.position;
                Player target = interiorTargets.OrderBy(player => Vector3.Distance(player.transform.position, point)).FirstOrDefault();
                if (target == null || !HasPath(prefab, point, target.transform.position))
                    continue;
                return SpawnMarked(prefab, point, lease, zone, BloodMoonExtraEnemyRole.Interior);
            }
            return false;
        }

        private static bool IsSurfaceSpawnPointAllowed(Vector3 point, List<Player> targets)
        {
            float nearest = targets.Min(player => Utils.DistanceXZ(player.transform.position, point));
            if (nearest < 35f || nearest > 90f)
                return false;

            Camera camera = Utils.GetMainCamera();
            if (camera != null && GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), new Bounds(point, Vector3.one * 2f)))
                return false;

            // PlayerBase and NoMonsters are intentionally ignored for Blood Moon event extras.
            return true;
        }

        private static bool HasPath(GameObject prefab, Vector3 from, Vector3 to)
        {
            BaseAI ai = prefab.GetComponent<BaseAI>();
            Character character = prefab.GetComponent<Character>();
            if (ai == null || character == null || character.m_flying || Pathfinding.instance == null)
                return true;
            return Pathfinding.instance.HavePath(from, to, ai.m_pathAgentType);
        }

        private static bool SpawnMarked(GameObject prefab, Vector3 point, BloodMoonSpawnLeaseState lease, Vector2i zone, BloodMoonExtraEnemyRole role)
        {
            GameObject spawned = UnityEngine.Object.Instantiate(prefab, point, Quaternion.identity);
            ZNetView nview = spawned.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                UnityEngine.Object.Destroy(spawned);
                return false;
            }

            ZDO zdo = nview.GetZDO();
            zdo.Set(EventMarker, lease.EventId);
            zdo.Set(GroupMarker, lease.GroupId);
            zdo.Set(RoleMarker, (int)role);

            MonsterAI ai = spawned.GetComponent<MonsterAI>();
            if (ai != null)
            {
                ai.m_eventCreature = false;
                ai.m_despawnInDay = false;
            }

            BloodMoonNetwork.SendSpawnReport(lease.EventId, lease.GroupId, lease.GroupRevision, zone.x, zone.y, lease.LeaseRevision, zdo.m_uid);
            LogInfo($"[BloodMoon][event:{lease.EventId}][zone:{zone}][spawn] {prefab.name} {zdo.m_uid} group={lease.GroupId} lease={lease.LeaseRevision}.");
            return true;
        }

        internal static void AcceptSpawnReport(BloodMoonEventState state, long sender, long groupId, int groupRevision, int zoneX, int zoneY, int leaseRevision, ZDOID spawnedId, double now)
        {
            if (state == null || state.SpawnsStopped || spawnedId.IsNone() || state.ExtraEnemyZdos.Contains(spawnedId.ToString()) || pendingSpawnReports.ContainsKey(spawnedId))
                return;

            string leaseKey = MakeLeaseKey(groupId, zoneX, zoneY);
            if (!state.SpawnLeases.TryGetValue(leaseKey, out BloodMoonSpawnLeaseState lease))
                return;
            if (lease.OwnerPeerId != sender || lease.EventId != state.EventId || lease.GroupRevision != groupRevision || lease.LeaseRevision != leaseRevision || lease.ExpiresAt < now || lease.Allowance <= 0)
                return;
            if (!state.Groups.TryGetValue(groupId, out BloodMoonGroupState group) || group.Revision != groupRevision)
                return;

            lease.Allowance--;
            int hardCap = Math.Max(0, BloodMoonConfig.ServerExtraEnemyHardCap.Value);
            int groupCap = BloodMoonConfig.GetGroupExtraEnemyCap(group.MemberPlayerIds.Count(id => state.Participants.TryGetValue(id, out BloodMoonParticipantState participant) && participant.IsCombatActive));
            int pendingServer = pendingSpawnReports.Values.Count(report => report.EventId == state.EventId);
            int pendingGroup = pendingSpawnReports.Values.Count(report => report.EventId == state.EventId && report.GroupId == groupId);
            if (state.ExtraEnemyZdos.Count + pendingServer >= hardCap || state.ExtraEnemyZdos.Count(id => GetMarkedGroupId(id) == groupId) + pendingGroup >= groupCap)
            {
                DestroyZdo(spawnedId);
                BloodMoonPersistence.Save(state);
                return;
            }

            PendingSpawnReport report = new PendingSpawnReport
            {
                EventId = state.EventId,
                GroupId = groupId,
                ZoneX = zoneX,
                ZoneY = zoneY,
                SpawnedId = spawnedId,
                ExpiresAt = now + PendingReportLifetimeSeconds
            };

            ZDO zdo = ZDOMan.instance?.GetZDO(spawnedId);
            if (zdo == null)
            {
                pendingSpawnReports[spawnedId] = report;
                BloodMoonPersistence.Save(state);
                return;
            }

            if (ValidateSpawnedZdo(report, zdo))
                state.ExtraEnemyZdos.Add(spawnedId.ToString());
            else
                DestroyZdo(spawnedId);
            BloodMoonPersistence.Save(state);
        }

        private static void ProcessPendingReports(BloodMoonEventState state, double now)
        {
            if (state == null || ZDOMan.instance == null || pendingSpawnReports.Count == 0)
                return;

            bool changed = false;
            foreach (KeyValuePair<ZDOID, PendingSpawnReport> entry in pendingSpawnReports.ToArray())
            {
                PendingSpawnReport report = entry.Value;
                if (report.EventId != state.EventId || now >= report.ExpiresAt)
                {
                    ZDO expired = ZDOMan.instance.GetZDO(entry.Key);
                    if (expired != null && expired.GetLong(EventMarker, -1L) == report.EventId)
                        DestroyZdo(entry.Key);
                    pendingSpawnReports.Remove(entry.Key);
                    changed = true;
                    continue;
                }

                ZDO zdo = ZDOMan.instance.GetZDO(entry.Key);
                if (zdo == null)
                    continue;

                if (ValidateSpawnedZdo(report, zdo))
                    state.ExtraEnemyZdos.Add(entry.Key.ToString());
                else
                    DestroyZdo(entry.Key);
                pendingSpawnReports.Remove(entry.Key);
                changed = true;
            }

            if (changed)
                BloodMoonPersistence.Save(state);
        }

        private static bool ValidateSpawnedZdo(PendingSpawnReport report, ZDO zdo)
        {
            return report != null && zdo != null && zdo.GetLong(EventMarker, -1L) == report.EventId &&
                zdo.GetLong(GroupMarker, -1L) == report.GroupId && zdo.GetSector() == new Vector2i(report.ZoneX, report.ZoneY);
        }

        internal static void StopServerLeases(BloodMoonEventState state)
        {
            state?.SpawnLeases.Clear();
            clientLeases.Clear();
            BloodMoonZoneOwnership.Reset();
        }

        internal static void CleanupExtraEnemies(BloodMoonEventState state)
        {
            if (state == null || ZDOMan.instance == null)
                return;

            double now = SeasonState.IsActive ? seasonState.GetTotalSeconds() : 0d;
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values
                .Where(zdo => zdo.GetLong(EventMarker, -1L) == state.EventId)
                .ToArray())
            {
                DestroyZdo(zdo.m_uid);
            }

            foreach (PendingSpawnReport report in pendingSpawnReports.Values.Where(report => report.EventId == state.EventId).ToArray())
                pendingCleanupZdos[report.SpawnedId] = now + PendingCleanupLifetimeSeconds;
            pendingSpawnReports.Clear();
            state.ExtraEnemyZdos.Clear();
            state.SpawnLeases.Clear();
            clientLeases.Clear();
            BloodMoonZoneOwnership.Reset();
        }

        private static void ProcessPendingCleanup(double now)
        {
            if (ZDOMan.instance == null || pendingCleanupZdos.Count == 0)
                return;
            foreach (KeyValuePair<ZDOID, double> pending in pendingCleanupZdos.ToArray())
            {
                ZDO zdo = ZDOMan.instance.GetZDO(pending.Key);
                if (zdo != null)
                {
                    if (zdo.GetLong(EventMarker, -1L) >= 0L)
                        DestroyZdo(pending.Key);
                    pendingCleanupZdos.Remove(pending.Key);
                }
                else if (now >= pending.Value)
                {
                    pendingCleanupZdos.Remove(pending.Key);
                }
            }
        }

        internal static void Recover(BloodMoonEventState state)
        {
            pendingSpawnReports.Clear();
            pendingCleanupZdos.Clear();
            if (state == null || ZDOMan.instance == null)
                return;

            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.ToArray())
            {
                long markedEvent = zdo.GetLong(EventMarker, -1L);
                if (markedEvent < 0L)
                    continue;

                if (markedEvent == state.EventId && state.IsCombatLive)
                    state.ExtraEnemyZdos.Add(zdo.m_uid.ToString());
                else
                    DestroyZdo(zdo.m_uid);
            }

            PruneMissingExtras(state);
            if (!state.IsCombatLive)
                CleanupExtraEnemies(state);
        }

        internal static void ResetClientState()
        {
            clientLeases.Clear();
            clientSpawnTimer = 0f;
            serverMaintenanceTimer = 0f;
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                pendingSpawnReports.Clear();
                pendingCleanupZdos.Clear();
            }
            BloodMoonZoneOwnership.Reset();
        }

        internal static long GetMarkedGroupId(string zdoId)
        {
            if (!TryParseZdoId(zdoId, out ZDOID id) || ZDOMan.instance == null)
                return -1L;
            return ZDOMan.instance.GetZDO(id)?.GetLong(GroupMarker, -1L) ?? -1L;
        }

        private static void PruneServerLeases(BloodMoonEventState state, double now)
        {
            foreach (string key in state.SpawnLeases.Keys.Where(key => state.SpawnLeases[key].ExpiresAt <= now || state.SpawnLeases[key].EventId != state.EventId).ToList())
                state.SpawnLeases.Remove(key);
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
            if (!BloodMoonInteractionRules.IsBloodMoonSpawned(character))
                return true;
            __result = new List<KeyValuePair<GameObject, int>>();
            return false;
        }
    }

    [HarmonyPatch(typeof(Ragdoll), nameof(Ragdoll.Setup))]
    internal static class BloodMoonExtraRagdollPatch
    {
        private static void Prefix(Ragdoll __instance, CharacterDrop characterDrop)
        {
            Character character = characterDrop != null ? characterDrop.GetComponent<Character>() : null;
            if (!BloodMoonInteractionRules.IsBloodMoonSpawned(character) || __instance.m_nview == null || !__instance.m_nview.IsValid())
                return;

            ZDO source = character.m_nview.GetZDO();
            ZDO ragdoll = __instance.m_nview.GetZDO();
            ragdoll.Set(EventMarker, source.GetLong(EventMarker, -1L));
            ragdoll.Set(GroupMarker, source.GetLong(GroupMarker, -1L));
            ragdoll.Set(RoleMarker, source.GetInt(RoleMarker, 0));
            __instance.m_ttl = __instance.m_ttl <= 0f ? 2f : Mathf.Min(__instance.m_ttl, 2f);
            __instance.m_dropItems = false;
        }
    }
}
