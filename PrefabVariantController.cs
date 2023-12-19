﻿using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
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

        public int m_myListIndex = -1;
        public static readonly List<PrefabVariantController> s_allControllers = new List<PrefabVariantController>();

        private readonly Dictionary<Renderer, Dictionary<int, Dictionary<string, TextureVariants>>> m_materialVariants = new Dictionary<Renderer, Dictionary<int, Dictionary<string, TextureVariants>>>();
        private readonly Dictionary<Renderer, Dictionary<int, Dictionary<string, Color[]>>> m_colorVariants = new Dictionary<Renderer, Dictionary<int, Dictionary<string, Color[]>>>();

        private static readonly MaterialPropertyBlock s_matBlock = new MaterialPropertyBlock();

        private const float noiseFrequency = 10000f;
        private const double noiseDivisor = 1.1;
        private const double noisePower = 1.3;

        public void Init(PrefabController controller, string prefabName = null)
        {
            if (String.IsNullOrEmpty(prefabName))
                m_prefabName = Utils.GetPrefabName(gameObject);
            else
                m_prefabName = prefabName;

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
                string path = rendererPath.Key;
                if (path.Contains(m_prefabName))
                {
                    path = rendererPath.Key.Substring(rendererPath.Key.IndexOf(m_prefabName) + m_prefabName.Length);
                    if (path.StartsWith("/"))
                        path = path.Substring(1);
                }

                string[] transformPath = path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                List<Renderer> renderers = new List<Renderer>();
                CheckRenderersInHierarchy(gameObject.transform, rendererPath.Value.type, transformPath, 0, renderers);

                foreach (Renderer renderer in renderers) 
                    AddMaterialVariants(renderer, rendererPath.Value);
            }

            if (controller.cachedRenderer != null)
            {
                Renderer renderer = gameObject.GetComponent(controller.cachedRenderer.type) as Renderer;
                if (renderer != null)
                    AddMaterialVariants(renderer, controller.cachedRenderer);
            }

            ToggleEnabled();
            UpdateColors();
        }

        private void Awake()
        {
            m_nview = gameObject.GetComponent<ZNetView>();
            s_allControllers.Add(this);
            m_myListIndex = s_allControllers.Count - 1;
        }

        private void OnEnable()
        {
            if (m_springFactor == 0 && m_summerFactor == 0 && m_fallFactor == 0 && m_winterFactor == 0)
            {
                Minimap.instance.WorldToMapPoint(transform.position, out float mx, out float my);
                UpdateFactors(mx, my);
            }

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

        private void RevertTextures()
        {
            foreach (KeyValuePair<Renderer, Dictionary<int, Dictionary<string, TextureVariants>>> materialVariants in m_materialVariants)
                foreach (KeyValuePair<int, Dictionary<string, TextureVariants>> materialIndex in materialVariants.Value)
                    materialVariants.Key.SetPropertyBlock(null);
        }

        public void UpdateColors()
        {
            if (m_nview != null && !m_nview.IsValid())
                return;

            if (!base.enabled)
                return;

            int variant = GetCurrentVariant();
            foreach (KeyValuePair<Renderer, Dictionary<int, Dictionary<string, TextureVariants>>> materialVariants in m_materialVariants)
                foreach (KeyValuePair<int, Dictionary<string, TextureVariants>> materialIndex in materialVariants.Value)
                    foreach (KeyValuePair<string, TextureVariants> texVar in materialIndex.Value)
                        if (texVar.Value.seasons.TryGetValue(seasonState.GetCurrentSeason(), out Dictionary<int, Texture2D> variants) && variants.TryGetValue(variant, out Texture2D texture))
                        {
                            materialVariants.Key.GetPropertyBlock(s_matBlock, materialIndex.Key);
                            s_matBlock.SetTexture(texVar.Key, texture);
                            materialVariants.Key.SetPropertyBlock(s_matBlock, materialIndex.Key);
                        }

            foreach (KeyValuePair<Renderer, Dictionary<int, Dictionary<string, Color[]>>> colorVariants in m_colorVariants)
                foreach (KeyValuePair<int, Dictionary<string, Color[]>> colorIndex in colorVariants.Value)
                    foreach (KeyValuePair<string, Color[]> colVar in colorIndex.Value)
                    {
                        colorVariants.Key.GetPropertyBlock(s_matBlock, colorIndex.Key);
                        s_matBlock.SetColor(colVar.Key, colVar.Value[(int)seasonState.GetCurrentSeason() * seasonsCount + variant]);
                        colorVariants.Key.SetPropertyBlock(s_matBlock, colorIndex.Key);
                    }
        }

        private void UpdateFactors(float m_mx, float m_my)
        {
            m_springFactor = GetNoise(m_mx, m_my);
            m_summerFactor = GetNoise(1 - m_mx, m_my);
            m_fallFactor = GetNoise(m_mx, 1 - m_my);
            m_winterFactor = GetNoise(1 - m_mx, 1 - m_my);
        }

        private int GetCurrentVariant()
        {
            switch (seasonState.GetCurrentSeason())
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
            base.enabled = Minimap.instance != null && (m_materialVariants.Count > 0 || m_colorVariants.Count > 0);
        }

        public void AddMaterialVariants(Renderer renderer, CachedRenderer cachedRenderer)
        {
            for (int i = 0; i < renderer.sharedMaterials.Length; i++)
            {
                Material material = renderer.sharedMaterials[i];

                if (material == null)
                    continue;

                foreach (KeyValuePair<string, CachedMaterial> cachedRendererMaterial in cachedRenderer.materials)
                {
                    if (cachedRendererMaterial.Value.textureProperties.Count > 0)
                    {
                        if (material.name.StartsWith(cachedRendererMaterial.Key) && (material.shader.name == cachedRendererMaterial.Value.shaderName))
                        {
                            if (!m_materialVariants.TryGetValue(renderer, out Dictionary<int, Dictionary<string, TextureVariants>> materialIndex))
                            {
                                materialIndex = new Dictionary<int, Dictionary<string, TextureVariants>>();
                                m_materialVariants.Add(renderer, materialIndex);
                            }

                            if (!materialIndex.TryGetValue(i, out Dictionary<string, TextureVariants> texVariants))
                            {
                                texVariants = new Dictionary<string, TextureVariants>();
                                materialIndex.Add(i, texVariants);
                            }

                            foreach (KeyValuePair<string, int> tex in cachedRendererMaterial.Value.textureProperties)
                                if (!texVariants.ContainsKey(tex.Key))
                                    texVariants.Add(tex.Key, SeasonalTextureVariants.textures[tex.Value]);
                        }
                    }

                    if (cachedRendererMaterial.Value.colorVariants.Count > 0)
                    {
                        if (material.name.StartsWith(cachedRendererMaterial.Key) && (material.shader.name == cachedRendererMaterial.Value.shaderName))
                        {
                            if (!m_colorVariants.TryGetValue(renderer, out Dictionary<int, Dictionary<string, Color[]>> colorIndex))
                            {
                                colorIndex = new Dictionary<int, Dictionary<string, Color[]>>();
                                m_colorVariants.Add(renderer, colorIndex);
                            }

                            if (!colorIndex.TryGetValue(i, out Dictionary<string, Color[]> colorVariants))
                            {
                                colorVariants = new Dictionary<string, Color[]>();
                                colorIndex.Add(i, colorVariants);
                            }

                            foreach (KeyValuePair<string, string[]> tex in cachedRendererMaterial.Value.colorVariants)
                                if (!colorVariants.ContainsKey(tex.Key))
                                {
                                    List<Color> colors = new List<Color>();
                                    foreach (string str in tex.Value)
                                    {
                                        if (!ColorUtility.TryParseHtmlString(str, out Color color))
                                            return;

                                        colors.Add(color);
                                    }
                                    colorVariants.Add(tex.Key, colors.ToArray());
                                }
                        }
                    }
                }
            }
        }

        private void CheckRenderersInHierarchy(Transform transform, string rendererType, string[] transformPath, int index, List<Renderer> renderers)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);

                if (child.name == transformPath[index])
                {
                    if (index == transformPath.Length - 1)
                    {
                        Renderer renderer = child.GetComponent(rendererType) as Renderer;
                        if (renderer != null)
                            renderers.Add(renderer);
                    }
                    else
                    {
                        CheckRenderersInHierarchy(child, rendererType, transformPath, index + 1, renderers);
                    }
                }
            }
        }

        public static void UpdatePrefabColors()
        {
            foreach (PrefabVariantController controller in s_allControllers)
                controller.UpdateColors();
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
            return Math.Round(Math.Pow(((double)Mathf.PerlinNoise(mx * noiseFrequency + seed, my * noiseFrequency - seed) +
                (double)Mathf.PerlinNoise(mx * 2 * noiseFrequency - seed, my * 2 * noiseFrequency + seed) * 0.5) / noiseDivisor, noisePower) * 20) / 20;
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
        private static void Postfix(MineRock5 __instance, MeshRenderer ___m_meshRenderer, ZNetView ___m_nview)
        {
            if (___m_meshRenderer == null)
                return;

            if (__instance.TryGetComponent(out PrefabVariantController prefabVariantController) && prefabVariantController.enabled)
                return;

            if (prefabVariantController == null)
            {
                if (___m_nview == null || !___m_nview.IsValid())
                    return;

                string prefabName = ZNetScene.instance.GetPrefab(___m_nview.GetZDO().GetPrefab()).name;

                if (!SeasonalTextureVariants.controllers.TryGetValue(prefabName, out PrefabController controller))
                    return;

                __instance.gameObject.AddComponent<PrefabVariantController>().Init(controller, prefabName);
            }
            else
            {
                if (!SeasonalTextureVariants.controllers.TryGetValue(prefabVariantController.m_prefabName, out PrefabController controller))
                    return;
                
                if (controller.cachedRenderer == null)
                    return;

                prefabVariantController.AddMaterialVariants(___m_meshRenderer, controller.cachedRenderer);
                prefabVariantController.ToggleEnabled();
                prefabVariantController.UpdateColors();
            }
        }
    }

}
