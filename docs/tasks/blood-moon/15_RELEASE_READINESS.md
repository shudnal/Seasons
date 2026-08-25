# Blood Moon release-readiness checkpoint

This file is the repository checkpoint for draft PR #42 before owner-side runtime playtest. It complements `13_IMPLEMENTATION_REPORT.md` and `14_CODE_REVIEW_STATUS.md`.

## Pull request state

```text
PR: #42 Blood Moon first implementation slice
base: master
head: feat/blood-moon
state: draft / open / not merged
URL: https://github.com/shudnal/Seasons/pull/42
```

The PR must remain unmerged until explicit owner approval.

The following release metadata remains intentionally unchanged:

- plugin version;
- public README;
- Thunderstore changelog;
- packaging/release metadata.

## Static code-review gate

The exact runtime code-freeze head:

```text
0be8eb89d600a95720f08387041fb7ab15ccd5f7
```

received a Codex result with no major issues after all prior confirmed findings were fixed and all inline review threads were resolved.

The final hardening present at that head includes:

- explicit server event/participant/resolution state machines;
- crash-recoverable current-event persistence with newest-valid canonical/`.new`/`.old` selection;
- CCS public routing plus targeted private participant details;
- monotonic resync and reconnect skill baselines;
- real-time-calendar-safe net-time and lease-time handling;
- authoritative zone-owner lease validation;
- frozen per-event extra-enemy prefab identity;
- exact prefab + `MonsterAI` spawn-report validation;
- creator/lease/group/zone validation for client-spawned extras;
- resolution freeze for both ordinary and mixed-context spawning;
- cleanup of completely unreported valid extras without deleting unrelated marked ZDOs;
- dormant `EventId == -1` cleanup safety;
- durable server-side spawn-pool history for stale prior-event cleanup;
- server-observed enemy death validation;
- immutable projectile/AOE/summon attribution guards;
- environmental hazard damage for participants without opening Blood-enemy farming paths;
- boss discovery/withdrawal navigation-context validation;
- durable boss parking/restoration;
- active-world Blood Craft preservation plus stale world-sink cleanup;
- disabled-recipe and stack-limit Blood Craft invariants;
- explicit pre-combat cancellation behavior;
- late defeat-report rejection after combat resolution starts;
- independent durable per-player outcome queue;
- persistence-aware local outcome acknowledgement;
- conservative cloud outcome acknowledgement based on a marker observed from character persistence in a fresh process.

A final Codex review is required after the internal documentation freeze (`13`, `14`, `15`). That final result should be recorded in the PR timeline without another documentation-only commit, preserving an exact reviewed final head.

## Authoritative game-source verification

All game API and Harmony target verification used:

```text
repository: https://github.com/shudnal/assemblies_combined
commit: cf2cda3a4c5c05e62cb8052a61753e5dcaecc28e
```

Freeze checks include:

- `ZRoutedRpc` sender semantics and peer/player identity;
- immutable `ZDOID` creator identity and `ZDOMan` ownership/sector behavior;
- `ZNetScene.GetPrefab(string/int)` and stable prefab hashes;
- raw `SpawnSystem` ownership and authored `CreatureSpawner` behavior;
- `Location.GetLocation`/interior navigation context;
- `Character` damage/death flow and ZDO health;
- `Projectile`, `Aoe`, `SpawnAbility` and `TriggerSpawnAbility`;
- `Player.RaiseSkill` override behavior;
- `PlayerProfile.SavePlayerData`, `PlayerProfile.Save`, `FileWriter` and `FileHelpers.ReplaceOldFile`;
- the current cloud-save failure path where vanilla can return successful profile save status while only producing a local recovery backup;
- `EnvMan`/`EnvSetup`, random events, sleep and boss-offering paths;
- vanilla inventory/container/world-consumer boundaries.

## No assistant-side runtime claim

No local Valheim build or runtime execution was performed by the assistant, per owner workflow.

Therefore this checkpoint is **static/review ready**, not runtime release-approved.

## Owner-side runtime acceptance gate

Owner-side validation must use `08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md`. At minimum the following scenarios remain required.

### Lifecycle and recovery

- single-player annual lifecycle;
- listen-host annual lifecycle;
- dedicated server with multiple clients;
- late join during Marked and Active;
- reconnect during Active and Resolving;
- dedicated-server restart during Active;
- restart in each persisted resolution step;
- first enable inside an already-running annual window;
- disable during Forewarning/Marked without time advance or success outcome;
- final 04:15/05:45/06:00 behavior and fade/input release.

### Defeated, GoalReached and recovery

- enemy-caused Defeated;
- environmental damage and environmental Defeated;
- recovery started on ground, in water and while falling;
- recovery through morning resolution and reconnect;
- GoalReached followed by helper combat;
- GoalReached followed by Defeated;
- Withdrawn and no re-entry;
- late defeat/report traffic after resolution begins.

### Networking and spawning

- real zone-owner migration with in-flight lease/report replication;
- lease expiry under ordinary and real-time-calendar worlds;
- mixed interior/surface participants in one map zone;
- authored interior CreatureSpawner pathing;
- configured extra prefab changed during an active event;
- configured extra prefab changed before restart of that same active event;
- valid extra with a completely lost spawn report cleaned at resolution;
- invalid/forged marked non-extra preserved by cleanup;
- dormant world recovery with `EventId == -1`;
- stale extra from an earlier event cleaned after a later event state exists;
- server/client cap behavior under rapid ownership movement.

### Bosses and combat routing

- persistent outdoor boss parking/restoration;
- interior boss withdrawal;
- nonpersistent surface boss withdrawal;
- surface player near dungeon boss and dungeon player near surface boss remain separated by navigation context;
- delayed projectile/AOE hits across personal exit and event rollover;
- permanent and temporary participant summons;
- environmental/null-source damage behavior;
- server-observed death reconciliation under replication delay.

### Blood Craft

- temporary item creation/upgrade/equip/use;
- reconnect and restart with valid temporary inventory;
- stale inventory from another world/event/owner;
- all guarded vanilla world sinks;
- multi-craft near stack limits;
- disabled source/upgrade recipes;
- permanent and temporary summon cleanup;
- third-party inventory/equipment compatibility where available.

### Skills and durable outcomes

- live x3 budget and per-event cap;
- reconnect private baseline before new bonus reporting;
- completion reward monotonic replay;
- offline participant outcome delivered after reconnect;
- outcome surviving a later annual event;
- local-profile save then server acknowledgement;
- cloud-profile successful persistence followed by fresh-process acknowledgement;
- simulated/observed cloud save failure retaining the server queue;
- repeated delivery remaining idempotent while acknowledgement is pending;
- chronicle/DreamText and one-time Rested removal.

### Presentation

- Forewarning/Marked/Active visuals in representative biomes and interiors;
- Fader cloud clone lifecycle through environment switching;
- random-event suppression/release;
- sleep and OfferingBowl suppression;
- Blood Moon/recovery status icon behavior;
- resolution fade/input timing.

## Known non-blocking content limitations

- Blood Moon music assets are not supplied; music work remains paused.
- Blood Moon-specific runtime wording is English-first until playtest wording stabilizes.
- Automatic raid/trophy/key/achievement-derived enemy-pool progression remains future work in `06_FUTURE_PROGRESSION_AND_REWARDS.md`; the current implementation uses an explicit configured bootstrap prefab and freezes it per event.
- Third-party custom world sinks or inventory implementations that bypass vanilla guarded boundaries may require compatibility patches after runtime discovery.
- Exact parked-boss animation/target/coroutine/HUD state is not reconstructed by design; the persistent boss ZDO is restored and vanilla runtime state resumes.

## Final repository checks

Before this checkpoint is handed to owner-side playtest, verify:

1. final documentation-inclusive Codex review has no confirmed finding;
2. all inline review threads are resolved;
3. PR #42 remains draft/open/unmerged;
4. `master...feat/blood-moon` contains no unintended release-metadata changes;
5. code and new English technical documentation contain no accidental Cyrillic outside intentional localization/design source material;
6. no assistant-side build/runtime result is claimed.

## Continuation rule

Any runtime defect found during owner-side validation must be recorded in the repository with its reproduction, design decision and fix before the next implementation step. Do not merge PR #42 or alter release metadata until the owner explicitly requests that step.
