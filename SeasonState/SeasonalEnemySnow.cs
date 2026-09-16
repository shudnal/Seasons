using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    public static class SeasonalEnemySnow
    {
        public const string DefaultSnowMaterialRanges =
            "Abomination_mat:0.7-0.8;goblin_armor:0.65-0.8;BruteArmor_mat:0.65-0.8;" +
            "GoblinStuff_mat:0.65-0.8;BruteHipCloth_mat:0.65-0.8;dvergerArbalest_mat:0.78-0.81;" +
            "RangerAshlands_mat:0.77-0.8;dvergermage_mat:0.77-0.79;DvergerMageICe_mat:0.77-0.8;" +
            "DvergerMageSupport_mat:0.77-0.8;goblin:0.7-0.75;GoblinBrute_hildir_mat:0.7-0.8;" +
            "GoblinBrute_mat:0.7-0.77;GoblinShaman_mat:0.45-0.65;GoblinShaman_Hildir_mat:0.66-0.72;" +
            "DvergerBody:0.75-0.775;DvergerBodyashlands_mat:0.74-0.765;Skeleton:0.68-0.78;" +
            "Skeleton_dark:0.7-0.8;Draugr_mat:0.7-0.8;Draugr_Archer_mat:0.7-0.8;" +
            "Draugr_elite_mat:0.7-0.8;troll:0.65-0.72";

        private const float SnowLevelEpsilon = 0.0001f;
        private const string MaterialInstanceSuffix = " (Instance)";
        private const string MaterialRendererKeySeparator = "\u001f";

        private static readonly int SnowCoverProperty = Shader.PropertyToID("_SnowCover");
        private static readonly Dictionary<string, Vector2> SnowMaterialRanges =
            new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Vector2> SnowMaterialRendererRanges =
            new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<GameObject, int> PendingSnowCover =
            new Dictionary<GameObject, int>();
        private static readonly List<KeyValuePair<GameObject, int>> PendingSnowCoverBuffer =
            new List<KeyValuePair<GameObject, int>>();

        private static string parsedConfigValue;

        internal static void RefreshSnowMaterialRanges()
        {
            string configValue = seasonalEnemySnowMaterialLevels?.Value ?? String.Empty;
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
                    LogWarning($"Invalid enemy snow-cover entry '{entry}'. Expected material[-renderer]:min-max.");
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
                    LogWarning($"Invalid enemy snow-cover entry '{entry}'. Expected material[-renderer]:min-max with finite values from 0 to 1.");
                    continue;
                }

                float minimum = Mathf.Clamp01(Mathf.Min(first, second));
                float maximum = Mathf.Clamp01(Mathf.Max(first, second));
                if (maximum <= SnowLevelEpsilon)
                {
                    LogWarning($"Enemy snow-cover entry '{entry}' does not contain a positive snow level.");
                    continue;
                }

                Vector2 range = new Vector2(minimum, maximum);
                if (rendererName == null)
                    SnowMaterialRanges[materialName] = range;
                else
                    SnowMaterialRendererRanges[GetMaterialRendererKey(materialName, rendererName)] = range;
            }
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

        private static bool ApplyRendererSnow(Renderer renderer, int seed, ref int materialIndex)
        {
            if (!renderer)
                return false;

            Material[] materials = renderer.sharedMaterials;
            if (materials == null)
                return false;

            foreach (Material material in materials)
            {
                if (!TryGetSnowMaterialRange(material, renderer, out Vector2 range))
                    continue;

                UnityEngine.Random.InitState(unchecked(seed + materialIndex));
                materialIndex++;

                float snowLevel = range.y <= range.x
                    ? range.x
                    : UnityEngine.Random.Range(range.x, range.y);
                MaterialMan.instance.SetValue(
                    renderer.gameObject,
                    SnowCoverProperty,
                    snowLevel);
                return true;
            }

            return false;
        }

        private static Renderer[] GetVisualRenderers(GameObject visual)
        {
            if (!visual)
                return Array.Empty<Renderer>();

            LODGroup lodGroup = visual.GetComponent<LODGroup>();
            if (lodGroup)
            {
                LOD[] lods = lodGroup.GetLODs();
                if (lods == null || lods.Length == 0 || lods[0].renderers == null)
                    return Array.Empty<Renderer>();

                return lods[0].renderers;
            }

            return visual.GetComponentsInChildren<Renderer>(true);
        }

        private static void ApplySnowCover(GameObject visual, int seed)
        {
            if (!visual || MaterialMan.instance == null || EnvMan.instance == null ||
                EnvMan.instance.GetSnowBuildup() <= 0f)
                return;

            RefreshSnowMaterialRanges();
            if (SnowMaterialRanges.Count == 0 && SnowMaterialRendererRanges.Count == 0)
                return;

            Renderer[] renderers = GetVisualRenderers(visual);
            if (renderers.Length == 0)
                return;

            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            try
            {
                int materialIndex = 0;
                foreach (Renderer renderer in renderers)
                    ApplyRendererSnow(renderer, seed, ref materialIndex);
            }
            finally
            {
                UnityEngine.Random.state = randomState;
            }
        }

        private static GameObject GetVisual(Ragdoll ragdoll)
        {
            if (!ragdoll)
                return null;

            Transform visual = ragdoll.transform.Find("Visual");
            return visual ? visual.gameObject : null;
        }

        private static void QueueSnowCover(GameObject visual, int seed)
        {
            if (visual)
                PendingSnowCover[visual] = seed;
        }

        private static void ProcessPendingSnowCover()
        {
            if (PendingSnowCover.Count == 0)
                return;

            PendingSnowCoverBuffer.Clear();
            PendingSnowCoverBuffer.AddRange(PendingSnowCover);
            PendingSnowCover.Clear();

            foreach (KeyValuePair<GameObject, int> entry in PendingSnowCoverBuffer)
                ApplySnowCover(entry.Key, entry.Value);

            PendingSnowCoverBuffer.Clear();
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Start))]
        private static class Humanoid_Start_SeasonalEnemySnow
        {
            [HarmonyPostfix]
            private static void Postfix(Humanoid __instance)
            {
                if (!__instance || __instance.IsPlayer())
                    return;

                QueueSnowCover(__instance.m_visual, __instance.m_seed);
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnRagdollCreated))]
        private static class Humanoid_OnRagdollCreated_SeasonalEnemySnow
        {
            [HarmonyPostfix]
            private static void Postfix(Humanoid __instance, Ragdoll ragdoll)
            {
                if (!__instance || __instance.IsPlayer() || !ragdoll)
                    return;

                QueueSnowCover(GetVisual(ragdoll), __instance.m_seed);
            }
        }

        [HarmonyPatch(typeof(MaterialMan), nameof(MaterialMan.Update))]
        private static class MaterialMan_Update_SeasonalEnemySnow
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                ProcessPendingSnowCover();
            }
        }
    }
}
