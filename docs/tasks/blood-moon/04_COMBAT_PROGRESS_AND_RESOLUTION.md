# Blood Moon — combat, progress, defeat and resolution

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

# 18. Hidden combat groups

Server-only grouping:

- recompute 3–5 seconds;
- connected components by distance;
- merge default 120 m;
- split default 160 m;
- stable ID by maximum member overlap;
- cap from participant count;
- server hard cap;
- spawn/scan anchor = real member, not geometric center.

В группы входят `Fighting` и `GoalReached`.

`Deferred`, `Exited`, `Resolved`, disconnected не входят.

Map markers отсутствуют.

# 19. Blood Moon enemies

В 23:00 используются два источника:

1. подходящие существующие монстры вокруг active groups;
2. дополнительные event spawns до cap.

## 19.1. Existing monster strategy — open spike

Сравнить:

### Direct conversion

Existing entity получает event marker и временные правила.

Плюсы: максимально просто.

Минусы:

- убитое реальное существо исчезает без loot;
- surviving creature меняет position/health/AI history;
- можно потерять редкое/starred/modded creature.

### Suspend original + blood copy

Original глобально скрывается/замораживается для всех клиентов без owner changes, blood copy спавнится на его месте.

Плюсы:

- состояние обычного мира сохраняется;
- clone можно безопасно убивать/удалять;
- тот же механизм пригоден для bosses.

Минусы:

- нужно надёжно suspend/restore render, colliders, AI, physics;
- restart/unload/nonpersistent cases сложнее.

Runtime spike обязан сравнить оба. Для первой production версии direct conversion допустим только после сознательного принятия world-state tradeoff.

## 19.2. Eligibility candidate

Стартовая рекомендация:

- valid untamed `MonsterAI`;
- hostile к хотя бы одному active Player;
- не Player;
- не boss;
- не tamed;
- не trader/NPC/Dvergr-neutral;
- не `PlayerSpawned`/pre-existing summon;
- не fish/bird/ambient-only creature;
- не already managed/blood.

Точный predicate фиксируется после scan реальных prefabs.

## 19.3. Dynamic scan

Периодически обрабатывать новые ordinary spawns, вошедшие в interaction radius. Не сканировать весь world каждый frame.

## 19.4. Markers

```text
Seasons.BloodMoon.EventId
Seasons.BloodMoon.GroupId
Seasons.BloodMoon.Origin
Seasons.BloodMoon.Role
Seasons.BloodMoon.OriginalZDOID (если clone)
```

## 19.5. Aggression

Blood enemy:

- всегда ищет active participant;
- не flee/idle при наличии допустимой цели;
- игнорирует PlayerBase/NoMonsters для spawn;
- не выбирает static targets;
- не выбирает tamed/NPC/boss/ordinary monster;
- GoalReached target имеет меньший score, пока есть Fighting.

Не мутировать shared prefab.

## 19.6. Health/damage

Не менять permanent max health existing entity.

Желаемая меньшая живучесть задаётся через event-specific incoming damage multiplier. Меньший outgoing damage — через event-specific outgoing multiplier.

Это одинаково работает для converted и spawned enemies.

## 19.7. Loot/ragdoll

Blood enemy:

- no ordinary loot;
- no economy contribution;
- ragdoll cleanup default 2 seconds;
- remaining spawned/copy enemies deleted at resolution.

# 20. Damage routing

## Enemy

Разрешено только:

```text
Blood enemy → Fighting/GoalReached Player
```

## Participant

Разрешено только:

```text
Fighting/GoalReached Player attack
→ Blood enemy current eventId
```

Запрещены:

- buildings;
- crops;
- trees/ores/resource objects;
- tamed;
- ordinary creatures;
- bosses;
- nonparticipants;
- event enemies stale/other eventId.

Traps/turrets/environment не должны убивать Blood enemies по умолчанию.

Фильтровать candidate hit до status/stagger/skill credit, final guard оставить.

# 21. Progress

Разделить:

```text
CombatBloodlustPoints
DisplayedBloodlustProgress
CombatContribution
```

Kills/share:

- server validates enemy ZDO and exactly-once death;
- points получают active members группы в configured radius;
- не только last hit;
- GoalReached actions могут идти в stats, но не progress.

At 100%:

- phase → `GoalReached`;
- outcome → `Success`;
- full buff remains;
- lower aggro only while unfinished targets exist.

Auto-complete:

```text
Displayed = max(combatProgress, automaticFloor)
```

Не даёт points/contribution/reward/Success.

# 22. Defeated dream collapse

Owner-side `Character.CheckDeath` intercept:

```text
Player
phase Fighting/GoalReached
health <= 0
not already Exited
```

No `Player.OnDeath`.

Restore:

```text
Health = max
Stamina = max
Eitr = max
Food unchanged
Adrenaline unchanged
```

Outcome:

```text
phase = Exited
outcome = Defeated
```

## DoT cleanup

Удалять только вычислимо damaging effects:

- `SE_Burning`;
- `SE_Poison`;
- `SE_Smoke`;
- `SE_Stats` с `m_tickInterval > 0 && m_healthPerTick < 0`.

Не использовать `RemoveAllStatusEffects`. Unknown modded DoT не удалять автоматически без доказуемого признака.

## Recovery protection

Stage 1:

```text
100% incoming protection
until IsOnGround || IsSwimming || IsAttached
hard cap 15 seconds
```

Покрывает fall damage текущей траектории.

Stage 2:

```text
10 seconds
75% incoming reduction
final multiplier 0.25
```

После этого lava/water/world damage полностью vanilla.

Direct `Player.OnDeath`/scripted removal не перехватывать. Admin HP reduction естественно приводит к Defeated.

# 23. Edge withdrawal

До vanilla edge death:

```csharp
ZoneSystemVariantController.IsBeyondWorldEdge(position, positiveSafetyOffset)
```

→ phase `Exited`, outcome `Withdrawn`.

Не менять transform. После снятия Blood Moon protection обычные edge rules снова действуют.

# 24. Contexts

- outdoor ground: Fighting;
- mounted/attached: Fighting, no forced detach;
- teleport: временно не spawn; destination re-evaluate;
- interior/dungeon at Active start: Deferred;
- ship/ocean at Active start: Deferred;
- выход Deferred Player в supported context: Fighting;
- вход уже Fighting Player в unsupported context: решение в `09`, рекомендован terminal Withdrawn.

# 25. Early/morning resolution

Early end:

- хотя бы один Player был enrolled;
- все enrolled имеют terminal Success/Defeated/Withdrawn/Disconnected.
- Deferred не terminal и удерживает event до forced end.

05:45 forced resolution.

Prepare:

1. freeze enrollment;
2. stop scan/spawn;
3. freeze outcomes;
4. fade request + timeout.

Resolve under fade:

1. delete blood spawned/copies;
2. clear converted/suspended ordinary state;
3. restore bosses;
4. clean Blood Craft;
5. remove force environment/VFX;
6. restore RandEventSystem;
7. time → 06:00;
8. remove Rested;
9. publish DreamText;
10. release input.

No Player transform changes.
