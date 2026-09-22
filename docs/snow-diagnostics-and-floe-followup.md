# Snow and ice floes: corrective follow-up

Date: 2026-09-22. Branch: `perf/snow-performance`.
Implementation baseline reviewed: `396be7d9a3aa3fa01cbcc2a11cec46ce28f50346`.
Status: sections 3-8 implemented and statically reviewed; unexecuted gameplay acceptance is recorded in sections 10 and 12.

## 1. Authority, history, and scope

This document is the current implementation brief. It supersedes the earlier version of this document for the topics below, especially server-only floe generation, universal visual proxies, the one-zone floe physics radius, and the unresolved MyLittleUI forecast explanation. The previous implementation report remains available in Git history at `396be7d`. Earlier snow plans are background, not authority to reintroduce rejected architecture.

The maintainer will implement through Codex and perform compilation/gameplay checks locally. This planning change does not alter source code, defaults, saves, or runtime behavior.

Preserve the completed snow runtime: four regional buckets, custom cover detection, heat topology, saved zero, construction starting at zero, discovery seed, distant prediction followed by confirmation, owner-zero visibility, exact buildup arithmetic, pooled cap materials, child/LOD bindings, and bounded visual work. Preserve the compact existing ZDO snapshot and summer idle behavior. Do not return seasonal snow to native `WearNTear.m_snowBuildup`, `ZDOVars.s_snow`, or MaterialMan ownership.

Preserve these completed changes:

- `fb87d0f`: explicit `Heightmap.Biome ruleBiome = Heightmap.Biome.None` fixes definite assignment. The rules path assigns it or retires the piece; the other path reuses the cached biome.
- `1c38ab1`: a Fireplace supplies heat only with a valid view, `IsBurning()`, and `!m_wet`. Smelter continues to use `IsActive()`.
- `SnowPeriod[]` replaces parallel timeline arrays and stores environment name, buildup, and cumulative gain. Keep its inspectability and `ToString()`; do not add timeline logging.
- The client-only diagnostic hover is already implemented. Improve its presentation, not its ownership or read-only nature.
- Environment texture retirement is already lifecycle-driven. Do not restore the full texture scan in the warm-status `FixedUpdate` postfix.

## 2. Gameplay evidence and limits

### Snow acceptance observations

The maintainer reports that ordinary accumulation, clearing, heat melting, cover detection, distant cap prediction, nearby confirmation, season changes, sleep, skiptime, and season/day overrides behave predictably. A flight toward a base produced no conspicuous freezes. These are user-run checks, not assistant-run tests, and not yet the final test on the reporting user's problematic large base.

The apparent mismatch between ordinary snow accumulation and MyLittleUI's precipitation countdown has been resolved by the maintainer: the forecast was configured to recognize only SnowStorm. Do not reopen this as an unexplained spontaneous-snow bug or change natural accumulation. A separate explicit `env` override discrepancy is confirmed below.

### Floe regression

On `396be7d`, the maintainer reports floes remaining stationary even at close range, no visible physical simulation, and a disabled collider. The new mode controller freezes registered bodies before granting physical mode. The exact source of the reported collider disable is not established; do not state that `Freeze()` directly disables colliders without evidence.

The accepted response is a scoped rollback toward the original floe approach, not another redesign of the new proxy/motion framework. The original prefab is simple; distant motion only needs modest vertical bobbing, not precise tilt or a generalized renderer abstraction.

### Profiler observations

The ordinary loading capture and the synthetic skiptime capture must not be conflated. In the latter, `EnvMan_UpdateTriggers_SeasonStateUpdate.Postfix` has maxima 53.071 / 36.808 / 34.825 ms but p99 about 0.003 ms and p95 about 0.002 ms. `ZNetScene_Update_SnowVisuals` has a maximum around 1.083 ms in that capture. These are individual samples, not aggregate per-frame totals or a measured breakdown of the trigger's callees.

Source inspection identifies synchronous world maintenance under day/season notifications, particularly the whole-world ZDO snapshot/scan and eligible terrain decultivation. It does not establish that all 53 ms is roof computation or identify the exact share of each child operation.

## 3. Kiln self-heat: verified formula and one default change

### Reported case

A full `charcoal_kiln` and an attached beehive both start at buildup 0.6. During kiln operation, the kiln reaches zero while the hive reaches approximately 0.57. Reported melt rates are 0.018 and 0.0009 per active second, a ratio of 20.

### Source finding

`SeasonalSnowHeat.cs` currently computes:

```text
area weight = DistanceWeight(piece position, area position) * (self ? selfMultiplier : 1)
source contribution = maximum weight among that source's active areas
HeatRate = 0.0018 * globalHeatMultiplier * pieceTypeMultiplier * sum(source contributions)
```

`RebuildHeatLinks` applies `seasonalSnowSelfHeatMultiplier` once, only when `source.Piece == state.Piece`. `RecalculateHeat` does not apply it again. Areas belonging to the same logical source use a maximum; independent sources are summed. Interactive melting has a separate path and must not be conflated with kiln self-heat.

Defaults at the reviewed revision:

- Global heat multiplier: 1.
- Distance weights: 2 at 1 meter or less, linearly decreasing to 0.5 at the maximum check distance of 3 meters. A point inside an area can retain the far contribution beyond that center distance.
- Self multiplier: 5.
- Ordinary non-roof/non-leaky piece multiplier: 1.

The reported rates match these coefficients exactly:

```text
kiln: 0.0018 * 1 * 1 * 2 * 5 = 0.018
hive: 0.0018 * 1 * 1 * 0.5   = 0.0009
ratio: 5 * (2 / 0.5) = 20
```

At constant rates, removing 0.6 from the kiln takes about 33.3 seconds; the hive loses 0.03 in that time and reaches 0.57. This is consistent with the report, not evidence that self-heat is squared. The supplied hover was captured after kiln heat stopped, so the active hive distance/flags were not independently captured; do not present inferred link weights as measured values.

### Accepted change

Change only the central config default for:

```text
Section: Season - Winter snow
Key: Snow melt speed multiplier - self-heating pieces
Field: seasonalSnowSelfHeatMultiplier
Default: 5f -> 2f
```

Keep the current formula, distance weights, ordinary heat, roof/leaky/covered multipliers, interactive multiplier, and maximum/seed values. Do not rename this key or migrate/overwrite an existing saved configuration. A user with a stored value of 5 must reset or change it manually to evaluate the new default.

With otherwise identical coefficients, the kiln rate becomes 0.0072, the hive remains at 0.0009, and their ratio becomes 8. This is a calculated expectation, not a gameplay result. The current documentation-only commit must not change the binding itself; Codex will make that small source change.

## 4. Explicit weather override must affect live accumulation

### Confirmed reproduction

The supplied `2026-09-22_21-08-22.png` shows an exposed, ready, locally owned kiln in winter:

```text
registered=True, confirmed=True, simulates=True
bucket=AccumulationOpen, Snow=0, limits approximately [0.30, 0.60]
covered=False, roof=False, leaky=False, shield=False
melting=False, heat=0, melt=0, catch-up=none
period=2482, timeline environment=Clear Winter, timeline buildup=0
cumulative gain now=consumed WeatherGain, approximately 0.399600029
current environment=SnowStorm Winter, current buildup approximately 0.4
native environment period=-1, force=none, debug=SnowStorm Winter
```

Thus this is not residual heat or a roof suppressing accumulation. `EnvMan.UpdateEnvironment` gives the debug override precedence and sets the native period to -1. The snow runtime still gates accumulation with natural `GainBetween` and integrates natural cumulative gain. In a dry natural period, the accumulation bucket is not visited just because the debug weather is snowing. Diagnostics correctly expose the disagreement but do not change the producer.

### Accepted behavior

Support the explicit vanilla `env` / `resetenv` debug-weather path in live seasonal snow calculation. An active, valid snowy override must grow eligible, currently simulated exposed caps even when the natural timeline period is dry. A dry override must suppress natural live snowfall for the interval it replaces. Returning to ordinary weather resumes the existing deterministic timeline without a jump or double accumulation.

Keep the natural `SnowPeriod[]` prediction as the default historical model. Do not rewrite the entire winter timeline using whatever weather happens to be active now. Do not retrospectively apply a new override before its activation or assume it was active throughout an unobserved/unloaded interval. Do not add a persisted override journal, new ZDO schema, new synchronized weather command, or a generic event-sourcing subsystem.

Implementation requirements:

- Resolve override applicability once for the relevant update/boundary, not through repeated component searches per piece. Use the existing accumulation coefficients and world-time units.
- Update both the scheduling/gating of accumulation buckets and the actual arithmetic; changing only `IntegratePiece` will not wake a bucket skipped during a dry natural period.
- Replace the natural increment for the observed overridden interval; do not add both increments.
- Keep consumed world-time/natural-gain bookkeeping coherent even during a dry override, so `resetenv`, a mode change, refresh, or save/reload does not re-add weather already deliberately replaced.
- Settle or delimit the previous source at override start/change/end. Preserve pending catch-up and snapshot boundaries; never mark an unprocessed interval as consumed.
- Honor current eligibility, winter-only execution, cover, heat/interactive melting, shields, caps at maximum, and the no-automatic-minimum-refill rule. Heat mode still melts rather than accumulating snow concurrently.
- Respect pause, time reversal, skiptime/sleep boundaries, timeline rebuild, and world shutdown. Define the live-override interval at these boundaries; never extrapolate the newly selected weather into earlier time.
- Preserve existing ownership/publication rules. Do not apply a local console override to a foreign owner's authoritative snapshot or broadcast it as global server weather. Singleplayer is the reported required scenario; document the behavior for owner-zero prediction and locally owned multiplayer pieces without adding authority.
- Limit this correction to the explicit debug override. Do not silently turn raid, intro, dungeon, persistent-event, or every local EnvZone override into new world-wide seasonal snowfall.
- Keep diagnostics able to show natural period data and effective live accumulation source separately. Use the existing hover, not timeline log spam. A concise source/rate indication is sufficient.

## 5. Diagnostic formatting only

Keep the existing read-only hover, central client config, 0.2-second target/block cache, and immediate disabled return. No state registration, heat/roof queries, material work or publications may be triggered by reading it.

Use invariant display formatting:

| Value | Display precision |
| --- | --- |
| World/calendar/clock time, WeatherTime, timeline start/end, catch-up boundaries | 1 decimal (`F1`) |
| Snow minimum/maximum from config | 2 decimals (`F2`) |
| Piece Snow, snapshot/baseline snow, target/applied snow or visual level | 5 decimals (`F5`) |
| WeatherGain, cumulative/consumed weather gain | 3 decimals (`F3`) |
| Environment buildup intensity | 2 decimals (`F2`) |
| HeatRate, MeltRate and effective live accumulation rate | 5 decimals (`F5`) |

Display time-valued snapshot `From` in seconds with one decimal and an explicit unit; do not present millisecond timestamps as seconds. Owner IDs, ZDOID, period numbers, revisions, and epoch identifiers remain exact integers when shown as identifiers. Format the period fields explicitly inside the hover rather than inserting a `G9` `SnowPeriod.ToString()` block that defeats the display rule. Keep precise stored values and the RUE-oriented record unchanged; this is not rounding simulation or persistence.

## 6. Floes: restore the original narrow approach

### 6.1 Producer and cleanup ownership

The earlier server-only producer requirement was incorrect and is withdrawn. The original released/master implementation at `988e98c514ce49369a92cbaf934a7aca384fe6e1` created floes on the peer that owned the loaded zone's `SpawnSystem` / zone-controller view. It did not require `ZNet.IsServer()` before placement. This allows a normal client to use its loaded placement physics. The server performs global seasonal cleanup.

Restore this division:

- Generate on the client/host that has the ordinarily loaded zone, valid placement data, and ownership of the zone control view. Do not grant generation to every client or claim the zone for convenience.
- No temporary `SpawnZone`, forced terrain loading, or proactive placement in ghost-only/unloaded zones. Generation remains approach-only.
- Preserve completion markers and existing marked-floe checks. A skipped/unavailable zone is not completed. Recheck ownership, season, and loaded state before continuing a sliced placement job; abandon stale authority without duplicate production.
- Retain useful small placement slices, private RNG/exclusion state, candidate/instantiation budgets, and queue coalescing. Restoring the original producer/physics does not require restoring monolithic whole-zone placement.
- Global removal and marker reset remain server responsibilities and must not require client terrain or water. Do not introduce cross-client deletion authority.
- Already created seasonal floes remain distant network objects. Preserve marks, scale/mass/health, placement exclusions, and climb behavior. Never set `OutsideZones` manually.

### 6.2 Scoped rollback, not a blanket Git revert

Use the original `Floating_CustomFixedUpdate_IceFloeRotation` and the original client-owner placement path in `Controllers/ZoneSystemVariantController.cs` at `988e98c` as reference. Do not revert the entire file or the entire Codex commit range: that would lose unrelated verified snow, water restoration, diagnostics, and texture lifecycle work.

Remove the new universal proxy-renderer/LOD replacement system and the broad freeze/mode/sampling framework that currently prevents normal nearby motion. Remove unused files/project entries/calls when their code is retired. Git history is sufficient to retain the experiment; do not leave a disabled parallel framework in the runtime.

Do not copy MeshRenderers, materials, property blocks or LOD arrays per floe. Lazy initialization is for the few references and original values actually required by the simple seasonal path, not for deferring a generic proxy constructor. Leave native collider enablement, health variants, and collision behavior intact. Trace the reported disabled collider instead of unconditionally enabling every descendant collider.

### 6.3 Near wave forces and safety

Keep normal `Floating.CustomFixedUpdate` as the buoyancy/update driver. For an eligible, locally authoritative seasonal floe with valid water, add the original four extra forces at points along/across the wind, using the original `ClosestPoint` and force formula. Preserve their fixed-step timing and scale/mass behavior. Do not replace this with a new global multi-phase physics engine or an arbitrary one-zone physical radius.

Validate every water probe independently. Missing/nonfinite water contributes zero extra impulse for that point. Do not substitute absolute liquid height 0 or keep the -10000 sentinel in the depth calculation. A valid submerged point retains its legitimate upward force.

Handle whole-body loss of buoyancy with a minimal, scoped safeguard coordinated with the actual `ZSyncTransform` gravity behavior. It must not leave a nearby floe permanently kinematic when water becomes available. Preserve/restore the original fields actually modified, handle ownership transitions and a camera-less server, and never steal ownership. Only marked seasonal floes are affected. Do not suppress out-of-world warnings as a fix.

Avoid redundant steady-state policy writes. Guard velocity assignments for kinematic bodies. A verified invalid saved height may need one bounded owner-authorized recovery; do not snap healthy roots or write their position every frame.

### 6.4 Correct water distance and cheap distant bobbing

Use one shared cache of the `Water.ApplySettings` calculation and its square:

```csharp
(float)ZNet.instance.GetSyncedSimulationDistance().NearSimulationDistance
    * ZoneSystem.instance.m_zoneSize
```

Refresh at initialization and synchronized distance changes. Do not read `_VisibleMaxDistance` from shared material, substitute TotalSimulationDistance, retain constant 120, or impose the new min(distance, one zone) physical policy. Keep `_WaterEdge` handling separate.

Beyond real wave-physics range, use only modest vertical center bobbing relative to a stable baseline. Exact remote wave tilt/phase is not required. No four distant collision/liquid queries, no nonuniform-scale tilt machinery, no copied renderers, and no published cosmetic motion. Prefer the narrow original far path with corrected distance and safety. Do not accumulate offsets on top of earlier cosmetic offsets; restore alignment before near interaction and before resuming pose publication.

Keep optional distant work cheap and staggered as needed. Do not run expensive wave sampling across all visible floes at maximum draw distance. Existing scheduler infrastructure may be retained only where it actually limits placement or lightweight work; remove complexity whose sole purpose was proxy tilt or broad physics-mode ownership.

## 7. Frozen ships: eliminate unsupported velocity writes

The maintainer reports both linear- and angular-velocity warnings while freezing/thawing ships.

At the reviewed baseline, `PlaceShip` first changes `isKinematic`; its Karve correction can subsequently assign `linearVelocity`. Native `Ship.CustomFixedUpdate` also contains buoyancy/damping assignments to both velocity fields without an `isKinematic` guard. These are concrete reachable write paths; a warning without its stack does not establish the exact share of each path.

Fix the local frozen-ship integration. Clear necessary velocities while the body is still dynamic, skip unsupported assignments after it becomes kinematic, and prevent only the physical force/damping portion from running for a Seasons-frozen kinematic ship. Preserve control, sail/rudder visuals, and other required nonphysics behavior. Restore dynamic/sync policy before the first thawed physical calculation; a coroutine delay must not leave a bad intermediate tick. No global Rigidbody setter patch, blanket warning filter, or indiscriminate skip of the whole ship update.

## 8. Skiptime peak: isolated world-maintenance follow-up

Keep this separate from floe physics and from the verified snow integration formula.

`EnvMan_UpdateTriggers_SeasonStateUpdate` can synchronously call `UpdateState`, notify a changed season/day, and reach `UpdateWaterState -> CheckZDODatabase`. `CustomSyncedValuesSynchronizer` applies immediately when not waiting for caching. `CheckZDODatabase` copies `m_objectsByID.Values.ToArray()` and scans the world, while eligible terrain decultivation can decompress, allocate grids, inspect paint/biomes and recompress in that same call. Queueing floe deletion did not remove the cost of this discovery pass.

As a final isolated optimization, change the trigger into a request for bounded maintenance. Coalesce repeated requests, resume safe portions of discovery/processing, revalidate current season/ownership before writes, and cancel on world shutdown. Avoid a full-world `ToArray()` up front, restarting a full scan every frame, or retaining an invalidatable Dictionary enumerator across frames. Use existing suitable sector/iteration facilities where practical; do not add a broad permanent indexing framework solely for this task. Give terrain work its own small budget. A time guard cannot interrupt a single native call.

The day-change path also queues geometry refresh for every snow region despite no demonstrated roof change. Separate time catch-up from unnecessary daily geometry invalidation where safe. Do not remove required geometry notifications or rewrite `RequestSnowCatchUp` / saved-cursor logic speculatively. Preserve the boundary arithmetic already verified by gameplay. If a safe limited change cannot be established, report it separately rather than turning this into another snow-runtime rewrite.

Do not claim the entire profiler peak was removed without a new measurement. Full terrain/WaterVolume rewrites are outside this corrective scope.

## 9. Codex execution brief

Implement sections 3-8 of this document on `perf/snow-performance`, preserving sections 1-2 and all newer maintainer work. This document, not the superseded server/proxy plan, is the source of current decisions.

Before editing, read applicable AGENTS.md, inspect the actual HEAD, git status and local diff, and preserve uncommitted work. The remotely reviewed code is `396be7d`; the documentation commit containing this revision follows it. Do not reset, clean, force-push, rewrite history, or overwrite a newer change. Work in the existing worktree; do not modify master or feat/blood-moon.

Read game code first from `shudnal/assemblies_combined`; the baseline used for this review is `d1374bfd9175ac8f733ae483b0a06e5c8b75906e` (1.0.15). Verify the revision appropriate to the worktree before relying on fields or Harmony targets.

All repository content, comments, documentation, config descriptions, logs and commit messages must be English. Do not build the mod, run Valheim, or execute tests. Static source/API/diff/project-inclusion review is allowed. Do not open a PR or start a separate cloud task. Do not bump the version, change packaging/dependencies, or modify snow JSON/ZDO schemas. The only accepted heat balance change is self-heat default 5 -> 2. No config migration, timeline logs, native snow ownership, or reinstated door tracking.

Suggested small commit boundaries:

1. `fix: restore client-owned floe generation on loaded zones`
2. `fix: restore simple floe wave physics and water-distance handling`
3. `fix: guard frozen ship physics and velocity transitions`
4. `fix: honor explicit debug weather in live snow accumulation`
5. `fix: lower the default self-heat multiplier`
6. `style: format seasonal snow hover values by meaning`
7. `perf: slice seasonal world maintenance at day boundaries`
8. `docs: record corrective implementation and remaining gameplay checks`

Keep each intermediate commit internally coherent. Adjust grouping if a removal and its replacements must be atomic; do not manufacture empty commits. Preserve simple behavior and avoid adding a replacement generalized motion framework. Record any unresolved engine constraint rather than silently weakening collision, ownership, duplicate prevention, or catch-up correctness.

Static review must cover all touched call sites, Harmony signatures, project includes, removal of dead proxy/motion calls, no unintended Cyrillic outside localization, correct velocity-write ordering, and no cosmetic pose publications. Inspect per-frame paths for accidental allocation or hierarchy scans. Do not label syntax/source checks as successful compilation or runtime acceptance.

## 10. Maintainer gameplay checks and final report

| Scenario | Acceptance target |
| --- | --- |
| Kiln + beehive, self=5 | Existing formula still explains 0.018 / 0.0009 under the same geometry; no second application of self multiplier. |
| Same setup, self=2 | Calculated target 0.0072 for the kiln, unchanged surrounding heat under identical conditions; stored custom config is not overwritten. |
| Natural weather, no env override | Existing timeline, catch-up, melting, roof and summer behavior remain unchanged. The MyLittleUI SnowStorm-only filter is not treated as a Seasons defect. |
| Natural dry period + snowy env | An exposed eligible simulated cap grows from its current value; hover distinguishes natural record and effective live source. |
| Natural snowy period + dry env, then resetenv | Live snowfall is replaced, not added; no delayed replay of suppressed snowfall or jump on reset. |
| Env start/change/end, pause, sleep/skiptime, save/reload and ownership changes | No retroactive override before activation, double gain, lost pending catch-up, wrong-owner publication, or false minimum refill. No weather log spam. |
| Diagnostic hover | Requested decimal precision, explicit time units, exact identifiers, readable period fields; no mutation of diagnosed state. |
| Floes in singleplayer and an ordinary client far from the host | Zone-owner generation on loaded terrain; no server-only terrain dependency, forced SpawnZone, duplicates, or stale completion marker. |
| Floes nearby / far / water-volume boundary / max distance | Working collider and climb, original four-point near physics, missing probe gives no impulse, no free-fall or permanent freeze, cheap center-only far bobbing. |
| Floe unload, scale variants, transfer, disconnect, winter end | Correct restoration and server cleanup; no proxy artifacts, cosmetic ZDO stream, or ownership stealing. |
| Frozen/thawed ships including Karve | No unsupported velocity warnings, required visuals/controls preserved, no intermediate kinematic force tick. |
| Large-base approach and synthetic skiptime | Preserve validated cap responsiveness; world maintenance is distributed without replacing it with another full-list spike. Final problematic-user-base measurement remains outstanding. |

The final Russian report must identify actual starting HEAD/dirty-state handling, commits and files, source-confirmed findings versus user observations, numerical heat expectations, explicit-env scope/limitations, how client generation and original physics were restored, maintenance budgets, static checks, and the exact checks still unexecuted. No invented FPS, memory, network, build, or gameplay result.

## 11. Source anchors

At `396be7d` in `shudnal/Seasons`:

- `WinterSnow/SeasonalSnowHeat.cs`: UnitMeltRate, DistanceWeight, RebuildHeatLinks, HeatLink.Contribution, RecalculateHeat, wet Fireplace polling.
- `Seasons.cs`: central defaults and client diagnostic binding.
- `WinterSnow/SeasonalSnowSimulation.cs`: IntegrateSnow, IntegrateBucket, IntegratePiece, heat/interactive mode selection.
- `WinterSnow/SeasonalSnowDiagnostics.cs` and `SeasonalSnow.cs`: display formats and inspectable period records.
- `Controllers/SeasonalIceFloes.cs`, `SeasonalIceFloeMotion.cs`, `SeasonalIceFloeVisual.cs`, `Utils/IceFloeClimb.cs`: the implementation being corrected, not the architecture to preserve wholesale.
- `Controllers/ZoneSystemVariantController.cs`: PlaceShip, frozen state restoration, UpdateWaterState, CheckZDODatabase.
- `SeasonState/EnvManPatches.cs`, `SeasonState.cs`, `TerrainDecultivation.cs`, `Utils/CustomSyncedValuesSynchronizer.cs`: trigger and maintenance call chain.

Original narrow floe reference: `Controllers/ZoneSystemVariantController.cs` at `988e98c514ce49369a92cbaf934a7aca384fe6e1`. Prior complete implementation record: this document at `396be7d9a3aa3fa01cbcc2a11cec46ce28f50346`.

Game-source mirror at `d1374bfd`: `assembly_valheim/EnvMan.cs`, `Terminal.cs`, `Water.cs`, `Floating.cs`, `ZSyncTransform.cs`, `Ship.cs`, `ZoneSystem.cs`, `ZDO.cs`, and `ZDOMan.cs`.

The attached AzuHoverStats decompilation remains only the historical reference for appending to Hud.UpdateCrosshair, not a new dependency. The screenshots are conversation evidence, not checked-in benchmark data. This revision records planned corrections; implementation status must be updated by Codex rather than inferred from this document's existence.

## 12. Corrective implementation status

Work started in the existing `perf/snow-performance` worktree at `396be7d9a3aa3fa01cbcc2a11cec46ce28f50346`. The worktree and index were clean; the maintainer's `ffd2488b4ec9df01b78466fe58481de8a82d1257` specification was fetched and applied by fast-forward. No local work or commits were discarded. No applicable `AGENTS.md` was found. Game-source inspection uses the clean `shudnal/assemblies_combined` checkout at `d1374bfd9175ac8f733ae483b0a06e5c8b75906e` (1.0.15). The user's subsequent instruction explicitly authorizes pushing, opening a PR and requesting Codex review; it supersedes this document's earlier no-PR instruction, but does not authorize merging or changing the protected branches.

### Completed: self-heat default (section 3)

Only the existing central `seasonalSnowSelfHeatMultiplier` binding default changed from `5f` to `2f`. Its key, description, formula, heat-link weighting and saved configuration are unchanged; no migration was added. Static arithmetic gives kiln `0.0072` and hive `0.0009` per active second under the specified identical coefficients (ratio 8). With an existing saved self value of 5, the calculated `0.018` / `0.0009` and ratio 20 remain. These are calculations, not new gameplay observations.

### Completed: zone-owner generation (section 6.1)

The ordinarily loaded zone's owned `SpawnSystem.m_nview` is again the producer on a client or host. Every placement slice checks loaded state, current season and that same ownership; it never claims ownership. Native spawning callbacks retry ownership arriving after zone loading. Server-only global removal and marker reset remain separate. Placement retains private RNG, exclusions, completion/existing-floe checks and the previous budgets: 8 discovery/request steps, 64 inspected objects, 4 candidates, 1 instantiation, 4 removals and 4 marker resets per service, a 1.5 ms guard, and 0.5 s scope refresh. No forced `SpawnZone` or unloaded placement was added. These are source checks; multiplayer placement remains a gameplay check.

### Completed: frozen ships (section 7)

`PlaceShip` clears linear and angular velocity only while dynamic, before applying the kinematic state; the subsequent Karve correction no longer writes velocity. The first fixed update observing thaw or changed ownership restores the captured body/synchronization policy synchronously. While frozen, saved body-velocity synchronization is disabled and later restored. A scoped Harmony guard skips only the native force/damping tail for the captured Seasons-frozen kinematic body. Controls, sail/rudder visuals, owner checks, damage and speed bookkeeping still run. The boundary was checked in the 1.0.15 source and matching publicized DLL (`worldCenterOfMass`, IL `0081-0087`, no exception handlers). No global setter patch or warning suppression was added; warnings and thaw behavior still require gameplay verification.

### Completed: diagnostic formatting (section 5)

The read-only hover uses invariant `F1` seconds, `F2` limits/intensity, `F3` cumulative gain and `F5` snow/visual/heat values. Snapshot timestamps are converted from milliseconds to seconds and labeled. IDs and epochs remain exact integers, and period fields are formatted explicitly in the hover. Stored values and the precise RUE `SnowPeriod.ToString()` are unchanged. The effective live-source line belongs to the explicit-weather correction below.

### Completed: explicit live weather (section 4)

Only a valid `EnvMan.m_debugEnv` selects an override. One observed in-memory interval replaces the natural gain for currently simulated locally owned pieces, using the same buildup coefficients and world-time units. Both bucket eligibility and arithmetic use the live interval, including dry overrides. Consumed natural gain/time advance together; existing snapshots checkpoint intentionally replaced weather even when Snow does not change. Start/change/end settle the previous source without consuming pending catch-up. No natural timeline records, storage schema or synchronized weather command changed.

Pause, discontinuous time, catch-up, timeline rebuild and shutdown delimit/reset observation. Unobserved, unloaded and skipped intervals retain natural history; an override is never backdated into them. Owner-zero prediction remains natural, and foreign-owner snapshots are not changed by a local console override. Raids, intro, dungeon/event and local EnvZone weather are outside this feature. The existing read-only hover separately shows the natural record and effective live source/rate. Boundary correctness has been reviewed statically; the env/resetenv scenarios in section 10 still require gameplay checks.

### Completed: narrow floe physics (sections 6.2-6.4)

`SeasonalIceFloeWaves.cs` replaces the rejected `SeasonalIceFloeMotion.cs` / `SeasonalIceFloeVisual.cs`, including their project entries and callers. Nearby locally owned marked floes retain native `Floating.CustomFixedUpdate` and the original four `ClosestPoint` wave-force formulas/timing. Every extra Water probe rejects missing/nonfinite heights independently. Missing center water temporarily holds gravity and sleeps a dynamic body; native `ZSyncTransform.m_useGravity` is coordinated and restored, without making floes kinematic. A missing/neighbor-volume callback permits one near-owner center fallback probe per 0.5 s, covering scaled floes at water boundaries and reappearing water. Ownership acquisition is checked around native saved pose/velocity adoption. A verified invalid height permits one owner-authorized recovery after valid water, retried at most every 0.5 s until possible.

The shared distance and squared distance use exactly `NearSimulationDistance * m_zoneSize`, refreshed at initialization/settings changes. Distant motion is only a 0.35 m vertical sine offset from a stable baseline: round-robin 16 cheap visits per late frame, no more than once per 0.25 s per floe. Far bobbing performs no liquid probes or copied renderer/LOD work and disables position/body-velocity publication; baseline and synchronization are restored before near interaction or ownership transfer. No healthy root snapping or collider enablement override was added.

Source tracing confirms the rejected `Freeze()` did not directly disable colliders. Native `Ledge.Changed` controls its collider, and rock health variants control collider-bearing child activation; the source mirror alone does not establish which caused the reported `ice1` state. `GetFloeSize` restores its temporary collider flag in `finally`. The exact reported disabled-collider cause and all motion behavior remain gameplay checks, not claimed diagnoses.

### Completed: isolated world-maintenance slicing (section 8)

`CheckZDODatabase` now coalesces a maintenance request. Discovery resumes a reverse cursor over the native sector lists, including sector zero, with no whole-world copy or Dictionary enumerator across frames. The lists append/remove natively; reverse traversal can repeat an ID after removal without skipping the surviving initial entries. An active-pass sector-add hook catches existing marked floes moving behind the cursor. Server-only seasonal cleanup rechecks the current season, and stale deletion/marker queues are cancelled when the floe window resumes.

Discovery is limited to 1,024 sector slots and 128 object reads per frame with a 1.0 ms guard. Terrain has a separate queue capped at 128 IDs, up to 8 validations and 1 operation per frame with a 0.5 ms guard. Full queues retain the discovery cursor; loaded terrain compilers retry their requests. Actual processing rechecks owner, season, day, world identity and readiness. Loaded terrain uses this same queue, retaining failed-attempt tracking. Pause/loading suspends service; world shutdown/reset cancels it. A single terrain decompress/paint/compress/native reload operation remains indivisible and can exceed the time guard.

Only `OnDayChange`'s unconditional snow geometry refresh was removed. Normal snow integration, explicit time-skip catch-up, season-change and actual geometry notifications remain; the verified catch-up cursor algorithm was not redesigned. Source inspection supports removing the synchronous discovery work from the day trigger, but no new profiler measurement establishes the size of the improvement or removal of the entire reported peak.

### Remaining work and verification

Sections 3-8 are implemented. No build, tests or Valheim execution has been performed. The maintainer's prior gameplay observations in section 2 are preserved as reported evidence, separate from acceptance of the corrective implementation. All gameplay checks in section 10, including the problematic large base and a new skiptime profile, remain for the maintainer.

Static validation completed: project XML parses and all 80 Compile entries exist without duplicates; Roslyn C# 10 syntax-only parsing reports no syntax errors (no compilation, semantic binding, emit or tests). `git diff --check` passes. Touched Harmony targets/fields were checked against game source 1.0.15, with frozen-ship and maintenance APIs also inspected in matching assembly metadata. Independent source reviews covered explicit weather, ship transitions and maintenance cursors; floe review found and corrected acquisition ordering and neighboring-water-volume handling before publication. No retired proxy/motion references, forced `SpawnZone`, manual `OutsideZones`, world-sized ZDO snapshot, newly added Cyrillic, timeline logging, version/package/dependency change or snow storage schema change remains in this corrective diff. The `ruleBiome` and wet-Fireplace fixes, inspectable records, texture retirement and existing cap pipeline are preserved.

Uncertainty remains about the reported disabled floe collider's exact native/prefab cause, runtime Harmony compatibility with other mods, boundary motion/ownership behavior, final large-base performance and the measured share of the skiptime peak. Source/syntax checks do not establish any of these runtime results. Compilation and the concrete gameplay matrix in section 10 remain maintainer work.

### External review follow-up

Codex review on PR #45 identified that the old center-biome shortcut prematurely settled mixed coastal zones whose centers were land. It was removed: loaded-zone ownership and bounded candidate work remain, and each placement point still checks its actual terrain biome, biome area, altitude, water and exclusions. Include a coastal zone with a land center and Ocean candidates in the maintainer placement checks. This correction was source/syntax reviewed without build or gameplay execution.

A conservative loaded-Heightmap prefilter avoids copying dense pure-land sector lists: only an initialized, ordinary heightmap belonging to the zone with no Ocean in any of its four corners can settle early. In the matching game source, candidate `GetGroundData` calls ordinary `Heightmap.GetBiome`, which can select only those corner biomes. Mixed coastal heightmaps continue to candidate processing; unavailable or distant heightmaps are not rejected by this shortcut. No terrain generation or new sampling was added.
