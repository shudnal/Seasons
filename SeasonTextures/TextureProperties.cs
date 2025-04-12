using System;
using UnityEngine;

namespace Seasons
{
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
