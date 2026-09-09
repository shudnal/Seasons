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
        private Minimap m_minimap;
        private static MinimapVariantController m_instance;

        private bool m_initialized;
        private Color32[] m_mapTexture;
        private Color32[] m_mapWinterTexture;
        private Texture2D m_forestTex;
        private bool m_isWinter;
        private volatile bool m_destroyed;

        public static MinimapVariantController instance => m_instance;

        private void Awake()
        {
            m_instance = this;
            m_minimap = GetComponent<Minimap>();
        }

        private void Start()
        {
            if (m_minimap == null || m_minimap.m_mapLargeShader == null)
                return;

            Texture forest = m_minimap.m_mapLargeShader.GetTexture("_ForestTex");
            if (forest == null)
                return;
            m_forestTex = new Texture2D(forest.width, forest.height, forest.graphicsFormat, UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
            Graphics.CopyTexture(forest, m_forestTex);
            StartCoroutine(GenerateWinterWorldMap());
        }

        private void OnDestroy()
        {
            m_destroyed = true;
            // The map is being torn down. Do not launch Marketplace's asynchronous territory
            // redraw from here, even when some of the old minimap textures are still alive.
            if (m_instance == this)
                m_instance = null;
            if (m_forestTex != null)
                Destroy(m_forestTex);
        }

        public void RevertTextures()
        {
            if (!m_initialized)
                return;

            bool wasWinter = m_isWinter;
            m_isWinter = false;
            if (!SetMapTextures(wasWinter, m_forestTex))
                m_isWinter = wasWinter;
        }

        public void UpdateColors()
        {
            UpdateColors(forceUpdate: false);
        }

        public void UpdateColors(bool forceUpdate)
        {
            if (!m_initialized || m_destroyed)
                return;

            if (!controlMinimap.Value)
            {
                RevertTextures();
                return;
            }

            Season season = seasonState.GetCurrentSeason();
            bool wasWinter = m_isWinter;
            m_isWinter = season == Season.Winter;
            if (!SetMapTextures(forceUpdate || wasWinter != m_isWinter, GetSeasonalForestTex(season)))
                m_isWinter = wasWinter;
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

        private bool SetMapTextures(bool updateTerrain, Texture2D forestTex)
        {
            if (m_destroyed || m_minimap == null || m_minimap != Minimap.instance ||
                m_minimap.m_mapTexture == null || m_minimap.m_mapLargeShader == null || m_minimap.m_mapSmallShader == null)
                return false;

            Color32[] colors = m_isWinter ? m_mapWinterTexture : m_mapTexture;
            try
            {
                if (updateTerrain)
                {
                    if (colors == null || colors.Length != m_minimap.m_mapTexture.width * m_minimap.m_mapTexture.height)
                        return false;
                    m_minimap.m_mapTexture.SetPixels32(colors);
                    m_minimap.m_mapTexture.Apply();
                    Compatibility.MarketplaceCompat.UpdateMap(m_minimap);
                }

                if (forestTex != null)
                {
                    m_minimap.m_mapLargeShader.SetTexture("_ForestTex", forestTex);
                    m_minimap.m_mapSmallShader.SetTexture("_ForestTex", forestTex);
                }
                return true;
            }
            catch (Exception exception)
            {
                LogWarning($"Error applying {(m_isWinter ? "winter" : "normal")} minimap colors ({colors?.Length ?? 0} pixels):\n{exception}");
                return false;
            }
        }

        public IEnumerator GenerateWinterWorldMap()
        {
            yield return new WaitUntil(() => WorldGenerator.instance != null || m_destroyed);
            if (m_destroyed || m_minimap == null)
                yield break;

            // Capture world references, dimensions and palettes on the main thread. A worker
            // from an unloading world must never read a new world's singletons or Unity UI.
            WorldGenerator generator = WorldGenerator.instance;
            int size = m_minimap.m_textureSize;
            int halfSize = size / 2;
            float pixelSize = m_minimap.m_pixelSize;
            float halfPixel = pixelSize / 2f;
            Dictionary<Heightmap.Biome, Color32> normalPalette = new Dictionary<Heightmap.Biome, Color32>();
            Dictionary<Heightmap.Biome, Color32> winterPalette = new Dictionary<Heightmap.Biome, Color32>();
            foreach (Heightmap.Biome biome in Enum.GetValues(typeof(Heightmap.Biome)))
            {
                normalPalette[biome] = m_minimap.GetPixelColor(biome);
                winterPalette[biome] = GetWinterPixelColor(biome);
            }

            Color32[] normalColors = new Color32[size * size];
            Color32[] winterColors = new Color32[size * size];
            Dictionary<Heightmap.Biome, List<int>> deferredColors = new Dictionary<Heightmap.Biome, List<int>>();
            Exception generationError = null;
            Stopwatch stopwatch = Stopwatch.StartNew();
            Thread worker = new Thread(() =>
            {
                try
                {
                    for (int row = 0; row < size && !m_destroyed; row++)
                    {
                        for (int column = 0; column < size; column++)
                        {
                            float x = (column - halfSize) * pixelSize + halfPixel;
                            float z = (row - halfSize) * pixelSize + halfPixel;
                            Heightmap.Biome biome = generator.GetBiome(x, z);
                            int index = row * size + column;
                            if (normalPalette.TryGetValue(biome, out Color32 normal) && winterPalette.TryGetValue(biome, out Color32 winter))
                            {
                                normalColors[index] = normal;
                                winterColors[index] = winter;
                            }
                            else
                            {
                                // Modded biome IDs need not be declared enum members. Defer their
                                // palette lookup to the main thread rather than painting them white.
                                if (!deferredColors.TryGetValue(biome, out List<int> indices))
                                {
                                    indices = new List<int>();
                                    deferredColors.Add(biome, indices);
                                }
                                indices.Add(index);
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    generationError = exception;
                }
            }) { IsBackground = true };

            worker.Start();
            yield return new WaitWhile(() => worker.IsAlive);
            worker.Join();
            if (m_destroyed || m_minimap == null || m_minimap != Minimap.instance)
                yield break;
            if (generationError != null)
            {
                LogWarning($"Could not generate seasonal minimap colors:\n{generationError}");
                yield break;
            }

            foreach (KeyValuePair<Heightmap.Biome, List<int>> entry in deferredColors)
            {
                Color32 normal = m_minimap.GetPixelColor(entry.Key);
                Color32 winter = GetWinterPixelColor(entry.Key);
                foreach (int index in entry.Value)
                {
                    normalColors[index] = normal;
                    winterColors[index] = winter;
                }
            }

            m_mapTexture = normalColors;
            m_mapWinterTexture = winterColors;
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
            if (!UseTextureControllers() || __instance.GetComponent<MinimapVariantController>() != null)
                return;
            __instance.gameObject.AddComponent<MinimapVariantController>();
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.GenerateWorldMap))]
    public static class Minimap_GenerateWorldMap_MinimapContollerInit
    {
        private static void Postfix()
        {
            if (UseTextureControllers())
                MinimapVariantController.instance?.UpdateColors(forceUpdate: true);
        }
    }
}
