# Blood Moon — combat, progress and resolution

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

# 13. Минимальная скрытая группировка

Несмотря на отсутствие map markers, первый работающий spawner не должен независимо умножать полный лимит вокруг каждого стоящего рядом игрока.

Реализовать минимальную server-only группировку:

- пересчёт каждые 3–5 секунд;
- connected components по дистанции;
- merge distance конфигурируемая, стартовое значение 120 м;
- split hysteresis, стартовое значение 160 м;
- стабильный group ID сохранять по максимальному пересечению состава;
- group spawn cap считать от количества участников;
- server-wide hard cap обязателен;
- spawn anchor выбирать из реальных участников группы, а не из геометрического центра цепочки игроков.

Не реализовывать пока:

- Momentum;
- StallTime;
- роли врагов;
- progression pool;
- map UI.

Архитектура группы должна позволить добавить эти поля позже без смены participant/event protocol.

---
# 14. Один тип ивентового врага

## 14.1. Prefab

Добавить серверный конфиг prefab name для первого среза. Дефолт выбрать консервативный ванильный melee prefab, пригодный для Meadows/раннего теста; перед выбором проверить фактический prefab в `assemblies_combined`/ObjectDB/ZNetScene.

Не прошивать сложную прогрессию в первый коммит.

## 14.2. Spawn

Custom spawner Blood Moon:

- не использовать `RandEventSystem` и обычный `SpawnSystem` как систему события;
- игнорировать `NoMonsters`, player base suppression и аналогичные ограничения обычного спавна;
- всё равно искать валидную точку на поверхности;
- не спавнить прямо в камере/внутри игрока;
- иметь min/max spawn distance;
- иметь per-group cap и server hard cap;
- прекращать spawn сразу при `StoppingSpawns`;
- помечать ZDO до того, как объект сможет участвовать в бою.

Минимальные ZDO fields:

```text
Seasons.BloodMoon.EventId
Seasons.BloodMoon.GroupId
Seasons.BloodMoon.Role
```

Для первого врага role можно считать `Swarm`/`Test`, но схема должна быть совместима с будущими ролями.

## 14.3. Ownership и reports

Не предполагать, что `Character.OnDeath` всегда выполняется на dedicated server.

Владелец event enemy сообщает серверу минимум:

```text
eventId
enemy ZDOID
reported killer player ID
position
```

Сервер проверяет:

- текущий event ID;
- event marker на ZDO;
- что этот ZDOID ещё не был зачтён;
- что enemy относится к допустимой группе;
- что killer/получатели progress являются активными участниками;
- разумную дистанцию;
- что событие ещё принимает combat results.

После зачёта ZDOID входит в bounded deduplication set текущего события.

Не доверять клиентскому числу очков: стоимость врага задаёт сервер.

## 14.4. Target eligibility

Ивентовый AI должен выбирать целью только:

- `Fighting` участников;
- `GoalReached` участников, если рядом нет более приоритетных незавершивших либо после применения пониженного score.

Не выбирать:

- посторонних игроков;
- eliminated/resolved игроков;
- tamed;
- постройки;
- crops;
- обычных NPC;
- обычных монстров.

В первом срезе ивентовые враги не сражаются друг с другом.

Недостаточно только обнулить финальный damage: AI также не должен тратить атаки и навигацию на запрещённые цели.

## 14.5. Final damage safety

Независимо от AI-фильтра, defence-in-depth patch должен запрещать ущерб от event enemy:

- `WearNTear`/постройкам;
- tamed;
- crops/destructibles;
- обычным существам;
- неучаствующим игрокам.

Урон разрешён только действующему участнику текущего event ID.

Target filtering и final damage filtering должны использовать централизованный `BloodMoonInteractionRules`/эквивалент. Не создавать отдельные несовпадающие списки допустимых целей в `MonsterAI`, `Character.Damage`, `WearNTear` и других patches.

## 14.6. Агрессивность

Первый враг должен активно искать игрока и не уходить в обычный flee/idle loop, несовместимый с мясным темпом.

Не реализовывать пока сложный anti-hide или двери.

## 14.7. Loot и ragdoll

Ивентовый враг:

- не выдаёт обычный loot;
- не участвует в обычной экономике;
- не оставляет долгоживущий ragdoll;
- ragdoll cleanup delay конфигурируемый, default 2 секунды;
- полностью удаляется при resolution/world cleanup.

Не мутировать shared prefab drop tables глобально. Решение должно зависеть от event marker конкретного экземпляра.

---
# 15. Bloodlust progress и завершение личной цели

## 15.1. Разделение величин

Сразу разделить:

```text
CombatBloodlustPoints
DisplayedBloodlustProgress
CombatContribution
```

В первом срезе `CombatContribution` может быть минимальным, но отдельное поле/DTO нужно уже сейчас.

## 15.2. Начисление

Для первого типа врага сервер задаёт `pointsPerKill`.

При подтверждённой смерти:

- начислить combat points действующим участникам той же группы в настроенном радиусе;
- не завязывать основной progress исключительно на last hit;
- killer можно учитывать отдельно в статистике;
- не начислять progress eliminated/resolved участникам;
- GoalReached участники уже не нуждаются в progress, но их действия можно считать в будущую статистику.

Балансировочные значения сделать конфигурируемыми.

## 15.3. 100%

Когда server combat progress достигает 100%:

- participant phase → `GoalReached`;
- outcome → `Success`;
- Blood Moon status и полный боевой buff **не снимаются**;
- игрок продолжает видеть/атаковать врагов;
- если рядом есть `Fighting` участники, target score GoalReached игрока уменьшается;
- если GoalReached игрок остался единственным доступным участником, враги продолжают нормально его атаковать.

Никакого отдельного `Sated` с ослаблением не вводить.

## 15.4. Auto-complete

В последнем настроенном интервале до 05:45:

- automatic floor линейно растёт к 100%;
- `DisplayedBloodlustProgress = max(combatProgress, automaticFloor)`;
- automatic floor не добавляет CombatBloodlustPoints;
- не добавляет contribution;
- не превращает спрятавшегося игрока в Success;
- не даёт будущую skill reward.

В forced end участник без реального Success получает исход `HiddenAtBase` либо `HiddenInWild`.

Для определения базы использовать штатный player-base/spawn-protection механизм игры из `assemblies_combined`, а не произвольный distance до собственного prefab list.

---
# 16. Боевые modifiers первого среза

Нужен минимальный, конфигурируемый Bloodlust scaling, чтобы первый playtest уже отражал целевой темп.

Принятое направление:

- на 0% игрок получает сильнейшее снижение входящего урона;
- по мере разгона защита уменьшается, но остаётся положительной;
- исходящий урон растёт;
- скорость движения растёт;
- на 100% сохраняется максимальный бафф.

Стартовые ориентиры, все через конфиги:

```text
Incoming damage reduction at 0%: 50%
Incoming damage reduction at 100%: 25%
Outgoing damage bonus at 0%: 15%
Outgoing damage bonus at 100%: 40%
Movement speed bonus at 0%: small/0
Movement speed bonus at 100%: 10–15%
```

Интерполяция пока линейная.

Не реализовывать в первом срезе:

- lifesteal;
- attack speed;
- stagger immunity;
- сложные weapon-specific modifiers.

Нужна чистая точка расширения после реального теста темпа боя.

---
# 17. Early completion и morning resolution

## 17.1. Условие раннего завершения

Событие разрешается досрочно, если:

- хотя бы один игрок был enrolled;
- для всех enrolled участников зафиксирован терминальный исход Success/Death/Disconnected.

GoalReached игроки продолжают помогать, пока есть хотя бы один Fighting участник.

## 17.2. Forced completion

В 05:45 сервер начинает resolution независимо от оставшегося progress.

## 17.3. Двухфазный сетевой протокол

### Prepare

Сервер:

1. закрывает enrollment;
2. прекращает spawn;
3. замораживает outcomes/statistics;
4. публикует `PrepareResolution`;
5. ждёт client fade ACK с коротким timeout.

Клиент:

- блокирует игровой input;
- начинает fade to black;
- отвечает ACK.

Один зависший/отключившийся клиент не должен блокировать сервер.

### Resolve

Под чёрным экраном сервер/клиенты выполняют в согласованном порядке:

1. удалить event enemies;
2. снять Blood Moon combat state;
3. остановить/очистить cloud VFX;
4. снять собственный forced environment;
5. восстановить RandEventSystem;
6. перевести world time к 06:00 следующего/текущего утра;
7. сбросить `Rested`;
8. опубликовать outcome и DreamText;
9. разблокировать input после завершения fade/явного input согласно выбранной безопасной UX-реализации.

## 17.4. Поза пробуждения

Не менять Y/позицию игрока вслепую.

- Если игрок безопасно grounded и не attached — можно включить подходящую loop/rest/lie анимацию.
- Если игрок на корабле, в воде, воздухе, данже, attached или состояние сомнительно — оставить позицию и ограничиться fade + DreamText.
- Не телепортировать игрока к terrain и не выбрасывать с корабля.

## 17.5. Rested

После Blood Moon удалить `Rested` как мягкое последствие ночи.

Не добавлять отдельный утренний наказующий debuff.

---
# 18. DreamText и локализация первого среза

Добавить локализуемые tokens минимум для English и Russian. Не генерировать машинные переводы на все языки в этой задаче.

Нужны варианты:

## Success

```text
Ты приходишь в себя на холодной земле.
В памяти остались красная луна, звон оружия и радость, которой ты теперь стыдишься.
Тело ломит, но кровь больше не поёт.
```

## Death

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

## GoalReached status cue

```text
Ты насытил ночь, но она не отпускает.
Теперь её взгляд скользит мимо тебя — к тем, кто ещё сопротивляется.
```

Тексты можно адаптировать под фактический API DreamTexts и ограничения UI, сохранив смысл и стиль.

---
# 19. Debugging и эксплуатационные команды

Ежегодное событие нельзя эффективно разрабатывать ожиданием календаря. В первом срезе обязательны admin/debug entry points.

Предпочесть существующий стиль console commands Seasons. Если единой команды нет, добавить один корень без загрязнения глобального namespace.

Минимум:

```text
seasons bloodmoon status
seasons bloodmoon start marked
seasons bloodmoon start active
seasons bloodmoon setprogress <0..100>
seasons bloodmoon spawn [prefab]
seasons bloodmoon resolve
seasons bloodmoon cleanup
seasons bloodmoon skip-current
seasons bloodmoon reset-current
seasons bloodmoon dump-participants
seasons bloodmoon dump-groups
```

Требования:

- сервер/admin only там, где команда меняет состояние;
- корректная работа в single-player/listen server;
- команды проходят через те же transition methods, а не вручную меняют поля;
- `cleanup` идемпотентен;
- `status` показывает event ID, revision, phase, schedule, resolution step, participants, enemy count, suppression и force-env ownership.

## 19.1. Логи

Информационно логировать только значимые переходы:

```text
[BloodMoon][event:<id>][phase]
[BloodMoon][event:<id>][player:<id>]
[BloodMoon][event:<id>][group:<id>]
[BloodMoon][event:<id>][resolution]
```

Per-spawn/per-hit шум — только под отдельным debug config.

Не использовать имена игроков как единственный идентификатор; логировать stable player ID и читаемое имя рядом.

---
# 20. Конфиги первого среза

Имена и секции согласовать с существующим стилем Seasons. Все gameplay/server-state конфиги синхронизируются через CCS как серверные.

Минимальный набор:

## Calendar

- Enable Blood Moon;
- Forewarning start autumn day (default 6);
- Final Blood Moon autumn day (default 9);
- Marked start time (default 18:00);
- Active start time (default 23:00);
- Forced end time (default 05:45);
- Morning target time (default 06:00);
- Auto-complete duration in game hours (default 1.5).

## Group/spawn

- Test enemy prefab;
- Merge distance (default 120);
- Split distance (default 160);
- Min/max spawn distance;
- Base max alive per group;
- Additional max alive per player;
- Server hard max alive;
- Spawn interval;
- Bloodlust points per kill;
- Progress share radius;
- Ragdoll cleanup delay (default 2 seconds).

## Player modifiers

- Incoming damage reduction at 0%;
- Incoming damage reduction at 100%;
- Outgoing damage bonus at 0%;
- Outgoing damage bonus at 100%;
- Movement speed bonus at 0%;
- Movement speed bonus at 100%.

## Diagnostics

- Detailed Blood Moon logging;
- optional visual debug factor/status display.

Не добавлять десятки конфигов для ещё не реализованных ролей/PvP/Blood Craft.

---
