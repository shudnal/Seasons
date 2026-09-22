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
        }

        private static bool EnsureWorld()
        {
            if (!ZoneSystem.instance || !ZNet.instance || ZDOMan.instance == null || !SeasonState.IsActive)
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

        internal static void RequestWorldScan()
        {
            if (EnsureWorld() && ZNet.instance.IsServer() && (!IsTimeForIceFloes() || IsTimeToDecultivateGround()))
                requested = true; // Requests during a pass coalesce into one following pass.
        }

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
            && IsTimeToDecultivateGround() && (zdo.IsOwner() || (!zdo.HasOwner() && ZNet.instance.IsServer()))
            && TerrainDecultivation.IsDecultivationDue(zdo, seasonState.GetCurrentWorldDay(), seasonState.GetYearLengthInDays());

        private static void Update()
        {
            if (!EnsureWorld())
            {
                Reset();
                return;
            }
            if (Game.IsPaused() || Time.timeScale <= 0f || !ZNetScene.instance
                || ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected
                || (!ZNet.instance.IsDedicated() && ZNetScene.instance.InLoadingScreen())
                || WorldGenerator.instance?.m_world?.m_biomeData?.IsReady != true)
                return;
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
                    if (!IsTimeForIceFloes())
                    {
                        if (zdo.GetPrefab() == s_iceFloePrefab && zdo.GetBool(SeasonsVars.s_iceFloeWatermark))
                            SeasonalIceFloes.ScheduleRemoval(zdo);
                        else if (zdo.GetPrefab() == s_zoneCtrlPrefab && zdo.GetBool(SeasonsVars.s_iceFloesSpawned))
                            SeasonalIceFloes.ScheduleMarkerReset(zdo);
                    }
                    if (zdo.GetPrefab() == s_terrainCompilerPrefab && !RequestTerrain(zdo))
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
                if (!scanning || manager != __instance || !ZNet.instance || !ZNet.instance.IsServer()
                    || !SeasonState.IsActive || IsTimeForIceFloes() || zdo == null || !zdo.IsValid()
                    || __instance.GetZDO(zdo.m_uid) != zdo || zdo.GetPrefab() != s_iceFloePrefab
                    || !zdo.GetBool(SeasonsVars.s_iceFloeWatermark))
                    return;
                SeasonalIceFloes.ScheduleRemoval(zdo);
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
