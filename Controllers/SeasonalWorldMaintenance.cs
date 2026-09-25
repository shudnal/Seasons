using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;
using static Seasons.Seasons;
using static Seasons.ZoneSystemVariantController;

namespace Seasons
{
    /// <summary>Budgeted terrain maintenance; floe removal has its own one-pass lifecycle.</summary>
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
        private static bool requested, scanning, deferredTerrainScan;

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
            requested = scanning = deferredTerrainScan = false;
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
            if (s_terrainCompilerPrefab == 0)
                s_terrainCompilerPrefab = "_TerrainCompiler".GetStableHashCode();
            return sectors != null;
        }

        private static bool TerrainWanted => SeasonState.IsActive && IsTimeToDecultivateGround();
        private static bool TerrainReady => SeasonState.IsActive && ZNetScene.instance &&
            (ZNet.instance.IsDedicated() || !ZNetScene.instance.InLoadingScreen()) &&
            WorldGenerator.instance?.m_world?.m_biomeData?.IsReady == true;

        internal static void RequestWorldScan()
        {
            // Existing config/season callers retain one entry point, but floe cleanup
            // never shares the terrain cursor, readiness checks or operation budgets.
            SeasonalIceFloes.RequestCleanup();
            if (!EnsureWorld() || !ZNet.instance.IsServer() || !TerrainWanted)
                return;
            requested = true;
            deferredTerrainScan |= !TerrainReady;
        }

        public static string GetFloeCleanupStatus() => SeasonalIceFloes.GetCleanupStatus();

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
            if (Game.IsPaused() || Time.timeScale <= 0f || !ZNetScene.instance ||
                ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected || !TerrainReady)
                return;
            if (deferredTerrainScan)
            {
                requested |= ZNet.instance.IsServer() && TerrainWanted;
                deferredTerrainScan = false;
            }
            ProcessTerrain();
            if (!ZNet.instance.IsServer())
            {
                requested = scanning = false;
                currentSector = null;
                return;
            }
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
            long deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * DiscoveryMilliseconds / 1000d);
            int slots = SectorSlotsPerFrame;
            int objects = ObjectsPerFrame;
            while (sectorIndex < sectors.Length && objects > 0 && Stopwatch.GetTimestamp() < deadline)
            {
                if (currentSector == null)
                {
                    if (slots-- <= 0)
                        break;
                    currentSector = sectors[sectorIndex];
                    objectIndex = currentSector == null ? -1 : currentSector.Count - 1;
                }
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
                if (zdo != null && zdo.IsValid() && manager.GetZDO(zdo.m_uid) == zdo &&
                    zdo.GetPrefab() == s_terrainCompilerPrefab && !RequestTerrain(zdo))
                    break;
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
                TerrainDecultivation.ProcessQueuedTerrain(zdo);
            }
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Update))]
        private static class ZoneSystem_Update_Maintenance
        {
            private static void Postfix() => Update();
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
