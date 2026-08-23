# Blood Moon — state and schedule

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

# 3. Архитектурные принципы

## 3.1. Сервер — единственный источник истины

Сервер авторитетно определяет:

- event ID;
- расписание;
- фазу события;
- регистрацию участников;
- состояния участников;
- состав скрытых боевых групп;
- spawn budget и maxAlive;
- принадлежность врага событию;
- Bloodlust combat progress;
- достижение 100%;
- death/disconnect outcome;
- раннее завершение;
- начало и шаги morning resolution;
- итоговый outcome.

Клиент не имеет права самостоятельно объявлять:

- начало/окончание события;
- достижение 100%;
- факт зачтённого убийства без серверной проверки;
- право на награду;
- завершение resolution.

Клиент отвечает за локальное отображение и может сообщать серверу факты, которые из-за ZDO ownership фактически исполняются на владеющем пире. Каждый такой report обязан валидироваться и дедуплицироваться сервером.

## 3.2. Явные state machines, а не набор boolean-флагов

Все переходы должны проходить через централизованные методы с проверкой допустимости, логированием и idempotency.

Не допускать распределённого кода вида:

```csharp
if (isBloodMoon && started && !ending && ...)
```

как основного механизма управления жизненным циклом.

### Event state

Рекомендуемая модель:

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

`Enabled/Disabled` остаётся feature gate конфигурации, а не ещё одним runtime state.

### Participant state

```csharp
internal enum BloodMoonParticipantPhase
{
    None,
    Marked,
    Fighting,
    GoalReached,
    Eliminated,
    Resolved
}
```

Outcome хранить отдельно:

```csharp
internal enum BloodMoonParticipantOutcome
{
    None,
    Success,
    Death,
    Disconnected,
    HiddenAtBase,
    HiddenInWild,
    LateWitness,
    Skipped
}
```

Отдельными полями хранить метаданные вроде `JoinedLate`, `AutoCompleted`, `WasAtBase`, а не раздувать enum взаимоисключающими и не взаимоисключающими признаками.

### Resolution steps

```csharp
internal enum BloodMoonResolutionStep
{
    None,
    FreezingEnrollment,
    StoppingSpawns,
    AwaitingClientFade,
    CleaningEnemies,
    CleaningTemporaryState,
    AdvancingTime,
    PublishingOutcomes,
    ReleasingClients,
    Complete
}
```

Morning resolution должна быть повторно вызываемой и безопасной после timeout/reconnect/restart.

### Боевые группы

Не создавать искусственную state machine `Forming/Merging/Splitting`.

Группа — производная серверная структура, пересчитываемая из активных участников:

```csharp
internal sealed class BloodMoonCombatGroup
{
    internal long Id;
    internal HashSet<long> PlayerIds;
    internal float AliveBudget;
    internal int AliveEnemies;
}
```

В первой итерации достаточно стабильной distance-based группировки и общего cap. Сложные Momentum/Stall и progression pools будут позже.

## 3.3. Идемпотентность

Каждая операция должна быть безопасна при повторе:

- повторный phase delta;
- повторный kill report;
- повторный disconnect callback;
- повторный cleanup;
- повторный клиентский ACK;
- повторное восстановление snapshot;
- старый пакет предыдущего события.

## 3.4. Единая граница Blood Moon interaction layer

Даже в первом срезе не разбрасывать проверки ивентового ZDO/status по отдельным Harmony-патчам.

Нужна единая, дешёвая и тестируемая точка правил, например:

```csharp
internal static class BloodMoonInteractionRules
{
    internal static bool IsEventEnemy(Character character);
    internal static bool IsActiveParticipant(Player player);
    internal static bool CanTarget(Character attacker, Character target);
    internal static bool CanDamage(Character attacker, IDamageable target, HitData hit);
}
```

Точное API можно скорректировать по фактическому коду игры, но смысл обязателен:

- первый срез реализует event enemy → active participant;
- event enemy → всё остальное запрещено и на уровне выбора цели, и как final damage safety;
- существующее оружие участника может использоваться для тестового убийства event enemy в первом срезе;
- не кодировать это как окончательную модель Blood Craft;
- будущий полный параллельный слой добавляет reciprocal player → blood-only routing, visibility и collision filtering через ту же границу;
- полный Blood Craft и персональная реальность должны быть реализованы одним этапом, чтобы UI/предметы, projectiles/AOE, target rules и client visibility не разошлись по разным несовместимым реализациям.

---
# 4. Event ID и версия протокола

Не использовать случайный GUID как основную идентичность ежегодного события.

Детерминированный `eventId` должен однозначно следовать из мирового дня, в котором началась финальная осенняя ночь:

```csharp
long eventId = eventWorldDay;
```

Допускается отдельное поле `protocolVersion`/`schemaVersion`.

`eventId` включать в:

- runtime state;
- persistent snapshot;
- participant records;
- synced DTO;
- RPC payloads;
- ZDO ивентового врага;
- дедупликацию убийств;
- будущие Blood Craft markers;
- будущую запись летописи.

Любой пакет, ZDO или временный объект с несовпадающим event ID считается stale.

---
# 5. Календарь и абсолютное расписание

## 5.1. Осеннее окно

Сохранить исходное продуктовое решение:

- предвестия начинаются с настраиваемого дня осени, по умолчанию 6;
- финальная кровавая ночь назначается на настраиваемый день, по умолчанию 9;
- если заданный финальный день превышает реальную длину осени, использовать последнюю ночь осени;
- событие бывает один раз за год.

Не считать каждый осенний день отдельным Blood Moon.

## 5.2. Время финальной ночи

Принятые значения по умолчанию:

```text
18:00 — начало Marked, блокировка сна/RandEventSystem, visual overlay
23:00 — начало Active, forced environment, SoftDeath, спавн
05:45 — принудительное завершение
06:00 — целевое время после утреннего skip
```

Auto-complete задавать длительностью до forced end, а не хрупким сравнением fraction через полночь. Начальное значение для тестов:

```text
1.5 игровых часа
```

то есть примерно с 04:15 до 05:45.

Все значения конфигурируемые, но хранятся/синхронизируются как понятные игровые часы либо минуты, а внутри превращаются в абсолютные секунды.

## 5.3. Абсолютное время

При создании события один раз вычислять:

```text
visualStartSeconds
combatStartSeconds
autoCompleteStartSeconds
forcedEndSeconds
morningTargetSeconds
```

от авторитетного server world time и длины дня.

Не строить основную state machine на прямых сравнениях `m_smoothDayFraction`.

`m_smoothDayFraction` допустим только для локальной визуальной интерполяции после получения авторитетного schedule.

Переход через полночь должен быть естественной частью абсолютной шкалы времени.

Если игровое время было резко перемотано вперёд, state machine должна пройти пропущенные переходы последовательно или безопасно сразу перейти в resolution, но не оставить событие зависшим.

## 5.4. Заморозка расписания

После создания текущего event record:

- event day;
- effective final autumn day;
- day length;
- все абсолютные timestamps

не меняются от hot reload конфигов. Изменения календарных конфигов применяются со следующего года.

---
