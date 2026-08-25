# Blood Moon Codex review hardening checkpoint — 2026-08-25

This is the recovery checkpoint after resolving the five supported-runtime findings from the Codex review of `75e6d27cb47171ebd3fe409313e06ae4a0142dab`. It supplements `13_IMPLEMENTATION_REPORT.md`, `14_CODE_REVIEW_STATUS.md`, `15_RELEASE_READINESS.md`, `16_CLIENT_TRUST_BOUNDARY.md` and `17_HARDENING_CHECKPOINT_2026-08-25.md`.

## Repository state

```text
branch: feat/blood-moon
PR: #42
base: master
state: draft / open / not merged
previous Codex-reviewed head: 75e6d27cb47171ebd3fe409313e06ae4a0142dab
runtime-code head immediately before this checkpoint update: 8694cc6f4ad37d64820f194c31bc25b1150d68fc
```

The exact documentation-inclusive review head must be taken from PR #42 after this file is committed and used verbatim in the next Codex review request.

No plugin version, public README, Thunderstore changelog, packaging or release metadata was changed.

No assistant-side Valheim build or runtime test was performed. Game API verification continues to use:

```text
shudnal/assemblies_combined@cf2cda3a4c5c05e62cb8052a61753e5dcaecc28e
```

## Review scope and trust boundary

`16_CLIENT_TRUST_BOUNDARY.md` remains authoritative.

Blood Moon must be correct for compatible unmodified clients under normal gameplay, latency/reordering, ZDO ownership migration, reconnect/restart/recovery, stale state and ordinary mod interoperability. Intentionally modified game/mod clients, fabricated Blood Moon RPC/marker data, packet injection and deliberate client-side state tampering are outside scope.

All five findings in this review pass have supported normal-runtime reproduction paths and were therefore treated as release-blocking correctness issues.

## Resolved Codex findings

### 1. Stale clients could bypass the OfferingBowl cutoff

Boss-producing `OfferingBowl.InitiateSpawnBoss` no longer decides whether to use Blood Moon authority from `BloodMoonNetwork.ClientGlobal`.

Every valid boss-producing bowl initiation is routed through `BloodMoonOfferingAuthority`, including a late joiner or reconnect whose global snapshot is still Dormant or belongs to an older event. The request carries bowl/action data, not a client-asserted Blood Moon event identity. The server reads the current `BloodMoonController.State` and decides whether the offering is allowed.

While the authoritative state is still `Forewarning`, the server also compares its current schedule time with the frozen `MarkedAt` boundary. This closes the small interval after 18:00 but before the next discrete `Forewarning -> Marked` state-machine tick. The frozen-time check applies only while the event is `Forewarning`, so a `Skipped` or `Resolved` event does not continue sealing offerings because of an old schedule window.

### 2. Accepted pre-cutoff offerings could be lost during bowl ownership migration

Server acceptance now creates an independent pending offering request identified by a request id and world identity.

The server repeatedly resolves the bowl's current ZDO owner. The owner relay explicitly acknowledges either completion or loss of ownership. If ownership moved before delivery, the server resolves the new owner and retries the same already-authorized request without re-checking the Blood Moon phase. An offering accepted before the cutoff therefore remains valid even if `Marked` begins while ownership is migrating.

Stale negative acknowledgements from an owner already superseded by another relay are ignored. Cross-world stale pending entries are discarded.

There is no arbitrary wall-clock expiry for an accepted request. It remains pending until a successful completion acknowledgement, a world change, or the bowl/ZDO becoming invalid. This avoids converting an unusually long but otherwise normal ownership migration into a silent loss of an already accepted vanilla action.

The central `ZNet.OnDestroy` Blood Moon lifecycle cleanup now explicitly clears pending offering requests. The cached `ZRoutedRpc` registration reference is deliberately retained, preserving the existing no-duplicate-registration invariant if runtime state resets while the same transport object remains alive.

Vanilla behavior used for this design was verified from `assemblies_combined`: `OfferingBowl.InitiateSpawnBoss` routes `RPC_SpawnBoss` to the bowl ZDO owner; `RPC_SpawnBoss` performs the owner/queue/`CanSpawnBoss` checks, schedules the delayed boss spawn, and only then sends the normal requester-side item-removal/feedback RPCs.

### 3. Listen-host zone owners rejected their own spawn leases

`BloodMoonNetwork.OnSpawnLease` no longer rejects a lease merely because the receiving process is also the server. The routed sender must still be the server peer and the lease is still deserialized through the same `BloodMoonSpawner.ReceiveLease` path used by remote clients.

Vanilla identity semantics were verified directly: `ZNet.Awake` assigns the routed RPC UID from `ZDOMan.GetSessionID()`. On a listen host, the local routed server identity and the local ZDO ownership session are therefore the same identity expected by zone claims and spawn leases.

This restores configured Blood Moon extra spawns for host-owned zones in single-player/listen-server games without adding a parallel special-case spawn path.

### 4. Damage/death confirmation was rejected after normal ownership migration

Receive-time `ZDO.GetOwner() == sender` is no longer treated as a valid invariant for a routed observation that may have been made before ownership migration.

A target owner can observe positive HP loss or death, send the normal Blood Moon report, and cease to be the current owner before the server receives it. The report remains eligible after that migration.

Stable checks remain:

- authoritative event identity and combat-live state;
- participant membership/activity;
- target identity and eligible Blood-enemy semantics;
- observed dead state for death credit;
- source identity/type;
- direct participant authorization remains bound to that participant's routed peer and player ZDO identity;
- summons remain bound to their durable event/owner marker;
- death credit still requires the reported participant to match the server-correlated two-sided actual-damage credit.

This correlation is retained for normal cross-owner timing/order correctness. It is not an anti-cheat guarantee.

### 5. A displaced force-environment lease restored the wrong environment

`BloodMoonEnvironment.AcquireForcedEnvironment` now compares the actual current `EnvMan.m_forceEnv` with `Seasons_BloodMoon`.

If another ordinary mod displaced Blood Moon while the event was active, a later reacquire captures that current external forced environment as the new restore target before setting the Blood Moon environment again. `ReleaseForcedEnvironment` therefore restores the latest displaced environment instead of the obsolete value captured before the event.

## Static verification after the fixes

The diff from the previous reviewed head `75e6d27cb47171ebd3fe409313e06ae4a0142dab` through runtime head `8694cc6f4ad37d64820f194c31bc25b1150d68fc` changes only these runtime files:

```text
BloodMoon/BloodMoonDamageCreditAuthority.cs
BloodMoon/BloodMoonEnemyDeathReports.cs
BloodMoon/BloodMoonEnvironment.cs
BloodMoon/BloodMoonNetwork.cs
BloodMoon/BloodMoonOfferingAuthority.cs
BloodMoon/BloodMoonWorldLifecycle.cs
```

All five review threads from that Codex pass were answered and resolved before this checkpoint.

The OfferingBowl relay was additionally audited for normal ordering and lifecycle edges after the review fix: stale negative ACKs are ignored, skipped/resolved events do not inherit the frozen cutoff, accepted requests have no arbitrary migration timeout, and world teardown explicitly removes any remaining accepted request state.

Vanilla `OfferingBowl.UseItem` was also rechecked before considering removal of the client-side interaction guard. It can perform `SetGlobalKey` after `InitiateSpawnBoss` while requester-side offering item removal still happens only after owner-side `RPC_SpawnBoss` acceptance. The existing client-side Marked/Active guard is therefore retained rather than replacing the whole vanilla interaction path with an asynchronous duplicated implementation solely to eliminate a short stale-snapshot false-deny window.

## Priority owner-side runtime acceptance

The complete acceptance matrix remains `08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md`. The highest-priority scenarios added or reinforced by this review pass are:

- single-player/listen-host extra spawning in host-owned zones;
- dedicated-server extra spawning remains unchanged;
- late join/reconnect at or after 18:00 while the client snapshot is stale;
- offering received just before vs just after the frozen 18:00 cutoff;
- accepted pre-cutoff offering followed by `Marked` before owner relay completes;
- bowl ownership migration, including a temporary ownerless/disconnected interval, before completion;
- stale negative relay acknowledgement after a newer owner has been selected;
- world teardown/switch while an accepted offering is pending;
- skipped Blood Moon event followed by normal boss offering during the old schedule window;
- positive damage and lethal death while target ownership migrates before server receipt;
- direct, projectile, AOE and summon damage correlation under ordinary multiplayer ownership changes;
- another mod replacing the forced environment during Active, followed by Blood Moon reacquire and world-system restoration;
- reconnect/restart and the previously documented full outcome/recovery/persistence matrix.

PR #42 must remain draft and must not be merged without explicit owner approval.
