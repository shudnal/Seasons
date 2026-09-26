# Seasonal snow and floe runtime lifecycle

Implementation base: `b5ce33e` on `perf/snow-performance`. Native source inspected at
`shudnal/assemblies_combined` `5a2365409cff644d6adaccd2b308178cc4179b19`;
the relevant native files have no diff from the brief's `d1374bfd` reference.
This document describes the implementation, its remaining native bridge cost,
and the maintainer's game checks. It does not report a build or a gameplay result.

## Sessions and activation

`SeasonState.WorldInitialized` becomes true only after the current world's
season state is constructed. Floe policy is reconciled from that initialization,
the connected peer notification, water/season/config changes, and zone-entry
notifications. A day-zero unsynchronized season cannot trigger server deletion.
The server still removes marked loaded and unloaded ZDOs and resets placement
markers in one pass. A newly loaded marked ZDO outside the interval requests an
immediate cleanup pass. Placement is budgeted only while there is a loaded-zone
discovery cursor or queued zone work; an empty queue disables its component.
The zone lifecycle hooks re-enable it when actual new work arrives.

Only marked, initialized `IceFloe` components join the floe participant list.
Activation walks already loaded components once; newly loaded marked ZDOs get
their component from `ZNetView.Awake`. New placements add it only after their
watermark and saved mass are written. The shared `ice1` prefab is not modified
with a component or sync-scale flag. Unmarked native Deep North ice stays on
the native path. When a marked floe is admitted, its active `Floating` and
`ZSyncTransform` are removed from the native `Instances` lists and are serviced
by one enabled component on `ZoneSystem`. Neither native component is disabled.
The driver calls client sync before the floe force pass and owner sync after it,
retaining BeforeSync, native-acquisition safety, ownerless motion, and bounded
fallback publication in their previous order. Native terrain recovery is
scheduled on that participant driver at the former 10-30 second initial delay
and 30 second cadence, only for nearby non-kinematic floes. Untracking restores
native list membership and `TerrainCheck` scheduling. The driver is disabled
when the last participant leaves. A stopped session invalidates worker epochs,
cancels queued tickets and releases the captured ocean spectrum; running work
checks cancellation between knots and cannot publish into the new epoch.

The snow driver is attached to `ZNetScene` and enabled only for an active winter
or finite retirement. It runs the existing singleton simulation and bounded
visual queue after the native update phase. Season/config changes start it or
allow it to retire pieces, clear authorized saved state, unregister RPCs and
empty region, heat, interaction and publication collections. After queued hides
drain, `ResetVisuals` restores native renderers/material ownership and disposes
pooled materials before the driver is disabled. The teardown can proceed while
the game is paused; an active winter simulation still respects pause.

## Routing inventory

| Shared native route before | Responsibility | Route now and activation | Deactivation / residual cost |
| --- | --- | --- | --- |
| `ZSyncTransform.ClientSync` and `OwnerSync` Harmony hooks on all transforms | Floe state refresh, suppression, acquisition, ownerless pose and publication | `SeasonalIceFloeDriver` calls native sync for its marked participant list in fixed/late phases | Driver disabled and native list membership restored on release. No sync hook or lookup on unrelated transforms. Native direct `SyncNow` and character-parent sync remain native; no known floe call site uses those paths. |
| `Floating.CustomFixedUpdate`, `OnEnable`, `OnDisable`, `TerrainCheck`, `SetLiquidLevel` hooks on all floaters | Forces, registration, recovery suppression, water observation | `IceFloe` owns registration; shared driver calls force solver and scheduled native terrain recovery. Native water trigger registration/level updates remain intact. | No floe helper on ordinary items. `TerrainCheck` invoke is restored on release. Water observation fields unused by the fixed Ocean sampler were removed. |
| `WaterVolume.UpdateFloaters` transpiler calling `FloeWaterObservation` for every interactable | Avoid duplicate floe wave sampling | Native `GetWaterSurface` is restored for all interactables; floe forces continue to use the accepted fixed Ocean sampler | No Seasons call or type/registry lookup for ordinary water interactables. This gives marked floes their native liquid-level calculation again; compare water cost in game. |
| `Hud.UpdateCrosshair` floe postfix | Diagnostics on child hover targets | `IceFloe.GetHoverText` retains direct component hover diagnostics | No global hover lookup; child objects with a different Hoverable no longer append floe diagnostics. |
| `ZoneSystem.Update` floe postfix | Policy checks, loaded-zone walk, placement, cleanup | Config/season/zone notifications reconcile policy; enabled placement component services bounded work | Disabled with no cursor/queue. One-time cleanup is immediate. `SeasonalWorldMaintenance` retains its separate terrain Update route. |
| `ZNetScene.Update` snow postfix | Simulation and visual queue | Enabled `SeasonalSnowDriver` on the scene | Disabled after retirement/visual release; no dormant scene postfix. |
| `ZNetScene.CreateDestroyObjects` snow postfix | Reference-area readiness invalidation | Active snow driver checks once per frame | No callback outside the active snow driver. |
| `Player.UpdateAttach` snow postfix | Attached-piece use melt | Active snow driver checks the local player | No per-player snow callback outside an active session. `AttachStop` remains an event hook. |
| `WearNTear.UpdateSnowVisual` prefix | Route seasonal caps and isolate native permanent snow | Transpiler branches on a static active-session field before calling the same visual router | Dormant path is one field load and branch, then native code. No Seasons helper, registry or name lookup. |
| `WearNTear.UpdateCover` prefix/postfix | Detect roof changes | Transpiler captures old roof only while active and calls the controller only after an actual change | Dormant path is a static branch at entry and return. Native cover logic is untouched. |
| `WearNTear.UpdateWear` wet-field redirect | Protect shared wet/snow cap objects | Transpiler uses native wet field directly when dormant and no cap aliases it; aliases still call the existing rule-aware helper | Dormant non-alias path has field/null/reference comparisons, no Seasons helper or registry lookup. Alias handling remains to preserve shared wet/snow objects. |
| `WearNTear.UpdateWear` native-snow prefix/postfix | Clear seasonal native fields before/after wear | Transpiler calls field isolation only in an active session and only when a native snow field is nonzero | Dormant path has a static branch. `Awake` still performs one-time legacy cleanup. |
| `ZNet.SetReferencePosition`, `ZDO.SetOwner`, `ZDO.Deserialize`, `ZDO.SetOwnerInternal`, `ZDO.InternalSetPosition`, `ZDO.SetRotation` snow hooks | Save checkpoint, snapshot and geometry invalidation | Inline native branches call the same handlers only while the snow session/retirement is active | No controller entry while dormant; each native method retains a field load and branch. Network and transform semantics are unchanged. |
| Snow `WearNTear.Start/Awake/OnPlaced/OnDestroy`, `ZNetScene.AddInstance`, heat/station, health, rock/heightmap, save and scene shutdown hooks | Admission, native snow isolation, geometry and persistence events | Retained as sparse object/world events | Required to wake or retire relevant pieces and to preserve ownership/cover changes. They do not schedule an inactive simulation pass. |

The remaining branch instructions are real native cost, so this is not a claim of
literal zero added instructions. The static bridge branches avoid callback
dispatch to Seasons and lookup on ordinary dormant frames. Floe direct
`Floating.SetLiquidLevel` and native liquid registration still run for every
interactable as part of Valheim, independent of this mod.

`SeasonalPlayerCapeSnow` follows live environment snow on the player's cape,
including native snowy biomes; `SeasonalEnemySnow` applies snow to creature
visuals that spawned in snowy weather; `SeasonalVagonSnow` extends the native
wagon winter condition. These are separate from piece buildup and from floe
placement. Frozen water, fish under ice and the general Seasons calendar retain
their own routes.

## Configuration and on-demand status

The main cfg still owns `Enable ice floes in winter`, the day interval and floe
health. Advanced defaults still come from `IceFloeConfiguration` if
`seasonalIceFloesJSON.Value` is empty. `SeasonSettings.ReadConfigFile` continues
to resolve both snow and floe JSON through the same `CustomSyncedValue` path;
`ValueChanged` still invokes the respective settings loader. No local shadow
file, migration, version or reload path was added.

`Seasons.GetSeasonalRuntimeStatus()` reports the snow state/driver, pieces,
regions, refresh/publication/visual queues, bindings, material pool, heat and
interaction counts, plus the floe participant/driver, placement queue/cursor,
worker queue/running state and epochs. It reads existing counters and does not
initialize a session or scan the world. During teardown, the driver and work
counts may be nonzero; sample again after they reach dormant. Reset the profiler
measurement after that transition. The old 60-second row includes calls made
before retirement and is not evidence of current recurring calls.

## Static review and game verification

The implementation was checked against native `Floating`/`ZSyncTransform`
`Instances` registration, `MonoUpdaters` phase order and copied iteration,
`Floating.Awake` invoke scheduling, water trigger dispatch, and ZDO/ZNetScene
signatures. Removal during a driver pass uses a stable scratch list with live
membership checks. World/feature teardown clears participants and late worker
results are rejected by epoch. The renamed component has one project compile
entry and no compatibility alias or second subscription.

No compilation, automated tests, Valheim run, multiplayer check, FPS capture or
profiler measurement was performed here. The maintainer should verify in this
order, keeping population, camera and graphics settings fixed when comparing:

1. Fresh spring load and the same large base after winter retirement: inspect
   `GetSeasonalRuntimeStatus` after queues drain, then reset the profiler and
   confirm dormant drivers, empty piece/visual/worker queues and no recurring
   snow/floe profiler rows. Check native mountain/Deep North snow and water.
2. Winter with both options off, then each option on separately. Change day
   range and frozen-water setting during play; verify immediate marked-floe
   deletion and marker reset, plus one-time loaded-zone reactivation.
3. Winter inland with no local marked floes. Confirm the floe participant driver
   sleeps after placement discovery and wakes for a newly streamed eligible
   Ocean zone. Check no unrelated sync/water routing appears.
4. Same Ocean floe population as the accepted baseline: uninstrumented FPS
   first, then fresh profiler capture. Check waterline, calm/windy motion,
   forecasts, climbing, contacts, overlap, distance transition and terrain
   recovery before treating any performance change as an improvement.
5. Snowy large base: check caps, wet aliases, health and roof changes, heat,
   shields, use effects, copied meshes/LODs, persistence and unloaded catch-up.
6. Second client and dedicated server: native ownership priority, fallback
   leases, replica poses, settings sync, save/unload/disconnect/world change
   while a forecast is running. Check no old epoch result moves a new object.

If any route differs in game, the game observation supersedes this static audit.
