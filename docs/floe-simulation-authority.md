# Cooperative floe simulation authority

Base: `0d9240fdeaeca5ca4a5a7176664ca81bb478a59f`, `perf/snow-performance`, PR #45.
Game reference: `shudnal/assemblies_combined` at `d1374bfd9175ac8f733ae483b0a06e5c8b75906e`.

This follow-up supersedes the native-owner-only eligibility statements in
`floe-controller-diagnostics.md`. Its physics model, shared coefficients,
collider waterline and selected wave spectrum are unchanged.

## Agreement

Use cooperative client-side candidacy and an expiring simulation lease in the
floe's existing ZDO. Do not introduce a server arbiter, claim native ownership,
or make ownership-dependent methods globally recognize the simulator as owner.
The server remains the ordinary ZDO transport relay.

The game owner's priority is unconditional. A nonzero native owner is not treated
as absent merely because it appears far away, disconnected, slow, or outside our
preferred radius. That decision remains with the game's native ownership code.
Only an actually ownerless, registered seasonal floe can elect a simulator.

The client-only distant visual bobbing remains separate. It must not publish its
pose, acquire/renew a lease or run the dynamic water forces. Stronger isolation
from remote ZDO movement beyond the visible-wave boundary and distance-dependent
wave attenuation are deferred, not implemented by this change.

## States and clocks

The authority state machine runs through the existing sync/physics callbacks.
There is no additional per-floe Update or FixedUpdate and no global world scan.

| Observed state | Behavior |
| --- | --- |
| Native owner is this peer | Ordinary dynamic physics and native synchronization |
| Another native owner exists | Native replication; no fallback claim or water forces |
| No native owner, local unchanged candidate | Wait one full observed server second before promotion |
| Another non-expired candidate exists | Wait; do not overwrite it every frame |
| Locally completed lease is still current | Run the existing forces/torque and publish bounded-rate snapshots |
| Another live lease exists | Follow its native pose fields and fresh body velocities |
| No live candidate/lease | Announce this peer as a candidate after a small stagger |
| Outside the visible-wave radius | Release local claim and retain local visual bobbing |
| Unload, disable, local pause, invalid clock/body | Stop fallback simulation; release a writable matching claim |

All decisions use `ZNet.GetTimeSeconds()`, the unwrapped synchronized server
world clock. No local machine wall clock, realtime stopwatch, Time.time timeout
or wrapped wave phase is used for the election. The local one-second stability
observation is also measured with that clock, not by counting frames.

ZDO fields (hashed once):

| Key | Type | Meaning |
| --- | --- | --- |
| SeasonsFloeCandidate | long | Candidate's network session ID |
| SeasonsFloeCandidateToken | long | Candidate round token: server world milliseconds |
| SeasonsFloeSimulator | long | Elected simulator's network session ID |
| SeasonsFloeSimulatorToken | long | Candidate token adopted by that lease |
| SeasonsFloeHeartbeat | long | Last publication's whole server world second |
| SeasonsFloeNativeRevision | int | Native OwnerRevision at promotion |

Candidate lifetime is three server seconds. A local candidate must remain the
same peer/token for one full locally observed server second; loading someone
else's older record never skips that wait. Candidate tokens prevent an old local
component from releasing a newer round by the same peer. The lease additionally
becomes invalid if native OwnerRevision changes, even when ownership returns to
zero between observations.

Heartbeat is rewritten only when its whole-second value changes, including at
rest. The lease is expired when the current server time is more than three
seconds beyond that value. Integer rounding means the effective silence interval
is approximate, not a three-second wall-clock guarantee. A one-second future
clock tolerance accommodates whole-second stamps and small peer corrections.
A backwards world-clock correction restarts the local round; large future records
are invalid. A forward time command can expire a lease. These are intentional
consequences of using server world time as requested.

After disconnect/reload, saved old-session records expire naturally. A record
naming this peer does not start simulation unless this component actually
completed the candidate round. No previous local lease is silently revived.

## Soft election, not distributed locking

The native `RPC_ZDOData` compares whole-ZDO revisions and ignores equal or older
DataRevision for data. It does not supply compare-and-set or a deterministic
candidate election. A delivery need not occur before the next client frame.

The implementation adds a small deterministic per-peer/per-floe announcement
stagger and one staggered revision-only re-announcement inside the candidate
window. The candidate value and its round token remain unchanged during that
re-announcement. Snapshot publication is also slightly dephased between peers.
This reduces equal-revision lockstep, without adding a server arbitration RPC.

On observing a different live lease, the client stops its own simulator and pose
writes. Before each fallback simulation and publication it checks native
ownership, the currently visible simulator/token, expiry and range again.

This remains an explicitly best-effort protocol. Under delayed/conflicting ZDO
snapshots, a partition, or simultaneous equal revisions, two peers may temporarily
believe they won. A one-second waiting period is not proof of a unique global
winner. Native whole-ZDO replication can also carry a stale native owner/pose
across a handoff; this change does not patch generic ZDO receive arbitration or
promise atomic publication. No claim of convergence under arbitrary delay or of
security against malicious clients is made. Multi-client runtime observation is
required before treating the behavior as accepted.

## Eligibility and distant rendering

A fallback candidate needs an existing active seasonal component, a valid dynamic
Rigidbody, a camera, and a position inside both the existing visible-wave distance
and camera far-clip distance. The distance check does not use frustum visibility:
turning the camera away must not continuously transfer authority.

The existing visible-wave distance remains
`NearSimulationDistance * ZoneSystem.m_zoneSize`, matching the reviewed
Water.ApplySettings implementation. Do not confuse this with HasOwner or with
being the closest player. This change does not enlarge the game's loaded area or
steal a still-assigned native owner to manufacture more candidates.

Distant local Dampen, camera clipping and the existing ownership transitions
remain in place. They never become leased dynamic simulation outside that radius.
Remote movement may still influence their existing ZDO baseline/rotation;
complete visual isolation was explicitly deferred.

## Physics and synchronization

The candidate itself does not apply water forces or publish motion. On promotion,
the component adopts the last replicated pose and velocities once, restores the
normal gravity setting, and starts the existing force-based simulation. There
are no additional pose/velocity assignments in the ongoing local force loop.

For the local lease simulator, the existing ZSyncTransform.ClientSync prefix skips
native pose adoption, gravity disable and ownerless velocity zeroing. Scale
synchronization is retained. This exception applies only to the actual registered
seasonal floe with a currently valid local lease. Native IsOwner/HasOwner and all
other objects' synchronization semantics are unchanged.

OwnerSync still runs normally. Its postfix publishes a fallback snapshot only
for a successful recent physics step and a current local lease. Snapshot interval
is 0.10-0.12 server seconds (at most 10 publications/s before native transport
batching/backpressure). Native position, rotation, vel, bodyVel and bodyAVel fields
are used. Ownerless position-only changes explicitly increase DataRevision;
ZDO.SetPosition otherwise increments it only for a native owner.

A replica retains native position/rotation/scale synchronization. After native
ClientSync, a narrow postfix restores the live fallback publisher's body
velocities, which HasOwner=false would otherwise zero. Velocity continuation is
limited to 0.5 server seconds since the latest observed ZDO revision. It does not
run a second water-force driver and it stops on lease expiry/native takeover.

A local native takeover from the same local fallback simulator preserves current
pose/momentum rather than snapping back to its own older published snapshot.
Other transitions return to ordinary native sync. Diagnostic step pairing is
invalidated at transitions and records the actual simulator ID and round token.

Release writes are conditional on the current peer and token and require a live
registered ZDO. They do not clear another round or write to a new remote native
owner. OnDisable/OnDestroy use the existing Untrack/ReleaseWaves lifecycle.
OnDestroy cannot be relied upon after a crash or after ZDO teardown; expiry is
the fallback. No destroyed ZDO is recreated to publish a release.

## Inspector and hover

`IceFloeClimb.EnableFallbackSimulation` is a shared runtime switch, initially true.
It is separate from ResetSurfacePhysicsSettings and does not alter native owner
physics. Disabling it releases local fallback claims on the next callback.
The instance action `ReleaseFallbackSimulation()` relinquishes a matching local
candidate/lease and delays that client's next proposal for about three seconds,
which allows a controlled handoff check. Neither control clears native ownership.

The cheap authority block is shown even when no physics sample can be captured:
Authority, simulatorLocal, this peer, candidate/token/age/stable time,
simulator/token/heartbeat/age, server seconds, publication/acquisition/release
counters and the last state reason. Hover reads never acquire or renew a lease.
Existing expensive force/surface diagnostics stay opt-in per selected floe.

For a lease-owned floe, native `local=False` is expected. Look for
`Authority=LeaseOwner`, `simulatorLocal=True`, a simulator ID matching `peer`, a
renewing heartbeat and advancing publication count. On another client the same
ZDO should show `Authority=LeaseReplica` and the same simulator/token.
Old force samples can remain visible after a transition; their sampled simulator,
token, fixed time and age identify their provenance.

## Maintainer-run checks

Use the same build on participating clients and keep the established shared
physics coefficients unchanged. Detailed capture on one or two selected floes is
enough; no need to pin diagnostics on the whole visible group.

1. Native owner baseline: movement/collisions remain unchanged and no lease is
   used while HasOwner is true.
2. Ownerless floe within the visible-wave radius: observe Candidate for at least
   one server second, then LeaseOwner. Check gravity, force calls, heartbeat and
   pose publication without any native owner assignment.
3. Two-client observation of the same floe: compare native owner, candidate,
   simulator, token and server-time ages on both clients. One runs forces, the
   other follows; do not infer this from a single client's view alone.
4. Call ReleaseFallbackSimulation on the simulator or leave its wave radius;
   another eligible client should announce, wait, and take over. Repeat with an
   abrupt simulator disconnect to exercise heartbeat expiry instead of cleanup.
5. Approach until the game assigns a native owner; the lease stops immediately
   on observation and native simulation wins. Check both same-peer and
   different-peer takeovers, then move away again.
6. Verify outside-wave-range floes still use local visual bobbing and do not
   increase fallback pose-publication counts. Turning the camera away while
   staying in range is not a release condition.
7. Check pause/resume, explicit world-time changes, unload/reload, and forced
   delay/concurrent arrival during candidacy. Report duplicate LeaseOwner views
   rather than assuming the one-second wait makes them impossible.

Provide full hovertext from both clients for checks 3-5, plus a brief movement
observation and which client moved/disconnected. Record before/after snapshots a
few seconds apart. No performance acceptance is claimed for 40-50 leased bodies:
force work and network publication are additional to the old client-only visuals.

## Validation boundary

No mod build, automated mod tests, physics execution or Valheim run was performed.
Validation is source/API review, exact base-blob hashes, diff review, C# lexical
structure checks, XML/reference checks and a Cyrillic scan of delivered files. Dependencies,
version, packaging, wave math, force coefficients and snow processing are unchanged.
One new C# Compile item is required. The six lease metadata fields are new network
and potentially saved ZDO data; they have no effect without the mod and expire
rather than being treated as persistent authority after reload.
