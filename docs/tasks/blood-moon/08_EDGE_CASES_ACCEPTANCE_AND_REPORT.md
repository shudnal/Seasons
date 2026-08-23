# Blood Moon — validation, edge cases and reporting

Part of `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

> Current acceptance target is the isolated runtime spike in file `10`, not production Blood Moon.

# 26. Mandatory spike scenarios

## 26.1. Modes

- single-player;
- listen server;
- dedicated server with participant and observer;
- late join;
- owner migration;
- owner disconnect;
- stale/duplicate RPC.

## 26.2. Layer matrix

- participant AwaitingContact sees ordinary + blood;
- participant Fighting sees blood but not ordinary creatures/tamed/boss;
- observer sees ordinary + participant but not blood enemy;
- participant/observer can each own ordinary or blood entity;
- hidden owner entity continues required remote simulation;
- no root GameObject disable;
- render/audio/HUD/collision restore cleanly after transition.

## 26.3. Hit transparency

- hidden ordinary enemy between participant and blood target;
- hidden blood enemy between observer and ordinary target;
- melee;
- arrow/bolt;
- thrown projectile;
- event projectile;
- AoE;
- status/stagger/skill-credit rejection for incompatible target;
- delayed hit after source state/owner change.

## 26.4. First contact

- incoming hit;
- outgoing melee/projectile;
- block;
- parry;
- fully mitigated hit;
- lethal first hit;
- miss/aggro/near projectile do not count;
- local transition before same hit resolution;
- server reject/resync;
- exactly once.

## 26.5. Dream collapse

- `Character.CheckDeath` intercepts health <=0;
- `Player.OnDeath` not called;
- no death point, effects, ragdoll, TombStone, food clear or respawn;
- health/stamina/eitr full;
- food/adrenaline unchanged;
- damaging DoT cleanup only;
- outcome `Defeated` exactly once;
- no re-entry;
- airborne landing;
- fall after blood knockback;
- swimming/attached stabilization;
- 10 seconds 75% reduction after stabilization;
- lava behavior after grace;
- direct forced `Player.OnDeath` remains vanilla.

## 26.6. Contexts

- outdoor ground;
- interior/dungeon atmosphere without enemies/layer;
- ship/ocean atmosphere without enemies/layer;
- generic attached first hit without detach;
- mounted bridge hit on rider and mount;
- voluntary dismount;
- visible boss HUD deferral and later entry;
- portal/teleport;
- edge-of-world preventive exit;
- Fighting Player entering unsupported context.

## 26.7. Ordinary simulation

- background simulation baseline;
- optional AI suspension when owner is participant and no real-world witness;
- AwaitingContact/Ejected as witnesses;
- observer enters/leaves;
- tamed/flying/swimming enemy;
- bridged mount excluded;
- resume correctness;
- CPU/network comparison.

---

# 27. Spike acceptance criteria

The spike is complete only when the report answers with runtime evidence:

1. Can local presentation be hidden independently of ZDO ownership?
2. Can a hidden owner entity still simulate correctly for another peer?
3. Can body/hitbox/projectile/AoE incompatibility be made transparent without global layer changes?
4. Can accepted first contact switch the Player before the same hit is processed?
5. Can the server validate/deduplicate provisional local contact?
6. Can `Character.CheckDeath` produce `Defeated` without any `Player.OnDeath` side effect?
7. Can damaging DoTs be cleared without removing buffs/food effects?
8. Can recovery protection terminate correctly on ground, water and attached states?
9. Can the mounted bridge work without forced dismount/transform change?
10. Can boss/interior/ship contexts defer engagement without breaking their systems?
11. Can preventive world-edge exit happen before edge death?
12. Is background simulation acceptable?
13. If not, is conditional AI suspension both necessary and safe?
14. What exact minimal Harmony points are needed?
15. Which desired behaviors are too fragile and should be simplified or dropped?

Failure of a candidate is a valid spike result if evidence and fallback are documented.

---

# 28. Spike branch and PR

Work only in:

```text
spike/blood-moon-parallel-layer
```

Base: current `feat/blood-moon`.

Open draft PR:

```text
spike/blood-moon-parallel-layer → feat/blood-moon
```

Do not open/merge into `master`. Do not version-bump or edit release files.

Run Codex code review on the spike PR. Fix correctness issues that invalidate experiments; do not polish experimental code into production prematurely.

---

# 29. Required report

Commit a report containing:

- exact Seasons base commit;
- exact `assemblies_combined` commit;
- test environment and player count;
- commands/config used;
- patches and alternatives tried;
- owner matrix;
- result for every scenario above;
- logs/screenshots where useful;
- CPU/network observations;
- confirmed production invariants;
- rejected approaches and reasons;
- fallback decisions;
- recommended production architecture;
- exact updates required in docs `01`–`09`;
- whether each spike file should be deleted, kept as a diagnostic, or promoted selectively.

Do not claim multiplayer, VFX or physics success without a real-game test.

---

# 30. Production gate after spike

Before production implementation, owner must explicitly accept:

- local visibility/collision technique;
- hit-transparency technique;
- exact first-contact patch points;
- dream-collapse DoT classifier;
- stabilization rule;
- mounted policy;
- boss-context synchronization;
- world-edge withdrawal outcome;
- unsupported-context-after-contact policy;
- background simulation or conditional suspension;
- ordinary interaction restrictions;
- projectile/AoE/summon attribution.

Then replace the spike task with a new production task and start the first playable slice. Do not infer approval from a partially successful experiment.
