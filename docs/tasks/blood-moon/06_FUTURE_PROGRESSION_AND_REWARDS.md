# Blood Moon — future progression and rewards

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

# 22. Обязательные последующие архитектурные контракты

Следующие разделы **не реализуются в первом срезе**, но текущий код не должен делать их невозможными или требовать переписывания event identity/protocol.

## 22.1. Автоматически выводимый пул противников без обязательных JSON-конфигов

Продуктовая цель — не заставлять администратора вручную перечислять существ ванили и каждого моддера.

Следующий этап должен по возможности восстановить связность из уже зарегистрированных данных игры, аналогично тому, как Seasons связывает пень → дерево → саженец → выращиваемое дерево.

### Источники кандидатов

1. **RandEventSystem**
   - читать реально зарегистрированные `RandEventSystem.m_events`;
   - использовать требования `RandomEvent.m_requiredGlobalKeys`, `m_notRequiredGlobalKeys`;
   - учитывать `m_altRequiredKnownItems`, `m_altRequiredNotKnownItems`;
   - учитывать `m_altRequiredPlayerKeysAny`, `m_altRequiredPlayerKeysAll`, `m_altNotRequiredPlayerKeys`;
   - использовать штатную семантику `RandEventSystem.HaveGlobalKeys` и `PlayerIsReadyForEvent`;
   - из удовлетворяющих группе событий извлекать prefabs из `RandomEvent.m_spawn`.

2. **Известные трофеи и дропы**
   - сканировать зарегистрированные character prefabs и их `CharacterDrop.m_drops`;
   - связать prefab существа с trophy/item prefabs, которые оно может выбросить;
   - сопоставить с `Player.m_trophies`, known materials/items и другими штатными признаками знания;
   - наличие известного трофея является сильным признаком, что игрок уже побеждал такое существо;
   - известный обычный дроп — более слабая эвристика и не должен в одиночку открывать очевидный spoiler, если предмет имеет другие источники.

3. **Global и Player keys**
   - Global keys задают общий baseline мира;
   - Player keys/known-items каждого участника добавляют персональные кандидаты;
   - не сводить прогрессию к жёсткому числовому индексу босса.

4. **Valheim 1.0 achievements/counters**
   - после появления актуальных игровых сборок повторно изучить `assemblies_combined`;
   - если в 1.0 есть per-creature achievements/kill counters, использовать их как наиболее точное evidence;
   - не строить текущий код на предположении о ещё отсутствующем API;
   - feature-detect и иметь fallback к рейдам/ключам/трофеям.

### Модель evidence

Для каждого candidate prefab полезно хранить причину допуска:

```text
GlobalProgress
EligibleRaid
PlayerKey
KnownTrophy
KnownDrop
KillCounter
```

Это нужно для диагностики, preferred target и предотвращения случайных spoilers.

### Групповая логика

1. Для каждой скрытой combat group собрать global snapshot.
2. Для каждого участника вычислить персональный candidate set.
3. Group pool = global baseline + объединение персональных sets.
4. Если тяжёлый противник доступен только из-за одного или нескольких игроков, хранить set таких `unlockingPlayerIds`.
5. Такой враг приоритетнее выбирает этих игроков, пока они являются допустимыми целями.
6. После split/merge group pool пересчитывается; ушедший продвинутый игрок перестаёт усложнять новые спавны оставшейся группе.
7. Пересчитывать по revision состава/прогрессии, а не сканировать весь ObjectDB/ZNetScene на каждый spawn.

### Серверное получение персонального evidence

Не предполагать, что dedicated server имеет прямой доступ к локальным `Player.m_trophies`, known materials и будущим achievement counters удалённого клиента.

- использовать уже существующий server-synced `possibleEvents`, который формируется через `Player.UpdateEvents` и читается `RandEventSystem.RefreshPlayerEventData`, как сильный источник eligible raids;
- для данных, которых нет в `m_serverSyncedPlayerData`, отправлять компактный revisioned evidence snapshot от владельца Player;
- сервер валидирует формат, event/protocol version и использует данные только для выбора пула, не принимает от клиента готовый prefab list или уровень сложности;
- при отсутствии/устаревании evidence использовать более консервативный global/raid fallback;
- не рассылать полный список known items каждый spawn.

### Modded content

Моддерские существа должны подхватываться автоматически, если они логично встроены хотя бы в один из источников:

- зарегистрированы в raid;
- имеют CharacterDrop/trophy;
- используют player/global keys;
- входят в будущие achievement counters.

Не добавлять обязательный JSON enemy catalog и не требовать ручного конфигурирования каждого мода.

Если существо нельзя надёжно вывести из данных игры, безопаснее его не включить, чем показать игроку spoiler. Отдельный диагностический dump candidate graph обязателен на этапе реализации.

Первый вертикальный срез по-прежнему использует один явный test prefab; это временный bootstrap, а не финальная настройка пула.

## 22.2. Momentum и StallTime

Не использовать один двусмысленный `Heat` в коде, чтобы не пересекаться с Summer Heat.

Будущие величины:

- `Momentum` — краткосрочная активность боя; высокий Momentum даёт больше слабого мяса/частые волны, а не только повышенную сложность;
- `StallTime` — время, когда рядом живы враги, но группа не наносит значимого урона; именно оно включает ranged/Howler/door pressure.

## 22.3. Skill rewards — единственная постоянная механическая награда

Blood Moon не должен выдавать:

- предметы;
- материалы;
- event currency;
- рецепты;
- world keys;
- постройки/декор;
- постоянные buffs;
- любые другие награды, меняющие состояние мира.

Разрешены:

- прирост боевых навыков;
- статистика;
- запись в `knownTexts`/летопись как информационный след.

Список допустимых навыков должен быть серверным конфигом на основе case-insensitive aliases `Skills.SkillType`.

- использовать понятные псевдонимы enum;
- поддержать canonical enum names;
- неизвестные aliases логировать и игнорировать;
- не награждать Run/Jump/Sneak/Swim и другие небоевые навыки по умолчанию;
- Blocking, оружие, Bows/Crossbows, ElementalMagic/BloodMagic и другие реальные combat skills должны быть доступны.

### Completion reward

Для каждого игрока во время события хранить вклад по навыкам на основе реально накопленного **базового** skill experience/level-equivalent, до Blood Moon multiplier.

Алгоритм:

1. Оставить только configured combat skills.
2. Отсортировать по накопленному базовому опыту.
3. Взять первые 5.
4. Общий budget успешного прохождения: `+25` level-equivalent.
5. При неполном combat progress/death масштабировать budget пропорционально реально достигнутому combat progress; auto-complete не учитывается.
6. Распределить budget пропорционально вкладу выбранных навыков.
7. После расчёта применить фактический cap `+10` на один skill.
8. Не перераспределять overflow, отрезанный per-skill cap, в другие навыки.

Пример:

```text
расчёт:      15 / 7 / 3
фактически:  10 / 7 / 3
```

### Limited x3 gain

Во время боя базовое накопление всегда остаётся минимум x1.

Пока у игрока не исчерпан глобальный live bonus budget:

- effective gain = x3;
- дополнительные x2 считаются бонусом Blood Moon;
- фактический дополнительный level-equivalent суммируется во все разрешённые навыки;
- общий live bonus cap = `+10`;
- после исчерпания cap оставшаяся прокачка идёт x1.

Нужно считать фактический level-equivalent с учётом нелинейной кривой `Skills`, а не число вызовов `RaiseSkill`.

Режимы администратора:

```text
None
CompletionOnly
LimitedTripleGainOnly
Hybrid
```

В `Hybrid` один конкретный skill может получить максимум до +20 бонусных уровней: до +10 через live bonus и до +10 через completion cap. Общий completion pool остаётся до +25, live pool — до +10.

Поддержку Blocking/BloodMagic/ElementalMagic и других support/combat skills получать наблюдением фактических `RaiseSkill` calls, а не угадыванием только по last-hit weapon.
