using System.Collections.Generic;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    /// <summary>Event-driven world maintenance. No terrain polling or deferred work.</summary>
    internal static class SeasonalWorldMaintenance
    {
        internal static void Reset() => TerrainDecultivation.ResetRuntimeState();

        // Existing water/configuration callers only reconcile the independent floe lifecycle.
        internal static void RequestWorldScan() => SeasonalIceFloes.RequestCleanup();

        public static string GetFloeCleanupStatus() => SeasonalIceFloes.GetCleanupStatus();

        internal static void OnWinterStarted(int winterStartDay)
        {
            if (!cultivatedGroundTurnsIntoDirtInWinter.Value || !ZNet.instance.IsServer())
                return;

            Heightmap fallbackTerrain = ZoneSystem.instance.m_zonePrefab.GetComponentInChildren<Heightmap>(true);
            if (fallbackTerrain == null || WorldGenerator.instance?.m_world?.m_biomeData?.IsReady != true)
            {
                LogWarning("Winter terrain decultivation was skipped: terrain geometry or biome data is unavailable.");
                return;
            }

            Dictionary<ZDO, TerrainComp> loaded = new Dictionary<ZDO, TerrainComp>();
            foreach (TerrainComp compiler in TerrainComp.s_instances)
                if (compiler != null && compiler.m_nview != null && compiler.m_nview.GetZDO() is ZDO data)
                    loaded[data] = compiler;

            int terrainPrefab = "_TerrainCompiler".GetStableHashCode();
            int processed = 0;
            int changed = 0;
            int failed = 0;
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
            {
                if (zdo.GetPrefab() != terrainPrefab ||
                    (zdo.GetInt(SeasonsVars.s_terrainDecultivated, out int previousWinter) && previousWinter >= winterStartDay))
                    continue;

                loaded.TryGetValue(zdo, out TerrainComp compiler);
                Heightmap terrain = compiler != null && compiler.m_hmap != null ? compiler.m_hmap : fallbackTerrain;
                if (!TerrainDecultivation.TryDecultivateGround(zdo, compiler, terrain, out bool didChange))
                {
                    failed++;
                    continue;
                }

                // Stamp successful no-ops too. Do not mark unsupported or corrupt snapshots.
                // Keep the existing field as a world-day marker, now identifying winter's start.
                zdo.Set(SeasonsVars.s_terrainDecultivated, winterStartDay);
                processed++;
                if (didChange)
                {
                    changed++;
                    TerrainDecultivation.RefreshLoadedCompiler(compiler);
                }
            }
            LogInfo($"Winter terrain decultivation: winter={winterStartDay}, processed={processed}, changed={changed}, failed={failed}");
        }
    }
}
