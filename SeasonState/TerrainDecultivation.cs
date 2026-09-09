using System;
using UnityEngine;

namespace Seasons
{
    public static class TerrainDecultivation
    {
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
            Heightmap terrainPrefab = ZoneSystem.instance?.m_zonePrefab?.GetComponentInChildren<Heightmap>(true);
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

            // Version 1 stores either the legacy width-squared paint grid or the new vertex-sized grid.
            int paintCount = zPackageRead.ReadInt();
            int paintPitch;
            float paintOffset;
            if (paintCount == heightCount)
            {
                paintPitch = terrainPrefab.m_width + 1;
                paintOffset = -0.5f; // Valheim 1.0.7 Heightmap.VertexMaskToWorld.
            }
            else if (paintCount == terrainPrefab.m_width * terrainPrefab.m_width)
            {
                paintPitch = terrainPrefab.m_width;
                paintOffset = 0.5f; // Legacy paint cells are centered inside the terrain grid.
            }
            else
            {
                Seasons.LogWarning("Seasons cannot decultivate ground: unsupported terrain paint grid dimensions.");
                return false;
            }

            m_modifiedPaint = new bool[paintCount];
            m_paintMask = new Color[paintCount];
            Vector3 terrainCenter = zdo.GetPosition();
            int halfWidth = terrainPrefab.m_width / 2;
            float scale = terrainPrefab.m_scale;
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

                    // Use the serialized grid's coordinates. In the Deep North, green stores snow manipulation.
                    float wx = terrainCenter.x + (j % paintPitch - halfWidth + paintOffset) * scale;
                    float wz = terrainCenter.z + (j / paintPitch - halfWidth + paintOffset) * scale;
                    float sharedSnowMask = Mathf.Min(color.r, color.b);
                    if (color.g > sharedSnowMask && !WorldGenerator.IsDeepnorth(wx, wz))
                    {
                        // Keep the common RGB contribution used by the DeepSnow paint mask.
                        color.r = Mathf.Max(color.r, color.g);
                        color.g = sharedSnowMask;
                        decultivated = true;
                    }

                    m_paintMask[j] = color;
                }
                else
                {
                    m_paintMask[j] = Color.black;
                }
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
            changed = true;

            return true;
        }
    }
}
