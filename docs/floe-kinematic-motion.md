# Native-owned dynamics and ownerless kinematic motion

Implementation base: `b03fffacce39a8f0d1a4e461701bb82a4de21eb2`, PR #45.
Game reference: `assemblies_combined` at `d1374bfd9175ac8f733ae483b0a06e5c8b75906e`.

## Body mode versus authority

Within visible waves, actual native `HasOwner()` selects body mode:

| Native state | Body and controller |
| --- | --- |
| Local native owner | Original dynamic Rigidbody, existing water forces and torque |
| Remote native owner | Dynamic native replica and normal game synchronization |
| No native owner, local confirmed lease | Non-colliding kinematic body following the forecast and publishing pose |
| No native owner, foreign lease or candidate waiting | Non-colliding kinematic replica smoothing received ZDO pose |

`IsOwner()==false` alone does not select kinematic motion. A remote native owner
still represents interactive native physics. No ownership is claimed or cleared
to enforce body mode. Native ownership arrival restores dynamics immediately on
observation, before running the next ordinary native sync/force path. There is
no collision-triggered activation or separate proximity search.

Beyond visible waves the existing local FarVisual remains dominant: flat water,
small bob, no collision response and no pose publication. Free-camera distance
still defines visible waves; native ownership remains defined by the game. A
free camera does not itself turn an ownerless floe into a native-owned body.

## Ownerless motion

The existing one-second candidate settling, three-second server-world heartbeat
lease and native-owner precedence remain. Intentional ownerless kinematics is
not a failure of lease eligibility. Lease publication still checks the current
owner, token and heartbeat before writing. It uses the existing ZDO pose and
velocity fields; no persistent schema or custom RPC is added.

Ownerless bodies skip per-fixed-step wave forces. Their pose is serviced from
OwnerSync, normally LateUpdate, at most once per rendered frame. The local lease
holder reuses the current distance-dependent forecast, full-water heave, filtered
normal and 70-percent collider waterline. The recent soft wind-refresh scheduling
is unchanged. No forecast is built merely to update a foreign lease replica.

The geometric center remains fixed horizontally while height and tilt approach
the forecast with a shared `KinematicResponseSeconds=0.15` response time. Moving
the pivot compensates for its offset from the collider center. This deliberately
does not preserve free horizontal drift outside native ownership. Pose-derived
center-of-mass velocity and angular velocity are stored separately, not assigned
to a kinematic Rigidbody. They are published and used when restoring dynamics.

Replica pose/scale reception is also performed once per render frame without
water forces or Rigidbody velocity assignments. It follows the latest ZDO pose
with time-based smoothing, not unlimited extrapolation. Fresh published velocity
is retained for native handoff; it expires after 0.5 seconds without a new ZDO
revision. No replica collision outcome is published.

Both ownerless modes disable collision detection and native interpolation while
under script pose control. The original collision mode, collision flag,
interpolation, gravity and native sync body-type flag are restored on native
ownership, exit to FarVisual or component release. FarVisual saves/restores the
underlying dynamic state, not a nested ownerless override. The existing same-peer
lease-to-native handoff keeps its current pose instead of adopting its own older
publication. Other native acquisitions retain the game's pose adoption.

Only native-owned nearby floes are intended to react physically to projectiles,
players, creatures and ships. There is no promise of physical hits on unloaded
or non-colliding ownerless distant floes. Use a real player/ship approach to check
native ownership and dynamic interaction; flying the camera alone tests visuals.

## Diagnostics and checks

Hover adds `Body motion`, `hasNativeOwner`, `kinematic`, `collisions` and
`pose/replica updates`. Retained samples identify `Kinematic pose` versus
`Dynamic forces`. Kinematic pose samples have zero submitted force/torque; older
retained dynamic samples remain identifiable by type and age. The six Apply
switches control the dynamic force model, not kinematic tracking.

Expected ownerless lease-holder observations: `Owner=0`, `LeaseOwner`,
`OwnerlessKinematic`, `kinematic=True`, `collisions=False`, growing pose and
publication counters, and no new force calls. On native ownership, body mode
returns to NativeDynamic with original collision settings and normal force work
on the owner. Remote native replicas remain dynamic without a second water driver.

Check one nearby native-owned floe, ownerless floes through freefly, native
ownership acquisition by moving the player or ship, ownerless handoff across two
clients, pause/resume and the FarVisual boundary. Keep the working wave and force
settings unchanged. Compare FPS with profiling off, including a wind-change
command. No specific speedup, network acceptance or collision behavior has yet
been measured for this change.

Validation is static source/API inspection, exact base blob checks where
reconstructed, C# lexical checks, project XML/Compile diff and changed-file
Cyrillic scanning. No mod compilation, mod tests, PhysX simulation or game run.
