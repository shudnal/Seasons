# Agreement audit and deferred performance review

Date: 2026-09-23. Branch: `perf/snow-performance`.
Review baseline: `14c5a5fbff9b6eb76da5a9cc7b219a8612ce4733`.
Corrected code: `dd14897ce9f6d36dd8891470cc55ddedfb73303e`.

This report supplements [the agreed implementation specification](snow-diagnostics-and-floe-followup.md). It does not replace the agreed floe model or authorize another redesign. The maintainer requested fixes for the three preceding findings, a check against the accepted decisions, and a separate performance review of new and older functionality. Additional performance changes were explicitly deferred.

Follow-up: the maintainer subsequently approved material-binding allocation improvements and change-gated water properties (items 1 and 2 in the chat summary, P1 and P3 below). They are implemented in section 6. Section 7 records the later approval and implementation of heading-only floe support caching (P6) and per-call water-context reuse (the narrow part of P7). Lighting (P2), mass recoloring (P4), minimap preparation (P5), distant scheduling (P8), and external placement notifications (P9) remain unchanged. Section 7 supersedes earlier requirements to monitor floe shape, scale, or hierarchy changes at runtime.

## 1. Changes actually committed

### 1.1 Skip pure land before sector discovery

Commit: `1d194744ca624dd880af56e5edd68f6f162a23cc`.
File: `Controllers/SeasonalIceFloes.cs`.

`ReadyGeometry` now precedes the pending work's controller discovery. A prepared ordinary Heightmap with no Ocean corner biome settles locally through `HaveBiome(Ocean)` before sector inspection or candidate placement. This restores the conservative prefilter lost in the previous rewrite.

This is not the rejected center-biome test. Mixed coastal heightmaps continue to candidate-level checks. The prefilter does not write a completed marker into a dry zone merely to cache that local conclusion. Existing cached-marker and foreign-owner early exits remain intact. Unavailable geometry is deferred rather than classified as dry.

### 1.2 Do not re-inspect the zone after adding our own floe

Commit: `1d194744ca624dd880af56e5edd68f6f162a23cc`.
File: `Controllers/SeasonalIceFloes.cs`.

Native ZNetView initialization adds a ZDO to its sector before all prefab fields are available. The previous AddToSector patch therefore restarted pending inspection on every floe created by that very job. Created IDs prevented false duplicate detection, but did not avoid repeated scans.

A synchronous creation scope now records matching-zone sector additions until the exact newly created floe ZDO is known. Only an addition of that exact object is exempted. A different/reentrant addition, including one before the floe's own Awake, still requests reinspection. Notifications for other zones continue normally. There is no blanket suppression of network arrivals and no early filter using an uninitialized prefab field.

The scope uses a work reference, a ZDO reference and a boolean; no temporary notification list is allocated. It closes in `finally`, together with balanced ghost initialization and restoration of the existing private RNG. Nested candidate entry is deferred. The scope never survives a queued yield. Exclusions and Created IDs continue to account for the successful new floe.

### 1.3 Do not reuse an old fallback as a live center-water observation

Commit: `dd14897ce9f6d36dd8891470cc55ddedfb73303e`.
File: `Controllers/SeasonalIceFloeWaves.cs`.

`Floating.m_waterLevel` survives disable/re-enable and may contain the previous mathematical fallback. Track no longer copies it into CallbackLevel. A new floe record starts with an invalid callback level, and HasWater requires an observed, valid WaterVolume containing the center.

Until a real SetLiquidLevel callback arrives, center buoyancy receives the current mathematical ocean height. The fallback does not call SetLiquidLevel and cannot mark itself as a live observation. Loss of the observed volume also returns to the mathematical path. The native four-force formula, Dampen, ownership policy and wind sampler are unchanged.

## 2. Agreement check

The reviewed paths still implement the following decisions. This table records source-level checks, not gameplay acceptance.

| Agreement | Source-level result |
| --- | --- |
| Placement by a client or host with normally loaded geometry | Preserved; a dedicated server does not independently place floes. |
| Owner-zero or local zone controller allowed, foreign owner excluded | Preserved through AllowedControl; placement does not claim ownership. |
| No temporary terrain SpawnZone | Preserved. Full/Ghost only describes floe network initialization. |
| Per-floe watermark immediately; zone marker after the candidate pass | Preserved, including zero-candidate completion. Interrupted work is not persistently completed. |
| Accepted residual two-client race | Unchanged. No reservation or locking protocol was added. |
| Mixed coastline and original candidate restrictions | Preserved; restored pure-land prefilter is conservative. |
| Server-only global cleanup | Preserved through bounded maintenance/removal queues; no periodic client summer cleanup. |
| Shared non-MonoBehaviour floe state | Preserved. No new component, renderer copy, material copy or visual pivot. |
| Original Floating plus four additional forces | Preserved. Lazy local points retain the one-degree last-build yaw/wind thresholds. |
| Wind through EnvMan accessors | Preserved through GetWindDir/GetWindIntensity. One effective wind instead of two-wave transition blending remains the documented approximation. |
| Native wave mathematics, normalized depth 1 | Preserved, including mathematical center fallback. Local water geometry is not reconstructed. |
| Water distance from NearSimulationDistance times zone size | Preserved and refreshed through the existing Water settings callbacks. |
| Distant Dampen from actual wave displacement | Preserved; no unrelated sinusoid was reintroduced. Position/body-velocity publication stays isolated. |
| Two targeted snow optimizations | Positive-only HeatLink allocation and exact per-pass weather reuse remain as implemented. |
| Verified snow behavior and storage | Four buckets, custom roofs, saved zero, construction zero, owner-zero prediction, own snapshots and pooled cap materials remain. |
| Existing surrounding corrections | Wet Fireplace, self-heat default 2, ruleBiome, read-only formatted hover, texture retirement and frozen-ship corrections remain. |
| Summer snow idle path | Main simulation returns before scene/heat/readiness discovery, apart from outstanding winter cleanup. |
| End-user snow documentation | README contains the snow file/field descriptions inline; no new external snow-format guide was introduced. |

No further confirmed deviation requiring a code change was established in the inspected paths beyond the three findings fixed above. This is not a claim that every line or every multiplayer ordering has been proved correct.

The original audit did not promise detection of same-mesh edits preserving identity/bounds/count. Section 7 now explicitly treats all internal floe geometry as fixed after placement and removes runtime geometry polling entirely. Other design limits remain: normalized depth and the effective-wind transition are approximations; simultaneous clients can still race; the old reported disabled-collider cause has not been reproduced or established.

## 3. Original additional performance findings

At the original review, all items in this section were deferred. The descriptions below preserve that baseline; section 6 records the subsequently approved implementation of P1 and P3, and section 7 records P6 and the per-call portion of P7. P2, P4, P5, P8, P9 and cross-callback center-height reuse remain unimplemented. Priority indicates which measurements or small changes are likely to be most useful, not measured milliseconds or a promised FPS improvement.

### P1. Repeated material-array copies and repeated setup during prefab binding

Area: older seasonal recoloring, initial object loading.
Sources: `Controllers/PrefabVariantController.cs`, `PrefabVariant.Initialize`, `AddMaterialVariants`, `AddLODGroupMaterialVariants`, `MaterialVariants.GetMaterialVariants`.

`AddMaterialVariants` reads `renderer.sharedMaterials` in the loop condition and again for each indexed material. For n slots the normal loop evaluates this array-returning property 2n+1 times. Unity documents that it returns a copy of the material array. The material objects are not cloned by that getter, but the managed arrays are unnecessary repeated allocations.

Cache hits in GetMaterialVariants also construct `Tuple.Create(material, context)`. Renderer paths are split again for each prefab instance, and LOD matching repeats LINQ filtering and name/type checks. These are distinct from the new cap-material pool; they are in the general seasonal texture/color binding path and can occur while a large base is loading.

Suggested later direction: read the renderer's materials once or into a reusable list; use a nonallocating value key without losing the CachedMaterial context; pre-tokenize paths in the shared prefab description. Do not turn shared materials into per-instance material clones. Exact time/GC contribution needs a loading profile.

### P2. Lighting state is allocated and all color conversions run on ordinary environment application

Area: older lighting customization, steady-state gameplay.
Sources: `SeasonState/EnvManPatches.cs`, `EnvMan_SetEnv_LuminancePatch`; native `EnvMan.FixedUpdate`; `Utils/HSLColor.cs`.

With lighting control enabled, the prefix allocates a LightState class, saves colors and scalar values, performs fourteen RGBA/HSL/RGBA color conversions, modifies fog/light values, and restores the fields in postfix/finalizer. Conversions also run for luminance multipliers equal to one.

Native SetEnv is called from each effective EnvMan.FixedUpdate, not only when weather changes. The native render-frame guard limits the original body, but does not make this an event-only operation. HSLColor itself is a struct; the claim is one LightState allocation plus repeated arithmetic, not fourteen allocated color objects.

Suggested later direction: skip identity transformations and use a safe per-invocation value state where appropriate. Preserve exception restoration, nested-call safety and the GammaOfNightLights Harmony ordering. Caching by environment name alone is not valid because interpolated environments are mutable.

### P3. Released water properties are revisited and can be rewritten unchanged every frame

Area: older water presentation/restoration, steady-state after released overrides.
Source: `Controllers/ZoneSystemVariantController.cs`, `WaterState.RestoreProperties`, controller Update and `WaterVolume_UpdateMaterials_RestoreReleasedProperties`.

RestoreProperties obtains the property block before determining whether inactive overrides need work. Active entries are skipped only inside the loop. A property with no pre-existing override is intentionally retained after restoration so it can follow changing material defaults. However, for such an inactive entry the code writes the current material default and sets changed=true even if the value did not change. This can lead to SetPropertyBlock on every call.

The large water plane is serviced from controller Update; registered WaterVolumes are serviced from the UpdateMaterials postfix. Thus the cost scales with loaded water surfaces. The mechanism protects compatibility, but constant rewriting is not required for an unchanged default.

Suggested later direction: a cheap no-retired-work exit, a narrow inactive set, and comparison before writing. Keep foreign properties and changes to shared-material defaults; do not clear the entire block or silently discard restoration ownership.

### P4. Mass recoloring still has synchronous passes, with per-vertex invariant work

Area: older terrain and prefab recoloring, season/shield/configuration boundaries.
Sources: `Controllers/PrefabVariantController.cs`, `UpdatePrefabColorsFromList`, `UpdatePrefabColorsAroundPositionDelayed`, `MaterialVariants.ApplySharedMaterial`; `Controllers/ZoneSystemVariantController.cs`, `UpdateTerrainColorsFromList`, `UpdateTerrainColor`.

Prefab color refresh iterates the selected registry synchronously. The delayed coroutine waits and then performs the entire pass; a delay is not time slicing. Around-position filtering traverses the full registry, and ApplySharedMaterial writes the slot without first checking for an already identical material.

Terrain refresh similarly processes all selected Heightmaps synchronously. Inside each vertex loop, IsProtectedHeightmap performs a List.Contains lookup even though membership is invariant for that Heightmap during the pass. Repeated transform reads also occur inside the loop. Position-dependent shield decisions still need their own calculation and must not be collapsed to one value for an entire map.

Suggested later direction: hoist map-constant checks, avoid unchanged material application, coalesce overlapping regional requests and distribute actual applications rather than only delaying their start. Preserve shield boundaries and original-color restoration. These source paths are candidates for residual season-transition spikes; they are not a measured breakdown of the earlier UpdateTriggers maximum.

### P5. Winter minimap derivation is prepared even when seasonal minimap control is disabled

Area: map initialization/reload, not per-piece steady state.
Source: `Controllers/MinimapVariantController.cs`, Start, OnMapDataReady, GenerateWinterWorldMap and UpdateColors.

The controller is created under UseTextureControllers. Map readiness captures pixels and starts winter-map generation without checking controlMinimap. That generation clones the complete pixel array and performs biome lookup for every map pixel. The disabled-feature check is reached later in UpdateColors. Capturing native data can be required for Marketplace compatibility, but that is a different task from deriving the winter map.

The coroutine yields every eight rows rather than according to elapsed time or a bounded number of pixels. Larger map widths therefore increase the work between yields; the whole-array clone is also indivisible.

Suggested later direction: retain needed native/Marketplace capture, but lazily generate the winter derivative when actually needed and handle enabling the setting later. Budget biome work explicitly. Do not move mutable WorldGenerator/compatibility calls onto a worker thread without a separate thread-safety design.

### P6. Four-point caching still performs substantial geometry validation every physical tick

Area: new floe wave path, many nearby owned floes.
Source: `Controllers/SeasonalIceFloeWaves.cs`, EnsurePoints and ReadGeometry.

A valid cache avoids ClosestPoint calls, but each validation still traverses the cached transform path, reads parent/local transforms, composes TRS matrices, reads lossyScale/centerOfMass and collider or mesh metadata, and compares geometry. Wind heading is calculated per floe although the wind direction was already sampled globally.

This is not a managed-allocation finding: the normal valid-cache path reuses its collections. The opportunity is reducing repeated native property access and common arithmetic. No measurement establishes that this costs more than the removed closest-point calls.

Original suggestion: compute common wind heading once, distinguish immutable prefab shape data from per-instance changes, and narrow geometry checks while preserving scale, reparenting and collider replacement invalidation. The maintainer subsequently rejected runtime geometry monitoring altogether; the simpler accepted implementation is recorded in section 7. The one-degree heading thresholds remain.

### P7. Water context and fallback center sampling repeat across the same floe's callbacks

Area: new floe wave path, especially without a loaded WaterVolume.
Source: `Controllers/SeasonalIceFloeWaves.cs`, BeforeFloating, BeforeSync, EnsureCenterWater and TrySurface.

The global Snapshot shares wind/time, but TrySurface repeats the containing-water test and surface-policy lookup for each of the four support points. The center fallback can also be sampled from both sync callbacks and the physical update. These paths reuse the same wave mathematics, but not the same floe-local surface context.

Suggested later direction: reuse stable per-floe inputs within a well-defined cycle, while still evaluating height at each actual point. Do not cache water height solely by render frame: several fixed ticks or a pose/ownership change can occur within it. Preserve the acquisition safeguards before accepting or publishing a saved pose.

### P8. Distant target budget does not bound all distant work

Area: new distant wave presentation.
Source: `Controllers/SeasonalIceFloeWaves.cs`, Track, UpdateBobs, Bob and ApplyBob.

bobOrder contains every tracked floe, including nearby floes that immediately return from Bob. These still consume the sixteen-visit quota. Target freshness therefore depends on total registered population. As an arithmetic example, 1,000 entries at 60 frames per second give about 1.04 seconds for a complete sixteen-per-frame rotation. This is not a benchmark.

ApplyBob interpolates each distant floe through sync callbacks, at most once per render frame. It recomputes the same exponential smoothing coefficient and can assign Body.position when effectively unchanged. The far-clip check bounds target sampling, not this interpolation path. The sixteen-visit number must not be described as a bound on all Unity work for all floes.

Suggested later direction: use the existing distant membership to avoid wasting target visits, share the smoothing factor, and avoid unchanged assignments. Keep current authoritative baselines and no cosmetic ZDO writes. Do not introduce another general animation manager without a measured need.

### P9. External loading notifications can repeatedly restart pending placement inspection

Area: new placement service, streamed or densely populated Ocean/coastal zones.
Source: `Controllers/SeasonalIceFloes.cs`, InvalidatePendingSector, RequestZone and RestartInspection.

The self-created-floe loop is fixed in section 1.2. External AddToSector notifications still reset the matching pending cursor and set NextAttempt to zero, including objects whose prefab/type has not yet been deserialized. Repeated loading can therefore repeat already inspected prefixes and bypass the usual readiness retry interval. Work per service is bounded, but total work and completion latency can increase.

Suggested later direction: coalesce notifications for an already dirty pending pass or separate unknown-arrival tracking from expensive readiness retries. Preserve detection of late static placement inputs and existing floes. Filtering on an uninitialized prefab or silently ignoring all dynamic-looking arrivals is unsafe. This item is deferred because correctness of streaming remains more important than speculative removal of a guard.

## 4. Review boundaries and verification

The source review covered the changed floe files and related native ZNetView/ZDO/Floating/WaterVolume paths; the current snow simulation, patches, materials, heat and live-weather paths; general prefab recoloring; environment lighting and texture retirement; water/terrain presentation; minimap derivation; summer-heat controller/visual entry points; and the texture-cache startup controller. The relevant project include list and current README snow section were inspected. This was targeted call-path review, not a claim that all source lines received a new exhaustive proof.

The source reference for game behavior is `shudnal/assemblies_combined` at `d1374bfd9175ac8f733ae483b0a06e5c8b75906e` (Valheim 1.0.15). Unity's Renderer.sharedMaterials documentation confirms the array-copy behavior; HSLColor was checked in this repository and is a struct.

Both source commits were read back through GitHub and their exact diffs inspected. Only the two floe source files were edited. No new Harmony target was introduced by these fixes; the existing AddToSector and SetLiquidLevel boundaries were checked against the game source. Changed source text is English. Project includes, configurations, defaults, versions, dependencies, snow storage, packaging and the other branch refs were not edited.

Writes were made through GitHub's contents API against the fetched blob SHAs. The maintainer's local worktree was not inspected or modified. No build, automated test, Valheim run, profiler capture, GC/FPS/network measurement or full Roslyn pass over all 80 source files was performed in this audit. No Codex review was requested and no PR was opened, merged or closed.

## 5. Maintainer verification

For the fixes made here, prioritize:

- Winter entry or approach to a dense pure-land base: no sector discovery/candidate placement for a confirmed non-Ocean Heightmap; no new zone marker merely for the local prefilter.
- Mixed Ocean coastline: candidate placement is still permitted despite a land center.
- Several floes from one placement pass: own created ZDOs do not reset static inspection; unexpected same-zone additions still do.
- Full and Ghost creation, an interrupted candidate, zone unload and ordinary external object loading between slices: RNG/ghost state is restored and no premature completed marker appears.
- Disable/re-enable a floe while no WaterVolume is available: the center follows current mathematical ocean height rather than retaining the preceding fallback value. Then restore local water and verify callback takeover.
- Previously accepted owner-zero/local/foreign-owner, distant Dampen, climb and server cleanup scenarios.

The reporting user's large-base and ocean profiles remain necessary. Separate initial binding from steady state, pure land from Ocean/coastal work, and one-time season/configuration transitions from ordinary gameplay. The remaining unimplemented candidates in section 3 should be chosen for later work from those observations, not treated as already fixed or as proven causes of the previous user freezes.

## 6. Approved material-binding and water-property follow-up

Baseline: `53b0f48352f8deda2af4260f6b627ad3ad2c093a`. Corrected code: `f9e79ea1135ee94eadbf96f24d9eb5462f3818b7`.

### 6.1 Material binding: implemented

Commit: `bd4da227bde64fa450a7cda7d76f6a3b52b6c2da`.

`AddMaterialVariants` reads shared materials once into a reusable list, separate from the material-application scratch list, and clears retained references in `finally`. It caches each slot's material/shader names for matching. It does not instantiate per-object materials or change variant selection.

The material-pool key is a value tuple retaining both the material and CachedMaterial context, so successful lookups no longer allocate a Tuple object. Relative path text is cached by source path and prefab name; immutable split segments are shared by path. These caches contain no instance transforms and are cleared on reinitialization and world teardown. LOD matching uses the same ordered comparisons without a Where closure/iterator, with renderer name/type read once per renderer.

The existing matching order, duplicate-name hierarchy traversal, texture/color rules, original-material restoration and mass-recolor scheduling are unchanged. This is P1 only, not implementation of P4.

### 6.2 Water properties: implemented

Commit: `f9e79ea1135ee94eadbf96f24d9eb5462f3818b7`.

WaterState now has a `m_propertiesChanged` flag. Setters and restoration set it only for an actual property-block change; ApplyProperties returns immediately otherwise and resets the flag after a write. An inactive-property count is maintained during acquire/release so ordinary maintenance returns before reading the renderer when there are no released overrides.

When released neutral overrides remain because MPB cannot remove one property, only their material defaults are compared first. Unchanged defaults return before GetPropertyBlock, ownership reconciliation, and SetPropertyBlock. Real default changes still obtain a fresh block and recheck whether the property remains ours before writing. A different foreign override is preserved, and unrelated block fields are never cleared. Explicit season/config restoration still reads a fresh block, including when no property had previously been owned, because it also prepares the next edit.

This does not claim zero material reads after thaw: those narrow comparisons intentionally preserve response to changed material defaults. It eliminates unchanged block copies/writes without global hooks for all material setters. Native water-time updates, frozen-water physics and existing restoration entry points remain unchanged.

### 6.3 Deferred scope and verification

Lighting state/conversion work (P2) is explicitly not changed. P4-P9, floe physics/placement, snow arithmetic/storage, environment-texture retirement, configurations, versions and packaging are also untouched in this follow-up.

The two published source diffs were read back and inspected. Native WaterVolume SetupMaterial/UpdateMaterials were checked from assemblies_combined at the revision listed above; no new Harmony target or project file is required. Added code and this documentation are English. No build, tests, Valheim, full-project syntax pass, FPS/GC/network measurements, PR action or Codex review was run.

Owner-run checks: load a large base and compare initial binding; inspect multi-material/LOD objects and duplicate-name hierarchies; reload texture rules and switch worlds; run freeze/thaw and partial-freeze transitions; verify original water overrides survive and released values follow a real material-default change without clobbering unrelated property-block values. Confirm ordinary lighting remains visually unchanged.

## 7. Fixed floe geometry and per-call surface inputs

Baseline: `cff3e1837676fec64414b39fb7af07a5fcfdf4a1`.
Code commit: `eaa135b6c0c861d2d511b847356f8b762c763017`.
File: `Controllers/SeasonalIceFloeWaves.cs`.

### 7.1 Heading-only support cache: implemented

The maintainer confirmed that the floe's internal structure and shape do not change during its lifetime. This supersedes the earlier requirements in this review and the implementation specification to poll scale, collider replacement, hierarchy changes, center of mass, or mesh metadata.

`ReadGeometry`, `Geometry`, the cached collider-parent path and the redundant collider reference have been removed. There is no geometry snapshot, TRS composition, shape comparison or vertex/bounds inspection in the support-cache path.

Each floe lazily builds four collider-local points through the original four `ClosestPoint` queries after initial placement. Once built, the only refresh triggers are a horizontal floe-heading change or a horizontal wind-heading change of at least one degree relative to the last successful build. `DeltaAngle` preserves wraparound and accumulation of small turns. The wind heading is calculated once with the existing common wind/time snapshot, not for every floe. Wind intensity still updates the wave calculation without invalidating support points.

The normal path reuses the four points with `TransformPoint` and the original force formula. Translation and rocking move the points with the body; they do not trigger geometry reconstruction. Basic unusable-input/component guards remain, but do not act as shape-change monitoring. An undefined heading keeps an existing cache rather than causing continuous rebuilds. Disable/unload removes the floe record through the existing lifecycle; a new record starts uninitialized.

### 7.2 Per-call surface context: implemented

A small value-type `SurfaceContext` is prepared once for the synchronous four-force pass. It contains the world water level, surface offset, wave-enable flag and whether world-edge correction applies. Selection of observed local water versus prefab fallback and the center-containment check are no longer repeated for all four probes.

Every probe still evaluates its own current position, native wave function, Deep North fading, world-edge distance, depth delta and force. No wave height is cached by frame or position, and there is no new persistent water cache. The existing shared EnvMan accessor/time snapshot and normalized depth approximation are unchanged.

Center buoyancy, recovery, distant sampling and synchronization calls continue to request fresh surface inputs independently. A previously calculated center height is not reused between `BeforeSync` and `BeforeFloating`. Missing-water observation handling, gravity/ownership safety, four-force coefficients and `Dampen` remain unchanged.

### 7.3 Scope and verification

Only this source file and this review document changed in the follow-up. Placement, cleanup, distant scheduling, lighting, mass recoloring, minimap generation, snow logic/storage, configurations and packaging are untouched. The existing general shield-direction fix in the baseline is preserved.

The published source diff was read back and checked for the removed geometry code, updated call sites, unchanged force/wave arithmetic and absence of unrelated edits. Added source and documentation are English. No new Harmony target or project include is needed. No build, tests, Valheim, full-project syntax pass or performance measurement was run.

Owner-run checks: initial loading of differently scaled floes; fixed-direction rocking without repeated ClosestPoint; slow yaw/wind changes reaching one degree and wraparound; intensity-only weather changes; disable/re-enable with missing water; transitions between observed and mathematical center water; unchanged nearby forces and distant Dampen. Arbitrary in-place geometry modification is deliberately outside this cache's contract.
