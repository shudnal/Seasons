using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonSkillReports
    {
        internal static void Accept(long sender, long eventId, long playerId, long sequence, Skills.SkillType skill, float baseEquivalent, float liveBonusEquivalent)
        {
            BloodMoonController controller = BloodMoonController.Instance;
            BloodMoonEventState state = controller?.State;
            if (state == null || state.EventId != eventId || sequence <= 0L || !IsFinite(baseEquivalent) || !IsFinite(liveBonusEquivalent))
                return;
            if (!state.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant) || !ValidateSender(sender, playerId))
                return;

            long previousSequence = participant.LastSkillReportSequence;
            if (state.IsCombatLive && participant.IsCombatActive)
                BloodMoonSkills.AcceptServerReport(participant, sequence, skill, baseEquivalent, liveBonusEquivalent);

            if (participant.LastSkillReportSequence != previousSequence)
            {
                state.UpdatedAt = SeasonState.IsActive ? seasonState.GetTotalSeconds() : state.UpdatedAt;
                state.Revision++;
                BloodMoonPersistence.Save(state);
                BloodMoonNetwork.Publish(state, state.UpdatedAt);
            }

            // A duplicate/retried report still receives the current durable high-water mark. This makes
            // a lost ACK harmless while never acknowledging a sequence that the server did not accept.
            BloodMoonSkillReportReliability.SendAck(sender, eventId, playerId, participant.LastSkillReportSequence);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

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
    }

    internal static class BloodMoonSkillReportReliability
    {
        private const string RecordKey = "Seasons.BloodMoon.LiveSkillReports";
        private const string RpcAck = "Seasons.BloodMoon.SkillGainAck";
        private const float RetrySeconds = 2f;
        private const int MaxReportsPerRetry = 8;

        [Serializable]
        private sealed class PendingSkillGainReport
        {
            public long Sequence;
            public int Skill;
            public float BaseEquivalent;
            public float LiveBonusEquivalent;
        }

        [Serializable]
        private sealed class LiveSkillReportRecord
        {
            public long WorldUid;
            public long EventId;
            public long PlayerId;
            public long LastAckSequence;
            public long LastSequence;
            public float LiveBonusUsed;
            public List<PendingSkillGainReport> Pending = new List<PendingSkillGainReport>();
        }

        private static readonly FieldInfo LocalSequenceField = AccessTools.Field(typeof(BloodMoonSkills), "localReportSequence");
        private static readonly FieldInfo LocalBonusField = AccessTools.Field(typeof(BloodMoonSkills), "localLiveBonusUsed");
        private static ZRoutedRpc registeredRpc;
        private static float retryTimer;

        internal static void RegisterRpc()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(registeredRpc, rpc))
                return;
            registeredRpc = rpc;
            retryTimer = 0f;
            rpc.Register<ZPackage>(RpcAck, OnAckRpc);
        }

        internal static void TrackOutgoing(long eventId, long playerId, long sequence, Skills.SkillType skill, float baseEquivalent, float liveBonusEquivalent)
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.GetPlayerID() != playerId || eventId < 0L || sequence <= 0L)
                return;

            LiveSkillReportRecord record = LoadOrCreate(player, eventId, playerId);
            if (record == null)
                return;

            bool changed = ReconcileServerBaseline(player, record);
            record.LastSequence = Math.Max(record.LastSequence, sequence);
            record.LiveBonusUsed = Math.Max(record.LiveBonusUsed, GetRuntimeLiveBonusUsed(eventId));
            if (!record.Pending.Any(item => item.Sequence == sequence))
            {
                record.Pending.Add(new PendingSkillGainReport
                {
                    Sequence = sequence,
                    Skill = (int)skill,
                    BaseEquivalent = baseEquivalent,
                    LiveBonusEquivalent = liveBonusEquivalent
                });
                changed = true;
            }

            if (changed || record.LastSequence == sequence)
                SaveRecord(player, record);
        }

        internal static void SendAck(long peerId, long eventId, long playerId, long sequence)
        {
            if (peerId == 0L || eventId < 0L || playerId == 0L || sequence < 0L || ZNet.instance == null || !ZNet.instance.IsServer() || ZRoutedRpc.instance == null)
                return;

            ZPackage package = new ZPackage();
            package.Write(BloodMoonNetwork.ProtocolVersion);
            package.Write(eventId);
            package.Write(playerId);
            package.Write(sequence);
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, RpcAck, package);
        }

        internal static void TickLocal(float dt)
        {
            RegisterRpc();
            Player player = Player.m_localPlayer;
            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            if (player == null || eventId < 0L)
                return;

            LiveSkillReportRecord record = LoadMatching(player, eventId, player.GetPlayerID());
            if (record == null)
                return;

            bool changed = ReconcileServerBaseline(player, record);
            SyncRuntime(record);
            if (changed)
                SaveRecord(player, record);

            BloodMoonEventPhase phase = BloodMoonNetwork.ClientGlobal.Phase;
            if (phase != BloodMoonEventPhase.Active && phase != BloodMoonEventPhase.AutoCompleting || record.Pending.Count == 0)
                return;

            retryTimer -= Mathf.Max(0f, dt);
            if (retryTimer > 0f)
                return;
            retryTimer = RetrySeconds;

            foreach (PendingSkillGainReport report in record.Pending
                .Where(item => item.Sequence > record.LastAckSequence)
                .OrderBy(item => item.Sequence)
                .Take(MaxReportsPerRetry)
                .ToArray())
            {
                BloodMoonNetwork.SendSkillGain(eventId, record.PlayerId, report.Sequence, (Skills.SkillType)report.Skill,
                    report.BaseEquivalent, report.LiveBonusEquivalent);
            }
        }

        internal static float GetPersistedLiveBonusUsed(long eventId)
        {
            Player player = Player.m_localPlayer;
            LiveSkillReportRecord record = player != null ? LoadMatching(player, eventId, player.GetPlayerID()) : null;
            return record != null ? Mathf.Max(0f, record.LiveBonusUsed) : 0f;
        }

        internal static void SyncRuntimeFromProfile(Player player, long eventId)
        {
            if (player == null || eventId < 0L)
                return;
            LiveSkillReportRecord record = LoadMatching(player, eventId, player.GetPlayerID());
            if (record == null)
                return;
            bool changed = ReconcileServerBaseline(player, record);
            SyncRuntime(record);
            if (changed)
                SaveRecord(player, record);
        }

        private static void OnAckRpc(long sender, ZPackage package)
        {
            if (package == null || ZRoutedRpc.instance == null || sender != ZRoutedRpc.instance.GetServerPeerID() ||
                package.ReadInt() != BloodMoonNetwork.ProtocolVersion)
                return;

            long eventId = package.ReadLong();
            long playerId = package.ReadLong();
            long sequence = package.ReadLong();
            Player player = Player.m_localPlayer;
            if (player == null || player.GetPlayerID() != playerId || sequence < 0L)
                return;

            LiveSkillReportRecord record = LoadMatching(player, eventId, playerId);
            if (record == null)
                return;

            long acknowledged = Math.Max(record.LastAckSequence, sequence);
            bool changed = acknowledged != record.LastAckSequence;
            record.LastAckSequence = acknowledged;
            int removed = record.Pending.RemoveAll(item => item.Sequence <= acknowledged);
            if (removed > 0)
                changed = true;
            record.LastSequence = Math.Max(record.LastSequence, acknowledged);
            SyncRuntime(record);
            if (changed)
                SaveRecord(player, record);
        }

        private static LiveSkillReportRecord LoadOrCreate(Player player, long eventId, long playerId)
        {
            long worldUid = GetCurrentWorldUid();
            if (worldUid == 0L || player == null || playerId == 0L)
                return null;

            LiveSkillReportRecord record = LoadRecord(player);
            if (record == null || record.WorldUid != worldUid || record.EventId != eventId || record.PlayerId != playerId)
            {
                record = new LiveSkillReportRecord
                {
                    WorldUid = worldUid,
                    EventId = eventId,
                    PlayerId = playerId
                };
            }
            record.Pending ??= new List<PendingSkillGainReport>();
            return record;
        }

        private static LiveSkillReportRecord LoadMatching(Player player, long eventId, long playerId)
        {
            if (eventId < 0L || player == null || playerId == 0L)
                return null;
            LiveSkillReportRecord record = LoadRecord(player);
            return record != null && record.WorldUid == GetCurrentWorldUid() && record.EventId == eventId && record.PlayerId == playerId
                ? record
                : null;
        }

        private static LiveSkillReportRecord LoadRecord(Player player)
        {
            if (player == null || !player.m_customData.TryGetValue(RecordKey, out string json) || string.IsNullOrWhiteSpace(json))
                return null;
            try
            {
                LiveSkillReportRecord record = JsonConvert.DeserializeObject<LiveSkillReportRecord>(json);
                if (record != null)
                    record.Pending ??= new List<PendingSkillGainReport>();
                return record;
            }
            catch (Exception ex)
            {
                LogWarning($"[BloodMoon.Skill] Invalid live skill report record removed: {ex.Message}");
                player.m_customData.Remove(RecordKey);
                return null;
            }
        }

        private static void SaveRecord(Player player, LiveSkillReportRecord record)
        {
            if (player == null || record == null)
                return;
            record.Pending = record.Pending
                .Where(item => item != null && item.Sequence > record.LastAckSequence)
                .GroupBy(item => item.Sequence)
                .Select(group => group.First())
                .OrderBy(item => item.Sequence)
                .ToList();
            player.m_customData[RecordKey] = JsonConvert.SerializeObject(record);
        }

        private static bool ReconcileServerBaseline(Player player, LiveSkillReportRecord record)
        {
            BloodMoonParticipantDetailSnapshot detail = BloodMoonParticipantDetails.ClientOwn;
            if (record == null || player == null || detail == null || detail.EventId != record.EventId || detail.PlayerId != record.PlayerId)
                return false;

            bool changed = false;
            long acknowledged = Math.Max(record.LastAckSequence, Math.Max(0L, detail.LastSkillReportSequence));
            if (acknowledged != record.LastAckSequence)
            {
                record.LastAckSequence = acknowledged;
                changed = true;
            }
            int removed = record.Pending.RemoveAll(item => item.Sequence <= acknowledged);
            if (removed > 0)
                changed = true;
            long sequence = Math.Max(record.LastSequence, Math.Max(0L, detail.LastSkillReportSequence));
            if (sequence != record.LastSequence)
            {
                record.LastSequence = sequence;
                changed = true;
            }
            float used = Math.Max(record.LiveBonusUsed, Mathf.Max(0f, detail.LiveSkillBonusUsed));
            if (Mathf.Abs(used - record.LiveBonusUsed) > 0.0001f)
            {
                record.LiveBonusUsed = used;
                changed = true;
            }
            return changed;
        }

        private static void SyncRuntime(LiveSkillReportRecord record)
        {
            if (record == null)
                return;

            if (LocalSequenceField != null)
            {
                long current = (long)LocalSequenceField.GetValue(null);
                if (record.LastSequence > current)
                    LocalSequenceField.SetValue(null, record.LastSequence);
            }

            if (LocalBonusField?.GetValue(null) is Dictionary<long, float> bonusByEvent)
            {
                float current = bonusByEvent.TryGetValue(record.EventId, out float value) ? value : 0f;
                bonusByEvent[record.EventId] = Mathf.Max(current, Mathf.Max(0f, record.LiveBonusUsed));
            }
        }

        private static float GetRuntimeLiveBonusUsed(long eventId)
        {
            if (LocalBonusField?.GetValue(null) is Dictionary<long, float> bonusByEvent && bonusByEvent.TryGetValue(eventId, out float value))
                return Mathf.Max(0f, value);
            return 0f;
        }

        private static long GetCurrentWorldUid()
        {
            return ZNet.m_world != null ? ZNet.m_world.m_uid : 0L;
        }
    }

    [HarmonyPatch(typeof(BloodMoonNetwork), nameof(BloodMoonNetwork.SendSkillGain))]
    internal static class BloodMoonSkillReportPersistencePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(long eventId, long playerId, long sequence, Skills.SkillType skill, float baseEquivalent, float liveBonusEquivalent)
        {
            BloodMoonSkillReportReliability.TrackOutgoing(eventId, playerId, sequence, skill, baseEquivalent, liveBonusEquivalent);
        }
    }

    [HarmonyPatch(typeof(BloodMoonSkills), "GetLocalLiveBonusUsed")]
    internal static class BloodMoonSkillPersistedBonusCapPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(long eventId, ref float __result)
        {
            __result = Mathf.Max(__result, BloodMoonSkillReportReliability.GetPersistedLiveBonusUsed(eventId));
        }
    }

    [HarmonyPatch(typeof(BloodMoonSkills), "EnsureLocalEvent")]
    internal static class BloodMoonSkillPersistedSequencePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Player player, ref bool __result)
        {
            if (__result)
                BloodMoonSkillReportReliability.SyncRuntimeFromProfile(player, BloodMoonNetwork.ClientGlobal.EventId);
        }
    }

    [HarmonyPatch(typeof(BloodMoonController), "FixedUpdate")]
    internal static class BloodMoonSkillReportRetryPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            BloodMoonSkillReportReliability.TickLocal(Time.fixedDeltaTime);
        }
    }
}
