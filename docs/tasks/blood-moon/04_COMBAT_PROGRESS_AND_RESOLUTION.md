# Blood Moon — combat, AI, progress, defeat and resolution

Обязательная часть `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

## 1. Hidden combat groups

Server-only grouping:

- recompute каждые 3–5 секунд;
- connected components по XZ-distance;
- merge default 120 м;
- split default 160 м;
- stable group ID по максимальному пересечению members;
- group cap от active participant count;
- server hard cap;
- spawn anchor — реальный member, не geometric center;
- map markers отсутствуют.

В группу входят:

```text
Fighting
GoalReached
```

Не входят:

```text
Exited
Resolved
Disconnected
```

GoalReached Player имеет меньший target priority, пока в группе есть Fighting Player.

## 2. Existing monsters: dynamic Blood behavior

Existing Character является Blood enemy, когда:

```text
Blood Moon Active
alive and valid
has MonsterAI
not boss
not tamed
faction not Players/PlayerSpawned/TrainingDummy
vanilla BaseAI.IsEnemy(monster, at least one active participant)
```

Не добавлять conversion marker.

Следствия:

- passive animals и neutral Dvergr остаются обычными;
- aggravated Dvergr включается только когда vanilla считает его врагом;
- hostile starred/unique/modded MonsterAI включается автоматически;
- ordinary loot/ragdoll сохраняются;
- killed existing monster не восстанавливается;
- survivor не удаляется;
- current health/position могут измениться как результат реального боя;
- shared prefab, max health и persistent ZDO AI settings не мутируются.

## 3. Extra spawned enemies

Только event-created enemy получает:

```text
Seasons.BloodMoon.SpawnedEventId
Seasons.BloodMoon.GroupId
Seasons.BloodMoon.Role
```

Marker устанавливается сразу после instantiate и до дальнейшей combat participation.

Для marked extra:

- no ordinary loot;
- ragdoll cleanup, default 2 sec;
- delete surviving ZDO при resolution;
- delete stale marked ZDO при recovery;
- lowering cap не удаляет живых;
- existing unmarked monster никогда не удаляется extra cleanup.

## 4. Zone-owner spawner

Dedicated server не выполняет zone `SpawnSystem`.

Server:

- определяет group/cap/pool;
- выбирает coordinator peer;
- отправляет targeted assignment.

Coordinator client:

- использует локальную surface/interior информацию;
- создаёт extra;
- ставит marker;
- сообщает ZDOID.

### Surface

Использовать собственный scheduler с проверками:

- min/max distance;
- не в камере/слишком близко;
- валидная terrain/nav point;
- игнор обычного PlayerBase/NoMonsters suppression для event extras;
- path feasibility where appropriate.

### Interior

Использовать только позиции загруженных `CreatureSpawner`:

- same interior/location context;
- full path к Player;
- optional small navmesh snap около authored point;
- не вызывать `CreatureSpawner.Spawn()`;
- не менять его ZDO connection/respawn bookkeeping;
- random navmesh fallback отсутствует;
- no valid candidate → no extra spawn.

## 5. Central interaction policy

Одна точка:

```csharp
internal static class BloodMoonInteractionRules
{
    internal static bool IsEligibleExistingMonster(Character character);
    internal static bool IsBloodEnemy(Character character);
    internal static bool IsBloodMoonSpawned(Character character);
    internal static bool IsActiveParticipant(Player player);
    internal static bool CanTarget(Character attacker, Character target);
    internal static bool CanDamage(Character attacker, IDamageable target, HitData hit);
    internal static bool CanReceiveProgress(Character target, HitData hit);
}
```

Не дублировать матрицу в разных patches.

### Разрешено

```text
Blood enemy → Fighting/GoalReached Player
Fighting/GoalReached Player source → current-event Blood enemy
```

### Запрещено

- Blood enemy → building/static target/tamed/NPC/boss/other monster/Exited Player;
- participant attack → building/crop/tree/ore/resource/tamed/NPC/boss/non-Blood enemy/other Player;
- trap/turret/environment → Blood enemy по умолчанию.

Forbidden target не получает:

- damage;
- pushback;
- stagger;
- status effect;
- skill credit;
- aggravation.

## 6. BaseAI/MonsterAI patch architecture

### 6.1. Не использовать persistent state methods

Для existing monsters не вызывать:

```text
SetHuntPlayer(true)
SetAggravated(...)
persistent event-creature flag
permanent faction/level/max-health changes
```

`SetHuntPlayer` и `SetAlerted` пишут ZDO.

### 6.2. Enemy selection

Для blood enemy owner:

- prefix `BaseAI.FindEnemy` может вернуть nearest valid active participant;
- использовать configurable hunt range;
- исключить debug-fly/ghost;
- не искать ordinary enemies/tamed/NPC.

Eligibility должна использовать vanilla hostility без recursion через patched result.

### 6.3. Target update

В `MonsterAI.UpdateTarget`:

- временно подавить `m_attackPlayerObjects`;
- очистить/не разрешить `m_targetStatic`;
- проверить current `m_targetCreature` через central policy;
- при потере цели reacquire valid participant;
- держать `m_lastKnownTargetPos`;
- не позволять обычному give-up permanently завершить hunt, пока valid target существует.

Не копировать весь `UpdateTarget`, если можно выполнить узкие prefix/postfix/transpiler hooks.

### 6.4. Alerted/hunt semantics

Предпочтительно:

- runtime override `HuntPlayer()`/`IsAlerted()` для Blood enemy;
- не записывать `s_huntPlayer`/`s_alert` в ZDO;
- custom Blood VFX показывает состояние;
- после Active condition false, vanilla state продолжает работать.

### 6.5. Flee/idle/no-monster area

Узко обойти для Blood enemy:

- `fleeIfNotAlerted`;
- `fleeIfLowHealth`;
- `fleeIfHurtWhenTargetCantBeReached`;
- Pheromone flee;
- NoMonsterArea / PlayerBase avoidance;
- idle/consume branch при наличии valid participant.

Не обязательно отменять физические hazard decisions вроде выхода из lava, если это не мешает gameplay.

### 6.6. Static targets

Поскольку `MonsterAI.UpdateTarget` отдельно ищет `StaticTarget`, одного patch `BaseAI.IsEnemy` недостаточно. Static-target branch должна быть выключена для Blood enemy.

### 6.7. Speed/aggression

Применять через runtime modifiers:

- movement speed;
- target update interval;
- hunt/sense range;
- attack cadence only if later needed.

Не менять shared item attacks/prefab fields.

## 7. Damage routing

Главный final guard — owner-side `Character.RPC_Damage`.

Guard должен выполняться после owner/attacker resolution, но до:

- `m_seman.OnDamaged`;
- aggravation;
- block/pushback;
- status effect;
- resistance/armor;
- stagger;
- `ApplyDamage`.

### Multipliers

```text
participant → Blood enemy:
    hit *= Blood enemy incoming multiplier

Blood enemy → participant:
    hit *= Blood enemy outgoing multiplier
```

Current config читается на каждый hit.

### Early candidate filtering

Дополнительно фильтровать в:

- `Attack.DoMeleeAttack`;
- `Attack.DoAreaAttack`;
- `Projectile.OnHit`/AOE;
- `Aoe`.

Это нужно, чтобы forbidden collider:

- не считался успешным hit;
- не останавливал projectile;
- не давал skill raise;
- не получал status/stagger/push.

Final `RPC_Damage` guard остаётся defense in depth.

## 8. Projectile/AOE/summon attribution

При первичном spawn записать immutable:

```text
eventId
source type: Participant | BloodEnemy
source Player/Character ZDOID
```

`eventId` не меняется, даже если source Player позднее `Exited`.

Правило:

- пока global event Active и target допустим, delayed projectile/AOE может нанести damage;
- если owner Player уже Exited, damage сохраняется, но progress/skill credit ему не начисляется;
- после global event end target перестаёт быть Blood enemy/participant, поэтому final damage policy блокирует stale Blood source;
- attribution не вычисляется по текущему weapon/phase в момент попадания.

Combat summon, созданный active participant:

- получает immutable event attribution;
- атакует Blood enemies;
- не даёт progress после owner exit;
- удаляется при personal/global cleanup, если он temporary Blood Craft summon.

## 9. Progress

Разделить:

```text
CombatBloodlustPoints
DisplayedBloodlustProgress
CombatContribution
```

Server validates each enemy death exactly once.

Points получают eligible group members в configured share radius. Основной progress не зависит только от last hit.

At 100%:

```text
GoalReached = true
phase = GoalReached
```

- Success фиксируется;
- completion reward сохраняется;
- full buff остаётся;
- Player помогает группе;
- поздний ExitReason хранится отдельно.

### Auto-complete

```text
Displayed = max(combatProgress, automaticFloor)
```

Auto-complete:

- не добавляет combat points;
- не добавляет contribution;
- не создаёт GoalReached/Success;
- не даёт reward.

## 10. Defeated

Owner-side `Character.CheckDeath`:

```text
local Player
phase Fighting/GoalReached
health <= 0
not already Exited
```

Действия:

- skip `Player.OnDeath`;
- no grave/death point/ragdoll/respawn;
- no inventory/food/transform changes;
- health/stamina/eitr → current max;
- food/adrenaline unchanged;
- remove computed damaging DoT;
- set `ExitReason=Defeated`;
- phase → Exited;
- preserve `GoalReached`;
- notify server;
- personal Blood Craft cleanup;
- no re-entry.

### DoT cleanup

Удалять только:

- `SE_Burning`;
- `SE_Poison`;
- `SE_Smoke`;
- `SE_Stats` with `m_tickInterval > 0 && m_healthPerTick < 0`.

Не использовать `RemoveAllStatusEffects`. Unknown modded DoT не удалять эвристически.

### Recovery

Stage 1:

```text
100% incoming protection
until IsOnGround || IsSwimming || IsAttached
max 15 seconds
```

Stage 2:

```text
10 seconds
incoming multiplier 0.25
```

Recovery timer продолжает работать после global morning resolution.

## 11. Withdrawn

Terminal `Withdrawn`:

- edge-of-world safety offset;
- Player в encounter с interior boss;
- Player в encounter с nonpersistent/unparkable boss.

Действия:

- phase → Exited;
- preserve GoalReached;
- personal Blood Craft cleanup;
- снять Bloodlust;
- перестать быть target/progress recipient;
- no re-entry;
- no transform changes.

## 12. Resolution

Early end возможен, когда хотя бы один Player был enrolled и все имеют terminal state:

```text
GoalReached and no unfinished participants
Defeated
Withdrawn
Disconnected
```

GoalReached Player остаётся активным, пока есть Fighting Player.

В 05:45 resolution начинается независимо от progress.

### Prepare

1. freeze enrollment;
2. stop new group assignments/spawn;
3. freeze result records;
4. request client fade;
5. wait ACK with timeout.

### Resolve under fade

1. disable global Blood behavior;
2. delete marked extra ZDO;
3. clear event VFX/transient caches;
4. restore parked bosses;
5. cleanup Blood Craft;
6. remove force environment/clouds;
7. restore RandEventSystem;
8. time → 06:00;
9. remove Rested;
10. publish DreamText/chronicle;
11. release input.

Player transform never changes.
