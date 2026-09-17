using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    internal static class SeasonalSnowMeshSettings
    {
        private readonly struct PositionOverride
        {
            public readonly Vector3 value;
            public readonly bool yOnly;

            public PositionOverride(Vector3 value, bool yOnly)
            {
                this.value = value;
                this.yOnly = yOnly;
            }

            public Vector3 ApplyTo(Vector3 original) => yOnly
                ? new Vector3(original.x, value.y, original.z)
                : value;
        }

        private sealed class RendererState
        {
            public readonly MeshRenderer renderer;
            private readonly Vector3 originalPosition;
            private readonly Vector3 originalScale;
            private readonly bool originalForceRenderingOff;
            private bool positionApplied;
            private bool scaleApplied;
            private bool exclusionApplied;

            public RendererState(MeshRenderer renderer)
            {
                this.renderer = renderer;
                originalPosition = renderer.transform.localPosition;
                originalScale = renderer.transform.localScale;
                originalForceRenderingOff = renderer.forceRenderingOff;
            }

            public void Apply(PositionOverride? position, Vector3? scale, bool excluded, bool refreshBounds)
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
                    Vector3 target = scale ?? originalScale;
                    if (!transform.localScale.Equals(target))
                    {
                        transform.localScale = target;
                        transformChanged = true;
                    }
                    scaleApplied = scale.HasValue;
                }

                if (excluded || exclusionApplied)
                {
                    // Do not fight the native new/worn/broken GameObject activation logic.
                    renderer.forceRenderingOff = excluded || originalForceRenderingOff;
                    exclusionApplied = excluded;
                }

                if (transformChanged || (refreshBounds && (hadTransformOverride || positionApplied || scaleApplied)))
                    RefreshBounds(renderer);
            }

            public void Restore() => Apply(null, null, false, false);
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

            public void Apply(PositionOverride? position, Vector3? scale, bool excluded, bool refreshBounds)
            {
                foreach (RendererState renderer in renderers)
                    renderer.Apply(position, scale, excluded, refreshBounds);
            }

            public void Restore()
            {
                foreach (RendererState renderer in renderers)
                    renderer.Restore();
            }
        }

        private static readonly HashSet<string> ExcludedPrefabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, PositionOverride> Positions =
            new Dictionary<string, PositionOverride>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Vector3> Scales =
            new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<WearNTear, InstanceState> Instances =
            new Dictionary<WearNTear, InstanceState>();

        public static void RebuildConfiguration()
        {
            ExcludedPrefabs.Clear();
            Positions.Clear();
            Scales.Clear();

            foreach (string rawName in (seasonalSnowExcludedPrefabs?.Value ?? String.Empty).Split(','))
            {
                string prefabName = rawName.Trim();
                if (prefabName.Length > 0)
                    ExcludedPrefabs.Add(prefabName);
            }

            ParseTransforms(seasonalSnowClippingFixes?.Value, allowYOnly: true);
            ParseTransforms(seasonalSnowMeshScales?.Value, allowYOnly: false);
            ApplyToLoadedInstances();
        }

        private static void ParseTransforms(string value, bool allowYOnly)
        {
            string configName = allowYOnly ? "Fix snow clipping through some pieces" : "Snow cap scales";
            string expected = allowYOnly ? "prefab:Y or prefab:X,Y,Z" : "prefab:X,Y,Z";
            foreach (string rawEntry in (value ?? String.Empty).Split(';'))
            {
                string entry = rawEntry.Trim();
                if (entry.Length == 0)
                    continue;

                int separator = entry.LastIndexOf(':');
                if (separator <= 0 || separator == entry.Length - 1)
                {
                    LogWarning($"Invalid '{configName}' entry '{entry}'. Expected {expected}; entry ignored.");
                    continue;
                }

                string prefabName = entry.Substring(0, separator).Trim();
                string[] components = entry.Substring(separator + 1).Split(',');
                if (prefabName.Length == 0 || prefabName.Contains(":"))
                {
                    LogWarning($"Invalid '{configName}' entry '{entry}'. Expected {expected}; entry ignored.");
                    continue;
                }

                bool yOnly = allowYOnly && components.Length == 1;
                if (!yOnly && components.Length != 3)
                {
                    LogWarning($"Invalid '{configName}' entry '{entry}': received {components.Length} components. Expected {expected}; entry ignored.");
                    continue;
                }

                Vector3 parsed = Vector3.zero;
                bool valid = true;
                for (int i = 0; i < components.Length; ++i)
                {
                    if (!Single.TryParse(components[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float number) ||
                        Single.IsNaN(number) || Single.IsInfinity(number))
                    {
                        valid = false;
                        break;
                    }
                    parsed[yOnly ? 1 : i] = number;
                }

                if (!valid)
                {
                    LogWarning($"Invalid '{configName}' entry '{entry}': use finite numbers with a dot as the decimal separator. Expected {expected}; entry ignored.");
                    continue;
                }

                if (allowYOnly)
                    Positions[prefabName] = new PositionOverride(parsed, yOnly);
                else
                    Scales[prefabName] = parsed;
            }
        }

        public static void Apply(WearNTear instance, bool refreshBounds = false)
        {
            // Never mutate shared prefab assets: each instance owns its original transforms.
            if (!instance || !instance.gameObject.scene.IsValid())
                return;

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

            bool hasPosition = Positions.TryGetValue(prefabName, out PositionOverride position);
            bool hasScale = Scales.TryGetValue(prefabName, out Vector3 scale);
            bool excluded = ExcludedPrefabs.Contains(prefabName);
            if (!hasPosition && !hasScale && !excluded)
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

            state.Apply(hasPosition ? position : (PositionOverride?)null,
                hasScale ? scale : (Vector3?)null, excluded, refreshBounds);
        }

        public static void ApplyToLoadedInstances()
        {
            // Include placement previews and other live instances without a valid ZDO.
            HashSet<WearNTear> targets = new HashSet<WearNTear>(Instances.Keys);
            targets.UnionWith(WearNTear.GetAllInstances());
            // Config changes are rare; include inactive previews that native instance lists omit.
            // Shared prefab assets are filtered out by Apply.
            targets.UnionWith(Resources.FindObjectsOfTypeAll<WearNTear>());
            foreach (WearNTear instance in targets)
            {
                if (!instance)
                {
                    if (!ReferenceEquals(instance, null))
                        Instances.Remove(instance);
                    continue;
                }
                Apply(instance);
            }
        }

        public static void Forget(WearNTear instance)
        {
            if (ReferenceEquals(instance, null) || !Instances.TryGetValue(instance, out InstanceState state))
                return;

            state.Restore();
            Instances.Remove(instance);
        }

        public static void Reset()
        {
            foreach (InstanceState state in Instances.Values)
                state.Restore();
            Instances.Clear();
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
