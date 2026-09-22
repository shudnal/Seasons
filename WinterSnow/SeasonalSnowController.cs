using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    /// <summary>Owns the world-scoped snow runtime without attaching components to pieces.</summary>
    internal sealed partial class SeasonalSnowController
    {
        internal static SeasonalSnowController Instance { get; } = new SeasonalSnowController();
        internal const int VisualsPerFrame = 50;
        private const int InitialBindingsPerFrame = 10;
        private const double InitialBindingBudgetSeconds = 0.001d;
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
            private readonly Material original;
            private Material assignedSingle;
            private readonly SeasonalSnowMaterials.Levels singleLevels;
            private readonly Material[] originals;
            private readonly Material[] assigned;
            private readonly SeasonalSnowMaterials.Levels[] levels;
            private int appliedIndex = -1;
            internal readonly bool Supported;

            internal SnowRendererBinding(Renderer renderer, SeasonalSnowMaterials materials)
            {
                Renderer = renderer;
                Object = renderer.gameObject;
                Material[] shared = renderer.sharedMaterials;
                if (shared.Length == 1)
                {
                    original = assignedSingle = shared[0];
                    singleLevels = materials.GetLevels(original);
                    Supported = singleLevels != null;
                }
                else
                {
                    originals = shared;
                    assigned = (Material[])originals.Clone();
                    levels = new SeasonalSnowMaterials.Levels[originals.Length];
                    for (int i = 0; i < originals.Length; ++i)
                    {
                        levels[i] = materials.GetLevels(originals[i]);
                        Supported |= levels[i] != null;
                    }
                }
                // Leave unrelated child renderers entirely outside snow ownership.
                if (!Supported)
                    return;

                // Seasons owns both renderer-wide and per-slot overrides on its caps.
                renderer.SetPropertyBlock(null);
                int slots = shared.Length;
                for (int i = 0; i < slots; ++i)
                    renderer.SetPropertyBlock(null, i);
            }

            internal void ApplyMaterial(int index)
            {
                if (!Renderer || index == appliedIndex)
                    return;

                if (singleLevels != null)
                {
                    Material target = index == 0 ? original : singleLevels.Get(index);
                    if (assignedSingle != target)
                    {
                        assignedSingle = target;
                        Renderer.sharedMaterial = target;
                    }
                    appliedIndex = index;
                    return;
                }

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
                    Renderer.sharedMaterials = assigned;
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
            internal bool Bound;
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
        private readonly List<Renderer> capRenderers = new List<Renderer>(8);
        private VisualState firstVisual;
        private VisualState lastVisual;
        private ZNetScene visualScene;
        private ZNetScene stoppedScene;
        private int lastVisualFrame = -1;

        private SeasonalSnowController() { }

        internal int RegisteredVisualCount => visuals.Count;
        internal int MaterialPoolCount => materials.PoolCount;
        internal int CreatedMaterialCount => materials.CreatedMaterialCount;

        // Native visual callbacks only request the latest singleton-owned target.
        internal bool TryQueueCurrentVisual(WearNTear piece)
        {
            if (!piece)
                return false;
            if (TryGetRuntimeSnow(piece, out float runtimeSnow))
            {
                if (ZNet.instance && ZNet.instance.IsDedicated())
                    return true;
                if (runtimeSnow <= VisibilityThreshold && !visuals.ContainsKey(piece))
                {
                    HideCapRoots(piece);
                    return true;
                }
                RequestSnowVisual(piece, force: false);
                return true;
            }
            if (SeasonalSnowMeshSettings.IsSnowDisabled(piece))
            {
                DisableVisual(piece);
                return true;
            }
            if (!SeasonalSnow.WinterReady)
            {
                SeasonalSnow.ClearInactiveLoadedSnow(piece);
                ReleaseVisual(piece, hide: true, restoreNative: true);
                return false;
            }
            // Registration performs the complete eligibility/biome check once. If the
            // piece belongs to native Deep North or an ignored rule, native visuals run.
            return RequestSnowVisual(piece, force: false);
        }

        // Some roof prefabs use the same object for wet effects and a snow cap.
        // Filter only UpdateWear's read; keep the field intact for native handoff.
        internal static GameObject GetWetVisual(WearNTear piece)
        {
            GameObject wet = piece.m_wet;
            if (!wet)
                return wet;

            SeasonalSnowController controller = Instance;
            bool managed = controller.snowPieces.ContainsKey(piece) || controller.visuals.ContainsKey(piece);
            if (managed && ((piece.m_snow && piece.m_snow.gameObject == wet) ||
                (piece.m_snowWorn && piece.m_snowWorn.gameObject == wet) ||
                (piece.m_snowBroken && piece.m_snowBroken.gameObject == wet)))
                return null;

            if (controller.visuals.TryGetValue(piece, out VisualState state) && state.Bound)
            {
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
            if (!visuals.ContainsKey(piece))
            {
                HideCapRoots(piece);
                return;
            }
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

        private static void HideCapRoots(WearNTear piece)
        {
            GameObject normal = piece.m_snow ? piece.m_snow.gameObject : null;
            GameObject worn = piece.m_snowWorn ? piece.m_snowWorn.gameObject : null;
            GameObject broken = piece.m_snowBroken ? piece.m_snowBroken.gameObject : null;
            if (normal && normal.activeSelf)
                normal.SetActive(false);
            if (worn && worn != normal && worn.activeSelf)
                worn.SetActive(false);
            if (broken && broken != normal && broken != worn && broken.activeSelf)
                broken.SetActive(false);
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
            // Do not discover renderer hierarchies or allocate material bindings for a
            // cap that has never been visible.
            if (state == null && snow <= VisibilityThreshold)
            {
                HideCapRoots(piece);
                return;
            }
            if (state == null)
            {
                state = new VisualState(piece);
                visuals.Add(piece, state);
            }

            bool prioritizeHide = SetVisualTarget(state, snow, disabled);

            if (!state.HasApplied || state.Target != state.Applied ||
                Mathf.Abs(state.TargetLevel - state.AppliedLevel) + 0.000001f >= VisualStep ||
                (force && !state.TargetLevel.Equals(state.AppliedLevel)))
                EnqueueVisual(state, prioritizeHide);
        }

        private static bool SetVisualTarget(VisualState state, float snow, bool disabled)
        {
            WearNTear piece = state.Piece;
            MeshRenderer target = null;
            if (!disabled && state.Normal && snow > VisibilityThreshold)
            {
                target = piece.m_healthPercentage <= 0.25f && state.Broken ? state.Broken :
                    piece.m_healthPercentage <= 0.75f && state.Worn ? state.Worn : state.Normal;
            }
            float level = target ? Mathf.Clamp01((snow - VisibilityThreshold) / (1f - VisibilityThreshold)) : 0f;
            // Preparation can update Target more than once before rendering. Keep a
            // hide urgent while the previously applied cap is still visible.
            bool prioritizeHide = !target && (state.Target || state.Applied || (disabled && !state.Disabled));
            state.Disabled = disabled;
            state.Target = target;
            state.TargetSnow = snow;
            state.TargetLevel = level;

            return prioritizeHide;
        }

        private void BindVisualState(VisualState state)
        {
            if (state.Bound)
                return;
            state.Bound = true;
            BindCap(state, state.Normal);
            BindCap(state, state.Worn);
            BindCap(state, state.Broken);
            RemoveNativeSnowProperty(state.Piece);
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
            else if (renderer.transform.childCount != 0)
            {
                capRenderers.Clear();
                renderer.GetComponentsInChildren(includeInactive: true, capRenderers);
                foreach (Renderer child in capRenderers)
                    BindCapRenderer(state, cap, child);
                capRenderers.Clear();
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
            int initialBindings = 0;
            long bindingStart = Stopwatch.GetTimestamp();
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
                if (!state.Bound && !state.Target)
                {
                    HideCapRoots(state.Piece);
                    state.HasApplied = true;
                    state.Applied = null;
                    state.AppliedLevel = 0f;
                    continue;
                }
                if (!state.Bound)
                {
                    if (initialBindings >= InitialBindingsPerFrame ||
                        (initialBindings > 0 && (Stopwatch.GetTimestamp() - bindingStart) /
                            (double)Stopwatch.Frequency >= InitialBindingBudgetSeconds))
                    {
                        EnqueueVisual(state, prioritize: false);
                        break;
                    }
                    BindVisualState(state);
                    initialBindings++;
                }

                // Arithmetic and preparation may have advanced while this handle waited.
                // Always render its current value, never a captured intermediate target.
                if (!state.Disabled)
                {
                    if (TryGetRuntimeSnow(state.Piece, out float snow))
                        SetVisualTarget(state, snow, disabled: false);
                    else if (!SeasonalSnow.WinterReady)
                        // Cleanup may have retired the runtime before this queued visual.
                        SetVisualTarget(state, 0f, disabled: false);
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
            {
                if (state.Bound)
                    foreach (CapBinding cap in state.Caps)
                        cap.SetVisible(false);
                else if (piece)
                    HideCapRoots(piece);
            }
            if (!state.Bound)
                return;
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
                if (!state.Bound)
                {
                    if (state.Piece)
                        HideCapRoots(state.Piece);
                    continue;
                }
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
            capRenderers.Clear();
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
