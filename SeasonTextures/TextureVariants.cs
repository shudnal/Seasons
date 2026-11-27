using System;
using System.Collections.Generic;
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
        public TextureProperties properties;
        // The runtime holds a single season. During a transition the new set is
        // prepared separately before these dictionaries are exchanged.
        public Dictionary<Season, Dictionary<int, Texture2D>> seasons = new Dictionary<Season, Dictionary<int, Texture2D>>();

        private const int RecolorPaletteBytes = 8 * 4 * sizeof(float);
        private readonly CachedData m_cache;
        private readonly int m_textureId;
        private Texture2D m_recolorMask;
        private Vector4[] m_recolorPalette;
        private bool m_recolorUnavailable;

        // A disabled or unreadable mask must not change generated-base eligibility.
        internal bool HasRecolorData => m_cache != null && m_cache.HasRecolor(m_textureId);

        internal static long RecolorDataLength(TextureProperties properties) =>
            RecolorPaletteBytes + ((long)properties.width * properties.height + 7) / 8;

        internal static byte[] BuildRecolorData(byte[] mask, ColorVariant[] variants)
        {
            byte[] data = new byte[checked(RecolorPaletteBytes + mask.Length)];
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
            Buffer.BlockCopy(mask, 0, data, RecolorPaletteBytes, mask.Length);
            return data;
        }

        internal bool TryGetRecolor(out Texture mask, Vector4[] palette)
        {
            mask = null;
            if (!HasRecolorData || m_recolorUnavailable)
                return false;

            if (!m_recolorMask)
            {
                Texture2D texture = null;
                try
                {
                    byte[] data = m_cache.ReadRecolor(m_textureId);
                    if (data.LongLength != RecolorDataLength(properties))
                        throw new InvalidDataException("Invalid seasonal recolor mask length.");

                    var colors = new Vector4[8];
                    using (var reader = new BinaryReader(new MemoryStream(data, writable: false)))
                        for (int i = 0; i < colors.Length; i++)
                            colors[i] = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

                    if (colors[4].x == 0f && colors[5].x == 0f && colors[6].x == 0f)
                    {
                        m_recolorUnavailable = true;
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
                    m_recolorUnavailable = true;
                    LogWarning($"Unable to load seasonal recolor mask: {error.Message}");
                    return false;
                }
            }

            Array.Copy(m_recolorPalette, palette, 8);
            mask = m_recolorMask;
            return true;
        }

        internal TextureVariants(CachedData cache, int id, CachedData.TextureData data)
        {
            m_cache = cache;
            m_textureId = id;
            originalName = data.name;
            properties = data.properties;
        }

        public TextureVariants(Texture texture)
        {
            SetOriginalTexture(texture);
        }

        public void SetOriginalTexture(Texture texture)
        {
            if (texture is not Texture2D source)
                return;
            original = source;
            // Keep the metadata describing the cached bytes, even if another mod
            // has replaced the original texture since this cache was generated.
            properties ??= new TextureProperties(source);
            originalName = original.name;
        }

        internal void LoadVariant(Season season, CachedData.VariantBlock block)
        {
            Texture2D texture = properties.CreateTexture();
            try
            {
                if (block.Raw)
                {
                    texture.LoadRawTextureData(block.Data);
                    texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                }
                else if (!texture.LoadImage(block.Data, true) ||
                    texture.width != properties.width || texture.height != properties.height)
                    throw new InvalidDataException($"Invalid PNG for seasonal texture {m_textureId}.");

                if (!seasons.TryGetValue(season, out Dictionary<int, Texture2D> variants))
                    seasons.Add(season, variants = new Dictionary<int, Texture2D>());
                variants.Add(block.Variant, texture);
            }
            catch
            {
                Object.Destroy(texture);
                throw;
            }
        }

        internal bool Initialized(Season season)
        {
            if (properties == null || !seasons.TryGetValue(season, out var variants) ||
                variants.Count != seasonColorVariants)
                return false;
            for (int i = 0; i < seasonColorVariants; i++)
                if (!variants.TryGetValue(i, out Texture2D texture) || !texture)
                    return false;
            return true;
        }

        internal void ExchangeSeason(TextureVariants replacement)
        {
            (seasons, replacement.seasons) = (replacement.seasons, seasons);
        }

        internal void ReleaseRecolor()
        {
            if (m_recolorMask)
                Object.Destroy(m_recolorMask);
            m_recolorMask = null;
            m_recolorPalette = null;
        }

        public void Dispose()
        {
            ReleaseRecolor();
            foreach (var variants in seasons.Values)
                foreach (Texture2D texture in variants.Values)
                    if (texture)
                        Object.Destroy(texture);
            seasons.Clear();
        }

        public bool HaveOriginalTexture() => original;

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
