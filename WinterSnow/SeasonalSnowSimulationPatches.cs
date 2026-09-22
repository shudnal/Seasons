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
            SeasonalSnowController.Instance.InvalidateSnowArea(__instance.transform.position, geometry: true);
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

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateCover))]
    internal static class WearNTear_UpdateCover_SnowHint
    {
        [HarmonyPrefix]
        private static void Prefix(WearNTear __instance, out int __state) =>
            __state = SeasonalSnow.WinterReady ? (__instance.m_haveRoof ? 1 : 0) : -1;

        [HarmonyPostfix]
        private static void Postfix(WearNTear __instance, int __state)
        {
            if (__state >= 0 && (__state != 0) != __instance.m_haveRoof)
                SeasonalSnowController.Instance.SnowCoverHintChanged(__instance);
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.SetHealthVisual))]
    internal static class WearNTear_SetHealthVisual_SnowGeometry
    {
        [HarmonyPrefix]
        private static void Prefix(WearNTear __instance, out int __state) =>
            __state = SeasonalSnowController.Instance.ObservesSnowGeometry
                ? SeasonalSnowController.HealthGeometryMask(__instance) : -1;
        [HarmonyPostfix]
        private static void Postfix(WearNTear __instance, int __state)
        {
            if (__state >= 0)
                SeasonalSnowController.Instance.HealthGeometryChanged(__instance, __state);
        }
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
            yield return AccessTools.Method(typeof(WearNTear), nameof(WearNTear.UpdateWear));
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
        [HarmonyPrefix]
        private static void Prefix(WearNTear __instance) => WearNTear_NativeSnow_IsolateFields.ClearNativeFields(__instance);
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

    [HarmonyPatch(typeof(Player), nameof(Player.UpdateAttach))]
    internal static class Player_UpdateAttach_SnowRuntime
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            if (SeasonalSnow.WinterReady)
                SeasonalSnowController.Instance.AttachedObjectUsed(__instance);
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
            SeasonalSnowController controller = SeasonalSnowController.Instance;
            if (controller.SnowPieceCount == 0)
                return;
            ZDO zdo = __instance.GetZDO();
            if (zdo == null)
                return;
            bool geometry = controller.CanAffectSnowCover(zdo);
            controller.BeforeSnowViewReset(__instance);
            if (geometry)
                controller.InvalidateSnowArea(zdo.GetPosition(), geometry: true, readyOnly: true);
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
        private struct TransformState
        {
            internal bool Observed;
            internal Vector3 Position;
            internal Vector3 Rotation;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ZDO), nameof(ZDO.InternalSetPosition));
            yield return AccessTools.Method(typeof(ZDO), nameof(ZDO.SetRotation));
            yield return AccessTools.Method(typeof(ZDO), nameof(ZDO.Deserialize));
        }

        [HarmonyPrefix]
        private static void Prefix(ZDO __instance, out TransformState __state)
        {
            __state = default;
            if (!SeasonalSnowController.Instance.ShouldObserveSnowTransform(__instance))
                return;
            __state.Observed = true;
            __state.Position = __instance.GetPosition();
            // Compare stored Euler vectors without constructing a Quaternion per ZDO.
            __state.Rotation = __instance.m_rotation;
        }

        [HarmonyPostfix]
        private static void Postfix(ZDO __instance, TransformState __state)
        {
            if (__state.Observed)
                SeasonalSnowController.Instance.SnowObjectTransformChanged(__instance, __state.Position, __state.Rotation);
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.SetReferencePosition))]
    internal static class ZNet_ReferencePosition_SnowCheckpoint
    {
        [HarmonyPrefix]
        private static void Prefix(Vector3 pos) => SeasonalSnowController.Instance.BeforeSnowReferencePositionChanged(pos);
    }

    [HarmonyPatch(typeof(ZDO), nameof(ZDO.SetOwner))]
    internal static class ZDO_SetOwner_SnowCheckpoint
    {
        // Never write from SetOwnerInternal: RPC_ZDOData has already assigned the
        // incoming DataRevision there and will deserialize the incoming snapshot next.
        [HarmonyPrefix]
        private static void Prefix(ZDO __instance, long uid) => SeasonalSnowController.Instance.BeforeSnowOwnerChanged(__instance, uid);
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
                SeasonalSnowController.Instance.InvalidateSnowArea(__instance.transform.position, geometry: true, readyOnly: true);
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
                SeasonalSnowController.Instance.InvalidateSnowArea(__instance.transform.position, geometry: true);
        }
    }
}
