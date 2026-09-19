# Snow performance

Status: implementation plan only. No runtime implementation, version bump, build, or game execution is part of this commit.

Branch: `perf/snow-performance`.

Baseline: `shudnal/Seasons` master at `988e98c514ce49369a92cbaf934a7aca384fe6e1`.
Game-source reference: `shudnal/assemblies_combined` at `d1374bfd9175ac8f733ae483b0a06e5c8b75906e`.

## 1. Purpose and evidence

A player reported a fall from approximately 40 FPS to 5 FPS when winter caps became visible, with recovery when caps were disabled. This establishes a useful reproduction scenario, not a measured CPU/GPU attribution. The implementation must reduce avoidable snow work without quietly redesigning snow mechanics.

The supplied prefab scan inspected 841 prefabs with `WearNTear`: 288 non-null snow renderer fields, 288 material slots, and five distinct material objects. All reported slots use `Valheim/Snow Mesh`: `snow_flat`, `snow_flat_low`, `snow`, `snow_rooftop`, and `snow_flat_low2`. The `snow_flat` example reports instancing enabled and a null `mainTexture`. It does not measure native material memory, enumerate every shader property, or prove that every future modded cap has one slot. The log counts renderer fields; do not reinterpret them as a count of unique scene objects.

Evidence fingerprints, SHA-256:

- Prefab/material scan: `d492a15da42a5c39582daa1185f1ae2364099083ab07806fcf99e077f437ea99`.
- Example material dump: `51ec4e38bad66dc0ca8411d26d35bf7c6e168854e7df9cf3adfcb015565d9e77`.

The branch baseline already contains `SeasonSettings/SeasonSnow.cs`, `WinterSnow`, the common JSON configuration path, and the trader fix. It does **not** contain the experimental `SeasonalSnowCapVisuals` material wrappers or snow-related `PrefabVariantController` changes from the later chat archive. Do not import that archive wholesale. The normalized Git blob hashes of the corresponding files in the supplied refactor archive match this baseline.

Primary source locations are listed in section 15. Statements about current behavior below refer to this pinned baseline, not to an earlier design sketch.

## 2. Agreed constraints

| Area | Contract |
| --- | --- |
| Persistence | Store seasonal buildup in Seasons-owned ZDO data, not `ZDOVars.s_snow`. The vanilla value must not retain Seasons snow after the mod is removed. |
| Rendering | Modify only the cap renderers referenced by `m_snow`, `m_snowWorn`, and `m_snowBroken`. No seasonal texture/material wrapper integration. |
| Material pools | One `Material[101]` per actual source material. Index 0 is the original material; indices 1..100 are lazy, immutable clones for `_SnowLevel = index / 100f`. |
| Storage | An ordinary `SeasonalSnowController` singleton owns runtime states, queues, links, and pools. No new `MonoBehaviour` per piece or heat source. |
| Scheduling | At most 20 piece-processing operations per frame across all snow work, not 20 per subsystem. No periodic snow simulation in `WearNTear.UpdateWear`. |
| Heat | Cache stationary sources and their nearby pieces. Contributions from distinct logical sources add. Dynamic heat sources are excluded. |
| Source activity | Cache `Fireplace`/`Smelter` references. Use their inexpensive `IsBurning()`/`IsActive()` predicates, with bounded periodic observation or their existing periodic update callbacks. |
| Cover | Keep the existing Seasons snow-cover test. Invalidate it on relevant geometry/sector changes; an observed `m_haveRoof` change is an additional signal, not a replacement test. |
| Migration | No startup/background sweep of every world ZDO. Reconcile activated pieces and the already-loaded set during season changes. |
| Configuration | Keep `SeasonSnow` in `SeasonSettings` and use the existing common JSON/`CustomSyncedValue` mechanism. No new parser, validator, merge layer, or config migration. |
| Scope | Building caps and their buildup/heat simulation only. Creature/cape snow, ice, skating, cart behavior, terrain colors, traders, and Marketplace are not redesigned. |

All implementation classes for this work belong in `WinterSnow`, apart from the existing settings model and central ZDO key declarations. The existing plugin instance can call `SeasonalSnowController.Instance.Update()`; `PrefabVariantController` is not the frame driver.

## 3. Gameplay behavior that must survive

### 3.1 Eligibility and modes

Preserve the existing eligibility checks: active season state, the snow switch, environment control, supported caps or valid donors, and the existing ignored-position rules. These rules exclude interiors, Ashlands, Deep North, and the existing high-Mountain condition. Do not reduce them to merely "outside Deep North". Biome/position readiness must precede permanent classification. [S1], [S2]

Keep the meanings of `Seasonal`, `Reduced`, `Ignore`, and `Disabled`:

- `Seasonal` and `Reduced` use their existing configured ranges and all normal feature gates.
- `Ignore` releases Seasons' buildup, copied visuals, and transforms without disabling native snow.
- `Disabled` suppresses both native and seasonal snow, including pre-snow and heavy-snow state, even when seasonal buildup is switched off. This explicit rule also applies in Deep North while the plugin is loaded.

Ordinary Deep North snow remains vanilla. Removing Seasons must remove its added seasonal snow, not remove naturally occurring Deep North snow. The explicit `Disabled` rule is a deliberate exception to leaving native state alone.

Retain the hard seasonal maximum of `0.99`, regardless of the visual pool having an index 100. Current bound values are ordinary `0.51..0.99` and reduced `0.3..0.6`; the internal fallback minimum `0.4` is not the bound default. Do not change these values during optimization. [S1], [S2]

### 3.2 Initialization, minimum, and seasonal transitions

Preserve these cases rather than replacing them with a generic continuous-growth formula:

| Situation | Required behavior |
| --- | --- |
| Existing exposed piece at winter initialization | Apply the current passive weather-history target, including the configured minimum, after area readiness. |
| Piece placed during winter | Record placement time and begin at zero; do not grant snow that fell before placement. |
| New or melted piece below the minimum | Keep its value while there is no subsequent eligible snowfall. On eligible snowfall, preserve the existing minimum-entry rule. |
| Roof or shield blocks a piece | Clear its seasonal snow and rebase accumulation. Do not merely stop growth while leaving the old cap visible. |
| Heat stops | Do not restore snowfall accumulated while heating was active. Resume from a rebased value. |
| Winter ends or seasonal snow is disabled | Clear seasonal state and hide seasonal caps; do not invent gradual spring melting. Processing may be spread over the shared budget. |
| Accumulation speed changes | Settle the preceding interval with the old speed before applying the new speed. |
| Maximum is lowered | Clamp to the new maximum, including values not aligned to a whole percent. Raising it does not instantly manufacture snow. |

Keep saved accumulation baselines, placement information, and the below-minimum history until an equivalent replacement is implemented. They cannot all be replaced by a last-update timestamp. [S1]

### 3.3 Heat is not an unapproved weather-balance redesign

Current code suppresses weather accumulation whenever the effective heat multiplier is positive. It does **not** compute `snowfall - heat` concurrently. Preserve this precedence. Additive heat changes how quickly heated pieces melt, not whether a sufficiently strong snowstorm defeats a fire. If a piece's roof multiplier is zero, its effective heat contribution is zero: it must not become permanently blocked from accumulating merely because heat areas are nearby. [S1]

Current interactive melting is another separate mechanism. Crafting-station use and attached/sitting objects have their own activity and speed, and suppress ordinary heat melting while active. Excluding moving heat sources does not authorize removing this feature. Preserve it with a small active-interaction set and the existing multiplier; do not sum multiple users into an additional unapproved melt bonus. [S1], [S2]

### 3.4 Explicit logic changes versus implementation changes

Accepted behavior changes are additive independent static heat sources, exclusion of moving heat sources, discrete visual updates, and bounded response latency from the 20-piece budget. Storage separation, material caching, and replacing polling are implementation changes, not permission to alter the rules above.

Frame-independent rates require one explicit normalization decision because the old live path is frame-dependent. The conservative reference is the existing prediction scale: `PredictedWearUpdateDelta = 0.01` per one-second wear interval. Weather gain per world second remains `environmentSnow * 0.01 * Game.m_snowBuildupSpeed * accumulationMultiplier`. Static heat uses `0.18 * 0.01 = 0.0018` buildup units per active simulation second before its existing multipliers. Interactive melting already uses elapsed time and remains `0.002 * interactiveMultiplier` per active simulation second. [S1]

This preserves the model's existing prediction reference, **not** every historical FPS-dependent live rate. Record this as a visible normalization and compare representative melt times during manual acceptance. Do not silently use `0.18` per elapsed second; that would be 100 times this reference. Any different balance target requires an explicit plan amendment before implementation.

## 4. State, precision, and persistence

### 4.1 Separate simulation, publication, and appearance

Use three distinct values: the precise simulated buildup, the last published snapshot, and the last applied visual level/material index. Do not round the simulation back to its published or rendered value after each operation.

**Refinement of the earlier sketch:** keep the Seasons-owned buildup snapshot as a `float`, rather than making an integer percentage the authoritative save value. A whole-percent save loses fractional limits, accumulates bias across ownership transfers, and needlessly restricts which visual pool entries can be selected. Throttling writes, rather than changing the numeric type, provides the required reduction in network updates. ZDO supports float keys directly. [G3]

Declare a new unambiguous key such as `Seasons_SeasonalSnowValue`. Retain/reuse compatible existing baseline/from keys. Store a winter epoch identifier, not only a boolean winter flag, so an unloaded piece can distinguish two different winters without requiring a whole-world cleanup pass. Use `GetFloat(key, out value)` or an equivalent presence check: zero is a valid saved value, not "uninitialized".

Normal level publication happens only after a change of at least `0.01` from the last published level. A snapshot contains a coherent value and its time/baseline context. Do not dirty a timestamp every frame or every due check with an unchanged value. Bounds, a transition through the visibility threshold, a change of integration baseline, deactivation, and terminal zero may require a final exact snapshot even below that normal step.

### 4.2 Vanilla-data separation and transition order

Seasonal simulation must not write its positive value into `m_snowBuildup`, `ZDOVars.s_snow`, or native `RPC_SetSnow`. Native support/damage/UI code must not mistake seasonal buildup for heavy snow.

For a proven legacy Seasons piece outside Deep North, migration is idempotent:

1. Identify old ownership using the existing `Seasons_SeasonalSnow` watermark or the other existing Seasons snow metadata. Do not seize arbitrary unmarked snow from another mod.
2. If the new value is absent, copy the old positive native buildup and the useful baseline/history into Seasons-owned state.
3. Clear the native stored value even when the new key already exists, so interruption between steps is recoverable.
4. Preserve or clear legacy fields according to a documented per-field mapping; do not erase placement/below-minimum information before it has been consumed.

Only the current object owner persists these changes. A non-owner can hide/preview locally and wait; it must not claim ownership for migration. Native `s_preSnow` is cleared only when it belongs to our controlled/disabled state, not indiscriminately across the world.

Perform migration at activation, ownership acquisition, and the budgeted loaded-piece season pass. There is no extra scan of `ZDOMan.m_objectsByID` on startup. Previously written vanilla snow in a never-reactivated old ZDO can still survive immediate removal of the mod; the chosen migration scope intentionally does not promise global cleanup of unseen legacy data.

### 4.3 Disable and teardown are different operations

Feature disable means clearing seasonal data and hiding/restoring visuals. Leaving a world means preserving the last authoritative seasonal data, releasing all scene references, and destroying only owned material clones. Do not clear a world's snow merely because its singleton is reset for disconnect.

The persistence guarantee applies after the new version has processed a piece and the changed world data has been saved. It is not a guarantee for an unsaved crash, an untouched legacy area, a different mod's snow, or native Deep North snow. No shutdown-only cleanup is allowed as the mechanism that makes removal safe.

## 5. Simulation and time

Keep the existing per-biome deterministic weather selection and cumulative weather timeline. Local `EnvMan.GetSnowBuildup()` at the observing player is not a replacement for weather at all pieces or for a headless owner. Preserve the existing environment lists, their order/weights, and Unity random-state restoration. [S1]

Use world time for weather history and the existing seasonal calendar. Use active, unpaused simulation elapsed time for live heat/interaction melting. Sleeping, a console time jump, offline time, or switching realtime-season settings must not be interpreted as many hours of continuous heat from the currently burning fireplace.

Preserve existing catch-up behavior: on reactivation or time skip, check readiness and current cover/heat, then reconcile the weather target. Current heat can block/rebase that catch-up; it is not evidence that the fire was burning throughout the unloaded interval. Do not reconstruct fuel history that is not stored. [S1]

Process condition transitions in time order. A delayed piece must settle the old rate to the event time and the new rate afterward. Two fire toggles or two weather changes before its queue turn cannot be collapsed to "the latest rate was active for the whole interval". Keep a shared transition history/cursor for pending work and prune it after consumers catch up. Coalesce only intervals that are mathematically equivalent under the same clamping and eligibility rules. Bound the amount of history processed per operation; do not silently discard transitions when backlogged or promise a fixed memory bound under an unlimited stream of external changes. Do not create an ever-growing per-piece event log.

Clamping is order-dependent: growth to the maximum followed by melting is not equivalent to summing net gains and clamping once. Use the existing cumulative weather representation for monotone segments, and split only at relevant condition changes. Long catch-up work must be resumable rather than looping through an unbounded timeline inside one piece operation.

On clock reversal, reset deadlines/rebase from the accepted snapshot; never integrate a negative duration. On entering another winter, use the new epoch. Do not let a piece stalled at its maximum prevent old transient histories from being retired.

## 6. Singleton and scheduler

`SeasonalSnowController` owns a world-generation token, stable handles for piece/source states, indexed work queues, a deadline heap, sector/cell tables, and material pools. Existing small helper classes may be used; they are not Unity components. Keep configuration reading and the settings DTO outside it.

Registration caches the `WearNTear`, `ZNetView`, ZDO identity, prefab hash, position, biome, rule, collider-derived metadata, three renderer bindings, their original materials/transforms, and queue indices. Collect components once at registration or a known hierarchy change, not every time the state is processed.

Use stable handles plus generations. `ZDO.Reset()` changes its identity, and scene objects can be destroyed while queued. A dictionary keyed by mutable ZDO identity without lifecycle removal is unsafe. Check both the recorded ID/generation and current object reference; save clones must never enter scene-state processing. [G3]

### Work and fairness

All normal piece work shares the same maximum of 20 operations per frame: initialization, environmental invalidation, simulation, migration, visual updates, and configuration refresh. Combine reasons into one pending state. Process a piece at most once in the frame unless it is a mandatory lifecycle cleanup.

A deadline heap contains only pieces capable of changing. Zero rate, full accumulation, zero snow during continued heat, or a blocked surface do not require recurring piece ticks. Still observe external source/weather/network events that can wake them later. When a heated zero-snow piece is sleeping, remember that heat still suppresses accumulation.

Do not hide unlimited work before or after this budget. Advancing a bulk cursor, promoting due heap entries, expanding source links, discarding stale handles, building timelines, and creating a new pool variant all need bounded/resumable work. A callback must not iterate 10,000 links merely to enqueue 20 processed pieces. Queue source/sector/bulk jobs with cursors; do not allocate `GetAllInstances().ToArray()` for every event.

Use fair progression between immediate reasons, overdue deadlines, and bulk jobs. Repeated heat toggles must not starve initialization or retirement. Duplicate invalidations update an existing job/revision rather than restarting an entire scan. A single expensive geometry job may span frames while keeping its piece marked dirty.

Deadlines are based on the next meaningful publication/visual/boundary event, not on visiting every intermediate percentage. If processing is late, advance to the current correct state and apply it once. At an exact falling threshold, schedule a strictly future event; rounding must not create an immediate requeue loop.

The 20-operation ceiling limits work count, not milliseconds. Keep cheap counters and an optional elapsed-time guard capable only of ending a frame early; never increase the 20-piece ceiling automatically. A theoretical single pass over 10,000 pieces takes at least 8.3 seconds at 60 FPS, 12.5 seconds at 40 FPS, and 100 seconds at 5 FPS, before extra work. Do not promise instantaneous global cap changes with this budget. Prioritize user-visible changes without starving the rest.

Mandatory destruction cleanup may immediately hide at most the object's own three caps and remove state; no mass recalculation belongs in that callback. During world shutdown, release references/materials in safe order instead of expecting a now-disabled frame driver to drain the normal queue.

## 7. Static heat registry and additive links

Register geometry using `EffectArea` lifecycle, including disabled/reenabled areas. Find and cache a logical `Fireplace` or `Smelter` owner once. A stationary `EffectArea` without either can be a logical source in its own right. A burning smelter is not automatically a heat source: preserve the requirement for a relevant heat area. Windmills and other `Smelter` implementations must not become heaters merely because `IsActive()` returns true.

Group multiple areas belonging to the same logical heater. For one heater/piece pair, combine its applicable areas using the strongest applicable contribution, not a sum. Then add the contributions of distinct heaters. This prevents low/high fire visuals or multiple child triggers from accidentally multiplying one fireplace.

Retain the existing center-distance falloff, the inside-heat-volume exception beyond the configured check distance, and self-heating eligibility even when its area center lies farther away. Preserve roof-over-leaky multiplier precedence and the stronger ordinary/self contribution within one logical source. The outside distance, inside-volume rule, source activation, and self relationship are different inputs, not interchangeable radius approximations. [S1]

Each static pair caches its geometric weight. Primitive collider membership may be evaluated analytically from cached sphere/box/capsule geometry; use cached AABBs only as a broad phase. Non-uniform scale and rotated boxes require their actual transform. For uncommon collider shapes, a narrow physics/closest-point check is acceptable during link construction, never as a periodic global heat search.

Activity observations use cached references and existing fireplace/smelter periodic updates where suitable. Observe `EffectArea.enabled`, active hierarchy, and collider enable state as well: an enabled area is not necessarily a burning source, and toggling a collider does not invoke `EffectArea.OnDisable`. Generic areas without an owner need a staggered bounded activity check; this is small source work, not a scan of all pieces.

When activity changes, settle affected pieces to the transition time before changing their aggregate rate. Addition/removal must be idempotent across duplicate enable/disable/destroy callbacks. Clamp numerical residuals near zero and rebuild an aggregate from its compact link list when topology changes. Do not allow repeated subtraction to produce negative heat.

A source is stationary by supported ownership/hierarchy, not `GameObject.isStatic`. Normal Valheim pieces can have that Unity flag false. Exclude carried items, characters, ships, carts, moving-parent constructions, and mobile rigidbody sources. A rigidbody being kinematic or asleep is not proof of lifetime immobility. If a supported source is relocated, invalidate its old/new footprint and rebuild its links; do not keep old distances.

Keep interactive melting separate. It affects the actively used object, not the whole heat neighborhood. Reuse existing activity signals and multiplier; replace native snow-value RPCs with owner-targeted Seasons activity signaling if remote activity requires it. Send activity, not a client-chosen snow value. Bound refresh frequency, expire it, deduplicate users, and verify sender/target/use eligibility on the owner. Loss of ownership or disconnect must expire activity, not leave permanent heat.

## 8. Spatial indexing and snow-cover invalidation

Reuse game sector coordinates at the top level. `ZoneSystem.GetSectorIndex` still describes 64-by-64-meter cells; the new base save chunk groups 8-by-8 sectors. `ChunkIndex` is not a finer heat grid. Use local 8-meter subcells inside sectors, or equivalent compact keys, only for the snow registry. Never rewrite the game's sector tables. [G5]

Index loaded snow pieces and registered static source footprints, not all ZDOs. Geometry is three-dimensional even though the cell key is XZ: vertical distance still matters. Handle negative coordinates using the same floor/origin convention as the game. Do not merge unrelated out-of-range/interior positions into `SectorZero`.

Heat candidate coverage must include actual area bounds, configured center radius, and self-owned areas. A large modded trigger must not be indexed only at its center. Registering either side builds only neighboring links and is order-independent.

### Preserve the custom cover algorithm

Keep the current highest-valid-collider surface probe, its fallback origin, upward offset, radius, cast distance, layer mask, and self-hierarchy exclusion. In the baseline the custom cast does not discard `leaky` hits like vanilla `HaveRoof` does. A valid `m_roof` is an existing fast positive case, but `m_haveRoof == false` cannot prove the Seasons test is clear. Do not turn the test into a root-position cast, a center-radius overlap, or an inversion of vanilla `m_haveRoof`. [S1], [G1]

Maintain sector cover revisions and pending sector jobs. Initial coverage and revisions must be checked after `ZNetScene.IsAreaReady`; partial zone loading must not permanently establish "no roof". Coalesce activation/removal bursts, and rerun if the revision changes during a queued test.

Invalidation must account for geometry that does not have a snow cap: ordinary building pieces, doors/gates, trees/rocks that disappear, terrain changes, and collider/state changes. Registration of snow states alone is not a complete geometry notification system. Use established load/unload and placement/removal paths, targeted destructible/door/terrain callbacks, and changed health-visual geometry. `m_haveRoof` prefix/postfix comparison is an additional cheap signal.

Invalidate the changed sector and neighboring sectors, expanded by the actual changed bounds where an object spans more than one sector. No wraparound arithmetic on packed sector IDs. A moved object invalidates both its old and new footprint. Compare cached state before invalidating: an unchanged periodic door/health callback must not dirty nine sectors continuously.

Recompute the cached surface-probe origin if the piece's geometry/transform changes. Reusing an origin derived from a now-disabled damaged collider is incorrect. Update bounds after physics has incorporated a structural change; avoid forcing `Physics.SyncTransforms` on every object.

No periodic recast of all static caps. Truly unobservable arbitrary collider/transform edits by another mod are not automatically solvable by sector revisions. Provide one internal invalidation entry point for known adapters and retain this as an explicit compatibility limit; do not claim complete coverage of uninstrumented moving geometry. Dynamic heat exclusion does not automatically mean dynamic roofs are supported by static links.

## 9. Rendering contract

The common path binds one material per cap, as verified by the supplied scan. Pools are keyed by actual original `Material` identity, never name; two distinct modded materials may share a name. Five observed originals mean at most 500 clones for this data set, not a global hard limit of five materials.

Index 0 always contains the unmodified original. Lazy variants 1..100 copy its settings once and set `_SnowLevel` once. Never change a pooled variant afterward, clone a pooled clone, access `renderer.material`, or update unrelated piece renderers. Reuse each cap's pool binding without material/name lookup in the hot path. Destroy only owned indices 1..100, after bindings are restored.

Use precise buildup to compute visibility and the visual level independently:

- Native threshold remains `buildup > 0.25`.
- Visible level remains `clamp01((buildup - 0.25) / 0.75)`.
- Assign a material only for a changed visual level of at least `0.01`, a changed active cap/binding, or an explicit final/boundary update.
- For a visible cap whose quantized level is below 1%, use the first visible variant; never use original index 0 as a guaranteed zero-level visible material.
- When hidden, restore index 0 and disable the cap only if these states actually differ.

A change of a `FloorToInt` bucket does not by itself prove that the underlying value changed by 0.01 since last application. Keep that last-applied level if the minimum-change rule is to be literal. Terminal zero/max, the 0.25 visibility boundary, forced hide, and damaged-cap changes bypass the ordinary magnitude gate so a small remaining change cannot leave the wrong state forever.

Do not quantize raw buildup to integer percent before remapping it. The remap slope is 4/3: contrary to the earlier sketch, one whole raw percent can skip visual levels, not collapse into fewer visual changes. Simulation precision, network cadence, and material buckets must remain separate.

Select normal/worn/broken exactly like vanilla, including missing-variant fallback and duplicate renderer references. Observe actual health-visual changes, not only one RPC path: loading, repair, damage, and other callers can change the selected visual. A changed variant forces a material application even if the level is unchanged. Do not activate all variants under a disabled parent.

Keep conservative displacement bounds for all levels and configured transforms; change bounds on geometry/transform changes, not every percentage. Preserve LOD membership, layer, shadow settings, enabled state, and the known single-material fast path. Cache a safe slot-specific binding or report an unsupported renderer once for an unexpected modded layout; never replace all of its materials with slot 0.

### MaterialMan and other property overrides

There are no snow calls to `MaterialMan.SetValue`, `ResetValue`, or `RegisterRenderers`, including zero writes from `Disabled` and copy refresh paths. Native seasonal `UpdateSnowVisual` is intercepted before it can publish `_SnowLevel`. No global `MaterialMan` patch, all-renderer unregistration, or per-frame override fight is part of this design. [S3], [G1], [G2]

Unrelated vanilla highlighting/ash effects can still register a whole piece with MaterialMan. That work does not disappear simply because Seasons stops publishing snow. A pre-existing property block with `_SnowLevel` would override a shared material value; assigning `sharedMaterial` is not an override-removal mechanism. New registrations must never create that stale state. Prevent that state rather than relying on an unavailable generic "remove one MPB property" operation: enter managed rendering before the first native snow write, never publish a zero override when disabling, and keep ordinary Deep North rendering outside the pool. A legacy file loaded by a restarted process does not recreate an old in-memory MaterialMan container. If an in-process handoff already has an override, restore an exactly captured snow-owned block when safe; an unknown/foreign block is a compatibility case to report, not permission to erase unrelated properties. Seamless replacement of the old plugin assembly in a live process is not a supported upgrade path. Verify native-to-managed handoff explicitly before claiming the pooled material controls the visual. [G2], [U1]

Pooled materials can group identical meshes at identical levels, but do not guarantee instancing for different meshes, passes, lightmaps, or conflicting property blocks. Visible geometry and shadow draws remain every frame even when no material is reassigned. Measure actual rendering rather than promising an FPS result from the material count alone. [U2]

## 10. Network authority and waking idle states

Only the current object owner integrates and writes authoritative snow. A dedicated server still processes pieces it owns and does not create material pools. A client's graphics preferences must not change authoritative buildup, cap eligibility, or heater classification.

Non-owners display accepted snapshots. The existing read-only provisional initialization may remain while ownership/readiness is pending, but it must not become a second authoritative simulation or feed back to ZDO. Loading order, heat activity timing, and local geometry differ, so "all peers are deterministic" is not a sufficient synchronization contract.

`DataRevision` is not an event. An empty queue will never notice that revision by itself. Observe completed incoming `ZDO.Deserialize` for tracked piece IDs and the `SetOwnerInternal` ownership path, enqueue a cheap reason, then process after the packet has completed. Packet deserialization must not perform renderer work or reenter writes. Initial objects received before registration are picked up at activation. [G3], [G4]

A tracked object's unrelated fuel/health/inventory data may also change its revision; compare the snow snapshot/epoch before generating visual work. Observe only known IDs and never patch every `ZDO.GetFloat`/`Set` globally. Preserve the game's own rejection of stale network state.

On ownership gain, cancel old deadlines, read the accepted snapshot, migrate if required, wait for area/config readiness, and reconcile. On loss, stop writes immediately and discard unsent local authority. Do not flush after ownership has been lost. Repeated ownership churn and reconnect must not reapply growth or lose fractional accumulation.

Reject non-finite or invalid runtime snow/deadline values at the calculation boundary so an unexpected mod-provided value cannot poison the heap or cause an immediate-requeue loop. This is a small runtime safety guard, not a new JSON validation subsystem.

Each publication uses ordinary ZDO synchronization, without a per-percentage broadcast RPC or force-send. Coalesce updates if a piece advanced several thresholds while queued. Track bytes/publications during the stress case: limiting visuals alone does not bound network cost.

## 11. Lifecycle, configuration, and isolation

Register pending pieces only after their real identity is available. Placement previews and `ZNetView.m_forceDisableInit` objects may receive copied/transformed cap visuals where currently supported, but no world-snow migration, persistence, or authority state.

Resolve donors after the world prefab catalog is ready, preserving existing `ZoneSystem.Start` behavior. Copy only a missing primary `m_snow`, use first sibling placement, apply position/scale before capturing final visual bindings, and never overwrite another mod's existing cap. Configuration removal deletes only Seasons' copy. Donor chains remain unsupported. Partial axes always restore/read the original transform, not the previous override. [S3], [S4]

`Seasonal snow.json` remains a whole-document override through the existing common configuration pipeline. Do not rebuild `ReadLocalFile`, per-file watcher branches, custom validation, or partial merge logic. Build new rule metadata once and apply changes to instances through resumable jobs. Changes to heat distance, multipliers, bounds, source classification, or the JSON must invalidate the appropriate cached links/probes/deadlines; a cache is not correct if it only refreshes on object creation.

Existing creature/cape snow updates still receive their settings. No material pool is shared with those systems, and no change to their update cadence is included without a separate request.

Global disable/change-of-world stops simulation publication first, invalidates pending jobs, and restores/deactivates cap bindings before disposing pools. Destroyed Unity objects are never dereferenced during late jobs. Teardown must handle callbacks arriving after the singleton starts resetting. Reentry from vanilla `Awake` or another Harmony postfix cannot register a state twice.

## 12. Implementation stages

1. Pin baseline and preserve the gameplay matrix in section 3. Add diagnostic counters needed to compare the old and new paths; do not introduce a new public profiling framework.
2. Separate persistence and vanilla visual ownership, with activation-only legacy migration and a distinct winter epoch. Check disable/remove/reconnect paths before replacing the scheduler.
3. Add cap-only material pools and bindings. Keep `PrefabVariantController` unchanged. Confirm property-override handoff and damaged-cap selection.
4. Introduce the singleton's identity registry, spatial tables, shared 20-operation scheduler, and bounded bulk jobs. Remove redundant snow `UpdateWear` work only after the replacement event paths exist.
5. Add static source registration, logical-owner deduplication, source activity observation, additive cached links, and preserved interactive melting.
6. Replace periodic custom cover polling with geometry revisions and readiness-aware invalidation while preserving the actual cast algorithm.
7. Integrate weather/history, sleep/time skips, configuration reload, and authority/receive events. Remove obsolete state/RPC/MaterialMan paths and all-world snow scans.
8. Run maintainer-side functional and performance acceptance. Document only intentional player-visible differences; do not claim the original FPS report is fixed without a comparable measurement.

Likely file boundaries are the controller/state/scheduler, heat registry, cover invalidation, persistence adapter, and cap visual pool within `WinterSnow`. They may be small helpers or partials of the singleton; do not create one subsystem class just to wrap every dictionary. The current settings model, central keys, and existing lifecycle drivers stay in their established locations.

## 13. Required acceptance cases

| Case | Expected result |
| --- | --- |
| Quiet exposed base after stabilization | No scheduled piece simulation, heat scans, snow ZDO writes, material assignments, or repeated `SetActive`; only cheap driver/event checks remain. |
| 10,000 pieces become eligible together | No callback performs the whole scan eagerly; 20 piece operations maximum per frame; queue age decreases without starvation. |
| Fast weather changes while backlogged | Integrate chronological conditions, not the newest rate over the entire delayed interval. |
| One fireplace, two fireplaces, then one disabled/destroyed | One source is not double-counted; two independent contributions add; removal cannot make heat negative. |
| Heater with multiple child areas or low/high fire objects | One logical source uses its strongest applicable active area, not the sum of visual duplicates. |
| Smelter with no ore/fuel, blocked smoke, or failed roof requirement | Cached links remain, activity follows the native predicate and active heat area. |
| Zero-snow heated piece, then heater turns off | Remains asleep but blocked while hot; wakes on the activity change and does not restore blocked historical snowfall. |
| Roof multiplier 0; roof and leaky tags together | No effective heat melting; original roof precedence is preserved. |
| Heat inside a large area beyond center radius; non-uniform scale | Inside-volume exception works; cell indexing does not discard the pair. |
| Carried torch, ship heat, moving-parent heater | Does not contribute to static melting; no global search is reintroduced to support it. |
| Crafting/sitting use, two users, remote owner, disconnect | Existing local-object melt behavior survives; activity expires; no native snow-value RPC remains. |
| Newly placed piece in clear winter weather | Starts at zero and does not receive earlier snowfall; next eligible snowfall follows the existing minimum rule. |
| Cover added/removed across a sector edge; very wide blocker | Affected footprint and neighboring sectors invalidate; no full-world polling. |
| Door, damage-state collider change, tree/rock removal, terrain edit | The custom cover result refreshes even if vanilla `m_haveRoof` does not change. |
| Partial loading, portal arrival, zone unload/reload | No premature snow initialization; readiness revision is checked again. |
| Pause, sleep, positive/negative time jump, later winter | Weather catch-up and active-time melting follow section 5; no giant heat catch-up or stale epoch. |
| 0.25 visibility boundary; fractional min/max; terminal zero | Correct visibility and exact terminal snapshot despite the normal 0.01 update threshold. |
| Normal/worn/broken switch with unchanged level | Correct fallback cap receives the current pool material; duplicate references are handled once. |
| Copy donor/rule changes and repeated config reload | No double scaling, leaked clone, donor mutation, or stale renderer binding. |
| Ignore/Disabled/Seasonal toggles, including Deep North | Correct ownership handoff, no persistent zero MPB masking a later pooled material. |
| Headless owner and clients with different visual settings | Same authoritative buildup and source rules, no headless material allocation. |
| Incoming snapshot for a completely idle non-owner piece | Receive event wakes it without requiring a periodic scan. |
| Ownership changes mid-melt and before queued migration | Only current owner writes; no double catch-up, lost baseline, or ownership claim. |
| Migrated piece saved, mod removed, world reloaded | Added seasonal cap remains absent; native Deep North snow is unaffected. |
| Unprocessed old area, mod immediately removed | Documented migration limitation, not a claim of global cleanup. |
| Repeated world changes | Material clone count, states, links, and queued jobs return to baseline; originals survive. |
| Original failure scene, static view and snowfall separately | Compare frame time, CPU snow work, MaterialMan work, GPU/draw calls, allocations, and network writes under the same scene/settings. |

## 14. Readiness and remaining empirical limits

This document fixes the conservative behavior to preserve and the known intentional changes. No implementation is authorized by the planning commit itself.

Before calling the eventual change complete, verify three empirical boundaries on the maintainer's game setup: the single-heater rate normalization versus the old frame-dependent path; whether the failure scene is dominated by CPU updates or persistent geometry/shadow cost; and the actual snow/property-block handoff on copied, disabled, and native caps. These are acceptance gates, not reasons to silently add distance culling, alter shaders, or drop mechanics.

The no-work-in-idle target applies to the snow subsystem, not to Valheim rendering, native wear, the network receive loop, or inexpensive periodic checks of cached heaters. The plan does not promise zero draw calls or zero work anywhere in the game.

## 15. Source map

[S1]: https://github.com/shudnal/Seasons/blob/988e98c514ce49369a92cbaf934a7aca384fe6e1/WinterSnow/SeasonalSnow.cs
[S2]: https://github.com/shudnal/Seasons/blob/988e98c514ce49369a92cbaf934a7aca384fe6e1/Seasons.cs
[S3]: https://github.com/shudnal/Seasons/blob/988e98c514ce49369a92cbaf934a7aca384fe6e1/WinterSnow/SeasonalSnowMeshSettings.cs
[S4]: https://github.com/shudnal/Seasons/blob/988e98c514ce49369a92cbaf934a7aca384fe6e1/SeasonSettings/SeasonSnow.cs
[G1]: https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/WearNTear.cs
[G2]: https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/MaterialMan.cs
[G3]: https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/ZDO.cs
[G4]: https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/ZDOMan.cs
[G5]: https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/ZoneSystem.cs
[U1]: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Renderer.SetPropertyBlock.html
[U2]: https://docs.unity3d.com/2022.3/Documentation/Manual/GPUInstancing.html

- [S1] `SeasonalSnow`: initialization and history (`TryInitializeCurrentWinterSnow`, `GetSnowTarget`), heat/cover (`GetSeasonalSnowBuildup`, `GetMeltMultiplier`, `HasSnowRoof`), interaction, time-skip/season cleanup, and the existing wear/RPC patches.
- [S2] `Seasons`: bound configuration defaults, `IsIgnoredPosition`, common configuration/lifecycle integration.
- [S3] `SeasonalSnowMeshSettings`: donor resolution, transform restoration, Ignore/Disabled behavior, and current MaterialMan interactions.
- [S4] `SeasonSnow`: unchanged JSON model and one-line defaults.
- [G1] Native cap thresholds, wear/cover behavior, renderer initialization, and snow persistence.
- [G2] MaterialMan container registration and property-block assignment to child renderers.
- [G3] ZDO snapshots, mutable lifecycle, deserialization, value types, and revisions.
- [G4] Network acceptance and ownership-update context; verify the exact hook order against this source during implementation.
- [G5] Sector/chunk coordinate and readiness integration.
- [U1] Property-block override semantics.
- [U2] Instancing requirements; shared materials alone are not proof of draw-call reduction.
