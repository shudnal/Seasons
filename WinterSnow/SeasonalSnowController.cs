using System;
using System.Collections.Generic;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    /// <summary>Owns the world-scoped snow runtime without attaching components to pieces.</summary>
    internal sealed partial class SeasonalSnowController
    {
        internal static SeasonalSnowController Instance { get; } = new SeasonalSnowController();
        internal const int VisualsPerFrame = 50;
        private const float VisualStep = 0.01f;
        private const float VisibilityThreshold = 0.25f;

        private sealed class CapBinding
        {
            internal readonly MeshRenderer Renderer;
            internal readonly GameObject Object;
            internal readonly int Mask;
            internal bool Supported;

            internal CapBinding(MeshRenderer renderer, int mask)
            {
                Renderer = renderer;
                Object = renderer.gameObject;
                Mask = mask;
            }

            internal void SetVisible(bool visible)
            {
                // Toggle the cap root only. Child activity and LOD selection belong to Unity.
                if (Object && Object.activeSelf != visible)
                    Object.SetActive(visible);
            }
        }

        private sealed class SnowRendererBinding
        {
            internal readonly Renderer Renderer;
            internal readonly GameObject Object;
            internal int CapMask;
            private readonly Material[] originals;
            private readonly Material[] assigned;
            private readonly SeasonalSnowMaterials.Levels[] levels;
            private int appliedIndex = -1;
            internal readonly bool Supported;

            internal SnowRendererBinding(Renderer renderer, SeasonalSnowMaterials materials)
            {
                Renderer = renderer;
                Object = renderer.gameObject;
                originals = renderer.sharedMaterials;
                assigned = (Material[])originals.Clone();
                levels = new SeasonalSnowMaterials.Levels[originals.Length];
                for (int i = 0; i < originals.Length; ++i)
                {
                    levels[i] = materials.GetLevels(originals[i]);
                    Supported |= levels[i] != null;
                }
                // Leave unrelated child renderers entirely outside snow ownership.
                if (!Supported)
                    return;

                // Seasons owns both renderer-wide and per-slot overrides on its caps.
                renderer.SetPropertyBlock(null);
                for (int i = 0; i < originals.Length; ++i)
                    renderer.SetPropertyBlock(null, i);
            }

            internal void ApplyMaterial(int index)
            {
                if (!Renderer || index == appliedIndex)
                    return;

                bool changed = false;
                for (int i = 0; i < assigned.Length; ++i)
                {
                    Material target = index == 0 || levels[i] == null ? originals[i] : levels[i].Get(index);
                    if (assigned[i] == target)
                        continue;
                    assigned[i] = target;
                    changed = true;
                }
                if (changed)
                {
                    if (assigned.Length == 1)
                        Renderer.sharedMaterial = assigned[0];
                    else
                        Renderer.sharedMaterials = assigned;
                }
                appliedIndex = index;
            }
        }

        private sealed class VisualState
        {
            internal readonly WearNTear Piece;
            internal readonly MeshRenderer Normal;
            internal readonly MeshRenderer Worn;
            internal readonly MeshRenderer Broken;
            internal readonly List<CapBinding> Caps = new List<CapBinding>(3);
            internal readonly List<SnowRendererBinding> Renderers = new List<SnowRendererBinding>(3);
            internal MeshRenderer Target;
            internal MeshRenderer Applied;
            internal float TargetLevel;
            internal float TargetSnow;
            internal float AppliedLevel;
            internal bool HasApplied;
            internal bool Disabled;
            internal bool Queued;
            internal VisualState Previous;
            internal VisualState Next;

            internal VisualState(WearNTear piece)
            {
                Piece = piece;
                Normal = piece.m_snow;
                Worn = piece.m_snowWorn;
                Broken = piece.m_snowBroken;
            }

            internal bool Matches(WearNTear piece) => ReferenceEquals(Normal, piece.m_snow) &&
                ReferenceEquals(Worn, piece.m_snowWorn) && ReferenceEquals(Broken, piece.m_snowBroken);
        }

        private readonly SeasonalSnowMaterials materials = new SeasonalSnowMaterials();
        private readonly Dictionary<WearNTear, VisualState> visuals = new Dictionary<WearNTear, VisualState>();
        private readonly HashSet<Renderer> ownedRenderers = new HashSet<Renderer>();
        private readonly HashSet<string> unsupportedCaps = new HashSet<string>(StringComparer.Ordinal);
        private VisualState firstVisual;
        private VisualState lastVisual;
        private ZNetScene visualScene;
        private ZNetScene stoppedScene;
        private int lastVisualFrame = -1;

        private SeasonalSnowController() { }

        internal int RegisteredVisualCount => visuals.Count;
        internal int MaterialPoolCount => materials.PoolCount;
        internal int CreatedMaterialCount => materials.CreatedMaterialCount;

        // The legacy simulation remains the producer until the four-list replacement.
        // Read its selected value explicitly, without changing the native runtime field.
        internal bool TryQueueCurrentVisual(WearNTear piece)
        {
            if (!piece)
                return false;
            if (SeasonalSnowMeshSettings.IsSnowDisabled(piece))
            {
                DisableVisual(piece);
                return true;
            }
            if (!SeasonalSnow.IsSeasonalSnowPosition(piece))
            {
                ReleaseVisual(piece, hide: true, restoreNative: true);
                return false;
            }
            if (ZNet.instance && ZNet.instance.IsDedicated())
                return true;

            float snow = SeasonalSnow.GetVisualSnow(piece);
            if (float.IsNaN(snow) || float.IsInfinity(snow))
                snow = 0f;
            QueueVisual(piece, snow, disabled: false,
                force: snow <= 0f || snow >= SeasonalSnow.GetSnowBuildupRange(piece).y);
            return true;
        }

        // Some roof prefabs use the same object for wet effects and a snow cap.
        // Filter only UpdateWear's read; keep the field intact for native handoff.
        internal static GameObject GetWetVisual(WearNTear piece)
        {
            GameObject wet = piece.m_wet;
            if (wet && Instance.visuals.TryGetValue(piece, out VisualState state))
            {
                for (int i = 0; i < state.Caps.Count; ++i)
                    if (state.Caps[i].Object == wet)
                        return null;
                for (int i = 0; i < state.Renderers.Count; ++i)
                    if (state.Renderers[i].Object == wet)
                        return null;
            }
            return wet;
        }

        internal void DisableVisual(WearNTear piece)
        {
            if (!piece || (ZNet.instance && ZNet.instance.IsDedicated()))
                return;
            QueueVisual(piece, 0f, disabled: true, force: true);
        }

        private bool EnsureVisualScene()
        {
            ZNetScene scene = ZNetScene.instance;
            if (!scene || ReferenceEquals(scene, stoppedScene))
                return false;
            if (!ReferenceEquals(scene, visualScene))
            {
                ResetVisuals();
                visualScene = scene;
                stoppedScene = null;
            }
            return true;
        }

        private void QueueVisual(WearNTear piece, float snow, bool disabled, bool force)
        {
            if (!EnsureVisualScene())
                return;
            if (visuals.TryGetValue(piece, out VisualState state) && !state.Matches(piece))
            {
                ReleaseVisual(piece, hide: true, restoreNative: false);
                state = null;
            }
            if (state == null)
            {
                state = new VisualState(piece);
                visuals.Add(piece, state);
                BindCap(state, state.Normal);
                BindCap(state, state.Worn);
                BindCap(state, state.Broken);
                RemoveNativeSnowProperty(piece);
            }

            MeshRenderer target = null;
            if (!disabled && state.Normal && snow > VisibilityThreshold)
            {
                target = piece.m_healthPercentage <= 0.25f && state.Broken ? state.Broken :
                    piece.m_healthPercentage <= 0.75f && state.Worn ? state.Worn : state.Normal;
            }
            float level = target ? Mathf.Clamp01((snow - VisibilityThreshold) / (1f - VisibilityThreshold)) : 0f;
            bool prioritizeHide = !target && (state.Target || (disabled && !state.Disabled));
            state.Disabled = disabled;
            state.Target = target;
            state.TargetSnow = snow;
            state.TargetLevel = level;

            if (!state.HasApplied || target != state.Applied ||
                Mathf.Abs(level - state.AppliedLevel) + 0.000001f >= VisualStep ||
                (force && !level.Equals(state.AppliedLevel)))
                EnqueueVisual(state, prioritizeHide);
        }

        private void BindCap(VisualState state, MeshRenderer renderer)
        {
            if (!renderer)
                return;
            foreach (CapBinding existing in state.Caps)
                if (existing.Renderer == renderer)
                    return;

            CapBinding cap = new CapBinding(renderer, 1 << state.Caps.Count);
            state.Caps.Add(cap);
            BindCapRenderer(state, cap, renderer);

            // Resolve dependencies once, including inactive LODs. Never rediscover them
            // when only the snow percentage changes, and never scan the whole piece.
            if (renderer.TryGetComponent(out LODGroup group))
            {
                foreach (LOD lod in group.GetLODs())
                {
                    if (lod.renderers == null)
                        continue;
                    foreach (Renderer child in lod.renderers)
                        BindCapRenderer(state, cap, child);
                }
            }
            else
            {
                foreach (Renderer child in renderer.GetComponentsInChildren<Renderer>(true))
                    BindCapRenderer(state, cap, child);
            }

            if (!cap.Supported)
            {
                string key = Utils.GetPrefabName(state.Piece.gameObject) + "/" + renderer.name;
                if (unsupportedCaps.Add(key))
                    LogWarning($"No supported snow cap materials on '{key}'; the cap will remain hidden.");
            }
        }

        private void BindCapRenderer(VisualState state, CapBinding cap, Renderer renderer)
        {
            // A malformed LOD list must not take control of another part of the piece.
            if (!renderer || !renderer.transform.IsChildOf(cap.Object.transform))
                return;

            foreach (SnowRendererBinding existing in state.Renderers)
            {
                if (existing.Renderer != renderer)
                    continue;
                existing.CapMask |= cap.Mask;
                cap.Supported = true;
                return;
            }

            SnowRendererBinding binding = new SnowRendererBinding(renderer, materials);
            if (!binding.Supported)
                return;

            binding.CapMask = cap.Mask;
            state.Renderers.Add(binding);
            cap.Supported = true;
            ownedRenderers.Add(renderer);
            DetachFromMaterialMan(renderer);
        }

        private void EnqueueVisual(VisualState state, bool prioritize = false)
        {
            if (state.Queued)
            {
                if (!prioritize)
                    return;
                RemoveQueuedVisual(state);
            }
            state.Queued = true;
            if (prioritize)
            {
                state.Previous = null;
                state.Next = firstVisual;
                if (firstVisual != null)
                    firstVisual.Previous = state;
                else
                    lastVisual = state;
                firstVisual = state;
                return;
            }
            state.Previous = lastVisual;
            state.Next = null;
            if (lastVisual != null)
                lastVisual.Next = state;
            else
                firstVisual = state;
            lastVisual = state;
        }

        private void RemoveQueuedVisual(VisualState state)
        {
            if (!state.Queued)
                return;
            if (state.Previous != null)
                state.Previous.Next = state.Next;
            else
                firstVisual = state.Next;
            if (state.Next != null)
                state.Next.Previous = state.Previous;
            else
                lastVisual = state.Previous;
            state.Queued = false;
            state.Previous = null;
            state.Next = null;
        }

        internal void UpdateVisuals(ZNetScene scene)
        {
            if (!ReferenceEquals(scene, visualScene) || scene != ZNetScene.instance ||
                ReferenceEquals(scene, stoppedScene) || lastVisualFrame == Time.frameCount)
                return;
            lastVisualFrame = Time.frameCount;
            int remaining = VisualsPerFrame;
            while (remaining-- > 0 && firstVisual != null)
            {
                VisualState state = firstVisual;
                RemoveQueuedVisual(state);
                if (!state.Piece)
                {
                    ReleaseVisual(state.Piece, hide: false, restoreNative: false);
                    continue;
                }
                if (!state.Matches(state.Piece))
                {
                    QueueVisual(state.Piece, state.TargetSnow, state.Disabled, force: true);
                    continue;
                }

                int index = state.Target ? Mathf.Clamp(Mathf.FloorToInt(state.TargetLevel * 100f + 0.00001f), 1, 100) : 0;
                // Hide old roots first, update every LOD/child material, then reveal
                // the selected root. A shared renderer is assigned only once.
                CapBinding targetCap = null;
                foreach (CapBinding cap in state.Caps)
                {
                    if (state.Target && cap.Renderer == state.Target && cap.Supported)
                        targetCap = cap;
                    else
                        cap.SetVisible(false);
                }
                int targetMask = targetCap != null ? targetCap.Mask : 0;
                foreach (SnowRendererBinding binding in state.Renderers)
                    binding.ApplyMaterial((binding.CapMask & targetMask) != 0 ? index : 0);
                targetCap?.SetVisible(true);
                state.HasApplied = true;
                state.Applied = state.Target;
                state.AppliedLevel = state.TargetLevel;
            }
        }

        internal void InvalidateVisual(WearNTear piece)
        {
            if (!ReferenceEquals(piece, null) && visuals.TryGetValue(piece, out VisualState state))
            {
                state.HasApplied = false;
                EnqueueVisual(state);
            }
        }

        internal void ReleaseVisual(WearNTear piece, bool hide, bool restoreNative)
        {
            if (ReferenceEquals(piece, null) || !visuals.TryGetValue(piece, out VisualState state))
                return;
            RemoveQueuedVisual(state);
            visuals.Remove(piece);
            if (hide)
                foreach (CapBinding cap in state.Caps)
                    cap.SetVisible(false);
            foreach (SnowRendererBinding binding in state.Renderers)
            {
                ownedRenderers.Remove(binding.Renderer);
                binding.ApplyMaterial(0);
                if (restoreNative && binding.Renderer)
                    RestoreToMaterialMan(binding.Renderer);
            }
        }

        internal void StopVisuals(ZNetScene scene)
        {
            if (!ReferenceEquals(scene, visualScene) && scene != ZNetScene.instance)
                return;
            ResetVisuals();
            stoppedScene = scene;
        }

        internal void ResetVisuals()
        {
            // Restore bindings before disposing their pooled materials.
            foreach (VisualState state in visuals.Values)
            {
                foreach (CapBinding cap in state.Caps)
                    cap.SetVisible(false);
                foreach (SnowRendererBinding binding in state.Renderers)
                {
                    binding.ApplyMaterial(0);
                    if (binding.Renderer)
                        RestoreToMaterialMan(binding.Renderer);
                }
            }
            visuals.Clear();
            ownedRenderers.Clear();
            unsupportedCaps.Clear();
            firstVisual = null;
            lastVisual = null;
            materials.Dispose();
            visualScene = null;
            lastVisualFrame = -1;
        }

        internal void FilterMaterialManRenderers(MaterialMan.PropertyContainer container)
        {
            if (ownedRenderers.Count == 0)
                return;
            List<Renderer> renderers = container.m_assignedRenderers;
            for (int i = renderers.Count - 1; i >= 0; --i)
                if (ownedRenderers.Contains(renderers[i]))
                    renderers.RemoveAt(i);
        }

        private static void RemoveNativeSnowProperty(WearNTear piece)
        {
            MaterialMan manager = MaterialMan.instance;
            if (manager && manager.m_blocks.TryGetValue(piece.gameObject.GetInstanceID(), out MaterialMan.PropertyContainer container) &&
                container.m_shaderProperties.Remove(WearNTear.s_snowLevel))
                manager.QueuePropertyUpdate(container);
        }

        private static void DetachFromMaterialMan(Renderer renderer)
        {
            MaterialMan manager = MaterialMan.instance;
            if (!manager)
                return;
            // Cover an already registered child, piece root, or parent container without a world scan.
            for (Transform parent = renderer.transform; parent; parent = parent.parent)
                if (manager.m_blocks.TryGetValue(parent.gameObject.GetInstanceID(), out MaterialMan.PropertyContainer container))
                    container.m_assignedRenderers.Remove(renderer);
        }

        private static void RestoreToMaterialMan(Renderer renderer)
        {
            MaterialMan manager = MaterialMan.instance;
            if (!manager)
                return;
            for (Transform parent = renderer.transform; parent; parent = parent.parent)
                if (manager.m_blocks.TryGetValue(parent.gameObject.GetInstanceID(), out MaterialMan.PropertyContainer container) &&
                    !container.m_assignedRenderers.Contains(renderer))
                {
                    container.m_assignedRenderers.Add(renderer);
                    manager.QueuePropertyUpdate(container);
                }
        }
    }
}
