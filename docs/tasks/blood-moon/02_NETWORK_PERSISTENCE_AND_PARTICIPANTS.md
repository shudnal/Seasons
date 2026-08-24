# Blood Moon — network, persistence and participants

Обязательная часть `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

## 1. Транспорт: CCS для состояния, RPC для адресных действий

### 1.1. `CustomSyncedValue`: global current state

Использовать обычный CCS `CustomSyncedValue<string>` с versioned JSON snapshot.

Причины:

- server/source-of-truth broadcast всем клиентам;
- current state автоматически приходит late join;
- equal state подавляется;
- pending updates coalesce до последнего состояния;
- JSON позволяет расширять schema без жёсткой зависимости от assembly-qualified DTO type;
- Seasons уже использует этот механизм для текущего сезона/дня и JSON-настроек.

Рекомендуются два public state value.

#### `Blood Moon global snapshot`

```json
{
  "schemaVersion": 1,
  "eventId": 123,
  "revision": 17,
  "phase": "Active",
  "resolutionStep": "None",
  "visualStartSeconds": 0,
  "combatStartSeconds": 0,
  "autoCompleteStartSeconds": 0,
  "forcedEndSeconds": 0,
  "morningTargetSeconds": 0,
  "randEventSuppressed": true,
  "forceEnvironmentActive": true
}
```

#### `Blood Moon participants snapshot`

Минимальный public routing state, нужный владельцам AI и UI:

```json
{
  "schemaVersion": 1,
  "eventId": 123,
  "revision": 31,
  "participants": [
    {
      "playerZdoId": "...",
      "stablePlayerId": "...",
      "phase": "Fighting",
      "goalReached": false,
      "exitReason": "None"
    }
  ]
}
```

Отправлять только при participant transition или редком aggregate update, не на каждый hit.

### 1.2. Почему не `SequencedCustomSyncedValue`

Event state полностью восстанавливается из последнего snapshot + revision.

Нет команды, где повторное равное значение само по себе обязано быть отдельным событием. Поэтому очередь sequenced values:

- не даёт полезной семантики;
- усложняет recovery;
- может накопить лишние переходы;
- не нужна для `Marked`, `Active`, `Resolving`, поскольку клиент может сразу применить актуальную phase.

### 1.3. Собственные RPC

Использовать для:

- local Player → server: `Defeated` notification;
- owner enemy → server: death/kill report;
- client → server: boss discovery report;
- server → selected peer: group spawn-coordinator assignment;
- server → specific Player: personal progress/reward/detail snapshot;
- client → server: fade ACK;
- client → server: explicit resync request;
- admin/debug commands;
- optional targeted diagnostics.

Каждый message содержит минимум:

```text
protocolVersion
eventId
sender/player identity
object/group identity when applicable
message-specific sequence/deduplication key
```

## 2. `Defeated`: client-authoritative local transition

Valheim ожидает, что local Player полностью принадлежит его клиенту.

Поэтому:

1. local owner обнаруживает `health <= 0` в `Character.CheckDeath`;
2. немедленно предотвращает `Player.OnDeath`;
3. применяет local recovery;
4. сохраняет local `Defeated`;
5. отправляет server notification;
6. server принимает terminal transition для этого sender-а;
7. server обновляет public participants CCS snapshot;
8. AI owners перестают выбирать Player целью.

Сервер не проверяет здоровье:

- оно уже client-owned;
- round-trip опоздает относительно `CheckDeath`;
- malicious client может лишь досрочно выйти из собственного события;
- серверная “проверка” не добавит реальной безопасности.

Server checks ограничены identity/event/idempotency.

## 3. Enemy death reports

`Character.OnDeath` выполняется на owner ZDO, который может быть клиентом.

Owner сообщает:

```text
eventId
enemy ZDOID
groupId
attacker Player ZDOID, если доступен
position
```

Server:

- проверяет, что enemy был Blood enemy текущего события;
- дедуплицирует ZDOID;
- определяет server-side point value;
- раздаёт progress eligible group members;
- не принимает от клиента готовое количество points.

Existing и marked extra enemies учитываются одинаково для progress. Различие marker влияет на loot/cleanup, не на trust model.

## 4. Group spawn coordinator

Dedicated server не имеет live `Character` и не выполняет zone `SpawnSystem`.

Server:

1. строит hidden groups по peer/player positions;
2. выбирает один ready peer, владеющий/обслуживающий нужную зону;
3. отправляет targeted assignment:

```text
eventId
groupId
groupRevision
member Player ZDOIDs
anchor position/member
alive cap
server hard-cap remainder
spawn pool revision
```

Selected client:

- запускает scheduler только для назначенной group revision;
- использует локальные `SpawnSystem`, terrain/interior/navmesh данные;
- создаёт extra enemy;
- немедленно ставит `SpawnedEventId`, `GroupId`, `Role`;
- сообщает серверу созданный ZDOID.

Server:

- считает marker-ZDO;
- не даёт нескольким coordinators превысить cap;
- при disconnect/reassignment увеличивает group revision;
- игнорирует report старой revision.

Это повторяет проверенную базовую модель Valheim/Custom Raids:

```text
server decides event
→ zone owner client performs actual spawn
```

## 5. Boss discovery and parking

Dedicated server может иметь только ZDO, без live `Character`.

### Discovery

Client, видящий boss instance, сообщает:

```text
eventId
boss ZDOID
observed InInterior
prefab hash/name для диагностики
```

Server проверяет:

- ZDO существует;
- prefab зарегистрирован;
- prefab/character metadata указывает boss;
- `Persistent == true`;
- ZDO не stale/parked;
- event Active;
- report sender находится в разумной зоне encounter;
- interior evidence согласуется настолько, насколько доступно.

Client не выбирает parking slot и не переносит boss.

### Parking network transaction

Server:

1. записывает marker/schema/original position;
2. `zdo.SetOwner(ZDOMan.GetSessionID())`;
3. увеличенный `OwnerRevision` делает server новым owner;
4. записывает deterministic far position через `SetPosition`;
5. эта запись меняет sector и server invalidates old sector for peers;
6. вызывает `ZDOMan.ForceSendZDO(zdo.m_uid)`;
7. в коротком pending-parking window повторно подтверждает server owner/far position и force-send, пока старый owner не успел принять новую revision и instance не выгрузился.

Причина pending window: `RPC_ZDOData` принимает пакет с большей `DataRevision`; уже поставленный в очередь пакет старого owner теоретически может прийти после первого server update. Server должен выиграть revision race повторным authoritative write, а не патчить весь `RPC_ZDOData`.

На dedicated server live instance обычно отсутствует. Код не должен зависеть от него.

На listen/single-player host server и client живут в одном процессе; live instance может существовать. Если после transfer он стал локальным owner, его transform нужно согласовать с far ZDO либо удерживать parking guard до unload, чтобы `ZSyncTransform.OwnerSync` не записал старый transform обратно.

### Force/sector behavior

- `SetPosition` переносит ZDO между sector collections.
- `ZDOSectorInvalidated` сообщает peers удалить старую sector-копию.
- `ForceSendZDO` ставит ZDO первым в send list.
- После выхода из active/distant areas `ZNetScene` уничтожает local instance.
- Persistent ZDO сохраняется.
- Nonpersistent boss не паркуется.

### Restore

1. server scan всех ZDO с parking marker;
2. server берёт ownership;
3. возвращает `OriginalPosition`;
4. force-send;
5. marker очищает последним;
6. old peer owner не восстанавливает.

Operation idempotent при crash между любыми шагами.

## 6. Persistence

Хранить минимум:

```text
schema/protocol version
eventId and absolute schedule
event phase/resolution step
last started/resolved/skipped event IDs
participant phases
GoalReached
ExitReason
progress/contribution
group IDs/revisions
marked extra enemy IDs или recoverable ZDO markers
parked boss ZDOIDs
revision
```

World-bound state привязан к world UID.

Рекомендуемая модель:

- небольшой persistent world marker последних event IDs/schema;
- atomic sidecar JSON для transient active event;
- ZDO markers — источник восстановления extra enemies и bosses.

## 7. Recovery

### Valid active snapshot

- восстановить phase/schedule/participants;
- сопоставить reconnect stable ID с новым peer/Player ZDOID;
- rebuild groups;
- удалить stale marked extras другого eventId;
- оставить matching parked bosses parked;
- продолжить текущую phase или resolve, если forced end прошёл.

### Missing/corrupt snapshot

- удалить stale marked extras;
- восстановить все parked bosses;
- снять own force environment/VFX/status;
- restore RandEventSystem;
- пометить текущий annual event skipped/resolved без reward;
- никогда не менять Player transform.

### First install

Если Blood Moon впервые включён уже внутри forewarning/final-night window, текущий год пропускается, если admin явно не запустил событие debug-командой.
