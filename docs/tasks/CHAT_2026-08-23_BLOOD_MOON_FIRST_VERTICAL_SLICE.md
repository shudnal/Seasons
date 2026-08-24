# CHAT 2026-08-23 — Blood Moon: первый вертикальный срез

## 0. Текущий статус

> **Статус:** preimplementation design. Production-код Blood Moon пока не начинать.

От идеи персональных параллельных слоёв мира принято отказаться. Она давала сильный визуальный образ, но требовала хрупкой сетевой логики видимости, коллизий, ownership, projectile/AOE routing и совместимости с большим количеством игровых и модовых систем.

Новая базовая модель глобальна и одинакова для всех клиентов:

```text
23:00
→ подходящие обычные монстры рядом с игроками получают Blood Moon behavior
→ дополнительно спавнятся Blood Moon противники
→ все Blood Moon противники видимы всем
→ они охотятся только на активных участников
→ участники наносят урон только Blood Moon противникам
→ база, tamed, NPC, боссы и обычная экономика мира не участвуют
```

Перед production-разработкой нужно закрыть оставшиеся вопросы и выполнить изолированный runtime-spike:

```text
docs/tasks/blood-moon/09_PREIMPLEMENTATION_DECISIONS_AND_SPIKES.md
docs/tasks/blood-moon/10_GLOBAL_EVENT_RUNTIME_SPIKE.md
```

Старую ветку и задачу `spike/blood-moon-parallel-layer` не использовать: они относятся к отвергнутой архитектуре.

Рабочий репозиторий:

```text
https://github.com/shudnal/Seasons
```

Основная ветка подготовки:

```text
feat/blood-moon
```

При чтении кода Valheim первым источником использовать:

```text
https://github.com/shudnal/assemblies_combined
```

## 1. Авторитетный комплект

1. `docs/tasks/CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`
2. `docs/tasks/blood-moon/01_STATE_AND_SCHEDULE.md`
3. `docs/tasks/blood-moon/02_NETWORK_PERSISTENCE_AND_PARTICIPANTS.md`
4. `docs/tasks/blood-moon/03_PRESENTATION_AND_SUPPRESSION.md`
5. `docs/tasks/blood-moon/04_COMBAT_PROGRESS_AND_RESOLUTION.md`
6. `docs/tasks/blood-moon/05_SEASONS_INTEGRATION.md`
7. `docs/tasks/blood-moon/06_FUTURE_PROGRESSION_AND_REWARDS.md`
8. `docs/tasks/blood-moon/07_BLOOD_CRAFT_AND_WORLD_PRESERVATION.md`
9. `docs/tasks/blood-moon/08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md`
10. `docs/tasks/blood-moon/09_PREIMPLEMENTATION_DECISIONS_AND_SPIKES.md`
11. `docs/tasks/blood-moon/10_GLOBAL_EVENT_RUNTIME_SPIKE.md`

При конфликте файлы `09` и `10` имеют приоритет для ещё не закрытых технических решений.

Не использовать старые вне-репозиторные `BloodMoon_Design_Document.md` и `BloodMoon_Codex_Implementation_Brief.md`.

## 2. Цель Blood Moon

Blood Moon — ежегодная кульминация осени, один раз примерно за 40 игровых дней при стандартной длине года.

Событие должно быть коротким мясным боевым эпизодом:

- высокая плотность относительно слабых противников;
- временное усиление игрока;
- бесплатный временный Blood Craft известных боевых предметов;
- простая цель — заполнить Bloodlust реальным боем;
- поражение не создаёт могилу, не вызывает respawn и не отнимает навыки;
- единственный постоянный механический результат — опыт боевых навыков;
- никаких материалов, валюты, декора, world keys или иных постоянных наград.

Ключевая формула после отказа от персональных слоёв:

> Кровавая ночь охватывает реальных существ вокруг игроков, но временно меняет правила их боя. Мир остаётся общим, а последствия события должны быть минимальны.

## 3. Планируемый цикл

```text
предвестия последних осенних ночей
→ Marked в 18:00
→ Blood Craft и линейное покраснение среды
→ Active в 23:00
→ forced Blood Moon environment
→ остановка RandEventSystem
→ активация/добавление Blood Moon противников
→ Fighting
→ server-authoritative Bloodlust progress
→ Success / Defeated / Withdrawn / Disconnected / forced morning
→ cleanup
→ fade + DreamText
→ перевод времени к 06:00
→ восстановление погоды, боссов и обычных систем
```

## 4. Уже принятые изменения

- `AwaitingContact` и `FirstBloodContactRecord` больше не нужны.
- Персональной visibility/collision layer нет.
- В 23:00 поддерживаемый Player сразу входит в `Fighting`.
- Vanilla `SoftDeath` status не добавляется.
- Безопасность поражения объясняется собственным Blood Moon status effect.
- Outcome безопасного поражения называется `Defeated`.
- `Player.OnDeath` для такого поражения не вызывается.
- Любое обычное `health <= 0` в активном Blood Moon бою приводит к dream collapse.
- Health, stamina и eitr восстанавливаются полностью; food и adrenaline не меняются.
- Удаляются только вычислимо определяемые damaging DoT.
- После `Defeated`: полная защита до стабилизации, максимум 15 секунд; затем 10 секунд 75% снижения входящего урона.
- При приближении к краю мира участник получает terminal outcome `Withdrawn` до действия edge/tidal death.
- Re-entry в первой версии отсутствует.
- Mounted и generic attached не требуют отдельного слоя и не должны принудительно отсоединяться.
- Interior/dungeon и ship/ocean получают визуал, но не являются spawn anchors.
- Босс на время события должен быть глобально убран из боя с последующим восстановлением состояния; точный механизм проверяется spike.

## 5. Процесс

До закрытия spike разрешены только:

- обновление документов;
- чтение `assemblies_combined`;
- debug-only runtime spike в отдельной ветке;
- фиксация результатов.

После отдельного решения владельца о старте production-разработки:

1. повторно прочитать весь комплект;
2. реализовать первый вертикальный срез в `feat/blood-moon`;
3. делать небольшие логические коммиты;
4. не менять version/README/changelog/package без отдельного запроса;
5. открыть draft PR в `master`;
6. провести отдельный Codex review;
7. исправить подтверждённые замечания;
8. PR не сливать без владельца.
