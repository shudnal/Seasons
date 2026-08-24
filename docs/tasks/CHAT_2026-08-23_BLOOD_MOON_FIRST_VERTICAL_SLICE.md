# CHAT 2026-08-23 — Blood Moon: авторитетный контекст и реализация

## 0. Статус

> **Blood Moon готов к production-разработке.** Дальнейшая работа ведётся непосредственно в одной ветке `feat/blood-moon`. Отдельные spike/MVP-ветки и задачи больше не используются.

Репозиторий:

```text
https://github.com/shudnal/Seasons
```

Единственная рабочая ветка:

```text
feat/blood-moon
```

При чтении игровых классов первым источником использовать:

```text
https://github.com/shudnal/assemblies_combined
```

Не использовать старые вне-репозиторные черновики:

```text
BloodMoon_Design_Document.md
BloodMoon_Codex_Implementation_Brief.md
```

Не возвращаться к отвергнутой персональной parallel-layer архитектуре.

## 1. Авторитетный комплект

Документы ниже вместе содержат достаточный контекст для продолжения работы в другом чате или передачи Codex:

1. `docs/tasks/CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`
2. `docs/tasks/blood-moon/01_STATE_AND_SCHEDULE.md`
3. `docs/tasks/blood-moon/02_NETWORK_PERSISTENCE_AND_PARTICIPANTS.md`
4. `docs/tasks/blood-moon/03_PRESENTATION_AND_SUPPRESSION.md`
5. `docs/tasks/blood-moon/04_COMBAT_PROGRESS_AND_RESOLUTION.md`
6. `docs/tasks/blood-moon/05_SEASONS_INTEGRATION.md`
7. `docs/tasks/blood-moon/06_FUTURE_PROGRESSION_AND_REWARDS.md`
8. `docs/tasks/blood-moon/07_BLOOD_CRAFT_AND_WORLD_PRESERVATION.md`
9. `docs/tasks/blood-moon/08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md`
10. `docs/tasks/blood-moon/09_IMPLEMENTATION_DECISIONS_AND_ORDER.md`
11. `docs/tasks/blood-moon/10_IMPLEMENTATION_TASK.md`
12. `docs/tasks/blood-moon/11_VALHEIM_NETWORK_AI_AND_CCS_RESEARCH.md`
13. `docs/tasks/blood-moon/12_RELATED_MODS_RESEARCH.md`

При конфликте:

- файлы `01`–`08` задают продуктовые и подсистемные требования;
- `09` фиксирует окончательно принятые решения;
- `10` задаёт порядок и границы текущей разработки;
- `11` фиксирует выводы из актуального игрового/CCS-кода;
- `12` фиксирует результаты исследования других актуальных модов.

## 2. Цель события

Blood Moon — ежегодная кульминация осени. При стандартном годе Seasons событие происходит один раз примерно за 40 игровых дней.

Это не обычный raid на базу. Это короткая, плотная боевая ночь:

- мир заранее краснеет и предупреждает о приближении события;
- игрок получает время подготовить временный бесплатный боевой билд;
- с 23:00 существующие подходящие монстры становятся агрессивными Blood enemies;
- дополнительно спавнятся временные противники до контролируемого cap;
- база, tamed, NPC, боссы и ресурсы мира не являются допустимыми целями;
- поражение `Defeated` не создаёт могилу, не вызывает respawn и не отнимает навыки;
- единственный постоянный механический результат — опыт боевых навыков;
- материальных, косметических, key/reputation/world-state наград нет;
- утром всё временное исчезает, но skill experience и запись в летописи остаются.

Blood Craft существует не как награда, а как инструмент:

- снять resource lock-in;
- дать попробовать уже известное оружие, броню, magic, ammo и consumables;
- позволить прокачать непривычные боевые навыки в настоящем бою;
- ничего материального не вынести из события.

## 3. Базовый цикл

```text
предвестия последних осенних ночей
→ Marked в 18:00
→ запрет сна и новых boss sacrifices
→ Blood Craft известных боевых предметов
→ линейное покраснение текущего environment
→ Active в 23:00
→ forced Blood Moon environment
→ parking persistent outdoor bosses
→ существующие eligible MonsterAI получают Blood behavior
→ дополнительные marked enemies спавнятся до cap
→ Fighting / GoalReached / Defeated / Withdrawn
→ early completion или 05:45
→ fade и cleanup
→ восстановление parked bosses
→ перевод времени к 06:00
→ DreamText, летопись, сброс Rested
```

## 4. Ключевые принятые решения

### Мир и противники

- Персональной visibility/collision layer нет.
- Все клиенты видят один общий набор противников.
- `AwaitingContact` и `FirstBloodContactRecord` отсутствуют.
- В 23:00 Player сразу входит в `Fighting`, кроме encounter с непаркуемым boss.
- Existing eligible monsters определяются динамически, без conversion ZDO marker.
- Existing monsters сохраняют ordinary loot/ragdoll и не удаляются утром.
- Только дополнительные custom-spawned enemies имеют `SpawnedEventId`; для них no loot, fast ragdoll и cleanup ZDO.
- Existing AI не получает persistent hunt/alert/max-health/shared-prefab mutation.

### Поражение

- Outcome — `BloodMoonParticipantOutcome.Defeated`.
- Локальный owner Player перехватывает `Character.CheckDeath`.
- `Player.OnDeath` не вызывается.
- Health, stamina и eitr восстанавливаются полностью.
- Food и adrenaline остаются.
- Vanilla `SoftDeath` не добавляется.
- Собственный Blood Moon status прямо объясняет отсутствие могилы и skill loss.
- DoT cleanup ограничен вычислимо damaging vanilla effects.
- Stage 1 recovery: полная защита до стабилизации, максимум 15 секунд.
- Stage 2: 10 секунд, incoming multiplier `0.25`.
- Re-entry в первой версии нет.

### Success и поздний выход

`GoalReached` и последующая причина выхода — независимые факты:

```text
GoalReached = true
ExitReason = None | Defeated | Withdrawn | Disconnected
```

Если игрок достиг 100%, Success и completion reward фиксируются навсегда для текущего `eventId`. Поздний `Defeated`, `Withdrawn` или disconnect не отменяет успех, но влияет на DreamText и статистику.

### Боссы

- С 18:00 блокируются boss-producing `OfferingBowl`.
- Persistent outdoor boss паркуется server-authoritative переносом ZDO в far XZ sector.
- Nonpersistent boss не паркуется и не переводится временно в persistent.
- Interior boss не паркуется.
- Affected Player в encounter с interior/nonpersistent/unparkable boss получает terminal `Withdrawn`.
- Exact animation/target/velocity/coroutine/HUD/runtime-only fields босса не восстанавливаются; требуется вернуть тот же ZDO на исходную позицию.
- Lingering projectile/AOE/summon босса специально не удаляются.

### Контексты

- Mounted и generic attached поддерживаются без forced detach.
- Ship/ocean поддерживается; land spawn может естественно отсутствовать.
- Ordinary interior поддерживается.
- Interior extras используют только позиции загруженных `CreatureSpawner`, без вызова их `Spawn()`.
- Teleport временно исключает Player из spawn-anchor расчёта и затем продолжает участие.
- Edge-of-world даёт terminal `Withdrawn` до vanilla edge death.
- Body blocking terminal Player-ом специально не исправляется.

### Сеть

- Глобальный event snapshot синхронизируется через обычный CCS `CustomSyncedValue`.
- `SequencedCustomSyncedValue` не нужен.
- Targeted/per-client/group сообщения идут собственными RPC.
- `Defeated` определяется локальным owner Player и затем рассылается как уведомление; сервер не пытается доказывать факт нулевого HP.
- Extra spawn выполняется клиентом, владеющим соответствующей зоной/`SpawnSystem`; сервер задаёт group/cap/pool, но не предполагает наличие live `Character` на dedicated server.

## 5. Процесс разработки

1. Работать только в `feat/blood-moon`.
2. Перед каждым patch игрового метода читать текущий код в `assemblies_combined`.
3. Реализовывать полноценную устойчивую архитектуру, а не отдельный временный MVP.
4. Делить работу на логические коммиты, но не выносить её в дополнительные ветки.
5. Сохранять и обновлять в репозитории:
   - принятые решения;
   - найденные особенности Valheim;
   - причины выбранных patch points;
   - ошибки и способы исправления;
   - незавершённый контекст и следующую точку.
6. Не менять version, public README, Thunderstore changelog и packaging без отдельного запроса.
7. После законченной реализации открыть draft PR `feat/blood-moon → master`.
8. Запустить Codex code review, исправить подтверждённые замечания.
9. PR не сливать без владельца.
