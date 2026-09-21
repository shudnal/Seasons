using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using static Seasons.Seasons;
using static Seasons.ZoneSystemVariantController;

namespace Seasons
{
    /// <summary>Server-side seasonal vegetation, independent of proximity ownership.</summary>
    internal static class SeasonalIceFloes
    {
        private sealed class RegionScan
        {
            internal long Peer;
            internal Vector2s Center;
            internal int Radius;
            internal int Ring;
            internal int Edge;

            internal bool Next(out Vector2s zone)
            {
                zone = Center;
                if (Ring > Radius)
                    return false;
                if (Ring == 0)
                {
                    Ring = 1;
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
                    Ring++;
                }
                return true;
            }
        }

        private static readonly List<RegionScan> scans = new List<RegionScan>();
        private static readonly List<ZDO> objects = new List<ZDO>();
        private static readonly List<ZDO> floes = new List<ZDO>();
        private static readonly HashSet<ZoneSystem.SectorIndex> visited = new HashSet<ZoneSystem.SectorIndex>();
        private static readonly List<ZoneSystem.ClearArea> exclusions = new List<ZoneSystem.ClearArea>();
        private static readonly List<GameObject> spawned = new List<GameObject>();
        private static readonly Queue<Vector2s> retry = new Queue<Vector2s>();
        private static readonly HashSet<Vector2s> retrySet = new HashSet<Vector2s>();
        private static ZoneSystem world;
        private static float nextStep;
        private static float nextPeerCheck;
        private static int scanCursor;
        private static int zonePrefab;
        private static bool wanted;
        private static bool prefabChecked;
        private static bool serviceRetry;

        internal static void InitializePrefab(ZoneSystem instance)
        {
            prefabChecked = true;
            s_iceFloe = instance.m_vegetation.Find(veg => veg.m_prefab?.name == _iceFloeName)?.Clone();
            if (s_iceFloe?.m_prefab == null)
                return;
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
            objects.Clear();
            floes.Clear();
            visited.Clear();
            exclusions.Clear();
            spawned.Clear();
            retry.Clear();
            retrySet.Clear();
            world = null;
            nextStep = nextPeerCheck = 0f;
            scanCursor = zonePrefab = 0;
            wanted = prefabChecked = serviceRetry = false;
        }

        private static bool Ready()
        {
            if (!ZoneSystem.instance || !ZNet.instance || !ZNet.instance.IsServer() ||
                !ZNetScene.instance || ZDOMan.instance == null || !SeasonState.IsActive)
                return false;
            if (world != ZoneSystem.instance)
            {
                Reset();
                world = ZoneSystem.instance;
                zonePrefab = Utils.GetPrefabName(world.m_zoneCtrlPrefab).GetStableHashCode();
            }
            if (!prefabChecked && world.m_vegetation.Count != 0)
                InitializePrefab(world);
            return waterStateInitialized && s_waterEdge > 100f && s_iceFloe?.m_prefab != null;
        }

        // Clients consume normal replicated ZDOs, never create competing floes for owner zero.
        internal static bool CheckWaterVolume(WaterVolume water)
        {
            if (!water || !water.m_heightmap || !ZNet.instance || !ZNet.instance.IsServer())
                return true;
            if (!Ready())
                return false;
            Schedule(ZoneSystem.GetZone(water.transform.position));
            return true;
        }

        private static void Schedule(Vector2s zone)
        {
            if (retrySet.Add(zone))
                retry.Enqueue(zone);
        }

        private static void Observe(long peer, Vector3 position, int radius)
        {
            Vector2s center = ZoneSystem.GetZone(position);
            RegionScan scan = scans.Find(item => item.Peer == peer);
            if (scan == null)
            {
                scans.Add(new RegionScan { Peer = peer, Center = center, Radius = radius });
                return;
            }
            if (scan.Center == center && scan.Radius == radius)
                return;
            scan.Center = center;
            scan.Radius = radius;
            scan.Ring = scan.Edge = 0;
        }

        private static bool InScope(Vector2s zone)
        {
            foreach (RegionScan scan in scans)
                if (Math.Abs(zone.x - scan.Center.x) <= scan.Radius && Math.Abs(zone.y - scan.Center.y) <= scan.Radius &&
                    (world.m_simulationDistance.IsClassic || world.ZonesWithinRadius(scan.Center, zone, scan.Radius, ghostZone: true)))
                    return true;
            return false;
        }

        internal static void Update()
        {
            if (!Ready() || Game.IsPaused() || Time.timeScale <= 0f || Time.time < nextStep)
                return;
            nextStep = Time.time + 0.1f;
            bool nowWanted = IsTimeForIceFloes();
            if (wanted != nowWanted)
            {
                wanted = nowWanted;
                foreach (RegionScan scan in scans)
                    scan.Ring = scan.Edge = 0;
            }
            if (Time.time >= nextPeerCheck)
            {
                nextPeerCheck = Time.time + 0.5f;
                int radius = ZNet.instance.GetSyncedSimulationDistance().TotalSimulationDistance;
                List<ZNetPeer> peers = ZNet.instance.GetPeers();
                scans.RemoveAll(scan => scan.Peer != 0L && !peers.Exists(peer => peer.m_uid == scan.Peer && peer.IsReady()));
                if (!ZNet.instance.IsDedicated())
                    Observe(0L, ZNet.instance.GetReferencePosition(), radius);
                foreach (ZNetPeer peer in peers)
                    if (peer.IsReady())
                        Observe(peer.m_uid, peer.GetRefPos(), radius);
            }

            Vector2s zone = default;
            bool found = false;
            serviceRetry = !serviceRetry;
            if (serviceRetry && retry.Count != 0)
            {
                zone = retry.Dequeue();
                retrySet.Remove(zone);
                found = true;
            }
            if (!found)
                for (int i = 0; i < scans.Count; ++i)
                {
                    scanCursor %= scans.Count;
                    if (scans[scanCursor++].Next(out zone))
                    {
                        found = true;
                        break;
                    }
                }
            if (!found && retry.Count != 0)
            {
                zone = retry.Dequeue();
                retrySet.Remove(zone);
                found = true;
            }
            if (!found || !InScope(zone) || !world.IsZoneGenerated(zone))
                return;
            if (!Process(zone, ZoneSystem.SpawnMode.Ghost, null, terrainExists: world.IsZoneLoaded(zone)))
                Schedule(zone);
        }

        private static bool Process(Vector2s zone, ZoneSystem.SpawnMode mode,
            List<ZoneSystem.ClearArea> initialExclusions, bool terrainExists)
        {
            Vector3 center = ZoneSystem.GetZonePos(zone);
            if (WorldGenerator.instance.GetBiome(center) != Heightmap.Biome.Ocean)
                return true;
            objects.Clear();
            floes.Clear();
            visited.Clear();
            ZDOMan.instance.FindObjects(zone, objects, visited);
            ZDO control = null;
            foreach (ZDO zdo in objects)
            {
                if (zdo == null || ZoneSystem.GetZone(zdo.GetPosition()) != zone)
                    continue;
                if (zdo.GetPrefab() == zonePrefab)
                    control = zdo;
                else if (zdo.GetPrefab() == s_iceFloePrefab && zdo.GetBool(SeasonsVars.s_iceFloeWatermark))
                    floes.Add(zdo);
            }
            if (control == null)
                return false;
            if (!IsTimeForIceFloes())
            {
                control.Set(SeasonsVars.s_iceFloesSpawned, 0, okForNotOwner: true);
                foreach (ZDO floe in floes)
                    RemoveObject(floe, force: true);
                return true;
            }
            foreach (ZDO floe in floes)
                floe.SetDistant(true);
            if (floes.Count != 0 || control.GetBool(SeasonsVars.s_iceFloesSpawned))
            {
                control.Set(SeasonsVars.s_iceFloesSpawned, 1, okForNotOwner: true);
                return true;
            }

            GameObject temporaryTerrain = null;
            exclusions.Clear();
            if (initialExclusions != null)
                exclusions.AddRange(initialExclusions);
            spawned.Clear();
            try
            {
                // Existing ghost sectors have ZDOs but no terrain instance on the server.
                // Client mode loads terrain only; it never regenerates vanilla vegetation.
                if (!terrainExists && (world.m_zones.ContainsKey(zone) ||
                    !world.SpawnZone(zone, ZoneSystem.SpawnMode.Client, out temporaryTerrain)))
                    return false;
                PlaceIceFloes(zone, center, exclusions, mode, spawned);
                control.Set(SeasonsVars.s_iceFloesSpawned, 1, okForNotOwner: true);
                return true;
            }
            finally
            {
                if (mode == ZoneSystem.SpawnMode.Ghost)
                    foreach (GameObject instance in spawned)
                        if (instance)
                            UnityEngine.Object.Destroy(instance);
                if (temporaryTerrain)
                    UnityEngine.Object.Destroy(temporaryTerrain);
                spawned.Clear();
                exclusions.Clear();
            }
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.PlaceZoneCtrl))]
        private static class ZoneSystem_PlaceZoneCtrl_Floes
        {
            private static void Postfix(Vector2s zoneID, ZoneSystem.SpawnMode mode)
            {
                if (Ready() && IsTimeForIceFloes() &&
                    (mode == ZoneSystem.SpawnMode.Ghost || mode == ZoneSystem.SpawnMode.Full))
                    Process(zoneID, mode, world.m_tempClearAreas, terrainExists: true);
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
        }
    }
}
