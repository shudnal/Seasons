using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using static Seasons.SeasonalTexturePrefabCache.ColorsCacheSettings;
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

        private const int RecolorPaletteBytes = 8 * 4 * sizeof(float);
        internal byte[] PendingRecolorData { get; private set; }
        private string m_recolorPath;
        private Texture2D m_recolorMask;
        private Vector4[] m_recolorPalette;

        internal bool HasRecolorData => PendingRecolorData != null || m_recolorPath != null;

        internal static long RecolorDataLength(TextureProperties properties) =>
            RecolorPaletteBytes + ((long)properties.width * properties.height + 7) / 8;

        // Persist the original pixel selection and the actual palette used to generate
        // this cache entry. Keep the bit-packed payload separate from cache.bin.
        internal void BuildRecolorData(int[] pixelsToChange, ColorVariant[] variants)
        {
            byte[] data = new byte[checked((int)RecolorDataLength(properties))];
            using (var writer = new BinaryWriter(new MemoryStream(data, writable: true)))
            {
                foreach (ColorVariant variant in variants)
                {
                    Color target = variant.colorValue;
                    writer.Write(target.r);
                    writer.Write(target.g);
                    writer.Write(target.b);
                    writer.Write(variant.targetProportion);
                }
                foreach (ColorVariant variant in variants)
                {
                    writer.Write(variant.useColor ? 1f : 0f);
                    writer.Write(variant.reduceOriginalColorToGrayscale ? 1f : 0f);
                    writer.Write(variant.restoreLuminance ? 1f : 0f);
                    writer.Write(0f);
                }
            }

            foreach (int pixel in pixelsToChange)
                data[RecolorPaletteBytes + (pixel >> 3)] |= (byte)(1 << (pixel & 7));
            PendingRecolorData = data;
        }

        // Called for a loaded cache entry or after its generation has been saved.
        internal void SetRecolorPath(string path)
        {
            m_recolorPath = path;
            PendingRecolorData = null;
        }

        internal bool TryGetRecolor(out Texture mask, Vector4[] palette)
        {
            mask = null;
            if (!HasRecolorData)
                return false;

            if (!m_recolorMask)
            {
                Texture2D texture = null;
                try
                {
                    byte[] data = PendingRecolorData ?? File.ReadAllBytes(m_recolorPath);
                    if (data.LongLength != RecolorDataLength(properties))
                        throw new InvalidDataException("Invalid seasonal recolor mask length.");

                    var colors = new Vector4[8];
                    using (var reader = new BinaryReader(new MemoryStream(data, writable: false)))
                        for (int i = 0; i < colors.Length; i++)
                            colors[i] = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

                    // An entirely disabled autumn palette needs neither a GPU mask
                    // nor the extra bake pass. The base variant remains usable.
                    if (colors[4].x == 0f && colors[5].x == 0f && colors[6].x == 0f)
                    {
                        SetRecolorPath(null);
                        return false;
                    }

                    byte[] pixels = new byte[checked(properties.width * properties.height)];
                    for (int i = 0; i < pixels.Length; i++)
                        pixels[i] = (data[RecolorPaletteBytes + (i >> 3)] & (1 << (i & 7))) != 0 ? (byte)255 : (byte)0;

                    texture = new Texture2D(properties.width, properties.height, TextureFormat.R8, properties.mipmapCount, true)
                    {
                        name = originalName + "_seasonal_recolor_mask",
                        filterMode = properties.filterMode,
                        anisoLevel = properties.anisoLevel,
                        mipMapBias = properties.mipMapBias,
                        wrapMode = properties.wrapMode
                    };
                    texture.SetPixelData(pixels, 0);
                    texture.Apply(true, true);
                    m_recolorPalette = colors;
                    m_recolorMask = texture;
                }
                catch (Exception error)
                {
                    if (texture)
                        Object.Destroy(texture);
                    // Do not repeatedly attempt to load a broken entry during rendering.
                    SetRecolorPath(null);
                    LogWarning($"Unable to load seasonal recolor mask: {error.Message}");
                    return false;
                }
            }

            Array.Copy(m_recolorPalette, palette, 8);
            mask = m_recolorMask;
            return true;
        }

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
            if (m_recolorMask)
                Object.Destroy(m_recolorMask);
            m_recolorMask = null;
            m_recolorPalette = null;
            SetRecolorPath(null);

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

            return GetGeneratedVariant(season, variant);
        }

        internal Texture2D GetGeneratedVariant(Season season, int variant)
        {
            if (seasons.TryGetValue(season, out Dictionary<int, Texture2D> variants) && variants.TryGetValue(variant, out Texture2D texture))
                return texture;

            return original;
        }
    }

}
