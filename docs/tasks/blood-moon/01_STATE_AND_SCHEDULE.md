# Blood Moon — state and schedule

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

> Production-код по этому документу не начинать до закрытия gate из `09_PREIMPLEMENTATION_DECISIONS_AND_SPIKES.md`.

# 3. Архитектурные принципы

## 3.1. Сервер — единственный источник истины

Сервер авторитетно определяет:

- event ID;
- расписание;
- фазу события;
- регистрацию участников;
- состояния участников;
- первый подтверждённый blood contact;
- состав скрытых боевых групп;
- spawn budget и maxAlive;
- принадлежность врага событию;
- Bloodlust combat progress;
- достижение 100%;
- ejection/death/disconnect outcome;
- раннее завершение;
- начало и шаги morning resolution;
- итоговый outcome.

Клиент не имеет права самостоятельно окончательно объявлять:

- начало/окончание события;
- достижение 100%;
- подтверждённый first contact без серверной проверки;
- факт зачтённого убийства без серверной проверки;
- право на награду;
- завершение resolution.

Клиент-владелец Player или event entity может немедленно применить безопасное provisional local state, если ожидание round-trip создаёт death/visibility race, но обязан отправить report с `eventId`, revision и объектной идентичностью. Сервер валидирует, дедуплицирует и публикует авторитетное состояние.

## 3.2. Явные state machines, а не набор boolean-флагов

Все переходы должны проходить через централизованные методы с проверкой допустимости, логированием и idempotency.

Не допускать распределённого кода вида:

```csharp
if (isBloodMoon && started && !ending && ...)
```

как основного механизма управления жизненным циклом.

### Event state

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
    AwaitingContact,
    Fighting,
    GoalReached,
    Ejected,
    Resolved
}
```

Смысл:

- `Marked` — подготовительная фаза до 23:00;
- `AwaitingContact` — Player уже находится в blood presentation/interaction layer, видит event enemies и может быть ими выбран целью, но первый допустимый blood interaction ещё не подтверждён;
- `Fighting` — подтверждён первый incoming/outgoing blood contact;
- `GoalReached` — combat progress достиг 100%, полный buff остаётся;
- `Ejected` — dream collapse/поражение завершило личное участие, Player снова находится в real-world layer без transform/respawn изменений;
- `Resolved` — итог события полностью опубликован и локальные временные состояния очищены.

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

`Death` описывает результат иллюзорного боя и не требует вызова vanilla `Player.OnDeath`.

Отдельными полями хранить метаданные:

```text
JoinedLate
AutoCompleted
WasAtBase
FirstContactConfirmed
FirstContactRecord
CurrentPeerUid
```

Не раздувать enum взаимоисключающими и не взаимоисключающими признаками.

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

Группа — производная серверная структура, пересчитываемая из combat-capable участников:

```csharp
internal sealed class BloodMoonCombatGroup
{
    internal long Id;
    internal HashSet<long> PlayerIds;
    internal float AliveBudget;
    internal int AliveEnemies;
}
```

В группу могут входить `AwaitingContact`, `Fighting` и `GoalReached`. `Ejected`, `Resolved` и terminal disconnected participants из неё исключаются.

В первой итерации достаточно стабильной distance-based группировки и общего cap. Сложные Momentum/Stall и progression pools будут позже.

## 3.3. Идемпотентность

Каждая операция должна быть безопасна при повторе:

- повторный phase delta;
- повторный first-contact report;
- повторный dream-collapse report;
- повторный kill report;
- повторный disconnect callback;
- повторный cleanup;
- повторный клиентский ACK;
- повторное восстановление snapshot;
- старый пакет предыдущего события.

## 3.4. Единая граница Blood Moon interaction layer

Не разбрасывать проверки event ZDO/status по отдельным Harmony-патчам.

Нужна единая, дешёвая и тестируемая точка правил, например:

```csharp
internal static class BloodMoonInteractionRules
{
    internal static bool IsEventEnemy(Character character);
    internal static bool IsBloodLayerParticipant(Player player);
    internal static bool CanTarget(Character attacker, Character target);
    internal static bool CanDamage(Character attacker, IDamageable target, HitData hit);
    internal static bool IsFirstContactCandidate(HitData hit, Character attacker, Character target);
}
```

Точное API уточняется после spikes, но смысл обязателен:

- event enemy → `AwaitingContact`/`Fighting`/`GoalReached` participant разрешено;
- event enemy → всё остальное запрещено и на уровне выбора цели, и как final damage safety;
- первый подтверждённый contact переводит `AwaitingContact` в `Fighting`;
- существующее оружие участника может использоваться для убийства event enemy;
- будущий полный параллельный слой добавляет reciprocal player → blood-only routing, visibility и collision filtering через ту же границу;
- полный Blood Craft и персональная реальность должны быть реализованы одним согласованным этапом, чтобы UI/предметы, projectiles/AOE, target rules и client visibility не разошлись.

---
# 4. Event ID и версия протокола

Не использовать случайный GUID как основную идентичность ежегодного события.

Детерминированный `eventId` следует из мирового дня, в котором началась финальная осенняя ночь:

```csharp
long eventId = eventWorldDay;
```

Допускается отдельное поле `protocolVersion`/`schemaVersion`.

`eventId` включать в:

- runtime state;
- persistent snapshot;
- participant records;
- FirstBloodContactRecord;
- synced DTO;
- RPC payloads;
- ZDO ивентового врага;
- дедупликацию убийств;
- dream-collapse report;
- будущие Blood Craft markers;
- будущую запись летописи.

Любой пакет, ZDO или временный объект с несовпадающим event ID считается stale.

---
# 5. Календарь и абсолютное расписание

## 5.1. Осеннее окно

- предвестия начинаются с настраиваемого дня осени, по умолчанию 6;
- финальная кровавая ночь назначается на настраиваемый день, по умолчанию 9;
- если заданный финальный день превышает реальную длину осени, использовать последнюю ночь осени;
- событие бывает один раз за год.

Не считать каждый осенний день отдельным Blood Moon.

## 5.2. Время финальной ночи

```text
18:00 — начало Marked, блокировка сна/RandEventSystem, visual overlay
23:00 — начало Active; enrolled Player → AwaitingContact; forced environment и spawn
05:45 — принудительное завершение
06:00 — целевое время после утреннего skip
```

Auto-complete задавать длительностью до forced end. Начальное значение для тестов:

```text
1.5 игровых часа
```

то есть примерно с 04:15 до 05:45.

Все значения конфигурируемые, но внутри превращаются в абсолютные секунды.

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

Если игровое время резко перемотано вперёд, state machine должна пройти пропущенные переходы последовательно или безопасно перейти в resolution, но не оставить событие зависшим.

## 5.4. Заморозка расписания

После создания текущего event record:

- event day;
- effective final autumn day;
- day length;
- все абсолютные timestamps

не меняются от hot reload конфигов. Изменения календарных конфигов применяются со следующего года.
