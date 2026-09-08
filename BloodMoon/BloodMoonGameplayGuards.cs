using HarmonyLib;
using System;
using UnityEngine;
using static Seasons.Seasons;
using static Terminal;

namespace Seasons.BloodMoon
{
    [HarmonyPatch(typeof(Bed), nameof(Bed.Interact))]
    internal static class BloodMoonBedInteractPatch
    {
        private static bool Prefix(Humanoid human, ref bool __result)
        {
            if (human is not Player player || BloodMoonInteractionRules.CanSleep(player))
                return true;
            human.Message(MessageHud.MessageType.Center, "You cannot sleep while marked by the Blood Moon.");
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.Interact))]
    internal static class BloodMoonOfferingInteractPatch
    {
        private static bool Prefix(OfferingBowl __instance, Humanoid user, ref bool __result)
        {
            if (BloodMoonInteractionRules.CanUseBossOffering(__instance))
                return true;
            user?.Message(MessageHud.MessageType.Center, "Boss offerings are sealed during the Blood Moon.");
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.UseItem))]
    internal static class BloodMoonOfferingUseItemPatch
    {
        private static bool Prefix(OfferingBowl __instance, Humanoid user, ref bool __result)
        {
            if (BloodMoonInteractionRules.CanUseBossOffering(__instance))
                return true;
            user?.Message(MessageHud.MessageType.Center, "Boss offerings are sealed during the Blood Moon.");
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.InitiateSpawnBoss))]
    internal static class BloodMoonOfferingInitiateSpawnBossPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(OfferingBowl __instance, Vector3 point, bool removeItemsFromInventory)
        {
            if (!BloodMoonOfferingAuthority.ShouldRelay(__instance))
                return true;
            return !BloodMoonOfferingAuthority.TryRequest(__instance, point, removeItemsFromInventory);
        }
    }

    [HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.RPC_SpawnBoss))]
    internal static class BloodMoonOfferingSpawnBossPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(OfferingBowl __instance)
        {
            return BloodMoonOfferingAuthority.IsAuthorizedCompletion || BloodMoonInteractionRules.CanUseBossOffering(__instance);
        }
    }

    /// <summary>
    /// Diagnostic extras reuse the production extra lifecycle, so they must use the same event-frozen
    /// prefab identity that cleanup and recovery can verify later.
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
