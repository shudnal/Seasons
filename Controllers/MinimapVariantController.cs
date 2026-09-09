using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    public class MinimapVariantController : MonoBehaviour
    {
        private sealed class MapGeneration
        {
            public volatile bool Cancelled;
            public volatile bool Completed;
            public Color32[] Pixels;
            public Exception Error;
        }

        private Minimap m_minimap;
        private static MinimapVariantController m_instance;
        private bool m_started;
        private bool m_mapDataReady;
        private bool m_initialized;
        private bool m_isWinter;
        private Color32[] m_mapTexture;
        private Color32[] m_mapWinterTexture;
        private Texture2D m_sourceTexture;
        private Texture m_largeForestTex;
        private Texture m_smallForestTex;
        private MapGeneration m_generation;
        private Coroutine m_generationCoroutine;

        public static MinimapVariantController instance => m_instance;

        private void Awake()
        {
            m_instance = this;
            m_minimap = GetComponent<Minimap>();
        }

        private void Start()
        {
            m_largeForestTex = m_minimap.m_mapLargeShader.GetTexture("_ForestTex");
            m_smallForestTex = m_minimap.m_mapSmallShader.GetTexture("_ForestTex");
            m_started = true;

            if (m_mapDataReady || m_minimap.m_hasGenerated)
                OnMapDataReady();
        }

        private void OnDestroy()
        {
            CancelGeneration();
            RevertTextures();
            if (m_instance == this)
                m_instance = null;
        }

        public void OnMapDataReady()
        {
            m_mapDataReady = true;
            if (!m_started || !m_minimap || !m_minimap.m_mapTexture)
                return;

            CancelGeneration();
            m_initialized = false;
            m_isWinter = false;
            m_sourceTexture = m_minimap.m_mapTexture;

            try
            {
                // Capture the actual vanilla map after generation or cache loading, not a reconstruction.
                m_mapTexture = m_sourceTexture.GetPixels32();
                m_generationCoroutine = StartCoroutine(GenerateWinterWorldMap());
            }
            catch (Exception error)
            {
                m_mapTexture = null;
                LogWarning($"Unable to prepare seasonal minimap textures:\n{error}");
            }
        }

        private void CancelGeneration()
        {
            if (m_generation != null)
                m_generation.Cancelled = true;
            m_generation = null;

            if (m_generationCoroutine != null)
                StopCoroutine(m_generationCoroutine);
            m_generationCoroutine = null;
        }

        public void RevertTextures()
        {
            if (!m_minimap)
                return;

            if (m_isWinter && ApplyMapTexture(m_mapTexture))
                m_isWinter = false;

            RestoreForestTextures();
        }

        public void UpdateColors() => UpdateColors(forceMapUpdate: false);

        private void UpdateColors(bool forceMapUpdate)
        {
            if (!m_initialized || !m_minimap)
                return;

            if (!controlMinimap.Value || !UseTextureControllers() || !SeasonState.IsActive)
            {
                RevertTextures();
                return;
            }

            Season season = seasonState.GetCurrentSeason();
            bool isWinter = season == Season.Winter;
            if ((forceMapUpdate || m_isWinter != isWinter) && ApplyMapTexture(isWinter ? m_mapWinterTexture : m_mapTexture))
                m_isWinter = isWinter;

            if (season == Season.Spring)
            {
                RestoreForestTextures();
                return;
            }

            Texture2D forestTex = GetSeasonalForestTex(season);
            if (forestTex == null)
                return;

            m_minimap.m_mapLargeShader.SetTexture("_ForestTex", forestTex);
            m_minimap.m_mapSmallShader.SetTexture("_ForestTex", forestTex);
        }

        private void RestoreForestTextures()
        {
            if (!m_started)
                return;

            if (m_minimap.m_mapLargeShader)
                m_minimap.m_mapLargeShader.SetTexture("_ForestTex", m_largeForestTex);
            if (m_minimap.m_mapSmallShader)
                m_minimap.m_mapSmallShader.SetTexture("_ForestTex", m_smallForestTex);
        }

        public Texture2D GetSeasonalForestTex(Season season)
        {
            return season switch
            {
                Season.Summer => Minimap_Summer_ForestTex,
                Season.Fall => Minimap_Fall_ForestTex,
                Season.Winter => Minimap_Winter_ForestTex,
                _ => m_largeForestTex as Texture2D,
            };
        }

        private bool ApplyMapTexture(Color32[] pixels)
        {
            if (!m_minimap || !m_sourceTexture || m_sourceTexture != m_minimap.m_mapTexture || pixels == null)
                return false;

            if (pixels.Length != m_sourceTexture.width * m_sourceTexture.height)
                return false;

            try
            {
                m_sourceTexture.SetPixels32(pixels);
                m_sourceTexture.Apply();
                Compatibility.MarketplaceCompat.UpdateMap();
                return true;
            }
            catch (Exception error)
            {
                LogWarning($"Error applying seasonal minimap texture ({pixels.Length} pixels):\n{error}");
                return false;
            }
        }

        public IEnumerator GenerateWinterWorldMap()
        {
            if (m_mapTexture == null || !m_sourceTexture || WorldGenerator.instance == null)
                yield break;

            if (m_generation != null)
                m_generation.Cancelled = true;

            WorldGenerator world = WorldGenerator.instance;
            Texture2D sourceTexture = m_sourceTexture;
            int textureSize = m_minimap.m_textureSize;
            float pixelSize = m_minimap.m_pixelSize;
            int center = textureSize / 2;
            float halfPixel = pixelSize / 2f;
            if (m_mapTexture.Length != textureSize * textureSize)
                yield break;

            Dictionary<Heightmap.Biome, Color32> winterColors = new Dictionary<Heightmap.Biome, Color32>();
            foreach (KeyValuePair<Heightmap.Biome, Color> entry in SeasonState.seasonBiomeSettings.SeasonalWinterMapColors)
                winterColors[entry.Key] = entry.Value;

            MapGeneration generation = new MapGeneration { Pixels = (Color32[])m_mapTexture.Clone() };
            m_generation = generation;
            Stopwatch stopwatch = Stopwatch.StartNew();

            // The worker only accesses captured world data and private managed buffers.
            Thread worker = new Thread(() =>
            {
                try
                {
                    for (int y = 0; y < textureSize && !generation.Cancelled; y++)
                    {
                        float wy = (y - center) * pixelSize + halfPixel;
                        for (int x = 0; x < textureSize; x++)
                        {
                            float wx = (x - center) * pixelSize + halfPixel;
                            if (winterColors.TryGetValue(world.GetBiome(wx, wy), out Color32 color))
                                generation.Pixels[y * textureSize + x] = color;
                        }
                    }
                }
                catch (Exception error)
                {
                    generation.Error = error;
                }
                finally
                {
                    generation.Completed = true;
                }
            }) { IsBackground = true, Name = "Seasons winter minimap" };
            worker.Start();

            yield return new WaitUntil(() => generation.Completed);

            if (generation.Cancelled || m_generation != generation || world != WorldGenerator.instance
                || !m_minimap || sourceTexture != m_minimap.m_mapTexture)
                yield break;

            m_generation = null;
            m_generationCoroutine = null;
            if (generation.Error != null)
            {
                LogWarning($"Unable to generate the winter minimap:\n{generation.Error}");
                yield break;
            }

            m_mapWinterTexture = generation.Pixels;
            m_initialized = true;
            LogInfo($"Minimap variant controller initialized in {stopwatch.Elapsed.TotalSeconds,-4:F2} seconds");
            UpdateColors(forceMapUpdate: true);
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
            if (UseTextureControllers() && !__instance.GetComponent<MinimapVariantController>())
                __instance.gameObject.AddComponent<MinimapVariantController>();
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.GenerateWorldMap))]
    public static class Minimap_GenerateWorldMap_MinimapContollerInit
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Minimap __instance)
        {
            __instance.GetComponent<MinimapVariantController>()?.OnMapDataReady();
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.TryLoadMinimapTextureData))]
    public static class Minimap_TryLoadMinimapTextureData_RefreshSeasonalMap
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Minimap __instance, bool __result)
        {
            if (__result)
                __instance.GetComponent<MinimapVariantController>()?.OnMapDataReady();
        }
    }
}
