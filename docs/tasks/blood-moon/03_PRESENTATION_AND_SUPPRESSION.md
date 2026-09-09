# Blood Moon — presentation, environment and world suppression

Обязательная часть `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

## 1. Forewarning

Каждая configured forewarning night должна иметь заметное не текстовое изменение мира:

- слабый красный environment overlay;
- слабые красные облака;
- только ночью;
- интенсивность растёт к финальной ночи;
- утром полностью очищается;
- без врагов, Bloodlust modifiers и блокировки сна.

Text/DreamText допускается как дополнительный слой.

Первый Blood Moon не обязан быть полностью понятным. Неудачное первое знакомство допустимо и соответствует циклическому характеру Seasons и Valheim.

## 2. Marked — 18:00

При входе:

1. enroll current players;
2. остановить current ordinary RandEvent;
3. блокировать новые RandEvent;
4. блокировать сон;
5. блокировать новые boss sacrifices;
6. добавить/обновить собственный Blood Moon status;
7. открыть Blood Craft после реализации;
8. начать linear red blend;
9. синхронизировать global snapshot.

### Status text

Vanilla `SoftDeath` не добавлять.

Marked объясняет:

- когда начнётся бой;
- что сон недоступен;
- что известные eligible предметы можно создать временно;
- всё кровавое исчезнет утром.

Fighting status прямо сообщает:

```text
Когда здоровье иссякнет, кровавая горячка оборвётся.
Ты не оставишь могилу и не потеряешь навыки.
```

GoalReached сообщает:

- 100% достигнуто;
- Success уже зафиксирован;
- full buff остаётся;
- можно помогать другим.

Recovery после `Defeated` имеет отдельный короткий status с оставшимся временем защиты.

## 3. Boss sacrifice block

Фактический altar — `OfferingBowl`, не `BossStone`.

Блокировать только instances с:

```csharp
m_bossPrefab != null
```

Guard:

- `OfferingBowl.UseItem`;
- `OfferingBowl.Interact` для item-stand altars;
- `OfferingBowl.RPC_SpawnBoss` как authoritative race guard.

Не блокировать item-producing bowls.

Offerings не расходуются. Уже размещённые attachments не удаляются.

Queued до 18:00 spawn:

- не отменять после возможного списания items;
- позволить завершиться;
- persistent outdoor boss, появившийся во время Active, немедленно park;
- interior/nonpersistent encounter приводит к `Withdrawn` affected Player-ов.

## 4. Active — 23:00

- forced Blood Moon environment;
- enrolled Player → `Fighting`, кроме affected boss encounter;
- persistent outdoor bosses паркуются;
- eligible existing MonsterAI получают dynamic Blood behavior;
- extra marked enemies spawn to cap;
- все клиенты видят один общий мир;
- mounted/attached/ship/ocean/ordinary interiors остаются допустимыми контекстами.

## 5. RandEventSystem

Blood Moon — самостоятельная система, не `RandomEvent`.

С 18:00 до полного resolution:

- остановить current ordinary random event штатным `SetRandomEvent(null, ...)`/эквивалентом;
- блокировать запуск новых random events;
- не позволять forced event перезаписать Blood Moon environment/music;
- restore в cleanup;
- не форсировать новый raid сразу после Blood Moon.

Boss forced event должен погаснуть после выгрузки parked boss instance; это не заменяет explicit RandEvent suppression.

## 6. Blood Moon EnvSetup

После готовности `EnvMan`:

1. найти `Fader`;
2. clone без изменения original;
3. имя `Seasons_BloodMoon`;
4. для каждого `Color`:
   ```csharp
   color.r = 1.0f;
   ```
5. установить:
   ```csharp
   m_windMin = 1f;
   m_windMax = 2f;
   m_sunAngle = 70f;
   ```
6. зарегистрировать один раз;
7. cleanup при world unload.

## 7. Overlay 18:00–23:00

```text
18:00 factor = 0
23:00 factor = 1
```

Первая формула линейная по authoritative absolute schedule.

Порядок в существующем transient `EnvMan.SetEnv` patch:

```text
original current environment
→ existing seasonal luminance
→ Blood Moon overlay toward target
→ vanilla SetEnv
→ restore all original fields
```

Blood Moon overlay не зависит от `controlLightings` или texture controllers.

Сохранять/восстанавливать:

```text
все изменяемые Color fields
m_windMin
m_windMax
m_sunAngle
```

Не использовать reflection каждый кадр.

## 8. Forced environment lease

С 23:00:

- прекратить old weather;
- force `Seasons_BloodMoon`;
- держать до resolution.

Lease:

- сохранить previous force value;
- считать Blood Moon owner только пока current force value равно собственному имени;
- cleanup не затирает override, изменённый другим модом после нас;
- restore previous/empty только если lease всё ещё принадлежит Blood Moon.

## 9. Ashlands_FaderFX

Клонировать vanilla `Ashlands_FaderFX`.

Оставить:

```text
cloud
cloud (1)
```

Для ParticleSystem:

- использовать проверенный `main.startColor`;
- `r = 1.0f`;
- сохранить original emission multipliers;
- 18:00–23:00: `emission * visualFactor`;
- Active: полная интенсивность;
- cleanup: stop emitting + clear под fade.

Не искать prefab/objects каждый кадр.

## 10. Music/SFX

Финальные tracks добавляются после создания музыки владельцем.

Оставить простые phase hooks:

```text
Forewarning
Marked
Active
GoalReached cue
Resolving
```

Не строить заранее сложную пустую audio abstraction.
