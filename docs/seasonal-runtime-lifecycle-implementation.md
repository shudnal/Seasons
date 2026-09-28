# Seasonal snow and floe runtime lifecycle

## Review follow-up: lifecycle and geometry (base `7d71142`)

`SeasonalIceFloeWater` now takes ownership of managed floes at admission. A
single volume/list pass retires pre-existing entries during bulk activation;
individual later admissions inspect existing volumes once. Retirement removes
only the marked Floating entry and balances its native water counter. Sparse
`WaterVolume` enter/exit hooks prevent a managed floe from being re-added,
including in dynamic mode. Candidate overlapping volumes survive collider
deactivation; release tests actual collider penetration, restores each live
native list entry/counter/level once, and late triggers cannot double count it.
Volume destruction drops candidate references. Release during a disabled water
volume records one pending native restoration for its `OnEnable` boundary;
destroyed floes and volumes remove that pending reference. A new admission
reclaims the pending candidate, and only world teardown discards remaining
pending entries. The existing custom surface
sets Floating's live water level; unrelated interactables use unmodified native
`UpdateFloaters`. This water isolation is independent of the render toggle.

Floe policy now clears its zone-controller cache on feature deactivation.
Snow's finite retirement also restores transforms, removes owned copied caps,
and clears seasonal disabled/ignored/instance bindings while retaining donor
and rule definitions for reactivation. Configured permanent native-cap bans
remain event-driven and do not retain a seasonal binding. Status includes these
auxiliary counts. The wet/cap IL bridge uses Unity object validity and equality
operators for destroyed managed wrappers. Kinematic hull reads accept the cached
mesh/transform when the child collider is inactive; dynamic force checks still
require the active collider. Creation-triggered one-pass floe cleanup uses one
scheduled Update after the native `CreateObjectsSorted` traversal, avoiding
destruction of later entries still in its temporary list. The component then
disables itself; season/config cleanup remains synchronous.

These are static source findings, not in-game acceptance. The maintainer must
check overlapping water volumes, late exits, disable/re-enable, permanent native
snow, remote ownership, and the paused/transition behavior in Valheim.

## Review follow-up: 32 metre instanced floe visuals

The synchronized `Seasonal ice floes.json` now has one opt-in field:
`rendering.enableInstancedRendering` (default `false`). It follows the existing
`seasonalIceFloesJSON.ValueChanged` route. Toggling it retires visual slots and
does not alter placement, authority, forecasts, forces, root scale or ZDO
publication. Dedicated peers never create a render driver or materials.

`SeasonalIceFloeBatching` adapts KG-BatchRenderer 1.4.0's key, persistent
bucket, last-slot swap removal and grouped draw loop. Provenance and the selected
reference code are in `reference/kg-batchrenderer-core.md`. The definition is
prepared from the known `ice1/default` MeshFilter, MeshRenderer and MeshCollider
once, including its actual submeshes, shared materials, renderer settings and
`root.worldToLocalMatrix * default.localToWorldMatrix`. Thus `default`'s local
scale of four and each root's full, possibly nonuniform scale appear exactly
once in `root.localToWorldMatrix * visualLocalMatrix`. One managed instancing
material copy per distinct source material is shared across slots and disposed
when empty; source assets are never destroyed. Custom per-renderer property
blocks or unsupported probe overrides keep that instance on native rendering.
The definition refreshes on the ordinary JSON and season/day lifecycle events,
not by a material-array query in every frame.

| Motion transition | Ordering and retained state |
| --- | --- |
| Dynamic native to managed ownerless/FarVisual | Validate template and graphics support, cache active hull shape and COM/inertia, create a cell slot with the current pose, freeze the pre-hide physical values, then hide only `default`. Water isolation already belongs to the floe session. |
| Kinematic or FarVisual movement | Existing simulator/replica/bob writes the body pose; `SeasonalIceFloeDriver.LateStep` then updates its matrix slot. FarVisual tilt does not reassign cells for an ordinary pivot shift. |
| Kinematic to FarVisual and back | The slot stays assigned while the mode changes if renewed authority remains ownerless kinematic; a native dynamic destination retires it in the same state refresh. |
| Batch to native dynamics | Swap-remove the slot and repair the moved handle; reactivate the owned child, restore automatic or manual COM/inertia mode, then restore native body collisions/velocities. Native force work sees an active collider. |
| JSON toggle off, unload, feature exit or world reset | Slots are removed synchronously, surviving owned child state is restored, empty buckets/material copies are released and the render driver is disabled. Worker/session invalidation remains in the floe lifecycle. |

World cells use `FloorToInt(anchor.x / 32)` and `FloorToInt(anchor.z / 32)`,
including negative coordinates. The anchor comes from the cached geometric hull
center; ordinary kinematic rocking does not move it. A real XZ relocation
reassigns the cell. Matrix lists persist, are updated in place, and are split
into at most 500 instances per submission. `Graphics.RenderMeshInstanced` derives
aggregate mesh bounds; no square cell bounds or camera-selection/culling policy
is supplied. The one render driver draws in late execution order for all cameras,
including paused frames, and stops when the last slot leaves. A failed template
admission stays on native visuals; a graphics exception restores all slots once
and disables instancing until the next configuration/session boundary.

The on-demand status now reports native and batched floe counts, water-owned
memberships, cells, matrices, submissions, rejected admissions, render-driver
state, and snow auxiliary registries. These are maintained counts; status does
not discover objects. Dormant native snow IL bridges still execute a static
field branch, and sparse water trigger patches still run on trigger boundaries.
There is no new `WaterVolume.UpdateFloaters` per-interactable helper or
Patch/Unpatch cycle.

Static inspection checked the Unity 6 `Graphics.RenderMeshInstanced` list/ref
signature, `RenderParams` fields, `Renderer.GetSharedMaterials`, Rigidbody
automatic COM/inertia APIs, and `Physics.ComputePenetration` in the current
assembly set. Syntax, project entries, JSON and diff consistency were inspected.
No mod compilation, test harness, game run, FPS/profiler or network measurement
was performed. The maintainer should check in this order:

1. Winter Ocean with identical loaded floes, batching off then on: compare
   uninstrumented FPS, render CPU/GPU and submissions, with waterline, poses,
   scales and motion unchanged.
2. Ownerless simulator, replica/waiting pose and FarVisual: verify each current
   smoothed pose, pause drawing, freefly rotation, negative/crossed cell edges
   and large tilted anisotropic floes without clipped bounds.
3. Approach/depart by player and ship and separate overlapping floes: contacts
   and climbing return with dynamic `default`, with no gap, double draw or new
   impulse; check native owner priority and fallback leases.
4. With batching both off and on, inspect water-volume membership/counters
   through disabled `default`/collider, overlapping volumes, late exits and
   reactivation. No duplicate native water sampling should return.
5. Toggle JSON, disable floes, change winter day/frozen-water policy, destroy a
   floe, unload and change world: verify immediate slot retirement, one-pass
   marked-ZDO cleanup, no ghost draw or retained feature-only registry.
6. Check permanent native snow and unmarked Deep North ice, then a second
   client and dedicated server for remote poses, ownership, settings sync and
   headless resource absence.

Implementation base: `7d71142` on `perf/snow-performance`. Native source inspected at
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
| `Floating.CustomFixedUpdate`, `OnEnable`, `OnDisable`, `TerrainCheck`, `SetLiquidLevel` hooks on all floaters | Forces, registration, recovery suppression, water observation | `IceFloe` owns registration; shared driver calls force solver and scheduled native terrain recovery. Scoped water admission retires its native water membership. | No floe helper on ordinary items. `TerrainCheck` invoke and eligible native water membership are restored on release. Water observation fields unused by the fixed Ocean sampler were removed. |
| `WaterVolume.UpdateFloaters` transpiler calling `FloeWaterObservation` for every interactable | Avoid duplicate floe wave sampling | The native loop stays unchanged; managed floes are explicitly absent from its `m_inWater` list. Sparse `OnTriggerEnter`/`OnTriggerExit` boundaries maintain candidates and counters. | No Seasons call or type/registry lookup inside `UpdateFloaters`. Unmanaged objects retain native water sampling. A released surviving floe rejoins an overlapping volume once. |
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
interactable as part of Valheim, independent of this mod. Seasonal floes managed
by the scoped water owner are absent from `WaterVolume.m_inWater`; sparse trigger
patches still add a boundary branch when water events occur.

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
