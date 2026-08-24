# Blood Moon — accepted simplifications and remaining preimplementation decisions

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

> **Статус:** production-код пока не начинать. Персональная parallel-world architecture отвергнута. Сначала выполнить global-event spike и закрыть вопросы ниже.

# 35. Окончательно принятые решения

## 35.1. Общий мир

Нет:

- персональной видимости;
- invisible ordinary monsters;
- observers fighting-air UX;
- cross-layer colliders;
- layer-aware owner transfer;
- first-contact transition;
- `AwaitingContact`;
- `SetOwner(0)` parking ordinary entities.

Все клиенты видят одних и тех же Blood Moon противников.

## 35.2. Active start

В 23:00 supported Player сразу получает `Fighting`.

Nearby eligible monsters получают Blood Moon treatment, additional enemies spawn to cap.

## 35.3. Defeated

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

## 35.4. No SoftDeath status

Vanilla `SoftDeath` status не добавляется.

Собственный status effect объясняет:

```text
исчерпание здоровья разрывает кровавую горячку;
могила не создаётся;
навыки не теряются.
```

## 35.5. DoT

Очищать только вычислимо damaging:

- `SE_Burning`;
- `SE_Poison`;
- `SE_Smoke`;
- negative-tick `SE_Stats`.

Unknown modded effects не удалять эвристически.

## 35.6. Recovery

Stage 1:

```text
full immunity
until IsOnGround || IsSwimming || IsAttached
max 15 sec
```

Stage 2:

```text
10 sec
75% reduction
multiplier 0.25
```

Lava remains dangerous after grace.

## 35.7. Withdrawn

До edge/tidal danger использовать existing `IsBeyondWorldEdge(..., positiveOffset)` и terminal outcome:

```csharp
Withdrawn
```

No re-entry.

## 35.8. Context

- interior/dungeon: visuals, no spawn while Deferred;
- ship/ocean: visuals, no spawn while Deferred;
- mounted/attached: supported, no forced detach;
- boss: globally suspended during event;
- teleport: short spawn pause + destination re-evaluation.

# 36. Главный remaining decision: existing monsters

## Option A — direct conversion

Самый простой и прямо соответствует «все монстры вокруг становятся кровавыми».

Need decide whether acceptable:

- real monster can die without loot;
- rare/starred creature may disappear;
- survivor moves/loses health;
- event alters ordinary local ecology.

## Option B — suspend original + event clone

Gameplay выглядит так же, но ordinary state is preserved.

Need prove:

- global suspension/restore;
- no ownership changes;
- no stale collider/renderer/AI;
- nonpersistent/restart behavior;
- acceptable performance.

**Current recommendation:** spike both; use direct conversion only if the world-state tradeoff is explicitly accepted. Use suspension+clone if implementation is stable enough.

# 37. Eligibility predicate

Need close exact rules.

Recommended start:

```text
valid Character + MonsterAI
untamed
non-boss
hostile to at least one active participant
not Dvergr/trader/NPC
not PlayerSpawned/pre-existing summon
not fish/bird/ambient
not already managed
```

Open:

- passive animals;
- aggravated Dvergr;
- modded humanoid NPC;
- summoned hostile entities;
- unique/named creatures;
- starred monsters.

# 38. Boss mechanism

User proposal: save position/rotation, move boss ZDO far beyond world, restore under fade.

Alternative: global in-place stasis.

## In-place stasis advantages

- no ZDO sector move;
- no ZSyncTransform owner race;
- exact state/position preserved;
- same marker/recovery mechanism as suspended originals.

## Parking advantages

- active instance naturally leaves active area;
- fewer component restore details.

## Parking risks

- server ownership transfer;
- owner writing old transform;
- nonpersistent modded boss;
- active-area/sector and restart;
- boss event/HUD/projectile/summon remains.

**Recommendation:** test in-place first, parking second. Neither production-accepted without runtime evidence.

Need also decide:

- block boss altar during event;
- lingering projectiles/AOE;
- boss summons/minions;
- multiple bosses;
- optional reappearance animation.

# 39. Context after Fighting

At Active start unsupported Player is `Deferred` and may join later.

Open choice when already Fighting Player enters dungeon/ship/ocean.

## A. Suspend/resume personal fight

Pros: can return.

Cons:

- temporary free gear enters ordinary content;
- safe-defeat promise overlaps real dungeon/ocean danger;
- more state switching.

## B. Terminal Withdrawn — recommended

- cleanup temporary personal state/items;
- normal world rules;
- no re-entry;
- simplest and clearest.

Teleport itself is not terminal; decide after destination.

# 40. Defeated/Withdrawn interaction with remaining event

All see blood enemies, but terminal Player:

- cannot damage them;
- is not target;
- does not receive progress;
- no Bloodlust buff;
- Blood Craft personal cleanup.

Open minor physics question:

- allow body collision;
- or ignore collision between terminal Player and Blood enemies to prevent body-blocking.

This is much smaller than rejected parallel-layer physics and can be tested independently.

# 41. Blood Craft personal exit

Recommended:

- immediate removal of all temporary items on Defeated/Withdrawn;
- safe unequip;
- ordinary gear remains;
- consumed food/mead effects remain;
- event projectile/AOE/summon of that Player removed.

Need test equipment mods/custom slots.

# 42. Blood enemy sources and scaling

Need settle:

- existing-monster scan radius;
- scan interval;
- conversion/clone cap;
- additional spawn composition;
- incoming multiplier instead of max-health mutation;
- outgoing multiplier;
- speed/aggression;
- whether world hazards damage Blood enemies (recommended no);
- physical collision with ordinary/tamed (damage no; collision can remain unless problematic).

# 43. Boss and random-event ordering

Recommended sequence at 23:00:

1. begin transition/brief combat freeze;
2. stop RandEventSystem;
3. suspend bosses and block new summons;
4. remove/neutralize lingering boss attacks;
5. activate nearby monsters;
6. spawn additional Blood enemies;
7. enter Player Fighting;
8. release control.

Need verify exact order under multiplayer.

# 44. Production gate

Before production:

1. choose direct conversion or suspension+clone;
2. finalize monster predicate;
3. choose boss stasis/parking;
4. decide Fighting→unsupported context;
5. decide terminal-player collision;
6. prove Defeated + recovery;
7. prove target/damage routing;
8. prove mounted/attached;
9. prove restart cleanup;
10. update all docs from spike evidence.

Only `10_GLOBAL_EVENT_RUNTIME_SPIKE.md` may create code before this gate is closed.
