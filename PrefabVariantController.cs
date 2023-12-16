using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static PrivilegeManager;
using static Seasons.PrefabController;
using static Seasons.Seasons;

namespace Seasons
{
    public class PrefabVariantController : MonoBehaviour
    {
        private ZNetView m_nview;

        public string m_prefabName;
        private double m_springFactor;
        private double m_summerFactor;
        private double m_fallFactor;
        private double m_winterFactor;
        private float m_mx;
        private float m_my;
        private double m_seasonSet = 0;

        private readonly Dictionary<Material, Dictionary<string, TextureVariants>> m_materialVariants = new Dictionary<Material, Dictionary<string, TextureVariants>>();

        public void Init(PrefabController controller)
        {
            m_prefabName = Utils.GetPrefabName(gameObject);

            if (controller.lodLevelMaterials.Count > 0)
                if (gameObject.TryGetComponent(out LODGroup lodGroup))
                {
                    LOD[] LODs = lodGroup.GetLODs();
                    for (int lodLevel = 0; lodLevel < lodGroup.lodCount; lodLevel++)
                    {
                        if (!controller.lodLevelMaterials.TryGetValue(lodLevel, out List<CachedRenderer> cachedRenderers))
                            continue;

                        LOD lod = LODs[lodLevel];

                        for (int i = 0; i < lod.renderers.Length; i++)
                        {
                            Renderer renderer = lod.renderers[i];
                            if (renderer == null)
                                continue;

                            foreach (CachedRenderer cachedRenderer in cachedRenderers.Where(cr => cr.type == renderer.GetType().Name && cr.name == renderer.name))
                                AddMaterialVariants(renderer, cachedRenderer);
                        }
                    }
                }

            foreach (KeyValuePair<string, CachedRenderer> rendererPath in controller.renderersInHierarchy)
            {
                Transform child = gameObject.transform.Find(rendererPath.Key);
                if (child == null && rendererPath.Key.Contains(m_prefabName))
                {
                    string path = rendererPath.Key.Substring(rendererPath.Key.IndexOf(m_prefabName) + m_prefabName.Length);
                    if (path.StartsWith("/"))
                        path = path.Substring(1);
                    
                    child = gameObject.transform.Find(path);
                }
                
                if (child == null)
                    continue;

                Renderer renderer = child.GetComponent(rendererPath.Value.type) as Renderer;
                if (renderer == null)
                    continue;

                AddMaterialVariants(renderer, rendererPath.Value);
            }

            if (controller.cachedRenderer != null)
            {
                Renderer renderer = gameObject.GetComponent(controller.cachedRenderer.type) as Renderer;
                if (renderer != null)
                    AddMaterialVariants(renderer, controller.cachedRenderer);
            }

            ToggleEnabled();
        }

        private void Awake()
        {
            m_nview = gameObject.GetComponent<ZNetView>();
        }

        private void OnEnable()
        {
            if (m_mx == 0 && m_my == 0 && Minimap.instance != null)
            {
                Minimap.instance.WorldToMapPoint(transform.position, out m_mx, out m_my);
                UpdateFactors();
            }

            UpdateColors();
        }

        private void LateUpdate()
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
                foreach (KeyValuePair<Material, Dictionary<string, TextureVariants>> materialVariants in m_materialVariants)
                {
                    materialVariants.Key.color = variant == 0 ? Color.red : variant == 1 ? Color.magenta : variant == 2 ? Color.blue : Color.white;
                }
            }
        }

        private void RevertTextures()
        {
            foreach (KeyValuePair<Material, Dictionary<string, TextureVariants>> materialVariants in m_materialVariants)
                foreach (KeyValuePair<string, TextureVariants> texVar in materialVariants.Value)
                    if (texVar.Value.HaveOriginalTexture())
                    {
                        materialVariants.Key.SetTexture(texVar.Key, texVar.Value.original);
                        LogInfo($"Reverting texture {texVar.Key} of {materialVariants.Key.name}");
                    }
        }

        private void UpdateColors()
        {
            int variant = GetCurrentVariant();
            foreach (KeyValuePair<Material, Dictionary<string, TextureVariants>> materialVariants in m_materialVariants)
                foreach (KeyValuePair<string, TextureVariants> texVar in materialVariants.Value)
                    if (texVar.Value.seasons.TryGetValue(seasonState.m_season, out Dictionary<int, Texture2D> variants) && variants.TryGetValue(variant, out Texture2D texture))
                    {
                        if (!texVar.Value.HaveOriginalTexture())
                        {
                            LogInfo($"Setting original vegetation texture {materialVariants.Key}");
                            texVar.Value.SetOriginalTexture(materialVariants.Key.GetTexture(texVar.Key));
                        }

                        materialVariants.Key.SetTexture(texVar.Key, texture);
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

        public void ToggleEnabled()
        {
            base.enabled = m_materialVariants.Count > 0;
        }

        public void AddMaterialVariants(Renderer renderer, CachedRenderer cachedRenderer)
        {
            List<Material> materials = new List<Material>();
            renderer.GetMaterials(materials);

            for (int m = 0; m < materials.Count; m++)
            {
                Material material = materials[m];

                if (material == null)
                    continue;

                foreach (KeyValuePair<string, CachedMaterial> cachedRendererMaterial in cachedRenderer.materials.Where(mat => mat.Value.textureProperties.Count > 0))
                {
                    if (!material.name.StartsWith(cachedRendererMaterial.Key) || !(material.shader.name == cachedRendererMaterial.Value.shaderName))
                        continue;

                    if (!m_materialVariants.TryGetValue(material, out Dictionary<string, TextureVariants> texVariants))
                    {
                        texVariants = new Dictionary<string, TextureVariants>();
                        m_materialVariants.Add(material, texVariants);
                    }

                    foreach (KeyValuePair<string, int> tex in cachedRendererMaterial.Value.textureProperties)
                        if (!texVariants.ContainsKey(tex.Key))
                            texVariants.Add(tex.Key, SeasonalTextureVariants.textures[tex.Value]);
                }
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
    public static class ZNetScene_CreateObject_PrefabVariantControllerInit
    {
        private static void Postfix(ref GameObject __result)
        {
            if (__result == null)
                return;

            if (!SeasonalTextureVariants.controllers.TryGetValue(Utils.GetPrefabName(__result), out PrefabController controller))
                return;

            __result.AddComponent<PrefabVariantController>().Init(controller);
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.SpawnProxyLocation))]
    public static class ZoneSystem_SpawnProxyLocation_PrefabVariantControllerInit
    {
        private static void Postfix(ref GameObject __result)
        {
            if (__result == null)
                return;

            if (!SeasonalTextureVariants.controllers.TryGetValue(Utils.GetPrefabName(__result), out PrefabController controller))
                return;

            __result.AddComponent<PrefabVariantController>().Init(controller);
        }
    }

    [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Start))]
    public static class MineRock5_Start_PrefabVariantControllerInit
    {
        private static void Postfix(MineRock5 __instance, MeshRenderer ___m_meshRenderer)
        {
            if (___m_meshRenderer == null)
                return;

            if (!__instance.TryGetComponent(out PrefabVariantController prefabVariantController))
                return;

            if (prefabVariantController.enabled)
                return;

            if (!SeasonalTextureVariants.controllers.TryGetValue(prefabVariantController.m_prefabName, out PrefabController controller))
                return;

            if (controller.cachedRenderer == null)
                return;

            prefabVariantController.AddMaterialVariants(___m_meshRenderer, controller.cachedRenderer);
            prefabVariantController.ToggleEnabled();
        }
    }

}
