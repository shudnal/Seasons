using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    public class MinimapVariantController : MonoBehaviour
    {
        private sealed class MapGeneration
        {
            public bool Cancelled;
            public Color32[] Pixels;
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
        private Dictionary<Heightmap.Biome, Color32> m_winterColors;

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

        private void Update()
        {
            if (m_started && m_mapDataReady && m_minimap && m_minimap.m_mapTexture
                && (m_sourceTexture != m_minimap.m_mapTexture || m_mapTexture == null
                    || m_mapTexture.Length != m_minimap.m_mapTexture.width * m_minimap.m_mapTexture.height))
                OnMapDataReady();
        }

        public void OnMapDataReady(bool nativePixelsChanged = false)
        {
            m_mapDataReady = true;
            if (!m_started || !m_minimap || !m_minimap.m_mapTexture)
                return;

            // Duplicate readiness notifications must not capture our winter output as the original.
            if (!nativePixelsChanged && m_sourceTexture == m_minimap.m_mapTexture && m_mapTexture != null
                && m_mapTexture.Length == m_sourceTexture.width * m_sourceTexture.height)
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
            if (m_mapDataReady && m_minimap && m_minimap.m_mapTexture
                && (m_sourceTexture != m_minimap.m_mapTexture || m_mapTexture == null
                    || m_mapTexture.Length != m_minimap.m_mapTexture.width * m_minimap.m_mapTexture.height))
                OnMapDataReady();

            if (!m_minimap)
                return;

            if (!controlMinimap.Value || !UseTextureControllers() || !SeasonState.IsActive)
            {
                RevertTextures();
                return;
            }

            if (!m_initialized)
                return;

            Dictionary<Heightmap.Biome, Color> configuredColors = SeasonState.seasonBiomeSettings.SeasonalWinterMapColors;
            bool colorsChanged = m_winterColors == null || m_winterColors.Count != configuredColors.Count;
            if (!colorsChanged)
                foreach (var color in configuredColors)
                    if (!m_winterColors.TryGetValue(color.Key, out Color32 previous) || !previous.Equals((Color32)color.Value))
                    {
                        colorsChanged = true;
                        break;
                    }
            if (colorsChanged)
            {
                CancelGeneration();
                m_initialized = false;
                m_generationCoroutine = StartCoroutine(GenerateWinterWorldMap());
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
            if (m_mapTexture == null || !m_sourceTexture || !m_minimap)
                yield break;

            if (m_generation != null)
                m_generation.Cancelled = true;

            Texture2D sourceTexture = m_sourceTexture;
            Color32[] sourcePixels = m_mapTexture;
            ZNet sourceNetwork = ZNet.instance;
            World sourceWorld = ZNet.m_world;
            int textureSize = m_minimap.m_textureSize;
            float pixelSize = m_minimap.m_pixelSize;
            if (sourcePixels.Length != textureSize * textureSize)
                yield break;

            // Register the request before waiting so a reload or world exit can cancel it.
            MapGeneration generation = new MapGeneration();
            m_generation = generation;

            bool IsCurrentRequest()
            {
                return !generation.Cancelled && m_generation == generation && this && m_minimap && sourceTexture
                    && sourceTexture == m_minimap.m_mapTexture && ReferenceEquals(sourcePixels, m_mapTexture)
                    && ReferenceEquals(sourceNetwork, ZNet.instance) && ReferenceEquals(sourceWorld, ZNet.m_world)
                    && textureSize == m_minimap.m_textureSize && pixelSize == m_minimap.m_pixelSize;
            }

            try
            {
                WorldGenerator world = null;
                while (IsCurrentRequest())
                {
                    world = WorldGenerator.instance;
                    if (world != null && (sourceWorld == null || ReferenceEquals(world.m_world, sourceWorld)))
                        break;

                    yield return null;
                }

                if (!IsCurrentRequest())
                    yield break;

                int center = textureSize / 2;
                float halfPixel = pixelSize / 2f;
                Dictionary<Heightmap.Biome, Color32> winterColors = new Dictionary<Heightmap.Biome, Color32>();
                foreach (KeyValuePair<Heightmap.Biome, Color> entry in SeasonState.seasonBiomeSettings.SeasonalWinterMapColors)
                    winterColors[entry.Key] = entry.Value;
                m_winterColors = winterColors;

                generation.Pixels = (Color32[])sourcePixels.Clone();
                Stopwatch stopwatch = Stopwatch.StartNew();

                // Game and compatibility biome lookups may use mutable world globals. Keep them
                // on the main thread, yielding in bounded batches and checking the world each time.
                for (int y = 0; y < textureSize; y++)
                {
                    if (!IsCurrentRequest() || world != WorldGenerator.instance)
                        yield break;

                    float wy = (y - center) * pixelSize + halfPixel;
                    for (int x = 0; x < textureSize; x++)
                    {
                        float wx = (x - center) * pixelSize + halfPixel;
                        if (winterColors.TryGetValue(world.GetBiome(wx, wy), out Color32 color))
                            generation.Pixels[y * textureSize + x] = color;
                    }
                    if (y % 8 == 7)
                        yield return null;
                }

                if (!IsCurrentRequest() || world != WorldGenerator.instance)
                    yield break;

                m_mapWinterTexture = generation.Pixels;
                m_initialized = true;
                LogInfo($"Minimap variant controller initialized in {stopwatch.Elapsed.TotalSeconds,-4:F2} seconds");
                UpdateColors(forceMapUpdate: true);
            }
            finally
            {
                generation.Cancelled = true;
                if (m_generation == generation)
                {
                    m_generation = null;
                    m_generationCoroutine = null;
                }
            }
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
            __instance.GetComponent<MinimapVariantController>()?.OnMapDataReady(nativePixelsChanged: true);
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.TryLoadMinimapTextureData))]
    public static class Minimap_TryLoadMinimapTextureData_RefreshSeasonalMap
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Minimap __instance, bool __result)
        {
            if (__result)
                __instance.GetComponent<MinimapVariantController>()?.OnMapDataReady(nativePixelsChanged: true);
        }
    }
}
