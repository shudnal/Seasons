using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonDreams
    {
        private const string RecordPrefix = "Seasons.BloodMoon.Dream.";

        [Serializable]
        private sealed class DreamRecord
        {
            public long WorldUid;
            public long EventId;
            public string Text = string.Empty;
            public bool Consumed;
        }

        internal static void Record(Player player, long eventId, string chronicle)
        {
            long worldUid = ZNet.m_world != null ? ZNet.m_world.m_uid : 0L;
            if (player == null || worldUid == 0L || eventId < 0L || string.IsNullOrWhiteSpace(chronicle))
                return;

            string key = GetKey(worldUid, eventId);
            if (player.m_customData.ContainsKey(key))
                return;

            string text = SelectDreamText(chronicle);
            if (string.IsNullOrEmpty(text))
                return;

            player.m_customData[key] = JsonConvert.SerializeObject(new DreamRecord
            {
                WorldUid = worldUid,
                EventId = eventId,
                Text = text,
                Consumed = false
            });
        }

        private static bool TryConsume(Player player, out DreamTexts.DreamText dream)
        {
            dream = null;
            long worldUid = ZNet.m_world != null ? ZNet.m_world.m_uid : 0L;
            if (player == null || worldUid == 0L)
                return false;

            List<KeyValuePair<string, DreamRecord>> records = new List<KeyValuePair<string, DreamRecord>>();
            foreach (KeyValuePair<string, string> entry in player.m_customData)
            {
                if (!entry.Key.StartsWith(RecordPrefix, StringComparison.Ordinal))
                    continue;
                DreamRecord record;
                try
                {
                    record = JsonConvert.DeserializeObject<DreamRecord>(entry.Value);
                }
                catch
                {
                    continue;
                }
                if (record != null && !record.Consumed && record.WorldUid == worldUid && record.EventId >= 0L && !string.IsNullOrWhiteSpace(record.Text))
                    records.Add(new KeyValuePair<string, DreamRecord>(entry.Key, record));
            }

            KeyValuePair<string, DreamRecord> selected = records.OrderBy(entry => entry.Value.EventId).FirstOrDefault();
            if (selected.Value == null)
                return false;

            selected.Value.Consumed = true;
            player.m_customData[selected.Key] = JsonConvert.SerializeObject(selected.Value);
            dream = new DreamTexts.DreamText
            {
                m_text = selected.Value.Text,
                m_chanceToDream = 1f
            };
            return true;
        }

        private static string SelectDreamText(string chronicle)
        {
            if (chronicle.Contains("Success, later defeated"))
                return "You dream of reaching the end of a crimson road, though the night takes its final toll.";
            if (chronicle.Contains("Success, later withdrawn"))
                return "You dream of standing beneath the fading red moon, then choosing a road away from its last shadows.";
            if (chronicle.Contains("Success, later disconnected"))
                return "You dream of surviving the crimson night before the memory breaks apart into silence.";
            if (chronicle.Contains("— Success"))
                return "You dream of a red moon sinking beyond the horizon, and of standing when its light finally fades.";
            if (chronicle.Contains("— Defeated"))
                return "You dream of red light closing over you, then waking before the final darkness.";
            if (chronicle.Contains("— Withdrawn"))
                return "You dream of turning from a blood-red horizon while distant horns fade behind you.";
            if (chronicle.Contains("— Disconnected"))
                return "You dream of a crimson night that breaks apart before its ending can be seen.";
            if (chronicle.Contains("Survived until dawn"))
                return "You dream of the red moon paling into dawn while the last shadows retreat.";
            if (chronicle.Contains("— Survived"))
                return "You dream of a long crimson night finally giving way to morning.";
            return string.Empty;
        }

        private static string GetKey(long worldUid, long eventId)
        {
            return RecordPrefix + worldUid.ToString(CultureInfo.InvariantCulture) + "." + eventId.ToString(CultureInfo.InvariantCulture);
        }

        [HarmonyPatch(typeof(DreamTexts), nameof(DreamTexts.GetRandomDreamText))]
        private static class DreamTextsGetRandomDreamTextPatch
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(ref DreamTexts.DreamText __result)
            {
                if (!TryConsume(Player.m_localPlayer, out DreamTexts.DreamText dream))
                    return true;
                __result = dream;
                return false;
            }
        }
    }
}
