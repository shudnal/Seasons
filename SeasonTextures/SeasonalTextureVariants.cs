using System.Collections.Generic;
using static Seasons.Seasons;
using System.IO;
using System.Diagnostics;
using System.Collections;
using System.Threading;
using System;
using UnityEngine;

namespace Seasons
{
    public class SeasonalTextureVariants
    {
        public Dictionary<string, PrefabController> controllers = new Dictionary<string, PrefabController>();
        public Dictionary<int, TextureVariants> textures = new Dictionary<int, TextureVariants>();
        public uint revision = 0;
        internal string sourceSettings;
        internal readonly Dictionary<string, int> textureContextIds = new Dictionary<string, int>();
        internal readonly Dictionary<int, Tuple<TextureProperties, Color[], byte[]>> sourceTextures = new Dictionary<int, Tuple<TextureProperties, Color[], byte[]>>();
        private bool m_reloading;
        private bool m_rebuildSucceeded;
        private static readonly object s_diskLock = new object();
        private readonly Dictionary<string, CachedData.TextureData> m_cachedTextures = new Dictionary<string, CachedData.TextureData>();
        public bool IsUpdating => m_reloading || Controllers.TextureCachingController.InProcess;

        private void SetCacheCandidates(CachedData cachedData)
        {
            m_cachedTextures.Clear();
            if (!cachedData.Initialized())
                return;
            foreach (CachedData.TextureData texture in cachedData.textures.Values)
                if (!string.IsNullOrEmpty(texture.sourceFingerprint))
                    m_cachedTextures[texture.sourceFingerprint] = texture;
        }

        internal bool TryReuseTexture(string fingerprint, Texture source, byte[] originalPNG, out TextureVariants variants)
        {
            variants = null;
            if (!m_cachedTextures.TryGetValue(fingerprint, out CachedData.TextureData cached))
                return false;
            TextureVariants candidate = new TextureVariants(cached);
            if (!candidate.Initialized())
            {
                candidate.Dispose();
                m_cachedTextures.Remove(fingerprint);
                return false;
            }
            candidate.SetOriginalTexture(source);
            candidate.originalPNG = originalPNG;
            variants = candidate;
            return true;
        }

        internal void ReleaseCacheCandidates()
        {
            m_cachedTextures.Clear();
            sourceTextures.Clear();
        }

        public void Dispose()
        {
            foreach (TextureVariants variants in textures.Values)
                variants.Dispose();
            textures.Clear();
            controllers.Clear();
            textureContextIds.Clear();
            sourceSettings = null;
            ReleaseCacheCandidates();
        }

        public bool Initialize(bool force = false)
        {
            if (!force && Initialized())
                return true;

            if (m_reloading || Controllers.TextureCachingController.InProcess)
                return false;

            PrefabVariantController.instance?.RevertPrefabsState();
            ClutterVariantController.Instance?.RevertColors();
            Dispose();

            revision = SeasonalTexturePrefabCache.GetRevision();
            if (!force)
            {
                CachedData cachedData = new CachedData(revision);
                lock (s_diskLock)
                    cachedData.LoadFromDisk();
                SetCacheCandidates(cachedData);
            }

            // Rebuild the graph from live assets: persisted Unity instance IDs are not durable.
            // Generated texture data is reused only after matching its source fingerprint.
            if (!runTextureCachingSync.Value)
            {
                Controllers.TextureCachingController.StartCaching(this);
            }
            else
            {
                SeasonalTexturePrefabCache.SetCurrentTextureVariants(this);

                do
                {
                    if (Initialized())
                        Dispose();
                    StartCoroutineSync(SeasonalTexturePrefabCache.FillWithGameData());
                    StartCoroutineSync(SaveCacheOnDisk());
                }
                while (sourceSettings != null && SeasonalTexturePrefabCache.NeedsRefresh(this));
            }

            return Initialized();
        }

        public IEnumerator SaveCacheOnDisk()
        {
            if (Initialized())
            {
                CachedData cachedData = new CachedData(revision);

                cachedData.textures.Clear();
                foreach (KeyValuePair<int, TextureVariants> texVariants in textures)
                {
                    CachedData.TextureData texData = new CachedData.TextureData(texVariants.Value);
                    if (texData.Initialized())
                        cachedData.textures.Add(texVariants.Key, texData);
                }

                cachedData.controllers.Copy(controllers);
                Exception saveError = null;
                var internalThread = new Thread(() =>
                {
                    try
                    {
                        lock (s_diskLock)
                        {
                            if (Directory.Exists(cachedData.CacheDirectory()))
                                Directory.Delete(cachedData.CacheDirectory(), recursive: true);
                            cachedData.SaveOnDisk();
                        }
                    }
                    catch (Exception error)
                    {
                        saveError = error;
                    }
                }) { IsBackground = true };

                internalThread.Start();
                while (internalThread.IsAlive == true)
                {
                    yield return waitForFixedUpdate;
                }

                if (saveError != null)
                    LogWarning($"Unable to save seasonal texture cache:\n{saveError}");
                ApplyTexturesToGPU();
            }
        }

        public bool Initialized()
        {
            return controllers.Count > 0 && textures.Count > 0;
        }

        public void ApplyTexturesToGPU()
        {
            foreach (KeyValuePair<int, TextureVariants> texture in textures)
                texture.Value.ApplyTextures();
        }

        public IEnumerator ReloadCache() => RebuildCache(reuseDiskCache: true);

        public IEnumerator RebuildCache() => RebuildCache(reuseDiskCache: false);

        private IEnumerator RebuildCache(bool reuseDiskCache)
        {
            SeasonalTexturePrefabCache.RequestUpdate();
            if (m_reloading || Controllers.TextureCachingController.InProcess)
                yield break;
            m_reloading = true;
            ZoneSystem sourceZone = ZoneSystem.instance;
            try
            {
                do
                {
                    m_rebuildSucceeded = false;
                    yield return RebuildCacheInternal(reuseDiskCache);
                }
                while (m_rebuildSucceeded && sourceZone && sourceZone == ZoneSystem.instance
                    && SeasonalTexturePrefabCache.NeedsRefresh(this));
            }
            finally
            {
                m_reloading = false;
                if (sourceZone && sourceZone == ZoneSystem.instance)
                {
                    SeasonalTexturePrefabCache.SetCurrentTextureVariants(this);
                    if (m_rebuildSucceeded)
                    {
                        PrefabVariantController.UpdatePrefabColors();
                        ClutterVariantController.Instance?.UpdateColors();
                    }
                }
            }
        }

        private IEnumerator RebuildCacheInternal(bool reuseDiskCache)
        {
            ZoneSystem sourceZone = ZoneSystem.instance;
            SeasonalTextureVariants newTexturesVariants = new SeasonalTextureVariants();
            try
            {
                if (reuseDiskCache)
                {
                    CachedData cachedData = new CachedData(SeasonalTexturePrefabCache.GetRevision());
                    var reader = new Thread(() =>
                    {
                        lock (s_diskLock)
                            cachedData.LoadFromDisk();
                    }) { IsBackground = true };
                    reader.Start();
                    while (reader.IsAlive)
                        yield return waitForFixedUpdate;
                    if (!sourceZone || sourceZone != ZoneSystem.instance)
                        yield break;
                    newTexturesVariants.SetCacheCandidates(cachedData);
                }

                SeasonalTexturePrefabCache.SetCurrentTextureVariants(newTexturesVariants);

                PrefabVariantController.instance?.RevertPrefabsState();
                ClutterVariantController.Instance?.RevertColors();

                yield return waitForFixedUpdate;

                if (!sourceZone || sourceZone != ZoneSystem.instance)
                    yield break;

                yield return SeasonalTexturePrefabCache.FillWithGameData();

                if (!sourceZone || sourceZone != ZoneSystem.instance || newTexturesVariants.sourceSettings == null)
                    yield break;

                Stopwatch stopwatch = Stopwatch.StartNew();

                List<TextureVariants> oldTextures = new List<TextureVariants>(textures.Values);
                controllers.Clear();
                textures.Clear();
                revision = newTexturesVariants.revision;
                sourceSettings = newTexturesVariants.sourceSettings;

                controllers.Copy(newTexturesVariants.controllers);
                textures.Copy(newTexturesVariants.textures);
                // Ownership moves to this cache; finally only disposes untransferred results.
                newTexturesVariants.controllers.Clear();
                newTexturesVariants.textures.Clear();

                SeasonalTexturePrefabCache.SetCurrentTextureVariants(this);

                try
                {
                    ClutterVariantController.Reinitialize();
                    PrefabVariantController.ReinitializePrefabVariants();
                }
                finally
                {
                    foreach (TextureVariants oldTexture in oldTextures)
                        oldTexture.Dispose();
                }

                yield return SaveCacheOnDisk();

                yield return waitForFixedUpdate;

                if (!sourceZone || sourceZone != ZoneSystem.instance)
                    yield break;
                m_rebuildSucceeded = true;
                LogInfo($"Colors reinitialized in {stopwatch.Elapsed.TotalSeconds,-4:F2} seconds");
                LogInfo("Cache rebuild ended");
            }
            finally
            {
                newTexturesVariants.Dispose();
                if (sourceZone && sourceZone == ZoneSystem.instance)
                    SeasonalTexturePrefabCache.SetCurrentTextureVariants(this);
            }
        }
    }

}
