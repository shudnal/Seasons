# Blood Moon — Blood Craft, personal cleanup and world preservation

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
- blood clone имеет отдельную identity, red subdued UI, no requirements;
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
- stale cleanup on Inventory.Load;
- fallback vanilla death removes before TombStone;
- stack merge temporary/permanent или different owner/event запрещён.

## Personal exit cleanup

При `Defeated` или `Withdrawn` Blood Craft право прекращается.

Предпочтительное правило:

- немедленно удалить temporary items этого Player;
- unequip them safely;
- ordinary gear остаётся в inventory и может быть надето вручную;
- consumed food/mead effects остаются;
- temporary projectiles/AOE/summons owner-а удалить.

Это предотвращает использование бесплатной armour/weapons после личного выхода.

# 27. Attack attribution

Главное правило — participant phase, не только marker item.

- обычное и Blood Craft оружие Fighting/GoalReached Player повреждает только Blood enemies;
- projectile/AOE получает `eventId` и source attribution при создании;
- delayed hit не зависит от later equipment/phase/owner;
- Blood Craft marker дополнительно отвечает за временность/owner;
- ordinary traps/turrets не становятся Blood source;
- combat summon, созданный active Player, может стать Blood entity и удаляется при personal/global exit.

# 28. Ordinary monster preservation

Остаётся выбрать:

1. direct conversion;
2. global suspension original + blood clone.

Если используется suspension:

- никакой per-client visibility;
- original скрыт/неинтерактивен одинаково для всех;
- owner не снимается;
- AI/physics/colliders/render state восстанавливаются;
- ZDO marker + event persistence обеспечивают restart cleanup;
- clone получает no-loot event rules.

Если используется direct conversion, это осознанно допускает смерть и перемещение реального ordinary creature во время события.

# 29. Boss suspension

Цель: убрать active bosses из боя на время Blood Moon, сохранив health/ZDO/state.

Сравнить два механизма.

## A. Global in-place stasis — рекомендованный первый кандидат

- mark boss ZDO with eventId;
- globally hide visual/audio/HUD;
- suspend AI/attacks/damage;
- disable combat colliders/physics safely;
- keep position/rotation/owner/ZDO;
- stop conflicting boss RandEvent/environment;
- restore under end fade;
- optional vanilla spawn/appearance effect on restore.

Преимущество: не менять sector/ownership/transform ZDO.

## B. Server ZDO parking

- save original position/rotation and previous owner;
- mark ZDO;
- transfer authoritative control to server if required;
- move to reserved inactive sector beyond world;
- restore marker-recorded transform under fade;
- scan ZDO markers on restart to restore.

Риски:

- owner/ZSyncTransform race;
- active-area/sector behavior;
- nonpersistent modded bosses;
- lingering projectile/summon/event bookkeeping.

## Common requirements

- multiple simultaneous bosses;
- restart during event;
- owner disconnect;
- block new boss summoning during Active;
- clean/neutralize lingering boss projectiles/AOE/summons;
- preserve health, level, defeat keys and location state;
- no boss loot or defeat event during suspension;
- restoration even if no Player remains near arena.

Runtime spike decides A/B/fallback. Far-map parking is not accepted without evidence.

# 30. No personal parallel world

Explicitly rejected:

- per-client hiding ordinary monsters;
- per-client hiding Blood enemies;
- layer-aware ownership pools;
- invisible hitbox transparency;
- `SetOwner(0)` parking;
- first-contact layer switch.

Все игроки видят общий набор Blood Moon противников. Сложность переносится в global event rules, а не в сетевую иллюзию.
