# Snow performance implementation progress

Design reference: [snow-performance.md](snow-performance.md).
Implementation baseline inspected: `af8765f446a0a100db110dc43d9259d4631f18d3` on `perf/snow-performance`.
Updated: 2026-09-22. This describes source integration and static review; build and gameplay acceptance remain with the maintainer.

## Current integration

The new simulation is included in `Seasons.csproj` and connected to game callbacks. All seven prepared files now have Compile entries: `SeasonalSnowState`, `SeasonalSnowRuntime`, `SeasonalSnowSimulation`, `SeasonalSnowHeat`, `SeasonalSnowInteraction`, `SeasonalSnowGeometry`, and `SeasonalSnowSimulationPatches`.

The ordinary `SeasonalSnowController` singleton owns both the simulation and the existing visual layer. The `ZNetScene.Update` postfix runs simulation before visual application. `WearNTear.Awake`/Start/OnPlaced register or initialize pieces; destruction, ZNetView reset, scene shutdown, and zone teardown flush or retire them. Save preparation flushes coherent owned snapshots while the season clock is available. No simulation component is added to a piece or heater.

`SeasonalSnow.cs` now provides eligibility, configuration, and deterministic per-biome weather history. Its old wear-driven arithmetic, heat lookup, interaction calculations, and provisional native-field adapters are removed. The transitional native-key storage transpiler is removed too. Seasonal visuals read the singleton's working state. Native snow fields remain neutral for managed seasonal pieces; ordinary Deep North snow retains the native path. Disabled caps retain explicit suppression, including in Deep North.

## Implementation history

| Commit or slice | Scope |
| --- | --- |
| `69d848c`, `4a65dde`, `ff9f85d`, `3d3a21a` | Material pools, complete cap ownership, coalesced visual work, and cleanup. |
| `4bb9775`, `bf1880f`, `376d000` | Wet-visual alias protection, pause handling, and recorded maintainer visual feedback. |
| `605ef4c` | Separate seasonal stat-modifier fix for Ashlands and Deep North. |
| `3a9bddd`, `fad274c` | Private persistence and saved-first confirmation groundwork. |
| `5b83bd8` | Complete child/LOD renderer groups. |
| `889b596`, `ac09c19` | Server-side floe generation and distant streaming groundwork. |
| Prepared runtime through `af8765f` | Unconnected simulation files audited as unfinished implementation, despite earlier completion-oriented commit titles. |
| `97f3f24` | Harden server floe readiness, location clear areas, retry handling, and remote-owned zone marker cleanup. |
| `9d94630` | Include and wire the prepared runtime, correct lifecycle/authority/weather handling, remove the old producer, and connect covered-piece melting. |

No version bump or packaging change is part of this integration. The ordinary snow default becomes `0.51..1.0`; reduced defaults remain `0.3..0.6`. Explicit stored config values remain user choices.

## Simulation and work boundaries

Each registered seasonal piece has one of four buckets: `AccumulationFull`, `AccumulationOpen`, `MeltingEmpty`, or `MeltingSnow`. Regions hold dense per-biome groups with stored indices and swap removal. Clear biomes skip their accumulation groups; full and empty bounds sleep until a relevant transition. Cached per-biome weather gain feeds arithmetic independently of the visual budget.

Effective static heat, active interaction, or shelter selects melting. That branch subtracts the cached melt rate and receives no snowfall term. Distinct logical sources add; multiple child heat areas of one fireplace/smelter/source contribute its strongest applicable influence once. Interaction replaces static heat while active; cover melting remains additive and independent.

The server-controlled setting `Snow melt speed multiplier - covered pieces` defaults to `2`. The selected reference rate remains `0.0018` buildup units per active second at 1x; the covered default therefore contributes `0.0036` per second. The interaction reference is `0.002`. These are implementation coefficients requiring balance acceptance, not measured equivalents of the old frame-dependent behavior.

Arithmetic, cheap refreshes, expensive geometry/source topology, visual application, and ZDO publication have separate work paths. Piece geometry is limited to 20 inspections per frame with a 1 ms elapsed-time guard; source topology has a separate four-job/0.5 ms guard. A single inspection cannot be interrupted, so these guards are scheduling targets rather than hard frame-time guarantees. Visual application still changes at most 50 pieces per frame, coalescing to the latest value; all cached cap renderers for one piece move together. Ordinary publication uses an independent 0.01 buildup change gate and a separate queue of up to 50 snapshots per frame, with exact terminal/confirmation snapshots. Neither threshold resets the precise working value after sub-threshold deltas. Pure rate configuration changes use cheap refreshes; link weights or source distance changes request the necessary topology work.

The custom highest-collider roof probe and upward sphere cast remain the cover rule. Geometry notifications invalidate the center sector and eight neighbors, including placement/removal, scene objects, terrain, doors, health-visual changes, and tree/rock/destructible lifecycle. Heat activity wakes linked pieces without forcing roof casts. Newly sheltered real snow melts gradually; a first-discovery provisional cap disproved by existing shelter is corrected directly.

The verified material pools, native 0.25 visibility threshold/remap, 0.01 material step, child/LOD support, MaterialMan exclusion, and `m_wet` alias protection remain in place. `PrefabVariantController` does not drive cap rendering.

## Initialization, persistence, and authority

At the first valid registration, an applicable private or recognized legacy snapshot takes precedence over prediction, including explicit zero. Without such a snapshot, the existing qualifying local-owner/ownerless distant preview remains available before area readiness. Ready-area confirmation computes placement, saved history, cover/shield, and initial mode before choosing one final visual target. A foreign owner is never claimed or overwritten.

An existing newly discovered exposed piece receives the minimum plus winter timeline snowfall once. New construction records placement and starts clean; later gain begins at placement. A current saved zero is never interpreted as missing state, and raising the maximum permits future growth without adding snow.

The private float `Seasons_SeasonalSnowValue` and winter epoch describe the saved level. From/baseline fields describe the weather interval already accounted for. `Seasons_SeasonalSnowPlacedEpoch` and `Seasons_SeasonalSnowPlacedAt` retain construction identity across unload and ordinary snow cleanup, applying only to that winter.

Active melting accounts for excluded snowfall even when it reaches zero. Mode changes and controlled deactivation settle dormant cursors without per-frame writes to sleeping pieces. Reactivation adds genuine unprocessed weather once, using current shelter to exclude unseen accumulation. It does not reconstruct historical fuel, roof changes, interaction, or melting.

Area readiness, local calculation, active-area live use/publication, and visual presence remain separate. Ready ownerless ambient/static-heat calculation can continue locally; its initial permitted snapshot does not grant ongoing authority. Gaining legitimate ownership preserves compatible local work. Incoming changes to tracked snow/owner data enqueue refreshes; unchanged snow or unrelated ZDO revisions do not repeatedly restore stale levels.

Legacy migration is limited to loaded managed seasonal pieces carrying Seasons metadata. It prefers private data, retries native cleanup after interrupted migration, and never transfers ordinary Deep North snow or arbitrary unmarked foreign snow. Loading a marked legacy piece outside winter clears its stale seasonal appearance instead of reviving the native cap. End-of-winter or feature shutdown stops seasonal arithmetic and drains prioritized cap hides and cleanup. Epoch checks reject unloaded old-winter data.

## Server ice floes

Only the server generates seasonal floes. Both the water-volume entry and public placement helper reject independent client generation; owner zero is not client generation permission. A bounded server scan follows ready peer reference regions using the game's synced simulation-distance policy, and the native zone-control placement callback covers new zones.

Generation checks the zone marker and existing watermarked floe ZDOs before placing objects. Previously generated ghost sectors load temporary terrain in client terrain mode, preserving the native generation boundary. Persisted location clear areas are restored when no location-placement pass supplies them. Existing ocean/altitude/depth, world-edge, block/clearance, configured count, scale, and health rules remain in the placement helper.

Floe ZDOs carry distant visibility and initial scale through normal replication. The ZNetView Awake prefix preserves the distant flag when a replicated seasonal floe is instantiated. Cleanup removes watermarked floes and clears zone markers on the server, including remotely owned zone controls, so a later winter can generate again.

## Static verification and limits

Static inspection covers project XML/source inclusion, call reachability, old-producer removal, changed-file scope, syntax structure, and native Harmony targets/signatures against the available game-source mirror at `d1374bfd9175ac8f733ae483b0a06e5c8b75906e`. Roslyn C# 10 syntax parsing found no errors in the 76 project source files; this did not compile or emit the mod. The project has no missing or duplicate Compile entries. Diff whitespace checks passed, and the text-source scan found no Cyrillic outside localization/resources. Relevant native source paths include `WearNTear`, `ZNetScene`, `ZNetView`, `ZDO`, `ZDOMan`, `ZoneSystem`, `EffectArea`, interaction objects, and geometry lifecycle objects. Review findings were fixed in code instead of treating file presence as completion.

No mod build, automated tests, game execution, or profiling was performed. The maintainer observations in the historical appendices below concern the earlier visual layer; they are not acceptance of the newly connected simulation or floes.

Known boundaries remain: untouched legacy areas are not swept globally; an uninstrumented external collider/heat hierarchy edit needs an invalidation adapter; an oversized blocker beyond the nine-sector cover neighborhood needs explicit broader invalidation. Moving heat sources remain excluded. Unloaded catch-up deliberately omits historical heat and geometry. Source observation and queued refreshes have bounded latency. Material sharing and fewer updates do not establish a measured GPU/CPU or FPS improvement.

## Maintainer verification

Rebuild locally, restart all participating peers with this branch, and use a copy of the world. The unchanged package version alone does not distinguish this private snapshot format from earlier incremental builds.

| Scenario | Expected result |
| --- | --- |
| Saved positive and explicit-zero snapshots, then distant approach | Saved-first appearance; one direct confirmation; no intermediate hide/minimum and no readiness/owner cutoff for allowed distant caps. |
| Existing exposed versus already sheltered discovery; new winter construction | Exposed discovery seeds once; existing shelter corrects provisional snow; construction starts clean and gains only post-placement weather. |
| Snowfall with one/two heaters and duplicated heat areas | Only melting while heat is effective; independent sources add; one source's area variants do not multiply its influence. |
| Fire out during snowfall, roof added/removed, covered multiplier zero | Continuous growth below minimum; gradual sheltered melting; no weather gain under cover even at zero cover rate. |
| Crafting station, chair/bed attachment, multiple users, remote owner | Existing use scope and precedence; one interaction contribution; owner-directed activity and timeout. |
| Zero/full sleeping pieces, heater/roof/config changes, clear biome beside snowing biome | Correct wake-up with no accumulated weather debt or minimum reset; clear-biome accumulation groups remain idle. |
| Unload/reload, sleep/time skip, ownership transfer, unrelated incoming ZDO changes | Coherent float/time snapshots, weather catch-up once, no historical fuel replay or stale-snapshot reset. |
| Ownerless ready snow, active-area exit/reentry, remote snapshots | Local visual continuity, narrow publication/use gates, and coherent publication when authority permits. |
| Maximum decrease/increase, feature off, winter exit, later winter, Ignore/Disabled/Deep North | Clamp only downward; immediate seasonal shutdown; correct native handoff and continued native Deep North behavior. |
| Doors, destroyed roof/tree/rock, terrain edits, sector-boundary blockers | Custom cover rechecks affected neighbors and reacts to changed geometry. |
| Multiple cap renderers/LODs, normal/worn/broken, copied caps, wet roofs | Preserve established material/remap/LOD appearance and alias protection. |
| Save processed world, then remove Seasons; repeated world changes; dedicated host | No added native seasonal buildup, no retained world runtime, and no headless material allocation. |
| Dedicated/listen server, two clients, remote-owned and ownerless ocean zones | Server-only floe creation with no duplicate sets or client-generated competing ZDOs. |
| Existing/new ghost sectors, location clearance, distant floe approach, winter/config cleanup | Correct terrain/placement boundaries, replicated scale and visibility, removed floes, reset markers, and successful next-winter generation. |
| Original weak-PC scene in calm/snowfall/melting plus large geometry/visual backlogs | Profile arithmetic, geometry, source work, visual setters, MaterialMan, network, allocations, and GPU/shadow work separately; report measured results. |

## Roof wet-visual alias regression

The maintainer reported disappearing sloped-roof caps after enabling Winter with Season override. A `wood_roof_45` dump showed saved buildup `0.7144539`, a positive controller target, an applied snow material, empty property blocks, and an inactive cap object. A second dump confirmed `m_wet == m_snow.gameObject`, with no alias in the health-visual objects.

Native `WearNTear.UpdateWear` toggles `m_wet` from `m_rainWet` before its snow-visual refresh. The former unconditional snow drawing could undo that toggle. The new 0.01 gate correctly skips unchanged material levels but exposed this second writer of the same object's activity. This evidence identifies a visibility conflict, not a failed custom roof cast or an incorrect remap.

Commit `4bb9775` filters only `UpdateWear`'s `m_wet` field reads through the controller's cached cap bindings. A managed cap is not returned as a wet visual, so the conflicting `SetActive` call is skipped. The actual field, all ordinary wet effects, rain wear, and native behavior after release remain unchanged. This prevents the conflicting write instead of adding a repeated scan or reactivation loop. No snow amount, threshold, timeline, cover query, or initialization predicate changed in that fix.

The scene-update driver also defers ordinary visual queue processing while `Game.IsPaused()` or `Time.timeScale <= 0`. Pending targets remain coalesced and resume without replaying intermediate levels. Explicit scene teardown and visual release still run; this is not a freeze of network state, configuration changes, or every vanilla callback.

## Maintainer follow-up: visual parity and frame stability

Reported on 2026-09-21 after the wet-visual fix and pause guard in `bf1880f`. These are maintainer-run observations, not assistant-executed tests.

The maintainer confirmed that caps no longer flicker in the previously reported scenario. A subsequent concern that minimum caps were approximately 30-40% lower was not reproduced when comparing the branch with the master build on winter day 1, with default settings and no snowfall. In that comparison the new visual matched the old visual.

The REPL height probe reported:

```text
buildup=0.51; materialLevel=0.34; exactLevel=0.3466667
```

Applying the exact unquantized value produced only a barely noticeable visual difference. Do not compensate by raising the minimum, removing the native remap, changing cap transforms, or changing the 0.01 material step. The suspected large height regression is closed for the tested scenario. Prior snowfall before an earlier day-2 comparison is a possible explanation for the initial impression, not a verified weather-history finding.

The maintainer also reported noticeably steadier FPS with the new visuals, before the calculation refactor. Record this as qualitative frame-stability feedback only: no frame-time trace, FPS gain, low-percentile statistic, or CPU/GPU attribution was supplied. It does not establish that the original weak-PC 40-to-5 FPS report has been resolved.

These results resolve the reported flicker and minimum-height concerns for the rendering stage; they do not mark every lifecycle, multiplayer, distance, material, or configuration scenario in the acceptance matrix as tested. The storage/confirmation changes in `fad274c` still require their own game verification.

## Nested snow meshes and LOD groups

The maintainer reported that only the renderer directly referenced by `m_snow` visibly melted. The supplied `piece_throne02` hierarchy has a `floor_2x2_snow` child beneath `floor_2x2_snow (1)`. A cap root is therefore not necessarily a complete list of its snow geometry. The earlier prefab scan counted the three WearNTear renderer fields, not all descendant renderers.

Commit `5b83bd8` separates cap-root visibility from renderer material bindings. For each distinct `m_snow`, `m_snowWorn`, and `m_snowBroken` root, registration includes the directly referenced renderer and checks for an `LODGroup` on that same GameObject. When present, it collects renderer references from every LOD; otherwise it collects descendant renderers with `includeInactive: true`. It does not search the whole WearNTear object or a parent's LODGroup. References outside the cap hierarchy are ignored rather than taking ownership of unrelated geometry.

Only renderers with a `Valheim/Snow Mesh` material enter snow ownership. Non-snow renderers retain their property blocks and materials; mixed renderers retain their non-snow material slots. Each snow material slot keeps its own source-material pool, so a multi-part cap need not use one material throughout. Renderer references repeated across LODs or cap collections share one binding and are assigned only once per visual application.

Every renderer belonging to the selected cap receives the same visual percentage, including currently inactive LODs. Only the cap root's active state is toggled; child active states, `Renderer.enabled`, LOD selection, LOD distances, transforms, and bounds are not rewritten. Materials are assigned before revealing the root. Hidden variants return to their original materials. The same complete binding list is used for MaterialMan exclusion, property-block removal at registration, original-material restoration, native handoff, and world cleanup. The wet-visual alias guard also recognizes cached child snow renderer objects.

`TryGetComponent`, `GetLODs`, and `GetComponentsInChildren` are registration operations only. Ordinary snow updates consume cached references; no hierarchy scan or component lookup is added to the percentage-update path. Replacing a WearNTear cap reference or removing a copied cap uses the existing release/rebind path. Arbitrary in-place hierarchy/LOD-list edits after registration require re-registration; this change does not introduce polling for them.

The 0.01 visual gate, latest-target queue, and 50-piece-per-frame application limit remain unchanged. That limit counts pieces, not individual renderers; all cached parts of a processed piece are updated together. Simulation, persistence, heat, cover, configuration defaults, and the version are unchanged by this fix.

Maintainer checks: melt and regrow all parts of `piece_throne02`; move between LOD distances during partial melting on a multi-renderer cap; check zero snow, normal/worn/broken changes, Ignore/Disabled, copied-cap replacement, and world reentry. A child using a different snow material must retain its own material family while sharing the selected level. These are pending game checks, not executed tests. Static review covers discovery placement, deduplication, material ownership/cleanup, diff scope, and source encoding. No compilation, gameplay execution, or profiling was performed.
