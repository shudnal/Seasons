using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    public class SeasonalTextureVariants
    {
        public Dictionary<string, PrefabController> controllers = new Dictionary<string, PrefabController>();
        public Dictionary<int, TextureVariants> textures = new Dictionary<int, TextureVariants>();
        public uint revision = 0;

        private bool m_reloading;
        private bool m_rebuildSucceeded;
        private static readonly object s_diskLock = new object();

        public bool IsUpdating => m_reloading || Controllers.TextureCachingController.InProcess;

        public void Dispose()
        {
            foreach (TextureVariants variants in textures.Values)
                variants.Dispose();
            textures.Clear();
            controllers.Clear();
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
            CachedData cachedData = new CachedData(revision);

            if (force)
            {
                lock (s_diskLock)
                    if (Directory.Exists(cachedData.CacheDirectory()))
                        Directory.Delete(cachedData.CacheDirectory(), recursive: true);
            }
            else
            {
                lock (s_diskLock)
                    cachedData.LoadFromDisk();
            }

            if (cachedData.Initialized())
            {
                controllers.Copy(cachedData.controllers);

                foreach (KeyValuePair<int, CachedData.TextureData> texData in cachedData.textures)
                {
                    TextureVariants texVariants = new TextureVariants(texData.Value);
                    if (!texVariants.Initialized())
                    {
                        texVariants.Dispose();
                        continue;
                    }
                    textures.Add(texData.Key, texVariants);
                }

                LogInfo($"Loaded from cache revision:{revision} controllers:{controllers.Count} textures:{textures.Count}");
                return Initialized();
            }

            if (!runTextureCachingSync.Value)
            {
                Controllers.TextureCachingController.StartCaching(this);
            }
            else
            {
                SeasonalTexturePrefabCache.SetCurrentTextureVariants(this);
                StartCoroutineSync(SeasonalTexturePrefabCache.FillWithGameData());
                StartCoroutineSync(SaveCacheOnDisk());
            }

            return Initialized();
        }

        public IEnumerator SaveCacheOnDisk()
        {
            if (Initialized())
            {
                CachedData cachedData = new CachedData(revision);

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
                while (internalThread.IsAlive)
                    yield return waitForFixedUpdate;

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

        public IEnumerator ReloadCache()
        {
            if (m_reloading || Controllers.TextureCachingController.InProcess)
                yield break;

            m_reloading = true;
            ZoneSystem sourceZone = ZoneSystem.instance;
            try
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

                if (!cachedData.Initialized())
                {
                    m_reloading = false;
                    yield return RebuildCache();
                    yield break;
                }

                Stopwatch stopwatch = Stopwatch.StartNew();
                List<TextureVariants> oldTextures = new List<TextureVariants>(textures.Values);
                var loadedTextures = new Dictionary<int, TextureVariants>();

                foreach (KeyValuePair<int, CachedData.TextureData> texData in cachedData.textures)
                {
                    TextureVariants variants = new TextureVariants(texData.Value);
                    if (variants.Initialized())
                        loadedTextures.Add(texData.Key, variants);
                    else
                        variants.Dispose();
                }

                if (loadedTextures.Count == 0)
                {
                    m_reloading = false;
                    yield return RebuildCache();
                    yield break;
                }

                PrefabVariantController.instance?.RevertPrefabsState();
                ClutterVariantController.Instance?.RevertColors();

                controllers.Clear();
                textures.Clear();
                revision = cachedData.revision;
                controllers.Copy(cachedData.controllers);
                textures.Copy(loadedTextures);

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

                yield return waitForFixedUpdate;
                PrefabVariantController.UpdatePrefabColors();
                ClutterVariantController.Instance?.UpdateColors();
                LogInfo($"Loaded from cache revision:{revision} controllers:{controllers.Count} textures:{textures.Count} in {stopwatch.Elapsed.TotalSeconds,-4:F2} seconds");
            }
            finally
            {
                m_reloading = false;
                if (sourceZone && sourceZone == ZoneSystem.instance)
                    SeasonalTexturePrefabCache.SetCurrentTextureVariants(this);
            }
        }

        public IEnumerator RebuildCache()
        {
            if (m_reloading || Controllers.TextureCachingController.InProcess)
                yield break;

            m_reloading = true;
            m_rebuildSucceeded = false;
            ZoneSystem sourceZone = ZoneSystem.instance;
            SeasonalTextureVariants newTexturesVariants = new SeasonalTextureVariants();
            try
            {
                SeasonalTexturePrefabCache.SetCurrentTextureVariants(newTexturesVariants);

                PrefabVariantController.instance?.RevertPrefabsState();
                ClutterVariantController.Instance?.RevertColors();

                yield return waitForFixedUpdate;

                if (!sourceZone || sourceZone != ZoneSystem.instance)
                    yield break;

                yield return SeasonalTexturePrefabCache.FillWithGameData();

                if (!sourceZone || sourceZone != ZoneSystem.instance || !newTexturesVariants.Initialized())
                    yield break;

                Stopwatch stopwatch = Stopwatch.StartNew();
                List<TextureVariants> oldTextures = new List<TextureVariants>(textures.Values);
                controllers.Clear();
                textures.Clear();
                revision = newTexturesVariants.revision;
                controllers.Copy(newTexturesVariants.controllers);
                textures.Copy(newTexturesVariants.textures);
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
                PrefabVariantController.UpdatePrefabColors();
                ClutterVariantController.Instance?.UpdateColors();
                LogInfo($"Colors reinitialized in {stopwatch.Elapsed.TotalSeconds,-4:F2} seconds");
                LogInfo("Cache rebuild ended");
            }
            finally
            {
                newTexturesVariants.Dispose();
                m_reloading = false;
                if (sourceZone && sourceZone == ZoneSystem.instance)
                    SeasonalTexturePrefabCache.SetCurrentTextureVariants(this);
            }
        }
    }
}
