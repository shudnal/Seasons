using HarmonyLib;
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
            {  c_forestBloomPrefabName, 4 },
            {  "instanced_heathgrass", 1 },
            {  "instanced_heathflowers", 2 },
            {  "grasscross_heath_green", 3 },
            {  "instanced_mistlands_grass_short", 0 },
            {  "instanced_mistlands_rockplant", 2 },
            {  "instanced_swamp_ormbunke", 1 },
            {  c_swampGrassBloomPrefabName, 2 },
            {  "instanced_swamp_grass", 3 },
        };

        private static readonly List<Color> s_tempColors = new List<Color>();

        private readonly Dictionary<Material, Dictionary<string, TextureVariants>> m_materialVariants = new Dictionary<Material, Dictionary<string, TextureVariants>>();
        private readonly Dictionary<Material, Dictionary<string, Color[]>> m_colorVariants = new Dictionary<Material, Dictionary<string, Color[]>>();

        private readonly Dictionary<Material, int> m_materialVariantOffset = new Dictionary<Material, int>();

        private readonly Dictionary<Material, Color> m_originalColors = new Dictionary<Material, Color>();

        private readonly Dictionary<string, Tuple<bool, float, float>> m_clutterDefaults = new Dictionary<string, Tuple<bool, float, float>>();

        public static ClutterVariantController Instance => m_instance;
        
        private static readonly List<Renderer> s_tempRenderers = new List<Renderer>();

        private static float s_grassPatchSize;
        private static float s_amountScale;

        public const string c_meadowsFlowersName = "meadows flowers";
        public const string c_meadowsFlowersPrefabName = "instanced_meadows_flowers";

        public const string c_forestBloomName = "forest groundcover bloom";
        public const string c_forestBloomPrefabName = "instanced_forest_groundcover_bloom";

        public const string c_swampGrassBloomName = "swampgrass bloom";
        public const string c_swampGrassBloomPrefabName = "instanced_swamp_grass_bloom";

        private static GameObject s_meadowsFlowers;
        private static GameObject s_forestBloom;
        private static GameObject s_swampBloom;

        public static Texture2D s_instanced_meadows_flowers = new Texture2D(64, 128, TextureFormat.RGBA32, false);
        public static Texture2D s_instanced_forest_groundcover_bloom = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        public static Texture2D s_instanced_swampgrass_bloom = new Texture2D(64, 64, TextureFormat.RGBA32, false);

        private void Awake()
        {
            m_instance = this;
        }

        private void Start()
        {
            s_grassPatchSize = ClutterSystem.instance.m_grassPatchSize;
            s_amountScale = ClutterSystem.instance.m_amountScale;

            m_clutterDefaults.Clear();
            foreach (Clutter clutter in ClutterSystem.instance.m_clutter.Where(c => c?.m_prefab != null))
            {
                string name = GetClutterName(clutter);
                if (!m_clutterDefaults.ContainsKey(name))
                    m_clutterDefaults.Add(name, Tuple.Create(clutter.m_enabled, clutter.m_scaleMin, clutter.m_scaleMax));
                
                if (!m_clutterDefaults.ContainsKey(clutter.m_prefab.name))
                    m_clutterDefaults.Add(clutter.m_prefab.name, Tuple.Create(clutter.m_enabled, clutter.m_scaleMin, clutter.m_scaleMax));

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
                            path = cachedRenderer.Key[(cachedRenderer.Key.IndexOf(prefab.name) + prefab.name.Length)..];
                            if (path.StartsWith("/"))
                                path = path[1..];
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
            m_instance = null;
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

            RevertSeasonalClutter();
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

                        if (texProp.Key == "_TerrainColorTex" && materialVariants.Value.ContainsKey("_MainTex"))
                            materialVariants.Key.SetTexture(texProp.Key, null);
                        else
                        {
                            if (CustomTextures.HaveCustomTexture(texProp.Value.originalName, seasonState.GetCurrentSeason(), variant, texProp.Value.properties, out Texture2D customTexture))
                                materialVariants.Key.SetTexture(texProp.Key, customTexture);
                            else
                                materialVariants.Key.SetTexture(texProp.Key, texture);
                        }

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
            {
                RevertGrass();
                return;
            }

            SeasonGrass seasonGrass = SeasonState.seasonGrassSettings.GetGrassSettings();

            ClutterSystem.instance.m_grassPatchSize = seasonGrass.m_grassPatchSize;
            ClutterSystem.instance.m_amountScale = seasonGrass.m_amountScale;

            foreach (Clutter clutter in ClutterSystem.instance.m_clutter.Where(c => c?.m_prefab != null))
            {
                if (!ControlGrassSize(clutter.m_prefab))
                    continue;

                if (!m_clutterDefaults.TryGetValue(GetClutterName(clutter), out Tuple<bool, float, float> defaultState))
                    continue;

                clutter.m_scaleMin = defaultState.Item2 * seasonGrass.m_scaleMin;
                clutter.m_scaleMax = defaultState.Item3 * seasonGrass.m_scaleMax;

                clutter.m_enabled = defaultState.Item1 && clutter.m_scaleMax != 0f;
            }

            UpdateSeasonalClutter();

            ClutterSystem.instance.ClearAll();
        }

        public void RevertGrass()
        {
            ClutterSystem.instance.m_grassPatchSize = s_grassPatchSize;
            ClutterSystem.instance.m_amountScale = s_amountScale;

            foreach (Clutter clutter in ClutterSystem.instance.m_clutter.Where(c => c?.m_prefab != null))
            {
                if (!ControlGrassSize(clutter.m_prefab))
                    continue;

                if (!m_clutterDefaults.TryGetValue(GetClutterName(clutter), out Tuple<bool, float, float> defaultState))
                    continue;

                clutter.m_scaleMin = defaultState.Item2;
                clutter.m_scaleMax = defaultState.Item3;
                
                clutter.m_enabled = defaultState.Item1;
            }

            UpdateSeasonalClutter();

            ClutterSystem.instance.ClearAll();
        }

        public void UpdateSeasonalClutter()
        {
            Dictionary<string, bool> seasonalClutter = SeasonState.seasonClutterSettings.GetSeasonalClutterState();
            foreach (Clutter clutter in ClutterSystem.instance.m_clutter)
            {
                if (clutter == null)
                    continue;

                if (clutter.m_name != null && seasonalClutter.TryGetValue(clutter.m_name, out bool nameEnabled))
                    clutter.m_enabled = nameEnabled;
                else if (clutter.m_prefab != null && seasonalClutter.TryGetValue(clutter?.m_prefab?.name, out bool prefabEnabled))
                    clutter.m_enabled = prefabEnabled;
            }
        }

        public void RevertSeasonalClutter()
        {
            Dictionary<string, bool> seasonalClutter = SeasonState.seasonClutterSettings.GetSeasonalClutterState();
            foreach (Clutter clutter in ClutterSystem.instance.m_clutter)
            {
                if (clutter == null)
                    continue;

                if (clutter.m_name != null && seasonalClutter.ContainsKey(clutter.m_name))
                    clutter.m_enabled = false;
                else if (clutter.m_prefab != null && seasonalClutter.ContainsKey(clutter.m_prefab.name))
                    clutter.m_enabled = false;
            }
        }

        public IEnumerator UpdateDayState()
        {
            yield return new WaitForSeconds(fadeOnSeasonChangeDuration.Value);

            UpdateColors();
        }

        private int GetCurrentMainVariant()
        {
            double factor = GetVariantFactor(seasonState.GetCurrentWorldDay() / 2);
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
            
            if (seed == 0)
                seed = 1;

            double seedFactor = Math.Log10(Math.Abs(seed));
            return (Math.Sin(Math.Sign(seed) * seedFactor * day) + Math.Sin(Math.Sqrt(seedFactor) * Math.E * day) + Math.Sin(Math.PI * day)) / 2;
        }

        private static string GetClutterName(Clutter clutter)
        {
            if (clutter == null)
                return "";

            string name = clutter.m_name;
            string prefab = clutter.m_prefab?.name;

            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(prefab))
                return $"{name}_{prefab}";
            else if (!string.IsNullOrWhiteSpace(prefab))
                return prefab;

            return name;
        }

        internal static void AddSeasonalClutter()
        {
            AddMeadowsFlowers();

            AddForestBloom();

            AddSwampgrassBloom();
        }

        internal static void AddMeadowsFlowers()
        {
            if (ClutterSystem.instance.m_clutter.Any(clutter => clutter?.m_name == c_meadowsFlowersName || clutter?.m_prefab?.name == c_meadowsFlowersPrefabName))
                return;

            Clutter clutter = ClutterSystem.instance.m_clutter.Find(clutter => clutter?.m_name == "heath flowers" || clutter?.m_prefab?.name == "instanced_heathflowers");
            if (clutter == null)
                return;

            Clutter flowers = JsonUtility.FromJson<Clutter>(JsonUtility.ToJson(clutter));
            flowers.m_name = c_meadowsFlowersName;
            flowers.m_biome = Heightmap.Biome.Meadows;
            flowers.m_enabled = false;

            if (!s_meadowsFlowers)
            {
                s_meadowsFlowers = CustomPrefabs.InitPrefabClone(flowers.m_prefab, c_meadowsFlowersPrefabName);
                LoadTexture("instanced_meadows_flowers.png", ref s_instanced_meadows_flowers);

                InstanceRenderer renderer = s_meadowsFlowers.GetComponent<InstanceRenderer>();
                renderer.m_material = new Material(renderer.m_material)
                {
                    name = c_meadowsFlowersPrefabName
                };
                renderer.m_material.SetTexture("_MainTex", s_instanced_meadows_flowers);
            }
            
            flowers.m_prefab = s_meadowsFlowers;
            flowers.m_amount = 100;
            flowers.m_maxTilt = 25;

            ClutterSystem.instance.m_clutter.Add(flowers);
        }
        
        internal static void AddForestBloom()
        {
            if (ClutterSystem.instance.m_clutter.Any(clutter => clutter?.m_name == c_forestBloomName || clutter?.m_prefab?.name == c_forestBloomPrefabName))
                return;

            Clutter clutter = ClutterSystem.instance.m_clutter.Find(clutter => clutter?.m_name == "forest groundcover" || clutter?.m_prefab?.name == "instanced_forest_groundcover");
            if (clutter == null)
                return;

            Clutter bloom = JsonUtility.FromJson<Clutter>(JsonUtility.ToJson(clutter));
            bloom.m_name = c_forestBloomName;
            bloom.m_biome = Heightmap.Biome.BlackForest;
            bloom.m_enabled = false;

            if (!s_forestBloom)
            {
                s_forestBloom = CustomPrefabs.InitPrefabClone(bloom.m_prefab, c_forestBloomPrefabName);
                LoadTexture("instanced_forest_groundcover_bloom.png", ref s_instanced_forest_groundcover_bloom);

                InstanceRenderer renderer = s_forestBloom.GetComponent<InstanceRenderer>();
                renderer.m_material = new Material(renderer.m_material)
                {
                    name = c_forestBloomPrefabName
                };
                renderer.m_material.SetTexture("_MainTex", s_instanced_forest_groundcover_bloom);
            }

            bloom.m_prefab = s_forestBloom;
            bloom.m_fractalTresholdMin = 0;
            bloom.m_fractalTresholdMax = 0.5f;

            ClutterSystem.instance.m_clutter.Add(bloom);
        }

        internal static void AddSwampgrassBloom()
        {
            if (ClutterSystem.instance.m_clutter.Any(clutter => clutter?.m_name == c_swampGrassBloomName || clutter?.m_prefab?.name == c_swampGrassBloomPrefabName))
                return;

            Clutter clutter = ClutterSystem.instance.m_clutter.Find(clutter => clutter?.m_name == "swampgrass" || clutter?.m_prefab?.name == "instanced_swamp_grass");
            if (clutter == null)
                return;

            Clutter bloom = JsonUtility.FromJson<Clutter>(JsonUtility.ToJson(clutter));
            bloom.m_name = c_swampGrassBloomName;
            bloom.m_biome = Heightmap.Biome.Swamp;
            bloom.m_enabled = false;

            if (!s_swampBloom)
            {
                s_swampBloom = CustomPrefabs.InitPrefabClone(bloom.m_prefab, c_swampGrassBloomPrefabName);
                LoadTexture("instanced_swamp_grass_bloom.png", ref s_instanced_swampgrass_bloom);

                InstanceRenderer renderer = s_swampBloom.GetComponent<InstanceRenderer>();
                renderer.m_material = new Material(renderer.m_material)
                {
                    name = c_swampGrassBloomPrefabName
                };
                renderer.m_material.SetTexture("_MainTex", s_instanced_swampgrass_bloom);
            }

            bloom.m_prefab = s_swampBloom;
            bloom.m_fractalTresholdMin = 0;
            bloom.m_fractalTresholdMax = 0.5f;

            ClutterSystem.instance.m_clutter.Add(bloom);
        }

        public static void Initialize()
        {
            if (!UseTextureControllers())
                return;

            ClutterSystem.instance.transform.gameObject.AddComponent<ClutterVariantController>();
        }

        public static void Reinitialize()
        {
            if (Instance != null)
                Destroy(Instance);

            m_instance = null;

            LogInfo("Reinitializing clutter colors");

            Initialize();
        }

        [HarmonyPatch(typeof(ClutterSystem), nameof(ClutterSystem.Awake))]
        public static class ClutterSystem_Awake_AddSeasonalClutter
        {
            private static void Postfix()
            {
                AddSeasonalClutter();
            }
        }
    }
}
