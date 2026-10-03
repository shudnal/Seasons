# Snow geometry events and profiler follow-up

Base: `83e3150d97ba04d0ef3cc4cdbd7889c71014ac85`, PR #50.
Game source reviewed: `shudnal/assemblies_combined` at
`5a2365409cff644d6adaccd2b308178cc4179b19` (Valheim 1.0.16).

## Health visuals are not snow-cover geometry events

`HealthVisualChanged` was a Seasons diagnostic category, not a native event.
The removed SetHealthVisual transpiler compared the activeSelf mask of
WearNTear.m_new/m_worn/m_broken before and after the native method. A changed mask
invalidated neighboring regions even without a change to the shared collision
shape. The observed MeadLightfoot/MeadSwimmer events came through this path; those
names are not special-cased.

Keep the existing native SetHealthVisual -> UpdateSnowVisual ->
TryQueueCurrentVisual path. It selects this piece's normal/worn/broken snow cap
without clearing its captured geometry, testing cover, or invalidating neighbors.
No replacement SetHealthVisual patch or per-call health-state capture is added.

The blanket ZDO position/rotation/deserialization geometry observer and the native
UpdateCover hint observer are also removed. Static obstacles are admitted and
retired through AddInstance/ResetZDO; OnPlaced retains its placement notification.
Snow snapshot receipt, owner-change checkpoints, save handling, area readiness,
and actual terrain/rock geometry notifications remain. This is not a removal of
snow synchronization or a change to other mods' ZDO/transform behavior.

The existing local position consistency check during an already requested piece
refresh remains; arbitrary moving structures are not continuously observed.
The legacy health/move and cover-hint diagnostic fields remain in the hover format
for comparison with earlier screenshots, but the removed observers cannot advance
them. No new diagnostic loops, event histories, or ZDO fields are introduced.

## Keypad neighborhood for local events

Partition the source zone into three equal columns and rows in world X/Z:

```text
7 8 9
4 5 6
1 2 3
```

- Cell 5 queues geometry only for the source zone.
- Cells 2/4/6/8 also queue the neighbor on that edge: two zones total.
- Cells 1/3/7/9 also queue both edge neighbors and their diagonal: four zones total.

The offset is measured from ZoneSystem.GetZonePos(ZoneSystem.GetZone(position)).
Third boundaries are at +/- one sixth of the zone width from its center. The zone
step is derived from GetZonePos instead of assuming that a negative world position
can be truncated to an integer zone. Exact internal boundaries include the edge
neighbor. Signed-short zone limits are checked before constructing a neighbor ID.

This applies to object addition/removal, placement, and the existing exceptional
local relocation path. Terrain, rock, and unclassified area events retain the
full previous neighborhood because their footprint is not a small point event.
This is the requested position-based policy, not exact collider-bounds routing:
unusually large or strongly offset modded structures may need explicit broader
invalidation. No per-event collider scan or spatial dependency index is added.

Geometry-only events queue Geometry, not Area | Geometry. Network membership
changes separately dirty readiness metadata in the native readiness neighborhood,
which can include all nine zones. This cheap metadata update does not enqueue a
cover scan by itself. IsAreaReady checks network-instance availability across
neighbors; reducing that footprint to the keypad footprint would conflate two
different dependencies. Existing retry intervals and confirmation passes remain.

## Floe profiler review (no floe changes in this commit)

The supplied General Method Profiler captures have nested scopes. The displayed
ms/frame values must not be summed into an exclusive CPU total. The first capture
reports 2623.5 BeforeSync and 2385 SimulatePhysics calls per frame, totaling the
5008.5 RefreshFloeState calls: the current fixed driver reaches it from both paths.
RefreshFloeState already has frame/owner/revision/pause guards, so this count is
not evidence of 5008.5 complete state recomputations.

Concrete remaining directions, separate from this snow change:

1. Avoid repeatedly dispatching non-physical ownerless/distant participants through
   the fixed-step physics entry. Preserve same-step native ownership transitions
   and revalidate after native ClientSync before physics work.
2. Keep common camera/reference/time inputs in a shared phase context instead of
   retrieving them repeatedly through fallback range and authority helper chains.
   Revalidate the lease/token/native owner immediately before motion/publication;
   do not cache permission across a handoff.
3. Assess a direct flat-surface path when UseWaves is false. The current kinematic
   path still maintains/evaluates a background curve in that case. Account for
   world-edge height discontinuities and invalidate forecasts on freeze/thaw.

Existing shared kinematic settings, geometry caches, decoded lease caching, and
background forecast jobs are already present. Re-adding those optimizations would
not address the remaining participant dispatch and main-thread orchestration.

## Manual comparison

Keep the same profiler selection, scene, camera, and loaded objects. After startup,
watch a stationary full snow cap: health/move/hint counters should no longer grow,
and cover queries should stabilize when no real obstacle/area event occurs.
Damage/repair a piece and check its own cap variant without regional geometry work.
Place/remove a small piece in cells 5, 8, and 7 to compare one/two/four geometry
regions, including negative-coordinate zones and exact internal boundaries.
Retain terrain excavation and rock destruction checks; those have broader events.
Check streaming, ownership handoff, and world-save persistence separately.

The 0.5 m fallback probe, initial snow calculation, integration, heat observation,
level-material compatibility, sliding, and release version are unchanged.
Validation: source/diff inspection and text/hash checks only. No mod build,
automated mod tests, or Valheim runtime execution was performed.
