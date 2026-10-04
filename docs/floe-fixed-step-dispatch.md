# Floe phase dispatch

Repository: `shudnal/Seasons`, branch `perf/snow-heat-diagnostics` (PR #50).
Base for this revision: `109dd96d217fb99f7584095d4fc5061ba8746d0f`.
Game source inspected: `shudnal/assemblies_combined` at
`5a2365409cff644d6adaccd2b308178cc4179b19` (Valheim 1.0.16).

This supersedes the preceding `PrepareFixedStep` optimization: passive floes
already skipped `SimulatePhysics`, but every fixed step still traversed all
participants and checked the passive preparation cache.

## Scope

Separate fixed-step participants from the world registration list. Remove the
repeated passive visits on catch-up physics steps, not the physics steps needed
by native dynamic bodies or network replicas. Reorder the normal-height early
exit in `RecoverInvalidHeight` ahead of the authority/range lookup.

Wave spectra, direct samples, interpolation, forecast workers, buoyancy/torque,
kinematic motion, lease checks, publication intervals, and world physics timing
are unchanged. Snow caps, sliding, creature level materials, profiler code, and
the release version are outside this change.

## Dispatch collections and phases

`SeasonalIceFloeDispatch.cs` owns a registration record, fixed-participant list,
reusable phase snapshots, and a deduplicated pending-change queue. Admission
allocates one record. Normal iteration reuses list/queue storage; formatting the
status is diagnostic-only. Membership changes use swap removal, not a scan.

The first fixed callback of a rendered frame prepares all registered floes once.
Subsequent callbacks in that frame prepare only changed entries and the fixed
participants. Pause/fallback-state changes or a changed water distance can require
an additional preparation; the unchanged-frame fast path does not scan passives.

`Fixed` means neither Distant nor OwnerlessKinematic. This is deliberately NOT a
list of local owners: a native remote replica still needs ClientSync. Each fixed
participant retains pre-sync preparation, native ClientSync, the post-callback
liveness check, and SimulatePhysics with its existing second state/authority check.
A participant prepared by this step's frame sweep or event drain does not repeat
its pre-sync preparation before ClientSync.

LateStep continues to visit the full registration snapshot once per rendered
frame. Its normal BeforeSync path (including the existing internal frame cache),
terrain check, bob, kinematic motion, OwnerSync, acquisition safety hold, fallback
publication, and batching remain in the same order. Thus there may still be a
cached BeforeSync entry in LateStep after the first fixed callback; the eliminated
cost is the full passive traversal on EVERY subsequent physics step. Frames with
no fixed callback still prepare and animate all registrations through LateStep.

Passive status/last-force fields remain meaningful, but LastRunFixedTime now
records an actual SimulatePhysics call, not a synthetic passive fixed-step visit.

## Notifications and handoff safety

Two small postfix notifications observe ZDO.SetOwnerInternal and ZDO.Deserialize.
They perform a registered-ID lookup and reference check, increment a local change
version, and enqueue that registration. They do not look up components, restore
bodies, change owners, interpret partial payloads, or write ZDOs. In particular,
SetOwnerInternal may execute before OwnerRevision and the rest of an incoming
payload have been assigned. Preparation is deferred to a driver boundary.

The climb interaction requests preparation after the existing RestoreWaves call.
Registration queues initial preparation; OnDisable/OnDestroy keep the existing
Untrack path, which removes fixed membership before body/lifecycle cleanup.

The queue is drained before fixed work and between fixed participants. A newly
promoted registration can join the current fixed pass, but a per-pass completion
stamp prevents a second physics visit after demotion/re-promotion. Each drain is
bounded to its initial queue length; notifications raised by a callback are kept
for the next drain, without recursive preparation. A pathological callback that
continually creates further changes can leave pending work for the next driver
boundary; ordinary native owner/payload notifications are processed before the
next fixed participant selection.

Snapshots and queue elements refer to individual registration records, not just
to a component or UID. Retired records cannot act on a later registration of the
same component. Every callback boundary checks the live component, current view,
ZDO reference, and UID. World-reset epochs abort an in-flight old-world pass.
Reentrant driver calls do not corrupt the shared snapshots. Exceptions propagate;
failed preparation is queued for retry and phase guards unwind in finally blocks.

The fixed list is a scheduling decision, NEVER a cached permission to move,
apply forces, renew a lease, or publish. Existing native-owner/token/lease/time
validation remains in the motion and publication paths. No new global ZDO
position or rotation observer is installed. Integrations that bypass native
owner/deserialization callbacks still receive the regular per-frame observation;
this is not a promise to observe arbitrary direct field edits mid-step.

## Recovery fast path

RecoverInvalidHeight first checks Recovered, world availability, and the retry
clock, then selects the same position as before: the incoming ZDO pose during
ownership adoption, otherwise the current Rigidbody pose. The existing coordinate
conditions reject normal height or unusable X/Z before checking HasPhysicsAuthority.
Only an actual correction reaches that authority check. Recovery mathematics,
recovery flags, velocity changes, and publication are unchanged.

## Read-only counters

The existing lifecycle status and opt-in floe hover now include:

- `dispatch total/fixed/kinematic/distant`: registered and classified participants.
  Newly queued registrations may briefly be unclassified.
- `pending`: queued registration records, including notifications already satisfied
  by a frame/late preparation but not yet popped. It is not a count of missing steps.
- `transitions`: changes between classified modes; initial classification is excluded.
- `sweeps/visits/events`: full first-fixed-frame sweeps, their candidate visits, and
  event-driven preparations. LateStep's normal visits are not frame-sweep visits.
- `fixed steps/visits`: driver callbacks and actual ClientSync/physics-path admissions.

These counters are main-thread local and reset with the world dispatcher. They
need no object scan, timer, log stream, synchronized field, or saved-state format.
Compare counter deltas rather than treating the totals as durations.

## Manual verification

1. With a stable island/ocean scene, compare FixedStep/LateStep using only those
   top-level methods. Use the same profiler build and selection before/after.
   Full sweep visits should grow with rendered frames, not fixed-step count.
2. Inspect total/fixed/kinematic/distant counts. Passive-heavy scenes should have
   substantially fewer fixed visits than total registrations times fixed steps.
3. Acquire/release native ownership and cross the distant boundary. Check collisions,
   gravity, pose continuity, and the first required native sync/physics step. Repeat
   with two clients and several fixed callbacks per rendered frame.
4. Climb onto a floe, unload/reload the area, toggle the component, and leave/re-enter
   the world. No queued old registration may move or publish for a new instance.
5. Pause/resume, change the supported water draw distance/fallback setting, and check
   frames with no fixed callback. Kinematic/distant motion remains frame-driven.
6. Check normal-height floes no longer enter the recovery authority chain. Exercise
   an actual invalid-height recovery separately; it must still validate authority.

Validation performed for this change is static source/API/call-order review,
syntax/XML/diff checks, and source/blob/text checks only. No mod build, automated
mod tests, or Valheim runtime execution was performed.

Unity phase reference:
https://docs.unity3d.com/ScriptReference/MonoBehaviour.FixedUpdate.html
Native handoff references: assembly_valheim/ZDO.cs and ZSyncTransform.cs in the
pinned game-source repository above.
