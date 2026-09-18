using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    internal static class SeasonalSnowMeshSettings
    {
        private readonly struct TransformOverride
        {
            private readonly float? x;
            private readonly float? y;
            private readonly float? z;

            public TransformOverride(float? x, float? y, float? z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            public Vector3 ApplyTo(Vector3 original) =>
                new Vector3(x ?? original.x, y ?? original.y, z ?? original.z);
        }

        private sealed class RendererState
        {
            public readonly MeshRenderer renderer;
            private readonly Vector3 originalPosition;
            private readonly Vector3 originalScale;
            private bool positionApplied;
            private bool scaleApplied;

            public RendererState(MeshRenderer renderer)
            {
                this.renderer = renderer;
                originalPosition = renderer.transform.localPosition;
                originalScale = renderer.transform.localScale;
            }

            public void Apply(TransformOverride? position, TransformOverride? scale, bool refreshBounds)
            {
                if (!renderer)
                    return;

                Transform transform = renderer.transform;
                bool transformChanged = false;
                bool hadTransformOverride = positionApplied || scaleApplied;
                if (position.HasValue || positionApplied)
                {
                    Vector3 target = position.HasValue ? position.Value.ApplyTo(originalPosition) : originalPosition;
                    if (!transform.localPosition.Equals(target))
                    {
                        transform.localPosition = target;
                        transformChanged = true;
                    }
                    positionApplied = position.HasValue;
                }

                if (scale.HasValue || scaleApplied)
                {
                    Vector3 target = scale.HasValue ? scale.Value.ApplyTo(originalScale) : originalScale;
                    if (!transform.localScale.Equals(target))
                    {
                        transform.localScale = target;
                        transformChanged = true;
                    }
                    scaleApplied = scale.HasValue;
                }

                if (transformChanged || (refreshBounds && (hadTransformOverride || positionApplied || scaleApplied)))
                    RefreshBounds(renderer);
            }

            public void Restore() => Apply(null, null, false);
        }

        private sealed class InstanceState
        {
            public readonly string prefabName;
            private readonly MeshRenderer snow;
            private readonly MeshRenderer worn;
            private readonly MeshRenderer broken;
            private readonly RendererState[] renderers;

            public InstanceState(WearNTear instance, string prefabName)
            {
                this.prefabName = prefabName;
                snow = instance.m_snow;
                worn = instance.m_snowWorn;
                broken = instance.m_snowBroken;
                // Snapshot every distinct renderer before changing any of them.
                renderers = new[] { snow, worn, broken }
                    .Where(renderer => renderer)
                    .Distinct()
                    .Select(renderer => new RendererState(renderer))
                    .ToArray();
            }

            public bool Matches(WearNTear instance) =>
                ReferenceEquals(snow, instance.m_snow) &&
                ReferenceEquals(worn, instance.m_snowWorn) &&
                ReferenceEquals(broken, instance.m_snowBroken);

            public void Apply(TransformOverride? position, TransformOverride? scale, bool refreshBounds)
            {
                foreach (RendererState renderer in renderers)
                    renderer.Apply(position, scale, refreshBounds);
            }

            public void Restore()
            {
                foreach (RendererState renderer in renderers)
                    renderer.Restore();
            }
        }

        private sealed class CopiedSnowState
        {
            public readonly string sourcePrefab;
            public readonly GameObject sourceObject;
            public readonly MeshRenderer renderer;

            public CopiedSnowState(string sourcePrefab, GameObject sourceObject, MeshRenderer renderer)
            {
                this.sourcePrefab = sourcePrefab;
                this.sourceObject = sourceObject;
                this.renderer = renderer;
            }
        }

        // Resolve donors once per world/configuration, never by searching prefabs during Awake.
        private static readonly Dictionary<string, string> CopySources =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, GameObject> SnowTemplates =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<WearNTear, CopiedSnowState> CopiedInstances =
            new Dictionary<WearNTear, CopiedSnowState>();
        private static ZNetScene copySourceScene;

        public static void InitializeCopySources()
        {
            copySourceScene = ZNetScene.instance;
            RebuildCopySources();
        }

        private static void RebuildCopySources()
        {
            CopySources.Clear();
            SnowTemplates.Clear();
            foreach (KeyValuePair<string, SeasonSnow.PieceSnow> entry in SeasonalSnowSettings.Current.pieces)
            {
                SeasonSnow.PieceSnow rule = entry.Value;
                if (rule.copyFrom != null && rule.buildup != SeasonSnow.SnowBuildup.Ignore &&
                    rule.buildup != SeasonSnow.SnowBuildup.Disabled)
                    CopySources.Add(entry.Key, rule.copyFrom);
            }

            if (!copySourceScene || copySourceScene != ZNetScene.instance)
                return;

            foreach (string source in CopySources.Values.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                GameObject prefab = copySourceScene.GetPrefab(source);
                WearNTear donor = prefab ? prefab.GetComponent<WearNTear>() : null;
                if (!donor || !donor.m_snow)
                {
                    LogWarning($"Cannot copy snow cap from '{source}': source prefab, WearNTear or m_snow is missing. Copy chains are not supported.");
                    continue;
                }

                GameObject template = donor.m_snow.gameObject;
                if (template == prefab || !template.transform.IsChildOf(prefab.transform) ||
                    template.GetComponentInChildren<ZNetView>(true) ||
                    template.GetComponentInChildren<WearNTear>(true))
                {
                    LogWarning($"Cannot copy snow cap from '{source}': m_snow must reference a child visual without ZNetView or WearNTear components.");
                    continue;
                }

                SnowTemplates[source] = template;
            }
        }

        public static bool HasSnowCopy(string prefabName) =>
            !IsPrefabIgnored(prefabName) && !IsPrefabDisabled(prefabName) && copySourceScene && copySourceScene == ZNetScene.instance &&
            CopySources.TryGetValue(prefabName, out string source) &&
            SnowTemplates.TryGetValue(source, out GameObject template) && template;

        private static bool TryCopySnowMesh(WearNTear instance)
        {
            if (!instance || instance.m_snow || CopySources.Count == 0 ||
                !copySourceScene || copySourceScene != ZNetScene.instance ||
                !instance.gameObject.scene.IsValid())
                return false;

            string prefabName = Utils.GetPrefabName(instance.gameObject);
            if (IsPrefabIgnored(prefabName) || IsPrefabDisabled(prefabName) || !CopySources.TryGetValue(prefabName, out string source) ||
                !SnowTemplates.TryGetValue(source, out GameObject template) || !template ||
                copySourceScene.GetPrefab(prefabName) == instance.gameObject)
                return false;

            GameObject clone = UnityEngine.Object.Instantiate(template, instance.transform, false);
            clone.name = template.name;
            clone.SetActive(false);
            clone.transform.SetAsFirstSibling();
            MeshRenderer renderer = clone.GetComponent<MeshRenderer>();
            if (!renderer)
            {
                UnityEngine.Object.Destroy(clone);
                return false;
            }

            // Do not inherit any world-space bounds override from a scene-backed donor.
            renderer.ResetBounds();
            instance.m_snow = renderer;
            CopiedInstances[instance] = new CopiedSnowState(source, template, renderer);
            return true;
        }

        private static bool UpdateCopiedSnowMesh(WearNTear instance)
        {
            if (!instance || !instance.gameObject.scene.IsValid() ||
                !copySourceScene || copySourceScene != ZNetScene.instance)
                return false;

            string prefabName = Utils.GetPrefabName(instance.gameObject);
            if (copySourceScene.GetPrefab(prefabName) == instance.gameObject)
                return false;

            bool changed = false;
            if (CopiedInstances.TryGetValue(instance, out CopiedSnowState copy))
            {
                bool matches = !IsPrefabIgnored(prefabName) && !IsPrefabDisabled(prefabName) && copy.renderer && instance.m_snow == copy.renderer &&
                    CopySources.TryGetValue(prefabName, out string source) &&
                    String.Equals(copy.sourcePrefab, source, StringComparison.OrdinalIgnoreCase) &&
                    SnowTemplates.TryGetValue(source, out GameObject template) && template == copy.sourceObject;
                if (matches)
                    return false;

                RestoreTransforms(instance);
                RemoveCopiedSnowMesh(instance);
                changed = true;
            }

            return TryCopySnowMesh(instance) || changed;
        }

        private static void RemoveCopiedSnowMesh(WearNTear instance)
        {
            if (!CopiedInstances.TryGetValue(instance, out CopiedSnowState copy))
                return;

            CopiedInstances.Remove(instance);
            if (!copy.renderer)
                return;

            // Another mod may have supplied a different renderer after our copy was added.
            // Only our own object and references to it belong to this cleanup.
            if (instance)
            {
                if (instance.m_snow == copy.renderer)
                    instance.m_snow = null;
                if (instance.m_snowWorn == copy.renderer)
                    instance.m_snowWorn = null;
                if (instance.m_snowBroken == copy.renderer)
                    instance.m_snowBroken = null;
            }

            GameObject clone = copy.renderer.gameObject;
            clone.SetActive(false);
            // Destroy is delayed; detach first so renderer-cache refreshes cannot retain it.
            clone.transform.SetParent(null, false);
            UnityEngine.Object.Destroy(clone);
        }

        private static void RefreshRendererCaches(WearNTear instance)
        {
            if (!instance)
                return;

            if (instance.m_renderers != null)
                instance.m_renderers = instance.GetHighlightRenderers();

            MaterialMan materials = MaterialMan.instance;
            if (materials && materials.m_blocks.TryGetValue(instance.gameObject.GetInstanceID(),
                out MaterialMan.PropertyContainer container))
            {
                container.RefreshRenderers(instance.gameObject);
                materials.QueuePropertyUpdate(container);
            }
        }

        private static readonly HashSet<string> ExcludedPrefabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> DisabledPrefabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<WearNTear> DisabledInstances = new HashSet<WearNTear>();
        private static readonly HashSet<WearNTear> IgnoredInstances = new HashSet<WearNTear>();
        private static readonly Dictionary<string, TransformOverride> Positions =
            new Dictionary<string, TransformOverride>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, TransformOverride> Scales =
            new Dictionary<string, TransformOverride>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<WearNTear, InstanceState> Instances =
            new Dictionary<WearNTear, InstanceState>();

        public static void RebuildConfiguration()
        {
            ExcludedPrefabs.Clear();
            DisabledPrefabs.Clear();
            Positions.Clear();
            Scales.Clear();

            foreach (KeyValuePair<string, SeasonSnow.PieceSnow> entry in SeasonalSnowSettings.Current.pieces)
            {
                SeasonSnow.PieceSnow rule = entry.Value;
                if (rule.buildup == SeasonSnow.SnowBuildup.Ignore)
                {
                    ExcludedPrefabs.Add(entry.Key);
                    continue;
                }
                if (rule.buildup == SeasonSnow.SnowBuildup.Disabled)
                {
                    DisabledPrefabs.Add(entry.Key);
                    continue;
                }
                if (rule.position != null)
                    Positions.Add(entry.Key, new TransformOverride(rule.position.x, rule.position.y, rule.position.z));
                if (rule.scale != null)
                    Scales.Add(entry.Key, new TransformOverride(rule.scale.x, rule.scale.y, rule.scale.z));
            }

            RebuildCopySources();
            SeasonalSnow.RebuildReducedSnowBuildupPrefabs();
            // No Unity-instance processing before ZoneSystem.Start resolves the current world's donors.
            if (!copySourceScene || copySourceScene != ZNetScene.instance)
                return;

            SeasonalSnow.InitializePrefabs();
            SeasonalSnow.OnSnowRangeConfigChanged();
            SeasonalSnow.UpdateLoadedSnowCover();
        }

        internal static bool IsPrefabDisabled(string prefabName) =>
            !String.IsNullOrEmpty(prefabName) && DisabledPrefabs.Contains(prefabName);

        internal static bool IsPrefabIgnored(string prefabName) =>
            !String.IsNullOrEmpty(prefabName) && ExcludedPrefabs.Contains(prefabName);

        internal static bool IsSnowIgnored(WearNTear instance)
        {
            if (ExcludedPrefabs.Count == 0 || !instance || !instance.gameObject.scene.IsValid())
                return false;

            string prefabName = Instances.TryGetValue(instance, out InstanceState state)
                ? state.prefabName
                : Utils.GetPrefabName(instance.gameObject);
            return IsPrefabIgnored(prefabName) &&
                (!ZNetScene.instance || ZNetScene.instance.GetPrefab(prefabName) != instance.gameObject);
        }

        public static bool IsSnowDisabled(WearNTear instance)
        {
            if (DisabledPrefabs.Count == 0 || !instance || !instance.gameObject.scene.IsValid())
                return false;

            string prefabName = Instances.TryGetValue(instance, out InstanceState state)
                ? state.prefabName
                : Utils.GetPrefabName(instance.gameObject);
            return DisabledPrefabs.Contains(prefabName) &&
                (!ZNetScene.instance || ZNetScene.instance.GetPrefab(prefabName) != instance.gameObject);
        }

        public static bool TryApplyDisabledSnow(WearNTear instance)
        {
            if (!IsSnowDisabled(instance))
                return false;

            DisabledInstances.Add(instance);
            instance.m_snowBuildup = 0f;
            instance.m_addPreSnow = false;
            instance.m_heavySnow = false;

            // Awake has not assigned m_nview yet. Never claim ownership or send reset RPCs:
            // the current owner clears the shared snow state, all peers clear their visuals.
            ZNetView view = instance.m_nview ? instance.m_nview : instance.GetComponent<ZNetView>();
            ZDO ownedZdo = view && view.IsValid() && view.IsOwner() ? view.GetZDO() : null;
            if (ownedZdo != null)
            {
                if (ownedZdo.GetFloat(ZDOVars.s_snow, 0f) != 0f)
                    ownedZdo.Set(ZDOVars.s_snow, 0f);
                if (ownedZdo.GetBool(ZDOVars.s_preSnow))
                    ownedZdo.Set(ZDOVars.s_preSnow, false);
            }
            SeasonalSnow.ClearInstanceSnowState(instance, ownedZdo);

            DeactivateSnowCap(instance.m_snow);
            DeactivateSnowCap(instance.m_snowWorn);
            DeactivateSnowCap(instance.m_snowBroken);

            // Native UpdateSnowVisual does not reset the shader value when buildup reaches zero.
            // Keep the shared per-instance property block consistent without dirtying it each call.
            MaterialMan materials = MaterialMan.instance;
            if (materials && materials.m_propertyBlock != null &&
                (!materials.m_blocks.TryGetValue(instance.gameObject.GetInstanceID(), out MaterialMan.PropertyContainer container) ||
                 !container.m_shaderProperties.TryGetValue(WearNTear.s_snowLevel, out MaterialMan.ShaderPropertyBase property) ||
                 !(property is MaterialMan.ShaderProperty<float> snowLevel) || snowLevel.Get() != 0f))
                materials.SetValue(instance.gameObject, WearNTear.s_snowLevel, 0f);

            return true;
        }

        private static void DeactivateSnowCap(MeshRenderer renderer)
        {
            if (renderer && renderer.gameObject.activeSelf)
                renderer.gameObject.SetActive(false);
        }

        public static void Apply(WearNTear instance, bool refreshBounds = false)
        {
            // Never mutate shared prefab assets: each instance owns its original transforms.
            if (!instance || !instance.gameObject.scene.IsValid())
                return;

            if (TryApplyDisabledSnow(instance))
            {
                RestoreTransforms(instance);
                if (CopiedInstances.ContainsKey(instance))
                {
                    RemoveCopiedSnowMesh(instance);
                    RefreshRendererCaches(instance);
                }
                return;
            }
            if (DisabledInstances.Remove(instance))
            {
                // Resume the ordinary snow lifecycle, not a snapshot from before the ban.
                SeasonalSnow.OnSnowMeshChanged(instance);
                if (instance.m_renderers != null && MaterialMan.instance)
                    instance.UpdateSnowVisual();
            }

            if (IsSnowIgnored(instance))
            {
                IgnoredInstances.Add(instance);
                RestoreTransforms(instance);
                if (CopiedInstances.ContainsKey(instance))
                {
                    RemoveCopiedSnowMesh(instance);
                    RefreshRendererCaches(instance);
                }
                // Restore only Seasons-owned state. Do not suppress vanilla snow or hide its mesh.
                SeasonalSnow.ReleaseIgnoredSnowState(instance);
                return;
            }
            if (IgnoredInstances.Remove(instance))
                SeasonalSnow.OnSnowMeshChanged(instance);

            Instances.TryGetValue(instance, out InstanceState state);
            if (state != null && !state.Matches(instance))
            {
                state.Restore();
                Instances.Remove(instance);
                state = null;
            }

            if (!instance.m_snow && !instance.m_snowWorn && !instance.m_snowBroken)
                return;

            string prefabName = state?.prefabName ?? Utils.GetPrefabName(instance.gameObject);
            if (ZNetScene.instance != null && ZNetScene.instance.GetPrefab(prefabName) == instance.gameObject)
            {
                // Some mods register live scene objects as prefabs rather than prefab assets.
                Forget(instance);
                return;
            }

            bool hasPosition = Positions.TryGetValue(prefabName, out TransformOverride position);
            bool hasScale = Scales.TryGetValue(prefabName, out TransformOverride scale);
            if (!hasPosition && !hasScale)
            {
                if (state != null)
                {
                    state.Restore();
                    Instances.Remove(instance);
                }
                return;
            }

            if (state == null)
            {
                state = new InstanceState(instance, prefabName);
                Instances.Add(instance, state);
            }

            state.Apply(hasPosition ? position : (TransformOverride?)null,
                hasScale ? scale : (TransformOverride?)null, refreshBounds);
        }

        public static void ApplyToLoadedInstances(bool updateCopies = false)
        {
            // Include placement previews and other live instances without a valid ZDO.
            HashSet<WearNTear> targets = new HashSet<WearNTear>(Instances.Keys);
            targets.UnionWith(CopiedInstances.Keys);
            targets.UnionWith(DisabledInstances);
            targets.UnionWith(IgnoredInstances);
            targets.UnionWith(WearNTear.GetAllInstances());
            // Config changes are rare; include inactive previews that native instance lists omit.
            // Shared prefab assets are filtered out by Apply.
            targets.UnionWith(Resources.FindObjectsOfTypeAll<WearNTear>());
            foreach (WearNTear instance in targets)
            {
                if (!instance)
                {
                    if (!ReferenceEquals(instance, null))
                    {
                        Instances.Remove(instance);
                        DisabledInstances.Remove(instance);
                        IgnoredInstances.Remove(instance);
                        RemoveCopiedSnowMesh(instance);
                    }
                    continue;
                }

                bool copiedMeshChanged = updateCopies && UpdateCopiedSnowMesh(instance);
                Apply(instance);
                if (copiedMeshChanged)
                {
                    if (instance.m_snow)
                        RefreshBounds(instance.m_snow);
                    RefreshRendererCaches(instance);
                    SeasonalSnow.OnSnowMeshChanged(instance);
                    if (instance.m_renderers != null && MaterialMan.instance)
                        instance.UpdateSnowVisual();
                }
            }
        }

        private static void RestoreTransforms(WearNTear instance)
        {
            if (!Instances.TryGetValue(instance, out InstanceState state))
                return;

            state.Restore();
            Instances.Remove(instance);
        }

        public static void Forget(WearNTear instance)
        {
            if (ReferenceEquals(instance, null))
                return;

            DisabledInstances.Remove(instance);
            IgnoredInstances.Remove(instance);
            RestoreTransforms(instance);
            if (CopiedInstances.ContainsKey(instance))
            {
                RemoveCopiedSnowMesh(instance);
                RefreshRendererCaches(instance);
            }
        }

        public static void Reset()
        {
            foreach (InstanceState state in Instances.Values)
                state.Restore();
            Instances.Clear();
            DisabledInstances.Clear();
            IgnoredInstances.Clear();
            foreach (WearNTear instance in CopiedInstances.Keys.ToArray())
            {
                RemoveCopiedSnowMesh(instance);
                RefreshRendererCaches(instance);
            }
            CopySources.Clear();
            SnowTemplates.Clear();
            copySourceScene = null;
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Awake))]
        private static class WearNTear_Awake_SnowMeshSettings
        {
            [HarmonyPrefix]
            private static void Prefix(WearNTear __instance)
            {
                // Clear persisted/pre-generated snow before native Awake reads it.
                TryApplyDisabledSnow(__instance);
                // Native Awake must see the clone when collecting renderers and initializing snow.
                if (TryCopySnowMesh(__instance))
                {
                    RefreshRendererCaches(__instance);
                    Apply(__instance);
                }
            }

            [HarmonyPostfix]
            private static void Postfix(WearNTear __instance)
            {
                // Awake assigns custom world bounds after its first visual updates.
                Apply(__instance, refreshBounds: true);
            }
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.CanHaveSnow), new[] { typeof(bool), typeof(bool) })]
        private static class WearNTear_CanHaveSnow_DisabledSnowCaps
        {
            [HarmonyPrefix]
            private static bool Prefix(WearNTear __instance, ref bool __result)
            {
                if (!TryApplyDisabledSnow(__instance))
                    return true;

                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.ChangeSnow))]
        private static class WearNTear_ChangeSnow_DisabledSnowCaps
        {
            [HarmonyPrefix]
            private static bool Prefix(WearNTear __instance) => !TryApplyDisabledSnow(__instance);

            [HarmonyFinalizer]
            private static void Finalizer(WearNTear __instance) => TryApplyDisabledSnow(__instance);
        }

        private static void RefreshBounds(MeshRenderer renderer)
        {
            // WearNTear assigns world-space bounds, which do not follow later transform edits.
            // Recalculate them without repeatedly growing the native snow displacement margin.
            renderer.ResetBounds();
            Bounds bounds = renderer.bounds;
            bounds.Expand(Vector3.up * WearNTear.c_ExtraSnowMeshBoundsHeight);
            bounds.center += Vector3.up * (0.5f * WearNTear.c_ExtraSnowMeshBoundsHeight);
            renderer.bounds = bounds;
        }
    }
}
