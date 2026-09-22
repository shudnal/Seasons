using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seasons
{
    // A client-only presentation helper. Proxies contain only a mesh and renderer;
    // even a mesh that shares its object with a collider can move without physics.
    internal sealed class SeasonalIceFloeVisual : IDisposable
    {
        private const float SmoothingRate = 6f;
        private const float MaximumInterpolationStep = 0.25f;
        private const float AlignedOffset = 0.001f;
        private const float AlignedAngle = 0.05f;

        private sealed class LodState
        {
            internal readonly LODGroup Group;
            internal readonly LOD[] Original;
            internal readonly LOD[] Cosmetic;

            internal LodState(LODGroup group)
            {
                Group = group;
                Original = group.GetLODs();
                Cosmetic = (LOD[])Original.Clone();
                for (int i = 0; i < Cosmetic.Length; ++i)
                    Cosmetic[i].renderers = (Renderer[])Original[i].renderers.Clone();
            }
        }

        private readonly struct LodSlot
        {
            internal readonly LodState State;
            internal readonly int Level;
            internal readonly int Index;

            internal LodSlot(LodState state, int level, int index)
            {
                State = state;
                Level = level;
                Index = index;
            }
        }

        private sealed class Binding
        {
            internal readonly MeshRenderer Original;
            internal readonly Transform Source;
            internal readonly MeshRenderer Proxy;
            internal readonly MeshFilter ProxyFilter;
            internal readonly Transform Transform;
            internal readonly bool OriginalForceRenderingOff;
            internal readonly List<LodSlot> LodSlots = new List<LodSlot>();
            internal readonly List<LodState> LodGroups = new List<LodState>();
            internal bool Active;
            internal bool Aligned = true;
            internal float Offset;
            internal Quaternion Tilt = Quaternion.identity;
            internal float LastApply = -1f;

            internal Binding(MeshRenderer original, MeshFilter filter)
            {
                Original = original;
                Source = original.transform;
                OriginalForceRenderingOff = original.forceRenderingOff;
                GameObject visual = new GameObject("Seasons floe visual");
                visual.layer = original.gameObject.layer;
                Transform = visual.transform;
                Transform.SetParent(Source, false);
                ProxyFilter = visual.AddComponent<MeshFilter>();
                ProxyFilter.sharedMesh = filter.sharedMesh;
                Proxy = visual.AddComponent<MeshRenderer>();
                Proxy.enabled = false;
                Proxy.sharedMaterials = original.sharedMaterials;
                Proxy.additionalVertexStreams = original.additionalVertexStreams;
                Proxy.shadowCastingMode = original.shadowCastingMode;
                Proxy.receiveShadows = original.receiveShadows;
                Proxy.lightProbeUsage = original.lightProbeUsage;
                Proxy.reflectionProbeUsage = original.reflectionProbeUsage;
                Proxy.probeAnchor = original.probeAnchor;
                Proxy.lightProbeProxyVolumeOverride = original.lightProbeProxyVolumeOverride;
                Proxy.lightmapIndex = original.lightmapIndex;
                Proxy.lightmapScaleOffset = original.lightmapScaleOffset;
                Proxy.realtimeLightmapIndex = original.realtimeLightmapIndex;
                Proxy.realtimeLightmapScaleOffset = original.realtimeLightmapScaleOffset;
                Proxy.sortingLayerID = original.sortingLayerID;
                Proxy.sortingOrder = original.sortingOrder;
                Proxy.motionVectorGenerationMode = original.motionVectorGenerationMode;
                Proxy.allowOcclusionWhenDynamic = original.allowOcclusionWhenDynamic;
                Proxy.forceRenderingOff = OriginalForceRenderingOff;

                // Preserve both renderer-wide and per-material shader state without
                // instantiating materials or retaining shared temporary arrays.
                MaterialPropertyBlock properties = new MaterialPropertyBlock();
                original.GetPropertyBlock(properties);
                Proxy.SetPropertyBlock(properties);
                int slots = original.sharedMaterials.Length;
                for (int i = 0; i < slots; ++i)
                {
                    original.GetPropertyBlock(properties, i);
                    Proxy.SetPropertyBlock(properties, i);
                }
            }

            internal void Activate()
            {
                Active = true;
                Original.forceRenderingOff = true;
                Proxy.enabled = Original.enabled;
                foreach (LodSlot slot in LodSlots)
                    slot.State.Cosmetic[slot.Level].renderers[slot.Index] = Proxy;
                foreach (LodState state in LodGroups)
                {
                    if (state.Group)
                        state.Group.SetLODs(state.Cosmetic);
                }
            }
        }

        private readonly Transform root;
        private readonly Binding[] bindings;
        private float targetOffset;
        private Quaternion targetTilt = Quaternion.identity;
        private int unaligned;
        private int activeCount;
        private bool disposed;

        internal SeasonalIceFloeVisual(Transform root)
        {
            this.root = root;
            // This is the sole hierarchy walk, performed when the tracked floe is
            // first given a client presentation helper, never by Apply or Visible.
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            List<Binding> captured = new List<Binding>(renderers.Length);
            Dictionary<Renderer, Binding> byRenderer = new Dictionary<Renderer, Binding>();
            foreach (MeshRenderer renderer in renderers)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh)
                    continue;
                Binding binding = new Binding(renderer, filter);
                captured.Add(binding);
                byRenderer.Add(renderer, binding);
            }
            bindings = captured.ToArray();

            LODGroup[] groups = root.GetComponentsInChildren<LODGroup>(true);
            for (int i = 0; i < groups.Length; ++i)
            {
                LodState state = new LodState(groups[i]);
                for (int level = 0; level < state.Original.Length; ++level)
                {
                    Renderer[] levelRenderers = state.Original[level].renderers;
                    for (int slot = 0; slot < levelRenderers.Length; ++slot)
                    {
                        Renderer renderer = levelRenderers[slot];
                        if (!renderer || !byRenderer.TryGetValue(renderer, out Binding binding))
                            continue;
                        binding.LodSlots.Add(new LodSlot(state, level, slot));
                        if (!binding.LodGroups.Contains(state))
                            binding.LodGroups.Add(state);
                    }
                }
            }
        }

        internal int Count => disposed ? 0 : bindings.Length;
        internal int ActiveCount => activeCount;
        internal bool IsAligned => unaligned == 0;

        internal bool Visible
        {
            get
            {
                if (disposed)
                    return false;
                foreach (Binding binding in bindings)
                {
                    Renderer renderer = binding.Active ? binding.Proxy : binding.Original;
                    if (renderer && renderer.enabled && renderer.isVisible)
                        return true;
                }
                return false;
            }
        }

        internal void SetTarget(float worldOffsetY, Quaternion worldTilt)
        {
            targetOffset = worldOffsetY;
            targetTilt = worldTilt;
        }

        // The central scheduler chooses the index and counts each true return as
        // one transform write. No loop over bindings is hidden in this method.
        internal bool Apply(int index, float dt)
        {
            if (disposed || !root || index < 0 || index >= bindings.Length)
                return false;
            Binding binding = bindings[index];
            if (!binding.Original || !binding.Proxy)
            {
                SetAligned(binding, true);
                return false;
            }
            if (!binding.Active)
            {
                if (Mathf.Abs(targetOffset) <= AlignedOffset && Quaternion.Angle(targetTilt, Quaternion.identity) <= AlignedAngle)
                    return false;
                binding.Activate();
                activeCount++;
            }
            float now = Time.time;
            float elapsed = binding.LastApply < 0f ? dt : Mathf.Max(0f, now - binding.LastApply);
            binding.LastApply = now;
            float fraction = 1f - Mathf.Exp(-SmoothingRate * Mathf.Clamp(elapsed, 0f, MaximumInterpolationStep));
            binding.Offset = Mathf.Lerp(binding.Offset, targetOffset, fraction);
            binding.Tilt = Quaternion.Slerp(binding.Tilt, targetTilt, fraction);
            bool aligned = Mathf.Abs(binding.Offset) <= AlignedOffset && Quaternion.Angle(binding.Tilt, Quaternion.identity) <= AlignedAngle;
            SetAligned(binding, aligned);
            // Snap only when returning to the physical baseline, leaving ordinary
            // remote wave targets continuously interpolated.
            if (aligned && targetOffset == 0f && targetTilt == Quaternion.identity)
            {
                binding.Offset = 0f;
                binding.Tilt = Quaternion.identity;
            }
            binding.Proxy.enabled = binding.Original.enabled;
            binding.Original.forceRenderingOff = true;
            Vector3 pivot = root.position;
            Vector3 position = pivot + binding.Tilt * (binding.Source.position - pivot) + Vector3.up * binding.Offset;
            binding.Transform.SetPositionAndRotation(position, binding.Tilt * binding.Source.rotation);
            return true;
        }

        private void SetAligned(Binding binding, bool aligned)
        {
            if (binding.Aligned == aligned)
                return;
            binding.Aligned = aligned;
            unaligned += aligned ? -1 : 1;
        }

        // Ordinary visibility/mode returns use this same per-renderer scheduler
        // budget as Apply. Other active proxies keep their current LOD mappings.
        internal bool Restore(int index)
        {
            if (disposed || index < 0 || index >= bindings.Length)
                return false;
            Binding binding = bindings[index];
            if (!binding.Active)
                return false;
            if (binding.Original)
                binding.Original.forceRenderingOff = binding.OriginalForceRenderingOff;
            if (binding.Proxy)
                binding.Proxy.enabled = false;
            bool wroteTransform = binding.Transform;
            if (wroteTransform)
                binding.Transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (LodSlot slot in binding.LodSlots)
                slot.State.Cosmetic[slot.Level].renderers[slot.Index] = binding.Original;
            foreach (LodState state in binding.LodGroups)
                if (state.Group)
                    state.Group.SetLODs(state.Cosmetic);
            binding.Active = false;
            SetAligned(binding, true);
            binding.Offset = 0f;
            binding.Tilt = Quaternion.identity;
            binding.LastApply = -1f;
            activeCount--;
            return wroteTransform;
        }

        internal void Restore()
        {
            if (disposed)
                return;
            targetOffset = 0f;
            targetTilt = Quaternion.identity;
            if (activeCount == 0)
                return;
            for (int i = 0; i < bindings.Length; ++i)
                if (bindings[i].Active)
                    Restore(i);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            Restore();
            foreach (Binding binding in bindings)
                if (binding.Proxy)
                {
                    // Destroy is deferred. A same-frame disable/enable must not discover
                    // a retired proxy as an original renderer during the next capture.
                    if (binding.ProxyFilter)
                        binding.ProxyFilter.sharedMesh = null;
                    UnityEngine.Object.Destroy(binding.Proxy.gameObject);
                }
            disposed = true;
        }
    }
}
