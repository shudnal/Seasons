using HarmonyLib;
using System;
using UnityEngine;
using static Seasons.Seasons;
using static Terminal;

namespace Seasons.BloodMoon
{
    /// <summary>
    /// Diagnostic extras deliberately reuse the production extra lifecycle, so they must use the same
    /// event-frozen prefab identity that cleanup/recovery can verify later.
    /// </summary>
    [HarmonyPatch(typeof(BloodMoonDiagnostics), "Spawn")]
    internal static class BloodMoonDiagnosticSpawnGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ConsoleEventArgs args)
        {
            if (args == null || args.Length < 4)
                return true;

            BloodMoonEventState state = BloodMoonController.Instance?.State;
            if (state == null || !state.IsCombatLive || ZNetScene.instance == null)
                return true;

            if (BloodMoonSpawner.FreezeSpawnPool(state))
                BloodMoonPersistence.Save(state);

            string frozenPrefabName = BloodMoonSpawner.GetFrozenSpawnPrefabName(state);
            GameObject requested = ZNetScene.instance.GetPrefab(args[3]);
            if (requested != null && !string.IsNullOrEmpty(frozenPrefabName) &&
                string.Equals(requested.name, frozenPrefabName, StringComparison.Ordinal))
                return true;

            string message = string.IsNullOrEmpty(frozenPrefabName)
                ? "Diagnostic Blood Moon spawn is unavailable because this event has no frozen extra-enemy prefab."
                : $"Diagnostic Blood Moon spawn is restricted to the event-frozen prefab '{frozenPrefabName}'.";
            args.Context?.AddString(message);
            LogInfo($"[BloodMoon.Diagnostics] {message}");
            return false;
        }
    }
}
