# Blood Moon — edge cases, acceptance and report

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

> **Текущий этап:** preimplementation design и technical spikes. Production-код первого вертикального среза не начинать, пока владелец мода явно не закроет gate из `09_PREIMPLEMENTATION_DECISIONS_AND_SPIKES.md`.

# 23. Preimplementation edge cases

До снятия gate необходимо проверить и зафиксировать решения минимум для следующих сценариев.

## 23.1. First contact

- incoming accepted hit от blood enemy;
- outgoing melee hit по blood enemy;
- projectile hit;
- block;
- parry;
- полностью resisted/immune hit;
- miss и near-projectile notification не считаются contact;
- lethal first hit;
- два одновременных first-contact report;
- owner migration между contact и server validation;
- stale contact report предыдущего event ID;
- late join сразу рядом с blood enemy;
- `AwaitingContact` Player достиг forced end, не вступив в бой.

## 23.2. Dream collapse

- прямой lethal hit blood enemy;
- fall damage после blood knockback;
- delayed projectile/AOE;
- poison/burning/status tick;
- drowning;
- lava;
- smoke;
- structural/cart/tree/environmental damage;
- `EdgeOfWorld`;
- admin/scripted kill;
- lethal hit одновременно с достижением 100%;
- lethal hit во время PrepareResolution;
- repeated `Character.CheckDeath` вызов до server acknowledgement;
- no `Player.OnDeath`;
- no death point;
- no death effects/ragdoll;
- no TombStone;
- no inventory/equipment/food changes;
- no respawn request;
- health restoration;
- server exactly-once `Death/Ejected` outcome;
- death-specific DreamText при общем resolution.

## 23.3. Ejection без transform changes

- Player на устойчивой земле;
- Player падает после knockback;
- Player плавает;
- Player в lava;
- Player на движущемся корабле;
- Player attached к ship controls;
- Player на mount;
- Player в dungeon/interior;
- Player в portal/teleport transition;
- ordinary enemy collider возвращается в той же точке;
- ordinary projectile/AOE уже летит в точку Player;
- transition grace покрывает landing, но имеет hard cap;
- velocity, position и rotation не задаются модом;
- grace не создаёт бесконечную неуязвимость.

## 23.4. Ownership и local layer

Проверить все комбинации:

1. blood enemy owned blood participant;
2. blood enemy owned real-world nonparticipant;
3. ordinary enemy owned blood participant;
4. ordinary enemy owned real-world nonparticipant;
5. ordinary tamed owned blood participant;
6. ownerless persistent ZDO автоматически получает owner через vanilla `ReleaseNearbyZDOS`;
7. прямой transfer ordinary entity от participant к nonparticipant;
8. owner disconnect;
9. late join рядом с entities;
10. split/merge групп меняет eligible peers;
11. server/listen-host как owner;
12. dedicated server без graphics/local Player.

Локальное скрытие не должно отключать root GameObject/AI владельца и ломать симуляцию для других peers.

## 23.5. Ordinary-world simulation policy

Сравнить:

- Policy A: background vanilla simulation;
- Policy B: conditional AI suspension на participant-owner при отсутствии real-world observer;
- прямую передачу ownership eligible nonparticipant без owner=0 interval.

Для Policy B проверить:

- ground melee monster;
- flying monster;
- swimming monster;
- tamed AI;
- regeneration/world-time update;
- movement/physics после resume;
- target state после resume;
- procreation/consume/follow/saddle paths;
- nonparticipant enters/leaves active area;
- CPU/network profile;
- cleanup при exception/world unload.

## 23.6. Visibility, collision и attacks

- renderer/LOD hidden;
- audio hidden;
- EnemyHud/name hidden;
- local Player не сталкивается с hidden ordinary/blood entity;
- entity всё ещё collides с terrain/world geometry;
- melee hit filtering;
- arrow/bolt;
- thrown weapon;
- bomb;
- persistent AOE;
- summon;
- projectile owner migration;
- delayed hit после ejection;
- nonparticipant видит participant, сражающегося с воздухом;
- event enemy owned nonparticipant продолжает атаковать remote participant;
- no stale collider/renderer after layer transition.

## 23.7. Context policy

Для каждого context выбрать `full`, `context-specific`, `AwaitingContact without forced engagement` или `safe skip`:

- outdoor ground;
- dungeon/interior;
- ship/ocean;
- mounted/attached;
- swimming;
- falling/flying;
- active boss encounter;
- portal/teleport;
- late join;
- empty server until 05:45.

## 23.8. DOT/status policy

- первый enemy не использует persistent DOT/status;
- pre-existing real-world Poison/Burning не удаляется глобально;
- blood-origin status attribution после owner/source death;
- ejection cleanup не стирает unrelated effect;
- future blood-specific status clone либо source-aware tracking рассматриваются отдельно.

## 23.9. Real-world interactions

До production implementation определить:

- ordinary ItemDrop visibility/pickup;
- containers и StackAll;
- ship/mount controls;
- crafting stations;
- doors;
- building/placement/terrain tools;
- trader/NPC;
- portals;
- traps/turrets;
- harvesting/mining/chopping;
- interaction с tamed;
- поведение в Marked, AwaitingContact, Fighting, GoalReached и Ejected.

---
# 24. Gate для начала production implementation

До отдельного решения владельца должны быть закрыты и записаны:

1. first-contact contract;
2. dream-collapse lethal scope;
3. health/stamina/eitr после collapse;
4. transition-grace rule и deny-list;
5. re-entry policy первой версии;
6. ordinary-world simulation policy;
7. ownership-transfer policy;
8. local visibility/collision mechanism;
9. context policy для dungeon и ship/ocean;
10. boss-overlap policy;
11. DOT/status policy;
12. допустимые real-world interactions;
13. обработка summons/projectiles/AOE;
14. поведение ordinary threats immediately after ejection.

До закрытия gate:

- не создавать production Blood Moon classes/patches;
- не открывать implementation PR;
- не менять plugin version/release docs;
- разрешены только docs, чтение `assemblies_combined` и отдельно согласованные spike commits.

---
# 25. Acceptance criteria technical spikes

Preimplementation считается достаточным для старта production-кода, когда:

1. `Character.CheckDeath` spike подтверждает или опровергает безопасный owner-side collapse до `Player.OnDeath`.
2. Подтверждено отсутствие TombStone/death point/respawn/inventory loss при collapse.
3. First contact корректно работает для incoming, outgoing и block/parry по утверждённому контракту.
4. Collapse точно один раз фиксируется сервером и не зависит от duplicate RPC.
5. Local layer isolation проверена минимум на двух клиентах при разных owner combinations.
6. Root GameObject/owner AI не выключается только из-за локальной невидимости.
7. Проверено, что `SetOwner(0)` переопределяется vanilla owner assignment и не используется как основной pause mechanism.
8. Выбрана Policy A или B ordinary-world simulation на основании runtime результата.
9. Direct owner transfer к eligible real-world peer проверен либо явно отложен с обоснованием.
10. Ejection grace не меняет transform, покрывает mid-air landing и имеет конечный cap.
11. Зафиксированы policies для land, dungeon, ship/ocean, attached/mount и portal transition.
12. Первый enemy prototype не требует нерешённого DOT attribution.
13. Все решения отражены в `01`–`09` документах без противоречий.
14. Владелец явно разрешил начало production implementation.

---
# 26. Будущие acceptance criteria первого playable slice

После снятия gate первый playable slice должен обеспечить:

1. Структурированные state machines без монолита в `Seasons.cs`.
2. Debug-команды позволяют прогнать событие без ожидания года.
3. Сервер авторитетно переводит событие через все фазы.
4. Выбранная CCS/RPC схема соответствует фактическим гарантиям CCS.
5. Late join получает самодостаточный snapshot.
6. First install внутри event window безопасно пропускает текущий год.
7. Restart восстанавливает либо безопасно разрешает событие.
8. Forewarning имеет видимый не текстовый эффект.
9. В 18:00 обычный RandEvent останавливается и подавляется.
10. С 18:00 до 23:00 текущая погода линейно краснеет.
11. В 23:00 включаются forced environment и `AwaitingContact`.
12. Первый accepted contact переводит Player в `Fighting`.
13. Поражение вызывает dream collapse/ejection без vanilla death flow и transform changes.
14. Skill loss невозможен; один `SoftDeath` status не считается достаточным доказательством.
15. Один event enemy появляется с group/server cap и ZDO marker.
16. Event enemy target/damage разрешён только blood-layer participants.
17. Buildings, crops, tamed, ordinary creatures и nonparticipants не повреждаются.
18. Target/damage rules централизованы.
19. Event enemy не оставляет loot/долгий ragdoll.
20. Kill credit exactly once меняет authoritative progress.
21. На 100% full buff остаётся.
22. Auto-complete не создаёт combat success/reward.
23. Все terminal outcomes дают early resolution.
24. В 05:45 происходит forced resolution.
25. Resolution имеет fade barrier/timeout, cleanup, time advance, DreamText и Rested reset.
26. Нет map markers.
27. Нет предметных/материальных/world-state rewards.
28. Нет version bump/release changes.
29. Все новые source files явно включены в `.csproj`.
30. Выполнены доступные Debug/Release builds.
31. Подготовлен manual multiplayer checklist.
32. Открыт draft PR, проведён Codex review, PR не слит.

---
# 27. Рекомендуемая последовательность после снятия gate

## Commit 1 — authoritative lifecycle

- configs;
- schedule/event ID;
- state records/transitions;
- CCS/RPC decision;
- persistence/recovery;
- `FirstBloodContactRecord`;
- centralized interaction policy;
- debug commands;
- localization tokens.

## Commit 2 — presentation and suppression

- forewarning visuals;
- Marked/AwaitingContact/Fighting status;
- real skill-loss protection;
- sleep/RandEvent suppression;
- environment overlay/force lease;
- cloud VFX;
- resolution fade/time/DreamText/Rested.

## Commit 3 — first combat loop

- groups;
- spawner;
- enemy marker;
- target/damage rules;
- no loot/ragdoll cleanup;
- owner reports/dedup;
- first contact/progress/100%/collapse/early completion.

## Commit 4 — validation fixes

- builds;
- edge cases;
- manual checklist;
- Codex-review fixes.

---
# 28. Итоговый отчёт Codex

После будущей реализации выдать:

- commits;
- architecture summary;
- CCS/RPC rationale;
- changed/new files;
- build results;
- static/automatic checks;
- manual runtime checklist;
- known limitations;
- draft PR;
- Codex review findings/fixes/rejections;
- точную следующую точку продолжения.

Не утверждать runtime multiplayer/VFX validation без реального запуска игры.
