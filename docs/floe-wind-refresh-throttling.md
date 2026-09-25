# Deferred wind refresh for predicted floes

Base: `85b3c292da9827ff3cf188aed404afbd7a790cb3`, `perf/snow-performance`, PR #45.
Game reference: `shudnal/assemblies_combined` at
`d1374bfd9175ac8f733ae483b0a06e5c8b75906e`, especially
`EnvMan.UpdateWindTransition` and `EnvMan.GetWindData`.

This follow-up changes forecast scheduling, rollover continuity and distance
labels. It does not enable another kinematic body mode. It supersedes the earlier
requirement to immediately invalidate a predicted window for changing wind.

## Maintainer observations

The latest two global records contain the same 21,276 wind resets, while forecast
hits rise from 607,821 to 1,003,954. Scheduled rebuilds rise from 8,860 to 15,903.
This shows sustained cache reuse in that interval, not continuously broken geometry.
The separately reported wind command causes FPS to fall from approximately 120
to 30 temporarily. These are maintainer observations, not measurements made here.

The old forecast invalidation compared effective wind direction, intensity, both
native wind vectors and their blend against the accepted snapshot on every call.
During native interpolation, small changes repeatedly crossed those thresholds
for many floes at once. One-build-per-render-frame prevented multiple builds for
one object in a catch-up frame, but did not stop a population-wide rebuild wave.

## Scheduling rule

In Predicted mode, a changed wind snapshot marks `WindRefreshPending` once for the
current valid window. It does not invalidate the window, shorten it, restart its
clock, or schedule an immediate rebuild. The existing distance-dependent horizon
remains the refresh deadline. At the ordinary completion, the next forecast uses
the latest available wind snapshot. Continuing wind changes cannot defer that
deadline indefinitely. Existing first-window per-floe staggering is preserved;
a wind command no longer resets every floe's independent schedule.

Geometry, changed water context or frozen state, explicit sampling settings,
authority/token changes, clock discontinuities, actual horizontal drift and a
shorter required horizon retain their existing hard checks. They may still require
an immediate rebuild and are never delayed merely because wind is also pending.
Near FullRate sampling is unchanged. FarVisual continues to use its local flat
water datum and small bob, without full wave sampling or pose publication.

The time to adopt a new wind snapshot is bounded by the remaining current horizon
(up to two server-world seconds with the existing defaults). Convergence of the
new target is also smoothed across its new horizon. After a large abrupt change,
settling toward that snapshot can therefore take roughly two horizons, not a
promised maximum of two seconds. Continuously changing weather can produce a
small intentional lag. Native water rendering itself is not delayed or modified.

This is not a global frame-time budget. Newly loaded floes, real teleports, forced
clock changes or other hard invalidations can still create bursts. No FPS target
or absence of all frame drops is claimed without an in-game measurement.

## Preserve a world-space plane while changing wind axes

The four saved heights are attached to the old wind-aligned sample axes. Reusing
them verbatim with new axes would rotate the old plane even before new height
values are blended. At a compatible rollover the old mean height, world-space
gradient and their rates are re-expressed in the new axes and probe radii.
This preserves the fitted normal and its initial motion. It preserves the fitted
plane, not every residual curvature value of the original four samples.

With scale-aware probe spacing, validation uses the accepted wind axes for the
accepted window. A wind turn can no longer bypass the soft refresh by appearing
as a change in probe radii. Explicit ProbeDistance/ScaleProbeDistance changes and
real geometry/body-heading changes still invalidate the appropriate data.

When wind is pending, the new forecast also samples its raw first knot. The
difference from the continuous old value and rate is faded with a cubic Hermite
correction across the new horizon. Both heights and their rates use the same
correction and derivative. This avoids concentrating a large height change into
only the first quarter-second segment. The last derivative-support knot is beyond
the nominal horizon; the final interpolation segment can retain a small residual
until that support knot. Every compatible rollover preserves the current curve.

There is at most one additional raw knot evaluation per wind-adopting window,
not one per physical step. Unchanged-wind windows retain first-knot reuse.
No forces, body mass, drag, torque gains, physical timestep or waterline values
are changed. Dynamic contact results are not overwritten by pose/velocity writes.

## Diagnostics

Existing `Build causes ... /wind/ ...` is retained for comparison, but ordinary
wind changes no longer return the hard Wind rebuild cause. New observations:

- `Wind refresh pending`: a newer wind snapshot awaits normal window completion.
- `deferred/adopted`: per-instance counts of marked windows and successful builds
  that accepted a pending snapshot. Not counts of individual weather changes.
- `remaining`: remaining time in the current accepted horizon at the last sample.
- `All floes wind deferred/adopted`: the same counts summed for the local population.

`ResetPredictionPerformanceCounters()` resets the aggregate counts including the
new ones, without discarding forecasts, moving bodies or changing settings.
A window marked before reset can be accepted afterward; the two reset counters
are not required to pair within an arbitrary observation interval.

The ambiguous `distance` hover field is replaced by
`Distance XZ camera/reference/player`. All three values are horizontal distances.
The visible-wave boundary still uses the camera; the active area and forecast
horizon still use the network reference position/zone. Those policies are not
changed. A free camera near a floe can produce camera distance 3 m and player
distance 405 m without selecting FarVisual. The network reference need not equal
the camera or the player under every game/mod/free-camera setup.

## Kinematic follow-up: deliberately not enabled here

The maintainer also suggested kinematic distant floes. Current Predicted floes
remain dynamic, including both force feedback and contacts; FarVisual is already
kinematic. Setting isKinematic on a lease owner by hand is not a valid performance
comparison: UpdateSimulationAuthority currently rejects that body and releases
its lease, while the force driver also exits on a kinematic body.

A future kinematic Predicted mode needs its own pose driver, publication of
consistent pose-derived velocities, lease eligibility, replica handling and safe
restoration of original body/sync state. It must not steal native ownership.
Near interactive floes should be promoted before contact based on player/creature/
ship proximity including bounds and approach velocity, with hysteresis before
returning to the cheap mode. A collision callback can be a fallback request, not
the sole activation mechanism: a kinematic body has already given the other body
an effectively immovable contact, and disabling collision detection removes that
notification entirely. This requires a separate reviewed change; no new live
kinematic switch is exposed by this commit.

## Maintainer check and verification boundary

Keep the existing physical settings and scene. With profiling and detailed capture
off, observe baseline FPS, issue one wind change, then inspect the response and
copy hover after a few seconds. Hits should continue during pending wind changes;
wind resets should not rise, while deferred/adopted counters should progress.
A second command before the first change finishes should not continually reset
forecast deadlines. Check a large direction reversal and scale-aware probes as
separate optional checks; near FullRate and FarVisual should retain their paths.

Only the prediction source and this note change. Validation is limited to exact
base/delivered blob hashes, static source/diff review, lexical checks and changed-
file Cyrillic scanning. No mod build, mod tests, physics simulation, game run or
performance measurement is performed. Cleanup, lease protocol, spawn density,
project Compile items, dependencies, version and snow code remain unchanged.
