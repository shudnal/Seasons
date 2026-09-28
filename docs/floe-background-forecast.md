# Rolling background forecasts for ownerless floes

Implementation base: `9e19676fd8bd45401e2d7d1a86f4c126492a2d05`, PR #45,
`perf/snow-performance`. This contract supersedes the earlier background-design
notes and local WaterVolume/depth sampling requirements. Date: 2026-09-26.

## Preserved behavior and scope

The maintainer accepted the visual waterline, native-owned dynamic interactions,
ownerless non-colliding kinematics, transitions between those modes, distant
flat-water bob, streaming placement and immediate seasonal deletion. These are
the regression boundary. Density, placement defaults, force coefficients, the
70-percent collider waterline, kinematic pose smoothing, replica smoothing,
lease election and publication cadence are not retuned here.

Only locally leased, ownerless kinematic floes send forecasts to the worker.
Native dynamic bodies retain their synchronous force path. Foreign kinematic
replicas only follow ZDO snapshots. FarVisual still publishes no decorative
motion and performs no full wave calculations. There is no second pose driver.

## Fixed Ocean surface

All floe sampling now uses the agreed Ocean approximation:

- normalized water depth is always 1;
- surface offset is always 0;
- base sea level comes from the world's ZoneSystem;
- full-spectrum, two-wind blended height controls heave;
- the existing filtered large-wave spectrum controls tilt;
- Deep North large-wave attenuation and the world-edge height drop remain.

There is no local WaterVolume selection, Depth call, or local offset read in the
floe sampler. Native water registration and explicit diagnostic comparison with
Floating.GetLiquidLevel remain on the main thread. The change does not patch
Depth globally or modify water for ships, characters or other floating items.
The fixed Ocean approximation is intentional, not a claim that every custom
water volume or shallow shoreline has exactly the same rendered surface.

The transient frozen-water flag suppresses waves until existing cleanup removes
floes. No frozen-ocean simulation is added. Deletion never waits for a worker.

## Shared immutable inputs

FloeWaveMath.Spectrum captures the native CreateWave delegate, a private copy of
the wave parameters and the constant secondary directions/tangents once per
world. The worker never reads or writes WaterVolume's shared direction arrays.
Direction zero always comes from the job's prepared wind, not the game array.

The maintainer explicitly treats CreateWave and TrochSin as trusted, unpatched
managed arithmetic. No Harmony patch scan, runtime compatibility probing or
alternate copied TrochSin implementation is added. The delegate's receiver is
retained only for that pure method call; the worker never checks Unity object
liveness, reads its fields or accesses Transform/Collider/WaterVolume properties.
A third-party patch breaking this contract is outside this supported path.

A common immutable wind snapshot is reused by all jobs. It contains prepared
effective-wind and two native wind-state directions/tangents, intensities and
blend. Normalization is reused even when only intensity changes. The snapshot
revision changes with the wind inputs, not with ordinary passage of time.
The running worker keeps its old complete snapshot when the main thread
publishes a newer one; no mutable parameter block is shared across the boundary.

A floe input adds a fixed geometric-center X/Z, sampling radii and four offsets,
full-height/spectrum settings, sea level and per-position attenuation, plus a
server-time/native-wave-phase anchor. These small spatial values are prepared
on the main thread when the input changes, not for every future time sample.
The worker's own CalcWave-equivalent summation calls the trusted CreateWave.

## One worker and rolling storage

There is one lazily started background Thread for the plugin. It sleeps on an
empty queue and never invokes a Unity callback or writes a ZDO. Queue bookkeeping
uses short critical sections; arithmetic runs outside the queue lock. No per-floe
Task.Run, main-thread Join/Result/Wait, job completion callback into Unity, or
main-thread execution of a full overdue horizon is used.

Each floe has at most one submitted block. The queue holds at most 2048 pending
blocks plus one running block. A block covers at most one server-world second
and contains knots with the configured spacing (0.1 to 0.25 seconds). Urgent
initial coverage and earlier deadlines sort ahead of speculative tail work.
The worker checks cancellation between knots and publishes an immutable result
through the requesting ticket. Each floe consumes its result on the main thread.
Completed result arrays are never recycled while a reader could still use them.

The main thread owns a bounded ring of time-stamped samples. It retains the
current segment, removes elapsed samples and appends only new tail knots; an
already accepted boundary knot is reused, not recalculated. Two 128-node rings
are retained per participating floe so replacement winds can be adopted without
mutating the curve currently being read. No whole-world snapshot is involved.

During startup or changing wind, useful coverage is grown in short blocks toward
about two seconds. Once the shared wind has been unchanged for two seconds,
remaining capacity is gradually filled toward BackgroundForecastSeconds (10 by
default). Subsequent work appends approximately one second per elapsed second;
it does not recalculate ten seconds each time. The stable reserve normally
oscillates below the target by roughly one block and scheduling delay.

Hermite interpolation and its derivative yield the current heights and vertical
velocity. Normals are reconstructed from the four interpolated heights. The
existing kinematic pose controller still applies the result once per rendered
frame. Interpolation remains main-thread arithmetic, but wave evaluation and
future knot construction execute in the worker. Total arithmetic is not claimed
to disappear; the purpose is to remove it from the game frame and amortize work.

## Wind changes, validity and cancellation

A newer wind revision does not immediately invalidate a usable curve. The floe
requests a short replacement at its next allowed wind-refresh deadline, using
its current distance-based PredictionSeconds with a 0.5-second floor and a small
per-floe stagger. It never waits for the entire ten-second reserve to expire.
An old speculative tail can be cancelled, but an in-flight replacement is not
cancelled repeatedly as wind interpolation advances.

While the replacement is pending the old valid curve is used. On adoption, its
current plane and plane velocity are expressed in the new wind axes. A cubic
correction fades to zero over the refresh interval, retaining height and slope
continuity where both curves have current coverage. A later small wind change
is accepted on another scheduled refresh, not by rebuilding every tick. Stable
long reserves resume only after wind stops changing.

Geometry/scale, sampling configuration, fixed X/Z displacement beyond tolerance,
sea level, wave enable state, native ownership revision, local lease token and
object identity determine hard input validity. A generic ZDO.DataRevision is
NOT a cancellation key. Publishing the floe's own pose therefore cannot cancel
its wave calculations. Current native ownership and lease eligibility are still
checked by the ordinary main-thread motion path before result use/publication.

World reset advances a worker epoch, cancels queued work and makes running/late
results inapplicable. Native wave-phase wrapping and a discontinuous server clock
also advance the epoch. Forecast blocks are clipped before the daily phase seam;
no Hermite segment bridges that discontinuity. Brief holds near that seam are
possible. Timed samples are evaluated at their actual server times, never
relabelled to start at the time the worker happened to finish.

Disable/unload, native dynamic activation, loss of the local lease, transition
to FarVisual or selecting the synchronous path cancels this floe's ticket and
releases captured inputs. Only the numeric rings remain reusable. World resets
leave the one worker asleep for reuse; plugin destruction signals it to stop.
No deletion or render frame waits for the thread to finish.

## Delays and failures

If initial or current coverage is not ready, the kinematic body retains its
last pose and publishes zero motion velocities. Its existing lease/heartbeat
continues normally: a queued forecast is not a failed physics step. No unbounded
extrapolation, whole-horizon synchronous rescue, pose teleport or automatic
fallback ownership churn is introduced. Expired results are discarded and
short urgent coverage is requested again. Once available, the existing pose
smoothing approaches the new surface. Short startup holds are expected; growing
wait counters in a warmed stable scene indicate a problem to investigate.

Queue-full conditions retry with a small stagger instead of blocking the main
thread. Worker exceptions produce an error result and a rate-limited warning
from the main thread, not a Unity/log call from the worker. A failed worker start
is exposed in diagnostics. Repeated failure retains the pose rather than secretly
returning all wave work to the main thread. Disabling the background option
explicitly restores the synchronous reference path.

## Configuration and inspection

No migration is introduced. Existing count 10..15, scale 1.25..2.5 and publication
interval 0.2 defaults are unchanged. The independent floe cfg gains:

```ini
[Ice floes - Background]
EnableBackgroundWaveForecast = true
BackgroundForecastSeconds = 10
```

Both entries are peer-local. EnableWavePrediction must also be true to use the
worker. BackgroundForecastSeconds is bounded to 2..10 and controls desired
reserve, not interpolation spacing or wind-reaction latency. Existing
PredictionKnotSeconds still controls spacing. MinimumPredictionSeconds and
MaximumPredictionSeconds retain their synchronous meanings and define the
distance-based background wind-refresh cadence, not the ten-second reserve.

The hover block for this path reports `Surface mode=Background rolling`,
reserve/target, accepted/discarded blocks, wait frames, last block queue/work
milliseconds, used/current wind revisions, adoptions and hard resets. It also
reports global queued/active/high-water counts, submitted/finished/cancelled/
rejected/error totals, calculated knots and accumulated queue/work time.
These timings measure worker bookkeeping/calculation, not frame time, rendering
or network throughput. A worker job can finish after a counters-only reset.

```csharp
Seasons.IceFloeClimb.ResetBackgroundForecastCounters();
```

This resets only cumulative worker counters. It does not discard forecasts,
restart the thread, reset individual floe counters, change settings or poses.
Explicit per-object diagnostics still include native/full-height comparisons;
leave them off while measuring FPS. The synchronous native-water comparison can
legitimately differ from the fixed-depth, zero-offset Ocean approximation.

## Maintainer verification

1. Keep the same existing population, camera, wind, size/count and publication
   settings. Compare EnableBackgroundWaveForecast=true/false with profiling off;
   do not respawn to attribute a population change to this optimization. After
   startup inspect a distant locally leased floe: rolling reserve should fill,
   blocks/hits should advance and steady-state waiting should stop or stay rare.
2. Change wind. Check deferred replacement, no ten-second reaction freeze,
   no target jump at the splice and no repeated hard resets. Report the complete
   hover block, uninstrumented FPS and any visible pause or error warning.
3. Recheck player/ship approach, separation of previously overlapping floes,
   FarVisual return, disable/re-enable, seasonal removal, pause, world reload and
   later two-client handoff. Dynamic forces and leases must not be duplicated.

No mod build, automated mod tests, game execution, physics simulation or
performance measurement was performed for this change. Verification is limited
to exact input file hashes, source/API and diff review, lexical C# structure,
project XML/Compile references and an accidental-Cyrillic scan. Numerical parity,
thread behavior in Valheim and a measurable FPS improvement remain unverified
until the maintainer runs the build.
