# Blood Moon — исследование Valheim network, AI и CCS

## 0. Sources

Primary game source:

```text
https://github.com/shudnal/assemblies_combined
```

Исследованы текущие:

```text
ZDO.cs
ZDOMan.cs
ZNetScene.cs
ZSyncTransform.cs
BaseAI.cs
MonsterAI.cs
Character.cs
SpawnSystem.cs
CreatureSpawner.cs
RandEventSystem.cs
OfferingBowl.cs
Pathfinding.cs
```

CCS:

```text
https://github.com/shudnal/ConditionalConfigSync
CustomSyncedValue.cs
Parts/Packages.cs
Parts/Transport.cs
```

Seasons:

```text
Utils/CustomSyncedValuesSynchronizer.cs
Seasons.cs
```

Перед реализацией повторно фиксировать exact commit `assemblies_combined`.

## 1. ZDO ownership and replication

### `ZDO.SetOwner`

- меняет owner;
- увеличивает `OwnerRevision`;
- server/client send logic считает более высокий OwnerRevision причиной отправки.

### `ZDO.SetPosition`

- меняет position;
- переносит ZDO между sectors;
- server вызывает `ZDOSectorInvalidated`;
- увеличивает `DataRevision`, если текущая сторона owner.

Поэтому parking order:

```text
server takes owner
→ server SetPosition
```

а не наоборот.

### ZDO network packet

Server/client отправляет:

```text
ZDOID
OwnerRevision
DataRevision
owner
position
serialized ZDO data
```

### `RPC_ZDOData`

При существующем ZDO:

- incoming `DataRevision <= current`: full data игнорируется;
- higher `OwnerRevision` всё равно может обновить owner;
- incoming `DataRevision > current`: incoming owner/position/data принимаются.

В этом method нет отдельной проверки “sender всё ещё current owner” перед применением higher DataRevision.

Следствие:

> Уже queued update старого owner теоретически может прийти после server ownership transfer и перезаписать первую far position, если его DataRevision выше.

Решение:

- marker-first;
- server takes owner;
- server authoritative write;
- `ForceSendZDO`;
- короткое pending parking confirmation;
- повторно reassert owner/far position/force-send до unload.

Не нужен глобальный transpiler `RPC_ZDOData`.

## 2. Sector invalidation and instance unload

`ZDO.SetPosition` меняет sector.

`ZDOMan.ZDOPeer.ZDOSectorInvalidated`:

- отмечает ZDO invalid для peer, который уже знал объект;
- peer получает invalid-sector list;
- old local copy удаляется из active set.

`ZDOMan.ForceSendZDO`:

- добавляет ZDOID в force-send set всех peers;
- при формировании package ZDO вставляется в начало send list.

`ZNetScene`:

- создаёт instance из ZDO, когда sector входит в active/distant lists;
- удаляет local instance, когда ZDO выходит из lists;
- Persistent ZDO сохраняет;
- nonpersistent owned ZDO может уничтожить целиком при unload.

Следствие:

- far-sector parking подходит persistent boss;
- nonpersistent boss не park;
- raw ZDO остаётся authoritative даже без live Character.

## 3. Dedicated vs listen host

На dedicated server нет local rendered world/Player и обычно нет live Character boss instance.

Production parking не должен требовать `Character`.

На listen/single-player host server и client coexist. Live instance может существовать. После server ownership transfer `ZSyncTransform.OwnerSync` нового local owner способен записать stale local transform в ZDO.

Поэтому:

- raw ZDO path обязателен;
- если local instance существует, синхронизировать его far transform/rigidbody;
- либо удерживать pending parking guard до instance unload.

## 4. ZSyncTransform

Only owner writes:

- position;
- rotation;
- velocity;
- relative parent data.

When ownership becomes local, transform first reads current ZDO.

Special behavior:

```text
transform.y < -5000
→ object rescued to ground
```

Parking uses far XZ + finite normal Y.

## 5. BaseAI owner model

`BaseAI.UpdateAI`:

- returns false if ZNetView invalid;
- non-owner only reads alerted ZDO state;
- full AI runs only on owner.

Therefore:

- behavior patches execute on whichever client owns monster;
- every owner needs public participant routing state;
- global event state via CCS is appropriate;
- no assumption that dedicated server executes monster AI.

## 6. Vanilla hostility

`BaseAI.IsEnemy` covers:

- factions;
- tamed relations;
- Dvergr aggravation;
- PlayerSpawned;
- groups.

Important:

- neutral Dvergr vs Player is not enemy;
- aggravated Dvergr becomes enemy;
- passive AnimalsVeg are not automatically suitable;
- modded MonsterAI using normal faction semantics can be included generically.

Eligibility should call vanilla semantics without recursion through patched result.

## 7. Target acquisition

`BaseAI.FindEnemy`:

- iterates loaded `Character.GetAllCharacters()`;
- requires `IsEnemy`;
- skips dead/`m_aiSkipTarget`;
- requires sensing;
- if no target and `HuntPlayer()`, returns closest Player within 200.

`SetHuntPlayer` writes ZDO `s_huntPlayer`.

Blood Moon should not call it on existing monsters.

Preferred:

- conditional `FindEnemy` override for Blood enemy;
- conditional `HuntPlayer()` true or equivalent without field/ZDO mutation;
- nearest valid active participant.

## 8. MonsterAI.UpdateTarget

It:

- periodically calls `FindEnemy`;
- can replace creature target with `StaticTarget`;
- validates current target through `IsEnemy`;
- gives up after not sensing/chase limits;
- writes target-info bool;
- can call `SetAlerted(false)`.

Required hooks:

- disable static target selection;
- validate targets via Blood policy;
- reacquire active Player;
- prevent give-up while valid participant exists;
- no persistent hunt/alert writes.

## 9. MonsterAI.UpdateAI

Branches that can contradict Blood behavior:

- event creature despawn;
- flee if not alerted;
- flee at low health;
- flee when target unreachable;
- pheromone flee;
- NoMonsterArea flee;
- fire avoidance;
- consume/idle;
- day despawn.

Use narrow conditional hooks/transpilers, not full replacement.

Keep physical hazard behavior where it does not undermine event.

## 10. Alert state

`BaseAI.SetAlerted` writes:

- local field/animator;
- ZDO `s_alert`;
- effects;
- boss bookkeeping.

Prefer runtime override:

- `IsAlerted()` returns true for Blood enemy;
- event VFX separate;
- suppress unnecessary `SetAlerted` writes for existing enemies.

No restore ledger then needed.

## 11. Character damage order

`Character.Damage` invokes owner RPC.

`Character.RPC_Damage` owner path performs:

1. validity/PVP checks;
2. enemy difficulty scaling;
3. SE `OnDamaged`;
4. aggravation/backstab/block/pushback;
5. status effect;
6. resistance/armor;
7. `ApplyDamage`;
8. DoT application.

Blood permission final guard must run before step 2/3.

Early filtering in Attack/Projectile/Aoe is also needed to avoid:

- projectile stopping on forbidden target;
- skill credit;
- status/stagger/push.

## 12. Defeated

`Character.CheckDeath` is the last safe point before virtual `OnDeath`.

Local Player owner can:

- see health <=0;
- restore health;
- skip original;
- notify server.

`Player.OnDeath` must not run because it handles grave/death point/food/skills/respawn.

Server-side HP verification is neither timely nor meaningful in Valheim’s ownership model.

## 13. SpawnSystem ownership

`SpawnSystem.UpdateSpawning` requires:

```text
m_nview owner
Player.m_localPlayer != null
```

Thus owner конкретной зоны выполняет actual spawning для этой зоны.

Не выбирать один peer coordinator на всю hidden group. Если group пересекает несколько зон с разными owners, каждый owner обслуживает только свои зоны.

Server:

- определяет groups/pool/caps;
- выдаёт per-zone lease/budget;
- суммирует reports всех zone owners;
- ограничивает общий group/server cap;
- при ownership migration меняет revision только этой зоны.

`CreatureSpawner` positions are useful authored interior candidates, but calling its `Spawn()` mutates its Spawned connection and alive/respawn bookkeeping. Blood Moon reads positions only.

## 14. CCS behavior

### Normal `CustomSyncedValue`

- state-like;
- equal assignment suppressed;
- source broadcasts to all;
- pending normal updates coalesce to latest;
- custom priority controls batch order;
- late join receives full state;
- JSON string is supported;
- complex value types use package serialization and assembly-qualified type.

### Sequenced

- preserves every assignment, including equal;
- deferred events stored as individual packages;
- queue bounded;
- intended for event pulses, not current state.

Blood Moon has recoverable current state, so normal value is correct.

### Seasons safe assignment queue

`CustomSyncedValuesSynchronizer` is a mod-side assignment coordinator around texture caching and priority. It coalesces latest assignment by target. Blood Moon state can use it where startup ordering intersects existing Seasons initialization, but it is not a network event queue.

## 15. Chosen CCS/RPC split

CCS:

```text
Global snapshot JSON
Public participant routing snapshot JSON
```

Own RPC:

```text
Defeated notification
enemy death report
boss discovery
per-zone spawn lease/owner assignment
zone-owner extra spawn report
targeted progress/reward
fade ACK
resync/debug
```

No `SequencedCustomSyncedValue`.
