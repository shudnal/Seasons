﻿using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static ClutterSystem;
using static Seasons.PrefabController;
using static Seasons.SeasonGrassSettings;
using static Seasons.Seasons;

namespace Seasons
{
    public class ClutterVariantController : MonoBehaviour
    {
        private static ClutterVariantController m_instance;

        private static readonly Dictionary<string, int> prefabOffsets = new Dictionary<string, int>()
        {
            {  "instanced_meadows_grass", 0 },
            {  "instanced_shrub", 1 },
            {  "instanced_meadows_grass_short", 2},
            {  "instanced_waterlilies", 3 },
            {  "instanced_forest_groundcover", 1 },
            {  "instanced_ormbunke", 2 },
            {  "instanced_forest_groundcover_brown", 3 },
            {  "instanced_heathgrass", 1 },
            {  "instanced_heathflowers", 2 },
            {  "grasscross_heath_green", 3 },
            {  "instanced_mistlands_grass_short", 0 },
            {  "instanced_mistlands_rockplant", 2 },
            {  "instanced_swamp_ormbunke", 1 },
            {  "instanced_swamp_grass", 3 },
        };

        private static readonly List<Color> s_tempColors = new List<Color>();

        private readonly Dictionary<Material, Dictionary<string, TextureVariants>> m_materialVariants = new Dictionary<Material, Dictionary<string, TextureVariants>>();
        private readonly Dictionary<Material, Dictionary<string, Color[]>> m_colorVariants = new Dictionary<Material, Dictionary<string, Color[]>>();

        private readonly Dictionary<Material, int> m_materialVariantOffset = new Dictionary<Material, int>();

        private readonly Dictionary<Material, Color> m_originalColors = new Dictionary<Material, Color>();

        public static ClutterVariantController instance => m_instance;
        
        private static Dictionary<Clutter, Tuple<float, float>> m_clutterDefaults = new Dictionary<Clutter, Tuple<float, float>>();

        private static readonly List<Renderer> s_tempRenderers = new List<Renderer>();

        private void Awake()
        {
            m_instance = this;
        }

        private void Start()
        {
            m_clutterDefaults.Clear();
            foreach (Clutter clutter in ClutterSystem.instance.m_clutter.Where(c => c.m_prefab != null))
            {
                m_clutterDefaults.Add(clutter, Tuple.Create(clutter.m_scaleMin, clutter.m_scaleMax));

                if (!texturesVariants.controllers.TryGetValue(Utils.GetPrefabName(clutter.m_prefab), out PrefabController controller))
                    continue;
               
                GameObject prefab = clutter.m_prefab;

                foreach (KeyValuePair<string, CachedRenderer> cachedRenderer in controller.renderersInHierarchy)
                {
                    if (cachedRenderer.Value.type == typeof(InstanceRenderer).ToString())
                    {
                        AddCachedInstanceRenderer(prefab, cachedRenderer.Key, cachedRenderer.Value);
                    }
                    else
                    {
                        string path = cachedRenderer.Key;
                        if (path.Contains(prefab.name))
                        {
                            path = cachedRenderer.Key.Substring(cachedRenderer.Key.IndexOf(prefab.name) + prefab.name.Length);
                            if (path.StartsWith("/"))
                                path = path.Substring(1);
                        }

                        string[] transformPath = path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                        s_tempRenderers.Clear();
                        CheckRenderersInHierarchy(prefab.transform, cachedRenderer.Value.type, transformPath, 0, s_tempRenderers);

                        foreach (Renderer renderer in s_tempRenderers)
                            AddMaterialVariants(prefab.name, renderer, cachedRenderer.Key, cachedRenderer.Value);
                    }
                }
            }

            base.enabled = m_materialVariants.Any(variant => variant.Value.Count > 0);
            
            UpdateColors();
        }

        private void AddCachedInstanceRenderer(GameObject prefab, string path, CachedRenderer cachedRenderer)
        {
            InstanceRenderer renderer = prefab.GetComponent<InstanceRenderer>();

            if (renderer.m_material == null)
                return;

            if (m_materialVariants.ContainsKey(renderer.m_material))
                return;

            if (path != renderer.transform.GetPath())
                return;

            if (cachedRenderer.name != renderer.name)
                return;

            if (!cachedRenderer.materials.TryGetValue(renderer.m_material.name, out CachedMaterial cachedMaterial))
                return;

            if (cachedMaterial.shaderName != renderer.m_material.shader.name)
                return;

            foreach (KeyValuePair<string, int> textureVariant in cachedMaterial.textureProperties)
            {
                if (!texturesVariants.textures.ContainsKey(textureVariant.Value))
                    continue;

                if (!m_materialVariants.TryGetValue(renderer.m_material, out Dictionary<string, TextureVariants> tv))
                {
                    tv = new Dictionary<string, TextureVariants>();
                    m_materialVariants.Add(renderer.m_material, tv);
                }

                tv.Add(textureVariant.Key, texturesVariants.textures[textureVariant.Value]);
            }

            foreach (KeyValuePair<string, string[]> colorVariant in cachedMaterial.colorVariants)
            {
                if (!m_colorVariants.TryGetValue(renderer.m_material, out Dictionary<string, Color[]> colorIndex))
                {
                    colorIndex = new Dictionary<string, Color[]>();
                    m_colorVariants.Add(renderer.m_material, colorIndex);
                }

                s_tempColors.Clear();
                foreach (string str in colorVariant.Value)
                {
                    if (ColorUtility.TryParseHtmlString(str, out Color color))
                        s_tempColors.Add(color);
                }
                colorIndex.Add(colorVariant.Key, s_tempColors.ToArray());
            }

            m_materialVariantOffset.Add(renderer.m_material, prefabOffsets.GetValueSafe(prefab.name));

        }

        private void CheckRenderersInHierarchy(Transform transform, string rendererType, string[] transformPath, int index, List<Renderer> renderers)
        {
            if (transformPath.Length == 0)
            {
                Renderer renderer = transform.GetComponent(rendererType) as Renderer;
                if (renderer != null)
                    renderers.Add(renderer);
            }
            else
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
        }

        private void AddMaterialVariants(string prefabName, Renderer renderer, string path, CachedRenderer cachedRenderer)
        {
            for (int i = 0; i < renderer.sharedMaterials.Length; i++)
            {
                Material material = renderer.sharedMaterials[i];

                if (material == null)
                    continue;

                if (material == null)
                    return;

                if (m_materialVariants.ContainsKey(material))
                    return;

                if (path != renderer.transform.GetPath())
                    return;

                if (cachedRenderer.name != renderer.name)
                    return;

                if (!cachedRenderer.materials.TryGetValue(material.name, out CachedMaterial cachedMaterial))
                    return;

                if (cachedMaterial.shaderName != material.shader.name)
                    return;

                foreach (KeyValuePair<string, int> textureVariant in cachedMaterial.textureProperties)
                {
                    if (!texturesVariants.textures.ContainsKey(textureVariant.Value))
                        continue;

                    if (!m_materialVariants.TryGetValue(material, out Dictionary<string, TextureVariants> tv))
                    {
                        tv = new Dictionary<string, TextureVariants>();
                        m_materialVariants.Add(material, tv);
                    }

                    tv.Add(textureVariant.Key, texturesVariants.textures[textureVariant.Value]);
                }

                foreach (KeyValuePair<string, string[]> colorVariant in cachedMaterial.colorVariants)
                {
                    if (!m_colorVariants.TryGetValue(material, out Dictionary<string, Color[]> colorIndex))
                    {
                        colorIndex = new Dictionary<string, Color[]>();
                        m_colorVariants.Add(material, colorIndex);
                    }

                    s_tempColors.Clear();
                    foreach (string str in colorVariant.Value)
                    {
                        if (ColorUtility.TryParseHtmlString(str, out Color color))
                            s_tempColors.Add(color);
                    }
                    colorIndex.Add(colorVariant.Key, s_tempColors.ToArray());
                }

                m_materialVariantOffset.Add(material, prefabOffsets.GetValueSafe(prefabName));
            }
        }

        private void OnEnable()
        {
            UpdateColors();
        }

        private void OnDisable()
        {
            RevertColors();
        }
        
        private void OnDestroy()
        {
            RevertColors();
        }

        public void RevertColors()
        {
            foreach (KeyValuePair<Material, Dictionary<string, TextureVariants>> materialVariants in m_materialVariants)
                foreach (KeyValuePair<string, TextureVariants> texProp in materialVariants.Value)
                {
                    if (!materialVariants.Key)
                        continue;

                    if (texProp.Value.HaveOriginalTexture())
                        materialVariants.Key.SetTexture(texProp.Key, texProp.Value.original);

                    if (m_originalColors.ContainsKey(materialVariants.Key))
                        materialVariants.Key.SetColor("_Color", m_originalColors[materialVariants.Key]);
                }

            foreach (KeyValuePair<Material, Dictionary<string, Color[]>> colorVariants in m_colorVariants)
                foreach (KeyValuePair<string, Color[]> colorProp in colorVariants.Value)
                {
                    if (!colorVariants.Key)
                        continue;

                    if (m_originalColors.ContainsKey(colorVariants.Key))
                        colorVariants.Key.SetColor(colorProp.Key, m_originalColors[colorVariants.Key]);
                }

            RevertGrass();
        }

        public void UpdateColors()
        {
            int variant = GetCurrentMainVariant();
            foreach (KeyValuePair<Material, Dictionary<string, TextureVariants>> materialVariants in m_materialVariants)
                foreach (KeyValuePair<string, TextureVariants> texProp in materialVariants.Value)
                {
                    int pos = (variant + m_materialVariantOffset.GetValueSafe(materialVariants.Key)) % seasonColorVariants;
                    if (texProp.Value.seasons.TryGetValue(seasonState.GetCurrentSeason(), out Dictionary<int, Texture2D> variants) && variants.TryGetValue(pos, out Texture2D texture))
                    {
                        if (!texProp.Value.HaveOriginalTexture())
                            texProp.Value.SetOriginalTexture(materialVariants.Key.GetTexture(texProp.Key));

                        if (!m_originalColors.ContainsKey(materialVariants.Key))
                            m_originalColors.Add(materialVariants.Key, materialVariants.Key.color);

                        materialVariants.Key.SetTexture(texProp.Key, texture);
                        if (materialVariants.Key.color == Color.clear)
                            materialVariants.Key.color = m_originalColors[materialVariants.Key];
                    }
                }

            foreach (KeyValuePair<Material, Dictionary<string, Color[]>> colorVariants in m_colorVariants)
                foreach (KeyValuePair<string, Color[]> colorProp in colorVariants.Value)
                {
                    if (!m_originalColors.ContainsKey(colorVariants.Key))
                        m_originalColors.Add(colorVariants.Key, colorVariants.Key.color);

                    int pos = (variant + m_materialVariantOffset.GetValueSafe(colorVariants.Key)) % seasonColorVariants;
                    colorVariants.Key.SetColor(colorProp.Key, colorProp.Value[(int)seasonState.GetCurrentSeason() * seasonsCount + pos]);
                }

            UpdateGrass();
        }

        public void UpdateGrass()
        {
            if (!controlGrass.Value)
                return;

            SeasonGrass seasonGrass = SeasonState.seasonGrassSettings.GetGrassSettings();

            ClutterSystem.instance.m_grassPatchSize = seasonGrass.m_grassPatchSize;
            ClutterSystem.instance.m_amountScale = seasonGrass.m_amountScale;

            foreach (Clutter clutter in ClutterSystem.instance.m_clutter.Where(c => c.m_prefab != null))
            {
                if (!m_clutterDefaults.TryGetValue(clutter, out Tuple<float, float> defaultSize))
                    continue;

                clutter.m_scaleMin = defaultSize.Item1 * seasonGrass.m_scaleMin;
                clutter.m_scaleMax = defaultSize.Item2 * seasonGrass.m_scaleMax;

                clutter.m_enabled = clutter.m_scaleMax != 0f;
            }

            ClutterSystem.instance.ClearAll();
        }

        public void RevertGrass()
        {
            if (!controlGrass.Value)
                return;

            ClutterSystem.instance.m_grassPatchSize = grassDefaultPatchSize.Value;
            ClutterSystem.instance.m_amountScale = grassDefaultAmountScale.Value;

            foreach (Clutter clutter in ClutterSystem.instance.m_clutter.Where(c => c.m_prefab != null))
            {
                clutter.m_enabled = true;

                if (!m_clutterDefaults.TryGetValue(clutter, out Tuple<float, float> defaultSize))
                    continue;

                clutter.m_scaleMin = defaultSize.Item1 * grassSizeDefaultScaleMin.Value;
                clutter.m_scaleMax = defaultSize.Item2 * grassSizeDefaultScaleMax.Value;
            }

            ClutterSystem.instance.ClearAll();
        }

        public IEnumerator UpdateDayState()
        {
            yield return new WaitForSeconds(fadeOnSeasonChangeDuration.Value);

            UpdateColors();
        }

        private int GetCurrentMainVariant()
        {
            double factor = GetVariantFactor(seasonState.GetCurrentWorldDay());
            if (factor < -0.5)
                return 0;
            else if (factor < 0)
                return 1;
            else if (factor < 0.5)
                return 2;
            else
                return 3;
        }

        private double GetVariantFactor(int day)
        {
            int seed = ZNet.m_world != null ? ZNet.m_world.m_seed : WorldGenerator.instance != null ? WorldGenerator.instance.GetSeed() : 0;
            double seedFactor = Math.Log10(Math.Abs(seed));
            return (Math.Sin(Math.Sign(seed) * seedFactor * day) + Math.Sin(Math.Sqrt(seedFactor) * Math.E * day) + Math.Sin(Math.PI * day)) / 2;
        }

        public static void Initialize()
        {
            if (!UseTextureControllers())
                return;

            ClutterSystem.instance.transform.gameObject.AddComponent<ClutterVariantController>();
        }

        public static void Reinitialize()
        {
            if (instance != null)
                Destroy(instance);

            m_instance = null;

            LogInfo("Reinitializing clutter colors");

            Initialize();
        }

    }
}
