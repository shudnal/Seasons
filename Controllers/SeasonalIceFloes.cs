using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;
using static Seasons.Seasons;
using static Seasons.ZoneSystemVariantController;

namespace Seasons
{
    /// <summary>Zone-owner placement in normally loaded zones, with server-only global cleanup.</summary>
    internal static class SeasonalIceFloes
    {
        // These bound service work, not an individual Unity call such as Instantiate or a terrain raycast.
        private const int DiscoveryBudget = 8;
        private const int ObjectBudget = 64;
        private const int CandidateBudget = 4;
        private const int InstanceBudget = 1;
        private const int RemovalBudget = 4;
        private const int MarkerBudget = 4;
        private const double ServiceMilliseconds = 1.5;
        private const float PeerRefreshSeconds = 0.5f;

        private sealed class RegionScan
        {
            internal long Peer;
            internal Vector2s Center;
            internal int NearRadius;
            internal int Radius;
            internal int Ring;
            internal int Edge;
            internal bool OutsideFirst;

            internal bool Next(out Vector2s zone)
            {
                zone = Center;
                if (Ring > Radius || Ring < 0)
                    return false;
                if (Ring == 0)
                {
                    Ring = OutsideFirst ? -1 : 1;
                    return true;
                }
                int side = Edge / (2 * Ring);
                int offset = Edge % (2 * Ring);
                int x = side == 0 ? -Ring + offset : side == 1 ? Ring : side == 2 ? Ring - offset : -Ring;
                int y = side == 0 ? -Ring : side == 1 ? -Ring + offset : side == 2 ? Ring : Ring - offset;
                zone = new Vector2s(Center.x + x, Center.y + y);
                if (++Edge == 8 * Ring)
                {
                    Edge = 0;
                    Ring += OutsideFirst ? -1 : 1;
                }
                return true;
            }
        }

        private sealed class ZoneWork
        {
            internal Vector2s Zone;
            internal readonly List<ZDO> Objects = new List<ZDO>();
            internal readonly List<ZoneSystem.ClearArea> Exclusions = new List<ZoneSystem.ClearArea>();
            internal int ObjectIndex;
            internal ZDOID Control;
            internal SpawnSystem SpawnSystem;
            internal bool ExistingFloe;
            internal bool RandomReady;
            internal int Remaining;
            internal UnityEngine.Random.State RandomState;
        }

        private enum CandidateResult { Skipped, Placed, Deferred }

        private static readonly List<RegionScan> scans = new List<RegionScan>();
        private static readonly HashSet<ZoneSystem.SectorIndex> visited = new HashSet<ZoneSystem.SectorIndex>();
        private static readonly Queue<Vector2s> requests = new Queue<Vector2s>();
        private static readonly HashSet<Vector2s> requestSet = new HashSet<Vector2s>();
        private static readonly Dictionary<Vector2s, ZoneWork> work = new Dictionary<Vector2s, ZoneWork>();
        private static readonly Dictionary<Vector2s, List<ZoneSystem.ClearArea>> initialExclusions = new Dictionary<Vector2s, List<ZoneSystem.ClearArea>>();
        private static readonly HashSet<Vector2s> settled = new HashSet<Vector2s>();
        private static readonly Queue<Vector2s> cleanupZones = new Queue<Vector2s>();
        private static readonly HashSet<Vector2s> cleanupSet = new HashSet<Vector2s>();
        private static readonly Queue<ZDOID> removals = new Queue<ZDOID>();
        private static readonly HashSet<ZDOID> removalSet = new HashSet<ZDOID>();
        private static readonly Queue<ZDOID> markerResets = new Queue<ZDOID>();
        private static readonly HashSet<ZDOID> markerSet = new HashSet<ZDOID>();
        private static ZoneWork cleanup;
        private static ZoneSystem world;
        private static float nextPeerCheck;
        private static int scanCursor;
        private static int zonePrefab;
        private static bool wanted;
        private static bool prefabChecked;

        internal static void InitializePrefab(ZoneSystem instance)
        {
            if (!instance || instance.m_vegetation.Count == 0)
                return;
            prefabChecked = true;
            s_iceFloe = instance.m_vegetation.Find(veg => veg.m_prefab?.name == _iceFloeName)?.Clone();
            if (s_iceFloe?.m_prefab == null)
            {
                LogWarning("Unable to initialize seasonal ice floes: the ice1 vegetation prefab was not found.");
                return;
            }
            ZNetView view = s_iceFloe.m_prefab.GetComponent<ZNetView>();
            if (!view || !s_iceFloe.m_prefab.GetComponent<Rigidbody>())
            {
                s_iceFloe = null;
                LogWarning("Unable to initialize seasonal ice floes: ice1 has no network view or rigidbody.");
                return;
            }
            s_iceFloe.m_biome = Heightmap.Biome.Ocean;
            view.m_syncInitialScale = true;
            if (!s_iceFloe.m_prefab.TryGetComponent<IceFloeClimb>(out _))
                s_iceFloe.m_prefab.AddComponent<IceFloeClimb>();
        }

        internal static void Reset()
        {
            scans.Clear();
            visited.Clear();
            requests.Clear();
            requestSet.Clear();
            work.Clear();
            initialExclusions.Clear();
            settled.Clear();
            cleanupZones.Clear();
            cleanupSet.Clear();
            removals.Clear();
            removalSet.Clear();
            markerResets.Clear();
            markerSet.Clear();
            cleanup = null;
            world = null;
            nextPeerCheck = 0f;
            scanCursor = zonePrefab = 0;
            wanted = prefabChecked = false;
            s_iceFloe = null;
        }

        private static bool PeerReady()
        {
            if (!ZoneSystem.instance || !ZNet.instance ||
                !ZNetScene.instance || ZDOMan.instance == null ||
                ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected)
                return false;
            if (world != ZoneSystem.instance)
            {
                Reset();
                world = ZoneSystem.instance;
                zonePrefab = Utils.GetPrefabName(world.m_zoneCtrlPrefab).GetStableHashCode();
            }
            return true;
        }

        private static bool ServerReady() => PeerReady() && ZNet.instance.IsServer();

        private static bool PlacementReady()
        {
            if (!prefabChecked && world.m_vegetation.Count != 0)
                InitializePrefab(world);
            return WorldGenerator.instance != null && (!ZNet.instance.IsServer() || world.LocationsGenerated) &&
                waterStateInitialized && s_waterEdge > 100f && s_iceFloe?.m_prefab != null;
        }

        // A normal water/load event only queues work; placement stays outside the callback.
        internal static bool CheckWaterVolume(WaterVolume water)
        {
            if (!water || !water.m_heightmap)
                return true;
            if (!PeerReady() || !PlacementReady())
                return false;
            ScheduleZone(ZoneSystem.GetZone(water.transform.position));
            return true;
        }

        private static void ScheduleZone(Vector2s zone)
        {
            if (!SeasonState.IsActive || !IsTimeForIceFloes())
            {
                ScheduleCleanup(zone);
                return;
            }
            if (!settled.Contains(zone) && requestSet.Add(zone))
                requests.Enqueue(zone);
        }

        private static void ScheduleCleanup(Vector2s zone)
        {
            if (ServerReady() && !(SeasonState.IsActive && IsTimeForIceFloes()) && cleanupSet.Add(zone))
                cleanupZones.Enqueue(zone);
        }

        // The existing world-cleanup pass supplies its established scope; this service adds no world scan.
        internal static void ScheduleRemoval(ZDO zdo)
        {
            if (zdo != null && ServerReady() && !(SeasonState.IsActive && IsTimeForIceFloes()) && removalSet.Add(zdo.m_uid))
                removals.Enqueue(zdo.m_uid);
        }

        internal static void ScheduleMarkerReset(ZDO zdo)
        {
            if (zdo != null && ServerReady() && !(SeasonState.IsActive && IsTimeForIceFloes()) && markerSet.Add(zdo.m_uid))
                markerResets.Enqueue(zdo.m_uid);
        }

        private static void CancelCleanup()
        {
            cleanup = null;
            cleanupZones.Clear();
            cleanupSet.Clear();
            removals.Clear();
            removalSet.Clear();
            markerResets.Clear();
            markerSet.Clear();
        }

        private static void Observe(long peer, Vector3 position, int nearRadius, int radius)
        {
            Vector2s center = ZoneSystem.GetZone(position);
            RegionScan scan = scans.Find(item => item.Peer == peer);
            if (scan == null)
            {
                scans.Add(new RegionScan { Peer = peer, Center = center, NearRadius = nearRadius, Radius = radius });
                return;
            }
            if (scan.Center == center && scan.Radius == radius && scan.NearRadius == nearRadius)
                return;
            scan.Center = center;
            scan.NearRadius = nearRadius;
            scan.Radius = radius;
            // Approaching a new zone should reach the newly eligible outer ring before old inner rings.
            scan.OutsideFirst = true;
            scan.Ring = radius;
            scan.Edge = 0;
        }

        private static bool InScope(Vector2s zone, bool near)
        {
            foreach (RegionScan scan in scans)
            {
                int radius = near ? scan.NearRadius : scan.Radius;
                if (Math.Abs(zone.x - scan.Center.x) <= radius && Math.Abs(zone.y - scan.Center.y) <= radius &&
                    (world.m_simulationDistance.IsClassic || world.ZonesWithinRadius(scan.Center, zone, radius, ghostZone: !near)))
                    return true;
            }
            return false;
        }

        private static bool BeforeDeadline(long deadline) => Stopwatch.GetTimestamp() < deadline;

        internal static void Update()
        {
            if (!PeerReady() || Game.IsPaused() || Time.timeScale <= 0f)
                return;
            long deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * ServiceMilliseconds / 1000.0);
            bool nowWanted = SeasonState.IsActive && IsTimeForIceFloes();
            if (wanted != nowWanted)
            {
                wanted = nowWanted;
                if (!wanted)
                {
                    foreach (Vector2s zone in work.Keys)
                        ScheduleCleanup(zone);
                    requests.Clear();
                    requestSet.Clear();
                    initialExclusions.Clear();
                }
                else
                    CancelCleanup();
                work.Clear();
                settled.Clear();
                foreach (RegionScan scan in scans)
                {
                    scan.OutsideFirst = false;
                    scan.Ring = scan.Edge = 0;
                }
                nextPeerCheck = 0f;
            }
            if (Time.time >= nextPeerCheck)
            {
                nextPeerCheck = Time.time + PeerRefreshSeconds;
                var distance = ZNet.instance.GetSyncedSimulationDistance();
                int radius = wanted ? distance.NearSimulationDistance : distance.TotalSimulationDistance;
                List<ZNetPeer> peers = ZNet.instance.GetPeers();
                scans.RemoveAll(scan => scan.Peer != 0L && !peers.Exists(peer => peer.m_uid == scan.Peer && peer.IsReady()));
                if (!ZNet.instance.IsDedicated())
                    Observe(0L, ZNet.instance.GetReferencePosition(), distance.NearSimulationDistance, radius);
                if (wanted)
                    scans.RemoveAll(scan => scan.Peer != 0L);
                foreach (ZNetPeer peer in peers)
                    if (!wanted && ZNet.instance.IsServer() && peer.IsReady())
                        Observe(peer.m_uid, peer.GetRefPos(), distance.NearSimulationDistance, radius);
            }

            int objectBudget = ObjectBudget;
            if (ZNet.instance.IsServer())
            {
                ServiceCleanup(ref objectBudget, deadline);
                ServiceRemovals(deadline);
            }

            // Normal load events already queued their zones. Discovery never runs placement in callbacks.
            for (int i = 0; i < DiscoveryBudget && BeforeDeadline(deadline); ++i)
            {
                bool found = false;
                Vector2s zone = default;
                for (int j = 0; j < scans.Count; ++j)
                {
                    scanCursor %= scans.Count;
                    if (scans[scanCursor++].Next(out zone))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    break;
                if ((wanted && settled.Contains(zone)) || !InScope(zone, wanted) ||
                    (!wanted && !world.IsZoneGenerated(zone)))
                    continue;
                if (!wanted || world.IsZoneLoaded(zone))
                    ScheduleZone(zone);
            }

            // Finish old-season cleanup before permitting new placement or writing a completion marker.
            if (!wanted || cleanup != null || cleanupZones.Count != 0 || removals.Count != 0 || markerResets.Count != 0 ||
                !PlacementReady())
                return;
            int candidates = CandidateBudget;
            int instances = InstanceBudget;
            int requestBudget = DiscoveryBudget;
            while (requests.Count != 0 && candidates > 0 && instances > 0 && requestBudget-- > 0 && BeforeDeadline(deadline))
            {
                Vector2s zone = requests.Dequeue();
                requestSet.Remove(zone);
                if (settled.Contains(zone) || !world.IsZoneLoaded(zone) || !InScope(zone, near: true))
                {
                    work.Remove(zone);
                    continue;
                }
                if (!work.TryGetValue(zone, out ZoneWork current))
                {
                    SpawnSystem spawn = FindOwnedSpawnSystem(zone);
                    if (!spawn)
                        continue; // An ordinary loaded zone owner is the only producer.
                    current = BeginZone(zone);
                    current.SpawnSystem = spawn;
                    current.Control = spawn.m_nview.GetZDO().m_uid;
                    work.Add(zone, current);
                }
                if (!OwnsLoadedZone(current))
                {
                    work.Remove(zone);
                    continue; // Discard stale authority and RNG state; a new owner checks existing floes.
                }
                if (!InspectZone(current, false, ref objectBudget, deadline))
                {
                    ScheduleZone(zone);
                    continue;
                }
                ZDO control = ZDOMan.instance.GetZDO(current.Control);
                if (control == null)
                {
                    work.Remove(zone);
                    continue; // Wait for another normal load event, not a distant retry loop.
                }
                if (current.ExistingFloe || control.GetBool(SeasonsVars.s_iceFloesSpawned))
                {
                    // A partial run from a previous session is also a duplicate-prevention input.
                    // Do not invent a completed marker for it, or recreate deliberately removed floes.
                    Complete(zone);
                    continue;
                }
                if (!current.RandomReady)
                    InitializeRandom(current);
                if (current.Remaining <= 0)
                {
                    control.Set(SeasonsVars.s_iceFloesSpawned, 1);
                    Complete(zone);
                    continue;
                }
                candidates--;
                CandidateResult result = PlaceCandidate(current);
                if (result == CandidateResult.Deferred)
                    continue; // Keep the exact RNG/candidate cursor for the next load/approach event.
                current.Remaining--;
                if (result == CandidateResult.Placed)
                    instances--;
                ScheduleZone(zone);
            }
        }

        private static SpawnSystem FindOwnedSpawnSystem(Vector2s zone)
        {
            foreach (SpawnSystem spawn in SpawnSystem.m_instances)
                if (spawn && spawn.m_heightmap && spawn.m_nview && spawn.m_nview.IsValid() &&
                    spawn.m_nview.IsOwner() && ZoneSystem.GetZone(spawn.transform.position) == zone)
                    return spawn;
            return null;
        }

        private static bool OwnsLoadedZone(ZoneWork current) => SeasonState.IsActive && IsTimeForIceFloes() &&
            world.IsZoneLoaded(current.Zone) && current.SpawnSystem && current.SpawnSystem.m_heightmap &&
            current.SpawnSystem.m_nview && current.SpawnSystem.m_nview.IsValid() &&
            current.SpawnSystem.m_nview.IsOwner() && current.SpawnSystem.m_nview.GetZDO().m_uid == current.Control;

        private static ZoneWork BeginZone(Vector2s zone)
        {
            ZoneWork current = new ZoneWork { Zone = zone };
            visited.Clear();
            ZDOMan.instance.FindObjects(zone, current.Objects, visited);
            if (initialExclusions.TryGetValue(zone, out List<ZoneSystem.ClearArea> exclusions))
            {
                current.Exclusions.AddRange(exclusions);
                initialExclusions.Remove(zone);
            }
            else if (world.m_locationInstances.TryGetValue(zone, out ZoneSystem.LocationInstance location) &&
                location.m_placed && location.m_location != null && location.m_location.m_clearArea)
                current.Exclusions.Add(new ZoneSystem.ClearArea(location.m_position, location.m_location.m_exteriorRadius));
            return current;
        }

        private static bool InspectZone(ZoneWork current, bool remove, ref int budget, long deadline)
        {
            while (current.ObjectIndex < current.Objects.Count && budget > 0 && BeforeDeadline(deadline))
            {
                budget--;
                ZDO zdo = current.Objects[current.ObjectIndex++];
                if (zdo == null || !zdo.IsValid() || ZDOMan.instance.GetZDO(zdo.m_uid) != zdo ||
                    ZoneSystem.GetZone(zdo.GetPosition()) != current.Zone)
                    continue;
                if (zdo.GetPrefab() == zonePrefab)
                {
                    if (remove)
                        current.Control = zdo.m_uid;
                    if (remove)
                        ScheduleMarkerReset(zdo);
                }
                else if (zdo.GetPrefab() == s_iceFloePrefab && zdo.GetBool(SeasonsVars.s_iceFloeWatermark))
                {
                    if (remove)
                        ScheduleRemoval(zdo);
                    else
                    {
                        current.ExistingFloe = true;
                        if (zdo.IsOwner())
                            zdo.SetDistant(true);
                    }
                }
            }
            if (current.ObjectIndex != current.Objects.Count)
                return false;
            current.Objects.Clear();
            current.ObjectIndex = 0;
            return true;
        }

        private static void Complete(Vector2s zone)
        {
            work.Remove(zone);
            initialExclusions.Remove(zone);
            settled.Add(zone);
        }

        private static void ServiceCleanup(ref int objectBudget, long deadline)
        {
            if (!ServerReady() || (SeasonState.IsActive && IsTimeForIceFloes()))
                return;
            int zones = DiscoveryBudget;
            while (zones-- > 0 && objectBudget > 0 && BeforeDeadline(deadline))
            {
                if (cleanup == null)
                {
                    if (cleanupZones.Count == 0)
                        break;
                    cleanup = BeginZone(cleanupZones.Dequeue());
                }
                if (!InspectZone(cleanup, true, ref objectBudget, deadline))
                    break;
                cleanupSet.Remove(cleanup.Zone);
                cleanup = null;
            }
        }

        private static void ServiceRemovals(long deadline)
        {
            // Season/day overrides can invalidate queues between request and service.
            if (!ServerReady() || (SeasonState.IsActive && IsTimeForIceFloes()))
                return;
            for (int i = 0; i < RemovalBudget && removals.Count != 0 && BeforeDeadline(deadline); ++i)
            {
                ZDOID id = removals.Dequeue();
                removalSet.Remove(id);
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                if (zdo != null && zdo.GetPrefab() == s_iceFloePrefab && zdo.GetBool(SeasonsVars.s_iceFloeWatermark))
                    RemoveObject(zdo, force: true);
            }
            for (int i = 0; i < MarkerBudget && markerResets.Count != 0 && BeforeDeadline(deadline); ++i)
            {
                ZDOID id = markerResets.Dequeue();
                markerSet.Remove(id);
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                if (zdo != null && zdo.GetPrefab() == zonePrefab)
                    zdo.Set(SeasonsVars.s_iceFloesSpawned, 0, okForNotOwner: true);
            }
        }

        private static void InitializeRandom(ZoneWork current)
        {
            UnityEngine.Random.State previous = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(WorldGenerator.instance.GetSeed() + current.Zone.x * 4271 +
                    current.Zone.y * 9187 + s_iceFloePrefab + (SeasonState.IsActive ? seasonState.GetCurrentWorldDay() : 0));
                current.Remaining = UnityEngine.Random.Range((int)amountOfIceFloesInWinterDays.Value.x,
                    (int)amountOfIceFloesInWinterDays.Value.y + 1);
                current.RandomState = UnityEngine.Random.state;
                current.RandomReady = true;
            }
            finally
            {
                UnityEngine.Random.state = previous;
            }
        }

        private static CandidateResult PlaceCandidate(ZoneWork current)
        {
            UnityEngine.Random.State previous = UnityEngine.Random.state;
            UnityEngine.Random.state = current.RandomState;
            CandidateResult result = CandidateResult.Deferred;
            try
            {
                result = PlaceCandidateWithRandom(current);
                return result;
            }
            finally
            {
                // Missing placement data retries this same candidate; every yield restores vanilla RNG.
                if (result != CandidateResult.Deferred)
                    current.RandomState = UnityEngine.Random.state;
                UnityEngine.Random.state = previous;
            }
        }

        private static CandidateResult PlaceCandidateWithRandom(ZoneWork current)
        {
            Vector3 center = ZoneSystem.GetZonePos(current.Zone);
            float halfZone = world.m_zoneSize / 2f;
            Vector3 p = new Vector3(UnityEngine.Random.Range(center.x - halfZone, center.x + halfZone), 0f,
                UnityEngine.Random.Range(center.z - halfZone, center.z + halfZone));
            if (IsBeyondWorldEdge(p, 100f) || world.InsideClearArea(current.Exclusions, p) ||
                (s_iceFloe.m_blockCheck && world.IsBlocked(p)))
                return CandidateResult.Skipped;
            world.GetGroundData(ref p, out _, out Heightmap.Biome biome, out Heightmap.BiomeArea biomeArea, out Heightmap hmap);
            if (!hmap)
                return CandidateResult.Deferred;
            // Coastal zones can have a land center and eligible Ocean candidates.
            // Only the actual placement point determines biome eligibility.
            float altitude = p.y - world.m_waterLevel;
            if (altitude < s_iceFloe.m_minAltitude || altitude > s_iceFloe.m_maxAltitude ||
                (s_iceFloe.m_biome & biome) == 0 || (s_iceFloe.m_biomeArea & biomeArea) == 0)
                return CandidateResult.Skipped;
            float oceanDepth = hmap.GetOceanDepth(p);
            if (s_iceFloe.m_minOceanDepth != s_iceFloe.m_maxOceanDepth &&
                (oceanDepth < s_iceFloe.m_minOceanDepth || oceanDepth > s_iceFloe.m_maxOceanDepth))
                return CandidateResult.Skipped;
            Vector3 waterProbe = new Vector3(p.x, world.m_waterLevel, p.z);
            float water = Floating.GetLiquidLevel(waterProbe, type: LiquidType.Water);
            if (water <= -10000f || float.IsNaN(water) || float.IsInfinity(water))
                return CandidateResult.Deferred;

            float depthFactor = GetOceanDepthFactor(oceanDepth);
            float scaleX = UnityEngine.Random.Range(iceFloesScale.Value.x, iceFloesScale.Value.y) * depthFactor;
            float scaleY = PowSquash(UnityEngine.Random.Range(iceFloesScale.Value.x, iceFloesScale.Value.y), 0.6f);
            float scaleZ = UnityEngine.Random.Range(iceFloesScale.Value.x, iceFloesScale.Value.y) * depthFactor;
            float halfX = s_floeSize.x * scaleX / 2;
            float halfZ = s_floeSize.y * scaleZ / 2;
            float radius = Mathf.Sqrt(halfX * halfX + halfZ * halfZ) + 0.2f;
            foreach (ZoneSystem.ClearArea area in current.Exclusions)
                if (IsInside(area, p, radius))
                    return CandidateResult.Skipped;
            if (s_iceFloe.m_snapToWater)
                p.y = world.m_waterLevel - _winterWaterSurfaceOffset;

            GameObject instance = UnityEngine.Object.Instantiate(s_iceFloe.m_prefab, p,
                Quaternion.Euler(0, UnityEngine.Random.Range(0, 360), 0));
            ZNetView view = instance.GetComponent<ZNetView>();
            view.SetLocalScale(new Vector3(scaleX, scaleY, scaleZ));
            float health = iceFloesHealth.Value * scaleX * scaleY * scaleZ;
            ZDO zdo = view.GetZDO();
            zdo.Set(SeasonsVars.s_iceFloeWatermark, true);
            view.m_distant = true;
            zdo.SetDistant(true);
            zdo.Set(SeasonsVars.s_iceFloeMass, view.m_body.mass * PowSquash(Mathf.Sqrt(Mathf.Abs(scaleX * scaleY * scaleZ)), 0.6f));
            zdo.Set(ZDOVars.s_health, health + Game.m_worldLevel * health * Game.instance.m_worldLevelMineHPMultiplier);
            current.Exclusions.Add(new ZoneSystem.ClearArea(p, GetFloeSize(instance) + 0.5f));
            return CandidateResult.Placed;
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.PlaceZoneCtrl))]
        private static class ZoneSystem_PlaceZoneCtrl_Floes
        {
            private static void Postfix(Vector2s zoneID, ZoneSystem.SpawnMode mode)
            {
                if (mode != ZoneSystem.SpawnMode.Full || !PeerReady() || !SeasonState.IsActive || !IsTimeForIceFloes())
                    return;
                // Copy only the normal full-load exclusions; never retain the shared vanilla list.
                initialExclusions[zoneID] = new List<ZoneSystem.ClearArea>(world.m_tempClearAreas);
                ScheduleZone(zoneID);
            }
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
        private static class ZoneSystem_Start_Floes
        {
            private static void Postfix(ZoneSystem __instance) => InitializePrefab(__instance);
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.PokeLocalZone))]
        private static class ZoneSystem_PokeLocalZone_Floes
        {
            private static void Postfix(Vector2s zoneID, bool __result)
            {
                if (__result && PeerReady())
                    ScheduleZone(zoneID);
            }
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.UnsetLoadingInZone))]
        private static class ZoneSystem_UnsetLoadingInZone_Floes
        {
            private static void Postfix(ZDO zdo)
            {
                if (PeerReady() && world.IsZoneLoaded(zdo.GetSector()))
                    ScheduleZone(zdo.GetSector());
            }
        }

        // Ownership can arrive after the load callback. Native spawning already retries once a second.
        [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.UpdateSpawning))]
        private static class SpawnSystem_UpdateSpawning_Floes
        {
            private static void Postfix(SpawnSystem __instance)
            {
                if (PeerReady() && __instance.m_nview && __instance.m_nview.IsValid() && __instance.m_nview.IsOwner())
                    ScheduleZone(ZoneSystem.GetZone(__instance.transform.position));
            }
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Update))]
        private static class ZoneSystem_Update_Floes
        {
            private static void Postfix() => Update();
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.OnDestroy))]
        private static class ZoneSystem_OnDestroy_Floes
        {
            private static void Postfix() => Reset();
        }

        [HarmonyPatch(typeof(ZNetView), nameof(ZNetView.Awake))]
        private static class ZNetView_Awake_DistantFloes
        {
            [HarmonyPrefix, HarmonyPriority(Priority.First)]
            private static void Prefix(ZNetView __instance)
            {
                ZDO zdo = ZNetView.m_initZDO;
                if (zdo != null && zdo.GetPrefab() == s_iceFloePrefab && zdo.GetBool(SeasonsVars.s_iceFloeWatermark))
                    __instance.m_distant = true;
            }

            private static void Postfix(ZNetView __instance)
            {
                ZDO zdo = __instance.GetZDO();
                if (zdo == null || zdo.GetPrefab() != s_iceFloePrefab || !zdo.GetBool(SeasonsVars.s_iceFloeWatermark))
                    return;
                __instance.m_distant = true;
                if (__instance.IsOwner())
                    zdo.SetDistant(true);
                if (!__instance.TryGetComponent<IceFloeClimb>(out _))
                    __instance.gameObject.AddComponent<IceFloeClimb>();
            }
        }
    }
}
