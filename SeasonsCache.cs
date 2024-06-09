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
using static Seasons.SeasonalTexturePrefabCache.ColorsCacheSettings;
using static Seasons.SeasonalTexturePrefabCache.ColorReplacementSpecifications;
using static Seasons.SeasonalTexturePrefabCache.ColorPositionsSettings;
using System.Diagnostics;
using BepInEx;
using System.Text;
using System.Collections;
using System.Threading;
using ServerSync;

namespace Seasons
{
    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
    public static class ZoneSystem_Start_SeasonsCache
    {
        private static void Postfix(ZoneSystem __instance)
        {
            SeasonalTexturePrefabCache.SetupConfigWatcher();
            SeasonalTexturePrefabCache.SaveDefaults();

            if (!UseTextureControllers())
                return;

            ClutterVariantController.AddSeasonalClutter();

            if (texturesVariants.Initialize())
            {
                __instance.gameObject.AddComponent<PrefabVariantController>();
                PrefabVariantController.AddControllerToPrefabs();
                ClutterVariantController.Initialize();
                __instance.gameObject.AddComponent<ZoneSystemVariantController>().Initialize(__instance);
                FillListsToControl();
                InvalidatePositionsCache();
            }
            else
                LogInfo("Missing textures variants");
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.OnDestroy))]
    public static class ZoneSystem_OnDestroy_SeasonsCache
    {
        private static void Postfix()
        {
            if (!UseTextureControllers())
                return;

            texturesVariants = new SeasonalTextureVariants();
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

            public CachedMaterial()
            {

            }

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

            public CachedRenderer() 
            {

            }

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
                name = textureVariants.originalName;
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
                    properties = JsonUtility.FromJson<TextureProperties>(File.ReadAllText(propertiesFile[0].FullName));
                
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
        public uint revision = 0;

        public CachedData(uint revision)
        {
            this.revision = revision;
        }

        public bool Initialized()
        {
            return controllers.Count > 0 && textures.Count > 0;
        }

        public void SaveOnDisk()
        {
            if (Initialized())
                if (cacheStorageFormat.Value == CacheFormat.Binary)
                    SaveToBinary();
                else if (cacheStorageFormat.Value == CacheFormat.Json)
                    SaveToJSON();
                else
                {
                    SaveToJSON();
                    SaveToBinary();
                }
        }

        public void LoadFromDisk()
        {
            if (cacheStorageFormat.Value == CacheFormat.Json)
                LoadFromJSON();
            else
                LoadFromBinary();
        }

        private void SaveToJSON()
        {
            string folder = CacheDirectory();

            Directory.CreateDirectory(folder);

            string filename = Path.Combine(folder, prefabCacheFileName);

            File.WriteAllText(filename, JsonConvert.SerializeObject(controllers, Formatting.Indented, new JsonSerializerSettings()
            {
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
            }));

            string directory = Path.Combine(folder, texturesDirectory);

            LogInfo($"Saved cache file {filename}");

            foreach (KeyValuePair<int, TextureData> tex in textures)
            {
                string texturePath = Path.Combine(directory, tex.Key.ToString());

                Directory.CreateDirectory(texturePath);

                File.WriteAllBytes(Path.Combine(texturePath, $"{tex.Value.name}{originalPostfix}"), tex.Value.originalPNG);

                File.WriteAllText(Path.Combine(texturePath, texturePropertiesFileName), JsonUtility.ToJson(tex.Value.properties, true));

                foreach (KeyValuePair<Season, Dictionary<int, byte[]>> season in tex.Value.variants)
                    foreach (KeyValuePair<int, byte[]> texData in season.Value)
                        File.WriteAllBytes(Path.Combine(texturePath, SeasonFileName(season.Key, texData.Key)), texData.Value);
            }

            LogInfo($"Saved {textures.Count} textures at {directory}");
        }

        private void LoadFromJSON()
        {
            string folder = CacheDirectory();

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

        private void SaveToBinary()
        {
            string folder = CacheDirectory();

            Directory.CreateDirectory(folder);

            using (FileStream fs = new FileStream(Path.Combine(folder, prefabCacheCommonFile), FileMode.Create))
            {
                BinaryFormatter bf = new BinaryFormatter();
                bf.Serialize(fs, this);
                fs.Dispose();
            }

            LogInfo($"Saved cache file {Path.Combine(folder, prefabCacheCommonFile)}");
        }

        private void LoadFromBinary()
        {
            string folder = CacheDirectory();

            string filename = Path.Combine(folder, prefabCacheCommonFile);
            if (!File.Exists(filename))
            {
                LogInfo($"File not found: {filename}");
                return;
            }

            try
            {
                using (FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    BinaryFormatter bf = new BinaryFormatter();
                    CachedData cd = (CachedData)bf.Deserialize(fs);
                    fs.Dispose();

                    controllers.Copy(cd.controllers);
                    textures.Copy(cd.textures);

                    cd = null;
                }
            }
            catch (Exception ex)
            {
                LogWarning($"Error loading binary cache data from {filename}:\n {ex}");
            }
        }

        public string CacheDirectory()
        {
            return Path.Combine(cacheDirectory, revision.ToString());
        }

        public static string SeasonFileName(Season season, int variant)
        {
            return $"{season}_{variant + 1}.png";
        }

    }

    public class TextureVariants
    {
        public Texture2D original;
        public string originalName;
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
            originalName = original.name;
        }

        public bool Initialized()
        {
            return seasons.Any(season => season.Value.Count > 0);
        }

        public bool HaveOriginalTexture()
        {
            return (bool)original;
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

    public class SeasonalTextureVariants
    {
        public Dictionary<string, PrefabController> controllers = new Dictionary<string, PrefabController>();
        public Dictionary<int, TextureVariants> textures = new Dictionary<int, TextureVariants>();
        public uint revision = 0;

        public bool Initialize(bool force = false)
        {
            if (!force && Initialized())
                return true;

            controllers.Clear();
            textures.Clear();

            revision = SeasonalTexturePrefabCache.GetRevision();
            CachedData cachedData = new CachedData(revision);

            if (force && Directory.Exists(cachedData.CacheDirectory()))
                Directory.Delete(cachedData.CacheDirectory(), recursive: true);

            cachedData.LoadFromDisk();

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

                LogInfo($"Loaded from cache revision:{revision} controllers:{controllers.Count} textures:{textures.Count}");
            }
            else
            {
                SeasonalTexturePrefabCache.SetCurrentTextureVariants(this);

                StartCoroutineSync(SeasonalTexturePrefabCache.FillWithGameData());

                StartCoroutineSync(SaveCacheOnDisk());
            }

            return Initialized();
        }

        public IEnumerator SaveCacheOnDisk()
        {
            if (Initialized())
            {
                CachedData cachedData = new CachedData(revision);

                cachedData.textures.Clear();
                foreach (KeyValuePair<int, TextureVariants> texVariants in textures)
                {
                    CachedData.TextureData texData = new CachedData.TextureData(texVariants.Value);
                    if (texData.Initialized())
                        cachedData.textures.Add(texVariants.Key, texData);
                }

                var internalThread = new Thread(() =>
                {
                    cachedData.controllers.Copy(controllers);

                    if (Directory.Exists(cachedData.CacheDirectory()))
                        Directory.Delete(cachedData.CacheDirectory(), recursive: true);

                    cachedData.SaveOnDisk();
                });

                internalThread.Start();
                while (internalThread.IsAlive == true)
                {
                    yield return waitForFixedUpdate;
                }

                ApplyTexturesToGPU();
            }
        }

        public bool Initialized()
        {
            return controllers.Count > 0 && textures.Count > 0;
        }

        public void ApplyTexturesToGPU()
        {
            foreach (KeyValuePair<int, TextureVariants> texture in textures)
                texture.Value.ApplyTextures();
        }

        public IEnumerator ReloadCache()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            CachedData cachedData = new CachedData(SeasonalTexturePrefabCache.GetRevision());

            var internalThread = new Thread(() =>
            {
                cachedData.LoadFromDisk();
            });

            internalThread.Start();
            while (internalThread.IsAlive == true)
            {
                yield return waitForFixedUpdate;
            }

            if (cachedData.Initialized())
            {
                revision = cachedData.revision;

                foreach (KeyValuePair<int, CachedData.TextureData> texData in cachedData.textures)
                {
                    if (textures.ContainsKey(texData.Key))
                        continue;

                    TextureVariants texVariants = new TextureVariants(texData.Value);

                    if (!texVariants.Initialized())
                        continue;

                    textures.Add(texData.Key, texVariants);
                }

                internalThread = new Thread(() =>
                {
                    controllers.Copy(cachedData.controllers);

                });

                internalThread.Start();
                while (internalThread.IsAlive == true)
                {
                    yield return waitForFixedUpdate;
                }

                LogInfo($"Loaded from cache revision:{revision} controllers:{controllers.Count} textures:{textures.Count} in {stopwatch.Elapsed.TotalSeconds,-4:F2} seconds");

                stopwatch.Restart();

                ClutterVariantController.Reinitialize();
                PrefabVariantController.ReinitializePrefabVariants();

                yield return waitForFixedUpdate;

                PrefabVariantController.UpdatePrefabColors();
                ClutterVariantController.instance.UpdateColors();

                LogInfo($"Colors reinitialized in {stopwatch.Elapsed.TotalSeconds,-4:F2} seconds");
            }
            else
            {
                yield return RebuildCache();
            }
        }

        public IEnumerator RebuildCache()
        {
            SeasonalTextureVariants newTexturesVariants = new SeasonalTextureVariants();

            SeasonalTexturePrefabCache.SetCurrentTextureVariants(newTexturesVariants);

            PrefabVariantController.instance.RevertPrefabsState();
            ClutterVariantController.instance.RevertColors();

            yield return waitForFixedUpdate;

            yield return SeasonalTexturePrefabCache.FillWithGameData();

            if (newTexturesVariants.Initialized())
            {
                Stopwatch stopwatch = Stopwatch.StartNew();

                controllers.Clear();
                textures.Clear();
                revision = newTexturesVariants.revision;

                var internalThread = new Thread(() =>
                {
                    controllers.Copy(newTexturesVariants.controllers);
                    textures.Copy(newTexturesVariants.textures);
                });

                internalThread.Start();
                while (internalThread.IsAlive == true)
                {
                    yield return waitForFixedUpdate;
                }

                yield return SaveCacheOnDisk();

                SeasonalTexturePrefabCache.SetCurrentTextureVariants(this);

                ClutterVariantController.Reinitialize();
                PrefabVariantController.ReinitializePrefabVariants();

                LogInfo($"Colors reinitialized in {stopwatch.Elapsed.TotalSeconds,-4:F2} seconds");
            }

            yield return waitForFixedUpdate;

            SeasonalTexturePrefabCache.SetCurrentTextureVariants(this);

            PrefabVariantController.UpdatePrefabColors();
            ClutterVariantController.instance.UpdateColors();
        }
    }

    public static class SeasonalTexturePrefabCache
    {
        [Serializable]
        public struct FloatRange
        {
            public float start;
            public float end;

            public FloatRange(float start, float end)
            {
                this.start = start;
                this.end = end;
            }

            public bool Fits(float value)
            {
                return (start == 0f || start <= value) && (end == 0f || value <= end);
            }
            public override string ToString()
            {
                return $"{start}-{end}";
            }

        }

        [Serializable]
        public struct IntRange
        {
            public int start;
            public int end;

            public IntRange(int start, int end)
            {
                this.start = start;
                this.end = end;
            }

            public bool Fits(int value)
            {
                return (start == 0 || start <= value) && (end == 0 || value <= end);
            }

            public override string ToString()
            {
                return $"{start}-{end}";
            }

        }

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
                    { "grasscross_mistlands_short", new string[] { "_Color" }},
                    { "Bush02_en", new string[] { "_Color" }},
                    { "shrub_heath", new string[] { "_Color" }},
                    { "bfp_straw_roof", new string[] { "_Color" }},
                    { "bfp_straw_roof_alpha", new string[] { "_Color" }},
                    { "bfp_straw_roof_corner_alpha", new string[] { "_Color" }},
                    { "bcp_clay", new string[] { "_Color" }},
                    { "Grausten_RoofSlab_mat", new string[] { "_Color" }},
                    { "Vine_Sapling_ashlands_mat", new string[] { "_Color" }},
                    { "Vines_ashlands_mat", new string[] { "_Color" }},
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
                    { "Vines_Mat", new string[] { } },
                    { "Grausten_RoofSlab_mat", new string[] { } },
                    { "Vine_Sapling_ashlands_mat", new string[] { }},
                    { "Vines_ashlands_mat", new string[] { }},
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
                    { "Custom/Vegetation", new string[] { "bark", "trunk", "_wood", "HildirFlowerGirland_", "HildirTentCloth_", "TraderTent_", "VinesBranch_mat", "VinesBranch_Ashlands_mat" } },
                };

                shaderOnlyMaterial = new Dictionary<string, string[]>
                {
                    { "Custom/Piece", new string[] { "straw", "RoofShingles", "beehive", "Midsummerpole_mat", "Pine_tree_xmas", "ReworkedValheim", "shipyardNewCloth", "M_Cloth_01", "bcp_clay", "Grausten_RoofSlab_mat" } },
                    { "Custom/Creature", new string[] { "HildirsLox", "lox", "lox_calf",
                                                        "Draugr_Archer_mat", "Draugr_mat", "Draugr_elite_mat", "Abomination_mat",
                                                        "greyling", "greydwarf", "greydwarf_elite", "greydwarf_shaman", "neck" } },
                    { "Standard", new string[] { "beech_particle", "birch_particle", "branch_particle", "branch_dead_particle", "oak_particle", "shoot_leaf_particle" }},
                    { "Particles/Standard Surface2", new string[] { "shrub2_leafparticle", "shrub2_leafparticle_heath", "leaf_en" }},
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
                    "piece_xmastree",
                    "VineAsh",
                    "VineAsh_sapling"
                };

                piecePrefabPartialName = new List<string>()
                {
                    "wood_roof",
                    "copper_roof",
                    "goblin_roof",
                    "roof_wood_",
                    "roof_darkwood_",
                    "elvenwood_roof",
                    "BFP_FineWoodRoof",
                    "cloth_roof",
                    "BFP_ClayRoof",
                    "piece_grausten_roof",
                    "VineAsh"
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
                    "TheHive",
                    "StoneVillage2",
                    "VoltureNest",
                    "CharredStone_Spawner",
                    "SulfurArch",
                    "FaderLocation",
                    "LeviathanLava",
                    "FernFiddleHeadAshlands",
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
                    "Mistlands_Mine",
                    "dvergrtown_",
                    "OLD_wood_roof",
                    "AbandonedLogCabin",
                    "DrakeNest",
                    "CharredRuins",
                    "MorgenHole",
                    "CharredTowerRuins",
                    "CharredRuins",
                    "CharredFortress",
                    "PlaceofMystery",
                    "DevWall",
                    "blackmarble_creep_slope",
                    "lavarock_",
                    "lavabomb_",
                    "FlametalRockstand",
                    "cliff_ashlands",
                    "AshlandsTree",
                    "AshlandsBush",
                    "AshlandsBranch",
                    "Ashlands_rock",
                    "instanced_ashlands_grass",
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
                public float targetProportion = 0f;
                public bool preserveAlphaChannel = true;
                public bool reduceOriginalColorToGrayscale = false;
                public bool restoreLuminance = true;

                [NonSerialized]
                public Color colorValue = Color.black;

                public Color MergeColors(Color colorToMerge)
                {
                    if (!useColor)
                        return colorToMerge;

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

                public ColorVariant GetColorVariant(Season season, int pos)
                {
                    return season switch
                    {
                        Season.Spring => pos > Spring.Count - 1 ? new ColorVariant() : Spring[Mathf.Clamp(pos, 0, 3)],
                        Season.Summer => pos > Summer.Count - 1 ? new ColorVariant() : Summer[Mathf.Clamp(pos, 0, 3)],
                        Season.Fall => pos > Fall.Count - 1 ? new ColorVariant() : Fall[Mathf.Clamp(pos, 0, 3)],
                        Season.Winter => pos > Winter.Count - 1 ? new ColorVariant() : Winter[Mathf.Clamp(pos, 0, 3)],
                        _ => pos > Spring.Count - 1 ? new ColorVariant() : Spring[Mathf.Clamp(pos, 0, 3)],
                    };
                }
            }

            [Serializable]
            public class SeasonalColorOverride
            {
                public SeasonalColorVariants colors = new SeasonalColorVariants();
            }

            [Serializable]
            public class PrefabOverrides : SeasonalColorOverride
            {
                public List<string> prefab = new List<string>();

                public PrefabOverrides(List<string> prefab, SeasonalColorVariants colors)
                {
                    this.prefab = prefab;
                    this.colors = colors;
                }
            }

            [Serializable]
            public class MaterialOverrides : SeasonalColorOverride
            {
                public List<string> material = new List<string>();
                
                public MaterialOverrides(List<string> material, SeasonalColorVariants colors)
                {
                    this.material = material;
                    this.colors = colors;
                }
            }

            public SeasonalColorVariants seasonal = new SeasonalColorVariants();
            public SeasonalColorVariants grass = new SeasonalColorVariants();
            public SeasonalColorVariants moss = new SeasonalColorVariants();
            public SeasonalColorVariants creature = new SeasonalColorVariants();
            public SeasonalColorVariants piece = new SeasonalColorVariants();
            public SeasonalColorVariants conifer = new SeasonalColorVariants();
            public SeasonalColorVariants bush = new SeasonalColorVariants();

            public List<PrefabOverrides> prefabOverrides = new List<PrefabOverrides>();
            public List<MaterialOverrides> materialOverrides = new List<MaterialOverrides>();

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
                seasonal.Fall.Add(new ColorVariant(new Color(0.8f, 0.2f, 0f), 0.70f));
                seasonal.Fall.Add(new ColorVariant(new Color(0.7f, 0.7f, 0f), 0.2f));

                seasonal.Winter.Add(new ColorVariant(new Color(1f, 0.98f, 0.98f), 0.65f, grayscale: true, restoreLuminance: false));
                seasonal.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.6f, grayscale: true, restoreLuminance: false));
                seasonal.Winter.Add(new ColorVariant(new Color(0.98f, 0.98f, 1f), 0.65f, grayscale: true, restoreLuminance: false));
                seasonal.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.65f, grayscale: true, restoreLuminance: false));

                grass.Spring.Add(new ColorVariant(new Color(0.45f, 0.80f, 0.27f), 0.5f));
                grass.Spring.Add(new ColorVariant(new Color(0.69f, 0.84f, 0.15f), 0.45f));
                grass.Spring.Add(new ColorVariant(new Color(0.51f, 0.65f, 0.13f), 0.5f));
                grass.Spring.Add(new ColorVariant());

                grass.Summer.Add(new ColorVariant(new Color(0.5f, 0.7f, 0.2f), 0.5f));
                grass.Summer.Add(new ColorVariant(new Color(0.7f, 0.75f, 0.2f), 0.5f));
                grass.Summer.Add(new ColorVariant(new Color(0.5f, 0.5f, 0f), 0.5f));
                grass.Summer.Add(new ColorVariant(new Color(0.7f, 0.7f, 0f), 0.2f));

                grass.Fall.Add(new ColorVariant(new Color(0.8f, 0.6f, 0.2f), 0.5f));
                grass.Fall.Add(new ColorVariant(new Color(0.8f, 0.5f, 0f), 0.5f));
                grass.Fall.Add(new ColorVariant(new Color(0.8f, 0.3f, 0f), 0.4f));
                grass.Fall.Add(new ColorVariant(new Color(0.7f, 0.7f, 0f), 0.4f));

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
                moss.Fall.Add(new ColorVariant(new Color(0.7f, 0.7f, 0f), 0.1f));
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

                conifer.Fall.Add(new ColorVariant(new Color(0.8f, 0.5f, 0f), 0.25f));
                conifer.Fall.Add(new ColorVariant(new Color(0.8f, 0.3f, 0f), 0.20f));
                conifer.Fall.Add(new ColorVariant(new Color(0.8f, 0.2f, 0f), 0.15f));
                conifer.Fall.Add(new ColorVariant(new Color(0.7f, 0.7f, 0f), 0.1f));

                conifer.Winter.Add(new ColorVariant(new Color(1f, 0.98f, 0.98f), 0.45f, grayscale: true, restoreLuminance: false));
                conifer.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.4f, grayscale: true, restoreLuminance: false));
                conifer.Winter.Add(new ColorVariant(new Color(0.98f, 0.98f, 1f), 0.45f, grayscale: true, restoreLuminance: false));
                conifer.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.45f, grayscale: true, restoreLuminance: false));

                bush.Spring.Add(new ColorVariant(new Color(0.27f, 0.80f, 0.27f), 0.6f));
                bush.Spring.Add(new ColorVariant(new Color(0.69f, 0.84f, 0.15f), 0.6f));
                bush.Spring.Add(new ColorVariant(new Color(0.43f, 0.56f, 0.11f), 0.6f));
                bush.Spring.Add(new ColorVariant());

                bush.Summer.Add(new ColorVariant(new Color(0.5f, 0.7f, 0.2f), 0.5f));
                bush.Summer.Add(new ColorVariant(new Color(0.7f, 0.7f, 0.2f), 0.5f));
                bush.Summer.Add(new ColorVariant(new Color(0.5f, 0.5f, 0f), 0.5f));
                bush.Summer.Add(new ColorVariant(new Color(0.7f, 0.7f, 0f), 0.2f));

                bush.Fall.Add(new ColorVariant(new Color(0.8f, 0.5f, 0f), 0.5f));
                bush.Fall.Add(new ColorVariant(new Color(0.8f, 0.3f, 0f), 0.5f));
                bush.Fall.Add(new ColorVariant(new Color(0.8f, 0.2f, 0f), 0.4f));
                bush.Fall.Add(new ColorVariant(new Color(0.7f, 0.7f, 0f), 0.2f));

                bush.Winter.Add(new ColorVariant(new Color(1f, 0.98f, 0.98f), 0.65f, grayscale: true, restoreLuminance: false));
                bush.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.6f, grayscale: true, restoreLuminance: false));
                bush.Winter.Add(new ColorVariant(new Color(0.98f, 0.98f, 1f), 0.65f, grayscale: true, restoreLuminance: false));
                bush.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.65f, grayscale: true, restoreLuminance: false));

                creature.Winter.Add(new ColorVariant(new Color(1f, 0.98f, 0.98f), 0.25f, grayscale: true, restoreLuminance: false));
                creature.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.2f, grayscale: true, restoreLuminance: false));
                creature.Winter.Add(new ColorVariant(new Color(0.98f, 0.98f, 1f), 0.25f, grayscale: true, restoreLuminance: false));
                creature.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.25f, grayscale: true, restoreLuminance: false));

                piece.Winter.Add(new ColorVariant(new Color(1f, 0.98f, 0.98f), 0.4f, grayscale: true, restoreLuminance: false));
                piece.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.3f, grayscale: true, restoreLuminance: false));
                piece.Winter.Add(new ColorVariant(new Color(0.98f, 0.98f, 1f), 0.4f, grayscale: true, restoreLuminance: false));
                piece.Winter.Add(new ColorVariant(new Color(1f, 1f, 1f), 0.4f, grayscale: true, restoreLuminance: false));

                prefabOverrides.Add(new PrefabOverrides(new List<string>() { "lox" }, new SeasonalColorVariants() 
                { 
                    Winter = new List<ColorVariant>() 
                    {
                        new ColorVariant(new Color(1f, 0.98f, 0.98f), 0.5f, grayscale: true, restoreLuminance: false),
                        new ColorVariant(new Color(1f, 1f, 1f), 0.4f, grayscale: true, restoreLuminance: false),
                        new ColorVariant(new Color(0.98f, 0.98f, 1f), 0.5f, grayscale: true, restoreLuminance: false),
                        new ColorVariant(new Color(1f, 1f, 1f), 0.5f, grayscale: true, restoreLuminance: false)
                    } 
                }));

                prefabOverrides.Add(new PrefabOverrides(new List<string>() { "goblin" }, new SeasonalColorVariants()
                {
                    Winter = new List<ColorVariant>()
                    {
                        new ColorVariant(new Color(1f, 0.98f, 0.98f), 0.35f, grayscale: true, restoreLuminance: false),
                        new ColorVariant(new Color(1f, 1f, 1f), 0.3f, grayscale: true, restoreLuminance: false),
                        new ColorVariant(new Color(0.98f, 0.98f, 1f), 0.35f, grayscale: true, restoreLuminance: false),
                        new ColorVariant(new Color(1f, 1f, 1f), 0.35f, grayscale: true, restoreLuminance: false)
                    }
                }));

                prefabOverrides.Add(new PrefabOverrides(new List<string>() { "YggdrasilBranch" }, new SeasonalColorVariants()
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
                }));

                prefabOverrides.Add(new PrefabOverrides(new List<string>() { "grasscross_heath_green" }, new SeasonalColorVariants()
                {
                    Spring = grass.Summer.ToList(),
                    Summer = grass.Spring.ToList(),
                    Fall = grass.Summer.ToList(),
                    Winter = grass.Winter.ToList(),
                }));

                materialOverrides.Add(new MaterialOverrides(new List<string>() { "Pine_tree_small_dead", "swamptree1_branch", "swamptree2_branch" }, new SeasonalColorVariants()
                {
                    Winter = seasonal.Winter
                }));

                materialOverrides.Add(new MaterialOverrides(new List<string>() { "Midsummerpole_mat" }, new SeasonalColorVariants()
                {
                    Spring = seasonal.Spring,
                    Summer = seasonal.Summer,
                    Fall = seasonal.Fall,
                    Winter = seasonal.Winter
                }));

                materialOverrides.Add(new MaterialOverrides(new List<string>() { "yggdrasil_branch" }, new SeasonalColorVariants()
                {
                    Spring = grass.Spring,
                    Summer = grass.Summer,
                    Fall = grass.Fall,
                    Winter = grass.Winter
                }));
                
            }

            private Dictionary<string, SeasonalColorVariants> _prefabs = new Dictionary<string, SeasonalColorVariants>();
            private Dictionary<string, SeasonalColorVariants> _materials = new Dictionary<string, SeasonalColorVariants>();

            public SeasonalColorVariants GetPrefabOverride(string name)
            {
                if (_prefabs.Count == 0 && prefabOverrides.Count != 0)
                    foreach (var prefabOverride in prefabOverrides)
                        foreach (var prefab in prefabOverride.prefab)
                            _prefabs.Add(prefab, prefabOverride.colors);

                return _prefabs.GetValueSafe(name);
            }

            public SeasonalColorVariants GetMaterialOverride(string name)
            {
                if (_materials.Count == 0 && materialOverrides.Count != 0)
                    foreach (var materialOverride in materialOverrides)
                        foreach (var mat in materialOverride.material)
                            _materials.Add(mat, materialOverride.colors);

                return _materials.GetValueSafe(name);
            }

            public static bool IsGrass(string shaderName)
            {
                return shaderName == "Custom/Grass";
            }

            public static bool IsMoss(string textureName)
            {
                return textureName.IndexOf("moss", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            public static bool IsPiece(Material material)
            {
                return material.shader.name == "Custom/Piece" || material.name.StartsWith("GoblinVillage");
            }

            public static bool IsCreature(string shaderName)
            {
                return shaderName == "Custom/Creature";
            }

            public static bool IsPine(string materialName, string prefab)
            {
                return materialName.IndexOf("pine", StringComparison.OrdinalIgnoreCase) >= 0 || prefab.IndexOf("pine", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            public static bool IsBush(string materialName, string prefab)
            {
                return materialName.IndexOf("bush", StringComparison.OrdinalIgnoreCase) >= 0 || prefab.IndexOf("bush", StringComparison.OrdinalIgnoreCase) >= 0
                    || materialName.IndexOf("shrub", StringComparison.OrdinalIgnoreCase) >= 0 || prefab.IndexOf("shrub", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            
        }

        [Serializable]
        public class ColorReplacementSpecifications
        {
            [Serializable]
            public class ColorFits
            {
                public FloatRange hue;
                public FloatRange saturation;
                public FloatRange luminance;

                public ColorFits(float hue1 = 0f, float hue2 = 360f, float s1 = 0f, float s2 = 1f, float l1 = 0f, float l2 = 1f)
                {
                    hue = new FloatRange(hue1, hue2);
                    saturation = new FloatRange(s1, s2);
                    luminance = new FloatRange(l1, l2);
                }

                public bool Fits(HSLColor hslcolor)
                {
                    return hue.Fits(hslcolor.h) && saturation.Fits(hslcolor.s) && luminance.Fits(hslcolor.l);
                }

                public override string ToString()
                {
                    return $"hue:{hue} sat:{saturation} lum:{luminance}";
                }
            }

            [Serializable]
            public class ColorSpecific
            {
                public List<MaterialFits> material = new List<MaterialFits>();
                public List<ColorFits> colors = new List<ColorFits>();

                public ColorSpecific(List<MaterialFits> material, List<ColorFits> colors)
                {
                    this.material = material;
                    this.colors = colors;
                }

                public bool FitsMaterial(string prefabName, string rendererName, string materialName)
                {
                    return MaterialFits.FitsMaterial(material, prefabName, rendererName, materialName);
                }

                public bool FitsColor(HSLColor color)
                {
                    return Fits(colors, color);
                }
            }

            public List<ColorFits> seasonal = new List<ColorFits>();
            public List<ColorFits> grass = new List<ColorFits>();
            public List<ColorFits> moss = new List<ColorFits>();

            public List<ColorSpecific> specific = new List<ColorSpecific>();

            public ColorReplacementSpecifications(bool loadDefaults = false)
            {
                if (!loadDefaults)
                    return;

                seasonal.Add(new ColorFits(80, 160, s1: 0.15f));
                seasonal.Add(new ColorFits(55, 91, s1: 0.20f, l1: 0.18f));
                seasonal.Add(new ColorFits(33, 57, s1: 0.28f, l1: 0.26f));

                moss.Add(new ColorFits());

                grass.Add(new ColorFits(65, 135, s1: 0.13f));
                grass.Add(new ColorFits(55, 65, s1: 0.55f, l1: 0.5f));
                grass.Add(new ColorFits(35, 65, s2: 0.35f, l1: 0.35f));
                grass.Add(new ColorFits(40, 60, s1: 0.4f));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>() 
                    { 
                        new MaterialFits(material: "HildirsLox"), 
                        new MaterialFits(prefab: "Lox"), 
                        new MaterialFits(prefab: "lox_ragdoll"), 
                        new MaterialFits(renderer: "Furr", only:true, partial:true) 
                    }, 
                    new List<ColorFits>()
                    {
                        new ColorFits(19, 51, s1: 0.40f, l2: 0.45f),
                        new ColorFits(43, 51, s2: 0.45f, l2: 0.45f),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "Lox_Calf", only:true),
                        new MaterialFits(renderer: "Furr", only:true, partial:true)
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(38, 62, s2: 0.55f, l1: 0.18f),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "draugr", only:true, partial:true)
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(80, 160, s1: 0.15f),
                        new ColorFits(55, 91, s1: 0.19f, l2: 0.40f),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "Abomination", only:true, partial:true)
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(80, 160, s1: 0.15f),
                        new ColorFits(55, 91, s1: 0.19f, l2: 0.50f),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "grey", only:true, partial:true)
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(80, 160, s1: 0.15f),
                        new ColorFits(51, 91, s1: 0.18f, l2: 0.50f),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(material: "Vines_Mat", only:true)
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(material: "Vine_Sapling_ashlands_mat", only:true)
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(material: "Vines_ashlands_mat", only:true)
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "goblin_roof", partial: true, only: true),
                        new MaterialFits(material: "GoblinVillage_Cloth", partial: true, only: true),
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "darkwood_roof", partial: true, only: true),
                        new MaterialFits(material: "RoofShingles", partial: true, only: true),
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "copper_roof", partial: true, only: true),
                        new MaterialFits(material: "RoofShingles", partial: true, only: true),
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "wood_roof", partial: true, only: true),
                        new MaterialFits(material: "straw", partial: true, only: true),
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(material: "Pine_tree_small_dead", only: true),
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "shrub_2_heath", only: true),
                        new MaterialFits(material: "shrub_heath", only: true),
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "instanced_meadows_grass", only: true),
                        new MaterialFits(material: "grasscross_meadows", only: true),
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(2),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "instanced_meadows_grass_short", only: true),
                        new MaterialFits(material: "grasscross_meadows_short", only: true),
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(2),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "instanced_mistlands_grass_short", only: true),
                        new MaterialFits(material: "grasscross_mistlands_short", only: true),
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(2),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(material: "neck", only:true),
                        new MaterialFits(prefab: "Neck", only:true),
                        new MaterialFits(renderer: "Lillies", only:true)
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(material: "leafparticle", partial: true, only:true),
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "roof_wood_", partial: true, only: true),
                        new MaterialFits(material: "straw", partial: true, only: true),
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "roof_darkwood_", partial: true, only: true),
                        new MaterialFits(material: "RoofShingles", partial: true, only: true),
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "elvenwood_roof", partial: true, only: true),
                        new MaterialFits(material: "straw", partial: true, only: false),
                        new MaterialFits(material: "ReworkedValheim", partial: true, only: false),
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "BFP_FineWoodRoof", partial: true, only: true),
                        new MaterialFits(material: "straw", partial: true, only: true),
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "cloth_roof", partial: true, only: true),
                        new MaterialFits(material: "shipyardNewCloth", partial: true, only: false),
                        new MaterialFits(material: "M_Cloth_01", partial: true, only: false),
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "BFP_ClayRoof", partial: true, only: true),
                        new MaterialFits(material: "bcp_clay", partial: true, only: true),
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(),
                    }
                ));

                specific.Add(new ColorSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "piece_grausten_roof", partial: true, only: true),
                        new MaterialFits(material: "Grausten_RoofSlab_mat", partial: true, only: true),
                    },
                    new List<ColorFits>()
                    {
                        new ColorFits(),
                    }
                ));

            }

            public bool ReplaceColor(Color color, bool isGrass, bool isMoss, string prefabName = null, string rendererName = null, string materialName = null)
            {
                if (color.a == 0f)
                    return false;

                HSLColor hslcolor = new HSLColor(color);
                if (prefabName != null || rendererName != null || materialName != null)
                    foreach (ColorSpecific spec in specific)
                        if (spec.FitsMaterial(prefabName, rendererName, materialName))
                            return spec.FitsColor(hslcolor);

                if (isGrass)
                    return Fits(grass, hslcolor);
                else if (isMoss)
                    return Fits(moss, hslcolor);

                return Fits(seasonal, hslcolor);
            }

            private static bool Fits(List<ColorFits> list, HSLColor color)
            {
                foreach (ColorFits colorFit in list)
                    if (colorFit.Fits(color))
                        return true;

                return false;
            }
        }

        [Serializable]
        public class MaterialFits
        {
            public string material;
            public string prefab;
            public string renderer;
            public bool only = false;
            public bool partial = false;
            public bool not = false;

            public MaterialFits(string material = null, string prefab = null, string renderer = null, bool only = false, bool partial = false, bool not = false)
            {
                this.material = material;
                this.prefab = prefab;
                this.renderer = renderer;
                this.only = only;
                this.partial = partial;
                this.not = not;
            }

            public bool Fits(string prefabName = null, string rendererName = null, string materialName = null)
            {
                return Compare(prefabName, prefab) || Compare(rendererName, renderer) || Compare(materialName, material);
            }

            private bool Compare(string name, string value)
            {
                if (String.IsNullOrEmpty(name) || String.IsNullOrEmpty(value))
                    return false;
                
                bool fits = partial ? name.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0 : name == value;
                return not ? !fits : fits;
            }

            public static bool FitsMaterial(List<MaterialFits> materials, string prefabName, string rendererName, string materialName)
            {
                bool fitsAnd = true; bool fitsOr = false; int or = 0;
                foreach (MaterialFits materialFits in materials)
                    if (materialFits.only)
                    {
                        fitsAnd = fitsAnd && materialFits.Fits(prefabName, rendererName, materialName);
                    }
                    else
                    {
                        fitsOr = fitsOr || materialFits.Fits(prefabName, rendererName, materialName);
                        or++;
                    }

                return fitsAnd && (or == 0 || fitsOr);
            }

        }

        [Serializable]
        public class ColorPositionsSettings
        {
            [Serializable]
            public class PositionFits
            {
                public IntRange height;
                public IntRange width;
                public bool not = false;

                public PositionFits(int heightStart = 0, int heightEnd = 0, int widthStart = 0, int widthEnd = 0, bool not = false)
                {
                    height = new IntRange(heightStart, heightEnd);
                    width = new IntRange(widthStart, widthEnd);
                    this.not = not;
                }

                public bool Fits(int textureWidth, int textureHeight, int pos)
                {
                    // From top left pixel
                    int widthPix = pos % textureWidth;
                    int heightPix = textureHeight - pos / textureWidth;
                    bool fit = height.Fits(heightPix) && width.Fits(widthPix);
                    return not ? !fit : fit;
                }

                public override string ToString()
                {
                    return $"height:{height} width:{width} not:{not}";
                }
            }

            [Serializable]
            public class PositionSpecific
            {
                public List<MaterialFits> material = new List<MaterialFits>();
                public List<PositionFits> bounds = new List<PositionFits>();
               
                public PositionSpecific(List<MaterialFits> material, List<PositionFits> bounds)
                {
                    this.material = material;
                    this.bounds = bounds;
                }

                public bool FitsMaterial(string prefabName, string rendererName, string materialName)
                {
                    return MaterialFits.FitsMaterial(material, prefabName, rendererName, materialName);
                }

                public bool FitsPosition(int textureWidth, int textureHeight, int pos)
                {
                    return bounds.Any(position => position.Fits(textureWidth, textureHeight, pos));
                }
            }

            public List<PositionSpecific> positions = new List<PositionSpecific>();

            public ColorPositionsSettings(bool loadDefaults = false)
            {
                if (!loadDefaults)
                    return;

                positions.Add(new PositionSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(material: "Pine_tree_", partial:true),
                    },
                    new List<PositionFits>()
                    {
                        new PositionFits(0, 44, 0, 93, not: true)
                    }
                ));

                positions.Add(new PositionSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(material: "Fir_tree_sapling", partial:true),
                    },
                    new List<PositionFits>()
                    {
                        new PositionFits(0, 11, 0, 20, not: true)
                    }
                ));

                positions.Add(new PositionSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "FirTree"),
                    },
                    new List<PositionFits>()
                    {
                        new PositionFits(0, 164, 0, 371, not: true)
                    }
                ));

                positions.Add(new PositionSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "Pinetree_01"),
                    },
                    new List<PositionFits>()
                    {
                        new PositionFits(0, 0, 127, 0, not: true)
                    }
                ));

                positions.Add(new PositionSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(material: "beehive"),
                    },
                    new List<PositionFits>()
                    {
                        new PositionFits(0, 46, 98, 0)
                    }
                ));

                positions.Add(new PositionSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(material: "Midsummerpole_mat"),
                    },
                    new List<PositionFits>()
                    {
                        new PositionFits(0, 175, 0, 0),
                        new PositionFits(0, 183, 54, 0)
                    }
                ));

                positions.Add(new PositionSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "goblin_roof", partial: true, only: true),
                        new MaterialFits(material: "GoblinVillage_Cloth", partial: true, only: true),
                    },
                    new List<PositionFits>()
                    {
                        new PositionFits(0, 230, 0, 130),
                        new PositionFits(0, 85, 0, 201)
                    }
                ));

                positions.Add(new PositionSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "darkwood_roof", partial: true, only: true),
                        new MaterialFits(material: "RoofShingles", partial: true, only: true),
                    },
                    new List<PositionFits>()
                    {
                        new PositionFits(0, 0, 0, 54),
                    }
                ));

                positions.Add(new PositionSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "copper_roof", partial: true, only: true),
                        new MaterialFits(material: "RoofShingles", partial: true, only: true),
                    },
                    new List<PositionFits>()
                    {
                        new PositionFits(0, 0, 0, 54),
                    }
                ));

                positions.Add(new PositionSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "wood_roof", partial: true, only: true),
                        new MaterialFits(material: "straw", partial: true, only: true),
                    },
                    new List<PositionFits>()
                    {
                        new PositionFits(),
                    }
                ));

                positions.Add(new PositionSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "shrub_2_heath", only: true),
                        new MaterialFits(material: "shrub_heath", only: true),
                    },
                    new List<PositionFits>()
                    {
                        new PositionFits(14, 0, 14, 0),
                    }
                ));

                positions.Add(new PositionSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "instanced_meadows_grass", only: true),
                        new MaterialFits(material: "grasscross_meadows", only: true),
                    },
                    new List<PositionFits>()
                    {
                        new PositionFits(),
                    }
                ));

                positions.Add(new PositionSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "instanced_meadows_grass_short", only: true),
                        new MaterialFits(material: "grasscross_meadows_short", only: true),
                    },
                    new List<PositionFits>()
                    {
                        new PositionFits(),
                    }
                ));

                positions.Add(new PositionSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(material: "neck", only:true),
                        new MaterialFits(prefab: "Neck", only:true),
                        new MaterialFits(renderer: "Lillies", only:true)
                    },
                    new List<PositionFits>()
                    {
                        new PositionFits(24, 40, 38, 54),
                        new PositionFits(50, 0, 52, 0),
                    }
                ));

                positions.Add(new PositionSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "roof_wood_", partial: true, only: true),
                        new MaterialFits(material: "straw", partial: true, only: true),
                    },
                    new List<PositionFits>()
                    {
                        new PositionFits(),
                    }
                ));

                positions.Add(new PositionSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "roof_darkwood_", partial: true, only: true),
                        new MaterialFits(material: "RoofShingles", partial: true, only: true),
                    },
                    new List<PositionFits>()
                    {
                        new PositionFits(),
                    }
                ));

                positions.Add(new PositionSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "elvenwood_roof", partial: true, only: true),
                        new MaterialFits(material: "straw", partial: true, only: false),
                        new MaterialFits(material: "ReworkedValheim", partial: true, only: false),
                    },
                    new List<PositionFits>()
                    {
                        new PositionFits(),
                    }
                ));

                positions.Add(new PositionSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "BFP_FineWoodRoof", partial: true, only: true),
                        new MaterialFits(material: "straw", partial: true, only: true),
                    },
                    new List<PositionFits>()
                    {
                        new PositionFits(),
                    }
                ));

                positions.Add(new PositionSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "cloth_roof", partial: true, only: true),
                        new MaterialFits(material: "shipyardNewCloth", partial: true, only: false),
                        new MaterialFits(material: "M_Cloth_01", partial: true, only: false),
                    },
                    new List<PositionFits>()
                    {
                        new PositionFits(),
                    }
                ));

                positions.Add(new PositionSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "BFP_ClayRoof", partial: true, only: true),
                        new MaterialFits(material: "bcp_clay", partial: true, only: true),
                    },
                    new List<PositionFits>()
                    {
                        new PositionFits(),
                    }
                ));

                positions.Add(new PositionSpecific(
                    new List<MaterialFits>()
                    {
                        new MaterialFits(prefab: "piece_grausten_roof", partial: true, only: true),
                        new MaterialFits(material: "Grausten_RoofSlab_mat", partial: true, only: true),
                    },
                    new List<PositionFits>()
                    {
                        new PositionFits(),
                    }
                ));

            }

            public bool IsPixelToChange(Color color, int pos, TextureProperties properties, bool isGrass, bool isMoss, Material material, PositionSpecific positionSpec, ColorSpecific colorSpec)
            {
                if (color.a == 0f)
                    return false;

                if (positionSpec != null && !positionSpec.FitsPosition(properties.width, properties.height, pos))
                    return false;

                if (colorSpec != null)
                    return colorSpec.FitsColor(color);

                if (IsCreature(material.shader.name) && positionSpec == null && colorSpec == null)
                    return false;

                return colorReplacement.ReplaceColor(color, isGrass, isMoss);
            }
        }

        public const string settingsSubdirectory = "Cache settings";
        public const string defaultsSubdirectory = "Defaults";
        public const string materialsSettingsFileName = "Materials.json";
        public const string colorsSettingsFileName = "Colors.json";
        public const string colorsReplacementsFileName = "Color ranges.json";
        public const string colorsPositionsFileName = "Color positions.json";

        public static MaterialCacheSettings materialSettings = new MaterialCacheSettings(loadDefaults: true);
        public static ColorsCacheSettings colorSettings = new ColorsCacheSettings(loadDefaults: true);
        public static ColorReplacementSpecifications colorReplacement = new ColorReplacementSpecifications(loadDefaults: true);
        public static ColorPositionsSettings colorPositions = new ColorPositionsSettings(loadDefaults: true);

        private static SeasonalTextureVariants currentTextureVariants = Seasons.texturesVariants;

        public static void SetCurrentTextureVariants(SeasonalTextureVariants texturesVariants)
        {
            currentTextureVariants = texturesVariants;
        }

        public static void OnCacheRevisionChange()
        {
            LogInfo($"Cache revision updated {cacheRevision.Value}");
            if (!configSync.InitialSyncDone && !configSync.IsSourceOfTruth && texturesVariants.revision != 0 && cacheRevision.Value != texturesVariants.revision)
                instance.StartCoroutine(texturesVariants.ReloadCache());
        }

        public static void SetupConfigWatcher()
        {
            string filter = $"*.json";

            FileSystemWatcher fileSystemWatcher1 = new FileSystemWatcher(CacheSettingsDirectory(), filter);
            fileSystemWatcher1.Changed += new FileSystemEventHandler(ReadConfigs);
            fileSystemWatcher1.Created += new FileSystemEventHandler(ReadConfigs);
            fileSystemWatcher1.Renamed += new RenamedEventHandler(ReadConfigs);
            fileSystemWatcher1.Deleted += new FileSystemEventHandler(ReadConfigs);
            fileSystemWatcher1.IncludeSubdirectories = false;
            fileSystemWatcher1.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            fileSystemWatcher1.EnableRaisingEvents = true;

            foreach (FileInfo file in new DirectoryInfo(CacheSettingsDirectory()).GetFiles("*.json", SearchOption.TopDirectoryOnly))
                ReadConfigFile(file.Name, file.FullName);

            cacheRevision.AssignLocalValue(GetRevision());
        }

        private static void ReadConfigs(object sender, FileSystemEventArgs eargs)
        {
            ReadConfigFile(eargs.Name, eargs.FullPath);
            if (eargs is RenamedEventArgs && GetSyncedValueToAssign((eargs as RenamedEventArgs).OldName, out CustomSyncedValue<string> syncedValue, out string logMessage))
            {
                syncedValue.AssignLocalValue("");
                LogInfo(logMessage);
            }

            cacheRevision.AssignLocalValue(GetRevision());
        }

        private static void ReadConfigFile(string filename, string fullname)
        {
            if (!GetSyncedValueToAssign(filename, out CustomSyncedValue<string> syncedValue, out string logMessage))
                return;

            try
            {
                ConfigSync.ProcessingServerUpdate = false;
                syncedValue.AssignLocalValue(File.ReadAllText(fullname));
            }
            catch (Exception e)
            {
                LogWarning($"Error reading file ({fullname})! Error: {e.Message}");
                syncedValue.AssignLocalValue("");
                logMessage += " defaults";
            }

            LogInfo(logMessage);
        }

        private static bool GetSyncedValueToAssign(string filename, out CustomSyncedValue<string> customSyncedValue, out string logMessage)
        {
            if (filename == materialsSettingsFileName)
            {
                customSyncedValue = customMaterialSettingsJSON;
                logMessage = "Custom materials settings loaded";
            }
            else if (filename == colorsSettingsFileName)
            {
                customSyncedValue = customColorSettingsJSON;
                logMessage = "Custom color settings loaded";
            }
            else if (filename == colorsReplacementsFileName)
            {
                customSyncedValue = customColorReplacementJSON;
                logMessage = "Custom color replacements loaded";
            }
            else if (filename == colorsPositionsFileName)
            {
                customSyncedValue = customColorPositionsJSON;
                logMessage = "Custom color positions loaded";
            }
            else
            {
                customSyncedValue = null;
                logMessage = "";
            }
            
            return customSyncedValue != null;
        }

        public static bool GetColorVariants(string prefabName, string rendererName, Material material, string propertyName, Color color, out Color[] colors, bool isPlant)
        {
            colors = null;

            bool isGrass = IsGrass(material.shader.name);
            bool isMoss = IsMoss(propertyName);
            bool isCreature = IsCreature(material.shader.name);

            if (!colorReplacement.ReplaceColor(color, isGrass, isMoss, prefabName, rendererName, material.name))
                return false;

            SeasonalColorVariants colorVariants = colorSettings.GetPrefabOverride(prefabName) ?? colorSettings.GetMaterialOverride(material.name);
            if (colorVariants == null)
            {
                if (IsPine(material.name, prefabName))
                    colorVariants = colorSettings.conifer;
                else if (IsPiece(material))
                    colorVariants = colorSettings.piece;
                else if (isGrass || isPlant)
                    colorVariants = colorSettings.grass;
                else if (isMoss)
                    colorVariants = colorSettings.moss;
                else if (isCreature)
                    colorVariants = colorSettings.creature;
                else if (IsBush(material.name, prefabName))
                    colorVariants = colorSettings.bush;
                else
                    colorVariants = colorSettings.seasonal;
            }

            List<Color> colorsList = new List<Color>();
            foreach (Season season in Enum.GetValues(typeof(Season)))
                for (int i = 0; i <= seasonColorVariants - 1; i++)
                    colorsList.Add(colorVariants.GetColorVariant(season, i).MergeColors(color));

            colors = colorsList.ToArray();

            return true;
        }

        public static bool GetTextureVariants(string prefabName, string rendererName, Material material, string propertyName, Texture texture, out TextureVariants textureVariants, bool isPlant)
        {
            textureVariants = new TextureVariants(texture);

            Color[] pixels = GetTexturePixels(texture, textureVariants.properties, out textureVariants.originalPNG);
            if (pixels.Length < 1)
                return false;

            bool isGrass = IsGrass(material.shader.name);
            bool isMoss = IsMoss(propertyName);
            bool isCreature = IsCreature(material.shader.name);

            PositionSpecific positionFits = null;
            foreach (PositionSpecific spec in colorPositions.positions)
                if (spec.FitsMaterial(prefabName, rendererName, material.name))
                {
                    positionFits = spec;
                    LogInfo($"Position specific prefab:{prefabName} renderer:{rendererName} material:{material.name} {spec.bounds.Join()}");
                    break;
                }

            ColorSpecific colorSpecific = null;
            foreach (ColorSpecific spec in colorReplacement.specific)
                if (spec.FitsMaterial(prefabName, rendererName, material.name))
                {
                    colorSpecific = spec;
                    LogInfo($"Color specific prefab:{prefabName} renderer:{rendererName} material:{material.name} {spec.colors.Join()}");
                    break;
                }

            List<int> pixelsToChange = new List<int>();
            for (int i = 0; i < pixels.Length; i++)
                if (colorPositions.IsPixelToChange(pixels[i], i, textureVariants.properties, isGrass, isMoss, material, positionFits, colorSpecific))
                    pixelsToChange.Add(i);

            if (pixelsToChange.Count == 0)
                return false;

            SeasonalColorVariants colorVariants = colorSettings.GetPrefabOverride(prefabName) ?? colorSettings.GetMaterialOverride(material.name);
            if (colorVariants == null)
            {
                if (IsPine(material.name, prefabName))
                    colorVariants = colorSettings.conifer;
                else if (IsPiece(material))
                    colorVariants = colorSettings.piece;
                else if (isGrass || isPlant)
                    colorVariants = colorSettings.grass;
                else if (isMoss)
                    colorVariants = colorSettings.moss;
                else if (isCreature)
                    colorVariants = colorSettings.creature;
                else if (IsBush(material.name, prefabName))
                    colorVariants = colorSettings.bush;
                else
                    colorVariants = colorSettings.seasonal;
            }

            foreach (Season season in Enum.GetValues(typeof(Season)))
            {
                List<ColorVariant> colorVariant = new List<ColorVariant>();
                for (int i = 0; i <= seasonColorVariants - 1; i++)
                    colorVariant.Add(colorVariants.GetColorVariant(season, i));

                GenerateTextureVariants(season, colorVariant.ToArray(), pixels, pixelsToChange.ToArray(), textureVariants.properties, textureVariants);
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
        
        private static void GenerateTextureVariants(Season season, ColorVariant[] colorVariants, Color[] pixels, int[] pixelsToChange, TextureProperties texProperties, TextureVariants textureVariants)
        {
            List<Color[]> seasonColors = new List<Color[]>();
            for (int i = 0; i < colorVariants.Length; i++)
                seasonColors.Add(pixels.ToArray());

            foreach (int i in pixelsToChange)
                for (int j = 0; j < colorVariants.Length; j++)
                    seasonColors[j][i] = colorVariants[j].MergeColors(pixels[i]);

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

        public static uint GetRevision()
        {
            InitSettings();

            StringBuilder sb = new StringBuilder(4);
            sb.Append(JsonConvert.SerializeObject(materialSettings));
            sb.Append(JsonConvert.SerializeObject(colorSettings));
            sb.Append(JsonConvert.SerializeObject(colorReplacement));
            sb.Append(JsonConvert.SerializeObject(colorPositions));
            sb.Append(pluginVersion);

            return (uint)sb.ToString().GetStableHashCode();
        }

        private static string CacheSettingsDirectory()
        {
            string directory = Path.Combine(configDirectory, settingsSubdirectory);
            Directory.CreateDirectory(directory);

            return directory;
        }

        public static void InitSettings()
        {
            if (!String.IsNullOrEmpty(customMaterialSettingsJSON.Value))
            {
                try
                {
                    materialSettings = JsonConvert.DeserializeObject<MaterialCacheSettings>(customMaterialSettingsJSON.Value);
                    LogInfo($"Custom materials settings applied");
                }
                catch (Exception e)
                {
                    materialSettings = new MaterialCacheSettings(loadDefaults: true);
                    LogWarning($"Error parsing custom materials settings:\n{e}");
                }
            }
            else
            {
                materialSettings = new MaterialCacheSettings(loadDefaults: true);
            }

            if (!String.IsNullOrEmpty(customColorSettingsJSON.Value))
            {
                try
                {
                    colorSettings = JsonConvert.DeserializeObject<ColorsCacheSettings>(customColorSettingsJSON.Value);
                    LogInfo($"Custom color settings applied");
                }
                catch (Exception e)
                {
                    colorSettings = new ColorsCacheSettings(loadDefaults: true);
                    LogWarning($"Error parsing custom color settings:\n{e}");
                }
            }
            else
            {
                colorSettings = new ColorsCacheSettings(loadDefaults: true);
            }

            if (!String.IsNullOrEmpty(customColorReplacementJSON.Value))
            {
                try
                {
                    colorReplacement = JsonConvert.DeserializeObject<ColorReplacementSpecifications>(customColorReplacementJSON.Value);
                    LogInfo($"Custom color replacements applied");
                }
                catch (Exception e)
                {
                    colorReplacement = new ColorReplacementSpecifications(loadDefaults: true);  
                    LogWarning($"Error parsing custom color replacements:\n{e}");
                }
            }
            else
            {
                colorReplacement = new ColorReplacementSpecifications(loadDefaults: true);
            }

            if (!String.IsNullOrEmpty(customColorPositionsJSON.Value))
            {
                try
                {
                    colorPositions = JsonConvert.DeserializeObject<ColorPositionsSettings>(customColorPositionsJSON.Value);
                    LogInfo($"Custom color positions applied");
                }
                catch (Exception e)
                {
                    colorPositions = new ColorPositionsSettings(loadDefaults: true);
                    LogWarning($"Error parsing custom color positions:\n{e}");
                }
            }
            else
            {
                colorPositions = new ColorPositionsSettings(loadDefaults: true);
            }
        }

        public static void SaveDefaults()
        {
            string defaultsFolder = Path.Combine(CacheSettingsDirectory(), defaultsSubdirectory);
            Directory.CreateDirectory(defaultsFolder);

            LogInfo($"Saving default materials settings");
            File.WriteAllText(Path.Combine(defaultsFolder, materialsSettingsFileName), JsonConvert.SerializeObject(new MaterialCacheSettings(loadDefaults: true), Formatting.Indented));

            LogInfo($"Saving default colors settings");
            File.WriteAllText(Path.Combine(defaultsFolder, colorsSettingsFileName), JsonConvert.SerializeObject(new ColorsCacheSettings(loadDefaults: true), Formatting.Indented));

            LogInfo($"Saving default colors ranges");
            File.WriteAllText(Path.Combine(defaultsFolder, colorsReplacementsFileName), JsonConvert.SerializeObject(new ColorReplacementSpecifications(loadDefaults: true), Formatting.Indented));

            LogInfo($"Saving default colors positions");
            File.WriteAllText(Path.Combine(defaultsFolder, colorsPositionsFileName), JsonConvert.SerializeObject(new ColorPositionsSettings(loadDefaults: true), Formatting.Indented));
        }

        public static IEnumerator FillWithGameData()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            LogInfo("Initializing cache settings");
            currentTextureVariants.revision = GetRevision();
            LogInfo($"Cache settings revision {currentTextureVariants.revision}");

            LogInfo("Caching clutters");
            yield return AddClutters();

            LogInfo("Caching prefabs");
            yield return AddZNetScenePrefabs();

            LogInfo("Caching locations");
            yield return AddLocations();

            LogInfo("Caching yggdrasil branch");
            yield return AddYggdrasilBranch();

            stopwatch.Stop();

            LogInfo($"Created cache revision {currentTextureVariants.revision} with {currentTextureVariants.controllers.Count} controllers, {currentTextureVariants.textures.Count} textures in {stopwatch.Elapsed.TotalSeconds,-4:F2} seconds.");
        }

        private static IEnumerator AddYggdrasilBranch()
        {
            Transform yggdrasilBranch = EnvMan.instance.transform.Find("YggdrasilBranch");
            if (yggdrasilBranch == null)
                yield break;

            foreach (MeshRenderer mrenderer in yggdrasilBranch.GetComponentsInChildren<MeshRenderer>())
                CacheMaterials(mrenderer.sharedMaterials, "YggdrasilBranch", mrenderer.name, mrenderer.GetType().Name, mrenderer.transform.GetPath(), isPlant: true);
        }

        private static IEnumerator AddLocations()
        {
            foreach (ZoneSystem.ZoneLocation loc in ZoneSystem.instance.m_locations)
            {
                if (!loc.m_prefab.IsValid)
                    continue;

                if (materialSettings.ignorePrefab.Contains(loc.m_prefabName))
                    continue;

                if (materialSettings.ignorePrefabPartialName.Any(namepart => loc.m_prefabName.Contains(namepart)))
                    continue;

                loc.m_prefab.Load();

                Transform root = loc.m_prefab.Asset.transform.Find("exterior") ?? loc.m_prefab.Asset.transform;

                foreach (MeshRenderer mrenderer in root.GetComponentsInChildren<MeshRenderer>())
                    CacheMaterials(mrenderer.sharedMaterials, loc.m_prefabName, mrenderer.name, mrenderer.GetType().Name, mrenderer.transform.GetPath());

                foreach (SkinnedMeshRenderer smrenderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                    CacheMaterials(smrenderer.sharedMaterials, loc.m_prefabName, smrenderer.name, smrenderer.GetType().Name, smrenderer.transform.GetPath());

                loc.m_prefab.Release();

                yield return null;
            }
        }

        private static IEnumerator AddClutters()
        {
            foreach (ClutterSystem.Clutter clutter in ClutterSystem.instance.m_clutter)
            {
                if (clutter.m_prefab == null)
                    continue;

                if (materialSettings.ignorePrefab.Contains(clutter.m_prefab.name))
                    continue;

                if (materialSettings.ignorePrefabPartialName.Any(namepart => clutter.m_prefab.name.Contains(namepart)))
                    continue;

                if (clutter.m_prefab.TryGetComponent(out InstanceRenderer renderer))
                    CacheMaterials(new Material[1] { renderer.m_material }, clutter.m_prefab.name, renderer.name, renderer.GetType().Name, renderer.transform.GetPath());
                else
                    foreach (MeshRenderer mrenderer in clutter.m_prefab.GetComponentsInChildren<MeshRenderer>())
                        CacheMaterials(mrenderer.sharedMaterials, clutter.m_prefab.name, mrenderer.name, mrenderer.GetType().Name, mrenderer.transform.GetPath());

                yield return waitForFixedUpdate;
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

                bool isNew = !currentTextureVariants.controllers.TryGetValue(prefabName, out PrefabController controller);

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

                        if (GetColorVariants(prefabName, rendererName, material, propertyName, color, out Color[] colors, isPlant: isPlant))
                            cachedRenderer.AddMaterialColors(material, propertyName, colors);
                    }
                }
                else if (materialSettings.shaderColors.TryGetValue(material.shader.name, out string[] colorNames))
                    foreach (string propertyName in colorNames)
                    {
                        Color color = material.GetColor(propertyName);
                        if (color == null || color == Color.clear || color == Color.white || color == Color.black)
                            continue;

                        if (GetColorVariants(prefabName, rendererName, material, propertyName, color, out Color[] colors, isPlant: isPlant))
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
                        if (currentTextureVariants.textures.ContainsKey(textureID))
                        {
                            cachedRenderer.AddMaterialTexture(material, propertyName, textureID);
                        }
                        else if (GetTextureVariants(prefabName, rendererName, material, propertyName, texture, out TextureVariants textureVariants, isPlant: isPlant))
                        {
                            currentTextureVariants.textures.Add(textureID, textureVariants);
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
                        if (currentTextureVariants.textures.ContainsKey(textureID))
                        {
                            cachedRenderer.AddMaterialTexture(material, propertyName, textureID);
                        }
                        else if (GetTextureVariants(prefabName, rendererName, material, propertyName, texture, out TextureVariants textureVariants, isPlant: isPlant))
                        {
                            currentTextureVariants.textures.Add(textureID, textureVariants);
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
                        currentTextureVariants.controllers.Add(prefabName, controller);

                    LogInfo($"Caching {prefabName}{controller}");

                    if (isSingleRenderer)
                        return;
                }
            }
        }

        private static IEnumerator AddZNetScenePrefabs()
        {
            int i = 0;
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
                        continue;

                    if (renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null)
                        continue;

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
                    if (lodGroup == null || !CachePrefabLODGroup(lodGroup, prefab.name, isLodInHierarchy: true))
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

                    if (!prefab.TryGetComponent(out LODGroup lodGroup) || lodGroup.lodCount < 2 || !CachePrefabLODGroup(lodGroup, prefab.name, isLodInHierarchy: false, isPlant: isPlant))
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
                    if (lodGroup == null || !CachePrefabLODGroup(lodGroup, prefab.name, isLodInHierarchy: true))
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

                if (i++ % 5 == 0)
                    yield return null;
            }
        }

        private static bool CachePrefabLODGroup(LODGroup lodGroup, string prefabName, bool isLodInHierarchy, bool isPlant = false)
        {
            bool result = false;

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
                    result = true;
                }
            }

            return result;
        }

        private static void CacheParticleSystemStartColor(ParticleSystem ps, string prefabName)
        {
            if (ps.main.startColor.color == Color.white)
                return;

            if (!materialSettings.particleSystemStartColors.Contains(ps.name) && !materialSettings.particleSystemStartColors.Contains(prefabName))
                return;

            string transformPath = ps.transform.GetPath();

            bool isNew = !currentTextureVariants.controllers.TryGetValue(prefabName, out PrefabController controller);

            if (isNew)
                controller = new PrefabController();
            else if (controller.particleSystemStartColors != null && controller.particleSystemStartColors.ContainsKey(transformPath))
                return;

            SeasonalColorVariants colorVariants = colorSettings.GetPrefabOverride(prefabName) ?? colorSettings.seasonal;

            List<string> colors = new List<string>();
            foreach (Season season in Enum.GetValues(typeof(Season)))
                for (int i = 0; i <= seasonColorVariants - 1; i++)
                {
                    ColorVariant colorVariant = colorVariants.GetColorVariant(season, i);
                    Color color = colorVariant.MergeColors(ps.main.startColor.color);
                    colors.Add($"#{ColorUtility.ToHtmlStringRGBA(color)}");
                }

            controller.particleSystemStartColors ??= new Dictionary<string, string[]>();

            controller.particleSystemStartColors.Add(transformPath, colors.ToArray());

            if (controller.Initialized())
            {
                if (isNew)
                    currentTextureVariants.controllers.Add(prefabName, controller);

                LogInfo($"Caching {prefabName}{controller}");
            }
        }
    }

}
