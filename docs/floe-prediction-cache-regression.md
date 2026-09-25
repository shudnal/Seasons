# Floe prediction cache regression follow-up

Date: 2026-09-26.
Branch: `perf/snow-performance`, PR #45.
Base: `d36c91d3999d1fc38236fbb285aa1386e8467762`.

This is a targeted correction to `floe-distance-prediction.md`, not another
physics model, ownership protocol, wave spectrum or distance policy.

## Maintainer evidence and verification limits

The maintainer reports correct-looking motion but an FPS regression from about
9 to 5 after distance prediction. The supplied Patch Profiler capture shows:

| Instrumented Seasons patch | ms/frame | calls/frame |
| --- | ---: | ---: |
| Floating.CustomFixedUpdate prefix | 533.165 | 5920 |
| ZSyncTransform.ClientSync prefix | 11.613 | 6870 |
| WaterVolume.CalcWave six-argument prefix | 15.828 | 81620 |

Earlier captures showed approximately 130-137 ms/frame for the Floating prefix
and 47-53 ms/frame for the ClientSync prefix. These are different captures, not a
controlled benchmark. Patch durations may include nested instrumented methods and
instrumentation overhead. Their sum is not an exclusive engine frame time.

The counters implicate excessive wave work inside the floe controller; they do
not identify which forecast invalidation condition fired. No runtime hover with
cache-hit and invalidation counters accompanied this report. The exact runtime
trigger distribution and the performance benefit of this correction remain to
be measured by the maintainer.

## Defect found in source

The previous implementation compared `Root.lossyScale` with Vector3.Equals in
both ReadHullGeometry and forecast compatibility. Any bit-level change rebuilt
the hull cache, invalidated the forecast, and then sampled the complete future
window again. A rotating world transform is not a stable exact-equality key for
an otherwise fixed prefab scale. Floating-point decomposition noise must not be
interpreted as an intentional scale edit.

A forecast rebuild evaluates several knots. Each knot samples the four tilt
positions twice and, with UseFullWaterHeight enabled, samples full heave twice.
Repeated invalidation can therefore cost more than direct per-step sampling,
especially with several catch-up fixed steps in one rendered frame. Full heave
also adds genuine work compared with the earlier filtered-height model; it is
retained to avoid reintroducing the known visible waterline mismatch.

## Correction

Geometry now tracks the collider identity, root parent identity and exact
Root.localScale. World-scale changes are compared against the last accepted
geometry scale with per-component tolerance:

```text
abs(current - accepted) <= 0.00001 + 0.0001 * max(abs(current), abs(accepted))
```

The accepted value is not updated on every noisy observation, so cumulative real
changes still invalidate the cache. Explicit local-scale edits or reparenting
rebuild immediately even when smaller than the world-scale tolerance. Deliberate
collider or child-transform edits retain the existing RebuildHullGeometry action.
This remains the known immutable-prefab contract, not a hierarchy/mesh watcher.

The forecast uses the resulting geometry revision instead of reading and exactly
comparing lossyScale a second time. No wave heights, torque coefficients, buoyancy
coefficients or target submergence change.

A floe may attempt at most one complete forecast build per rendered frame. If
another physical step in that frame requires a new forecast, the controller
samples the current surface directly for that step. It does not interpolate
incompatible data, skip forces, freeze the Rigidbody, multiply a long time delta
into an impulse or change Unity's fixed timestep. Failed build attempts also
consume this per-frame guard. This is a per-object anti-amplification guard, not
a global frame-time budget or a promise that 900 dynamic bodies become cheap.

## Counters without per-floe timing instrumentation

Existing direct/builds/hits/query counters remain. Added per-instance observations:
GeometryCacheBuilds, ScaleNoiseReuses, ForecastKnotsSampled,
PredictionDirectFallbacks and LastForecastRebuildCause.

ScaleNoiseReuses counts observations that would have failed the old exact world
scale check but now reuse the accepted geometry. An increase does not alone prove
that scale noise was the only source of the original performance regression.

Forecast causes are separated into geometry, clock, water context, wind, settings,
probe spacing, authority, horizontal motion, scheduled completion, a shorter
horizon and external resets. The original thresholds for non-geometry inputs are
preserved. Unknown combined "inputs changed" messages no longer hide the cause.

`IceFloeClimb.PredictionPerformance` aggregates counts for all floes on this peer.
The counters are simple main-thread increments, not Stopwatch timings, per-frame
logs or full diagnostic water comparisons. They accumulate even when individual
DiagnosticsEnabled is false. Hover formats them only for the selected floe.

```csharp
Seasons.IceFloeClimb.ResetPredictionPerformanceCounters();
```

This resets aggregate performance counts only. It does not reset physics, poses,
leases, per-instance counters or valid forecasts. Aggregate ForecastBuilds counts
attempts, including failed samples; the existing per-instance PredictionBuilds
counts completed windows. Aggregate DirectFrames includes both FullRate work and
same-frame safety fallbacks. They are method counts, not numbers of rendered
frames or unique floes.

## Next maintainer pass

Keep the current scene, wind, render distance and physical settings. In particular
leave prediction and full water height enabled and RestingSubmergence at 0.7.
No new parameter sweep is needed.

After load/placement has settled, reset the aggregate counters once. Let the
scene run for about 10 seconds with detailed per-floe capture and hover inactive.
Then copy one full hover block from a Predicted floe. The global lines cover the
whole population, so pinning diagnostics on hundreds of floes is unnecessary.
Compare the same profiler view and the observed FPS with the previous capture.
A further FPS observation with profiling disabled distinguishes runtime work
from instrumentation amplification; it is not a replacement for the same-view
comparison.

Expected evidence: many forecast hits between completed builds; hull builds do
not track every rocking step; discarded scale-noise observations can grow without
forcing new geometry; non-scheduled resets are separately visible. If a different
cause dominates, use that counter rather than assuming the cache issue is solved.

## Static validation boundary

The reconstructed base file matches Git blob
`fdd4f64aab977ef14e7f39c57fd8ca62ae490c09` exactly. Validation covers source/diff
inspection, lexical delimiter/string checks and accidental Cyrillic scanning of
changed files. No mod build, automated mod test, physics simulation, game run or
performance measurement is performed here. No project Compile entry, dependency,
version, packaging, spawn density, ownership field, water transpiler or snow code
changes are required. Only the prediction source and this note are changed.
