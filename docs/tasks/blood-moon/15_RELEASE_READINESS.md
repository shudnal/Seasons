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

The latest static hardening includes:

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
- preserved vanilla projectile collision lifecycle for both active and stale Blood attribution;
- actual-HP-loss-based death credit and Bloodlust lifesteal;
- server-controlled Bloodlust damage/movement/lifesteal endpoints with rolling healing cap;
- durable local Defeated terminal state and reconnect report retry;
- source-recipe-bound Blood Craft with source station/quality semantics;
- Blood Craft preflight support for `m_requireOnlyOneIngredient`;
- verified vanilla Blood Craft world-sink signatures;
- persistent outdoor boss parking/restoration and navigation-context withdrawal;
- live/completion skill reward accounting;
- independent durable per-player outcome queue;
- immediate current-resolution native DreamText presentation;
- bounded Dream presentation start/completion handshake separate from durable profile acknowledgement;
- consolidated production resolution guards instead of corrective self-patches;
- routed-RPC registration lifetime protection across world teardown.

## Routed-RPC teardown invariant

Current Valheim `ZRoutedRpc.Register` stores method handlers with `Dictionary.Add`. `ZNet.OnDestroy` does not synchronously discard the current `ZRoutedRpc` method table.

Therefore Blood Moon subsystem runtime reset must not clear its cached transport reference while that routed-RPC object may still be alive. The outcome queue and outcome-presentation handshake now clear only their event/store state during teardown. A new network session is detected by object-reference change and registers handlers once on the new routed-RPC instance.

A fresh full-diff audit found no remaining `registeredRpc = null` reset in the Blood Moon changes.

## Exact static source checks in the latest pass

The latest hardening re-read the current Valheim source for:

- Player/Character death semantics and `CheckDeath` ordering;
- `Character.RPC_Damage` and actual health loss;
- `Projectile.OnHit`, TTL, skill/adrenaline and spawn-on-hit behavior;
- current vanilla `IDestructible.Damage(HitData)` implementations;
- movement calculations used for temporary Bloodlust movement modifiers;
- `Recipe.GetAmount`;
- `InventoryGui.OnCraftPressed`, `SetupRequirementList`, `HideRequirement` and `DoCrafting`;
- `OfferingBowl` inventory/item-stand/RPC/delayed-spawn flow;
- `ZRoutedRpc.Register` and `ZNet.OnDestroy` transport lifetime;
- Blood Craft world-sink signatures for `ItemStand`, `ArmorStand`, `Fermenter`, `CookingStation`, `Smelter`, `Turret` and `Catapult`;
- existing network/ZDO/spawn/profile-persistence APIs documented by the earlier review trail.

A compile-oriented source audit also removed direct relational operators from enum comparisons and rechecked current private Harmony targets. This is not a build claim.

## Final Codex gate

The earlier Codex request for head `a2728234234308c03045ac7f0cc298382094d10c` is superseded because later runtime-code fixes changed the branch.

After this documentation commit, request Codex review on the new exact `feat/blood-moon` head. The final review is accepted only if:

1. the exact reviewed SHA equals the current PR head;
2. there is no unresolved confirmed finding;
3. all inline review threads are resolved;
4. the result is recorded in the PR timeline without another documentation-only commit.

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
- immediate process termination after local defeat before server acknowledgement;
- reconnect while the durable Defeated marker exists;
- recovery on ground, swimming, attached/mounted and falling longer than 15 seconds;
- stage 2 duration and 0.25 incoming multiplier;
- protection expiry while terminal Defeated state remains;
- morning during recovery;
- GoalReached followed by Defeated or Withdrawn.

### Combat and Bloodlust

- melee, area, projectile, thrown weapon, AOE and combat summon paths;
- blocked/resisted/zero-damage hits do not steal death credit;
- actual damaging hit updates credit;
- outgoing/incoming endpoint interpolation across progress;
- movement interpolation and field restoration across walk/run/crouch/swim;
- lifesteal from actual enemy HP loss;
- rolling maximum-health-per-second lifesteal cap;
- no direct lifesteal from participant summons unless deliberately changed later;
- no building/tree/rock/tamed/NPC/boss/other-player Blood-source damage;
- stale projectiles collide and expire without damage/credit/persistent spawn effects.

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

### Bosses

- persistent outdoor vanilla boss parking/restoration;
- owner migration and listen-host parking;
- multiple parked bosses;
- restart while parked;
- interior boss withdrawal;
- nonpersistent surface boss withdrawal;
- surface/dungeon navigation contexts remain separated;
- OfferingBowl immediately before and after 18:00;
- specifically test an interaction initiated just before Marked whose bowl-owner RPC is delivered after Marked.

The last scenario is an explicit runtime boundary. Vanilla source confirms resources are consumed only after owner-side spawn-RPC acceptance and an already accepted `DelayedSpawnBoss` is not cancelled. The current authoritative guard rejects an RPC that actually arrives after the Marked block is active; no speculative preauthorization channel was added without evidence that this sub-second network race matters in real play.

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
- The OfferingBowl request-in-flight boundary described above remains a targeted multiplayer acceptance item rather than a proven blocker.

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
