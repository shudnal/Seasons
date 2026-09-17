using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonSpawnPoolRegistry
    {
        private const int Schema = 1;

        [Serializable]
        private sealed class Store
        {
            public int Schema = BloodMoonSpawnPoolRegistry.Schema;
            public long WorldUid;
            public long Revision;
            public long UpdatedAtUtcTicks;
            public Dictionary<long, string> PrefabsByEvent = new Dictionary<long, string>();
        }

        private sealed class Candidate
        {
            internal string Path;
            internal Store Store;
            internal long FileTicks;
        }

        private static readonly JsonSerializerSettings serializerSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        private static Store store;
        private static long loadedWorldUid;

        internal static void Register(long worldUid, long eventId, string prefabName)
        {
            prefabName = prefabName?.Trim() ?? string.Empty;
            if (worldUid == 0L || eventId < 0L || string.IsNullOrEmpty(prefabName))
                return;

            EnsureLoaded(worldUid);
            if (store == null)
                return;

            if (store.PrefabsByEvent.TryGetValue(eventId, out string existing) && string.Equals(existing, prefabName, StringComparison.Ordinal))
                return;

            if (!string.IsNullOrEmpty(existing) && !string.Equals(existing, prefabName, StringComparison.Ordinal))
            {
                LogError($"[BloodMoon][event:{eventId}][spawn] Refusing to replace durable frozen spawn-pool identity '{existing}' with '{prefabName}'.");
                return;
            }

            store.PrefabsByEvent[eventId] = prefabName;
            Save();
        }

        internal static string GetPrefabName(long worldUid, long eventId)
        {
            if (worldUid == 0L || eventId < 0L)
                return string.Empty;

            EnsureLoaded(worldUid);
            return store != null && store.PrefabsByEvent.TryGetValue(eventId, out string prefabName)
                ? prefabName?.Trim() ?? string.Empty
                : string.Empty;
        }

        internal static void ResetRuntime()
        {
            store = null;
            loadedWorldUid = 0L;
        }

        private static void EnsureLoaded(long worldUid)
        {
            if (worldUid == 0L || loadedWorldUid == worldUid && store != null)
                return;

            loadedWorldUid = worldUid;
            store = Load(worldUid);
        }

        private static Store Load(long worldUid)
        {
            string path = GetPath(worldUid);
            List<Candidate> valid = new List<Candidate>();
            foreach (string candidatePath in new[] { path, path + ".new", path + ".old" })
            {
                if (!File.Exists(candidatePath))
                    continue;

                try
                {
                    Store loaded = JsonConvert.DeserializeObject<Store>(File.ReadAllText(candidatePath), serializerSettings);
                    if (loaded == null || loaded.Schema != Schema || loaded.WorldUid != worldUid)
                        continue;
                    loaded.PrefabsByEvent ??= new Dictionary<long, string>();
                    valid.Add(new Candidate
                    {
                        Path = candidatePath,
                        Store = loaded,
                        FileTicks = File.GetLastWriteTimeUtc(candidatePath).Ticks
                    });
                }
                catch (Exception ex)
                {
                    LogWarning($"[BloodMoon.SpawnPool] Failed to load '{candidatePath}': {ex.Message}");
                }
            }

            if (valid.Count == 0)
                return new Store { WorldUid = worldUid };

            Candidate selected = valid
                .OrderByDescending(candidate => candidate.Store.Revision)
                .ThenByDescending(candidate => candidate.Store.UpdatedAtUtcTicks)
                .ThenByDescending(candidate => candidate.FileTicks)
                .First();

            Store result = selected.Store;
            if (!string.Equals(selected.Path, path, StringComparison.Ordinal))
            {
                LogWarning($"[BloodMoon.SpawnPool] Recovered newest valid spawn-pool registry from '{selected.Path}'. Rewriting canonical snapshot.");
                store = result;
                Save();
            }
            return result;
        }

        private static void Save()
        {
            if (store == null || store.WorldUid == 0L)
                return;

            string path = GetPath(store.WorldUid);
            string temporary = path + ".new";
            string backup = path + ".old";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                store.Revision++;
                store.UpdatedAtUtcTicks = DateTime.UtcNow.Ticks;
                File.WriteAllText(temporary, JsonConvert.SerializeObject(store, serializerSettings));
                if (File.Exists(path))
                {
                    if (File.Exists(backup))
                        File.Delete(backup);
                    File.Move(path, backup);
                }
                File.Move(temporary, path);
            }
            catch (Exception ex)
            {
                LogError($"[BloodMoon.SpawnPool] Failed to save '{path}': {ex}");
            }
        }

        private static string GetPath(long worldUid)
        {
            return Path.Combine(configDirectory, "BloodMoon", $"{worldUid}.spawn-pools.json");
        }
    }

    [HarmonyPatch(typeof(BloodMoonSpawner), nameof(BloodMoonSpawner.FreezeSpawnPool))]
    internal static class BloodMoonSpawnPoolRegistryFreezePatch
    {
        private static void Postfix(BloodMoonEventState state)
        {
            if (state == null || !state.SpawnPoolFrozen)
                return;

            BloodMoonSpawnPoolRegistry.Register(state.WorldUid, state.EventId, BloodMoonSpawner.GetFrozenSpawnPrefabName(state));
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    internal static class BloodMoonSpawnPoolRegistryWorldPatch
    {
        private static void Prefix()
        {
            BloodMoonSpawnPoolRegistry.ResetRuntime();
        }
    }
}
