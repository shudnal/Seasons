using System;
using System.Collections.Generic;
using System.Linq;
using static Seasons.Seasons;
using UnityEngine;
using System.IO;
using HarmonyLib;
using Object = UnityEngine.Object;
using Newtonsoft.Json;
using System.Runtime.Serialization.Formatters.Binary;
using static Seasons.PrefabController;

namespace Seasons
{
    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
    public static class ZoneSystem_Start_SeasonsCache
    {
        private static void Postfix(ZoneSystem __instance)
        {
            if (!UseTextureControllers())
                return;

            if (SeasonalTextureVariants.Initialize())
            {
                __instance.gameObject.AddComponent<PrefabVariantController>();
                PrefabVariantController.AddControllerToPrefabs();
                ClutterVariantController.Init();
                __instance.gameObject.AddComponent<ZoneSystemVariantController>().Init(__instance);
                FillPickablesListToControlGrowth();
                InvalidatePositionsCache();
            }
            else
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

            public CachedRenderer(string rendererName, string rendererType)
            {
                name = rendererName;
                type = rendererType;
            }

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

        public Dictionary<string, Dictionary<int, List<CachedRenderer>>> lodsInHierarchy = new Dictionary<string, Dictionary<int, List<CachedRenderer>>>();
        public Dictionary<int, List<CachedRenderer>> lodLevelMaterials = new Dictionary<int, List<CachedRenderer>>();
        public Dictionary<string, CachedRenderer> renderersInHierarchy = new Dictionary<string, CachedRenderer>();
        public CachedRenderer cachedRenderer;
        public Dictionary<string, string[]> particleSystemStartColors;

        public bool Initialized()
        {
            return lodsInHierarchy.Count > 0 || lodLevelMaterials.Count > 0 || renderersInHierarchy.Count > 0 || cachedRenderer != null || particleSystemStartColors != null;
        }

        public override string ToString()
        {
            return $"{(cachedRenderer == null ? "" : " 1 main renderer")}{(particleSystemStartColors == null ? "" : " 1 particles start color")} " +
                        $"{(lodsInHierarchy.Count > 0 ? $" {lodsInHierarchy.Count} LOD groups" : "")}" +
                        $"{(lodLevelMaterials.Count > 0 ? $" {lodLevelMaterials.Count} LODs" : "")}" +
                        $"{(renderersInHierarchy.Count > 0 ? $" {renderersInHierarchy.Count} renderersInHierarchy" : "")}";
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

        internal const string cacheSubdirectory = "Cache";
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

        public void SaveToJSON()
        {
            string folder = Path.Combine(configDirectory, cacheSubdirectory);

            Directory.CreateDirectory(folder);

            string filename = Path.Combine(folder, prefabCacheFileName);

            File.WriteAllText(filename, JsonConvert.SerializeObject(controllers, Formatting.Indented));

            string directory = Path.Combine(folder, texturesDirectory);

            LogInfo($"Saved cache file {filename}");

            foreach (KeyValuePair<int, TextureData> tex in textures)
            {
                string texturePath = Path.Combine(directory, tex.Key.ToString());

                Directory.CreateDirectory(texturePath);

                File.WriteAllBytes(Path.Combine(texturePath, $"{tex.Value.name}{originalPostfix}"), tex.Value.originalPNG);

                File.WriteAllText(Path.Combine(texturePath, texturePropertiesFileName), JsonUtility.ToJson(tex.Value.properties));

                foreach (KeyValuePair<Season, Dictionary<int, byte[]>> season in tex.Value.variants)
                    foreach (KeyValuePair<int, byte[]> texData in season.Value)
                        File.WriteAllBytes(Path.Combine(texturePath, SeasonFileName(season.Key, texData.Key)), texData.Value);
            }

            LogInfo($"Saved {textures.Count} textures at {directory}");
        }

        public void LoadFromJSON()
        {
            string folder = Path.Combine(configDirectory, cacheSubdirectory);

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
                LogWarning($"Error loading JSON cache data from {cacheFile[0].FullName}\n{ex}");
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

        public void SaveToBinary()
        {
            string folder = Path.Combine(configDirectory, cacheSubdirectory);

            Directory.CreateDirectory(folder);

            using (FileStream fs = new FileStream(Path.Combine(folder, prefabCacheCommonFile), FileMode.OpenOrCreate))
            {
                BinaryFormatter bf = new BinaryFormatter();
                bf.Serialize(fs, this);
            }

            LogInfo($"Saved cache file {Path.Combine(folder, prefabCacheCommonFile)}");
        }

        public void LoadFromBinary()
        {
            string folder = Path.Combine(configDirectory, cacheSubdirectory);

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
                LogWarning($"Error loading binary cache data from {filename}:\n {ex}");
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

        public static bool Initialize()
        {
            if (Initialized())
                return true;

            controllers.Clear();
            textures.Clear();

            CachedData cachedData = new CachedData();
            if (cacheStorageFormat.Value == CacheFormat.Json)
                cachedData.LoadFromJSON();
            else
                cachedData.LoadFromBinary();

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
                            cachedData.SaveToBinary();
                        else if (cacheStorageFormat.Value == CacheFormat.Json)
                            cachedData.SaveToJSON();
                        else
                        {
                            cachedData.SaveToJSON();
                            cachedData.SaveToBinary();
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
        public static bool GetColorVariants(string prefabName, string rendererName, Material material, string propertyName, Color color, out Color[] colors, bool isPlant)
        {
            colors = null;

            bool isGrass = IsGrass(material.shader.name);
            bool isMoss = IsMoss(propertyName);
            bool isCreature = IsCreature(material.shader.name);
            bool replaceOverride = ReplaceColorOverride(prefabName, material.name, propertyName);

            if (!replaceOverride && !(isCreature ? ReplaceCreatureColor(color, prefabName, rendererName, material.name) : ReplaceColor(color, isGrass, isMoss)))
                return false;

            List<Color> colorsList = new List<Color>();
            foreach (Season season in Enum.GetValues(typeof(Season)))
            {
                for (int i = 1; i <= seasonColorVariants; i++)
                {
                    Color colorVariant;
                    if (isGrass || isPlant || material.name == "yggdrasil_branch")
                        colorVariant = GetGrassConfigColor(season, i);
                    else if (isMoss)
                        colorVariant = GetMossConfigColor(season, i);
                    else if (isCreature)
                        colorVariant = GetCreatureConfigColor(season, i);
                    else
                        colorVariant = GetSeasonConfigColor(season, i);

                    if (IsPine(material.name, prefabName))
                        colorVariant.a /= season == Season.Winter ? 1.5f : 2f;

                    if (IsPiece(material))
                        colorVariant.a /= 1.5f;

                    if (GenerateOnlyWinterColor(prefabName, material, propertyName) && season != Season.Winter)
                        colorsList.Add(color);
                    else
                        colorsList.Add(MergeColors(color, colorVariant, colorVariant.a, season == Season.Winter));
                }
            }

            colors = colorsList.ToArray();

            return true;
        }

        private static bool IsPixelToChange(Color color, int pos, TextureProperties properties, bool isGrass, bool isMoss, string prefabName, string rendererName, Material material, string propertyName)
        {
            if (IsCreature(material.shader.name))
            {
                return ReplaceCreatureColor(color, prefabName, rendererName, material.name);
            }

            // From top left pixel
            int widthPix = pos % properties.width;
            int heightPix = properties.height - pos / properties.width;

            if (material.name.StartsWith("Pine_tree_"))
                if (widthPix < 94 && heightPix < 45)
                    return false;

            if (material.name.StartsWith("Fir_tree_sapling"))
                if (widthPix < 21 && heightPix < 12)
                    return false;

            if (prefabName == "FirTree")
                if (widthPix < 372 && heightPix < 165)
                    return false;

            if (prefabName == "Pinetree_01")
                if (widthPix > 126)
                    return false;

            if (material.name == "beehive")
                return widthPix > 97 && heightPix < 47;

            if (material.name == "Midsummerpole_mat")
                return heightPix < 176 || widthPix > 53 && heightPix < 184;

            if (prefabName.IndexOf("goblin_roof", StringComparison.OrdinalIgnoreCase) >= 0 && material.name.IndexOf("GoblinVillage_Cloth", StringComparison.OrdinalIgnoreCase) >= 0)
                return (widthPix <= 130 && heightPix < 231) || (widthPix < 202 && heightPix < 86);

            if (prefabName.IndexOf("darkwood_roof", StringComparison.OrdinalIgnoreCase) >= 0 && material.name.IndexOf("RoofShingles", StringComparison.OrdinalIgnoreCase) >= 0)
                return widthPix <= 54;

            if (prefabName.IndexOf("copper_roof", StringComparison.OrdinalIgnoreCase) >= 0 && material.name.IndexOf("RoofShingles", StringComparison.OrdinalIgnoreCase) >= 0)
                return widthPix <= 54;

            if (prefabName.IndexOf("wood_roof", StringComparison.OrdinalIgnoreCase) >= 0 && material.name.IndexOf("straw", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (isGrass && !prefabName.StartsWith("Pickable_Flax") && prefabName != "sapling_flax" && prefabName != "instanced_heathflowers" && prefabName != "instanced_shrub" && prefabName != "instanced_vass")
                return !((prefabName == "instanced_meadows_grass" && propertyName == "_MainTex") || (prefabName == "instanced_meadows_grass_short" && propertyName == "_MainTex") || (prefabName == "instanced_mistlands_grass_short" && propertyName == "_MainTex"));

            return ReplaceColor(color, isGrass, isMoss);
        }

        private static bool GenerateOnlyWinterColor(string prefabName, Material material, string propertyName)
        {
            if (material.name == "Pine_tree_small_dead")
                return true;

            if (material.name == "swamptree1_branch")
                return true;

            if (material.name == "swamptree2_branch")
                return true;

            if (material.name == "Midsummerpole_mat")
                return false;

            if (IsPiece(material))
                return true;

            if (IsCreature(material.shader.name))
                return true;

            return false;
        }

        public static bool GetTextureVariants(string prefabName, string rendererName, Material material, string propertyName, Texture texture, out TextureVariants textureVariants, bool isPlant)
        {
            textureVariants = new TextureVariants(texture);

            Color[] pixels = GetTexturePixels(texture, textureVariants.properties, out textureVariants.originalPNG);
            if (pixels.Length < 1)
                return false;

            bool isGrass = IsGrass(material.shader.name);
            bool isMoss = IsMoss(propertyName);

            List<int> pixelsToChange = new List<int>();
            for (int i = 0; i < pixels.Length; i++)
                if (IsPixelToChange(pixels[i], i, textureVariants.properties, isGrass, isMoss, prefabName, rendererName, material, propertyName))
                    pixelsToChange.Add(i);

            if (pixelsToChange.Count == 0)
                return false;

            foreach (Season season in Enum.GetValues(typeof(Season)))
            {
                List<Color> colorVariants = new List<Color>();
                for (int i = 1; i <= seasonColorVariants; i++)
                {
                    Color colorVariant;
                    if (isGrass || isPlant)
                        colorVariant = GetGrassConfigColor(season, i);
                    else if (isMoss)
                        colorVariant = GetMossConfigColor(season, i);
                    else
                        colorVariant = GetSeasonConfigColor(season, i);

                    if (IsPine(material.name, prefabName))
                        colorVariant.a /= season == Season.Winter ? 1.5f : 2f;

                    if (IsPiece(material))
                        colorVariant.a /= prefabName.Contains("goblin") ? 2f : 1.5f;
                    else if (IsCreature(material.shader.name))
                        colorVariant.a /= (prefabName.IndexOf("lox", StringComparison.OrdinalIgnoreCase) >= 0) ? 1.5f : 3f;
                    else if (prefabName == "YggdrasilBranch" && season == Season.Winter)
                        colorVariant.a /= 3;

                    if (GenerateOnlyWinterColor(prefabName, material, propertyName) && season != Season.Winter)
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
                return ColorFits(hslcolor, 100f, 35f, 0.13f) || ColorFits(hslcolor, 60f, 5f, 0.55f, 0.5f) || ColorFits(hslcolor, 50f, 15f, -0.35f, 0.35f) || ColorFits(hslcolor, 50f, 10f, 0.4f);
            else if (isMoss)
                return true;

            return ColorFits(hslcolor, 120f, 40f, 0.15f) || ColorFits(hslcolor, 73f, 18f, 0.20f, 0.18f) || ColorFits(hslcolor, 45f, 12f, 0.28f, 0.26f);
        }

        private static bool ReplaceCreatureColor(Color color, string prefabName, string rendererName, string materialName)
        {
            if (color.a == 0f)
                return false;

            HSLColor hslcolor = new HSLColor(color);

            if ((materialName == "HildirsLox" || prefabName == "Lox" || prefabName == "lox_ragdoll") && rendererName.StartsWith("Furr"))
                return ColorFits(hslcolor, 35f, 16f, 0.40f, -0.45f) || ColorFits(hslcolor, 47f, 4f, -0.45f, -0.45f);
            else if (prefabName == "Lox_Calf" && rendererName.StartsWith("Furr"))
                return ColorFits(hslcolor, 50f, 12f, -0.55f, 0.18f);
            else if (prefabName.IndexOf("draugr", StringComparison.OrdinalIgnoreCase) >= 0)
                return ColorFits(hslcolor, 120f, 40f, 0.15f) || ColorFits(hslcolor, 73f, 18f, 0.19f, -0.40f);
            else if (prefabName.IndexOf("Abomination", StringComparison.OrdinalIgnoreCase) >= 0)
                return ColorFits(hslcolor, 120f, 40f, 0.15f) || ColorFits(hslcolor, 73f, 18f, 0.19f, -0.50f);
            else if (prefabName.IndexOf("grey", StringComparison.OrdinalIgnoreCase) == 0)
                return ColorFits(hslcolor, 120f, 40f, 0.15f) || ColorFits(hslcolor, 71f, 20f, 0.18f, -0.50f);

            return false;
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

        public static Color MergeColors(Color color1, Color color2, float t, bool winterColor)
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

        private static bool IsPiece(Material material)
        {
            return material.shader.name == "Custom/Piece" || material.name.StartsWith("GoblinVillage");
        }

        private static bool IsCreature(string shaderName)
        {
            return shaderName == "Custom/Creature";
        }

        private static bool IsPine(string materialName, string prefab)
        {
            return materialName.IndexOf("pine", StringComparison.OrdinalIgnoreCase) >= 0 || prefab.IndexOf("pine", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public static class SeasonalTexturePrefabCache
    {
        [Serializable]
        public class MaterialCacheSettings
        {
            public List<string> particleSystemStartColors;
            public Dictionary<string, string[]> shadersTypes;
            public Dictionary<string, string[]> shaderColors;
            public Dictionary<string, string[]> materialColors;
            public Dictionary<string, string[]> shaderTextures;
            public Dictionary<string, string[]> materialTextures;
            public Dictionary<string, string[]> shaderIgnoreMaterial;
            public Dictionary<string, string[]> shaderOnlyMaterial;
            public List<string> effectPrefab;
            public List<string> creaturePrefab;
            public List<string> piecePrefab;
            public List<string> piecePrefabPartialName;
            public List<string> ignorePrefab;
            public List<string> ignorePrefabPartialName;

            public MaterialCacheSettings(bool loadDefaults = false)
            {
                if (!loadDefaults)
                    return;

                particleSystemStartColors = new List<string>()
                {
                    "leaf_particles",
                    "vfx_bush_destroyed",
                    "vfx_bush_destroyed_heath",
                    "vfx_bush_leaf_puff",
                    "vfx_bush_leaf_puff_heath",
                    "vfx_bush2_e_hit",
                    "vfx_bush2_en_destroyed",
                    "vfx_shrub_2_hit",
                };

                shaderColors = new Dictionary<string, string[]>
                {
                    { "Custom/StaticRock", new string[] { "_MossColor" }},
                    { "Custom/Yggdrasil_root", new string[] { "_MossColor" }},
                };

                materialColors = new Dictionary<string, string[]>
                {
                    { "Vines_Mat", new string[] { "_Color" }},
                    { "carrot_blast", new string[] { "_Color" }},
                    { "barley_sapling", new string[] { "_Color" }},
                    { "Bush01_raspberry", new string[] { "_Color" }},
                };

                materialTextures = new Dictionary<string, string[]>
                {
                    { "swamptree1_log", new string[] { "_MossTex" }},
                    { "swamptree2_log", new string[] { "_MossTex" }},
                    { "swamptree1_bark", new string[] { "_MossTex" }},
                    { "swamptree2_bark", new string[] { "_MossTex" }},
                    { "swamptree_stump", new string[] { "_MossTex" }},
                    { "beech_bark", new string[] { "_MossTex" }},
                    { "oak_bark", new string[] { "_MossTex" }},
                    { "yggdrasil_branch", new string[] { "_MossTex" }},
                };

                shaderTextures = new Dictionary<string, string[]>
                {
                    { "Custom/Vegetation", new string[] { "_MainTex" } },
                    { "Custom/Grass", new string[] { "_MainTex", "_TerrainColorTex" } },
                    { "Custom/Creature", new string[] { "_MainTex" } },
                    { "Custom/Piece", new string[] { "_MainTex" }},
                    { "Custom/StaticRock", new string[] { "_MossTex" }},
                    { "Standard", new string[] { "_MainTex" }},
                    { "Particles/Standard Surface2", new string[] { "_MainTex" }},
                    { "Custom/Yggdrasil", new string[] { "_MainTex" }},
                };

                shaderIgnoreMaterial = new Dictionary<string, string[]>
                {
                    { "Custom/Vegetation", new string[] { "bark", "trunk", "_wood", "HildirFlowerGirland_", "HildirTentCloth_", "TraderTent_" } },
                };

                shaderOnlyMaterial = new Dictionary<string, string[]>
                {
                    { "Custom/Piece", new string[] { "straw", "RoofShingles", "beehive", "Midsummerpole_mat", "Pine_tree_xmas" } },
                    { "Custom/Creature", new string[] { "HildirsLox", "lox", "lox_calf",
                    "Draugr_Archer_mat", "Draugr_mat", "Draugr_elite_mat", "Abomination_mat",
                    "greyling", "greydwarf", "greydwarf_elite", "greydwarf_shaman" } },
                    { "Standard", new string[] { "beech_particle", "birch_particle", "branch_particle", "branch_dead_particle", "oak_particle", "shoot_leaf_particle" }},
                    { "Particles/Standard Surface2", new string[] { "shrub2_leafparticle", "shrub2_leafparticle_heath" }},
                };

                shadersTypes = new Dictionary<string, string[]>
                {
                    { typeof(MeshRenderer).Name, new string[] { "Custom/Vegetation", "Custom/Grass", "Custom/StaticRock", "Custom/Piece", "Custom/Yggdrasil", "Custom/Yggdrasil_root" } },
                    { typeof(InstanceRenderer).Name, new string[] { "Custom/Vegetation", "Custom/Grass" } },
                    { typeof(SkinnedMeshRenderer).Name, new string[] { "Custom/Creature" } },
                    { typeof(ParticleSystemRenderer).Name, new string[] { "Standard", "Particles/Standard Surface2" } }
                };

                effectPrefab = new List<string>()
                {
                    "lox_ragdoll",
                    "loxcalf_ragdoll",
                    "Draugr_elite_ragdoll",
                    "Draugr_ragdoll",
                    "Draugr_ranged_ragdoll",
                    "Abomination_ragdoll",
                    "Greydwarf_ragdoll",
                    "Greydwarf_elite_ragdoll",
                    "Greydwarf_Shaman_ragdoll",
                    "Greyling_ragdoll",
                    "vfx_beech_cut",
                    "vfx_oak_cut",
                    "vfx_yggashoot_cut",
                    "vfx_bush_destroyed",
                    "vfx_bush_destroyed_heath",
                    "vfx_bush_leaf_puff",
                    "vfx_bush_leaf_puff_heath",
                    "vfx_bush2_e_hit",
                    "vfx_bush2_en_destroyed",
                };

                creaturePrefab = new List<string>()
                {
                    "Lox",
                    "Lox_Calf",
                    "Draugr",
                    "Draugr_Elite",
                    "Draugr_Ranged",
                    "Abomination",
                    "Greydwarf",
                    "Greydwarf_Elite",
                    "Greydwarf_Shaman",
                    "Greyling",
                };

                piecePrefab = new List<string>()
                {
                    "vines",
                    "piece_beehive",
                    "piece_maypole",
                    "piece_xmastree"
                };

                piecePrefabPartialName = new List<string>()
                {
                    "wood_roof",
                    "copper_roof",
                    "goblin_roof",
                };

                ignorePrefab = new List<string>()
                {
                    "Rock_destructible_test",
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
                    "Hildir_crypt",
                    "MountainGraveStone01",
                    "crypt_skeleton_chest",
                    "dungeon_sunkencrypt_irongate_rusty",
                    "stonechest",
                    "SunkenKit_int_towerwall",
                    "SunkenKit_int_towerwall_LOD",
                    "marker01",
                    "marker02",
                };

                ignorePrefabPartialName = new List<string>()
                {
                    "Mistlands_GuardTower",
                    "WoodHouse",
                    "DevHouse",
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
                    "OLD_wood_roof",
                    "AbandonedLogCabin",
                    "DrakeNest",
                };

            }
        }

        [Serializable]
        public class ColorsCacheSettings
        {
            [Serializable]
            public class ColorVariant
            {
                public bool useColor = true;
                public string color;
                public float targetProportion;
                public bool preserveAlphaChannel = true;
                public bool reduceOriginalColorToGrayscale;
                public bool restoreLuminance = true;

                [NonSerialized]
                public Color colorValue = Color.black;

                public Color MergeColors(Color colorToMerge)
                {
                    if (colorValue == Color.black && ColorUtility.ToHtmlStringRGBA(colorValue) != color && !ColorUtility.TryParseHtmlString(color, out colorValue))
                        LogInfo($"Error at parsing color: ({color})");

                    Color newColor = new Color(colorValue.r, colorValue.g, colorValue.b, preserveAlphaChannel ? colorToMerge.a : colorValue.a);
                    Color oldColor = reduceOriginalColorToGrayscale ? new Color(colorToMerge.grayscale, colorToMerge.grayscale, colorToMerge.grayscale, colorToMerge.a) : colorToMerge;

                    HSLColor newHSLColor = new HSLColor(Color.Lerp(oldColor, newColor, targetProportion));

                    if (restoreLuminance)
                        newHSLColor.l = new HSLColor(colorToMerge).l;

                    return newHSLColor.ToRGBA();
                }

                public ColorVariant()
                {
                    useColor = false;
                }

                public ColorVariant(Color color, float t)
                {
                    this.color = $"#{ColorUtility.ToHtmlStringRGBA(color)}";
                    targetProportion = t;
                }

                public ColorVariant(Color color, float t, bool grayscale, bool restoreLuminance)
                {
                    this.color = $"#{ColorUtility.ToHtmlStringRGBA(color)}";
                    targetProportion = t;
                    reduceOriginalColorToGrayscale = grayscale;
                    this.restoreLuminance = restoreLuminance;
                }
            }

            [Serializable]
            public class SeasonalColorVariants
            {
                public List<ColorVariant> Spring = new List<ColorVariant>();
                public List<ColorVariant> Summer = new List<ColorVariant>();
                public List<ColorVariant> Fall = new List<ColorVariant>();
                public List<ColorVariant> Winter = new List<ColorVariant>();
            }

            public SeasonalColorVariants seasonal = new SeasonalColorVariants();
            public SeasonalColorVariants grass = new SeasonalColorVariants();
            public SeasonalColorVariants moss = new SeasonalColorVariants();
            public SeasonalColorVariants creature = new SeasonalColorVariants();
            public SeasonalColorVariants piece = new SeasonalColorVariants();
            public SeasonalColorVariants conifer = new SeasonalColorVariants();

            public Dictionary<string, SeasonalColorVariants> prefabOverrides = new Dictionary<string, SeasonalColorVariants>();

            public ColorsCacheSettings(bool loadDefaults = false)
            {
                if (!loadDefaults)
                    return;

                seasonal.Spring.Add(new ColorVariant(new Color(0.27f, 0.80f, 0.27f), 0.75f));
                seasonal.Spring.Add(new ColorVariant(new Color(0.69f, 0.84f, 0.15f), 0.75f));
                seasonal.Spring.Add(new ColorVariant(new Color(0.43f, 0.56f, 0.11f), 0.75f));
                seasonal.Spring.Add(new ColorVariant());

                seasonal.Summer.Add(new ColorVariant(new Color(0.5f, 0.7f, 0.2f), 0.5f));
                seasonal.Summer.Add(new ColorVariant(new Color(0.7f, 0.7f, 0.2f), 0.5f));
                seasonal.Summer.Add(new ColorVariant(new Color(0.5f, 0.5f, 0f), 0.5f));
                seasonal.Summer.Add(new ColorVariant(new Color(0.7f, 0.7f, 0f), 0.2f));

                seasonal.Fall.Add(new ColorVariant(new Color(0.8f, 0.5f, 0f), 0.75f));
                seasonal.Fall.Add(new ColorVariant(new Color(0.8f, 0.3f, 0f), 0.75f));
                seasonal.Fall.Add(new ColorVariant(new Color(0.8f, 0.2f, 0f), 0.75f));
                seasonal.Fall.Add(new ColorVariant());

                seasonal.Winter.Add(new ColorVariant(new Color(1f, 0.98f, 0.98f), 0.65f, grayscale: true, restoreLuminance: false));
                seasonal.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.6f, grayscale: true, restoreLuminance: false));
                seasonal.Winter.Add(new ColorVariant(new Color(0.98f, 0.98f, 1f), 0.65f, grayscale: true, restoreLuminance: false));
                seasonal.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.65f, grayscale: true, restoreLuminance: false));

                grass.Spring.Add(new ColorVariant(new Color(0.27f, 0.80f, 0.27f), 0.75f));
                grass.Spring.Add(new ColorVariant(new Color(0.69f, 0.84f, 0.15f), 0.75f));
                grass.Spring.Add(new ColorVariant(new Color(0.43f, 0.56f, 0.11f), 0.75f));
                grass.Spring.Add(new ColorVariant());

                grass.Summer.Add(new ColorVariant(new Color(0.5f, 0.7f, 0.2f), 0.5f));
                grass.Summer.Add(new ColorVariant(new Color(0.7f, 0.75f, 0.2f), 0.5f));
                grass.Summer.Add(new ColorVariant(new Color(0.5f, 0.5f, 0f), 0.5f));
                grass.Summer.Add(new ColorVariant(new Color(0.7f, 0.7f, 0f), 0.2f));

                grass.Fall.Add(new ColorVariant(new Color(0.8f, 0.6f, 0.2f), 0.5f));
                grass.Fall.Add(new ColorVariant(new Color(0.8f, 0.5f, 0f), 0.5f));
                grass.Fall.Add(new ColorVariant(new Color(0.8f, 0.3f, 0f), 0.5f));
                grass.Fall.Add(new ColorVariant());

                grass.Winter.Add(new ColorVariant(new Color(1f, 0.98f, 0.98f), 0.65f, grayscale: true, restoreLuminance: false));
                grass.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.6f, grayscale: true, restoreLuminance: false));
                grass.Winter.Add(new ColorVariant(new Color(0.98f, 0.98f, 1f), 0.65f, grayscale: true, restoreLuminance: false));
                grass.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.65f, grayscale: true, restoreLuminance: false));

                moss.Spring.Add(new ColorVariant(new Color(0.43f, 0.56f, 0.11f), 0.25f));
                moss.Spring.Add(new ColorVariant());
                moss.Spring.Add(new ColorVariant(new Color(0.27f, 0.80f, 0.27f), 0.25f));
                moss.Spring.Add(new ColorVariant(new Color(0.69f, 0.84f, 0.15f), 0.25f));

                moss.Summer.Add(new ColorVariant(new Color(0.5f, 0.5f, 0f), 0.2f));
                moss.Summer.Add(new ColorVariant(new Color(0.7f, 0.7f, 0f), 0.07f));
                moss.Summer.Add(new ColorVariant(new Color(0.5f, 0.7f, 0.2f), 0.2f));
                moss.Summer.Add(new ColorVariant(new Color(0.7f, 0.75f, 0.2f), 0.2f));

                moss.Fall.Add(new ColorVariant(new Color(0.8f, 0.3f, 0f), 0.2f));
                moss.Fall.Add(new ColorVariant());
                moss.Fall.Add(new ColorVariant(new Color(0.8f, 0.6f, 0.2f), 0.2f));
                moss.Fall.Add(new ColorVariant(new Color(0.8f, 0.5f, 0f), 0.2f));

                moss.Winter.Add(new ColorVariant(new Color(0.98f, 0.98f, 1f), 0.65f, grayscale: true, restoreLuminance: false));
                moss.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.65f, grayscale: true, restoreLuminance: false));
                moss.Winter.Add(new ColorVariant(new Color(1f, 0.98f, 0.98f), 0.65f, grayscale: true, restoreLuminance: false));
                moss.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.6f, grayscale: true, restoreLuminance: false));

                conifer.Spring.Add(new ColorVariant(new Color(0.27f, 0.80f, 0.27f), 0.35f));
                conifer.Spring.Add(new ColorVariant(new Color(0.69f, 0.84f, 0.15f), 0.35f));
                conifer.Spring.Add(new ColorVariant(new Color(0.43f, 0.56f, 0.11f), 0.35f));
                conifer.Spring.Add(new ColorVariant());

                conifer.Summer.Add(new ColorVariant(new Color(0.5f, 0.7f, 0.2f), 0.25f));
                conifer.Summer.Add(new ColorVariant(new Color(0.7f, 0.7f, 0.2f), 0.25f));
                conifer.Summer.Add(new ColorVariant(new Color(0.5f, 0.5f, 0f), 0.25f));
                conifer.Summer.Add(new ColorVariant(new Color(0.7f, 0.7f, 0f), 0.1f));

                conifer.Fall.Add(new ColorVariant(new Color(0.8f, 0.5f, 0f), 0.35f));
                conifer.Fall.Add(new ColorVariant(new Color(0.8f, 0.3f, 0f), 0.35f));
                conifer.Fall.Add(new ColorVariant(new Color(0.8f, 0.2f, 0f), 0.35f));
                conifer.Fall.Add(new ColorVariant());

                conifer.Winter.Add(new ColorVariant(new Color(1f, 0.98f, 0.98f), 0.45f, grayscale: true, restoreLuminance: false));
                conifer.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.4f, grayscale: true, restoreLuminance: false));
                conifer.Winter.Add(new ColorVariant(new Color(0.98f, 0.98f, 1f), 0.45f, grayscale: true, restoreLuminance: false));
                conifer.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.45f, grayscale: true, restoreLuminance: false));
                
                creature.Winter.Add(new ColorVariant(new Color(1f, 0.98f, 0.98f), 0.25f, grayscale: true, restoreLuminance: false));
                creature.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.2f, grayscale: true, restoreLuminance: false));
                creature.Winter.Add(new ColorVariant(new Color(0.98f, 0.98f, 1f), 0.25f, grayscale: true, restoreLuminance: false));
                creature.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.25f, grayscale: true, restoreLuminance: false));

                piece.Winter.Add(new ColorVariant(new Color(1f, 0.98f, 0.98f), 0.4f, grayscale: true, restoreLuminance: false));
                piece.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.3f, grayscale: true, restoreLuminance: false));
                piece.Winter.Add(new ColorVariant(new Color(0.98f, 0.98f, 1f), 0.4f, grayscale: true, restoreLuminance: false));
                piece.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.4f, grayscale: true, restoreLuminance: false));

                prefabOverrides.Add("lox", new SeasonalColorVariants() 
                { 
                    Winter = new List<ColorVariant>() 
                    {
                        new ColorVariant(new Color(1f, 0.98f, 0.98f), 0.5f, grayscale: true, restoreLuminance: false),
                        new ColorVariant(new Color(1f, 1f, 1f), 0.4f, grayscale: true, restoreLuminance: false),
                        new ColorVariant(new Color(0.98f, 0.98f, 1f), 0.5f, grayscale: true, restoreLuminance: false),
                        new ColorVariant(new Color(1f, 1f, 1f), 0.5f, grayscale: true, restoreLuminance: false)
                    } 
                });

                prefabOverrides.Add("goblin", new SeasonalColorVariants()
                {
                    Winter = new List<ColorVariant>()
                    {
                        new ColorVariant(new Color(1f, 0.98f, 0.98f), 0.35f, grayscale: true, restoreLuminance: false),
                        new ColorVariant(new Color(1f, 1f, 1f), 0.3f, grayscale: true, restoreLuminance: false),
                        new ColorVariant(new Color(0.98f, 0.98f, 1f), 0.35f, grayscale: true, restoreLuminance: false),
                        new ColorVariant(new Color(1f, 1f, 1f), 0.35f, grayscale: true, restoreLuminance: false)
                    }
                });

                prefabOverrides.Add("YggdrasilBranch", new SeasonalColorVariants()
                {
                    Spring = seasonal.Spring,
                    Summer = seasonal.Summer,
                    Fall = seasonal.Fall,
                    Winter = new List<ColorVariant>() 
                    {
                        new ColorVariant(new Color(1f, 0.98f, 0.98f), 0.21f, grayscale: true, restoreLuminance: false),
                        new ColorVariant(new Color(1f, 1f, 1f), 0.2f, grayscale: true, restoreLuminance: false),
                        new ColorVariant(new Color(0.98f, 0.98f, 1f), 0.21f, grayscale: true, restoreLuminance: false),
                        new ColorVariant(new Color(1f, 1f, 1f), 0.21f, grayscale: true, restoreLuminance: false)
                    }
                });
            }
        }

        public const string settingsSubdirectory = "Cache settings";
        public const string defaultsSubdirectory = "Defaults";
        public const string materialsSettingsFileName = "Materials.json";
        public const string colorsSettingsFileName = "Colors.json";

        public static MaterialCacheSettings materialSettings = new MaterialCacheSettings(loadDefaults: true);
        public static ColorsCacheSettings colorSettings = new ColorsCacheSettings(loadDefaults: true);

        public static void InitSettings()
        {
            string folder = Path.Combine(configDirectory, settingsSubdirectory);
            Directory.CreateDirectory(folder);

            SaveDefaults(folder);

            string fileInConfigFolder = Path.Combine(folder, materialsSettingsFileName);
            if (File.Exists(fileInConfigFolder))
            {
                LogInfo($"Loading materials settings: {fileInConfigFolder}");
                try
                {
                    materialSettings = JsonConvert.DeserializeObject<MaterialCacheSettings>(File.ReadAllText(fileInConfigFolder));
                }
                catch (Exception e)
                {
                    LogWarning($"Error reading file ({fileInConfigFolder})! Error: {e.Message}");
                }
            }

            fileInConfigFolder = Path.Combine(folder, colorsSettingsFileName);
            if (File.Exists(fileInConfigFolder))
            {
                LogInfo($"Loading color settings: {fileInConfigFolder}");
                try
                {
                    colorSettings = JsonConvert.DeserializeObject<ColorsCacheSettings>(File.ReadAllText(fileInConfigFolder));
                }
                catch (Exception e)
                {
                    LogWarning($"Error reading file ({fileInConfigFolder})! Error: {e.Message}");
                }
            }
            
        }

        public static void SaveDefaults(string folder)
        {
            string defaultsFolder = Path.Combine(folder, defaultsSubdirectory);
            Directory.CreateDirectory(defaultsFolder);

            LogInfo($"Saving default materials settings");
            File.WriteAllText(Path.Combine(defaultsFolder, materialsSettingsFileName), JsonConvert.SerializeObject(new MaterialCacheSettings(loadDefaults: true), Formatting.Indented));

            LogInfo($"Saving default colors settings");
            File.WriteAllText(Path.Combine(defaultsFolder, colorsSettingsFileName), JsonConvert.SerializeObject(new ColorsCacheSettings(loadDefaults: true), Formatting.Indented));
        }

        public static void FillWithGameData()
        {
            LogInfo("Initializing settings");
            InitSettings();

            LogInfo("Caching clutters");
            AddClutters();

            LogInfo("Caching prefabs");
            AddZNetScenePrefabs();

            LogInfo("Caching locations");
            AddLocations();

            LogInfo("Caching yggdrasil branch");
            AddYggdrasilBranch();

            LogInfo($"Added {SeasonalTextureVariants.controllers.Count} controllers, {SeasonalTextureVariants.textures.Count} textures");
        }

        private static void AddYggdrasilBranch()
        {
            Transform yggdrasilBranch = EnvMan.instance.transform.Find("YggdrasilBranch");
            if (yggdrasilBranch == null)
                return;

            foreach (MeshRenderer mrenderer in yggdrasilBranch.GetComponentsInChildren<MeshRenderer>())
                CacheMaterials(mrenderer.sharedMaterials, "YggdrasilBranch", mrenderer.name, mrenderer.GetType().Name, mrenderer.transform.GetPath(), isPlant: true);
        }

        private static void AddLocations()
        {
            foreach (ZoneSystem.ZoneLocation loc in ZoneSystem.instance.m_locations)
            {
                if (loc.m_prefab == null)
                    continue;

                if (materialSettings.ignorePrefab.Contains(loc.m_prefabName))
                    continue;

                if (materialSettings.ignorePrefabPartialName.Any(namepart => loc.m_prefabName.Contains(namepart)))
                    continue;

                Transform root = loc.m_prefab.transform.Find("exterior") ?? loc.m_prefab.transform;

                foreach (MeshRenderer mrenderer in root.GetComponentsInChildren<MeshRenderer>())
                    CacheMaterials(mrenderer.sharedMaterials, loc.m_prefabName, mrenderer.name, mrenderer.GetType().Name, mrenderer.transform.GetPath());

                foreach (SkinnedMeshRenderer smrenderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                    CacheMaterials(smrenderer.sharedMaterials, loc.m_prefabName, smrenderer.name, smrenderer.GetType().Name, smrenderer.transform.GetPath());
            }
        }

        private static void AddClutters()
        {
            foreach (ClutterSystem.Clutter clutter in ClutterSystem.instance.m_clutter.Where(c => c.m_prefab != null && !materialSettings.ignorePrefab.Contains(c.m_prefab.name)))
            {
                if (!clutter.m_prefab.TryGetComponent(out InstanceRenderer renderer))
                    continue;

                CacheMaterials(new Material[1] { renderer.m_material }, clutter.m_prefab.name, renderer.name, renderer.GetType().Name, renderer.transform.GetPath());
            }
        }

        private static void CacheMaterials(Material[] materials, string prefabName, string rendererName, string rendererType, string transformPath, int lodLevel = -1, bool isSingleRenderer = false, bool isLodInHierarchy = false, bool isPlant = false)
        {
            for (int m = 0; m < materials.Length; m++)
            {
                Material material = materials[m];

                if (material == null)
                    continue;

                if (!materialSettings.shadersTypes.TryGetValue(rendererType, out string[] shaders) || !shaders.Contains(material.shader.name))
                    continue;

                if (!materialSettings.materialTextures.ContainsKey(material.name) && !materialSettings.materialColors.ContainsKey(material.name))
                    if (materialSettings.shaderIgnoreMaterial.TryGetValue(material.shader.name, out string[] ignoreMaterial) && ignoreMaterial.Any(ignore => material.name.IndexOf(ignore, StringComparison.OrdinalIgnoreCase) >= 0)
                       || materialSettings.shaderOnlyMaterial.TryGetValue(material.shader.name, out string[] onlyMaterial) && !onlyMaterial.Any(onlymat => material.name.IndexOf(onlymat, StringComparison.OrdinalIgnoreCase) >= 0))
                    continue;

                bool isNew = !SeasonalTextureVariants.controllers.TryGetValue(prefabName, out PrefabController controller);

                if (isNew)
                    controller = new PrefabController();

                if (!controller.renderersInHierarchy.TryGetValue(transformPath, out CachedRenderer cachedRenderer))
                    cachedRenderer = new CachedRenderer(rendererName, rendererType);

                if (materialSettings.materialColors.TryGetValue(material.name, out string[] materialColorNames))
                {
                    foreach (string propertyName in materialColorNames)
                    {
                        Color color = material.GetColor(propertyName);
                        if (color == null || color == Color.clear || color == Color.white || color == Color.black)
                            continue;

                        if (SeasonalTextureVariantsGenerator.GetColorVariants(prefabName, rendererName, material, propertyName, color, out Color[] colors, isPlant: isPlant))
                            cachedRenderer.AddMaterialColors(material, propertyName, colors);
                    }
                }
                else if (materialSettings.shaderColors.TryGetValue(material.shader.name, out string[] colorNames))
                    foreach (string propertyName in colorNames)
                    {
                        Color color = material.GetColor(propertyName);
                        if (color == null || color == Color.clear || color == Color.white || color == Color.black)
                            continue;

                        if (SeasonalTextureVariantsGenerator.GetColorVariants(prefabName, rendererName, material, propertyName, color, out Color[] colors, isPlant: isPlant))
                            cachedRenderer.AddMaterialColors(material, propertyName, colors);
                    }

                if (materialSettings.materialTextures.TryGetValue(material.name, out string[] materialTextureNames))
                {
                    foreach (string propertyName in material.GetTexturePropertyNames().Where(mat => materialTextureNames.Any(text => mat.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)))
                    {
                        Texture texture = material.GetTexture(propertyName);
                        if (texture == null)
                            continue;

                        int textureID = texture.GetInstanceID();
                        if (SeasonalTextureVariants.textures.ContainsKey(textureID))
                        {
                            cachedRenderer.AddMaterialTexture(material, propertyName, textureID);
                        }
                        else if (SeasonalTextureVariantsGenerator.GetTextureVariants(prefabName, rendererName, material, propertyName, texture, out TextureVariants textureVariants, isPlant: isPlant))
                        {
                            SeasonalTextureVariants.textures.Add(textureID, textureVariants);
                            cachedRenderer.AddMaterialTexture(material, propertyName, textureID);
                        }
                    }
                }
                else if (materialSettings.shaderTextures.TryGetValue(material.shader.name, out string[] textureNames))
                {
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
                        else if (SeasonalTextureVariantsGenerator.GetTextureVariants(prefabName, rendererName, material, propertyName, texture, out TextureVariants textureVariants, isPlant: isPlant))
                        {
                            SeasonalTextureVariants.textures.Add(textureID, textureVariants);
                            cachedRenderer.AddMaterialTexture(material, propertyName, textureID);
                        }
                    }
                }

                if (!cachedRenderer.Initialized())
                    continue;

                if (lodLevel >= 0)
                {
                    if (isLodInHierarchy)
                    {
                        if (!controller.lodsInHierarchy.TryGetValue(transformPath, out Dictionary<int, List<CachedRenderer>> lodInHierarchy))
                        {
                            controller.lodsInHierarchy.Add(transformPath, new Dictionary<int, List<CachedRenderer>>());
                            lodInHierarchy = controller.lodsInHierarchy[transformPath];
                        }

                        if (!lodInHierarchy.TryGetValue(lodLevel, out List<CachedRenderer> lodRenderers))
                            lodInHierarchy.Add(lodLevel, new List<CachedRenderer>() { cachedRenderer });
                        else
                            lodRenderers.Add(cachedRenderer);
                    }
                    else
                    {
                        if (!controller.lodLevelMaterials.TryGetValue(lodLevel, out List<CachedRenderer> lodRenderers))
                            controller.lodLevelMaterials.Add(lodLevel, new List<CachedRenderer>() { cachedRenderer });
                        else
                            lodRenderers.Add(cachedRenderer);
                    }
                }
                else if (isSingleRenderer)
                {
                    controller.cachedRenderer = cachedRenderer;
                }
                else
                {
                    if (!controller.renderersInHierarchy.ContainsKey(transformPath))
                        controller.renderersInHierarchy.Add(transformPath, cachedRenderer);
                }

                if (controller.Initialized())
                {
                    if (isNew)
                        SeasonalTextureVariants.controllers.Add(prefabName, controller);

                    LogInfo($"Caching {prefabName}{controller}");

                    if (isSingleRenderer)
                        return;
                }
            }
        }

        private static void AddZNetScenePrefabs()
        {
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (materialSettings.ignorePrefab.Contains(prefab.name) || prefab.layer == 12)
                    continue;

                if (materialSettings.ignorePrefabPartialName.Any(namepart => prefab.name.Contains(namepart)))
                    continue;

                if (prefab.layer == 8 && !materialSettings.effectPrefab.Contains(prefab.name))
                    continue;

                if (prefab.layer == 0 && prefab.TryGetComponent<Ship>(out _))
                    continue;

                if (prefab.layer == 16 && !prefab.TryGetComponent<Pickable>(out _) && !prefab.TryGetComponent<Plant>(out _))
                    continue;

                if (prefab.layer == 10 
                   && !(materialSettings.piecePrefab.Contains(prefab.name) || materialSettings.piecePrefabPartialName.Any(namepart => prefab.name.IndexOf(namepart, StringComparison.OrdinalIgnoreCase) >= 0))
                   && !prefab.TryGetComponent<Pickable>(out _) && !prefab.TryGetComponent<Plant>(out _))
                    continue;
                
                if (prefab.layer == 15 && (prefab.TryGetComponent<MineRock5>(out _) || prefab.TryGetComponent<MineRock>(out _)))
                {
                    MeshRenderer renderer = prefab.GetComponentInChildren<MeshRenderer>();

                    if (renderer == null)
                        return;

                    if (renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null)
                        return;

                    CacheMaterials(renderer.sharedMaterials, prefab.name, renderer.name, renderer.GetType().Name, renderer.transform.GetPath(), isSingleRenderer: true);
                    continue;
                }

                if (prefab.TryGetComponent<TimedDestruction>(out _))
                {
                    foreach (ParticleSystemRenderer renderer in prefab.GetComponentsInChildren<ParticleSystemRenderer>())
                    {
                        if (renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null)
                            continue;

                        CacheMaterials(renderer.sharedMaterials, prefab.name, renderer.name, renderer.GetType().Name, renderer.transform.GetPath());
                    }

                    foreach (ParticleSystem ps in prefab.GetComponentsInChildren<ParticleSystem>())
                    {
                        CacheParticleSystemStartColor(ps, prefab.name);
                    }
                }

                if (prefab.layer == 8)
                {
                    LODGroup lodGroup = prefab.GetComponentInChildren<LODGroup>();
                    if (lodGroup != null)
                    {
                        CachePrefabLODGroup(lodGroup, prefab.name, isLodInHierarchy: true);
                    }
                    else
                    {
                        SkinnedMeshRenderer[] renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>();
                        foreach (SkinnedMeshRenderer renderer in renderers)
                        {
                            if (renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null)
                                continue;

                            CacheMaterials(renderer.sharedMaterials, prefab.name, renderer.name, renderer.GetType().Name, renderer.transform.GetPath());
                        }
                    }
                }
                else if (prefab.layer != 9)
                {
                    bool isPlant = prefab.TryGetComponent<Pickable>(out _) || prefab.TryGetComponent<Plant>(out _);

                    if (prefab.TryGetComponent(out WearNTear wnt))
                    {
                        if (wnt.m_new != null && wnt.m_new.TryGetComponent(out LODGroup wntLodGroupNew))
                            CachePrefabLODGroup(wntLodGroupNew, prefab.name, isLodInHierarchy: true);
                        if (wnt.m_worn != null && wnt.m_worn.TryGetComponent(out LODGroup wntLodGroupWorn))
                            CachePrefabLODGroup(wntLodGroupWorn, prefab.name, isLodInHierarchy: true);
                        if (wnt.m_broken != null && wnt.m_broken.TryGetComponent(out LODGroup wntLodGroupBroken))
                            CachePrefabLODGroup(wntLodGroupBroken, prefab.name, isLodInHierarchy: true);
                        if (wnt.m_wet != null && wnt.m_wet.TryGetComponent(out LODGroup wntLodGroupWet))
                            CachePrefabLODGroup(wntLodGroupWet, prefab.name, isLodInHierarchy: true);
                    }
                    
                    if (prefab.TryGetComponent(out LODGroup lodGroup) && lodGroup.lodCount > 1)
                    {
                        CachePrefabLODGroup(lodGroup, prefab.name, isLodInHierarchy: false, isPlant:isPlant);
                    }
                    else
                    {
                        foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>())
                        {
                            if (renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null)
                                continue;

                            CacheMaterials(renderer.sharedMaterials, prefab.name, renderer.name, renderer.GetType().Name, renderer.transform.GetPath(), isPlant: isPlant);
                        }
                    }
                }
                else
                {
                    LODGroup lodGroup = prefab.GetComponentInChildren<LODGroup>();
                    if (lodGroup != null)
                    {
                        CachePrefabLODGroup(lodGroup, prefab.name, isLodInHierarchy: true);
                    }
                    else
                    {
                        SkinnedMeshRenderer[] renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>();
                        foreach (SkinnedMeshRenderer renderer in renderers)
                        {
                            if (renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null)
                                continue;

                            CacheMaterials(renderer.sharedMaterials, prefab.name, renderer.name, renderer.GetType().Name, renderer.transform.GetPath());
                        }
                    }
                }

                if (prefab.TryGetComponent<TreeBase>(out _) || prefab.TryGetComponent<Destructible>(out _))
                {
                    foreach (ParticleSystemRenderer renderer in prefab.GetComponentsInChildren<ParticleSystemRenderer>())
                    {
                        if (renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null)
                            continue;

                        CacheMaterials(renderer.sharedMaterials, prefab.name, renderer.name, renderer.GetType().Name, renderer.transform.GetPath());
                    }

                    foreach (ParticleSystem ps in prefab.GetComponentsInChildren<ParticleSystem>())
                    {
                        CacheParticleSystemStartColor(ps, prefab.name);
                    }
                }
            }
        }

        private static void CachePrefabLODGroup(LODGroup lodGroup, string prefabName, bool isLodInHierarchy, bool isPlant = false)
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

                    CacheMaterials(renderer.sharedMaterials, prefabName, renderer.name, renderer.GetType().Name, lodGroup.transform.GetPath(), lodLevel, isLodInHierarchy: isLodInHierarchy, isPlant: isPlant);
                }
            }
        }

        private static void CacheParticleSystemStartColor(ParticleSystem ps, string prefabName)
        {
            if (ps.main.startColor.color == Color.white)
                return;

            if (!materialSettings.particleSystemStartColors.Contains(ps.name) && !materialSettings.particleSystemStartColors.Contains(prefabName))
                return;

            string transformPath = ps.transform.GetPath();

            bool isNew = !SeasonalTextureVariants.controllers.TryGetValue(prefabName, out PrefabController controller);

            if (isNew)
                controller = new PrefabController();
            else if (controller.particleSystemStartColors != null && controller.particleSystemStartColors.ContainsKey(transformPath))
                return;

            List<string> colors = new List<string>();
            foreach (Season season in Enum.GetValues(typeof(Season)))
                for (int i = 1; i <= seasonColorVariants; i++)
                {
                    Color colorVariant = GetSeasonConfigColor(season, i);
                    Color color = SeasonalTextureVariantsGenerator.MergeColors(ps.main.startColor.color, colorVariant, colorVariant.a, season == Season.Winter);
                    colors.Add($"#{ColorUtility.ToHtmlStringRGBA(color)}");
                }

            controller.particleSystemStartColors ??= new Dictionary<string, string[]>();

            controller.particleSystemStartColors.Add(transformPath, colors.ToArray());

            if (controller.Initialized())
            {
                if (isNew)
                    SeasonalTextureVariants.controllers.Add(prefabName, controller);

                LogInfo($"Caching {prefabName}{controller}");
            }
        }
    }

}
