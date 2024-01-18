using BepInEx.Logging;
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
        private WearNTear m_wnt;

        public string m_prefabName;
        private double m_springFactor;
        private double m_summerFactor;
        private double m_fallFactor;
        private double m_winterFactor;

        private bool m_covered = true;

        public int m_myListIndex = -1;
        public static readonly List<PrefabVariantController> s_allControllers = new List<PrefabVariantController>();
        public static readonly Dictionary<WearNTear, PrefabVariantController> s_pieceControllers = new Dictionary<WearNTear, PrefabVariantController>();

        private readonly Dictionary<Renderer, Dictionary<int, Dictionary<string, TextureVariants>>> m_materialVariants = new Dictionary<Renderer, Dictionary<int, Dictionary<string, TextureVariants>>>();
        private readonly Dictionary<Renderer, Dictionary<int, Dictionary<string, Color[]>>> m_colorVariants = new Dictionary<Renderer, Dictionary<int, Dictionary<string, Color[]>>>();
        private readonly Dictionary<ParticleSystem, Color[]> m_startColors = new Dictionary<ParticleSystem, Color[]>();

        private static readonly MaterialPropertyBlock s_matBlock = new MaterialPropertyBlock();

        private const float noiseFrequency = 10000f;
        private const double noiseDivisor = 1.1;
        private const double noisePower = 1.3;
        
        public static int s_rayMask = 0;

        public void Init(PrefabController controller, string prefabName = null)
        {
            if (String.IsNullOrEmpty(prefabName))
                m_prefabName = Utils.GetPrefabName(gameObject);
            else
                m_prefabName = prefabName;

            foreach (KeyValuePair<string, Dictionary<int, List<CachedRenderer>>> rendererPath in controller.lodsInHierarchy)
            {
                string transformPath = GetRelativePath(rendererPath.Key, m_prefabName);

                Transform transformWithLODGroup = gameObject.transform.Find(transformPath);
                if (transformWithLODGroup == null)
                    continue;

                if (transformWithLODGroup.gameObject.TryGetComponent(out LODGroup lodGroupTransform))
                    AddLODGroupMaterialVariants(lodGroupTransform, rendererPath.Value);
            }

            if (controller.lodLevelMaterials.Count > 0 && gameObject.TryGetComponent(out LODGroup lodGroup))
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

            if (controller.particleSystemStartColors != null)
            {
                foreach (KeyValuePair<string, string[]> psPath in controller.particleSystemStartColors)
                {
                    string transformPath = GetRelativePath(psPath.Key, m_prefabName);

                    Transform transformWithPS = gameObject.transform.Find(transformPath);
                    if (transformWithPS == null)
                        continue;

                    if (transformWithPS.gameObject.TryGetComponent(out ParticleSystem ps))
                        AddStartColorVariants(ps, psPath.Value);
                }
            }

            ToggleEnabled();
            UpdateColors();
        }

        private void Awake()
        {
            m_nview = gameObject.GetComponent<ZNetView>();
            m_wnt = gameObject.GetComponent<WearNTear>();

            s_allControllers.Add(this);
            m_myListIndex = s_allControllers.Count - 1;

            if (m_wnt != null)
            {
                s_pieceControllers.Add(m_wnt, this);
            }

            if (s_rayMask == 0)
                s_rayMask = LayerMask.GetMask("piece", "static_solid", "Default_small", "terrain");
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

            if (m_wnt != null)
            {
                s_pieceControllers.Remove(m_wnt);
            }
        }

        private void RevertTextures()
        {
            foreach (KeyValuePair<Renderer, Dictionary<int, Dictionary<string, TextureVariants>>> materialVariants in m_materialVariants)
                foreach (KeyValuePair<int, Dictionary<string, TextureVariants>> materialIndex in materialVariants.Value)
                    materialVariants.Key.SetPropertyBlock(null, materialIndex.Key);
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

            if (!base.enabled)
                return;

            if (m_wnt != null && m_covered)
            {
                RevertTextures();
                return;
            }

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

            foreach (KeyValuePair<ParticleSystem, Color[]> startColor in m_startColors)
            {
                ParticleSystem.MainModule mainModule = startColor.Key.main;
                mainModule.startColor = startColor.Value[(int)seasonState.GetCurrentSeason() * seasonsCount + variant];
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
            return seasonState.GetCurrentSeason() switch
            {
                Season.Spring => GetVariant(m_springFactor),
                Season.Summer => GetVariant(m_summerFactor),
                Season.Fall => GetVariant(m_fallFactor),
                Season.Winter => GetVariant(m_winterFactor),
                _ => GetVariant(m_springFactor),
            };
        }

        public void ToggleEnabled()
        {
            base.enabled = Minimap.instance != null && (m_materialVariants.Count > 0 || m_colorVariants.Count > 0) || m_startColors.Count > 0;
        }

        public void AddLODGroupMaterialVariants(LODGroup lodGroup, Dictionary<int, List<CachedRenderer>> lodLevelMaterials)
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

        public void AddStartColorVariants(ParticleSystem ps, string[] colorVariants)
        {
            if (!m_startColors.ContainsKey(ps))
            {
                List<Color> colors = new List<Color>();
                foreach (string str in colorVariants)
                {
                    if (!ColorUtility.TryParseHtmlString(str, out Color color))
                        return;

                    colors.Add(color);
                }
                m_startColors.Add(ps, colors.ToArray());
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

            if (m_prefabName == "vines")
                return false;

            if (!m_wnt.HaveRoof())
                return false;

            int num = Physics.SphereCastNonAlloc(base.transform.position + new Vector3(0, 2f, 0), 0.1f, Vector3.up, WearNTear.s_raycastHits, 100f, s_rayMask);
            for (int i = 0; i < num; i++)
            {
                if (WearNTear.s_raycastHits[i].collider.transform.root == m_wnt.transform.root)
                    continue;

                GameObject go = WearNTear.s_raycastHits[i].collider.gameObject;
                if (go != null && go != m_wnt && !go.CompareTag("leaky") && (m_wnt.m_colliders == null || !m_wnt.m_colliders.Any(coll => coll.gameObject == go)))
                    return true;
            }

            return false;
        }

        public static void AddComponentTo(GameObject gameObject, bool checkLocation = true)
        {
            if (!UseTextureControllers())
                return;

            if (gameObject == null)
                return;

            string prefabName = Utils.GetPrefabName(gameObject);
            if (prefabName == "YggdrasilRoot" && !controlYggdrasil.Value)
                return;

            if (!SeasonalTextureVariants.controllers.TryGetValue(prefabName, out PrefabController controller))
                return;

            if (gameObject.TryGetComponent<PrefabVariantController>(out _))
                return;

            if (checkLocation && IsIgnoredLocation(gameObject.transform.position))
                return;

            gameObject.AddComponent<PrefabVariantController>().Init(controller, prefabName);
        }

        public static void AddComponentTo(Humanoid humanoid)
        {
            if (!UseTextureControllers())
                return;

            if (humanoid.InInterior())
                return;

            if (SeasonalTextureVariants.controllers.TryGetValue(Utils.GetPrefabName(humanoid.gameObject), out PrefabController controller))
                if (!humanoid.gameObject.TryGetComponent<PrefabVariantController>(out _))
                    humanoid.gameObject.AddComponent<PrefabVariantController>().Init(controller);
        }

        public static void AddComponentTo(WearNTear wnt)
        {
            if (!UseTextureControllers())
                return;

            if (s_pieceControllers.ContainsKey(wnt) || !SeasonalTextureVariants.controllers.TryGetValue(Utils.GetPrefabName(wnt.gameObject), out PrefabController controller))
                return;

            if (IsIgnoredLocation(wnt.transform.position))
                return;

            wnt.gameObject.AddComponent<PrefabVariantController>().Init(controller);
        }

        public static void AddComponentTo(MineRock5 mineRock)
        {
            if (!UseTextureControllers())
                return;

            if (mineRock.m_nview == null || !mineRock.m_nview.IsValid())
                return;

            string prefabName = ZNetScene.instance.GetPrefab(mineRock.m_nview.GetZDO().GetPrefab()).name;

            if (!SeasonalTextureVariants.controllers.TryGetValue(prefabName, out PrefabController controller))
                return;

            if (IsIgnoredLocation(mineRock.transform.position))
                return;

            mineRock.gameObject.AddComponent<PrefabVariantController>().Init(controller, prefabName);
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

        public static bool IsIgnoredLocation(Vector3 position)
        {
            if (WorldGenerator.instance == null)
                return true;

            float baseHeight = WorldGenerator.instance.GetBaseHeight(position.x, position.z, menuTerrain: false);

            if (baseHeight > WorldGenerator.mountainBaseHeightMin + 0.05f)
                return true;

            Heightmap.Biome biome = WorldGenerator.instance.GetBiome(position);

            return biome == Heightmap.Biome.DeepNorth || biome == Heightmap.Biome.AshLands;
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.CreateObject))]
    public static class ZNetScene_CreateObject_AddPrefabVariantController
    {
        private static void Postfix(GameObject __result)
        {
            PrefabVariantController.AddComponentTo(__result);
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.SpawnProxyLocation))]
    public static class ZoneSystem_SpawnProxyLocation_AddPrefabVariantController
    {
        private static void Postfix(GameObject __result)
        {
            PrefabVariantController.AddComponentTo(__result);
        }
    }

    [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Start))]
    public static class MineRock5_Start_AddPrefabVariantController
    {
        private static void Postfix(MineRock5 __instance, MeshRenderer ___m_meshRenderer)
        {
            if (___m_meshRenderer == null)
                return;

            if (__instance.TryGetComponent(out PrefabVariantController prefabVariantController) && prefabVariantController.enabled)
                return;

            if (prefabVariantController == null)
            {
                PrefabVariantController.AddComponentTo(__instance);
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

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.SetHealthVisual))]
    public static class WearNTear_SetHealthVisual_UpdateCoverStatus
    {
        private static void Postfix(WearNTear __instance)
        {
            if (PrefabVariantController.s_pieceControllers.TryGetValue(__instance, out PrefabVariantController controller))
                controller.CheckCoveredStatus();
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Start))]
    public static class WearNTear_Start_AddPrefabVariantController
    {
        private static void Postfix(WearNTear __instance)
        {
            PrefabVariantController.AddComponentTo(__instance);
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Start))]
    public static class Humanoid_Start_AddPrefabVariantController
    {
        private static void Postfix(Humanoid __instance)
        {
            PrefabVariantController.AddComponentTo(__instance);
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnRagdollCreated))]
    public static class Humanoid_OnRagdollCreated_AddPrefabVariantController
    {
        private static void Postfix(Ragdoll ragdoll)
        {
            PrefabVariantController.AddComponentTo(ragdoll.gameObject);
        }
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.Awake))]
    public static class Plant_Awake_AddPrefabVariantController
    {
        private static void Postfix(Plant __instance)
        {
            PrefabVariantController.AddComponentTo(__instance.gameObject);
        }
    }

    [HarmonyPatch(typeof(EffectList), nameof(EffectList.Create))]
    public static class EffectList_Create_AddPrefabVariantController
    {
        private static void Postfix(Transform baseParent, GameObject[] __result)
        {
            if (baseParent == null)
                return;

            foreach (GameObject obj in __result)
                PrefabVariantController.AddComponentTo(obj);
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
    public static class ZoneSystem_Start_AddPrefabVariantControllerToYggdrasil
    {
        private static void Postfix()
        {
            if (!controlYggdrasil.Value)
                return;

            Transform yggdrasilBranch = EnvMan.instance.transform.Find("YggdrasilBranch");
            if (yggdrasilBranch == null)
                return;

            PrefabVariantController.AddComponentTo(yggdrasilBranch.gameObject, checkLocation: false);
        }
    }
}
