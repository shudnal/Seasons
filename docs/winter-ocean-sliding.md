# Seasonal ice sliding

Base: Seasons 1.10.6, master `4054af6e8823d787921d673607e4942554872b66`.
Game source reviewed: `shudnal/assemblies_combined` at
`5a2365409cff644d6adaccd2b308178cc4179b19` (1.0.16).

## Surface and actor policy

Only the existing frozen-water `IceSurface` belongs to this movement domain.
There is no biome, water-depth, or underwater terrain query in its routing.
Native slippery surfaces and floating floes are separate domains. No launch
velocity, run-up readiness, or native `m_slipperySpeed` is transferred between them.
Leaving seasonal ice releases the seasonal state; it does not zero the Rigidbody.

With the master toggle enabled:

| Actor / equipment | Seasonal frozen water |
| --- | --- |
| Player, ordinary footwear | Basic Seasons inertia, steady run-up, optional long glide |
| Player, ice skates (`m_skating`) | Native slipping and skating, with native coefficients |
| Ice shoes / snowshoes (`m_iceShoes`) | No additional seasonal slipping; takes precedence over skates |
| Non-player character | Native slipping; no Seasons movement state or custom slide triggers |

The master toggle disables both custom movement and the native-slip extension on
seasonal ice, including creatures and skates. It does not disable slipping on the
game's own surfaces. A zero basic slipperiness factor also disables the seasonal
extension. The old shallow-water switch has been removed entirely.

## Player controls

Run steadily on seasonal ice, then release movement to enter a long glide once the
run-up requirement is met. Releasing only Run while continuing to hold movement
stays in basic inertia. Run-up counts actual horizontal speed and alignment with
intended movement, not button-hold time. Airborne time, blocked movement, attacks,
sharp direction changes and running into a wall do not charge a glide.

Speed increases gradually while running, relative to the fully modified native
running speed. Releasing input does not multiply velocity or inject an impulse.
Repeated Run presses cannot restore a lost launch speed.

While gliding, look yaw steers the trajectory at a bounded angular rate. The entry
heading offset is retained, so entering a glide does not snap travel toward the
camera. Controller look uses the same existing look direction. Turning loses
speed and cannot generate acceleration. The character faces actual travel after
the native walking rotation without changing its look yaw.

Movement keys / stick input, a new Run press, attack, block, jump, dodge, crouch,
interaction, item use, equipment changes and other blocking actions cancel the
long glide and its animation. Opposing keyboard keys are not a release gesture.
Opening inventory, map, a store or build UI also cancels the long glide. Actions
are not consumed or suppressed: their native logic still runs. The intended Run
cancellation currently reads the processed run argument; a raw Run press without
movement can be discarded by the controller. Raw-button handling remains pending.

Cancellation changes the mode to basic inertia without zeroing velocity or
restoring a remembered vector. Native root motion, jump impulses and pushback
still have their normal downstream effects. A new long glide needs a new run-up.
A jump cancels the glide; vertical speed is not converted into horizontal speed.
The current airborne override preserves actual horizontal velocity but also prevents
normal input steering in the air. Restoring that control is a separate pending
change. Landing on another surface ends the seasonal state rather than transferring
it into a different slip motor.

## Motion and animation integration

The integration stays inside `Character.UpdateWalking`:

1. Resolve seasonal native-slip eligibility after the native `CheckRun` result has
   been assigned to `m_running`. Never call `CheckRun` a second time.
2. Adjust the final requested walking speed after native speed modifiers and before
   it is multiplied by movement direction.
3. Set only the local physics velocity for the owning local ordinary-shoe player,
   immediately before the native root-motion call. Use actual Rigidbody X/Z velocity
   every step, not a launch cache. Keep the original m_currentVel blend unchanged
   so passive coasting does not masquerade as commanded walking in the Animator.
4. Call the real patched root-motion method and retain native pushback, ground
   forces, air control, velocity-change force application and velocity hooks.
   Other surfaces retain the complete native slope and movement path.
5. Resolve the final slipping animation value at the existing synchronized
   `ZSyncAnimation.SetBool(s_slipping, ...)` call.

Custom gliding never enables `Character.m_slipping`. Animation ownership is released
on cancellation, invalid actor state, footwear changes, disable, thaw and world
reset. There is no per-frame false/true correction pair; exceptional exits can
clear an owned animation once. Animator behavior is assumed to be visual for this
iteration; no global root-motion override or animation-controller replacement is
included. Actual clip behavior remains a gameplay verification item.

Basic inertia separates forward acceleration, lateral grip, opposing-input braking
and idle coast deceleration. Its combined velocity cannot exceed the larger of
actual speed and requested speed merely by combining lateral and forward control.
External impulses are not clamped to a fixed running-speed ceiling. Long gliding
uses linear deceleration and additional loss proportional to actual turning angle.

There is one local player state, no creature dictionary, new component, coroutine,
raycast, terrain sampling, RPC, or network persistence for the movement state.
The game's existing animation and player movement replication remain in use.
The former ground-contact, SyncVelocity, SetRun, dodge-velocity cache and late
ApplyGroundForce blending hooks are removed. Integration mismatches log a warning
and disable the seasonal extension rather than combining partial custom motors.

## Small velocity corrections and other movement mods

The native walking force guard ignores a velocity delta of 0.01 m/s or less.
At a 0.02 s step the default glide deceleration requests only 0.005 m/s, so it
must not be lost on every straight, grounded glide step.

The walking transpiler runs at Priority.Last and recognizes the magnitude guard
immediately around the same local velocity delta passed to this Character body's
AddForce(Vector3, ForceMode.VelocityChange). It keeps the original constant, branch,
force vector, force mode and call. Only the comparison threshold becomes zero when
all of these conditions hold:

- The incoming threshold is still exactly the native 0.01 value. Another mod's
  changed value is returned unchanged, whether it is smaller, larger or zero.
- A fresh local flag confirms that the Seasons grounded solver contributed a
  finite, nonzero horizontal correction in this UpdateWalking invocation.
- The owning local ordinary-shoe player is still eligible and on seasonal ice.

The marker is initialized per invocation and stored after our existing inertia /
root-motion wrapper returns. It is not a cached per-player or per-frame permission.
Airborne movement, other actors, skates, shoes, native surfaces and inactive or
skipped seasonal processing keep the existing threshold. Strict greater-than
comparison still excludes a zero delta and unordered (NaN) comparisons.

There is no second AddForce, Rigidbody setter, global force patch or replacement
of another mod's result. Native force clamping, root motion, pushback, ground forces
and other pre-existing conditions remain. If the local force-guard shape is absent
or ambiguous, only this precision adjustment is omitted with a patch-time warning;
the existing movement path is left intact. Priorities cannot guarantee compatibility
with every later transpiler or a mod replacing the whole movement method.

Airborne steering and raw Run-button edge handling are separate pending follow-ups;
this change does not alter either input behavior or the airborne velocity override.

## Live tuning

All entries are declared in the common `Seasons.ConfigInit`, in the single new
section **Winter ocean - Slipperiness**, using server-controlled configuration.
No JSON settings are added. Values take effect during play. The master toggle
also immediately discards the local state and releases its animation.

| Setting | Initial value | Units / purpose |
| --- | ---: | --- |
| Enabled | true | Entire seasonal slipping extension |
| Basic slipperiness factor | 1.25 | Divides basic braking, lateral grip and coasting loss |
| Movement speed multiplier | 1.10 | Multiplies the fully modified native requested speed |
| Movement acceleration | 12 | m/s squared |
| Basic steering acceleration | 5 | m/s squared, before slipperiness factor |
| Basic braking deceleration | 4 | m/s squared, before slipperiness factor |
| Basic coasting deceleration | 1.5 | m/s squared, before slipperiness factor |
| Basic coasting minimum speed | 1 | m/s; slower idle movement settles using braking |
| Basic collider friction | 0.05 | Physical capsule contact friction |
| Long glide enabled | true | Earned player glide, independent of basic inertia |
| Run-up duration | 2 | Seconds at qualifying actual speed and alignment |
| Run-up speed fraction | 0.85 | Fraction of modified running speed |
| Run-up alignment angle | 25 | Degrees from intended movement |
| Run-up speed bonus | 0.25 | Maximum additional speed fraction while running |
| Run-up full bonus time | 3 | Seconds of qualifying run-up for the full bonus |
| Long glide deceleration | 0.25 | m/s squared; not scaled by basic slipperiness |
| Long glide minimum speed | 3 | m/s; returns to basic inertia below this speed |
| Long glide turn speed | 25 | Degrees per second |
| Long glide turning speed loss | 0.4 | m/s lost per radian turned |
| Long glide collider friction | 0 | Physical capsule contact friction |
| Movement input dead zone | 0.10 | Analog release threshold; keyboard keys remain explicit |

These values are starting points for runtime tuning, not measured balance.
Old entries under `Season - Winter ocean` are no longer bound. The basic factor
now has its own key in the new section; previous values are not silently mapped to
the new force-based model. Unrelated configuration and localization are unchanged.

The read-only console command `seasons_sliding` reports local mode, grounded state,
actual horizontal speed, run-up time, animation ownership, transition count and
last reason. It performs no background sampling and makes no gameplay changes.

## Manual checks

- Short run/release, walking turns and braking; compare basic duration and response.
- Glide straight with a low deceleration and zero glide collider friction. Verify
  a gradual speed decrease without needing to turn. Compare with other movement
  mods and inspect any force-guard warning; their own thresholds are not overridden.
- Earn a run-up, release movement, steer with look only, then cancel separately with
  each movement key, jump, dodge, attack, block, interaction and item use. Confirm
  immediate animation cancellation without a synthetic stop or later speed rebound.
- Hold opposing movement keys, use controller drift near the configured dead zone,
  and repeatedly tap Run. None should create a free glide impulse.
- Hit an obstacle, jump, fall vertically onto ice, equip skates or ice shoes, cross
  shallow/deep ocean areas, walk onto land and native slippery surfaces, and thaw.
- Toggle the master and long-glide options during movement, then leave and re-enter
  the world. There should be no retained owned animation or old readiness.
- Spawn ordinary creatures and observe the native response on the frozen surface.
  Creature behavior has not been evaluated in-game by the assistant.
- With another client, inspect the same trajectory and synchronized animation.

Validation is source/API and diff inspection only. No mod build, mod tests, or
Valheim runtime execution was performed. The release version remains 1.10.6;
Marketplace compatibility, snow simulation, hoe clearing and floe physics are not
part of this change.
