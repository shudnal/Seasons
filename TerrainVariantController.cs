using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using static Heightmap;
using static Seasons.Seasons;

namespace Seasons
{
    public class TerrainVariantController : MonoBehaviour
    {
       private Heightmap m_heightmap;

        public int m_myListIndex = -1;
        public static readonly List<TerrainVariantController> s_allControllers = new List<TerrainVariantController>();

        public static Dictionary<Biome, Dictionary<Season, Biome>> seasonalBiomeOverride = new Dictionary<Biome, Dictionary<Season, Biome>>
                {
                    { Biome.BlackForest, new Dictionary<Season, Biome>() { { Season.Fall, Biome.Swamp }, { Season.Winter, Biome.Mountain } } },
                    { Biome.Meadows, new Dictionary<Season, Biome>() { { Season.Fall, Biome.Plains }, { Season.Winter, Biome.Mountain } } },
                    { Biome.Plains, new Dictionary<Season, Biome>() { { Season.Spring, Biome.Meadows }, { Season.Winter, Biome.Mountain } } },
                    { Biome.Mistlands, new Dictionary<Season, Biome>() { { Season.Winter, Biome.Mountain } } },
                    { Biome.Swamp, new Dictionary<Season, Biome>() { { Season.Winter, Biome.Mountain } } },
                };

        private void Awake()
        {
            m_heightmap = GetComponentInParent<Heightmap>();
            s_allControllers.Add(this);
            m_myListIndex = s_allControllers.Count - 1;
        }
        
        private void OnEnable()
        {
            UpdateColors();
        }

        private void OnDestroy()
        {
            if (m_myListIndex >= 0)
            {
                s_allControllers[m_myListIndex] = s_allControllers[s_allControllers.Count - 1];
                s_allControllers[m_myListIndex].m_myListIndex = m_myListIndex;
                s_allControllers.RemoveAt(s_allControllers.Count - 1);
                m_myListIndex = -1;
            }
        }

        private void UpdateColors()
        {
            if (m_heightmap.m_renderMesh == null)
                return;

            int num = m_heightmap.m_width + 1;
            Vector3 vector = base.transform.position + new Vector3((float)((double)m_heightmap.m_width * (double)m_heightmap.m_scale * -0.5), 0f, (float)((double)m_heightmap.m_width * (double)m_heightmap.m_scale * -0.5));
            s_tempColors.Clear();
            for (int i = 0; i < num; i++)
            {
                float iy = DUtils.SmoothStep(0f, 1f, (float)((double)i / (double)m_heightmap.m_width));
                for (int j = 0; j < num; j++)
                {
                    float ix = DUtils.SmoothStep(0f, 1f, (float)((double)j / (double)m_heightmap.m_width));
                    if (m_heightmap.m_isDistantLod)
                    {
                        float wx = (float)((double)vector.x + (double)j * (double)m_heightmap.m_scale);
                        float wy = (float)((double)vector.z + (double)i * (double)m_heightmap.m_scale);
                        Biome biome = WorldGenerator.instance.GetBiome(wx, wy);
                        s_tempColors.Add(GetBiomeColor(biome));
                    }
                    else
                    {
                        s_tempColors.Add(m_heightmap.GetBiomeColor(ix, iy));
                    }
                }
            }

            m_heightmap.m_renderMesh.SetColors(s_tempColors);
        }
        
        public static void UpdateTerrainColors()
        {
            foreach (TerrainVariantController controller in s_allControllers)
                controller.UpdateColors();
        }
        
    }

    [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.Awake))]
    public static class Heightmap_Awake_TerrainVariantControllerInit
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Heightmap __instance)
        {
            __instance.gameObject.AddComponent<TerrainVariantController>();
        }
    }

    [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.GetBiomeColor), new[] { typeof(Biome) })]
    public static class Heightmap_GetBiomeColor_TerrainColor
    {
        private static bool callBaseMethod = false;

        private static Color32 GetBaseBiomeColor(Biome biome)
        {
            callBaseMethod = true;
            Color32 color = GetBiomeColor(biome);
            callBaseMethod = false;
            return color;
        }

        [HarmonyPriority(Priority.Last)]
        private static void Prefix(ref Biome biome, ref Biome __state)
        {
            if (callBaseMethod)
                return;

            __state = Biome.None;

            if (TerrainVariantController.seasonalBiomeOverride.TryGetValue(biome, out Dictionary<Season, Biome> overrideBiome) && overrideBiome.TryGetValue(seasonState.GetCurrentSeason(), out Biome overridedBiome))
            {
                __state = biome;
                biome = overridedBiome;
            }
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(ref Biome biome, Biome __state)
        {
            if (callBaseMethod)
                return;

            if (__state == Biome.None)
                return;

            biome = __state;
        }
    }
}
