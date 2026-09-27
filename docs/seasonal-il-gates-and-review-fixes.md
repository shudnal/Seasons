# Inactive IL gates and Codex review corrections

Base: `660f2fdb62e485633b40a740e2c4b381c1108fd9`, `perf/snow-performance`, PR #45.
Date: 2026-09-27.

## Dispatch changes

Both exact `WaterVolume.CalcWave` overloads now use one transpiler. Its entry
reads the existing `ZoneSystemVariantController.s_freezeStatus` field directly:
when it equals 1, the body returns a zero wave displacement; otherwise the
original instructions run. There is no per-call Seasons prefix or policy helper,
new policy flag, season poll, or runtime patch/unpatch operation. `CreateWave`,
`TrochSin`, floe samplers, fish scheduling and water registration are unchanged.

The branch target is a new nop before the original instructions. Original entry
labels and exception boundaries remain attached to the original instructions;
the early return is outside those regions. The unordered-not-equal branch also
preserves the original false result of `s_freezeStatus == 1f` for NaN.

`WearNTear.SetHealthVisual` no longer has a Seasons snow-geometry prefix/postfix
pair. The existing VisualBridgeActive flag gates capture in the method body.
An explicitly initialized -1 local means no observation was captured. Only a
nonnegative captured mask reaches the exit notification; that handler retains
the current ObservesSnowGeometry and changed-mask checks. Return labels and
exception metadata move to the inserted exit check. Native health visuals,
roof logic, the seasonal texture UpdateCoverStatus hook, and permanent native
snow continue unchanged. No per-piece state registry is added.

These gates eliminate managed callback entries on inactive paths, not every
added instruction. An early return inside the body is not Harmony's
`Prefix -> false`: patches inspecting `__runOriginal` will see that the body ran.
Likewise, the snow notification is now at the native body exit, before Harmony
postfixes. This is not a promise of identical ordering for arbitrary third-party
patches that alter visuals in their own postfixes.

## Review 4113971719: local queries of shared placement exclusions

The accepted session-wide exclusion data remain shared between placement jobs
and survive zone unload. The container is now a static dictionary of zone-local
lists. Each existing circle is inserted into every zone touched by its XZ
bounding square. Candidate center checks use only the point's zone, retaining
the native InsideClearArea square test. Radius checks visit only zones covered
by the candidate's own bounding square and retain the existing IsInside test.

Intersecting circles have intersecting bounding squares and therefore share a
queried cell. The search does not assume an exclusion fits in one zone or only
one ring of neighbors. Negative coordinates use the same ZoneSystem.GetZone
mapping as the game. Duplicate location hints are checked in the center cell,
not by scanning every circle accumulated during the session.

Neither candidate scans the entire visited-world collection. No frame task
maintains the index while placement is idle. World/policy cleanup clears all
buckets and the count. Reservations are not discarded on zone unload, which
would reopen the original cross-zone placement gap. As before, this is session
placement memory, not a synchronized occupancy database or moving-body index.
Its storage still grows with reservations until session/policy reset; candidate
work now depends on local occupancy, not distant visited zones. This does not
make instantiation or arbitrary dense local content a hard 1.5 ms operation.

Candidate dimensions, root scale/depth factors, the 0.2 m candidate margin and
GetFloeSize(instance) + 0.5 m stored radius are unchanged. No additional local
scale factor, geometry recalibration, respawn policy, or 96 m render-grid coupling
was introduced. Status keeps the unique `exclusions` count and adds
`exclusionCells` for occupied index buckets.

## Review 4113971721: disable clears unloaded snow snapshots

OnEnabledConfigChanged still refreshes the weather timeline and reconciles the
loaded runtime. When the effective setting is disabled on the server, it also
performs one pass over existing ZDO records. Only records with Seasons value,
epoch or legacy seasonal metadata are cleared through SeasonalSnowStorage.Clear.
This includes records without a loaded GameObject. The pass does not spawn
objects, change native ownership, remove ZDOs or alter the save schema.

Current and legacy seasonal snapshot metadata are removed, so re-enabling in
the same calendar winter cannot accept the old saved value by epoch alone.
Placement metadata keeps its existing independent meaning. Unmarked native
snow is not selected. Native snow keys are cleared only for legacy seasonal
records outside Deep North, preserving permanent native snow. The existing
loaded teardown/visual queues still run to completion; the global data reset
is an explicit config event, not a dormant update scan.

After re-enable, an object with no snapshot can legitimately get the existing
natural-weather prediction. The fix prevents importing the stale stored value;
it does not promise snow-free scenery while winter simulation is enabled.

## Verification boundary

The four source baselines were matched to their Git blob hashes before editing.
Review covers the narrow diffs, return/exception-label handling, native square
clear-area semantics, footprint coverage, unchanged radius expressions, server
selection, snapshot removal, English repository text and lexical C# structure.
No mod build, automated mod tests, substitute runtime harness, Unity launch,
multiplayer run or performance measurement was performed.

Maintainer checks: reset profiler stats after switching winter/spring; the two
CalcWave prefixes and the SetHealthVisual snow prefix/postfix should no longer
receive calls. Native target rows may remain. Confirm frozen zero waves and
normal thawed waves, then repair/damage snow-covered pieces during winter.
For placement, generate fresh adjacent zones with the same scale/density/gaps
and compare local candidate service after long travel. Existing overlaps are
not moved. For snow, unload a snowy piece, disable/re-enable snow in the same
winter, then reload it; repeat with save/reload and a second client. Check that
its old snapshot is gone and native Deep North snow remains unchanged.
