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
                Seasons.LogWarning($"Season can not decultivate ground due to changes in terrain compiler data");
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

                    if (color.g > 0)
                    {
                        color.r = Mathf.Max(color.r, color.g);
                        color.g = 0;
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
