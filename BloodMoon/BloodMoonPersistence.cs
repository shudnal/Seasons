using Newtonsoft.Json;
using System;
using System.IO;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonPersistence
    {
        private const string StateDirectoryName = "BloodMoon";
        private static readonly JsonSerializerSettings serializerSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        internal static string GetStatePath(long worldUid)
        {
            return Path.Combine(configDirectory, StateDirectoryName, $"{worldUid}.json");
        }

        internal static BloodMoonEventState Load(long worldUid)
        {
            BloodMoonEventState clean = CreateClean(worldUid);
            string path = GetStatePath(worldUid);
            if (!File.Exists(path))
                return clean;

            try
            {
                BloodMoonEventState state = JsonConvert.DeserializeObject<BloodMoonEventState>(File.ReadAllText(path), serializerSettings);
                if (state == null || state.Schema != BloodMoonStateSchema.Current || state.WorldUid != worldUid)
                {
                    LogWarning($"[BloodMoon.Persistence] Ignoring incompatible state file '{path}'.");
                    return clean;
                }

                Normalize(state);
                LogInfo($"[BloodMoon.Persistence] Loaded event {state.EventId}, phase {state.Phase}, revision {state.Revision}.");
                return state;
            }
            catch (Exception ex)
            {
                LogError($"[BloodMoon.Persistence] Failed to load '{path}': {ex}");
                return clean;
            }
        }

        internal static void Save(BloodMoonEventState state)
        {
            if (state == null || state.WorldUid == 0L)
                return;

            string path = GetStatePath(state.WorldUid);
            string directory = Path.GetDirectoryName(path);
            string temporary = path + ".new";
            string backup = path + ".old";

            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(temporary, JsonConvert.SerializeObject(state, serializerSettings));
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
                LogError($"[BloodMoon.Persistence] Failed to save '{path}': {ex}");
                try
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
                catch
                {
                }
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
            state.Participants ??= new System.Collections.Generic.Dictionary<long, BloodMoonParticipantState>();
            state.Groups ??= new System.Collections.Generic.Dictionary<long, BloodMoonGroupState>();
            state.SpawnLeases ??= new System.Collections.Generic.Dictionary<string, BloodMoonSpawnLeaseState>();
            state.ReportedEnemyDeaths ??= new System.Collections.Generic.HashSet<string>();
            state.ExtraEnemyZdos ??= new System.Collections.Generic.HashSet<string>();
            state.ParkedBosses ??= new System.Collections.Generic.Dictionary<string, BloodMoonBossParkingState>();

            foreach (BloodMoonParticipantState participant in state.Participants.Values)
            {
                participant.SkillContribution ??= new System.Collections.Generic.Dictionary<int, float>();
                participant.LiveSkillBonusEquivalent ??= new System.Collections.Generic.Dictionary<int, float>();
            }
        }
    }
}
