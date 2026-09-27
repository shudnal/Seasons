# Immutable floe geometry and render slots

Base: `25fb072ec34b60ebe28dfc640a6e20957ef5fe79`, `perf/snow-performance`, PR #45.
Date: 2026-09-27.

The maintainer confirmed that seasonal ice1 uses a fixed hierarchy and mesh.
Its default child contains only MeshCollider, MeshRenderer and MeshFilter, with
local scale (4, 4, 4). Spawn scale belongs to the root and does not change while
a floe rocks. This contract replaces the earlier per-frame defensive discovery
of arbitrary mesh, hierarchy, renderer and collider edits.

## Geometry

`ReadHullGeometry` prepares the scaled shape once per admitted instance, or after
explicit invalidation. Mesh bounds, collider attachment, child transforms and
world scale are read only in that preparation branch. A cached read only derives
the world center and vertical extent from the stored offsets and current body
pose. Intrinsic thickness remains cached. Kinematic settings reuse the cached
world scale instead of reading lossyScale per instance per rendered frame.

There is no repeated parent/sharedMesh/local-transform comparison, scale-noise
comparison, tolerance classification or associated noise counter. The existing
GeometryCacheBuilds/HullRebuilds counters still count actual preparation.
The hover now reports `geometryValidation=explicit`; the old noise columns were
removed rather than kept as permanently zero diagnostics.

The nearby dynamic solver still checks its real live body and active collider.
Registration and destruction, finite pose validation, native ownership and
lease checks remain intact. This is not permission to simulate a destroyed body
or to publish without current authority. The existing force and wave formulas,
full heave/filtered tilt split, curve storage and background work are unchanged.

## Scale application and explicit edits

The existing scale-source cache is shared by the managed motion modes. Where
`ZSyncTransform.m_syncScale` is enabled, the input is read only for a changed ZDO
payload or a newly established source. The saved scale is compared with the
previously accepted numeric value, not a hierarchy snapshot. An ordinary pose
or lease publication therefore does not rebuild the geometry.

A changed scale explicitly retires a visual slot and invalidates geometry and
its pending forecast. Nonowners apply the new scale before surface sampling;
the native owner is never overwritten with a received scale and continues its
existing OwnerSync publication. Native sync callbacks are not patched or
replaced. Initial saved scale is already applied before first hull preparation.
No speculative general-purpose transform watcher was added.

`RebuildHullGeometry()` remains the explicit instance notification for manual
inspector/integration edits. It restores any batched default child and automatic
COM/inertia mode, cancels this floe's old forecast, and schedules fresh geometry
through the next normal use. Calling it does not regenerate floes, change wind
or write ZDO. A changed visual definition is validated again at admission;
unsupported definitions stay on native rendering. Silent third-party mesh,
parent, child transform or renderer mutations during a live slot are not polled.
After such an intentional edit, notify the instance explicitly. Normal root
scale synchronization is handled by the existing scale path instead.

## Rendering

An admitted floe keeps a direct render-slot handle. The slot points to its bucket
and matrix index. Normal `Reconcile` retains live eligibility checks but no
longer re-reads default's component states, shared mesh or local transform, nor
looks up the slot/bucket in dictionaries. It writes the accepted current matrix
into that known slot. Material/template checks stay at admission and the
existing explicit configuration/material refresh boundaries.

The list of nonempty draw buckets is persistent. Creating/removing a bucket
updates that list once, with last-entry swap removal and corrected indices.
Draw no longer clears and rebuilds it from the dictionary each frame. Instance
removal likewise repairs the moved floe's direct slot index and clears the
removed handle immediately. Dictionaries remain lifecycle indexes, not the
normal matrix-update route.

Ordinary simulator rocking and FarVisual bob do not reclassify XZ cells. Entry
into batching assigns the cell. Real horizontal movement of a received replica
and return from FarVisual explicitly update the anchor. Scale/explicit shape
edits retire and re-admit the slot. Automatic graphics bounds still use actual
matrices, not the cell square. No camera test, visibility-based motion policy,
new scheduler or background matrix calculation was introduced.

Keep 96 x 96 metre cells, 500 instances per submission, the existing material
copies, shadows, pause rendering, mode transitions and the synchronized
`rendering.enableInstancedRendering` toggle. This change does not attempt the
previously noted disabled-WaterVolume OnEnable correction or snow optimization.

## Verification boundary

Source files were matched to their Git blob hashes before editing. Static review
covers the changed control flow, native ZSyncTransform scale behavior, direct
slot/list removal pairs, cached-shape use with inactive default, forecast
cancellation on explicit edits, textual references and C# lexical structure.
No mod build, automated mod tests, physics/threading harness, game execution or
performance measurement is performed here.

For the maintainer: compare the same loaded floes, camera, wind, render distance
and 96 m cells before/after, then batching off/on. No respawn or settings reset is
needed. Verify dynamic/ownerless/FarVisual transitions, replica movement,
negative/crossed cell edges, pause, batch toggle and immediate deletion. Actual
hull-build counts should not grow from rocking. To verify an intentional manual
shape change, notify that instance with RebuildHullGeometry. Multiplayer scale
application and measured speedup remain to be verified in game.
