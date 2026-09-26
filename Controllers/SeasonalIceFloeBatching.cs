using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static Seasons.Seasons;

namespace Seasons
{
    // Bucket/key/slot-removal and grouped drawing follow KG-BatchRenderer 1.4.0,
    // maintainer-supplied excerpts in docs/reference/kg-batchrenderer-core.md.
    // Moving matrices, reversible physics/visual state and immediate removal are
    // specific to Seasons' marked ice1 instances.
    internal static class SeasonalIceFloeBatching
    {
        private const float CellInverse = 1f / 32f;
        private const int ChunkCapacity = 500; // Below Unity's 511 default two-matrix limit.

        private readonly struct BatchKey : IEquatable<BatchKey>
        {
            internal readonly int CellX, CellZ;
            internal BatchKey(int x, int z) { CellX = x; CellZ = z; }
            public bool Equals(BatchKey other) => CellX == other.CellX && CellZ == other.CellZ;
            public override bool Equals(object value) => value is BatchKey other && Equals(other);
            public override int GetHashCode() => (CellX * 397) ^ CellZ;
        }

        private sealed class Bucket
        {
            internal readonly List<Matrix4x4> Matrices = new List<Matrix4x4>(32);
            internal readonly List<IceFloe> Instances = new List<IceFloe>(32);
        }

        private sealed class Definition
        {
            internal Mesh Mesh;
            internal Material[] Sources;
            internal Material[] Owned;
            internal RenderParams[] Params;
            internal Matrix4x4 LocalOffset;
            internal Vector3 LocalPosition, LocalScale;
            internal Quaternion LocalRotation;
            internal int Layer;
            internal ShadowCastingMode Shadows;
            internal bool ReceiveShadows;
            internal LightProbeUsage LightProbes;
            internal ReflectionProbeUsage ReflectionProbes;
            internal MotionVectorGenerationMode MotionVectors;
            internal uint RenderingLayer;
            internal int Priority;
            internal LightProbeProxyVolume ProbeVolume;
            internal GameObject ProbeOverride;
        }

        private sealed class Slot
        {
            internal BatchKey Key;
            internal int Index;
            internal Vector3 Anchor;
            internal Vector3 RootAnchor;
            internal GameObject Visual;
            internal MeshRenderer Renderer;
            internal MeshFilter Filter;
            internal MeshCollider Collider;
            internal bool OriginalActive, OriginalRenderer, OriginalCollider;
            internal bool AutoCenter, AutoInertia;
            internal Vector3 Center, Inertia;
            internal Quaternion InertiaRotation;
        }

        private static readonly Dictionary<BatchKey, Bucket> buckets = new Dictionary<BatchKey, Bucket>();
        private static readonly Dictionary<IceFloe, Slot> slots = new Dictionary<IceFloe, Slot>();
        private static readonly HashSet<IceFloe> rejected = new HashSet<IceFloe>();
        private static readonly List<Bucket> drawBuckets = new List<Bucket>();
        private static readonly List<IceFloe> retireScratch = new List<IceFloe>();
        private static readonly List<Material> materialScratch = new List<Material>(4);
        private static Definition definition;
        private static SeasonalIceFloeRenderDriver driver;
        private static bool configured, failed;
        private static int lastDrawFrame = -1, lastSubmissions;
        private static int matrixCount;

        internal static bool Configured => configured;
        internal static int BatchedCount => slots.Count;
        internal static string Status =>
            $"batchEnabled={configured} batchDriver={(driver && driver.enabled)} native={SeasonalIceFloeWaves.ParticipantCount - slots.Count} " +
            $"batched={slots.Count} rejected={rejected.Count} cells={buckets.Count} matrices={matrixCount} submissions={lastSubmissions} " +
            $"drawFrame={lastDrawFrame} batchFailed={failed}";

        internal static void Configure(bool enabled)
        {
            // The ordinary synchronized JSON notification is also the material
            // refresh boundary; no material-array discovery occurs in a frame loop.
            RetireAll();
            configured = enabled;
            failed = false;
        }

        internal static void RefreshSourceMaterials()
        {
            if (configured && slots.Count != 0)
                RetireAll();
        }

        internal static void Reset()
        {
            RetireAll();
            failed = false;
            driver = null;
        }

        private static void RetireAll()
        {
            retireScratch.Clear();
            rejected.Clear();
            retireScratch.AddRange(slots.Keys);
            foreach (IceFloe floe in retireScratch)
            {
                try { ReturnNative(floe); }
                catch (Exception exception) { LogWarning($"Floe visual restoration failed: {exception}"); }
            }
            retireScratch.Clear();
            if (driver)
                driver.enabled = false;
            DisposeDefinition();
            buckets.Clear();
            matrixCount = lastSubmissions = 0;
        }

        private static void DisposeDefinition()
        {
            if (definition == null)
                return;
            HashSet<Material> ownedCopies = new HashSet<Material>(definition.Owned);
            foreach (Material material in ownedCopies)
                if (material)
                    UnityEngine.Object.Destroy(material);
            definition = null;
        }

        private static BatchKey Cell(Vector3 anchor) =>
            new BatchKey(Mathf.FloorToInt(anchor.x * CellInverse), Mathf.FloorToInt(anchor.z * CellInverse));

        private static bool Eligible(IceFloe floe) => configured && !failed && floe && floe.WaveValid &&
            (floe.OwnerlessKinematic || floe.Distant) && floe.Body && floe.Body.isKinematic &&
            floe.m_view && floe.m_view.IsValid() && ZNet.instance && !ZNet.instance.IsDedicated();

        internal static void Reconcile(IceFloe floe)
        {
            if (!Eligible(floe))
            {
                rejected.Remove(floe);
                ReturnNative(floe);
                return;
            }
            if (rejected.Contains(floe))
                return;
            if (!slots.TryGetValue(floe, out Slot slot))
            {
                bool admitted;
                try { admitted = TryAdmit(floe); }
                catch (Exception exception)
                {
                    failed = true;
                    LogWarning($"Seasonal floe instancing admission stopped: {exception}");
                    RetireAll();
                    return;
                }
                if (!admitted)
                {
                    rejected.Add(floe);
                    return;
                }
                slot = slots[floe];
            }
            if (!slot.Visual || slot.Visual.activeSelf || !slot.Renderer || !slot.Renderer.enabled ||
                !slot.Collider || !slot.Collider.enabled || floe.m_floating.m_collider != slot.Collider ||
                !slot.Filter || slot.Filter.sharedMesh != definition.Mesh ||
                slot.Visual.transform.localPosition != definition.LocalPosition ||
                slot.Visual.transform.localRotation != definition.LocalRotation ||
                slot.Visual.transform.localScale != definition.LocalScale)
            {
                ReturnNative(floe);
                floe.RebuildHullGeometry();
                rejected.Add(floe);
                return;
            }
            // FarVisual flattens an off-center root. Its changing hull-center XZ
            // is ordinary tilt, not a new world-cell assignment.
            Vector3 rootDelta = floe.Body.position - slot.RootAnchor;
            bool relocated = rootDelta.x * rootDelta.x + rootDelta.z * rootDelta.z > 16f;
            Vector3 anchor = floe.Distant && !relocated ? slot.Anchor : floe.CurrentHullAnchor;
            BatchKey next = Cell(anchor);
            if (!next.Equals(slot.Key))
            {
                MoveCell(floe, slot, next, anchor);
                slot.RootAnchor = floe.Body.position;
            }
            else if (floe.Distant && relocated)
            {
                slot.Anchor = anchor;
                slot.RootAnchor = floe.Body.position;
            }
            buckets[slot.Key].Matrices[slot.Index] = floe.Root.localToWorldMatrix * definition.LocalOffset;
        }

        private static bool TryAdmit(IceFloe floe)
        {
            if (!SystemInfo.supportsInstancing || !floe.PrepareInactiveHull())
                return false;
            Transform child = floe.Root.Find("default");
            if (!child || child.parent != floe.Root || child.childCount != 0)
                return false;
            GameObject visual = child.gameObject;
            MeshCollider collider = child.GetComponent<MeshCollider>();
            MeshRenderer renderer = child.GetComponent<MeshRenderer>();
            MeshFilter filter = child.GetComponent<MeshFilter>();
            if (!visual.activeSelf || !collider || !collider.enabled || !renderer || !renderer.enabled ||
                !filter || !filter.sharedMesh || renderer.HasPropertyBlock() ||
                renderer.probeAnchor || (renderer.lightProbeUsage == LightProbeUsage.UseProxyVolume &&
                    !renderer.lightProbeProxyVolumeOverride) || floe.m_floating.m_collider != collider)
                return false;
            if (definition == null && !CreateDefinition(floe, child, filter, renderer))
                return false;
            if (!MatchesDefinition(child, filter, renderer))
            {
                if (slots.Count == 0) DisposeDefinition();
                return false;
            }
            if (!driver)
            {
                if (!ZoneSystem.instance)
                {
                    if (slots.Count == 0) DisposeDefinition();
                    return false;
                }
                driver = ZoneSystem.instance.GetComponent<SeasonalIceFloeRenderDriver>();
                if (!driver)
                    driver = ZoneSystem.instance.gameObject.AddComponent<SeasonalIceFloeRenderDriver>();
            }
            Slot slot = new Slot
            {
                Visual = visual, Renderer = renderer, Filter = filter, Collider = collider,
                OriginalActive = visual.activeSelf, OriginalRenderer = renderer.enabled,
                OriginalCollider = collider.enabled, AutoCenter = floe.Body.automaticCenterOfMass,
                AutoInertia = floe.Body.automaticInertiaTensor, Center = floe.Body.centerOfMass,
                Inertia = floe.Body.inertiaTensor, InertiaRotation = floe.Body.inertiaTensorRotation,
                Anchor = floe.CurrentHullAnchor, RootAnchor = floe.Body.position
            };
            slot.Key = Cell(slot.Anchor);
            AddSlot(floe, slot);
            // Freeze the established dynamic shape's COM/inertia while Unity sees
            // no collider. Restore the original automatic/manual mode on exit.
            floe.Body.automaticCenterOfMass = false;
            floe.Body.centerOfMass = slot.Center;
            floe.Body.automaticInertiaTensor = false;
            floe.Body.inertiaTensor = slot.Inertia;
            floe.Body.inertiaTensorRotation = slot.InertiaRotation;
            visual.SetActive(false);
            driver.enabled = true;
            return true;
        }

        private static bool CreateDefinition(IceFloe floe, Transform child, MeshFilter filter, MeshRenderer renderer)
        {
            materialScratch.Clear();
            renderer.GetSharedMaterials(materialScratch);
            Definition candidate = null;
            try
            {
                if (materialScratch.Count == 0 || filter.sharedMesh.subMeshCount == 0)
                    return false;
                candidate = new Definition
                {
                    Mesh = filter.sharedMesh, Sources = materialScratch.ToArray(),
                    LocalOffset = floe.Root.worldToLocalMatrix * child.localToWorldMatrix,
                    LocalPosition = child.localPosition, LocalRotation = child.localRotation,
                    LocalScale = child.localScale, Layer = child.gameObject.layer,
                    Shadows = renderer.shadowCastingMode, ReceiveShadows = renderer.receiveShadows,
                    LightProbes = renderer.lightProbeUsage, ReflectionProbes = renderer.reflectionProbeUsage,
                    MotionVectors = renderer.motionVectorGenerationMode,
                    RenderingLayer = renderer.renderingLayerMask, Priority = renderer.rendererPriority,
                    ProbeOverride = renderer.lightProbeProxyVolumeOverride,
                    ProbeVolume = renderer.lightProbeProxyVolumeOverride
                        ? renderer.lightProbeProxyVolumeOverride.GetComponent<LightProbeProxyVolume>() : null
                };
                if (candidate.ProbeOverride && !candidate.ProbeVolume)
                    return false;
                candidate.Owned = new Material[candidate.Sources.Length];
                int draws = Mathf.Max(candidate.Mesh.subMeshCount, candidate.Sources.Length);
                candidate.Params = new RenderParams[draws];
                Dictionary<Material, Material> copies = new Dictionary<Material, Material>();
                for (int i = 0; i < candidate.Sources.Length; ++i)
                {
                    Material source = candidate.Sources[i];
                    if (!source)
                    {
                        foreach (Material owned in copies.Values)
                            if (owned) UnityEngine.Object.Destroy(owned);
                        return false;
                    }
                    if (!copies.TryGetValue(source, out Material copy))
                    {
                        copy = new Material(source) { enableInstancing = true, name = source.name + " (Seasons floe instances)" };
                        copies.Add(source, copy);
                    }
                    candidate.Owned[i] = copy;
                }
                for (int i = 0; i < draws; ++i)
                {
                    Material copy = candidate.Owned[Mathf.Min(i, candidate.Owned.Length - 1)];
                    candidate.Params[i] = new RenderParams(copy)
                    {
                        layer = candidate.Layer, shadowCastingMode = candidate.Shadows,
                        receiveShadows = candidate.ReceiveShadows, lightProbeUsage = candidate.LightProbes,
                        reflectionProbeUsage = candidate.ReflectionProbes, motionVectorMode = candidate.MotionVectors,
                        renderingLayerMask = candidate.RenderingLayer, rendererPriority = candidate.Priority,
                        lightProbeProxyVolume = candidate.ProbeVolume
                    };
                }
                definition = candidate;
                return true;
            }
            catch
            {
                if (candidate?.Owned != null)
                    foreach (Material owned in new HashSet<Material>(candidate.Owned))
                        if (owned) UnityEngine.Object.Destroy(owned);
                throw;
            }
            finally { materialScratch.Clear(); }
        }

        private static bool MatchesDefinition(Transform child, MeshFilter filter, MeshRenderer renderer)
        {
            if (definition == null || filter.sharedMesh != definition.Mesh ||
                child.localPosition != definition.LocalPosition || child.localRotation != definition.LocalRotation ||
                child.localScale != definition.LocalScale || child.gameObject.layer != definition.Layer ||
                renderer.shadowCastingMode != definition.Shadows || renderer.receiveShadows != definition.ReceiveShadows ||
                renderer.lightProbeUsage != definition.LightProbes || renderer.reflectionProbeUsage != definition.ReflectionProbes ||
                renderer.motionVectorGenerationMode != definition.MotionVectors ||
                renderer.renderingLayerMask != definition.RenderingLayer || renderer.rendererPriority != definition.Priority ||
                renderer.lightProbeProxyVolumeOverride != definition.ProbeOverride || renderer.probeAnchor)
                return false;
            materialScratch.Clear();
            renderer.GetSharedMaterials(materialScratch);
            bool matches = materialScratch.Count == definition.Sources.Length;
            for (int i = 0; matches && i < materialScratch.Count; ++i)
                matches = materialScratch[i] == definition.Sources[i];
            materialScratch.Clear();
            return matches;
        }

        private static void AddSlot(IceFloe floe, Slot slot)
        {
            if (!buckets.TryGetValue(slot.Key, out Bucket bucket))
                buckets.Add(slot.Key, bucket = new Bucket());
            slot.Index = bucket.Matrices.Count;
            bucket.Matrices.Add(floe.Root.localToWorldMatrix * definition.LocalOffset);
            bucket.Instances.Add(floe);
            slots.Add(floe, slot);
            matrixCount++;
        }

        private static void RemoveSlot(IceFloe floe, Slot slot)
        {
            Bucket bucket = buckets[slot.Key];
            int last = bucket.Matrices.Count - 1;
            if (slot.Index != last)
            {
                bucket.Matrices[slot.Index] = bucket.Matrices[last];
                IceFloe moved = bucket.Instances[last];
                bucket.Instances[slot.Index] = moved;
                slots[moved].Index = slot.Index;
            }
            bucket.Matrices.RemoveAt(last);
            bucket.Instances.RemoveAt(last);
            if (bucket.Matrices.Count == 0)
                buckets.Remove(slot.Key);
            slots.Remove(floe);
            matrixCount--;
        }

        private static void MoveCell(IceFloe floe, Slot slot, BatchKey next, Vector3 anchor)
        {
            RemoveSlot(floe, slot);
            slot.Key = next;
            slot.Anchor = anchor;
            AddSlot(floe, slot);
        }

        internal static void ReturnNative(IceFloe floe)
        {
            rejected.Remove(floe);
            if (ReferenceEquals(floe, null) || !slots.TryGetValue(floe, out Slot slot))
                return;
            RemoveSlot(floe, slot);
            if (floe && slot.Visual)
            {
                if (slot.OriginalActive && !slot.Visual.activeSelf)
                    slot.Visual.SetActive(true);
            }
            // Renderer/Collider.enabled were never changed by this feature.
            // Respect any later third-party change to those exact flags.
            if (floe && floe.Body)
            {
                if (slot.AutoCenter)
                    floe.Body.ResetCenterOfMass();
                else
                    floe.Body.centerOfMass = slot.Center;
                if (slot.AutoInertia)
                    floe.Body.ResetInertiaTensor();
                else
                {
                    floe.Body.inertiaTensor = slot.Inertia;
                    floe.Body.inertiaTensorRotation = slot.InertiaRotation;
                }
            }
            if (slots.Count == 0)
            {
                if (driver)
                    driver.enabled = false;
                DisposeDefinition();
            }
        }

        internal static void Draw()
        {
            if (slots.Count == 0 || definition == null)
            {
                if (driver) driver.enabled = false;
                return;
            }
            lastDrawFrame = Time.frameCount;
            lastSubmissions = 0;
            drawBuckets.Clear();
            drawBuckets.AddRange(buckets.Values);
            try
            {
                foreach (Bucket bucket in drawBuckets)
                {
                    int count = bucket.Matrices.Count;
                    for (int start = 0; start < count && definition != null; start += ChunkCapacity)
                    {
                        int chunk = Mathf.Min(ChunkCapacity, count - start);
                        for (int material = 0; material < definition.Params.Length; ++material)
                        {
                            int submesh = Mathf.Min(material, definition.Mesh.subMeshCount - 1);
                            Graphics.RenderMeshInstanced(ref definition.Params[material], definition.Mesh,
                                submesh, bucket.Matrices, chunk, start);
                            lastSubmissions++;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                failed = true;
                LogWarning($"Seasonal floe instancing stopped; native visuals restored: {exception}");
                RetireAll();
            }
            finally { drawBuckets.Clear(); }
        }
    }

    [DefaultExecutionOrder(20000)]
    internal sealed class SeasonalIceFloeRenderDriver : MonoBehaviour
    {
        private void LateUpdate() => SeasonalIceFloeBatching.Draw();
    }
}
