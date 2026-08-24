# Blood Moon — state, authority and schedule

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

> Production-код не начинать до закрытия `09_PREIMPLEMENTATION_DECISIONS_AND_SPIKES.md`.

# 3. Серверная авторитетность

Сервер определяет:

- `eventId`, расписание и фазу;
- enrollment и participant state;
- supported/unsupported context;
- скрытые combat groups;
- какие существа являются Blood Moon противниками;
- spawn budget и caps;
- progress, contribution и outcomes;
- `Defeated`, `Withdrawn`, disconnect и Success;
- раннее завершение;
- morning resolution;
- boss suspension/restore;
- cleanup.

Клиент отвечает за:

- UI, VFX и собственный environment;
- локальное применение авторитетных participant modifiers;
- owner reports о событиях ZDO, которые реально исполняются на пире-владельце;
- fade ACK;
- provisional `Defeated` до server round-trip, чтобы не успел сработать `Player.OnDeath`.

Любой client report проверяется по `eventId`, sender identity, ZDO identity, revision и допустимому переходу.

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

`Enabled` — config gate, а не runtime phase.

## 4.2. Participant

```csharp
internal enum BloodMoonParticipantPhase
{
    None,
    Marked,
    Deferred,
    Fighting,
    GoalReached,
    Exited,
    Resolved
}
```

- `Deferred` — Active уже начался, но Player находится в неподдерживаемом контексте и ещё не вступил в бой.
- `Fighting` — Player является spawn anchor/target и получает Bloodlust modifiers.
- `GoalReached` — 100% достигнуто, полный buff остаётся.
- `Exited` — terminal personal outcome уже зафиксирован.

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

`Defeated` не является `Player.OnDeath`.

## 4.3. Context

Контекст не смешивать с phase:

```csharp
internal enum BloodMoonParticipantContext
{
    Supported,
    Teleporting,
    Interior,
    ShipOrOcean
}
```

Mounted и generic attached считаются поддерживаемыми и тестируются без forced detach.

Активный boss encounter не является персональным context: boss временно выводится из боя глобальным server mechanism.

## 4.4. Resolution

```csharp
internal enum BloodMoonResolutionStep
{
    None,
    FreezingEnrollment,
    StoppingSpawns,
    AwaitingClientFade,
    CleaningBloodEnemies,
    RestoringOrdinaryState,
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

Одна точка правил:

```csharp
internal static class BloodMoonInteractionRules
{
    internal static bool IsBloodEnemy(Character character);
    internal static bool IsActiveParticipant(Player player);
    internal static bool CanTarget(Character attacker, Character target);
    internal static bool CanDamage(Character attacker, IDamageable target, HitData hit);
    internal static bool CanReceiveBloodDamageSource(Character target, HitData hit);
}
```

Базовая матрица:

| Источник | Цель | Результат |
|---|---|---|
| Blood enemy | Fighting/GoalReached Player | target/damage разрешены |
| Blood enemy | Defeated/Withdrawn/Deferred/обычный Player | запрещены |
| Blood enemy | building/tamed/NPC/boss/ordinary monster | запрещены |
| Fighting/GoalReached Player attack | Blood enemy текущего eventId | разрешено |
| Fighting/GoalReached Player attack | ordinary world, boss, tamed, building, crop | запрещено |
| Defeated/Withdrawn/Deferred Player | Blood enemy | запрещено |
| ordinary world source/trap/environment | Blood enemy | по умолчанию запрещено, чтобы событие нельзя было проходить базовыми ловушками |

Финальный damage guard обязателен даже при AI/attack filtering.

# 6. Event ID

```csharp
long eventId = eventWorldDay;
```

Event ID входит в:

- runtime/persistence;
- snapshots/RPC;
- participant records;
- Blood enemy ZDO markers;
- deduplication;
- boss/original-creature suspension marker;
- Blood Craft marker;
- chronicle.

# 7. Schedule

Defaults:

```text
Forewarning start: autumn day 6
Final event night: autumn day 9
18:00 Marked
23:00 Active
04:15 auto-complete start (1.5 game hours before end)
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

После создания текущего event record расписание не меняется от hot reload конфигов.
