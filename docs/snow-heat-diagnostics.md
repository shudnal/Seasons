# Snow refresh diagnostics and native fireplace observations

Base: `cb3d4b58d2bc7ee22e5855d5dbaeeb7aed0da144` (Seasons 1.10.3).
Game source reviewed: `shudnal/assemblies_combined` at
`5a2365409cff644d6adaccd2b308178cc4179b19` (Valheim 1.0.16).

## Scope

This change identifies the producers of repeated snow-cover refreshes. It does not
narrow regional invalidation, change snow amounts, restore a saved zero, or change
heat strengths, source ranges, persistence, material rules, or the release version.

Registered heat areas are stationary. Their position, rotation, scale, and collider
shape are captured at registration, including the parent transform. Continuous
matrix comparisons have been removed. Primitive collider geometry remains usable
while its GameObject is inactive. The existing non-primitive collider fallback may
need an active collider to capture its native bounds; it is not a movement watcher.

## Fireplace state

A local transpiler in `Fireplace.UpdateState` copies the result of the existing
`IsBurning()` call without replacing that call or changing its branch value. After
the native method applies its active objects, the result is passed to the registered
snow heat source. The existing `!Fireplace.m_wet` rule and active-area checks remain.

Fireplaces are excluded from the snow polling list. `SeasonalSnowHeat` no longer
calls `Fireplace.IsBurning()`. Native `UpdateFireplace`, fuel interactions, and
on/off updates retain their normal behavior. Until a newly registered source gets
its first native sample, its currently active heat areas supply its initial state;
no synthetic `UpdateState` or `IsBurning` call is made to initialize it.

Non-fireplace sources keep their existing bounded activity polling. Their geometry
is also treated as stationary. Heat-area enable/disable events remain observed.
Only an actual area-activity change queues linked receivers, apart from initial
registration. Static topology reindexing is reserved for area registration/removal,
source-range settings, and the existing non-primitive activation fallback.

## Reading the hover output

Use the existing snow hover diagnostics setting. No additional config is required.
Counters accumulate only while that setting is enabled and reset with their
piece/region or winter/world lifecycle. Turning diagnostics off does not run a
cleanup scan; it simply stops collecting and showing observations.

- `Cover checks`: actual cover queries for the hovered piece and its region;
  `piece cover hints` counts native `m_haveRoof` changes on that piece.
- `Geometry events`: regional invalidation requests by cause, before queue
  coalescing. One object movement may issue old- and new-position requests to the
  same region. These are not counts of unique moved objects or actual cover queries.
- `Last geometry`: event cause, available source prefab/instance, ZDO ID when the
  event supplied it, event position, and elapsed unscaled seconds.
- `Heat watch (all)`: registered event-driven fireplace sources, reused native
  samples, other polled sources, actual activity changes, and completed reindexes.
  These heat counters describe the entire local snow runtime, not just the piece.
- `Last heat activity` and `Last heat reindex`: the source and elapsed time of each
  kind of change. Reindex reasons distinguish registration, removal, settings, and
  non-primitive collider activation.

No per-event log messages, stack traces, event-history lists, or diagnostic ZDO
writes are used. Source names and diagnostic text are resolved only during hover
formatting, at the existing 0.2-second refresh interval.

## Manual checks

1. On the affected base, keep the same camera position and let initialization finish.
   Capture two hover blocks 10-15 seconds apart while the fireplace burns steadily.
   Native samples should increase, but a stable fireplace should not continually
   increase activity changes or reindexes. Identify any growing geometry cause.
2. Extinguish and relight the fireplace through ordinary gameplay. Check nearby
   non-roof pieces: heat activity must change, but static heat geometry should not
   be recaptured or reindexed for a standard primitive area.
3. Add fuel while the fire is already burning. Native samples may increase; heat
   activity should not change unless native effective heat actually changes.
4. Repeat after loading an existing winter world and after enabling winter with
   an already burning fireplace. Check wet/blocked and out-of-fuel states as well.
5. Place and remove a building piece, repair a damaged piece, and change terrain.
   Verify that the diagnostic cause and source explain any regional cover work.
6. Disable diagnostics: no diagnostic counters should advance. Disable seasonal
   snow or leave winter: the native fireplace observer must not enter snow handling.

Static source/diff/API review only. No build, automated mod tests, or game runtime
execution was performed.
