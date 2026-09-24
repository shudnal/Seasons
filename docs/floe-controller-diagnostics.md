# Floe controller diagnostics

## Current objective and handoff

The component-owned iteration started from `e44249803b43f3cc0b2e6275d82d7c1b808f63e3`.
The granular physics follow-up starts from `5b5bdfa2bef8eda5408268e275e35174655202b3`
on `perf/snow-performance` (PR #45).

Maintainer observations:

- Four-point rocking worked in Seasons 1.8.2 before the game release, although
  its physics was expensive. Rocking stopped working after the 1.9.0 rewrite.
- Distant motion also failed. Subsequent work corrected water simulation distance
  and clarified zone/floe ownership boundaries.
- The latest point/water A/B screenshots show nonzero angular impulses accepted
  by the Rigidbody accumulator but no expected visible rocking. These captures
  bracketed only the four extra forces, before native Floating and the solver.
- An earlier attempt to zero m_balanceForceFraction did not restore rocking and
  only increased immersion. Bottom balancing is not an established root cause.
- Seasonality was inspected for comparison only. Its seasonal ice clones ice1
  without an analogous four-point force driver. No Seasonality code is used here.

The objective is to locate the regression, not to replace the known force formula
with a different simulation or tune away its symptoms. This iteration supersedes
older requirements to keep per-floe state outside MonoBehaviours and to execute
the native Floating physics body after the mod's forces.

## Execution and scope

The existing `IceFloeClimb` component owns its references, support points, water
observations, ownership transitions, gravity safeguards, distant pose, physics
contributions and diagnostic samples. Its partial class is split between
`Utils/IceFloeClimb.cs` (interaction/lifecycle) and
`Controllers/SeasonalIceFloeWaves.cs` (controller implementation).

`SeasonalIceFloeWaves` retains shared wave inputs/math, distance settings, callback
lookups and the bounded distant-target service. The existing
`Floating.CustomFixedUpdate` prefix invokes `IceFloeClimb.SimulatePhysics` once
for a tracked valid seasonal floe and then returns false. The original method
body does not add buoyancy a second time. There is no independent FixedUpdate.
Untracked objects and unmarked native ice retain native Floating behavior.

Floating remains enabled for lifecycle and water callbacks. Impact/surface-effect
helpers, the existing terrain checks, native sync and safety paths are retained.
The physical block was moved from `assembly_valheim/Floating.cs` at
`shudnal/assemblies_combined` revision `d1374bfd9175ac8f733ae483b0a06e5c8b75906e`
(Valheim 1.0.15). With valid inputs and all switches enabled, the order remains:

1. Four extra point impulses using the existing wave formula and selected modes.
2. The native bottom-point balance impulse when float depth is nonpositive.
3. The native center-of-mass buoyancy impulse under the same condition.
4. The native linear and angular velocity damping under the same condition.

The copied block reads coefficients from the actual `m_floating` component.
No duplicate force/damping settings, new multipliers or extra AddTorque calls
were introduced. Missing wave inputs or disabled wave forces do not disable
center buoyancy. Non-finite center float depth is not used for physics.

Placement, cleanup, snow, the water sampler, point caching, gravity safeguards,
distant motion, mass, force coefficients, ForceMode, versions, dependencies and
persistent data are not changed. Rigidbody damping, inertia, constraints and
collision response are not overridden. Other mods' Harmony patches can still
run; skipping the original body is not a global patch bypass.

## Inspector controls

Open IceFloeClimb on a nearby, locally owned seasonal ice1 in RUE.

`ShowDiagnosticsInHover` defaults to true. Hover requests capture from the next
real physics callback, kept active for 0.5 seconds after the latest request.
Text refreshes every 0.2 seconds. `DiagnosticsEnabled` pins capture on the selected
component without hover. `FreezeDiagnostics` freezes both diagnostic records,
not the simulation. `RebuildWavePoints()` requests a lazy point-cache rebuild.
`ClearWaveDiagnostics()` clears samples/counters without changing physics.

Hover formatting does not query water, rebuild points, apply forces or log.
The existing narrow HUD fallback handles another Hoverable on the same object
without appending the block twice. Arrays and the text builder are reused.
Do not benchmark while hovering or pinning capture: comparison queries and
accumulator reads intentionally add diagnostic work.

All physics switches default to true, are local to the selected component and
are not saved in ZDO or synchronized. They do not bypass ownership, water safety
or the near/distant boundary and do not control the separate distant bobbing path.

| Switch | Controlled contribution |
| --- | --- |
| ApplyWaveForces | Master switch for the four extra wave impulses |
| ApplyWavePoint0 / 1 / 2 / 3 | Individual impulses: +wind, -wind, wind cross up, and its opposite |
| ApplyCenterBuoyancy | Original Floating impulse at the center of mass |
| ApplyBottomBalance | Original Floating impulse at the lowest collider point |
| ApplyFloatingLinearDamping | Original submerged linear-velocity damping using Floating.m_damping |
| ApplyFloatingAngularDamping | Original submerged angular-velocity damping using Floating.m_damping |

The master switch retains the four point selections. `waves=1010` means P0/P2
are enabled and P1/P3 disabled. WavePointMask uses bits 0 through 3.

The damping switches do not change Rigidbody.linearDamping/angularDamping.
Those independent engine coefficients remain editable through Body in RUE and
are included in captured diagnostics. Disabling bottom balancing removes lift
as well as moment; disabling center buoyancy can make the floe sink. Disabling
all listed contributions does not disable gravity, contacts, sync or other scripts.

## Point and water modes

Defaults remain PointMode=Cached and WaterMode=Mathematical.

| Mode | Sampling |
| --- | --- |
| Cached | Cached collider-local supports transformed to the current world pose |
| FreshClosestPoint | Fresh supports using the effective wind used by Cached |
| LegacyClosestPoint | Fresh supports using 1.8.2-style wind interpolation through EnvMan.GetWindData |
| Mathematical | Shared game-derived ocean wave mathematics |
| NativeLiquidLevel | Floating.GetLiquidLevel at the selected point; invalid heights are skipped |

Only enabled points in the selected combination apply forces. Alternative
combinations are read-only estimates. The central missing-water fallback and
distant Dampen retain their behavior regardless of these controls. The legacy
combination reproduces old active sampling on the current game, not the old
game's prefab, physics engine or water implementation.

## Point diagnostics

Diagnostics.Probes[0..3] use the original along/across-wind ordering.

| Field | Meaning |
| --- | --- |
| LocalPoint / CachedWorld | Cached collider-local support and its current world position |
| BuildRoundTripError | Error after TransformPoint(InverseTransformPoint(original)) at the last build |
| FreshWorld | Fresh ClosestPoint with the same current effective wind |
| CacheErrorXZ / CacheErrorY / CacheError | Horizontal, signed vertical and total difference from FreshWorld |
| LegacyWorld / LegacyError | Fresh legacy-direction support and its difference from CachedWorld |
| OutsideDistance | Distance to collider.ClosestPoint(CachedWorld); zero means inside/on the collider |
| Enabled / Applied | Effective switch permission and whether AddForceAtPosition was actually called |
| AppliedWorld / AppliedWater / AppliedImpulse | Selected position, sampled height and submitted impulse |

WaveProbesCaptured distinguishes unavailable point diagnostics from valid zeros.
ValidProbeCount counts valid selected samples regardless of switches; ForceCalls
counts extra wave submissions, excluding the native center/bottom contributions.
During capture, disabled points are sampled but cannot apply forces. Without
capture, disabled point queries are skipped. Expected alternative angular impulses
remain all-four-point estimates; they do not follow the enabled-point mask.

The existing geometry approximation remains: a cumulative horizontal floe/wind
turn of at least 1 degree rebuilds supports. Pitch/roll drift from a fresh support
is distinct from coordinate round-trip error. No new geometry scan or projection
of force positions onto the collider is introduced. Applying a force outside a
collider still supplies a moment; its perpendicular lever arm matters.

## Complete controller call and following step

Diagnostics now records the complete controlled call before the physics solver:

- SubmittedAngularImpulse and EngineAngularImpulse: computed and engine-measured
  wave contributions. EngineImpulse is the corresponding linear impulse.
- BottomImpulse and BottomAngularImpulse: native bottom balance contribution.
  CenterImpulse is the native central contribution.
- FloatingEngineAngularImpulse: engine-measured native-block contribution.
  TotalEngineAngularImpulse and TotalEngineImpulse cover both controlled blocks.
- AngularVelocity and AngularVelocityAfterDamping: velocity before and after the
  controlled call, not a prediction that accumulated impulses have been solved.
- Submerged, FloatDepth, BuoyancyFactor and DampingFactor explain native gating.
- Mass, Inertia, InertiaRotation, Rigidbody damping, MaxAngularVelocity,
  Constraints, gravity and FloatingBodyMatches/SyncBodyMatches expose body state.

GetAccumulatedForce/GetAccumulatedTorque deltas are converted from ForceMode.Force
units using this callback's dt. They are inputs, not post-solver motion.

LastPhysicsStep pairs the previous captured call with the next eligible physics
callback. It retains source time, owner, sampling modes and switch mask, then
records observed angular velocity and the angle of Body.rotation change. Small
rotation angles use quaternion-vector Atan2 rather than Quaternion.Angle's
near-one dot-product threshold. Pairing is rejected across a detected pause,
ownership/body change, distant/safety transition or a skipped-step time gap.

This observation includes the solver, contacts, Rigidbody damping and any scripts
between callbacks, including native ZSyncTransform before Floating. It does not
isolate PhysX or identify an external writer by itself. It also does not assume
that a nonzero angular impulse must produce a visibly large turn: inertia matters.
The full inertia tensor orientation is available in Diagnostics for that analysis.

Live state and retained samples are deliberately separate. Check frame/time/age,
owner, switches and capture flags before comparing values. A paused live status
or changed live switches do not relabel an older sample. Old LastPhysicsStep
values remain identifiable by their source/observation times and age.

## Next owner-run check

Use one nearby, freely floating, locally owned floe, not touching a ship, shore
or another floe. Do not stand on it. Pin DiagnosticsEnabled, leave
FreezeDiagnostics=false, and keep the game unpaused during observations.
Keep one sampling combination throughout: LegacyClosestPoint/NativeLiquidLevel
with valid native water at all four points is useful for this isolated check.
Do not rebuild/reload or change wind between passes unnecessarily.

| Pass | Changes from the preceding pass |
| --- | --- |
| A | All nine physics switches true; collect a baseline |
| B | Only ApplyFloatingAngularDamping=false |
| C | Also ApplyBottomBalance=false; retain center buoyancy and linear damping |
| D, only if still unclear | Also disable ApplyWavePoint1/2/3, leaving P0 and the wave master enabled |

Allow several seconds of active simulation per pass and capture the hover or
Inspector records. For D, choose another single point if P0's actual impulse or
horizontal lever arm is effectively zero; a zero test impulse proves nothing.
Stop if a pass gives a clear response and restore the preceding switches to
check reversibility. Restore all switches after the short experiment.

The immediate questions are whether native balancing cancels the wave moment,
whether explicit angular damping suppresses persistent motion, and whether one
uncancelled point produces a measurable next-step turn. A nonzero total moment
with little response requires inspection of inertia, constraints, engine damping
and intervening body/sync writes; it is not proof of any one cause. Do not change
these extra variables during the first passes. Do not disable the Floating
component itself: it remains the driver and water callback receiver.

## Verification boundary

This follow-up is diagnostic instrumentation and component ownership of the
native physical block, not a confirmed rocking fix. Source/API inspection and
manual diff/lifecycle review are performed without building the mod, running
Valheim or executing automated tests. Compilation, gameplay acceptance, ownership
handoff, re-enable/unload behavior, missing water and distant-to-near checks remain
with the maintainer. No performance or runtime-success claim is made.
