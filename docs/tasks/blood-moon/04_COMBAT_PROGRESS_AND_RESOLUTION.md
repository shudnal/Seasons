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

`Exited`, `Resolved` и disconnected не входят.

Map markers отсутствуют.

# 19. Blood Moon enemies

В 23:00 используются два источника:

1. все подходящие существующие небоссовые монстры, реально загруженные рядом с active groups;
2. дополнительные custom-spawned enemies до group/server cap.

## 19.1. Прямая динамическая конверсия — принято

Suspend+clone отвергнут как ненужная сложность.

Существующий eligible monster считается Blood enemy по текущему event state, а не за счёт необратимого изменения prefab/instance данных:

```text
Blood Moon Active
AND IsEligibleExistingMonster(character)
```

По возможности поведение реализуется conditionally через patches/policy:

- Player-only target selection;
- no flee/idle при наличии цели;
- event damage multipliers;
- event visual treatment;
- no damage ordinary world.

Не менять permanent max health. Не мутировать shared prefab.

После event existing survivor автоматически возвращается к vanilla behavior, когда global Active выключен. Cleanup должен снять event VFX и очистить только target/hunt state, принудительно созданный самим Blood Moon.

Если existing monster погиб во время события:

- это обычная смерть реального существа;
- ordinary loot разрешён;
- ordinary ragdoll/cleanup остаются;
- объект не восстанавливается.

## 19.2. Дополнительные event spawns

Только custom-spawned противники получают marker:

```text
Seasons.BloodMoon.SpawnedEventId
Seasons.BloodMoon.GroupId
Seasons.BloodMoon.Role
```

Для них:

- ordinary loot подавляется;
- ragdoll быстро очищается, default 2 seconds;
- выжившие ZDO удаляются server-side при resolution;
- stale marker другого/завершённого event удаляется при recovery/load.

В конце server итерирует copy коллекции ZDO, чтобы безопасно уничтожить marked extras без изменения dictionary во время enumeration.

## 19.3. Eligibility — остаётся закрыть

Рекомендуемый старт:

- valid `Character` + `MonsterAI`;
- non-boss;
- untamed;
- hostile к хотя бы одному active participant;
- не trader/именованный NPC;
- не neutral non-aggravated Dvergr;
- не `PlayerSpawned`/pre-existing summon;
- не fish/bird/ambient entity.

Нужно отдельно подтвердить passive animals, aggravated Dvergr, unique creatures и modded humanoid NPC.

## 19.4. Dynamic discovery

Не требуется помечать все ZDO мира.

- active loaded character автоматически квалифицируется через policy;
- вновь созданный vanilla monster во время события автоматически становится Blood enemy;
- periodic scan нужен только для VFX/cached AI hooks и diagnostic bookkeeping;
- не сканировать весь world каждый frame.

## 19.5. Aggression

Blood enemy:

- всегда ищет active participant;
- не flee/idle при наличии допустимой цели;
- игнорирует PlayerBase/NoMonsters для additional spawn;
- не выбирает static targets;
- не выбирает tamed/NPC/boss/другого monster;
- GoalReached target имеет меньший score, пока есть Fighting;
- обычные event-creature despawn rules не должны заставлять его уходить из-за остановленного RandEventSystem.

## 19.6. Health/damage

Меньшая живучесть задаётся event-specific incoming damage multiplier.

Меньший исходящий урон — event-specific outgoing multiplier.

Existing health/max health не переписываются.

# 20. Damage routing

Разрешено:

```text
Blood enemy → Fighting/GoalReached Player
Fighting/GoalReached Player attack → Blood enemy
```

Запрещены:

- buildings;
- crops;
- trees/ores/resource objects;
- tamed;
- ordinary NPC;
- bosses;
- nonparticipants/Exited;
- traps/turrets/environment как способ убивать Blood enemies по умолчанию.

Фильтровать candidate hit до status/stagger/skill credit; final damage guard оставить.

Projectile/AOE/summon должен сохранять event/source attribution при создании, если исходное `HitData`/attacker недостаточны после owner migration.

# 21. Progress

Разделить:

```text
CombatBloodlustPoints
DisplayedBloodlustProgress
CombatContribution
```

Kill/share:

- server validates enemy identity и exactly-once death;
- points получают active members группы в configured radius;
- не только last hit;
- GoalReached actions могут идти в stats, но не progress;
- kills existing и marked extra enemies учитываются одинаково, стоимость задаёт server.

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

Не использовать `RemoveAllStatusEffects`. Unknown modded DoT не удалять автоматически.

## Recovery protection

Stage 1:

```text
100% incoming protection
until IsOnGround || IsSwimming || IsAttached
hard cap 15 seconds
```

Stage 2:

```text
10 seconds
75% incoming reduction
final multiplier 0.25
```

Direct `Player.OnDeath`/scripted removal не перехватывать. Admin HP reduction естественно приводит к Defeated.

# 23. Edge withdrawal

До vanilla edge death:

```csharp
ZoneSystemVariantController.IsBeyondWorldEdge(position, positiveSafetyOffset)
```

→ phase `Exited`, outcome `Withdrawn`.

No transform changes. Re-entry отсутствует.

# 24. Contexts

- outdoor ground: Fighting;
- mounted/attached: Fighting, no forced detach;
- ship/ocean: Fighting; land additional spawner может не найти поверхность, existing sea monsters продолжают работать;
- ordinary interior/dungeon: Fighting; existing monsters становятся Blood enemies, custom interior spawn исследуется через navmesh/path validity;
- teleport: временно pause spawn, затем продолжить в destination;
- active unparked interior boss: отдельное обязательное решение из `09`.

Body blocking Exited Player-ом остаётся vanilla.

# 25. Early/morning resolution

Early end:

- хотя бы один Player был enrolled;
- все enrolled имеют terminal Success/Defeated/Withdrawn/Disconnected.

05:45 forced resolution.

Prepare:

1. freeze enrollment;
2. stop additional spawn;
3. freeze outcomes;
4. fade request + timeout.

Resolve under fade:

1. disable global Blood Moon monster behavior;
2. delete marked extra enemy ZDO;
3. clear event-created target/VFX/runtime state on existing monsters;
4. restore parked bosses;
5. clean Blood Craft;
6. remove force environment/VFX;
7. restore RandEventSystem;
8. time → 06:00;
9. remove Rested;
10. publish DreamText;
11. release input.

No Player transform changes.
