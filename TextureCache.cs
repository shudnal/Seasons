using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;
using static Seasons.Seasons;
using UnityEngine.Rendering;
using static Seasons.TextureSeasonVariants;
using BepInEx.Logging;

namespace Seasons
{
    public static class SeasonsTexture
    {
        [Serializable]
        public class TextureProperties
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

        public static bool CacheMaterialTextures(Material material, string rendererFolder, string shaderFolder, string prefab, int lodLevel, string[] textureNames)
        {
            string pathMaterial = Path.Combine(cacheFolder, rendererFolder, shaderFolder, prefab, lodLevel.ToString(), material.name);
            if (Directory.Exists(pathMaterial))
                return true;

            foreach (string textName in material.GetTexturePropertyNames().Where(mat => textureNames.Any(text => mat.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                Texture texture = material.GetTexture(textName);
                if (texture == null)
                    continue;

                TextureProperties texProperties = new TextureProperties(texture as Texture2D);

                byte[] texData = GetTextureData(texture, texProperties, out Color[] pixels);
                if (texData.Length < 1)
                    continue;

                List<int> pixelsToChange = new List<int>();
                for (int i = 0; i < pixels.Length; i++)
                    if (IsCloseToGreen(pixels[i]))
                        pixelsToChange.Add(i);

                if (pixelsToChange.Count == 0)
                    continue;

                Dictionary<string, byte[]> seasonalTexturesData = new Dictionary<string, byte[]>();

                foreach (Season season in Enum.GetValues(typeof(Season)))
                {
                    List<Color> colorVariants = new List<Color>();
                    for (int i = 1; i <= seasonColorVariants; i++)
                    {
                        Color colorVariant;
                        if (IsGrass(material.shader.name))
                            colorVariant = instance.GetGrassConfigColor(season, i);
                        else if (IsMoss(textName))
                            colorVariant = instance.GetMossConfigColor(season, i); 
                        else 
                            colorVariant = instance.GetSeasonConfigColor(season, i);

                        if (IsPine(material.name, prefab))
                            colorVariant.a /= 2;
                        
                        colorVariants.Add(colorVariant);
                    }

                    GenerateColorVariants(season, colorVariants.ToArray(), pixels, pixelsToChange, texProperties, seasonalTexturesData);
                }

                if (seasonalTexturesData.Count == 0)
                    continue;

                LogInfo($"Caching {pathMaterial}");

                string texturePath = Path.Combine(pathMaterial, textName);

                Directory.CreateDirectory(texturePath);

                File.WriteAllBytes("\\\\?\\" + Path.Combine(texturePath, $"{texture.name}{originalPostfix}"), texData);

                File.WriteAllText("\\\\?\\" + Path.Combine(texturePath, textureProperties), JsonUtility.ToJson(texProperties));

                foreach (KeyValuePair<string, byte[]> textureVariant in seasonalTexturesData)
                    File.WriteAllBytes("\\\\?\\" + Path.Combine(texturePath, textureVariant.Key), textureVariant.Value);
            }

            return Directory.Exists(pathMaterial);
        }

        public static byte[] GetTextureData(Texture texture, TextureProperties texProperties, out Color[] pixels)
        {
            Texture2D newTexture = GetReadableTexture(texture, out pixels, texProperties);

            byte[] texData = newTexture.EncodeToPNG();

            Object.Destroy(newTexture);

            return texData;
        }

        public static Texture2D GetReadableTexture(Texture texture, out Color[] pixels, TextureProperties texProperties = null)
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

            pixels = textureCopy.GetPixels();

            Texture2D newTexture = new Texture2D(texture.width, texture.height, texProperties.format, texProperties.mipmapCount, false)
            {
                filterMode = texProperties.filterMode,
                anisoLevel = texProperties.anisoLevel,
                mipMapBias = texProperties.mipMapBias,
                wrapMode = texProperties.wrapMode
            };
            newTexture.SetPixels(pixels);
            newTexture.Apply();

            Object.Destroy(textureCopy);

            return newTexture;
        }

        public static Texture2D GetReadableTexture(Texture texture, TextureProperties texProperties = null)
        {
            return GetReadableTexture(texture, out _, texProperties);
        }

        public static void GenerateColorVariants(Season season, Color[] colorVariants, Color[] pixels, List<int> pixelsToChange, TextureProperties texProperties, Dictionary<string, byte[]> seasonalTexturesData)
        {
            List<Color[]> seasonColors = new List<Color[]>();
            for (int i = 0; i < colorVariants.Length; i++)
                seasonColors.Add(pixels.ToArray());

                    foreach (int i in pixelsToChange)
                for (int j = 0; j < colorVariants.Length; j++)
                    seasonColors[j][i] = MergeColors(pixels[i], colorVariants[j], colorVariants[j].a, season == Season.Winter);

            Texture2D tex = new Texture2D(texProperties.width, texProperties.height, texProperties.format, texProperties.mipmapCount, false)
            {
                filterMode = texProperties.filterMode,
                anisoLevel = texProperties.anisoLevel,
                mipMapBias = texProperties.mipMapBias,
                wrapMode = texProperties.wrapMode
            };

            for (int variant = 0; variant < colorVariants.Length; variant++)
            {
                tex.SetPixels(seasonColors[variant]);
                tex.Apply();

                string filename = SeasonFileName(season, variant);

                seasonalTexturesData.Add(filename, tex.EncodeToPNG());
            }

            Object.Destroy(tex);
        }

        public static string SeasonFileName(Season season, int variant)
        {
            return $"{season}_{variant + 1}.png";
        }

        private static bool IsCloseToGreen(Color color)
        {
            HSLColor hslcolor = new HSLColor(color);
            return color.a != 0f && (hslcolor.s >= 0.25f && GetHueDistance(hslcolor.h, 85f) <= 55f || hslcolor.s >= 0.15f && GetHueDistance(hslcolor.h, 120f) <= 40f);
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
            
            HSLColor hslcolor1 = new HSLColor(color1);
            if (!winterColor)
            {
                newHSLColor.l = hslcolor1.l;
                //newColor.s = hslcolor1.s;//Mathf.Lerp(hslcolor1.s, newHSLColor.s, 0.75f);
            }
            else
            {
                //newHSLColor.s = hslcolor1.s * 0.5f;
            }
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

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
    public static class ZoneSystem_Start_TextureCache
    {
        public static HashSet<Material> cachedMaterials = new HashSet<Material>();

        public static void CacheMaterials(string prefabName, int lodLevel, string rendererType, List<Material> materials)
        {
            for (int m = 0; m < materials.Count; m++)
            {
                Material material = materials[m];

                if (material == null || cachedMaterials.Contains(material))
                    continue;

                if (shadersTypes.TryGetValue(rendererType, out string[] shaders) && shaders.Contains(material.shader.name) && shaderFolders.TryGetValue(material.shader.name, out string shaderFolder))
                {
                    if (shaderIgnoreMaterial.TryGetValue(material.shader.name, out string[] ignoreMaterial) && ignoreMaterial.Any(ignore => material.name.IndexOf(ignore, StringComparison.OrdinalIgnoreCase) >= 0))
                        continue;

                    if (!shaderTextures.TryGetValue(material.shader.name, out string[] textureNames))
                        textureNames = new string[0];

                    if (SeasonsTexture.CacheMaterialTextures(material, renderersFolders[rendererType], shaderFolder, prefabName, lodLevel, textureNames))
                        cachedMaterials.Add(material);
                }
            }
        }

        public static void CreateTextures()
        {

            foreach (ClutterSystem.Clutter clutter in ClutterSystem.instance.m_clutter.Where(c => c.m_prefab != null))
            {
                if (!clutter.m_prefab.TryGetComponent(out InstanceRenderer renderer))
                    continue;

                Material material = renderer.m_material;

                if (material == null || material.shader == null)
                    continue;

                if (cachedMaterials.Contains(material))
                    continue;

                string rendererType = renderer.GetType().ToString();

                if (shadersTypes.TryGetValue(rendererType, out string[] shaders) && shaders.Contains(material.shader.name) && shaderFolders.TryGetValue(material.shader.name, out string shaderFolder))
                {
                    if (!shaderTextures.TryGetValue(material.shader.name, out string[] textureNames))
                        textureNames = new string[0];

                    if (SeasonsTexture.CacheMaterialTextures(material, renderersFolders[rendererType], shaderFolder, clutter.m_prefab.name, 0, textureNames))
                        cachedMaterials.Add(material);
                }

            }

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
                    if (prefab.TryGetComponent<LODGroup>(out LODGroup lodGroup) && lodGroup.lodCount > 0)
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

                cachedMaterials.Clear();
            }
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix()
        {
            if (prefabControllers.Count > 0 || TextureSeasonVariants.CacheFiles())
                return;

            CreateTextures();

            if (!TextureSeasonVariants.CacheFiles())
                LogInfo("Missing cache");
        }
    }
}
