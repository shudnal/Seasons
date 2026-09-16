using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    public static class SeasonalPlayerCapeSnow
    {
        public const string DefaultSnowMaterialRanges =
            "CapeLinen:0.76-0.80;Ashcape_Mat:0.76-0.79;asksvincape_mat:0.7-0.79;" +
            "NordCape_mat:0.4-0.74;MageCape_mat:0.75-0.78;feathercape_mat:0.76-0.79;" +
            "LoxCape_Mat:0.75-0.79;CapeTrollHide:0.75-0.79;CapeDeerHide:0.76-0.81;" +
            "WolfCape-WolfCape_cloth:0.50-0.75;WolfCapeChain-WolfCape:0.5-0.70";

        private const float SnowLevelEpsilon = 0.0001f;
        private const float VisualUpdateThreshold = 0.001f;
        private const float SnowAccumulationSeconds = 180f;
        private const float NearFireMeltMultiplier = 3f;
        private const string MaterialInstanceSuffix = " (Instance)";
        private const string MaterialRendererKeySeparator = "\u001f";

        private static readonly int SnowCoverProperty = Shader.PropertyToID("_SnowCover");
        private static readonly Dictionary<string, Vector2> SnowMaterialRanges =
            new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Vector2> SnowMaterialRendererRanges =
            new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);

        private sealed class CapeSnowState
        {
            public float snow;
            public float lastVisualSnow = Single.NaN;
            public float lastSyncedSnow = Single.NaN;
            public int shoulderSignature = Int32.MinValue;
            public int configRevision = -1;
        }

        private static ConditionalWeakTable<Player, CapeSnowState> PlayerSnowStates =
            new ConditionalWeakTable<Player, CapeSnowState>();
        private static string parsedConfigValue;
        private static int configRevision;

        internal static void RefreshSnowMaterialRanges()
        {
            string configValue = seasonalPlayerCapeSnowMaterialLevels?.Value ?? String.Empty;
            if (String.Equals(parsedConfigValue, configValue, StringComparison.Ordinal))
                return;

            parsedConfigValue = configValue;
            SnowMaterialRanges.Clear();
            SnowMaterialRendererRanges.Clear();

            foreach (string rawEntry in configValue.Split(';'))
            {
                string entry = rawEntry.Trim();
                if (String.IsNullOrWhiteSpace(entry))
                    continue;

                int valueSeparator = entry.LastIndexOf(':');
                if (valueSeparator <= 0 || valueSeparator >= entry.Length - 1)
                {
                    LogWarning($"Invalid player cape snow-cover entry '{entry}'. Expected material[-renderer]:min-max.");
                    continue;
                }

                string targetName = entry.Substring(0, valueSeparator).Trim();
                string materialName = targetName;
                string rendererName = null;
                int rendererSeparator = targetName.IndexOf('-');
                if (rendererSeparator > 0 && rendererSeparator < targetName.Length - 1)
                {
                    materialName = targetName.Substring(0, rendererSeparator).Trim();
                    rendererName = targetName.Substring(rendererSeparator + 1).Trim();
                }

                string rangeText = entry.Substring(valueSeparator + 1).Trim();
                int rangeSeparator = rangeText.IndexOf('-');
                if (String.IsNullOrWhiteSpace(materialName) ||
                    (rendererName != null && String.IsNullOrWhiteSpace(rendererName)) ||
                    rangeSeparator <= 0 || rangeSeparator >= rangeText.Length - 1 ||
                    !TryParseSnowLevel(rangeText.Substring(0, rangeSeparator), out float first) ||
                    !TryParseSnowLevel(rangeText.Substring(rangeSeparator + 1), out float second) ||
                    Single.IsNaN(first) || Single.IsInfinity(first) ||
                    Single.IsNaN(second) || Single.IsInfinity(second))
                {
                    LogWarning($"Invalid player cape snow-cover entry '{entry}'. Expected material[-renderer]:min-max with finite values from 0 to 1.");
                    continue;
                }

                float minimum = Mathf.Clamp01(Mathf.Min(first, second));
                float maximum = Mathf.Clamp01(Mathf.Max(first, second));
                if (maximum <= SnowLevelEpsilon)
                {
                    LogWarning($"Player cape snow-cover entry '{entry}' does not contain a positive snow level.");
                    continue;
                }

                Vector2 range = new Vector2(minimum, maximum);
                if (rendererName == null)
                    SnowMaterialRanges[materialName] = range;
                else
                    SnowMaterialRendererRanges[GetMaterialRendererKey(materialName, rendererName)] = range;
            }

            configRevision++;
        }

        private static bool TryParseSnowLevel(string value, out float snowLevel)
        {
            return Single.TryParse(
                value.Trim().Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out snowLevel);
        }

        private static string GetMaterialName(Material material)
        {
            if (!material)
                return String.Empty;

            string materialName = material.name?.Trim() ?? String.Empty;
            if (materialName.EndsWith(MaterialInstanceSuffix, StringComparison.Ordinal))
            {
                materialName = materialName.Substring(
                    0,
                    materialName.Length - MaterialInstanceSuffix.Length);
            }

            return materialName;
        }

        private static string GetMaterialRendererKey(string materialName, string rendererName)
        {
            return materialName + MaterialRendererKeySeparator + rendererName;
        }

        private static bool TryGetSnowMaterialRange(Material material, Renderer renderer, out Vector2 range)
        {
            range = Vector2.zero;
            if (!renderer)
                return false;

            string materialName = GetMaterialName(material);
            if (String.IsNullOrWhiteSpace(materialName))
                return false;

            string rendererName = renderer.name?.Trim() ?? String.Empty;
            if (SnowMaterialRendererRanges.TryGetValue(
                    GetMaterialRendererKey(materialName, rendererName),
                    out range))
                return true;

            return SnowMaterialRanges.TryGetValue(materialName, out range);
        }

        private static void ApplyRendererSnow(Renderer renderer, float snow)
        {
            if (!renderer)
                return;

            Material[] materials = renderer.sharedMaterials;
            if (materials == null)
                return;

            foreach (Material material in materials)
                if (TryGetSnowMaterialRange(material, renderer, out Vector2 range))
                    MaterialMan.instance.SetValue(renderer.gameObject, SnowCoverProperty, snow);
        }

        private static void ApplySnowCover(Player player, float snow)
        {
            if (!player || MaterialMan.instance == null)
                return;

            RefreshSnowMaterialRanges();
            if (SnowMaterialRanges.Count == 0 && SnowMaterialRendererRanges.Count == 0)
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
                    ApplyRendererSnow(renderer, snow);
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
                current <= 0f && previous > 0f ||
                current >= 1f && previous < 1f;
        }

        private static void UpdateLocalSnow(Player player, CapeSnowState state, ZDO zdo)
        {
            float snow = state.snow;
            float change = Time.fixedDeltaTime / SnowAccumulationSeconds;

            if (player.m_nearFireTimer < 0.25f)
            {
                snow -= change * NearFireMeltMultiplier;
            }
            else if (player.m_nearFireTimer > 0.25f)
            {
                bool inShelter = player.InShelter();
                if (EnvMan.instance != null && EnvMan.instance.GetSnowBuildup() > 0.1f && !inShelter)
                    snow += change;
                else if (inShelter)
                    snow -= change;
            }

            state.snow = Mathf.Clamp01(snow);
            if (ReachedSyncThreshold(state.snow, state.lastSyncedSnow))
            {
                zdo.Set(SeasonsVars.s_playerCapeSnow, state.snow);
                state.lastSyncedSnow = state.snow;
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
                state.snow = Mathf.Clamp01(zdo.GetFloat(SeasonsVars.s_playerCapeSnow, 0f));
            }

            RefreshSnowMaterialRanges();
            int shoulderSignature = GetShoulderSignature(player);
            bool visualChanged = Single.IsNaN(state.lastVisualSnow) ||
                Mathf.Abs(state.snow - state.lastVisualSnow) >= VisualUpdateThreshold ||
                state.snow <= 0f && state.lastVisualSnow > 0f ||
                state.snow >= 1f && state.lastVisualSnow < 1f ||
                shoulderSignature != state.shoulderSignature ||
                state.configRevision != configRevision;

            if (!visualChanged)
                return;

            ApplySnowCover(player, state.snow);
            state.lastVisualSnow = state.snow;
            state.shoulderSignature = shoulderSignature;
            state.configRevision = configRevision;
        }

        internal static void Reset()
        {
            PlayerSnowStates = new ConditionalWeakTable<Player, CapeSnowState>();
            parsedConfigValue = null;
            SnowMaterialRanges.Clear();
            SnowMaterialRendererRanges.Clear();
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
                        state.snow = Mathf.Clamp01(zdo.GetFloat(SeasonsVars.s_playerCapeSnow, 0f));
                        state.lastSyncedSnow = state.snow;
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
    }
}
