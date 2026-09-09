# Blood Moon release-readiness checkpoint

This file is the repository checkpoint for draft PR #42 before owner-side runtime playtest. It complements `13_IMPLEMENTATION_REPORT.md`, `14_CODE_REVIEW_STATUS.md` and the authoritative acceptance matrix in `08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md`.

## Pull request state

```text
PR: #42 Blood Moon first implementation slice
base: master
head: feat/blood-moon
state: draft / open / not merged
URL: https://github.com/shudnal/Seasons/pull/42
```

The PR must remain draft and unmerged until explicit owner approval.

Release metadata intentionally remains unchanged:

- plugin version;
- public README;
- Thunderstore changelog;
- packaging/release metadata.

## Static verification source

Game API and patch-point verification uses:

```text
repository: https://github.com/shudnal/assemblies_combined
commit: cf2cda3a4c5c05e62cb8052a61753e5dcaecc28e
```

The assistant did not build or run the Valheim mod. This checkpoint can become static/review ready, but runtime release approval requires owner-side Valheim testing.

## Current hardened architecture

The current static hardening includes:

- explicit server event/participant/resolution state machines;
- world-bound newest-valid persistence recovery;
- CCS global/public snapshots plus targeted private RPC state;
- monotonic same-event resync and reconnect skill baselines;
- real-time-calendar-safe net-time handling;
- Unity monotonic client lease expiry;
- server-validated per-zone spawn leases and frozen extra-enemy prefab identity;
- exact prefab/`MonsterAI` spawn validation and stale prior-event cleanup history;
- dynamic existing-monster Blood behavior without persistent conversion mutation;
- owner-side final damage guard plus early direct/projectile/AOE filtering;
- contract-correct projectile pass-through for forbidden character colliders;
- preserved world-geometry and stale-projectile physical lifecycle;
- source-owner authorization plus target-owner actual-damage confirmation for kill credit;
- lethal-hit credit observation before `CheckDeath` through `Character.SetHealth`;
- server-controlled Bloodlust damage/movement/lifesteal endpoints using the same matched actual-damage authority;
- rolling max-health-per-second healing cap and monotonic targeted healing grants;
- durable local Defeated terminal state, immediate profile-save request and reconnect report retry;
- source-recipe-bound Blood Craft with source station/quality semantics;
- Blood Craft preflight support for `m_requireOnlyOneIngredient`;
- common static `ItemDrop.DropItem` rejection for temporary world materialization;
- verified vanilla Blood Craft world-consumer signatures;
- server-authorized Forewarning boss-offering relay across the Marked cutover;
- persistent outdoor boss parking/restoration and navigation-context withdrawal;
- live/completion skill reward accounting;
- independent durable per-player outcome queue;
- durable queue capture as a prerequisite for current-resolution outcome delivery;
- immediate current-resolution native DreamText presentation;
- bounded Dream presentation start/completion handshake separate from durable profile acknowledgement;
- consolidated production resolution guards instead of corrective self-patches;
- routed-RPC registration lifetime protection across world teardown;
- proactive hit-attribution/offering RPC registration on peers that may only own targets.

## Routed-RPC teardown invariant

Current Valheim `ZRoutedRpc.Register` stores method handlers with `Dictionary.Add`. Blood Moon subsystem runtime reset therefore clears event/runtime state without clearing a cached registration reference while that routed-RPC object may still be alive.

A genuinely new network session is detected by object-reference change and registers each handler once on the new `ZRoutedRpc` instance.

## Exact static source checks in the latest pass

The latest hardening re-read the current Valheim source for:

- Player/Character death semantics, `SetHealth`, `RPC_Damage` and `CheckDeath` ordering;
- `Projectile.OnHit`, `FixedUpdate`, `SpawnOnHit`, TTL, skill/adrenaline and item respawn;
- `ItemDrop.ItemData.Clone` and static `ItemDrop.DropItem`;
- current vanilla `IDestructible.Damage(HitData)` implementations;
- movement calculations used for temporary Bloodlust movement modifiers;
- `Recipe.GetAmount` and source-station APIs;
- `InventoryGui` Blood Craft patch points;
- `OfferingBowl` interaction, owner-RPC, item-consumption and delayed-spawn flow;
- `ZNetView` owner RPC routing;
- `ZNetScene.FindInstance` and prefab lookup;
- `ZRoutedRpc.Register` transport lifetime;
- `ZPackage` float/vector/ZDOID serialization;
- Blood Craft world-consumer signatures for `ItemStand`, `ArmorStand`, `Fermenter`, `CookingStation`, `Smelter`, `Turret` and `Catapult`;
- existing network/ZDO/spawn/profile-persistence APIs documented by the earlier review trail.

This is a static source audit, not a build or runtime claim.

## Current Codex gate

The Codex review on:

```text
39d12ec6361f82f930c94cd0c258f272e3a8a916
```

reported six findings. Five were confirmed and fixed; the projectile-collision suggestion was rejected because it conflicts with the explicit authoritative requirement that a forbidden character collider must not stop the projectile. The exact dispositions are recorded in `14_CODE_REVIEW_STATUS.md`.

The runtime-code hardening after that review was complete at:

```text
3b55e99f4006b2aca73686a5130e8b843888ee3f
```

After this documentation freeze and PR-thread disposition, request Codex review on the new exact documentation-inclusive head. The review gate is accepted only if:

1. the exact reviewed SHA equals the current PR head;
2. there is no unresolved confirmed finding;
3. all inline review threads are resolved;
4. the final result is recorded in the PR timeline without another documentation-only commit.

If a confirmed issue is found, fix it and review the new exact head again.

## Owner-side runtime acceptance gate

The complete matrix remains `08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md`. The following scenarios are especially important after the latest hardening.

### Lifecycle and recovery

- single-player annual lifecycle;
- listen-host annual lifecycle;
- dedicated server with multiple clients;
- first enable before and inside the current event window;
- late join in Marked and Active;
- disconnect/reconnect in Active and Resolving;
- dedicated-server restart in Active and every persisted resolution step;
- world unload/reload in the same process, specifically checking duplicate RPC registration absence;
- disable during Forewarning/Marked without normal outcome or time advance;
- 04:15 / 05:45 / 06:00 transitions and input/fade release.

### Defeated and recovery

- direct enemy Defeated;
- fall/lava/drowning/environmental Defeated;
- immediate process termination after local defeat around the immediate profile-save request/server acknowledgement;
- reconnect while the durable Defeated marker exists;
- new enrollment clears the old durable marker;
- recovery on ground, swimming, attached/mounted and falling longer than 15 seconds;
- stage 2 duration and 0.25 incoming multiplier;
- protection expiry while terminal Defeated state remains;
- morning during recovery;
- GoalReached followed by Defeated or Withdrawn.

### Combat and Bloodlust

- melee, area, projectile, thrown weapon, AOE and combat summon paths;
- source owner and target owner on different peers;
- lethal hit reports the same player as the final positive HP loss;
- blocked/resisted/zero-damage hits do not steal death credit;
- reordered authorization/confirmation/death-report delivery;
- outgoing/incoming endpoint interpolation across progress;
- movement interpolation and field restoration across walk/run/crouch/swim;
- lifesteal only from matched authorized direct participant damage;
- rolling maximum-health-per-second lifesteal cap;
- no direct lifesteal from participant summons unless deliberately changed later;
- no building/tree/rock/tamed/NPC/boss/other-player Blood-source damage;
- forbidden character collider does not stop projectile;
- active world-geometry collision keeps vanilla physical lifecycle without world damage;
- stale projectile collision keeps permanent item respawn but blocks stale damage/credit/harmful spawned branches;
- temporary projectile item cannot materialize through hit or TTL respawn.

### Networking and spawning

- multiple zone owners in one combat group;
- ownership migration with in-flight leases and reports;
- lease expiry under normal and real-time-calendar worlds;
- OS clock adjustment while leases are active;
- mixed surface/interior participants in one map zone;
- authored interior `CreatureSpawner` candidates;
- no interior candidate;
- configured extra prefab changed during an event and before restart;
- completely lost spawn report followed by resolution cleanup;
- forged/invalid marked non-extra preserved by cleanup;
- stale extra from a previous event after a later event state exists.

### Bosses and offerings

- persistent outdoor vanilla boss parking/restoration;
- owner migration and listen-host parking;
- multiple parked bosses;
- restart while parked;
- interior boss withdrawal;
- nonpersistent surface boss withdrawal;
- surface/dungeon navigation contexts remain separated;
- item-producing OfferingBowl remains usable according to vanilla rules;
- boss OfferingBowl immediately before and after 18:00;
- request accepted by the server in Forewarning but delivered to the bowl owner after Marked still completes;
- request first received by the server after Marked is blocked without consuming inventory or altar items;
- vanilla `DelayedSpawnBoss` accepted before Marked is not cancelled after the transition.

The authoritative cutover is intentionally server receipt: a local click that has not reached the server before Marked is not considered a queued pre-Marked boss spawn.

### Blood Craft

- ordinary source recipe remains available;
- temporary creation and upgrade are material-free;
- source crafting/repair station type is required;
- vanilla station-level quality progression is preserved;
- `m_requireOnlyOneIngredient` source recipe does not fail before craft execution;
- temporary-upgrade requirement display is free while station/quality feedback remains correct;
- permanent item upgrade is not free;
- multi-craft near stack limits;
- temporary/permanent/different-owner/different-event stack isolation;
- ordinary inventory drop is blocked for temporary items;
- temporary thrown weapon can perform its combat action but cannot become a world item afterward;
- `ItemStand`, `ArmorStand`, `Fermenter`, `CookingStation`, `Smelter`, `Turret` and `Catapult` reject temporary Blood Craft items;
- reconnect/restart with valid temporary inventory;
- stale temporary inventory from another world/event/owner;
- temporary summon cleanup;
- third-party inventory/equipment compatibility where available.

### Skills and outcomes

- live x3 budget and +10 bonus cap;
- reconnect baseline before further skill reporting;
- completion +25 budget / top five / +10 per-skill cap;
- monotonic interrupted reward replay;
- offline outcome delivery after reconnect and after a later annual event;
- forced outcome-store write failure leaves resolution in `PublishingOutcomes` until persistence succeeds;
- immediate DreamText at current resolution, not next sleep;
- delayed outcome delivery under simulated latency does not release input before presentation finishes;
- presentation start/completion timeout fail-safe;
- next ordinary sleep remains vanilla after the outcome;
- local profile persistence acknowledgement;
- cloud profile fresh-process acknowledgement;
- cloud save failure retains the server queue entry;
- repeated delivery remains idempotent while durable acknowledgement is pending.

### Presentation and systems

- forewarning/Marked/Active visuals across representative biomes/interiors;
- cloned Fader clouds across environment switching;
- force-environment lease conflict behavior;
- random-event suppression/release;
- sleep blocking/release;
- Blood Moon and recovery status UI;
- resolution fade and DreamText input guards;
- real-time-calendar event resolution does not alter Valheim net time.

## Known non-blocking limitations

- Blood Moon music assets are not supplied; music work remains paused.
- Runtime Blood Moon wording is English-first until owner playtest stabilizes it.
- Automatic enemy-pool inference from raids/keys/trophies/achievements remains the future contract documented in `06_FUTURE_PROGRESSION_AND_REWARDS.md`; the current implementation freezes the explicit configured bootstrap prefab per event.
- Third-party custom world sinks or inventory systems that bypass vanilla boundaries may require compatibility patches after a concrete runtime report.
- Exact parked-boss animation/target/coroutine/HUD runtime state is intentionally not reconstructed.

## Final repository checks

Before owner-side playtest handoff, verify all of the following:

1. the final documentation-inclusive Codex review has no unresolved confirmed finding;
2. all inline review threads are resolved;
3. PR #42 remains draft/open/unmerged;
4. `master...feat/blood-moon` contains no unintended release-metadata changes;
5. new source code, comments, logs and English technical documentation contain no accidental Cyrillic;
6. no assistant-side Valheim build/runtime result is claimed;
7. any remaining runtime-only uncertainty is explicitly documented.

## Continuation rule

Any runtime defect found during owner-side validation must be recorded with reproduction, decision and fix before the next implementation step. Do not merge PR #42 or change release metadata until the owner explicitly requests that work.
