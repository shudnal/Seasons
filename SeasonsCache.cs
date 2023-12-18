﻿using System;
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
using static Seasons.PrefabController;
using static Seasons.CachedData;

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
    public class TextureProperties
    {
        public TextureProperties(Texture2D tex)
        {
            mipmapCount = tex.mipmapCount;
            wrapMode = tex.wrapMode;
            filterMode = tex.filterMode;
            anisoLevel = tex.anisoLevel;
            mipMapBias = tex.mipMapBias;
            width = tex.width;
            height = tex.height;
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

    [Serializable]
    public class PrefabController
    {
        [Serializable]
        public class CachedMaterial
        {
            public string name = string.Empty;
            public string shaderName = string.Empty;
            public Dictionary<string, int> textureProperties = new Dictionary<string, int>();
            public Dictionary<string, string[]> colorVariants = new Dictionary<string, string[]>();

            public CachedMaterial(string materialName, string shader, string propertyName, int textureID)
            {
                name = materialName;
                shaderName = shader;
                AddTexture(propertyName, textureID);
            }

            public CachedMaterial(string materialName, string shader, string propertyName, Color[] colors)
            {
                name = materialName;
                shaderName = shader;
                AddColors(propertyName, colors);
            }

            public void AddTexture(string propertyName, int textureID)
            {
                if (!textureProperties.ContainsKey(propertyName))
                    textureProperties.Add(propertyName, textureID);
            }

            public void AddColors(string propertyName, Color[] colors)
            {
                List<string> vec = new List<string>();
                colors.Do(x => vec.Add($"#{ColorUtility.ToHtmlStringRGBA(x)}"));

                if (!colorVariants.ContainsKey(propertyName))
                    colorVariants.Add(propertyName, vec.ToArray());
            }
        }

        [Serializable]
        public class CachedRenderer
        {
            public string name = string.Empty;
            public string type = string.Empty;
            public Dictionary<string, CachedMaterial> materials = new Dictionary<string, CachedMaterial>();

            public bool Initialized()
            {
                return materials.Any(m => m.Value.textureProperties.Count > 0 || m.Value.colorVariants.Count > 0);
            }

            public void AddMaterialTexture(Material material, string propertyName, int textureID)
            {
                if (!materials.TryGetValue(material.name, out CachedMaterial cachedMaterial))
                    materials.Add(material.name, new CachedMaterial(material.name, material.shader.name, propertyName, textureID));
                else
                    cachedMaterial.AddTexture(propertyName, textureID);
            }

            public void AddMaterialColors(Material material, string propertyName, Color[] colors)
            {
                if (!materials.TryGetValue(material.name, out CachedMaterial cachedMaterial))
                    materials.Add(material.name, new CachedMaterial(material.name, material.shader.name, propertyName, colors));
                else
                    cachedMaterial.AddColors(propertyName, colors);
            }

        }

        public Dictionary<int, List<CachedRenderer>> lodLevelMaterials = new Dictionary<int, List<CachedRenderer>>();
        public Dictionary<string, CachedRenderer> renderersInHierarchy = new Dictionary<string, CachedRenderer>();
        public CachedRenderer cachedRenderer;

        public bool Initialized()
        {
            return lodLevelMaterials.Count > 0 || renderersInHierarchy.Count > 0 || cachedRenderer != null;
        }
    }

    [Serializable]
    public class CachedData
    {
        [Serializable]
        public class TextureData
        {
            public string name;
            public byte[] originalPNG;
            public TextureProperties properties;
            public Dictionary<Season, Dictionary<int, byte[]>> variants = new Dictionary<Season, Dictionary<int, byte[]>>();

            public bool Initialized()
            {
                return variants.Any(variant => variant.Value.Count > 0);
            }

            public TextureData(TextureVariants textureVariants)
            {
                if (textureVariants == null)
                    return;

                originalPNG = textureVariants.originalPNG;
                name = textureVariants.original.name;
                properties = textureVariants.properties;

                foreach (KeyValuePair<Season, Dictionary<int, Texture2D>> texSeason in textureVariants.seasons)
                {
                    variants.Add(texSeason.Key, new Dictionary<int, byte[]>());
                    foreach (KeyValuePair<int, Texture2D> texData in texSeason.Value)
                        variants[texSeason.Key].Add(texData.Key, texData.Value.EncodeToPNG());
                }
            }

            public TextureData(DirectoryInfo texDirectory)
            {
                FileInfo[] propertiesFile = texDirectory.GetFiles(texturePropertiesFileName);
                if (propertiesFile.Length > 0)
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(propertiesFile[0].FullName), properties);

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
        internal const string originalPostfix = ".orig.png";
        internal const string texturePropertiesFileName = "properties.json";


        public Dictionary<string, PrefabController> controllers = new Dictionary<string, PrefabController>();
        public Dictionary<int, TextureData> textures = new Dictionary<int, TextureData>();

        public bool Initialized()
        {
            return controllers.Count > 0 && textures.Count > 0;
        }

        public void SaveToJSON(string folder)
        {
            Directory.CreateDirectory(folder);

            string filename = Path.Combine(folder, prefabCacheFileName);

            File.WriteAllText(filename, JsonConvert.SerializeObject(controllers, Formatting.Indented));

            string directory = Path.Combine(folder, texturesDirectory);

            LogInfo($"Saved cache file {filename}");

            foreach (KeyValuePair<int, TextureData> tex in textures)
            {
                string texturePath = Path.Combine(directory, tex.Key.ToString());

                Directory.CreateDirectory(texturePath);

                File.WriteAllBytes("\\\\?\\" + Path.Combine(texturePath, $"{tex.Value.name}{originalPostfix}"), tex.Value.originalPNG);

                File.WriteAllText("\\\\?\\" + Path.Combine(texturePath, texturePropertiesFileName), JsonUtility.ToJson(tex.Value.properties));

                foreach (KeyValuePair<Season, Dictionary<int, byte[]>> season in tex.Value.variants)
                    foreach (KeyValuePair<int, byte[]> texData in season.Value)
                        File.WriteAllBytes("\\\\?\\" + Path.Combine(texturePath, SeasonFileName(season.Key, texData.Key)), texData.Value);
            }

            LogInfo($"Saved {textures.Count} textures at {directory}");
        }

        public void LoadFromJSON(string folder)
        {
            DirectoryInfo cacheDirectory = new DirectoryInfo(folder);
            if (!cacheDirectory.Exists)
                return;

            FileInfo[] cacheFile = cacheDirectory.GetFiles(prefabCacheFileName);
            if (cacheFile.Length == 0)
            {
                LogInfo($"File not found: {Path.Combine(folder, prefabCacheFileName)}");
                return;
            }

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

            LogInfo($"Saved cache file {Path.Combine(folder, prefabCacheCommonFile)}");
        }

        public void LoadFromBinary(string folder)
        {
            string filename = Path.Combine(folder, prefabCacheCommonFile);
            if (!File.Exists(filename))
            {
                LogInfo($"File not found: {filename}");
                return;
            }

            try
            {
                FileStream fs = new FileStream(filename, FileMode.Open);
                BinaryFormatter bf = new BinaryFormatter();
                CachedData cd = (CachedData)bf.Deserialize(fs);

                controllers.Copy(cd.controllers);
                textures.Copy(cd.textures);

                fs.Close();
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
        public byte[] originalPNG;
        public TextureProperties properties;
        public Dictionary<Season, Dictionary<int, Texture2D>> seasons = new Dictionary<Season, Dictionary<int, Texture2D>>();

        public TextureVariants(CachedData.TextureData texData)
        {
            if (texData == null)
                return;

            properties = texData.properties;

            foreach (Season season in Enum.GetValues(typeof(Season)))
            {
                if (!texData.variants.TryGetValue(season, out Dictionary<int, byte[]> variants))
                    continue;

                for (int variant = 0; variant < seasonColorVariants; variant++)
                {
                    if (!variants.TryGetValue(variant, out byte[] data))
                        continue;

                    Texture2D tex = new Texture2D(properties.width, properties.height, properties.format, properties.mipmapCount, false)
                    {
                        filterMode = properties.filterMode,
                        anisoLevel = properties.anisoLevel,
                        mipMapBias = properties.mipMapBias,
                        wrapMode = properties.wrapMode
                    };

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
            properties = new TextureProperties(texture as Texture2D);
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
            if (cacheStorageFormat.Value == CacheFormat.Json)
                cachedData.LoadFromJSON(folder);
            else
                cachedData.LoadFromBinary(folder);

            if (cachedData.Initialized())
            {
                controllers.Copy(cachedData.controllers);

                foreach (KeyValuePair<int, CachedData.TextureData> texData in cachedData.textures)
                {
                    if (textures.ContainsKey(texData.Key))
                        continue;

                    TextureVariants texVariants = new TextureVariants(texData.Value);

                    if (!texVariants.Initialized())
                        continue;

                    textures.Add(texData.Key, texVariants);
                }

                LogInfo($"Loaded from cache controllers:{controllers.Count} textures:{textures.Count}");
            }
            else
            {
                SeasonalTexturePrefabCache.FillWithGameData();

                if (Initialized())
                {
                    cachedData.controllers.Copy(controllers);

                    cachedData.textures.Clear();
                    foreach (KeyValuePair<int, TextureVariants> texVariants in textures)
                    {
                        CachedData.TextureData texData = new CachedData.TextureData(texVariants.Value);
                        if (texData.Initialized())
                            cachedData.textures.Add(texVariants.Key, texData);
                    }

                    if (cachedData.Initialized())
                        if (cacheStorageFormat.Value == CacheFormat.Binary)
                            cachedData.SaveToBinary(folder);
                        else if (cacheStorageFormat.Value == CacheFormat.Json)
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
        public static bool GetColorVariants(string prefabName, Material material, string propertyName, Color color, out Color[] colors)
        {
            colors = null;

            bool isGrass = IsGrass(material.shader.name);
            bool isMoss = IsMoss(propertyName);
            bool replaceOverride = ReplaceColorOverride(prefabName, material.name, propertyName);

            if (!replaceOverride && !ReplaceColor(color, isGrass, isMoss))
                return false;

            List<Color> colorsList = new List<Color>();
            foreach (Season season in Enum.GetValues(typeof(Season)))
            {
                for (int i = 1; i <= seasonColorVariants; i++)
                {
                    Color colorVariant;
                    if (isGrass)
                        colorVariant = instance.GetGrassConfigColor(season, i);
                    else if (isMoss)
                        colorVariant = instance.GetMossConfigColor(season, i);
                    else
                        colorVariant = instance.GetSeasonConfigColor(season, i);

                    if (IsPine(material.name, prefabName))
                        colorVariant.a /= season == Season.Winter ? 1.5f : 2f;

                    if (GenerateOnlyWinterColor(prefabName, material.name, propertyName) && season != Season.Winter)
                        colorsList.Add(color);
                    else
                        colorsList.Add(MergeColors(color, colorVariant, colorVariant.a, season == Season.Winter));
                }
            }

            colors = colorsList.ToArray();

            return true;
        }

        private static bool IsPixelToChange(Color color, int pos, TextureProperties properties, bool isGrass, bool isMoss, string prefabName, string materialName, string propertyName)
        {
            if (materialName.StartsWith("Pine_tree_small"))
                if (pos % properties.width < 94 && properties.height - pos / properties.width < 45)
                    return false;

            if (materialName.StartsWith("Fir_tree_sapling"))
                if (pos % properties.width < 21 && properties.height - pos / properties.width < 12)
                    return false;

            if (prefabName == "FirTree")
                if (pos % properties.width < 372 && properties.height - pos / properties.width < 165)
                    return false;

            if (prefabName == "Pinetree_01")
                if (pos % properties.width > 126)
                    return false;

            if (isGrass && !prefabName.StartsWith("Pickable_Flax") && prefabName != "sapling_flax" && prefabName != "instanced_heathflowers" && prefabName != "instanced_shrub" && prefabName != "instanced_vass")
                return !((prefabName == "instanced_meadows_grass" && propertyName == "_MainTex") || (prefabName == "instanced_meadows_grass_short" && propertyName == "_MainTex") || (prefabName == "instanced_mistlands_grass_short" && propertyName == "_MainTex"));

            return ReplaceColor(color, isGrass, isMoss);
        }

        private static bool GenerateOnlyWinterColor(string prefabName, string materialName, string propertyName)
        {
            if (materialName == "Pine_tree_small_dead")
                return true;

            /*if (materialName == "Vines_Mat")
                return true;*/

            return false;
        }

        public static bool GetTextureVariants(string prefabName, Material material, string propertyName, Texture texture, out TextureVariants textureVariants)
        {
            textureVariants = new TextureVariants(texture);

            Color[] pixels = GetTexturePixels(texture, textureVariants.properties, out textureVariants.originalPNG);
            if (pixels.Length < 1)
                return false;

            bool isGrass = IsGrass(material.shader.name);
            bool isMoss = IsMoss(propertyName);

            List<int> pixelsToChange = new List<int>();
            for (int i = 0; i < pixels.Length; i++)
                if (IsPixelToChange(pixels[i], i, textureVariants.properties, isGrass, isMoss, prefabName, material.name, propertyName))
                    pixelsToChange.Add(i);

            if (pixelsToChange.Count == 0)
                return false;

            foreach (Season season in Enum.GetValues(typeof(Season)))
            {
                List<Color> colorVariants = new List<Color>();
                for (int i = 1; i <= seasonColorVariants; i++)
                {
                    Color colorVariant;
                    if (isGrass)
                        colorVariant = instance.GetGrassConfigColor(season, i);
                    else if (isMoss)
                        colorVariant = instance.GetMossConfigColor(season, i);
                    else
                        colorVariant = instance.GetSeasonConfigColor(season, i);

                    if (IsPine(material.name, prefabName))
                        colorVariant.a /= season == Season.Winter ? 1.5f : 2f;

                    if (GenerateOnlyWinterColor(prefabName, material.name, propertyName) && season != Season.Winter)
                        colorVariants.Add(Color.clear);
                    else
                        colorVariants.Add(colorVariant);

                }

                GenerateColorVariants(season, colorVariants.ToArray(), pixels, pixelsToChange.ToArray(), textureVariants.properties, textureVariants);
            }

            return textureVariants.Initialized();
        }

        private static Color[] GetTexturePixels(Texture texture, TextureProperties texProperties, out byte[] originalPNG)
        {
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
            originalPNG = textureCopy.EncodeToPNG();

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

        private static bool ReplaceColor(Color color, bool isGrass, bool isMoss)
        {
            if (color.a == 0f)
                return false;

            HSLColor hslcolor = new HSLColor(color);

            if (isGrass)
                return ColorFits(hslcolor, 100f, 35f, 0.13f) || ColorFits(hslcolor, 60f, 5f, 0.55f, 0.5f) || ColorFits(hslcolor, 50f, 15f, -0.35f, 0.35f) || ColorFits(hslcolor, 50f, 10f, 0.4f); //&& !ColorFits(hslcolor, 50f, 10f, 0.13f);

            else if (isMoss)
                return true;// ColorFits(hslcolor, 90f, 50f, 0.2f);

            return ColorFits(hslcolor, 120f, 40f, 0.15f) || ColorFits(hslcolor, 73f, 18f, 0.20f, 0.18f) || ColorFits(hslcolor, 45f, 12f, 0.28f, 0.26f);
            //ColorFits(hslcolor, 0.24f, 85f, 50f) || 
        }

        private static bool ReplaceColorOverride(string prefabName, string materialName, string propertyName)
        {
            if (materialName == "Vines_Mat")
                return true;

            return false;
        }

        private static bool ColorFits(HSLColor hslcolor, float hue, float hueDelta, float saturation = 0f, float luminance = 0f)
        {
            return GetHueDistance(hslcolor.h, hue) <= hueDelta
                && (saturation == 0f || (saturation > 0 && hslcolor.s >= saturation) || (saturation < 0 && hslcolor.s <= -saturation))
                && (luminance == 0f || (luminance > 0 && hslcolor.l >= luminance) || (luminance < 0 && hslcolor.l <= -luminance));
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
            return shaderName == "Custom/Grass";
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
        public static readonly Dictionary<string, string[]> shaderColors = new Dictionary<string, string[]>
            {
                { "Custom/StaticRock", new string[] { "_MossColor" }}
            };

        public static readonly Dictionary<string, string[]> materialColors = new Dictionary<string, string[]>
            {
                { "Vines_Mat", new string[] { "_Color" }}
            };

        public static readonly Dictionary<string, string[]> shaderTextures = new Dictionary<string, string[]>
            {
                { "Custom/Vegetation", new string[] { "_MainTex" } },
                { "Custom/Grass", new string[] { "_MainTex", "_TerrainColorTex" } },
                //{ "Custom/Creature", new string[] { "_MainTex" } },
                { "Custom/StaticRock", new string[] { "_MossTex" }}
            };

        public static readonly Dictionary<string, string[]> shaderIgnoreMaterial = new Dictionary<string, string[]>
            {
                { "Custom/Vegetation", new string[] { "bark", "trunk", "_wood", "HildirFlowerGirland_", "HildirTentCloth_", "TraderTent_" } }
            };

        public static readonly Dictionary<string, string[]> shadersTypes = new Dictionary<string, string[]>
            {
                { typeof(MeshRenderer).Name, new string[] { "Custom/Vegetation", "Custom/Grass", "Custom/StaticRock" } },
                { typeof(InstanceRenderer).Name, new string[] { "Custom/Vegetation", "Custom/Grass" } },
                //{ typeof(SkinnedMeshRenderer).Name, new string[] { "Custom/Creature" } }
            };

        public static readonly List<string> piecePrefab = new List<string>()
        {
            "vines"
        };

        private static readonly List<string> ignorePrefab = new List<string>()
        {
            "SwampTree2_log",
            "Rock_destructible_test",
            "DevHouse1",
            "SwampTree1_Stub",
            "HugeRoot1",
            "Hildir_cave",
            "PineTree_log",
            "PineTree_log_half",
            "MountainGrave01",
            "PineTree_log_halfOLD",
            "PineTree_logOLD",
            "sapling_magecap",
            "FirTree_log",
            "FirTree_log_half",
            "Hildir_crypt"
        };

        private static readonly List<string> ignorePrefabPartialName = new List<string>()
        {
            "Mistlands_GuardTower",
            "WoodHouse",
            "StoneTower",
            "SunkenCrypt",
            "MountainCave",
            "Mistlands_Lighthouse",
            "Mistlands_Viaduct",
            "Mistlands_Dvergr",
            "Mistlands_Statue",
            "Mistlands_Excavation",
            "Mistlands_Giant",
            "Mistlands_Harbour",
            "dvergrtown_",
            //"blackmarble_creep"
        };

        public static void FillWithGameData()
        {
            LogInfo("Caching clutters");
            AddClutters();

            LogInfo("Caching prefabs");
            AddZNetScenePrefabs();

            LogInfo("Caching locations");
            AddLocations();

            LogInfo($"Added {SeasonalTextureVariants.controllers.Count} controllers, {SeasonalTextureVariants.textures.Count} textures");
        }

        private static void AddLocations()
        {
            foreach (ZoneSystem.ZoneLocation loc in ZoneSystem.instance.m_locations)
            {
                if (loc.m_prefab == null)
                    continue;

                if (ignorePrefab.Contains(loc.m_prefabName))
                    continue;

                if (ignorePrefabPartialName.Any(namepart => loc.m_prefabName.Contains(namepart)))
                    continue;

                Transform root = loc.m_prefab.transform.Find("exterior") ?? loc.m_prefab.transform;

                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
                {
                    if (renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null)
                        continue;

                    CacheMaterials(renderer.sharedMaterials, loc.m_prefabName, renderer.name, renderer.GetType().Name, renderer.transform.GetPath());
                }
            }
        }

        private static void AddClutters()
        {
            foreach (ClutterSystem.Clutter clutter in ClutterSystem.instance.m_clutter.Where(c => c.m_prefab != null && !ignorePrefab.Contains(c.m_prefab.name)))
            {
                if (!clutter.m_prefab.TryGetComponent(out InstanceRenderer renderer))
                    continue;

                CacheMaterials(new Material[1] { renderer.m_material }, clutter.m_prefab.name, renderer.name, renderer.GetType().Name, renderer.transform.GetPath());
            }
        }

        private static void CacheMaterials(Material[] materials, string prefabName, string rendererName, string rendererType, string rendererPath, int lodLevel = -1, bool singleRenderer = false)
        {
            for (int m = 0; m < materials.Length; m++)
            {
                Material material = materials[m];

                if (material == null)
                    continue;

                if (!shadersTypes.TryGetValue(rendererType, out string[] shaders) || !shaders.Contains(material.shader.name)
                    || shaderIgnoreMaterial.TryGetValue(material.shader.name, out string[] ignoreMaterial) && ignoreMaterial.Any(ignore => material.name.IndexOf(ignore, StringComparison.OrdinalIgnoreCase) >= 0))
                    continue;

                bool isNew = !SeasonalTextureVariants.controllers.TryGetValue(prefabName, out PrefabController controller);

                if (isNew)
                    controller = new PrefabController();

                CachedRenderer cachedRenderer = new CachedRenderer
                {
                    name = rendererName,
                    type = rendererType
                };

                if (shaderColors.TryGetValue(material.shader.name, out string[] colorNames))
                    foreach (string propertyName in colorNames)
                    {
                        Color color = material.GetColor(propertyName);
                        if (color == null || color == Color.clear || color == Color.white || color == Color.black)
                            continue;

                        if (SeasonalTextureVariantsGenerator.GetColorVariants(prefabName, material, propertyName, color, out Color[] colors))
                            cachedRenderer.AddMaterialColors(material, propertyName, colors);
                    }


                if (materialColors.TryGetValue(material.name, out string[] materialColorNames))
                {
                    foreach (string propertyName in materialColorNames)
                    {
                        Color color = material.GetColor(propertyName);
                        if (color == null || color == Color.clear || color == Color.white || color == Color.black)
                            continue;

                        if (SeasonalTextureVariantsGenerator.GetColorVariants(prefabName, material, propertyName, color, out Color[] colors))
                            cachedRenderer.AddMaterialColors(material, propertyName, colors);
                    }
                }

                if (shaderTextures.TryGetValue(material.shader.name, out string[] textureNames))
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
                        else if (SeasonalTextureVariantsGenerator.GetTextureVariants(prefabName, material, propertyName, texture, out TextureVariants textureVariants))
                        {
                            SeasonalTextureVariants.textures.Add(textureID, textureVariants);
                            cachedRenderer.AddMaterialTexture(material, propertyName, textureID);
                        }
                    }

                if (!cachedRenderer.Initialized())
                    continue;

                if (lodLevel >= 0)
                {
                    if (!controller.lodLevelMaterials.TryGetValue(lodLevel, out List<CachedRenderer> lodRenderers))
                        controller.lodLevelMaterials.Add(lodLevel, new List<CachedRenderer>() { cachedRenderer });
                    else
                        lodRenderers.Add(cachedRenderer);
                }
                else if (singleRenderer)
                {
                    controller.cachedRenderer = cachedRenderer;
                }
                else
                {
                    if (!controller.renderersInHierarchy.ContainsKey(rendererPath))
                        controller.renderersInHierarchy.Add(rendererPath, cachedRenderer);
                }

                if (controller.Initialized())
                {
                    if (isNew)
                        SeasonalTextureVariants.controllers.Add(prefabName, controller);

                    LogInfo($"Caching {prefabName}{(controller.cachedRenderer == null ? "" : ", main renderer,")} {controller.lodLevelMaterials.Count} LODs, {controller.renderersInHierarchy.Count} renderersInHierarchy");

                    if (singleRenderer)
                        return;
                }
            }
        }

        private static void AddZNetScenePrefabs()
        {
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (ignorePrefab.Contains(prefab.name) || prefab.layer == 8 || prefab.layer == 12)
                    continue;

                if (prefab.layer == 0 && prefab.TryGetComponent<Ship>(out _))
                    continue;

                if (prefab.layer == 16 && !prefab.TryGetComponent<Pickable>(out _) && !prefab.TryGetComponent<Plant>(out _))
                    continue;

                if (prefab.layer == 10 && !piecePrefab.Contains(prefab.name) && !prefab.TryGetComponent<Pickable>(out _) && !prefab.TryGetComponent<Plant>(out _))
                {
                    continue;
                }
                
                if (ignorePrefabPartialName.Any(namepart => prefab.name.Contains(namepart)))
                    continue;

                if (prefab.layer == 15 && (prefab.TryGetComponent<MineRock5>(out _) || prefab.TryGetComponent<MineRock>(out _)))
                {
                    MeshRenderer renderer = prefab.GetComponentInChildren<MeshRenderer>();

                    if (renderer == null)
                        return;

                    if (renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null)
                        return;

                    CacheMaterials(renderer.sharedMaterials, prefab.name, renderer.name, renderer.GetType().Name, renderer.transform.GetPath(), singleRenderer: true);
                    continue;
                }

                if (prefab.layer != 9)
                {
                    if (prefab.TryGetComponent(out LODGroup lodGroup) && lodGroup.lodCount > 1)
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

                                CacheMaterials(renderer.sharedMaterials, prefab.name, renderer.name, renderer.GetType().Name, renderer.transform.GetPath(), lodLevel);
                            }
                        }
                    }
                    else
                    {
                        foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>())
                        {
                            if (renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null)
                                continue;

                            CacheMaterials(renderer.sharedMaterials, prefab.name, renderer.name, renderer.GetType().Name, renderer.transform.GetPath());
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

                        CacheMaterials(renderer.sharedMaterials, prefab.name, renderer.name, renderer.GetType().Name, renderer.transform.GetPath());
                    }
                }
            }
        }


    }

}