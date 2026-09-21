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
            SeasonalSnowMeshSettings.Forget(__instance);
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

    [HarmonyPatch]
    internal static class WearNTear_NativeSnow_IsolateFields
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(WearNTear), nameof(WearNTear.Awake));
            yield return AccessTools.Method(typeof(WearNTear), nameof(WearNTear.UpdateWear));
        }

        internal static void ClearNativeFields(WearNTear piece)
        {
            if (!SeasonalSnowMeshSettings.IsSnowDisabled(piece) && !SeasonalSnow.IsSeasonalSnowPosition(piece))
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
        [HarmonyPrefix]
        private static void Prefix(WearNTear __instance) => WearNTear_NativeSnow_IsolateFields.ClearNativeFields(__instance);
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

    [HarmonyPatch(typeof(Player), nameof(Player.AttachStop))]
    internal static class Player_AttachStop_SnowRuntime
    {
        [HarmonyPrefix]
        private static void Prefix(Player __instance) => SeasonalSnowController.Instance.StopAttachedSnowUse(__instance);
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.AddInstance))]
    internal static class ZNetScene_AddInstance_SnowRegion
    {
        [HarmonyPostfix]
        private static void Postfix(ZDO zdo)
        {
            if (zdo != null)
                // Default-type objects may contain tree/rock/destructible colliders too.
                SeasonalSnowController.Instance.InvalidateSnowArea(zdo.GetPosition(), geometry: true);
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
            SeasonalSnowController.Instance.InvalidateSnowArea(zdo.GetPosition(), geometry: true);
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

    [HarmonyPatch]
    internal static class ZDO_Transform_SnowGeometry
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ZDO), nameof(ZDO.InternalSetPosition));
            yield return AccessTools.Method(typeof(ZDO), nameof(ZDO.SetRotation));
            yield return AccessTools.Method(typeof(ZDO), nameof(ZDO.Deserialize));
        }

        [HarmonyPrefix]
        private static void Prefix(ZDO __instance, out KeyValuePair<Vector3, Quaternion> __state) =>
            __state = new KeyValuePair<Vector3, Quaternion>(__instance.GetPosition(), __instance.GetRotation());

        [HarmonyPostfix]
        private static void Postfix(ZDO __instance, KeyValuePair<Vector3, Quaternion> __state) =>
            SeasonalSnowController.Instance.SnowObjectTransformChanged(__instance, __state.Key, __state.Value);
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

    [HarmonyPatch(typeof(MineRock), nameof(MineRock.RPC_Hide))]
    internal static class MineRock_Hide_SnowCover
    {
        [HarmonyPrefix]
        private static void Prefix(MineRock __instance, int index, out bool __state)
        {
            Collider area = __instance.GetHitArea(index);
            __state = area && area.gameObject.activeSelf;
        }

        [HarmonyPostfix]
        private static void Postfix(MineRock __instance, bool __state)
        {
            if (__state)
                SeasonalSnowController.Instance.InvalidateSnowArea(__instance.transform.position, geometry: true);
        }
    }

    [HarmonyPatch(typeof(MineRock), nameof(MineRock.UpdateVisability))]
    internal static class MineRock_Visibility_SnowCover
    {
        [HarmonyPrefix]
        private static void Prefix(MineRock __instance, out bool __state)
        {
            __state = false;
            if (!__instance.m_nview || !__instance.m_nview.IsValid() || __instance.m_hitAreas == null)
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
                SeasonalSnowController.Instance.InvalidateSnowArea(__instance.transform.position, geometry: true);
        }
    }

    [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.UpdateMesh))]
    internal static class MineRock5_Mesh_SnowCover
    {
        [HarmonyPrefix]
        private static void Prefix(MineRock5 __instance, out bool __state)
        {
            __state = false;
            if (__instance.m_hitAreas == null)
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
                SeasonalSnowController.Instance.InvalidateSnowArea(__instance.transform.position, geometry: true);
        }
    }

}
