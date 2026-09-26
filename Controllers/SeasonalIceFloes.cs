using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;
using static Seasons.Seasons;
using static Seasons.ZoneSystemVariantController;

namespace Seasons
{
    /// <summary>Budgeted local placement and one-pass server-only seasonal removal.</summary>
    internal static class SeasonalIceFloes
    {
        // Placement is budgeted; explicit seasonal cleanup is deliberately not sliced.
        private const int RequestBudget = 8;
        private const int ObjectBudget = 64;
        private const int CandidateBudget = 4;
        private const int InstanceBudget = 1;
        private const double ServiceMilliseconds = 1.5;
        private const float PendingRetrySeconds = 0.5f;

        // Read native sector lists in place. No full-sector snapshot is hidden in a work item.
        private sealed class SectorCursor
        {
            private List<ZDO> source;
            private int stage;
            private int index;
            private bool started;

            internal void Reset()
            {
                source = null;
                stage = index = 0;
                started = false;
            }

            internal bool Next(Vector2s zone, out ZDO zdo)
            {
                ZoneSystem.SectorIndex sector = ZoneSystem.SectorToIndex(zone);
                while (stage < 2)
                {
                    List<ZDO> current;
                    if (stage == 0)
                        current = ZDOMan.instance.m_objectsBySector[sector.Sector];
                    else
                        ZDOMan.instance.m_portalObjects.TryGetValue(sector, out current);
                    if (!started || !ReferenceEquals(current, source))
                    {
                        source = current;
                        index = current == null ? -1 : current.Count - 1;
                        started = true;
                    }
                    if (source != null)
                        index = Math.Min(index, source.Count - 1);
                    if (index >= 0)
                    {
                        // Native mutations append or List.Remove. Backward traversal tolerates
                        // removals; AddToSector invalidates pending inspections on append.
                        // This is not a cross-peer placement transaction.
                        zdo = source[index--];
                        return true;
                    }
                    stage++;
                    started = false;
                    source = null;
                }
                zdo = null;
                return false;
            }
        }

        private sealed class ZoneWork
        {
            internal Vector2s Zone;
            internal ZDOID Control;
            internal Heightmap Terrain;
            internal readonly SectorCursor Cursor = new SectorCursor();
            internal readonly List<ZoneSystem.ClearArea> Exclusions = new List<ZoneSystem.ClearArea>();
            internal readonly HashSet<ZDOID> Created = new HashSet<ZDOID>();
            internal bool Inspected, StaticMissing, Ghost, RandomReady;
            internal int Remaining;
            internal float NextAttempt;
            internal UnityEngine.Random.State RandomState;
        }

        private enum CandidateResult { Skipped, Placed, Deferred }

        private static readonly Queue<Vector2s> requests = new Queue<Vector2s>();
        private static readonly HashSet<Vector2s> requestSet = new HashSet<Vector2s>();
        private static readonly Dictionary<Vector2s, ZoneWork> work = new Dictionary<Vector2s, ZoneWork>();
        private static readonly Dictionary<Vector2s, ZDOID> controllers = new Dictionary<Vector2s, ZDOID>();
        private static readonly Dictionary<Vector2s, List<ZoneSystem.ClearArea>> initialExclusions = new Dictionary<Vector2s, List<ZoneSystem.ClearArea>>();
        private static readonly HashSet<Vector2s> settled = new HashSet<Vector2s>();
        // Temporary selection buffers, emptied in the same call. Never a multi-frame queue.
        private static readonly List<ZDOID> cleanupFloes = new List<ZDOID>();
        private static readonly List<ZDOID> cleanupMarkers = new List<ZDOID>();
        private static bool cleanupRequested, cleanupRunning, policyKnown;
        private static int cleanupPasses, lastRemovedFloes, lastResetMarkers;
        private static IEnumerator<Vector2s> loadedZones;
        private static ZoneSystem world;
        private static int zonePrefab;
        private static bool wanted, prefabChecked;
        // AddToSector runs before a new ZDO's prefab is initialized. Delay only this
        // candidate's matching-zone notifications until its exact ZDO is known.
        private static ZoneWork creatingFloe;
        private static ZDO creationSectorZdo;
        private static bool creationHasOtherAdds;

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
            loadedZones?.Dispose();
            loadedZones = null;
            requests.Clear();
            requestSet.Clear();
            work.Clear();
            controllers.Clear();
            initialExclusions.Clear();
            settled.Clear();
            cleanupFloes.Clear();
            cleanupMarkers.Clear();
            cleanupRequested = cleanupRunning = policyKnown = false;
            cleanupPasses = lastRemovedFloes = lastResetMarkers = 0;
            world = null;
            zonePrefab = 0;
            wanted = prefabChecked = false;
            creatingFloe = null;
            creationSectorZdo = null;
            creationHasOtherAdds = false;
            s_iceFloe = null;
        }

        private static bool PeerReady()
        {
            if (!ZoneSystem.instance || !ZNet.instance || !ZNetScene.instance || ZDOMan.instance == null ||
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

        private static bool PlacementSeason() => SeasonState.IsActive && IsTimeForIceFloes();

        private static bool PlacementReady()
        {
            if (!prefabChecked && world.m_vegetation.Count != 0)
                InitializePrefab(world);
            return !ZNet.instance.IsDedicated() && WorldGenerator.instance != null &&
                waterStateInitialized && s_waterEdge > 100f && s_iceFloe?.m_prefab != null;
        }

        internal static bool CheckWaterVolume(WaterVolume water)
        {
            if (!water || !water.m_heightmap || !ZNet.instance || ZNet.instance.IsDedicated() || !PlacementSeason())
                return true;
            if (!PeerReady())
                return false;
            RequestZone(ZoneSystem.GetZone(water.transform.position));
            return true;
        }

        private static bool ValidInZone(ZDO zdo, Vector2s zone) => zdo != null && zdo.IsValid() &&
            ZDOMan.instance.GetZDO(zdo.m_uid) == zdo && ZoneSystem.GetZone(zdo.GetPosition()) == zone;

        private static ZDO CachedControl(Vector2s zone)
        {
            if (!controllers.TryGetValue(zone, out ZDOID id))
                return null;
            ZDO zdo = ZDOMan.instance.GetZDO(id);
            if (ValidInZone(zdo, zone) && zdo.GetPrefab() == zonePrefab)
                return zdo;
            controllers.Remove(zone);
            work.Remove(zone); // Reacquisition must rebuild the missing controller's static inputs.
            return null;
        }

        private static bool AllowedControl(ZDO zdo) => zdo != null && (!zdo.HasOwner() || zdo.IsOwner());

        private static void RequestZone(Vector2s zone)
        {
            // Summer has no client placement; only the server deletes seasonal objects.
            if (!PlacementSeason() || ZNet.instance.IsDedicated() || settled.Contains(zone) || !world.m_zones.ContainsKey(zone))
                return;
            ZDO control = CachedControl(zone);
            if (control != null && control.GetBool(SeasonsVars.s_iceFloesSpawned))
            {
                StopZone(zone);
                return; // Check the persistent completion marker before creating or inspecting work.
            }
            if (control != null && !AllowedControl(control))
                return;
            if (work.TryGetValue(zone, out ZoneWork current))
            {
                current.NextAttempt = 0f;
                if (!requestSet.Contains(zone))
                    RestartInspection(current);
            }
            Queue(zone);
        }

        private static void Queue(Vector2s zone)
        {
            if (requestSet.Add(zone))
                requests.Enqueue(zone);
        }

        private static void ObserveControl(ZDO zdo)
        {
            if (!PeerReady() || ZNet.instance.IsDedicated() || zdo == null || zdo.GetPrefab() != zonePrefab || !zdo.IsValid())
                return;
            Vector2s zone = ZoneSystem.GetZone(zdo.GetPosition());
            if (!world.m_zones.ContainsKey(zone))
                return;
            controllers[zone] = zdo.m_uid;
            RequestZone(zone);
        }

        private static bool CleanupPolicyReady => enableIceFloes != null &&
            (!enableIceFloes.Value || SeasonState.IsActive);
        private static bool CleanupRequired => CleanupPolicyReady && !PlacementSeason();

        // Called by config/season updates. Coalesce callbacks until the next main-thread
        // update, then complete the whole operation, including unloaded ZDOs, in one call.
        internal static void RequestCleanup()
        {
            if (PeerReady() && ZNet.instance.IsServer() && CleanupRequired && !cleanupRunning)
                cleanupRequested = true;
        }

        public static string GetCleanupStatus() =>
            $"server={(ZNet.instance && ZNet.instance.IsServer())} cleanupRequired={CleanupRequired} " +
            $"pending={cleanupRequested} running={cleanupRunning} passes={cleanupPasses} " +
            $"lastRemovedFloes={lastRemovedFloes} lastResetMarkers={lastResetMarkers}";

        private static void CleanupAll()
        {
            if (cleanupRunning || !ZNet.instance.IsServer() || !CleanupRequired)
                return;
            cleanupRequested = false;
            cleanupRunning = true;
            cleanupFloes.Clear();
            cleanupMarkers.Clear();
            lastRemovedFloes = lastResetMarkers = 0;
            ZDOMan manager = ZDOMan.instance;
            try
            {
                // Enumerate once, select only our records. Native destruction removes dictionary
                // entries and can release pooled ZDOs, so do not destroy inside this enumeration.
                foreach (KeyValuePair<ZDOID, ZDO> entry in manager.m_objectsByID)
                {
                    ZDO zdo = entry.Value;
                    if (zdo == null || !zdo.IsValid())
                        continue;
                    int prefab = zdo.GetPrefab();
                    if (prefab == s_iceFloePrefab && zdo.GetBool(SeasonsVars.s_iceFloeWatermark))
                        cleanupFloes.Add(entry.Key);
                    else if (prefab == zonePrefab && zdo.GetBool(SeasonsVars.s_iceFloesSpawned))
                        cleanupMarkers.Add(entry.Key);
                }
                foreach (ZDOID id in cleanupMarkers)
                {
                    ZDO zdo = manager.GetZDO(id);
                    if (zdo == null || !zdo.IsValid() || zdo.GetPrefab() != zonePrefab)
                        continue;
                    zdo.Set(SeasonsVars.s_iceFloesSpawned, 0, okForNotOwner: true);
                    lastResetMarkers++;
                }
                foreach (ZDOID id in cleanupFloes)
                {
                    ZDO zdo = manager.GetZDO(id);
                    if (zdo == null || !zdo.IsValid() || zdo.GetPrefab() != s_iceFloePrefab ||
                        !zdo.GetBool(SeasonsVars.s_iceFloeWatermark))
                        continue;
                    RemoveObject(zdo, force: true);
                    lastRemovedFloes++;
                }
                // Flush native destruction once for the whole batch. Do not manually remove
                // dictionary entries or bypass dead-ZDO tracking and network notifications.
                if (lastRemovedFloes != 0)
                    manager.SendDestroyed();
                cleanupPasses++;
            }
            finally
            {
                cleanupFloes.Clear();
                cleanupMarkers.Clear();
                cleanupRunning = false;
            }
        }

        private static bool BeforeDeadline(long deadline) => Stopwatch.GetTimestamp() < deadline;

        private static void DiscoverLoadedZones(long deadline)
        {
            if (loadedZones == null)
                return;
            for (int i = 0; i < RequestBudget && BeforeDeadline(deadline); i++)
            {
                try
                {
                    if (loadedZones.MoveNext())
                    {
                        RequestZone(loadedZones.Current);
                        continue;
                    }
                    loadedZones.Dispose();
                    loadedZones = null;
                    return;
                }
                catch (InvalidOperationException)
                {
                    // Normal zone loading can change the dictionary between frames. Restart
                    // this one-off winter-entry walk; coalesced/settled zones stay cheap.
                    loadedZones.Dispose();
                    loadedZones = world.m_zones.Keys.GetEnumerator();
                    return;
                }
            }
        }

        internal static void Update()
        {
            if (!PeerReady() || !CleanupPolicyReady)
                return;
            bool nowWanted = PlacementSeason();
            if (!policyKnown || wanted != nowWanted)
            {
                policyKnown = true;
                wanted = nowWanted;
                loadedZones?.Dispose();
                loadedZones = null;
                work.Clear();
                settled.Clear();
                requests.Clear();
                requestSet.Clear();
                if (!wanted)
                    initialExclusions.Clear();
                cleanupRequested = !wanted && ZNet.instance.IsServer();
                if (wanted && !ZNet.instance.IsDedicated())
                    loadedZones = world.m_zones.Keys.GetEnumerator();
            }
            // Removing disabled floes does not depend on biome readiness, placement budgets,
            // a terrain-loading screen or simulation time advancing in the config menu.
            if (cleanupRequested && !wanted)
                CleanupAll();
            if (!wanted || ZNet.instance.IsDedicated() || Game.IsPaused() || Time.timeScale <= 0f)
                return;
            long deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * ServiceMilliseconds / 1000d);
            DiscoverLoadedZones(deadline);
            if (!PlacementReady())
                return;

            int objects = ObjectBudget, candidates = CandidateBudget, instances = InstanceBudget;
            int attempts = Math.Min(RequestBudget, requests.Count);
            while (attempts-- > 0 && candidates > 0 && instances > 0 && BeforeDeadline(deadline))
            {
                Vector2s zone = requests.Dequeue();
                requestSet.Remove(zone);
                if (!world.m_zones.ContainsKey(zone))
                {
                    ForgetZone(zone);
                    continue;
                }
                if (settled.Contains(zone))
                    continue;
                ZDO control = CachedControl(zone);
                if (control != null && control.GetBool(SeasonsVars.s_iceFloesSpawned))
                {
                    StopZone(zone);
                    continue;
                }
                if (control != null && !AllowedControl(control))
                    continue; // No ownership request. SetOwnerInternal/load events can retry later.
                if (!work.TryGetValue(zone, out ZoneWork current) ||
                    (control != null && current.Control != ZDOID.None && current.Control != control.m_uid))
                {
                    current = new ZoneWork { Zone = zone };
                    work[zone] = current;
                }
                if (Time.time < current.NextAttempt)
                {
                    Queue(zone);
                    continue;
                }
                if (!ReadyGeometry(current))
                {
                    Defer(current);
                    continue;
                }
                // Ordinary Heightmap.GetBiome selects from these corner biomes. Settle
                // pure land locally before any sector discovery; mixed coasts still qualify.
                if (!current.Terrain.HaveBiome(Heightmap.Biome.Ocean))
                {
                    StopZone(zone);
                    continue;
                }
                if (control == null && !FindControl(current, ref objects, deadline, out control))
                    continue;
                if (control.GetBool(SeasonsVars.s_iceFloesSpawned))
                {
                    StopZone(zone);
                    continue;
                }
                if (!AllowedControl(control))
                    continue;
                if (current.Control != control.m_uid)
                {
                    current.Control = control.m_uid;
                    RestartInspection(current);
                }
                try
                {
                    if (!current.Inspected && !InspectPlacement(current, ref objects, deadline))
                        continue;
                    // Marker, season, static loading and foreign ownership can change between slices.
                    if (!PlacementSeason() || !AllowedControl(control) || !ValidInZone(control, zone) ||
                        world.m_loadingObjectsInZones.ContainsKey(zone))
                    {
                        Defer(current, inspectAgain: true);
                        continue;
                    }
                    if (!current.RandomReady)
                        InitializeRandom(current);
                    if (current.Remaining <= 0)
                    {
                        // The integer overload updates DataRevision, ClientChanged and dirty-sector
                        // state for owner zero too. Normal replication sends it without SetOwner.
                        if (!control.GetBool(SeasonsVars.s_iceFloesSpawned))
                            control.Set(SeasonsVars.s_iceFloesSpawned, 1, okForNotOwner: true);
                        StopZone(zone);
                        continue;
                    }
                    candidates--;
                    CandidateResult result = PlaceCandidate(current);
                    if (result == CandidateResult.Deferred)
                    {
                        Defer(current, inspectAgain: true);
                        continue;
                    }
                    current.Remaining--;
                    if (result == CandidateResult.Placed)
                        instances--;
                    Queue(zone);
                }
                catch (Exception exception)
                {
                    // Any already created floe remains watermarked. Do not retry a failed partial
                    // spawn locally or publish a completed marker for it.
                    StopZone(zone);
                    LogWarning($"Seasonal ice floe placement stopped in {zone}: {exception}");
                }
            }
        }

        private static bool FindControl(ZoneWork current, ref int budget, long deadline, out ZDO control)
        {
            control = null;
            while (budget > 0 && BeforeDeadline(deadline))
            {
                if (!current.Cursor.Next(current.Zone, out ZDO zdo))
                {
                    Defer(current, inspectAgain: true);
                    return false;
                }
                budget--;
                if (!ValidInZone(zdo, current.Zone) || zdo.GetPrefab() != zonePrefab)
                    continue;
                controllers[current.Zone] = zdo.m_uid;
                current.Cursor.Reset();
                control = zdo;
                return true;
            }
            Queue(current.Zone);
            return false;
        }

        private static bool ReadyGeometry(ZoneWork current)
        {
            if (!world.m_zones.TryGetValue(current.Zone, out ZoneSystem.ZoneData zone) || !zone.m_root)
                return false;
            if (!current.Terrain)
                current.Terrain = zone.m_root.GetComponentInChildren<Heightmap>();
            Heightmap terrain = current.Terrain;
            // m_loadingObjectsInZones is populated by deferred LocationProxy/DungeonGenerator
            // work. Only this zone matters; unrelated dynamic/adjacent objects are not a gate.
            return terrain && terrain.isActiveAndEnabled && !terrain.m_isDistantLod && terrain.m_buildData != null &&
                terrain.m_collider && terrain.m_collider.enabled && terrain.m_collider.sharedMesh &&
                ZoneSystem.GetZone(terrain.transform.position) == current.Zone && !world.m_loadingObjectsInZones.ContainsKey(current.Zone);
        }

        private static void RestartInspection(ZoneWork current)
        {
            current.Cursor.Reset();
            current.Inspected = current.StaticMissing = current.Ghost = false;
        }

        private static void InvalidatePendingSector(ZoneSystem.SectorIndex sector, ZDO zdo)
        {
            if (!world || work.Count == 0)
                return;
            // AddToSector runs before RPC deserialization and before a moved ZDO receives
            // its new m_position. The destination sector, not prefab/type/old position,
            // identifies pending work. Sector zero is the native out-of-range bucket.
            Vector2s zone = sector.Sector == 0 ? ZoneSystem.GetZone(zdo.GetPosition()) : ZoneSystem.IndexToSector(sector.Sector);
            if (!work.TryGetValue(zone, out ZoneWork current))
                return;
            if (ReferenceEquals(current, creatingFloe))
            {
                if (creationSectorZdo == null)
                    creationSectorZdo = zdo;
                else if (!ReferenceEquals(creationSectorZdo, zdo))
                    creationHasOtherAdds = true;
                return;
            }
            RestartInspection(current);
            current.NextAttempt = 0f;
            Queue(zone);
        }

        private static void FinishFloeCreation(ZoneWork current, ZDO created)
        {
            // Ignore only the exact floe just created. Unexpected reentrant additions
            // still invalidate the pass, including additions before its ZNetView.Awake.
            bool inspectAgain = creationHasOtherAdds ||
                (creationSectorZdo != null && !ReferenceEquals(creationSectorZdo, created));
            creatingFloe = null;
            creationSectorZdo = null;
            creationHasOtherAdds = false;
            if (inspectAgain && work.TryGetValue(current.Zone, out ZoneWork pending) && ReferenceEquals(pending, current))
            {
                RestartInspection(current);
                current.NextAttempt = 0f;
                Queue(current.Zone);
            }
        }

        private static void ForgetZone(Vector2s zone)
        {
            work.Remove(zone);
            controllers.Remove(zone);
            initialExclusions.Remove(zone);
            settled.Remove(zone);
            // Keep an existing queued key until its bounded dequeue. If normal loading
            // resumes first, that same key services fresh work without a duplicate entry.
        }

        private static void ForgetGeometry(Heightmap heightmap)
        {
            if (!world || heightmap.m_isDistantLod)
                return;
            Vector2s zone = ZoneSystem.GetZone(heightmap.transform.position);
            if (world.m_zones.TryGetValue(zone, out ZoneSystem.ZoneData loaded) && loaded.m_root &&
                !heightmap.transform.IsChildOf(loaded.m_root.transform))
                return; // A retired/ghost heightmap must not evict a newer normal root.
            ForgetZone(zone);
        }

        private static void Defer(ZoneWork current, bool inspectAgain = false)
        {
            if (inspectAgain)
                RestartInspection(current);
            current.NextAttempt = Time.time + PendingRetrySeconds;
            Queue(current.Zone);
        }

        private static void AddExclusion(ZoneWork current, ZoneSystem.ClearArea area)
        {
            foreach (ZoneSystem.ClearArea existing in current.Exclusions)
                if (existing.m_center == area.m_center && existing.m_radius == area.m_radius)
                    return;
            current.Exclusions.Add(area);
        }

        private static bool InspectPlacement(ZoneWork current, ref int budget, long deadline)
        {
            if (initialExclusions.TryGetValue(current.Zone, out List<ZoneSystem.ClearArea> exclusions))
            {
                foreach (ZoneSystem.ClearArea area in exclusions)
                    AddExclusion(current, area);
                initialExclusions.Remove(current.Zone);
            }
            if (world.m_locationInstances.TryGetValue(current.Zone, out ZoneSystem.LocationInstance location) &&
                location.m_placed && location.m_location != null && location.m_location.m_clearArea)
                AddExclusion(current, new ZoneSystem.ClearArea(location.m_position, location.m_location.m_exteriorRadius));
            while (budget > 0 && BeforeDeadline(deadline))
            {
                if (!current.Cursor.Next(current.Zone, out ZDO zdo))
                {
                    if (current.StaticMissing)
                        Defer(current, inspectAgain: true);
                    else
                        current.Inspected = true;
                    return current.Inspected;
                }
                budget--;
                if (!ValidInZone(zdo, current.Zone))
                    continue;
                if (zdo.GetPrefab() == s_iceFloePrefab && zdo.GetBool(SeasonsVars.s_iceFloeWatermark))
                {
                    if (zdo.IsOwner())
                        zdo.SetDistant(true);
                    if (!current.Created.Contains(zdo.m_uid))
                    {
                        // Existing and unknown partial passes suppress duplicates but are not
                        // falsely promoted to a completed persistent placement marker.
                        StopZone(current.Zone);
                        return false;
                    }
                    continue;
                }
                if (!ZNetScene.instance.IsPrefabZDOValid(zdo))
                    continue;
                ZNetView view = ZNetScene.instance.FindInstance(zdo);
                int locationHash = zdo.GetInt(ZDOVars.s_location);
                if (locationHash != 0)
                {
                    ZoneSystem.ZoneLocation settings = world.GetLocation(locationHash);
                    if (settings != null && settings.m_clearArea)
                        AddExclusion(current, new ZoneSystem.ClearArea(zdo.GetPosition(), settings.m_exteriorRadius));
                    LocationProxy proxy = view ? view.GetComponent<LocationProxy>() : null;
                    if (!proxy || proxy.m_locationNeedsSpawn || !proxy.m_instance)
                        current.StaticMissing = true;
                }
                if (!view)
                {
                    current.Ghost = true;
                    if (zdo.m_uid != current.Control && (zdo.Type == ZDO.ObjectType.Solid || zdo.Type == ZDO.ObjectType.Terrain))
                        current.StaticMissing = true;
                }
            }
            Queue(current.Zone);
            return false;
        }

        private static void StopZone(Vector2s zone)
        {
            work.Remove(zone);
            initialExclusions.Remove(zone);
            settled.Add(zone);
        }

        private static void InitializeRandom(ZoneWork current)
        {
            UnityEngine.Random.State previous = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(WorldGenerator.instance.GetSeed() + current.Zone.x * 4271 +
                    current.Zone.y * 9187 + s_iceFloePrefab + (SeasonState.IsActive ? seasonState.GetCurrentWorldDay() : 0));
                current.Remaining = UnityEngine.Random.Range((int)SeasonalIceFloeSettings.AmountPerZone.x,
                    (int)SeasonalIceFloeSettings.AmountPerZone.y + 1);
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
            if (ZNetView.m_ghostInit || creatingFloe != null)
                return CandidateResult.Deferred;
            Vector3 center = ZoneSystem.GetZonePos(current.Zone);
            float halfZone = world.m_zoneSize / 2f;
            Vector3 p = new Vector3(UnityEngine.Random.Range(center.x - halfZone, center.x + halfZone), 0f,
                UnityEngine.Random.Range(center.z - halfZone, center.z + halfZone));
            if (IsBeyondWorldEdge(p, 100f) || world.InsideClearArea(current.Exclusions, p) ||
                (s_iceFloe.m_blockCheck && world.IsBlocked(p)))
                return CandidateResult.Skipped;
            world.GetGroundData(ref p, out _, out Heightmap.Biome biome, out Heightmap.BiomeArea biomeArea, out Heightmap hmap);
            if (!hmap || hmap.m_isDistantLod || hmap.m_buildData == null)
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
            Vector2 scaleRange = SeasonalIceFloeSettings.Scale;
            float scaleX = UnityEngine.Random.Range(scaleRange.x, scaleRange.y) * depthFactor;
            float scaleY = PowSquash(UnityEngine.Random.Range(scaleRange.x, scaleRange.y), 0.6f);
            float scaleZ = UnityEngine.Random.Range(scaleRange.x, scaleRange.y) * depthFactor;
            float halfX = s_floeSize.x * scaleX / 2;
            float halfZ = s_floeSize.y * scaleZ / 2;
            float radius = Mathf.Sqrt(halfX * halfX + halfZ * halfZ) + 0.2f;
            foreach (ZoneSystem.ClearArea area in current.Exclusions)
                if (IsInside(area, p, radius))
                    return CandidateResult.Skipped;
            if (s_iceFloe.m_snapToWater)
                p.y = world.m_waterLevel - _winterWaterSurfaceOffset;

            // Full/Ghost describes this floe's network initialization, never terrain generation.
            // Missing unrelated dynamic instances selects Ghost instead of blocking local geometry.
            GameObject instance = null;
            ZDO created = null;
            bool ghost = current.Ghost;
            bool ghostStarted = false;
            creatingFloe = current;
            creationSectorZdo = null;
            creationHasOtherAdds = false;
            try
            {
                if (ghost)
                {
                    ZNetView.StartGhostInit();
                    ghostStarted = true;
                }
                instance = UnityEngine.Object.Instantiate(s_iceFloe.m_prefab, p,
                    Quaternion.Euler(0, UnityEngine.Random.Range(0, 360), 0));
                ZNetView view = instance.GetComponent<ZNetView>();
                ZDO zdo = view.GetZDO();
                created = zdo;
                zdo.Set(SeasonsVars.s_iceFloeWatermark, true);
                current.Created.Add(zdo.m_uid);
                view.m_distant = true;
                zdo.SetDistant(true);
                view.SetLocalScale(new Vector3(scaleX, scaleY, scaleZ));
                float health = iceFloesHealth.Value * scaleX * scaleY * scaleZ;
                zdo.Set(SeasonsVars.s_iceFloeMass, view.m_body.mass * PowSquash(Mathf.Sqrt(Mathf.Abs(scaleX * scaleY * scaleZ)), 0.6f));
                zdo.Set(ZDOVars.s_health, health + Game.m_worldLevel * health * Game.instance.m_worldLevelMineHPMultiplier);
                current.Exclusions.Add(new ZoneSystem.ClearArea(p, GetFloeSize(instance) + 0.5f));
                return CandidateResult.Placed;
            }
            finally
            {
                try
                {
                    if (ghostStarted)
                    {
                        ZNetView.FinishGhostInit();
                        if (instance)
                        {
                            instance.SetActive(false);
                            UnityEngine.Object.Destroy(instance);
                        }
                    }
                }
                finally
                {
                    FinishFloeCreation(current, created);
                }
            }
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.PlaceZoneCtrl))]
        private static class ZoneSystem_PlaceZoneCtrl_Floes
        {
            private static void Postfix(Vector2s zoneID, ZoneSystem.SpawnMode mode)
            {
                if (mode != ZoneSystem.SpawnMode.Full || !PeerReady() || ZNet.instance.IsDedicated() || !PlacementSeason())
                    return;
                initialExclusions[zoneID] = new List<ZoneSystem.ClearArea>(world.m_tempClearAreas);
                RequestZone(zoneID);
            }
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
        private static class ZoneSystem_Start_Floes
        {
            private static void Postfix(ZoneSystem __instance)
            {
                PeerReady();
                InitializePrefab(__instance);
            }
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.PokeLocalZone))]
        private static class ZoneSystem_PokeLocalZone_Floes
        {
            private static void Postfix(Vector2s zoneID, bool __result)
            {
                if (__result && PeerReady())
                    RequestZone(zoneID);
            }
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.UnsetLoadingInZone))]
        private static class ZoneSystem_UnsetLoadingInZone_Floes
        {
            private static void Postfix(ZDO zdo)
            {
                if (PeerReady() && zdo != null)
                    RequestZone(zdo.GetSector());
            }
        }

        [HarmonyPatch(typeof(ZDO), nameof(ZDO.SetOwnerInternal))]
        private static class ZDO_SetOwnerInternal_FloePermission
        {
            private static void Postfix(ZDO __instance)
            {
                if (ZoneSystem.instance && __instance.GetPrefab() == zonePrefab && PlacementSeason())
                    ObserveControl(__instance);
            }
        }

        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.AddToSector))]
        private static class ZDOMan_AddToSector_PendingFloeInputs
        {
            private static void Postfix(ZDOMan __instance, ZDO zdo, ZoneSystem.SectorIndex sectorIndex)
            {
                if (__instance == ZDOMan.instance && world == ZoneSystem.instance && zdo != null)
                    InvalidatePendingSector(sectorIndex, zdo);
            }
        }

        [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.OnDestroy))]
        private static class Heightmap_OnDestroy_FloePlacement
        {
            private static void Prefix(Heightmap __instance) => ForgetGeometry(__instance);
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

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.AddInstance))]
        private static class ZNetScene_AddInstance_FloeCleanup
        {
            private static void Postfix(ZDO zdo)
            {
                if (zdo != null && zdo.GetPrefab() == s_iceFloePrefab &&
                    zdo.GetBool(SeasonsVars.s_iceFloeWatermark))
                    RequestCleanup();
            }
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
                if (zdo == null)
                    return;
                if (zdo.GetPrefab() == zonePrefab)
                    ObserveControl(zdo);
                if (zdo.GetPrefab() != s_iceFloePrefab || !zdo.GetBool(SeasonsVars.s_iceFloeWatermark))
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
