# Blood Moon — Blood Craft, ordinary monsters and boss preservation

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

# 27. Blood Craft

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

Не пытаться автоматически восстанавливать прежний комплект ordinary equipment: custom slots и изменённый inventory делают это хрупким. Recovery protection даёт Player время вручную экипироваться.

# 28. Attack attribution

Главное правило — participant phase, не только marker item.

- обычное и Blood Craft оружие `Fighting`/`GoalReached` Player повреждает только Blood enemies;
- projectile/AOE получает `eventId` и source attribution при создании;
- delayed hit не зависит от later equipment/phase/owner;
- ordinary traps/turrets не становятся Blood source;
- combat summon, созданный active Player, может стать Blood entity и удаляется при personal/global exit.

# 29. Existing monsters — принятое прямое преобразование

Suspension+clone не используется.

Существующий eligible non-boss monster становится Blood enemy динамически, пока global event Active:

```text
Blood Moon Active
AND alive Character with MonsterAI
AND not boss/tamed/Players/PlayerSpawned/TrainingDummy
AND BaseAI.IsEnemy(monster, at least one active participant)
```

Не нужен ZDO marker conversion origin.

Следствия:

- existing monster сохраняет ordinary loot;
- ordinary ragdoll/death flow сохраняется;
- killed existing monster не восстанавливается;
- survivor может изменить position/health и после event продолжает существовать;
- max health/shared prefab не мутируются;
- `m_huntPlayer`, persistent alert fields и другие ZDO AI-настройки не меняются Blood Moon;
- target/flee/damage behavior задаётся conditional policy/patches и исчезает при окончании Active;
- cleanup existing survivor не удаляет ZDO и не пытается восстановить pre-event AI history; удаляются только event VFX/transient caches.

Это осознанный компромисс в пользу простоты. Ordinary loot existing creature не считается отдельной event reward; это обычная награда за реально существовавшего монстра.

# 30. Additional spawned enemies

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

# 31. Boss sacrifices с 18:00

Фактический boss altar — `OfferingBowl`.

Блокировать только `OfferingBowl` с `m_bossPrefab != null`:

- `UseItem`;
- `Interact` для item-stand altars;
- `RPC_SpawnBoss` как authoritative race guard.

Offerings не потребляются; уже размещённые item-stand attachments не удаляются.

Если spawn был queued до 18:00, не отменять его после уже совершённого списания offerings. Позволить spawn завершиться; если к этому моменту Active начался и boss persistent/outdoor, немедленно park его. Если boss нельзя park, затронутые encounter Players получают `Withdrawn`.

# 32. Far-sector boss ZDO parking — принято для persistent outdoor boss

## 32.1. Scope

Автоматически park только boss, который:

- жив и загружен/наблюдается в текущем encounter;
- `Character.IsBoss()`/boss prefab подтверждён;
- не `Character.InInterior()`;
- имеет `ZDO.Persistent == true`;
- не имеет актуального parking marker.

Nonpersistent boss не park и не получает временный `Persistent=true`. Если позже появятся обращения по конкретному модовому boss, совместимость рассматривается отдельно.

Player, находящийся в encounter с interior или nonpersistent/unparkable boss, получает terminal `Withdrawn`; Blood Craft очищается, re-entry этой ночью отсутствует. Другие Players/groups продолжают Blood Moon.

## 32.2. Markers

Минимум:

```text
Seasons.BloodMoon.ParkedEventId
Seasons.BloodMoon.ParkingSchema
Seasons.BloodMoon.OriginalPosition
```

Rotation не сохранять и не менять. Для diagnostics допустимы prefab hash и timestamp.

## 32.3. Parking protocol

1. подтвердить alive persistent outdoor boss;
2. записать marker/original position до перемещения;
3. server принимает ownership ZDO;
4. если server имеет live instance, синхронно переместить transform/rigidbody и обнулить velocities;
5. установить ZDO position в deterministic reserved far XZ sector с нормальным finite Y;
6. force-send/sector invalidation, чтобы clients выгрузили instance;
7. не восстанавливать старый peer owner после event.

Не использовать `y < -5000`, поскольку `ZSyncTransform` имеет out-of-world rescue.

Каждый одновременно parked boss получает отдельный slot.

## 32.4. Что требуется сохранить

Корректность определяется минимально:

- тот же boss ZDO не уничтожен;
- marker переживает restart;
- после restore ZDO возвращён в `OriginalPosition`;
- при загрузке arena создаётся валидный boss instance;
- defeat key/loot/death flow не срабатывали во время parking.

Не требуется сохранять или восстанавливать:

- текущую animation state;
- target;
- velocity;
- attack coroutine;
- client-only effects/HUD state;
- runtime-only поля модовых компонентов.

Health/level/custom state сохраняются только в той мере, в которой сам prefab уже хранит их в ZDO. Точная иммерсивная непрерывность boss fight не является требованием.

## 32.5. Restore

Под fade или при fail-safe recovery:

1. server scan всех ZDO с parking marker;
2. принять ownership при необходимости;
3. вернуть `OriginalPosition`;
4. force sync/sector invalidation;
5. очистить marker только после успешного восстановления;
6. optional appearance effect/короткая AI grace допустимы, но не обязательны.

Операции idempotent:

- crash после marker, но до move → restore к original position;
- crash после restore, но до marker clear → повторный restore безопасен;
- stale marker без active event → restore при world startup.

## 32.6. Lingering boss objects

Parking не переносит projectiles, AOE, summons/minions или delayed effects.

Специальный source-aware cleanup для них не реализуется. Они продолжают собственный lifecycle и со временем исчезают/погибают. Общая target/damage policy остаётся защитой от запрещённого взаимодействия, если источник можно определить.

Boss minion, который сам проходит ordinary eligibility predicate, может стать обычным Blood enemy.

## 32.7. Discovery without server live instance

Dedicated server может не иметь live `Character`, даже когда boss instance существует у owner peer.

Spike должен выбрать минимальную схему:

- client/owner report boss ZDOID и `InInterior()` observation;
- server validation prefab/`IsBoss`/`Persistent`/position/event phase;
- authoritative parking выполняет только server;
- client report никогда не задаёт outcome или parking position.

# 33. Interior additional spawn

Existing dungeon monsters автоматически становятся Blood enemies.

Для extras использовать только уже загруженные `CreatureSpawner` как authored candidate positions:

1. spawner относится к той же loaded interior/location области;
2. candidate не слишком близко и по возможности вне прямой камеры;
3. выбранный enemy prefab имеет полный path от candidate до Player;
4. при необходимости допустим небольшой navmesh snap около самого spawner point;
5. не вызывать `CreatureSpawner.Spawn()` и не менять его connection/respawn bookkeeping;
6. не генерировать случайные navmesh points как fallback;
7. если подходящего spawner point нет — extra spawn пропускается.

Появление противника из другой комнаты считается желательным результатом, если существует полный путь.

# 34. No personal parallel world

Explicitly rejected:

- per-client hiding;
- layer-aware ownership;
- invisible hitbox transparency;
- first-contact layer switch;
- clone preservation ordinary monsters.

Все клиенты видят один общий Blood Moon event.
