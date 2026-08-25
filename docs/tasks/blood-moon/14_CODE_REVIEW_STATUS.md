# Blood Moon code review status

This file records the Codex/manual review trail for draft PR #42 and the exact continuation rule after the latest Blood Moon hardening.

## Pull request

```text
PR: #42 Blood Moon first implementation slice
base: master
head: feat/blood-moon
state: draft / open / not merged
URL: https://github.com/shudnal/Seasons/pull/42
```

The PR must remain draft and unmerged until explicit owner approval.

## Authoritative game source

All game/API verification for this review sequence uses:

```text
repository: https://github.com/shudnal/assemblies_combined
commit: cf2cda3a4c5c05e62cb8052a61753e5dcaecc28e
```

No assistant-side Valheim build or runtime test was performed.

## Previous Codex chronology

Earlier full-diff Codex passes found and verified fixes for:

- private skill baselines and local-player-ready resync;
- active Blood Craft preservation during restart recovery;
- pre-combat disable without morning advance;
- random-event suppression teardown;
- boss and AI interior-context filtering;
- environmental damage remaining valid for participants;
- direct-attack Harmony target enumeration;
- mixed surface/interior spawning;
- server-observed enemy-death validation;
- world-scoped chronicle identity;
- independent durable offline outcomes;
- monotonic same-event resync;
- real-time-calendar net-time separation;
- lease clock-domain separation;
- newest-valid persistence recovery;
- hostile attribution validation;
- interaction routing through early Resolving;
- disabled Blood Craft recipe exclusion;
- exact extra-enemy prefab validation;
- resolution spawn-race prevention;
- persistence-aware outcome acknowledgement;
- late Defeated report rejection;
- dormant `EventId == -1` cleanup safety;
- frozen per-event extra-enemy identity;
- durable stale-prior-event spawn-pool history.

An earlier exact code head received a clean Codex result:

```text
0be8eb89d600a95720f08387041fb7ab15ccd5f7
Didn't find any major issues.
```

A later documentation-inclusive review correctly found that received lease expiry was still tied to an adjustable wall-clock domain. That was fixed at:

```text
9f8e24d6fd8adb656aa37d067cd5b4a466224105
```

Received lease lifetime is now bounded once and tracked with Unity `Time.realtimeSinceStartup`.

## Manual hardening after the earlier clean review

The branch was intentionally reopened for static scrutiny. The latest runtime-code hardening before this documentation update is:

```text
28e846aa62b3a2754fee80ddae189f6f348367fa
```

The main findings and fixes are below.

### Projectile collision and stale attribution

Blood-source projectiles now preserve vanilla hit/effect/attach/destroy lifecycle when colliding with world geometry while forbidden world damage and unrelated side effects remain suppressed.

Stale event-attributed projectiles no longer pass through geometry after event end. They retain vanilla collision/destruction but suppress damage, skill/adrenaline credit, spawn-on-hit and spawn-on-TTL effects. Stale AOE is rejected.

### World-damage coverage

The world-damage guard was expanded to the current vanilla `IDestructible` implementations exposing `Damage(HitData)`, including `HitArea` and `Raven` in addition to build pieces, destructibles, rocks and trees.

### Actual-damage credit and Bloodlust

Enemy death credit is now committed only after positive owner-observed HP loss. Fully blocked, resisted or zero-damage hits cannot overwrite the previous real damaging source.

Bloodlust has server-controlled full-factor endpoints for outgoing damage, incoming damage, movement and actual-damage lifesteal. Lifesteal reports require:

- current event and active participant;
- target-owner authority;
- server-side raw Blood-enemy ZDO eligibility;
- monotonic report sequence;
- rolling max-health-per-second healing cap.

### Blood Craft source semantics and preflight

Temporary item identity preserves the exact source recipe. Source crafting/repair station type and normal quality-to-station-level progression remain enforced while materials are free.

`Recipe.GetAmount` is guarded for Blood Craft so `m_requireOnlyOneIngredient` cannot dereference an absent ingredient before `DoCrafting`. Temporary-upgrade material rows are hidden while station/quality feedback remains authoritative.

The vanilla world-sink reflection matrix was rechecked against the exact game-source commit. Current signatures match for:

- `ItemStand`;
- `ArmorStand`;
- `Fermenter`;
- `CookingStation`;
- `Smelter`;
- `Turret`;
- `Catapult`.

### Durable local Defeated state

The short recovery timer is no longer the only local evidence of Defeated. A world/event-bound player custom-data marker restores terminal local state after process loss and retries the Defeated notification while the server still reports the participant as combat-active.

### Immediate DreamText and presentation ordering

Outcome DreamText is presented immediately through a dedicated native `SleepText`-derived UI rather than intercepting the next ordinary sleep.

Connected clients report presentation start/completion. `PublishingOutcomes` keeps the minimum presentation hold and delays `ReleasingClients` until completion or bounded real-time fail-safe timeout. This transport acknowledgement remains separate from durable profile-persistence acknowledgement.

### Resolution consolidation

The production controller now owns the previously corrective behavior directly:

- pre-combat cancellation;
- late Defeated rejection;
- real-time fade timeout;
- real-time-calendar morning guard;
- ordinary-world net-time broadcast;
- outcome-presentation release gating.

The redundant self-patches were removed. This also removed a stale Harmony patch targeting the no-longer-existing `ReplayResolvedOutcomes` method.

### Compile-oriented source audit

Direct relational operators on `BloodMoonResolutionStep` enum values were removed in favor of explicit numeric comparisons. New `Recipe.GetAmount` and `InventoryGui` Harmony signatures were verified against the authoritative game source.

This is static source review only, not a build result.

### Routed-RPC registration lifetime

A final post-documentation audit found an additional teardown hazard in the RPC registration guards.

Current Valheim `ZRoutedRpc.Register` inserts handlers with `Dictionary.Add`. `ZNet.OnDestroy` clears `ZNet.m_instance` but does not synchronously clear the existing `ZRoutedRpc.s_instance` or its method table. Resetting a subsystem's cached `registeredRpc` reference during world teardown could therefore cause the next controller tick to register the same RPC name again on the still-live routed-RPC instance and throw a duplicate-key exception.

The fix was applied to both:

- `BloodMoonOutcomePresentationHandshake`;
- `BloodMoonOutcomeQueue`.

Their runtime reset now clears event/store state but preserves the cached transport reference. A genuinely new network session still registers normally because the new `ZRoutedRpc` object has a different reference.

A fresh full-diff search after the fix found no remaining `registeredRpc = null` reset in the Blood Moon changes.

## Review request rule

A Codex request was previously submitted for documentation head `a2728234234308c03045ac7f0cc298382094d10c`, but subsequent transport-lifetime fixes changed runtime code. That request is superseded and must not be treated as the final review gate.

After this document and `15_RELEASE_READINESS.md` are frozen, request Codex review for the new exact head. If Codex reports a confirmed issue:

1. fix it on `feat/blood-moon`;
2. resolve the corresponding review thread;
3. update repository context if the finding changes implementation knowledge;
4. request another exact-head review.

The final clean reviewed SHA/result should be recorded in the PR timeline without another documentation-only commit so that the reviewed head remains exact.

## Runtime gate

A clean Codex review does not replace owner-side Valheim testing. Runtime acceptance remains defined by `08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md`, `13_IMPLEMENTATION_REPORT.md` and `15_RELEASE_READINESS.md`.

## Final invariants

Before owner-side playtest handoff:

- the final documentation-inclusive Codex review has no unresolved confirmed finding;
- all inline review threads are resolved;
- PR #42 remains draft/open/unmerged;
- plugin version, public README, Thunderstore changelog and packaging/release metadata remain unchanged;
- no assistant-side Valheim build/runtime result is claimed;
- runtime-only uncertainty is explicitly documented rather than presented as proven behavior.
