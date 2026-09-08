# Blood Moon: authoritative context and implementation

## 0. Current work

Repository: `https://github.com/shudnal/Seasons`

Only working branch: `feat/blood-moon`

Existing implementation and runtime hardening: draft PR #42. Continue from its current head; do not start another branch or recreate the implementation from the early planning text.

**Current project-conformance source of truth:**

`docs/tasks/blood-moon/25_PROJECT_CONFORMANCE_MATRIX.md`

**Current continuation/review evidence:**

- `docs/tasks/blood-moon/26_PR42_CONFORMANCE_REVIEW_HANDOFF.md`
- `docs/tasks/blood-moon/27_CODEX_CONFORMANCE_REVIEW_FIXES_2026-09-08.md`
- `docs/tasks/blood-moon/28_CODEX_CONFORMANCE_REVIEW_ROUND2_FIXES_2026-09-08.md`
- `docs/tasks/blood-moon/29_CODEX_CONFORMANCE_REVIEW_ROUND3_FIXES_2026-09-08.md`

The matrix maps accepted behavior to the effective implementation, records current precedence between old and new documents, separates static conformance from owner-side runtime/visual gates, and is the required basis for complete PR reviews. Documents `26`-`29` are implementation/review evidence for later exact heads; they do not silently redefine gameplay.

Immediately preceding accepted product/compatibility corrections are:

- `docs/tasks/blood-moon/22_DREAMTEXT_AND_INPUT_POLICY.md`
- `docs/tasks/blood-moon/23_PR42_RUNTIME_HARDENING_AND_CLOCK_POLICY.md`
- `docs/tasks/blood-moon/24_MARKETPLACE_9_9_4_COMPATIBILITY.md`

For game classes, read `https://github.com/shudnal/assemblies_combined` first.

Do not use the old external drafts `BloodMoon_Design_Document.md` or `BloodMoon_Codex_Implementation_Brief.md`. Do not return to the rejected personal parallel-layer architecture.

## 1. Authoritative document set

### Original product and subsystem decisions

1. `docs/tasks/CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`
2. `docs/tasks/blood-moon/01_STATE_AND_SCHEDULE.md`
3. `docs/tasks/blood-moon/02_NETWORK_PERSISTENCE_AND_PARTICIPANTS.md`
4. `docs/tasks/blood-moon/03_PRESENTATION_AND_SUPPRESSION.md`
5. `docs/tasks/blood-moon/04_COMBAT_PROGRESS_AND_RESOLUTION.md`
6. `docs/tasks/blood-moon/05_SEASONS_INTEGRATION.md`
7. `docs/tasks/blood-moon/06_FUTURE_PROGRESSION_AND_REWARDS.md`
8. `docs/tasks/blood-moon/07_BLOOD_CRAFT_AND_WORLD_PRESERVATION.md`
9. `docs/tasks/blood-moon/08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md`
10. `docs/tasks/blood-moon/09_IMPLEMENTATION_DECISIONS_AND_ORDER.md`
11. `docs/tasks/blood-moon/10_IMPLEMENTATION_TASK.md`
12. `docs/tasks/blood-moon/11_VALHEIM_NETWORK_AI_AND_CCS_RESEARCH.md`
13. `docs/tasks/blood-moon/12_RELATED_MODS_RESEARCH.md`

Files `01`-`08` define the original product/subsystem requirements, `09` records accepted implementation decisions, `10` defines implementation order/scope, and `11`-`12` record source research. They must be read together with the later authoritative corrections below.

### Implementation evidence and later corrections

Historical implementation/review evidence:

- `docs/tasks/blood-moon/13_IMPLEMENTATION_REPORT.md`
- `docs/tasks/blood-moon/14_CODE_REVIEW_STATUS.md`
- `docs/tasks/blood-moon/15_RELEASE_READINESS.md`
- `docs/tasks/blood-moon/17_HARDENING_CHECKPOINT_2026-08-25.md`
- `docs/tasks/blood-moon/18_CODEX_REVIEW_HARDENING_2026-08-25.md`
- `docs/tasks/blood-moon/19_NEW_PR_COMMENTS_2026-08-25.md`
- `docs/tasks/blood-moon/21_CLASSIC_CSPROJ_COMPILE_ITEMS.md`
- `docs/tasks/blood-moon/26_PR42_CONFORMANCE_REVIEW_HANDOFF.md`
- `docs/tasks/blood-moon/27_CODEX_CONFORMANCE_REVIEW_FIXES_2026-09-08.md`
- `docs/tasks/blood-moon/28_CODEX_CONFORMANCE_REVIEW_ROUND2_FIXES_2026-09-08.md`
- `docs/tasks/blood-moon/29_CODEX_CONFORMANCE_REVIEW_ROUND3_FIXES_2026-09-08.md`

Current authoritative corrections/clarifications in their explicit scope:

- `docs/tasks/blood-moon/16_CLIENT_TRUST_BOUNDARY.md`
- `docs/tasks/blood-moon/20_RUNTIME_JSON_AND_ZDO_PARKING.md`
- `docs/tasks/blood-moon/22_DREAMTEXT_AND_INPUT_POLICY.md`
- `docs/tasks/blood-moon/23_PR42_RUNTIME_HARDENING_AND_CLOCK_POLICY.md`
- `docs/tasks/blood-moon/24_MARKETPLACE_9_9_4_COMPATIBILITY.md`
- `docs/tasks/blood-moon/25_PROJECT_CONFORMANCE_MATRIX.md`

### Precedence rules

Read later corrections before modifying the corresponding subsystem. In their explicit scope, later accepted decisions override earlier proposals and historical implementation reports.

In particular:

- `22_DREAMTEXT_AND_INPUT_POLICY.md` overrides every earlier description of an immediate/forced Blood Moon DreamText screen, a DreamText presentation handshake, or Blood Moon-owned global input suppression. Current behavior is a profile-backed pending Blood Moon dream consumed by the next ordinary vanilla sleep; Blood Moon does not patch `Player.TakeInput`.
- `23_PR42_RUNTIME_HARDENING_AND_CLOCK_POLICY.md` overrides older ambiguous time-control wording. Natural/`skiptime` progression is forward-only; backward time is not gameplay rollback; required forward transition side effects still run.
- `24_MARKETPLACE_9_9_4_COMPATIBILITY.md` supersedes the Marketplace 9.8.9 API evidence section in `23` for the current Marketplace territory-map contract.
- `25_PROJECT_CONFORMANCE_MATRIX.md` is the current cross-subsystem conformance checkpoint. It also clarifies stale wording such as old group-coordinator terminology, Blood Craft schema/station semantics, current damage/death trust flow, and which historical report claims are no longer current.
- `26`-`29` record continuation and exact-head review/fix evidence. They may clarify how the matrix is implemented, but are not independent authorization to change accepted gameplay mechanics.
- `13`, `14` and `15` remain valuable evidence for the commits/reviews they describe, but they are historical checkpoints rather than current product authority when they conflict with `22`-`25`.

Implementation/review reports are evidence for the commits they identify, not a guarantee that a later head has passed review or runtime testing. The absence of an exception in an owner playtest does not establish multiplayer correctness.

## 2. Event purpose

Blood Moon is the annual autumn culmination, approximately once every 40 game days with the standard season lengths.

It is not a conventional base raid. It is a short, dense combat night: the world gives advance visual warnings; players can prepare temporary free equipment from known recipes; eligible existing monsters become aggressive Blood enemies at 23:00; additional marked enemies spawn within controlled limits; and personal Bloodlust progression gives a simple combat objective.

Buildings, tamed creatures, NPCs, bosses, and resource objects are not valid Blood Moon damage targets. `Defeated` does not create a tombstone, request respawn, or remove skills.

The event's explicit durable reward is combat skill experience. There is no event currency, material/cosmetic reward catalogue, reputation/key reward, or permanent event equipment. Existing monsters retain their ordinary loot and are not restored after being killed; that accepted direct-conversion consequence is distinct from an event reward system. Event-created extras do not drop loot.

Blood Craft is a temporary tool, not a reward. It removes the material-resource barrier to trying known weapons, armor, magic, ammunition, and consumables while preserving the source crafting-station and station-level requirements. Only acquired skill experience and normal non-event consequences remain after temporary inventory cleanup.

## 3. Core cycle

```text
Late-autumn nightly forewarning
-> Marked at 18:00
-> Sleep and new boss sacrifices suppressed
-> Blood Craft preparation from known eligible recipes
-> Linear red overlay on the current environment
-> Active at 23:00
-> Forced Blood Moon environment
-> Persistent outdoor bosses parked through their ZDOs
-> Eligible existing MonsterAI use Blood behavior
-> Additional marked enemies spawn within caps
-> Fighting / GoalReached / Defeated / Withdrawn
-> Early completion or forced end at 05:45
-> Resolution and cleanup
-> Parked bosses restored
-> Time advances toward the scheduled 06:00 morning where applicable
-> Outcome/chronicle is persisted and a Blood Moon dream becomes pending for a future ordinary sleep
```

The current DreamText/input presentation contract is exclusively defined by `22_DREAMTEXT_AND_INPUT_POLICY.md`. This cycle is not authorization to introduce a separate DreamText overlay or a global input-lock mechanism.

## 4. Key accepted decisions

### World and enemies

All clients see one shared set of creatures. There is no personal visibility/collision layer, `AwaitingContact`, or first-contact record.

At 23:00 a participant enters Fighting unless an encounter with an unparkable boss excludes that player. Existing eligible monsters are classified dynamically without a conversion ZDO marker. They retain ordinary loot/ragdolls and are not deleted at dawn. Additional custom-spawned enemies have a nonnegative `SpawnedEventId`; their lifetime provenance controls no-loot/fast-ragdoll behavior, and server cleanup removes them.

Existing AI behavior is changed through conditional runtime patches, not persistent hunt/alert/max-health/shared-prefab mutation.

### Defeat

The local Player owner intercepts `Character.CheckDeath` while blood combat behavior is live. `Player.OnDeath` is not invoked. Health, stamina, and eitr are restored; food and adrenaline remain unchanged. No transform, respawn, or tombstone manipulation occurs.

Vanilla `SoftDeath` is not added. The Blood Moon status explains the safe-defeat behavior. DoT cleanup is limited to the accepted calculable damaging vanilla effects.

Recovery has two stages: full protection until stabilization, capped at 15 seconds, followed by 10 seconds with incoming damage multiplied by `0.25`. Repeated synchronization must not heal again or restart these stages. Ordinary re-entry after terminal exit is not supported.

### Success and subsequent exit

`GoalReached` and the later exit reason are independent:

```text
GoalReached = true
ExitReason = None | Defeated | Withdrawn | Disconnected
```

Reaching the genuine combat objective fixes Success for that event ID. A later defeat, withdrawal, or disconnect does not revoke that success or its completion entitlement, but affects the chronicle and eventual DreamText. Automatic display completion is not genuine combat success.

### Bosses

Boss-producing `OfferingBowl` interactions are blocked from the Marked cutoff. A request accepted server-side before the cutoff may complete afterward. Persistent outdoor bosses are parked by server-owned raw-ZDO relocation to a far XZ sector and later restored to their durable original transform.

Nonpersistent and interior bosses are not parked. An affected player in the same validated encounter/navigation context receives terminal Withdrawn. Boss animation, target, velocity, coroutine, HUD, and other runtime-only state do not have to be reconstructed; the same persistent ZDO must be restored. Lingering boss projectiles, AOE, and summons are not explicitly removed.

### Player contexts

Mounted and generic attached players are supported without forced detach. Ship/ocean participation remains supported even when no valid land spawn exists. Ordinary interiors participate; additional interior spawn positions come only from loaded `CreatureSpawner` placements, without invoking their `Spawn()` lifecycle.

Teleport temporarily excludes a player as a spawn anchor, then participation continues. Approaching the world edge causes terminal Withdrawn before the dangerous boundary. Body blocking by terminal players is not given a special collision system.

### Networking and spawning

Ordinary CCS `CustomSyncedValue` carries the current global event state and public participant routing snapshot. `SequencedCustomSyncedValue` is not required. Addressed player/group operations use the feature's RPCs.

The local Player owns defeat detection; the server receives a notification and enforces identity/event/idempotency boundaries rather than independently proving zero health. Damage/progress hardening uses the source/target authority split documented by the current implementation and matrix, without turning the feature into a hostile-client anti-cheat system.

There is no group-wide spawn coordinator. Each relevant zone's actual current owner performs extra spawning for that zone only. The server controls group/cap/pool rules and per-zone leases without requiring live Character instances on a dedicated server.

Read `16_CLIENT_TRUST_BOUNDARY.md`, `20_RUNTIME_JSON_AND_ZDO_PARKING.md`, `23_PR42_RUNTIME_HARDENING_AND_CLOCK_POLICY.md` and `25_PROJECT_CONFORMANCE_MATRIX.md` before adding validation, persistence or spawn-authority rules.

### Time changes and administrative control

Normal time and `skiptime` can advance the event through its legal transitions. Required transition side effects are preserved even when a large forward jump crosses multiple phases. A jump past forced end enters the normal resolution transaction rather than assigning `Resolved` directly.

Backward time changes do not roll back phases, acquired experience, temporary-item cleanup, terminal outcomes, killed creatures, parked-boss history, or event history.

Explicit admin commands control diagnostic phase starts. Starting an earlier phase of a live event requires explicit cleanup first; a debug restart is not a reward rollback or an independent production event if it reuses the same world-day ID. `23_PR42_RUNTIME_HARDENING_AND_CLOCK_POLICY.md` contains the detailed policy and scenarios.

## 5. Development and continuation rules

Work directly in `feat/blood-moon` using logical commits. Do not create additional experimental branches or replace the accepted architecture with a disposable implementation.

Before changing a game-method patch, read its actual source in `assemblies_combined`. When fixing a Blood Moon method, inspect existing internal Harmony adapters too; some earlier hardening is implemented in those adapters. Prefer a direct production correction to adding another overriding wrapper.

Maintain decisions, patch-point reasoning, defects/fixes, and the next continuation point in the repository. New or rewritten implementation-facing documentation, comments, logs, and identifiers should use English except intentional localization resources.

Do not change version, public README, Thunderstore changelog, packaging, or release metadata without a separate request. Do not claim an assistant-side Valheim build/runtime result. Clearly distinguish static inspection, requested review, completed review, and owner-side runtime evidence.

After implementation changes, request Codex review on the exact current PR head. The final pass must be a complete **project-conformance review**, not merely a latest-delta review: Codex must read this index and `25_PROJECT_CONFORMANCE_MATRIX.md`, then the current review checkpoints `26`-`29`, apply the precedence rules above, and review the full effective `master...feat/blood-moon` behavior including internal Harmony adapters.

Fix confirmed findings without treating an older review as approval for newer code. Keep PR #42 draft, open, and unmerged until the owner decides otherwise.
