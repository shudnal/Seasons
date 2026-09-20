# Snow performance

Status: planning only. No runtime code, version change, build, or game execution is included.

Branch: `perf/snow-performance`.
Baseline: `shudnal/Seasons` at `988e98c514ce49369a92cbaf934a7aca384fe6e1`.
Game reference: `shudnal/assemblies_combined` at `d1374bfd9175ac8f733ae483b0a06e5c8b75906e`.

This revision supersedes both earlier concurrent snowfall/heat formulas. A managed piece is either accumulating or melting; active effective heat always selects melting, without a snowfall counter-term. Four mutually exclusive buckets hold the pieces, and region-filtered trigger queues maintain membership. The verified distant provisional display, ready-area reconciliation, and active-area/authority boundaries remain separate from these buckets. One-time discovery seeding, clean construction, gradual melting under a newly added roof, a full 1.0 maximum, separate work budgets, and exclusive cap ownership remain the selected design. This is a plan, not a claim that the runtime changes already exist.

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
| Ambient simulation | Four exclusive buckets: accumulation-full, accumulation-open, melting-empty, melting-snow. Arithmetic visits only accumulation-open during eligible snowfall and melting-snow with a positive rate. No percentage heap. |
| Visual application | A deduplicated dirty list, initially up to 50 piece-visual applications per frame, using the latest level rather than replaying intermediate levels. |
| Expensive geometry | Independent resumable work, initially up to 20 piece inspections per frame and an elapsed-time guard. This is not a limit on arithmetic. |
| Network work | Separate coalesced publication work. No write/RPC on every arithmetic update. |
| Cover | Preserve the Seasons cover algorithm. Invalidate the center sector and its eight neighbors, without calculating each changed object's full bounds. |
| Heat | Cache stationary sources and links; sum distinct heaters into a melt rate. Source changes enqueue only linked pieces/affected regions. No per-source snowfall term. |
| Distance and authority | Preserve provisional caps before area readiness, geometry-aware reconciliation after readiness, and active-area live-use/publication gates independently. Never gate all cap display on readiness, activity, or ownership. |
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
| First discovery of an eligible existing exposed piece for the current winter, without a valid snapshot | `min(maximum, configuredMinimum + timelineSnowSinceWinterStart)`. Apply once, then use normal deltas. Current heat does not replace this target with an invented zero; geometry-ready melting handles it within the permitted local calculation scope. |
| A piece is newly built during the current winter | Start at zero and record placement time. It receives neither the discovery minimum nor snowfall from before placement. |
| A known piece is reactivated | Restore its snapshot and account only for its unprocessed weather interval. Do not classify activation itself as first discovery and do not add the minimum again. |

The minimum is a discovery seed, never a floor for ongoing accumulation. Growth after construction, melting, heater shutdown, or roof removal passes continuously through values below the configured minimum. Delete the old minimum-entry jump and any catch-up formula that silently reinserts it.

A first-discovered piece already sheltered by the custom cover test starts clean. If it previously had only an unverified distant cap, remove that provisional cap as a correction when geometry becomes ready; do not convert that visual estimate into real snow and melt it slowly. A known piece with real snow that subsequently acquires a roof follows gradual melting below. A shield remains a separate hard suppression rule. These two exclusions must not be conflated with ordinary heating. The first-discovery sheltered exception is retained explicitly rather than silently applying an exposed-piece seed indoors.

Retain placement identity, initialization epoch, and weather accounting position across unloads. A valid zero snapshot is not absence. An object built in an earlier season/winter is an existing object for a new winter; construction metadata must not suppress discovery seeding forever. Preliminary appearance before `IsAreaReady` remains read-only and cannot irrevocably seed or reject an object before geometry/placement state is known.

### 3.3 Live changes

| Condition | Behavior |
| --- | --- |
| Snowfall and effective heat coexist | The piece is in melting mode. Subtract its combined melt rate; do not add or subtract snowfall in this branch. Snowfall cannot reverse or slow this mode. |
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

A covered piece belongs to melting mode, uses no weather input, and subtracts cover melting plus the applicable heater or interaction rate. Cover melting does not require a fire and is not multiplied by the existing `roof pieces` heat multiplier, whose default is zero. It is also independent of the `all heat sources` multiplier: disabling fireplaces' contribution should not disable sheltered melting. Config zero stops this new melting but still blocks snowfall under a roof.

Cache `covered` and the resulting rate after geometry inspection. Each subsequent update is subtraction and clamping only. Move a covered piece from melting-snow to melting-empty at zero; retain its registry and source links for later condition changes. No extra raycast, collider access, or source search is needed for gradual melting. The remaining cost is continued rendering while a visible cap melts; unlike instant removal, geometry/shadow work lasts until buildup crosses the visibility threshold. Do not promise that extra visible lifetime is free.

## 4. Two modes and four exclusive buckets

Let `s` be the precise working buildup, `maximum` the configured limit, `A` the cached eligible per-biome weather gain for the interval, and `M` the cached effective melt rate.

- Accumulation mode: `s = min(maximum, s + A)`, only when eligible snowfall supplies positive gain.
- Melting mode: `s = max(0, s - M * elapsedActiveSeconds)`. There is no weather term.

This replaces both `A - combinedHeat` and `sourceCount * A - combinedHeat`. There is no source-count multiplier, net-rate sign classification, snowfall-dependent slowing of a heater, or weak-heater snow amplification. Distinct heaters still add their effective melt contributions. Source iteration maintains coefficients and invalidates states; it does not update the same piece once per heater.

### 4.1 The four lists

| Bucket | Membership | Normal arithmetic |
| --- | --- | --- |
| `AccumulationFull` | Accumulation mode, `s == maximum` | None. |
| `AccumulationOpen` | Accumulation mode, `0 <= s < maximum` | Add weather only for a permitted evaluation scope and a snowing biome. No snowfall means no piece pass. |
| `MeltingEmpty` | Melting mode, `s == 0` | None, even during snowfall. |
| `MeltingSnow` | Melting mode, `s > 0` | Subtract the cached positive melt rate while the evaluation scope permits it. |

Every registered Seasons-managed seasonal piece has exactly one bucket, including a piece with a provisional visual value. Bucket membership does not certify that this value is authoritative or that geometry is ready. Section 7 defines those independent permissions. Native Deep North pieces, previews, Ignore/Disabled pieces, and globally disabled/out-of-season processing do not become a fifth simulation bucket: they are outside ordinary seasonal simulation while their cleanup/release is handled explicitly.

Use four logical buckets partitioned by sector so regional work does not first scan all pieces. Keep dense lists and a stored bucket/index; swap-removal moves a state without `List.Contains`, `IndexOf`, or linear deletion. The registry owns each state once. Source links, the state-refresh queue, and the visual dirty queue hold handles to it, not independent copies of snow state.

### 4.2 Melting includes shelter, not just a nearby fireplace

A positive effective stationary-heater contribution, an eligible active interaction, or confirmed shelter selects melting mode. A newly added roof therefore uses the same two melting buckets without a fifth roof-processing list. Preserve the existing separate interaction precedence: it replaces ordinary static-heat melting while active; cover melting remains independent. Do not add multiple users' interaction rates.

A merely nearby inactive heater or a link whose effective multiplier is zero is not positive heat. Preserve the meanings of global heat disable, zero distance/self multipliers, and the existing roof-over-leaky heat rule. Thus a roof-tagged exposed piece with heat multiplier zero is not automatically stripped by a nearby fireplace. Distinguish that heat immunity from being geometrically covered by another roof.

Shelter always blocks accumulation, even when its new covered-piece melt multiplier is zero. Such a nonempty piece remains in `MeltingSnow` with a cached zero rate and does no numeric integration; it wakes on a relevant parameter/source/cover change. This is a paused configured case, not a new simulated mode. For the selected positive 2x default it melts normally. Shields retain their independent hard-suppression behavior.

### 4.3 Region-filtered state-refresh queue

External triggers enqueue reevaluation, not direct scene queries or immediate reassignment of all pieces. Deduplicate one pending record per piece/sector, combine reason flags, and retain the latest source/geometry revision. Scope work using source links or sector indices:

- Heater ignition/extinction/destruction: its linked receivers, including zero/full idle receivers.
- Roof/geometry change: the center sector plus its eight neighbors; a relocation invalidates both old and new groups.
- Area readiness/active-area changes: affected regions, using the exact existing predicates where their result can differ within one sector.
- Configuration, mode, ownership, relevant incoming snapshot, placement, and season changes: the applicable pieces/regions.

A queued refresh uses cached data for cheap reasons. Only geometry/hierarchy/topology reasons request expensive work. Changing a fire's active bit must not force a new roof cast or full source search for every linked piece. While waiting for reevaluation a state remains in its previous bucket; it cannot be duplicated or disappear from the registry.

After reevaluation, settle the old numeric mode to the refresh's effective boundary, update cached causes/rate/evaluation permissions, and move to the matching bucket. Source observation/queued classification is deliberately approximate: by default the refreshed state becomes effective when that refresh is applied. Do not backdate its new rate over the entire queue delay. Multiple toggles before a refresh can coalesce to the current observed state; no retained fuel/event history is required. Prompt cheap refresh processing limits this latency independently of the visual budget.

When arithmetic reaches zero/maximum, clamp exactly to the bound and request a cheap bucket move. Drain these moves at a safe pass boundary, preferably the same frame; do not send them through geometry inspection or the renderer budget. A threshold move does not recompute sources or cover. Keep source/sector membership at the bounds so a later trigger reaches the sleeping piece.

### 4.4 Per-frame work

Process state refreshes, then the permitted accumulation-open groups and melting-snow groups. Read cached numbers and integrate each unique piece at most once for that interval. Queue a visual/publication change only when its independent threshold is reached. Do not iterate `AccumulationFull` or `MeltingEmpty` to confirm that nothing happened.

Weather onset/cessation changes the weather input of accumulation groups; it does not transfer pieces between heating and cooling lists. A piece can remain in `AccumulationOpen` throughout clear weather at zero cost. Heating during a blizzard still drains `MeltingSnow` into `MeltingEmpty` and remains there until the heat/shelter cause changes. A second heater only changes `M`, unless it changes whether any effective melting cause exists.

## 5. Time, background catch-up, and transitions

Use the existing per-biome weather timeline and cumulative gain, not the current environment at the local player's position for all pieces. Preserve deterministic environment selection, ordering/weights, random-state restoration, season calendar, and compatibility inputs. Cache per-biome weather gain once per step. [S1]

Live heat/cover/interaction melting uses elapsed unpaused simulation time, not the frame duration at an infrequent `UpdateWear` callback. Weather history uses the existing world/season time basis. Reference coefficients from the first plan were weather `environmentSnow * 0.01 * Game.m_snowBuildupSpeed * accumulationMultiplier`, static heat `0.18 * 0.01`, and interaction `0.002 * interactiveMultiplier`. They must be reviewed as explicit rates during implementation, not automatically multiplied by 60 or 100 to match a chosen FPS. This revision does not claim an empirical old-to-new speed match.

### 5.1 Returning to an unloaded or non-simulated area

Do not inspect historical fuel timestamps or reconstruct when fires stopped. At reactivation, restore the latest usable snapshot and add genuine timeline snowfall for the unprocessed interval, clipped to the applicable winter and not earlier than placement. Then apply the current heater states through ordinary live calculation. This guarantees the requested case: a nearly exhausted fire is out on return, and snowfall during the absence is added without a minimum jump.

The gap approximation uses genuine weather gain once; current heater counts do not multiply it. Do not replay historical heat, cover melting, or interaction melting. A currently lit fire does not cause hours/days of instantaneous melting; it melts the restored snow live. This can show temporarily more snow even beside a still-burning fire and is an accepted approximation that avoids historical fuel simulation.

Use current cached cover to avoid crediting unseen snowfall to currently sheltered pieces; keep stored snow and let it melt live. Geometry history during absence is not reconstructed. The last-accounted weather boundary must cover live heating, even at a saturated zero, so reactivation cannot repay snowfall already excluded by melting mode in a **loaded, actually evaluated** interval. Represent this with the state/region mode interval and settle its weather cursor on transition or controlled deactivation. Do not visit every `MeltingEmpty` piece or write timestamps each frame just to advance that cursor.

A still-loaded ownerless piece that has been simulated locally has no gap merely because it lacks an owner. Preserve its runtime weather cursor and melt result. Likewise an incoming packet's snapshot time is not automatically the start of a new unprocessed interval. State handoff must distinguish actual skipped simulation from already integrated local time.

### 5.2 Queue classification, not each arithmetic step

Only state/topology/readiness changes wait for the region-filtered refresh queue. The current mode's ordinary arithmetic and its accumulated fractional deltas do not wait for the 50-item visual budget. Apply newly observed inputs at the queued refresh boundary described in section 4; no per-percentage heap, net-rate arbitration, or general event log is required.

Integrate an old mode to a known boundary at most once, then change the mode/rate. A piece migrating between lists in one frame cannot be integrated in both lists for the same time interval. Keep a last numeric time or pass stamp. Bound-triggered moves need only cheap metadata changes.

Pause does not melt snow. Sleep/time jumps use the existing weather catch-up time basis once and no giant heat delta. On clock reversal, rebase from the accepted runtime/snapshot boundary, never integrate a negative duration. Distinguish winter epochs to prevent old snapshots reappearing next winter. A readiness/ownership transition is not inherently elapsed offline time or a new discovery.

A queued load-order correction is different from a physical condition change. Do not use a provisional distant cap as the old real value to integrate before readiness: first select the real saved/initialized state, correct unsupported provisional visuals, then start the permitted mode.

## 6. Work partition and performance envelope

### 6.1 Numeric work

Use the four sector-partitioned buckets, cached references/rates, and swap-removal indices. Arithmetic visits only permitted accumulation-open groups during snowfall and positive-rate melting-snow groups. Source links are revisited on relevant queued changes, not automatically once per rendered frame. Keep links for idle buckets so triggers can wake them. Region evaluation status selects real or provisional processing without a full per-piece readiness poll in the numeric loop.

Normal arithmetic and cheap metadata-only list moves have no arbitrary 20/50-piece cap. They may process their eligible collections promptly; expensive classification and Unity setters retain separate budgets. It must contain no `GetComponent*`, hierarchy walks, prefab-name parsing, collider/transform reads, material access, ZDO reads/writes, LINQ, or per-step allocations. Basic operations on cached vectors/numbers are allowed; no managed worker thread touches Unity APIs.

For scale: 10,000 changing pieces at 60 updates/second imply 600,000 small state evaluations/second. This is an operation-count illustration, not a time benchmark. At 100 FPS it is 1,000,000. Cache locality, collection layout, runtime, and concurrent systems determine the cost. Use counters and an infrequent elapsed-time sample before choosing a lower arithmetic cadence or chunk size. Do not introduce a queue/heap whose administration costs more than the numeric work it schedules.

### 6.2 Visual work

A dirty list holds a piece handle at most once. Its entry references the newest desired level/variant. Apply initially up to **50 piece visuals per frame**; count work consistently because one piece may switch up to three cap objects. Avoid setter calls when the selected material and active state are already correct.

The 0.01 change gate compares against the last applied visual level, not against the previous frame's tiny delta. Do not advance that reference until application actually happens. A queued piece may become unchanged again; remove/skip it safely. Zero, maximum, visibility threshold, binding changes, damage-state switches, and forced cleanup must reach a correct final visual even with a smaller difference.

At 60 FPS, 50 applications/frame permit at most 3,000 piece applications/second; a single 10,000-piece wave needs at least 3.33 seconds. At 100 FPS the lower bound is 2 seconds. These are throughput limits, not CPU timing estimates. Ordinary math must not inherit this delay. End-of-winter/Disabled hides have priority and do not leave pieces in a gradual-melting state while awaiting visual application.

### 6.3 Geometry and publication

Custom cover tests, hierarchy changes, initial component discovery, link construction, and material creation are not classified as pure math merely because called from the calculation path. Start with an independent small geometry budget (20 piece inspections/frame plus an elapsed-time stop); subdivide unusually expensive registrations rather than hiding unbounded work in one item.

Publish coalesced coherent snapshots independently of visual application, after a normal 0.01 buildup change or a required baseline/terminal transition. A renderer queue is not a network rate limiter. Ownerless publication retains the baseline's restricted initialization behavior, not a new per-frame broadcast. No routine per-percentage force-send or RPC.

End-of-world cleanup restores bindings before destroying owned material variants. It cannot depend on a frame driver that has already stopped. Destruction of one object retires its local state promptly; it never starts a world-wide calculation.

## 7. Preserve the verified distant-cap progression

The four thermal buckets are not four distance states. They must not replace the baseline's independent provisional appearance, geometry readiness, active area, and authority decisions. Preserve the existing predicates and their order, not a new fixed radius, a requirement for a loaded zone before drawing, or a blanket owner check.

### 7.1 Code-derived contract at the pinned baseline

The following facts were re-read in `WinterSnow/SeasonalSnow.cs` and the pinned `ZNetScene.cs`. They describe current code; the new arithmetic elsewhere in this plan is a separate approved change. [S1], [G6]

| Current method/path | Actual gate/order | Required preservation |
| --- | --- | --- |
| `TryInitializeSeasonalSnowOnStart` / `WearNTear.Start` | Applies mesh settings, prepares a local roof-probe origin, and queues initialization. | Keep instance discovery independent of acquiring ownership. Local collider-origin preparation is not evidence that the surrounding world is ready. |
| `ProcessPendingSnowInitializations` | Calls `UpdatePendingLocalSnowVisual` **before** testing `IsAreaReady`; shares readiness results by `ZoneSystem.GetZone` during the pass. Invoked after `ZNetScene.CreateDestroyObjects`. | A missing/not-ready zone cannot block all cap display. Preserve the retry-after-scene-creation opportunity while replacing the full pending `ToArray()` scan with regional jobs. |
| `UpdatePendingLocalSnowVisual` | Client-only provisional path; valid ZDO, winter/config/position gates; `CanInitializeSnowState`; no current-winter marker and no existing positive runtime/persisted snow. Shield revision can refresh it. | Keep a read-only passive timeline preview for qualifying ownerless/local-owner distant pieces. Do not overwrite a valid zero/current-winter snapshot or another owner's state with a new discovery minimum. |
| `ApplyLocalSnowVisual` / `TryGetVisualSnowOverride` | Stores `localVisualApplied` and `localVisualSnow`; the old visual prefix temporarily supplies the value and restores `m_snowBuildup` in its finalizer. | Preserve the **separation** of provisional visual and stored/real state, not the temporary native-field rendering technique. Render the estimate directly with the new pool. |
| `CanInitializeSnowState` | `owner == 0` **or** local ownership; does not require active-area membership. | Keep ownerless ready initialization. Never claim ownership to display snow. |
| `TryInitializeLoadedSnow` / `TryInitializeCurrentWinterSnow` | Waits for season data and `IsAreaReady`, invalidates environment caches, then resolves real state; there is no blanket active-area gate in this initialization. | Inspect cover/heat after readiness without waiting for proximity ownership. Preserve the writer/readiness branches, replacing only the specifically revised seed/melt arithmetic. |
| `SynchronizeSnowAuthority` / `ApplyPersistedSnowVisual` | Observes owner changes, requests reconciliation, and returns to persisted state for an actual other owner. | Do not treat all non-owners as ownerless, erase a cap on owner changes, or publish over another owner. Translate persistence to the new key. |
| `UpdateActiveAreaState` | Uses `!OutsideActiveArea(position)`, records entry/exit, invalidates caches, and requests reconciliation. | Active-area entry is another transition, not the first point at which caps can exist or cover can be checked. |
| `TryReconcileOwnedSnow` / live heat and interactive paths | Reconciliation requires readiness and local ownership; current continuous heat/use callers have additional activity checks. | Preserve the scope distinctions. The previously requested ready-area local static-heat evaluation is explicit, not a claim that the old owner-side loop already runs everywhere. Keep interactive/write gates separate. |

Relevant baseline ranges: provisional/pending handling `432..609`; ready initialization `667..801`; authority/activity `803..879`; override/owner checks `896..947`; custom cover geometry `1546..1673`; owner reconciliation `1706..1793`; start/live heat/placement `1883..1980`; creation-pass hook `2540..2548`; visual override restoration `2730..2754`. Line numbers are navigation aids for the pinned revision, not future patch anchors.

Native `ZNetScene.IsAreaReady(point)` first requires the zone to be loaded, then inspects sector objects using `SimulationDistance(1, 0)` and requires instances for valid prefab ZDOs. It is not a cheap distance predicate. `OutsideActiveArea` is separate and uses the reference-position zone and synced simulation-distance policy. Reuse those rules; do not replace them with `owner != 0`, `IsZoneLoaded` alone, a single fixed meter cutoff, or an assumed LOD distance. The maintainer's established distant-LOD setup is a regression target, not authorization to change draw distance. [G6]

### 7.2 Working progression, without coupling the axes

| Scene/evaluation situation | Cap behavior | What is not allowed |
| --- | --- | --- |
| A distant piece instance exists, but its zone/area is not ready | Keep existing persisted appearance, or the qualifying read-only timeline preview from the baseline's fallback. It may be visible with `owner == 0` and an unloaded zone. | Do not hide it merely because readiness/active area/owner is absent. Do not commit the provisional value, missing roofs, or missing heaters as resolved facts. |
| Geometry becomes ready, before the player enters active area | Refresh the affected region, run the existing custom cover/heat classification, select real initialized/saved state, and correct provisional caps. Ready ownerless initialization remains possible. | Do not wait for owner acquisition to inspect geometry. Do not turn the erroneous part of a provisional cap into persistent snow. |
| The player enters the existing active area | Reconcile under the existing readiness/authority conditions and enable the applicable live-use/write paths. Keep the already resolved appearance. | No second minimum seed, double timeline credit, temporary zero cap, or restart from a stale snapshot just because activity changed. |
| Player leaves, ownership changes, or readiness is lost while an instance remains | Retain the valid local/persisted appearance and use the appropriate fallback/retry. Cancel only processes whose required scope was lost. | Leaving active area must not remove the renderer state. A pending state does not lose its bucket or become a new piece. |

These are independent facts, not a mandatory linear enum: a ready piece may still have no owner, another owner may exist before local readiness, and activity/authority can change separately. Cache evaluation/provenance flags alongside the single bucket membership. A provisional value can occupy a logical bucket for display bookkeeping, but must not be integrated/published as authoritative geometry-aware snow. Unknown heat is not confirmed absence of heat.

**Provisional correction versus roof melting:** if readiness reveals that the provisional snow was on an already covered or shielded surface, replace that estimate with the correct real value, normally zero for a first-discovered covered piece. Do not slowly melt nonexistent confirmed snow. Conversely, if real snow was already initialized and a roof is later built, move the piece into melting mode and use the configured gradual cover rate. An existing saved snowy piece also keeps its real value rather than being confused with a provisional preview.

### 7.3 Regional retries and practical cost

Keep region-filtered state refreshes separate from the four buckets. Registration, scene creation/removal, zone readiness changes, cover/source events, and activity/reference-zone changes enqueue affected regions. Recheck `IsAreaReady` once per relevant sector/pass after scene changes, not once per snow piece and not in either arithmetic loop. A pending unloaded region must not hot-loop through all its pieces every frame.

Do not cache false readiness forever: neighbor instantiation can make it true even without a new snow piece, and a previously ready region can become incomplete. Preserve the post-`CreateDestroyObjects` retry opportunity and update the readiness revision before settling geometry. Use exact per-position active-area checks for boundary pieces when a policy boundary cuts through a sector; sector grouping is a work filter, not permission to round the game's activation shape.

Do not ban physics outside active area. The baseline deliberately performs geometry checks once readiness allows them before active-area entry; keep that capability and the custom origin/cast algorithm. Avoid repeated expensive queries by invalidation, not by reducing the proven distance at which they work.

Changing the evaluation stage does not add a second four-list system or a fifth simulation list. The same state keeps its cap bindings, snapshot identity, weather cursor, and bucket. Queued stage refreshes control which numeric/visual/publication operations are valid.

### 7.4 Authority and receive behavior

Ownerless display and ownerless initialization are not authorization to overwrite another peer's owned ZDO. Preserve the baseline's restricted ownerless initialization/reconciliation separately from ordinary current-owner publication. An ownerless local mutation is not proof of server delivery. Neither geometry-ready local static heat nor visual application may be gated by `CanOwnSnowState` alone.

For ready unowned pieces, the previously selected local ambient/static-heat calculation may use resolved geometry without an ownership-radius gate. Keep that local working value separate from a persistable snapshot. When the same process gains legitimate ownership, retain compatible already-calculated state instead of replaying its interval. Another owner's valid newer snapshot can rebase presentation; repeated unchanged snow data or an unrelated `DataRevision` must not reset a locally progressing cap. Do not turn speculative preview from a not-ready area into a writable snapshot during this handoff.

Observe incoming snow/owner changes for tracked IDs and enqueue refresh reasons outside packet parsing. Idle pieces must be reachable by these triggers without a global per-frame ZDO scan. Headless owners calculate applicable state but never create material pools. Interactive use keeps its existing valid-player, active-area, readiness, and owner/RPC boundaries. [G3], [G4]

No extra chat export is required to establish these gates: the pinned baseline code is the reference. Preserve its scope and ordering explicitly before deleting old integration paths.

## 8. Heat registry and spatial links

Use a logical `Fireplace`/`Smelter` source when relevant heat areas belong to it. Cache the component once. Multiple child areas, including low/high fire variants, contribute at most the strongest applicable influence of that logical source to one piece. Distinct sources add. A `Smelter` implementation without a relevant heat area is not automatically a heater.

Observe `IsBurning()` / `IsActive()` and cached area/collider activity periodically or through existing periodic state callbacks. Do not read fuel history or add component lookups to the poll. An area can be enabled while its fireplace is not burning; a collider can be disabled without an `EffectArea.OnDisable` call. Register inactive links as topology, then update effective activity separately. Group generic sources without a fireplace/smelter by the existing logical source boundary.

Preserve center-distance falloff, the inside-volume exception, self-heating, and roof-over-leaky multiplier precedence. Cache geometry weights once. Primitive shape containment can be arithmetic after registration; uncommon shapes may need a one-time narrow geometry check. AABB is a broad-phase filter, not an exact rotated-box/mesh containment test.

Use game `SectorIndex` (64x64 meters) with local 8-meter heat cells. Save `ChunkIndex` aggregates sectors; it is not a fine heat grid. Handle negative coordinates and sector boundaries using the game's mapping. Heat-area footprint/radius handling remains necessary for neighbor lookup; the simplified nine-sector **roof invalidation** rule does not mean an arbitrarily large heater is indexed only at its center. [G5]

Exclude moving sources: carried items, characters, ships/carts, and mobile parent hierarchies. `isStatic == false` or an asleep/kinematic rigidbody alone is not a reliable classification. A relocated supported source invalidates old/new links. Cached identity and generation protect against destruction/reuse. Removal/duplicate callbacks cannot double-subtract influence or leave a negative aggregate.

Interaction activity remains a small separate source registry with its existing multiplier, single-object scope, and precedence over ordinary static-heat melting while a use is active. Its affected piece still belongs to exactly one of the four buckets. It does not multiply by number of users and, like other melting, receives no snowfall term. Cover melting is independent. Use/timeout changes enqueue affected state refreshes; do not delete station/attachment behavior as a side effect of excluding moving heat sources.

## 9. Cover algorithm and sector invalidation

Retain Seasons' highest-valid-collider surface probe, fallback origin, upward offset, cast radius/distance, layer mask, and self-hierarchy exclusion. It is not interchangeable with vanilla `HaveRoof`, especially for leaky geometry. `m_haveRoof` prefix/postfix comparison is an additional invalidation signal, not the cached result of our test. [S1], [G1]

For a changed object, mark its center sector and eight neighbors dirty. Coalesce revisions and pending sector work. Do not enumerate geometry or calculate world-space bounds merely to narrow the affected region. For relocation, dirty the nine sectors around both old and new centers. For known multi-sector terrain edits use the touched sector coordinates already provided by the operation.

This is intentionally conservative for ordinary pieces and intentionally incomplete for an arbitrarily huge modded collider reaching beyond the center-plus-neighbors region. Do not disguise that limit as exact coverage. A known adapter can explicitly invalidate more sector coordinates; the default path does not inspect every object's bounds.

Notifications must include relevant non-snow geometry: placement/removal, zone load/unload, door/gate state, health-visual collider changes, trees/rocks/destructibles, and terrain edits. Compare the old/new state so unchanged periodic callbacks do not keep nine sectors dirty. A moving or unknown collider changed by a different mod requires an explicit notification; arbitrary uninstrumented changes cannot be inferred from a revision table.

Area readiness and sector revisions protect against loading-order mistakes. If a revision changes while an inspection is pending, run again for the latest revision. Invalidate cached probe origins when the receiving piece's geometry changes. Avoid forcing `Physics.SyncTransforms` per piece. No stable static piece receives a periodic blanket roof recast.

After the queued inspection, update the cached covered flag/rate and move the same piece to its correct bucket at a safe list boundary. Real roof acquisition triggers gradual melting; ready-area correction of a provisional cap does not. Shelter and shield are separate inputs. No additional geometry is required until the next invalidation.

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
4. Add the singleton, sector-partitioned four-bucket membership, region-filtered state-refresh queue, direct arithmetic, and independent visual/geometry/publication work. Do not implement a percentage heap or net-rate scheduler.
5. Cache summed effective heat with existing source predicates, preserve interaction boundaries, and add covered-piece melting. Both causes select melting without any concurrent snowfall term.
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
| Two heaters vs one / low-high child areas | Independent heat contributions add once per logical source; duplicates do not. The piece is integrated once, without snowfall. |
| Add a weak but effective heater during snow | Piece changes from accumulation to melting at refresh; snowfall cannot reverse or slow the selected melt rate. |
| Same sources iterated in another order | Sum contributions then clamp once; no collection-order bias. |
| `MeltingEmpty` during a stronger snowstorm | No numeric pass and no accumulation. Only losing the melt cause or changing real state can move it out; source links remain. |
| Heat disappears from `MeltingEmpty` | Refresh moves it to `AccumulationOpen`; it stays zero in clear weather and grows gradually when snow falls. |
| Reach zero/maximum during arithmetic | Exactly one terminal value and one cheap deferred bucket move; no duplicate membership or integration. |
| Repeated trigger while refresh pending | One queued state with combined reason flags; no full-list reconstruction or redundant geometry check. |
| Roof added, removed midway, then added again | Gradual configured loss, no repeated raycasts, continuous remaining value, no reset to minimum. |
| Cover multiplier 0 / all-heat multiplier 0 / roof-tag multiplier 0 | Independent documented meanings; roof cover loss is not disabled by an unrelated heat setting. |
| Shield vs roof / end of winter | Shield and season cleanup remain hard suppression; only ordinary roof acquisition melts gradually. |
| Full 1.0 own buildup | Variant 100, no native heavy snow damage/support state and no new global damage patch. |
| Ready ownerless distant pieces with static heat | Snow is calculated and shown without approaching the ownership radius. |
| Distant existing instance, unloaded zone, owner 0 | Qualifying passive cap is visible before readiness, through the existing fallback gates. No forced ownership or publication of a guessed value. |
| Provisional covered cap, then area ready outside active area | Run custom geometry and remove the incorrect estimate promptly; do not keep it to melt slowly. |
| Real initialized cap, then a roof is built | Gradual cover melting; not the provisional-correction path. |
| Area not ready, neighbor instantiates, still outside active area | Regional readiness retry resolves state without waiting for another snow piece or ownership acquisition. |
| Owner changes before/after readiness | Treat ownership independently of the geometry/evaluation stage; preserve fallback and accepted-state precedence. |
| Active-area entry/exit and distant LOD transition | No missing caps, no new fixed distance cutoff, no second discovery minimum, and no double weather accounting; narrow interaction/write gates survive. |
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

The latest decision selects exclusive accumulation or melting and four bucket lists. Earlier simultaneous snowfall-minus-heat and repeated snowfall formulas are superseded, not optional modes. The covered-piece config default 2x remains selected; its per-second balance anchor remains to be checked against desired gameplay. State-refresh latency is separate from rendering latency, and provisional distant appearance remains deliberately separate from confirmed geometry-aware state.

No per-percentage heap, net-rate arbitration, generic event-history subsystem, fuel-history simulator, partial JSON merge, renderer ownership negotiation, whole-world migration sweep, LOD redesign, or distance culling is part of this plan. Geometry/render/network work remains bounded separately even when arithmetic is allowed to run promptly. A quiet numerical state does not mean Unity stops drawing visible snow meshes.

## 15. Source map

[S1]: https://github.com/shudnal/Seasons/blob/988e98c514ce49369a92cbaf934a7aca384fe6e1/WinterSnow/SeasonalSnow.cs
[S2]: https://github.com/shudnal/Seasons/blob/988e98c514ce49369a92cbaf934a7aca384fe6e1/Seasons.cs
[S3]: https://github.com/shudnal/Seasons/blob/988e98c514ce49369a92cbaf934a7aca384fe6e1/WinterSnow/SeasonalSnowMeshSettings.cs
[G1]: https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/WearNTear.cs
[G2]: https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/MaterialMan.cs
[G3]: https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/ZDO.cs
[G4]: https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/ZDOMan.cs
[G5]: https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/ZoneSystem.cs
[G6]: https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/ZNetScene.cs
[U1]: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Renderer.SetPropertyBlock.html
[U2]: https://docs.unity3d.com/2022.3/Documentation/Manual/GPUInstancing.html

- [S1] Existing timeline, initialization, `CanInitializeSnowState`, provisional display, readiness/active-area gates, cover, heat, interaction, and native snow patches. Its old minimum/heat/roof arithmetic is intentionally superseded where stated above.
- [S2] Existing config defaults and position eligibility; the new covered-piece multiplier and maximum adjustment are planned changes, not current source behavior.
- [S3] Existing donor, transform, Ignore/Disabled, and MaterialMan interactions to adapt.
- [G1] Native cap mapping, visual selection, snow storage, and heavy-snow inputs to isolate from own buildup.
- [G2] `PropertyContainer.RefreshRenderers` and `UpdateBlock` explain why clearing a cap block once without registration exclusion is insufficient.
- [G3], [G4] ZDO identity, typed data, deserialization, ownership, and synchronization boundaries.
- [G5] Sector coordinates and zone lifecycle.
- [G6] Exact `IsAreaReady`, `OutsideActiveArea`/`PointInsideActiveArea`, distant instance creation, and the scene-creation retry boundary.
- [U1] Removing per-renderer/per-material overrides; a retained zero is still an override.
- [U2] Instancing requirements and limits of material sharing as a performance claim.
