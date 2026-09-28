# Wind-surface floe physics and shared runtime diagnostics

Date: 2026-09-25. Branch: `perf/snow-performance`, PR #45.
Waterline follow-up base: `a6a40bb31b1f8371ca38c005e0232ae0ecbdb50f`.
Game source reference: `shudnal/assemblies_combined` at
`d1374bfd9175ac8f733ae483b0a06e5c8b75906e` (WaterVolume, Floating, ZSyncTransform).

## Current agreement and evidence

The wind-surface implementation supersedes the four-point force and native
Floating physics requirements in earlier floe documents. Placement, ownership,
water simulation distance, persistence and snow are outside this follow-up.
The previous design and diagnostic reports remain available in this document at
commits `5aa3621` and `a6a40bb`. No Seasonality code is used.

Earlier A/B/C/D observations confirmed that the owner submits nonzero impulses
accepted by the Rigidbody, that removing native bottom balance and angular
velocity damping permits rotation, and that a single active point responds.
They did not prove the precise historical cause of the 1.9.0 regression.

The two latest maintainer runs on a6a40bb separate surface mismatch from tracking:

| Spectrum | Plane minus native, m | COM target minus actual, m | Tilt error, degrees |
| --- | --- | --- | --- |
| Primary only, sample 1 | 2.237 | -0.009 | 0.118 |
| Primary only, sample 2 | 1.048 | 0.011 | 0.021 |
| Secondary weight 1, sample 1 | 0.070 | 0.032 | 1.176 |
| Secondary weight 1, sample 2 | 0.749 | 0.039 | 0.029 |

These are individual observed samples, not maxima, distributions or synchronized
A/B replay. The primary-only controller can accurately follow an unsuitable
surface metres above visible water. More force is not a remedy for that mismatch.
Weight 1 was visibly better and is now the default. The maintainer also reported
a bottom-positioned prefab pivot and requested half-depth immersion plus shared
runtime controls for observing 40-50 floes at once. The previous hover did not
show the collider center/pivot, so it cannot establish their exact separation.

## One dynamic force driver

The IceFloeClimb partial component owns the simulation entry point, invoked by
the existing Floating.CustomFixedUpdate prefix. The original method is skipped
only for a registered valid seasonal floe. Floating stays enabled for water
observations, lifecycle, terrain safety and impact/surface presentation.
Unmarked ice and other Floating objects retain their native implementation.

Near owner simulation submits a combined center-of-mass force and a combined
world-space torque, both with ForceMode.Force. It does not assign the body pose or
velocities, make the body kinematic, freeze axes or alter contact materials.
Four water samples measure a plane; they are not four force-application points.
There is no second FixedUpdate or hidden legacy force driver.

Existing missing-water/invalid-height recovery and distant pose handling still
have their narrow velocity/position safeguards. Ownership and distance gates
remain unchanged. Contacts with players, creatures, ships and other floes retain
the normal dynamic solver path; multiplayer/contact acceptance is not claimed
without gameplay checks.

## Shared controls and instance state

All physics tuning fields in Seasons.IceFloeClimb are now public static fields:
spectrum, probe radius/scaling, mass, displacement, waterline, resistance, tilt,
limits, and all six Apply switches. Edit the static fields of the type in RUE.
An edit is read by every existing registered floe on this peer during its normal
callback; newly created/reloaded floes use the same current values. No per-object
copy or prefab serialization can keep an older setting alive. Only a floe owned
by this peer receives its near water forces. Native mass/lifecycle work retains
its previous handling of non-owner instances.

ResetSurfacePhysicsSettings is static and resets these controls for all floes on
this peer. It never resets their pose/velocity. Ordinary spawn, unload, ownership
handoff and world cleanup do not reset the static controls; explicit reset or
plugin reload does. Values are not config keys, not ZDO data and not network
synchronized. A remote owner uses that peer's current values, not this client's
inspector edits. Synchronization/persistence of tuning is not added here.

Body, Sync, references, geometry, ownership, SourceMass, gravity restoration and
captured samples remain instance state. ShowDiagnosticsInHover,
DiagnosticsEnabled and FreezeDiagnostics also remain per-instance: tuning 50
floes must not silently enable expensive full/native comparisons on all of them.
Hover capture is enabled by default; pin detailed capture only on chosen floes.

## Water spectrum and plane

Native CalcWave sums ten CreateWave terms. Only term 0 changes direction with
wind. Each CreateWave multiplies two TrochSin factors; the slower transverse
factor belongs to that wave and is retained, not removed as ripple noise.

The physics sampler calls native CreateWave with native parameters and phase,
shared effective GetWindDir/GetWindIntensity, wrapped game time, surface offset,
Deep North attenuation and the accepted normalized Ocean depth of 1. No seasonal
wind multiplier is applied twice. Frozen water suppresses these waves.

SecondarySwellWeight now defaults to 1: term 0 and native fixed-direction terms
1..4 are included. Terms 5..9 remain excluded. Setting the weight to 0 still
selects primary-only diagnostics; any positive value evaluates all five terms
and changes their amplitudes. This does not modify the rendered water or its
native spectrum. Remaining plane/full/native differences can come from omitted
terms, spatial averaging, depth, wind interpolation and rendering time; do not
hide a changing metre-scale gap with a constant HeightOffset.

Four current mathematical samples are taken at +wind, -wind, +side and -side,
where side = wind cross world-up. The cross is now centered on the collider's
geometric center rather than an assumed root/COM datum. ProbeDistance defaults
to 2 world metres. Optional ScaleProbeDistance projects X/Z scale into the wind
frame without using ordinary pitch/roll as additional sampling scale.

The symmetric cross yields the least-squares plane:

```text
H = (h(+W) + h(-W) + h(+S) + h(-S)) / 4
gW = (h(+W) - h(-W)) / (2 * radiusW)
gS = (h(+S) - h(-S)) / (2 * radiusS)
n = normalize(up - W*gW - S*gS)
```

Target inclination is bounded by MaxSurfaceTilt, not by a Rigidbody constraint.
The same four offsets are sampled 0.05 seconds ahead for surface velocity/normal
change. Horizontal travel uses the geometric center's GetPointVelocity, so an
offset COM and rotation contribute correctly to the sampling location's motion.
Wind and shape are held at the current snapshot during this short derivative;
no stored cross-frame derivative can spike after owner/time/wind changes.

Default wave cost remains 40 single-term CreateWave evaluations per active call;
primary-only uses 8. Diagnostic full/native comparisons add work only during
capture. Geometry work below adds bounded transform math, not collider queries.
No measured performance result is claimed.

## Collider waterline and the bottom pivot

TryHullGeometry reads the referenced Floating collider's local bounds:
BoxCollider.center/size or MeshCollider.sharedMesh.bounds. No hierarchy, renderer,
mesh-vertex or collider enumeration is added. Unsupported/invalid geometry is
reported as NoHullGeometry and uses the existing safety hold rather than silently
pretending that the root pivot is the collider center.

Child transforms and scale are applied once. Intrinsic thickness is the extent
along the floe's un-tilted local up axis, not Collider.bounds.size.y, which grows
when a wide floe tilts. The geometric center is mapped to the current Rigidbody
pose, avoiding a render-interpolated root pose as the physics position. World
bottom/top bounds are retained only as diagnostic observations.

Let C be the actual collider center, T its scaled intrinsic thickness, H the
sampled plane height, f RestingSubmergence, r RelativeDensity, and D the scaled
effective HullThickness. The new height reference is:

```text
targetHullY = H + HeightOffset + (0.5 - f) * T
error = targetHullY - C.y
targetComY = actualComY + error
targetPivotY = actualPivotY + error
effectiveDisplacement = clamp01(r + error / D)
F_buoyancy = mass * g * effectiveDisplacement / r
```

At default f=0.5 and HeightOffset=0 the collider center is on the sampled plane.
For an upright box with its pivot on the bottom, the resulting pivot is half the
scaled collider height below the plane. The actual center-of-mass offset is
accounted for instead of assuming it equals either the root or collider center.
Floating.m_waterLevelOffset is not added to near buoyancy; it is shown in hover
as nativeOffset(unused). There is no second half-height subtraction.

RestingSubmergence is a geometric gameplay waterline, not a new physical density.
RelativeDensity continues to control the virtual displacement reserve and force
saturation. At r=0.9, static lift still caps at weight/0.9. HullThickness controls
virtual stiffness/immersion response, not the collider's measured thickness.
This is an effective slab with an explicitly chosen geometric datum, not an exact
submerged irregular-mesh calculation. NominalHullSubmergence is an upright-bound
height estimate, not an exact volume fraction on a curved wave or tilted mesh.
Dry status and force cutoff refer to the effective slab, not a contact query.

At equilibrium lift still balances body weight. Removing the old datum offset
and using f=0.5 does not introduce a new gravity or an unbounded position spring.
Water-relative vertical damping now compares the collider-center point velocity
with the surface velocity; forces remain applied at COM, without an extra lever
moment. Density, mass, drag limits and tilt gains were not retuned in this pass.

## Preserved resistance, torque and mass

Vertical drag is implicit and bounded by MaxWaterDragAcceleration. Its reference
rate is 2*VerticalDampingRatio*sqrt(g/(r*D)), weighted by effective immersion.
It resists motion relative to water, not the world's zero velocity. Horizontal
drag remains independent and does not impose an X/Z position target.

The implicit tilt PD uses the normal error and angular velocity relative to the
moving normal. It handles inverted floes and imposes no compass heading. Tilt
acceleration is limited; yaw drag is separate. The resulting acceleration is
mapped through the actual rotated inertia tensor and submitted as real torque.
Native Rigidbody damping remains unchanged. No solver contact velocity is erased.

MassMultiplier still defaults to 4 times the saved seasonal floe mass. The actual
mass override is applied on the regular callback after a shared multiplier edit,
not by a new global iteration. SourceMass and conditional restoration remain
per-body. No new mass value is written to ZDO; another mod's replacement mass is
not overwritten every tick. Effective reserve load remains mass*(1/r - 1), so the
model does not support arbitrarily heavy objects by enforcing a pose.

## Shared field defaults

| Field | Default | Role |
| --- | --- | --- |
| ProbeDistance / ScaleProbeDistance | 2 / false | Cross spacing, optional X/Z scale |
| SecondarySwellWeight | 1 | Primary plus four secondary large waves |
| RestingSubmergence | 0.5 | Geometric waterline, larger means deeper |
| HeightOffset | 0 | Extra world-height adjustment, positive means higher |
| MassMultiplier | 4 | Actual collision mass multiplier |
| HullThickness | 1 | Effective slab thickness before scale Y |
| RelativeDensity | 0.9 | Effective reserve lift, not geometric waterline |
| VerticalDampingRatio | 1 | Relative-water vertical damping |
| MaxWaterDragAcceleration | 6 | Vertical drag limit, m/s^2 |
| HorizontalDamping | 0.15 | Horizontal drag rate, 1/s |
| TiltFrequency / TiltDampingRatio | 0.65 / 1 | Tilt response Hz and damping |
| MaxTiltAcceleration / MaxSurfaceTilt | 1.5 / 45 | rad/s^2 and target degrees |
| YawDamping | 0.15 | Yaw drag rate, 1/s |

All six static switches default to true: ApplyBuoyancy,
ApplyVerticalWaterDamping, ApplyHorizontalWaterDamping, ApplySurfaceAlignment,
ApplyTiltDamping and ApplyYawDamping. Runtime values are bounded for calculations
without rewriting inspector fields. The captured Settings record stores the
values used by that call; editing this retained record does not tune physics.

## Hover and the next maintainer pass

The hover header includes shared settings. Diagnostics adds:

- gap plane/native, separating spectrum mismatch from geometric placement;
- Y pivot/hull/COM, collider type, intrinsic thickness and world bottom/top;
- target/actual nominal waterline fractions and shared HeightOffset;
- target pivot/hull heights and the unused legacy native offset;
- water/hull relative vertical velocity (the previous-step record still shows COM).

Existing normals, error, forces, torque, accepted impulses and following-step
observations remain. No sample is a claim of isolated solver response: contacts,
network sync and other scripts still act between observed callbacks.

For the next pass, reset shared physics once, keep all Apply switches on,
SecondarySwellWeight=1, RestingSubmergence=0.5 and HeightOffset=0. Keep the same
wind. Observe 40-50 nearby floes without standing on them, then copy hover from
one ordinary floe and visibly misplaced examples of different scales. All
reported near samples should show local=True, distant=False, gravityHold=False.
Two samples a few seconds apart plus a brief movement description remain useful.
A small number of selected diagnostic captures is enough; there is no need to
pin detailed capture on every visible floe.

If placement is still systematically too high/low, adjust the shared
RestingSubmergence (scale-aware geometric fraction) or HeightOffset (world metres)
one at a time, not density/torque gains. Dynamic plane/native mismatch is a
separate issue. The new geometry and waterline need runtime confirmation.

The distant full-spectrum Dampen path and its existing height convention are
unchanged. New near-waterline controls do not bypass distance gates or make remote
peers follow local edits. Treat distant placement/near-far transitions as a
separate follow-up, not as evidence that the shared near settings were ignored.

## Verification boundary

No mod build, mod tests, physics simulation or game execution is performed here.
Validation is source/API review, byte/hash verification of the source base,
lexical C# structure, targeted reference/static-field and diff/English-text
checks. The two code paths are already included in Seasons.csproj. This follow-up
changes no dependencies, project Compile entries, versions, packages, snow logic
or persistent/network data schema. Runtime appearance and collision acceptance
remain with the maintainer.
