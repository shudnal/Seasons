# Blood Moon — Blood Craft, ordinary monsters and boss preservation

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

# 26. Blood Craft

Blood Craft — не награда, а временный инструмент для тестирования других боевых билдов.

- только already known recipes;
- оружие, armour, trinkets, ammo и разрешённые consumables;
- no blood currency/drop loop;
- доступен в Marked и active personal participation;
- временные items исчезают;
- consumed food/mead effects могут остаться;
- free upgrade только temporary item;
- permanent item нельзя бесплатно upgrade.

## Craft UI

- custom tabs: не вмешиваться;
- Craft: добавить runtime clone eligible recipe рядом с ordinary recipe;
- blood clone имеет отдельную identity, subdued red UI и no requirements;
- shared original recipe не мутировать;
- actual craft validates clone identity;
- Upgrade: не дублировать список; temporary `ItemData` row/button red, upgrade free.

## Marker

```text
Seasons.BloodCraft.Schema
Seasons.BloodCraft.EventId
Seasons.BloodCraft.OwnerPlayerId
```

## Inventory-only invariant

> Blood Craft item существует только в поддерживаемом inventory своего owner и текущего eventId.

- `ItemDrop.Awake` уничтожает dropped temporary item;
- `Interactable.UseItem` не принимает temporary item в world objects;
- запрет container/ship storage/item stand/armour stand/trade/external inventory;
- stale cleanup on `Inventory.Load`;
- fallback vanilla death removes temporary items before TombStone;
- stack merge temporary/permanent или different owner/event запрещён.

## Personal exit cleanup

При `Defeated` или `Withdrawn`:

- немедленно safely unequip и удалить все temporary items Player-а;
- ordinary gear остаётся;
- consumed food/mead effects остаются;
- temporary projectile/AOE/summon owner-а удаляется;
- re-entry отсутствует.

# 27. Attack attribution

Главное правило — participant phase, не только marker item.

- обычное и Blood Craft оружие `Fighting`/`GoalReached` Player повреждает только Blood enemies;
- projectile/AOE получает `eventId` и source attribution при создании;
- delayed hit не зависит от later equipment/phase/owner;
- ordinary traps/turrets не становятся Blood source;
- combat summon, созданный active Player, может стать Blood entity и удаляется при personal/global exit.

# 28. Existing monsters — принятое прямое преобразование

Suspension+clone не используется.

Существующий eligible non-boss monster становится Blood enemy динамически, пока global event Active:

```text
Blood Moon Active
AND IsEligibleExistingMonster(character)
```

Не нужен ZDO marker conversion origin.

Следствия:

- existing monster сохраняет ordinary loot;
- ordinary ragdoll/death flow сохраняется;
- killed existing monster не восстанавливается;
- survivor может изменить position/health и после event продолжает существовать;
- max health/shared prefab не мутируются;
- Blood behavior реализуется conditional policy/patches и исчезает при окончании global Active;
- cleanup existing survivor не удаляет ZDO, а только снимает event VFX и очищает event-created transient target/hunt state, если это необходимо.

Это осознанный компромисс в пользу простоты. Ordinary loot existing creature не считается отдельной event reward; это обычная награда за реально существовавшего монстра.

# 29. Additional spawned enemies

Только специально заспавненные событием объекты имеют marker:

```text
Seasons.BloodMoon.SpawnedEventId
Seasons.BloodMoon.GroupId
Seasons.BloodMoon.Role
```

Marker определяет:

- no ordinary loot;
- fast ragdoll cleanup;
- server-side destruction всех оставшихся marked ZDO при resolution;
- stale cleanup после crash/restart.

Итерировать копию ZDO collection, не изменять source dictionary во время enumeration.

# 30. Boss sacrifices с 18:00

Фактический boss altar — `OfferingBowl`.

Блокировать только `OfferingBowl` с `m_bossPrefab != null`:

- `UseItem`;
- `Interact` для item-stand altars;
- `RPC_SpawnBoss` как authoritative race guard.

Offerings не потребляются; уже размещённые item-stand attachments не удаляются.

Если spawn был queued до 18:00, не отменять его после уже совершённого списания offerings. Позволить spawn завершиться; если к этому моменту Active начался и boss outdoor, немедленно park его.

# 31. Far-sector boss ZDO parking — основной кандидат

## 31.1. Markers

Минимум:

```text
Seasons.BloodMoon.ParkedEventId
Seasons.BloodMoon.ParkingSchema
Seasons.BloodMoon.OriginalPosition
```

Rotation можно не сохранять, если она не меняется. Для диагностики допустимы prefab hash, timestamp и original persistent flag.

## 31.2. Parking protocol

Для каждого loaded active outdoor boss:

1. убедиться, что boss жив, не interior и не parked;
2. записать marker/original position до перемещения;
3. server принимает ownership ZDO;
4. если live instance существует на server, синхронно переместить transform/rigidbody и обнулить velocities;
5. установить ZDO position в зарезервированный far XZ sector;
6. force-send/invalidated-sector sync, чтобы clients быстро выгрузили instance;
7. оставить rotation без изменений;
8. не восстанавливать старый peer owner после event — vanilla ownership handoff выполнится позднее.

Parking position:

- далеко за world edge и всеми active/distant areas;
- finite normal Y;
- не использовать `y < -5000`, поскольку `ZSyncTransform` имеет out-of-world rescue;
- уникальный deterministic slot для каждого одновременно parked boss.

## 31.3. Persistence

Vanilla persistent boss — основной поддерживаемый случай.

Nonpersistent modded boss нельзя безусловно перемещать: owner-side `ZNetScene.RemoveObjects` может уничтожить его ZDO после выхода из active area. Варианты после spike:

- временно force `Persistent=true` с сохранением original flag;
- либо не park unsupported boss и использовать encounter hold/fallback.

Не принимать первый вариант без runtime проверки custom state/restart.

## 31.4. Restore

Под fade или при fail-safe recovery:

1. server scan всех ZDO с parking marker;
2. принять ownership при необходимости;
3. вернуть `OriginalPosition`;
4. force sync/sector invalidation;
5. очистить marker только после успешного восстановления;
6. дать recreated boss короткую AI grace при необходимости;
7. optional appearance effect — только косметика, не условие корректности.

Если crash произошёл между marker и move или между restore и marker clear, повторная операция должна быть idempotent.

Startup policy:

- matching active event → оставить parked;
- no active event/stale event/corrupt snapshot → восстановить все marked bosses до обычной игры.

## 31.5. Что parking не убирает автоматически

Boss instance/HUD/forced event исчезают после выгрузки active instance, но отдельные объекты остаются:

- projectiles;
- persistent AOE;
- boss-created summons/minions;
- altar delayed spawn queue.

Их нужно удалить, нейтрализовать или классифицировать отдельно. Existing eligible minion может стать обычным Blood enemy.

## 31.6. Interior boss

Loaded boss с `Character.InInterior()` не park.

Raw unloaded ZDO не имеет надёжного универсального interior marker; high-Y может использоваться только как conservative diagnostic heuristic, не как единственная истина.

Особенно проверить Queen и modded interior bosses. Политика Player-ов в active unparked interior boss encounter остаётся обязательным решением файла `09`.

## 31.7. Scope

Parking можно переиспользовать только для self-contained persistent transform ZDO.

Не применять автоматически к ships, portals, multi-ZDO assemblies, parented objects или модовым сущностям с runtime-only state.

# 32. No personal parallel world

Explicitly rejected:

- per-client hiding;
- layer-aware ownership;
- invisible hitbox transparency;
- first-contact layer switch;
- clone preservation ordinary monsters.

Все клиенты видят один общий Blood Moon event.
