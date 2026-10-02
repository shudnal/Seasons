using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace Seasons
{
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Start))]
    internal static class WearNTear_Start_SnowRuntime
    {
        [HarmonyPostfix]
        private static void Postfix(WearNTear __instance)
        {
            if (SeasonalSnow.WinterReady)
                SeasonalSnowController.Instance.RegisterSnow(__instance);
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.OnPlaced))]
    internal static class WearNTear_OnPlaced_SnowRuntime
    {
        [HarmonyPostfix]
        private static void Postfix(WearNTear __instance)
        {
            if (!SeasonalSnow.WinterReady)
                return;
            SeasonalSnowController.Instance.SnowPlaced(__instance);
            SeasonalSnowController.Instance.InvalidateSnowArea(__instance.transform.position, geometry: true,
                cause: SeasonalSnowController.SnowGeometryCause.Placed, source: __instance);
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.OnDestroy))]
    internal static class WearNTear_OnDestroy_SnowRuntime
    {
        [HarmonyPrefix]
        private static void Prefix(WearNTear __instance)
        {
            // ZNetView.ResetZDO already publishes and invalidates networked pieces.
            // OnDestroy only releases instance-owned caches.
            SeasonalSnowController.Instance.ForgetSnow(__instance);
            SeasonalSnowMeshSettings.Forget(__instance);
        }
    }

    // SetHealthVisual already calls UpdateSnowVisual, whose seasonal bridge updates
    // only this piece's cap. Damage variants do not invalidate surrounding cover.
    // Static obstacle lifetime is observed through AddInstance/ResetZDO/OnPlaced;
    // do not instrument every ZDO position/rotation write or native roof poll.
    [HarmonyPatch]
    internal static class WearNTear_NativeSnow_SkipSeasonal
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(WearNTear), nameof(WearNTear.ChangeSnow));
            yield return AccessTools.Method(typeof(WearNTear), nameof(WearNTear.RPC_SetSnow));
        }
        [HarmonyPrefix]
        private static bool Prefix(WearNTear __instance)
        {
            if (SeasonalSnowController.Instance.HasSnowRuntime(__instance))
                return false;
            return !SeasonalSnowMeshSettings.TryApplyDisabledSnow(__instance) &&
                !SeasonalSnow.IsSeasonalSnowPosition(__instance);
        }
    }

    [HarmonyPatch]
    internal static class WearNTear_NativeSnow_IsolateFields
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(WearNTear), nameof(WearNTear.Awake));
        }

        internal static void ClearNativeFields(WearNTear piece)
        {
            // This runs before and after native wear. A clean piece needs neither
            // prefab-name lookup nor seasonal eligibility/biome work.
            if (!piece || (piece.m_snowBuildup == 0f && !piece.m_addPreSnow && !piece.m_heavySnow))
                return;
            if (!SeasonalSnowController.Instance.HasSnowRuntime(piece) &&
                !SeasonalSnowMeshSettings.IsSnowDisabled(piece) && !SeasonalSnow.IsSeasonalSnowPosition(piece))
                return;
            // Legacy native ZDO values can still arrive from another owner during
            // migration. They must not become this instance's seasonal working state.
            piece.m_snowBuildup = 0f;
            piece.m_addPreSnow = false;
            piece.m_heavySnow = false;
        }

        [HarmonyPostfix, HarmonyPriority(Priority.First)]
        private static void Postfix(WearNTear __instance) => ClearNativeFields(__instance);
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateWear))]
    internal static class WearNTear_UpdateWear_SnowBoundary
    {
        private static List<CodeInstruction> GuardedClear(ILGenerator generator)
        {
            Label clear = generator.DefineLabel();
            Label done = generator.DefineLabel();
            List<CodeInstruction> code = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(SeasonalSnowController),
                    nameof(SeasonalSnowController.VisualBridgeActive))),
                new CodeInstruction(OpCodes.Brfalse, done),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(WearNTear), nameof(WearNTear.m_snowBuildup))),
                new CodeInstruction(OpCodes.Ldc_R4, 0f),
                new CodeInstruction(OpCodes.Bne_Un, clear),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(WearNTear), nameof(WearNTear.m_addPreSnow))),
                new CodeInstruction(OpCodes.Brtrue, clear),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(WearNTear), nameof(WearNTear.m_heavySnow))),
                new CodeInstruction(OpCodes.Brfalse, done),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(WearNTear_NativeSnow_IsolateFields),
                    nameof(WearNTear_NativeSnow_IsolateFields.ClearNativeFields))),
                new CodeInstruction(OpCodes.Nop)
            };
            code[12].labels.Add(clear);
            code[14].labels.Add(done);
            return code;
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            List<CodeInstruction> entry = GuardedClear(generator);
            entry[0].labels.AddRange(code[0].labels);
            code[0].labels.Clear();
            foreach (CodeInstruction instruction in entry)
                yield return instruction;
            foreach (CodeInstruction instruction in code)
            {
                if (instruction.opcode == OpCodes.Ret)
                {
                    List<CodeInstruction> exit = GuardedClear(generator);
                    exit[0].labels.AddRange(instruction.labels);
                    instruction.labels.Clear();
                    foreach (CodeInstruction check in exit)
                        yield return check;
                }
                yield return instruction;
            }
        }
    }

    [HarmonyPatch(typeof(EffectArea), nameof(EffectArea.Awake))]
    internal static class EffectArea_Awake_SnowHeat
    {
        [HarmonyPostfix]
        private static void Postfix(EffectArea __instance)
        {
            if (SeasonalSnow.WinterReady)
                SeasonalSnowController.Instance.RegisterHeatArea(__instance);
        }
    }

    [HarmonyPatch]
    internal static class EffectArea_Activity_SnowHeat
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(EffectArea), nameof(EffectArea.OnEnable));
            yield return AccessTools.Method(typeof(EffectArea), nameof(EffectArea.OnDisable));
        }
        [HarmonyPostfix]
        private static void Postfix(EffectArea __instance)
        {
            if (SeasonalSnow.WinterReady)
                SeasonalSnowController.Instance.HeatAreaChanged(__instance);
        }
    }

    [HarmonyPatch(typeof(EffectArea), nameof(EffectArea.OnDestroy))]
    internal static class EffectArea_OnDestroy_SnowHeat
    {
        [HarmonyPrefix]
        private static void Prefix(EffectArea __instance)
        {
            if (SeasonalSnowController.Instance.SnowPieceCount != 0)
                SeasonalSnowController.Instance.RemoveHeatArea(__instance);
        }
    }

    [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.PokeInUse))]
    internal static class CraftingStation_PokeInUse_SnowRuntime
    {
        [HarmonyPostfix]
        private static void Postfix(CraftingStation __instance)
        {
            if (SeasonalSnow.WinterReady)
                SeasonalSnowController.Instance.StationUsed(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.AttachStop))]
    internal static class Player_AttachStop_SnowRuntime
    {
        [HarmonyPrefix]
        private static void Prefix(Player __instance)
        {
            if (SeasonalSnow.WinterReady)
                SeasonalSnowController.Instance.StopAttachedSnowUse(__instance);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.AddInstance))]
    internal static class ZNetScene_AddInstance_SnowRegion
    {
        [HarmonyPostfix]
        private static void Postfix(ZDO zdo) => SeasonalSnowController.Instance.SnowObjectAdded(zdo);
    }

    [HarmonyPatch(typeof(ZNetView), nameof(ZNetView.ResetZDO))]
    internal static class ZNetView_ResetZDO_SnowSnapshot
    {
        [HarmonyPrefix]
        private static void Prefix(ZNetView __instance)
        {
            SeasonalSnowController controller = SeasonalSnowController.Instance;
            if (controller.SnowPieceCount == 0)
                return;
            ZDO zdo = __instance.GetZDO();
            if (zdo == null)
                return;
            bool geometry = controller.CanAffectSnowCover(zdo);
            controller.BeforeSnowViewReset(__instance);
            if (geometry)
                controller.InvalidateSnowArea(zdo.GetPosition(), geometry: true, readyOnly: true,
                    cause: SeasonalSnowController.SnowGeometryCause.ObjectRemoved, source: __instance, sourceZdo: zdo);
        }
    }

    [HarmonyPatch]
    internal static class ZDO_Receive_SnowSnapshot
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ZDO), nameof(ZDO.Deserialize));
            yield return AccessTools.Method(typeof(ZDO), nameof(ZDO.SetOwnerInternal));
        }
        private static void Received(ZDO zdo) => SeasonalSnowController.Instance.SnowSnapshotReceived(zdo);

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ret)
                {
                    Label done = generator.DefineLabel();
                    CodeInstruction check = new CodeInstruction(OpCodes.Ldsfld,
                        AccessTools.Field(typeof(SeasonalSnowController), nameof(SeasonalSnowController.VisualBridgeActive)));
                    check.labels.AddRange(instruction.labels);
                    instruction.labels.Clear();
                    instruction.labels.Add(done);
                    yield return check;
                    yield return new CodeInstruction(OpCodes.Brfalse, done);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(ZDO_Receive_SnowSnapshot), nameof(Received)));
                }
                yield return instruction;
            }
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.SetReferencePosition))]
    internal static class ZNet_ReferencePosition_SnowCheckpoint
    {
        private static void BeforeChange(Vector3 pos) =>
            SeasonalSnowController.Instance.BeforeSnowReferencePositionChanged(pos);

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            Label native = generator.DefineLabel();
            code[0].labels.Add(native);
            yield return new CodeInstruction(OpCodes.Ldsfld,
                AccessTools.Field(typeof(SeasonalSnowController), nameof(SeasonalSnowController.VisualBridgeActive)));
            yield return new CodeInstruction(OpCodes.Brfalse, native);
            yield return new CodeInstruction(OpCodes.Ldarg_1);
            yield return new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(ZNet_ReferencePosition_SnowCheckpoint), nameof(BeforeChange)));
            foreach (CodeInstruction instruction in code)
                yield return instruction;
        }
    }

    [HarmonyPatch(typeof(ZDO), nameof(ZDO.SetOwner))]
    internal static class ZDO_SetOwner_SnowCheckpoint
    {
        // Never write from SetOwnerInternal: RPC_ZDOData has already assigned the
        // incoming DataRevision there and will deserialize the incoming snapshot next.
        private static void BeforeChange(ZDO zdo, long uid) =>
            SeasonalSnowController.Instance.BeforeSnowOwnerChanged(zdo, uid);

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            Label native = generator.DefineLabel();
            code[0].labels.Add(native);
            yield return new CodeInstruction(OpCodes.Ldsfld,
                AccessTools.Field(typeof(SeasonalSnowController), nameof(SeasonalSnowController.VisualBridgeActive)));
            yield return new CodeInstruction(OpCodes.Brfalse, native);
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Ldarg_1);
            yield return new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(ZDO_SetOwner_SnowCheckpoint), nameof(BeforeChange)));
            foreach (CodeInstruction instruction in code)
                yield return instruction;
        }
    }

    [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.PrepareSave))]
    internal static class ZDOMan_PrepareSave_SnowSnapshot
    {
        [HarmonyPrefix]
        private static void Prefix() => SeasonalSnowController.Instance.FlushSnow();
    }

    [HarmonyPatch]
    internal static class Heightmap_Geometry_SnowCover
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Heightmap), nameof(Heightmap.RebuildCollisionMesh));
            yield return AccessTools.Method(typeof(Heightmap), nameof(Heightmap.OnDestroy));
        }
        [HarmonyPostfix]
        private static void Postfix(Heightmap __instance)
        {
            if (SeasonalSnowController.Instance.ObservesSnowGeometry && !__instance.IsDistantLod)
                SeasonalSnowController.Instance.InvalidateSnowArea(__instance.transform.position, geometry: true, readyOnly: true,
                    cause: SeasonalSnowController.SnowGeometryCause.TerrainChanged, source: __instance);
        }
    }

    [HarmonyPatch(typeof(MineRock), nameof(MineRock.RPC_Hide))]
    internal static class MineRock_Hide_SnowCover
    {
        [HarmonyPrefix]
        private static void Prefix(MineRock __instance, int index, out bool __state)
        {
            __state = false;
            if (!SeasonalSnowController.Instance.ObservesSnowGeometry)
                return;
            Collider area = __instance.GetHitArea(index);
            __state = area && area.gameObject.activeSelf;
        }

        [HarmonyPostfix]
        private static void Postfix(MineRock __instance, bool __state)
        {
            if (__state)
                SeasonalSnowController.Instance.InvalidateSnowArea(__instance.transform.position, geometry: true,
                    cause: SeasonalSnowController.SnowGeometryCause.RockChanged, source: __instance);
        }
    }

    [HarmonyPatch(typeof(MineRock), nameof(MineRock.UpdateVisability))]
    internal static class MineRock_Visibility_SnowCover
    {
        [HarmonyPrefix]
        private static void Prefix(MineRock __instance, out bool __state)
        {
            __state = false;
            if (!SeasonalSnowController.Instance.ObservesSnowGeometry ||
                !__instance.m_nview || !__instance.m_nview.IsValid() || __instance.m_hitAreas == null)
                return;
            ZDO zdo = __instance.m_nview.GetZDO();
            for (int i = 0; i < __instance.m_hitAreas.Length; ++i)
            {
                Collider area = __instance.m_hitAreas[i];
                if (area && area.gameObject.activeSelf != (zdo.GetFloat("Health" + i, __instance.GetHealth()) > 0f))
                {
                    __state = true;
                    return;
                }
            }
        }

        [HarmonyPostfix]
        private static void Postfix(MineRock __instance, bool __state)
        {
            if (__state)
                SeasonalSnowController.Instance.InvalidateSnowArea(__instance.transform.position, geometry: true,
                    cause: SeasonalSnowController.SnowGeometryCause.RockChanged, source: __instance);
        }
    }

    [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.UpdateMesh))]
    internal static class MineRock5_Mesh_SnowCover
    {
        [HarmonyPrefix]
        private static void Prefix(MineRock5 __instance, out bool __state)
        {
            __state = false;
            if (!SeasonalSnowController.Instance.ObservesSnowGeometry || __instance.m_hitAreas == null)
                return;
            foreach (MineRock5.HitArea area in __instance.m_hitAreas)
                if (area.m_collider && area.m_collider.gameObject.activeSelf != (area.m_health > 0f))
                {
                    __state = true;
                    return;
                }
        }

        [HarmonyPostfix]
        private static void Postfix(MineRock5 __instance, bool __state)
        {
            if (__state)
                SeasonalSnowController.Instance.InvalidateSnowArea(__instance.transform.position, geometry: true,
                    cause: SeasonalSnowController.SnowGeometryCause.RockChanged, source: __instance);
        }
    }

    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.UpdateState))]
    internal static class Fireplace_UpdateState_SnowHeat
    {
        private static void StateApplied(Fireplace fireplace, bool burning) =>
            SeasonalSnowController.Instance.FireplaceStateUpdated(fireplace, burning);

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            var isBurning = AccessTools.Method(typeof(Fireplace), nameof(Fireplace.IsBurning));
            int lastCall = code.FindLastIndex(instruction => instruction.Calls(isBurning));
            if (lastCall < 0)
            {
                Seasons.LogWarning("Could not observe Fireplace.UpdateState for seasonal snow: no IsBurning call found.");
                return code;
            }

            LocalBuilder burning = generator.DeclareLocal(typeof(bool));
            LocalBuilder observed = generator.DeclareLocal(typeof(bool));
            var active = AccessTools.Field(typeof(SeasonalSnowController), nameof(SeasonalSnowController.VisualBridgeActive));
            var notify = AccessTools.Method(typeof(Fireplace_UpdateState_SnowHeat), nameof(StateApplied));
            List<CodeInstruction> result = new List<CodeInstruction>(code.Count + 26);
            // An early return may bypass the selected call. Do not report a sample
            // unless that last call site was actually reached by this invocation.
            result.Add(new CodeInstruction(OpCodes.Ldc_I4_0));
            result.Add(new CodeInstruction(OpCodes.Stloc, observed));
            for (int i = 0; i < code.Count; ++i)
            {
                CodeInstruction instruction = code[i];
                if (instruction.opcode == OpCodes.Ret)
                {
                    Label done = generator.DefineLabel();
                    CodeInstruction check = new CodeInstruction(OpCodes.Ldsfld, active);
                    check.labels.AddRange(instruction.labels);
                    check.blocks.AddRange(instruction.blocks);
                    instruction.labels.Clear();
                    instruction.blocks.Clear();
                    instruction.labels.Add(done);
                    result.Add(check);
                    result.Add(new CodeInstruction(OpCodes.Brfalse, done));
                    result.Add(new CodeInstruction(OpCodes.Ldloc, observed));
                    result.Add(new CodeInstruction(OpCodes.Brfalse, done));
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    result.Add(new CodeInstruction(OpCodes.Ldloc, burning));
                    result.Add(new CodeInstruction(OpCodes.Call, notify));
                }
                result.Add(instruction);
                if (i == lastCall)
                {
                    // Observe only the last matching call site, preserving its native value.
                    result.Add(new CodeInstruction(OpCodes.Dup));
                    result.Add(new CodeInstruction(OpCodes.Stloc, burning));
                    result.Add(new CodeInstruction(OpCodes.Ldc_I4_1));
                    result.Add(new CodeInstruction(OpCodes.Stloc, observed));
                }
            }
            return result;
        }
    }
}
