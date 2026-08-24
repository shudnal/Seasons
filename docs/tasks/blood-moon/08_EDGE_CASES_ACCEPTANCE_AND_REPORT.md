# Blood Moon — edge cases, acceptance and report

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

# 31. Обязательные edge cases

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
- current RandEvent at 18:00;
- new RandEvent blocked;
- force env lease conflict;
- missing `Fader`/`Ashlands_FaderFX`;
- world unload every phase.

## Existing monsters

- direct conversion candidate;
- suspension+clone candidate;
- starred monster;
- modded MonsterAI;
- neutral Dvergr/NPC excluded;
- tamed excluded;
- PlayerSpawned summon excluded;
- new ordinary spawn during event;
- owner migration/disconnect;
- surviving converted monster cleanup;
- killed converted monster world-state consequence;
- nonpersistent original unload/restart.

## Boss

- vanilla boss active at 23:00;
- multiple bosses;
- boss owner participant/server/other peer;
- boss projectile/AOE in flight;
- boss summon/minion;
- boss event/environment/music/HUD;
- new summoning attempt during event;
- restart while suspended/parked;
- restore with no nearby Player;
- modded/nonpersistent boss;
- restore health/level/position/rotation exactly where required.

## Defeated/recovery

- direct blood hit;
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

- interior/dungeon at 23:00;
- ship/ocean at 23:00;
- Deferred exits to land;
- Fighting enters dungeon/ship;
- teleport in progress;
- mounted;
- generic attached;
- edge safety offset → Withdrawn;
- Defeated/Withdrawn cannot damage or be targeted by Blood enemies.

## Damage routing

- Blood enemy ignores building/tamed/NPC/boss/ordinary monster;
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
- consumed food effect remains.

# 32. Runtime-spike acceptance

`10_GLOBAL_EVENT_RUNTIME_SPIKE.md` должен ответить:

1. Direct conversion или suspension+clone?
2. Какой точный eligibility predicate безопасен?
3. Можно ли globally suspend/restore ordinary Character без owner changes?
4. Можно ли safely suspend active boss in place?
5. Если нет — надёжен ли ZDO parking?
6. Сохраняются ли boss health/state/restart recovery?
7. Работает ли CheckDeath-based Defeated без vanilla death side effects?
8. Корректны ли 15s stabilization cap + 10s 75% protection?
9. Работает ли event target/damage routing для melee/projectile/AOE?
10. Поддерживаются ли mounted/attached без forced detach?
11. Как вести Fighting→unsupported context?
12. Каков минимальный production patch set?

# 33. Production acceptance

После снятия gate первый vertical slice готов только если:

- state machines/server authority/persistence работают;
- no personal layer code;
- no vanilla SoftDeath status;
- custom status честно описывает safe defeat only when active;
- event can be debug-run end-to-end;
- visible forewarning;
- Marked 18:00, Active 23:00, end 05:45;
- RandEvent blocked/restored;
- environment/VFX cleanup;
- existing monster strategy реализована согласно spike;
- additional spawn caps;
- target/damage matrix centralized;
- no loot/long ragdoll;
- progress exactly once;
- GoalReached behavior;
- Defeated dream collapse;
- Withdrawn edge behavior;
- boss suspend/restore;
- no Player transform changes;
- no material/world-state rewards;
- build result and manual multiplayer checklist documented;
- draft PR + Codex review;
- PR not merged.

# 34. Report

Codex должен указать:

- commits and files;
- architecture;
- CCS/RPC decision;
- exact `assemblies_combined` commit;
- build result;
- runtime scenarios actually tested;
- untested multiplayer/VFX clearly stated;
- boss and ordinary-monster strategy evidence;
- known limitations;
- draft PR/review result;
- next continuation point.
