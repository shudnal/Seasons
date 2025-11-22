using System.Collections.Generic;
using static Seasons.Seasons;
using System.IO;
using System.Diagnostics;
using System.Collections;
using System.Threading;

namespace Seasons
{
    public class SeasonalTextureVariants
    {
        public Dictionary<string, PrefabController> controllers = new Dictionary<string, PrefabController>();
        public Dictionary<int, TextureVariants> textures = new Dictionary<int, TextureVariants>();
        public uint revision = 0;

        public bool Initialize(bool force = false)
        {
            if (!force && Initialized())
                return true;

            controllers.Clear();
            textures.Clear();

            revision = SeasonalTexturePrefabCache.GetRevision();
            CachedData cachedData = new CachedData(revision);

            if (force && Directory.Exists(cachedData.CacheDirectory()))
                Directory.Delete(cachedData.CacheDirectory(), recursive: true);

            cachedData.LoadFromDisk();

            if (cachedData.Initialized())
            {
                controllers.Copy(cachedData.controllers);

                foreach (KeyValuePair<int, CachedData.TextureData> texData in cachedData.textures)
                {
                    if (textures.ContainsKey(texData.Key))
                        continue;

                    TextureVariants texVariants = new TextureVariants(texData.Value);

                    if (!texVariants.Initialized())
                        continue;

                    textures.Add(texData.Key, texVariants);
                }

                LogInfo($"Loaded from cache revision:{revision} controllers:{controllers.Count} textures:{textures.Count}");
            }
            else if (!runTextureCachingSync.Value)
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

                cachedData.textures.Clear();
                foreach (KeyValuePair<int, TextureVariants> texVariants in textures)
                {
                    CachedData.TextureData texData = new CachedData.TextureData(texVariants.Value);
                    if (texData.Initialized())
                        cachedData.textures.Add(texVariants.Key, texData);
                }

                var internalThread = new Thread(() =>
                {
                    cachedData.controllers.Copy(controllers);

                    if (Directory.Exists(cachedData.CacheDirectory()))
                        Directory.Delete(cachedData.CacheDirectory(), recursive: true);

                    cachedData.SaveOnDisk();
                });

                internalThread.Start();
                while (internalThread.IsAlive == true)
                {
                    yield return waitForFixedUpdate;
                }

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
            Stopwatch stopwatch = Stopwatch.StartNew();

            CachedData cachedData = new CachedData(SeasonalTexturePrefabCache.GetRevision());

            var internalThread = new Thread(() =>
            {
                cachedData.LoadFromDisk();
            });

            internalThread.Start();
            while (internalThread.IsAlive == true)
            {
                yield return waitForFixedUpdate;
            }

            if (cachedData.Initialized())
            {
                revision = cachedData.revision;

                foreach (KeyValuePair<int, CachedData.TextureData> texData in cachedData.textures)
                {
                    if (textures.ContainsKey(texData.Key))
                        continue;

                    TextureVariants texVariants = new TextureVariants(texData.Value);

                    if (!texVariants.Initialized())
                        continue;

                    textures.Add(texData.Key, texVariants);
                }

                internalThread = new Thread(() =>
                {
                    controllers.Copy(cachedData.controllers);

                });

                internalThread.Start();
                while (internalThread.IsAlive == true)
                {
                    yield return waitForFixedUpdate;
                }

                LogInfo($"Loaded from cache revision:{revision} controllers:{controllers.Count} textures:{textures.Count} in {stopwatch.Elapsed.TotalSeconds,-4:F2} seconds");

                stopwatch.Restart();

                ClutterVariantController.Reinitialize();
                PrefabVariantController.ReinitializePrefabVariants();

                yield return waitForFixedUpdate;

                PrefabVariantController.UpdatePrefabColors();
                ClutterVariantController.Instance.UpdateColors();

                LogInfo($"Colors reinitialized in {stopwatch.Elapsed.TotalSeconds,-4:F2} seconds");
            }
            else
            {
                yield return RebuildCache();
            }
        }

        public IEnumerator RebuildCache()
        {
            SeasonalTextureVariants newTexturesVariants = new SeasonalTextureVariants();

            SeasonalTexturePrefabCache.SetCurrentTextureVariants(newTexturesVariants);

            PrefabVariantController.instance?.RevertPrefabsState();
            ClutterVariantController.Instance?.RevertColors();

            yield return waitForFixedUpdate;

            yield return SeasonalTexturePrefabCache.FillWithGameData();

            if (newTexturesVariants.Initialized())
            {
                Stopwatch stopwatch = Stopwatch.StartNew();

                controllers.Clear();
                textures.Clear();
                revision = newTexturesVariants.revision;

                var internalThread = new Thread(() =>
                {
                    controllers.Copy(newTexturesVariants.controllers);
                    textures.Copy(newTexturesVariants.textures);
                });

                internalThread.Start();
                while (internalThread.IsAlive == true)
                {
                    yield return waitForFixedUpdate;
                }

                yield return SaveCacheOnDisk();

                SeasonalTexturePrefabCache.SetCurrentTextureVariants(this);

                ClutterVariantController.Reinitialize();
                PrefabVariantController.ReinitializePrefabVariants();

                yield return waitForFixedUpdate;

                LogInfo($"Colors reinitialized in {stopwatch.Elapsed.TotalSeconds,-4:F2} seconds");
            }

            yield return waitForFixedUpdate;

            SeasonalTexturePrefabCache.SetCurrentTextureVariants(this);

            PrefabVariantController.UpdatePrefabColors();
            ClutterVariantController.Instance?.UpdateColors();

            LogInfo($"Cache rebuild ended");
        }
    }

}
