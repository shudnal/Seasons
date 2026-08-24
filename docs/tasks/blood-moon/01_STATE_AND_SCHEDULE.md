# Blood Moon — state, authority and schedule

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

> Production-код не начинать до закрытия `09_PREIMPLEMENTATION_DECISIONS_AND_SPIKES.md`.

# 3. Серверная авторитетность

Сервер определяет:

- `eventId`, расписание и event phase;
- enrollment и participant state;
- скрытые combat groups;
- global Blood Moon active state;
- eligibility обычных монстров;
- какие ZDO были дополнительно заспавнены событием;
- spawn budget и caps;
- progress, contribution и outcomes;
- `Defeated`, `Withdrawn`, disconnect и Success;
- раннее завершение и morning resolution;
- boss parking/restore;
- cleanup.

Клиент отвечает за UI/VFX/environment и owner-side reports о событиях объектов, реально исполняемых на пире-владельце.

`Defeated` применяется локально до network round-trip, чтобы `Player.OnDeath` не успел сработать, затем сервер валидирует и публикует authoritative outcome.

# 4. State machines

## 4.1. Event

```csharp
internal enum BloodMoonEventPhase
{
    Dormant,
    Forewarning,
    Marked,
    Active,
    AutoCompleting,
    Resolving,
    Resolved,
    Skipped
}
```

`Enabled` остаётся config gate.

## 4.2. Participant

```csharp
internal enum BloodMoonParticipantPhase
{
    None,
    Marked,
    Fighting,
    GoalReached,
    Exited,
    Resolved
}
```

Outcome отдельно:

```csharp
internal enum BloodMoonParticipantOutcome
{
    None,
    Success,
    Defeated,
    Withdrawn,
    Disconnected,
    HiddenAtBase,
    HiddenInWild,
    LateWitness,
    Skipped
}
```

- `Fighting` — Player является допустимой целью, получает Bloodlust modifiers и может повреждать Blood enemies.
- `GoalReached` — 100% достигнуто, полный buff остаётся.
- `Exited` — terminal personal outcome уже зафиксирован.
- `Defeated` не является vanilla death.

Ship/ocean, mount, generic attached и обычный interior сами по себе не меняют phase. Teleport лишь временно исключает Player из spawn-anchor расчёта.

Активный unparked interior boss является отдельным ещё не закрытым encounter exception из файла `09`, а не общей системой персональных слоёв.

## 4.3. Resolution

```csharp
internal enum BloodMoonResolutionStep
{
    None,
    FreezingEnrollment,
    StoppingSpawns,
    AwaitingClientFade,
    CleaningSpawnedEnemies,
    ClearingTemporaryMonsterState,
    RestoringBosses,
    CleaningTemporaryItems,
    AdvancingTime,
    PublishingOutcomes,
    ReleasingClients,
    Complete
}
```

Все операции idempotent.

# 5. Central interaction policy

Одна дешёвая точка правил:

```csharp
internal static class BloodMoonInteractionRules
{
    internal static bool IsEligibleExistingMonster(Character character);
    internal static bool IsBloodEnemy(Character character);
    internal static bool IsBloodMoonSpawned(Character character);
    internal static bool IsActiveParticipant(Player player);
    internal static bool CanTarget(Character attacker, Character target);
    internal static bool CanDamage(Character attacker, IDamageable target, HitData hit);
}
```

`IsBloodEnemy` не требует conversion marker для существующего монстра:

```text
Blood Moon Active
AND eligible non-boss monster
```

Дополнительный marker означает только происхождение:

```text
Seasons.BloodMoon.SpawnedEventId == current eventId
```

Базовая матрица:

| Источник | Цель | Результат |
|---|---|---|
| Blood enemy | Fighting/GoalReached Player | target/damage разрешены |
| Blood enemy | Exited/обычный Player | запрещены |
| Blood enemy | building/tamed/NPC/boss/другой monster | запрещены |
| Fighting/GoalReached Player attack | Blood enemy текущего event | разрешено |
| Fighting/GoalReached Player attack | boss/tamed/building/crop/resource/обычный объект | запрещено |
| Exited Player | Blood enemy | запрещено |
| trap/turret/world source | Blood enemy | по умолчанию запрещено |

Body collision terminal Player-а отдельно не отключается, пока runtime не докажет реальную проблему.

# 6. Event ID и markers

```csharp
long eventId = eventWorldDay;
```

Event ID входит в:

- runtime/persistence;
- snapshots/RPC;
- participant records;
- extra-spawned enemy marker;
- death-report deduplication;
- parked boss marker;
- Blood Craft marker;
- chronicle.

Минимальные extra-spawn markers:

```text
Seasons.BloodMoon.SpawnedEventId
Seasons.BloodMoon.GroupId
Seasons.BloodMoon.Role
```

Минимальные boss parking markers:

```text
Seasons.BloodMoon.ParkedEventId
Seasons.BloodMoon.OriginalPosition
```

Дополнительные fields допускаются для schema/persistence diagnostics.

# 7. Schedule

Defaults:

```text
Forewarning start: autumn day 6
Final event night: autumn day 9
18:00 Marked
23:00 Active
04:15 auto-complete start
05:45 forced end
06:00 morning target
```

При слишком короткой осени финальная ночь = последняя ночь осени.

Вычислять абсолютные timestamps один раз:

```text
visualStartSeconds
combatStartSeconds
autoCompleteStartSeconds
forcedEndSeconds
morningTargetSeconds
```

`m_smoothDayFraction` используется только для локальной визуальной интерполяции.

После создания event record расписание не меняется от hot reload конфигов.
