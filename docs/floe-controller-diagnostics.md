# Floe controller diagnostics

## Scope

This diagnostic iteration starts from `e44249803b43f3cc0b2e6275d82d7c1b808f63e3`.
It deliberately supersedes the earlier requirement to keep per-floe physics state
outside MonoBehaviours until the active wave behavior has been diagnosed.

The existing `IceFloeClimb` component now owns its references, support points,
water observations, ownership transitions, gravity safeguards, distant pose,
force submission and diagnostic samples. It is one partial class, split between
`Utils/IceFloeClimb.cs` (interaction/lifecycle) and
`Controllers/SeasonalIceFloeWaves.cs` (controller implementation).

`SeasonalIceFloeWaves` retains only shared wave inputs/math, distance settings,
callback lookup tables pointing to the actual components, and the existing
bounded distant-target service. There is no separate per-floe state object or
second force driver. Native `Floating.CustomFixedUpdate` still invokes the
controller through a prefix, exactly once for that call. No independent
`FixedUpdate` was added. Native center buoyancy and damping still execute after
the additional forces when the original method is allowed.

Placement, cleanup, snow simulation, snow accumulation throttling, mass, balance,
damping, the force formula and default wave behavior are not changed here.
This is instrumentation and a state relocation, not a confirmed physics fix.

## Using the inspector and hover

Open `IceFloeClimb` on a locally owned nearby seasonal `ice1` in RUE.

- `ShowDiagnosticsInHover` defaults to true for this diagnostic iteration.
  Hovering requests capture from the next real physics call and keeps it active
  for 0.5 seconds after the latest hover request. The text refreshes every 0.2 seconds.
- `DiagnosticsEnabled` pins capture on this particular component without hover.
- `FreezeDiagnostics` freezes the captured sample, not the simulation or the
  current live state. Frame/time and sampled modes identify the retained sample.
- `RebuildWavePoints()` requests a normal lazy cache rebuild.
- `ClearWaveDiagnostics()` clears samples and counters without changing physics.

Detailed comparisons and native accumulator reads run only while capture is
requested. Their arrays and text builder are reused. Hover formatting does not
query water, build support points, apply forces, or log to the console.
Diagnostic hover does not require that the character is in climbing range.
A narrow HUD fallback appends the same block when another `Hoverable` on the
object is selected by the game; the native IceFloeClimb path is not appended twice.

Inspect `Status`, `LastRunFrame`, `LastForceCalls`, `TotalForceCalls`, ownership,
`PointsReady`, `Distant`, `HoldingGravity`, and the referenced native components
before interpreting a previously captured `Diagnostics` record.

Do not benchmark while hovering or pinning detailed diagnostics. Comparing with
old liquid queries intentionally adds work for the selected object.

## Point comparison in one physics call

`Diagnostics.Probes[0..3]` uses the original along/across-wind query ordering.

| Field | Meaning |
| --- | --- |
| `LocalPoint` / `CachedWorld` | Stored collider-local support and its current world position |
| `BuildRoundTripError` | Distance between the original ClosestPoint result and TransformPoint(InverseTransformPoint(result)) at the last cache build |
| `FreshWorld` | New ClosestPoint query with the same current effective wind; isolates cache reuse from changes in wind selection |
| `CacheErrorXZ` / `CacheErrorY` / `CacheError` | Horizontal, signed vertical and total difference from FreshWorld |
| `LegacyWorld` / `LegacyError` | Fresh 1.8.2-style query direction from EnvMan.GetWindData and its difference from CachedWorld |
| `OutsideDistance` | Distance from CachedWorld to collider.ClosestPoint(CachedWorld); zero means inside or on the collider, not necessarily the same surface support |
| `AppliedWorld`, `AppliedWater`, `AppliedImpulse`, `Applied` | Actual selected input and whether AddForceAtPosition was called |

The fixed-geometry assumption remains: only a cumulative horizontal floe or wind
turn of at least 1 degree rebuilds the four cached supports. No ReadGeometry,
mesh/scale watcher or collider-hierarchy scan has been restored.

A round-trip error should be close to floating-point precision. Drift from a
fresh ClosestPoint result between rebuilds is a different measurement and is an
accepted approximation, not automatically a coordinate conversion bug.

Do not mix a parent's InverseTransformPoint with a child's TransformPoint.
No diagnostic automatically projects an applied point back onto the collider.

## Independent per-object A/B controls

The defaults remain `PointMode=Cached`, `WaterMode=Mathematical`.

`PointMode`:
- `Cached`: current cached collider-local supports.
- `FreshClosestPoint`: fresh supports using the same effective wind as Cached.
- `LegacyClosestPoint`: fresh supports using 1.8.2-style wind interpolation,
  obtained through EnvMan.GetWindData rather than direct static wind fields.

`WaterMode`:
- `Mathematical`: current shared native wave mathematics.
- `NativeLiquidLevel`: original Floating.GetLiquidLevel at the selected point.
  Invalid liquid values are skipped; they are never interpreted as height zero
  or used as a large downward force.

Only the selected combination applies four forces. All other combinations are
read-only estimates, never extra forces. Controls are local, are not saved in
ZDO or synchronized, and return to their defaults on a new instance. Apply A/B
changes only on the locally authoritative test floe. The central missing-water
fallback and distant Dampen retain their current behavior in every combination.

The legacy selection reproduces the old active sampling approach on the current
game; it does not load the old game's prefab, physics engine or water formulas.

## Submitted inputs versus simulation results

`SubmittedAngularImpulse` is the sum of `(point - centerOfMass) x impulse` for
actual force calls. `EngineAngularImpulse` and `EngineImpulse` are differences
in Rigidbody.GetAccumulatedTorque/GetAccumulatedForce around that force block,
converted from ForceMode.Force units using the current physics delta.

These are inputs recorded before native Floating and the physics simulation,
not a measurement of resulting tilt or of all contacts/damping. Alternative
cached/fresh/native combinations are explicitly labeled expected values.
Missing native water remains visible as an invalid height and validity count.

AddForceAtPosition does not require that its world-space position lies inside
the collider. An external point still supplies a force and a moment. The moment
uses the perpendicular lever arm, so a distant wrong point can produce excessive
rotation. With a vertical impulse, horizontal position error changes the lever
arm; vertical position error can still change the buoyancy impulse calculation.

## Verification boundary

The changed source received a manual callback/lifecycle and API review,
lexical delimiter checks, an English-text scan, and published blob verification.
Both existing source paths are already included in Seasons.csproj.
No build, automated tests, Valheim run, or FPS/GC/network measurement was performed.

Owner-run checks: default motion/climb; point round-trip and drift; one-variable
A/B changes; valid native probes; submitted versus engine impulses; pause/freeze;
re-enable and missing WaterVolume; ownership handoff and distant-to-near return.
Disable detailed capture before performance measurements.
