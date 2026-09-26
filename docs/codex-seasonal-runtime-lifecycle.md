# Task: remove dormant snow/floe work and localize active callbacks

## Execution and source baseline

Implement this task in `shudnal/Seasons`, on top of `perf/snow-performance` (PR #45). The inspected code baseline is `a3e2775abd815680e795116ac87de016440e9379`. Fetch the current branch before editing, preserve newer maintainer changes, and record the actual implementation base. This document is an implementation brief, not a request for another proposal-only response.

The maintainer will run Codex and perform compilation/gameplay verification. Do not build the mod, run automated mod tests, launch Valheim, or construct a substitute runtime/physics test harness. Static source/API inspection, diff checks, C# syntax inspection, project-reference inspection, and textual consistency checks are allowed. Do not change ValheimProfiler or its instrumentation.

Keep all repository content in English except intentional localization resources. Do not add configuration migrations, change version/dependencies/packaging, or merge PR #45. Work in the existing work branch or a child branch based on it, never master. Return commit(s) and a concrete verification plan; if repository writes fail, return an archive containing the complete changed/new files and a deletion manifest.

Read any applicable AGENTS.md first. None was found at the repository root or in docs at the inspected baseline; do not assume this remains true. Read the relevant current implementations and these existing contracts before changing behavior:

- `docs/floe-background-forecast.md`, `docs/floe-json-settings.md`, `docs/floe-kinematic-motion.md`, `docs/floe-cleanup-lifecycle.md`, `docs/floe-simulation-authority.md`;
- `docs/snow-diagnostics-and-floe-followup.md`, `docs/snow-performance-agreement-review.md`, and the actual current snow runtime/storage code.

Historical documents describe superseded intermediate implementations. The maintainer's accepted behavior and requirements below take precedence. Game source/API inspection must start from `shudnal/assemblies_combined`; the reference used in this investigation is commit `d1374bfd9175ac8f733ae483b0a06e5c8b75906e`, directory `assembly_valheim`.

## 1. Goal and meaning of inactivity

Outside winter, there must be no recurring seasonal snow/floe simulation, discovery, visual servicing, publication, or empty-registry polling. During winter, each feature must be equally dormant when its configuration is disabled. Floes additionally obey their existing winter-day interval and frozen-water exclusion.

The maintainer's sentence saying there should be no calculations "in winter" was inconsistent with the surrounding request; this brief interprets it as "outside winter". Enabled winter features must continue working.

The requested solution is lifecycle-driven activation/deactivation, not an additional `if (!enabled) return` inside still-invoked callbacks. Do not toggle Harmony patches with Patch/Unpatch at season/config changes. Do not hide callbacks from the profiler, rename them to conceal overhead, or move the same unconditional dispatch into a transpiler helper.

After a finite deactivation/cleanup transition, the subsystem must have:

- no scheduled Update/FixedUpdate/LateUpdate tick, coroutine, timer, or repeated discovery work;
- no snow/floe per-instance loop or recurring lookup performed by callbacks on unrelated game objects;
- empty world-scoped instance registries, mutable simulation indexes, refresh/publication queues and visual bindings;
- no worker jobs, leases, pose publications, or physics forces attributable to the inactive feature.

One-time cleanup, settings/season/world lifecycle notifications, and activation when a relevant object is loaded remain necessary. The general Seasons calendar, settings synchronization, and unrelated functionality are not being disabled. Immutable configuration/default data are not live simulation collections and need not be deleted. A sleeping worker thread with no jobs is acceptable; polling it from every frame is not. Do not destroy saved world data merely to make runtime collection counts zero.

Also distinguish an enabled feature with no local participants from an active populated feature. With no floes locally loaded and no real placement work, there must be no floe update loop. Reactivation must come from legitimate scene/zone lifecycle notifications, not a permanent scanner asking whether anything has appeared.

## 2. Accepted behavior: preserve this regression boundary

The maintainer now reports that floe appearance and physics work correctly. Preserve:

- streamed placement from distant loaded Ocean zones and immediate one-pass server deletion, including unloaded marked ZDOs and placement-marker reset;
- native-owned nearby dynamic floes, reciprocal contacts, player/creature/ship/projectile interaction and climbing;
- ownerless non-colliding kinematics, existing soft lease/election protocol, replica pose smoothing, and the current publication interval;
- native ownership taking priority over a fallback lease; another player's native ownership must not be treated as ownerlessness;
- local, unpublished FarVisual flat-water positioning/bob beyond visible waves and smooth entry/exit;
- two overlapping floes harmlessly overlapping in kinematic mode, then separating normally when native dynamics resumes;
- 70% nominal collider immersion, fixed Ocean depth 1 and offset 0, the accepted full-height/filtered-tilt formulas, wind refresh throttling and one rolling background worker with a gradual 10-second reserve;
- ordinary floating items, ships, creatures and unmarked native Deep North ice.

Do not change generation density, scale, forces, wave formulas, prediction spacing, wind cadence, global fixed timestep, or ZDO cadence to manufacture a performance gain in this task.

Keep snow's accepted model: natural deterministic weather timeline, explicit live-weather override handling, unloaded-region catch-up/epochs, owner-authorized compact persistence, roof/heat/shield/use effects, and separation from native snow/heavy-snow damage. Preserve native permanent snow in Deep North and native snowy mountain locations. Seasonal piece caps, copied caps, shared wet/snow objects, damage variants, LOD children and material restoration must not regress.

Review seasonal cape/creature/vagon helpers as well. Identify which feature controls each path before changing it. Frozen ocean, fish-under-ice handling, rain protection and general season effects are not automatically the same feature as seasonal snow or floes. Do not disable legitimate spring/game behavior solely because its profiler owner is Seasons.

## 3. Settings and previous false spawn diagnosis

Keep these three controls in the main cfg, under `Season - Winter ocean`:

```ini
Enable ice floes in winter = true
Fill the water with ice floes at given days from to = {"x":4.0,"y":10.0}
Health of ice floes = 20
```

Other floe settings remain in `Seasonal ice floes.json`, using the same `CustomSyncedValue` loading and ValueChanged path as `seasonalSnowJSON`. No separate cfg, local shadow JSON source, special filename handling in ReadConfigs/ReadConfigFile, migrations, or extra reload pipeline. Amount defaults remain 10..15 and scale 1.25..2.5.

The recent "floes do not spawn" report was resolved by the maintainer: the world was Winter day 2 while the configured range was 4..10. Do not keep adding speculative spawn fixes to solve that already-explained report. Preserve existing working placement/deletion unless changing callback scheduling requires a specific, justified adjustment.

## 4. Maintainer observations and profiling interpretation

The latest uninstrumented Ocean measurement is approximately 100-110 FPS with floes versus 165 FPS without them. The previous large-population result was 60-70 FPS. These are reported observations, not a controlled proof attributing the improvement to any single commit. Keep population/camera/settings unchanged when making implementation comparisons.

Six new screenshots cover three distinct scenes:

| Scene | Reported context | Relevant visible 1-second rows (ms/frame, calls/frame) |
| --- | --- | --- |
| A | Winter day 910; player far inland, no visible floes/water or large buildings | Floe ClientSync prefix 0.062 / 279.83; OwnerSync prefix 0.044 / 219 and postfix 0.042 / 219; FloeWaterObservation 0.215 / 61; snow UpdateSnowVisual prefix 0.458 / 41.5; snow ZNetScene.Update postfix 0.181 / 1. |
| B | Spring day 208; base of about 27,000 instances; about 68 FPS without profiler | Floe ClientSync prefix 0.170 / 858; OwnerSync prefix and postfix each about 0.082 / 429; FloeWaterObservation 1.022 / 239; snow UpdateCover prefix 0.199 / 906.22 and postfix 0.188 / 906.22. |
| C | Same base/camera in winter, snowfall; about 58-62 FPS without profiler; only a few very distant floes | Snow ZNetScene.Update postfix 1.307 / 0.95; floe ClientSync prefix 0.185 / 920.77; OwnerSync prefix 0.083 / 431 and postfix 0.082 / 431; FloeWaterObservation 0.076 / 239. |

The winter 60-second view also contains a 14.045 ms raw maximum for `ZNetScene_Update_SnowVisuals.Postfix`; investigate what that callback services, without assuming every maximum is an inherent algorithmic spike.

The screenshot timestamps are 2026-09-26 21:03-21:13. These images are in the maintainer's chat; this brief includes the relevant readings so it is usable without them.

Important: vanilla WaterVolume.UpdateFloaters can still run when no water is visible. Calls are not evidence that a floe exists. `FloeWaterObservation` calls native GetWaterSurface for unrelated interactables, so its inclusive time is not all additional Seasons overhead. Do not sum parent/child instrumented times as exclusive cost. Snow callbacks outside winter prove invocation, not necessarily a full snow simulation pass or a leak. Inspect actual state and paths.

## 5. Source-confirmed starting points

These paths/methods were inspected at the baseline. Produce a before/after routing inventory, not just a shorter list of profiler rows.

### Floes

`Controllers/SeasonalIceFloeWaves.cs` currently owns global `floaters` and `syncs` lookup dictionaries. Its Harmony prefixes/postfixes intercept every native:

- `ZSyncTransform.ClientSync` and `OwnerSync`;
- `Floating.CustomFixedUpdate`, OnEnable/OnDisable, SetLiquidLevel and TerrainCheck;
- `Hud.UpdateCrosshair` for diagnostic hover.

The sync hooks run a lookup even for unrelated objects. Their responsibilities include BeforeSync, native-acquisition handling, UpdateOwnerlessMotion, motion suppression, and PublishFallbackPose; preserve those responsibilities while removing their global hot-path routing.

`Controllers/SeasonalIceFloePrediction.cs` replaces the GetWaterSurface call in `WaterVolume.UpdateFloaters` with `FloeWaterObservation` for every registered water interactable. Thus non-floes execute the helper too. Eliminate this cross-cutting call from ordinary interactables; preserve any water/effect lifecycle actually still needed by seasonal floes.

`Controllers/SeasonalIceFloes.cs` services placement from a `ZoneSystem.Update` postfix even when there is no active placement. Retain its one-pass removal and zone-entry correctness, but activate the placement driver only while work exists. Discovering new eligible zones is a lifecycle event, not a justification for a dormant per-frame callback.

`Utils/IceFloeClimb.cs` already owns Start/OnEnable/OnDisable/OnDestroy, interaction and hover, but recurring movement is externally routed. Rename the actual component and its partials to `IceFloe` as part of giving it the complete instance lifecycle. Update project Compile paths and all references; keep localization tokens and existing ZDO identities unchanged. Avoid a compatibility alias component that creates duplicate subscriptions.

Native `Floating` and `ZSyncTransform` each maintain an `Instances` list from OnEnable/OnDisable in the reference assemblies. Their relevant methods are not virtual. Merely adding a same-named method in a subclass will not reroute the existing call. `Floating.Awake` also schedules TerrainCheck with InvokeRepeating. Review actual registry traversal, startup order, interface dispatch, water registration and scheduled callbacks before relying on `enabled=false` or changing membership.

### Snow

`WinterSnow/SeasonalSnowVisualPatches.cs` contains:

- unconditional snow routing from `ZNetScene.Update` (only pause is checked at the entry);
- the `WearNTear.UpdateSnowVisual` prefix with an inactive path;
- an UpdateWear transpiler redirecting wet-visual access through a Seasons helper;
- native lifecycle/material hooks.

`WinterSnow/SeasonalSnowSimulationPatches.cs` includes UpdateCover prefix/postfix, health-visual geometry hints, native snow isolation, Player.UpdateAttach, scene-readiness notifications, ZDO receive/transform/ownership hooks, save checkpoints, heat/terrain notifications. Many reach a controller even when nothing is registered. Classify and localize these paths without losing active-season invalidation or publication ordering.

`SeasonalSnowSimulation.UpdateSnowSimulation` already returns before starting the full simulation when completely inactive. Preserve that semantic protection, but it is not the requested absence of invocation.

`EndSnowWinter()` clears simulation/heat/interaction queues and retires pieces, queuing visual hides with `releaseVisual:false`. It does not call `ResetVisuals()`. `SeasonalSnowController.UpdateVisuals` can hide caps while retaining `visuals`, renderer bindings and pooled materials. Check the complete lifecycle; make retirement finish by releasing inactive visual state and owned resources once. Do not describe the screenshot alone as proof that the collections are currently nonempty.

Audit `SeasonalSnow.InitializePrefabs`, SeasonalSnowTimelines/Environments, mesh-copy caches, geometry indexes, materials, heat sources, interactions and diagnostics as well. Make live world-scoped state follow the same activation contract; do not repeatedly rebuild it while inactive.

## 6. Implementation direction

### Event-driven lifetimes

Use the existing season/config/world notifications to establish or tear down a feature session. The rendering/physics drivers must actually be disabled/unregistered when dormant. Starting or stopping may do a finite amount of required initialization/restoration. Do not add a continuously running manager that polls IsWinter for each subsystem every frame.

A feature session can have setup/active/teardown phases as needed, but do not add a generic orchestration framework. The end state matters: no queued work, participants or ticking driver. If teardown is bounded across frames for the large snow base, expose that finite transitional state and detach immediately when drained; do not continue service callbacks indefinitely afterward. Keep floe deletion immediate as already accepted.

### Floe-local instance operations, shared heavy work

`IceFloe` owns only its own body/view/Floating/sync references, registration, climbing/hover, per-instance state and lifecycle. Shared ocean snapshots, forecasts, queues and heavy math stay in one coordinator/worker. Use explicit participant lists by required update phase; do not multiply native-managed callbacks for hundreds of inactive objects.

A concrete direction to evaluate is to stop automatic native tick participation for the marked seasonal instances that the local driver takes over, then explicitly invoke preserved native sync operations where needed for that floe. This can remove global ClientSync/OwnerSync interception without replacing synchronization of every game object. Exact mechanics must follow the native API, not this sketch. Preserve active-season native replica behavior and ownership-acquisition ordering, prevent double ticks, and restore original component state exactly when relinquishing responsibility. Do not leave native force code running alongside the custom force driver.

Only marked seasonal floes should receive active instance services. Do not attach a global per-frame lookup burden to normal items or permanently alter the shared ice1 prefab's native Deep North instances. Lifecycle hooks that identify loaded saved floes must be sparse and narrowly justified.

Do not replace the removed sync hooks with equally broad Floating or MonoUpdaters helper calls that execute for every item. If native dispatch needs a bridge, document the precise residual instruction/branch cost and ensure an inactive/unrelated path does not call a Seasons helper, perform a registry/name lookup, or enqueue work. A hidden generic per-object router is not zero-work deactivation. Never claim literally zero added instructions if a static bridge remains.

### Snow singleton and passive instance state

Keep the existing region/bucket math in the snow singleton. Do not solve a 27,000-piece base by adding an Update/FixedUpdate to every piece. Passive local bindings/components or specific notifications are acceptable when they actually eliminate the global polling path.

Maintain health/cap variant changes, cover changes, moving geometry, heat source/use/shield effects and owner/snapshot changes using scoped active subscriptions or correctly placed native dispatch bridges. Do not simply remove the notifications that keep the simulation correct. At stable snow values, clean pieces must not be reclassified, rebound, or republished on every vanilla visual call.

Finish deactivation in a defined order: stop admission; settle/persist or clear owned seasonal state according to existing semantics; detach event/RPC/update subscriptions; restore native fields/materials/renderers; retire participants; clear transient data and disable the driver. Pending callbacks and worker results must not recreate the session. Loaded/unloaded/save/network cases must obey the same ownership rules. Native permanent snow and unrelated material property overrides must survive.

### Worker and configuration

Keep one background calculator; do not introduce per-object tasks. Stop submissions and cancel/invalidate feature work at deactivation, and release captured scene/delegate references. Late results from an old world, session, lease or object must not recreate registries or move a body. Do not Join/Wait on the main thread.

Use the existing unified JSON ValueChanged mechanism and main cfg events. Do not make startup activation depend on the presence of a user override file: defaults are valid. Respect asynchronous settings/season synchronization at join, winter-day/frozen-water conditions and normal runtime toggle changes.

## 7. Required verification and reporting

Static checks must cover callback registration/removal pairs, reentrancy during removal, mutation while native lists are being iterated, double registration/ticks, prefab versus instance handling, OnDisable versus OnDestroy, worker epoch/cancellation, exceptions during teardown, and loaded-world reactivation. No stale candidate/heartbeat, force accumulation, duplicate RPC, disabled native replica, missing component reference or disposed material still assigned to a renderer.

Provide a table for every removed/retained/replaced shared hot hook: old responsibility, new owner, activation condition, deactivation mechanism and any residual native bridge cost. Explain why any retained hook is required. Include a direct audit of the generic JSON path to demonstrate it was not specialized again.

Add or extend a cheap on-demand lifecycle status (not per-frame logging). It must report feature active/transitional state, registered drivers/participants, relevant queue counts, visual/material bindings, snow regions/heat entries, worker queued/running state and lifetime epoch. Reading the status must not initialize the feature or scan the world. Use existing diagnostics where possible.

After transition completion, dormant callbacks/ticks and work counters must remain unchanged through ordinary frames. Prove this structurally from dispatch and registration, not by a guard inside a still-called helper. Do not use stale 60-second profiler rows as evidence of ongoing calls; the maintainer should reset the measurement after transitions.

The maintainer's manual matrix (do not execute here):

| Scenario | Acceptance |
| --- | --- |
| Fresh spring load; both options enabled | No winter snow/floe recurring callbacks/participants. General Seasons and native water/wear continue. |
| Winter inland without local seasonal floes | No per-object floe checks on unrelated sync/water objects; placement can wake on legitimate Ocean zone loading. |
| Spring, same 27,000-piece base | Snow/floe runtime registries and queues empty after transition; no repeated winter cleanup or discovery. |
| Winter, snow off and floes off | Same dormant behavior; no patch/unpatch cycle. Native frozen-water functionality follows its separate setting. |
| Winter, one feature on and the other off | Only enabled feature's relevant services run. |
| Runtime off/on, season transitions, frozen/unfrozen interval | Finite cleanup, correct restoration, immediate floe deletion and correct reactivation without reload. |
| Ocean with same existing floe population | Same visuals, waterline, windy/calm motion and background reserve, measured without profiler first. |
| Approach/leave, contacts, overlapping floes | Native dynamic / ownerless kinematic / FarVisual transitions retain accepted behavior. |
| Second client and dedicated server | Lease/owner transitions, remote settings, snapshots and restoration remain correct; not yet certified by maintainer. |
| Deep North and native snowy mountain location | No loss of permanent native snow or unmarked native ice behavior. |
| Load/unload, save, disconnect, world change while jobs are active | No resurrection, stale scene references, stopped-but-ticking driver or wrong-world result. |

Measure identical population, camera, season/weather and graphics settings. Report uninstrumented FPS separately from profiler tables. Do not promise 165 FPS with 900 rendered floes; rendering and necessary instance motion still have a cost.

Deliver coherent reviewable commits, the routing/lifecycle audit and updated current documentation. Explicitly distinguish static verification from compilation, game behavior, network verification and measured performance, which remain with the maintainer. Do not finish with only a plan, an early-return patch, or a claim that moving method names out of the profiler proves deactivation.

## Reference anchors

- Current Seasons baseline: https://github.com/shudnal/Seasons/tree/a3e2775abd815680e795116ac87de016440e9379
- Native update registration: https://github.com/shudnal/assemblies_combined/blob/d1374bfd9175ac8f733ae483b0a06e5c8b75906e/assembly_valheim/Floating.cs and `ZSyncTransform.cs` in the same directory; inspect MonoUpdaters and WearNTear dispatch as needed.
- Unity enable/disable semantics: https://docs.unity3d.com/cn/6000.0/ScriptReference/Behaviour.html (native custom update lists still require their own lifecycle audit).
- Harmony callback flow: https://harmony.pardeike.net/v2/articles/patching.html (transpilation time is not runtime helper-call time).
