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

Только редкие server→client transitions, если фактические гарантии очереди подходят:

```text
Marked
Active
PrepareResolution
PublishOutcome
ReleaseClient
```

## Собственные RPC

- owner→server extra-spawned enemy death report;
- owner→server provisional `Defeated` report;
- owner/client→server boss-presence report, когда server не имеет live instance;
- targeted participant state/progress;
- snapshot/resync;
- fade ACK;
- diagnostics.

Progress не отправлять на каждый hit; разумный лимит около четырёх обновлений в секунду.

Boss-presence report является low-trust фактом наблюдения. Сервер проверяет ZDOID, prefab, `IsBoss`, `Persistent`, позицию/контекст и допустимость текущей фазы; клиент не выбирает способ parking и не сообщает готовый outcome.

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
participant phase/outcome
progress/contribution/goalReached
parked boss ZDOIDs
extra-spawned enemy IDs or recoverable ZDO markers
Defeated/Withdrawn flags
revision
```

Не хранить position/rotation Player как restore anchor.

World marker привязан к world UID. Transient active state — atomic sidecar JSON или существующий безопасный механизм проекта.

Первое включение внутри текущего forewarning/final-night window пропускает текущий год, если admin явно не запустил debug event.

При corrupted snapshot:

- удалить stale extra-spawned Blood Moon ZDO;
- восстановить все ZDO с boss parking marker по `OriginalPosition`;
- очистить временный VFX/status/force environment;
- пометить текущий event skipped/resolved без reward;
- никогда не перемещать Player.

Nonpersistent boss не входит в parking persistence: он остаётся в обычном мире, а затронутые им participants получают `Withdrawn`.

# 11. Participant lifecycle

## Marked

Игроки онлайн с 18:00 получают `Marked`. Late join до resolution регистрируется в текущем event.

С 18:00 запрещаются новые boss sacrifices. Уже принятый до 18:00 delayed summon не должен терять offerings: он завершается; если созданный boss persistent/outdoor и Active уже начался, его паркуют.

## Active start

В 23:00 enrolled Player сразу переходит в `Fighting`, если он не находится в encounter с boss, который нельзя park.

- mounted/attached не отсоединяются;
- ship/ocean остаётся полноценным участием, но land-spawner может не найти поверхность;
- обычный interior остаётся полноценным участием; existing dungeon monsters становятся Blood enemies, extras используют только подходящие позиции загруженных `CreatureSpawner`;
- teleport временно исключает Player из spawn-anchor расчёта, затем состояние продолжается в destination;
- encounter с interior boss или nonpersistent/unparkable boss приводит к terminal `Withdrawn` для затронутого Player.

Нет `AwaitingContact`, `Deferred` или персональной layer-видимости.

## Boss encounter detection

Продуктовое правило принято, техническая валидация проверяется spike:

- client `EnemyHud`/live `Character` может сообщить boss ZDOID;
- server подтверждает nearby/loaded boss и его parking eligibility;
- если boss persistent и outdoor — park;
- если boss interior, nonpersistent или parking transaction отклонён — affected Player получает `Withdrawn`;
- другие группы/игроки продолжают Blood Moon.

## Terminal outcomes

Для early completion:

```text
Success / GoalReached
Defeated / Exited
Withdrawn / Exited
Disconnected
```

`goalReached` хранить отдельно от последующей причины выхода. Рекомендованная семантика: если Player уже достиг 100%, последующий `Defeated`, `Withdrawn` или disconnect завершает его участие, но не отнимает зафиксированный Success и полный completion reward.

## Defeated

Локальный owner перехватывает `Character.CheckDeath` до `Player.OnDeath`, затем сервер подтверждает outcome.

Не создаются:

- death point;
- death effects/ragdoll;
- TombStone;
- respawn;
- inventory/food changes.

После personal exit:

- Player больше не является целью;
- не может повреждать Blood enemies;
- не получает progress;
- Bloodlust modifiers снимаются;
- future Blood Craft items/projectiles/summons этого Player очищаются;
- re-entry отсутствует.

Body blocking не запрещается специально.

Recovery protection является отдельным конечным личным состоянием и может пережить global morning resolution до собственного истечения. Disconnect/reconnect во время этого короткого окна должен иметь явно проверенную политику; предпочтительно сохранять server timestamp окончания либо безопасно завершать protection при reconnect.

## Empty server

Если в 23:00 никого нет, event остаётся доступным для late join до 05:45. Если никто не вошёл — `Unwitnessed/Resolved`, без клиентского fade/reward.
