using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seasons
{
    // One typed lookup format is shared by creature and cape snow. No delimiter parsing at runtime.
    internal sealed class SeasonalSnowMaterialRules
    {
        private sealed class Rule
        {
            public readonly Vector2 range;
            public readonly HashSet<string> renderers;

            public Rule(SeasonSnow.MaterialSnow settings)
            {
                range = new Vector2(settings.min, settings.max);
                renderers = settings.renderers == null ? null : new HashSet<string>(settings.renderers, StringComparer.OrdinalIgnoreCase);
            }
        }

        private const string MaterialInstanceSuffix = " (Instance)";
        private readonly Dictionary<string, Rule> rules = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, SeasonSnow.MaterialSnow> source;

        public bool IsEmpty => rules.Count == 0;

        public bool Reload(Dictionary<string, SeasonSnow.MaterialSnow> settings)
        {
            if (ReferenceEquals(source, settings))
                return false;

            rules.Clear();
            foreach (KeyValuePair<string, SeasonSnow.MaterialSnow> entry in settings)
                rules.Add(entry.Key, new Rule(entry.Value));
            source = settings;
            return true;
        }

        public bool TryGetRange(Material material, Renderer renderer, out Vector2 range)
        {
            range = Vector2.zero;
            if (!material || !renderer)
                return false;

            string name = material.name?.Trim() ?? String.Empty;
            if (name.EndsWith(MaterialInstanceSuffix, StringComparison.Ordinal))
                name = name.Substring(0, name.Length - MaterialInstanceSuffix.Length);
            if (!rules.TryGetValue(name, out Rule rule) ||
                (rule.renderers != null && !rule.renderers.Contains(renderer.name?.Trim() ?? String.Empty)))
                return false;

            range = rule.range;
            return true;
        }

        public void Reset()
        {
            source = null;
            rules.Clear();
        }
    }

    // Track only values applied by this caller. Removing a JSON rule must remove its live overlay too.
    internal sealed class SeasonalSnowMaterialOverrides
    {
        private sealed class State
        {
            public bool hadOriginal;
            public float original;
            public float applied;
        }

        public static readonly int SnowCoverProperty = Shader.PropertyToID("_SnowCover");
        private readonly Dictionary<GameObject, State> states = new Dictionary<GameObject, State>();
        private readonly List<GameObject> staleObjects = new List<GameObject>();
        private float nextPruneTime;

        private static bool TryGetValue(MaterialMan manager, GameObject target, out float value)
        {
            value = 0f;
            if (!manager || !target ||
                !manager.m_blocks.TryGetValue(target.GetInstanceID(), out MaterialMan.PropertyContainer container) ||
                !container.m_shaderProperties.TryGetValue(SnowCoverProperty, out MaterialMan.ShaderPropertyBase property) ||
                !(property is MaterialMan.ShaderProperty<float> snow))
                return false;

            value = snow.Get();
            return true;
        }

        public void Set(GameObject target, float value)
        {
            MaterialMan manager = MaterialMan.instance;
            if (!target || !manager)
                return;

            if (!states.TryGetValue(target, out State state))
            {
                state = new State();
                state.hadOriginal = TryGetValue(manager, target, out state.original);
                states.Add(target, state);
            }
            manager.SetValue(target, SnowCoverProperty, value);
            state.applied = value;
        }

        public void RestoreAll()
        {
            MaterialMan manager = MaterialMan.instance;
            foreach (KeyValuePair<GameObject, State> entry in states)
            {
                // Do not remove a value that another component changed after our last assignment.
                if (!TryGetValue(manager, entry.Key, out float current) || !current.Equals(entry.Value.applied))
                    continue;

                if (entry.Value.hadOriginal)
                    manager.SetValue(entry.Key, SnowCoverProperty, entry.Value.original);
                else
                    manager.ResetValue(entry.Key, SnowCoverProperty);
            }
            states.Clear();
        }

        public void PruneDestroyedObjects()
        {
            if (Time.unscaledTime < nextPruneTime)
                return;
            nextPruneTime = Time.unscaledTime + 10f;
            staleObjects.Clear();
            foreach (GameObject target in states.Keys)
                if (!target)
                    staleObjects.Add(target);
            foreach (GameObject target in staleObjects)
                states.Remove(target);
            staleObjects.Clear();
        }
    }
}
