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

        public static bool DecultivateGround(ZDO zdo)
        {
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
            m_modifiedHeight = new bool[zPackageRead.ReadInt()];
            m_levelDelta = new float[m_modifiedHeight.Length];
            m_smoothDelta = new float[m_modifiedHeight.Length];

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

            m_modifiedPaint = new bool[zPackageRead.ReadInt()];
            m_paintMask = new Color[m_modifiedPaint.Length];

            Heightmap terrainPrefab = ZoneSystem.instance?.m_zonePrefab?.GetComponentInChildren<Heightmap>(true);
            if (terrainPrefab == null || m_modifiedHeight.Length != (terrainPrefab.m_width + 1) * (terrainPrefab.m_width + 1))
            {
                Seasons.LogWarning("Seasons cannot decultivate ground: the terrain dimensions could not be resolved safely.");
                return false;
            }

            // Version 1 stores either the legacy width-squared paint grid or the new vertex-sized grid.
            int paintPitch;
            if (m_modifiedPaint.Length == m_modifiedHeight.Length)
                paintPitch = terrainPrefab.m_width + 1;
            else if (m_modifiedPaint.Length == terrainPrefab.m_width * terrainPrefab.m_width)
                paintPitch = terrainPrefab.m_width;
            else
            {
                Seasons.LogWarning("Seasons cannot decultivate ground: unsupported terrain paint grid dimensions.");
                return false;
            }

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

                    // Match Heightmap.VertexMaskToWorld. In the Deep North, green stores snow manipulation.
                    float wx = terrainCenter.x + (j % paintPitch - halfWidth - 0.5f) * scale;
                    float wz = terrainCenter.z + (j / paintPitch - halfWidth - 0.5f) * scale;
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
                return false;

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

            return true;
        }
    }
}
