# Blood Moon — presentation, environment and suppression

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

# 12. Forewarning

На каждой configured forewarning night нужен заметный не текстовый признак:

- слабый красный environment overlay;
- слабые красные облака;
- интенсивность растёт к финальной ночи;
- только ночью;
- утром полностью очищается;
- без spawn, combat modifiers и блокировки сна.

Text/DreamText допускается дополнительно, но не вместо изменения мира.

# 13. Marked — 18:00

При входе:

1. enroll players;
2. остановить текущий RandEvent;
3. заблокировать новые RandEvent;
4. заблокировать сон;
5. добавить собственный Blood Moon status;
6. начать линейный red blend;
7. открыть Blood Craft после его реализации.

## Status text

Не добавлять vanilla `SoftDeath`.

Собственный status должен прямо сообщать смысл:

```text
Когда здоровье иссякнет, кровавая горячка оборвётся.
Ты не оставишь могилу и не потеряешь навыки.
```

Эта формулировка показывается только для `Fighting`/`GoalReached`.

Для `Deferred`:

```text
Кровавая охота не достигает тебя здесь.
Обычные опасности остаются настоящими.
```

# 14. Active — 23:00

- forced Blood Moon environment;
- supported Player сразу → `Fighting`;
- activation/conversion nearby monsters;
- additional spawner;
- no personal visibility layers;
- все клиенты видят одних и тех же Blood Moon противников.

# 15. RandEventSystem

Blood Moon — собственная система.

С 18:00 до полного resolution:

- остановить current random/forced event через штатный путь;
- блокировать новые;
- не позволять им перезаписать environment/music;
- restore в cleanup;
- не запускать новый raid немедленно после Blood Moon.

Boss suspension обрабатывается отдельно; не использовать уничтожение boss bookkeeping как замену.

# 16. Environment

## Target EnvSetup

Копия `Fader`:

- уникальное имя `Seasons_BloodMoon`;
- каждому `Color`: `r = 1.0f`, остальные каналы сохранить;
- `m_windMin = 1f`;
- `m_windMax = 2f`;
- `m_sunAngle = 70f`.

## Overlay 18:00–23:00

```text
18:00 factor 0
23:00 factor 1
```

Порядок:

```text
original env
→ seasonal luminance
→ Blood Moon overlay
→ vanilla SetEnv
→ restore original fields
```

Overlay не зависит от `controlLightings` или texture controllers.

## Forced env

С 23:00 использовать own force-environment lease:

- сохранить previous force value;
- restore только если текущее значение всё ещё принадлежит Blood Moon;
- не затирать override другого мода;
- в resolution восстановить previous/empty.

## Cloud VFX

Клонировать `Ashlands_FaderFX`, оставить:

```text
cloud
cloud (1)
```

Для нужных ParticleSystem использовать проверенный `main.startColor`, установить red channel `1.0f`, emission масштабировать по visual factor.

# 17. Music/SFX

Финальная музыка позже. Архитектура предоставляет phase hooks, но не добавляет пустую сложную abstraction.
