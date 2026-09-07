# Blood Moon: authoritative context and implementation

## 0. Current work

Repository: `https://github.com/shudnal/Seasons`

Only working branch: `feat/blood-moon`

Existing implementation and runtime hardening: draft PR #42. Continue from its current head; do not start another branch or recreate the implementation from the early planning text.

**Current continuation checkpoint:**

`docs/tasks/blood-moon/23_PR42_RUNTIME_HARDENING_AND_CLOCK_POLICY.md`

That checkpoint records the Marketplace/minimap corrections, client lifecycle and spawn hardening, accepted `skiptime` behavior, evidence limits, and owner-side validation scenarios. The exact code-review request and reviewed commit belong in the PR timeline.

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

Files `01`-`08` define product/subsystem requirements, `09` records accepted implementation decisions, `10` defines implementation order/scope, and `11`-`12` record source research.

### Implementation evidence and later corrections

- `docs/tasks/blood-moon/13_IMPLEMENTATION_REPORT.md`
- `docs/tasks/blood-moon/14_CODE_REVIEW_STATUS.md`
- `docs/tasks/blood-moon/15_RELEASE_READINESS.md`
- `docs/tasks/blood-moon/16_CLIENT_TRUST_BOUNDARY.md`
- `docs/tasks/blood-moon/17_HARDENING_CHECKPOINT_2026-08-25.md`
- `docs/tasks/blood-moon/18_CODEX_REVIEW_HARDENING_2026-08-25.md`
- `docs/tasks/blood-moon/19_NEW_PR_COMMENTS_2026-08-25.md`
- `docs/tasks/blood-moon/20_RUNTIME_JSON_AND_ZDO_PARKING.md`
- `docs/tasks/blood-moon/21_CLASSIC_CSPROJ_COMPILE_ITEMS.md`
- `docs/tasks/blood-moon/22_DREAMTEXT_AND_INPUT_POLICY.md`
- `docs/tasks/blood-moon/23_PR42_RUNTIME_HARDENING_AND_CLOCK_POLICY.md`

Read the later corrections before modifying the corresponding subsystem. In their explicit scope, later accepted decisions override earlier proposals. In particular, client trust, JSON/ZDO persistence, vanilla DreamText/input presentation, and clock control must not be reconstructed from superseded early assumptions.

Implementation/review reports are evidence for the commits they identify, not a guarantee that a later head has passed review or runtime testing. The absence of an exception in the owner's initial playtest does not establish multiplayer correctness.

## 2. Event purpose

Blood Moon is the annual autumn culmination, approximately once every 40 game days with the standard season lengths.

It is not a conventional base raid. It is a short, dense combat night: the world gives advance visual warnings; players can prepare temporary free equipment from known recipes; eligible existing monsters become aggressive Blood enemies at 23:00; additional marked enemies spawn within controlled limits; and personal Bloodlust progression gives a simple combat objective.

Buildings, tamed creatures, NPCs, bosses, and resource objects are not valid Blood Moon damage targets. `Defeated` does not create a tombstone, request respawn, or remove skills.

The event's explicit durable reward is combat skill experience. There is no event currency, material/cosmetic reward catalogue, reputation/key reward, or permanent event equipment. Existing monsters retain their ordinary loot and are not restored after being killed; that accepted direct-conversion consequence is distinct from an event reward system. Event-created extras do not drop loot.

Blood Craft is a temporary tool, not a reward. It removes the resource barrier to trying known weapons, armor, magic, ammunition, and consumables, while retaining only the acquired skill experience after the temporary inventory is cleaned up.

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
-> Time advances toward the scheduled 06:00 morning
-> Outcome, chronicle, Rested removal and presentation
```

The current presentation contract is in `22_DREAMTEXT_AND_INPUT_POLICY.md`. This cycle is not authorization to introduce a separate DreamText overlay or input-lock mechanism contrary to that document.

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

Reaching the genuine combat objective fixes Success for that event ID. A later defeat, withdrawal, or disconnect does not revoke that success or its completion entitlement, but affects the chronicle and presentation. Automatic display completion is not genuine combat success.

### Bosses

Boss-producing `OfferingBowl` interactions are blocked from 18:00. Persistent outdoor bosses are parked by server-owned raw-ZDO relocation to a far XZ sector and later restored to their original position.

Nonpersistent and interior bosses are not parked. An affected player fighting such an unparkable boss receives terminal Withdrawn. Boss animation, target, velocity, coroutine, HUD, and runtime-only state do not have to be restored; the same persistent ZDO must return to the original position. Lingering boss projectiles, AOE, and summons are not explicitly removed.

### Player contexts

Mounted and generic attached players are supported without forced detach. Ship/ocean participation remains supported even when no valid land spawn exists. Ordinary interiors participate; additional interior spawn positions come only from loaded `CreatureSpawner` placements, without invoking their `Spawn()` lifecycle.

Teleport temporarily excludes a player as a spawn anchor, then participation continues. Approaching the world edge causes terminal Withdrawn before the dangerous boundary. Body blocking by terminal players is not given a special collision system.

### Networking

Ordinary CCS `CustomSyncedValue` carries the current global event state and public participant routing snapshot. `SequencedCustomSyncedValue` is not required. Addressed player/group operations use the feature's RPCs.

The local Player owns defeat detection; the server receives a notification and enforces identity/event/idempotency boundaries rather than independently proving zero health. Each zone's actual owner performs extra spawning. The server controls group/cap/pool rules without requiring live Character instances on a dedicated server.

Read `16_CLIENT_TRUST_BOUNDARY.md` and the networking/runtime addenda before adding validation or persistence rules.

### Time changes and administrative control

Normal time and `skiptime` can advance the event through its legal transitions. Backward time changes do not roll back phases, acquired experience, temporary-item cleanup, terminal outcomes, or event history.

Explicit admin commands control diagnostic phase starts. Starting an earlier phase of a live event requires explicit cleanup first; a debug restart is not a reward rollback or an independent production event if it reuses the same world-day ID. `23_PR42_RUNTIME_HARDENING_AND_CLOCK_POLICY.md` contains the exact policy and scenarios.

## 5. Development and continuation rules

Work directly in `feat/blood-moon` using logical commits. Do not create additional experimental branches or replace the accepted architecture with a disposable implementation.

Before changing a game-method patch, read its actual source in `assemblies_combined`. When fixing a Blood Moon method, inspect existing internal Harmony adapters too; some earlier hardening is implemented in those adapters. Prefer a direct production correction to adding another overriding wrapper.

Maintain decisions, patch-point reasoning, defects/fixes, and the next continuation point in the repository. All new or rewritten repository documentation, comments, logs, and identifiers must be English except intentional localization resources.

Do not change version, public README, Thunderstore changelog, packaging, or release metadata without a separate request. Do not compile or run the Valheim mod on the assistant side under the current owner instruction. Clearly distinguish static inspection, requested review, completed review, and owner-side runtime evidence.

After changes, request Codex review on the exact current PR head. Fix confirmed findings without treating an older review as approval for newer code. Keep PR #42 draft, open, and unmerged until the owner decides otherwise.
