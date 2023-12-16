using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static Heightmap;
using static Seasons.Seasons;

namespace Seasons
{
    public class TerrainVariantController : MonoBehaviour
    {
        public static Dictionary<Biome, Dictionary<Season, Biome>> seasonalBiomeOverride = new Dictionary<Biome, Dictionary<Season, Biome>>
                {
                    { Biome.BlackForest, new Dictionary<Season, Biome>() { { Season.Fall, Biome.Swamp }, { Season.Winter, Biome.Mountain } } },
                    { Biome.Meadows, new Dictionary<Season, Biome>() { { Season.Fall, Biome.Plains }, { Season.Winter, Biome.Mountain } } },
                    { Biome.Plains, new Dictionary<Season, Biome>() { { Season.Spring, Biome.Meadows }, { Season.Winter, Biome.Mountain } } },
                    //{ Biome.Mistlands, new Dictionary<Season, Biome>() { { Season.Winter, Biome.Mountain } } },
                    { Biome.Swamp, new Dictionary<Season, Biome>() { { Season.Winter, Biome.Mountain } } },
                };

        public static Dictionary<Biome, Dictionary<Season, Biome>> seasonalBiomeMerge = new Dictionary<Biome, Dictionary<Season, Biome>>
                {
                    { Biome.BlackForest, new Dictionary<Season, Biome>() { { Season.Fall, Biome.BlackForest } } },
                    { Biome.Meadows, new Dictionary<Season, Biome>() { { Season.Fall, Biome.Meadows } } },
                    { Biome.Plains, new Dictionary<Season, Biome>() { { Season.Spring, Biome.Plains } } },
                    { Biome.Swamp, new Dictionary<Season, Biome>() { { Season.Winter, Biome.Swamp } } },
                };

        private Heightmap m_heightmap;
        private double m_seasonSet = 0;

        public void Init(Heightmap hmap_instance)
        {
            m_heightmap = hmap_instance;
        }

        public void LateUpdate()
        {
            if (!modEnabled.Value)
                return;

            if (m_seasonSet < seasonState.seasonChanged)
            {
                m_seasonSet = seasonState.seasonChanged;
                UpdateColors();
            }
        }

        public void UpdateColors()
        {
            if (!modEnabled.Value)
                return;

            if (!seasonState.IsActive())
                return;

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
    }

    [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.Awake))]
    public static class Heightmap_Awake_TerrainVariantControllerInit
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Heightmap __instance)
        {
            if (!modEnabled.Value)
                return;

            __instance.gameObject.AddComponent<TerrainVariantController>().Init(__instance);
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
            if (!modEnabled.Value)
                return;

            if (callBaseMethod)
                return;

            if (!seasonState.IsActive())
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
            if (!modEnabled.Value)
                return;

            if (callBaseMethod)
                return;

            if (!seasonState.IsActive())
                return;

            if (__state == Biome.None)
                return;

            biome = __state;
        }
    }
}
