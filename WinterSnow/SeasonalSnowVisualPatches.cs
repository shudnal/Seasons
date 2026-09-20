using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
using static Seasons.Seasons;

namespace Seasons
{
    // Keep the existing saved/predicted calculation as the producer during the staged
    // refactor. The last prefix consumes its chosen value and replaces only native drawing.
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateSnowVisual))]
    internal static class WearNTear_UpdateSnowVisual_PooledCaps
    {
        [HarmonyPrefix, HarmonyPriority(Priority.Last)]
        private static bool Prefix(WearNTear __instance) =>
            !SeasonalSnowController.Instance.TryQueueCurrentVisual(__instance);
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateWear))]
    internal static class WearNTear_UpdateWear_ExcludeSnowFromWetVisuals
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var wetField = AccessTools.Field(typeof(WearNTear), nameof(WearNTear.m_wet));
            var getWetVisual = AccessTools.Method(typeof(SeasonalSnowController), nameof(SeasonalSnowController.GetWetVisual));
            bool found = false;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.LoadsField(wetField))
                {
                    // WearNTear -> GameObject has the same stack effect as the field load.
                    // Retain labels and exception blocks on the replaced instruction.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = getWetVisual;
                    found = true;
                }
                yield return instruction;
            }
            if (!found)
                LogWarning("Failed to isolate WearNTear.UpdateWear wet visuals from managed snow caps.");
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Awake))]
    internal static class WearNTear_Awake_PooledCaps
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(WearNTear __instance)
        {
            // Native Awake hides caps after UpdateVisual and expands their bounds.
            // Force one final application without reading a second/predicted level here.
            SeasonalSnowController.Instance.InvalidateVisual(__instance);
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.OnDestroy))]
    internal static class WearNTear_OnDestroy_PooledCaps
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(WearNTear __instance) =>
            SeasonalSnowController.Instance.ReleaseVisual(__instance, hide: false, restoreNative: false);
    }

    [HarmonyPatch(typeof(MaterialMan.PropertyContainer), nameof(MaterialMan.PropertyContainer.RefreshRenderers))]
    internal static class MaterialMan_RefreshRenderers_ExcludeSnowCaps
    {
        [HarmonyPostfix]
        private static void Postfix(MaterialMan.PropertyContainer __instance) =>
            SeasonalSnowController.Instance.FilterMaterialManRenderers(__instance);
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Update))]
    internal static class ZNetScene_Update_SnowVisuals
    {
        [HarmonyPostfix]
        private static void Postfix(ZNetScene __instance)
        {
            // The native Update can return early while Harmony still runs postfixes.
            if (Game.IsPaused() || UnityEngine.Time.timeScale <= 0f)
                return;
            SeasonalSnowController.Instance.UpdateVisuals(__instance);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Shutdown))]
    internal static class ZNetScene_Shutdown_SnowVisuals
    {
        [HarmonyPrefix]
        private static void Prefix(ZNetScene __instance) => SeasonalSnowController.Instance.StopVisuals(__instance);
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.OnDestroy))]
    internal static class ZNetScene_OnDestroy_SnowVisuals
    {
        [HarmonyPrefix]
        private static void Prefix(ZNetScene __instance) => SeasonalSnowController.Instance.StopVisuals(__instance);
    }
}
