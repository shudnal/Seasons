using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Seasons.BloodMoon
{
    [HarmonyPatch(typeof(BloodMoonSpawner), "TrySpawnInterior")]
    internal static class BloodMoonInteriorSpawnContextPatch
    {
        private static readonly MethodInfo HasPathMethod = AccessTools.Method(typeof(BloodMoonSpawner), "HasPath",
            new[] { typeof(GameObject), typeof(Vector3), typeof(Vector3) });
        private static readonly MethodInfo SpawnMarkedMethod = AccessTools.Method(typeof(BloodMoonSpawner), "SpawnMarked",
            new[] { typeof(GameObject), typeof(Vector3), typeof(BloodMoonSpawnLeaseState), typeof(Vector2i), typeof(BloodMoonExtraEnemyRole) });

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(GameObject prefab, BloodMoonSpawnLeaseState lease, Vector2i zone, List<Player> targets, ref bool __result)
        {
            __result = false;
            if (prefab == null || lease == null || targets == null || targets.Count == 0 || HasPathMethod == null || SpawnMarkedMethod == null)
                return false;

            List<Player> interiorTargets = targets.Where(player => player != null && player.InInterior()).ToList();
            if (interiorTargets.Count == 0)
                return false;

            foreach (CreatureSpawner spawner in CreatureSpawner.m_creatureSpawners
                .Where(spawner => spawner != null && spawner.m_nview != null && spawner.m_nview.IsValid() && spawner.m_nview.IsOwner())
                .Where(spawner => ZoneSystem.GetZone(spawner.transform.position) == zone && Character.InInterior(spawner.transform.position))
                .OrderBy(spawner => Vector3.Distance(spawner.transform.position, lease.Anchor)))
            {
                Player target = interiorTargets
                    .Where(player => SameInteriorContext(spawner.transform.position, player.transform.position))
                    .OrderBy(player => Vector3.Distance(player.transform.position, spawner.transform.position))
                    .FirstOrDefault();
                if (target == null)
                    continue;

                bool hasPath = HasPathMethod.Invoke(null, new object[] { prefab, spawner.transform.position, target.transform.position }) is bool path && path;
                if (!hasPath)
                    continue;

                __result = SpawnMarkedMethod.Invoke(null, new object[]
                {
                    prefab,
                    spawner.transform.position,
                    lease,
                    zone,
                    BloodMoonExtraEnemyRole.Interior
                }) is bool spawned && spawned;
                if (__result)
                    return false;
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
                return firstLocation != null && ReferenceEquals(firstLocation, secondLocation);

            return ZoneSystem.GetZone(first) == ZoneSystem.GetZone(second);
        }
    }
}
