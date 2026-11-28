using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    public class SeasonalTextureVariants
    {
        public Dictionary<string, PrefabController> controllers = new Dictionary<string, PrefabController>();
        public Dictionary<int, TextureVariants> textures = new Dictionary<int, TextureVariants>();
        public uint revision;

        private CachedData m_cache;
        private Season? m_loadedSeason;
        private bool m_running;
        private bool m_updating;
        private bool m_disposed;
        private bool m_reloadRequested;
        private bool m_forceRequested;
        private bool m_waitingForFade;
        private SeasonalTextureVariants m_buildTarget;
        private CachedData.Writer m_pendingWriter;
        private SeasonLoad m_activeLoad;
        private Task<CachedData> m_metadataTask;

        internal CachedData.Writer BuildWriter { get; private set; }

        public bool IsUpdating => m_updating || Controllers.TextureCachingController.InProcess;
        internal Season AppearanceSeason => m_loadedSeason ?? DesiredSeason();
        internal bool IsSeasonLoading => m_running && m_buildTarget == null && m_loadedSeason != DesiredSeason();

        private static Season DesiredSeason()
        {
            if (SeasonState.WorldInitialized && SeasonState.IsActive)
                return seasonState.GetCurrentSeason();
            var synced = SeasonState.GetSyncedCurrentSeasonDay();
            return synced.Item2 > 0 ? synced.Item1 : seasonState.GetCurrentSeason();
        }

        private bool WorldAlive(ZoneSystem zone) =>
            !m_disposed && zone && zone == ZoneSystem.instance && ReferenceEquals(this, texturesVariants);

        public bool Initialized() => m_cache != null && m_loadedSeason.HasValue &&
            controllers.Count > 0 && textures.Count > 0;

        internal void BeginBuild()
        {
            BuildWriter = new CachedData.Writer(revision, cacheStorageFormat.Value);
        }

        public void Dispose()
        {
            if (m_disposed)
                return;
            m_disposed = true;
            if (ReferenceEquals(this, texturesVariants))
                VegetationApi.InvalidateAppearance();

            // Cancel and join the bounded reader before releasing its buffers or
            // deleting staging files. The worker performs only disk/CPU work.
            m_activeLoad?.Dispose();
            m_activeLoad = null;
            if (m_metadataTask != null)
                try { m_metadataTask.GetAwaiter().GetResult(); }
                catch (Exception) { }
            m_metadataTask = null;
            m_buildTarget?.Dispose();
            m_buildTarget = null;
            m_pendingWriter?.Dispose();
            m_pendingWriter = null;
            BuildWriter?.Dispose();
            BuildWriter = null;

            foreach (TextureVariants variants in textures.Values)
                variants.Dispose();
            textures.Clear();
            controllers.Clear();
            m_cache = null;
            m_loadedSeason = null;
            m_updating = false;
        }

        public bool Initialize(bool force = false)
        {
            if (!force && Initialized())
                return true;
            if (m_running || Controllers.TextureCachingController.InProcess || m_disposed)
                return false;

            if (!runTextureCachingSync.Value)
            {
                Controllers.TextureCachingController.StartCaching(this, force);
                return false;
            }

            StartCoroutineSync(InitializeCache(force, synchronous: true));
            return Initialized();
        }

        internal IEnumerator InitializeCache(bool force, bool synchronous = false)
        {
            m_reloadRequested = true;
            m_forceRequested |= force;
            yield return ProcessChanges(initial: true, synchronous: synchronous);
        }

        internal void PrepareSeasonChange()
        {
            if (!Initialized() || m_loadedSeason == DesiredSeason())
                return;
            m_waitingForFade = true;
            RequestSeason();
        }

        internal void CompleteSeasonChange()
        {
            m_waitingForFade = false;
            RequestSeason();
        }

        internal void RequestSeason()
        {
            if (m_disposed || m_running || Controllers.TextureCachingController.InProcess ||
                (!m_reloadRequested && !m_forceRequested && (!Initialized() || m_loadedSeason == DesiredSeason())))
                return;
            instance.StartCoroutine(ProcessChanges(initial: false, synchronous: false));
        }

        public IEnumerator ReloadCache()
        {
            if (!UseTextureControllers() || m_disposed)
                yield break;
            m_reloadRequested = true;
            if (!m_running && !Controllers.TextureCachingController.InProcess)
                yield return ProcessChanges(initial: false, synchronous: false);
        }

        public IEnumerator RebuildCache()
        {
            if (!UseTextureControllers() || m_disposed)
                yield break;
            m_forceRequested = true;
            if (!m_running && !Controllers.TextureCachingController.InProcess)
                yield return ProcessChanges(initial: false, synchronous: false);
        }

        private sealed class CacheResult
        {
            internal CachedData Cache;
            internal CachedData.Writer Writer;
            internal Exception Error;
        }

        private IEnumerator ReadCache(CacheResult result, uint wantedRevision, CacheFormat format, bool synchronous)
        {
            Task<CachedData> task = Task.Run(() =>
                Directory.Exists(CachedData.CacheDirectory(wantedRevision))
                    ? CachedData.Open(wantedRevision, format) : null);
            m_metadataTask = task;
            try
            {
                if (!synchronous)
                    while (!task.IsCompleted && !m_disposed)
                        yield return null;
                if (!m_disposed)
                    try { result.Cache = task.GetAwaiter().GetResult(); }
                    catch (Exception error) { result.Error = error; }
            }
            finally
            {
                if (ReferenceEquals(m_metadataTask, task))
                    m_metadataTask = null;
            }
        }

        private IEnumerator BuildCache(CacheResult result, ZoneSystem zone)
        {
            var target = new SeasonalTextureVariants();
            m_buildTarget = target;
            SeasonalTexturePrefabCache.SetCurrentTextureVariants(target);
            IEnumerator routine = SeasonalTexturePrefabCache.FillWithGameData();
            try
            {
                // FillWithGameData flattens its nested iterators and disposes an
                // incomplete build. Catch disk errors at this boundary as well.
                while (WorldAlive(zone) && !m_reloadRequested && !m_forceRequested)
                {
                    bool more;
                    try { more = routine.MoveNext(); }
                    catch (Exception error)
                    {
                        result.Error = error;
                        break;
                    }
                    if (!more)
                        break;
                    yield return routine.Current;
                }

                if (!WorldAlive(zone) || result.Error != null || m_reloadRequested || m_forceRequested)
                    yield break;

                try
                {
                    if (target.BuildWriter == null)
                        throw new InvalidDataException("Incomplete seasonal cache generation.");
                    var metadata = target.textures.ToDictionary(entry => entry.Key, entry => new CachedData.TextureData
                    {
                        name = entry.Value.originalName,
                        properties = entry.Value.properties
                    });
                    result.Cache = target.BuildWriter.Finish(target.controllers, metadata);
                    result.Writer = target.BuildWriter;
                    m_pendingWriter = result.Writer;
                    target.BuildWriter = null;
                }
                catch (Exception error)
                {
                    result.Error = error;
                }
            }
            finally
            {
                (routine as IDisposable)?.Dispose();
                target.Dispose();
                if (ReferenceEquals(m_buildTarget, target))
                    m_buildTarget = null;
                if (WorldAlive(zone))
                    SeasonalTexturePrefabCache.SetCurrentTextureVariants(this);
            }
        }

        // At most one decompressed block is waiting for upload. A second block
        // may be decoded while the main thread uploads the previous one.
        private sealed class SeasonLoad : IDisposable
        {
            internal readonly Dictionary<int, TextureVariants> Textures;
            private readonly BlockingCollection<CachedData.VariantBlock> m_blocks =
                new BlockingCollection<CachedData.VariantBlock>(1);
            private readonly SemaphoreSlim m_slots = new SemaphoreSlim(1);
            private readonly CancellationTokenSource m_cancel = new CancellationTokenSource();
            private readonly Task m_reader;
            private bool m_disposed;

            internal SeasonLoad(CachedData source, Season season)
            {
                Textures = source.textures.ToDictionary(entry => entry.Key,
                    entry => new TextureVariants(source, entry.Key, entry.Value));
                m_reader = Task.Run(() =>
                {
                    try
                    {
                        using var reader = source.ReadSeason(season).GetEnumerator();
                        while (true)
                        {
                            m_slots.Wait(m_cancel.Token);
                            if (!reader.MoveNext())
                                break;
                            m_blocks.Add(reader.Current, m_cancel.Token);
                        }
                    }
                    finally
                    {
                        m_blocks.CompleteAdding();
                    }
                });
            }

            internal bool Completed => m_blocks.IsCompleted;

            internal bool UploadNext(Season season, int waitMilliseconds)
            {
                CachedData.VariantBlock block;
                if (!m_blocks.TryTake(out block, waitMilliseconds))
                    return false;
                m_slots.Release();
                Textures[block.TextureId].LoadVariant(season, block);
                return true;
            }

            internal void Validate(Season season)
            {
                m_reader.GetAwaiter().GetResult();
                if (Textures.Values.Any(texture => !texture.Initialized(season)))
                    throw new InvalidDataException($"Incomplete {season} seasonal textures.");
            }

            public void Dispose()
            {
                if (m_disposed)
                    return;
                m_disposed = true;
                m_cancel.Cancel();
                try { m_reader.GetAwaiter().GetResult(); }
                catch (Exception) { } // The reader error is reported by Validate.
                m_blocks.Dispose();
                m_slots.Dispose();
                m_cancel.Dispose();
                foreach (TextureVariants texture in Textures.Values)
                    texture.Dispose();
                Textures.Clear();
            }
        }

        private IEnumerator PumpSeason(SeasonLoad load, Season season, CacheResult result, ZoneSystem zone, bool synchronous)
        {
            var frame = Stopwatch.StartNew();
            while (WorldAlive(zone) && season == DesiredSeason() &&
                !m_reloadRequested && !m_forceRequested && !load.Completed)
            {
                bool uploaded = false;
                // Allow the producer a short wait within the upload budget;
                // otherwise small textures could be limited to one per frame.
                int wait = synchronous ? 10 : Math.Max(0, Math.Min(2, 8 - (int)frame.ElapsedMilliseconds));
                try { uploaded = load.UploadNext(season, wait); }
                catch (Exception error) { result.Error = error; }
                if (result.Error != null)
                    yield break;

                if (!synchronous && (!uploaded || frame.ElapsedMilliseconds >= 8))
                {
                    yield return null;
                    frame.Restart();
                }
            }

            if (WorldAlive(zone) && season == DesiredSeason() &&
                !m_reloadRequested && !m_forceRequested)
                try { load.Validate(season); }
                catch (Exception error) { result.Error = error; }
        }

        private IEnumerator ProcessChanges(bool initial, bool synchronous)
        {
            if (m_running || m_disposed)
                yield break;
            m_running = true;
            ZoneSystem zone = ZoneSystem.instance;
            try
            {
                while (WorldAlive(zone))
                {
                    bool force = m_forceRequested;
                    bool refresh = force || m_reloadRequested || !Initialized();
                    m_forceRequested = m_reloadRequested = false;
                    if (!refresh && m_loadedSeason == DesiredSeason())
                        yield break;

                    m_updating = true;
                    var result = new CacheResult();
                    bool committed = false;
                    bool rebuilt = false;
                    try
                    {
                        if (!refresh)
                            result.Cache = m_cache;
                        else if (!force)
                            yield return ReadCache(result, SeasonalTexturePrefabCache.GetRevision(), cacheStorageFormat.Value, synchronous);

                        while (WorldAlive(zone) && !m_forceRequested && !m_reloadRequested)
                        {
                            if (result.Cache == null)
                            {
                                if (rebuilt)
                                    break;
                                if (result.Error != null)
                                    LogWarning($"Unable to read seasonal cache, rebuilding: {result.Error.Message}");
                                result.Error = null;
                                rebuilt = true;
                                yield return BuildCache(result, zone);
                                if (!WorldAlive(zone) || result.Cache == null || m_forceRequested || m_reloadRequested)
                                    break;
                            }

                            Season season = DesiredSeason();
                            // A rapid override can return to the still-resident season.
                            if (ReferenceEquals(result.Cache, m_cache) && m_loadedSeason == season)
                            {
                                m_updating = false;
                                RefreshAppearance(newCache: false, initial: initial);
                                committed = true;
                                break;
                            }
                            var load = new SeasonLoad(result.Cache, season);
                            m_activeLoad = load;
                            try
                            {
                                yield return PumpSeason(load, season, result, zone, synchronous);
                                if (!WorldAlive(zone) || m_forceRequested || m_reloadRequested)
                                    break;

                                if (result.Error != null)
                                {
                                    if (rebuilt)
                                        break;
                                    result.Cache = null;
                                    continue;
                                }
                                if (season != DesiredSeason())
                                    continue;

                                // Keep the existing fade: prepare during fade-out
                                // and switch after the season-change callback.
                                while (!initial && m_waitingForFade && m_loadedSeason != season &&
                                    WorldAlive(zone) && season == DesiredSeason() && !m_forceRequested && !m_reloadRequested)
                                    yield return null;
                                if (!WorldAlive(zone) || m_forceRequested || m_reloadRequested)
                                    break;
                                if (season != DesiredSeason())
                                    continue;

                                // Initial synchronization or cache settings may have
                                // changed while a staged revision was being generated.
                                if ((refresh || rebuilt) && SeasonalTexturePrefabCache.GetRevision() != result.Cache.revision)
                                {
                                    m_reloadRequested = true;
                                    break;
                                }

                                try
                                {
                                    result.Writer?.Publish(result.Cache);
                                    CommitAppearance(result.Cache, season, load.Textures, initial);
                                    committed = true;
                                }
                                catch (Exception error)
                                {
                                    result.Error = error;
                                }
                                break;
                            }
                            finally
                            {
                                load.Dispose();
                                if (ReferenceEquals(m_activeLoad, load))
                                    m_activeLoad = null;
                            }
                        }
                    }
                    finally
                    {
                        result.Writer?.Dispose();
                        if (ReferenceEquals(m_pendingWriter, result.Writer))
                            m_pendingWriter = null;
                    }

                    if (!WorldAlive(zone))
                        yield break;
                    if (m_forceRequested || m_reloadRequested)
                        continue;
                    if (!committed)
                    {
                        m_updating = false;
                        LogWarning($"Seasonal cache update failed: {result.Error?.Message ?? "incomplete data"}");
                        // The previous appearance is still valid. Repaint objects
                        // registered while the failed load was in progress.
                        if (Initialized() && !initial)
                            RefreshAppearance(newCache: false, initial: false);
                        yield break;
                    }
                    if (m_loadedSeason == DesiredSeason())
                        yield break;
                }
            }
            finally
            {
                m_updating = false;
                m_running = false;
            }
        }

        private void CommitAppearance(CachedData source, Season season,
            Dictionary<int, TextureVariants> incoming, bool initial)
        {
            bool newCache = !ReferenceEquals(m_cache, source);
            TextureVariants[] retired = null;
            if (newCache)
            {
                retired = textures.Values.ToArray();
                controllers.Clear();
                controllers.Copy(source.controllers);
                textures.Clear();
                textures.Copy(incoming);
                incoming.Clear();
                m_cache = source;
                revision = source.revision;
            }
            else
                foreach (var entry in textures)
                    entry.Value.ExchangeSeason(incoming[entry.Key]);

            m_loadedSeason = season;
            VegetationApi.InvalidateAppearance();
            m_updating = false;

            try
            {
                RefreshAppearance(newCache, initial);
                LogInfo($"Loaded seasonal cache revision:{revision} season:{season} textures:{textures.Count}");
            }
            finally
            {
                if (retired != null)
                    foreach (TextureVariants texture in retired)
                        texture.Dispose();
                if (season != Season.Fall)
                    foreach (TextureVariants texture in textures.Values)
                        texture.ReleaseRecolor();
            }
        }

        private void RefreshAppearance(bool newCache, bool initial)
        {
            if (initial)
                return; // The initial loading controller creates the bindings.

            if (!PrefabVariantController.instance)
            {
                SeasonState.InitializeTextureControllers();
                return;
            }

            CustomTextures.UpdateTextures();
            PrefabVariantController.MaterialVariants.UpdateSeasonalMaterials();
            if (newCache)
            {
                ClutterVariantController.Reinitialize();
                PrefabVariantController.ReinitializePrefabVariants();
            }
            else
            {
                ClutterVariantController.Instance?.UpdateColors();
                PrefabVariantController.UpdatePrefabColors();
            }
            PrefabVariantController.MaterialVariants.RetireInactiveSeason(AppearanceSeason);
        }
    }
}
