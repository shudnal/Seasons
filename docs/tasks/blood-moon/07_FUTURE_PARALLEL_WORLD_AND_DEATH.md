# Blood Moon — parallel world, Blood Craft and death

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

> Production-код по этому документу не начинать до закрытия gate из `09_PREIMPLEMENTATION_DECISIONS_AND_SPIKES.md`.

# 22. Связанные архитектурные контракты

## 22.1. Параллельный слой и Blood Craft реализуются согласованно

Эти системы образуют один gameplay contract:

- participant state определяет принадлежность Player к blood layer;
- Blood Craft определяет временные items и attack sources;
- visibility/collision определяет, что локально существует для Player;
- target/damage routing определяет допустимые взаимодействия;
- projectiles/AOE/summons сохраняют `eventId` после создания и owner migration;
- ejection возвращает Player в real-world layer без transform/respawn изменений.

Blood Craft не является обязательным условием самого layer controller, но production-этапы должны использовать одну interaction policy и не создавать параллельные несовместимые модели.

### Финальная матрица

| Источник | Цель | Результат |
|---|---|---|
| `AwaitingContact`/`Fighting`/`GoalReached` Player | blood enemy текущего event ID | видит, сталкивается, hit разрешён |
| blood-layer Player | ordinary enemy, boss, tamed | локально скрыто/неинтерактивно, target/damage запрещены |
| blood-layer Player attack | building, crop, tree, ore, ordinary destructible | world geometry остаётся, damage/resource action запрещены |
| blood-layer Player | другой Player | обычная видимость; damage только через optional PvP |
| blood enemy | blood-layer Player текущего event ID | target/damage разрешены |
| blood enemy | `Ejected`, nonparticipant, ordinary world | target/damage запрещены |
| nonparticipant client | blood enemy | не видит, не сталкивается, не попадает projectile/AOE |
| ordinary enemy | blood-layer Player | target/damage запрещены |
| ordinary enemy | real-world Player/NPC/tamed | зависит от выбранной ordinary-world simulation policy |

Player остаются видимыми друг другу. Real-world observer может видеть, как participant сражается с воздухом.

## 22.2. Не отключать root GameObject из-за локальной невидимости

Client, который не должен видеть entity, всё ещё может быть её ZDO owner и обязан симулировать её для других peers.

Presentation controller должен отдельно управлять:

- Renderer/LOD;
- audio;
- EnemyHud/name;
- local Character/hitbox collider interaction;
- projectile masks;
- AoE acceptance;
- target selection;
- final damage.

Не использовать `GameObject.SetActive(false)`/полное выключение AI как универсальный способ скрытия.

## 22.3. Ownership ordinary entities

### Не использовать устойчивый `SetOwner(0)` parking

Vanilla `ZDOMan.ReleaseZDOS` периодически вызывает `ReleaseNearbyZDOS` и назначает persistent ownerless ZDO ближайшему active peer. Поэтому `SetOwner(0)` без изменения центральной owner-selection logic не создаёт стабильную паузу.

Также ownerless interval создаёт лишние owner revisions, RPC/ownership races и неопределённое поведение нестандартных nonpersistent modded entities.

### Предпочтительный порядок

1. Не менять owner, если это не требуется.
2. Если ordinary entity owned blood participant и рядом есть eligible real-world peer, server может напрямую назначить этого peer owner без промежуточного owner=0.
3. Если eligible peer нет, сохранить current owner.
4. Для сохранения образа «real world застыл» локально suspend ordinary AI на participant owner по выбранной Policy B.
5. После ejection/resolve suspension снимается немедленно.
6. Если suspension окажется хрупкой, fallback — background simulation без owner changes.
7. Не patch-ить глобальный `ZDO.SetOwner`/`ReleaseNearbyZDOS` до отдельного доказанного spike.

Event enemies могут быть owned participant или nonparticipant. Nonparticipant owner продолжает AI simulation, но локально не видит/не сталкивается с entity; central target rules разрешают event enemy атаковать только blood-layer Player.

### Simulation policy

Предпочтительно проверить:

- **Policy A:** ordinary world продолжает background simulation;
- **Policy B:** ordinary entity owned participant suspend-ится, если рядом нет real-world observer; при появлении observer ownership по возможности передаётся ему;
- **Policy C:** глобальный layer-aware owner pool — не рекомендуется без необходимости.

Предварительный выбор — Policy B, fallback — A.

## 22.4. First contact и ejection

В 23:00 Player входит в `AwaitingContact` blood layer без transform changes.

Первый accepted blood interaction переводит в `Fighting` и создаёт `FirstBloodContactRecord`. Запись не является positional backup.

Preferred defeat flow:

- lethal condition перехватывается до `Player.OnDeath`;
- no TombStone/death point/ragdoll/respawn;
- обычный inventory/equipment/food остаются;
- health восстанавливается;
- phase → `Ejected`, outcome → `Death`;
- blood layer отключается;
- real world возвращается в той же position/rotation/velocity;
- применяется короткая grace после отдельного решения.

Никаких автоматических teleport/raycast/ground restore.

## 22.5. Blood Craft доступность

Blood Craft работает во время Marked и blood-layer phases, пока у Player есть соответствующее право/status.

- только известные recipes;
- бесплатно оружие, armour, trinkets, ammo и разрешённые consumables;
- никаких blood drops/event currency;
- временные items не ломаются;
- бесплатный upgrade только временному item;
- постоянный item нельзя бесплатно upgrade;
- употреблённая бесплатная food/mead может оставить эффект после события;
- оставшиеся ammo/consumables/items удаляются.

Blood Craft — временное снятие resource/skill lock-in, а не reward.

## 22.6. Craft/Upgrade UI

Перед реализацией повторно проверить актуальный `assemblies_combined`.

### Кастомная вкладка

Если вкладка не vanilla Craft/Upgrade:

- ничего не менять;
- не добавлять recipes;
- не менять requirements/button/UI другого мода.

### Upgrade

- не дублировать базовый список;
- после формирования `m_availableRecipes` проверять `RecipeDataPair.ItemData`;
- Blood Craft item row подсвечивать приглушённо-красным;
- selected temporary item делает Upgrade button красной;
- upgrade бесплатный;
- marker/owner/event ID сохраняются;
- ordinary item остаётся vanilla.

### Craft

- после vanilla списка известных recipes добавить runtime Blood Craft clone для eligible recipe;
- original permanent recipe остаётся;
- clone имеет отдельную identity и локализуемый marker;
- row/button подсвечиваются;
- requirements бесплатны;
- shared `Recipe` не мутируется;
- clones очищаются при rebuild/close/unload;
- actual craft path валидирует clone identity, а не цвет UI.

Точную Harmony-точку выбрать после проверки совместимости с custom tabs.

## 22.7. Marker и inventory invariant

```text
Seasons.BloodCraft.Schema
Seasons.BloodCraft.EventId
Seasons.BloodCraft.OwnerPlayerId
```

> Blood Craft item существует только в поддерживаемом inventory своего owner и только в соответствующем event ID.

### Drop cleanup

`ItemDrop.Awake` немедленно уничтожает Blood Craft item.

### `Interactable.UseItem`

Временный item нельзя применить к world object/station/stand/container consumer.

Нельзя запрещать player use:

- equip weapon/armour;
- fire ammo;
- drink mead;
- eat food.

### Tombstone/load/external inventories

- при dream collapse TombStone не создаётся;
- при любом fallback vanilla death удалить Blood Craft items до TombStone transfer;
- очищать stale markers при Inventory.Load;
- запрещать container/ship storage/item stand/armour stand/trade/external inventory;
- учитывать сторонние equipment inventories через owner invariant без hard dependency.

### Stack merge

`m_customData` не предотвращает vanilla stack merge. Запрещать объединение:

- temporary + permanent;
- разные event ID;
- разные owner ID.

До реализации сравнить:

1. точечный transpiler/override stack compatibility;
2. временное извлечение temporary items вокруг merge operation;
3. virtual inventory только как fallback.

## 22.8. Damage routing и attribution

Layer принадлежность Player является главным правилом:

- обычное и Blood Craft оружие blood-layer Player повреждает только blood enemies текущего event ID;
- никакие его attacks не повреждают ordinary enemies, bosses, tamed, crops, buildings, trees/ores и nonparticipants;
- projectile/AOE получает event/layer attribution при создании;
- delayed hit не зависит от текущего weapon/status/owner;
- blood enemy projectile/AOE также сохраняет marker после source death/owner migration.

## 22.9. Combat summons

Для полноценной поддержки magic/BloodMagic:

- combat summon, созданный blood-layer Player в Active, становится blood entity текущего event ID;
- атакует только blood enemies;
- невидим/неинтерактивен для real-world Player;
- исчезает при owner ejection/resolve;
- summon/tamed, существовавший до blood layer, остаётся ordinary и скрывается от participant;
- ordinary turret/trap не становится blood entity автоматически.

## 22.10. DOT/status attribution

Vanilla poison/burning и другие persistent effects не имеют достаточной event attribution по одному hash.

До production enemy pool выбрать:

1. первый enemy без persistent DOT/status attacks;
2. blood-specific status clones;
3. source-aware SE tracking.

Для первого combat prototype принят вариант 1. Не удалять глобально Poison/Burning, потому что можно стереть pre-existing real-world effect.

## 22.11. Context policy

До реализации определить минимум:

- outdoor ground;
- dungeon/interior;
- ship/ocean;
- mounted/attached;
- swimming/falling;
- boss encounter;
- portal/teleport;
- late join.

Для context выбрать:

```text
full participation
context-specific blood pool
AwaitingContact без forced engagement
safe skip for this Player
```

Surface-only spawn не является production support.

## 22.12. World interactions

Принято:

- geometry/terrain/buildings остаются видимыми/коллизионными;
- blood attacks не меняют world resources/objects;
- doors/crafting stations остаются usable;
- Player transform/ship position не изменяются модом.

Открыто:

- ordinary ItemDrop visibility/pickup;
- containers;
- ship/mount controls;
- building/placement/terrain tools;
- trader/NPC;
- traps/turrets.

Решение должно сохранять agency и не давать непонятных полуработающих действий.

## 22.13. Re-entry

Первая версия может не иметь re-entry.

Будущий предпочтительный вариант — один temporary owner/event-bound Blood Craft consumable, подготовленный в Marked и используемый из inventory после ejection. Он не требует базы, не меняет world и исчезает утром.

## 22.14. Lifesteal и музыка

Lifesteal отложен до playtest темпа.

Музыка позже может иметь несколько tracks по state. Текущая architecture предоставляет чистые phase transitions, но audio не входит в первый prototype.
