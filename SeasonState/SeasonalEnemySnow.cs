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

        private static readonly int SnowCoverProperty = Shader.PropertyToID("_SnowCover");
        private static readonly Dictionary<string, Vector2> SnowMaterialRanges =
            new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<VisEquipment> PendingSnowCover =
            new HashSet<VisEquipment>();
        private static readonly List<VisEquipment> PendingSnowCoverBuffer =
            new List<VisEquipment>();

        private static string parsedConfigValue;

        internal static void RefreshSnowMaterialRanges()
        {
            string configValue = seasonalEnemySnowMaterialLevels?.Value ?? String.Empty;
            if (String.Equals(parsedConfigValue, configValue, StringComparison.Ordinal))
                return;

            parsedConfigValue = configValue;
            SnowMaterialRanges.Clear();

            foreach (string rawEntry in configValue.Split(';'))
            {
                string entry = rawEntry.Trim();
                if (String.IsNullOrWhiteSpace(entry))
                    continue;

                int valueSeparator = entry.LastIndexOf(':');
                if (valueSeparator <= 0 || valueSeparator >= entry.Length - 1)
                {
                    LogWarning($"Invalid enemy snow-cover entry '{entry}'. Expected material:min-max.");
                    continue;
                }

                string materialName = entry.Substring(0, valueSeparator).Trim();
                string rangeText = entry.Substring(valueSeparator + 1).Trim();
                int rangeSeparator = rangeText.IndexOf('-');
                if (String.IsNullOrWhiteSpace(materialName) ||
                    rangeSeparator <= 0 || rangeSeparator >= rangeText.Length - 1 ||
                    !TryParseSnowLevel(rangeText.Substring(0, rangeSeparator), out float first) ||
                    !TryParseSnowLevel(rangeText.Substring(rangeSeparator + 1), out float second) ||
                    Single.IsNaN(first) || Single.IsInfinity(first) ||
                    Single.IsNaN(second) || Single.IsInfinity(second))
                {
                    LogWarning($"Invalid enemy snow-cover entry '{entry}'. Expected material:min-max with finite values from 0 to 1.");
                    continue;
                }

                float minimum = Mathf.Clamp01(Mathf.Min(first, second));
                float maximum = Mathf.Clamp01(Mathf.Max(first, second));
                if (maximum <= SnowLevelEpsilon)
                {
                    LogWarning($"Enemy snow-cover entry '{entry}' does not contain a positive snow level.");
                    continue;
                }

                SnowMaterialRanges[materialName] = new Vector2(minimum, maximum);
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

        private static bool TryGetSnowMaterialRange(Material material, out Vector2 range)
        {
            range = Vector2.zero;
            return SnowMaterialRanges.TryGetValue(GetMaterialName(material), out range);
        }

        private static bool TryGetSnowSeed(VisEquipment instance, out int seed)
        {
            seed = 0;
            if (!instance)
                return false;

            Humanoid humanoid = instance.GetComponent<Humanoid>();
            if (!humanoid)
                humanoid = instance.GetComponentInParent<Humanoid>();
            if (humanoid)
            {
                if (humanoid.IsPlayer())
                    return false;

                seed = humanoid.m_seed;
                return true;
            }

            Ragdoll ragdoll = instance.GetComponent<Ragdoll>();
            if (!ragdoll)
                ragdoll = instance.GetComponentInParent<Ragdoll>();
            ZDO zdo = ragdoll?.m_nview?.GetZDO();
            if (zdo == null)
                return false;

            seed = zdo.GetInt(ZDOVars.s_seed, 0);
            return seed != 0;
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
                if (!TryGetSnowMaterialRange(material, out Vector2 range))
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

        private static void ApplySnowCover(VisEquipment instance)
        {
            if (!instance || MaterialMan.instance == null || EnvMan.instance == null ||
                EnvMan.instance.GetSnowBuildup() <= 0f || instance.m_lodGroup == null ||
                !TryGetSnowSeed(instance, out int seed))
                return;

            RefreshSnowMaterialRanges();
            if (SnowMaterialRanges.Count == 0)
                return;

            LOD[] lods = instance.m_lodGroup.GetLODs();
            if (lods == null || lods.Length == 0 || lods[0].renderers == null)
                return;

            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            try
            {
                int materialIndex = 0;
                foreach (Renderer renderer in lods[0].renderers)
                    ApplyRendererSnow(renderer, seed, ref materialIndex);
            }
            finally
            {
                UnityEngine.Random.state = randomState;
            }
        }

        private static void QueueSnowCover(VisEquipment instance)
        {
            if (instance)
                PendingSnowCover.Add(instance);
        }

        private static void ProcessPendingSnowCover()
        {
            if (PendingSnowCover.Count == 0)
                return;

            PendingSnowCoverBuffer.Clear();
            PendingSnowCoverBuffer.AddRange(PendingSnowCover);
            PendingSnowCover.Clear();

            foreach (VisEquipment instance in PendingSnowCoverBuffer)
                ApplySnowCover(instance);

            PendingSnowCoverBuffer.Clear();
        }

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.Start))]
        private static class VisEquipment_Start_SeasonalEnemySnow
        {
            [HarmonyPostfix]
            private static void Postfix(VisEquipment __instance)
            {
                QueueSnowCover(__instance);
            }
        }

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.RefreshSnowLevel))]
        private static class VisEquipment_RefreshSnowLevel_SeasonalEnemySnow
        {
            [HarmonyPostfix]
            private static void Postfix(VisEquipment __instance)
            {
                QueueSnowCover(__instance);
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.SetupVisEquipment))]
        private static class Humanoid_SetupVisEquipment_SeasonalEnemySnow
        {
            [HarmonyPostfix]
            private static void Postfix(Humanoid __instance, VisEquipment visEq, bool isRagdoll)
            {
                if (!isRagdoll || !__instance || __instance.IsPlayer() || !visEq ||
                    visEq.m_nview == null || !visEq.m_nview.IsValid())
                    return;

                ZDO zdo = visEq.m_nview.GetZDO();
                if (zdo == null)
                    return;

                zdo.Set(ZDOVars.s_seed, __instance.m_seed, okForNotOwner: true);
                QueueSnowCover(visEq);
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
