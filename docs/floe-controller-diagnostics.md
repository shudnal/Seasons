# Wind-surface floe physics and runtime diagnostics

Date: 2026-09-25. Branch: `perf/snow-performance`, PR #45.
Implementation base: `75881a1d565305fd45f1e151aa88aa9162dfce3e`.
Game source reference: `shudnal/assemblies_combined` at
`d1374bfd9175ac8f733ae483b0a06e5c8b75906e` (WaterVolume, Floating, ZSyncTransform).

## Current agreement and evidence

This implementation supersedes the four-point force and native Floating physics
requirements in earlier floe documents. Earlier agreements about placement,
ownership, water simulation distance, distant motion, persistence and snow remain
unchanged. The previous diagnostic implementation is available in this file and
`Controllers/SeasonalIceFloeWaves.cs` at commit `5aa3621`.

The maintainer's A/B/C/D observations established that:

- The active owner submits nonzero wave impulses accepted by the Rigidbody.
- Disabling Floating angular damping alone did not give the intended rocking.
- Disabling bottom balance as well allowed large, inertial rocking.
- The corrected single-point experiment responded to that point's wave force.
- Center buoyancy remained enabled in C and both D observations. The first D had
  all four wave points disabled; the corrected D had only P0 enabled.

There is no evidence here that AddForceAtPosition was globally broken, nor proof
of the precise historical 1.9.0 regression. The maintainer chose to replace the
model instead of continuing to tune four edge forces. No Seasonality code is used.

## What now drives the floe

The existing IceFloeClimb partial component contains one simulation entry point,
called from the existing Floating.CustomFixedUpdate prefix. The prefix skips the
original method for a registered valid seasonal floe. Floating stays enabled for
water observations, lifecycle, terrain safety and impact/surface presentation.
Unmarked ice and other Floating objects keep their native implementation.

The old edge supports, ClosestPoint caches, point modes, four extra impulses,
lowest-point balance, native center buoyancy and explicit native velocity damping
are removed from the active controller. The four remaining points only measure
water. They do not touch a collider and receive no forces themselves.

A near, locally authoritative floe receives one combined force at its center of
mass and one combined world-space torque, both using ForceMode.Force. There is no
additional FixedUpdate. The controller never assigns its near simulation pose,
linear velocity or angular velocity, does not make the body kinematic, and does
not freeze rotation or alter collision materials. Existing invalid-height and
missing-water recovery, ownership transitions and distant bobbing retain their
narrow pose/velocity handling.

Players, creatures, ships and other floes still collide with the actual Rigidbody.
Contact impulses and the controller's bounded water forces are resolved by the
same physics solver. This preserves a dynamic interaction path; it is not a claim
that all gameplay/network collision cases have already been accepted in-game.

## Water spectrum and the four-point plane

Native WaterVolume.CalcWave sums ten CreateWave terms. Only term 0 changes its
direction with wind. Every CreateWave term multiplies two TrochSin factors; the
slow transverse factor is part of that wave, not the separate ripple spectrum.

The new physics sampler calls native CreateWave directly with the reviewed native
parameters, phase and coordinate mapping. It retains both factors. It uses the
existing shared effective GetWindDir/GetWindIntensity snapshot, native wrapped
time, the accepted normalized Ocean depth of 1, surface offset and Deep North
large-wave attenuation. Frozen water suppresses these waves. No seasonal wind
multiplier is applied twice.

Default spectrum: term 0 only (speed 10, spatial frequency 0.04, height 8,
sharpness 0.5). SecondarySwellWeight optionally adds native terms 1..4 with their
original directions and parameters. Terms 5..9 (spatial frequency at least 1)
are always omitted. Terms 1..4 are substantial swells, not just tiny ripples.

Therefore a primary-only plane is intentionally not the exact rendered/full
water surface. It cannot both exclude other waves and coincide with them at every
point. Diagnostics show plane/full/native heights separately. Raising
SecondarySwellWeight can restore more large-scale surface variation while still
omitting short waves; no shader or global water function is modified.

Each real physics call constructs four offsets from the current wind direction:
+wind, -wind, +(wind cross world-up), and its opposite. ProbeDistance is their
half-spacing, default 2 world metres. These horizontal axes do not rock with the
body. There are no cached supports, rotations or collider searches.

ScaleProbeDistance defaults to false. When enabled, local X/Z scale is projected
into the horizontal wind frame, ignoring ordinary pitch/roll. Thickness always
uses absolute scale Y. The known fixed ice prefab is approximated as a slab; no
mesh/volume reconstruction is attempted.

Four points on a curved wave generally are not coplanar. On a symmetric cross the
least-squares plane is particularly simple:

```text
H = (h(+W) + h(-W) + h(+S) + h(-S)) / 4
gW = (h(+W) - h(-W)) / (2 * radiusW)
gS = (h(+S) - h(-S)) / (2 * radiusS)
n = normalize(up - W*gW - S*gS)
```

The target gradient is limited by MaxSurfaceTilt. This limits the target normal,
not the actual body's rotation or external collision response.

For water-relative damping and tilt feed-forward the same four positions are also
sampled 0.05 seconds ahead, including predicted horizontal body travel. This gives
the time derivative along the floe's horizontal motion without differentiating a
stored previous frame. It does not teleport/predict the Rigidbody itself. Wind and
geometry are held at the current snapshot during this derivative calculation, so
an owner switch or an explicit wind/time command cannot create a stored-history
derivative spike. Abrupt surface target changes still produce bounded forces.

Default cost: eight single-term CreateWave evaluations per active floe call (four
current heights plus four derivative heights). Nonzero secondary weight evaluates
five terms per point instead. Diagnostic full/native comparisons add work only
while capturing. No performance improvement is claimed without profiling.

## Vertical displacement and drag

Let m be the actual Rigidbody mass, r the relative ice/water density, D the scaled
effective slab thickness, and H the filtered plane height. The existing
Floating.m_waterLevelOffset is a datum offset, not a force coefficient.

```text
reference = H + Floating.m_waterLevelOffset + HeightOffset
fraction = clamp01(0.5 + (reference - centerOfMass.y) / D)
F_buoyancy = m * g * fraction / r
resting centerOfMass.y = reference + (0.5 - r) * D
```

This corresponds to an effective displacement volume V = m/(waterDensity*r),
with thickness D controlling its effective area and vertical stiffness. It is not
a claim to measure the irregular collider's actual submerged mesh volume.

Static buoyancy saturates when the slab is fully displaced. At r=0.9 its maximum
is about 1.111 times body weight, rather than growing with arbitrary immersion
depth. With no drag or external load, net upward acceleration while fully submerged
is about 0.111*g. Once dry, buoyancy and water drag vanish; there is no airborne
magnetic attachment to a wave.

Damping acts on body vertical velocity minus water vertical velocity, not velocity
relative to the world. Its small-motion rate is
2 * VerticalDampingRatio * sqrt(g/(r*D)), weighted by contact with water. The drag
step is implicit and includes predicted buoyancy/gravity velocity for that step;
its acceleration is limited by MaxWaterDragAcceleration. It is passive resistance
to relative motion, not a second unbounded height spring. Dynamic drag may exceed
static buoyancy transiently, and its limit is reported separately.

Horizontal drag uses an independent 1/s rate. It imposes no X/Z position target.
Native Rigidbody damping is left unchanged and remains visible in diagnostics.
There are no assignments that erase collision velocity after the solver.

This aims to reduce overshoot and lag without claiming exact continuous contact
with every visible wave. A sufficiently violent collision, under-tuned damping,
or deliberately filtered water can still cause separation or immersion.

## Actual mass, load capacity and lifecycle

MassMultiplier defaults to 4 relative to the already saved seasonal floe mass.
It changes actual collision mass, not a second fake gravity. Both buoyancy and
water drag scale with that mass; mass alone is not the cure for vertical bobbing.
The dynamic force model, density and water-relative damping provide that change.

Mass is applied after IceFloeClimb.Start reads the existing saved mass, including
on non-owner instances. No new ZDO value is written and the prefab is not mutated.
The override is restored on release/disable/world shutdown if the body's mass is
still the value this component applied. It does not rewrite another mod's mass
every tick. An explicit runtime change to MassMultiplier can adopt an externally
changed mass as its new baseline. SourceMass is visible in the inspector.

Finite displacement means finite load capacity: static reserve is
m*(1/r - 1). For the reported 308.837 base mass, multiplier 4 and r=0.9, this is
about 137.3 mass units of extra supported load. Heavier total loads may submerge the
floe. The model is not intended to support an arbitrarily heavy ship by enforcing
a pose. Default coefficients are initial tuning choices, not measured acceptance.

## Torque controller

The shortest axis-angle error aligns body-up with the plane normal. It explicitly
handles a fully inverted floe, for which an unqualified cross product is zero.
No desired compass heading is imposed. Tilt damping uses relative angular velocity
against the changing surface normal; yaw drag is separate.

```text
w = 2*pi*TiltFrequency
kp = w*w * wetWeight                 (when alignment is enabled)
kd = 2*TiltDampingRatio*w*wetWeight   (when tilt damping is enabled)
alpha = (kp*angleError + (kd + kp*dt)*(targetOmega - bodyOmega)_tilt)
        / (1 + kd*dt + kp*dt*dt)
```

Tilt acceleration is limited, and optional yaw drag is added. The resulting
acceleration is transformed through the Rigidbody's actual rotated inertia tensor
into world-space torque. AddTorque(ForceMode.Force) applies it through the physics
solver. There is no rotation assignment, scalar-mass torque approximation,
duplicate dt multiplication or explicit cancellation of external contacts.

## Inspector settings

All fields are local per-instance runtime controls, not persistent configuration
keys. Change them on the authoritative test floe. Other peers start with identical
defaults but do not receive inspector edits; ownership transfer may therefore
change experimentally edited coefficients. Invalid numeric values are replaced
by defaults or bounded for the calculation, without rewriting inspector fields.
The captured Settings record contains the effective values.

| Field | Default | Effect |
| --- | --- | --- |
| ProbeDistance | 2 m | Sampling half-spacing along/across wind |
| ScaleProbeDistance | false | Optional X/Z scale-aware spacing |
| SecondarySwellWeight | 0 | Optional native fixed-direction long swells, range 0..1 |
| MassMultiplier | 4 | Actual body mass relative to the saved base mass |
| HullThickness | 1 m | Effective displacement thickness before scale Y |
| RelativeDensity | 0.9 | Resting submerged fraction; larger values give less reserve lift |
| HeightOffset | 0 m | Additional vertical datum correction |
| VerticalDampingRatio | 1 | Damping of motion relative to water, not a height target gain |
| MaxWaterDragAcceleration | 6 m/s^2 | Limit on vertical drag, separate from buoyancy |
| HorizontalDamping | 0.15 /s | Drag against horizontal motion |
| TiltFrequency | 0.65 Hz | Normal-following responsiveness |
| TiltDampingRatio | 1 | Suppression of angular overshoot relative to the wave |
| MaxTiltAcceleration | 1.5 rad/s^2 | Limits the tilt controller's authority |
| MaxSurfaceTilt | 45 degrees | Maximum target inclination, not a body constraint |
| YawDamping | 0.15 /s | Drag around body-up without heading lock |

Independent switches, all initially true: ApplyBuoyancy,
ApplyVerticalWaterDamping, ApplyHorizontalWaterDamping, ApplySurfaceAlignment,
ApplyTiltDamping, ApplyYawDamping. Disabling alignment alone does not disable
rotational water drag. Disabling all switches does not disable gravity, other
scripts, engine damping, collisions or native synchronization.

ResetSurfacePhysicsSettings restores these runtime fields, without teleporting or
zeroing the body. Mass takes effect through the normal callback. ClearWaveDiagnostics
clears measurements only. The old per-point force and point/water mode controls
are intentionally removed; there is no hidden parallel legacy driver.

## Diagnostics and maintainer checks

DiagnosticsEnabled pins capture; otherwise hover requests it for 0.5 seconds.
FreezeDiagnostics retains the current and previous records, not the simulation.
SamplePosition0..3 reconstruct retained water sample positions without querying
physics. Force/torque calls now count at most two calls, not four point forces.

The retained record shows filtered/full/native height, the four heights and probe
radii, target and actual normals, COM target/error, water/body relative velocity,
density/displacement, forces in N, torque in Nm, effective settings, actual body
mass/inertia/damping/constraints and expected versus engine-accepted impulses.
LastPhysicsStep pairs the preceding call with the next callback's observed
velocity and rotation; it includes contacts, sync and any other intervening script.
Do not infer isolated controller response from that observation alone.

Initial maintainer pass:

1. Use a free, upright owner floe with local=True, distant=False and
   gravityHold=False. Leave default settings enabled and retain the same wind.
2. Inspect plane/full/native height before changing gains. A large difference
   concerns the wave spectrum or depth approximation, not weak torque. Compare
   SecondarySwellWeight=0 and 1 while leaving the other settings unchanged.
3. Adjust VerticalDampingRatio for vertical overshoot and TiltDampingRatio for
   angular overshoot. Use TiltFrequency for response speed. Do not change mass,
   density, height offset and several damping values simultaneously.
4. Check several scales, a player standing/moving on the floe, creatures, ship
   contact and floe-to-floe contact. Check finite load capacity and recovery from
   immersion/inversion; contacts must not be overwritten by pose/velocity writes.
5. Check pause/resume, explicit wind/time changes, ownership handoff, missing
   WaterVolume, unload/reload, disable/re-enable and distant-to-near return.
6. Turn off detailed capture before profiling.

The distant full-spectrum Dampen path remains deliberately unchanged in this
iteration and is not controlled by the new near-force switches. Its height
convention can differ from the new density-based equilibrium. No new distant
rotation, pose publication or ownership rule is introduced.

## Verification boundary

No mod build, automated mod tests, game execution or performance measurement was
performed. Validation is limited to source/API review, lexical C# structure,
reference/diff checks and an English-text scan of delivered files. Native methods
used here are referenced by the already compiled project paths; no dependency,
project Compile item, package, version or snow/persistence-schema changes are
required. Gameplay tuning and multiplayer acceptance remain with the maintainer.
