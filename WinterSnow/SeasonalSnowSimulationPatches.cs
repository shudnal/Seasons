using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Seasons
{
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Start))]
    internal static class WearNTear_Start_SnowRuntime
    {
        [HarmonyPostfix]
        private static void Postfix(WearNTear __instance)
        {
            SeasonalSnowMeshSettings.Apply(__instance);
            SeasonalSnowController.Instance.RegisterSnow(__instance);
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.OnPlaced))]
    internal static class WearNTear_OnPlaced_SnowRuntime
    {
        [HarmonyPostfix]
        private static void Postfix(WearNTear __instance)
        {
            SeasonalSnowController.Instance.SnowPlaced(__instance);
            SeasonalSnowController.Instance.InvalidateSnowArea(__instance.transform.position, geometry: true);
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.OnDestroy))]
    internal static class WearNTear_OnDestroy_SnowRuntime
    {
        [HarmonyPrefix]
        private static void Prefix(WearNTear __instance)
        {
            SeasonalSnowController.Instance.ForgetSnow(__instance);
            SeasonalSnowController.Instance.InvalidateSnowArea(__instance.transform.position, geometry: true);
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateCover))]
    internal static class WearNTear_UpdateCover_SnowHint
    {
        [HarmonyPrefix]
        private static void Prefix(WearNTear __instance, out bool __state) => __state = __instance.m_haveRoof;
        [HarmonyPostfix]
        private static void Postfix(WearNTear __instance, bool __state)
        {
            if (__state != __instance.m_haveRoof)
                SeasonalSnowController.Instance.SnowCoverHintChanged(__instance);
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.SetHealthVisual))]
    internal static class WearNTear_SetHealthVisual_SnowGeometry
    {
        [HarmonyPrefix]
        private static void Prefix(WearNTear __instance, out int __state) =>
            __state = SeasonalSnowController.HealthGeometryMask(__instance);
        [HarmonyPostfix]
        private static void Postfix(WearNTear __instance, int __state) =>
            SeasonalSnowController.Instance.HealthGeometryChanged(__instance, __state);
    }

    [HarmonyPatch]
    internal static class WearNTear_NativeSnow_SkipSeasonal
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(WearNTear), nameof(WearNTear.ChangeSnow));
            yield return AccessTools.Method(typeof(WearNTear), nameof(WearNTear.RPC_SetSnow));
        }
        [HarmonyPrefix]
        private static bool Prefix(WearNTear __instance) =>
            !SeasonalSnowMeshSettings.TryApplyDisabledSnow(__instance) && !SeasonalSnow.IsSeasonalSnowPosition(__instance);
    }

    [HarmonyPatch(typeof(EffectArea), nameof(EffectArea.Awake))]
    internal static class EffectArea_Awake_SnowHeat
    {
        [HarmonyPostfix]
        private static void Postfix(EffectArea __instance) => SeasonalSnowController.Instance.RegisterHeatArea(__instance);
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
        private static void Postfix(EffectArea __instance) => SeasonalSnowController.Instance.HeatAreaChanged(__instance);
    }

    [HarmonyPatch(typeof(EffectArea), nameof(EffectArea.OnDestroy))]
    internal static class EffectArea_OnDestroy_SnowHeat
    {
        [HarmonyPrefix]
        private static void Prefix(EffectArea __instance) => SeasonalSnowController.Instance.RemoveHeatArea(__instance);
    }

    [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.PokeInUse))]
    internal static class CraftingStation_PokeInUse_SnowRuntime
    {
        [HarmonyPostfix]
        private static void Postfix(CraftingStation __instance) => SeasonalSnowController.Instance.StationUsed(__instance);
    }

    [HarmonyPatch(typeof(Player), nameof(Player.UpdateAttach))]
    internal static class Player_UpdateAttach_SnowRuntime
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance) => SeasonalSnowController.Instance.AttachedObjectUsed(__instance);
    }

    [HarmonyPatch(typeof(Character), nameof(Character.AttachStop))]
    internal static class Character_AttachStop_SnowRuntime
    {
        [HarmonyPrefix]
        private static void Prefix(Character __instance)
        {
            if (__instance is Player player)
                SeasonalSnowController.Instance.StopAttachedSnowUse(player);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.AddInstance))]
    internal static class ZNetScene_AddInstance_SnowRegion
    {
        [HarmonyPostfix]
        private static void Postfix(ZDO zdo)
        {
            if (zdo != null)
                SeasonalSnowController.Instance.InvalidateSnowArea(zdo.GetPosition(),
                    geometry: zdo.Type == ZDO.ObjectType.Solid || zdo.Type == ZDO.ObjectType.Terrain);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.CreateDestroyObjects))]
    internal static class ZNetScene_CreateDestroyObjects_SnowReadiness
    {
        [HarmonyPostfix]
        private static void Postfix() => SeasonalSnowController.Instance.SnowSceneObjectsChanged();
    }

    [HarmonyPatch(typeof(ZNetView), nameof(ZNetView.ResetZDO))]
    internal static class ZNetView_ResetZDO_SnowSnapshot
    {
        [HarmonyPrefix]
        private static void Prefix(ZNetView __instance)
        {
            ZDO zdo = __instance.GetZDO();
            if (zdo == null)
                return;
            SeasonalSnowController.Instance.BeforeSnowViewReset(__instance);
            SeasonalSnowController.Instance.InvalidateSnowArea(zdo.GetPosition(),
                geometry: zdo.Type == ZDO.ObjectType.Solid || zdo.Type == ZDO.ObjectType.Terrain);
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
        [HarmonyPostfix]
        private static void Postfix(ZDO __instance) => SeasonalSnowController.Instance.SnowSnapshotReceived(__instance);
    }

    [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.PrepareSave))]
    internal static class ZDOMan_PrepareSave_SnowSnapshot
    {
        [HarmonyPrefix]
        private static void Prefix() => SeasonalSnowController.Instance.FlushSnow();
    }

    [HarmonyPatch(typeof(Door), nameof(Door.SetState))]
    internal static class Door_SetState_SnowCover
    {
        [HarmonyPrefix]
        private static void Prefix(Door __instance, out int __state) =>
            __state = __instance.m_animator ? __instance.m_animator.GetInteger("state") : int.MinValue;
        [HarmonyPostfix]
        private static void Postfix(Door __instance, int state, int __state)
        {
            if (state != __state)
                SeasonalSnowController.Instance.SnowDoorChanged(__instance);
        }
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
            if (!__instance.IsDistantLod)
                SeasonalSnowController.Instance.InvalidateSnowArea(__instance.transform.position, geometry: true);
        }
    }
}
