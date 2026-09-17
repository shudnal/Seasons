using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonPersistence
    {
        private const string StateDirectoryName = "BloodMoon";
        private static readonly Dictionary<long, long> generationHighWater = new Dictionary<long, long>();

        private sealed class Candidate
        {
            internal string Path;
            internal BloodMoonEventState State;
            internal long FileTicks;
        }

        internal static string GetStatePath(long worldUid)
        {
            return Path.Combine(configDirectory, StateDirectoryName, $"{worldUid}.json");
        }

        internal static BloodMoonEventState Load(long worldUid)
        {
            BloodMoonEventState clean = CreateClean(worldUid);
            string path = GetStatePath(worldUid);
            string[] candidatePaths = { path, path + ".new", path + ".old" };
            bool anyFile = false;
            List<Candidate> valid = new List<Candidate>();

            foreach (string candidatePath in candidatePaths)
            {
                if (!File.Exists(candidatePath))
                    continue;
                anyFile = true;
                if (TryLoadCandidate(candidatePath, worldUid, out BloodMoonEventState state))
                {
                    valid.Add(new Candidate
                    {
                        Path = candidatePath,
                        State = state,
                        FileTicks = File.GetLastWriteTimeUtc(candidatePath).Ticks
                    });
                }
            }

            if (valid.Count == 0)
            {
                generationHighWater.Remove(worldUid);
                if (anyFile)
                    LogError($"[BloodMoon.Persistence] No valid state snapshot could be recovered for world {worldUid}.");
                return clean;
            }

            long observedGeneration = valid.Max(candidate => Math.Max(0L, candidate.State.PersistenceGeneration));
            generationHighWater[worldUid] = Math.Max(generationHighWater.TryGetValue(worldUid, out long cachedGeneration) ? cachedGeneration : 0L, observedGeneration);

            // Every successful Save() assigns a monotonically increasing generation before writing the
            // temporary snapshot. World/game time is intentionally not used as the primary ordering key:
            // skiptime can move backwards and debug cleanup creates a replacement state whose UpdatedAt
            // may be lower than the previous active snapshot. Legacy snapshots have generation zero and
            // retain deterministic timestamp/revision tie-breaking below.
            Candidate selected = valid
                .OrderByDescending(candidate => candidate.State.PersistenceGeneration)
                .ThenByDescending(candidate => candidate.FileTicks)
                .ThenByDescending(candidate => candidate.State.UpdatedAt)
                .ThenByDescending(candidate => candidate.State.EventId)
                .ThenByDescending(candidate => candidate.State.Revision)
                .First();

            BloodMoonEventState loaded = selected.State;
            Normalize(loaded);
            bool reconciled = BloodMoonRecoverySchedule.ReconcileLoadedState(loaded);
            LogInfo($"[BloodMoon.Persistence] Loaded event {loaded.EventId}, phase {loaded.Phase}, revision {loaded.Revision}, generation {loaded.PersistenceGeneration} from '{selected.Path}'.");

            if (reconciled || !string.Equals(selected.Path, path, StringComparison.Ordinal))
            {
                if (!string.Equals(selected.Path, path, StringComparison.Ordinal))
                    LogWarning($"[BloodMoon.Persistence] Recovered newest valid state from '{selected.Path}'. Rewriting canonical snapshot.");
                Save(loaded);
            }
            return loaded;
        }

        private static bool TryLoadCandidate(string path, long worldUid, out BloodMoonEventState state)
        {
            state = null;
            try
            {
                state = BloodMoonJson.DeserializePersistence<BloodMoonEventState>(File.ReadAllText(path));
                if (state == null || state.Schema != BloodMoonStateSchema.Current || state.WorldUid != worldUid)
                {
                    LogWarning($"[BloodMoon.Persistence] Ignoring incompatible state file '{path}'.");
                    state = null;
                    return false;
                }
                state.PersistenceGeneration = Math.Max(0L, state.PersistenceGeneration);
                return true;
            }
            catch (Exception ex)
            {
                LogError($"[BloodMoon.Persistence] Failed to load '{path}': {ex}");
                state = null;
                return false;
            }
        }

        internal static bool Save(BloodMoonEventState state)
        {
            if (state == null || state.WorldUid == 0L)
                return false;

            string path = GetStatePath(state.WorldUid);
            string directory = Path.GetDirectoryName(path);
            string temporary = path + ".new";
            string backup = path + ".old";

            long cachedGeneration = generationHighWater.TryGetValue(state.WorldUid, out long generation) ? generation : 0L;
            state.PersistenceGeneration = Math.Max(cachedGeneration, Math.Max(0L, state.PersistenceGeneration)) + 1L;
            generationHighWater[state.WorldUid] = state.PersistenceGeneration;

            bool durableTemporaryWritten = false;
            try
            {
                string serialized = BloodMoonJson.SerializePersistence(state);
                Directory.CreateDirectory(directory);
                File.WriteAllText(temporary, serialized);
                // File.WriteAllText closes the file before returning. From this point the current
                // generation is recoverable even if canonical/backup rotation fails: Load() validates
                // and considers .new alongside the canonical and .old snapshots.
                durableTemporaryWritten = true;
                if (File.Exists(path))
                {
                    if (File.Exists(backup))
                        File.Delete(backup);
                    File.Move(path, backup);
                }
                File.Move(temporary, path);
                return true;
            }
            catch (Exception ex)
            {
                LogError($"[BloodMoon.Persistence] Failed to save '{path}': {ex}");
                // Keep a successfully written .new file for Load() recovery. Only remove an empty/invalid temporary file.
                try
                {
                    if (File.Exists(temporary) && new FileInfo(temporary).Length == 0L)
                        File.Delete(temporary);
                }
                catch
                {
                }
                return durableTemporaryWritten;
            }
        }

        internal static void Delete(long worldUid)
        {
            if (worldUid == 0L)
                return;

            string path = GetStatePath(worldUid);
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                if (File.Exists(path + ".old"))
                    File.Delete(path + ".old");
                if (File.Exists(path + ".new"))
                    File.Delete(path + ".new");
                generationHighWater.Remove(worldUid);
            }
            catch (Exception ex)
            {
                LogError($"[BloodMoon.Persistence] Failed to delete state for world {worldUid}: {ex}");
            }
        }

        internal static BloodMoonEventState CreateClean(long worldUid)
        {
            return new BloodMoonEventState
            {
                WorldUid = worldUid,
                Phase = BloodMoonEventPhase.Dormant,
                ResolutionStep = BloodMoonResolutionStep.None
            };
        }

        private static void Normalize(BloodMoonEventState state)
        {
            state.PersistenceGeneration = Math.Max(0L, state.PersistenceGeneration);
            state.Participants ??= new Dictionary<long, BloodMoonParticipantState>();
            state.Groups ??= new Dictionary<long, BloodMoonGroupState>();
            state.SpawnLeases ??= new Dictionary<string, BloodMoonSpawnLeaseState>();
            state.ReportedEnemyDeaths ??= new HashSet<string>();
            state.ExtraEnemyZdos ??= new HashSet<string>();

            foreach (BloodMoonParticipantState participant in state.Participants.Values)
            {
                participant.SkillContribution ??= new Dictionary<int, float>();
                participant.LiveSkillBonusEquivalent ??= new Dictionary<int, float>();
            }
        }
    }
}
