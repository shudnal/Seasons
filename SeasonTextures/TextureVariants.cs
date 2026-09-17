using System;
using System.Collections.Generic;
using System.Linq;
using static Seasons.Seasons;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Seasons
{
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
            if (properties == null)
                return;

            foreach (Season season in Enum.GetValues(typeof(Season)))
            {
                if (!texData.variants.TryGetValue(season, out Dictionary<int, byte[]> variants))
                    continue;

                for (int variant = 0; variant < seasonColorVariants; variant++)
                {
                    if (!variants.TryGetValue(variant, out byte[] data))
                        continue;

                    Texture2D tex = properties.CreateTexture();

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
            if (texture is not Texture2D)
                return;
            original = texture as Texture2D;
            properties = new TextureProperties(texture as Texture2D);
            originalName = original.name;
        }

        public void Dispose()
        {
            foreach (var variants in seasons.Values)
                foreach (Texture2D texture in variants.Values)
                    if (texture)
                        Object.Destroy(texture);
            seasons.Clear();
        }

        public bool Initialized()
        {
            return properties != null && Enum.GetValues(typeof(Season)).Cast<Season>().All(season =>
                seasons.TryGetValue(season, out Dictionary<int, Texture2D> variants)
                && Enumerable.Range(0, seasonColorVariants).All(variant => variants.TryGetValue(variant, out Texture2D texture) && texture));
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

        public Texture2D GetSeasonalVariant(Season season, int variant)
        {
            if (CustomTextures.HaveCustomTexture(originalName, season, variant, properties, out Texture2D customTexture))
                return customTexture;

            if (seasons.TryGetValue(season, out Dictionary<int, Texture2D> variants) && variants.TryGetValue(variant, out Texture2D texture))
                return texture;

            return original;
        }
    }

}
