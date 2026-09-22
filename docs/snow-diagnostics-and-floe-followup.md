# Snow diagnostics, environment cleanup, and ice floes

Date: 2026-09-22. Branch: `perf/snow-performance`.
Status: accepted decisions; implementation progress and remaining verification are recorded in section 10.

This is the authoritative follow-up to [the initialization review](snow-performance-deep-review.md), [the previous follow-up](snow-performance-followup.md), and [the implementation history](snow-performance-progress.md) for the topics below. It supersedes earlier proposals to generate floes in unloaded distant zones, load temporary terrain for them, or log the weather timeline. Unrelated snow behavior and previously accepted optimizations remain unchanged.

## 1. Baseline and evidence

The remote implementation baseline checked for this decision record is `1c38ab1cba31f713edfe7335725abd0aabf4bac9`.

- `fb87d0f5df5217642449a540ff86b4ff4cfe13d2` explicitly initializes `ruleBiome` before the short-circuit condition. Preserve it: the rules path assigns the output biome or retires the piece, and the other path reuses `state.Biome`.
- `1c38ab1` adds `!source.Fireplace.m_wet` to the valid-view / `IsBurning()` heat condition. This is already committed, not outstanding work. Smelter still uses `IsActive()`.
- The maintainer reports successful gameplay checks of cap initialization at distance, nearby confirmation, heat melting, snowfall accumulation, summer removal, winter activation, skiptime, sleep, and season overrides. These observations are not an independent automated or multiplayer acceptance result.
- The maintainer also observed growth during apparently clear weather, with MyLittleUI predicting precipitation several hours later. Snow and SnowStorm particle systems are configured together with snow buildup; no other weather-changing mod was reported. The specific growth remains unexplained. Do not dismiss it as a UI classification mismatch or declare it correct merely because a timeline exists.
- Floe problems were reproduced in singleplayer: `Object fell out of world:ice1(Clone)`, nearby motion but stationary distant floes, and substantial loading spikes at large simulation distances. A dedicated-server-only explanation is insufficient.
- One supplied profiler capture shows a 64.898 ms maximum in `SeasonalIceFloes.ZoneSystem_Update_Floes.Postfix`, with the next two maxima around 0.127 and 0.090 ms. This identifies a large isolated service call, not a constant 65 ms per frame or a measured breakdown of terrain, placement, and physics costs.

### Confirmed source findings versus unresolved causes

| Finding | Evidence and limit |
| --- | --- |
| Timeline construction selects a registered environment separately for each absolute weather period and biome, then reads its `m_snowBuildup`. | `WinterSnow/SeasonalSnow.cs`: `RefreshWeatherTimeline`, `GetPredictedSnowGain`, `GetCumulativeSnowGainAt`. It does not seed all periods from the currently interpolated `EnvMan.m_currentEnv.m_snowBuildup`. |
| Season/day overrides can change the reconstructed winter start and the historical interval. | `SeasonState.GetStartOfCurrentSeason` and timeline start calculation. This is a possible source of catch-up, not proof of the reported sustained growth. |
| Environment cleanup runs in the warm-status `FixedUpdate` postfix. | `SeasonState/EnvManPatches.cs` and `SeasonState.ReleaseUnusedEnvironmentTextures`: `ToArray()` over generated textures plus repeated `m_environments.Any(...)`. Live textures remain in the collection, so the scan repeats in steady state. |
| Floe service limits one zone per step, but its `Process` can do a whole expensive placement operation. | `Controllers/SeasonalIceFloes.cs`: temporary `SpawnZone(..., Client, ...)`, `PlaceIceFloes`, and destruction of temporary objects. A delay between steps does not bound one step's cost. |
| Extra wave force accepts a missing-water sentinel as a real surface height. | `AddWaveForce` subtracts `Floating.GetLiquidLevel(position)` without checking its missing result, `-10000f`. This permits a large downward impulse. It is not yet proven to be the exact cause of the reported fall. |
| Missing liquid can also leave an owner body without buoyancy while gravity is restored by sync. | `Floating` and `ZSyncTransform.OwnerSync`. Full-body stability must be handled separately from rejecting an invalid force probe. |
| The old material distance is not the renderer's effective distance. | `Water.ApplySettings` writes `NearSimulationDistance * zoneSize` into a property block; Seasons reads `sharedMaterial` once. |
| `OutsideZones` is sector bookkeeping, not permission for distant physics. | `ZDO.SetSector` uses it to choose the previous sector, removes/adds sector membership, and updates the flag. Do not set it manually. |

## 2. Accepted change: inspectable weather period records

Replace the parallel `BiomeSnowTimeline.snowBuildup` and `cumulativeSnowGain` arrays with one array of value-type period records, for example `SnowPeriod[]`.

Keep the per-biome timeline dictionary and the separate environment lists used as prediction input. Only merge the two parallel period arrays; do not merge unrelated registries.

Each record contains:

| Field | Meaning |
| --- | --- |
| `EnvironmentName` (`string`) | Name of the environment actually selected while constructing this period. Store dry-period names too; use a clear missing marker such as `<none>` when selection returns null. |
| `SnowBuildup` (`float`) | The nonnegative environment buildup actually used by the existing calculation. |
| `CumulativeSnowGain` (`float`) | Potential accumulated weather gain from the winter timeline start through the end of this period, clipped to the winter end. |

The cumulative field excludes the initial cap minimum, per-piece heat, and per-piece maximum. Preserve partial first/last periods and the calculation for a time inside a period: previous record's cumulative value plus the current partial-period gain.

Add `ToString()` returning the stored name, buildup, and cumulative gain with invariant numeric formatting and enough precision to reveal small differences. Example format, with illustrative values only:

```text
Rain Winter | buildup=0.4 | cumulative=0.23712
Clear Winter | buildup=0 | cumulative=0.23712
```

Store the name and numbers when building the record. Do not retain an `EnvSetup` reference as the diagnostic source or rerun prediction from `ToString()`. The maintainer will inspect the collection in RuntimeUnityEditor (RUE). Do not add timeline dumps, periodic timeline logging, a new logging option, or a new persisted/networked timeline format.

This is a representation/diagnostics change, not a balance change. Preserve period seeds, weighted selection order, timeline boundaries, accumulation coefficients, partial-period arithmetic, catch-up behavior, and existing snapshot semantics. Update every reader of the old arrays. Do not add unconditional accumulation, adjust the rate, or automatically refill the minimum.

The diagnostic invariant is: with the same timeline, a flat cumulative gain, no unapplied history, and no snapshot/reinitialization transition, ordinary accumulation must not increase exact `Snow`. Distinguish a changing `Snow` from delayed application of an unchanged target visual. Investigate any contrary evidence, but report a newly proven gameplay discrepancy before changing its behavior outside this agreed task.

## 3. Accepted change: client-only diagnostic piece hover

Add `WinterSnow/SeasonalSnowDiagnostics.cs` (or an equally focused file) and bind the config in the existing central configuration method, not in the diagnostic class:

- Section: `Season - Winter snow`.
- Name: `Show snow diagnostics on hover`.
- Default: `false`.
- Description: `Shows technical seasonal snow runtime information while hovering a building piece.`
- Use the existing `AlwaysClientControlled` mechanism; this debug preference is not server-synchronized.

The attached reference is Azumatt-AzuHoverStats v1.1.11, `HoverTextPatches.HudUpdateCrosshairPatch`: its `Hud.UpdateCrosshair` postfix gets the hovered piece/object and appends to `m_hoverName.text`, respecting TextViewer. Reuse that integration idea only. Do not import its other statistics, UI construction, dependencies, or source dump. If the attachment is unavailable in the Codex worktree, this description supplies the intended behavior; verify the native Hud signature from game sources.

Append a clearly separated Seasons diagnostic block to the current hover. Preserve other mods' and vanilla text, do not append duplicates over successive frames, and handle absent HUD/player/piece/TextViewer safely. Show own snow stats, not a general object inspector.

Read already existing state through a narrow read-only controller method. Do not make internal dictionaries public just for this feature. Useful grouped fields are:

| Group | Diagnostic values |
| --- | --- |
| Identity/authority | Prefab, instance ID, ZDOID, owner/local-owner, cached biome, publication eligibility. |
| Runtime | Registered/valid/confirmed/simulates, bucket, exact `Snow`, minimum/maximum, saved/construction, refresh flags and queued states. |
| Cover/heat | Region readiness, ready/geometry generations, covered/roof/leaky/shielded, melting, heat/melt rates, interactive activity, independent heat-link count. |
| Weather | Period number and stored period record, timeline bounds, current environment and buildup, cumulative gain now, consumed `WeatherGain`/`WeatherTime`, catch-up interval or none, current world time and relevant override state. |
| Persistence/visual | Stored snapshot presence/value/epoch/from, native snow fields for comparison, visual state/binding existence, target/applied cap names and remapped levels, queue state. |

Keep the text compact with grouped lines and invariant numbers. Cache the currently hovered target and formatted diagnostic block; refresh immediately on target change and periodically while held (about 0.2 seconds is sufficient). Clear caches on target/world loss or disable. Ordinary per-frame hover assembly may still append the cached string; do not promise zero allocation while the feature is enabled.

When disabled, return at the config check before allocations, component lookup, ZDO access, or formatting. When enabled, do not call `RegisterSnow`, force readiness, perform roof casts/heat queries, bind materials, settle catch-up, publish snapshots, or otherwise change what is being diagnosed. A piece with no runtime must remain unregistered and show `Seasonal snow: not registered`, `WinterReady`, available saved/native state, and cap-field presence rather than initiating it.

## 4. Accepted change: environment texture lifecycle cleanup

Remove the unconditional `ReleaseUnusedEnvironmentTextures()` scan from `EnvMan_FixedUpdate_UpdateWarmStatus`. Keep the cold-state/overheat behavior of that patch intact.

Use lifecycle-driven retirement of owned generated textures rather than a continuous garbage-collector-like scan:

1. When seasonal environments are replaced/removed or control is released, collect only Seasons-owned retired texture candidates.
2. After the environment registry change is complete, identify still-live references in a single pass rather than scanning every environment separately for every texture.
3. Keep candidates referenced by current/previous/next environments or an active shader transition until that transition completes or is superseded. A cheap pending/transition check is acceptable; a recurring full scan over live textures is not.
4. Reevaluate pending candidates at relevant transition/registry lifecycle boundaries. Handle interrupted transitions and external environment rebuilds, not only normal completion.
5. On world shutdown, release remaining owned resources once safe and clear tracking. Do not destroy native, restored-default, or other mods' textures.

Reuse existing environment-control boundaries and game patches where possible; do not Harmony-patch this mod's own methods. No `Resources.UnloadUnusedAssets`, forced managed GC, or broad periodic resource sweep. Preserve safe aurora blending and environment restoration. Stable environments with no retired candidates require no collection iteration or temporary arrays for cleanup.

## 5. Accepted change: generation only on approach

**This is the latest explicit scope decision.** Generate new seasonal floes only as players approach and the required terrain/water becomes available through the game's normal loading. Remove temporary `SpawnZone` calls made solely for floes. Do not replace them with a new distant terrain-data sampler in this iteration.

Generation, rendering, movement, and cleanup have different eligibility:

- New generation waits for an ordinarily loaded near zone and usable placement data. Being generated as a world zone, being within a large distant radius, or having a distant ZDO is not enough.
- Do not proactively place floes in ghost-only/unloaded zones. Do not alter the game's own zone spawning. Normal generation callbacks may enqueue eligible work but must not force terrain loading for this subsystem.
- Already generated floes retain `ZNetView.m_distant` / `ZDO.SetDistant(true)` and can remain visible at distance. Do not delete or hide them merely because new generation is proximity-only.
- Cleanup of existing marked floes and zone markers must not require a WaterVolume or terrain load. Preserve the existing seasonal cleanup scope without adding a global world scan.

Keep the server as the only producer, including in singleplayer. In multiplayer, use the server's ordinarily loaded zones around any relevant player, not only a local camera and not a rule allowing every client to generate. Generation must not require stealing an existing object's ownership.

If terrain/water is unavailable, leave the zone uncompleted, drop/defer the generation work until an appropriate normal load/approach event, and avoid a busy retry queue over distant zones. Do not mark a skipped zone as successfully spawned. Existing spawn watermarks and marked floes remain the duplicate-prevention inputs; do not respawn deliberately removed floes on every approach.

Retain placement exclusions, water/depth/biome checks, scale/mass/health, world-edge handling, and climb interaction where applicable. Use available near-zone data; do not silently remove placement checks for speed.

Bound remaining work: coalesce repeated requests, prioritize newly eligible zones rather than repeatedly scanning completed inner rings, and avoid synchronous whole-zone placement in callbacks. Separate discovery/placement/cleanup from movement. Limit repeated candidates, instantiations, and removals per frame, with an elapsed-time scheduling guard. Preserve the per-zone random sequence across resumed work and restore Unity's random state before yielding/returning. Handle a zone unloading, season changing, or partial generation being cancelled without duplicates or prematurely completed markers. Do not hold shared vanilla temporary lists across frames.

An elapsed-time guard cannot interrupt a single Unity call. Reducing frequency alone is not an acceptable substitute for removing avoidable monolithic work. The exact numeric budgets are implementation constants to document, not a new gameplay configuration family or an asserted FPS result.

The maintainer will evaluate whether proximity-only generation is visually sufficient. Distant generation without loaded terrain is deliberately deferred; do not implement it preemptively.

## 6. Accepted change: safe and bounded floe motion

### Invalid water probes

A missing water sample must contribute **zero additional impulse**. Do not replace a missing absolute surface height with `0f` and then subtract it from the probe's world Y; that still creates an artificial downward force. Skip the extra force for that invalid probe, equivalently use a zero depth delta for that probe only. Preserve legitimate upward and downward forces from valid samples. Do not change the global meaning of `Floating.GetLiquidLevel` for other objects.

Validate all extra force probes independently; `HaveLiquidLevel()` for the center does not prove that every edge probe has water. Also protect a whole owner body that loses liquid availability from free fall. Do not suppress the out-of-world log as a substitute for fixing the state.

### Distance source

Stop reading `_VisibleMaxDistance` from the water material. Use exactly the `Water.ApplySettings` calculation:

```csharp
(float)ZNet.instance.GetSyncedSimulationDistance().NearSimulationDistance
    * ZoneSystem.instance.m_zoneSize
```

Cache the distance and its square centrally. Refresh on world initialization and synchronized simulation-distance changes, using the existing Water settings lifecycle where appropriate. Do not read materials/property blocks or recalculate settings per floe per tick. Do not substitute `TotalSimulationDistance` or the old constant material value. Preserve unrelated `_WaterEdge` handling.

This distance aligns the motion policy with the game's effective water setting; it does not make absent water valid and must not blindly expand expensive physics to every distant instance.

### Runtime modes

Retire the obsolete pre-release distance/sync workaround and implement coherent modes:

- Near physical mode: valid water and appropriate gameplay proximity; authoritative Floating/Rigidbody physics, normal collisions and climb interaction, safe extra wave forces, normal required synchronization.
- Remote stable mode: authoritative root/collision remain stable at a valid baseline, with no unsupported gravity-driven fall or repeated cosmetic transform publications. Ownership is not claimed for convenience.
- Remote visual motion: on clients only, cached visual transforms bob/tilt using compatible water time/wind/wave sampling without requiring a WaterVolume trigger at every distant point. Root, colliders, ZDO position/rotation/velocities are not animated by the cosmetic layer. No visual work on dedicated servers or outside the relevant visible range.

Use the corrected water distance, actual liquid availability, and gameplay relevance as separate inputs. Preserve smooth entry/exit and use hysteresis so the mode does not chatter on a boundary. Restore visible/collision alignment before interaction. Do not invent an additional configurable physics radius; keep the policy explicit in implementation and document it.

Cache references and original modified Rigidbody/ZSyncTransform/visual properties once. Restore modified settings and local visual transforms on mode exit, ownership changes, disable, unload, destruction, and world shutdown as appropriate. Reconcile the ordering of `ZSyncTransform` and `Floating`: disabling gravity once is insufficient if the next native sync restores it. Handle host owner, remote owner, owner zero, and a camera-less server. Do not rely on camera existence for stability or overwrite a newer owner's authoritative position with a local cosmetic offset.

Use a centralized bounded motion scheduler with staggered wave targets, distance-based refresh where useful, and smooth interpolation. Bound both sampling and transform work; replacing many callbacks with one unbounded full-list callback is not the objective. No steady-state component hierarchy scans, per-frame temporary arrays, blanket `Physics.SyncTransforms`, or per-frame cosmetic ZDO revisions. Keep changes scoped to Seasons-marked floes; leave native unrelated ice untouched. Never manually set `OutsideZones`.

## 7. Codex execution contract

Implement sections 2-6; preserve the already committed corrections in section 1. This document records requirements, not completed code.

1. Work in `shudnal/Seasons`, branch `perf/snow-performance`. The maintainer previously used `D:/work/codex/Seasons/.worktrees/snow-performance`; verify the actual worktree, HEAD, and `git status` first. Read applicable `AGENTS.md`. Preserve local and newer remote work. No reset, clean, forced push, history rewrite, or automatic overwrite of conflicts.
2. Read this document before older plans. In particular, do not resurrect temporary terrain, distant generation, separate timeline logging, or an assumption that the observed clear-weather growth is already diagnosed.
3. Read game sources first from `shudnal/assemblies_combined`. The source baseline used in the analysis is `d1374bfd9175ac8f733ae483b0a06e5c8b75906e` (1.0.15 in that analysis). Verify Harmony targets and the revision matching the current project; do not guess API signatures.
4. Do not build the mod, run Valheim, or execute tests. Static source inspection, diff/XML checks, call-path review, and manual arithmetic reasoning are allowed. The maintainer compiles and tests in-game.
5. All repository content, technical comments, config descriptions, and commit messages are English. Check accidental Cyrillic outside intentional localization. Final report to the maintainer is Russian.
6. Make small logically complete commits. Do not open a PR. Do not modify `master` or `feat/blood-moon`, bump the version, change packaging/dependencies/publication automation, alter snow JSON or ZDO schemas, or change accumulation/melting balance.
7. Preserve four-bucket snow simulation, cold/heat gameplay, custom roof casts, native Deep North, saved zero, master-format import, ownerless distant caps, frame budgets, material pools, child/LOD caps, and 0.01 visual thresholds. No second producer or new per-WNT MonoBehaviour.
8. Include new source files in the project and remove replaced floe paths rather than leaving duplicate producers/updaters. Update implementation status and verification notes in the existing documentation after each completed work item.
9. If new evidence requires a gameplay decision outside these boundaries, report it without silently broadening scope. Do not stop at another plan for the work already agreed here.

Suggested commits: inspectable timeline records; read-only snow hover; lifecycle environment texture retirement; proximity-only budgeted floe generation; safe floe physics/distance transitions; bounded distant cosmetic motion; final status documentation. Combine tightly coupled safety changes rather than leaving an unsafe intermediate runtime, and do not create empty commits for `m_wet` or `ruleBiome`.

## 8. Acceptance and owner verification

Static completion checks: all old timeline-array readers converted; no numerical rule changes; no timeline logging; hover disabled path trivial and enabled path read-only; no unconditional environment cleanup scan; no subsystem-forced temporary `SpawnZone`; no invalid sample force; no manual `OutsideZones`; corrected distance invalidation; restored runtime fields; no cosmetic ZDO writes; new files included; no duplicate patch paths or accidental Cyrillic. Inspect all changed call paths without claiming compilation or gameplay acceptance.

Owner gameplay checks:

| Scenario | Expected result |
| --- | --- |
| RUE timeline, including dry periods and partial boundaries | Each entry exposes its selected name and both numbers through `ToString`; period math matches the prior behavior. No additional timeline logs. |
| Reported clear-weather growth, same piece before/after | Timeline records, exact snow, consumed gain, snapshots, and applied visual level distinguish new weather gain from catch-up/reinitialization/visual delay. The cause is documented only if demonstrated. |
| Hover off/on, target changes, no runtime, summer | No diagnostic work when off; no text accumulation or side-effect registration when on; normal hover remains usable. |
| Wet/dry Fireplace transitions | Existing `!m_wet` heat behavior remains; Smelter and independent-source summation are unchanged. |
| Environment reload/disable and repeated/interrupted transitions | No missing aurora textures, premature destruction, or unbounded retired-texture growth; no steady-state scan in warm-status updates. |
| Singleplayer approach at default and maximum distances | New floes wait for normal nearby loading; no temporary terrain peak; placement remains valid and duplicate-free. Assess whether this generation distance is sufficient. |
| Existing floes after retreat or season change | Distant visibility and bounded cosmetic motion work without falling; cleanup does not require loading terrain. |
| Water trigger boundary, camera movement, setting changes, missing probes | Invalid probes add no force, no out-of-world fall, smooth mode transitions, correct live distance updates. |
| Host/client/dedicated, owner zero and ownership transfer | One producer, stable remote bodies, intact nearby interaction, and no cosmetic position/velocity revision stream. |
| Winter/summer cap regression checks | Previously working cap behavior and initialization/idle performance remain intact. |

Codex's final report must separate implemented changes, static checks, user-reported prior observations, and unexecuted gameplay checks. Include starting HEAD/dirty-state handling, commit IDs, modified files, remaining uncertainty about clear-weather growth, chosen scheduling constants, and any out-of-scope findings. No invented FPS, memory, traffic, or build results.

## 9. Source anchors

Repository findings above refer to the implementation at `1c38ab1` unless noted:

- [Snow timeline](https://github.com/shudnal/Seasons/blob/1c38ab1cba31f713edfe7335725abd0aabf4bac9/WinterSnow/SeasonalSnow.cs) and [simulation](https://github.com/shudnal/Seasons/blob/1c38ab1cba31f713edfe7335725abd0aabf4bac9/WinterSnow/SeasonalSnowSimulation.cs).
- [Environment patches](https://github.com/shudnal/Seasons/blob/1c38ab1cba31f713edfe7335725abd0aabf4bac9/SeasonState/EnvManPatches.cs) and [texture lifecycle](https://github.com/shudnal/Seasons/blob/1c38ab1cba31f713edfe7335725abd0aabf4bac9/SeasonState/SeasonState.cs).
- [Floe service](https://github.com/shudnal/Seasons/blob/1c38ab1cba31f713edfe7335725abd0aabf4bac9/Controllers/SeasonalIceFloes.cs) and [old floe motion/distance path](https://github.com/shudnal/Seasons/blob/1c38ab1cba31f713edfe7335725abd0aabf4bac9/Controllers/ZoneSystemVariantController.cs).
- Game sources: [Water](https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/Water.cs), [Floating](https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/Floating.cs), [ZSyncTransform](https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/ZSyncTransform.cs), [ZDO](https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/ZDO.cs), and [EnvMan](https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/EnvMan.cs).
- Attached Azumatt-AzuHoverStats v1.1.11 decompilation: `HoverTextPatches.HudUpdateCrosshairPatch`, lines 293-312 in the supplied consolidated file. This is a reference attachment, not a repository dependency.

Screenshots and gameplay reports are supplied conversation evidence, not repository benchmark artifacts. No runtime source changes accompany this decision-record commit.

## 10. Implementation status

Implementation started from a clean `perf/snow-performance` worktree at `2ffa4ad7eea1c083711c4eb538285b0d97cd23b0`, fast-forwarded to the requested documentation commit `8229b43146071eb8589731a9058d2eef9a0f31d9`. No local work was discarded. The `master` and `feat/blood-moon` branches are unchanged. No applicable `AGENTS.md` was found. The clean game-source mirror is at the specified `d1374bfd9175ac8f733ae483b0a06e5c8b75906e` revision, also recorded by the existing project implementation notes; API checks use that source rather than a newer guessed signature.

### Completed: inspectable timeline records (section 2)

`WinterSnow/SeasonalSnow.cs` now stores one array of readonly `SnowPeriod` values per biome. Each period stores the selected environment name (including dry periods, or `<none>`), nonnegative buildup, and cumulative gain. `ToString()` uses invariant `G9` float formatting for RUE. All former parallel-array readers use these records. The random seed, selection order, winter boundaries, arithmetic and snapshot behavior are unchanged; no timeline logging was added.

Static review compared every changed arithmetic expression and reader with the baseline. RUE inspection and the clear-weather gameplay investigation remain unexecuted. A flat cumulative record alone does not prove the cause of the reported growth: consumed history, initialization/snapshots, exact snow and delayed visuals still need to be compared on the same piece. No balance change is included.

### Completed: read-only snow hover (section 3)

`WinterSnow/SeasonalSnowDiagnostics.cs` appends a marker-delimited diagnostic block from the native `Hud.UpdateCrosshair(Player, float)` postfix. The existing central `clientConfig` binding uses `AlwaysClientControlled`, the specified text, and a default of false. The disabled path returns before HUD, component, ZDO or formatting work. The current target/block are cached for 0.2 unscaled seconds and cleared on config changes, world reset or target loss. Marker removal also handles text retained by another HUD patch across cache resets.

The narrow controller reader reports existing runtime/region/heat/weather/snapshot/visual caches and native save fields. Unregistered pieces remain unregistered. Static call-path review found no registration, readiness/cast, material binding, simulation or publication call; native signatures and fields were checked against the source baseline. Project inclusion and XML were inspected. Hover layout, coexistence with HUD mods, summer/unregistered targets and the reported clear-weather growth still require gameplay checks.

### Completed: environment texture retirement (section 4)

`SeasonState/SeasonState.cs` and `SeasonState/EnvManPatches.cs` retire only tracked Seasons-generated textures at registry replacement/restoration boundaries. Nested native initialization/append calls are batched; the completed registry is traversed once to identify live references. Pending candidates retain current/previous/next environments and active aurora shader references. Native queue/interpolation boundaries compare cached texture identities and do no collection work when nothing retired is pending or the references are unchanged. Shutdown unbinds only owned shader textures and clears owned resources/tracking.

The old texture scan is removed from the warm-status `FixedUpdate`; its cold/overheat behavior is unchanged. Static inspection covered native `AppendEnvironment`, `InitializeEnvironment`, `QueueEnvironment(EnvSetup)`, `InterpolateEnvironment(float)` and `OnDestroy`, including interrupted transition shader ordering, external rebuilds and restoration. Repeated/interrupted transitions, control disable and external environment reloads still need in-game aurora/resource checks.

### Completed: incremental approach-only generation (section 5)

`Controllers/SeasonalIceFloes.cs` now queues normal full-load/approach events and resumes individual placement candidates using private per-zone RNG state and exclusions. It requires `IsZoneLoaded`, a near-player scope, ground data and finite water before placement. No floe path calls `SpawnZone`, creates ghost terrain or samples unloaded terrain. Every yield/return restores Unity's RNG; unavailable data retains the unconsumed candidate without a busy distant retry. Native full-load exclusion lists are copied rather than held across frames.

Existing markers and marked floes suppress duplicates; only a finished candidate sequence writes a completion marker. Unloading retains partial work for a subsequent normal event, and seasonal cancellation queues its zone for cleanup. Native placement exclusions, altitude/biome/area/depth, scale, health, mass, world edge and climb behavior are retained. Marked restored floes retain distant flags through `ZNetView.Awake`; unrelated native ice is not made distant. Prefab/climb initialization uses the native zone lifecycle on every peer, independently of graphics texture-controller initialization.

Cleanup has its own queues and does not depend on terrain, water or placement readiness. The existing world cleanup pass supplies its existing scope and now queues floe removals/marker resets; no new global world scan was added. Constants per rendered frame: 8 discovery, request and cleanup-zone advances per respective phase; 64 inspected ZDOs shared by placement/cleanup; 4 candidates; 1 instantiation; 4 removals; 4 marker resets; 1.5 ms service guard. Peer scopes refresh every 0.5 seconds. These are upper scheduling budgets, not measured frame-time results; one native object lookup/raycast/Instantiate/Destroy call cannot be preempted. Newly approached rings are discovered first after movement, normal load events take priority, and settled zones skip repeated placement work.

Static review covered callback signatures, state/RNG restoration, missing-data/unload/cancellation, duplicate prevention, negative spawn-count behavior and queue fairness. **Source-confirmed multiplayer limitation:** `ZoneSystem.Update` normally loads terrain around the server reference; remote peers are passed to `CreateGhostZones`. The service considers every ready player's scope but still requires a zone actually loaded on the server. Dedicated/remote-only approaches can therefore have no new floes. This implementation deliberately does not force server terrain loads, use ghost-only placement, delegate production to clients or broaden the agreed scope. The maintainer must evaluate this limitation and the visual sufficiency of approach-only generation.

### Remaining implementation and verification

Section 6 is in progress. No mod build, tests or Valheim run has been performed. The maintainer's previously reported observations in section 1 remain separate from acceptance of these changes.
