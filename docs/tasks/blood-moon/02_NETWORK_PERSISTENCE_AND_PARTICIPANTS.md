# Blood Moon — network, persistence and participants

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

# 8. CCS и RPC

До production implementation изучить:

- `Utils/CustomSyncedValuesSynchronizer.cs`;
- CCS `CustomSyncedValue<T>`;
- `SequencedCustomSyncedValue<T>`;
- queue semantics.

Предпочтительная гибридная схема:

## CCS snapshot

Низкочастотный self-contained snapshot:

```text
protocolVersion
eventId
revision
phase
schedule
resolutionStep
suppression/environment flags
```

## Sequenced channel

Только редкие server→client transitions, если гарантии очереди подходят:

```text
Marked
Active
PrepareResolution
PublishOutcome
ReleaseClient
```

## Собственные RPC

- owner→server blood-enemy death report;
- owner→server `Defeated` report;
- targeted participant state/progress;
- snapshot/resync;
- fade ACK;
- diagnostics.

Progress не отправлять на каждый hit; разумный лимит около 4 обновлений/сек.

# 9. Identity

Различать:

```text
stable profile/player ID
current peer/session UID
Player ZDOID
```

Stable ID нужен для persistence/outcome, peer UID — для RPC, ZDOID — для combat attribution.

# 10. Persistence

Хранить минимум:

```text
schema/protocol version
eventId and schedule
event phase/resolution step
last started/resolved/skipped IDs
participant phase/outcome/context
progress/contribution
boss suspension records
managed ordinary/blood entity IDs
Defeated/Withdrawn flags
revision
```

Не хранить position/rotation Player как restore anchor.

World marker привязан к world UID. Transient active state — atomic sidecar JSON или существующий безопасный механизм проекта.

Первое включение внутри текущего forewarning/final-night window пропускает текущий год, если admin явно не запустил debug event.

При corrupted snapshot:

- cleanup blood objects and markers;
- restore bosses/ordinary suspended state;
- clear own force environment;
- mark current event skipped/resolved without reward;
- never move Player.

# 11. Participant lifecycle

## Marked

Игроки онлайн с 18:00 получают `Marked`. Late join до resolution регистрируется в текущем event.

## Active start

В 23:00:

- supported Player → `Fighting`;
- interior/dungeon или ship/ocean → `Deferred`;
- mounted/attached Player → `Fighting`;
- boss globally suspended before ordinary combat starts.

Нет `AwaitingContact`.

`Deferred` не получает safe-defeat promise и не является target/spawn anchor. При выходе в supported context до forced end он может впервые перейти в `Fighting`.

## Leaving supported context after Fighting

Это остаётся preimplementation decision.

Рекомендуемый простой вариант:

- brief teleport transition только приостанавливает spawner;
- если destination = interior/dungeon или ship/ocean, participant получает terminal `Withdrawn`;
- Blood Craft personal cleanup выполняется;
- re-entry отсутствует.

Так бесплатная экипировка и dream-collapse protection не переносятся в обычный dungeon/ocean gameplay.

## Terminal outcomes

Для early completion:

```text
Success / GoalReached
Defeated / Exited
Withdrawn / Exited
Disconnected
```

`Deferred` не terminal и может удерживать событие до утра, позволяя игроку добраться до поддерживаемого контекста.

## Defeated

Локальный owner перехватывает `Character.CheckDeath` до `Player.OnDeath`, затем сервер подтверждает outcome.

Не создаются:

- death point;
- death effects/ragdoll;
- TombStone;
- respawn;
- inventory/food changes.

## Re-entry

В первой версии отсутствует.

## Empty server

Если в 23:00 никого нет, event остаётся доступным для late join до 05:45. Если никто не вошёл — `Unwitnessed/Resolved`, без клиентского fade/reward.
