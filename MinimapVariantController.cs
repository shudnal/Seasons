using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
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

        private static readonly Dictionary<Color, Color> winterColors = new Dictionary<Color, Color>();
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
        }

        public void RevertTextures()
        {
            if (!m_initialized)
                return;

            SetMapTextures(winter:false, m_forestTex);
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
            SetMapTextures(season == Season.Winter, GetSeasonalForestTex(season));
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

        private void SetMapTextures(bool winter, Texture2D forestTex)
        {
            m_minimap.m_mapTexture.SetPixels32(winter ? m_mapWinterTexture : m_mapTexture);
            m_minimap.m_mapTexture.Apply();

            m_minimap.m_mapLargeShader.SetTexture("_ForestTex", forestTex);
            m_minimap.m_mapSmallShader.SetTexture("_ForestTex", forestTex);
        }

        public IEnumerator GenerateWinterWorldMap()
        {
            int num = m_minimap.m_textureSize / 2;
            float num2 = m_minimap.m_pixelSize / 2f;
            m_mapWinterTexture = new Color32[m_minimap.m_textureSize * m_minimap.m_textureSize];

            while (WorldGenerator.instance == null)
                yield return new WaitForSeconds(1f);

            var internalThread = new Thread(() =>
            {
                for (int i = 0; i < m_minimap.m_textureSize; i++)
                    for (int j = 0; j < m_minimap.m_textureSize; j++)
                    {
                        float wx = (j - num) * m_minimap.m_pixelSize + num2;
                        float wy = (i - num) * m_minimap.m_pixelSize + num2;
                        Heightmap.Biome biome = WorldGenerator.instance.GetBiome(wx, wy);
                        m_mapWinterTexture[i * m_minimap.m_textureSize + j] = GetPixelColor(biome);
                    }
            });

            internalThread.Start();
            while (internalThread.IsAlive == true)
            {
                yield return null;
            }

            m_mapTexture = m_minimap.m_mapTexture.GetPixels32();
            m_initialized = true;

            UpdateColors();
        }

        public Color GetPixelColor(Heightmap.Biome biome)
        {
            return biome switch
            {
                Heightmap.Biome.Meadows => GetWinterColor(m_minimap.m_meadowsColor),
                Heightmap.Biome.AshLands => m_minimap.m_ashlandsColor,
                Heightmap.Biome.BlackForest => GetWinterColor(m_minimap.m_blackforestColor),
                Heightmap.Biome.DeepNorth => m_minimap.m_deepnorthColor,
                Heightmap.Biome.Plains => GetWinterColor(m_minimap.m_heathColor),
                Heightmap.Biome.Swamp => GetWinterColor(m_minimap.m_swampColor),
                Heightmap.Biome.Mountain => m_minimap.m_mountainColor,
                Heightmap.Biome.Mistlands => GetWinterColor(m_minimap.m_mistlandsColor),
                Heightmap.Biome.Ocean => Color.white,
                _ => Color.white,
            };
        }

        public static Color GetWinterColor(Color color)
        {
            if (!winterColors.ContainsKey(color))
            {
                Color newColor = new Color(0.98f, 0.98f, 1f, color.a);
                winterColors[color] = new HSLColor(Color.Lerp(color, newColor, 0.6f)).ToRGBA();
            }

            return winterColors[color];
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
}
