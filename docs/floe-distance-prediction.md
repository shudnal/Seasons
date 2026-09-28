# Distance-adaptive floe surface prediction

Base: `da9344c36690f3664ce38212644925e30f895c6e`, `perf/snow-performance`, PR #45.
Game sources: `shudnal/assemblies_combined` at
`d1374bfd9175ac8f733ae483b0a06e5c8b75906e`.

This follow-up supersedes the every-step distant water sampling and far-height
conventions in `floe-controller-diagnostics.md` and the deferred far-pose isolation
in `floe-simulation-authority.md`. Their cooperative election, native owner
priority, one-second candidacy and server-time lease rules are unchanged.

## Scope and observed motivation

The maintainer reports good physical motion but severe frame-rate loss with
about 900 visible floes at maximum view range. Patch-profiler captures show many
physics passes per rendered frame and substantial time both in the force prefix
and in repeated synchronization preparation. These instrumented measurements do
not separately measure all PhysX/GPU cost and are not a performance guarantee.

The chosen solution keeps floe density, generation, the working force/torque
model and native fixed timestep. It reduces expensive water evaluations using
future samples, separates those evaluations from per-step force feedback, and
makes beyond-wave motion strictly local. No arbitrary nearest-object count cap,
worldwide ownership replacement or global physics-time setting is introduced.

## Three local sampling modes

| Mode | Eligibility | Surface work | Body work |
| --- | --- | --- | --- |
| FullRate | Inside native active area, or prediction disabled | Current water/tilt samples every fixed step for the current authority | Existing forces and contacts every fixed step |
| Predicted | Outside native active area but inside visible-wave radius | Rebuild a future trajectory on a distance-dependent schedule | Interpolate the trajectory; apply feedback forces every fixed step |
| FarVisual | Outside visible-wave radius | Flat water datum and a small sine bob; no full wave sampling | Local non-colliding kinematic pose, once per rendered frame |

Modes describe local sampling, not authority. Native replicas still do not run a
second water-force controller; a native owner or confirmed local fallback lease
is required for the first two modes' forces. Native ownership is not stolen or
released to enforce a sampling mode.

`ZNetScene.InActiveArea(bodyPosition, referencePosition)` supplies the native
active-area decision. In the reviewed game this is based on a 1 or 1.5 zone
half-width, with a special non-classic distance-2 boundary, not the complete
render/simulation-distance disk. The visible-wave radius remains
`NearSimulationDistance * ZoneSystem.m_zoneSize`, matching Water.ApplySettings.

Outside the native area the prediction horizon increases from 0.1 seconds to
at most 2 seconds as distance beyond the active zone grows toward the visible
wave boundary. The native boundary is used directly for classification; the
horizon uses excess horizontal Chebyshev distance beyond its ordinary half-width.
Diagonal directions can consequently have a shorter horizon at the same radial
distance. The first forecast is shortened by a deterministic per-floe phase to
avoid regularly synchronizing all future rebuilds.

A headless server has no camera-based far visual; its native-owned active bodies
retain the normal path. The change does not enlarge loaded zones or assign more
fallback leases than the existing protocol permits.

## Predict the water, not the collision solution

External forces do not change the prescribed water function. They do change the
floe position, velocities and contacts. Therefore only the expensive surface
trajectory is reused. The controller still reads current Rigidbody state and
submits force and torque on every physical step. There is no accumulated
multi-second impulse or per-floe call to Physics.Simulate, and no velocity write
that erases contact impulses in either authoritative dynamic mode.

The forecast samples the same four wind-aligned mathematical points as before,
including first derivatives from a 0.05 second forward difference. Sampling
positions follow predicted horizontal travel of the collider center. Complete
heave height is additionally sampled at that center (see waterline below).

A two-second forecast is NOT just its two endpoints: it contains knots spaced by
at most 0.25 seconds. This is required because the native spectrum has curved
crest/trough motion that a two-second straight line can miss. The fixed reusable
buffer has room for the permitted 0.1-second minimum knot spacing, the horizon
and the small derivative look-ahead. It is allocated lazily and not per step.

Cubic Hermite interpolation evaluates height and its analytic velocity from the
same curve. Four interpolated heights reconstruct the plane normal; the same
curve a short step ahead supplies normal motion for tilt damping. Thus heave,
normal and their motion are consistent rather than independently lerped. Normal
alignment still uses the existing bounded torque and inertia tensor.

A compatible rollover carries its current interpolated value and slope into the
new first knot. A late call rebuilds from the present, never replays an unbounded
backlog. Forecasts are invalidated by changed authority/token, geometry or scale,
sampling controls, water context, material wind changes, clock/phase discontinuity
or excessive unpredicted horizontal drift (default 0.5 metres). Small wind changes
are tolerated until refresh; future weather is not known exactly. Near the native
daily wave-phase wrap, direct sampling avoids interpolating across a discontinuity.

The maximum time horizon is a work/quality setting, not a claim that future wind,
modded water or future collision motion is known perfectly.

## Waterline: 70 percent immersion in every mode

`RestingSubmergence` now defaults and resets to **0.7**. It is a nominal fraction
of the referenced collider's intrinsic thickness, not a mesh-volume fraction.

```text
targetHullY = waterHeight + HeightOffset + (0.5 - RestingSubmergence) * thickness
heightError = targetHullY - actualColliderCenterY
targetPivotY = actualPivotY + heightError
```

For an upright collider with its root on the bottom and no extra HeightOffset,
the target pivot is 0.7 of its scaled intrinsic thickness below water; the top
is 0.3 above. Root position and center of mass are not mistaken for the collider
center. The effective displacement density remains 0.9 and still controls reserve
lift; geometric immersion is not implemented by making the body an arbitrarily
strong spring or by changing its mass again.

There were two distinct ways for the previous floe to look too high:

1. The old far visual still added Floating.m_waterLevelOffset to the root and
   ignored the collider waterline. FarVisual now uses the same 70-percent geometry
   convention, with no native offset added.
2. Even perfect following of a filtered large-wave plane can put it above the
   complete visible water. Heave and tilt now have separate sampling references.

With `UseFullWaterHeight=true` (default), heave uses complete native wave height
at the collider center, including native wind blending and local WaterVolume
Depth when available. The tilt plane still uses the chosen large waves and omits
short ripple terms, preserving broad stable rocking. The same heave samples
provide relative-water velocity for drag. Without a local volume, the previous
normalized Ocean-depth approximation of 1 remains.

The heave sampler calls native CalcWave with explicit time/wind inputs; the tilt
sampler calls selected native CreateWave terms. The full sampler is not called
from synchronization preparation or FarVisual. Turning UseFullWaterHeight off is
an explicit A/B control restoring mean tilt-plane heave; it can reintroduce the
previous height mismatch. No rendered water, global shader or native wave formula
is changed.

Native CPU water time and rendered smoothed water time are not identical, and a
curved wave need not coincide with a flat collider over its full footprint. Thus
70 percent is the equilibrium datum, not a guarantee that every vertex is exactly
70 percent submerged during motion, collisions or render-time interpolation.

## Remove redundant work

- BeforeSync no longer samples water, recovers heights or repeats full physics
  preparation before native ClientSync's own fast paths.
- Local mode/preparation is cached per rendered frame. Changed ZDO data can still
  refresh authority during that frame; native owner/revision, pause and fallback
  enable changes also invalidate the guard. Immediate lease validation remains
  before force submission and publication.
- Geometry from the known BoxCollider or MeshCollider bounds is cached until its
  referenced collider or scale changes. Each step only transforms the cached
  center/extents into the current physical pose. No hierarchy/vertex search is
  introduced. RebuildHullGeometry is available for deliberate inspector edits to
  an otherwise immutable prefab collider or child transform.
- The original 16-per-LateUpdate full-spectrum distant target queue is removed.
  FarVisual performs only a flat datum plus bounded sine, once per render frame.
- A narrowly validated transpiler changes only the water-height query for
  registered Predicted/FarVisual seasonal floes in WaterVolume.UpdateFloaters.
  It retains native interactable enumeration, liquid counts and level callbacks,
  while using a cheap base level for that redundant observation. Authoritative
  physics supplies its own sampled height before impact/surface presentation.
  Ordinary objects and FullRate floes retain native water observations.

If the transpiler does not find the reviewed call/local pattern, it leaves the
original method unchanged, logs a warning and exposes nativeWaterThrottling=False
in hover. No partially modified IL is returned. This preserves functionality at
higher observation cost rather than silently corrupting liquid bookkeeping.

The change reduces wave work; it does not remove PhysX cost from predicted
dynamic bodies or guarantee an FPS target. Complete heave adds two full-spectrum
samples at each exact sampling knot: near calculations are more accurate but not
cheaper in wave terms. At a stable two-second horizon and default knot spacing,
roughly 4-5 knot evaluations per second replace 50 complete per-step evaluations
for a 0.02-second timestep. This is a count estimate, not measured CPU speedup;
invalidations, extra wind blending, diagnostics and knot rebuild bursts add work.

## Far visual isolation and lifecycle

Entering FarVisual releases only a matching local fallback claim once, saves the
physical pose/velocity and body collision/kinematic settings, suppresses both
native OwnerSync and ClientSync for that local floe, and stops dynamic contacts.
It does not change native IsOwner/HasOwner or write a decorative position,
rotation, scale or velocity to ZDO. Claim-release metadata is the only deliberate
transition write. No fallback heartbeat or pose publication continues there.

Horizontal baseline and yaw are local retained data, not continually copied from
another client's wave-driven ZDO. The body gradually levels and sits at flat
water plus a default 0.08-metre, 6-second bob. Outside the camera far-clip range,
even that pose work is skipped. Frozen/no-wave water has no bob amplitude.

On returning within visible waves, collision/kinematic settings and authoritative
pose/velocities are restored once. Same unchanged native ownership uses the saved
physical baseline; otherwise the latest ZDO is adopted. The local decorative pose
never becomes a new authoritative position. Surface prediction is invalidated.
This can visibly transition between a flat visual and a real wave; seamless
cross-fading of radically different surfaces is not claimed. Unload/disable/world
reset restores per-instance overrides through the existing lifecycle.

FarVisual is intentionally non-colliding and non-authoritative. It is not a
replacement for interactive physics at a player/ship contact. Test wave-radius
crossings, especially non-default minimal radii and free-camera use, before
accepting those boundary conditions for multiplayer gameplay.

## Shared inspector controls and next checks

All new tuning fields are static on Seasons.IceFloeClimb and reset through
ResetSurfacePhysicsSettings. Per-instance diagnostic capture remains unchanged.

| Field | Default |
| --- | --- |
| EnableWavePrediction | true |
| UseFullWaterHeight | true |
| MinimumPredictionSeconds / MaximumPredictionSeconds | 0.1 / 2 |
| PredictionKnotSeconds | 0.25 |
| PredictionPositionTolerance | 0.5 |
| RestingSubmergence / HeightOffset | 0.7 / 0 |
| DistantBobAmplitude / DistantBobPeriod | 0.08 / 6 |

Keep all six Apply switches on and SecondarySwellWeight=1. Do not change the
working force coefficients while evaluating this follow-up. EnableFallbackSimulation
is still a separate shared switch and is not reset by the physics reset method.

Hover adds Surface mode, activeArea, distance/waveRadius, prediction interval/age,
knot count, direct/builds/hits/query counters, invalidation reason and native-water
throttling status. Water diagnostics now distinguish heave/tilt/full/native; the
full diagnostic comparison still uses the previous effective-wind approximation,
whereas native and full-height heave use native wind blending. Retained force
samples can be older than live mode/authority fields after a transition.

Initial maintainer sequence:

1. Reset shared settings once. Check an owner floe in FullRate for the expected
   70-percent waterline and preserved interaction/motion.
2. Check an authoritative floe outside the native area in Predicted mode.
   Forecast hits should grow between builds, and intervals should generally grow
   with distance. There must be no two-second pose jump or two-second force pulse.
3. Check FarVisual: flat-water immersion plus small bob, no forecast builds or
   fallback pose publications, and no copying a foreign wave-driven Y/rotation.
4. On the same 900-floe scene, disable detailed per-object diagnostic capture and
   compare the same profiler view/camera/wind after generation has settled. Record
   FPS as well as profiler call counts; catch-up-step count itself may change.
5. Check wave/active boundary crossings, scale edits, wind/time changes, local
   disable/re-enable and actual native/fallback handoff separately. Network quality
   was not accepted by the preceding single-client physics observations.

## Verification boundary

No build, mod tests, physics simulation or game execution is performed here.
Verification is source/API review against the named game snapshot, exact original
blob hashes, lexical C# delimiter/string checks, project XML and Compile-entry
diffs, targeted reference checks and English-text scans. The new C# file is
explicitly added to the existing project. No dependency, version, packaging,
spawn density, snow code or additional persistent ZDO field is changed.
