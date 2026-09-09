# Blood Moon — state, authority and schedule

Обязательная часть `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

## 1. Серверная авторитетность

Сервер является источником истины для:

- `eventId`;
- расписания и фазы события;
- enrollment;
- публичного participant state;
- hidden combat groups;
- spawn budget/caps/pool revision;
- server-side progress и completion records;
- early/morning resolution;
- suppression `RandEventSystem`;
- boss parking/restore;
- cleanup marked extra ZDO;
- persistence/recovery.

Клиент-владелец Player является источником факта своего локального dream collapse:

- он немедленно перехватывает `Character.CheckDeath`;
- восстанавливает Player;
- переводит себя в local `Defeated`;
- отправляет server notification;
- не ждёт network round-trip, чтобы не допустить `Player.OnDeath`.

Сервер не верифицирует нулевое HP. Он проверяет только:

- sender соответствует собственному Player;
- `eventId` актуален;
- Player был активным участником;
- terminal transition ещё не обработан;
- сообщение не является duplicate/stale.

Это защита целостности состояния, а не античит.

## 2. Event state machine

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

`Enabled` остаётся config gate, а не phase.

Переходы выполняются только централизованными idempotent methods:

```text
Dormant → Forewarning
Forewarning → Marked
Marked → Active
Active → AutoCompleting
Active/AutoCompleting → Resolving
Resolving → Resolved
Dormant/Forewarning/Marked → Skipped
```

Резкий time skip должен либо последовательно выполнить пропущенные переходы, либо безопасно перейти в `Resolving`.

## 3. Participant state

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

```csharp
internal enum BloodMoonParticipantExitReason
{
    None,
    Defeated,
    Withdrawn,
    Disconnected
}
```

Отдельно хранить:

```csharp
bool GoalReached;
bool AutoCompleted;
bool JoinedLate;
BloodMoonParticipantExitReason ExitReason;
```

### Инварианты

- `GoalReached=true` необратимо для текущего `eventId`.
- Completion reward и Success не отменяются поздним `Defeated`, `Withdrawn` или disconnect.
- `Exited` означает прекращение personal participation, но не стирает `GoalReached`.
- `Resolved` означает, что итог опубликован и personal temporary state очищен.
- Re-entry после `Exited` в первой версии отсутствует.

## 4. Resolution state machine

```csharp
internal enum BloodMoonResolutionStep
{
    None,
    FreezingEnrollment,
    StoppingSpawns,
    AwaitingClientFade,
    DisablingBloodBehavior,
    CleaningExtraEnemies,
    RestoringBosses,
    CleaningTemporaryItems,
    RestoringWorldSystems,
    AdvancingTime,
    PublishingOutcomes,
    ReleasingClients,
    Complete
}
```

Каждый шаг повторно вызываем и безопасен после reconnect/restart/timeout.

## 5. Event ID

```csharp
long eventId = eventWorldDay;
```

`eventWorldDay` — мировой день, в котором в 18:00 началась финальная ночь.

`eventId` входит в:

- CCS snapshots;
- RPC;
- participant records;
- marked extra enemies;
- parked bosses;
- Blood Craft items;
- projectile/AOE attribution;
- deduplication;
- persistence;
- chronicle.

Stale marker другого `eventId` не считается частью текущего события.

## 6. Календарь

Defaults:

```text
Forewarning start: autumn day 6
Final Blood Moon night: autumn day 9
Marked: 18:00
Active: 23:00
Auto-complete: 04:15
Forced end: 05:45
Morning target: 06:00
```

Если configured final day превышает длину осени, используется последняя ночь осени.

Событие бывает один раз за игровой год.

## 7. Абсолютное расписание

При создании event record один раз вычислить:

```text
visualStartSeconds
combatStartSeconds
autoCompleteStartSeconds
forcedEndSeconds
morningTargetSeconds
```

Формула строится от:

```text
eventWorldDay
dayLengthSecondsAtEventCreation
```

Переход через полночь выражается одной абсолютной шкалой.

`m_smoothDayFraction` используется только для локальной визуальной интерполяции, не для authority.

## 8. Hot reload

В текущей ночи замораживаются:

- event day;
- effective final autumn day;
- day length;
- absolute timestamps;
- `eventId`.

На лету применяются:

- damage multipliers — следующий hit;
- speed/aggression — следующий AI update;
- spawn interval/radius — следующий scheduler tick;
- group distances — следующий group recompute;
- cap: увеличение разрешает новые spawn; уменьшение не удаляет уже живых;
- pool/weights — будущие spawn;
- VFX intensities — следующий visual update.

Отключение Blood Moon посреди события запускает безопасный `Resolving`, а не просто снимает boolean.
