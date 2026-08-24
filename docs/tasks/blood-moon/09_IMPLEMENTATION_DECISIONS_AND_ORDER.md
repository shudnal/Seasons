# Blood Moon — окончательные решения и порядок реализации

Обязательная часть `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

## 1. Решения, которые не пересматриваются без нового требования

### Event

- один раз за игровой год;
- forewarning с осеннего дня 6;
- финальная ночь по умолчанию день 9;
- Marked 18:00;
- Active 23:00;
- forced end 05:45;
- morning 06:00;
- server event state machine;
- deterministic `eventId=eventWorldDay`.

### Мир

- один общий мир для всех клиентов;
- нет personal visibility/collision layers;
- нет `AwaitingContact`;
- нет first-contact gate;
- нет clone preservation existing monsters.

### Existing monsters

- generic dynamic predicate через `MonsterAI`, faction/tamed/boss и vanilla hostility;
- no conversion marker;
- ordinary loot/ragdoll;
- no deletion/restore;
- no persistent AI/shared-prefab mutation;
- runtime policy only.

### Extra enemies

- custom zone-owner spawn;
- `SpawnedEventId` marker;
- no loot;
- fast ragdoll;
- delete surviving/stale marker ZDO.

### Player

- `Defeated` client-owned at `CheckDeath`;
- no `Player.OnDeath`;
- full HP/stamina/eitr;
- food/adrenaline unchanged;
- computed DoT cleanup;
- 15 sec max full protection, then 10 sec 75%;
- no re-entry;
- no transform changes;
- no vanilla SoftDeath.

### Goal

- `GoalReached` independent from later `ExitReason`;
- Success/reward never revoked after 100%;
- GoalReached Player keeps full buff and helps others.

### Boss

- OfferingBowl block from 18:00;
- persistent outdoor boss → far-sector ZDO parking;
- nonpersistent/interior/unparkable boss → affected Player `Withdrawn`;
- no temporary persistence;
- no residual projectile/AOE cleanup;
- restore same ZDO position, not exact runtime state.

### Context

- mount/attached supported;
- ship/ocean supported;
- ordinary interior supported;
- interior extras only from loaded CreatureSpawner positions;
- edge → Withdrawn;
- body blocking remains vanilla.

### Network

- one feature branch `feat/blood-moon`;
- CCS normal CustomSyncedValue for global/public current snapshots;
- no SequencedCustomSyncedValue;
- own RPC for targeted/client-owned operations;
- zone owner client performs extra spawn;
- local Player notification for Defeated, no HP verification.

## 2. Implementation order

Это не набор throwaway MVP. Каждый шаг должен сразу иметь production-compatible API, cleanup и error handling.

### Step 1 — composition root and state

- `BloodMoon/` structure;
- config binding;
- event/participant/resolution state;
- absolute schedule;
- persistence records;
- debug command root;
- cleanup/world unload.

### Step 2 — CCS/RPC

- versioned global JSON snapshot;
- public participant routing JSON snapshot;
- targeted RPC registration;
- identity/eventId/revision/deduplication;
- late join/resync;
- group coordinator assignment.

### Step 3 — calendar, Forewarning, Marked, Active

- annual scheduling;
- safe first-install skip;
- sleep block;
- OfferingBowl block;
- RandEvent suppression;
- phase transitions;
- status text.

### Step 4 — environment and VFX

- Fader clone;
- overlay integration;
- force lease;
- Ashlands clouds;
- cleanup;
- debug visual factors.

### Step 5 — combat groups and extra spawner

- server group graph;
- stable IDs/hysteresis;
- cap accounting;
- coordinator selection/reassignment;
- surface and CreatureSpawner interior candidates;
- markers and stale cleanup.

### Step 6 — existing monster behavior

- generic eligibility;
- central interaction policy;
- BaseAI/MonsterAI target/flee/static-target patches;
- no ZDO hunt/alert mutation;
- VFX;
- hot-reload balance.

### Step 7 — damage/projectile/progress

- early hit candidate filtering;
- final RPC_Damage guard;
- immutable event attribution;
- enemy death reports;
- exactly-once points;
- Bloodlust progress;
- GoalReached behavior.

### Step 8 — Defeated/Withdrawn/recovery

- CheckDeath interception;
- resource restore;
- DoT cleanup;
- recovery status;
- edge handling;
- unsupported boss encounter handling;
- GoalReached + ExitReason model.

### Step 9 — boss parking

- discovery report;
- server validation;
- parking transaction;
- revision-race confirmation window;
- multiple slots;
- restart/stale restore;
- queued OfferingBowl spawn.

### Step 10 — Blood Craft

- recipe UI;
- item marker;
- inventory invariant;
- stack protection;
- external sinks;
- personal/global cleanup;
- projectiles/summons;
- custom slots compatibility.

### Step 11 — skills and chronicle

- RaiseSkill tracking;
- live bonus budget;
- completion reward;
- aliases;
- knownTexts/history;
- DreamText variants.

### Step 12 — final polish

- balance configs;
- Odin observer;
- music hooks/tracks when available;
- localization;
- performance;
- public docs/release only on separate request.

## 3. Commit policy

Logical commits, same branch:

```text
feat/blood-moon
```

Examples:

```text
feat: add Blood Moon state and synchronization
feat: add Blood Moon environment and forewarning
feat: add Blood Moon combat routing
feat: add Blood Moon boss parking
feat: add Blood Craft
feat: add Blood Moon skill rewards
test/docs: finalize Blood Moon diagnostics and context
```

Не создавать дополнительные feature/spike branches.

## 4. Required repository context updates

После каждого существенного решения/ошибки обновлять:

- этот файл, если меняется product/architecture decision;
- `11_VALHEIM_NETWORK_AI_AND_CCS_RESEARCH.md`, если найден новый game behavior;
- `12_RELATED_MODS_RESEARCH.md`, если заимствован/отклонён внешний approach;
- итоговый task/report с current head и continuation point.

## 5. Compatibility policy

- собственные существующие Seasons patterns приоритетнее чужих при равноценной функциональности;
- чужой approach заимствуется, если он доказанно надёжнее/совместимее;
- no hardcoded compatibility unless generic game relationships insufficient;
- unknown modded boss unsupported by default rather than risky generic mutation;
- no JSON enemy catalog unless automatic inference eventually proves impossible;
- patch scope narrow and conditionally active only during Blood Moon.
