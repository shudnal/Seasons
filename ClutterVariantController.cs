using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
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

        private readonly Dictionary<Material, Dictionary<string, TextureVariants>> m_materialVariants = new Dictionary<Material, Dictionary<string, TextureVariants>>();
        private double m_daySet = 0;
        private double m_seasonSet = 0;

        private readonly Dictionary<Material, int> m_materialVariantOffset = new Dictionary<Material, int>();

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
        }

        private void LateUpdate()
        {
            if (!modEnabled.Value)
                return;

            if (m_daySet < seasonState.dayChanged || m_seasonSet < seasonState.seasonChanged)
            {
                m_daySet = seasonState.dayChanged;
                m_seasonSet = seasonState.seasonChanged;
                UpdateColors();
            }
        }

        private void UpdateColors()
        {
            int variant = GetCurrentMainVariant();
            foreach (KeyValuePair<Material, Dictionary<string, TextureVariants>> materialVariants in m_materialVariants)
                foreach (KeyValuePair<string, TextureVariants> texProp in materialVariants.Value)
                {
                    if (texProp.Value.seasons.TryGetValue(seasonState.m_season, out Dictionary<int, Texture2D> variants) && variants.TryGetValue((variant + m_materialVariantOffset.GetValueSafe(materialVariants.Key)) % seasonColorVariants, out Texture2D texture))
                    {
                        if (!texProp.Value.HaveOriginalTexture())
                        {
                            LogInfo($"Setting original clutter texture {materialVariants.Key}");
                            texProp.Value.SetOriginalTexture(materialVariants.Key.GetTexture(texProp.Key));
                        }
                        materialVariants.Key.SetTexture(texProp.Key, texture);
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
            int seed = WorldGenerator.instance.GetSeed();
            double seedFactor = Math.Log10(Math.Abs(seed));
            return (Math.Sin(Math.Sign(seed) * seedFactor * day) + Math.Sin(Math.Sqrt(seedFactor) * Math.E * day) + Math.Sin(Math.PI * day)) / 2;
        }
        
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
    public static class ZoneSystem_Start_ClutterContollerInit
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            if (!modEnabled.Value)
                return;

            ClutterSystem.instance.transform.gameObject.AddComponent<ClutterVariantController>();
        }
    }
}
