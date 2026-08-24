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
2. остановить текущий ordinary RandEvent;
3. заблокировать новые RandEvent;
4. заблокировать сон;
5. добавить собственный Blood Moon status;
6. начать линейный red blend;
7. открыть Blood Craft после его реализации;
8. заблокировать новые boss sacrifices.

## Boss sacrifice block

Фактический boss altar — `OfferingBowl`, а не `BossStone`.

Проверять минимум:

- `OfferingBowl.UseItem` для inventory offerings;
- `OfferingBowl.Interact` для item-stand altars;
- `OfferingBowl.RPC_SpawnBoss` как authoritative/race guard.

Блокировать только bowls с `m_bossPrefab != null`; item-producing offerings не затрагивать.

Если sacrifice был принят до 18:00 и `DelayedSpawnBoss` уже queued, не отнимать offerings отменой: позволить spawn завершиться и немедленно park созданного boss, если Active уже начался.

## Status text

Vanilla `SoftDeath` status не добавлять.

Собственный status прямо сообщает:

```text
Когда здоровье иссякнет, кровавая горячка оборвётся.
Ты не оставишь могилу и не потеряешь навыки.
```

Для `GoalReached` отдельно объяснить, что full buff остаётся и можно помогать другим.

Для terminal personal exit Bloodlust status снимается; recovery protection показывается собственным коротким status/tooltip.

# 14. Active — 23:00

- forced Blood Moon environment;
- все enrolled Player → `Fighting`, кроме отдельно решаемого active unparked interior-boss encounter;
- eligible existing monsters получают dynamic Blood Moon behavior;
- additional marked enemies spawn to cap;
- outdoor active bosses паркуются в far sector;
- персональной visibility/collision layer нет;
- все клиенты видят одних и тех же противников.

Mounted/attached не отсоединяются. Ship/ocean и обычный interior сохраняют Active state; отсутствие подходящей spawn point лишь уменьшает дополнительный spawn.

# 15. RandEventSystem

Blood Moon — собственная система.

С 18:00 до полного resolution:

- остановить current ordinary random event через штатный путь;
- блокировать новые random events;
- с 23:00 не позволять boss forced event/environment/music перезаписать Blood Moon;
- restore систему в cleanup;
- не запускать новый raid немедленно после Blood Moon.

После парковки boss instance исчезает из active area; его EnemyHud и forced boss event должны естественно погаснуть. Всё равно проверить это runtime и не полагаться только на визуальный HUD.

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
