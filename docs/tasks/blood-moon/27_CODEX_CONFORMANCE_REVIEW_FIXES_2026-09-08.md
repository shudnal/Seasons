# Blood Moon: Codex project-conformance review corrections — 2026-09-08

## 0. Scope

This checkpoint records the first Codex review that explicitly used the repository project-conformance documentation for PR #42.

Reviewed head:

```text
231ae391891e330277868bf7733723333ea59521
```

The review referenced the authoritative project index and `25_PROJECT_CONFORMANCE_MATRIX.md` and returned six supported-runtime findings. All six were accepted as real correctness/conformance issues and corrected in `feat/blood-moon`.

This document supplements `25_PROJECT_CONFORMANCE_MATRIX.md` and `26_PR42_CONFORMANCE_REVIEW_HANDOFF.md`. It does not change gameplay design. The original mechanics remain defined by the authoritative index and its precedence rules.

## 1. Retained enemy-death evidence across ZDO deletion

Finding: a valid target-owner damage confirmation and death report could arrive before a delayed source authorization, then ordinary dead-ZDO cleanup could remove the target ZDO. Both pending-death validation and damage-credit authorization depended on the still-live ZDO, so a legitimate kill could lose progress under normal message ordering.

Corrections:

- `BloodMoonEnemyDeathPending` captures a bounded five-second evidence record only after the server has observed the actual dead, eligible Blood enemy ZDO and computed server-side points;
- the retained record does not itself grant credit;
- the independently matched source authorization + actual-damage confirmation is still mandatory;
- `BloodMoonEnemyDeathReports.TryValidate` can consume the retained evidence when the ZDO has already disappeared;
- `BloodMoonDamageCreditAuthority` accepts that bounded retained target evidence for delayed authorization/confirmation;
- participant/event/activity checks remain in force and evidence is discarded on timeout/world reset.

Relevant commits:

```text
abb7a0a4cbf8b331e343a2097270162cf5e752d6
d9ddd406c339291bcc1b933336aaae381226e9dd
50bfbec6bbd850c11414175c065bdd985d097443
```

Runtime evidence still required: cross-owner lethal hit with authorization/confirmation/death-report reordering and target ZDO destruction inside the bounded pending window.

## 2. Unsupported interior boss encounter context

Finding: when `Location.GetLocation(bossPosition)` returned null, the previous compatibility adapter treated every other interior within the 120 m XZ radius as the same encounter context.

Game-source verification uses `shudnal/assemblies_combined` first. Current `Location.GetLocation` resolves interior context by zone through `GetZoneLocation`. Therefore the accepted fallback is:

```text
both Location objects resolve -> they must be the same Location
both are null -> boss and participant must be in the same ZoneSystem zone
only one resolves -> contexts do not match
```

The surface/interior equality and distance test remain mandatory.

Relevant commit:

```text
92ce835742a0dc887b0496b75658be5be591b168
```

Runtime evidence still required: two unrelated modded interiors without `Location` objects in nearby XZ coordinates.

## 3. Monotonic persistence generation

Finding: candidate selection for `<world>.json`, `.new` and `.old` used `UpdatedAt` as the primary ordering key, but `UpdatedAt` is Valheim/game time. Backward time and a clean debug replacement can make the newest logical state have a lower `UpdatedAt`, allowing an older active snapshot to win after restart.

Corrections:

- `BloodMoonEventState` now persists `PersistenceGeneration`;
- every `BloodMoonPersistence.Save` advances a per-world high-water generation before serialization;
- load inspects all valid candidates, restores the generation high-water, and selects by generation first;
- file timestamp / game time / event / revision remain deterministic fallback ordering for legacy generation-zero snapshots;
- `.new` written before a crash can therefore outrank the previous canonical/backup state;
- `Delete` clears the process-local generation high-water for that world.

No Blood Moon schema bump is required: this subsystem is still pre-release and an absent JSON field naturally reads as generation zero.

Relevant commits:

```text
9d4be0497f464c1641275b23596a1425b1a3c475
3ab413ec79f2e2f30c5db33ae061f2a53ce24a6b
```

Runtime evidence still required: debug cleanup -> process restart, backward `skiptime` -> save -> restart, and crash boundaries around `.new`/canonical rotation.

## 4. Same-event debug restart and client lease revisions

Finding: `seasons bloodmoon cleanup` may deliberately restart the same world-day/event ID with `LeaseSequence` reset, but client-side `clientLeaseHistory` survived resolution/cleanup. Fresh low revisions were then rejected as stale and extras could stop spawning.

Correction:

- `BloodMoonPresentation.OnResolutionComplete` now calls `BloodMoonSpawner.ResetClientState()`;
- the existing `resolution-complete` client action reaches listen-host and remote clients;
- active leases, lease history, event high-water and zone-claim runtime state are cleared before a same-event diagnostic restart;
- ordinary resolution uses the same cleanup and is unaffected semantically.

Relevant commit:

```text
6b2de7c68d890f7f2520d1af84ba33fb015e9cac
```

Runtime evidence still required: `cleanup -> start active` on listen host and remote client while reusing the same event ID.

## 5. Group topology changes and extra-enemy caps

Finding: live extras retain their immutable spawn-time `GroupId`. After group merge/split, exact marker equality could make extras from an absorbed group disappear from the new group's cap accounting, allowing the server to issue additional leases above the intended group cap.

Correction keeps provenance immutable rather than rewriting ZDO markers:

- live extra accounting is projected onto the **current** group topology by the extra's current position and the nearest current group anchor, with GroupId tie-breaking;
- pending reports are projected the same way, using the spawned ZDO position when available and the immutable spawn-zone center while metadata is still pending;
- existing server lease allowances are clamped before the ordinary lease pass;
- after the ordinary pass, every resulting lease is re-clamped against current topology;
- if the ordinary pass sent a larger allowance, the corrected smaller allowance is immediately sent with the **same lease revision**;
- client same-revision handling already keeps the minimum allowance observed, so delivery order cannot replenish tokens;
- exhausted reservations are removed server-side so later capacity obtains a genuinely new revision;
- `BloodMoonGroupState.ExtraEnemyCount` is also projected from current topology for diagnostics.

Global server cap continues to count all extras independently of group topology. Lowering/merging caps does not delete already-live extras.

Relevant commit:

```text
f93ed3f85f692b3a22b2ed3bc65a607e2e7eba76
```

Runtime evidence still required: two capped solo groups merge, one group splits, in-flight pending report during merge, and lease delivery reordering.

## 6. Durable live-skill report ACK/retry

Finding: the live x3 bonus is applied locally before a fire-and-forget skill report reaches the server. If the player profile persisted the raised skill and the process disconnected/crashed before the server report became durable, reconnect used a lower server baseline and could grant additional live bonus beyond the configured cap.

Corrections are additive and do not change the existing `SkillGain` payload:

- outgoing skill reports are recorded in `Player.m_customData` **before** network send;
- the record is world/event/player bound and stores sequence, base/live deltas, total locally consumed live bonus and pending reports;
- the local Bloodlust skill cap reads the durable local total as well as the server baseline;
- local sequence is restored to at least the persisted high-water mark before another report is generated;
- pending reports retry every two seconds during Active/AutoCompleting, in sequence order;
- the server returns a separate `SkillGainAck` containing its durable accepted sequence high-water;
- duplicate/retried reports remain idempotent and receive the current ACK without applying their deltas twice;
- the private participant detail baseline is also treated as an ACK/high-water source, covering a lost explicit ACK;
- ACK processing removes only pending reports at or below the server-confirmed high-water mark;
- no forced profile save is required per skill action: Valheim persists skills and `m_customData` in the same player profile. If a crash occurs before that profile is saved, both the skill change and pending record are lost together; if the skill is durable, the pending record is durable with it.

The added ACK RPC uses the existing Blood Moon protocol header/version because no existing payload was changed. Blood Moon is still pre-release; exact-build multiplayer compatibility remains the supported development assumption.

Relevant commit:

```text
bc66f0d3df219db209be5cc9b6a3bd4f3e5f778f
```

Runtime evidence still required: save/disconnect before ACK, ACK loss, reconnect with pending report, duplicate retry, live-bonus cap after reconnect, and listen-host self-routing.

## 7. Review status and next gate

All six findings from the Codex review of `231ae391891e330277868bf7733723333ea59521` have been addressed in source.

These are new runtime/network/persistence changes and have **not** been compiled or executed by the assistant. Owner compilation and the affected cases from `08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md` remain mandatory runtime gates.

The next Codex request must again be a full project-conformance review, not a latest-delta-only review. It must read:

```text
docs/tasks/CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md
docs/tasks/blood-moon/25_PROJECT_CONFORMANCE_MATRIX.md
docs/tasks/blood-moon/26_PR42_CONFORMANCE_REVIEW_HANDOFF.md
docs/tasks/blood-moon/27_CODEX_CONFORMANCE_REVIEW_FIXES_2026-09-08.md
```

and independently re-check the complete effective `master...feat/blood-moon` behavior, including the six corrected distributed-system paths above and all existing matrix sections.

PR #42 remains draft, open and unmerged.
