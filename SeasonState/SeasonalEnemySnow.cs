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
        public const string DefaultSnowLevelRanges =
            "Abomination:0.7-0.8;Draugr:0.7-0.8;Draugr_Elite:0.7-0.8;Draugr_elite_ragdoll:0.7-0.8;" +
            "Draugr_ragdoll:0.7-0.8;Draugr_Elite_sleeping:0.7-0.8;Draugr_Ranged:0.7-0.8;" +
            "Draugr_ranged_ragdoll:0.7-0.8;Draugr_Ranged_sleeping:0.7-0.8;Draugr_sleeping:0.7-0.8;" +
            "Dverger:0.75-0.78;Dverger_ragdoll:0.75-0.78;DvergerAshlands:0.74-0.765;" +
            "DvergerDeepNorth:0.74-0.765;DvergerMage:0.75-0.78;DvergerMageFire:0.75-0.78;" +
            "DvergerMageIce:0.75-0.78;DvergerMageSupport:0.75-0.78;Goblin:0.7-0.75;GoblinArcher:0.7-0.75;" +
            "Goblin_DN_Dragdoll:0.7-0.78;Goblin_Dragdoll:0.7-0.78;GoblinBrute:0.7-0.78;" +
            "GoblinBrute_ragdoll:0.7-0.78;GoblinBrute_Hildir:0.7-0.8;GoblinBrute_Hildir_ragdoll:0.7-0.8;" +
            "GoblinBruteBros:0.7-0.78;GoblinBruteBros_nochest:0.7-0.78;GoblinDeepNorth:0.74-0.77;" +
            "GoblinShaman:0.45-0.65;GoblinShaman_ragdoll:0.45-0.65;GoblinShaman_Hildir:0.66-0.72;" +
            "GoblinShaman_Hildir_nochest:0.66-0.72;GoblinShaman_Hildir_ragdoll:0.66-0.72;" +
            "Skeleton:0.68-0.78;Skeleton_Friendly:0.7-0.8;Skeleton_Meadows:0.68-0.78;" +
            "Skeleton_Mountains:0.68-0.78;Skeleton_NoArcher:0.68-0.78;Skeleton_Poison:0.68-0.78;" +
            "Skeleton_Swamps:0.68-0.78;Skeleton_Swamps_noarcher:0.68-0.78;Troll:0.65-0.72;" +
            "Troll_sleeping:0.65-0.72";

        private const float SnowLevelEpsilon = 0.0001f;

        private static readonly Dictionary<string, Vector2> SnowLevelRanges =
            new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        private static readonly System.Random SnowLevelRandom = new System.Random();

        private static string parsedConfigValue;

        private static void RefreshSnowLevelRanges()
        {
            string configValue = seasonalEnemySnowLevels?.Value ?? String.Empty;
            if (String.Equals(parsedConfigValue, configValue, StringComparison.Ordinal))
                return;

            parsedConfigValue = configValue;
            SnowLevelRanges.Clear();

            foreach (string rawEntry in configValue.Split(';'))
            {
                string entry = rawEntry.Trim();
                if (String.IsNullOrWhiteSpace(entry))
                    continue;

                int valueSeparator = entry.LastIndexOf(':');
                if (valueSeparator <= 0 || valueSeparator >= entry.Length - 1)
                {
                    LogWarning($"Invalid enemy snow-cover entry '{entry}'. Expected prefab:min-max.");
                    continue;
                }

                string prefabName = entry.Substring(0, valueSeparator).Trim();
                string rangeText = entry.Substring(valueSeparator + 1).Trim();
                int rangeSeparator = rangeText.IndexOf('-');
                if (String.IsNullOrWhiteSpace(prefabName) ||
                    rangeSeparator <= 0 || rangeSeparator >= rangeText.Length - 1 ||
                    !TryParseSnowLevel(rangeText.Substring(0, rangeSeparator), out float first) ||
                    !TryParseSnowLevel(rangeText.Substring(rangeSeparator + 1), out float second) ||
                    Single.IsNaN(first) || Single.IsInfinity(first) ||
                    Single.IsNaN(second) || Single.IsInfinity(second))
                {
                    LogWarning($"Invalid enemy snow-cover entry '{entry}'. Expected prefab:min-max with finite values from 0 to 1.");
                    continue;
                }

                float minimum = Mathf.Clamp01(Mathf.Min(first, second));
                float maximum = Mathf.Clamp01(Mathf.Max(first, second));
                if (maximum <= SnowLevelEpsilon)
                {
                    LogWarning($"Enemy snow-cover entry '{entry}' does not contain a positive snow level.");
                    continue;
                }

                SnowLevelRanges[prefabName] = new Vector2(minimum, maximum);
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

        private static bool TryGetSnowLevelRange(string prefabName, out Vector2 range)
        {
            range = Vector2.zero;
            RefreshSnowLevelRanges();
            return !String.IsNullOrWhiteSpace(prefabName) &&
                SnowLevelRanges.TryGetValue(prefabName, out range);
        }

        private static string GetOwnerPrefabName(VisEquipment instance, ZDO zdo)
        {
            if (ZNetScene.instance != null)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
                if (prefab)
                    return prefab.name;
            }

            GameObject owner = instance.m_nview != null
                ? instance.m_nview.gameObject
                : instance.gameObject;
            return Utils.GetPrefabName(owner);
        }

        private static float GetRandomSnowLevel(Vector2 range)
        {
            if (range.y <= range.x)
                return range.x;

            return Mathf.Lerp(range.x, range.y, (float)SnowLevelRandom.NextDouble());
        }

        private static void ApplyVisualSnowLevel(VisEquipment instance, float snowLevel)
        {
            snowLevel = Mathf.Clamp01(snowLevel);
            if (MaterialMan.instance != null)
                instance.SnowLevel = snowLevel;
            else
                instance.m_snowLevel = snowLevel;
        }

        private static void ApplySnowLevel(VisEquipment instance)
        {
            if (!instance || EnvMan.instance == null || instance.m_nview == null ||
                !instance.m_nview.IsValid())
                return;

            ZDO zdo = instance.m_nview.GetZDO();
            if (zdo == null ||
                !TryGetSnowLevelRange(GetOwnerPrefabName(instance, zdo), out Vector2 range))
                return;

            float storedSnowLevel = zdo.GetFloat(SeasonsVars.s_enemySnowLevel, 0f);
            bool invalidStoredSnowLevel = Single.IsNaN(storedSnowLevel) ||
                Single.IsInfinity(storedSnowLevel) || storedSnowLevel < 0f;
            if (invalidStoredSnowLevel)
            {
                if (instance.m_nview.IsOwner())
                    zdo.Set(SeasonsVars.s_enemySnowLevel, 0f);
                storedSnowLevel = 0f;
            }
            else if (storedSnowLevel <= SnowLevelEpsilon)
            {
                storedSnowLevel = 0f;
            }
            else
            {
                storedSnowLevel = Mathf.Clamp01(storedSnowLevel);
            }

            if (EnvMan.instance.GetSnowBuildup() > 0f)
            {
                if (storedSnowLevel <= SnowLevelEpsilon)
                {
                    if (!instance.m_nview.IsOwner())
                        return;

                    storedSnowLevel = GetRandomSnowLevel(range);
                    zdo.Set(SeasonsVars.s_enemySnowLevel, storedSnowLevel);
                }

                ApplyVisualSnowLevel(instance, storedSnowLevel);
                return;
            }

            if (storedSnowLevel <= SnowLevelEpsilon)
                return;

            if (instance.m_nview.IsOwner())
                zdo.Set(SeasonsVars.s_enemySnowLevel, 0f);

            if (!Mathf.Approximately(instance.m_snowLevel, 0f))
                ApplyVisualSnowLevel(instance, 0f);
        }

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.Awake))]
        private static class VisEquipment_Awake_SeasonalEnemySnow
        {
            [HarmonyPostfix]
            private static void Postfix(VisEquipment __instance)
            {
                ApplySnowLevel(__instance);
            }
        }
    }
}
