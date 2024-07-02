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

            private bool m_covered = true;

            private readonly Dictionary<Renderer, Dictionary<int, Dictionary<string, TextureVariants>>> m_materialVariants = new Dictionary<Renderer, Dictionary<int, Dictionary<string, TextureVariants>>>();
            private readonly Dictionary<Renderer, Dictionary<int, Dictionary<string, Color[]>>> m_colorVariants = new Dictionary<Renderer, Dictionary<int, Dictionary<string, Color[]>>>();
            private readonly Dictionary<ParticleSystem, Color[]> m_startColors = new Dictionary<ParticleSystem, Color[]>();

            public bool Initialize(PrefabController controller, GameObject gameObject, string prefabName = null, ZNetView netView = null, WearNTear wnt = null, MeshRenderer meshRenderer = null)
            {
                m_gameObject = gameObject;
                m_wnt = wnt ?? m_gameObject.GetComponent<WearNTear>();

                m_nview = netView ?? (m_wnt == null ? m_gameObject.GetComponent<ZNetView>() : m_wnt.m_nview);

                if (m_nview != null && (!m_nview.IsValid() || m_nview.m_ghost))
                    return false;

                m_prefabName = String.IsNullOrEmpty(prefabName) ? GetPrefabName(m_gameObject) : prefabName;
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

                if (m_materialVariants.Count == 0 && m_colorVariants.Count == 0 && m_startColors.Count == 0)
                    return false;

                WorldToMapPoint(m_gameObject.transform.position, out float mx, out float my);
                UpdateFactors(mx, my);

                return true;
            }

            public bool Reinitialize(PrefabController controller)
            {
                if (m_gameObject == null)
                    return false;

                m_materialVariants.Clear();
                m_colorVariants.Clear();
                m_startColors.Clear();
                
                return Initialize(controller, m_gameObject, m_prefabName, m_nview, m_wnt, m_renderer);
            }

            public void RevertState()
            {
                foreach (KeyValuePair<Renderer, Dictionary<int, Dictionary<string, TextureVariants>>> materialVariants in m_materialVariants)
                    foreach (KeyValuePair<int, Dictionary<string, TextureVariants>> materialIndex in materialVariants.Value)
                        if (materialVariants.Key != null && materialVariants.Key.HasPropertyBlock() && materialVariants.Key.sharedMaterials.Length >= materialIndex.Key)
                            materialVariants.Key.SetPropertyBlock(null, materialIndex.Key);

                foreach (KeyValuePair<Renderer, Dictionary<int, Dictionary<string, Color[]>>> colorVariants in m_colorVariants)
                    foreach (KeyValuePair<int, Dictionary<string, Color[]>> colorIndex in colorVariants.Value)
                        if (colorVariants.Key != null && colorVariants.Key.HasPropertyBlock() && colorVariants.Key.sharedMaterials.Length >= colorIndex.Key)
                            colorVariants.Key.SetPropertyBlock(null, colorIndex.Key);
            }

            public void CheckCoveredStatus()
            {
                bool haveRoof = HaveRoof();
                if (m_covered == haveRoof)
                    return;

                m_covered = haveRoof;
                UpdateColors();
            }

            public void UpdateColors()
            {
                if (m_nview != null && !m_nview.IsValid())
                    return;

                if (m_wnt != null && m_covered || m_gameObject.layer != 9 && IsProtectedPosition(m_gameObject.transform.position))
                {
                    RevertState();
                    return;
                }

                int variant = GetCurrentVariant();
                foreach (KeyValuePair<Renderer, Dictionary<int, Dictionary<string, TextureVariants>>> materialVariants in m_materialVariants)
                    foreach (KeyValuePair<int, Dictionary<string, TextureVariants>> materialIndex in materialVariants.Value)
                        foreach (KeyValuePair<string, TextureVariants> texVar in materialIndex.Value)
                            if (texVar.Value.seasons.TryGetValue(seasonState.GetCurrentSeason(), out Dictionary<int, Texture2D> variants) && variants.TryGetValue(variant, out Texture2D texture))
                            {
                                if (!texVar.Value.HaveOriginalTexture())
                                    texVar.Value.SetOriginalTexture(materialVariants.Key.sharedMaterials[materialIndex.Key].GetTexture(texVar.Key));

                                materialVariants.Key.GetPropertyBlock(s_matBlock, materialIndex.Key);

                                if (CustomTextures.HaveCustomTexture(texVar.Value.originalName, seasonState.GetCurrentSeason(), variant, texVar.Value.properties, out Texture2D customTexture))
                                    s_matBlock.SetTexture(texVar.Key, customTexture);
                                else
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

                foreach (KeyValuePair<ParticleSystem, Color[]> startColor in m_startColors)
                {
                    ParticleSystem.MainModule mainModule = startColor.Key.main;
                    mainModule.startColor = startColor.Value[(int)seasonState.GetCurrentSeason() * seasonsCount + variant];
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
                                        texVariants.Add(tex.Key, texturesVariants.textures[tex.Value]);
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
                                        s_tempColors.Clear();
                                        foreach (string str in tex.Value)
                                        {
                                            if (ColorUtility.TryParseHtmlString(str, out Color color))
                                                s_tempColors.Add(color);
                                        }
                                        colorVariants.Add(tex.Key, s_tempColors.ToArray());
                                    }
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
                if (m_wnt == null)
                    return false;

                if (IsProtectedPosition(m_gameObject.transform.position))
                    return true;

                if (m_prefabName == "vines")
                    return false;

                if (!m_wnt.HaveRoof())
                    return false;

                int num = Physics.SphereCastNonAlloc(m_gameObject.transform.position + new Vector3(0, 2f, 0), 0.1f, Vector3.up, s_raycastHits, 100f, instance.m_rayMask);
                for (int i = 0; i < num; i++)
                {
                    if (s_raycastHits[i].collider.transform.root == m_wnt.transform.root)
                        continue;

                    GameObject go = s_raycastHits[i].collider.gameObject;
                    if (go != null && go != m_wnt && !go.CompareTag("leaky") && (m_wnt.m_colliders == null || !m_wnt.m_colliders.Any(coll => coll.gameObject == go)))
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
            m_seed = seed == 0 ? 0 : Mathf.Log10(Math.Abs(seed));
        }

        private void OnDestroy()
        {
            m_pieceControllers.Clear();

            RevertPrefabsState();
            m_prefabVariants.Clear();

            m_instance = null;
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

            AddControllerTo(ragdoll.gameObject, checkLocation: false, ragdoll.m_nview);
        }

        public void AddControllerTo(WearNTear wnt)
        {
            if (m_pieceControllers.ContainsKey(wnt))
                return;

            AddControllerTo(wnt.gameObject, checkLocation: true, wnt.m_nview, wnt);
        }

        public void AddControllerTo(MineRock5 mineRock, MeshRenderer meshRenderer)
        {
            string prefabName = ZNetScene.instance.GetPrefab(mineRock.m_nview.GetZDO().GetPrefab()).name;

            AddControllerTo(mineRock.gameObject, checkLocation: true, mineRock.m_nview, wnt:null, prefabName, meshRenderer);
        }

        public void RemoveController(GameObject gameObject)
        {
            if (!m_prefabVariants.TryGetValue(gameObject, out PrefabVariant prefabVariant))
                return;

            prefabVariant.RevertState();
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
            yield return new WaitForSeconds(delay);

            UpdatePrefabColorsFromList(instance.m_prefabVariants.Where(kvp => kvp.Key == null || Vector3.Distance(kvp.Key.transform.position, position) < radius));
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
            LogInfo("Reinitializing prefabs colors");

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
        private static void Postfix(ZNetView __instance)
        {
            if (__instance != null && !__instance.m_ghost && __instance.IsValid())
                PrefabVariantController.instance?.AddControllerTo(__instance.gameObject, checkLocation: true, __instance);
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
            {
                PrefabVariantController.instance?.RemoveController(value.gameObject);
            }
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
    public static class MineRock5_Start_AddPrefabVariantController
    {
        private static void Postfix(MineRock5 __instance, MeshRenderer ___m_meshRenderer)
        {
            if (___m_meshRenderer == null)
                return;

            PrefabVariantController.instance?.AddControllerTo(__instance, ___m_meshRenderer);
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
            if (PrefabVariantController.instance != null && PrefabVariantController.instance.m_pieceControllers.TryGetValue(__instance, out PrefabVariant controller))
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
        public static readonly Dictionary<ShieldGenerator, int> shieldRadius = new Dictionary<ShieldGenerator, int>();

        [HarmonyPriority(Priority.First)]
        private static void Prefix(ShieldGenerator shield, Vector3 position, float radius)
        {
            if (!shieldRadius.ContainsKey(shield) || shieldRadius[shield] != (int)radius)
            {
                shieldRadius[shield] = (int)radius;
                UpdatePrefabColorsAroundPosition(position, shield.m_maxShieldRadius);
            }
        }
    }

    [HarmonyPatch(typeof(ShieldDomeImageEffect), nameof(ShieldDomeImageEffect.RemoveShield))]
    public static class ShieldDomeImageEffect_RemoveShield_ProtectedStateChange
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(ShieldGenerator shield)
        {
            if (ShieldDomeImageEffect_SetShieldData_ProtectedStateChange.shieldRadius.ContainsKey(shield))
            {
                ShieldDomeImageEffect_SetShieldData_ProtectedStateChange.shieldRadius.Remove(shield);
                UpdatePrefabColorsAroundPosition(shield.transform.position, shield.m_maxShieldRadius, delay: 5f);
            }
        }
    }
 
}
