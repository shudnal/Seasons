# Blood Moon code review status

This file records the Codex/manual review trail for draft PR #42 and the current exact continuation rule.

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

## Earlier Codex chronology

The branch received repeated full-diff Codex passes. Confirmed findings were fixed before later passes, and the associated inline threads were resolved.

### Pass 1 — `f11ff4d4ff`

Confirmed fixes included:

- private skill baselines in resync;
- active Blood Craft preservation during restart recovery;
- pre-combat disable avoiding morning time advance;
- random-event suppression cleanup on world teardown;
- boss-withdrawal interior-context filtering.

### Pass 2 — `ddbb65649e`

Confirmed fixes included:

- environmental/null-attacker damage remaining valid for participants;
- local-player-ready resync retry;
- explicit direct-attack target enumeration;
- mixed surface/interior spawn routing;
- server-observed enemy-death validation;
- world-scoped chronicle keys.

This pass also confirmed that skill interception must target the real `Player.RaiseSkill` override.

### Pass 3 — `e870e5805b`

Hardening covered:

- outcomes surviving later annual event replacement;
- monotonic explicit resync;
- immutable projectile/AOE source context;
- real-time-calendar net-time separation.

### Pass 4 — `f7ed0e6bdc`

Confirmed fixes included:

- client/server lease clock-domain separation;
- newest-valid event persistence selection;
- stronger Blood-enemy attribution validation;
- combat routing through early Resolving until Blood behavior disables;
- disabled Blood Craft recipe exclusion.

### Pass 5 — `688b901477`

Confirmed fixes included:

- exact prefab/`MonsterAI` validation for extra-enemy reports;
- boss discovery navigation context;
- resolution spawn-race prevention;
- persistence-aware durable outcome acknowledgement;
- late Defeated report rejection after resolution starts.

Manual follow-up also ensured that a valid extra whose report was completely lost remains cleanable at resolution without trusting arbitrary event markers.

### Pass 6 — `51c6e99098`

Confirmed fixes included:

- dormant `EventId == -1` cleanup safety;
- frozen per-event extra-enemy prefab identity so config changes cannot strand legitimate extras.

### Pass 7 — `0b423b0350`

Confirmed fix:

- durable server-side spawn-pool history preserves verifiable cleanup for extras from prior events after the current event state has been replaced.

### Earlier clean code review — `0be8eb89d600a95720f08387041fb7ab15ccd5f7`

Codex result:

```text
Didn't find any major issues.
```

Later documentation-inclusive review correctly found that the claimed client lease clock was not actually monotonic.

### Documentation review finding — fixed at `9f8e24d6fd8adb656aa37d067cd5b4a466224105`

The lease remaining lifetime is now bounded once on receipt and tracked against Unity `Time.realtimeSinceStartup`. OS wall-clock corrections can no longer prematurely expire or extend a received lease.

## Post-review manual hardening

The exact pre-documentation hardening head for the latest manual pass was:

```text
3e653828134b7cbeaf888e9044e2b7ddca9bd98b
```

This pass intentionally reopened static scrutiny instead of treating the earlier clean review as permanent. It found and fixed additional issues not covered by the previous review head.

### Projectile collision lifecycle

An active Blood-source projectile hitting world geometry previously risked skipping vanilla hit lifecycle, and stale event-attributed projectiles could phase through geometry after event end. The current implementation preserves vanilla collision/effect/attach/destroy behavior while suppressing forbidden damage, skill/adrenaline credit and stale persistent spawn effects.

### World damage coverage

The world-damage guard was expanded to the current vanilla `IDestructible` implementations with `Damage(HitData)`, including `HitArea` and `Raven` in addition to build pieces, generic destructibles, rocks and trees.

### Bloodlust implementation hardening

Bloodlust now has explicit server-controlled full-factor endpoints for outgoing damage, incoming damage, movement and actual-damage lifesteal.

Death credit and lifesteal are committed only after positive owner-observed HP loss. Lifesteal target reports additionally require server-side raw Blood-enemy ZDO validation and a rolling max-health-per-second cap.

### Blood Craft source-recipe semantics and preflight

Temporary item markers retain exact source recipe identity. Source station type and normal quality-level progression are preserved while materials remain free.

`Recipe.GetAmount` preflight is now guarded for Blood Craft, closing the `m_requireOnlyOneIngredient` empty-resource dereference path that occurs before `DoCrafting`. Temporary-upgrade material rows are hidden while source station/quality rules remain enforced.

### Immediate DreamText outcome path

Outcome DreamText no longer intercepts or changes the next normal sleep. Resolution presents a dedicated native-`SleepText` UI immediately, with independent local input protection.

A separate presentation handshake now waits for connected clients to start/complete the presentation before `ReleasingClients`, subject to bounded real-time fail-safe timeouts. Durable profile acknowledgement remains a separate persistence concern.

### Durable local Defeated state

The short recovery timer is no longer the only local evidence of Defeated. A world/event-bound player custom-data marker restores terminal local state after process loss and retries the Defeated notification while the server still reports the participant as combat-active.

### Actual-damage death credit

A zero-damage, fully blocked or fully resisted accepted hit can no longer overwrite the previous real damaging source used for enemy death credit.

### Resolution consolidation

Production controller code now directly owns:

- pre-combat feature-disable cancellation;
- late Defeated rejection;
- real-time fade timeout;
- real-time-calendar morning guard;
- ordinary-world net-time broadcast;
- outcome-presentation release gating.

Corrective self-patches for those behaviors were deleted after integration. This also removed a stale Harmony patch that still targeted the no-longer-existing `ReplayResolvedOutcomes` method.

### Compile-oriented source audit

A manual C# source audit found relational comparisons being used directly on `BloodMoonResolutionStep` enum values. These were replaced with explicit `(int)` comparisons.

New Harmony signatures for `Recipe.GetAmount`, `InventoryGui.SetupRequirementList` and related crafting paths were re-verified against the exact game-source commit.

This is static source review only. It is not a build result.

## Current review state

At the time of this documentation freeze:

- all previously listed inline review threads were resolved;
- PR #42 remained draft/open/unmerged;
- the latest manual hardening changed runtime code after the earlier clean Codex head;
- therefore a new exact-head Codex review is required after `13_IMPLEMENTATION_REPORT.md`, this file and `15_RELEASE_READINESS.md` are frozen.

The resulting documentation-inclusive review SHA and Codex result must be recorded in the PR timeline without another documentation-only commit. If the new review reports a confirmed issue, fix it and request another exact-head review; the new reviewed SHA then replaces the previous checkpoint.

## Runtime gate

Even a clean final Codex pass does not substitute for owner-side Valheim testing. Priority runtime acceptance remains documented in `13_IMPLEMENTATION_REPORT.md`, `15_RELEASE_READINESS.md` and the authoritative `08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md`.

## Final invariants

Before owner-side playtest handoff, all of the following must remain true:

- final documentation-inclusive Codex pass has no unresolved confirmed finding;
- all inline review threads are resolved;
- PR #42 is draft/open/unmerged;
- plugin version, public README, Thunderstore changelog and packaging/release metadata are unchanged;
- no assistant-side Valheim build/runtime result is claimed;
- any remaining runtime-only uncertainty is stated explicitly rather than presented as proven behavior.
