using System;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace Seasons
{
    public static class TerrainDecultivation
    {
        private sealed class FailedAttempt
        {
            public uint Revision;
            public int WorldDay;
        }

        private static readonly ConditionalWeakTable<TerrainComp, FailedAttempt> failedAttempts = new ConditionalWeakTable<TerrainComp, FailedAttempt>();
        public static int terrainCompVersion;
        public static int m_operations;
        public static Vector3 m_lastOpPoint;
        public static float m_lastOpRadius;

        public static bool[] m_modifiedHeight;
        public static float[] m_levelDelta;
        public static float[] m_smoothDelta;
        public static bool[] m_modifiedPaint;
        public static Color[] m_paintMask;

        public static bool DecultivateGround(ZDO zdo) => TryDecultivateGround(zdo, out bool changed) && changed;

        internal static bool IsDecultivationDue(ZDO zdo, int worldDay, int yearLength)
        {
            return !zdo.GetInt(SeasonsVars.s_terrainDecultivated, out int processedDay)
                || Math.Abs((long)worldDay - processedDay) >= yearLength;
        }

        // Success includes a fully inspected no-op; unavailable or unsupported data remains retryable.
        internal static bool TryDecultivateGround(ZDO zdo, out bool changed)
        {
            changed = false;
            try
            {
                return DecultivateGroundCore(zdo, out changed);
            }
            catch (Exception error)
            {
                changed = false;
                Seasons.LogWarning($"Seasons could not process terrain decultivation; the processed day was not advanced:\n{error}");
                return false;
            }
        }

        private static bool DecultivateGroundCore(ZDO zdo, out bool changed)
        {
            changed = false;
            // Remote owners must process their own loaded compiler, otherwise their next Save can undo this edit.
            if (zdo == null || !zdo.IsValid() || (zdo.HasOwner() && !zdo.IsOwner()) || WorldGenerator.instance?.m_world?.m_biomeData?.IsReady != true)
                return false;

            byte[] byteArray = zdo.GetByteArray(ZDOVars.s_TCData);
            if (byteArray == null)
                return false;

            ZPackage zPackageRead = new ZPackage(Utils.Decompress(byteArray));
            terrainCompVersion = zPackageRead.ReadInt();

            if (terrainCompVersion != ZoneSystemVariantController.s_terrainCompVersion)
            {
                Seasons.LogWarning("Seasons cannot decultivate ground: unsupported terrain compiler data version.");
                return false;
            }

            bool decultivated = false;

            m_operations = zPackageRead.ReadInt();
            m_lastOpPoint = zPackageRead.ReadVector3();
            m_lastOpRadius = zPackageRead.ReadSingle();

            int heightCount = zPackageRead.ReadInt();
            TerrainComp loadedCompiler = TerrainComp.s_instances.Find(compiler => compiler != null && compiler.m_nview != null && compiler.m_nview.GetZDO() == zdo);
            Heightmap terrainPrefab = loadedCompiler != null ? loadedCompiler.m_hmap : ZoneSystem.instance?.m_zonePrefab?.GetComponentInChildren<Heightmap>(true);
            if (terrainPrefab == null || heightCount != (terrainPrefab.m_width + 1) * (terrainPrefab.m_width + 1))
            {
                Seasons.LogWarning("Seasons cannot decultivate ground: the terrain dimensions could not be resolved safely.");
                return false;
            }

            m_modifiedHeight = new bool[heightCount];
            m_levelDelta = new float[heightCount];
            m_smoothDelta = new float[heightCount];

            for (int i = 0; i < m_modifiedHeight.Length; i++)
            {
                m_modifiedHeight[i] = zPackageRead.ReadBool();
                if (m_modifiedHeight[i])
                {
                    m_levelDelta[i] = zPackageRead.ReadSingle();
                    m_smoothDelta[i] = zPackageRead.ReadSingle();
                }
                else
                {
                    m_levelDelta[i] = 0f;
                    m_smoothDelta[i] = 0f;
                }
            }

            // Both supported snapshots load legacy cells into a vertex-sized grid before applying paint.
            int paintCount = zPackageRead.ReadInt();
            if (paintCount != heightCount && paintCount != terrainPrefab.m_width * terrainPrefab.m_width)
            {
                Seasons.LogWarning("Seasons cannot decultivate ground: unsupported terrain paint grid dimensions.");
                return false;
            }

            m_modifiedPaint = new bool[paintCount];
            m_paintMask = new Color[paintCount];
            for (int j = 0; j < m_modifiedPaint.Length; j++)
            {
                m_modifiedPaint[j] = zPackageRead.ReadBool();
                if (m_modifiedPaint[j])
                {
                    Color color = default;
                    color.r = zPackageRead.ReadSingle();
                    color.g = zPackageRead.ReadSingle();
                    color.b = zPackageRead.ReadSingle();
                    color.a = zPackageRead.ReadSingle();

                    m_paintMask[j] = color;
                }
                else
                {
                    m_paintMask[j] = Color.black;
                }
            }

            if (zPackageRead.GetPos() != zPackageRead.Size())
            {
                Seasons.LogWarning("Seasons cannot decultivate ground: unexpected trailing terrain data.");
                return false;
            }

            int paintPitch = terrainPrefab.m_width + 1;
            if (paintCount != heightCount)
            {
                bool[] legacyModifiedPaint = m_modifiedPaint;
                Color[] legacyPaintMask = m_paintMask;
                m_modifiedPaint = new bool[heightCount];
                m_paintMask = new Color[heightCount];
                // TerrainComp.Load repeats the final legacy column and row at the new outer vertices.
                for (int y = 0; y < paintPitch; y++)
                    for (int x = 0; x < paintPitch; x++)
                    {
                        int currentIndex = y * paintPitch + x;
                        int legacyIndex = Math.Min(y, terrainPrefab.m_width - 1) * terrainPrefab.m_width + Math.Min(x, terrainPrefab.m_width - 1);
                        m_modifiedPaint[currentIndex] = legacyModifiedPaint[legacyIndex];
                        m_paintMask[currentIndex] = legacyPaintMask[legacyIndex];
                    }
            }

            Vector3 terrainCenter = zdo.GetPosition();
            int halfWidth = terrainPrefab.m_width / 2;
            float scale = terrainPrefab.m_scale;
            float halfSize = terrainPrefab.m_width * scale * 0.5f;
            Heightmap.Biome[] cornerBiomes =
            {
                WorldGenerator.instance.GetBiomeSector(terrainCenter.x - halfSize, terrainCenter.z - halfSize).Biome,
                WorldGenerator.instance.GetBiomeSector(terrainCenter.x + halfSize, terrainCenter.z - halfSize).Biome,
                WorldGenerator.instance.GetBiomeSector(terrainCenter.x - halfSize, terrainCenter.z + halfSize).Biome,
                WorldGenerator.instance.GetBiomeSector(terrainCenter.x + halfSize, terrainCenter.z + halfSize).Biome
            };
            for (int j = 0; j < m_modifiedPaint.Length; j++)
            {
                if (!m_modifiedPaint[j])
                    continue;

                Color color = m_paintMask[j];
                // Heightmap.VertexMaskToWorld: sample coordinates have a -0.5 offset after native migration.
                float wx = terrainCenter.x + (j % paintPitch - halfWidth - 0.5f) * scale;
                float wz = terrainCenter.z + (j / paintPitch - halfWidth - 0.5f) * scale;
                float sharedSnowMask = Mathf.Min(color.r, color.b);
                // Cultivate also edits snow by geometric region; biome data can independently assign northern terrain.
                if (color.g <= sharedSnowMask || WorldGenerator.IsDeepnorth(wx, wz)
                    || WorldGenerator.instance.GetBiomeSector(wx, wz).Biome == Heightmap.Biome.DeepNorth
                    || IsNorthernTerrain(cornerBiomes, (wx - terrainCenter.x) / (halfSize * 2f) + 0.5f, (wz - terrainCenter.z) / (halfSize * 2f) + 0.5f)
                    || (loadedCompiler != null && terrainPrefab.GetBiome(new Vector3(wx, terrainCenter.y, wz)) == Heightmap.Biome.DeepNorth))
                    continue;

                color.r = Mathf.Max(color.r, color.g);
                color.g = sharedSnowMask;
                m_paintMask[j] = color;
                decultivated = true;
            }

            if (!decultivated)
                return true;

            ZPackage zPackageWrite = new ZPackage();
            zPackageWrite.Write(terrainCompVersion);
            zPackageWrite.Write(m_operations);
            zPackageWrite.Write(m_lastOpPoint);
            zPackageWrite.Write(m_lastOpRadius);
            zPackageWrite.Write(m_modifiedHeight.Length);
            for (int i = 0; i < m_modifiedHeight.Length; i++)
            {
                zPackageWrite.Write(m_modifiedHeight[i]);
                if (m_modifiedHeight[i])
                {
                    zPackageWrite.Write(m_levelDelta[i]);
                    zPackageWrite.Write(m_smoothDelta[i]);
                }
            }
            zPackageWrite.Write(m_modifiedPaint.Length);
            for (int j = 0; j < m_modifiedPaint.Length; j++)
            {
                zPackageWrite.Write(m_modifiedPaint[j]);
                if (m_modifiedPaint[j])
                {
                    zPackageWrite.Write(m_paintMask[j].r);
                    zPackageWrite.Write(m_paintMask[j].g);
                    zPackageWrite.Write(m_paintMask[j].b);
                    zPackageWrite.Write(m_paintMask[j].a);
                }
            }
            byte[] bytes = Utils.Compress(zPackageWrite.GetArray());
            zdo.Set(ZDOVars.s_TCData, bytes);
            // CheckLoad updates arrays and m_lastDataRevision before another operation can save stale paint.
            if (loadedCompiler != null)
            {
                loadedCompiler.CheckLoad();
                loadedCompiler.m_lastHash = loadedCompiler.ComputePaintMaskHash();
            }
            changed = true;

            return true;
        }

        private static bool IsNorthernTerrain(Heightmap.Biome[] corners, float x, float y)
        {
            // Heightmap.GetBiome groups these corner weights; the same layout must protect unloaded tiles.
            Vector4 weights = new Vector4(Heightmap.Distance(x, y, 0f, 0f), Heightmap.Distance(x, y, 1f, 0f), Heightmap.Distance(x, y, 0f, 1f), Heightmap.Distance(x, y, 1f, 1f));
            float northWeight = 0f;
            for (int i = 0; i < corners.Length; i++)
                if (corners[i] == Heightmap.Biome.DeepNorth)
                    northWeight += weights[i];
            if (northWeight == 0f)
                return false;
            for (int i = 0; i < corners.Length; i++)
            {
                if (corners[i] == Heightmap.Biome.DeepNorth)
                    continue;
                float biomeWeight = 0f;
                for (int j = 0; j < corners.Length; j++)
                    if (corners[j] == corners[i])
                        biomeWeight += weights[j];
                if (biomeWeight > northWeight)
                    return false;
            }
            return true;
        }

        [HarmonyLib.HarmonyPatch(typeof(TerrainComp), nameof(TerrainComp.Update))]
        private static class TerrainComp_Update_DecultivateOwnedTerrain
        {
            private static void Prefix(TerrainComp __instance)
            {
                if (!SeasonState.IsActive || !ZoneSystemVariantController.IsTimeToDecultivateGround()
                    || !__instance.m_initialized || __instance.m_nview == null || !__instance.m_nview.IsValid() || !__instance.m_nview.IsOwner())
                    return;

                ZDO zdo = __instance.m_nview.GetZDO();
                int worldDay = Seasons.seasonState.GetCurrentWorldDay();
                if (!IsDecultivationDue(zdo, worldDay, Seasons.seasonState.GetYearLengthInDays()))
                    return;

                if (WorldGenerator.instance?.m_world?.m_biomeData?.IsReady != true
                    || (failedAttempts.TryGetValue(__instance, out FailedAttempt previous) && previous.Revision == zdo.DataRevision && previous.WorldDay == worldDay))
                    return;

                if (TryDecultivateGround(zdo, out _))
                {
                    zdo.Set(SeasonsVars.s_terrainDecultivated, worldDay);
                    failedAttempts.Remove(__instance);
                }
                else
                {
                    FailedAttempt failed = failedAttempts.GetValue(__instance, _ => new FailedAttempt());
                    failed.Revision = zdo.DataRevision;
                    failed.WorldDay = worldDay;
                }
            }
        }
    }
}
