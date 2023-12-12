using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using static ClutterSystem;
using static Seasons.Seasons;
using static Seasons.TextureSeasonVariants;


namespace Seasons
{
    public class ClutterVariantController : MonoBehaviour
    {
        private static ClutterVariantController m_instance;

        private Dictionary<Material, List<SeasonalTextures>> m_materialVariants = new Dictionary<Material, List<SeasonalTextures>>();
        private double m_daySet = 0;
        private double m_seasonSet = 0;

        private Dictionary<Material, int> m_materialVariantOffset = new Dictionary<Material, int>();

        private static Dictionary<string, int> prefabOffsets = new Dictionary<string, int>()
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

        public void Awake()
        {
            m_instance = this;
        }

        public void Start()
        {
            foreach (Clutter clutter in ClutterSystem.instance.m_clutter.Where(c => c.m_prefab != null))
            {
                if (!prefabControllers.TryGetValue(Utils.GetPrefabName(clutter.m_prefab), out PrefabControllerData controllerData))
                    continue;

                if (controllerData.m_renderer != typeof(InstanceRenderer))
                    continue;

                GameObject prefab = clutter.m_prefab;

                InstanceRenderer renderer = prefab.GetComponent<InstanceRenderer>();
                
                string transformPath = renderer.transform.GetPath();
                transformPath = transformPath.Substring(transformPath.IndexOf(prefab.name) + prefab.name.Length);

                if (!controllerData.GetMaterialTextures(transformPath, out List<MaterialTextures> materialTextures))
                    continue;

                foreach (MaterialTextures materialTexture in materialTextures)
                    if (renderer.m_material.name.StartsWith(materialTexture.m_materialName) && renderer.m_material.shader.name == materialTexture.m_shader && !m_materialVariants.ContainsKey(renderer.m_material))
                    {
                        m_materialVariants.Add(renderer.m_material, materialTexture.m_textures);
                        m_materialVariantOffset.Add(renderer.m_material, prefabOffsets.GetValueSafe(prefab.name));
                    }
            }

            base.enabled = m_materialVariants.Count > 0;
        }

        public void LateUpdate()
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

        public void UpdateColors()
        {
            int variant = GetCurrentMainVariant();
            foreach (KeyValuePair<Material, List<SeasonalTextures>> materialVariants in m_materialVariants)
                foreach (SeasonalTextures st in materialVariants.Value)
                    if (st.m_seasons.TryGetValue(seasonState.m_season, out Dictionary<int, Texture2D> variants) && variants.TryGetValue((variant + m_materialVariantOffset.GetValueSafe(materialVariants.Key)) % seasonColorVariants, out Texture2D texture))
                    {
                        if (!st.HaveOriginalTexture())
                        {
                            LogInfo($"Setting original clutter texture {materialVariants.Key}");
                            st.SetOriginalTexture(materialVariants.Key.GetTexture(st.textureProperty));
                        }
                        materialVariants.Key.SetTexture(st.textureProperty, texture);
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
