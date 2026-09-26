# Floe runtime configuration and repeated-work reduction

Initial implementation: `55445c9`, PR #45, `perf/snow-performance`.
Current follow-up base: `1a3f1e9359a0800fd68c2f36559288d839c8655b`.
Date: 2026-09-26. This contract supersedes the initial legacy-value migration.

## Accepted baseline supplied by the maintainer

The maintainer reports that immediate deletion completes without perceptible lag;
waterline, distant spawning, flat far-water bob, forecast-based motion, native
activation and deactivation are visually correct. Nearby collisions work. Two
floes can overlap in ownerless kinematic mode and resume dynamic separation on
approach without a noticeable transition. This approach is retained.

The reported ocean comparison is about 165 FPS without floes (688 instances),
and 60-70 FPS with floes (about 1,550 instances). Calm and strong winds have a
similar steady cost. These are maintainer measurements, not measurements made
while developing this change. Multiplayer election/replication acceptance is
still separate from the reported local visual and collision acceptance.

At 165 versus 65 FPS, total frame time changes from about 6.06 to 15.38 ms. The
roughly 9.32 ms difference includes every consequence of the additional objects,
including rendering and engine work. It is not all attributable to a particular
patch or to wave mathematics. Instrumented patch totals must not be subtracted
from these uninstrumented frame times. The 60-second percentile table describes
individual invocations, not whole-frame costs.

## Independent configuration file: no migration

All floe settings are initialized before Harmony patch registration and before
connecting to a world. The generated file is:

```text
BepInEx/config/shudnal.Seasons.IceFloes.cfg
```

The five former floe entries and floe logging entry are removed from the main
config. Their values are NOT transferred, inspected to choose defaults, or used
to initialize the separate file. Only the old entries are discarded; unrelated
main-config settings and unrelated orphaned entries are retained.

BepInEx 5 preserves unbound entries when saving. Removal therefore consumes each
obsolete definition once as opaque text and removes it via the public ConfigFile
API, while SaveOnConfigSet is false. The discarded entry's Value is never read,
registered with ConditionalConfigSync, or assigned to a new setting. This is
obsolete-key deletion, not migration or mirroring.

If the separate file is absent, it receives the new defaults. If it already
exists, its own configured values are retained, including values previously
written by an older development build. There is no heuristic to distinguish old
transferred values from intentional edits. Missing entries use current defaults.
Removing the separate file while the game is stopped regenerates defaults on
startup; the main file will not repopulate them. No world ZDOs are migrated.

| Setting | Default | Meaning |
| --- | --- | --- |
| Enable ice floes in winter | true | Existing seasonal feature switch |
| Fill the water with ice floes at given days from to | 4 / 10 | Inclusive winter days |
| Amount of ice floes in one zone | 10 / 15 | Placement attempts in one 64x64 zone |
| Scale of ice floes | 1.25 / 2.5 | Base random scale before shape/depth adjustments |
| Health of ice floes | 20 | Base health before volume/world-level scaling |

The maintainer accepted the larger scale range and maximum of 15 attempts per
zone. This is not a world-wide or on-screen cap. Placement exclusions can reduce
the actual count. Size, health and count changes affect subsequent spawning, not
already saved floes. Disable and re-enable during an eligible winter period to
regenerate the same ocean with different placement settings.

Seasons' existing ConfigEntry fields reference the independent entries, so
placement, season callbacks and immediate cleanup have one source of settings.
There is still one ConditionalConfigSync instance and one configuration lock.
Generation, physical coefficients and the new publication interval are
server-controlled. Prediction quality, far-visual bob, fallback participation and
floe logging remain peer-local. Direct RUE edits to runtime static fields are
still temporary diagnostic edits, not persistent or synchronized settings.

The working buoyancy, drag, tilt, waterline, kinematic response and forecast
coefficients are unchanged. Ranges reject non-finite numeric inputs. SettingChanged
callbacks update shared runtime fields; entries are not polled for every floe.
The file watcher coalesces changes and reloads on the main thread. Its callback
thread never accesses Unity objects. Manual actions remain available:

```csharp
Seasons.SeasonalIceFloeSettings.Reload();
Seasons.SeasonalIceFloeSettings.ApplyConfiguredRuntimeValues();
```

The second action reapplies effective settings after temporary RUE experiments,
without file writes or pose resets. IceFloeClimb.ResetSurfacePhysicsSettings()
restores the compiled physics diagnostic baseline, not a saved cfg. It does not
reset fallback participation or the new publication interval. Listing the second
file in every Configuration Manager variant is not assumed.

## Less frequent kinematic publication

The new server-controlled setting is:

```ini
[Ice floes - Authority]
KinematicPublishIntervalSeconds = 0.2
```

The interval is validated to 0.1..0.25 seconds. The existing per-floe staggering
adds up to 0.02 seconds. Thus the default is approximately 4.5-5 publications per
server-world second instead of 8.3-10. Setting 0.1 restores the previous cadence.
A rendered frame may add scheduling delay. No exact network packet rate is
promised: native ZDO transport still batches and schedules its own updates.

Only an ownerless kinematic lease holder uses this interval. Local pose tracking
continues every rendered frame; native-owned dynamic forces and ordinary game
synchronization are unchanged. FarVisual still publishes no decorative motion.
The initial lease publication remains immediately eligible. Later deadlines
start at the actual publication time; there is no catch-up publication loop.
Changing the cfg takes effect when the next deadline is scheduled.

Every due publication still validates the current native owner, OwnerRevision,
lease token, peer, server time and pose freshness before writing. The complete
pose, linear/angular velocities and heartbeat are retained. There is no new
position deadband, velocity suppression, or early loss of a quiet-water lease.
The maximum scheduled interval including staggering is below 0.27 seconds, so
the existing 0.5-second replica velocity freshness window and three-second lease
timeout remain unchanged. Network stalls can still exceed either threshold.

PublishFallbackPose checks the deadline before the heavier camera/range and
lease validation. PosePublishNotDue counts cheap deadline exits, not lost packets.
Hover now includes poseInterval, showing the effective interval including stagger.
On native-owned objects it describes the unused fallback interval, not the
frequency of the game's OwnerSync.

The profiler's OwnerSync Postfix includes this publication path but is not an
isolated measurement of ZDO serialization or network transmission. Fewer due
publications should reduce local writes/revisions and associated work; they do
not eliminate per-frame pose updates or guarantee a particular FPS gain.

## Previously removed repeated work

ReadLease caches only the six decoded fields by ZDO identity, ZDOID and
DataRevision. Normal local writes or received revisions invalidate the record;
pooled-object reuse cannot retain another object's record. Permission and expiry
are checked live. No-claim release calls skip fields they cannot own.
LeaseRecordReads / LeaseRecordReuses remain available in hover.

Shared runtime settings are validated once per rendered frame for kinematic
motion. Per-instance effective thickness retains its own scale calculation.
Configuration callbacks invalidate the common cache immediately; temporary
static edits are seen no later than the next rendered frame. Equal smoothing
factors and unchanged synchronized scale are reused. No native callback-list
membership is changed, so raw callback counts need not decrease.

## Background forecast evaluation: reviewed follow-up, not implemented

The appropriate off-thread unit is a batch of future water samples/forecast
knots for ownerless kinematic floes, not UpdateOwnerlessMotion or an entire Unity
callback. The current SampleKnot calls TryPhysicsSurface and TryWaterlineSurface,
which still depend on main-thread world and water state.

Review reference: shudnal/assemblies_combined at
`d1374bfd9175ac8f733ae483b0a06e5c8b75906e`, WaterVolume.cs. CalcWave writes
s_createWaveDirections[0] and s_createWaveTangents[0] on each call. Invoking it
concurrently is a data race even though its output is mathematical. Depth may
read Transform and collider bounds; surface construction reads WaterVolume,
EnvMan, ZoneSystem and world attenuation. Existing methods must not simply be
wrapped in Task.Run. A lock around the worker alone cannot protect native game
calls which do not take that lock.

A suitable separate implementation has these boundaries:

1. On the main thread, capture immutable numeric inputs: sample positions, phase
   and server time origin, wind snapshots/blend, wave coefficients and directions,
   water datum/freeze state, depth and attenuation, and settings/geometry versions.
   No MonoBehaviour, Transform, Collider, ZDO or live shared array enters a job.
2. A bounded worker evaluates the same scalar wave formulas and existing finite
   differences into private knot buffers. Start with one worker and batched jobs,
   not one Task per floe per frame. A latest-request/version policy bounds pending
   work and discards obsolete work; buffers are not mutated by two threads.
3. The main thread accepts completed buffers only for the same world/session,
   live object generation, native ownership/lease token, geometry/settings/water
   revision and valid phase/time interval. Ordinary wind changes retain the
   accepted delayed-refresh policy instead of discarding every in-flight job.
4. Request the next window before the current one expires. Continue evaluating
   the existing curve while work is pending. Never Wait/Result/Join on the main
   thread or rebuild hundreds of complete windows synchronously when behind.
   A bounded late-result policy must retain finite motion and avoid indefinite
   extrapolation. World unload/removal cancels or invalidates outstanding work.

Retain pure evaluation on the main thread as a comparison path. Validate samples
against the current native sampler, including both TrochSin factors, the unusual
world X/Z mapping, depth, wrapped phase, full-water blending and frozen water.
Replacing native calls also bypasses Harmony patches on those calls; our own
relevant effects must be represented explicitly, and foreign-patched water needs
a defined compatibility/fallback policy. Do not claim bit-identical values from
an independently evaluated scalar implementation without checking them.

Accepting the forecast, interpolating the current pose, reading body state,
applying forces/transforms, scene lifecycle and ZDO publication stay on the main
thread. Threading may reduce main-thread forecast-build cost, but does not remove
rendering or the persistent per-object callback/transform cost. The current
profiler screenshots do not isolate SampleKnot enough to predict the speedup.
No worker, new math kernel or background Unity call is introduced in this commit.

## Verification and next comparison

Compare KinematicPublishIntervalSeconds=0.2 and 0.1 on the same population without
respawning and with profiling disabled for FPS. Local motion should be unchanged;
check replica smoothness and native acquisition separately with two clients. Do
not attribute the separate larger/fewer-floe preset to this code optimization.

For config removal, check an old-only main cfg, an existing independent cfg,
missing new entries and unrelated main keys. Check ordinary live reload and
immediate disable/re-enable removal. Changes to localization made after the
initial config commit are preserved by using the current branch head as base.

Validation here is source/API review, exact original code blob hashes, diff and
C# lexical checks, changed-file Cyrillic scanning and delivered blob hashes.
No mod build, automated mod tests, game execution, physics simulation, worker
benchmark or measured FPS improvement. Version, dependencies, project Compile
entries, shaders, physics formulas and accepted movement behavior are unchanged.
