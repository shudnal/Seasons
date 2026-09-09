# Blood Moon — исследование актуальных похожих модов

## 0. Scope and cutoff

Рассматривались только не deprecated моды, обновлённые не раньше чем за шесть месяцев до Valheim `0.219.13 — The Bog Witch`.

Порог:

```text
Bog Witch public release: 2024-10-29
cutoff: 2024-04-29
```

Основные источники:

- Thunderstore package/version pages;
- публичные GitHub repositories;
- Thunderstore decompiled source как вспомогательный материал.

Hexium не понадобился: для рассматриваемых пакетов достаточно Thunderstore decompile и публичного GitHub source. Decompiled code используется только для архитектурных наблюдений; signatures всегда сверяются с `assemblies_combined`.

Собственные существующие решения Seasons имеют приоритет, если чужой подход не даёт явного преимущества.

## 1. MintTeamRu/BloodMoon 1.0.0

Package:

```text
https://thunderstore.io/c/valheim/p/MintTeamRu/BloodMoon/
version 1.0.0
uploaded 2026-02-04
```

Заявлено:

- event раз в N дней;
- red visuals;
- +1 temporary star;
- increased spawn;
- multiplayer sync;
- dawn revert;
- handling unloaded enemies.

### Наблюдаемый подход

По decompiled source:

- server вычисляет boolean event;
- broadcast simple `BM_SetState`;
- visual controller сохраняет/восстанавливает RenderSettings;
- `Character.Awake` применяет temporary level marker;
- ZDO markers предотвращают повторное star stacking;
- morning revert проходит по loaded characters;
- unloaded entity lazily reverts при следующем `Character.Awake`.

### Полезное

- idempotent ZDO marker;
- lazy repair/revert on entity load;
- duplicate-application guard;
- explicit stale-state cleanup.

### Не заимствовать напрямую

- temporary level mutation противоречит нашей dynamic runtime policy;
- server-side `Character.Awake` assumption не подходит dedicated ownership model;
- simple broadcast без authoritative late-join snapshot/revision слабее CCS;
- direct RenderSettings mutation слабее существующего Seasons EnvSetup pipeline;
- no group/cap/participant state comparable to our design.

### Вывод

Заимствовать только общий принцип:

> persisted temporary mutation обязана иметь event marker и lazy load-time repair.

Для existing Blood enemies mutation отсутствует, поэтому он нужен только marked extras, Blood Craft и parked bosses.

## 2. 1010101110/bloodmoon 0.0.1

Package:

```text
https://thunderstore.io/c/valheim/p/1010101110/bloodmoon/
version 0.0.1
uploaded 2025-05-09
```

Заявлено:

- every N nights;
- darkness change;
- monsters attack;
- spawn around every connected Player;
- multiplayer/config sync.

### Наблюдаемый подход

- local Player update determines active window;
- client-local spawn attempts near Player;
- ZDO marker identifies event enemy;
- cap scales with player count;
- hardcoded prefab selection by biome/global progression;
- `SetHuntPlayer(true)` used;
- direct environment/skybox tint.

### Полезное

- confirms practical client/zone-local spawn;
- immediate ZDO marker after spawn;
- simple cap from players;
- sleep block/event-night timing.

### Не заимствовать

- per-Player independent spawn loops duplicate density in groups;
- hardcoded enemy pool;
- persistent `SetHuntPlayer`;
- direct shared EnvSetup mutation;
- weak recovery/late-join state;
- no distinction existing vs event-created loot.

### Вывод

Borrow:

```text
zone owner spawns
marker immediately
server cap
```

Replace per-player loops with server groups/coordinator.

## 3. ASharpPen/Custom Raids 1.8.1

Package:

```text
https://thunderstore.io/c/valheim/p/ASharpPen/Custom_Raids/
version 1.8.1
uploaded 2026-02-07
repository https://github.com/ASharpPen/Valheim.CustomRaids
```

Это наиболее зрелый релевантный источник.

Документация прямо фиксирует модель Valheim:

```text
server decides raid
→ broadcasts assignment
→ client in charge of zone performs actual spawning
```

### Useful source patterns

#### `OnSpawnPatch`

- transpiler captures actual object created by `SpawnSystem.Spawn`;
- postfix applies modifiers only after instance exists.

Полезно для:

- marker immediately after instantiate;
- post-spawn VFX/role;
- compatibility with vanilla spawn ownership.

#### `PreSpawnFilterPatch`

- prefix filters event spawner list;
- leaves ordinary spawn lists untouched.

Полезно как пример narrow conditional patch.

#### Changelog lessons

- faction modifier reapplied on creature load;
- support player/global-key progression;
- World Advancement Progression compatibility;
- debug dumps map creatures to keys;
- moved modifier application later for CLLC compatibility;
- event spawn owned by client/zone.

### Borrow

- server decision + zone owner execution;
- post-spawn decoration/marker rather than server live Character assumption;
- explicit debug candidate/key dumps;
- apply modifiers at a point compatible with CLLC/other spawn mods;
- narrow event-only filters.

### Do not adopt wholesale

- Blood Moon is not a `RandomEvent`;
- no config-file raid definition needed;
- existing monsters need dynamic policy, not only spawned modifiers;
- our CCS/participant/recovery requirements are different.

## 4. ASharpPen/Spawn That 1.2.18

Package:

```text
https://thunderstore.io/c/valheim/p/ASharpPen/Spawn_That/
version 1.2.18
uploaded 2025-06-08
repository https://github.com/ASharpPen/Valheim.SpawnThat
```

Relevant as a mature spawn compatibility reference.

### Useful lessons

- work with vanilla `SpawnSystem` ownership rather than fighting it;
- separate world spawn and local CreatureSpawner paths;
- apply conditions/modifiers close to actual spawn;
- avoid mutating unrelated spawners;
- explicit compatibility with custom biomes/prefabs.

### Application

Use its architecture as confirmation for:

- coordinator client;
- separate surface/interior source;
- future spawn pool changes affect future spawn only;
- no global per-frame ObjectDB scan.

## 5. Smoothbrain/CreatureLevelAndLootControl 4.6.4

Package:

```text
https://thunderstore.io/c/valheim/p/Smoothbrain/CreatureLevelAndLootControl/
version 4.6.4
uploaded 2025-05-26
```

Relevant because it modifies creature level/loot and commonly interacts with spawn mods.

### Lessons

- do not mutate shared prefab;
- apply event multiplier after creature exists;
- allow CLLC to finish its own level/loot logic first;
- marker-based no-loot must be scoped only to event-created extras;
- existing creatures must retain ordinary CLLC loot/level.

### Application

Harmony ordering/late application around spawn and loot should be compatibility-conscious. Blood Moon effective HP uses damage multiplier, not `SetLevel`/max-health rewrite.

## 6. Deprecated/ignored

`RRRBetterRaids` and similar old raid frameworks were ignored:

- deprecated;
- years out of date;
- pre-cutoff;
- architecture tied to old Valheim versions.

## 7. Final borrowed approaches

Adopted:

1. **Custom Raids:** server selects, zone owner spawns.
2. **Custom Raids/Spawn That:** narrow spawn hooks and post-spawn modification.
3. **Mint BloodMoon:** idempotent event markers and lazy stale repair.
4. **CLLC compatibility:** do not rewrite existing creature level/loot/shared prefab.
5. **All current bloodmoon mods:** sleep/event timing and explicit morning cleanup as basic expectation.

Rejected:

1. server-live-Character assumptions;
2. per-player uncoordinated spawn loops;
3. `SetHuntPlayer(true)` persistent mutation;
4. hardcoded prefab progression catalog;
5. star-level mutation/revert for existing creatures;
6. direct mutation of every EnvSetup/RenderSettings;
7. full `RandEventSystem` integration.

## 8. Priority rule

When external pattern and current Seasons pattern are functionally equivalent:

```text
use Seasons pattern
```

Use external pattern only where it is clearly better:

- zone-owner spawning;
- post-spawn marker hook;
- load-time stale marker repair;
- compatibility-conscious modifier ordering.
