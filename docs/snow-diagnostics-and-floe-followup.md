# Ice-floe simplification and targeted snow optimizations

Date: 2026-09-23. Branch: `perf/snow-performance`.
Last reviewed implementation: `e4f67d1563a232170a6c98aee31205be3d02a131`.
Status: agreed implementation task for Codex; not implemented by this documentation change.

This revision records the latest maintainer agreement and the complete Codex task previously supplied in the conversation. It supersedes conflicting ice-floe requirements in earlier revisions of this document, especially strict local-owner-only placement, per-tick support-point discovery, arbitrary sine bobbing, renderer proxies, and generalized physical-mode management. The previous corrective implementation report and its verification limits remain available in [this document at e4f67d1](https://github.com/shudnal/Seasons/blob/e4f67d1563a232170a6c98aee31205be3d02a131/docs/snow-diagnostics-and-floe-followup.md).

The goal is to preserve the original, proven floe approach and correct specific loading and computation problems. Do not introduce another general-purpose movement, renderer, or distributed-generation framework. The two snow optimizations below are independent of floe behavior.

## Agreed decisions

- A client with normally loaded, suitable placement geometry may initialize an unprocessed zone when its zone-controller ZDO has no owner or belongs to that client. A foreign owner blocks placement; ownership is not claimed. A host counts as a client for this purpose. Global cleanup remains server-side.
- Native ghost generation of an entire zone and client-side ghost initialization of individual floes are different operations. Do not restore temporary terrain generation.
- The per-floe watermark is written when that floe is created. The zone completion marker is written only after placement finishes. Cross-client simultaneous placement remains an accepted residual race; no reservation protocol is requested.
- All floe cache/state belongs to a non-MonoBehaviour singleton collection. Four local support points are built lazily and invalidated by at least 1 degree of floe horizontal heading or wind-direction change from the last cache build.
- Keep native Floating and the original four extra forces. Use game-derived wave mathematics for seasonal Ocean floes, with normalized depth 1f as an accepted approximation. Obtain effective wind through appropriate EnvMan methods, not direct wind-field/intensity reads.
- Compute distance using Water's NearSimulationDistance times zone size rule. Distant vertical motion must use the original Dampen applied to actual wave displacement, not an unrelated sine animation.
- Only two additional snow changes are authorized: avoid allocations for rejected heat-link candidates, and reuse identical per-pass live-weather calculations. Preserve snow behavior and persistence.

## 0. Working rules and references

Implement this task in `shudnal/Seasons`, branch `perf/snow-performance`.

Previously used local worktree, if still applicable:

```text
D:/work/codex/Seasons/.worktrees/snow-performance
```

Before editing:

- Read applicable AGENTS.md files.
- Inspect actual HEAD, git status, and uncommitted changes.
- Preserve all maintainer changes and newer commits.
- Do not reset the branch to the reference commit.
- Do not clean, force-push, rebase, or rewrite existing history.

Read game sources first from `shudnal/assemblies_combined`.

Previously reviewed game-source revision:
`d1374bfd9175ac8f733ae483b0a06e5c8b75906e` (Valheim 1.0.15).
Verify signatures and lifecycle behavior against the game-source revision matching the project before using APIs.

Original floe behavior reference:
`shudnal/Seasons` at `988e98c514ce49369a92cbaf934a7aca384fe6e1`, especially `Controllers/ZoneSystemVariantController.cs`.
Use that revision to recover the intended placement, four-point forces, and Dampen behavior. Do not restore the whole file or discard unrelated fixes made since then.

Relevant current files include:

- `Controllers/SeasonalIceFloes.cs`
- `Controllers/SeasonalIceFloeWaves.cs`
- `Controllers/SeasonalWorldMaintenance.cs`
- `Controllers/ZoneSystemVariantController.cs`
- `Utils/IceFloeClimb.cs`
- `WinterSnow/SeasonalSnowHeat.cs`
- `WinterSnow/SeasonalSnowLiveWeather.cs`
- `WinterSnow/SeasonalSnowSimulation.cs`

Do not build the mod, run Valheim, or execute tests. Static source, syntax, project-reference, diff, and API checks are allowed. Gameplay and profiling will be performed by the maintainer.

All repository content, comments, documentation, logs, and commit messages must be in English. The final report must be in Russian.

## 1. Preserve completed features and limit scope

Preserve:

- The completed snow simulation and its four regional buckets.
- Custom roof detection and event-driven geometry updates.
- Saved zero, clean new construction, distant prediction/confirmation.
- Existing heat formulas and the self-heat default of 2.
- `Fireplace.IsBurning() && !Fireplace.m_wet`.
- The explicit `ruleBiome` definite-assignment fix.
- SnowPeriod records, exact accumulation arithmetic, and RUE ToString().
- Existing env/resetenv semantics and weather checkpoint behavior.
- Pooled snow materials, child/LOD bindings, and visual budgets.
- Read-only diagnostic hover and its current numeric formatting.
- Lifecycle-driven environment-texture retirement.
- Ship freeze/thaw fixes.
- Bounded world maintenance and terrain processing.

Do not:

- Change versions, dependencies, packaging, or publication automation.
- Change config keys, defaults, snow JSON, or persistent ZDO schemas.
- Add config migrations or timeline logging.
- Reopen the resolved MyLittleUI forecast discrepancy.
- Redesign snow ownership, persistence, or the four-bucket simulation.
- Restore renderer proxies, copied meshes/materials, or universal floe physical-mode controllers.
- Introduce new MonoBehaviours to store per-floe simulation state.

Only the two snow optimizations in section 7 are authorized.

## 2. Floe placement: local geometry, not enemy-spawn ownership

### 2.1 Preserve the separation of responsibilities

Seasonal floes are placed by a client with suitable locally loaded geometry. This includes the player-facing side of singleplayer and a hosted game.

A dedicated server does not independently place seasonal floes.

Global seasonal cleanup remains server-side, using the floe prefab and the existing per-floe watermark.

After creation, ordinary Valheim ZDO loading/unloading and Distant handling manage the floe instances.

Do not restore temporary SpawnZone calls to obtain terrain. Do not add generation in terrain that has not loaded normally.

### 2.2 Distinguish native zone generation from floe ghost initialization

Inspect the current native chain:

```text
ZoneSystem.Update
-> CreateLocalZones / CreateGhostZones
-> PokeLocalZone
-> SpawnZone
-> PlaceLocations / PlaceVegetation / PlaceZoneCtrl
```

Do not assume PlaceVegetation contains a reusable client-ownership permission check. Native primary world generation and the mod's seasonal additions are different operations.

The original mod could initialize a floe through:

```text
ZNetView.StartGhostInit()
-> Instantiate floe
-> populate its ZDO
-> FinishGhostInit()
-> destroy the temporary GameObject
```

That does not require generating a new ghost terrain zone.

Preserve this narrow Full/Ghost floe-creation capability where useful. Balance ghost-init calls with try/finally, and do not leave ghost-init enabled across frames or queued work.

### 2.3 Placement readiness

Schedule a zone when its normal local geometry becomes available, using suitable existing lifecycle callbacks.

Examples to inspect:

- Successful PokeLocalZone.
- Arrival of the zone controller.
- Completion of deferred location loading / UnsetLoadingInZone.
- Existing WaterVolume/Heightmap initialization callbacks.

Do not require:

- The enemy-spawn/active-area radius.
- SpawnSystem.IsOwner() to be true when the zone has no owner.
- A client-side IsZoneGenerated() flag that normal Client-mode loading does not populate.
- Completion of all unrelated dynamic objects in adjacent zones.
- A hypothetical "entire zone loaded but not yet rendered" stage.

Require the terrain and static placement inputs actually used by the existing algorithm. Preserve location exclusions and collision checks.

Do not repeatedly invoke ZNetScene.IsAreaReady() for every loaded zone. If additional readiness checks are necessary, limit them to pending, unprocessed zones and perform bounded/event-driven retries.

In the original approach, IsAreaReady selected Full versus Ghost floe creation; false was not automatically a prohibition on placement.

### 2.4 Permission to process the existing zone

After obtaining a valid existing zone-controller ZDO:

```csharp
if (zoneZdo.HasOwner() && !zoneZdo.IsOwner())
    return;
```

Allow owner-zero and local-owner zones. Do not touch a zone owned by another network session. Do not claim or steal ownership for placement.

Use the existing zone ZDO even when the corresponding SpawnSystem instance is not yet suitable as an ownership gate. Avoid a global SpawnSystem scan on every update.

Check the completed placement marker before expensive discovery, candidate preparation, or copying sector objects.

Respect the current ZDO API when publishing an allowed owner-zero marker update. Verify that the chosen write path reaches ordinary replication without changing ownership.

### 2.5 Marker semantics

Use the existing markers, with distinct meanings:

- Per-floe watermark: set on each created floe immediately, so server cleanup can identify it.
- Zone placement marker: set only after the complete placement pass finishes successfully.

A completed pass with zero valid candidates is still completed. Deferred, cancelled, or failed work is not completed.

Do not set a persistent reservation before spawning. Do not introduce leases, lock RPCs, elections, or new reservation fields.

Prevent repeated local work with singleton-owned pending/in-progress state. Recheck season, zone validity, and the foreign-owner exclusion before continuing queued placement.

Simultaneous placement by two clients is an explicitly accepted residual race. Do not claim the marker makes generation atomic across peers.

Retain existing duplicate avoidance for already created floes, including partial prior work, without introducing a new persistence protocol.

### 2.6 Preserve placement behavior and keep its cost bounded

Preserve:

- Existing prefab and per-floe watermark.
- Placement seed and random-state restoration.
- Counts, scaling, mass, health, spacing, and clear areas.
- Candidate-level biome/altitude/depth checks.
- The coastal fix: a mixed zone must not be rejected solely because its center is not Ocean.
- Distant visibility of newly created and existing seasonal floes.
- Climb interaction.

The motion approximation in section 5 must not weaken placement checks.

Keep useful bounded placement work where it already exists. Do not expand this into a generalized world scheduler.

Do not hide an unbounded full-sector copy inside an allegedly bounded inspection step. Prefer early exits and narrow/reusable discovery, especially for already marked zones.

### 2.7 Cleanup stays server-side

Remove periodic client-side zone cleanup requests caused by ordinary SpawnSystem.UpdateSpawning or equivalent repeated callbacks.

A clean summer zone must not be repeatedly scanned for nonexistent floes.

Keep the existing bounded server maintenance path for seasonal removal and resetting placement markers.

Do not write an absent marker as zero just to "clear" it. Reset only an actually set marker, with a final recheck before writing.

Do not remove server cleanup merely because placement is client-side. Never set ZDO.OutsideZones manually as a visibility or simulation workaround.

## 3. Singleton-owned floe state

Store all new floe state and caches in a collection owned by a non-MonoBehaviour singleton in the mod.

Reuse/refactor the current floe implementation where practical. Do not distribute the same state across several new managers.

Cache only required references and values:

- Floating, Rigidbody, Collider/Transform, ZNetView, and relevant sync.
- Four local support points.
- Orientation and wind direction at the last point-cache build.
- Minimal data required to restore temporary distant/safety changes.

Create expensive geometry data lazily, when forces are first needed. Do not enumerate renderers, copy materials, or construct visual proxies.

Remove entries on destruction/unload, handle disable/re-enable without duplicates, and clear all state at world shutdown.

Do not move this cache into IceFloeClimb or a new component. Existing interaction components may notify the singleton but do not own its simulation data.

## 4. Preserve four-point forces and cache the geometry

Keep the original narrow patch around Floating.CustomFixedUpdate. Native Floating remains responsible for center buoyancy and damping.

Preserve the four additional force calculations, coefficients, ForceMode, mass scaling, and fixed-delta-time treatment.

### 4.1 Lazy point initialization

On the first valid physical update:

- Use the original four query directions: along/across wind.
- Find the four support points with Collider.ClosestPoint().
- Store them in collider-local coordinates.

Each subsequent update:

- Transform the cached local points to current world positions.
- Evaluate water height for those positions.
- Apply the original forces.

Do not cache world-space points. Do not run ClosestPoint four times per tick when the cache is valid.

### 4.2 Invalidation

Rebuild when:

- Floe horizontal heading changes by at least 1 degree from the heading recorded at the last cache build.
- Horizontal wind direction changes by at least 1 degree from the wind direction recorded at the last cache build.
- The relevant collider, its geometry, or its scale changes.

Compare against the last cache build, not the previous frame. Handle 359-to-0 degree wraparound correctly.

Do not invalidate for:

- Translation.
- Vertical bobbing.
- Ordinary pitch/roll.
- Wind intensity changes without a direction change.

Use a horizontal-heading calculation that does not deliberately turn ordinary rocking into a full-rotation invalidation.

Do not build points from a disabled/inactive/invalid collider. Handle a temporarily unusable horizontal direction without rebuilding continuously or generating invalid values.

Four fixed local support points are an accepted approximation. Do not attempt to reproduce the exact ClosestPoint result on every small tilt.

Do not add quantized XZ caches for liquid queries in this iteration.

## 5. Ocean wave sampling through the game's mathematics

Replace the four repeated spatial liquid searches with a narrow mathematical ocean-surface sampler for marked seasonal floes.

The required surface is derived from the actual Valheim wave formula, not an arbitrary sine animation.

### 5.1 Scope and depth approximation

For floe motion, use normalized ocean depth 1f. This is an accepted simplification for seasonal Ocean floes.

Do not introduce:

- A coastal-depth reconstruction system.
- Per-zone depth grids.
- Spatially quantized water caches.
- Temporary terrain or WaterVolume GameObjects.
- Global changes to Floating.GetLiquidLevel for other objects.

Placement restrictions remain unchanged.

### 5.2 Reuse the native wave calculation

Inspect:

- WaterVolume.GetWaterSurface.
- Both WaterVolume.CalcWave overloads.
- WaterVolume.CreateWave and water-time handling.
- EnvMan wind accessors.

Prefer reusing native mathematical routines where feasible.

If the relevant routine is instance-based, do not require the instance's collider to contain the sampled point, and do not accidentally use that unrelated instance's local Depth(point) or base height.

If a small instance-independent mathematical adapter is necessary, keep it narrowly scoped and traceable to the reviewed native formula. Do not duplicate the whole WaterVolume implementation or create a fake MonoBehaviour solely to call a mathematical function.

Retain relevant native phase/time behavior and world-position dependence. Use the correct world water baseline and applicable surface offset. Do not double-apply Floating.m_waterLevelOffset.

### 5.3 Obtain wind through EnvMan APIs

Use suitable EnvMan methods, not direct reads of internal wind fields or WaterVolume's cached global wind fields.

Use:

- GetWindDir() for effective current direction.
- GetWindIntensity() for effective current intensity.
- GetWindData(...) where transition inputs are needed.

Do not extract current intensity directly from Vector4.w in mod logic. Do not assume GetWindForce() or GetWindData() invokes GetWindIntensity().

The sampler must respect results modified through the relevant EnvMan methods, including the mod's own wind-intensity multiplier.

Do not apply a seasonal multiplier twice. Do not bypass patched accessors by calling a static/cached path that silently uses different wind data.

Use one shared wind/time snapshot per appropriate update cycle, not repeated accessor calls for every floe point.

Explain the chosen accessor/transition mapping in a short code comment and final report. Minor physical approximation is acceptable; silently ignoring effective wind overrides is not.

### 5.4 Central Floating level and missing water

Use the mathematical surface for the four additional force points.

When a marked seasonal floe lacks a valid local water-volume level, provide a consistent mathematical ocean level to its native Floating calculation as well. Otherwise native center buoyancy would still exit while only the extra forces run.

Keep this fallback local to seasonal floes. Do not pretend every arbitrary Floating object is always above ocean.

Never interpret -10000, NaN, or infinity as a physical water height. If valid inputs are temporarily unavailable, skip the affected force and retain only the narrow safety needed to prevent free-fall.

Do not leave a valid nearby floe kinematic or permanently gravity-free. Do not disable its collider. Do not suppress the "fell out of world" log instead of fixing bad inputs.

Any temporary gravity/sync change must be restored appropriately on:

- Return to normal buoyancy.
- Ownership transition.
- Unload/disable.
- Destruction or world shutdown.

Native sync may restore its own cached gravity and pose; account for that without adding a general simulation controller.

## 6. Distance and original Dampen behavior

### 6.1 Distance

Calculate the wave distance using the Water component's rule:

```csharp
NearSimulationDistance * ZoneSystem.instance.m_zoneSize
```

Cache the distance and its square centrally. Update them when synchronized simulation-distance settings change.

Do not read _VisibleMaxDistance from sharedMaterial. Do not restore the fixed 120-meter assumption. Do not impose a new one-zone physics radius.

For this iteration, retain a simple distance-based distinction between ordinary four-point physics and distant vertical presentation, using the corrected distance.

Do not add a new ownership/activity classification framework. Do not claim that visible water automatically gives a floe local ownership; native authority rules remain intact.

### 6.2 Distant motion

Restore the original Dampen behavior:

```text
Dampen(value) = value / (1f + Abs(value))

targetY = waterLevel
        + floating.m_waterLevelOffset
        + Dampen(surfaceLevel - waterLevel)
```

Here surfaceLevel must come from the same game-derived ocean wave sampler, with actual wind and time inputs.

Remove the current unrelated fixed-amplitude sine bob. Do not add random phase or a separate invented wave function.

Distant presentation:

- Changes vertical position only.
- Does not calculate tilt or apply four-point forces.
- Preserves XZ.
- Computes height from the water baseline, not from the previous bob.
- Does not publish cosmetic pose/velocity to ZDO.
- Does not accumulate offsets.
- Does not run cosmetic animation on a dedicated server.

Use the original narrow approach to local pose/sync isolation. Do not introduce renderer copies or pivots to achieve this.

Before normal sync, nearby interaction, or ownership handoff can use the object, restore its actual authoritative pose and sync policy. Do not restore an obsolete nonowner pose over a newer received snapshot.

Keep necessary work bounded, but avoid abrupt height jumps caused by infrequent large updates. Do not add another general animation system.

Whether this distant mode remains visually necessary at every current water-distance setting is a gameplay question. Preserve the specified Dampen fallback and report the exact activation condition.

## 7. Two targeted snow optimizations

These are the accepted items 4 and 6 from the performance review. Implement them separately from floe behavior changes.

### 7.1 Allocate heat links only for accepted candidates

File: `WinterSnow/SeasonalSnowHeat.cs`.
Method: `RebuildHeatLinks`.

Currently a HeatLink and its Weights array can be allocated before checking whether any area contributes to the piece.

Change this so rejected spatial candidates create neither the link nor the weights array.

Allocate lazily after the first positive contributing weight. Preserve all weights and index correspondence for accepted links.

Preserve:

- Distance-weight formula.
- Self-heat applied once.
- Maximum among active areas of one logical source.
- Sum of independent sources.
- Existing interaction priority and cached source-area topology.

Do not introduce pools, a new memory manager, or a topology rewrite. Do not change melt rates or defaults.

### 7.2 Reuse common live-weather calculations

Files: `WinterSnow/SeasonalSnowLiveWeather.cs`, `WinterSnow/SeasonalSnowSimulation.cs`, and related narrow runtime paths only as necessary.

Avoid repeatedly computing identical cumulative natural weather values for every piece of the same biome and interval while env override is active.

Reuse the existing per-pass biome/weather context where possible. Keep an individual calculation for pieces with genuinely different WeatherTime, catch-up boundaries, or ownership state.

Do not merge distinct intervals merely because pieces share a biome. Do not introduce an unbounded cache indexed by arbitrary timestamps.

Invalidate/rebuild the shared context at the appropriate boundaries:

- New update interval.
- Timeline/rule change.
- env/resetenv transition.
- Time jump or relevant season change.

Preserve:

- Live override replacing, not adding to, natural gain.
- Dry override consuming the suppressed natural interval.
- No double counting after resetenv, save/reload, or ownership change.
- Natural-history behavior for unobserved intervals and owner-zero prediction.
- Existing snapshot schema, publication policy, and exact arithmetic.

Do not time-slice the override-transition settlement by applying a new weather source retroactively to pieces that have not consumed the old interval.

## 8. Documentation, commits, and static checks

Update this document with implementation progress. Distinguish:

- Previously implemented behavior.
- Decisions superseded by this task.
- Changes actually completed now.
- Remaining gameplay checks.

Document:

- Native zone ghost generation versus mod floe ghost initialization.
- Local-owner/owner-zero permission and foreign-owner exclusion.
- Completion marker written after placement.
- Server-only global cleanup.
- Singleton-owned four-point cache and 1-degree invalidation.
- EnvMan accessor usage.
- Normalized depth 1f as an intentional approximation.
- Mathematical center/edge water levels.
- The original Dampen formula and current activation condition.
- The two local snow optimizations.

Work in small, coherent commits. Suggested boundaries:

1. `fix: initialize floes from ready local and ownerless zones`
2. `refactor: keep floe state in a singleton and cache support points`
3. `fix: sample ocean waves through native math and EnvMan accessors`
4. `fix: restore wave-driven distant floe dampening`
5. `perf: allocate only contributing snow heat links`
6. `perf: reuse per-pass live weather calculations`
7. `docs: record floe behavior and verification boundaries`

Combine tightly coupled steps when splitting would leave broken code. Do not create empty commits for already satisfied requirements.

Static verification:

- Harmony signatures and callback order.
- Ghost-init cleanup and random-state restoration.
- Owner-zero marker write path.
- No accidental ownership claims during placement.
- No extra per-floe MonoBehaviour or renderer hierarchy.
- No steady-state allocations in point/force updates.
- No direct wind-intensity field reads bypassing EnvMan.
- No cosmetic ZDO writes.
- No unsupported velocity writes to kinematic bodies.
- No recurrent summer scanning of already processed zones.
- Project includes all required files and excludes removed ones.
- No accidental Cyrillic outside intentional localization resources.
- No unrelated version, packaging, configuration, or schema changes.

Commit and push normally to perf/snow-performance. Do not open another PR or merge/close the existing PR. Do not modify master or feat/blood-moon. If pushing fails, report the failure and provide the result as an archive.

## 9. Final report and owner-run checklist

Report in Russian:

- Starting and final HEAD; initial/final worktree state.
- Commits and changed files.
- Actual placement callback/readiness conditions.
- Owner-zero and foreign-owner behavior.
- Full/Ghost floe creation and completion-marker handling.
- Which obsolete floe code was removed.
- Where singleton state lives and when point caches rebuild.
- Exact water/wind/time APIs and accepted approximations.
- Distant Dampen activation and sync isolation.
- What changed in heat allocations and live-weather reuse.
- Remaining uncertainty and checks not executed.

Do not claim compilation, tests, gameplay, GC measurements, FPS improvements, or network measurements were performed.

Provide this gameplay checklist for the maintainer:

- Singleplayer and ordinary client away from the host.
- Newly generated and previously generated loaded Ocean zones.
- Mixed coastline with Ocean candidates.
- Owner-zero, own owner, and foreign owner zone controller.
- Completed, zero-candidate, interrupted, and repeated placement.
- Ghost-created ZDO becoming a normal distant/near instance.
- Two clients approaching the same zone; accepted residual race.
- No periodic client cleanup work in summer; server seasonal cleanup.
- Floe collider, climb, collision, buoyancy, and unchanged force behavior.
- Slow yaw changes accumulating past 1 degree and 359/0 wraparound.
- Wind-direction changes, intensity-only changes, and patched EnvMan.
- Missing/unloaded WaterVolume with valid mathematical ocean height.
- Dampen driven by real waves, without tilt, random bob, or ZDO spam.
- Dynamic water-distance changes and return from distant to near mode.
- Unload, ownership transfer, shutdown, and season transitions.
- Kiln/beehive heat rates unchanged by allocation optimization.
- env/resetenv, dry overrides, skiptime/sleep, saved zero, and snapshots.
- Large-base and large-ocean profiles, separating initial loading from steady-state behavior.

## 10. Planning status and historical evidence

This revision saves the agreed task only. No source files, defaults, schemas, or runtime behavior are changed by this planning commit. Codex implementation has not been started by the assistant.

At the reviewed baseline, the maintainer/Codex report records the earlier corrective commits for self-heat, zone-owner placement, frozen ships, diagnostic formatting, live env/resetenv accumulation, native Floating restoration, sliced world maintenance, and the mixed-coast fix. Their implementation report is preserved at the immutable e4f67d1 link above; it must not be confused with completion of the new owner-zero placement, point-cache, mathematical sampling, or Dampen requirements.

The maintainer reported predictable snow behavior in ordinary gameplay and synthetic skiptime checks. The MyLittleUI discrepancy was resolved by its SnowStorm-only recognition setting. These observations do not establish performance on the original reporting user's large base, explain the earlier disabled floe collider, or constitute acceptance of changes requested here.

Before this documentation update, only the task, branch reference, existing documentation, and applicable repository instruction-file presence were checked for saving the agreed specification. No new implementation review, build, test, gameplay run, or benchmark was performed in this documentation-only operation.
