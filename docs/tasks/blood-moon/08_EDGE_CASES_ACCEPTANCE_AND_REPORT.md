# Blood Moon — edge cases, acceptance and report

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

# 33. Обязательные edge cases

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
- config disable mid-event.

## Visual/system

- every forewarning night has visible non-text effect;
- ordinary RandEvent active at 18:00;
- new RandEvent blocked;
- boss forced event remains coherent before parking and clears after parking;
- force env lease conflict;
- missing `Fader`/`Ashlands_FaderFX`;
- world unload every phase.

## Existing monsters

- eligible hostile ground MonsterAI;
- passive animal;
- tamed;
- boss;
- neutral and aggravated Dvergr;
- trader/named NPC;
- `PlayerSpawned` summon;
- starred monster;
- modded MonsterAI;
- newly loaded/spawned ordinary monster during Active;
- owner migration/disconnect;
- survivor after event;
- killed existing monster keeps ordinary loot/ragdoll;
- no permanent max-health/shared-prefab mutation;
- event-created target/hunt/VFX state does not remain stale.

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
- pre-18 queued spawn does not lose offerings and is parked if Active.

## Boss parking

- vanilla persistent outdoor boss active at 23:00;
- multiple bosses;
- boss owner server/client/other peer;
- owner transfer and old-owner transform race;
- live server instance transform and ZDO position stay consistent;
- far XZ/normal Y does not trigger out-of-world rescue;
- clients unload instance;
- HUD/music/forced event clear;
- boss projectile/AOE in flight;
- boss summons/minions;
- restart while parked;
- restore with no nearby Player;
- marker left after crash before/after move;
- restore position/rotation/health/level;
- dead/destroying boss not parked;
- modded persistent boss;
- nonpersistent boss fallback;
- interior/Queen boss excluded;
- boss loaded after Active begins;
- optional recreation/appearance effect.

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
- direct forced `Player.OnDeath` remains vanilla.

## Context

- outdoor;
- ship/ocean with no valid land spawn;
- sea monster conversion;
- normal interior with existing monsters;
- interior custom navmesh spawn;
- active unparked interior boss;
- teleport in progress/destination;
- mounted;
- generic attached;
- edge safety offset → Withdrawn;
- Exited Player body collision remains vanilla;
- Exited Player cannot damage or be targeted by Blood enemies.

## Damage routing

- Blood enemy ignores building/tamed/NPC/boss/other monster;
- Player attack damages only Blood enemy;
- no building/tree/ore/crop damage;
- ordinary trap/turret cannot farm Blood enemy;
- projectile/AOE delayed attribution;
- no status/stagger/skill credit on forbidden target;
- GoalReached aggro fallback.

## Blood Craft future

- custom tabs untouched;
- recipe clones not leaked;
- upgrade permanent item not free;
- temporary/permanent stack never merges;
- drop destroyed;
- TombStone fallback;
- personal cleanup on Defeated/Withdrawn;
- consumed food effect remains;
- custom equipment slots do not retain temporary item.

# 34. Runtime-spike acceptance

`10_GLOBAL_EVENT_RUNTIME_SPIKE.md` должен ответить:

1. Точный eligibility predicate обычных монстров.
2. Можно ли реализовать dynamic direct conversion без persistent instance mutation.
3. Надёжно ли различаются ordinary existing и marked extra spawns.
4. Работает ли centralized target/damage routing.
5. Работает ли CheckDeath-based Defeated без vanilla death side effects.
6. Корректны ли 15s stabilization cap + 10s 75% protection.
7. Поддерживаются ли mounted/attached/ship/ocean/interior без forced movement.
8. Как спавнить extras в interior через navmesh/path validation.
9. Надёжен ли far-sector boss parking для vanilla persistent boss.
10. Какой fallback нужен interior/nonpersistent/modded boss.
11. Как очищать lingering boss attacks/summons.
12. Каков минимальный production patch set.

# 35. Production acceptance

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
- direct existing-monster rules;
- extra-spawn markers and cleanup;
- target/damage matrix centralized;
- existing loot preserved; extra loot suppressed;
- progress exactly once;
- GoalReached behavior;
- Defeated dream collapse;
- Withdrawn edge behavior;
- boss parking/restore and documented fallback;
- no Player transform changes;
- no material/world-state event rewards beyond ordinary loot of existing creatures;
- build result and manual multiplayer checklist documented;
- draft PR + Codex review;
- PR not merged.

# 36. Report

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
- draft PR/review result;
- next continuation point.
