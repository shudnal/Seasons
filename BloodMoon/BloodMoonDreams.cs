using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonDreams
    {
        private const string PendingPrefix = "Seasons.BloodMoon.PendingDream.";
        private const string PresentedPrefix = "Seasons.BloodMoon.DreamPresented.";

        internal static bool IsPresented(Player player, long eventId)
        {
            long worldUid = ZNet.m_world != null ? ZNet.m_world.m_uid : 0L;
            return player != null && worldUid != 0L && eventId >= 0L &&
                player.m_customData.ContainsKey(GetPresentedKey(worldUid, eventId));
        }

        internal static bool Present(Player player, long eventId, string chronicle)
        {
            long worldUid = ZNet.m_world != null ? ZNet.m_world.m_uid : 0L;
            if (player == null || player != Player.m_localPlayer || worldUid == 0L || eventId < 0L || string.IsNullOrWhiteSpace(chronicle))
                return false;
            if (IsPresented(player, eventId))
                return true;

            string text = SelectDreamText(chronicle);
            if (string.IsNullOrWhiteSpace(text))
            {
                LogWarning($"[BloodMoon.Outcome] No DreamText variant matched event {eventId}; outcome remains pending.");
                return false;
            }

            string key = GetPendingKey(worldUid, eventId);
            if (!player.m_customData.TryGetValue(key, out string existing) || !string.Equals(existing, text, StringComparison.Ordinal))
                player.m_customData[key] = text;
            return true;
        }

        internal static void CleanupTransientPresentation()
        {
            // Blood Moon dreams are profile-backed pending records and use vanilla SleepText.
            // There is no transient presenter to destroy on world changes.
        }

        internal static bool TryGetPending(out long worldUid, out long eventId, out string text)
        {
            worldUid = ZNet.m_world != null ? ZNet.m_world.m_uid : 0L;
            eventId = -1L;
            text = string.Empty;

            Player player = Player.m_localPlayer;
            if (player == null || worldUid == 0L || player.m_customData == null)
                return false;

            string prefix = PendingPrefix + worldUid.ToString(CultureInfo.InvariantCulture) + ".";
            KeyValuePair<string, string> pending = player.m_customData
                .Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(entry => new
                {
                    Entry = entry,
                    EventId = long.TryParse(entry.Key.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
                        ? parsed
                        : -1L
                })
                .Where(item => item.EventId >= 0L && !string.IsNullOrWhiteSpace(item.Entry.Value))
                .OrderBy(item => item.EventId)
                .Select(item => item.Entry)
                .FirstOrDefault();

            if (string.IsNullOrEmpty(pending.Key))
                return false;

            if (!long.TryParse(pending.Key.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out eventId))
                return false;

            text = pending.Value;
            return true;
        }

        internal static void MarkPresented(long worldUid, long eventId)
        {
            Player player = Player.m_localPlayer;
            if (player == null || worldUid == 0L || eventId < 0L || ZNet.m_world == null || ZNet.m_world.m_uid != worldUid)
                return;

            player.m_customData.Remove(GetPendingKey(worldUid, eventId));
            player.m_customData[GetPresentedKey(worldUid, eventId)] = "1";
            BloodMoonOutcomeQueue.OnLocalDreamPresentationCompleted(worldUid, eventId, player.GetPlayerID());
            Game.instance?.SavePlayerProfile(false);
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

        private static string GetPendingKey(long worldUid, long eventId)
        {
            return PendingPrefix + worldUid.ToString(CultureInfo.InvariantCulture) + "." + eventId.ToString(CultureInfo.InvariantCulture);
        }

        private static string GetPresentedKey(long worldUid, long eventId)
        {
            return PresentedPrefix + worldUid.ToString(CultureInfo.InvariantCulture) + "." + eventId.ToString(CultureInfo.InvariantCulture);
        }
    }

    [HarmonyPatch(typeof(SleepText), nameof(SleepText.ShowDreamText))]
    internal static class BloodMoonSleepDreamPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(SleepText __instance)
        {
            if (__instance == null || __instance.m_dreamField == null ||
                !BloodMoonDreams.TryGetPending(out long worldUid, out long eventId, out string text))
                return true;

            __instance.m_dreamField.text = Localization.instance != null ? Localization.instance.Localize(text) : text;
            __instance.m_dreamField.enabled = true;
            __instance.Invoke(nameof(SleepText.DelayedCrossFadeStart), 0.1f);
            __instance.Invoke(nameof(SleepText.HideDreamText), 6.5f);
            BloodMoonDreams.MarkPresented(worldUid, eventId);
            LogInfo($"[BloodMoon.Outcome] Presented pending DreamText for event {eventId} through vanilla SleepText.");
            return false;
        }
    }
}
