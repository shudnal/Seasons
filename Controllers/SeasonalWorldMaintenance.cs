using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;
using static Seasons.Seasons;
using static Seasons.ZoneSystemVariantController;

namespace Seasons
{
    /// <summary>Requests seasonal world maintenance without taking a world-sized ZDO snapshot.</summary>
    internal static class SeasonalWorldMaintenance
    {
        private const int SectorSlotsPerFrame = 1024;
        private const int ObjectsPerFrame = 128;
        private const double DiscoveryMilliseconds = 1.0;
        private const int PendingTerrainLimit = 128;
        private const int TerrainQueueChecksPerFrame = 8;
        private const int TerrainOperationsPerFrame = 1;
        private const double TerrainMilliseconds = 0.5;

        private static readonly Queue<ZDOID> terrainRequests = new Queue<ZDOID>();
        private static readonly HashSet<ZDOID> terrainSet = new HashSet<ZDOID>();
        private static ZDOMan manager;
        private static ZoneSystem world;
        private static List<ZDO>[] sectors;
        private static List<ZDO> currentSector;
        private static int sectorIndex;
        private static int objectIndex = -1;
        private static bool requested;
        private static bool scanning;
        private static bool cleanupPolicyKnown, previousCleanupRequired, loadedCleanupRequested;
        private static bool deferredTerrainScan;
        private static readonly Queue<Vector2s> priorityCleanupZones = new Queue<Vector2s>();
        private static readonly HashSet<Vector2s> priorityCleanupZoneSet = new HashSet<Vector2s>();
        private static List<ZDO> priorityCleanupSector;
        private static int priorityCleanupIndex = -1;
        private static int lastLoadedFloesQueued, lastLoadedMarkersQueued;

        internal static void Reset()
        {
            terrainRequests.Clear();
            terrainSet.Clear();
            manager = null;
            world = null;
            sectors = null;
            currentSector = null;
            sectorIndex = 0;
            objectIndex = -1;
            requested = scanning = false;
            cleanupPolicyKnown = previousCleanupRequired = loadedCleanupRequested = deferredTerrainScan = false;
            ClearPriorityCleanup();
            lastLoadedFloesQueued = lastLoadedMarkersQueued = 0;
        }

        private static bool EnsureWorld()
        {
            if (!ZoneSystem.instance || !ZNet.instance || ZDOMan.instance == null)
                return false;
            if (manager != ZDOMan.instance || world != ZoneSystem.instance || sectors != ZDOMan.instance.m_objectsBySector)
            {
                Reset();
                manager = ZDOMan.instance;
                world = ZoneSystem.instance;
                sectors = manager.m_objectsBySector;
            }
            if (s_zoneCtrlPrefab == 0)
                s_zoneCtrlPrefab = Utils.GetPrefabName(world.m_zoneCtrlPrefab).GetStableHashCode();
            if (s_terrainCompilerPrefab == 0)
                s_terrainCompilerPrefab = "_TerrainCompiler".GetStableHashCode();
            return sectors != null;
        }

        // An absent season during startup is not evidence that winter has ended. An
        // explicit feature disable, however, is sufficient even before season setup.
        private static bool CleanupPolicyReady => enableIceFloes != null &&
            (!enableIceFloes.Value || SeasonState.IsActive);
        private static bool CleanupRequired => CleanupPolicyReady &&
            (!enableIceFloes.Value || !IsTimeForIceFloes());
        private static bool TerrainWanted => SeasonState.IsActive && IsTimeToDecultivateGround();
        private static bool TerrainReady => SeasonState.IsActive && ZNetScene.instance &&
            (ZNet.instance.IsDedicated() || !ZNetScene.instance.InLoadingScreen()) &&
            WorldGenerator.instance?.m_world?.m_biomeData?.IsReady == true;

        internal static void RequestWorldScan()
        {
            if (!EnsureWorld() || !ZNet.instance.IsServer())
                return;
            if (CleanupRequired)
            {
                requested = true;
                loadedCleanupRequested = true;
            }
            if (TerrainWanted)
            {
                requested = true; // Requests during a pass coalesce into one following pass.
                deferredTerrainScan |= !TerrainReady;
            }
        }

        private static void ObserveCleanupPolicy()
        {
            if (!CleanupPolicyReady)
                return;
            bool cleanup = CleanupRequired;
            if (cleanupPolicyKnown && previousCleanupRequired == cleanup)
                return;
            cleanupPolicyKnown = true;
            previousCleanupRequired = cleanup;
            ClearPriorityCleanup();
            loadedCleanupRequested = cleanup;
            if (cleanup)
                requested = true; // Includes loading an old winter world with floes disabled.
        }

        private static void ClearPriorityCleanup()
        {
            priorityCleanupZones.Clear();
            priorityCleanupZoneSet.Clear();
            priorityCleanupSector = null;
            priorityCleanupIndex = -1;
        }

        private static bool IsLiveZdo(ZDO zdo) => zdo != null && zdo.IsValid() &&
            manager.GetZDO(zdo.m_uid) == zdo;

        private static void ScheduleCleanup(ZDO zdo, bool prioritizeZone)
        {
            if (!IsLiveZdo(zdo))
                return;
            int prefab = zdo.GetPrefab();
            if (prefab == s_iceFloePrefab && zdo.GetBool(SeasonsVars.s_iceFloeWatermark))
            {
                SeasonalIceFloes.ScheduleRemoval(zdo);
                if (prioritizeZone && priorityCleanupZoneSet.Add(zdo.GetSector()))
                    priorityCleanupZones.Enqueue(zdo.GetSector());
            }
            else if (prefab == s_zoneCtrlPrefab && zdo.GetBool(SeasonsVars.s_iceFloesSpawned))
                SeasonalIceFloes.ScheduleMarkerReset(zdo);
        }

        private static void QueueLoadedCleanup()
        {
            loadedCleanupRequested = false;
            lastLoadedFloesQueued = lastLoadedMarkersQueued = 0;
            // A transition-only walk of already instantiated objects, NOT all world ZDOs.
            // Only enqueue IDs here: destroying an instance would mutate this dictionary.
            // Visible floes must not wait behind a million-object background scan.
            foreach (KeyValuePair<ZDO, ZNetView> entry in ZNetScene.instance.m_instances)
            {
                ZDO zdo = entry.Key;
                if (!entry.Value || !IsLiveZdo(zdo))
                    continue;
                if (zdo.GetPrefab() == s_iceFloePrefab && zdo.GetBool(SeasonsVars.s_iceFloeWatermark))
                    lastLoadedFloesQueued++;
                else if (zdo.GetPrefab() == s_zoneCtrlPrefab && zdo.GetBool(SeasonsVars.s_iceFloesSpawned))
                    lastLoadedMarkersQueued++;
                else
                    continue;
                ScheduleCleanup(zdo, prioritizeZone: true);
            }
        }

        private static void DiscoverPriorityCleanup(ref int objects, long deadline)
        {
            // Distant floes can be instantiated without their zone controller. Find its
            // persistent spawn marker in the resident sector before the global cursor
            // reaches it, so off/on can populate the visible ocean again.
            while (priorityCleanupZones.Count != 0 && objects > 0 && Stopwatch.GetTimestamp() < deadline)
            {
                if (priorityCleanupSector == null)
                {
                    Vector2s zone = priorityCleanupZones.Peek();
                    priorityCleanupSector = sectors[ZoneSystem.SectorToIndex(zone).Sector];
                    priorityCleanupIndex = priorityCleanupSector == null ? -1 : priorityCleanupSector.Count - 1;
                    objects--; // Empty sectors also consume the discovery budget.
                }
                if (priorityCleanupSector != null)
                    priorityCleanupIndex = Math.Min(priorityCleanupIndex, priorityCleanupSector.Count - 1);
                if (priorityCleanupIndex < 0)
                {
                    priorityCleanupZones.Dequeue();
                    priorityCleanupSector = null;
                    continue;
                }
                if (objects <= 0)
                    return;
                objects--;
                ScheduleCleanup(priorityCleanupSector[priorityCleanupIndex--], prioritizeZone: false);
            }
        }

        // Runtime-inspector action; does not request work, change settings or delete objects.
        public static string GetFloeCleanupStatus() =>
            $"server={(ZNet.instance && ZNet.instance.IsServer())} cleanupRequired={CleanupRequired} " +
            $"loadedSweepPending={loadedCleanupRequested} loadedFloesQueued={lastLoadedFloesQueued} " +
            $"loadedMarkersQueued={lastLoadedMarkersQueued} priorityZones={priorityCleanupZones.Count} " +
            $"worldScanRequested={requested} worldScanning={scanning} sector={sectorIndex}/{sectors?.Length ?? 0}";

        // False means backpressure: discovery retains its cursor, loaded compilers retry next Update.
        internal static bool RequestTerrain(ZDO zdo)
        {
            if (!EnsureWorld() || !CanProcessTerrain(zdo))
                return true;
            if (terrainSet.Contains(zdo.m_uid))
                return true;
            if (terrainRequests.Count >= PendingTerrainLimit)
                return false;
            terrainSet.Add(zdo.m_uid);
            terrainRequests.Enqueue(zdo.m_uid);
            return true;
        }

        private static bool CanProcessTerrain(ZDO zdo) => zdo != null && zdo.IsValid()
            && manager.GetZDO(zdo.m_uid) == zdo && zdo.GetPrefab() == s_terrainCompilerPrefab
            && TerrainWanted && (zdo.IsOwner() || (!zdo.HasOwner() && ZNet.instance.IsServer()))
            && TerrainDecultivation.IsDecultivationDue(zdo, seasonState.GetCurrentWorldDay(), seasonState.GetYearLengthInDays());

        private static void Update()
        {
            if (!EnsureWorld())
            {
                Reset();
                return;
            }
            if (Game.IsPaused() || Time.timeScale <= 0f || !ZNetScene.instance
                || ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected)
                return;
            // Floe removal only needs live ZDOs. Terrain loading and biome-generation
            // readiness must never prevent disabling floes or removing expired ones.
            bool terrainReady = TerrainReady;
            if (terrainReady)
                ProcessTerrain();
            if (!ZNet.instance.IsServer())
            {
                requested = scanning = false;
                currentSector = null;
                return;
            }
            ObserveCleanupPolicy();
            bool cleanup = CleanupRequired;
            if (cleanup && loadedCleanupRequested)
                QueueLoadedCleanup();
            if (!TerrainWanted)
                deferredTerrainScan = false;
            else if (!terrainReady)
                deferredTerrainScan = true;
            else if (deferredTerrainScan)
            {
                requested = true;
                deferredTerrainScan = false;
            }
            if (!cleanup && !TerrainWanted)
            {
                requested = scanning = false;
                currentSector = null;
                return;
            }

            long deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * DiscoveryMilliseconds / 1000d);
            int slots = SectorSlotsPerFrame;
            int objects = ObjectsPerFrame;
            if (cleanup)
                DiscoverPriorityCleanup(ref objects, deadline);
            if (!cleanup && !terrainReady)
                return; // Defer terrain-only work without blocking a cleanup pass.
            if (!scanning && requested)
            {
                requested = false;
                scanning = true;
                sectorIndex = 0;
                objectIndex = -1;
                currentSector = null;
            }
            if (!scanning)
                return;

            while (sectorIndex < sectors.Length && objects > 0 && Stopwatch.GetTimestamp() < deadline)
            {
                if (currentSector == null)
                {
                    if (slots-- <= 0)
                        break;
                    currentSector = sectors[sectorIndex];
                    objectIndex = currentSector == null ? -1 : currentSector.Count - 1;
                }
                // Native sector mutations append or List.Remove. Walking backward tolerates
                // removals before the cursor; a repeated ID is harmless to the coalesced queues.
                if (currentSector != null)
                    objectIndex = Math.Min(objectIndex, currentSector.Count - 1);
                if (objectIndex < 0)
                {
                    ++sectorIndex;
                    currentSector = null;
                    continue;
                }
                --objects;
                ZDO zdo = currentSector[objectIndex];
                if (zdo != null && zdo.IsValid() && manager.GetZDO(zdo.m_uid) == zdo)
                {
                    if (cleanup)
                        ScheduleCleanup(zdo, prioritizeZone: false);
                    if (terrainReady && zdo.GetPrefab() == s_terrainCompilerPrefab && !RequestTerrain(zdo))
                        break;
                }
                --objectIndex;
            }
            if (sectorIndex >= sectors.Length)
            {
                scanning = false;
                currentSector = null;
            }
        }

        private static void ProcessTerrain()
        {
            long deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * TerrainMilliseconds / 1000d);
            int checks = TerrainQueueChecksPerFrame;
            int operations = TerrainOperationsPerFrame;
            while (terrainRequests.Count != 0 && checks-- > 0 && operations > 0 && Stopwatch.GetTimestamp() < deadline)
            {
                ZDOID id = terrainRequests.Dequeue();
                terrainSet.Remove(id);
                ZDO zdo = manager.GetZDO(id);
                if (!CanProcessTerrain(zdo))
                    continue;
                --operations;
                // Decompression, paint processing, compression and native CheckLoad form
                // one indivisible operation; a time guard cannot interrupt that call.
                TerrainDecultivation.ProcessQueuedTerrain(zdo);
            }
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Update))]
        private static class ZoneSystem_Update_Maintenance
        {
            private static void Postfix() => Update();
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.AddToSector))]
        private static class ZDOMan_AddToSector_MovingFloeCleanup
        {
            private static void Postfix(ZDOMan __instance, ZDO zdo)
            {
                // An existing floe can move behind the discovery cursor. New ZDOs call
                // AddToSector before initialization/map insertion and are deliberately excluded.
                if (manager != __instance || !ZNet.instance || !ZNet.instance.IsServer()
                    || !CleanupRequired || zdo == null || !zdo.IsValid()
                    || __instance.GetZDO(zdo.m_uid) != zdo || zdo.GetPrefab() != s_iceFloePrefab
                    || !zdo.GetBool(SeasonsVars.s_iceFloeWatermark))
                    return;
                SeasonalIceFloes.ScheduleRemoval(zdo);
            }
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.AddInstance))]
        private static class ZNetScene_AddInstance_ExpiredFloeCleanup
        {
            private static void Postfix(ZDO zdo)
            {
                if (!ZNet.instance || !ZNet.instance.IsServer() || !CleanupRequired || !EnsureWorld())
                    return;
                // Initialization may be in progress. Schedule only; normal service destroys
                // marked floes later and rechecks the current season before each batch.
                ScheduleCleanup(zdo, prioritizeZone: true);
            }
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ShutDown))]
        private static class ZDOMan_ShutDown_Maintenance
        {
            private static void Prefix() => Reset();
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.OnDestroy))]
        private static class ZoneSystem_OnDestroy_Maintenance
        {
            private static void Prefix() => Reset();
        }
    }
}
