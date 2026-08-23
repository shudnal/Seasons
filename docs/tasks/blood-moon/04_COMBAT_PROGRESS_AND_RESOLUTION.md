# Blood Moon — combat, progress and resolution

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

> Production-код по этому документу не начинать до закрытия gate из `09_PREIMPLEMENTATION_DECISIONS_AND_SPIKES.md`.

# 13. Минимальная скрытая группировка

Spawner не должен независимо умножать полный лимит вокруг каждого стоящего рядом Player.

Планируемая server-only группировка:

- пересчёт каждые 3–5 секунд;
- connected components по дистанции;
- merge distance, стартово 120 м;
- split hysteresis, стартово 160 м;
- стабильный group ID по максимальному пересечению состава;
- group spawn cap от количества combat-capable participants;
- server-wide hard cap;
- spawn anchor выбирается из реальных members, не из геометрического центра цепочки.

В группу входят:

```text
AwaitingContact
Fighting
GoalReached
```

Не входят:

```text
Ejected
Resolved
terminal Disconnected
```

Пока не реализовывать Momentum, StallTime, роли, production progression pool и map UI.

---
# 14. Первый тип event enemy

## 14.1. Prefab

Первый combat prototype использует один консервативный vanilla melee prefab без persistent DOT/status attacks. Prefab name — server config только для bootstrap playtest.

Перед выбором проверить текущий prefab в `assemblies_combined`, ObjectDB/ZNetScene и его AI/damage/drop components.

## 14.2. Spawn

Custom Blood Moon spawner:

- не использует `RandEventSystem` как event controller;
- игнорирует `NoMonsters`, PlayerBase suppression и аналогичные ограничения обычного spawn;
- ищет валидную context-appropriate точку;
- не спавнит прямо внутри Player/камеры;
- имеет min/max distance;
- имеет per-group cap и server hard cap;
- прекращает spawn при `StoppingSpawns`;
- ставит ZDO markers до начала полноценной симуляции.

Минимум:

```text
Seasons.BloodMoon.EventId
Seasons.BloodMoon.GroupId
Seasons.BloodMoon.Role
```

Surface-only spawn не считается финальной поддержкой dungeon/ship/ocean. Эти contexts закрываются до production implementation отдельным gate/spike.

## 14.3. Ownership и reports

Не предполагать, что `Character.OnDeath` event enemy всегда выполняется на dedicated server.

Owner event enemy сообщает:

```text
eventId
enemy ZDOID
reported killer Player ZDOID/stable ID
position
group ID
```

Сервер проверяет:

- event ID;
- marker на ZDO;
- exactly-once ZDOID;
- допустимую группу;
- активных получателей progress;
- разумную дистанцию;
- фазу, принимающую combat results.

Стоимость врага определяет сервер.

## 14.4. Target eligibility

Event AI может выбирать:

- `AwaitingContact`;
- `Fighting`;
- `GoalReached` с пониженным score, если рядом есть незавершившие.

Не выбирать:

- nonparticipants;
- `Ejected`/`Resolved`;
- tamed;
- buildings/crops/static targets;
- ordinary NPC/monsters;
- blood entities другого event ID.

Недостаточно обнулить final damage: AI не должен тратить pathfinding/attacks на запрещённые цели.

## 14.5. Final damage safety

Через единый `BloodMoonInteractionRules` запрещать event enemy damage по:

- `WearNTear`/buildings;
- tamed;
- crops/destructibles;
- ordinary creatures;
- nonparticipants;
- stale event participants.

Урон разрешён только blood-layer participant текущего event ID.

## 14.6. Агрессивность

Первый enemy активно ищет допустимого Player и не уходит в обычный flee/idle loop, противоречащий мясному темпу.

Сложный anti-hide и doors пока не реализовывать.

## 14.7. Loot и ragdoll

- no ordinary loot;
- no world economy contribution;
- ragdoll cleanup, default 2 секунды;
- полное удаление при ejection/resolve/world cleanup;
- не мутировать shared prefab drop table глобально.

---
# 15. First contact, Bloodlust progress и личный исход

## 15.1. Разделение величин

```text
CombatBloodlustPoints
DisplayedBloodlustProgress
CombatContribution
```

Auto-complete влияет только на displayed progress.

## 15.2. `AwaitingContact` → `Fighting`

В 23:00 Player входит в `AwaitingContact`.

Первый допустимый incoming/outgoing blood interaction создаёт `FirstBloodContactRecord` и переводит Player в `Fighting`.

До отдельного решения не фиксировать в коде, требуется ли положительная потеря health. Предварительно рекомендован accepted attack contact, включая block/parry, но исключая miss/near projectile.

First contact:

- дедуплицируется;
- валидируется сервером;
- не хранит transform anchor;
- position используется только для диагностики/статистики;
- должен корректно обработать lethal first hit.

## 15.3. Начисление progress

Для первого enemy сервер задаёт `pointsPerKill`.

При подтверждённой смерти:

- начислить combat points участникам той же группы в настроенном радиусе;
- не привязывать основной progress только к last hit;
- killer хранить отдельно;
- не начислять `Ejected`/`Resolved`;
- GoalReached больше не получает progress, но его действия можно считать в статистику.

## 15.4. 100%

При combat progress 100%:

- phase → `GoalReached`;
- outcome → `Success`;
- Bloodlust/full combat buff остаются;
- Player продолжает атаковать event enemies;
- aggro ниже только при наличии `Fighting`/`AwaitingContact` participants;
- если он единственный доступный target, enemies продолжают его атаковать.

Не вводить `Sated` с ослаблением.

## 15.5. Auto-complete

В финальном интервале до 05:45:

```text
DisplayedBloodlustProgress = max(combatProgress, automaticFloor)
```

Automatic floor:

- не добавляет combat points;
- не добавляет contribution;
- не создаёт Success;
- не даёт skill reward.

На forced end незавершившийся Player получает `HiddenAtBase` или `HiddenInWild`.

---
# 16. Боевые modifiers первого прототипа

Направление:

- максимальная защита на 0%;
- защита постепенно уменьшается, но остаётся положительной;
- outgoing damage растёт;
- movement speed растёт;
- на 100% full buff сохраняется.

Стартовые ориентиры:

```text
Incoming damage reduction at 0%: 50%
Incoming damage reduction at 100%: 25%
Outgoing damage bonus at 0%: 15%
Outgoing damage bonus at 100%: 40%
Movement speed bonus at 0%: 0/small
Movement speed bonus at 100%: 10–15%
```

Интерполяция линейная.

Не реализовывать до playtest:

- lifesteal;
- attack speed;
- stagger immunity;
- weapon-specific modifiers.

Нужно отдельно решить, применяются ли базовые modifiers уже в `AwaitingContact` или только после first contact. Предварительная рекомендация: defensive base modifier действует сразу, чтобы lethal first hit не обходил смысл события; contribution/reward начинается только после contact.

---
# 17. Dream collapse, early completion и morning resolution

## 17.1. Preferred dream collapse

Предпочтительный личный defeat flow:

1. owner-side lethal condition обнаруживается до `Player.OnDeath`;
2. `Player.OnDeath` не вызывается;
3. no death point, ragdoll, TombStone, inventory move, food clear или respawn;
4. health восстанавливается до принятого значения, стартово 100%;
5. position/rotation/velocity не задаются модом;
6. outcome → `Death`, phase → `Ejected`;
7. blood targeting/presentation/collisions прекращаются;
8. real-world layer возвращается;
9. short transition grace применяется после отдельного решения;
10. death DreamText показывается при общем resolution.

Предпочтительная техническая точка spike — `Character.CheckDeath` owner-side prefix, так как vanilla вызывает `OnDeath` после health `<= 0` на следующем death check.

## 17.2. Lethal scope — gate

До реализации выбрать:

- только direct blood hit;
- любой lethal damage в `Fighting`/`GoalReached`;
- attribution window после blood hit.

Предварительно рекомендован любой lethal damage в blood combat с явным deny-list для `EdgeOfWorld`/scripted kills. Причина: fall, delayed AOE и DOT часто теряют исходную attribution, а TombStone из иллюзорного боя недопустим.

## 17.3. Transition grace — gate

Без transform changes Player может быть ejected в воздухе, на корабле, в воде или внутри возвращаемого ordinary collider.

Предварительный кандидат:

```text
минимум 2 секунды invulnerability
далее до первого устойчивого IsOnGround()
hard cap 8–10 секунд
```

Grace не перемещает Player и не обнуляет velocity. Поведение в lava/water/EdgeOfWorld требует отдельного решения.

## 17.4. Early completion

Событие разрешается досрочно, если:

- хотя бы один Player был enrolled;
- каждый enrolled participant получил terminal outcome:
  - Success/GoalReached;
  - Death/Ejected;
  - Disconnected.

GoalReached остаётся в бою, пока есть `AwaitingContact`/`Fighting` participants.

## 17.5. Forced completion

В 05:45 сервер начинает resolution независимо от progress.

## 17.6. Двухфазный протокол

### Prepare

Сервер:

1. закрывает enrollment;
2. прекращает spawn;
3. замораживает outcomes/statistics;
4. публикует `PrepareResolution`;
5. ждёт client fade ACK с timeout.

Клиент:

- блокирует input;
- начинает fade;
- отвечает ACK.

### Resolve

Под fade:

1. удалить event enemies/summons/projectiles;
2. снять Blood Moon combat/layer state;
3. очистить cloud VFX;
4. снять собственный force environment;
5. восстановить RandEventSystem;
6. перевести world time к 06:00;
7. удалить `Rested`;
8. опубликовать outcome/DreamText;
9. разблокировать input.

## 17.7. Поза пробуждения

Никаких transform changes.

- safe grounded Player может получить lie/rest emote;
- ship/water/air/dungeon/attached — fade + DreamText без forced pose;
- не опускать Player к terrain и не менять rotation.

## 17.8. Rested

После Blood Moon удалить `Rested`. Отдельный утренний наказующий debuff не нужен.

---
# 18. DreamText первого прототипа

Минимум English и Russian tokens.

## Success

```text
Ты приходишь в себя на холодной земле.
В памяти остались красная луна, звон оружия и радость, которой ты теперь стыдишься.
Тело ломит, но кровь больше не поёт.
```

## Death/Ejected

```text
Ты помнишь удар.
Потом землю, тьму и далёкий вой.
Когда рассвет касается лица, ты уже не уверен, что всё это было сном.
```

## Hidden at base

```text
Ты просыпаешься разбитым.
В кошмаре стены были тоньше бумаги, а за дверью кто-то называл тебя по имени.
Снаружи тихо. Слишком тихо.
```

## Hidden in wild

```text
Ты приходишь в себя далеко от того места, где началась ночь.
Ноги сами несли тебя сквозь темноту, пока луна не насытилась.
Ты не знаешь, что видел. И не хочешь знать.
```

## GoalReached cue

```text
Ты насытил ночь, но она не отпускает.
Теперь её взгляд скользит мимо тебя — к тем, кто ещё сопротивляется.
```

---
# 19. Debugging

После снятия gate нужны:

```text
seasons bloodmoon status
seasons bloodmoon start marked
seasons bloodmoon start active
seasons bloodmoon firstcontact
seasons bloodmoon collapse
seasons bloodmoon setprogress <0..100>
seasons bloodmoon spawn [prefab]
seasons bloodmoon resolve
seasons bloodmoon cleanup
seasons bloodmoon skip-current
seasons bloodmoon reset-current
seasons bloodmoon dump-participants
seasons bloodmoon dump-groups
```

Команды меняют состояние только через production transition methods.

Структурированные логи:

```text
[BloodMoon][event:<id>][phase]
[BloodMoon][event:<id>][player:<id>]
[BloodMoon][event:<id>][contact]
[BloodMoon][event:<id>][collapse]
[BloodMoon][event:<id>][group:<id>]
[BloodMoon][event:<id>][resolution]
```

Per-hit/per-spawn logging только под debug config.

---
# 20. Планируемые конфиги первого среза

## Calendar

- Enable Blood Moon;
- Forewarning start autumn day;
- Final Blood Moon autumn day;
- Marked start time 18:00;
- Active start time 23:00;
- Forced end 05:45;
- Morning target 06:00;
- Auto-complete duration 1.5 game hours.

## Group/spawn

- Test enemy prefab;
- Merge/split distance;
- Min/max spawn distance;
- Base/per-player/server max alive;
- Spawn interval;
- Points per kill;
- Progress share radius;
- Ragdoll cleanup delay.

## Player modifiers

- incoming reduction at 0/100%;
- outgoing bonus at 0/100%;
- movement bonus at 0/100%.

## Ejection

Не добавлять production-конфиги, пока не закрыты lethal scope/grace. После решения оставить минимум действительно полезных параметров, а не выносить каждую деталь state machine в config.

## Diagnostics

- detailed Blood Moon logging;
- visual factor/status debug.
