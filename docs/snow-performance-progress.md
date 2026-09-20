# Snow performance implementation progress

Design reference: [snow-performance.md](snow-performance.md).
Baseline: `f77249d625dadf5e5262cb969a9a8455e3c7ccbc` on `perf/snow-performance`.

## Current boundary

The cap-rendering stage is connected. The simulation and persistence replacement is **not** connected yet. This branch is an incremental implementation, not the completed optimization or a release candidate.

The existing `SeasonalSnow` calculation is deliberately still the producer of saved/provisional snow values. Its `IsAreaReady`, active-area, ownership, timeline and interaction behavior have not been rewritten in this stage. The last-priority prefix on the native `WearNTear.UpdateSnowVisual` consumes the value selected by the existing prefix, queues the latest target, and prevents the native material update for managed caps. No Seasons method is patched.

This is a temporary adapter for separating rendering from simulation. Remove the old visual-value substitution and this adapter when the new snapshot/working-state path is connected; do not retain two independent snow simulations.

## Landed slices

| Commit | Scope |
| --- | --- |
| `69d848c` | Immutable material pools, one original and up to 100 lazy variants per source material. |
| `4a65dde` | Singleton-owned renderer bindings, latest-target visual queue, 0.01 visual change gate, and at most 50 applications per frame. |
| `ff9f85d` | Disabled and copied caps use the same ownership/cleanup path. Remove MaterialMan calls from `SeasonalSnowMeshSettings`. |
| Scene-lifecycle follow-up | Scope the queue to its ZNetScene, reject late stopped-scene work, prioritize hides, restore native Ignore visuals once, and bound unsupported-material warnings. |

No version, gameplay configuration default, snow JSON schema, or release changelog has changed.

## Implemented rendering behavior

- Capture each distinct normal/worn/broken renderer once, preserving source materials and non-snow material slots.
- Preserve the native 0.25 visibility threshold and damaged-cap fallback order.
- Clear renderer-wide and per-slot property blocks when taking ownership. Exclude managed caps from existing MaterialMan assignments and future renderer refreshes, without filtering unrelated piece renderers.
- Reuse `sharedMaterial` variants; do not mutate originals, use `renderer.material`, or involve `PrefabVariantController`.
- Deduplicate pending visuals. A later value replaces the target rather than appending another percentage to replay.
- Restore original materials before destroying pooled clones. Release copied renderers before removing their objects. No additional MonoBehaviour is attached to a piece or heater.
- Drive visual application from the existing scene Update callback, once per frame for the current scene. Reject shutdown callbacks from a different scene.
- Do not create material variants on a dedicated server.

## Still required by the approved plan

1. Seasons-owned float snapshot and winter epoch, activation-only legacy migration, and removal of positive seasonal writes to native snow state. Until then, this branch does **not** provide the promised mod-removal persistence guarantee.
2. Saved-first initialization, including an explicitly saved zero, followed by one complete ready-area confirmation. Queue coalescing is in place, but it does not by itself replace the existing initialization rules.
3. Four region-partitioned simulation buckets and region-filtered state-refresh work, replacing seasonal calculations in UpdateWear.
4. Cached stationary heat links, additive independent sources, preserved live interactions, and the new covered-piece melt setting.
5. Event-driven custom roof invalidation and separate geometry/publication budgets.
6. The agreed gameplay changes (discovery-only minimum, clean construction, full 1.0 maximum, and gradual melting of real snow under newly added cover).
7. Maintainer-side game verification and comparable performance measurements. No FPS improvement is claimed from static inspection alone.

## Verification performed for these slices

Static review of native target signatures, project XML/source inclusion, duplicate includes, changed-file scope, lexical delimiter balance, and accidental Cyrillic outside localization. Remote content hashes are compared with the reviewed local files.

No compilation, mod execution, automated gameplay tests, or in-game profiling was performed.
