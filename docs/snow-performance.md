# Snow performance

Status: planning only. No runtime code, version change, build, or game execution is included.

Branch: `perf/snow-performance`.
Baseline: `shudnal/Seasons` at `988e98c514ce49369a92cbaf934a7aca384fe6e1`.
Game reference: `shudnal/assemblies_combined` at `d1374bfd9175ac8f733ae483b0a06e5c8b75906e`.

This revision replaces the initial plan's conflicting rules. The latest maintainer decisions take precedence over preserving old gameplay: concurrent snowfall/heat, one-time discovery minimum, gradual covered-piece melting, a full 1.0 maximum, distance-independent ambient calculation, separate work budgets, and exclusive cap-renderer ownership are intentional changes. The source-oriented accumulation approximation and its consequences are recorded explicitly in section 4; it must not be mistaken for exact single-count snowfall.

## 1. Evidence and scope

A reported fall from approximately 40 FPS to 5 FPS when caps appeared motivates this work. It does not establish whether CPU updates, geometry, shadows, or another bottleneck dominates. The design target is to keep overhead small at 60 FPS and above, not to consume the entire frame budget of a struggling 40-FPS machine. No claim about typical Valheim FPS or measured execution time follows from this plan.

The supplied scan reported 841 `WearNTear` prefabs, 288 non-null snow renderer fields, 288 material slots, and five distinct materials: `snow_flat`, `snow_flat_low`, `snow`, `snow_rooftop`, and `snow_flat_low2`. All reported slots use `Valheim/Snow Mesh`. The example `snow_flat` dump has instancing enabled and null `mainTexture`; it does not measure native memory. Counts describe the supplied setup, not all possible modded prefabs.

The baseline contains `SeasonSettings/SeasonSnow.cs`, `WinterSnow`, common JSON loading, and the trader fix. It does not contain the experimental snow-material wrappers or snow-related `PrefabVariantController` edits from the later chat archive. Do not import that archive wholesale. Cap rendering is independent of seasonal texture recoloring. Creature/cape snow, ice, carts, traders, and Marketplace remain outside this change.

## 2. Fixed architecture

| Area | Decision |
| --- | --- |
| Runtime storage | One ordinary `SeasonalSnowController` singleton owns states, source links, active lists, work cursors, and material pools. No additional component per piece/source. |
| Persistence | Precise Seasons-owned float buildup plus snapshot/baseline time and winter epoch. Never store positive seasonal buildup in native `s_snow` or `m_snowBuildup`. |
| Materials | `Dictionary<Material, Material[101]>`, keyed by original identity. Index 0 is the original. Indices 1..100 are lazy immutable variants. Five observed originals imply at most 500 clones for this setup. |
| Ambient simulation | Iterate compact changing-state collections directly. No heap or queued percentage event per piece. Use cached numeric inputs, not scene/component queries. |
| Visual application | A deduplicated dirty list, initially up to 50 piece-visual applications per frame, using the latest level rather than replaying intermediate levels. |
| Expensive geometry | Independent resumable work, initially up to 20 piece inspections per frame and an elapsed-time guard. This is not a limit on arithmetic. |
| Network work | Separate coalesced publication work. No write/RPC on every arithmetic update. |
| Cover | Preserve the Seasons cover algorithm. Invalidate the center sector and its eight neighbors, without calculating each changed object's full bounds. |
| Heat | Cached stationary logical sources and nearby pieces; add different heaters, deduplicate multiple areas belonging to one heater. |
| Ownership | No owner/active-radius gate on ambient appearance and static-heat calculation in ready loaded areas. Retain narrower gates for live interactions and existing publication authority. |
| Integration | Existing plugin drives the singleton. `PrefabVariantController` is neither the driver nor part of snow rendering. |
| Configuration | Existing `SeasonSnow` model and common `CustomSyncedValue`/JSON path. No custom validation, partial overrides, or configuration migration. |

Helpers stay in `WinterSnow`; settings stay in `SeasonSettings` and ZDO hashes in the existing central declaration. Do not create a subsystem class merely to wrap a dictionary.

## 3. Gameplay state and initialization

### 3.1 Eligibility and modes

Keep the existing position/biome eligibility rules, environment-control gate, season readiness, donor support, and interior exclusions. Preserve ordinary Deep North snow as native. `Ignore` relinquishes Seasons management; `Disabled` continues to suppress native and seasonal snow, including in Deep North while the mod is installed. End-of-winter cleanup applies unconditionally to Seasons-managed seasonal caps, not to unrelated native Deep North snow. [S1], [S2], [S3]

Remove the artificial hard maximum of `0.99`: the new calculation permits `1.0`. The planned ordinary default becomes `0.51..1.0`; reduced defaults remain `0.3..0.6`. Existing explicitly stored config values remain user choices. Do not add a new damage/support patch to permit 1.0: isolate seasonal data from native heavy-snow inputs instead. Clear legacy heavy/pre-snow state only at the relevant handoff/cleanup. A native snow-routing boundary is still necessary; storage separation is not permission to let vanilla overwrite our visual. [G1]

### 3.2 Three different initialization paths

| Event | Initial value and subsequent behavior |
| --- | --- |
| First discovery of an eligible existing exposed piece for the current winter, without a valid snapshot | `min(maximum, configuredMinimum + timelineSnowSinceWinterStart)`. Apply once, then use normal deltas. Current heat does not replace this target with an invented zero; live melting handles it. |
| A piece is newly built during the current winter | Start at zero and record placement time. It receives neither the discovery minimum nor snowfall from before placement. |
| A known piece is reactivated | Restore its snapshot and account only for its unprocessed weather interval. Do not classify activation itself as first discovery and do not add the minimum again. |

The minimum is a discovery seed, never a floor for ongoing accumulation. Growth after construction, melting, heater shutdown, or roof removal passes continuously through values below the configured minimum. Delete the old minimum-entry jump and any catch-up formula that silently reinserts it.

A first-discovered piece already sheltered by the custom cover test starts clean, preserving the distinction between exposure and eligibility; a known snowy piece that acquires a roof follows gradual melting below. A shield remains a separate hard suppression rule. These two exclusions must not be conflated with ordinary heating. The first-discovery sheltered exception is retained explicitly rather than silently applying an exposed-piece seed indoors.

Retain placement identity, initialization epoch, and weather accounting position across unloads. A valid zero snapshot is not absence. An object built in an earlier season/winter is an existing object for a new winter; construction metadata must not suppress discovery seeding forever. Preliminary appearance before `IsAreaReady` remains read-only and cannot irrevocably seed or reject an object before geometry/placement state is known.

### 3.3 Live changes

| Condition | Behavior |
| --- | --- |
| Snowfall and heat coexist | Integrate signed accumulation minus melting. Do not retain the old rule that any positive heat disables snowfall. Section 4 defines the selected source-wise approximation. |
| Heat stops during snowfall | Continue from current buildup with positive weather delta. Never jump to the minimum or repay weather already processed during heating. |
| A roof appears | Stop new snowfall on the piece and gradually melt its existing snow using a new covered-piece rate. |
| A roof disappears | Stop covered-piece melting and resume exposed accumulation from the remaining value. No discovery reseed. |
| Shield appears | Preserve existing immediate hard suppression. Gradual roof melting does not change shields without a separate decision. |
| Winter ends / feature disabled / piece becomes Disabled | Force the desired state to zero and retire calculation immediately; drain high-priority visual hides independently of ordinary magnitude checks. No spring melting phase. |
| Maximum decreases / increases | Clamp an excess down; a larger maximum only permits future growth. It does not manufacture snow. |
| A parameter changes | Integrate to the effective boundary with the old inputs, then change cached rates and active membership. |

### 3.4 Covered-piece melting

Proposed new server-controlled config in `Season - Winter snow`:

`Snow melt speed multiplier - covered pieces = 2f`

Define 1x as the normalized unit melt rate, before distance/self/leaky/roof heat multipliers. The previous plan's provisional reference is `0.0018` buildup units per active second; at 2x the independent cover rate would be `0.0036` per second. This numeric anchor remains a balance proposal, not a measured equivalence to the old frame-dependent implementation.

A covered piece uses zero weather input and subtracts cover melting plus any applicable live heater/interaction melting. Cover melting does not require a fire and is not multiplied by the existing `roof pieces` heat multiplier, whose default is zero. It is also independent of the `all heat sources` multiplier: disabling fireplaces' contribution should not disable sheltered melting. Config zero stops this new melting but still blocks snowfall under a roof.

Cache `covered` and the resulting rate after geometry inspection. Each subsequent update is subtraction and clamping only. Remove a covered piece from the changing list when it reaches zero, until an external condition changes. No extra raycast, collider access, or source search is needed for gradual melting. The remaining cost is continued rendering while a visible cap melts; unlike instant removal, geometry/shadow work lasts until buildup crosses the visibility threshold. Do not promise that extra visible lifetime is free.

## 4. Source-wise arithmetic and repeated snowfall

The maintainer explicitly allows a simple source-oriented loop in which each active heater/piece link applies its own `snowfall - melting` contribution, even though the weather term repeats. Preserve that as an explicit selected approximation rather than silently substituting a different formula.

For one simulation interval, let `A_i` be genuine exposed weather gain for piece i, `H_si` the positive effective melt contribution of logical heater s, and `n_i` the count of contributing active logical heaters. Without the separate interaction path:

- No effective heater: `delta_i = A_i - coverMelt_i`.
- At least one effective heater: `delta_i = n_i * A_i - sum(H_si) - coverMelt_i`.

Covered pieces have `A_i = 0`. Disabled/inactive areas, duplicate areas of one logical heater, and zero-weight links do not increase `n_i`. A heater shared between biomes cannot supply one universal weather value: `A_i` uses the receiving piece's biome and cover state. Per-link distance/self/roof/leaky factors remain cached.

This is **not** identical to exact `A_i - sum(H_si)`. Its excess weather is `(n_i - 1) * A_i`. It may slow melting as desired, but it also means adding a weak heater with `H_si < A_i` can increase accumulation or reverse net melting. The assumption that every heater always exceeds snowfall is not guaranteed by existing configurable rates. Record this behavior honestly and include it in acceptance; do not fix it later as a performance refactor without changing this plan.

The exact alternative needs the same source graph and only avoids repeating `A_i`; it is not computationally more expensive. It remains the recommended alternative if weak-heater snow amplification is undesirable. There is no new user-facing formula toggle in this plan.

### 4.1 Avoid repeated mutable piece updates

Do not clamp the piece once per source: the result at zero/maximum would depend on collection order. Accumulate contributions for a common interval, then update/clamp each distinct piece once. Swapping source iteration order must not alter visible accumulation, apart from insignificant floating-point summation noise.

The simplest direct implementation can use a reusable touched-state list and a numeric accumulator, not an event queue: source loops add contributions, then one pass finalizes unique pieces. There are no allocations or Unity calls in these passes.

A cheaper equivalent implementation caches `combinedHeatRate` and `activeLogicalSourceCount` when source activity/topology/coefficients change. Each live step then iterates unique changing pieces and evaluates the chosen formula once. Static sources do not require recomputing their distances or traversing every unchanged link every frame. Keep source-to-piece links for localized updates and reverse membership; prefer this cached aggregate path when it simplifies the code.

### 4.2 Sleeping states

A piece can leave the arithmetic collection when:

- buildup is zero and the **combined** rate is non-positive;
- buildup is at its maximum and the combined rate is non-negative;
- the combined rate is zero.

`buildup == 0` alone is insufficient: after snowfall strengthens or a heater turns off, the rate may be positive. Do not remove the topology links when retiring arithmetic work. Source changes, weather changes, cover changes, config updates, incoming snapshots, and new interaction activity wake the affected states directly. At zero with a still-negative rate there are no periodic snow calculations or writes merely to confirm zero.

## 5. Time, background catch-up, and transitions

Use the existing per-biome weather timeline and cumulative gain, not the current environment at the local player's position for all pieces. Preserve deterministic environment selection, ordering/weights, random-state restoration, season calendar, and compatibility inputs. Cache per-biome weather gain once per step. [S1]

Live heat/cover/interaction melting uses elapsed unpaused simulation time, not the frame duration at an infrequent `UpdateWear` callback. Weather history uses the existing world/season time basis. Reference coefficients from the first plan were weather `environmentSnow * 0.01 * Game.m_snowBuildupSpeed * accumulationMultiplier`, static heat `0.18 * 0.01`, and interaction `0.002 * interactiveMultiplier`. They must be reviewed as explicit rates during implementation, not automatically multiplied by 60 or 100 to match a chosen FPS. This revision does not claim an empirical old-to-new speed match.

### 5.1 Returning to an unloaded or non-simulated area

Do not inspect historical fuel timestamps or reconstruct when fires stopped. At reactivation, restore the latest usable snapshot and add genuine timeline snowfall for the unprocessed interval, clipped to the applicable winter and not earlier than placement. Then apply the current heater states through ordinary live calculation. This guarantees the requested case: a nearly exhausted fire is out on return, and snowfall during the absence is added without a minimum jump.

The simple approximation uses genuine weather gain **once** for the gap, not `n_i` times based on the heater count observed on return. Do not replay historical heat, cover melting, or interaction melting. A currently lit fire does not cause hours/days of instantaneous melting; it melts the restored snow live. This can show temporarily more snow even beside a still-burning fire and is an accepted approximation that avoids historical fuel simulation.

Use current cached cover to avoid crediting unseen snowfall to currently sheltered pieces; keep stored snow and let it melt live. Geometry history during absence is not reconstructed. The stored last-accounted weather position must advance during live heating, even at a saturated zero, so reactivation cannot repay snowfall already cancelled in a **loaded** interval. Publish/rebase at transitions or controlled deactivation as needed; do not write timestamps every frame at zero.

A still-loaded ownerless piece that has been simulated locally has no gap merely because it lacks an owner. Preserve its runtime weather cursor and melt result. Likewise an incoming packet's snapshot time is not automatically the start of a new unprocessed interval. State handoff must distinguish actual skipped simulation from already integrated local time.

### 5.2 No delayed arithmetic event history

The earlier per-percentage heap and deferred condition-history architecture is unnecessary for the normal path. Compute numbers in direct collection passes. Observed source/weather changes settle affected numeric states to the observation boundary before updating cached rates. No piece waits behind the 50-item visual budget to have its arithmetic updated.

Source polling approximates change time by observation time; sub-poll fuel/wind transitions are not reconstructed. Several observations in one frame can be processed directly without a retained event log. Rendering can lag and use only the newest result. If a pathological math/topology pass is split across frames, preserve a cursor and an integration boundary so later inputs cannot be applied retroactively; this safeguard is not a generic multi-event history framework.

Pause does not melt snow. Sleep/time jumps use weather catch-up once and no giant heat delta. On clock reversal, rebase from the accepted runtime/snapshot boundary, never integrate a negative duration. Distinguish winter epochs to prevent old snapshots reappearing next winter. Ordered intervals matter near clamps: clamping after a hot interval and then a snowy interval is not equivalent to one final clamp over their net sum.

## 6. Work partition and performance envelope

### 6.1 Numeric work

Use compact lists of unique changing pieces, cached references, cached coefficients, and swap-removal indices. Source links need traversal on topology/activity changes, not automatically once per rendered frame. Maintain source lists even while all nearby pieces sleep, so a later change can wake them.

Normal arithmetic has no arbitrary 20/50-piece cap. It may process the whole changing collection promptly. It must contain no `GetComponent*`, hierarchy walks, prefab-name parsing, collider/transform reads, material access, ZDO reads/writes, LINQ, or per-step allocations. Basic operations on cached vectors/numbers are allowed; no managed worker thread touches Unity APIs.

For scale: 10,000 changing pieces at 60 updates/second imply 600,000 small state evaluations/second. This is an operation-count illustration, not a time benchmark. At 100 FPS it is 1,000,000. Cache locality, collection layout, runtime, and concurrent systems determine the cost. Use counters and an infrequent elapsed-time sample before choosing a lower arithmetic cadence or chunk size. Do not introduce a queue/heap whose administration costs more than the numeric work it schedules.

### 6.2 Visual work

A dirty list holds a piece handle at most once. Its entry references the newest desired level/variant. Apply initially up to **50 piece visuals per frame**; count work consistently because one piece may switch up to three cap objects. Avoid setter calls when the selected material and active state are already correct.

The 0.01 change gate compares against the last applied visual level, not against the previous frame's tiny delta. Do not advance that reference until application actually happens. A queued piece may become unchanged again; remove/skip it safely. Zero, maximum, visibility threshold, binding changes, damage-state switches, and forced cleanup must reach a correct final visual even with a smaller difference.

At 60 FPS, 50 applications/frame permit at most 3,000 piece applications/second; a single 10,000-piece wave needs at least 3.33 seconds. At 100 FPS the lower bound is 2 seconds. These are throughput limits, not CPU timing estimates. Ordinary math must not inherit this delay. End-of-winter/Disabled hides have priority and do not leave pieces in a gradual-melting state while awaiting visual application.

### 6.3 Geometry and publication

Custom cover tests, hierarchy changes, initial component discovery, link construction, and material creation are not classified as pure math merely because called from the calculation path. Start with an independent small geometry budget (20 piece inspections/frame plus an elapsed-time stop); subdivide unusually expensive registrations rather than hiding unbounded work in one item.

Publish coalesced coherent snapshots independently of visual application, after a normal 0.01 buildup change or a required baseline/terminal transition. A renderer queue is not a network rate limiter. Ownerless publication retains the baseline's restricted initialization behavior, not a new per-frame broadcast. No routine per-percentage force-send or RPC.

End-of-world cleanup restores bindings before destroying owned material variants. It cannot depend on a frame driver that has already stopped. Destruction of one object retires its local state promptly; it never starts a world-wide calculation.

## 7. Distance, readiness, ownership, and replication

The original implementation already has a deliberate ownerless initialization path: `CanInitializeSnowState` allows `owner == 0` or the local owner, and its initialization helpers update state. `UpdatePendingLocalSnowVisual` can present a read-only passive target while area readiness is pending. `UpdateActiveAreaState`/`CanOwnSnowState` apply different gates to live owner-side work. The first plan's blanket owner-only interpretation was incorrect. [S1]

Keep these concerns separate:

| Concern | Required boundary |
| --- | --- |
| Discovery / provisional distant appearance | Do not wait for ownership acquisition. Preserve existing pending/readiness behavior. |
| Final geometry/source classification | Wait for `IsAreaReady`; retry only affected pending sectors/states. |
| Loaded ambient weather and static heat | Calculate/render in the supported loaded, ready area even when `owner == 0` or it is outside the narrow ownership/interaction radius. |
| Live use of a station, bed, or chair | Retain existing active-area, valid-player/use, ownership/RPC boundaries. |
| Normal authoritative publication | Current owner publishes; keep the existing ownerless initialization/reconciliation contract separately. Never claim ownership to paint or melt caps. |
| Another actual owner | Accept its valid newer state; local ambient presentation is not permission to overwrite that owner's ZDO. |

Map `IsAreaReady`, `OutsideActiveArea`, ownerless initialization, active-area entry/exit, and local provisional state method by method before deleting old code. Retain their purposes, distances, and readiness protection; do not preserve their old arithmetic when this revision intentionally replaces it. Do not gate all work behind `CanOwnSnowState`, and do not equate ready-but-unowned with unloaded.

For unowned ready pieces, maintain real local ambient state and timestamps, not just a static minimum preview. Keep the baseline's idempotent ownerless initialization/reconciliation of own snow metadata; continuous observations can stay local until the legitimate writer takes over. A local unowned ZDO change is not proof of delivery to the server. On acquiring ownership in the same process, retain already simulated continuous state when its epoch/snapshot lineage is valid instead of restarting it from an older initialization. Cross-peer handoff reconciles accepted snapshots and weather; no historical heat or takeover claim is invented.

For another owner's pieces, reuse valid accepted snow state and preserve distant presentation. Do not continuously reset a locally progressing visual to the same old packet. Rebase only for a newer snow snapshot/epoch, not every unrelated `DataRevision` change. Rendering/static-heat scope must not shrink to the interaction radius when a remote owner appears.

`DataRevision` is not a notification. Observe completed incoming snow-relevant snapshots and ownership changes for tracked IDs, with numeric/state integration outside packet parsing. Idle pieces must wake on actual changed data. Do not patch every `ZDO.GetFloat` or write overload or add a global per-frame ZDO scan. [G3], [G4]

Dedicated servers do not create material pools, but still perform applicable authoritative calculation. Visual preferences must not affect persistent snow or heater classification. Use the existing multiplayer trust boundary, expire remote interaction activity, and never accept a client-selected arbitrary snow value as authoritative.

## 8. Heat registry and spatial links

Use a logical `Fireplace`/`Smelter` source when relevant heat areas belong to it. Cache the component once. Multiple child areas, including low/high fire variants, contribute at most the strongest applicable influence of that logical source to one piece. Distinct sources add. A `Smelter` implementation without a relevant heat area is not automatically a heater.

Observe `IsBurning()` / `IsActive()` and cached area/collider activity periodically or through existing periodic state callbacks. Do not read fuel history or add component lookups to the poll. An area can be enabled while its fireplace is not burning; a collider can be disabled without an `EffectArea.OnDisable` call. Register inactive links as topology, then update effective activity separately. Group generic sources without a fireplace/smelter by the existing logical source boundary.

Preserve center-distance falloff, the inside-volume exception, self-heating, and roof-over-leaky multiplier precedence. Cache geometry weights once. Primitive shape containment can be arithmetic after registration; uncommon shapes may need a one-time narrow geometry check. AABB is a broad-phase filter, not an exact rotated-box/mesh containment test.

Use game `SectorIndex` (64x64 meters) with local 8-meter heat cells. Save `ChunkIndex` aggregates sectors; it is not a fine heat grid. Handle negative coordinates and sector boundaries using the game's mapping. Heat-area footprint/radius handling remains necessary for neighbor lookup; the simplified nine-sector **roof invalidation** rule does not mean an arbitrarily large heater is indexed only at its center. [G5]

Exclude moving sources: carried items, characters, ships/carts, and mobile parent hierarchies. `isStatic == false` or an asleep/kinematic rigidbody alone is not a reliable classification. A relocated supported source invalidates old/new links. Cached identity and generation protect against destruction/reuse. Removal/duplicate callbacks cannot double-subtract influence or leave a negative aggregate.

Interactive melting remains a small separate active set and keeps its existing multiplier, single-object scope, and precedence over ordinary static-heat melting while a use is active. It does not multiply by number of users. Snowfall still participates in the signed calculation; use one weather contribution for this separate interaction path. Cover melting remains an independent environmental loss. Do not delete station/attachment behavior as a side effect of excluding moving heat sources.

## 9. Cover algorithm and sector invalidation

Retain Seasons' highest-valid-collider surface probe, fallback origin, upward offset, cast radius/distance, layer mask, and self-hierarchy exclusion. It is not interchangeable with vanilla `HaveRoof`, especially for leaky geometry. `m_haveRoof` prefix/postfix comparison is an additional invalidation signal, not the cached result of our test. [S1], [G1]

For a changed object, mark its center sector and eight neighbors dirty. Coalesce revisions and pending sector work. Do not enumerate geometry or calculate world-space bounds merely to narrow the affected region. For relocation, dirty the nine sectors around both old and new centers. For known multi-sector terrain edits use the touched sector coordinates already provided by the operation.

This is intentionally conservative for ordinary pieces and intentionally incomplete for an arbitrarily huge modded collider reaching beyond the center-plus-neighbors region. Do not disguise that limit as exact coverage. A known adapter can explicitly invalidate more sector coordinates; the default path does not inspect every object's bounds.

Notifications must include relevant non-snow geometry: placement/removal, zone load/unload, door/gate state, health-visual collider changes, trees/rocks/destructibles, and terrain edits. Compare the old/new state so unchanged periodic callbacks do not keep nine sectors dirty. A moving or unknown collider changed by a different mod requires an explicit notification; arbitrary uninstrumented changes cannot be inferred from a revision table.

Area readiness and sector revisions protect against loading-order mistakes. If a revision changes while an inspection is pending, run again for the latest revision. Invalidate cached probe origins when the receiving piece's geometry changes. Avoid forcing `Physics.SyncTransforms` per piece. No stable static piece receives a periodic blanket roof recast.

After inspection, update the cached covered flag and arithmetic collection directly. Gradual melting requires no further geometry work until the next invalidation. Shelter and shield are separate cached inputs.

## 10. Exclusive cap rendering

Bind the three actual cap renderer references once, deduplicating identical references and preserving vanilla fallback selection for missing worn/broken variants. Observe all real health-visual changes, not only one RPC, so repairs/loading and direct state changes also select the correct cap. Cap pools never wrap seasonal-color materials.

Keep the existing visible mapping: buildup must be greater than 0.25; `_SnowLevel = clamp01((buildup - 0.25) / 0.75)`. Use precise buildup before quantization. A positive visible level below 1% selects the first visible variant, not original index 0 as an assumed zero-level material. Restore index 0 when hidden. At 1.0 buildup use variant 100; native `m_snowBuildup` still remains zero.

Use `sharedMaterial` and cached pool bindings. No `.material`, per-object material copies, repeated `CopyPropertiesFromMaterial`, or hot material-name/shader lookup. Lazy clones are immutable after setting their index once. Preserve original shaders/keywords/instancing flags, displacement bounds, layers, shadows, LOD selection, and nullable-axis position/scale rules. Destroy only indices 1..100 after binding restoration. Modded materials of the same name are distinct pools if their original references differ.

### 10.1 Taking and keeping visual ownership

The maintainer explicitly chooses exclusive ownership of the managed cap renderers. Do not merge or preserve foreign cap property overrides while Seasons owns them. Another snow controller must be disabled, or Seasons' snow feature must be disabled. This is a stated compatibility boundary, not a contest between two writers each frame.

At acquisition/rebinding, remove both the per-renderer block and any per-material blocks on those cap renderers. Unity provides `SetPropertyBlock(null)` and its material-index overload to remove the corresponding overrides. Setting `_SnowLevel = 0` in a retained block is **not** equivalent: that zero would still override our positive pooled material value. Neutralize stale native/cached snow state, then leave no cap property block. Do not set the original shared asset's `_SnowLevel` to zero; index 0 remains the original. [U1]

Clearing the renderer alone is insufficient. Native `MaterialMan.PropertyContainer.UpdateBlock` reapplies its block to all assigned renderers, and `RefreshRenderers` gathers children again. Exclude our managed caps from existing relevant containers and from future refresh/registration lists. Filter at registration/handoff with cached renderer identity, not by scanning every container every frame or patching Unity's global setter. Handle previously registered containers once through bounded discovery/reverse membership. [G2]

If the controlled piece's existing container retains a legacy `_SnowLevel` entry, remove that stale snow entry and rebuild the remaining non-snow properties once as needed. Preserve unrelated renderers and highlight/ash properties of the building. Do not call `MaterialMan.SetValue` to render snow, unregister the entire building, or create a notifier as part of normal snow rendering. A small cap-exclusion integration with MaterialMan is allowed; snow calculation/rendering does not use it.

The deliberate tradeoff is that caps themselves no longer inherit MaterialMan-driven highlight/ash overrides; the underlying building still does. Releasing Seasons ownership restores normal registration eligibility, not obsolete captured foreign snow overrides. Do not add permanent snapshot/merge logic for unsupported concurrent snow controllers. A different mod writing a cap block afterward is outside the chosen compatibility contract; no costly global per-frame enforcement is promised.

Shared materials may reduce repeated state work but do not guarantee batching across different meshes/passes or eliminate geometry and shadow draws. The 50-item budget limits visual **changes**, not how many enabled caps Unity draws every frame. [U2]

## 11. Persistence, migration, and lifecycle

Retain a Seasons-owned float level, winter epoch, placement/initialization distinction, and a coherent last-accounted weather time/baseline. No post-initialization minimum-floor flag is required for the new rule; consume useful legacy placement/history before retiring obsolete below-minimum mechanics. Use field presence rather than zero to identify uninitialized state.

Normal publication is limited by actual level change of 0.01, with exact terminal/baseline snapshots when needed. Snapshot value and timestamp must describe the same integration state. Do not advance timestamp alone while leaving an older value that would lose fractional accumulation. Sleeping zero/max states need transition/deactivation rebasing, not recurring writes.

Migrate legacy native seasonal snow only at activation, the loaded-piece season pass, or the appropriate authority/ownerless initialization transition. Prefer a valid new snapshot; if absent, transfer marked legacy Seasons snow/history before clearing native `s_snow`. Retry clearing native state even if a previous attempt already created the new value. Ownerless initialization follows the existing local contract; real remote ownership is not stolen. Arbitrary unmarked native/foreign snow and native Deep North remain outside this legacy migration.

No background scan of every world ZDO. Unvisited old records can retain old native data if the mod is removed before processing them; do not promise otherwise. New data does not depend on shutdown code to be invisible without Seasons. A saved, processed piece has no positive native seasonal buildup to restore after removal.

Season end clears all loaded Seasons seasonal state/desired visuals without a melting phase, regardless of heat, cover, or owner. Invalidate the winter epoch so unloaded old state cannot revive. This does not mean synchronously repainting every renderer in one frame or traversing all world saves. Feature disable clears its own snow; disconnect preserves coherent snapshots and releases references/materials.

Keep real-world registration separate from placement previews. Previews may retain existing copy/transform handling but never receive persistence or migration. Donor copying occurs only when the primary cap is absent, before native renderer initialization as required, with first-sibling placement. Configuration changes restore original transforms and remove only Seasons-created copies. Build new metadata once, then schedule geometry/binding work through its own budget; keep the existing common configuration path.

Use world/session generations and stable recorded ZDO IDs. A destroyed/recycled object or reset ZDO cannot remain as an active key or dirty visual. Retire references before disposing pools; callbacks during teardown cannot restart world processing. [G3]

## 12. Implementation order

1. Preserve the pinned baseline and map current readiness/ownership/active-area behavior. Treat this revision's gameplay decisions as intentional replacements, not an excuse to change unrelated gates.
2. Separate precise own persistence and discovery/construction/reactivation paths. Remove the 0.99 cap and automatic minimum jumps; add winter epochs and correct weather-cursor accounting.
3. Introduce cap-only pools, exclusive property-block cleanup, and MaterialMan cap exclusion. Keep `PrefabVariantController` unchanged.
4. Add the singleton, compact source/piece relations, direct arithmetic, sleeping membership, and independent visual/geometry/publication work. Do not implement the superseded per-percentage heap.
5. Add additive/source-wise heat, existing source predicates, preserved interaction boundaries, and covered-piece melting with the new config.
6. Replace periodic custom cover recasts with center-plus-neighbor sector invalidation without changing the custom test itself.
7. Integrate distant ownerless state, incoming snapshots, gap weather catch-up, time skips, season cleanup, configuration changes, and teardown.
8. Remove obsolete native snow RPC/value routes and redundant calculation paths. Manually measure functional/performance acceptance on the maintainer's game; no build/game execution is requested from the assistant.

## 13. Acceptance cases

| Case | Expected result |
| --- | --- |
| First discovery on winter day N | Exposed eligible existing piece gets minimum plus timeline gain exactly once. Current heat then melts live. |
| Build during snowfall | Starts clean and gains only later weather; no jump to the discovery minimum. |
| Repeated zone reload / accepted zero snapshot | No reseeding or duplicate weather credit. |
| Heat stops during snowfall | Continuous accumulation from current level, including below the minimum. |
| Fire burns out during absence | Restore snapshot, add skipped genuine snowfall once, then use current activity. No fuel-history reconstruction. |
| Fire still burns on return / sleep skips time | No retrospective heat loss over the entire gap; ordinary live melting resumes. |
| Two heaters vs one / low-high child areas | Distinct logical sources count; duplicates of one source do not. Weather multiplicity follows section 4. |
| Add weak heater during snow | Exercise and document the selected approximation's possible increase in snow rate; do not assume every heater beats weather. |
| Same sources iterated in another order | Sum contributions then clamp once; no collection-order bias. |
| Zero snow, heat weakens or snowfall strengthens | Sleeping piece wakes if combined rate becomes positive. Topology was not discarded at zero. |
| Roof added, removed midway, then added again | Gradual configured loss, no repeated raycasts, continuous remaining value, no reset to minimum. |
| Cover multiplier 0 / all-heat multiplier 0 / roof-tag multiplier 0 | Independent documented meanings; roof cover loss is not disabled by an unrelated heat setting. |
| Shield vs roof / end of winter | Shield and season cleanup remain hard suppression; only ordinary roof acquisition melts gradually. |
| Full 1.0 own buildup | Variant 100, no native heavy snow damage/support state and no new global damage patch. |
| Ready ownerless distant pieces with static heat | Snow is calculated and shown without approaching the ownership radius. |
| Area not ready, then ready | Provisional display is not a permanent geometry/placement decision; initialize correctly after readiness. |
| Active-area entry/exit | Existing narrow interaction boundaries survive; ambient calculation is not accidentally disabled. |
| Ownership changes, unrelated ZDO revision | No reset to a repeated stale snapshot, double catch-up, arbitrary ownership claim, or foreign-owner writes. |
| Idle remote snapshot arrives | Changed snow wakes the state without a scan of every ZDO. |
| 10,000 numeric changes, slow visual queue | Arithmetic completes promptly; at most 50 piece visual changes/frame; coalesce to latest result. |
| Tiny repeated numeric deltas | They accumulate to the 0.01 publication/application threshold; no early reference reset loses them. |
| Large geometry invalidation | Its independent small budget cannot block unrelated arithmetic. Repeated sector revisions coalesce. |
| Ordinary blocker across a sector boundary | Nine-sector invalidation reaches neighbors; custom snow-cover semantics remain intact. |
| Giant external collider beyond the nine sectors | Known limitation/explicit adapter, not an implicit bounds scan. |
| Door, damage geometry, terrain, tree/rock removal | Invalidate custom cover even if vanilla `m_haveRoof` does not change. |
| Old/foreign property block, then highlight refresh | Managed cap has no block and stays excluded from MaterialMan; underlying building retains other effects. |
| Disabled/Ignore/normal transition | Acquire/release exclusive cap ownership without preserving stale zero overrides. |
| Three cap variants / missing or duplicate references | Correct active cap, no material creation per instance, no redundant setters. |
| Config copy/position/scale changes | No donor mutation, scale accumulation, leaked copy, or stale binding. |
| Migrated piece saved, mod removed | Its added snow is absent; native Deep North snow remains. |
| Repeated world changes, headless host | No retained runtime state/material leak; no headless material allocation. |
| Original FPS failure scene in calm/snow/melting | Compare CPU arithmetic, geometry checks, visual setters, MaterialMan, GPU/draw/shadow work, allocations, and network publications separately. |

## 14. Explicit proposals and non-goals

The requested source-wise repeated snowfall is selected and disclosed, not silently corrected. Exact single-count snowfall is an equally cheap alternative but needs an explicit formula decision to replace it. The covered-piece config default 2x is selected as a planning proposal; the underlying per-second balance anchor remains to be checked against desired gameplay, not against a promise of a particular FPS.

No retained per-percentage heap, generic event-history subsystem, fuel-history simulator, partial JSON merge, renderer ownership negotiation, whole-world migration sweep, LOD redesign, or distance culling is part of this plan. Geometry/render/network work remains bounded separately even when arithmetic is allowed to run promptly. A quiet numerical state does not mean Unity stops drawing visible snow meshes.

## 15. Source map

[S1]: https://github.com/shudnal/Seasons/blob/988e98c514ce49369a92cbaf934a7aca384fe6e1/WinterSnow/SeasonalSnow.cs
[S2]: https://github.com/shudnal/Seasons/blob/988e98c514ce49369a92cbaf934a7aca384fe6e1/Seasons.cs
[S3]: https://github.com/shudnal/Seasons/blob/988e98c514ce49369a92cbaf934a7aca384fe6e1/WinterSnow/SeasonalSnowMeshSettings.cs
[G1]: https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/WearNTear.cs
[G2]: https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/MaterialMan.cs
[G3]: https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/ZDO.cs
[G4]: https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/ZDOMan.cs
[G5]: https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/ZoneSystem.cs
[U1]: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Renderer.SetPropertyBlock.html
[U2]: https://docs.unity3d.com/2022.3/Documentation/Manual/GPUInstancing.html

- [S1] Existing timeline, initialization, `CanInitializeSnowState`, provisional display, readiness/active-area gates, cover, heat, interaction, and native snow patches. Its old minimum/heat/roof arithmetic is intentionally superseded where stated above.
- [S2] Existing config defaults and position eligibility; the new covered-piece multiplier and maximum adjustment are planned changes, not current source behavior.
- [S3] Existing donor, transform, Ignore/Disabled, and MaterialMan interactions to adapt.
- [G1] Native cap mapping, visual selection, snow storage, and heavy-snow inputs to isolate from own buildup.
- [G2] `PropertyContainer.RefreshRenderers` and `UpdateBlock` explain why clearing a cap block once without registration exclusion is insufficient.
- [G3], [G4] ZDO identity, typed data, deserialization, ownership, and synchronization boundaries.
- [G5] Sector coordinates and area-readiness integration.
- [U1] Removing per-renderer/per-material overrides; a retained zero is still an override.
- [U2] Instancing requirements and limits of material sharing as a performance claim.
