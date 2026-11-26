using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        private static readonly Dictionary<string, GameObject> s_shieldedPrefabs = new Dictionary<string, GameObject>();

        private static readonly List<Color> s_tempColors = new List<Color>();

        private readonly Dictionary<Material, Dictionary<string, TextureVariants>> m_materialVariants = new Dictionary<Material, Dictionary<string, TextureVariants>>();
        private readonly Dictionary<Material, Dictionary<string, Color[]>> m_colorVariants = new Dictionary<Material, Dictionary<string, Color[]>>();

        private readonly Dictionary<Material, int> m_materialVariantOffset = new Dictionary<Material, int>();

        private readonly Dictionary<Material, Dictionary<string, Color>> m_originalColors = new Dictionary<Material, Dictionary<string, Color>>();
        private readonly Dictionary<Tuple<Material, CachedMaterial, string>, Material> m_contextMaterials = new Dictionary<Tuple<Material, CachedMaterial, string>, Material>();
        private readonly Dictionary<InstanceRenderer, Tuple<Material, Material>> m_instanceMaterials = new Dictionary<InstanceRenderer, Tuple<Material, Material>>();
        private readonly Dictionary<Renderer, Tuple<Material[], Material[]>> m_rendererMaterials = new Dictionary<Renderer, Tuple<Material[], Material[]>>();
        private ClutterSystem m_clutterSystem;
        private bool m_started;

        private readonly Dictionary<Clutter, Tuple<bool, float, float>> m_clutterDefaults = new Dictionary<Clutter, Tuple<bool, float, float>>();
        private readonly HashSet<GameObject> m_registeredClutterPrefabs = new HashSet<GameObject>();

        public static ClutterVariantController Instance => m_instance;
        
        private static readonly List<Renderer> s_tempRenderers = new List<Renderer>();

        private float s_grassPatchSize;
        private float s_amountScale;

        public const string c_meadowsFlowersName = "meadows flowers";
        public const string c_meadowsFlowersPrefabName = "instanced_meadows_flowers";

        public const string c_forestBloomName = "forest groundcover bloom";
        public const string c_forestBloomPrefabName = "instanced_forest_groundcover_bloom";

        public const string c_swampGrassBloomName = "swampgrass bloom";
        public const string c_swampGrassBloomPrefabName = "instanced_swamp_grass_bloom";

        public const string c_shieldedGrassSuffix = "_inshield_seasons";

        private static GameObject s_meadowsFlowers;
        private static GameObject s_forestBloom;
        private static GameObject s_swampBloom;

        public static Texture2D s_instanced_meadows_flowers = new Texture2D(64, 128, TextureFormat.RGBA32, false);
        public static Texture2D s_instanced_forest_groundcover_bloom = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        public static Texture2D s_instanced_swampgrass_bloom = new Texture2D(64, 64, TextureFormat.RGBA32, false);

        private void Awake()
        {
            m_instance = this;
            m_clutterSystem = GetComponent<ClutterSystem>();
        }

        private void Start()
        {
            s_grassPatchSize = ClutterSystem.instance.m_grassPatchSize;
            s_amountScale = ClutterSystem.instance.m_amountScale;

            foreach (Clutter clutter in m_clutterSystem.m_clutter)
                CaptureClutter(clutter);

            m_started = true;
            UpdateColors();
        }

        private void CaptureClutter(Clutter clutter)
        {
            if (clutter?.m_prefab == null || m_clutterDefaults.ContainsKey(clutter))
                return;

            m_clutterDefaults.Add(clutter, Tuple.Create(clutter.m_enabled, clutter.m_scaleMin, clutter.m_scaleMax));

            if (!m_registeredClutterPrefabs.Add(clutter.m_prefab) ||
                !texturesVariants.controllers.TryGetValue(Utils.GetPrefabName(clutter.m_prefab), out PrefabController controller))
                return;
           
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

        internal void RefreshExternalClutter()
        {
            if (!m_started || !m_clutterSystem || m_clutterSystem != ClutterSystem.instance)
                return;

            // EWD publishes new entries, but restores the original entry objects when disabled.
            // Restore removed entries before forgetting their unscaled values.
            var current = new HashSet<Clutter>(m_clutterSystem.m_clutter.Where(c => c?.m_prefab != null));
            if (current.SetEquals(m_clutterDefaults.Keys))
                return;

            foreach (Clutter clutter in m_clutterDefaults.Keys.Where(c => !current.Contains(c)).ToArray())
            {
                var defaults = m_clutterDefaults[clutter];
                clutter.m_enabled = defaults.Item1;
                clutter.m_scaleMin = defaults.Item2;
                clutter.m_scaleMax = defaults.Item3;
                m_clutterDefaults.Remove(clutter);
            }

            foreach (Clutter clutter in m_clutterSystem.m_clutter)
                CaptureClutter(clutter);

            // EWD already requests a clutter refresh after publishing its new entries.
            UpdateColors(rebuildGrass: false);
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
            ApplyMaterialBindings(originals: true);
            foreach (Material material in m_contextMaterials.Values)
                Destroy(material);
            m_contextMaterials.Clear();
            s_tempRenderers.Clear();
            s_tempColors.Clear();
            isAnyShieldActive = false;
            InstanceRenderer_AddInstance_PreventInProtectedArea.ResetWorldState();
            if (m_instance == this)
                m_instance = null;
        }

        private Material GetContextMaterial(Material original, CachedMaterial context, string prefabName)
        {
            var key = Tuple.Create(original, context, prefabName);
            if (!m_contextMaterials.TryGetValue(key, out Material material))
                m_contextMaterials[key] = material = new Material(original) { name = original.name };
            return material;
        }

        private bool ApplyMaterialBindings(bool originals)
        {
            bool changed = false;
            foreach (var binding in m_instanceMaterials)
                if (binding.Key)
                {
                    Material current = binding.Key.m_material;
                    Material target = originals ? binding.Value.Item1 : binding.Value.Item2;
                    if (current != target && (current == binding.Value.Item2 || !originals && current == binding.Value.Item1))
                    {
                        binding.Key.m_material = target;
                        changed = true;
                    }
                }
            foreach (var binding in m_rendererMaterials)
                if (binding.Key)
                {
                    Material[] current = binding.Key.sharedMaterials;
                    bool rendererChanged = false;
                    for (int i = 0; i < current.Length && i < binding.Value.Item1.Length; i++)
                    {
                        Material target = originals ? binding.Value.Item1[i] : binding.Value.Item2[i];
                        if (current[i] != target && (current[i] == binding.Value.Item2[i] || !originals && current[i] == binding.Value.Item1[i]))
                        {
                            current[i] = target;
                            rendererChanged = true;
                        }
                    }
                    if (rendererChanged)
                    {
                        binding.Key.sharedMaterials = current;
                        changed = true;
                    }
                }
            return changed;
        }

        private static bool SetTextureIfChanged(Material material, string property, Texture value)
        {
            if (material.GetTexture(property) == value)
                return false;
            material.SetTexture(property, value);
            return true;
        }

        private static bool SetColorIfChanged(Material material, string property, Color value)
        {
            if (material.GetColor(property).Equals(value))
                return false;
            material.SetColor(property, value);
            return true;
        }

        private void AddCachedInstanceRenderer(GameObject prefab, string path, CachedRenderer cachedRenderer)
        {
            InstanceRenderer renderer = prefab.GetComponent<InstanceRenderer>();

            if (!renderer || renderer.m_material == null)
                return;

            if (m_instanceMaterials.ContainsKey(renderer))
                return;

            if (path != renderer.transform.GetPath())
                return;

            if (cachedRenderer.name != renderer.name)
                return;

            if (!cachedRenderer.materials.TryGetValue(renderer.m_material.name, out CachedMaterial cachedMaterial))
                return;

            if (cachedMaterial.shaderName != renderer.m_material.shader.name)
                return;

            Material material = GetContextMaterial(renderer.m_material, cachedMaterial, prefab.name);
            m_instanceMaterials.Add(renderer, Tuple.Create(renderer.m_material, material));
            if (m_materialVariantOffset.ContainsKey(material))
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

            m_materialVariantOffset.Add(material, prefabOffsets.GetValueSafe(prefab.name));

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

                if (path != renderer.transform.GetPath())
                    return;

                if (cachedRenderer.name != renderer.name)
                    return;

                if (!cachedRenderer.materials.TryGetValue(material.name, out CachedMaterial cachedMaterial))
                    continue;

                if (cachedMaterial.shaderName != material.shader.name)
                    continue;

                if (!m_rendererMaterials.TryGetValue(renderer, out var binding))
                {
                    Material[] originals = renderer.sharedMaterials;
                    binding = Tuple.Create(originals, (Material[])originals.Clone());
                    m_rendererMaterials.Add(renderer, binding);
                }
                material = GetContextMaterial(material, cachedMaterial, prefabName);
                binding.Item2[i] = material;
                if (m_materialVariantOffset.ContainsKey(material))
                    continue;

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

        public void RevertColors() => RevertColors(rebuildGrass: true);

        private void RevertColors(bool rebuildGrass)
        {
            bool changed = ApplyMaterialBindings(originals: true);
            foreach (KeyValuePair<Material, Dictionary<string, TextureVariants>> materialVariants in m_materialVariants)
                foreach (KeyValuePair<string, TextureVariants> texProp in materialVariants.Value)
                {
                    if (!materialVariants.Key)
                        continue;

                    if (texProp.Value.HaveOriginalTexture())
                        changed |= SetTextureIfChanged(materialVariants.Key, texProp.Key, texProp.Value.original);

                }

            foreach (var material in m_originalColors)
                if (material.Key)
                    foreach (var property in material.Value)
                        changed |= SetColorIfChanged(material.Key, property.Key, property.Value);

            ApplyGrassSettings(seasonal: false, changed, rebuildGrass);

        }

        public void UpdateColors() => UpdateColors(rebuildGrass: true);

        private void UpdateColors(bool rebuildGrass)
        {
            if (!m_started || !m_clutterSystem || m_clutterSystem != ClutterSystem.instance || texturesVariants.IsUpdating)
                return;
            if (!enabled || !UseTextureControllers() || !SeasonState.IsActive)
            {
                RevertColors(rebuildGrass);
                return;
            }

            int variant = GetCurrentMainVariant();
            bool changed = ApplyMaterialBindings(originals: false);
            foreach (KeyValuePair<Material, Dictionary<string, TextureVariants>> materialVariants in m_materialVariants)
                foreach (KeyValuePair<string, TextureVariants> texProp in materialVariants.Value)
                {
                    if (!materialVariants.Key)
                        continue;
                    int pos = (variant + m_materialVariantOffset.GetValueSafe(materialVariants.Key)) % seasonColorVariants;
                    if (texProp.Value.seasons.TryGetValue(seasonState.GetCurrentSeason(), out Dictionary<int, Texture2D> variants) && variants.TryGetValue(pos, out Texture2D texture))
                    {
                        if (!texProp.Value.HaveOriginalTexture())
                            texProp.Value.SetOriginalTexture(materialVariants.Key.GetTexture(texProp.Key));

                        RememberColor(materialVariants.Key, "_Color");

                        if (texProp.Key == "_TerrainColorTex" && materialVariants.Value.ContainsKey("_MainTex"))
                            changed |= SetTextureIfChanged(materialVariants.Key, texProp.Key, null);
                        else
                        {
                            if (CustomTextures.HaveCustomTexture(texProp.Value.originalName, seasonState.GetCurrentSeason(), variant, texProp.Value.properties, out Texture2D customTexture))
                                changed |= SetTextureIfChanged(materialVariants.Key, texProp.Key, customTexture);
                            else
                                changed |= SetTextureIfChanged(materialVariants.Key, texProp.Key, texture);
                        }

                        if (materialVariants.Key.color == Color.clear)
                            changed |= SetColorIfChanged(materialVariants.Key, "_Color", m_originalColors[materialVariants.Key]["_Color"]);
                    }
                }

            foreach (KeyValuePair<Material, Dictionary<string, Color[]>> colorVariants in m_colorVariants)
                foreach (KeyValuePair<string, Color[]> colorProp in colorVariants.Value)
                {
                    if (!colorVariants.Key)
                        continue;
                    RememberColor(colorVariants.Key, colorProp.Key);

                    int pos = (variant + m_materialVariantOffset.GetValueSafe(colorVariants.Key)) % seasonColorVariants;
                    changed |= SetColorIfChanged(colorVariants.Key, colorProp.Key, colorProp.Value[(int)seasonState.GetCurrentSeason() * seasonsCount + pos]);
                }

            ApplyGrassSettings(seasonal: true, changed, rebuildGrass);
        }

        private void RememberColor(Material material, string property)
        {
            if (!m_originalColors.TryGetValue(material, out Dictionary<string, Color> colors))
                m_originalColors[material] = colors = new Dictionary<string, Color>();
            if (!colors.ContainsKey(property))
                colors[property] = material.GetColor(property);
        }

        public static void UpdateGrassOnSettingChanged()
        {
            if (Instance == null || ClutterSystem.instance == null)
                return;

            Instance.UpdateGrass();
        }

        public void UpdateGrass() =>
            ApplyGrassSettings(enabled && UseTextureControllers() && SeasonState.IsActive, changed: false, rebuildGrass: true);

        private void ApplyGrassSettings(bool seasonal, bool changed, bool rebuildGrass)
        {
            if (!m_started || !m_clutterSystem || m_clutterSystem != ClutterSystem.instance)
                return;

            changed |= RefreshShieldActiveState(seasonal);
            SeasonGrass grass = seasonal && controlGrass.Value ? SeasonState.seasonGrassSettings.GetGrassSettings() : null;
            float patchSize = grass?.m_grassPatchSize ?? s_grassPatchSize;
            float amountScale = grass?.m_amountScale ?? s_amountScale;
            if (!m_clutterSystem.m_grassPatchSize.Equals(patchSize))
            {
                m_clutterSystem.m_grassPatchSize = patchSize;
                changed = true;
            }
            if (!m_clutterSystem.m_amountScale.Equals(amountScale))
            {
                m_clutterSystem.m_amountScale = amountScale;
                changed = true;
            }

            Dictionary<string, bool> seasonalClutter = seasonal ? SeasonState.seasonClutterSettings.GetSeasonalClutterState() : null;
            foreach (Clutter clutter in m_clutterSystem.m_clutter)
            {
                if (clutter == null)
                    continue;

                bool wanted = clutter.m_enabled;
                if (m_clutterDefaults.TryGetValue(clutter, out var defaults))
                {
                    if (clutter.m_prefab && ControlGrassSize(clutter.m_prefab))
                    {
                        float min = defaults.Item2 * (grass?.m_scaleMin ?? 1f);
                        float max = defaults.Item3 * (grass?.m_scaleMax ?? 1f);
                        if (!clutter.m_scaleMin.Equals(min) || !clutter.m_scaleMax.Equals(max))
                        {
                            clutter.m_scaleMin = min;
                            clutter.m_scaleMax = max;
                            changed = true;
                        }
                        wanted = defaults.Item1 && (grass == null || max != 0f);
                    }

                    if (!seasonal)
                        wanted = defaults.Item1;
                }

                if (seasonalClutter != null)
                {
                    if (clutter.m_name != null && seasonalClutter.TryGetValue(clutter.m_name, out bool nameEnabled))
                        wanted = nameEnabled;
                    else if (clutter.m_prefab && seasonalClutter.TryGetValue(clutter.m_prefab.name, out bool prefabEnabled))
                        wanted = prefabEnabled;
                }

                // Shield copies are internal: they must never generate outside active protection.
                if (clutter.m_prefab && s_shieldedPrefabs.ContainsKey(clutter.m_prefab.name))
                    wanted = isAnyShieldActive;

                if (clutter.m_enabled != wanted)
                {
                    clutter.m_enabled = wanted;
                    changed = true;
                }
            }

            // FarLands extends the same patch system. Rebuild only when the final
            // material bindings, visual properties or placement settings changed.
            if (changed && rebuildGrass)
                m_clutterSystem.ClearAll();
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

            double seedFactor = Math.Log10(Math.Abs((double)seed));
            return (Math.Sin(Math.Sign(seed) * seedFactor * day) + Math.Sin(Math.Sqrt(seedFactor) * Math.E * day) + Math.Sin(Math.PI * day)) / 2;
        }

        internal static void AddSeasonalClutter()
        {
            AddMeadowsFlowers();

            AddForestBloom();

            AddSwampgrassBloom();

            AddShieldedGrass();
        }

        internal static void AddShieldedGrass()
        {
            foreach (string clutterName in new string[] { 
                "instanced_meadows_grass",
                "instanced_meadows_grass_short",
                "instanced_forest_groundcover",
                "instanced_forest_groundcover_brown",
                "instanced_heathgrass",
                "instanced_swamp_grass",
                "instanced_mistlands_grass_short"})
            {
                Clutter clutter = ClutterSystem.instance.m_clutter.Find(clutter => clutter?.m_name == clutterName || clutter?.m_prefab?.name == clutterName);
                if (clutter == null || clutter.m_prefab == null)
                    continue;

                string clutterShieldedName = clutter.m_name + c_shieldedGrassSuffix;
                if (ClutterSystem.instance.m_clutter.Any(clutter => clutter?.m_name == clutterShieldedName || clutter?.m_prefab?.name == clutterShieldedName))
                    continue;

                Clutter shieldedCopy = JsonUtility.FromJson<Clutter>(JsonUtility.ToJson(clutter));
                shieldedCopy.m_name = clutterShieldedName;
                shieldedCopy.m_enabled = false;

                clutterShieldedName = clutter.m_prefab.name + c_shieldedGrassSuffix;

                if (!s_shieldedPrefabs.TryGetValue(clutterShieldedName, out GameObject prefab) || !prefab)
                {
                    prefab = CustomPrefabs.InitPrefabClone(clutter.m_prefab, clutterShieldedName);
                    InstanceRenderer renderer = prefab.GetComponent<InstanceRenderer>();
                    
                    Material copiedMaterial = new Material(renderer.m_material)
                    {
                        name = clutterShieldedName
                    };

                    renderer.m_material = copiedMaterial;

                    s_shieldedPrefabs[prefab.name] = prefab;
                }

                shieldedCopy.m_prefab = prefab;

                ClutterSystem.instance.m_clutter.Add(shieldedCopy);
            }
        }

        internal static void AddMeadowsFlowers()
        {
            if (ClutterSystem.instance.m_clutter.Any(clutter => clutter?.m_name == c_meadowsFlowersName || clutter?.m_prefab?.name == c_meadowsFlowersPrefabName))
                return;

            Clutter clutter = ClutterSystem.instance.m_clutter.Find(clutter => clutter?.m_name == "heath flowers" || clutter?.m_prefab?.name == "instanced_heathflowers");
            if (clutter == null || clutter.m_prefab == null)
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
            if (clutter == null || clutter.m_prefab == null)
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
            bloom.m_fractalTresholdMax = 0.35f;

            ClutterSystem.instance.m_clutter.Add(bloom);
        }

        internal static void AddSwampgrassBloom()
        {
            if (ClutterSystem.instance.m_clutter.Any(clutter => clutter?.m_name == c_swampGrassBloomName || clutter?.m_prefab?.name == c_swampGrassBloomPrefabName))
                return;

            Clutter clutter = ClutterSystem.instance.m_clutter.Find(clutter => clutter?.m_name == "swampgrass" || clutter?.m_prefab?.name == "instanced_swamp_grass");
            if (clutter == null || clutter.m_prefab == null)
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
            bloom.m_fractalTresholdMax = 0.35f;

            ClutterSystem.instance.m_clutter.Add(bloom);
        }

        public static void Initialize()
        {
            if (!UseTextureControllers() || !ClutterSystem.instance || Instance)
                return;

            ClutterSystem.instance.transform.gameObject.AddComponent<ClutterVariantController>();
        }

        public static void Reinitialize()
        {
            if (Instance != null)
            {
                Instance.enabled = false;
                Destroy(Instance);
            }

            m_instance = null;

            LogInfo("Reinitializing clutter colors");

            Initialize();
        }

        [HarmonyPatch(typeof(ClutterSystem), nameof(ClutterSystem.Awake))]
        public static class ClutterSystem_Awake_AddSeasonalClutter
        {
            private static void Postfix()
            {
                if (FejdStartup.instance)
                    return;

                AddSeasonalClutter();
            }
        }

        private static bool isAnyShieldActive = false;

        private static bool RefreshShieldActiveState(bool seasonal)
        {
            bool active = seasonal && ShieldDomeImageEffect_SetShieldData_ProtectedStateChange.IsThereAnyActiveShieldedArea();
            bool changed = isAnyShieldActive != active;
            isAnyShieldActive = active;
            return changed;
        }

        public static void UpdateShieldActiveState()
        {
            bool changed = RefreshShieldActiveState(Instance && Instance.m_started && Instance.enabled &&
                SeasonState.IsActive && UseTextureControllers());
            if (!ClutterSystem.instance)
                return;

            foreach (Clutter clutter in ClutterSystem.instance.m_clutter)
                if (clutter?.m_prefab != null && s_shieldedPrefabs.ContainsKey(clutter.m_prefab.name) &&
                    clutter.m_enabled != isAnyShieldActive)
                {
                    clutter.m_enabled = isAnyShieldActive;
                    changed = true;
                }

            if (changed)
                ClutterSystem.instance.ClearAll();
        }

        [HarmonyPatch]
        public static class InstanceRenderer_AddInstance_PreventInProtectedArea
        {
            private static readonly Dictionary<string, bool> s_isShieldedGrassByRendererName = new Dictionary<string, bool>(StringComparer.Ordinal);

            internal static void ResetWorldState() => s_isShieldedGrassByRendererName.Clear();

            private static bool IsShieldedGrass(InstanceRenderer instance)
            {
                string rendererName = instance.name;
                if (s_isShieldedGrassByRendererName.TryGetValue(rendererName, out bool isShieldedGrass))
                    return isShieldedGrass;

                isShieldedGrass = s_shieldedPrefabs.ContainsKey(Utils.GetPrefabName(rendererName));
                s_isShieldedGrassByRendererName[rendererName] = isShieldedGrass;
                return isShieldedGrass;
            }

            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(InstanceRenderer), nameof(InstanceRenderer.AddInstance), new Type[3] { typeof(Vector3), typeof(Quaternion), typeof(float) });
                yield return AccessTools.Method(typeof(InstanceRenderer), nameof(InstanceRenderer.AddInstance), new Type[2] { typeof(Vector3), typeof(Quaternion) });
            }

            [HarmonyPriority(Priority.First)]
            private static bool Prefix(InstanceRenderer __instance, Vector3 pos)
            {
                if (!isAnyShieldActive)
                    return true;

                bool isShieldedGrass = IsShieldedGrass(__instance);
                if (SeasonState.IsActive && IsShieldedPosition(pos))
                    return isShieldedGrass && ShieldDomeImageEffect_SetShieldData_ProtectedStateChange.IsCoveredByShield(pos);

                return !isShieldedGrass;
            }
        }
    }
}
