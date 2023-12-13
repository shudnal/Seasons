using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;
using static Seasons.TextureSeasonVariants;

namespace Seasons
{
    public class VegetationVariantController : MonoBehaviour
    {
        private ZNetView m_nview;

        private double m_springFactor;
        private double m_summerFactor;
        private double m_fallFactor;
        private double m_winterFactor;
        private float m_mx;
        private float m_my;
        private double m_seasonSet = 0;

        private Dictionary<Material, List<SeasonalTextures>> m_materialVariants = new Dictionary<Material, List<SeasonalTextures>>();

        public void Init(PrefabControllerData controllerData)
        {
            if (controllerData.m_renderer == typeof(MeshRenderer))
                foreach (MeshRenderer renderer in gameObject.GetComponentsInChildren<MeshRenderer>())
                {
                    string transformPath = renderer.transform.GetPath();
                    transformPath = transformPath.Substring(transformPath.IndexOf(gameObject.name) + gameObject.name.Length);
                    if (!controllerData.GetMaterialTextures(transformPath, out List<MaterialTextures> materialTextures))
                        continue;
                    
                    List<Material> materials = new List<Material>();
                    renderer.GetMaterials(materials);

                    foreach (MaterialTextures materialTexture in materialTextures)
                        foreach (Material material in materials.Where(m => m.name.StartsWith(materialTexture.m_materialName) && m.shader.name == materialTexture.m_shader && !m_materialVariants.ContainsKey(m)))
                            m_materialVariants.Add(material, materialTexture.m_textures);
                }

            base.enabled = m_materialVariants.Count > 0;
        }

        public void Awake()
        {
            m_nview = gameObject.GetComponent<ZNetView>();
        }

        public void OnEnable()
        {
            if (m_mx == 0 && m_my == 0 && Minimap.instance != null)
            {
                Minimap.instance.WorldToMapPoint(transform.position, out m_mx, out m_my);
                UpdateFactors();
            }

            UpdateColors();
        }

        public void RevertTextures()
        {
            foreach (KeyValuePair<Material, List<SeasonalTextures>> materialVariants in m_materialVariants)
                foreach (SeasonalTextures st in materialVariants.Value)
                    if (st.HaveOriginalTexture())
                    {
                        materialVariants.Key.SetTexture(st.textureProperty, st.m_original);
                        LogInfo($"Reverting texture {st.textureProperty} of {materialVariants.Key.name}");
                    }
        }

        public void LateUpdate()
        {
            if (!modEnabled.Value)
                return;

            if (m_nview != null && !m_nview.IsValid())
                return;

            if (m_seasonSet < seasonState.seasonChanged)
            {
                m_seasonSet = seasonState.seasonChanged;
                UpdateColors();
            }

            if (recalculateNoise.Value)
            {
                UpdateFactors();
                int variant = GetCurrentVariant();
                foreach (KeyValuePair<Material, List<SeasonalTextures>> materialVariants in m_materialVariants)
                {
                    materialVariants.Key.color = variant == 0 ? testColor1.Value : variant == 1 ? testColor2.Value : variant == 2 ? testColor3.Value : Color.white;
                }
            }
        }

        public void UpdateColors()
        {
            int variant = GetCurrentVariant();
            foreach (KeyValuePair<Material, List<SeasonalTextures>> materialVariants in m_materialVariants)
                foreach (SeasonalTextures st in materialVariants.Value)
                    if (st.m_seasons.TryGetValue(seasonState.m_season, out Dictionary<int, Texture2D> variants) && variants.TryGetValue(variant, out Texture2D texture))
                    {
                        if (!st.HaveOriginalTexture())
                        {
                            LogInfo($"Setting original vegetation texture {materialVariants.Key}");
                            st.SetOriginalTexture(materialVariants.Key.GetTexture(st.textureProperty));
                        }

                        materialVariants.Key.SetTexture(st.textureProperty, texture);
                    }
        }

        private void UpdateFactors()
        {
            m_springFactor = GetNoise(m_mx, m_my);
            m_summerFactor = GetNoise(1 - m_mx, m_my);
            m_fallFactor = GetNoise(m_mx, 1 - m_my);
            m_winterFactor = GetNoise(1 - m_mx, 1 - m_my);
        }

        private int GetCurrentVariant()
        {
            switch (seasonState.m_season)
            {
                case Season.Spring:
                    return GetVariant(m_springFactor);
                case Season.Summer:
                    return GetVariant(m_summerFactor);
                case Season.Fall:
                    return GetVariant(m_fallFactor);
                case Season.Winter:
                    return GetVariant(m_winterFactor);
                default:
                    return GetVariant(m_springFactor);
            }
        }

        public static int GetVariant(double factor)
        {
            if (factor < 0.25)
                return 0;
            else if (factor < 0.5)
                return 1;
            else if (factor < 0.75)
                return 2;
            else
                return 3;
        }

        public static double GetNoise(float mx, float my)
        {
            float seed = WorldGenerator.instance != null ? Mathf.Log10(Math.Abs(WorldGenerator.instance.GetSeed())) : 0f;
            return Math.Round(Math.Pow(((double)Mathf.PerlinNoise(mx * noiseFrequency.Value + seed, my * noiseFrequency.Value - seed) +
                (double)Mathf.PerlinNoise(mx * 2 * noiseFrequency.Value - seed, my * 2 * noiseFrequency.Value + seed) * 0.5) / noiseDivisor.Value, noisePower.Value) * 20) / 20;
        }

    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.CreateObject))]
    public static class ZNetScene_CreateObject_VegetationVariantControllerInit
    {
        private static void Postfix(ref GameObject __result)
        {
            if (__result == null)
                return;

            if (!prefabControllers.TryGetValue(Utils.GetPrefabName(__result), out PrefabControllerData controllerData))
                return;

            __result.AddComponent<VegetationVariantController>().Init(controllerData);
        }
    }

}
