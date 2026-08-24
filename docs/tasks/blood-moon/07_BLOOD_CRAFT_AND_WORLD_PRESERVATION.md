# Blood Moon — Blood Craft, world preservation and boss parking

Обязательная часть `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

## 1. Blood Craft purpose

Blood Craft — временный инструмент события, не награда.

Цель:

- дать попробовать уже известные боевые предметы без resource cost;
- позволить сменить build;
- прокачать непривычный combat skill;
- ничего материального не вынести утром.

Нет blood currency/drop loop. Подготовка должна быть простой и доступной с 18:00.

## 2. Eligible content

Только already known recipes.

Предпочтительные categories:

- weapons;
- shields;
- armour;
- trinkets;
- ammo;
- battle consumables;
- configured food/mead.

Не включать:

- construction pieces;
- stations;
- ships;
- portals;
- progression/quest items;
- world objects;
- recipes, которые Player ещё не знает.

## 3. Craft UI

### Custom tabs

Если active tab не vanilla Craft/Upgrade:

- не менять список;
- не менять requirements;
- не перекрашивать UI;
- не вмешиваться в другой мод.

### Craft

После vanilla list известных recipes:

- original permanent recipe остаётся;
- для eligible recipe добавить runtime Blood Craft clone;
- clone имеет отдельную identity/localized marker;
- row и selected Craft button — subdued red;
- requirements free;
- shared original `Recipe` не мутируется;
- clones очищаются при rebuild/close/world unload;
- actual craft path проверяет clone identity, не цвет UI.

### Upgrade

Список не дублировать.

После формирования `RecipeDataPair`:

- если selected `ItemData` — temporary Blood Craft, row/button red;
- upgrade free;
- marker/owner/eventId сохраняются;
- ordinary permanent item нельзя бесплатно upgrade.

## 4. Item marker

```text
Seasons.BloodCraft.Schema
Seasons.BloodCraft.EventId
Seasons.BloodCraft.OwnerPlayerId
```

Fail-safe:

- stale eventId;
- wrong owner;
- missing/corrupt schema

→ item удалить.

## 5. Inventory-only invariant

> Blood Craft item может существовать только в поддерживаемом inventory своего owner и соответствующего eventId.

### Drop

`ItemDrop.Awake` уничтожает temporary item.

### World sinks

`Interactable.UseItem` и конкретные consumers не принимают temporary item:

- ItemStand;
- ArmorStand;
- Fermenter;
- CookingStation;
- Smelter;
- Catapult;
- другие world objects.

Не блокировать нормальное использование Player:

- equip;
- fire ammo;
- drink;
- eat;
- throw permitted weapon.

### External inventories

Запретить:

- containers;
- ship storage;
- trade;
- tombstone;
- external/custom equipment inventories чужого owner;
- transfer другому Player.

Использовать owner invariant и soft compatibility, а не hard dependencies.

### Stack merge

Vanilla `m_customData` не является безопасной stack boundary.

Запретить:

- temporary + permanent;
- different eventId;
- different ownerId.

Предпочтительно точечно переопределить stack compatibility/merge path. Virtual inventory — только запасной путь.

## 6. Personal cleanup

При `Defeated` или `Withdrawn`:

1. безопасно unequip temporary items;
2. удалить temporary items из всех supported owner inventories;
3. refresh equipment visual;
4. ordinary gear оставить;
5. consumed food/mead effects оставить;
6. temporary Blood Craft projectile/AOE/summon удалить или лишить дальнейшего credit;
7. re-entry отсутствует.

Fallback vanilla death удаляет temporary items до TombStone transfer.

`Inventory.Load` очищает stale markers после crash/Alt+F4.

## 7. Attack attribution

Participant phase является главным правом damage, item marker — правом временности.

- ordinary и Blood Craft weapon active participant повреждает только Blood enemies;
- projectile/AOE записывает immutable `eventId`/source при создании;
- позднее изменение equipment/owner/participant phase не меняет attribution;
- owner exit не отменяет уже летящий damage, но прекращает skill/progress credit;
- global end блокирует stale Blood source через target policy;
- ordinary traps/turrets не становятся Blood source.

## 8. Existing monsters and world state

Existing eligible MonsterAI:

- динамически считается Blood enemy;
- не получает conversion marker;
- сохраняет ordinary loot;
- погибает реально;
- survivor остаётся;
- max health/shared prefab/ZDO hunt state не меняются;
- после event runtime policy просто выключается.

Это осознанный компромисс в пользу совместимости и простоты.

Only event extras имеют `SpawnedEventId` и удаляются.

## 9. Boss sacrifice block

С 18:00 блокировать boss-producing `OfferingBowl`:

- `UseItem`;
- item-stand `Interact`;
- authoritative `RPC_SpawnBoss`.

Только если `m_bossPrefab != null`.

Queued spawn до 18:00 не отменять после item consumption.

## 10. Persistent outdoor boss parking

### Scope

Park только:

- alive boss;
- outdoor/not interior;
- `ZDO.Persistent == true`.

Не park:

- nonpersistent boss;
- interior boss;
- ships/portals/multi-ZDO assemblies;
- unknown object merely resembling boss.

Для unsupported encounter affected Players получают `Withdrawn`.

### Markers

```text
Seasons.BloodMoon.ParkedEventId
Seasons.BloodMoon.ParkingSchema
Seasons.BloodMoon.OriginalPosition
```

Optional diagnostics:

```text
OriginalPrefabHash
ParkingTimestamp
```

Rotation не сохранять, если не меняется.

### Deterministic slot

- far XZ beyond world edge and all active/distant areas;
- finite normal Y;
- не использовать `y < -5000`;
- separate slot per ZDOID to avoid overlap.

### Network-safe parking transaction

1. server validates ZDO/prefab/persistence/interior evidence;
2. write marker + original position first;
3. server `SetOwner(serverSessionId)`;
4. set far position;
5. if listen host has local live instance after ownership transfer, synchronize local transform/rigidbody or guard against stale owner sync;
6. zero body/serialized velocities where available;
7. `ForceSendZDO`;
8. keep pending authoritative parking record briefly;
9. reassert server owner/far position/force-send until old owner has observed higher `OwnerRevision` and object unloads.

Причина: queued old-owner ZDO package with higher `DataRevision` can race the first server write. Reassertion is narrower and safer than global patch `ZDOMan.RPC_ZDOData`.

### Restore

1. scan all ZDO markers from copied collection;
2. server takes owner;
3. set original position;
4. force-send/sector invalidation;
5. clear marker last;
6. do not restore old peer owner.

Correctness requires:

- same persistent ZDO;
- original position;
- health/level/ZDO data preserved;
- no boss death/loot/key.

Не требуется exact:

- animation;
- target;
- velocity;
- coroutine;
- HUD;
- client-only effects;
- runtime-only mod fields.

### Restart

- matching active event → leave parked;
- no active/stale/corrupt event → restore immediately;
- marker-first/clear-last makes operation idempotent.

### Residual objects

Boss projectile/AOE/summon/delayed effects специально не чистятся. Они завершают свой lifecycle.

Boss minion, проходящий generic MonsterAI predicate, может стать Blood enemy.

## 11. Interior/nonpersistent boss

Не park и не force `Persistent=true`.

Affected Player определяется по observed boss HUD/live encounter + server validation.

Outcome:

```text
Withdrawn
```

- Bloodlust/Blood Craft personal cleanup;
- ordinary boss fight vanilla;
- no re-entry;
- другие groups продолжают Blood Moon.

Compatibility с конкретным modded boss добавляется только по обращениям.
