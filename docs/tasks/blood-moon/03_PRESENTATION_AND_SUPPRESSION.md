# Blood Moon — presentation and suppression

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

> Production-код по этому документу не начинать до закрытия gate из `09_PREIMPLEMENTATION_DECISIONS_AND_SPIKES.md`.

# 9. Forewarning и Marked

## 9.1. Forewarning

Текстового сообщения недостаточно. Игрок должен заранее заметить, что с осенью происходит что-то неправильное, даже если впервые не понимает точных правил.

В первом срезе нужен минимум один не текстовый внутриигровой признак на каждой ночи configured forewarning range:

- тот же Blood Moon environment overlay и/или cloud VFX на низкой интенсивности;
- эффект только ночью;
- интенсивность растёт к последней ночи перед финальной;
- forced environment не включается;
- нет combat modifiers, spawn, SoftDeath или блокировки сна;
- сила эффекта хранится в одном настраиваемом месте;
- текст/DreamText допустимы только как дополнительный слой.

Стартовая формула:

```text
lastForewarningDay = effectiveFinalEventDay - 1
forewarningDayFactor = InverseLerp(firstForewarningDay, lastForewarningDay, currentAutumnDay)
nightFactor = 0..1..0 в пределах ночи
visualFactor = forewarningDayFactor * nightFactor * lowForewarningCap
```

`lowForewarningCap` должен быть заметно слабее финального перехода 18:00–23:00.

Не превращать forewarning в tutorial. Первый Blood Moon может застать игрока врасплох и закончиться неудачно; цикличность Seasons позволяет лучше подготовиться в следующий год.

## 9.2. Marked в 18:00

При входе в Marked:

1. зарегистрировать текущих игроков;
2. остановить и подавить `RandEventSystem`;
3. заблокировать сон;
4. добавить/обновить Blood Moon status effect;
5. начать линейный visual blend;
6. синхронизировать authoritative state;
7. залогировать transition один раз.

Status tooltip должен объяснять минимум:

- в 23:00 начнётся blood-layer фаза;
- сон недоступен;
- смерть/поражение не отнимет навыки;
- в будущем Marked также даст Blood Craft preparation.

Не обещать Blood Craft в пользовательском тексте до его фактической реализации.

## 9.3. Active в 23:00: `AwaitingContact`

В 23:00 Player не переводится сразу в `Fighting`.

- participant phase → `AwaitingContact`;
- blood presentation/interaction layer включается;
- ordinary entities скрываются/изолируются только после реализации согласованного layer controller;
- event enemies могут видеть и выбирать такого Player целью;
- Blood Moon status показывает 0%/ожидание первого контакта;
- первый допустимый incoming/outgoing blood interaction переводит Player в `Fighting`;
- transform Player не меняется.

Точное условие first contact закрывается отдельным preimplementation решением.

---
# 10. RandEventSystem

Blood Moon реализуется полностью собственной системой. Не создавать и не подменять `RandomEvent`.

## 10.1. Suppression window

С 18:00 (`Marked`) до полного завершения morning resolution:

- остановить текущее событие RandEventSystem;
- не позволять запускать новые random events;
- не позволять запускать/продолжать forced events RandEventSystem в части, конфликтующей с Blood Moon;
- не позволять `RandEventSystem` переопределять environment/music/event state Blood Moon.

В начале Marked сервер должен корректно остановить существующее ordinary random event через штатные методы, а не только обнулить визуальный указатель.

Поведение active boss/event-zone forced state отдельно входит в preimplementation gate: нельзя уничтожать boss или необратимо портить его bookkeeping. После resolution вернуть систему в обычный режим, но не форсировать немедленный запуск нового raid.

## 10.2. Patch policy

Патчи должны быть узкими и включаться только пока authoritative Blood Moon suppression active.

Не оставлять глобально отключённый RandEventSystem при исключении, reload или смене мира. Cleanup обязан быть идемпотентным и присутствовать в world-unload paths.

## 10.3. Карта

Не рисовать raid circles, восклицательные знаки и другие map markers.

Группировка — скрытая механика, ощущаемая только по плотности и составу боя.

---
# 11. Визуальная среда

Визуальная часть изолируется в отдельном компоненте/namespace. Не добавлять сторонние ассеты.

## 11.1. Blood Moon EnvSetup

После доступности `EnvMan`:

1. найти ванильный environment `Fader`;
2. создать отдельную копию, не мутируя оригинал;
3. дать уникальное внутреннее имя, например `Seasons_BloodMoon`;
4. для каждого поля типа `Color` установить `color.r = 1.0f`, сохранив остальные компоненты;
5. установить:

```csharp
m_windMin = 1f;
m_windMax = 2f;
m_sunAngle = 70f;
```

6. зарегистрировать environment один раз на world/session;
7. корректно удалить/разрегистрировать копию при unload.

Значения уже проверены владельцем мода в игре.

## 11.2. Плавный overlay 18:00–23:00

До 23:00 не форсировать новый environment.

```text
18:00 factor = 0
23:00 factor = 1
```

Коэффициент первого варианта линейный по авторитетному schedule.

Интегрировать overlay в существующий transient patch `EnvMan.SetEnv`:

```text
original current environment
→ existing seasonal luminance modifications
→ Blood Moon transition toward copied target environment
→ vanilla SetEnv
→ restore all original fields
```

Blood Moon overlay не зависит от `controlLightings` или texture controllers.

Сохранять/восстанавливать минимум:

```text
m_windMin
m_windMax
m_sunAngle
```

Не выполнять reflection по всем Color fields каждый кадр.

## 11.3. Forced environment с 23:00

В `Active`:

- прекратить старую погоду;
- установить Blood Moon environment через force-environment mechanism;
- держать до resolution;
- не позволять дождю/снегу/обычным override ухудшать видимость и бой.

Реализовать ownership/lease:

- сохранить предыдущее force environment;
- считать Blood Moon владельцем только пока текущее значение равно собственному имени;
- при cleanup не затирать значение, изменённое другим модом после нас;
- восстановить допустимое предыдущее значение либо очистить force, если его не было.

## 11.4. Ashlands_FaderFX

Создать локальную копию ванильного `Ashlands_FaderFX` и оставить визуальные ветки:

```text
cloud
cloud (1)
```

Для ParticleSystem:

- использовать `main.startColor`;
- установить красный канал в `1.0f`;
- не писать лишнюю gradient-обвязку: prefab проверен владельцем;
- сохранить исходные emission multipliers;
- с 18:00 до 23:00 умножать emission на visual factor;
- с 23:00 держать полную интенсивность;
- при cleanup остановить emission и очистить частицы под fade.

Не искать и не создавать объекты каждый кадр.

## 11.5. Music/SFX

Финальная музыка и внешние clips не входят в первый срез. Оставить чистую точку интеграции для нескольких будущих треков по состояниям.

---
# 12. Blood Moon status и реальная защита смерти

## 12.1. Blood Moon status effect

Можно использовать один custom status effect с динамическими названием/tooltip/icon state либо несколько простых эффектов.

### Marked

- countdown;
- сон заблокирован;
- краткое объяснение события.

### AwaitingContact

- blood layer активен;
- progress 0%;
- первый контакт начнёт личный бой;
- поражение не отнимет навыки.

### Fighting

- Bloodlust progress;
- текущие modifiers;
- поражение выбрасывает в real world;
- Player не будет перемещён или respawn-нут при принятом dream-collapse flow.

### GoalReached

- 100% достигнуто;
- полный buff сохраняется;
- враги предпочитают незавершивших;
- можно помогать группе.

### Ejected

Blood Moon combat status снимается или заменяется коротким transition status; Player видит real world и больше не является целью event enemies.

## 12.2. Vanilla SoftDeath — только индикация, не гарантия

В текущем Valheim `Player.HardDeath()` определяется `m_timeSinceDeath`. Игра сама добавляет `SoftDeath`, когда `HardDeath()` уже false.

Следовательно:

- простое добавление `SEMan.s_statusEffectSoftDeath` не гарантирует отсутствие skill loss;
- если vanilla death остаётся допустима в каком-либо blood-layer edge case, нужен узкий patch `Player.HardDeath`/эквивалент;
- при dream collapse skill loss отсутствует потому, что `Player.OnDeath` не вызывается;
- SoftDeath можно показывать как знакомую визуальную подсказку, но пользовательский tooltip Blood Moon должен быть источником правды.

## 12.3. Preferred dream collapse

Предпочтительный flow:

- перехват lethal condition до `Player.OnDeath`;
- no death point, ragdoll, TombStone, food clear или respawn;
- health restoration;
- participant → `Ejected`;
- no position/rotation changes;
- server validation;
- short grace после отдельного решения.

Точный lethal scope и grace являются preimplementation gate. До их закрытия не реализовывать vanilla death как окончательный вариант.
