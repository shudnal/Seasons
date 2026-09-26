# Floe runtime configuration and repeated-work reduction

Base: `dbf3334e45689da85e1dbe43333b01d7713fd679`, PR #45,
`perf/snow-performance`. Date: 2026-09-26.

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

## Separate configuration file

All floe settings are initialized before Harmony patch registration and before
connecting to a world. The generated file is:

```text
BepInEx/config/shudnal.Seasons.IceFloes.cfg
```

The former five floe entries and floe logging entry move out of the main config.
Their section/key identities remain unchanged. Seasons' existing ConfigEntry
fields reference the new entries, so placement, season callbacks and immediate
cleanup continue to consume the same settings, not a competing second source.
There is still one ConditionalConfigSync instance and one configuration lock.
No additional plugin, RPC protocol or dependency is introduced.

Fresh-install placement defaults are:

| Setting | Default | Meaning |
| --- | --- | --- |
| Enable ice floes in winter | true | Existing seasonal feature switch |
| Fill the water with ice floes at given days from to | 4 / 10 | Inclusive winter days |
| Amount of ice floes in one zone | 10 / 15 | Placement attempts in one 64x64 zone |
| Scale of ice floes | 1.25 / 2.5 | Base random scale before existing shape/depth adjustments |
| Health of ice floes | 20 | Base health before volume/world-level scaling |

The larger scale range is an initial editable preset, not a measured optimum.
The count's upper default is 15 per zone, not 15 in the world or on screen.
Placement exclusions can reduce the actual count. Scaling, health and count
changes affect subsequent spawning, not already saved floes. To regenerate the
same ocean with different placement settings, disable floes, allow the immediate
cleanup to finish, then enable them again during an eligible winter period.

### Migration and persistence

When the separate file does not yet exist, existing saved main-config values
are transferred, including values equal to the previous defaults. The new count
and scale defaults do not silently override a configured world. When a legacy
key is absent, its new default is used. Values outside the supported input ranges
are normalized; finite ordered ranges are required.

If the new file already exists, it wins. Missing entries use shipped defaults.
The replacement file is saved before removing old bound keys from the main
file. Other main-config entries, including unrelated orphaned entries, are not
removed. This is a one-time move, not continuous mirroring between two files.

Generation and physical coefficients use the existing server-controlled policy.
Prediction quality, far-visual bob, fallback participation and floe logging are
peer-local. This replaces the earlier diagnostic-only, unsynchronized treatment
of persistent physics coefficients. Direct RUE edits to runtime static fields
remain temporary diagnostic edits, not persistent or synchronized settings.

The file contains the proven buoyancy, drag, tilt, waterline, kinematic response,
wave sampling and forecast controls with their existing numerical defaults.
Ranges reject non-finite numeric inputs. Changes update the shared runtime fields
through SettingChanged callbacks, not through ConfigEntry reads on every floe.

A watcher coalesces exact-file changes and performs reload on the main thread.
Its callback thread never accesses Unity objects. Reload is debounced, with a
small bounded retry for temporary file-write failures. Watcher setup failure is
reported and the manual main-thread action remains available:

```csharp
Seasons.SeasonalIceFloeSettings.Reload();
```

To discard temporary RUE experiments and reapply the currently effective config,
without writing files or resetting object poses:

```csharp
Seasons.SeasonalIceFloeSettings.ApplyConfiguredRuntimeValues();
```

The older IceFloeClimb.ResetSurfacePhysicsSettings action still restores the
compiled diagnostic baseline; it does not save a new cfg or override the
configuration's stored defaults. Use ApplyConfiguredRuntimeValues afterward to
return to configured values. Automatic listing of the second file in every
Configuration Manager variant is not assumed; file reload and inspector actions
are independent of that UI integration.

## Repeated work removed without changing the motion contract

### Lease field decoding

ReadLease now caches only the six decoded ZDO fields by object identity, ZDOID
and DataRevision. Normal local Set calls and received ZDO data revisions
invalidate the decoded record. Reinitialization clears it; pooled-object reuse
cannot retain a different ZDOID's record.

This is NOT a cached permission or cached expiry result. Each permission check
still evaluates current native HasOwner, native OwnerRevision, token, peer and
current synchronized server time. Publication still validates authority directly
before writing. Candidate settling, heartbeat lifetime, publication cadence,
release and native-owner precedence are unchanged.

No-claim release calls skip looking up and clearing fields they cannot own.
The per-object hover counters LeaseRecordReads / LeaseRecordReuses show decoding
versus reuse without a timer or full diagnostic capture on every object.

### Publication deadline

PublishFallbackPose now checks the existing publication deadline and pose
freshness before invoking the heavier camera/range and lease validation. A call
between publications performs no unnecessary full authority validation, but a
call which can write still performs it. Publication remains at the original
0.10-0.12-second interval. No heartbeat, pose or velocity field is dropped.
PosePublishNotDue counts cheap deadline exits; it does not count lost packets.

### Kinematic common inputs

Shared runtime settings are validated once per rendered frame for kinematic
motion. Per-instance scaled effective thickness is recalculated with exactly the
existing formula; the first floe's thickness is never copied to other sizes.
Configuration callbacks invalidate the common settings immediately. Temporary
static inspector edits are observed no later than the next rendered frame.
Dynamic force settings continue through their existing path.

Exponential tracking factors are reused for equal delta time and response time.
This changes neither the factor nor how frequently a pose is updated. Ownerless
scale reads reuse the current accepted ZDO revision while checking actual local
scale, so local edits still trigger the original synchronization behavior.
Ownerless mode transitions clear this scale cache.

## Deliberately unchanged

No native callback-list membership is changed, so Floating and ZSyncTransform
callback counts do not automatically fall. This pass reduces repeated work
inside them. It does not disable native lifecycle/water callbacks, add a second
motion driver, skip dynamic force steps, change quaternion motion, move the
camera/active-area boundaries or alter the far-visual isolation contract.

The physical formulas, 70-percent collider waterline, wave spectrum, native
full-water height, Hermite forecast, soft wind refresh, static geometry cache,
kinematic/dynamic transitions, collision settings, server election and immediate
cleanup are unchanged. The accepted behavior is the regression boundary.

New defaults reduce the population after regeneration, but that is distinct from
code optimization. This pass does not claim that rendering 900 objects can cost
as little as not creating them, or promise a particular FPS gain.

## Next comparison and static verification boundary

First compare the same existing population, camera, weather and settings with
profiling disabled. No respawn or new preset is needed for this code-only A/B.
Then separately apply the desired new size/count and regenerate to measure that
population. Keep the benchmark cases distinct. Check that the config reloads and
that ordinary disable/re-enable still performs immediate deletion and placement.

Static validation covers exact original blob reconstruction, changed-file
lexical delimiter checks, project XML, one added Compile entry, targeted source
invariants, accidental Cyrillic scanning and delivered blob hashes. There is no
mod build, automated mod test, physics simulation, game execution or performance
measurement on the assistant side. No version, dependency or packaging change.
