using System;
using UnityEngine;

namespace Seasons
{
    [Serializable]
    public class TextureProperties
    {
        public TextureProperties() { }

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

        internal int RawDataLength()
        {
            if (width <= 0 || height <= 0 || mipmapCount <= 0 || format != TextureFormat.ARGB32)
                throw new System.IO.InvalidDataException("Invalid cached texture properties.");
            long size = 0;
            int w = width, h = height;
            for (int mip = 0; mip < mipmapCount; mip++)
            {
                size = checked(size + (long)w * h * 4);
                if (mip + 1 < mipmapCount && w == 1 && h == 1)
                    throw new System.IO.InvalidDataException("Invalid cached mip count.");
                w = Math.Max(1, w / 2);
                h = Math.Max(1, h / 2);
            }
            return checked((int)size);
        }

        public Texture2D CreateTexture()
        {
            return new Texture2D(width, height, format, mipmapCount, false)
            {
                filterMode = filterMode,
                anisoLevel = anisoLevel,
                mipMapBias = mipMapBias,
                wrapMode = wrapMode
            };
        }
    }

}
