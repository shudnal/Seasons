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

        [Serializable]
        private sealed class Store
        {
            public int Schema = BloodMoonOutcomeQueue.Schema;
            public long WorldUid;
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

        private static readonly JsonSerializerSettings serializerSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        private static Store store;
        private static long loadedWorldUid;
        private static float retryTimer;
        private static ZRoutedRpc registeredRpc;

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
                    if (TryApplyLocal(store.WorldUid, outcome))
                    {
                        store.Pending.Remove(entry.Key);
                        Save();
                    }
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
            if (!TryApplyLocal(worldUid, outcome))
                return;

            ZPackage ack = new ZPackage();
            ack.Write(BloodMoonNetwork.ProtocolVersion);
            ack.Write(worldUid);
            ack.Write(outcome.EventId);
            ack.Write(outcome.PlayerId);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcAck, ack);
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

            EnsureLoaded(worldUid);
            if (store == null || !store.Pending.Remove(MakeKey(eventId, playerId)))
                return;

            Save();
            LogInfo($"[BloodMoon][event:{eventId}][player:{playerId}][outcome] Durable outcome acknowledged.");
        }

        private static bool TryApplyLocal(long worldUid, PendingOutcome outcome)
        {
            Player player = Player.m_localPlayer;
            if (player == null || outcome == null || outcome.PlayerId != player.GetPlayerID() || ZNet.m_world == null || ZNet.m_world.m_uid != worldUid)
                return false;

            try
            {
                BloodMoonSkills.ApplySerializedReward(player, outcome.RewardPayload);

                string chronicleKey = $"Blood Moon {worldUid}:{outcome.EventId}";
                bool chronicleChanged = !string.IsNullOrWhiteSpace(outcome.Chronicle) &&
                    (!player.m_knownTexts.TryGetValue(chronicleKey, out string existing) || !string.Equals(existing, outcome.Chronicle, StringComparison.Ordinal));
                if (chronicleChanged)
                    player.AddKnownText(chronicleKey, outcome.Chronicle);
                if (!string.IsNullOrWhiteSpace(outcome.Chronicle))
                    BloodMoonDreams.Record(player, outcome.EventId, outcome.Chronicle);
                if (chronicleChanged)
                    player.Message(MessageHud.MessageType.Center, outcome.Chronicle);

                string restedKey = RestedRemovalPrefix + worldUid.ToString(CultureInfo.InvariantCulture) + "." + outcome.EventId.ToString(CultureInfo.InvariantCulture);
                if (!player.m_customData.ContainsKey(restedKey))
                {
                    player.GetSEMan()?.RemoveStatusEffect(SEMan.s_statusEffectRested);
                    player.m_customData[restedKey] = "1";
                }
                return true;
            }
            catch (Exception ex)
            {
                LogWarning($"[BloodMoon][event:{outcome.EventId}][player:{outcome.PlayerId}][outcome] Deferred outcome application failed: {ex}");
                return false;
            }
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
            foreach (string candidate in new[] { path, path + ".new", path + ".old" })
            {
                if (!File.Exists(candidate))
                    continue;
                try
                {
                    Store loaded = JsonConvert.DeserializeObject<Store>(File.ReadAllText(candidate), serializerSettings);
                    if (loaded == null || loaded.Schema != Schema || loaded.WorldUid != worldUid)
                        continue;
                    loaded.Pending ??= new Dictionary<string, PendingOutcome>();
                    if (!string.Equals(candidate, path, StringComparison.Ordinal))
                    {
                        store = loaded;
                        Save();
                    }
                    return loaded;
                }
                catch (Exception ex)
                {
                    LogWarning($"[BloodMoon.Outcome] Failed to load '{candidate}': {ex.Message}");
                }
            }

            return new Store { WorldUid = worldUid };
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
    }

    [HarmonyPatch(typeof(BloodMoonNetwork), nameof(BloodMoonNetwork.RegisterRpcs))]
    internal static class BloodMoonOutcomeRpcRegistrationPatch
    {
        private static void Postfix()
        {
            BloodMoonOutcomeQueue.RegisterRpc();
        }
    }

    [HarmonyPatch(typeof(BloodMoonController), "PublishOutcomes")]
    internal static class BloodMoonOutcomeCapturePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(BloodMoonController __instance)
        {
            BloodMoonOutcomeQueue.Capture(__instance?.State);
        }
    }

    [HarmonyPatch(typeof(BloodMoonController), "FixedUpdate")]
    internal static class BloodMoonOutcomeTickPatch
    {
        private static void Postfix()
        {
            // The controller runs FixedUpdate on all peers. TickServer internally gates the server role.
            BloodMoonOutcomeQueue.TickServer(Time.fixedDeltaTime);
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
