using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;
using Object = UnityEngine.Object;

namespace Seasons
{
    public class TextureSeasonVariants
    {
        private static System.Reflection.Assembly[] currentAssemblies = null;
        private static Dictionary<string, Type> cachedTypes = new Dictionary<string, Type>();

        public class SeasonalTextures
        {
            public Texture2D m_original;
            public Dictionary<Season, Dictionary<int, Texture2D>> m_seasons = new Dictionary<Season, Dictionary<int, Texture2D>>();
            public string textureProperty;

            public void SetOriginalTexture(Texture texture)
            {
                m_original = texture as Texture2D;
            }

            public bool Initialized()
            {
                return !String.IsNullOrEmpty(textureProperty) && m_seasons.Count > 0;
            }

            public bool HaveOriginalTexture()
            {
                return m_original != null;
            }

            public void AddVariant(Season season, int variant, Texture2D tex)
            {
                if (!m_seasons.TryGetValue(season, out Dictionary<int, Texture2D> variants))
                {
                    variants = new Dictionary<int, Texture2D>();
                    m_seasons.Add(season, variants);
                }

                if (!variants.ContainsKey(variant))
                    variants.Add(variant, tex);
            }
        }

        public class MaterialTextures
        {
            public List<SeasonalTextures> m_textures = new List<SeasonalTextures>();
            public string m_materialName;
            public string m_shader;

            public bool Initialized()
            {
                return !String.IsNullOrWhiteSpace(m_materialName) && !String.IsNullOrWhiteSpace(m_shader) && m_textures.Count > 0;
            }

            public void AddTextures(SeasonalTextures st)
            {
                m_textures.Add(st);
            }
        }

        public class PrefabControllerData
        {
            public Type m_renderer;
            public string m_prefabName;
            public Dictionary<int, List<MaterialTextures>> m_materials = new Dictionary<int, List<MaterialTextures>>();

            public bool Initialized()
            {
                return m_renderer != null && !String.IsNullOrWhiteSpace(m_prefabName) && m_materials.Count > 0;
            }

            public void AddMaterialTexture(int lodLevel, string materialName, string shader, SeasonalTextures st)
            {
                if (!m_materials.TryGetValue(lodLevel, out List<MaterialTextures> transformMaterials))
                {
                    transformMaterials = new List<MaterialTextures>();
                    m_materials.Add(lodLevel, transformMaterials);
                }

                MaterialTextures materialTextures;
                if (!transformMaterials.Any(m => m.m_materialName == materialName))
                {
                    materialTextures = new MaterialTextures
                    {
                        m_materialName = materialName,
                        m_shader = shader
                    };
                    transformMaterials.Add(materialTextures);
                }
                else
                {
                    materialTextures = transformMaterials.Find(m => m.m_materialName == materialName);
                }

                materialTextures.AddTextures(st);
            }
        
            public bool GetMaterialTextures(int lodLevel, out List<MaterialTextures> materialTextures)
            {
                return m_materials.TryGetValue(lodLevel, out materialTextures);
            }
        }

        public static bool GetTexture(string filename, out Texture2D tex, SeasonsTexture.TextureProperties texProperties)
        {
            tex = null;
            return ChangeTexture(filename, ref tex, texProperties);
        }

        public static bool ChangeTexture(string filename, ref Texture2D tex, SeasonsTexture.TextureProperties texProperties)
        {
            try
            {
                if (tex != null)
                    Object.Destroy(tex);

                tex = new Texture2D(texProperties.width, texProperties.height, texProperties.format, texProperties.mipmapCount, false)
                {
                    filterMode = texProperties.filterMode,
                    anisoLevel = texProperties.anisoLevel,
                    mipMapBias = texProperties.mipMapBias,
                    wrapMode = texProperties.wrapMode
                };
                tex.LoadImage(File.ReadAllBytes(filename), true);
                return true;
            }
            catch (Exception ex) 
            {
                LogInfo(ex);
            }

            tex = null;
            return false;
        }

        public static bool CacheFiles()
        {
            prefabControllers.Clear();

            DirectoryInfo cacheDirectory = new DirectoryInfo(cacheFolder);
            foreach (DirectoryInfo renderer in cacheDirectory.GetDirectories())
            {
                if (!renderersFolders.ContainsValue(renderer.Name))
                    continue;

                string rendererName = renderersFolders.First(s => s.Value == renderer.Name).Key;

                if (!shadersTypes.ContainsKey(rendererName))
                    continue;

                foreach (DirectoryInfo shader in renderer.GetDirectories())
                {
                    if (!shaderFolders.ContainsValue(shader.Name))
                        continue;

                    string shaderName = shaderFolders.First(s => s.Value == shader.Name).Key;

                    if (!shadersTypes[rendererName].Any(s => s.Equals(shaderName)))
                        continue;

                    foreach (DirectoryInfo prefab in shader.GetDirectories())
                    {
                        if (prefabControllers.ContainsKey(prefab.Name))
                        {
                            LogInfo($"Found another renderer {rendererName} shader {shaderName} for prefab {prefab.Name}");
                            continue;
                        }

                        PrefabControllerData prefabController = new PrefabControllerData
                        {
                            m_renderer = GetTypeByName(rendererName),
                            m_prefabName = prefab.Name
                        };

                        foreach (DirectoryInfo lodLevel in prefab.GetDirectories())
                            foreach (DirectoryInfo material in lodLevel.GetDirectories())
                                foreach (DirectoryInfo texName in material.GetDirectories())
                                {
                                    SeasonalTextures seasonalTextures = new SeasonalTextures();

                                    SeasonsTexture.TextureProperties texProperties = new SeasonsTexture.TextureProperties();
                                    FileInfo[] properties = texName.GetFiles(textureProperties);
                                    if (properties.Length > 0)
                                        texProperties = JsonUtility.FromJson<SeasonsTexture.TextureProperties>(File.ReadAllText(properties[0].FullName));

                                    seasonalTextures.textureProperty = texName.Name;

                                    foreach (Season season in Enum.GetValues(typeof(Season)))
                                    {
                                        for (int variant = 0; variant < seasonColorVariants; variant++)
                                        {
                                            FileInfo[] files = texName.GetFiles(SeasonsTexture.SeasonFileName(season, variant));
                                            if (files.Length == 0)
                                                continue;

                                            if (GetTexture(files[0].FullName, out Texture2D tex, texProperties))
                                                seasonalTextures.AddVariant(season, variant, tex);
                                        }
                                    }

                                    if (!seasonalTextures.Initialized())
                                        continue;

                                    prefabController.AddMaterialTexture(int.Parse(lodLevel.Name), material.Name, shaderName, seasonalTextures);
                                }

                        if (!prefabController.Initialized())
                            continue;

                        prefabControllers.Add(prefab.Name, prefabController);

                        LogInfo($"Added prefab season variant controller {prefabController.m_prefabName}:{prefabController.m_renderer} materials:{prefabController.m_materials.Count} textures:{prefabController.m_materials.Sum(m => m.Value.Count)}");
                    }
                }
            }

            return prefabControllers.Count > 0;
        }

        public static Type GetTypeByName(string name)
        {
            if (cachedTypes.TryGetValue(name, out Type type))
                return type;

            if (currentAssemblies == null)
                currentAssemblies = AppDomain.CurrentDomain.GetAssemblies().Reverse().ToArray();

            foreach (var assembly in currentAssemblies)
            {
                var tt = assembly.GetType(name);
                if (tt != null)
                {
                    cachedTypes.Add(name, tt);
                    return tt;
                }
            }

            return null;
        }
    }
}
