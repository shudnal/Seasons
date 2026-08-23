# Blood Moon — network, persistence and participants

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

> Production-код по этому документу не начинать до закрытия gate из `09_PREIMPLEMENTATION_DECISIONS_AND_SPIKES.md`.

# 6. CCS CustomSyncedValues и собственные RPC

До реализации сетевого слоя отдельно изучить:

- существующий `Utils/CustomSyncedValuesSynchronizer.cs` в Seasons;
- `CustomSyncedValue<T>` в ConditionalConfigSync;
- `SequencedCustomSyncedValue<T>`;
- очередь sequenced values и её ограничения;
- уже используемые project priorities/queued assignment.

## 6.1. Требование к результату анализа

В коде или отдельной короткой секции итогового отчёта зафиксировать выбранную схему и причины.

Не реализовывать всё собственными RPC, если CCS надёжно решает доставку текущего авторитетного состояния и initial/late-join sync. Одновременно не использовать CCS там, где нужен target-specific response, owner report или client fade ACK.

## 6.2. Рекомендуемая гибридная схема

### CCS current snapshot

Обычный `CustomSyncedValue<string>` или другой поддерживаемый payload для низкочастотного глобального snapshot:

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

`SequencedCustomSyncedValue` допустим для редких упорядоченных server→client transition commands, если фактические гарантии очереди подходят:

```text
Marked entered
Active/AwaitingContact entered
PrepareResolution
PublishOutcome
ReleaseClient
```

Не отправлять через sequenced queue высокочастотный progress каждого Player.

### Собственные RPC

Собственные RPC нужны минимум для:

- client→server owner report о first blood contact;
- client→server owner report о dream collapse/lethal condition;
- client→server owner report о смерти event ZDO;
- targeted snapshot/resync;
- targeted participant progress/outcome;
- client fade ACK;
- диагностического запроса.

Каждый owner report содержит `eventId`, sender/Player identity, object identity и данные, достаточные для server validation. Клиент не передаёт серверу готовое количество очков или итоговый outcome как доверенное значение.

## 6.3. Revision и stale data

Каждый snapshot/delta имеет монотонный `revision` внутри event ID.

Клиент игнорирует:

- меньшую revision;
- устаревший event ID;
- transition, недопустимый из текущей state machine.

При сомнении клиент запрашивает полный snapshot.

## 6.4. Stable identity и peer identity

Participant record обязан различать:

```text
stable profile/player ID
current ZNet peer/session UID
Player ZDOID
```

- stable ID используется для ежегодного outcome/reconnect/persistence;
- peer UID используется для targeted RPC и будущей ownership policy;
- Player ZDOID используется для combat attribution;
- reconnect может изменить peer UID, не создавая нового participant.

## 6.5. Частота

Progress HUD не требует пакета на каждый hit.

Сервер агрегирует изменения и отправляет progress с разумным лимитом, например не чаще четырёх раз в секунду. Клиент может визуально интерполировать подтверждённые значения.

First-contact и collapse transitions отправляются немедленно и дедуплицируются.

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
participant phases/outcomes
stable player ID / last peer UID / Player ZDOID
FirstBloodContactRecord
combat progress
ejection state
auto-complete flag
revision
```

Не сохранять position/rotation как точку автоматического восстановления Player.

Ивентовые враги могут быть пересобраны/очищены по ZDO marker; хранить полный объектный граф не требуется.

## 7.2. World identity

Snapshot привязывается к UID мира, а не только к имени сервера.

Использовать существующий подход проекта, если он есть. Не создавать бесконечный набор global keys по одному на каждый год.

Допустима комбинация:

- маленький world-bound marker текущей схемы/последнего resolved ID;
- атомарный sidecar JSON для transient active state.

## 7.3. Безопасность существующих миров

Если Blood Moon впервые включена уже внутри текущего forewarning/final-night окна, не запускать событие внезапно.

Текущий ежегодный event ID пометить `Skipped`, сохранить решение и ждать следующего года. Debug-команда может явно снять skip для теста.

## 7.4. Рестарт во время события

При валидном snapshot:

1. восстановить event state и schedule;
2. проверить текущее абсолютное время;
3. восстановить participant records;
4. после reconnect сопоставить stable Player ID с новым peer UID/ZDOID;
5. очистить stale event enemies с неправильным event ID;
6. заново построить скрытые группы;
7. продолжить допустимую фазу либо перейти к resolution, если forced end прошёл;
8. не начислять повторно first contact, collapse, kill или outcome.

Нужен reconnect grace, чтобы сервер после старта не решил, что все participants мгновенно disconnected.

При повреждённом/несовместимом snapshot:

- залогировать причину;
- безопасно очистить event objects/status/forced env/layer presentation;
- пометить событие текущего года resolved/skipped без награды;
- не перезапускать его в этом же году;
- никогда не менять Player transform при recovery.

---
# 8. Participant lifecycle

## 8.1. Enrollment и Active entry

- Игроки онлайн в 18:00 получают `Marked`.
- Игроки, вошедшие с 18:00 до начала resolution, регистрируются в текущем event ID.
- В 23:00 eligible Player переходит не сразу в `Fighting`, а в `AwaitingContact`.
- `AwaitingContact` уже входит в blood presentation/interaction layer, видит event enemies и может быть ими выбран целью.
- Первый подтверждённый incoming/outgoing blood interaction переводит Player в `Fighting`.
- Вошедший во время auto-complete получает текущий automatic display floor, но не получает фиктивный combat contribution.
- После `FreezingEnrollment` новые игроки получают `LateWitness` и не удерживают resolution.
- Player с terminal outcome в этом event ID не вступает повторно после reconnect.

## 8.2. FirstBloodContactRecord

При первом допустимом blood interaction сохранить:

```text
eventId
stable player ID
current peer UID
authoritative timestamp
contact kind
blood enemy ZDOID
player position только для диагностики
```

Не сохранять rotation/ground point как restore anchor. Не использовать запись для teleport или forced reposition.

Точное определение contact (`health damage` против `accepted hit including block/parry`) является preimplementation gate. Предварительно рекомендован accepted hit, включая block/parry.

## 8.3. Терминальные личные исходы

Для раннего завершения терминальными считаются:

- `GoalReached` / Success;
- `Ejected` / Death;
- Disconnected.

`GoalReached` Player остаётся в blood layer и сохраняет полный buff, пока другие participants ещё не завершили цель.

`Ejected` Player:

- остаётся в той же position/rotation;
- не создаёт TombStone и не respawn-ится при принятом dream-collapse flow;
- возвращается в real-world presentation;
- больше не является целью event enemies;
- не вступает повторно без отдельной re-entry механики.

## 8.4. Пустой сервер

Если в 23:00 participants нет:

- не спавнить врагов;
- не запускать early resolution только из-за изначально пустого списка;
- оставить окно для late join до 05:45;
- если никто не вошёл, завершить как `Unwitnessed/Resolved` без клиентского fade и наград.

Если хотя бы один Player был enrolled, а затем все получили terminal outcomes, разрешить событие досрочно.

## 8.5. Disconnect

Disconnect — terminal outcome текущей Blood Moon.

При reconnect в тот же event ID Player не возвращается в бой. Накопленный progress и будущая skill-награда сохраняются.

## 8.6. Dream collapse вместо positional restore

Предпочтительный flow фиксируется в `09_PREIMPLEMENTATION_DECISIONS_AND_SPIKES.md`:

- owner-side lethal interception до `Player.OnDeath`;
- health restoration;
- no TombStone/death point/respawn;
- no inventory movement;
- no transform changes;
- server-validated `Death` outcome;
- local/authoritative transition в `Ejected`;
- короткая transition grace после отдельного решения.

До закрытия gate не кодировать vanilla death как окончательную механику и не реализовывать positional backup/return.

## 8.7. Re-entry

Для первой версии допустимо отсутствие re-entry.

Будущий предпочтительный вариант — один owner-bound/event-bound временный Blood Craft consumable, подготовленный в Marked и используемый после ejection без campfire/base/world interaction.
