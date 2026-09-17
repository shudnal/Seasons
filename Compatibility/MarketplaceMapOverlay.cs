using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Seasons.Compatibility
{
    // All rendering and all calls into Marketplace stay on the Unity thread. Requests
    // are advanced in bounded batches and only a complete, current request is applied.
    internal sealed class MarketplaceMapOverlay : IDisposable
    {
        internal enum Shape
        {
            Circle,
            Square,
            Rectangle
        }

        internal sealed class Territory
        {
            public Shape AreaShape;
            public float X;
            public float Y;
            public float Width;
            public float Height;
            public float Radius;
            public int Priority;
            public bool ShowExternalWater;
            public bool RevealOnMap;
            public Color32 Color;
            public Func<Vector2, Color32> Gradient;
        }

        private readonly int size;
        private readonly float pixelSize;
        private readonly IEnumerator drawing;
        private readonly Stopwatch stopwatch = new Stopwatch();

        internal readonly Color32[] MapColors;
        internal readonly Color[] HeightColors;
        internal BitArray Revealed { get; private set; }

        internal MarketplaceMapOverlay(Color32[] mapColors, Color[] heightColors, int size,
            float pixelSize, IEnumerable<Territory> territories)
        {
            this.size = size;
            this.pixelSize = pixelSize;
            MapColors = (Color32[])mapColors.Clone();
            HeightColors = (Color[])heightColors.Clone();
            drawing = Draw(territories);
        }

        // Returns true when every territory has been processed. Texture upload is
        // deliberately separate so the caller can recheck the map lifetime first.
        internal bool Advance()
        {
            stopwatch.Restart();
            do
            {
                if (!drawing.MoveNext())
                    return true;
            }
            while (stopwatch.Elapsed.TotalMilliseconds < 2.0);
            return false;
        }

        private IEnumerator Draw(IEnumerable<Territory> territories)
        {
            int center = size / 2;
            float halfPixel = pixelSize / 2f;
            int work = 0;
            foreach (Territory territory in territories)
            {
                float minY = territory.AreaShape == Shape.Rectangle ? territory.Y : territory.Y - territory.Radius;
                float maxY = territory.AreaShape == Shape.Rectangle ? territory.Y + territory.Height : territory.Y + territory.Radius;
                int firstRow = WorldToPixel(minY, center, halfPixel);
                int lastRow = WorldToPixel(maxY, center, halfPixel);
                for (int y = firstRow; y < lastRow; ++y)
                {
                    float minX;
                    float maxX;
                    if (territory.AreaShape == Shape.Rectangle)
                    {
                        minX = territory.X;
                        maxX = territory.X + territory.Width;
                    }
                    else if (territory.AreaShape == Shape.Circle)
                    {
                        float dy = (y - center) * pixelSize - halfPixel - territory.Y;
                        if (Mathf.Abs(dy) > territory.Radius)
                            continue;
                        float halfWidth = Mathf.Sqrt(Mathf.Max(0f, territory.Radius * territory.Radius - dy * dy));
                        minX = territory.X - halfWidth;
                        maxX = territory.X + halfWidth;
                    }
                    else
                    {
                        minX = territory.X - territory.Radius;
                        maxX = territory.X + territory.Radius;
                    }

                    int firstColumn = WorldToPixel(minX, center, halfPixel);
                    int lastColumn = WorldToPixel(maxX, center, halfPixel);
                    for (int x = firstColumn; x < lastColumn; ++x)
                    {
                        int index = y * size + x;
                        MapColors[index] = territory.Gradient == null ? territory.Color
                            : territory.Gradient(new Vector2((x - center) * pixelSize, (y - center) * pixelSize));
                        if (territory.ShowExternalWater)
                            HeightColors[index] = new Color(Mathf.Clamp(HeightColors[index].r, 29f, 89f), 0f, 0f);
                        if (territory.RevealOnMap)
                        {
                            Revealed ??= new BitArray(MapColors.Length);
                            Revealed[index] = true;
                        }
                        if (++work >= 1024)
                        {
                            work = 0;
                            yield return null;
                        }
                    }
                    // Empty/clipped rows must also yield, including off-map territories.
                    yield return null;
                }
                yield return null;
            }
        }

        private int WorldToPixel(float coordinate, int center, float halfPixel)
        {
            float pixel = (coordinate + halfPixel) / pixelSize + center;
            // Clamp before converting to an integer: custom territories can be outside
            // the map, and invalid bounds must never wrap into another pixel row.
            return Mathf.RoundToInt(Mathf.Clamp(pixel, 0f, size));
        }

        internal void Apply(Minimap minimap, BitArray previouslyRevealed)
        {
            Color32[] fog = null;
            if (Revealed != null || previouslyRevealed != null)
            {
                // Read the current fog at commit time, not a snapshot from a previous
                // frame. Never replace m_explored or m_exploredOthers: the player may
                // have explored new pixels while this redraw was being prepared.
                fog = minimap.m_fogTexture.GetPixels32();
                for (int i = 0; i < fog.Length; ++i)
                {
                    if (Revealed != null && Revealed[i])
                        fog[i].r = 0;
                    else if (previouslyRevealed != null && previouslyRevealed[i])
                        fog[i].r = minimap.m_explored[i] ? (byte)0 : byte.MaxValue;
                }
            }

            minimap.m_mapTexture.SetPixels32(MapColors);
            minimap.m_heightTexture.SetPixels(HeightColors);
            if (fog != null)
                minimap.m_fogTexture.SetPixels32(fog);
            minimap.m_mapTexture.Apply(updateMipmaps: false);
            minimap.m_heightTexture.Apply(updateMipmaps: false);
            if (fog != null)
                minimap.m_fogTexture.Apply(updateMipmaps: false);
        }

        public void Dispose()
        {
            (drawing as IDisposable)?.Dispose();
        }
    }
}
