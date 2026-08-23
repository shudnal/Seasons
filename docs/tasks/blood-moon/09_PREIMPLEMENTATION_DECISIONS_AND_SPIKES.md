# Blood Moon — preimplementation decisions and technical spikes

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

> **Статус:** production-разработку Blood Moon пока не начинать. Этот файл имеет приоритет для границы входа в параллельный слой, defeat flow, ownership, local visibility/collision и context policies. Сначала закрыть решения и провести узкие runtime spikes.

# 27. Уже принятые ограничения

## 27.1. Никаких перемещений Player

Blood Moon не меняет:

- position;
- rotation;
- parent/attach state;
- ship/mount position;
- velocity через принудительное восстановление;
- Y через raycast к земле.

Допустимы только status/effect/UI/fade/emote, изменение combat resources через штатные API и локальные interaction-layer rules.

`Character.m_lastGroundPoint` не является безопасным spawn anchor: это последняя contact point, она может быть устаревшей, относиться к движущемуся collider/rigidbody и не описывает валидную позицию capsule.

## 27.2. Positional backup удалён из дизайна

`CombatEntrySnapshot` как точка teleport/restore больше не нужен.

Разрешено сохранить только диагностический `FirstBloodContactRecord`:

```text
eventId
stable player ID
current peer/session UID
Player ZDOID
authoritative timestamp
contact kind
blood enemy ZDOID
player position только для диагностики/статистики
```

Position из записи никогда не применяется к transform.

## 27.3. Настоящий `Player.OnDeath` нежелателен

Предпочтительное поражение — **dream collapse** до vanilla death flow:

- no death point;
- no death effects/ragdoll;
- no TombStone;
- no inventory/equipment/food transfer;
- no respawn request;
- no transform changes;
- participant получает terminal defeat outcome и возвращается в real-world layer на том же месте.

Предпочтительная техническая точка spike — owner-side prefix `Character.CheckDeath`: current game вызывает `OnDeath()` после того, как health уже стал `<= 0`, но до `Player.OnDeath` ещё можно восстановить health и завершить иллюзорный бой.

## 27.4. `SoftDeath` — только понятная индикация

Простое добавление `SEMan.s_statusEffectSoftDeath` не гарантирует отсутствие skill loss. `Player.HardDeath()` определяется `m_timeSinceDeath`; vanilla добавляет SoftDeath status уже после того, как `HardDeath()` false.

Если какой-либо fallback всё ещё вызывает vanilla death, нужен отдельный узкий guard `HardDeath == false`. При dream collapse skill loss отсутствует потому, что `Player.OnDeath` не вызывается.

## 27.5. `SetOwner(0)` не является pause mechanism

Vanilla `ZDOMan.ReleaseZDOS` периодически вызывает `ReleaseNearbyZDOS` и снова назначает persistent ownerless ZDO ближайшему active peer. Поэтому owner=0:

- нестабилен;
- может сразу вернуть owner тому же participant;
- создаёт owner-revision churn;
- требует вмешательства в центральный owner-selection path;
- опасен для нестандартных nonpersistent entities и owner-targeted RPC.

Не строить parallel-world simulation на массовом снятии owner.

# 28. Граница входа в параллельный слой — главный открытый выбор

Нужно различать **видимость кровавых проявлений** и **фактическое выпадение из обычного мира**.

## Модель A — полный blood layer с 23:00

В 23:00 Player сразу:

- перестаёт видеть/interact с ordinary entities;
- видит blood enemies;
- может быть ими атакован;
- остаётся `AwaitingContact` только для progress/statistics.

Плюс: простая фаза.

Минус: Player на корабле, mount, в dungeon, boss fight или другой непредсказуемой ситуации принудительно теряет ordinary context ещё до собственного участия.

## Модель B — вторжение до контакта, layer switch на первом контакте

В 23:00 Player входит в `AwaitingContact`:

- ordinary world пока остаётся видимым и интерактивным;
- blood enemies становятся видимыми и могут искать Player;
- Player может атаковать blood enemy;
- blood enemy не взаимодействует с ordinary world;
- accepted first blood interaction атомарно переводит Player в `Fighting` и включает полный blood-only layer.

Первый blood hit должен переключить layer **до применения этого же hit**, чтобы defence modifier, lethal interception и routing уже действовали на первом контакте.

**Предварительная рекомендация: модель B.** Она точнее соответствует идее «контакт с иллюзорным миром», сохраняет agency и заметно уменьшает число forced context transitions. Определения `AwaitingContact` в файлах `01`–`04` считать provisional до утверждения этой модели.

# 29. Что считать первым контактом

## Вариант A — только фактическая потеря health

Недостатки:

- block/parry не считается участием;
- immune/resisted hit не считается;
- shield/support Player может активно сражаться, оставаясь вне `Fighting`.

## Вариант B — принятый attack contact

Контакт считается состоявшимся, когда допустимый blood hit дошёл до attack/block/damage pipeline:

- incoming damage;
- outgoing melee/projectile hit;
- block;
- parry;
- полностью mitigated hit.

Не считаются:

- miss;
- near-projectile notification;
- trigger overlap без attack resolution;
- простое обнаружение/aggro.

**Рекомендация: вариант B.** Он лучше поддерживает разные боевые стили. Нужен spike для точных Harmony points incoming/outgoing/block/parry и exactly-once server validation.

# 30. Dream collapse

## 30.1. Предпочтительный flow

После подтверждённого layer entry (`Fighting`/`GoalReached`):

1. owner Player обнаруживает lethal condition до `Player.OnDeath`;
2. health восстанавливается через штатный API;
3. outcome фиксируется как `Defeated`/`Collapsed` (финальное имя ещё выбрать), phase — `Ejected`;
4. blood target/damage/presentation/collision rules отключаются;
5. ordinary layer возвращается;
6. Player остаётся в той же position/rotation и с текущей velocity;
7. применяется короткая transition grace;
8. повторный вход не происходит автоматически.

Кодовый outcome лучше назвать `Defeated` или `Collapsed`, а не `Death`, чтобы не смешивать его с реальным `Player.OnDeath`. Death-specific DreamText при этом сохраняется.

## 30.2. Какие lethal причины покрывать

### Вариант A — только direct blood hit

Ломается на fall после knockback, delayed AOE/projectile и DOT attribution.

### Вариант B — любой lethal damage после входа в `Fighting`/`GoalReached`

Даёт простую гарантию: пока Player внутри иллюзорного боя, TombStone не возникает ни при какой обычной причине.

### Вариант C — attribution window после blood hit

Сложная и спорная эвристика.

**Рекомендация: вариант B.** Ejection terminal для редкого ежегодного события, поэтому возможная одноразовая защита от world hazard несущественнее, чем гарантированная целостность иллюзорной механики.

Отдельно решить специальные причины:

- `EdgeOfWorld`;
- admin/scripted kill;
- world shutdown;
- forced character removal.

## 30.3. Combat resources после collapse

Уже принято: food и ordinary inventory не меняются.

Нужно выбрать:

- health = 100%;
- stamina/eitr оставить текущими;
- либо восстановить stamina/eitr полностью/частично.

**Предварительная рекомендация:** full health и full current-cap stamina/eitr. Defeat уже завершает редкое событие; цель collapse — безопасно вернуть agency, а не создать вторую немедленную смерть в real world.

# 31. Transition grace после ejection

Без transform changes Player может вернуться:

- в воздухе после knockback;
- внутри collider скрытого ordinary creature;
- под ordinary projectile/AOE;
- в воде/lava;
- на корабле/mount;
- в узком проходе.

Предварительный контракт:

```text
минимум 2 секунды invulnerability
далее до устойчивого IsOnGround() и отсутствия overlap с ordinary Character collider
hard cap 8–10 секунд
```

Во время grace:

- blood enemies уже не видят Player;
- ordinary world визуально возвращается;
- Player сохраняет velocity;
- fall damage текущего падения подавляется;
- collision с overlapping ordinary characters возвращается только после separation или hard cap;
- world geometry collision не отключается;
- после cap vanilla rules полностью возвращаются.

Нужно отдельно решить lava/water/EdgeOfWorld и ordinary projectile, созданный до ejection.

# 32. Re-entry

**Рекомендация для первой версии: re-entry отсутствует.** Поражение должно сохранять смысл.

Будущий совместимый вариант — один temporary owner/event-bound Blood Craft consumable:

- готовится в Marked;
- работает из inventory в поле;
- не требует campfire/base/world object;
- ограничен одним использованием;
- исчезает утром;
- возвращает в `AwaitingContact`, а не сразу в `Fighting`.

# 33. Ownership и ordinary-world simulation

## 33.1. Предпочтительная ownership policy

1. Не менять owner без необходимости.
2. Event enemy может быть owned participant или nonparticipant; owner продолжает симуляцию, local presentation зависит от local layer.
3. Если ordinary entity owned blood participant и рядом есть eligible real-world peer, server может **напрямую** передать owner этому peer без owner=0 interval.
4. Если eligible peer нет, current owner сохраняется.
5. Не patch-ить глобальный `ZDO.SetOwner`/`ReleaseNearbyZDOS` до доказанной необходимости.

Participant identity должна различать:

```text
stable profile/player ID
current peer/session UID
Player ZDOID
```

## 33.2. Policy A — background simulation

Ordinary AI продолжает vanilla simulation, но не видит/damage blood-layer Player и локально скрыт от него.

Плюс: максимальная совместимость.

Минус: ordinary enemies могут уйти, атаковать tamed/base и изменить мир, пока solo Player не может это видеть.

## 33.3. Policy B — conditional AI suspension

Если ordinary creature owned blood participant и рядом нет real-world nonparticipant:

- owner сохраняется;
- BaseAI/MonsterAI locomotion/targeting suspend-ятся узким gate;
- после ejection/resolve AI продолжается;
- при появлении real-world observer server по возможности напрямую передаёт owner ему и снимает suspension.

Это не полный snapshot-freeze мира. Character physics, status timers, procreation и другие компоненты могут продолжаться и требуют отдельной проверки.

**Предварительная рекомендация: spike Policy B; fallback Policy A.** Layer-aware глобальный owner pool считать последним, наиболее инвазивным вариантом.

## 33.4. Размер suspension scope

Нужно решить, какие ordinary characters suspend-ить:

- весь active area participant;
- group interaction radius + hysteresis;
- только entities, которые видимы/могут взаимодействовать с participant.

Рекомендация для spike: group interaction radius с отдельным enter/leave hysteresis, а не весь мир или все sectors.

# 34. Local visibility/collision не равна отключению объекта

Нельзя выключать root GameObject/AI только потому, что local Player не должен видеть entity: client может быть owner и симулировать её для других peers.

Layer controller раздельно управляет:

- Renderer/LOD;
- audio;
- EnemyHud/name;
- main Character collider относительно local Player;
- hitbox colliders;
- projectile collision/continuation;
- AoE overlap acceptance;
- target selection;
- final damage.

Вероятный spike-кандидат:

- hide render/audio/HUD локально;
- `Physics.IgnoreCollision` для local Player ↔ hidden Character main collider;
- отдельная фильтрация hitbox/projectile/AOE;
- никогда не отключать terrain/world collision owner entity.

Обязательные owner cases:

1. blood enemy owned participant;
2. blood enemy owned nonparticipant;
3. ordinary enemy owned participant;
4. ordinary enemy owned nonparticipant;
5. owner migration во время attack;
6. owner disconnect;
7. late join рядом с уже существующими entities.

# 35. Projectiles, AOE, summons и status attribution

## 35.1. Projectile/AOE

При создании сохранять:

```text
eventId
layer/source kind
source Player/enemy ZDOID
```

Delayed hit не зависит от текущего weapon, phase или owner источника.

## 35.2. Combat summons

- summon, созданный `Fighting`/`GoalReached` Player, становится blood entity текущего event ID;
- атакует только blood enemies;
- скрыт от real-world clients;
- исчезает при ejection/resolve;
- pre-existing summon/tamed остаётся ordinary;
- ordinary turret/trap не становится blood entity автоматически.

## 35.3. DOT/status

Vanilla Poison/Burning hash не даёт достаточной source attribution.

Для первого enemy prototype принято:

- использовать enemy без persistent DOT/status attacks;
- не удалять глобально ordinary Poison/Burning;
- позднее выбрать blood-specific status clones или source-aware tracking.

# 36. Context policies, которые нужно выбрать

Для каждого context определить одну политику:

```text
full participation
context-specific blood enemy pool
AwaitingContact без forced engagement
safe skip этого Player
```

Обязательные contexts:

- outdoor ground;
- dungeon/interior;
- ship/ocean;
- mounted/attached;
- swimming/falling;
- active boss encounter;
- portal/teleport transition;
- late join.

Предварительные рекомендации для первого релиза:

- outdoor ground — full;
- portal/teleport — spawn pause, state следует за Player;
- unsupported dungeon/ship/mount context — `AwaitingContact` без forced engagement до выхода из context;
- active boss encounter — не переключать Player в full blood layer; держать deferred eligibility и разрешить поздний entry, если boss context закончился до forced end.

# 37. Real-world interactions в full blood layer

Принято:

- terrain/geometry/buildings остаются видимыми и коллизионными;
- blood attacks не повреждают ordinary world;
- doors и Blood Craft stations должны быть usable;
- Player transform/ship position не меняются.

Нужно решить:

- ordinary ItemDrop visibility/pickup;
- containers/StackAll;
- ordinary crafting/upgrade;
- ship/mount controls;
- portals;
- building/placement/terrain tools;
- harvesting/mining/chopping;
- trader/NPC;
- traps/turrets;
- ordinary Player-to-Player item transfer.

Предварительная совместимая политика:

- в `Marked` и `AwaitingContact` ordinary world interactions остаются vanilla;
- после `Fighting` разрешить movement, doors, ladders, escape controls и Blood Craft;
- ordinary pickups, resource-changing actions, trader и external inventory interactions скрыть/запретить до ejection/resolve;
- Blood Craft items в любом случае не покидают owner inventory.

# 38. Technical spikes

## Spike A — first contact + atomic layer switch

Single-player, listen и dedicated:

- incoming/outgoing contact;
- block/parry;
- lethal first hit;
- layer switch до применения первого hit;
- duplicate/stale report;
- server rejection/resync.

## Spike B — dream collapse

- `Character.CheckDeath` interception;
- no `Player.OnDeath` side effects;
- health/resource restore;
- no TombStone/death point/respawn;
- ejection exactly once;
- landing/overlap grace;
- real-world projectile immediately after return.

## Spike C — local parallel presentation

Два клиента и все owner combinations:

- renderer/audio/HUD;
- main collider/hitbox;
- projectile/AOE;
- observer видит Player, сражающегося с воздухом;
- hidden owner entity продолжает remote simulation.

## Spike D — ordinary simulation

Сравнить Policy A/B:

- ground/flying/swimming/tamed AI;
- suspend/resume;
- direct owner transfer;
- observer enters/leaves;
- owner disconnect;
- CPU/network profile.

## Spike E — context support

- land;
- dungeon;
- ship/ocean;
- mount/attached;
- portal;
- active boss.

## Spike F — spawned combat objects

- melee;
- arrow/bolt;
- thrown;
- bomb;
- persistent AOE;
- summon;
- owner migration/delayed hit.

# 39. Gate для production-кода

До отдельного решения владельца закрыть:

1. Model A или B границы входа;
2. first-contact contract;
3. dream-collapse lethal scope;
4. кодовое имя defeat outcome;
5. health/stamina/eitr после collapse;
6. transition grace и collision separation;
7. re-entry первой версии;
8. Policy A/B ordinary simulation;
9. suspension scope;
10. local visibility/collision mechanism;
11. dungeon и ship/ocean policy;
12. boss-overlap policy;
13. DOT/status policy;
14. real-world interactions;
15. projectile/AOE/summon attribution.

Пока gate не закрыт:

- не начинать production implementation;
- не открывать implementation PR;
- не делать version bump;
- разрешены только документация, чтение `assemblies_combined` и отдельно согласованные spike commits.
