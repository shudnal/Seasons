using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Seasons.BloodMoon
{
    [HarmonyPatch(typeof(BloodMoonSpawner), "TrySpawnFromLease")]
    internal static class BloodMoonMixedContextSpawnPatch
    {
        private static readonly MethodInfo TrySpawnSurfaceMethod = AccessTools.Method(typeof(BloodMoonSpawner), "TrySpawnSurface",
            new[] { typeof(GameObject), typeof(BloodMoonSpawnLeaseState), typeof(Vector2i), typeof(List<Player>) });
        private static readonly MethodInfo HasPathMethod = AccessTools.Method(typeof(BloodMoonSpawner), "HasPath",
            new[] { typeof(GameObject), typeof(Vector3), typeof(Vector3) });
        private static readonly MethodInfo SpawnMarkedMethod = AccessTools.Method(typeof(BloodMoonSpawner), "SpawnMarked",
            new[] { typeof(GameObject), typeof(Vector3), typeof(BloodMoonSpawnLeaseState), typeof(Vector2i), typeof(BloodMoonExtraEnemyRole) });

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BloodMoonSpawnLeaseState lease, Vector2i zone)
        {
            if (lease == null || lease.Allowance <= 0 || ZNetScene.instance == null)
                return false;

            string prefabName = BloodMoonConfig.TestEnemyPrefab.Value?.Trim();
            GameObject prefab = string.IsNullOrEmpty(prefabName) ? null : ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null || prefab.GetComponent<MonsterAI>() == null)
                return false;

            List<Player> targets = BloodMoonInteractionRules.GetLoadedActiveParticipants(preferFighting: false)
                .Where(player => Utils.DistanceXZ(player.transform.position, ZoneSystem.GetZonePos(zone)) <= 180f)
                .ToList();
            if (targets.Count == 0)
                return false;

            List<Player> interiorTargets = targets.Where(player => player.InInterior()).ToList();
            List<Player> surfaceTargets = targets.Where(player => !player.InInterior()).ToList();

            bool spawned;
            if (interiorTargets.Count == 0)
            {
                spawned = TrySpawnSurface(prefab, lease, zone, surfaceTargets);
            }
            else if (surfaceTargets.Count == 0)
            {
                spawned = TrySpawnInterior(prefab, lease, zone, interiorTargets);
            }
            else
            {
                Player selected = targets[Random.Range(0, targets.Count)];
                if (selected.InInterior())
                {
                    spawned = TrySpawnInterior(prefab, lease, zone, interiorTargets);
                    if (!spawned)
                        spawned = TrySpawnSurface(prefab, lease, zone, surfaceTargets);
                }
                else
                {
                    spawned = TrySpawnSurface(prefab, lease, zone, surfaceTargets);
                    if (!spawned)
                        spawned = TrySpawnInterior(prefab, lease, zone, interiorTargets);
                }
            }

            if (spawned)
                lease.Allowance--;
            return false;
        }

        private static bool TrySpawnSurface(GameObject prefab, BloodMoonSpawnLeaseState lease, Vector2i zone, List<Player> targets)
        {
            if (TrySpawnSurfaceMethod == null || targets == null || targets.Count == 0)
                return false;
            return TrySpawnSurfaceMethod.Invoke(null, new object[] { prefab, lease, zone, targets }) is bool result && result;
        }

        private static bool TrySpawnInterior(GameObject prefab, BloodMoonSpawnLeaseState lease, Vector2i zone, List<Player> targets)
        {
            if (HasPathMethod == null || SpawnMarkedMethod == null || targets == null || targets.Count == 0)
                return false;

            foreach (CreatureSpawner spawner in CreatureSpawner.m_creatureSpawners
                .Where(spawner => spawner != null && spawner.m_nview != null && spawner.m_nview.IsValid() && spawner.m_nview.IsOwner())
                .Where(spawner => ZoneSystem.GetZone(spawner.transform.position) == zone && Character.InInterior(spawner.transform.position))
                .OrderBy(spawner => Vector3.Distance(spawner.transform.position, lease.Anchor)))
            {
                Player target = targets
                    .Where(player => SameInteriorContext(spawner.transform.position, player.transform.position))
                    .OrderBy(player => Vector3.Distance(player.transform.position, spawner.transform.position))
                    .FirstOrDefault();
                if (target == null)
                    continue;

                bool hasPath = HasPathMethod.Invoke(null, new object[] { prefab, spawner.transform.position, target.transform.position }) is bool path && path;
                if (!hasPath)
                    continue;

                if (SpawnMarkedMethod.Invoke(null, new object[]
                {
                    prefab,
                    spawner.transform.position,
                    lease,
                    zone,
                    BloodMoonExtraEnemyRole.Interior
                }) is bool spawned && spawned)
                    return true;
            }
            return false;
        }

        private static bool SameInteriorContext(Vector3 first, Vector3 second)
        {
            if (!Character.InInterior(first) || !Character.InInterior(second))
                return false;

            Location firstLocation = Location.GetLocation(first);
            Location secondLocation = Location.GetLocation(second);
            if (firstLocation != null || secondLocation != null)
                return firstLocation != null && object.ReferenceEquals(firstLocation, secondLocation);

            return ZoneSystem.GetZone(first) == ZoneSystem.GetZone(second);
        }
    }
}
