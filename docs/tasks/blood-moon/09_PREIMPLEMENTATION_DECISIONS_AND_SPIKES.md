# Blood Moon — accepted simplifications and remaining preimplementation decisions

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

> **Статус:** production-код пока не начинать. Персональная layer-архитектура и clone preservation ordinary monsters отвергнуты. Выполнить global-event spike и закрыть небольшой набор вопросов ниже.

# 37. Окончательно принятые решения

## 37.1. Общий мир

Нет:

- персональной visibility;
- hidden ordinary monsters;
- cross-layer colliders;
- layer-aware ownership;
- first-contact transition;
- `AwaitingContact`;
- clone original + blood copy.

Все клиенты видят общий набор Blood Moon противников.

## 37.2. Active

В 23:00 Player сразу получает `Fighting`, кроме отдельно решаемого active unparked interior-boss encounter.

Ship/ocean, mount, attached и normal interior не являются общей причиной выхода из события.

## 37.3. Existing monsters

Принято dynamic direct conversion:

- любой eligible existing non-boss MonsterAI считается Blood enemy во время Active;
- existing monster не получает origin marker;
- ordinary loot/ragdoll сохраняются;
- killed existing monster не восстанавливается;
- survivor не удаляется при cleanup;
- event behavior исчезает вместе с global Active;
- permanent max health/shared prefab не меняются.

## 37.4. Extra spawns

Только custom-spawned enemy получает `SpawnedEventId` marker.

- no loot;
- fast ragdoll;
- delete surviving/stale marked ZDO;
- ordinary existing ZDO никогда не удалять этим cleanup.

## 37.5. Defeated

```csharp
BloodMoonParticipantOutcome.Defeated
```

- no `Player.OnDeath`;
- intercept `Character.CheckDeath` at health <= 0;
- no grave/death point/ragdoll/respawn;
- full health/stamina/eitr;
- food/adrenaline unchanged;
- no position/rotation/parent/velocity changes;
- terminal, no re-entry.

## 37.6. No vanilla SoftDeath

Безопасность поражения объясняет собственный status effect.

## 37.7. DoT и recovery

Очищать только:

- `SE_Burning`;
- `SE_Poison`;
- `SE_Smoke`;
- negative-tick `SE_Stats`.

Recovery:

```text
Stage 1: full immunity until IsOnGround || IsSwimming || IsAttached, max 15 sec
Stage 2: 10 sec, incoming multiplier 0.25
```

## 37.8. Withdrawn

До edge/tidal danger использовать `IsBeyondWorldEdge(..., positiveOffset)` и terminal `Withdrawn`.

Body blocking остаётся vanilla.

## 37.9. Boss summon block

С 18:00 boss-producing `OfferingBowl` не принимает новые sacrifices. Guard local paths и authoritative `RPC_SpawnBoss`.

## 37.10. Boss parking

Far-sector server ZDO parking принят как основной кандидат для loaded outdoor persistent boss. Runtime spike должен доказать точный ownership/persistence/restart protocol, но in-place stasis больше не является равноправной продуктовой альтернативой.

# 38. Eligibility ordinary monsters — требуется финальная граница

Фраза «все монстры кроме боссов» технически должна получить predicate.

Рекомендуемая интерпретация:

```text
valid Character + MonsterAI
not Player
not boss
not tamed
hostile to at least one active participant
not trader/named friendly NPC
not neutral non-aggravated Dvergr
not PlayerSpawned/pre-existing summon
not fish/bird/ambient entity
```

Так passive animals и мирные Dvergr не превращаются в источник обычного loot без исходной враждебности.

Открыто:

- aggravated Dvergr;
- unique/named hostile creature;
- hostile modded NPC;
- hostile PlayerSpawned creature;
- creature без обычной faction semantics.

Нужен diagnostic dump inclusion/exclusion reason.

# 39. Far-sector parking — принятые требования и runtime checks

## Обязательный порядок

1. detect loaded alive outdoor boss;
2. записать marker/original position;
3. server takes ZDO ownership;
4. синхронизировать live transform/rigidbody и ZDO far position;
5. zero velocity;
6. force-send/sector invalidation;
7. дождаться unload instance;
8. restore marker-first/idempotent protocol under fade/recovery.

Не использовать very negative Y: `ZSyncTransform` имеет rescue ниже `-5000`.

Не восстанавливать old peer owner.

## Persistent

Основной production support — persistent boss.

Для nonpersistent modded boss открыто:

- temporary `Persistent=true` с original flag;
- либо encounter hold/fallback.

## Interior

Boss с `Character.InInterior()` не park.

Это создаёт отдельный обязательный вопрос, прежде всего для Queen.

# 40. Active unparked interior boss — главный оставшийся продуктовый выбор

Оставить boss обычным, одновременно применяя Blood Moon damage rules, нельзя без противоречий:

- participant attacks по общему правилу должны повреждать только Blood enemies;
- free Blood Craft не должен использоваться для boss progression;
- safe `Defeated` и Bloodlust buff не должны превращать boss fight в бесплатный режим.

Рекомендуемый простой вариант — **Boss Encounter Hold** для затронутой combat group/Player:

- red atmosphere остаётся;
- ordinary boss fight и его damage rules остаются vanilla;
- Bloodlust modifiers/safe Defeated/Blood Craft combat право временно не действуют;
- Blood enemies не спавнятся и existing ordinary monsters не получают Blood behavior относительно этой группы;
- после boss HUD/nearby boss исчезает и проходит stability delay, Player впервые входит или возвращается в `Fighting`, если до 05:45 осталось время;
- это не visibility layer, а временная приостановка личного event participation.

Альтернатива — полностью отложить Blood Moon для такого Player до утра. Нужен выбор владельца после spike Queen/interior.

# 41. Interior custom spawn

Existing dungeon monsters автоматически становятся Blood enemies.

Для additional spawn исследовать:

1. выбрать candidate point вокруг Player в тех же interior coordinates;
2. `Pathfinding.FindValidPoint` с agent type prefab-а;
3. require full `HavePath` до Player;
4. не использовать surface `Heightmap/GetGroundData` path;
5. проверять same interior/high-Y context;
6. по возможности исключать прямую камеру/слишком близкую точку;
7. если valid point нет — не spawn.

Это runtime вопрос, не повод исключать все interiors.

# 42. Event-created AI state

Dynamic policy предпочтительнее persistent field mutation.

Нужно определить минимальный cleanup:

- event VFX;
- event-only target override;
- forced `SetAlerted`/hunt state, если он действительно записывается;
- attack/projectile attribution caches.

Не обещать точное восстановление всей pre-event AI history. Hostile survivor после события может остаться рядом/alerted по обычным правилам.

# 43. Boss residual objects

Parking boss ZDO не переносит автоматически:

- projectiles;
- AOE;
- summons/minions;
- delayed altar invocation.

Нужно выбрать source-aware cleanup/neutralization.

Рекомендуется короткая restore grace для recreated boss, чтобы он не атаковал до окончания fade.

# 44. Balance/runtime only

Не блокируют архитектуру, но требуют playtest:

- group radii/hysteresis;
- extra spawn min/max distance;
- scan/cache interval;
- group/server caps;
- Blood enemy incoming/outgoing multipliers;
- speed/aggression;
- mounted rider reachability;
- sea/interior density.

# 45. Production gate

До production закрыть runtime evidence для:

1. exact eligibility predicate;
2. dynamic direct conversion and ordinary loot;
3. marked extra cleanup;
4. boss ownership transfer and far parking;
5. persistent/restart restore;
6. interior/nonpersistent boss fallback;
7. interior additional spawn;
8. lingering boss attack cleanup;
9. Defeated + recovery;
10. centralized damage routing.

Только `10_GLOBAL_EVENT_RUNTIME_SPIKE.md` может создавать debug code до снятия gate.
