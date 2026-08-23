# Blood Moon — network, persistence and participants

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

# 6. CCS CustomSyncedValues и собственные RPC

До реализации сетевого слоя отдельно изучить:

- существующий `Utils/CustomSyncedValuesSynchronizer.cs` в Seasons;
- `CustomSyncedValue<T>` в ConditionalConfigSync;
- `SequencedCustomSyncedValue<T>`;
- очередь sequenced values и её ограничения;
- уже используемые project priorities/queued assignment.

## 6.1. Требование к результату анализа

В коде или в отдельной короткой секции итогового отчёта зафиксировать выбранную схему и причины.

Не следует автоматически реализовывать всё собственными RPC, если CCS уже надёжно решает доставку текущего авторитетного состояния и очередность переходов.

Одновременно не пытаться использовать CCS там, где нужен target-specific client response или client→server report.

## 6.2. Рекомендуемая гибридная схема

Это ориентир, а не запрет на лучшее решение после изучения исходников:

### CCS current snapshot

Обычный `CustomSyncedValue<string>` или другой поддерживаемый сериализуемый payload для низкочастотного текущего глобального snapshot:

```text
protocolVersion
eventId
revision
phase
schedule
resolutionStep
suppression/visual flags
```

Snapshot должен быть самодостаточным для поздно подключившегося клиента.

### CCS sequenced channel

`SequencedCustomSyncedValue` можно использовать для редких упорядоченных server→client transition commands, если его реальные гарантии и очередь подходят:

```text
Marked entered
Active entered
PrepareResolution
PublishOutcome
ReleaseClient
```

Не отправлять через sequenced queue высокочастотный progress каждого игрока.

### Собственные RPC

Оставить собственные RPC для:

- client→server owner reports о смерти/ударе ивентового ZDO;
- targeted snapshot/resync при необходимости;
- targeted participant progress/outcome;
- client fade ACK;
- диагностического запроса.

## 6.3. Revision и stale data

Каждый snapshot/delta имеет монотонный `revision` внутри event ID.

Клиент игнорирует:

- меньшую revision;
- другой устаревший event ID;
- transition, недопустимый из текущей state machine.

При сомнении клиент запрашивает полный snapshot.

## 6.4. Частота

Progress HUD не требует сетевого пакета на каждый hit.

Сервер агрегирует изменения и отправляет participant progress не чаще разумного лимита, например 4 раза в секунду. Клиент может визуально интерполировать между подтверждёнными значениями.

---
# 7. Persistence, первый запуск и recovery

## 7.1. Что должно переживать рестарт

Минимум:

```text
schemaVersion
eventId
event schedule
event phase
resolution step
last started/resolved/skipped event IDs
participant states/outcomes
combat progress
CombatEntrySnapshot участника
auto-complete flag
revision
```

Ивентовые враги могут быть пересобраны/очищены по ZDO marker; хранить полный объектный граф в JSON не требуется.

## 7.2. World identity

Snapshot должен быть привязан к UID мира, а не только к имени сервера.

Использовать существующий подход проекта, если он есть. Не создавать бесконечный набор global keys по одному на каждый год.

Допустима комбинация:

- маленький world-bound marker текущей схемы/последнего resolved ID;
- атомарный sidecar JSON для transient active state.

## 7.3. Безопасность существующих миров

Если функциональность Blood Moon впервые появилась/включилась уже внутри текущего forewarning/final-night окна, **не запускать событие внезапно**.

Текущий ежегодный event ID пометить `Skipped`, сохранить это решение и ждать следующего года.

Административная debug-команда может явно сбросить skip и принудительно начать тестовое событие.

## 7.4. Рестарт во время события

При валидном snapshot:

1. восстановить state и schedule;
2. проверить текущее абсолютное время;
3. восстановить/пересчитать участников после подключения;
4. очистить stale event enemies с неправильным event ID;
5. заново построить скрытые группы;
6. продолжить допустимую фазу либо перейти к resolution, если forced end уже прошёл;
7. не начислять повторно уже зачтённые убийства/исходы.

Нужен небольшой reconnect grace, чтобы сервер после старта не решил, что все участники мгновенно disconnected, пока клиенты ещё загружаются.

При повреждённом/несовместимом snapshot:

- залогировать причину;
- безопасно очистить event objects/status/forced env;
- пометить событие текущего года resolved/skipped без награды;
- не перезапускать его в этом же году.

---
# 8. Participant lifecycle

## 8.1. Enrollment

- Игроки онлайн в 18:00 получают Marked.
- Игроки, вошедшие с 18:00 до начала resolution, регистрируются в текущем event ID и получают состояние текущей фазы.
- Вошедший во время auto-complete получает текущий automatic display floor, но не получает фиктивный combat contribution.
- После `FreezingEnrollment` новые игроки получают `LateWitness` и не удерживают resolution.
- Игрок, уже получивший терминальный outcome в этом event ID, не вступает повторно после reconnect.

## 8.2. Терминальные личные исходы

Для определения раннего конца терминальными считаются:

- `GoalReached` / Success;
- Death;
- Disconnected.

Игрок с GoalReached остаётся физически в бою и сохраняет полный Bloodlust buff, пока другие участники ещё не завершили цель.

## 8.3. Пустой сервер

Если в 23:00 участников нет:

- не спавнить врагов;
- не запускать early resolution только из-за изначально пустого списка;
- оставить окно открытым для late join до 05:45;
- если никто так и не вошёл, завершить как `Unwitnessed/Resolved` без клиентского fade и наград.

Если хотя бы один игрок был enrolled, а затем все участники получили терминальные outcomes, разрешить событие досрочно.

## 8.4. Disconnect

Disconnect — терминальный исход текущей Blood Moon.

При reconnect в тот же event ID игрок не возвращается в бой. Его накопленный прогресс и будущая skill-награда не теряются.

## 8.5. CombatEntrySnapshot и отложенное решение dream-death

Игрока может застать Blood Moon далеко от базы, в nomap/noportals прохождении, на корабле, в данже или в другом месте, откуда обычный путь к могиле создаёт большой оперативный откат даже без потери навыков.

Окончательная механика смерти пока **не блокирует первый срез**, но фундамент обязан сохранить снимок при переходе участника в `Fighting` и при позднем входе в Active:

```text
eventId
authoritative timestamp
position
rotation
world/dungeon context
attached flag
attached-to-ship flag
последняя известная безопасная grounded-позиция, если доступна
```

Требования к снимку:

- хранить в participant record и transient persistence;
- не использовать имя игрока как ключ;
- не телепортировать и не восстанавливать игрока автоматически в первом срезе;
- первый срез сохраняет vanilla death/respawn flow под `SoftDeath`;
- весь код фиксации исхода смерти проходит через один `ResolveParticipantDeath`/эквивалент, чтобы позднее заменить физический death flow без переделки state machine.

Будущее решение должно рассмотреть «dream collapse»:

- lethal outcome завершает участие;
- не создаётся долгий поход к могиле либо игрок возвращается к валидной точке входа в бой;
- нельзя допустить duplication/loss инвентаря;
- нужен fallback для корабля, воды, воздуха, данжа, attached и невалидной сохранённой позиции;
- необходимо отдельно решить, перехватываются ли только удары blood enemies или любая смерть при Bloodlust.

Пока это открытое продуктовое решение, а не скрытое обещание текущего первого среза.

---
