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
        private static readonly MethodInfo TrySpawnInteriorMethod = AccessTools.Method(typeof(BloodMoonSpawner), "TrySpawnInterior",
            new[] { typeof(GameObject), typeof(BloodMoonSpawnLeaseState), typeof(Vector2i), typeof(List<Player>) });

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
                spawned = Invoke(TrySpawnSurfaceMethod, prefab, lease, zone, surfaceTargets);
            }
            else if (surfaceTargets.Count == 0)
            {
                spawned = Invoke(TrySpawnInteriorMethod, prefab, lease, zone, interiorTargets);
            }
            else
            {
                Player selected = targets[Random.Range(0, targets.Count)];
                if (selected.InInterior())
                {
                    spawned = Invoke(TrySpawnInteriorMethod, prefab, lease, zone, interiorTargets);
                    if (!spawned)
                        spawned = Invoke(TrySpawnSurfaceMethod, prefab, lease, zone, surfaceTargets);
                }
                else
                {
                    spawned = Invoke(TrySpawnSurfaceMethod, prefab, lease, zone, surfaceTargets);
                    if (!spawned)
                        spawned = Invoke(TrySpawnInteriorMethod, prefab, lease, zone, interiorTargets);
                }
            }

            if (spawned)
                lease.Allowance--;
            return false;
        }

        private static bool Invoke(MethodInfo method, GameObject prefab, BloodMoonSpawnLeaseState lease, Vector2i zone, List<Player> targets)
        {
            if (method == null || targets == null || targets.Count == 0)
                return false;
            return method.Invoke(null, new object[] { prefab, lease, zone, targets }) is bool result && result;
        }
    }
}
