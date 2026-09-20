using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seasons
{
    /// <summary>World-scoped, immutable snow materials shared by all cap renderers.</summary>
    internal sealed class SeasonalSnowMaterials : IDisposable
    {
        internal const int LastLevel = 100;
        internal const string ShaderName = "Valheim/Snow Mesh";

        internal sealed class Levels
        {
            private readonly Material[] materials = new Material[LastLevel + 1];
            private bool disposed;

            internal Material Original => materials[0];
            internal int CreatedCount { get; private set; }

            internal Levels(Material original)
            {
                materials[0] = original;
            }

            internal Material Get(int level)
            {
                if (disposed)
                    throw new ObjectDisposedException(nameof(Levels));
                if ((uint)level > LastLevel)
                    throw new ArgumentOutOfRangeException(nameof(level));
                if (materials[level])
                    return materials[level];
                if (!Original)
                    return null;

                Material material = new Material(Original)
                {
                    name = $"{Original.name} [Seasons snow {level}]",
                    hideFlags = HideFlags.HideAndDontSave
                };
                material.SetFloat(WearNTear.s_snowLevel, level * 0.01f);
                materials[level] = material;
                CreatedCount++;
                return material;
            }

            internal void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                // Slot zero is owned by the game or another mod, never by this pool.
                for (int i = 1; i < materials.Length; ++i)
                {
                    if (materials[i])
                        UnityEngine.Object.Destroy(materials[i]);
                    materials[i] = null;
                }
                materials[0] = null;
                CreatedCount = 0;
            }
        }

        private readonly Dictionary<Material, Levels> pools = new Dictionary<Material, Levels>();

        internal int PoolCount => pools.Count;

        internal int CreatedMaterialCount
        {
            get
            {
                int count = 0;
                foreach (Levels levels in pools.Values)
                    count += levels.CreatedCount;
                return count;
            }
        }

        internal Levels GetLevels(Material original)
        {
            if (!original || !original.shader || original.shader.name != ShaderName)
                return null;
            if (!pools.TryGetValue(original, out Levels levels))
            {
                levels = new Levels(original);
                pools.Add(original, levels);
            }
            return levels;
        }

        public void Dispose()
        {
            foreach (Levels levels in pools.Values)
                levels.Dispose();
            pools.Clear();
        }
    }
}
