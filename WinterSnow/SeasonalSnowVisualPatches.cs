using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    // Seasonal caps consume the singleton's value without substituting a native field.
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateSnowVisual))]
    internal static class WearNTear_UpdateSnowVisual_PooledCaps
    {
        private static bool RouteVisual(WearNTear piece)
        {
            SeasonalSnowController controller = SeasonalSnowController.Instance;
            if (!SeasonalSnow.WinterReady && !controller.HasSnowRuntime(piece) &&
                !controller.HasSnowVisual(piece))
            {
                if (piece.m_snowBuildup > 0f || piece.m_addPreSnow)
                {
                    if (SeasonalSnowMeshSettings.TryApplyDisabledSnow(piece))
                        return true;
                    SeasonalSnow.ClearInactiveLoadedSnow(piece);
                }
                return false;
            }
            return controller.TryQueueCurrentVisual(piece);
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            // One native branch remains; dormant calls do not enter a Seasons helper.
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            Label native = generator.DefineLabel();
            code[0].labels.Add(native);
            yield return new CodeInstruction(OpCodes.Ldsfld,
                AccessTools.Field(typeof(SeasonalSnowController), nameof(SeasonalSnowController.VisualBridgeActive)));
            yield return new CodeInstruction(OpCodes.Brfalse, native);
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(WearNTear_UpdateSnowVisual_PooledCaps), nameof(RouteVisual)));
            yield return new CodeInstruction(OpCodes.Brfalse, native);
            yield return new CodeInstruction(OpCodes.Ret);
            foreach (CodeInstruction instruction in code)
                yield return instruction;
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateWear))]
    internal static class WearNTear_UpdateWear_ExcludeSnowFromWetVisuals
    {
        private static GameObject GetWetVisual(WearNTear piece)
        {
            GameObject wet = SeasonalSnowController.GetWetVisual(piece);
            if (!wet)
                return null;

            // Disabled caps are usually unbound and therefore absent from the visual
            // controller. Resolve the rule only for the rare direct wet/cap alias.
            bool aliasesCap = (piece.m_snow && piece.m_snow.gameObject == wet) ||
                (piece.m_snowWorn && piece.m_snowWorn.gameObject == wet) ||
                (piece.m_snowBroken && piece.m_snowBroken.gameObject == wet);
            return aliasesCap && SeasonalSnowMeshSettings.IsSnowDisabled(piece) ? null : wet;
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var wetField = AccessTools.Field(typeof(WearNTear), nameof(WearNTear.m_wet));
            var getWetVisual = AccessTools.Method(typeof(WearNTear_UpdateWear_ExcludeSnowFromWetVisuals), nameof(GetWetVisual));
            var bridge = AccessTools.Field(typeof(SeasonalSnowController), nameof(SeasonalSnowController.VisualBridgeActive));
            var gameObject = AccessTools.PropertyGetter(typeof(Component), nameof(Component.gameObject));
            FieldInfo[] caps =
            {
                AccessTools.Field(typeof(WearNTear), nameof(WearNTear.m_snow)),
                AccessTools.Field(typeof(WearNTear), nameof(WearNTear.m_snowWorn)),
                AccessTools.Field(typeof(WearNTear), nameof(WearNTear.m_snowBroken))
            };
            LocalBuilder piece = generator.DeclareLocal(typeof(WearNTear));
            LocalBuilder wet = generator.DeclareLocal(typeof(GameObject));
            LocalBuilder cap = generator.DeclareLocal(typeof(MeshRenderer));
            bool found = false;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.LoadsField(wetField))
                {
                    Label useHelper = generator.DefineLabel();
                    Label useNative = generator.DefineLabel();
                    Label done = generator.DefineLabel();
                    CodeInstruction start = new CodeInstruction(OpCodes.Stloc, piece);
                    start.labels.AddRange(instruction.labels);
                    start.blocks.AddRange(instruction.blocks);
                    yield return start;
                    yield return new CodeInstruction(OpCodes.Ldsfld, bridge);
                    yield return new CodeInstruction(OpCodes.Brtrue, useHelper);
                    yield return new CodeInstruction(OpCodes.Ldloc, piece);
                    yield return new CodeInstruction(OpCodes.Ldfld, wetField);
                    yield return new CodeInstruction(OpCodes.Stloc, wet);
                    yield return new CodeInstruction(OpCodes.Ldloc, wet);
                    yield return new CodeInstruction(OpCodes.Brfalse, useNative);
                    foreach (FieldInfo field in caps)
                    {
                        Label nextCap = generator.DefineLabel();
                        yield return new CodeInstruction(OpCodes.Ldloc, piece);
                        yield return new CodeInstruction(OpCodes.Ldfld, field);
                        yield return new CodeInstruction(OpCodes.Stloc, cap);
                        yield return new CodeInstruction(OpCodes.Ldloc, cap);
                        yield return new CodeInstruction(OpCodes.Brfalse, nextCap);
                        yield return new CodeInstruction(OpCodes.Ldloc, cap);
                        yield return new CodeInstruction(OpCodes.Callvirt, gameObject);
                        yield return new CodeInstruction(OpCodes.Ldloc, wet);
                        yield return new CodeInstruction(OpCodes.Ceq);
                        yield return new CodeInstruction(OpCodes.Brtrue, useHelper);
                        CodeInstruction next = new CodeInstruction(OpCodes.Nop);
                        next.labels.Add(nextCap);
                        yield return next;
                    }
                    CodeInstruction native = new CodeInstruction(OpCodes.Ldloc, wet);
                    native.labels.Add(useNative);
                    yield return native;
                    yield return new CodeInstruction(OpCodes.Br, done);
                    CodeInstruction helper = new CodeInstruction(OpCodes.Ldloc, piece);
                    helper.labels.Add(useHelper);
                    yield return helper;
                    yield return new CodeInstruction(OpCodes.Call, getWetVisual);
                    CodeInstruction end = new CodeInstruction(OpCodes.Nop);
                    end.labels.Add(done);
                    yield return end;
                    found = true;
                    continue;
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
            // Cleanup does not require area readiness, roof casts or a winter runtime.
            SeasonalSnow.ClearInactiveLoadedSnow(__instance);
            if (!SeasonalSnow.WinterReady)
                return;
            // Native Awake hides caps after UpdateVisual and expands their bounds.
            // Select saved data (including zero) before any prediction or ready-area work.
            SeasonalSnow.CaptureInitialSnowVisual(__instance);
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

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
    internal static class ZNetScene_Awake_SnowLifecycle
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            SeasonalSnowController.Instance.ReconcileLifecycle();
            SeasonalIceFloes.ReconcilePolicy();
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Shutdown))]
    internal static class ZNetScene_Shutdown_SnowVisuals
    {
        [HarmonyPrefix]
        private static void Prefix(ZNetScene __instance)
        {
            SeasonalSnowController.Instance.StopSnowScene(__instance);
            SeasonalSnowController.Instance.StopVisuals(__instance);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.OnDestroy))]
    internal static class ZNetScene_OnDestroy_SnowVisuals
    {
        [HarmonyPrefix]
        private static void Prefix(ZNetScene __instance)
        {
            SeasonalSnowController.Instance.StopSnowScene(__instance);
            SeasonalSnowController.Instance.StopVisuals(__instance);
        }
    }

    [DefaultExecutionOrder(1000)]
    internal sealed class SeasonalSnowDriver : MonoBehaviour
    {
        private ZNetScene scene;

        private void Awake() => scene = GetComponent<ZNetScene>();

        private void Update()
        {
            if (SeasonalSnow.WinterReady && (Game.IsPaused() || Time.timeScale <= 0f))
                return;
            SeasonalSnowController controller = SeasonalSnowController.Instance;
            if (SeasonalSnow.WinterReady)
            {
                controller.SnowSceneObjectsChanged();
                controller.AttachedObjectUsed(Player.m_localPlayer);
            }
            controller.UpdateSnowSimulation(scene);
            controller.UpdateVisuals(scene);
            controller.CompleteDormancy();
        }
    }
}
