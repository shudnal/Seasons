using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    public static class SeasonalPlayerCapeSnow
    {
        private const float VisualUpdateThreshold = 0.001f;
        private const float SnowAccumulationSeconds = 180f;
        private const float NearFireMeltMultiplier = 3f;
        private static readonly SeasonalSnowMaterialRules MaterialRules = new SeasonalSnowMaterialRules();
        private static readonly SeasonalSnowMaterialOverrides MaterialOverrides = new SeasonalSnowMaterialOverrides();

        private sealed class CapeSnowState
        {
            public float snowPercent;
            public float lastVisualSnowPercent = Single.NaN;
            public float lastSyncedSnowPercent = Single.NaN;
            public int shoulderSignature = Int32.MinValue;
            public int configRevision = -1;
        }

        private static ConditionalWeakTable<Player, CapeSnowState> PlayerSnowStates =
            new ConditionalWeakTable<Player, CapeSnowState>();
        private static int configRevision;

        internal static void RefreshSnowMaterialRanges()
        {
            MaterialOverrides.PruneDestroyedObjects();
            if (!MaterialRules.Reload(SeasonalSnowSettings.Current.playerCapeMaterials))
                return;

            MaterialOverrides.RestoreAll();
            configRevision++;
        }

        private static void ApplyRendererSnow(Renderer renderer, float snowPercent)
        {
            if (!renderer)
                return;

            Material[] materials = renderer.sharedMaterials;
            if (materials == null)
                return;

            foreach (Material material in materials)
            {
                if (!MaterialRules.TryGetRange(material, renderer, out Vector2 range))
                    continue;

                float snowLevel = snowPercent <= 0f
                    ? 0f
                    : Mathf.Lerp(range.x, range.y, Mathf.Clamp01(snowPercent));
                MaterialOverrides.Set(renderer.gameObject, snowLevel);
                return;
            }
        }

        private static void ApplySnowCover(Player player, float snowPercent)
        {
            if (!player || MaterialMan.instance == null)
                return;

            RefreshSnowMaterialRanges();
            if (MaterialRules.IsEmpty)
                return;

            VisEquipment visEquipment = player.GetVisEquipment();
            List<GameObject> shoulderInstances = visEquipment?.m_shoulderItemInstances;
            if (shoulderInstances == null || shoulderInstances.Count == 0)
                return;

            foreach (GameObject shoulderInstance in shoulderInstances)
            {
                if (!shoulderInstance)
                    continue;

                foreach (Renderer renderer in shoulderInstance.GetComponentsInChildren<Renderer>(true))
                    ApplyRendererSnow(renderer, snowPercent);
            }
        }

        private static int GetShoulderSignature(Player player)
        {
            VisEquipment visEquipment = player?.GetVisEquipment();
            List<GameObject> shoulderInstances = visEquipment?.m_shoulderItemInstances;
            if (shoulderInstances == null || shoulderInstances.Count == 0)
                return 0;

            unchecked
            {
                int signature = shoulderInstances.Count;
                foreach (GameObject instance in shoulderInstances)
                    signature = signature * 397 ^ (instance ? instance.GetInstanceID() : 0);
                return signature;
            }
        }

        private static bool ReachedSyncThreshold(float current, float previous)
        {
            if (Single.IsNaN(previous))
                return true;

            return Mathf.Abs(current - previous) >= VisualUpdateThreshold ||
                current > 0f && previous <= 0f ||
                current <= 0f && previous > 0f ||
                current >= 1f && previous < 1f;
        }

        private static void UpdateLocalSnow(Player player, CapeSnowState state, ZDO zdo)
        {
            float snowPercent = state.snowPercent;
            float change = Time.fixedDeltaTime / SnowAccumulationSeconds;

            if (player.m_nearFireTimer < 0.25f)
            {
                snowPercent -= change * NearFireMeltMultiplier;
            }
            else if (player.m_nearFireTimer > 0.25f)
            {
                bool inShelter = player.InShelter();
                if (EnvMan.instance != null && EnvMan.instance.GetSnowBuildup() > 0.1f && !inShelter)
                    snowPercent += change;
                else if (inShelter)
                    snowPercent -= change;
            }

            state.snowPercent = Mathf.Clamp01(snowPercent);
            if (ReachedSyncThreshold(state.snowPercent, state.lastSyncedSnowPercent))
            {
                zdo.Set(SeasonsVars.s_playerCapeSnow, state.snowPercent);
                state.lastSyncedSnowPercent = state.snowPercent;
            }
        }

        private static void UpdateSnow(Player player)
        {
            if (!player || player.m_nview == null || !player.m_nview.IsValid() ||
                !PlayerSnowStates.TryGetValue(player, out CapeSnowState state))
                return;

            ZDO zdo = player.m_nview.GetZDO();
            if (zdo == null)
                return;

            if (Player.m_localPlayer == player)
            {
                UpdateLocalSnow(player, state, zdo);
            }
            else
            {
                state.snowPercent = Mathf.Clamp01(zdo.GetFloat(SeasonsVars.s_playerCapeSnow, 0f));
            }

            RefreshSnowMaterialRanges();
            int shoulderSignature = GetShoulderSignature(player);
            bool visualChanged = Single.IsNaN(state.lastVisualSnowPercent) ||
                Mathf.Abs(state.snowPercent - state.lastVisualSnowPercent) >= VisualUpdateThreshold ||
                state.snowPercent > 0f && state.lastVisualSnowPercent <= 0f ||
                state.snowPercent <= 0f && state.lastVisualSnowPercent > 0f ||
                state.snowPercent >= 1f && state.lastVisualSnowPercent < 1f ||
                shoulderSignature != state.shoulderSignature ||
                state.configRevision != configRevision;

            if (!visualChanged)
                return;

            ApplySnowCover(player, state.snowPercent);
            state.lastVisualSnowPercent = state.snowPercent;
            state.shoulderSignature = shoulderSignature;
            state.configRevision = configRevision;
        }

        internal static void Reset()
        {
            PlayerSnowStates = new ConditionalWeakTable<Player, CapeSnowState>();
            MaterialOverrides.RestoreAll();
            MaterialRules.Reset();
            configRevision = 0;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Start))]
        private static class Player_Start_SeasonalCapeSnow
        {
            [HarmonyPostfix]
            private static void Postfix(Player __instance)
            {
                if (!__instance)
                    return;

                PlayerSnowStates.Remove(__instance);
                CapeSnowState state = new CapeSnowState();
                if (__instance.m_nview != null && __instance.m_nview.IsValid())
                {
                    ZDO zdo = __instance.m_nview.GetZDO();
                    if (zdo != null)
                    {
                        state.snowPercent = Mathf.Clamp01(zdo.GetFloat(SeasonsVars.s_playerCapeSnow, 0f));
                        state.lastSyncedSnowPercent = state.snowPercent;
                    }
                }
                PlayerSnowStates.Add(__instance, state);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.FixedUpdate))]
        private static class Player_FixedUpdate_SeasonalCapeSnow
        {
            [HarmonyPostfix]
            private static void Postfix(Player __instance)
            {
                UpdateSnow(__instance);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnDestroy))]
        private static class Player_OnDestroy_SeasonalCapeSnow
        {
            [HarmonyPostfix]
            private static void Postfix(Player __instance)
            {
                if (__instance)
                    PlayerSnowStates.Remove(__instance);
            }
        }

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetShoulderEquipped))]
        private static class VisEquipment_SetShoulderEquipped_ResetSnowStateOnEquip
        {
            private static void Postfix(VisEquipment __instance, bool __result)
            {
                if (__result && Player.m_localPlayer && __instance == Player.m_localPlayer.GetVisEquipment() && PlayerSnowStates.TryGetValue(Player.m_localPlayer, out CapeSnowState state))
                    state.snowPercent = 0;
            }
        }
    }
}
