# CHAT 2026-08-23 — Blood Moon: первый вертикальный срез

## 0. Текущий статус

> **Статус:** preimplementation design. Production-код Blood Moon пока не начинать.

Blood Moon — единое глобальное состояние, одинаковое для всех клиентов. Персональные слои видимости и clone-preservation обычных монстров отвергнуты.

Базовая модель:

```text
18:00
→ Marked, запрет сна и новых boss sacrifices
→ Blood Craft после его реализации
→ линейное покраснение текущей среды

23:00
→ forced Blood Moon environment
→ все загруженные eligible небоссовые MonsterAI получают Blood Moon behavior
→ дополнительно спавнятся временные Blood Moon монстры
→ противники охотятся только на активных участников
→ участники наносят урон только Blood Moon противникам

05:45 или раннее завершение
→ cleanup marked extra spawns и личных временных предметов
→ восстановление запаркованных persistent outdoor bosses
→ fade + DreamText
→ перевод времени к 06:00
```

Рабочий репозиторий:

```text
https://github.com/shudnal/Seasons
```

Основная ветка подготовки:

```text
feat/blood-moon
```

Изолированный runtime-spike:

```text
spike/blood-moon-global-event
```

При чтении игрового кода первым источником использовать:

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

При конфликте файлы `09` и `10` имеют приоритет для ещё не подтверждённых runtime-решений.

Не использовать старые вне-репозиторные `BloodMoon_Design_Document.md` и `BloodMoon_Codex_Implementation_Brief.md`.

## 2. Цель Blood Moon

Blood Moon — ежегодная кульминация осени, один раз примерно за 40 игровых дней при стандартных настройках.

Событие должно быть коротким мясным боевым эпизодом:

- высокая плотность относительно слабых противников;
- временное усиление игрока;
- бесплатный временный Blood Craft уже известных боевых предметов;
- простая цель — заполнить Bloodlust реальным боем;
- `Defeated` не создаёт могилу, не вызывает respawn и не отнимает навыки;
- единственный постоянный механический результат — опыт боевых навыков;
- никаких материалов, валюты, декора, world keys или иных постоянных наград.

## 3. Принятые решения

- Персональной visibility/collision layer нет.
- `AwaitingContact` и `FirstBloodContactRecord` не нужны.
- В 23:00 Player сразу входит в `Fighting`, кроме encounter с неприпаркованным boss.
- Vanilla `SoftDeath` status не добавляется; безопасное поражение объясняет собственный Blood Moon status.
- Outcome безопасного поражения — `BloodMoonParticipantOutcome.Defeated`.
- `Player.OnDeath` для `Defeated` не вызывается.
- Любое обычное `health <= 0` во время активного Blood Moon боя приводит к dream collapse.
- Health, stamina и eitr восстанавливаются полностью; food и adrenaline не меняются.
- Удаляются только вычислимо определяемые damaging DoT.
- После `Defeated`: полная защита до стабилизации, максимум 15 секунд; затем 10 секунд 75% снижения входящего урона.
- При приближении к краю мира участник получает terminal `Withdrawn` до edge/tidal death.
- Re-entry в первой версии отсутствует.
- Mounted, attached, ship/ocean и обычные interiors поддерживаются без forced detach и персональных слоёв.
- Body blocking terminal Player-ом не запрещается специально.
- Все загруженные eligible небоссовые MonsterAI считаются Blood enemies динамически, пока событие активно.
- Eligibility определяется общими игровыми признаками: живой `MonsterAI`, не boss, не tamed, не `Players`/`PlayerSpawned`/`TrainingDummy`, враждебен хотя бы одному активному участнику через штатную faction/aggravation semantics.
- Существующие монстры сохраняют обычный loot/ragdoll и не удаляются при cleanup.
- Только дополнительные custom-spawned монстры получают `SpawnedEventId`; для них loot подавляется, ragdoll быстро очищается, а выжившие/stale ZDO удаляются.
- AI-настройки и shared prefab существующих монстров не мутируются; Blood behavior задаётся условными runtime-патчами.
- Призывание новых боссов блокируется с 18:00 через `OfferingBowl`.
- Persistent outdoor boss паркуется server-authoritative переносом ZDO в far sector и возвращается по marker/original position.
- Nonpersistent boss не паркуется. Игроки в его encounter получают terminal `Withdrawn`; конкретная совместимость добавляется только по обращениям.
- Interior boss не паркуется. Игроки в его encounter получают terminal `Withdrawn`.
- Boss projectiles/AOE/summons специально не очищаются: они завершают собственный lifecycle; общие damage rules продолжают действовать.
- Для additional spawn в interior используются только позиции уже загруженных `CreatureSpawner` с path/context validation. Если подходящей позиции нет, дополнительный spawn не выполняется.
- Balance/runtime configs применяются на лету. Снижение cap не удаляет уже живых extra enemies, а только блокирует новый spawn до возврата ниже cap.

## 4. Что осталось до production-кода

Продуктовые развилки почти закрыты. Нужны runtime-доказательства и точные технические точки:

1. минимальный набор Harmony-патчей для target selection, flee/idle, damage и progress без persistent AI mutation;
2. server-authoritative discovery loaded boss, определение `Persistent`/interior и валидация owner report;
3. boss parking transaction: ownership handoff, live transform/ZDO sync, sector invalidation, unload, restart и idempotent restore;
4. проверка `CreatureSpawner`-точек и полного пути внутри разных dungeon layouts;
5. `Character.CheckDeath`-based `Defeated`, DoT cleanup и двухэтапная recovery protection;
6. projectile/AOE/summon attribution для общей damage matrix;
7. окончательный выбор границ CCS snapshot/sequenced channel и собственных RPC;
8. семантика Player, который достиг 100%, а затем получил `Defeated`/`Withdrawn`: рекомендовано не отнимать уже зафиксированный Success/reward, а хранить последующую причину выхода отдельно.

Балансовые значения не блокируют архитектуру: они сразу становятся server configs с live apply и настраиваются плейтестами.

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
