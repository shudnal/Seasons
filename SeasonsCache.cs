using System;
using System.Collections.Generic;
using System.Linq;
using static Seasons.Seasons;
using UnityEngine;
using System.IO;
using HarmonyLib;
using Object = UnityEngine.Object;
using BepInEx.Logging;
using Newtonsoft.Json;
using System.Runtime.Serialization.Formatters.Binary;

namespace Seasons
{
    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
    public static class ZoneSystem_Start_SeasonsCache
    {
        [HarmonyPriority(Priority.First)]
        private static void Postfix()
        {
            if (!SeasonalTextureVariants.Initialize(cacheFolder))
                LogInfo("Missing textures variants");
        }
    }

    [Serializable]
    public class PrefabController
    {
        [Serializable]
        public class CachedMaterial
        {
            public string shaderName = string.Empty;
            public Dictionary<string, int> textureProperties = new Dictionary<string, int>();
        }
        
        [Serializable]
        public class CachedRenderer
        {
            public string name = string.Empty;
            public string type = string.Empty;
            public Dictionary<string, CachedMaterial> materials = new Dictionary<string, CachedMaterial>();

            public bool Initialized()
            {
                return materials.Any(m => m.Value.textureProperties.Count > 0);
            }

            public void AddMaterialTexture(Material material, string propertyName, int textureID)
            {
                if (!materials.TryGetValue(material.name, out PrefabController.CachedMaterial cachedMaterial))
                {
                    cachedMaterial = new PrefabController.CachedMaterial
                    {
                        shaderName = material.shader.name
                    };

                    materials.Add(material.name, cachedMaterial);
                }

                if (!cachedMaterial.textureProperties.ContainsKey(propertyName))
                    cachedMaterial.textureProperties.Add(propertyName, textureID);
            }
        }

        public Dictionary<int, List<CachedMaterial>> lodLevelMaterials = new Dictionary<int, List<CachedMaterial>>();
        public Dictionary<string, CachedRenderer> renderersInHierarchy = new Dictionary<string, CachedRenderer>();
    }

    [Serializable]
    public class CachedData
    {
        [Serializable]
        public class TextureData
        { 
            public string name;
            public byte[] data;
            public Dictionary<Season, Dictionary<int, byte[]>> variants = new Dictionary<Season, Dictionary<int, byte[]>>();
            
            public bool Initialized()
            {
                return variants.Any(variant => variant.Value.Count > 0);
            }

            public TextureData(TextureVariants textureVariants)
            {
                if (textureVariants == null)
                    return;

                foreach (KeyValuePair<Season, Dictionary<int, Texture2D>> texSeason in textureVariants.seasons)
                {
                    variants.Add(texSeason.Key, new Dictionary<int, byte[]>());
                    foreach (KeyValuePair<int, Texture2D> texData in texSeason.Value)
                        variants[texSeason.Key].Add(texData.Key, texData.Value.EncodeToPNG());
                }
            }

            public TextureData(DirectoryInfo texDirectory)
            {
                foreach (Season season in Enum.GetValues(typeof(Season)))
                {
                    variants.Add(season, new Dictionary<int, byte[]>());

                    for (int variant = 0; variant < seasonColorVariants; variant++)
                    {
                        FileInfo[] files = texDirectory.GetFiles(SeasonFileName(season, variant));
                        if (files.Length == 0)
                            continue;

                        variants[season].Add(variant, File.ReadAllBytes(files[0].FullName));
                    }
                }
            }

        }

        internal const string prefabCacheCommonFile = "cache.bin";
        internal const string prefabCacheFileName = "cache.json";
        internal const string texturesDirectory = "textures";

        public Dictionary<string, PrefabController> controllers = new Dictionary<string, PrefabController>();
        public Dictionary<int, TextureData> textures = new Dictionary<int, TextureData>();

        public bool Initialized()
        {
            return controllers.Count > 0 && textures.Count > 0;
        }

        public void SaveToJSON(string folder)
        {
            Directory.CreateDirectory(folder);

            File.WriteAllText(Path.Combine(folder, prefabCacheFileName), JsonConvert.SerializeObject(controllers, Formatting.Indented));

            string directory = Path.Combine(folder, texturesDirectory);

            foreach (KeyValuePair<int, TextureData> tex in textures)
            {
                string texturePath = Path.Combine(directory, tex.Key.ToString());
               
                Directory.CreateDirectory(texturePath);
                
                File.WriteAllBytes("\\\\?\\" + Path.Combine(texturePath, $"{tex.Value.name}{originalPostfix}"), tex.Value.data);

                foreach (KeyValuePair<Season, Dictionary<int, byte[]>> season in tex.Value.variants)
                    foreach (KeyValuePair<int, byte[]> texData in season.Value)
                        File.WriteAllBytes("\\\\?\\" + Path.Combine(texturePath, SeasonFileName(season.Key, texData.Key)), texData.Value);
            }
        }

        public void LoadFromJSON(string folder)
        {
            DirectoryInfo cacheDirectory = new DirectoryInfo(folder);
            if (!cacheDirectory.Exists)
                return;

            FileInfo[] cacheFile = cacheDirectory.GetFiles(prefabCacheFileName);
            if (cacheFile.Length == 0)
                return;

            try
            {
                controllers = JsonConvert.DeserializeObject<Dictionary<string, PrefabController>>(File.ReadAllText(cacheFile[0].FullName));
            }
            catch (Exception ex)
            {
                LogInfo($"Error loading JSON cache data from {cacheFile[0].FullName}\n{ex}");
                return;
            }

            DirectoryInfo[] texDir = cacheDirectory.GetDirectories(texturesDirectory);
            if (texDir.Length == 0)
                return;

            foreach (DirectoryInfo texDirectory in texDir[0].GetDirectories())
            {
                int hash = Int32.Parse(texDirectory.Name);
                if (textures.ContainsKey(hash))
                    continue;

                TextureData texData = new TextureData(texDirectory);

                if (!texData.Initialized())
                    continue;

                textures.Add(hash, texData);
            }
        }

        public void SaveToBinary(string folder)
        {
            Directory.CreateDirectory(folder);

            using (FileStream fs = new FileStream(Path.Combine(folder, prefabCacheCommonFile), FileMode.OpenOrCreate))
            {
                BinaryFormatter bf = new BinaryFormatter();
                bf.Serialize(fs, this);
            }
        }

        public void LoadFromBinary(string folder)
        {
            string filename = Path.Combine(folder, prefabCacheCommonFile);
            if (!File.Exists(filename))
                return;

            try
            {
                FileStream fs = new FileStream(filename, FileMode.Open);
                BinaryFormatter bf = new BinaryFormatter();
                CachedData cd = (CachedData)bf.Deserialize(fs);

                cd.controllers.Copy(controllers);
                cd.textures.Copy(textures);

                cd = null;
            }
            catch (Exception ex)
            {
                LogInfo($"Error loading binary cache data from {filename}:\n {ex}");
            }
        }

        public static string SeasonFileName(Season season, int variant)
        {
            return $"{season}_{variant + 1}.png";
        }
    }
    
    public class TextureVariants
    {
        public Texture2D original;
        public Dictionary<Season, Dictionary<int, Texture2D>> seasons = new Dictionary<Season, Dictionary<int, Texture2D>>();

        public TextureVariants(CachedData.TextureData texData)
        {
            if (texData == null)
                return;

            foreach (Season season in Enum.GetValues(typeof(Season)))
            {
                if (!texData.variants.TryGetValue(season, out Dictionary<int, byte[]> variants))
                    continue;

                for (int variant = 0; variant < seasonColorVariants; variant++)
                {
                    if (!variants.TryGetValue(variant, out byte[] data))
                        continue;

                    Texture2D tex = new Texture2D(2, 2);
                    if (tex.LoadImage(data, true))
                        AddVariant(season, variant, tex);
                    else
                        Object.Destroy(tex);
                }
            }
        }

        public TextureVariants(Texture texture)
        {
            SetOriginalTexture(texture);
        }

        public void SetOriginalTexture(Texture texture)
        {
            original = texture as Texture2D;
        }

        public bool Initialized()
        {
            return seasons.Any(season => season.Value.Count > 0);
        }

        public bool HaveOriginalTexture()
        {
            return original != null;
        }

        public void ApplyTextures()
        {
            foreach (KeyValuePair<Season, Dictionary<int, Texture2D>> season in seasons)
                foreach (KeyValuePair<int, Texture2D> variant in season.Value)
                    variant.Value.Apply(true, true);
        }

        public void AddVariant(Season season, int variant, Texture2D tex)
        {
            if (!seasons.TryGetValue(season, out Dictionary<int, Texture2D> variants))
            {
                variants = new Dictionary<int, Texture2D>();
                seasons.Add(season, variants);
            }

            if (!variants.ContainsKey(variant))
                variants.Add(variant, tex);
        }

    }

    public static class SeasonalTextureVariants
    {
        public static Dictionary<string, PrefabController> controllers = new Dictionary<string, PrefabController>();
        public static Dictionary<int, TextureVariants> textures = new Dictionary<int, TextureVariants>();

        public static bool Initialize(string folder)
        {
            if (Initialized())
                return true;

            controllers.Clear();
            textures.Clear();

            CachedData cachedData = new CachedData();
            if (cacheStorageFormat.Value == cacheFormat.Json)
                cachedData.LoadFromJSON(folder);
            else
                cachedData.LoadFromBinary(folder);

            if (cachedData.Initialized())
            {
                cachedData.controllers.Copy(controllers);

                foreach (KeyValuePair<int, CachedData.TextureData> texData in cachedData.textures)
                {
                    if (textures.ContainsKey(texData.Key))
                        continue;

                    TextureVariants texVariants = new TextureVariants(texData.Value);

                    if (!texVariants.Initialized())
                        continue;

                    textures.Add(texData.Key, texVariants);
                }
            }
            else
            {
                SeasonalTexturePrefabCache.FillWithGameData();
                
                if (Initialized())
                {
                    controllers.Copy(cachedData.controllers);

                    cachedData.textures.Clear();
                    foreach (KeyValuePair<int, TextureVariants> texVariants in textures)
                    {
                        CachedData.TextureData texData = new CachedData.TextureData(texVariants.Value);
                        if (texData.Initialized())
                            cachedData.textures.Add(texVariants.Key, texData);
                    }

                    if (cachedData.Initialized())
                        if (cacheStorageFormat.Value == cacheFormat.Binary)
                            cachedData.SaveToBinary(folder);
                        else if (cacheStorageFormat.Value == cacheFormat.Json)
                            cachedData.SaveToJSON(folder);
                        else
                        {
                            cachedData.SaveToJSON(folder);
                            cachedData.SaveToBinary(folder);
                        }

                    ApplyTexturesToGPU();
                }
            }

            return Initialized();
        }

        private static bool Initialized()
        {
            return controllers.Count > 0 && textures.Count > 0;
        }
        
        public static void ApplyTexturesToGPU()
        {
            foreach (KeyValuePair<int, TextureVariants> texture in textures)
                texture.Value.ApplyTextures();
        }
    
    }

    public static class SeasonalTextureVariantsGenerator
    {
        private class TextureProperties
        {
            public TextureProperties(Texture2D tex = null)
            {
                if (tex != null)
                {
                    mipmapCount = tex.mipmapCount;
                    wrapMode = tex.wrapMode;
                    filterMode = tex.filterMode;
                    anisoLevel = tex.anisoLevel;
                    mipMapBias = tex.mipMapBias;
                    width = tex.width;
                    height = tex.height;
                }
            }

            public TextureFormat format = TextureFormat.ARGB32;
            public int mipmapCount = 1;
            public TextureWrapMode wrapMode = TextureWrapMode.Repeat;
            public FilterMode filterMode = FilterMode.Point;
            public int anisoLevel = 1;
            public float mipMapBias = 0;
            public int width = 2;
            public int height = 2;
        }

        public static bool GetTextureVariants(string prefabName, Material material, string propertyName, Texture texture, out TextureVariants textureVariants)
        {
            textureVariants = new TextureVariants(texture);
            
            TextureProperties texProperties = new TextureProperties(texture as Texture2D);

            Color[] pixels = GetTexturePixels(texture, texProperties);
            if (pixels.Length < 1)
                return false;

            List<int> pixelsToChange = new List<int>();
            for (int i = 0; i < pixels.Length; i++)
                if (ReplaceColor(pixels[i]))
                    pixelsToChange.Add(i);

            if (pixelsToChange.Count == 0)
                return false;

            foreach (Season season in Enum.GetValues(typeof(Season)))
            {
                List<Color> colorVariants = new List<Color>();
                for (int i = 1; i <= seasonColorVariants; i++)
                {
                    Color colorVariant;
                    if (IsGrass(material.shader.name))
                        colorVariant = instance.GetGrassConfigColor(season, i);
                    else if (IsMoss(propertyName))
                        colorVariant = instance.GetMossConfigColor(season, i);
                    else
                        colorVariant = instance.GetSeasonConfigColor(season, i);

                    if (IsPine(material.name, prefabName))
                        colorVariant.a /= season == Season.Winter ? 1.5f : 2f;

                    colorVariants.Add(colorVariant);
                }

                GenerateColorVariants(season, colorVariants.ToArray(), pixels, pixelsToChange.ToArray(), texProperties, textureVariants);
            }

            return textureVariants.Initialized();
        }

        private static Color[] GetTexturePixels(Texture texture, TextureProperties texProperties)
        {
            if (texProperties == null)
                texProperties = new TextureProperties(texture as Texture2D);

            RenderTexture tmp = RenderTexture.GetTemporary(
                                    texture.width,
                                    texture.height,
                                    24, RenderTextureFormat.ARGB32);

            tmp.autoGenerateMips = true;
            tmp.useMipMap = true;
            tmp.anisoLevel = texProperties.anisoLevel;
            tmp.mipMapBias = texProperties.mipMapBias;
            tmp.wrapMode = texProperties.wrapMode;
            tmp.filterMode = texProperties.filterMode;

            Graphics.Blit(texture, tmp);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = tmp;

            Texture2D textureCopy = new Texture2D(texture.width, texture.height, texProperties.format, texProperties.mipmapCount, false)
            {
                filterMode = texProperties.filterMode,
                anisoLevel = texProperties.anisoLevel,
                mipMapBias = texProperties.mipMapBias,
                wrapMode = texProperties.wrapMode
            };

            textureCopy.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0, true);
            textureCopy.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(tmp);

            Color[] pixels = textureCopy.GetPixels();

            /*Texture2D newTexture = new Texture2D(texture.width, texture.height, texProperties.format, texProperties.mipmapCount, false)
            {
                filterMode = texProperties.filterMode,
                anisoLevel = texProperties.anisoLevel,
                mipMapBias = texProperties.mipMapBias,
                wrapMode = texProperties.wrapMode
            };

            newTexture.SetPixels(pixels);
            newTexture.Apply();

            Object.Destroy(newTexture);*/
            Object.Destroy(textureCopy);

            return pixels;
        }

        private static void GenerateColorVariants(Season season, Color[] colorVariants, Color[] pixels, int[] pixelsToChange, TextureProperties texProperties, TextureVariants textureVariants)
        {
            List<Color[]> seasonColors = new List<Color[]>();
            for (int i = 0; i < colorVariants.Length; i++)
                seasonColors.Add(pixels.ToArray());

            foreach (int i in pixelsToChange)
                for (int j = 0; j < colorVariants.Length; j++)
                    seasonColors[j][i] = MergeColors(pixels[i], colorVariants[j], colorVariants[j].a, season == Season.Winter);

            for (int variant = 0; variant < colorVariants.Length; variant++)
            {
                Texture2D tex = new Texture2D(texProperties.width, texProperties.height, texProperties.format, texProperties.mipmapCount, false)
                {
                    filterMode = texProperties.filterMode,
                    anisoLevel = texProperties.anisoLevel,
                    mipMapBias = texProperties.mipMapBias,
                    wrapMode = texProperties.wrapMode
                };

                tex.SetPixels(seasonColors[variant]);
                tex.Apply();

                textureVariants.AddVariant(season, variant, tex);
            }
        }

        private static bool ReplaceColor(Color color)
        {
            HSLColor hslcolor = new HSLColor(color);
            return color.a != 0f && (hslcolor.s >= 0.24f && GetHueDistance(hslcolor.h, 85f) <= 55f || hslcolor.s >= 0.15f && GetHueDistance(hslcolor.h, 120f) <= 40f);
        }

        private static float GetHueDistance(float hue1, float hue2)
        {
            float dh = Math.Abs(hue1 - hue2);
            return dh > 180 ? 360 - dh : dh;
        }

        private static Color MergeColors(Color color1, Color color2, float t, bool winterColor)
        {
            Color newColor = new Color(color2.r, color2.g, color2.b, color1.a);
            Color oldColor = winterColor ? new Color(color1.grayscale, color1.grayscale, color1.grayscale, color1.a) : new Color(color1.r, color1.g, color1.b, color1.a);

            HSLColor newHSLColor = new HSLColor(Color.Lerp(oldColor, newColor, t));

            if (!winterColor)
                newHSLColor.l = new HSLColor(color1).l;

            return newHSLColor.ToRGBA();
        }

        private static bool IsGrass(string shaderName)
        {
            return shaderFolders.TryGetValue(shaderName, out string folder) && folder == "Grass";
        }

        private static bool IsMoss(string textureName)
        {
            return textureName.IndexOf("moss", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPine(string materialName, string prefab)
        {
            return materialName.IndexOf("pine", StringComparison.OrdinalIgnoreCase) >= 0 || prefab.IndexOf("pine", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public static class SeasonalTexturePrefabCache
    {
        private static List<string> ignorePrefab = new List<string>()
        {
            "SwampTree2_log"
        };

        public static void FillWithGameData()
        {
            CacheClutters();

            LogInfo($"Cached prefabs:{SeasonalTextureVariants.controllers.Count} textures:{SeasonalTextureVariants.textures.Count}");
        }
        
        private static void CacheClutters()
        {
            foreach (ClutterSystem.Clutter clutter in ClutterSystem.instance.m_clutter.Where(c => c.m_prefab != null))
            {
                if (!clutter.m_prefab.TryGetComponent(out InstanceRenderer renderer))
                    continue;

                Material material = renderer.m_material;

                if (material == null || material.shader == null)
                    continue;

                string rendererType = renderer.GetType().ToString();

                if (!shadersTypes.TryGetValue(rendererType, out string[] shaders) || !shaders.Contains(material.shader.name)
                    || !shaderFolders.TryGetValue(material.shader.name, out string shaderFolder) || !shaderTextures.TryGetValue(material.shader.name, out string[] textureNames))
                    continue;

                PrefabController.CachedRenderer cachedRenderer = new PrefabController.CachedRenderer
                {
                    name = renderer.name,
                    type = rendererType
                };

                foreach (string propertyName in material.GetTexturePropertyNames().Where(mat => textureNames.Any(text => mat.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)))
                {
                    Texture texture = material.GetTexture(propertyName);
                    if (texture == null)
                        continue;

                    int textureID = texture.GetInstanceID();
                    if (SeasonalTextureVariants.textures.ContainsKey(textureID))
                    {
                        cachedRenderer.AddMaterialTexture(material, propertyName, textureID);
                    }
                    else if (SeasonalTextureVariantsGenerator.GetTextureVariants(clutter.m_prefab.name, material, propertyName, texture, out TextureVariants textureVariants))
                    {
                        SeasonalTextureVariants.textures.Add(textureID, textureVariants);
                        cachedRenderer.AddMaterialTexture(material, propertyName, textureID);
                    }
                }

                if (!cachedRenderer.Initialized())
                    continue;

                if (!SeasonalTextureVariants.controllers.TryGetValue(clutter.m_prefab.name, out PrefabController controller))
                {
                    controller = new PrefabController();
                    SeasonalTextureVariants.controllers.Add(clutter.m_prefab.name, controller);
                }

                string rendererPath = renderer.transform.GetPath();

                if (!controller.renderersInHierarchy.ContainsKey(rendererPath))
                {
                    controller.renderersInHierarchy.Add(rendererPath, cachedRenderer);
                    LogInfo($"Caching {rendererPath}");
                }
            }
        }

        private static void CacheMaterials(string prefabName, int lodLevel, string rendererType, List<Material> materials)
        {
            for (int m = 0; m < materials.Count; m++)
            {
                Material material = materials[m];

                if (material == null)
                    continue;

                if (shadersTypes.TryGetValue(rendererType, out string[] shaders) && shaders.Contains(material.shader.name) && shaderFolders.TryGetValue(material.shader.name, out string shaderFolder))
                {
                    if (shaderIgnoreMaterial.TryGetValue(material.shader.name, out string[] ignoreMaterial) && ignoreMaterial.Any(ignore => material.name.IndexOf(ignore, StringComparison.OrdinalIgnoreCase) >= 0))
                        continue;

                    if (!shaderTextures.TryGetValue(material.shader.name, out string[] textureNames))
                        textureNames = new string[0];

                    SeasonsTexture.CacheMaterialTextures(material, renderersFolders[rendererType], shaderFolder, prefabName, lodLevel, textureNames);
                }
            }
        }

        private static void CreateTextures()
        {
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs.Where(p => p.layer != 8 && p.layer != 12))
            {
                if (prefab.layer == 0 && prefab.TryGetComponent<Ship>(out _))
                    continue;

                if (prefab.layer == 16 && !prefab.TryGetComponent<Pickable>(out _) && !prefab.TryGetComponent<Plant>(out _))
                    continue;

                if (prefab.layer == 10 && !prefab.TryGetComponent<Pickable>(out _) && !prefab.TryGetComponent<Plant>(out _))
                    continue;

                if (prefab.layer != 9)
                {
                    if (prefab.TryGetComponent<LODGroup>(out LODGroup lodGroup) && lodGroup.lodCount > 1)
                    {
                        LOD[] LODs = lodGroup.GetLODs();
                        for (int lodLevel = 0; lodLevel < lodGroup.lodCount; lodLevel++)
                        {
                            LOD lod = LODs[lodLevel];
                            for (int i = 0; i < lod.renderers.Length; i++)
                            {
                                Renderer renderer = lod.renderers[i];

                                if (renderer == null)
                                    continue;

                                if (renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null)
                                    continue;

                                List<Material> materials = new List<Material>();
                                renderer.GetSharedMaterials(materials);

                                CacheMaterials(prefab.name, lodLevel, renderer.GetType().ToString(), materials);
                            }
                        }
                    }
                    else
                    {
                        MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>();
                        foreach (MeshRenderer renderer in renderers)
                        {
                            if (renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null)
                                continue;

                            List<Material> materials = new List<Material>();
                            renderer.GetSharedMaterials(materials);

                            CacheMaterials(prefab.name, 0, renderer.GetType().ToString(), materials);
                        }
                    }
                }
                else
                {
                    continue;
                    SkinnedMeshRenderer[] renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>();
                    foreach (SkinnedMeshRenderer renderer in renderers)
                    {
                        if (renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null)
                            continue;

                        List<Material> materials = new List<Material>();
                        renderer.GetSharedMaterials(materials);

                        CacheMaterials(prefab.name, 0, renderer.GetType().ToString(), materials);
                    }
                }
            }
        }
        

    }

}
