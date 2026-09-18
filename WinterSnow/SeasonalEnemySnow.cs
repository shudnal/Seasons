using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    public static class SeasonalEnemySnow
    {
        private static readonly SeasonalSnowMaterialRules MaterialRules = new SeasonalSnowMaterialRules();
        private static readonly SeasonalSnowMaterialOverrides MaterialOverrides = new SeasonalSnowMaterialOverrides();
        private static readonly Dictionary<GameObject, int> SnowVisuals = new Dictionary<GameObject, int>();
        private static readonly List<GameObject> StaleVisuals = new List<GameObject>();
        private static float nextVisualPruneTime;
        private static readonly Dictionary<GameObject, int> PendingSnowCover =
            new Dictionary<GameObject, int>();
        private static readonly List<KeyValuePair<GameObject, int>> PendingSnowCoverBuffer =
            new List<KeyValuePair<GameObject, int>>();

        internal static void RefreshSnowMaterialRanges()
        {
            if (!MaterialRules.Reload(SeasonalSnowSettings.Current.creatureMaterials))
                return;

            MaterialOverrides.RestoreAll();
            // Existing snowy visuals retain their deterministic spawn seed when rules change.
            // Do not turn a later weather change into a different snow-on-spawn policy.
            foreach (KeyValuePair<GameObject, int> entry in SnowVisuals)
                if (entry.Key)
                    PendingSnowCover[entry.Key] = entry.Value;
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
                if (!MaterialRules.TryGetRange(material, renderer, out Vector2 range))
                    continue;

                UnityEngine.Random.InitState(unchecked(seed + materialIndex));
                materialIndex++;

                float snowLevel = range.y <= range.x
                    ? range.x
                    : UnityEngine.Random.Range(range.x, range.y);
                MaterialOverrides.Set(renderer.gameObject, snowLevel);
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
            if (!visual || MaterialMan.instance == null)
                return;

            if (!SnowVisuals.ContainsKey(visual))
            {
                if (EnvMan.instance == null || EnvMan.instance.GetSnowBuildup() <= 0.1f)
                    return;
                SnowVisuals.Add(visual, seed);
            }
            if (MaterialRules.IsEmpty)
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

        private static GameObject GetRagdollVisual(Ragdoll ragdoll)
        {
            if (!ragdoll)
                return null;

            return ragdoll.GetComponentInChildren<LODGroup>() is LODGroup lodGroup ? lodGroup.gameObject : ragdoll.gameObject;
        }

        private static GameObject GetHumanoidVisual(Humanoid human)
        {
            if (!human)
                return null;

            if (human.m_lodGroup)
                return human.m_lodGroup.gameObject;

            if (human.m_visual)
                return human.m_visual.gameObject;

            return human.gameObject;
        }

        private static void QueueSnowCover(GameObject visual, int seed)
        {
            if (visual)
                PendingSnowCover[visual] = seed;
        }

        private static void ProcessPendingSnowCover()
        {
            RefreshSnowMaterialRanges();
            MaterialOverrides.PruneDestroyedObjects();
            if (Time.unscaledTime >= nextVisualPruneTime)
            {
                nextVisualPruneTime = Time.unscaledTime + 10f;
                StaleVisuals.Clear();
                foreach (GameObject visual in SnowVisuals.Keys)
                    if (!visual)
                        StaleVisuals.Add(visual);
                foreach (GameObject visual in StaleVisuals)
                    SnowVisuals.Remove(visual);
                StaleVisuals.Clear();
            }

            if (PendingSnowCover.Count == 0 || !MaterialMan.instance)
                return;

            PendingSnowCoverBuffer.Clear();
            PendingSnowCoverBuffer.AddRange(PendingSnowCover);
            PendingSnowCover.Clear();

            foreach (KeyValuePair<GameObject, int> entry in PendingSnowCoverBuffer)
                ApplySnowCover(entry.Key, entry.Value);

            PendingSnowCoverBuffer.Clear();
        }

        internal static void Reset()
        {
            MaterialOverrides.RestoreAll();
            MaterialRules.Reset();
            SnowVisuals.Clear();
            StaleVisuals.Clear();
            nextVisualPruneTime = 0f;
            PendingSnowCover.Clear();
            PendingSnowCoverBuffer.Clear();
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Start))]
        private static class Humanoid_Start_SeasonalEnemySnow
        {
            [HarmonyPostfix]
            private static void Postfix(Humanoid __instance)
            {
                if (!__instance || __instance.IsPlayer() || __instance.InInterior())
                    return;

                QueueSnowCover(GetHumanoidVisual(__instance), __instance.m_seed);
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnRagdollCreated))]
        private static class Humanoid_OnRagdollCreated_SeasonalEnemySnow
        {
            [HarmonyPostfix]
            private static void Postfix(Humanoid __instance, Ragdoll ragdoll)
            {
                if (!__instance || __instance.IsPlayer() || __instance.InInterior() || !ragdoll)
                    return;

                QueueSnowCover(GetRagdollVisual(ragdoll), __instance.m_seed);
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

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.RefreshSnowLevel))]
        private static class VisEquipment_RefreshSnowLevel_PreventVanillaSnowUpdate
        {
            private static bool Prefix() => false;
        }

    }
}