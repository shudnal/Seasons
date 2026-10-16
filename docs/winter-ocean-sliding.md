# Seasonal ice sliding

The balanced travel cycle builds on `feature/winter-ocean-sliding` at
`00da7d9b570e60b5eacd22d9768b404775cd07d8` (Seasons 1.10.6).
Game source reviewed: `shudnal/assemblies_combined` at
`6bfb1776f3f59869fcf67afae3b12885e14ad6a1`.

## Surface and actor policy

Only the existing frozen-water `IceSurface` belongs to this movement domain.
There is no biome, water-depth, or underwater terrain query in its routing.
Native slippery surfaces and floating floes are separate domains. No launch
velocity, readiness, or native `m_slipperySpeed` is transferred between them.
Leaving seasonal ice releases the seasonal state; it does not zero the Rigidbody.

| Actor / equipment | Seasonal frozen water |
| --- | --- |
| Player, ordinary footwear (default) | Basic Seasons inertia, steady run preparation, optional timed glide |
| Player, ordinary footwear (`Use vanilla slipping`) | Native slippery movement; no Seasons basic inertia, run-up, long glide, cadence or FOV cue |
| Player, ice skates (`m_skating`) | Native slipping and skating, with native coefficients |
| Ice shoes / snowshoes (`m_iceShoes`) | No additional seasonal slipping; takes precedence over skates |
| Non-player character | Native slipping; no Seasons movement state or custom slide triggers |

The master toggle disables both custom movement and the native-slip extension on
seasonal ice, including creatures and skates. It does not disable slipping on the
game's own surfaces. A zero basic slipperiness factor also disables the seasonal
extension. The old shallow-water switch has been removed entirely.

`Use vanilla slipping` switches ordinary players on seasonal ice to the game's
native slippery movement, without altering the other surface and footwear rules.
Changing this setting clears existing Seasons slide preparation, glide and
animation ownership without resetting Rigidbody velocity. Native movement takes
over on the next walking update. The master toggle and nonzero basic factor are
still required; the basic factor's magnitude does not modify native coefficients.

## Running and preparing a glide

Running does not receive a physical speed bonus from Seasons. The requested target
is at most the fully modified speed already calculated by `Character.UpdateWalking`,
including the game, equipment, skills, status effects and earlier movement patches.
`CheckRun` and speed modifiers are not called again. Existing external momentum can
remain above this target and settle under basic braking; the cap is not a global
Rigidbody speed limit and does not erase collision, combat or jump impulses.

A first full preparation has two consecutive stages:

1. Run on seasonal ice in approximately the same direction for **3 seconds**.
2. Continue for **2 seconds** while locomotion cadence and the FOV cue ramp up.

The second stage must finish to earn a launch. At full charge the FOV cue reaches
**+10 degrees** by default; release movement to glide. An earlier release keeps
basic inertia and gives no boost. Readiness remains available while qualifying
running continues. Actual speed and heading are checked again on release.

Run-up counts actual horizontal speed, not held-key time. The input and actual
velocity must stay aligned with each other and the preparation heading. Larger
turns restart preparation. Running against an obstacle, airborne time, blocking
actions and input captured by UI do not charge a glide.

Releasing Shift slightly before movement retains a completed charge for **0.25
seconds**, making the release gesture tolerant of keyboard event ordering. This
window neither accumulates charge nor preserves it through an action, obstacle,
UI capture, or loss of ice contact. Shift alone never cancels an active glide.

The visual cadence bonus defaults to **17.5%**: the former 25% run-up bonus reduced
by 30%. This reduces the *bonus*, not the complete base animation speed. The old
additional 10% movement multiplier no longer contributes to movement or cadence.
Only synchronized forward/sideways locomotion parameters are scaled. There is no
`Animator.speed` override and no animation-speed change for attacks, dodges or jumps.
The exact visual result depends on the player's existing Animator blend tree.

## Finite glide speed profile

A completed charge grants one launch toward **110%** of the captured running
speed. It is applied through the existing walking force path, not by a second
`AddForce` or a direct Rigidbody assignment. Launch never multiplies the current
glide speed, and external speed already above the launch target is not amplified.
The charge is consumed even if another movement mod suppresses that force.

For a straight, unobstructed glide without additional contact losses:

| Time after release | Speed relative to captured running speed | Additional FOV |
| --- | ---: | ---: |
| 0 seconds | 110% | +10 degrees |
| 15 seconds | 105% | +5 degrees |
| 30 seconds | 100% | 0 degrees |
| 35 seconds | 95% | 0 degrees |
| 40 seconds | 90%; switch to basic inertia | 0 degrees |

The first interval removes the configured speed bonus linearly. The second
interval removes the remaining configured fraction linearly. The animated glide
ends at its duration limit even if an external impulse kept the body faster.
Reaching the configured end-speed fraction earlier also ends it.

Every step subtracts the scheduled loss from **actual horizontal Rigidbody
velocity**. The nominal profile is a loss schedule, not a target servo. Turns,
contact friction, obstacles and other mods may reduce the speed further; Seasons
does not restore those losses or pull the player back to an old launch vector.
Vertical speed is never used to increase horizontal speed.

While gliding, look yaw steers the trajectory at a bounded angular rate. The entry
heading offset is retained, so entry does not snap travel toward the camera.
Controller look uses the same existing look direction. Turning loses speed and
cannot generate acceleration. The character faces actual travel after the native
walking rotation without changing its look yaw.

## Renewing the boost

During the first **30 seconds**, while actual speed is still above **100%**,
returning directly to aligned running skips the initial 3-second stage. The full
**2-second charge** is still required before the next launch.

Movement immediately cancels the glide animation and returns to basic handling.
Only a successful native running calculation and aligned movement consume the
one-use fast-recharge permission. Walking, braking, attacking, jumping, a large
turn or a late restart does not retain that permission. After the bonus phase,
preparation starts from the full 3 + 2 seconds again.

Every new launch has a fresh finite profile and a bounded 110% target; bonuses do
not stack. Tapping movement or Shift without completing preparation cannot inject
another boost. Repeated travel therefore requires actual running and its normal
stamina costs, rather than indefinite passive gliding.

## Other controls and interruptions

Movement keys / stick input, attack, block, jump, dodge, crouch, auto-run,
interaction, item use, equipment changes and other movement or animation actions
cancel the long glide and its animation. Opposing keyboard keys are not a release
gesture. Actions are not consumed or suppressed: their native logic still runs.
Run / Shift / JoyRun is only a modifier; pressing, holding or releasing it alone
does not cancel a glide, change drag or grant acceleration.

Opening a map, inventory, console or other UI does not by itself cancel an active
glide. Input capture does prevent charging or starting a new glide: the controller's
suppressed input is not a deliberate release. Raw movement keys are not interpreted
as gameplay while UI has input focus. Use without an interaction does not cancel.

Cancellation does not zero Rigidbody velocity. Native root motion, jump impulses
and pushback still have their normal downstream effects. A jump ends the glide;
airborne movement targets are not replaced by Seasons. No input keeps native
inertial flight, movement uses native air control, and look alone no longer steers.
Landing on seasonal ice resumes basic inertia at actual remaining velocity, with
no retained charge. Other surfaces keep their own movement domain.

## FOV integration and compatibility

The FOV cue grows with the 2-second charge. During the glide it follows the
**actual positive speed bonus**, capped by the configured launch bonus. One
percentage point gives one additional degree by default. Speeds below 100% never
narrow the base view. Remaining physical bonus can remain visible while recharging.
The cue and its scale are local visual preferences, independent of server physics.

The camera transpiler adds the offset to the existing main- and sky-camera writes
in `GameCamera.UpdateCamera`. It does not modify `m_fov`, `m_fovBase`, `m_fovTarget`,
player preferences or a cached base FOV. Offsets cannot accumulate across frames.
Cancellation, death, footwear changes, disabling, thaw, logout and camera replacement
leave no persistent FOV override: the next normal camera write uses its own base.
Free-fly is not affected. Other camera effects using the native FOV fields remain
upstream of the additional offset.

Both camera writes must have the recognized non-accumulating FOV source. If another
transpiler has replaced the path or it is ambiguous, only the visual cue is omitted
with a warning. Cadence likewise requires both known locomotion writes. These
optional integrations never reconstruct or overwrite an unknown animation/camera
path. A later mod replacing the camera output can override the cue; custom projection
matrices or VR camera providers may also control projection independently.

## Motion integration and small corrections

The movement integration stays inside `Character.UpdateWalking`:

1. Resolve seasonal slip eligibility after the native `CheckRun` result is assigned
   to `m_running`.
2. Capture the fully modified requested speed before multiplication by movement
   direction. Update preparation without increasing the physical running target.
3. Modify only the local physics velocity of the owning ordinary-shoe player with
   current seasonal ground contact, immediately before native root motion. Keep
   `m_currentVel` independent so passive coasting does not animate ordinary walking.
4. Retain the real patched root-motion call, pushback, ground forces, air control,
   velocity-change force application and downstream velocity hooks.
5. Resolve `s_slipping` in the existing synchronized bool write. Ordinary players do
   not enable `Character.m_slipping` merely to animate a Seasons glide.

The small-correction integration is unchanged. The native 0.01 velocity-delta
threshold is relaxed only for a finite nonzero grounded Seasons correction in the
current invocation. Altered thresholds and ambiguous force guards are respected.
The force vector, mode, clamp and call are preserved. There is no second force
application or global Rigidbody patch.

There is one local player state, no creature dictionary, new component, coroutine,
raycast, terrain sampling, RPC or movement-state persistence. The existing player
movement and animation replication remain in use. Native slippery surfaces,
creatures and footwear modes retain their previous behavior.

## Live configuration

All entries are declared in the common `Seasons.ConfigInit` under
**Winter ocean - Slipperiness**. Gameplay values are server-controlled. FOV cue
preferences are client-side. Existing basic-inertia parameters remain available.

| Setting | Default | Purpose |
| --- | ---: | --- |
| Enabled | true | Entire seasonal sliding extension |
| Use vanilla slipping | false | Use native slippery movement for ordinary players on seasonal ice instead of Seasons basic and long glide |
| Basic slipperiness factor | 1.25 | Divides basic braking, lateral grip and idle loss |
| Movement speed multiplier | 1.00 | Driven target only, range 0.1..1.0 |
| Movement acceleration | 12 | Basic acceleration, m/s squared |
| Basic steering acceleration | 5 | Lateral grip before slipperiness scaling |
| Basic braking deceleration | 4 | Braking before slipperiness scaling |
| Basic coasting deceleration | 1.5 | Idle loss before slipperiness scaling |
| Basic coasting minimum speed | 1 | Below this speed use basic braking |
| Basic collider friction | 0.05 | Additional physical contact friction |
| Long glide enabled | true | Enable preparation and animated travel |
| Steady run duration | 3 | Initial stable running, seconds |
| Run-up speed fraction | 0.85 | Minimum actual / requested running speed |
| Run-up alignment angle | 25 | Maximum deviation, degrees |
| Glide charge duration | 2 | Visual preparation; also required for renewal |
| Glide release grace | 0.25 | Run/movement release ordering tolerance, seconds |
| Long glide speed bonus | 0.10 | Earned launch bonus; not running acceleration |
| Long glide bonus duration | 30 | Time from launch to 100%, seconds |
| Long glide coast duration | 10 | Additional time to end-speed fraction, seconds |
| Long glide end speed fraction | 0.90 | Early exit threshold and profile endpoint |
| Run-up animation speed bonus | 0.175 | Visual locomotion bonus at full charge |
| Glide FOV cue | true | Local visual toggle |
| Glide FOV degrees per percent | 1 | Local additional degrees per bonus point |
| Long glide turn speed | 25 | Degrees per second |
| Long glide turning speed loss | 0.4 | m/s lost per radian turned |
| Long glide collider friction | 0 | Contact friction during animated gliding |
| Movement input dead zone | 0.10 | Analog release threshold |

The old `Run-up duration`, `Run-up speed bonus`, `Run-up full bonus time`,
`Long glide deceleration` and `Long glide minimum speed` keys are no longer bound:
their values describe the previous model and are not silently reused for the new
one. Existing `Movement speed multiplier` values above one are clamped to one.
No other settings or localization files are rewritten.

Parameters are read during play. The running-speed reference is captured from real
running and retained for that glide; changing equipment cancels it. Changing a
profile duration or loss parameter affects subsequent steps without restoring lost
speed. Disabling the master or long-glide toggle immediately releases local state.
Toggling `Use vanilla slipping` also releases custom state. While enabled, custom
speed, braking, preparation, glide, cadence and FOV settings do not affect native
slipping.

The read-only `seasons_sliding` command reports mode, ground contact, actual speed,
captured running speed, their ratio, steady-run and charge progress, readiness,
elapsed glide time, animation ownership, transition reason, FOV offset and visual
hook availability. It does not enable background sampling or change gameplay.

## Manual checks

- Run continuously on ice: body speed does not accelerate with charge; cadence and
  FOV do. Release after a full charge, and separately just before it completes.
- Glide straight and inspect speed/FOV at 0, 15, 30 and 40 seconds. Repeat with a
  different running skill/equipment speed and at different physics/frame rates.
- Release Shift just before W within the grace window; repeat outside that window.
  Press/hold/release Shift during a glide: no cancellation or free acceleration.
- Return to aligned Shift + movement at 15 seconds: only 2 seconds of preparation.
  Repeat after 30 seconds, below 100% following a turn, and without running.
- Tap movement and release immediately: no renewed boost. Hit an obstacle and
  confirm neither the nominal profile nor FOV reconstructs the lost speed.
- Turn, attack, jump, dodge, interact and change footwear. Check air control and
  basic inertia after landing, with no automatic return of charge.
- Open UI during a glide versus during preparation. Test master/long-glide/FOV
  toggles, thaw, death, logout and free-fly for leftover visual state.
- Enable `Use vanilla slipping` while gliding, then disable it while on ice.
  Verify that native movement and animation work without leftover speed, FOV or
  cadence effects. Repeat with ice shoes, skates, creatures and native icy ground.
- Verify another client sees the cadence and glide animation. Compare with camera
  and movement mods and inspect any optional-integration warnings.

Validation is static source/API and diff inspection only. No mod build, mod tests,
or Valheim runtime execution is performed. Coefficients remain tunable starting
values. Release version, mechanic toggles, EWD/Marketplace compatibility, crops,
snow simulation, hoe clearing and floe physics are outside this change.
