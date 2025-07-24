using HarmonyLib;
using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    public class MinimapVariantController : MonoBehaviour
    {
        private Minimap m_minimap;
        private static MinimapVariantController m_instance;

        private bool m_initialized = false;
        private Color32[] m_mapTexture;
        private Color32[] m_mapWinterTexture;
        private Texture2D m_forestTex;
        
        private bool m_isWinter = false;

        public static MinimapVariantController instance => m_instance;

        private void Awake()
        {
            m_instance = this;
            m_minimap = Minimap.instance;
        }

        private void Start()
        {
            Texture forest = m_minimap.m_mapLargeShader.GetTexture("_ForestTex");
            m_forestTex = new Texture2D(forest.width, forest.height, forest.graphicsFormat, UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
            Graphics.CopyTexture(forest, m_forestTex);

            StartCoroutine(GenerateWinterWorldMap());
        }

        private void OnDestroy()
        {
            RevertTextures();
            m_instance = null;
        }

        public void RevertTextures()
        {
            if (!m_initialized)
                return;

            SetMapTextures(winterChanged: m_isWinter, m_forestTex);
        }

        public void UpdateColors()
        {
            if (!m_initialized)
                return;

            if (!controlMinimap.Value)
            {
                RevertTextures();
                return;
            }

            Season season = seasonState.GetCurrentSeason();
            bool winterChanged = m_isWinter != (m_isWinter = season == Season.Winter);
            SetMapTextures(winterChanged, GetSeasonalForestTex(season));
        }

        public Texture2D GetSeasonalForestTex(Season season)
        {
            return season switch
            {
                Season.Spring => m_forestTex,
                Season.Summer => Minimap_Summer_ForestTex,
                Season.Fall => Minimap_Fall_ForestTex,
                Season.Winter => Minimap_Winter_ForestTex,
                _ => m_forestTex,
            };
        }

        private void SetMapTextures(bool winterChanged, Texture2D forestTex)
        {
            try
            {
                if (winterChanged)
                {
                    m_minimap.m_mapTexture.SetPixels32(m_isWinter ? m_mapWinterTexture : m_mapTexture);
                    m_minimap.m_mapTexture.Apply();

                    Compatibility.MarketplaceCompat.UpdateMap();
                }
            }
            catch (Exception e)
            {
                LogWarning($"Error applying {(m_isWinter ? "winter ": "")}map texture length {(m_isWinter ? m_mapWinterTexture : m_mapTexture).Length} to minimap texture length {m_minimap.m_mapTexture.height * m_minimap.m_mapTexture.width}:\n{e}");
            }

            m_minimap.m_mapLargeShader.SetTexture("_ForestTex", forestTex);
            m_minimap.m_mapSmallShader.SetTexture("_ForestTex", forestTex);
        }

        public IEnumerator GenerateWinterWorldMap()
        {
            int num = m_minimap.m_textureSize / 2;
            float num2 = m_minimap.m_pixelSize / 2f;
            m_mapWinterTexture = new Color32[m_minimap.m_textureSize * m_minimap.m_textureSize];
            m_mapTexture = new Color32[m_minimap.m_textureSize * m_minimap.m_textureSize];

            yield return new WaitUntil(() => WorldGenerator.instance != null);

            Stopwatch stopwatch = Stopwatch.StartNew();

            var internalThread = new Thread(() =>
            {
                for (int i = 0; i < m_minimap.m_textureSize; i++)
                    for (int j = 0; j < m_minimap.m_textureSize; j++)
                    {
                        float wx = (j - num) * m_minimap.m_pixelSize + num2;
                        float wy = (i - num) * m_minimap.m_pixelSize + num2;
                        Heightmap.Biome biome = WorldGenerator.instance.GetBiome(wx, wy);
                        m_mapWinterTexture[i * m_minimap.m_textureSize + j] = GetWinterPixelColor(biome);
                        m_mapTexture[i * m_minimap.m_textureSize + j] = Minimap.instance.GetPixelColor(biome);
                    }
            });

            internalThread.Start();

            yield return new WaitWhile(() => internalThread.IsAlive == true);

            m_initialized = true;

            LogInfo($"Minimap variant controller initialized in {stopwatch.Elapsed.TotalSeconds,-4:F2} seconds");

            UpdateColors();
        }

        public static Color GetWinterPixelColor(Heightmap.Biome biome)
        {
            if (SeasonState.seasonBiomeSettings.SeasonalWinterMapColors.TryGetValue(biome, out Color color))
                return color;

            return Minimap.instance ? Minimap.instance.GetPixelColor(biome) : Color.white;
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.Start))]
    public static class Minimap_Start_MinimapContollerInit
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Minimap __instance)
        {
            if (!UseTextureControllers())
                return;

            __instance.transform.gameObject.AddComponent<MinimapVariantController>();
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.GenerateWorldMap))]
    public static class Minimap_GenerateWorldMap_MinimapContollerInit
    {
        private static void Postfix()
        {
            if (!UseTextureControllers())
                return;

            MinimapVariantController.instance.UpdateColors();
        }
    }
}
