using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonOutcomeQueue
    {
        private const int Schema = 1;
        private const float RetryIntervalSeconds = 5f;
        private const string RpcDeliver = "Seasons.BloodMoon.OutcomeDeliver";
        private const string RpcAck = "Seasons.BloodMoon.OutcomeAck";
        private const string RestedRemovalPrefix = "Seasons.BloodMoon.RestedRemoved.";
        private const string OutcomeAppliedPrefix = "Seasons.BloodMoon.OutcomeApplied.";

        private enum LocalApplyResult
        {
            Failed,
            PresentationPending,
            AppliedThisProcess,
            DurableMarkerPresent
        }

        [Serializable]
        private sealed class Store
        {
            public int Schema = BloodMoonOutcomeQueue.Schema;
            public long WorldUid;
            public long Revision;
            public long UpdatedAtUtcTicks;
            public Dictionary<string, PendingOutcome> Pending = new Dictionary<string, PendingOutcome>();
        }

        [Serializable]
        private sealed class PendingOutcome
        {
            public long EventId;
            public long PlayerId;
            public string RewardPayload = string.Empty;
            public string Chronicle = string.Empty;
        }

        private sealed class PendingAck
        {
            internal long WorldUid;
            internal long EventId;
            internal long PlayerId;
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

        private static readonly Dictionary<string, PendingAck> pendingLocalAcks = new Dictionary<string, PendingAck>();
        private static readonly HashSet<string> pendingDreamCompletionKeys = new HashSet<string>(StringComparer.Ordinal);
        // This deliberately survives ZNet world teardown. For cloud profiles, a marker is considered durable
        // only when it was not created by this running process, because vanilla PlayerProfile.Save() returns
        // true even when its cloud FileWriter fails and only a local recovery backup is produced.
        private static readonly HashSet<string> processAppliedOutcomeKeys = new HashSet<string>(StringComparer.Ordinal);
        private static Store store;
        private static long loadedWorldUid;
        private static float retryTimer;
        private static ZRoutedRpc registeredRpc;
        private static bool pendingLocalProfileCaptured;

        internal static void RegisterRpc()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(registeredRpc, rpc))
                return;

            registeredRpc = rpc;
            rpc.Register<ZPackage>(RpcDeliver, OnDeliver);
            rpc.Register<ZPackage>(RpcAck, OnAck);
        }

        internal static void Capture(BloodMoonEventState state)
        {
            if (state == null || state.WorldUid == 0L || state.EventId < 0L || state.ResolutionCancelledBeforeCombat)
                return;

            EnsureLoaded(state.WorldUid);
            if (store == null)
                return;

            bool changed = false;
            foreach (BloodMoonParticipantState participant in state.Participants.Values)
            {
                if (participant == null || participant.PlayerId == 0L)
                    continue;

                string key = MakeKey(state.EventId, participant.PlayerId);
                if (store.Pending.ContainsKey(key))
                    continue;

                store.Pending[key] = new PendingOutcome
                {
                    EventId = state.EventId,
                    PlayerId = participant.PlayerId,
                    RewardPayload = BloodMoonSkills.SerializeCompletionReward(participant),
                    Chronicle = BloodMoonPresentation.BuildChronicle(participant)
                };
                changed = true;
            }

            if (changed)
                Save();
            retryTimer = 0f;
        }

        internal static void TickServer(float dt)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZNet.m_world == null)
                return;

            RegisterRpc();
            EnsureLoaded(ZNet.m_world.m_uid);
            if (store == null || store.Pending.Count == 0)
                return;

            retryTimer -= Mathf.Max(0f, dt);
            if (retryTimer > 0f)
                return;
            retryTimer = RetryIntervalSeconds;

            BloodMoonController controller = BloodMoonController.Instance;
            if (controller == null)
                return;

            foreach (KeyValuePair<string, PendingOutcome> entry in store.Pending
                .OrderBy(pair => pair.Value.EventId)
                .ThenBy(pair => pair.Value.PlayerId)
                .ToArray())
            {
                PendingOutcome outcome = entry.Value;
                if (outcome == null)
                {
                    store.Pending.Remove(entry.Key);
                    Save();
                    continue;
                }

                if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == outcome.PlayerId)
                {
                    HandleLocalApplyResult(store.WorldUid, outcome, TryApplyLocal(store.WorldUid, outcome));
                    continue;
                }

                long peerId = controller.GetPeerForPlayer(outcome.PlayerId);
                if (peerId == 0L || ZRoutedRpc.instance == null)
                    continue;

                ZPackage pkg = new ZPackage();
                pkg.Write(BloodMoonNetwork.ProtocolVersion);
                pkg.Write(store.WorldUid);
                pkg.Write(outcome.EventId);
                pkg.Write(outcome.PlayerId);
                pkg.Write(outcome.RewardPayload ?? string.Empty);
                pkg.Write(outcome.Chronicle ?? string.Empty);
                ZRoutedRpc.instance.InvokeRoutedRPC(peerId, RpcDeliver, pkg);
            }
        }

        internal static string Dump()
        {
            long worldUid = ZNet.m_world != null ? ZNet.m_world.m_uid : loadedWorldUid;
            if (worldUid != 0L)
                EnsureLoaded(worldUid);
            if (store == null || store.Pending.Count == 0)
                return "No pending Blood Moon outcomes.";
            return string.Join("\n", store.Pending.Values
                .OrderBy(outcome => outcome.EventId)
                .ThenBy(outcome => outcome.PlayerId)
                .Select(outcome => $"event={outcome.EventId} player={outcome.PlayerId}"));
        }

        internal static void ResetRuntime()
        {
            store = null;
            loadedWorldUid = 0L;
            retryTimer = 0f;
            registeredRpc = null;
            pendingLocalAcks.Clear();
            pendingDreamCompletionKeys.Clear();
            pendingLocalProfileCaptured = false;
        }

        internal static void OnLocalDreamPresentationCompleted(long worldUid, long eventId, long playerId)
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.GetPlayerID() != playerId || ZNet.m_world == null || ZNet.m_world.m_uid != worldUid)
                return;

            string processKey = MakeProcessAppliedKey(worldUid, eventId, playerId);
            if (!pendingDreamCompletionKeys.Remove(processKey))
                return;

            MarkOutcomeApplied(player, worldUid, eventId, playerId);
            QueueLocalAck(worldUid, new PendingOutcome { EventId = eventId, PlayerId = playerId });
        }

        internal static void OnLocalPlayerDataCaptured(PlayerProfile profile, Player player)
        {
            if (pendingLocalAcks.Count == 0 || profile == null || player == null || Game.instance == null ||
                !ReferenceEquals(profile, Game.instance.GetPlayerProfile()) || player != Player.m_localPlayer)
                return;

            pendingLocalProfileCaptured = true;
        }

        internal static void OnLocalProfileSaved(PlayerProfile profile, bool success)
        {
            if (!success || !pendingLocalProfileCaptured || pendingLocalAcks.Count == 0 || profile == null || Game.instance == null ||
                !ReferenceEquals(profile, Game.instance.GetPlayerProfile()))
                return;

            // Vanilla PlayerProfile.SavePlayerToDisk() always returns true after a cloud FileWriter failure,
            // so its bool result is not a durable cloud-write acknowledgement. Keep the server queue entry
            // until a fresh process observes the outcome marker loaded back from the character profile.
            if (profile.m_fileSource == FileHelpers.FileSource.Cloud)
                return;

            foreach (KeyValuePair<string, PendingAck> entry in pendingLocalAcks.ToArray())
            {
                PendingAck ack = entry.Value;
                if (ack == null)
                {
                    pendingLocalAcks.Remove(entry.Key);
                    continue;
                }

                if (TryAcknowledge(ack.WorldUid, ack.EventId, ack.PlayerId))
                    pendingLocalAcks.Remove(entry.Key);
            }

            pendingLocalProfileCaptured = pendingLocalAcks.Count > 0;
        }

        private static void OnDeliver(long sender, ZPackage pkg)
        {
            if (pkg == null || ZNet.instance == null || ZNet.instance.IsServer() || ZRoutedRpc.instance == null ||
                sender != ZRoutedRpc.instance.GetServerPeerID() || pkg.ReadInt() != BloodMoonNetwork.ProtocolVersion)
                return;

            long worldUid = pkg.ReadLong();
            PendingOutcome outcome = new PendingOutcome
            {
                EventId = pkg.ReadLong(),
                PlayerId = pkg.ReadLong(),
                RewardPayload = pkg.ReadString(),
                Chronicle = pkg.ReadString()
            };

            Player player = Player.m_localPlayer;
            if (player == null || player.GetPlayerID() != outcome.PlayerId || ZNet.m_world == null || ZNet.m_world.m_uid != worldUid)
                return;

            HandleLocalApplyResult(worldUid, outcome, TryApplyLocal(worldUid, outcome));
        }

        private static void OnAck(long sender, ZPackage pkg)
        {
            if (pkg == null || ZNet.instance == null || !ZNet.instance.IsServer() || pkg.ReadInt() != BloodMoonNetwork.ProtocolVersion)
                return;

            long worldUid = pkg.ReadLong();
            long eventId = pkg.ReadLong();
            long playerId = pkg.ReadLong();
            if (ZNet.m_world == null || ZNet.m_world.m_uid != worldUid || !ValidateSender(sender, playerId))
                return;

            RemovePendingOutcome(worldUid, eventId, playerId);
        }

        private static void HandleLocalApplyResult(long worldUid, PendingOutcome outcome, LocalApplyResult result)
        {
            if (result == LocalApplyResult.DurableMarkerPresent)
                TryAcknowledge(worldUid, outcome.EventId, outcome.PlayerId);
            else if (result == LocalApplyResult.AppliedThisProcess)
                QueueLocalAck(worldUid, outcome);
        }

        private static void QueueLocalAck(long worldUid, PendingOutcome outcome)
        {
            if (worldUid == 0L || outcome == null || outcome.EventId < 0L || outcome.PlayerId == 0L)
                return;

            string key = MakeLocalAckKey(worldUid, outcome.EventId, outcome.PlayerId);
            if (pendingLocalAcks.ContainsKey(key))
                return;

            pendingLocalAcks[key] = new PendingAck
            {
                WorldUid = worldUid,
                EventId = outcome.EventId,
                PlayerId = outcome.PlayerId
            };
            pendingLocalProfileCaptured = false;
            LogInfo($"[BloodMoon][event:{outcome.EventId}][player:{outcome.PlayerId}][outcome] Applied locally; acknowledgement deferred until durable character persistence is proven.");
        }

        private static bool TryAcknowledge(long worldUid, long eventId, long playerId)
        {
            if (worldUid == 0L || eventId < 0L || playerId == 0L || ZNet.m_world == null || ZNet.m_world.m_uid != worldUid ||
                Player.m_localPlayer == null || Player.m_localPlayer.GetPlayerID() != playerId)
                return false;

            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                RemovePendingOutcome(worldUid, eventId, playerId);
                return true;
            }

            if (ZRoutedRpc.instance == null)
                return false;

            ZPackage pkg = new ZPackage();
            pkg.Write(BloodMoonNetwork.ProtocolVersion);
            pkg.Write(worldUid);
            pkg.Write(eventId);
            pkg.Write(playerId);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcAck, pkg);
            return true;
        }

        private static void RemovePendingOutcome(long worldUid, long eventId, long playerId)
        {
            EnsureLoaded(worldUid);
            if (store == null || store.WorldUid != worldUid || !store.Pending.Remove(MakeKey(eventId, playerId)))
                return;

            Save();
            LogInfo($"[BloodMoon][event:{eventId}][player:{playerId}][outcome] Durable outcome acknowledgement removed the server queue entry.");
        }

        private static LocalApplyResult TryApplyLocal(long worldUid, PendingOutcome outcome)
        {
            Player player = Player.m_localPlayer;
            if (player == null || outcome == null || outcome.PlayerId != player.GetPlayerID() || ZNet.m_world == null || ZNet.m_world.m_uid != worldUid)
                return LocalApplyResult.Failed;

            string markerKey = MakeOutcomeMarkerKey(worldUid, outcome.EventId);
            string processKey = MakeProcessAppliedKey(worldUid, outcome.EventId, outcome.PlayerId);
            if (player.m_customData.ContainsKey(markerKey))
                return processAppliedOutcomeKeys.Contains(processKey) ? LocalApplyResult.AppliedThisProcess : LocalApplyResult.DurableMarkerPresent;

            try
            {
                BloodMoonSkills.ApplySerializedReward(player, outcome.RewardPayload);

                string restedKey = RestedRemovalPrefix + worldUid.ToString(CultureInfo.InvariantCulture) + "." + outcome.EventId.ToString(CultureInfo.InvariantCulture);
                if (!player.m_customData.ContainsKey(restedKey))
                {
                    player.GetSEMan()?.RemoveStatusEffect(SEMan.s_statusEffectRested);
                    player.m_customData[restedKey] = "1";
                }

                if (string.IsNullOrWhiteSpace(outcome.Chronicle))
                    return LocalApplyResult.Failed;

                string chronicleKey = $"Blood Moon {worldUid}:{outcome.EventId}";
                if (!player.m_knownTexts.TryGetValue(chronicleKey, out string existing) || !string.Equals(existing, outcome.Chronicle, StringComparison.Ordinal))
                    player.AddKnownText(chronicleKey, outcome.Chronicle);

                bool dreamAlreadyPresented = BloodMoonDreams.IsPresented(player, outcome.EventId);
                if (!BloodMoonDreams.Present(player, outcome.EventId, outcome.Chronicle))
                    return LocalApplyResult.Failed;

                if (!dreamAlreadyPresented)
                {
                    pendingDreamCompletionKeys.Add(processKey);
                    return LocalApplyResult.PresentationPending;
                }

                MarkOutcomeApplied(player, worldUid, outcome.EventId, outcome.PlayerId);
                return LocalApplyResult.AppliedThisProcess;
            }
            catch (Exception ex)
            {
                LogWarning($"[BloodMoon][event:{outcome.EventId}][player:{outcome.PlayerId}][outcome] Deferred outcome application failed: {ex}");
                return LocalApplyResult.Failed;
            }
        }

        private static void MarkOutcomeApplied(Player player, long worldUid, long eventId, long playerId)
        {
            player.m_customData[MakeOutcomeMarkerKey(worldUid, eventId)] = "1";
            processAppliedOutcomeKeys.Add(MakeProcessAppliedKey(worldUid, eventId, playerId));
        }

        private static bool ValidateSender(long sender, long playerId)
        {
            if (playerId == 0L || ZNet.instance == null || ZRoutedRpc.instance == null || ZDOMan.instance == null)
                return false;
            if (sender == ZRoutedRpc.instance.GetServerPeerID())
                return Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId;

            ZNetPeer peer = ZNet.instance.GetPeer(sender);
            if (peer == null || peer.m_characterID.IsNone())
                return false;
            ZDO playerZdo = ZDOMan.instance.GetZDO(peer.m_characterID);
            return playerZdo != null && playerZdo.GetLong(ZDOVars.s_playerID, 0L) == playerId;
        }

        private static void EnsureLoaded(long worldUid)
        {
            if (worldUid == 0L || loadedWorldUid == worldUid && store != null)
                return;

            loadedWorldUid = worldUid;
            store = Load(worldUid);
            retryTimer = 0f;
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
                    loaded.Pending ??= new Dictionary<string, PendingOutcome>();
                    valid.Add(new Candidate
                    {
                        Path = candidatePath,
                        Store = loaded,
                        FileTicks = File.GetLastWriteTimeUtc(candidatePath).Ticks
                    });
                }
                catch (Exception ex)
                {
                    LogWarning($"[BloodMoon.Outcome] Failed to load '{candidatePath}': {ex.Message}");
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
                LogWarning($"[BloodMoon.Outcome] Recovered newest valid outcome snapshot from '{selected.Path}'. Rewriting canonical snapshot.");
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
                LogError($"[BloodMoon.Outcome] Failed to save '{path}': {ex}");
            }
        }

        private static string GetPath(long worldUid)
        {
            return Path.Combine(configDirectory, "BloodMoon", $"{worldUid}.outcomes.json");
        }

        private static string MakeKey(long eventId, long playerId)
        {
            return eventId.ToString(CultureInfo.InvariantCulture) + ":" + playerId.ToString(CultureInfo.InvariantCulture);
        }

        private static string MakeLocalAckKey(long worldUid, long eventId, long playerId)
        {
            return worldUid.ToString(CultureInfo.InvariantCulture) + ":" + MakeKey(eventId, playerId);
        }

        private static string MakeOutcomeMarkerKey(long worldUid, long eventId)
        {
            return OutcomeAppliedPrefix + worldUid.ToString(CultureInfo.InvariantCulture) + "." + eventId.ToString(CultureInfo.InvariantCulture);
        }

        private static string MakeProcessAppliedKey(long worldUid, long eventId, long playerId)
        {
            return worldUid.ToString(CultureInfo.InvariantCulture) + ":" + eventId.ToString(CultureInfo.InvariantCulture) + ":" + playerId.ToString(CultureInfo.InvariantCulture);
        }
    }

    [HarmonyPatch(typeof(PlayerProfile), nameof(PlayerProfile.SavePlayerData))]
    internal static class BloodMoonOutcomePlayerDataCapturePatch
    {
        private static void Postfix(PlayerProfile __instance, Player player)
        {
            BloodMoonOutcomeQueue.OnLocalPlayerDataCaptured(__instance, player);
        }
    }

    [HarmonyPatch(typeof(PlayerProfile), nameof(PlayerProfile.Save))]
    internal static class BloodMoonOutcomeProfileSavePatch
    {
        private static void Postfix(PlayerProfile __instance, bool __result)
        {
            BloodMoonOutcomeQueue.OnLocalProfileSaved(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    internal static class BloodMoonOutcomeRuntimeResetPatch
    {
        private static void Prefix()
        {
            BloodMoonOutcomeQueue.ResetRuntime();
        }
    }
}
