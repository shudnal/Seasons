using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HarmonyLib;

namespace Seasons
{
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

}
