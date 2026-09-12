using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Seasons.PrefabController;
using static Seasons.PrefabVariantController;
using static Seasons.Seasons;

namespace Seasons
{
    public class PrefabVariantController : MonoBehaviour
    {
        public class MaterialVariants
        {
            public Material m_originalMaterial;
            public Dictionary<string, TextureVariants> m_textureVariants = new Dictionary<string, TextureVariants>();
            public Dictionary<string, Color[]> m_colorVariants = new Dictionary<string, Color[]>();
            public Material[] seasonalMaterials = Array.Empty<Material>();

            public Season season;
            public bool updateSeasonalMaterials = true;

            public readonly static Dictionary<Tuple<Material, CachedMaterial>, MaterialVariants> s_materialVariants = new Dictionary<Tuple<Material, CachedMaterial>, MaterialVariants>();

            private readonly static List<Material> s_tempMaterials = new List<Material>();

            private MaterialVariants(Material originalMaterial, CachedMaterial context)
            {
                m_originalMaterial = originalMaterial;

                seasonalMaterials = new Material[seasonColorVariants];
                for (int i = 0; i < seasonColorVariants; i++)
                    seasonalMaterials[i] = new Material(m_originalMaterial);

                updateSeasonalMaterials = true;

                s_materialVariants[Tuple.Create(m_originalMaterial, context)] = this;
            }

            public void InitializeTextureVariants(Dictionary<string, int> cachedTextures)
            {
                foreach (KeyValuePair<string, int> tex in cachedTextures)
                    if (texturesVariants.textures.TryGetValue(tex.Value, out TextureVariants variants)
                        && m_originalMaterial.GetTexture(tex.Key) is Texture2D)
                        m_textureVariants[tex.Key] = variants;

                foreach (KeyValuePair<string, TextureVariants> textureVariants in m_textureVariants)
                    if (!textureVariants.Value.HaveOriginalTexture())
                        textureVariants.Value.SetOriginalTexture(m_originalMaterial.GetTexture(textureVariants.Key));
            }

            public void InitializeColorVariants(Dictionary<string, string[]> cachedColors)
            {
                foreach (KeyValuePair<string, string[]> tex in cachedColors)
                    if (!m_colorVariants.ContainsKey(tex.Key))
                    {
                        s_tempColors.Clear();
                        foreach (string str in tex.Value)
                        {
                            if (ColorUtility.TryParseHtmlString(str, out Color color))
                                s_tempColors.Add(color);
                        }
                        m_colorVariants.Add(tex.Key, s_tempColors.ToArray());
                    }
            }

            public void ReplaceSharedMaterial(Renderer renderer, int materialIndex, int variant)
            {
                if (updateSeasonalMaterials || season != seasonState.GetCurrentSeason())
                {
                    updateSeasonalMaterials = false;
                    season = seasonState.GetCurrentSeason();

                    for (int i = 0; i < seasonColorVariants; i++)
                    {
                        foreach (KeyValuePair<string, TextureVariants> textureVariants in m_textureVariants)
                            seasonalMaterials[i].SetTexture(textureVariants.Key, textureVariants.Value.GetSeasonalVariant(season, i));

                        foreach (KeyValuePair<string, Color[]> colorVariants in m_colorVariants)
                            seasonalMaterials[i].SetColor(colorVariants.Key, colorVariants.Value[(int)season * seasonColorVariants + i]);
                    }
                }

                ApplySharedMaterial(renderer, materialIndex, seasonalMaterials[variant]);
            }

            public void RevertSharedMaterial(Renderer renderer, int materialIndex)
            {
                if (!renderer)
                    return;
                Material[] current = renderer.sharedMaterials;
                if (materialIndex < current.Length && seasonalMaterials.Contains(current[materialIndex]))
                    ApplySharedMaterial(renderer, materialIndex, m_originalMaterial);
            }

            public static void ApplySharedMaterial(Renderer renderer, int materialIndex, Material material)
            {
                if (renderer == null || material == null)
                    return;

                s_tempMaterials.Clear();
                renderer.GetSharedMaterials(s_tempMaterials);

                if (s_tempMaterials.Count <= materialIndex)
                    return;

                s_tempMaterials[materialIndex] = material;
                renderer.SetSharedMaterials(s_tempMaterials);
            }

            public static MaterialVariants GetMaterialVariants(Material material, CachedMaterial context)
            {
                if (s_materialVariants.TryGetValue(Tuple.Create(material, context), out MaterialVariants materialVariants))
                    return materialVariants;

                return new MaterialVariants(material, context);
            }

            public static void UpdateSeasonalMaterials()
            {
                s_materialVariants.Values.Do(matVar => matVar.updateSeasonalMaterials = true);
            }

            public static void Clear()
            {
                s_tempMaterials.Clear();
                s_materialVariants.Values.Do(matVar => matVar.seasonalMaterials.Do(Destroy));
                s_materialVariants.Clear();
            }
        }

        public class PrefabVariant
        {
            private ZNetView m_nview;
            private WearNTear m_wnt;
            private GameObject m_gameObject;
            private MeshRenderer m_renderer;

            public string m_prefabName;
            private double m_springFactor;
            private double m_summerFactor;
            private double m_fallFactor;
            private double m_winterFactor;

            private bool m_isVines = false;
            private bool m_covered = true;
            private float m_nextCoveredStatusCheckTime = -1f;
            private const float CoveredStatusCheckInterval = 5f;

            private readonly Dictionary<Renderer, Dictionary<int, MaterialVariants>> m_materialVariants = new Dictionary<Renderer, Dictionary<int, MaterialVariants>>();
            private readonly Dictionary<ParticleSystem, Color[]> m_startColors = new Dictionary<ParticleSystem, Color[]>();
            private readonly Dictionary<ParticleSystem, ParticleSystem.MinMaxGradient> m_originalStartColors = new Dictionary<ParticleSystem, ParticleSystem.MinMaxGradient>();
            private readonly Dictionary<ParticleSystem, Color> m_appliedStartColors = new Dictionary<ParticleSystem, Color>();

            public bool Initialize(PrefabController controller, GameObject gameObject, string prefabName = null, ZNetView netView = null, WearNTear wnt = null, MeshRenderer meshRenderer = null)
            {
                m_gameObject = gameObject;
                m_wnt = wnt ?? m_gameObject.GetComponent<WearNTear>();

                m_nview = netView ?? (m_wnt == null ? m_gameObject.GetComponent<ZNetView>() : m_wnt.m_nview);

                if (m_nview != null && (!m_nview.IsValid() || m_nview.m_ghost))
                    return false;

                m_prefabName = string.IsNullOrEmpty(prefabName) ? GetPrefabName(m_gameObject) : prefabName;
                m_renderer = meshRenderer;

                if (m_renderer != null)
                {
                    AddMaterialVariants(m_renderer, controller.cachedRenderer);
                }
                else
                {
                    foreach (KeyValuePair<string, Dictionary<int, List<CachedRenderer>>> rendererPath in controller.lodsInHierarchy)
                    {
                        string transformPath = GetRelativePath(rendererPath.Key, m_prefabName);

                        Transform transformWithLODGroup = m_gameObject.transform.Find(transformPath);
                        if (transformWithLODGroup == null)
                            continue;

                        if (transformWithLODGroup.gameObject.TryGetComponent(out LODGroup lodGroupTransform))
                            AddLODGroupMaterialVariants(lodGroupTransform, rendererPath.Value);
                    }

                    if (controller.lodLevelMaterials.Count > 0 && m_gameObject.TryGetComponent(out LODGroup lodGroup))
                        AddLODGroupMaterialVariants(lodGroup, controller.lodLevelMaterials);

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

                        s_tempRenderers.Clear();
                        CheckRenderersInHierarchy(m_gameObject.transform, rendererPath.Value.type, transformPath, 0, s_tempRenderers);

                        foreach (Renderer renderer in s_tempRenderers)
                            AddMaterialVariants(renderer, rendererPath.Value);
                    }

                    if (controller.cachedRenderer != null)
                    {
                        Renderer renderer = m_gameObject.GetComponent(controller.cachedRenderer.type) as Renderer;
                        if (renderer != null)
                            AddMaterialVariants(renderer, controller.cachedRenderer);
                    }

                    if (controller.particleSystemStartColors != null)
                    {
                        foreach (KeyValuePair<string, string[]> psPath in controller.particleSystemStartColors)
                        {
                            string transformPath = GetRelativePath(psPath.Key, m_prefabName);

                            Transform transformWithPS = m_gameObject.transform.Find(transformPath);
                            if (transformWithPS == null)
                                continue;

                            if (transformWithPS.gameObject.TryGetComponent(out ParticleSystem ps))
                                AddStartColorVariants(ps, psPath.Value);
                        }
                    }
                }

                if (m_materialVariants.Count == 0 && m_startColors.Count == 0)
                    return false;

                WorldToMapPoint(m_gameObject.transform.position, out float mx, out float my);
                UpdateFactors(mx, my);
                CheckIsVine();

                return true;
            }

            public bool Reinitialize(PrefabController controller)
            {
                if (m_gameObject == null)
                    return false;

                RevertState();
                m_materialVariants.Clear();
                m_startColors.Clear();
                m_originalStartColors.Clear();

                return Initialize(controller, m_gameObject, m_prefabName, m_nview, m_wnt, m_renderer);
            }

            public void RevertState()
            {
                foreach (KeyValuePair<Renderer, Dictionary<int, MaterialVariants>> materialVariants in m_materialVariants)
                    foreach (KeyValuePair<int, MaterialVariants> materialIndex in materialVariants.Value)
                        materialIndex.Value.RevertSharedMaterial(materialVariants.Key, materialIndex.Key);

                foreach (var original in m_originalStartColors)
                    if (original.Key)
                    {
                        ParticleSystem.MainModule main = original.Key.main;
                        if (m_appliedStartColors.TryGetValue(original.Key, out Color applied)
                            && main.startColor.mode == ParticleSystemGradientMode.Color && main.startColor.color == applied)
                            main.startColor = original.Value;
                    }
                m_appliedStartColors.Clear();
            }

            public void CheckCoveredStatus()
            {
                if (Time.time < m_nextCoveredStatusCheckTime)
                    return;

                m_nextCoveredStatusCheckTime = Time.time + CoveredStatusCheckInterval;

                bool haveRoof = HaveRoof();
                if (m_covered == haveRoof)
                    return;

                m_covered = haveRoof;
                UpdateColors();
            }

            public void CheckIsVine()
            {
                m_isVines = m_prefabName == "vines" || m_wnt != null && m_gameObject.GetComponent<Vine>() != null;
            }

            public void UpdateColors()
            {
                if (!m_gameObject || texturesVariants.IsUpdating)
                    return;

                if (!UseTextureControllers() || !SeasonState.IsActive)
                {
                    RevertState();
                    return;
                }

                if (m_nview != null && !m_nview.IsValid())
                    return;

                if (m_wnt != null && m_covered || m_gameObject.layer != 9 && IsProtectedPosition(m_gameObject.transform.position))
                {
                    RevertState();
                    return;
                }

                int variant = GetCurrentVariant();
                foreach (KeyValuePair<Renderer, Dictionary<int, MaterialVariants>> materialVariants in m_materialVariants)
                    foreach (KeyValuePair<int, MaterialVariants> materialIndex in materialVariants.Value)
                        materialIndex.Value.ReplaceSharedMaterial(materialVariants.Key, materialIndex.Key, variant);

                foreach (KeyValuePair<ParticleSystem, Color[]> startColor in m_startColors)
                {
                    if (!startColor.Key)
                        continue;
                    ParticleSystem.MainModule mainModule = startColor.Key.main;
                    if (!m_appliedStartColors.TryGetValue(startColor.Key, out Color applied)
                        || mainModule.startColor.mode != ParticleSystemGradientMode.Color || mainModule.startColor.color != applied)
                        m_originalStartColors[startColor.Key] = mainModule.startColor;
                    Color color = startColor.Value[(int)seasonState.GetCurrentSeason() * seasonsCount + variant];
                    mainModule.startColor = color;
                    m_appliedStartColors[startColor.Key] = color;
                }
            }

            public void AddToPrefabList()
            {
                instance.m_prefabVariants.Add(m_gameObject, this);

                if (m_wnt != null)
                    instance.m_pieceControllers.Add(m_wnt, this);

                UpdateColors();
            }

            public void RemoveFromPrefabList()
            {
                if (m_wnt != null)
                    instance.m_pieceControllers.Remove(m_wnt);

                instance.m_prefabVariants.Remove(m_gameObject);
            }

            public Material GetOriginalMaterial(Renderer renderer, Material material)
            {
                int index = Array.IndexOf(renderer.sharedMaterials, material);
                if (index < 0)
                    return null;

                if (!m_materialVariants.TryGetValue(renderer, out var materialVariants))
                    return null;

                return materialVariants.TryGetValue(index, out MaterialVariants variants) ? variants.m_originalMaterial : null;
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
                return seasonState.GetCurrentSeason() switch
                {
                    Season.Spring => GetVariant(m_springFactor),
                    Season.Summer => GetVariant(m_summerFactor),
                    Season.Fall => GetVariant(m_fallFactor),
                    Season.Winter => GetVariant(m_winterFactor),
                    _ => GetVariant(m_springFactor),
                };
            }

            private void AddLODGroupMaterialVariants(LODGroup lodGroup, Dictionary<int, List<CachedRenderer>> lodLevelMaterials)
            {
                LOD[] LODs = lodGroup.GetLODs();
                for (int lodLevel = 0; lodLevel < lodGroup.lodCount; lodLevel++)
                {
                    if (!lodLevelMaterials.TryGetValue(lodLevel, out List<CachedRenderer> cachedRenderers))
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

            private void AddMaterialVariants(Renderer renderer, CachedRenderer cachedRenderer)
            {
                for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                {
                    Material material = renderer.sharedMaterials[i];

                    if (material == null)
                        continue;

                    foreach (KeyValuePair<string, CachedMaterial> cachedRendererMaterial in cachedRenderer.materials)
                    {
                        if (cachedRendererMaterial.Value.textureProperties.Count > 0 || cachedRendererMaterial.Value.colorVariants.Count > 0)
                        {
                            if (material.name.StartsWith(cachedRendererMaterial.Key) && (material.shader.name == cachedRendererMaterial.Value.shaderName))
                            {
                                if (!m_materialVariants.TryGetValue(renderer, out Dictionary<int, MaterialVariants> materialIndex))
                                {
                                    materialIndex = new Dictionary<int, MaterialVariants>();
                                    m_materialVariants.Add(renderer, materialIndex);
                                }

                                if (!materialIndex.TryGetValue(i, out MaterialVariants materialVariants))
                                {
                                    materialVariants = MaterialVariants.GetMaterialVariants(material, cachedRendererMaterial.Value);
                                    materialIndex.Add(i, materialVariants);
                                }

                                materialVariants.InitializeTextureVariants(cachedRendererMaterial.Value.textureProperties);

                                materialVariants.InitializeColorVariants(cachedRendererMaterial.Value.colorVariants);
                            }
                        }
                    }
                }
            }

            private void AddStartColorVariants(ParticleSystem ps, string[] colorVariants)
            {
                if (!m_startColors.ContainsKey(ps))
                {
                    s_tempColors.Clear();
                    foreach (string str in colorVariants)
                    {
                        if (!ColorUtility.TryParseHtmlString(str, out Color color))
                            return;

                        s_tempColors.Add(color);
                    }
                    m_startColors.Add(ps, s_tempColors.ToArray());
                    m_originalStartColors[ps] = ps.main.startColor;
                }
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

            private bool HaveRoof()
            {
                if (m_wnt == null || m_isVines)
                    return false;

                if (IsProtectedPosition(m_gameObject.transform.position))
                    return true;

                if (!m_wnt.HaveRoof())
                    return false;

                int num = Physics.SphereCastNonAlloc(m_gameObject.transform.position + new Vector3(0, 2f, 0), 0.15f, Vector3.up, s_raycastHits, 100f, instance.m_rayMask);
                for (int i = 0; i < num; i++)
                {
                    if (s_raycastHits[i].collider.transform.root == m_wnt.transform.root)
                        continue;

                    GameObject go = s_raycastHits[i].collider.gameObject;
                    if (go != null && go != m_wnt && !go.CompareTag("leaky") && !IsWearNTearCollider(go))
                        return true;
                }

                return false;
            }

            private bool IsWearNTearCollider(GameObject go)
            {
                if (m_wnt.m_colliders == null)
                    return false;

                for (int i = 0; i < m_wnt.m_colliders.Length; i++)
                {
                    Collider collider = m_wnt.m_colliders[i];
                    if (collider != null && collider.gameObject == go)
                        return true;
                }

                return false;
            }
        }

        public int m_rayMask;
        private float m_seed;

        public readonly Dictionary<GameObject, PrefabVariant> m_prefabVariants = new Dictionary<GameObject, PrefabVariant>();
        public readonly Dictionary<WearNTear, PrefabVariant> m_pieceControllers = new Dictionary<WearNTear, PrefabVariant>();

        private static readonly MaterialPropertyBlock s_matBlock = new MaterialPropertyBlock();

        private static readonly List<Renderer> s_tempRenderers = new List<Renderer>();
        private static readonly List<Color> s_tempColors = new List<Color>();
        private static readonly Dictionary<string, string> s_tempPrefabNames = new Dictionary<string, string>();
        private static readonly List<GameObject> s_tempObjects = new List<GameObject>();
        public static readonly RaycastHit[] s_raycastHits = new RaycastHit[128];

        private const float noiseFrequency = 10000f;
        private const double noiseDivisor = 1.1;
        private const double noisePower = 1.3;
        private const string yggdrasilBranch = "YggdrasilBranch";

        private static PrefabVariantController m_instance;

        public static PrefabVariantController instance => m_instance;

        private void Awake()
        {
            m_instance = this;

            m_rayMask = LayerMask.GetMask("piece", "static_solid", "Default_small", "terrain");

            int seed = ZNet.m_world != null ? ZNet.m_world.m_seed : WorldGenerator.instance != null ? WorldGenerator.instance.GetSeed() : 0;
            m_seed = seed == 0 ? 0 : (float)Math.Log10(Math.Abs((double)seed));
        }

        private void OnDestroy()
        {
            m_pieceControllers.Clear();

            RevertPrefabsState();
            m_prefabVariants.Clear();

            if (m_instance == this)
            {
                MaterialVariants.Clear();
                m_instance = null;
            }
        }

        public void RevertPrefabsState()
        {
            foreach (KeyValuePair<GameObject, PrefabVariant> item in m_prefabVariants)
            {
                if (item.Key != null)
                    item.Value.RevertState();
            }
        }

        public void AddControllerTo(GameObject gameObject, bool checkLocation = true, ZNetView netView = null, WearNTear wnt = null, string prefabName = null, MeshRenderer meshRenderer = null)
        {
            if (!UseTextureControllers())
                return;

            if (gameObject == null)
                return;

            if (m_prefabVariants.ContainsKey(gameObject))
                return;

            prefabName ??= GetPrefabName(gameObject);
            if (prefabName == "YggdrasilRoot" && !controlYggdrasil.Value)
                return;

            if (!texturesVariants.controllers.TryGetValue(prefabName, out PrefabController controller))
                return;

            if (checkLocation && IsIgnoredPosition(gameObject.transform.position))
                return;

            PrefabVariant prefabVariant = new PrefabVariant();
            if (!prefabVariant.Initialize(controller, gameObject, prefabName, netView, wnt, meshRenderer))
                return;

            prefabVariant.AddToPrefabList();
        }

        public void AddControllerTo(Humanoid humanoid, Ragdoll ragdoll)
        {
            if (humanoid.InInterior())
                return;

            if (ragdoll.m_nview == null || !ragdoll.m_nview.IsValid())
                return;

            AddControllerTo(ragdoll.gameObject, checkLocation: false, ragdoll.m_nview);
        }

        public void AddControllerTo(WearNTear wnt)
        {
            if (m_pieceControllers.ContainsKey(wnt))
                return;

            if (wnt.m_nview == null || !wnt.m_nview.IsValid())
                return;

            AddControllerTo(wnt.gameObject, checkLocation: true, wnt.m_nview, wnt);
        }

        public void AddControllerTo(MineRock5 mineRock)
        {
            if (mineRock.m_nview == null || !mineRock.m_nview.IsValid())
                return;

            int prefab = mineRock.m_nview.GetZDO().GetPrefab();
            if (prefab == 0)
                return;

            GameObject gameObject = ZNetScene.instance.GetPrefab(prefab);
            if (gameObject == null)
                return;

            AddControllerTo(mineRock.gameObject, checkLocation: true, mineRock.m_nview, wnt: null, gameObject.name, mineRock.m_meshRenderer);
        }

        public void RemoveController(GameObject gameObject)
        {
            if (!m_prefabVariants.TryGetValue(gameObject, out PrefabVariant prefabVariant))
                return;

            prefabVariant.RemoveFromPrefabList();
        }

        private static string GetRelativePath(string rendererPath, string prefabName)
        {
            string path = rendererPath;
            if (path.Contains(prefabName))
            {
                path = rendererPath.Substring(rendererPath.IndexOf(prefabName) + prefabName.Length);
                if (path.StartsWith("/"))
                    path = path.Substring(1);
            }

            return path;
        }

        public static void UpdatePrefabColors()
        {
            if (instance == null)
                return;

            UpdatePrefabColorsFromList(instance.m_prefabVariants);
        }

        public static void UpdatePrefabColorsAroundPosition(Vector3 position, float radius, float delay = 0f)
        {
            if (instance == null)
                return;

            if (delay == 0f)
                UpdatePrefabColorsFromList(instance.m_prefabVariants.Where(kvp => kvp.Key == null || Vector3.Distance(kvp.Key.transform.position, position) < radius));
            else
                instance.StartCoroutine(UpdatePrefabColorsAroundPositionDelayed(position, radius, delay));
        }

        public static void UpdateShieldStateAfterConfigChange()
        {
            ClutterVariantController.UpdateShieldActiveState();
            ZoneSystemVariantController.UpdateTerrainColors();
            ShieldDomeImageEffect_SetShieldData_ProtectedStateChange.shieldRadius.Where(kvp => !IsIgnoredPosition(kvp.Key.GetShieldPosition())).Do(kvp => UpdatePrefabColorsAroundPosition(kvp.Key.GetShieldPosition(), kvp.Value + 1));
        }

        private static void UpdatePrefabColorsFromList(IEnumerable<KeyValuePair<GameObject, PrefabVariant>> variants)
        {
            if (instance == null)
                return;

            s_tempObjects.Clear();
            foreach (KeyValuePair<GameObject, PrefabVariant> controller in variants)
                if (controller.Key == null)
                    s_tempObjects.Add(controller.Key);
                else
                    controller.Value.UpdateColors();

            foreach (GameObject item in s_tempObjects)
                instance.m_prefabVariants.Remove(item);
        }

        public static IEnumerator UpdatePrefabColorsAroundPositionDelayed(Vector3 position, float radius, float delay = 0f)
        {
            PrefabVariantController controller = instance;
            yield return new WaitForSeconds(delay);

            if (controller && controller == instance)
                UpdatePrefabColorsFromList(controller.m_prefabVariants.Where(kvp => kvp.Key == null || Vector3.Distance(kvp.Key.transform.position, position) < radius));
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
            return Math.Round(Math.Pow(((double)Mathf.PerlinNoise(mx * noiseFrequency + instance.m_seed, my * noiseFrequency - instance.m_seed) +
                (double)Mathf.PerlinNoise(mx * 2 * noiseFrequency - instance.m_seed, my * 2 * noiseFrequency + instance.m_seed) * 0.5) / noiseDivisor, noisePower) * 20) / 20;
        }

        public static void AddControllerToPrefabs()
        {
            if (controlYggdrasil.Value)
            {
                Transform yggdrasilBranch = EnvMan.instance.transform.Find(PrefabVariantController.yggdrasilBranch);
                if (yggdrasilBranch == null)
                    return;

                instance.AddControllerTo(yggdrasilBranch.gameObject, checkLocation: false);
            }
        }

        public static void ReinitializePrefabVariants()
        {
            if (!instance || !ZNetScene.instance)
                return;

            LogInfo("Reinitializing prefabs colors");

            instance.RevertPrefabsState();
            MaterialVariants.Clear();

            List<PrefabVariant> listToRemove = new List<PrefabVariant>();
            foreach (PrefabVariant prefabVariant in instance.m_prefabVariants.Values)
            {
                if (!texturesVariants.controllers.TryGetValue(prefabVariant.m_prefabName, out PrefabController controller))
                {
                    listToRemove.Add(prefabVariant);
                    continue;
                }

                if (!prefabVariant.Reinitialize(controller))
                {
                    listToRemove.Add(prefabVariant);
                    continue;
                }
            }

            foreach (PrefabVariant prefabVariant in listToRemove)
            {
                prefabVariant.RevertState();
                prefabVariant.RemoveFromPrefabList();
            }

            foreach (ZNetView nview in ZNetScene.instance.m_instances.Values)
            {
                if (!(bool)nview)
                    continue;

                instance.AddControllerTo(nview.gameObject);
            }

            UpdatePrefabColors();
        }

        public static string GetPrefabName(GameObject go)
        {
            if (!s_tempPrefabNames.TryGetValue(go.name, out string prefabName))
        {
                prefabName = Utils.GetPrefabName(go);
                s_tempPrefabNames.Add(go.name, prefabName);
            }

            return prefabName;
        }

        public static void WorldToMapPoint(Vector3 p, out float mx, out float my)
        {
            int num = 1024;
            mx = p.x / 12 + num;
            my = p.z / 12 + num;
            mx /= 2048;
            my /= 2048;
        }
    }

    [HarmonyPatch(typeof(ZNetView), nameof(ZNetView.Awake))]
    public static class ZNetView_Awake_AddPrefabVariantController
    {
        private static PrefabVariantController CachedController => PrefabVariantController.instance;

        private static void Postfix(ZNetView __instance)
        {
            PrefabVariantController controller = CachedController;
            if (controller != null && __instance != null && !__instance.m_ghost && __instance.IsValid())
                controller.AddControllerTo(__instance.gameObject, checkLocation: true, __instance);
        }
    }

    [HarmonyPatch(typeof(ZNetView), nameof(ZNetView.OnDestroy))]
    public static class ZNetView_OnDestroy_RemovePrefabVariantController
    {
        private static void Prefix(ZNetView __instance)
        {
            PrefabVariantController.instance?.RemoveController(__instance.gameObject);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Destroy))]
    public static class ZNetScene_Destroy_RemovePrefabVariantController
    {
        private static void Prefix(GameObject go)
        {
            PrefabVariantController.instance?.RemoveController(go);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.OnZDODestroyed))]
    public static class ZNetScene_OnZDODestroyed_RemovePrefabVariantController
    {
        private static void Prefix(Dictionary<ZDO, ZNetView> ___m_instances, ZDO zdo)
        {
            if (___m_instances.TryGetValue(zdo, out var value))
                PrefabVariantController.instance?.RemoveController(value.gameObject);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Shutdown))]
    public static class ZNetScene_Shutdown_RemovePrefabVariantController
    {
        private static void Prefix(Dictionary<ZDO, ZNetView> ___m_instances)
        {
            foreach (ZNetView nview in ___m_instances.Values)
                if ((bool)nview)
                    PrefabVariantController.instance?.RemoveController(nview.gameObject);
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.SpawnProxyLocation))]
    public static class ZoneSystem_SpawnProxyLocation_AddPrefabVariantController
    {
        private static void Postfix(GameObject __result)
        {
            PrefabVariantController.instance?.AddControllerTo(__result);
        }
    }

    [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Awake))]
    public static class MineRock5_Awake_AddPrefabVariantController
    {
        private static void Postfix(MineRock5 __instance)
        {
            if (__instance.m_meshRenderer == null)
                return;

            PrefabVariantController.instance?.AddControllerTo(__instance);
        }
    }

    [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.UpdateMesh))]
    public static class MineRock5_UpdateMesh_FallbackToDefaultMaterial
    {
        const int materialIndex = 0;

        private static void Prefix(MineRock5 __instance, ref Material __state)
        {
            if (__instance.m_meshRenderer == null || !PrefabVariantController.instance)
                return;

            if (!PrefabVariantController.instance.m_prefabVariants.TryGetValue(__instance.gameObject, out PrefabVariant prefabVariant))
                return;

            if (__instance.m_meshRenderer.sharedMaterials == null || __instance.m_meshRenderer.sharedMaterials.Length == 0)
                return;

            Material originalMat = prefabVariant.GetOriginalMaterial(__instance.m_meshRenderer, __instance.m_meshRenderer.sharedMaterials[materialIndex]);
            if (originalMat == null)
                return;

            __state = __instance.m_meshRenderer.sharedMaterials[materialIndex];

            MaterialVariants.ApplySharedMaterial(__instance.m_meshRenderer, materialIndex, originalMat);
        }

        private static void Finalizer(MineRock5 __instance, Material __state)
        {
            if (__state == null)
                return;

            MaterialVariants.ApplySharedMaterial(__instance.m_meshRenderer, materialIndex, __state);
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Awake))]
    public static class WearNTear_Start_AddPrefabVariantController
    {
        private static void Postfix(WearNTear __instance)
        {
            PrefabVariantController.instance?.AddControllerTo(__instance);
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.SetHealthVisual))]
    public static class WearNTear_SetHealthVisual_UpdateCoverStatus
    {
        private static void Postfix(WearNTear __instance)
        {
            PrefabVariantController instance = PrefabVariantController.instance;
            if (instance != null && instance.m_pieceControllers.TryGetValue(__instance, out PrefabVariant controller))
                controller.CheckCoveredStatus();
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnRagdollCreated))]
    public static class Humanoid_OnRagdollCreated_AddPrefabVariantController
    {
        private static void Postfix(Humanoid __instance, Ragdoll ragdoll)
        {
            PrefabVariantController.instance?.AddControllerTo(__instance, ragdoll);
        }
    }

    [HarmonyPatch(typeof(EffectList), nameof(EffectList.Create))]
    public static class EffectList_Create_AddPrefabVariantController
    {
        private static void Postfix(Transform baseParent, GameObject[] __result)
        {
            if (baseParent == null || __result == null)
                return;

            foreach (GameObject obj in __result)
                PrefabVariantController.instance?.AddControllerTo(obj);
        }
    }

    [HarmonyPatch(typeof(ShieldDomeImageEffect), nameof(ShieldDomeImageEffect.SetShieldData))]
    public static class ShieldDomeImageEffect_SetShieldData_ProtectedStateChange
    {
        public static readonly Dictionary<ShieldGenerator, float> shieldRadius = new Dictionary<ShieldGenerator, float>();
        private static readonly Dictionary<ShieldGenerator, float> s_visualRadius = new Dictionary<ShieldGenerator, float>();
        private static readonly Dictionary<Vector3, bool> _cachedShieldCoverPositions = new Dictionary<Vector3, bool>();

        public static bool IsThereAnyActiveShieldedArea()
        {
            if (shieldRadius.Count == 0 || !IsShieldProtectionActive())
                return false;

            foreach (KeyValuePair<ShieldGenerator, float> shield in shieldRadius)
            {
                if (!shield.Key || shield.Value <= 0)
                    continue;

                if (!IsIgnoredPosition(shield.Key.GetShieldPosition()))
                    return true;
            }

            return false;
        }

        public static bool IsCoveredByShield(Vector3 position)
        {
            Vector3 pos = position;
            if (_cachedShieldCoverPositions.TryGetValue(pos, out bool covered))
                return covered;

            if (_cachedShieldCoverPositions.Count > 15000)
                _cachedShieldCoverPositions.Clear();

            covered = false;
            foreach (KeyValuePair<ShieldGenerator, float> shield in shieldRadius)
            {
                if (shield.Key && Vector3.Distance(shield.Key.GetShieldPosition(), position) < shield.Value - 2)
                {
                    covered = true;
                    break;
                }
            }

            _cachedShieldCoverPositions[pos] = covered;
            return covered;
        }

        public static void InvalidateShieldCoverCache() => _cachedShieldCoverPositions.Clear();

        public static void Clear()
        {
            shieldRadius.Clear();
            s_visualRadius.Clear();
            InvalidateShieldCoverCache();
        }

        public static void ForgetVisualRadius(ShieldGenerator shield) => s_visualRadius.Remove(shield);

        [HarmonyPriority(Priority.First)]
        private static void Prefix(ShieldGenerator shield, Vector3 position, float radius)
        {
            if (!shield || !shieldRadius.TryGetValue(shield, out float currentRadius))
                currentRadius = 0f;
            if (!shield)
                return;

            if (!shieldRadius.ContainsKey(shield) || currentRadius != radius)
            {
                shieldRadius[shield] = radius;
                InvalidateShieldCoverCache();
                ShieldGenerator.m_instanceChangeID++;
                bool firstVisual = !s_visualRadius.TryGetValue(shield, out float previousVisualRadius);
                if (IsShieldProtectionActive() && (firstVisual || Mathf.Abs(previousVisualRadius - radius) >= 3f || radius == shield.m_radiusTarget))
                {
                    s_visualRadius[shield] = radius;
                    float affectedRadius = Mathf.Max(previousVisualRadius, Mathf.Max(currentRadius, radius));
                    UpdatePrefabColorsAroundPosition(position, shield.m_maxShieldRadius);
                    ZoneSystemVariantController.UpdateTerrainColorsAroundPosition(position, affectedRadius);
                    ClutterSystem.instance?.ResetGrass(position, affectedRadius + 1f);
                }
                ClutterVariantController.UpdateShieldActiveState();
            }
        }
    }

    public static class ShieldGeneratorExtensions
    {
        public static Vector3 GetShieldPosition(this ShieldGenerator shield) => shield.m_shieldDome?.transform?.position ?? shield.transform.position;
    }

    [HarmonyPatch(typeof(ShieldDomeImageEffect), nameof(ShieldDomeImageEffect.RemoveShield))]
    public static class ShieldDomeImageEffect_RemoveShield_ProtectedStateChange
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(ShieldGenerator shield)
        {
            if (ShieldDomeImageEffect_SetShieldData_ProtectedStateChange.shieldRadius.Remove(shield))
            {
                ShieldDomeImageEffect_SetShieldData_ProtectedStateChange.ForgetVisualRadius(shield);
                ShieldDomeImageEffect_SetShieldData_ProtectedStateChange.InvalidateShieldCoverCache();
                ClutterVariantController.UpdateShieldActiveState();
                if (IsShieldProtectionActive())
                {
                    Vector3 position = shield.GetShieldPosition();
                    UpdatePrefabColorsAroundPosition(position, shield.m_maxShieldRadius, delay: 5f);
                    ZoneSystemVariantController.UpdateTerrainColorsAroundPosition(position, shield.m_maxShieldRadius, delay: 5f);
                }
            }
        }
    }
}
