using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static ClutterSystem;
using static Seasons.PrefabController;
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
            {  "instanced_heathflowers", 3 },
            {  "instanced_mistlands_grass_short", 0 },
            {  "instanced_mistlands_rockplant", 2 },
            {  "instanced_swamp_ormbunke", 1 },
            {  "instanced_swamp_grass", 3 },
        };

        private readonly Dictionary<Material, Dictionary<string, TextureVariants>> m_materialVariants = new Dictionary<Material, Dictionary<string, TextureVariants>>();

        private readonly Dictionary<Material, int> m_materialVariantOffset = new Dictionary<Material, int>();

        private readonly Dictionary<Material, Color> m_originalColors = new Dictionary<Material, Color>();

        public static ClutterVariantController instance => m_instance;

        private void Awake()
        {
            m_instance = this;
        }

        private void Start()
        {
            foreach (Clutter clutter in ClutterSystem.instance.m_clutter.Where(c => c.m_prefab != null))
            {
                if (!SeasonalTextureVariants.controllers.TryGetValue(Utils.GetPrefabName(clutter.m_prefab), out PrefabController controller))
                    continue;
               
                GameObject prefab = clutter.m_prefab;

                foreach (KeyValuePair<string, CachedRenderer> cachedRenderer in controller.renderersInHierarchy)
                {
                    if (cachedRenderer.Value.type != typeof(InstanceRenderer).ToString())
                        continue;

                    InstanceRenderer renderer = prefab.GetComponent<InstanceRenderer>();

                    if (renderer.m_material == null)
                        continue;

                    if (m_materialVariants.ContainsKey(renderer.m_material))
                        continue;

                    if (cachedRenderer.Key != renderer.transform.GetPath())
                        continue;

                    if (cachedRenderer.Value.name != renderer.name)
                        continue;

                    if (!cachedRenderer.Value.materials.TryGetValue(renderer.m_material.name, out CachedMaterial cachedMaterial))
                        continue;

                    if (cachedMaterial.shaderName != renderer.m_material.shader.name)
                        continue;

                    foreach (KeyValuePair<string, int> textureVariant in cachedMaterial.textureProperties)
                    {
                        if (!SeasonalTextureVariants.textures.ContainsKey(textureVariant.Value))
                            continue;

                        if (!m_materialVariants.TryGetValue(renderer.m_material, out Dictionary<string, TextureVariants> tv))
                        {
                            tv = new Dictionary<string, TextureVariants>();
                            m_materialVariants.Add(renderer.m_material, tv);
                        }

                        tv.Add(textureVariant.Key, SeasonalTextureVariants.textures[textureVariant.Value]);
                    }

                    m_materialVariantOffset.Add(renderer.m_material, prefabOffsets.GetValueSafe(prefab.name));
                }
            }

            base.enabled = m_materialVariants.Any(variant => variant.Value.Count > 0);

            UpdateColors();
        }

        private void OnEnable()
        {
            UpdateColors();
        }

        private void OnDisable()
        {
            RevertTextures();
        }
        
        private void OnDestroy()
        {
            RevertTextures();
        }

        public void RevertTextures()
        {
            foreach (KeyValuePair<Material, Dictionary<string, TextureVariants>> materialVariants in m_materialVariants)
                foreach (KeyValuePair<string, TextureVariants> texProp in materialVariants.Value)
                {
                    if (texProp.Value.HaveOriginalTexture())
                        materialVariants.Key.SetTexture(texProp.Key, texProp.Value.original);

                    if (m_originalColors.ContainsKey(materialVariants.Key))
                        materialVariants.Key.color = m_originalColors[materialVariants.Key];
                }
        }

        public void UpdateColors()
        {
            List<string> hideMaterialByName = hideGrassListInWinter.Value.Split(',').Select(p => p.Trim().ToLower()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
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

                        if (hideGrassInWinter.Value && seasonState.GetCurrentSeason() == Season.Winter && hideMaterialByName.Contains(materialVariants.Key.name.ToLower()))
                            materialVariants.Key.color = Color.clear;
                        else
                        {
                            materialVariants.Key.SetTexture(texProp.Key, texture);
                            if (materialVariants.Key.name == "grasscross_mistlands_short")
                                materialVariants.Key.color = GetGrassConfigColor(seasonState.GetCurrentSeason(), pos);
                            else
                                materialVariants.Key.color = m_originalColors[materialVariants.Key];
                        }
                    }
                }
        }

        private int GetCurrentMainVariant()
        {
            double factor = GetVariantFactor(EnvMan.instance.GetCurrentDay());
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

        public static void Init()
        {
            if (!UseTextureControllers())
                return;

            ClutterSystem.instance.transform.gameObject.AddComponent<ClutterVariantController>();
        }
    }
}
