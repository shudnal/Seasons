# Blood Moon — edge cases, acceptance and report

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

# 35. Обязательные edge cases

## Lifecycle/network

- disabled;
- single/listen/dedicated;
- first enable before/inside event window;
- restart in Marked/Active/Resolving;
- time jump across phases;
- late join/disconnect/reconnect;
- stale RPC/eventId;
- duplicate enemy death report;
- cleanup twice;
- config disable mid-event;
- recovery protection overlapping morning resolution;
- disconnect/reconnect during recovery protection.

## Visual/system

- every forewarning night has visible non-text effect;
- ordinary RandEvent active at 18:00;
- new RandEvent blocked;
- boss forced event remains coherent before parking and clears after parking;
- force env lease conflict;
- missing `Fader`/`Ashlands_FaderFX`;
- world unload every phase.

## Existing monsters

- hostile ground MonsterAI included;
- passive animal excluded by `BaseAI.IsEnemy`;
- tamed excluded;
- boss excluded;
- neutral Dvergr excluded, aggravated hostile Dvergr included;
- `Players`/`PlayerSpawned`/`TrainingDummy` excluded;
- trader/non-MonsterAI NPC excluded;
- unique/starred hostile monster included;
- hostile modded MonsterAI included by generic predicate;
- newly loaded/spawned ordinary monster during Active;
- owner migration/disconnect;
- survivor after event;
- killed existing monster keeps ordinary loot/ragdoll;
- no permanent max-health/shared-prefab/ZDO hunt mutation;
- only event VFX/transient caches require cleanup.

## Additional spawned enemies

- marker written before combat participation;
- no ordinary loot;
- fast ragdoll;
- surviving ZDO deleted at resolution;
- stale marked ZDO deleted on recovery;
- cleanup iterates a copy safely;
- no accidental deletion of ordinary existing monster.

## Boss sacrifice

- inventory offering blocked at/after 18:00;
- item-stand altar interaction blocked;
- `RPC_SpawnBoss` race guarded;
- non-boss OfferingBowl item rewards still work;
- already placed attachments remain;
- pre-18 queued spawn does not lose offerings;
- queued persistent outdoor boss is parked if it appears during Active;
- queued unparkable boss causes affected encounter Player to become `Withdrawn`, not item loss.

## Boss parking

- vanilla persistent outdoor boss active at 23:00;
- multiple bosses and deterministic separate parking slots;
- boss owner server/client/other peer;
- server ownership handoff and old-owner transform race;
- live server instance transform and ZDO position stay consistent;
- far XZ/normal Y does not trigger out-of-world rescue;
- clients unload instance;
- HUD/music/forced event clear naturally after unload;
- restart while parked;
- restore with no nearby Player;
- marker left after crash before/after move;
- same boss ZDO returns to original position;
- recreated instance is valid when arena loads;
- no boss death/loot/defeat key during parking;
- dead/destroying boss not parked;
- boss loaded after Active begins;
- interior boss not parked → affected Player `Withdrawn`;
- nonpersistent boss not parked → affected Player `Withdrawn`;
- no requirement to preserve animation, target, velocity, coroutine or client-only state;
- lingering boss projectile/AOE/summon is not explicitly cleaned and completes its own lifecycle.

## Defeated/recovery

- direct Blood hit;
- fall after knockback;
- environmental health loss;
- lava;
- water/drowning;
- already grounded;
- airborne >15 sec;
- swimming;
- mounted/attached;
- `SE_Burning`, `SE_Poison`, `SE_Smoke`;
- negative-tick `SE_Stats`;
- unknown modded DoT retained;
- no `Player.OnDeath`, TombStone, death point, food clear or respawn;
- health/stamina/eitr max;
- food/adrenaline unchanged;
- stage1 ≤15 sec;
- stage2 exactly 10 sec at 0.25 incoming multiplier;
- direct forced `Player.OnDeath` remains vanilla;
- Player reaches 100%, then is defeated: full Success/reward policy is explicitly verified after final decision.

## Context

- outdoor;
- ship/ocean with no valid land spawn;
- sea monster dynamic Blood behavior;
- normal interior with existing monsters;
- interior extra spawn from loaded `CreatureSpawner` point;
- no suitable/pathable interior spawner → no extra spawn;
- active interior boss → affected Player `Withdrawn`;
- active nonpersistent/unparkable boss → affected Player `Withdrawn`;
- teleport in progress/destination;
- mounted;
- generic attached;
- edge safety offset → `Withdrawn`;
- Exited Player body collision remains vanilla;
- Exited Player cannot damage or be targeted by Blood enemies.

## Damage routing

- Blood enemy ignores building/tamed/NPC/boss/other monster;
- Player attack damages only Blood enemy;
- no building/tree/ore/crop damage;
- ordinary trap/turret cannot farm Blood enemy;
- projectile/AOE delayed attribution;
- no status/stagger/skill credit on forbidden target;
- GoalReached aggro fallback;
- lingering parked-boss attacks are not deleted and do not become Blood sources.

## Live config

- multiplier change affects next hit;
- AI speed/aggression change affects next update;
- caps and intervals update without restart;
- lowering cap does not delete live extras;
- group distance change triggers recompute;
- schedule/calendar of current event remains frozen.

## Blood Craft future

- custom tabs untouched;
- recipe clones not leaked;
- upgrade permanent item not free;
- temporary/permanent stack never merges;
- drop destroyed;
- TombStone fallback;
- personal cleanup on Defeated/Withdrawn;
- consumed food effect remains;
- custom equipment slots do not retain temporary item;
- previous ordinary equipment is not auto-restored.

# 36. Runtime-spike acceptance

`10_GLOBAL_EVENT_RUNTIME_SPIKE.md` должен ответить:

1. Работает ли generic eligibility predicate без prefab allowlist.
2. Можно ли реализовать dynamic Blood behavior без persistent instance/ZDO mutation.
3. Надёжно ли различаются ordinary existing и marked extra spawns.
4. Работает ли centralized target/damage routing для melee/projectile/AOE.
5. Работает ли CheckDeath-based `Defeated` без vanilla death side effects.
6. Корректны ли 15s stabilization cap + 10s 75% protection.
7. Поддерживаются ли mounted/attached/ship/ocean/interior без forced movement.
8. Достаточны ли loaded `CreatureSpawner` positions для optional interior extras.
9. Не теряет ли OfferingBowl items/attachments при block и queued-spawn race.
10. Надёжен ли far-sector parking persistent outdoor boss при owner migration/restart.
11. Надёжно ли определяется interior/nonpersistent/unparkable boss и применяется `Withdrawn` только к affected Player.
12. Каков минимальный production patch set и CCS/RPC split.

# 37. Production acceptance

После снятия gate первый vertical slice готов только если:

- state machines/server authority/persistence работают;
- no personal layer code;
- no vanilla SoftDeath status;
- custom status честно описывает `Defeated`;
- event can be debug-run end-to-end;
- visible forewarning;
- Marked 18:00, Active 23:00, end 05:45;
- OfferingBowl boss summon block from 18:00;
- RandEvent blocked/restored;
- environment/VFX cleanup;
- generic direct existing-monster rules;
- extra-spawn markers and cleanup;
- target/damage matrix centralized;
- existing loot preserved; extra loot suppressed;
- progress exactly once;
- GoalReached behavior and post-goal exit semantics;
- Defeated dream collapse;
- Withdrawn edge/boss behavior;
- persistent outdoor boss parking/restore;
- nonpersistent/interior boss fallback documented;
- no Player transform changes;
- no material/world-state event rewards beyond ordinary loot of existing creatures;
- live balance configs work with documented semantics;
- build result and manual multiplayer checklist documented;
- draft PR + Codex review;
- PR not merged.

# 38. Report

Codex указывает:

- commits/files/architecture;
- CCS/RPC decision;
- exact `assemblies_combined` commit;
- build result;
- runtime scenarios actually tested;
- untested multiplayer/VFX clearly stated;
- eligibility and interior-spawn findings;
- boss parking ownership/persistence/restart evidence;
- known limitations/fallbacks;
- live config behavior;
- draft PR/review result;
- next continuation point.
