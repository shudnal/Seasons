# Blood Moon — future parallel world and death

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

# 22. Обязательные последующие архитектурные контракты

Следующие разделы **не реализуются в первом срезе**, но текущий код не должен делать их невозможными или требовать переписывания event identity/protocol.

## 22.4. Параллельный слой и Blood Craft — один этап

Эти системы нельзя реализовывать независимо:

- Blood Craft определяет временные предметы и источники атак;
- parallel-world layer определяет, что игрок видит, с чем сталкивается и чему может нанести урон;
- projectiles/AOE должны сохранять принадлежность к слою;
- без общей матрицы получится ситуация «вижу цель, но не понимаю, почему не наношу урон».

### Финальная матрица взаимодействий

| Источник | Цель | Видимость/коллизия/урон |
|---|---|---|
| Active/GoalReached participant | blood enemy текущего event ID | разрешено |
| Active/GoalReached participant | ordinary enemy, boss, tamed | скрыто/неинтерактивно для локального участника, урон запрещён |
| Active/GoalReached participant | building, crop и другие world objects | остаются частью пространства мира, но blood-layer атаки не наносят им урон |
| Active/GoalReached participant | другой player | обычная видимость; урон только через отдельный optional PvP |
| blood enemy | Active/GoalReached participant текущего event ID | разрешено |
| blood enemy | любой другой player/обычный мир | не выбирать целью, урон запрещён |
| nonparticipant client | blood enemy | не видеть, не сталкиваться локальным Player/projectile, не наносить урон |
| ordinary enemy | Active/GoalReached participant | не выбирать целью и не наносить урон, пока игрок находится в blood layer |

Игроки должны оставаться видимыми друг другу: неучаствующий наблюдатель может видеть, как участник сражается с воздухом.

Не отключать event enemy GameObject/AI целиком на клиенте-наблюдателе, особенно если этот пир владеет ZDO. Presentation и локальные collision/hit rules должны отделяться от сетевой симуляции и ownership.

Полноценная реализация должна проверять не только Renderer:

- physics collisions;
- projectile hit masks;
- AoE overlaps;
- AI target lists;
- final damage;
- ragdoll/effects;
- ownership migration;
- переход в/из Bloodlust без stale colliders/renderers.

### Blood Craft доступность

Blood Craft работает во время Marked и Active, пока у игрока есть соответствующий status.

- только уже известные рецепты;
- бесплатно оружие, броня, trinkets, ammo и разрешённые consumables;
- никаких blood drops/event currency для крафта;
- подготовка должна быть простой и доступной до начала боя;
- временные предметы не ломаются;
- бесплатный upgrade разрешён только уже временному предмету;
- постоянный предмет нельзя бесплатно улучшить;
- эффекты уже употреблённой бесплатной еды/расходников после события остаются;
- оставшиеся ammo/consumables/items удаляются.

Blood Craft — не награда. Это временное снятие resource/skill lock-in, чтобы игрок мог попробовать другой билд в реальном бою.

### Интеграция с Craft/Upgrade UI

Перед реализацией повторно проверить актуальный `assemblies_combined`. В текущем коде:

- `Player.GetAvailableRecipes(ref List<Recipe>)` формирует исходный набор;
- `InventoryGui.UpdateRecipeList(List<Recipe>)` различает Craft/Upgrade через `InCraftTab()` и создаёт `m_availableRecipes`.

Принятый UX:

#### Любая кастомная вкладка

Если активная вкладка не является ванильной Craft или Upgrade:

- не менять список;
- не добавлять дубли;
- не менять requirements;
- не перекрашивать кнопку;
- не вмешиваться в UI другого мода.

#### Upgrade

- базовый список рецептов не модифицировать и не дублировать;
- после формирования `m_availableRecipes` определить строки, где `RecipeDataPair.ItemData` является Blood Craft item;
- выделить такие строки приглушённым красным текстом/фоном/обводкой;
- при выборе временного item сделать кнопку Upgrade красной;
- upgrade временного item бесплатный;
- upgrade обычного item остаётся полностью ванильным;
- результат upgrade сохраняет marker, owner и event ID.

#### Craft

- после получения ванильного списка известных/доступных рецептов пройти по нему;
- для каждого eligible recipe добавить отдельный runtime clone/дубль Blood Craft recipe;
- оригинальный постоянный recipe остаётся в списке;
- clone имеет явный локализуемый префикс/маркер и отдельную runtime identity;
- blood row подсвечивается приглушённо-красным;
- при выборе blood clone кнопка Craft красная и требования бесплатны;
- обычный recipe рядом остаётся платным;
- не мутировать исходный shared `Recipe`;
- временные recipe clones очищаются при перестроении списка/закрытии UI/world unload;
- actual crafting path обязан валидировать выбранный clone, а не доверять только цвету UI.

Точную Harmony-точку (`Player.GetAvailableRecipes` postfix/finalizer, `InventoryGui.UpdateRecipeList` prefix/postfix/finalizer либо комбинацию) выбрать после проверки актуального кода и совместимости с кастомными вкладками. Требование важнее конкретной точки патча.

### Marker

```text
Seasons.BloodCraft.Schema
Seasons.BloodCraft.EventId
Seasons.BloodCraft.OwnerPlayerId
```

### Инвариант

> Blood Craft item может существовать только в поддерживаемом inventory своего Player-владельца и только в соответствующем event ID.

### Damage routing

Основной routing определяется участием Player в текущем blood layer, а не только marker предмета:

- melee, projectile и persistent AOE активного участника относятся к текущему `eventId`;
- такие атаки наносят урон только blood enemies текущего event ID;
- обычное постоянное оружие участника также может использоваться против blood enemies, чтобы игрок не сталкивался с необъяснимым «моё оружие перестало работать»;
- Blood Craft marker отвечает за временность/владение предметом и дополнительно переносится в attack attribution;
- никакие blood-layer атаки не наносят урон ordinary enemies, bosses, tamed, crops, buildings и nonparticipants;
- projectile/AOE получают event/layer marker в момент создания, чтобы позднее попадание не зависело от текущего оружия или состояния UI.

### Drop cleanup

В `ItemDrop.Awake` предмет с Blood Craft marker немедленно уничтожается.

### Interactable.UseItem

Под `UseItem` имеется в виду `Interactable.UseItem`: временный item нельзя применить к объекту мира/станции/стойке/контейнерному потребителю.

При этом нельзя запрещать нормальное player use:

- экипировать оружие/броню;
- стрелять ammo;
- пить разрешённый mead;
- есть разрешённую еду.

### Tombstone/load/external inventories

- удалить временные предметы до переноса в TombStone;
- очищать stale markers при Inventory.Load;
- запрещать перенос в container, ship storage, item stand, armour stand, trade/external inventories;
- учитывать сторонние equipment inventories через owner invariant, насколько возможно без жёстких зависимостей.

### Stack merge

В текущей игре `m_customData` не предотвращает слияние стеков. `Inventory.AddItem`/`FindFreeStackItem` ориентируются прежде всего на name/quality/world level.

Слияние временного и постоянного stack должно быть **явно запрещено**.

Перед реализацией выбрать наиболее надёжный путь:

1. точечный transpiler/override stack compatibility;
2. временное извлечение Blood Craft items вокруг конкретной vanilla merge operation и безопасное возвращение;
3. отдельный virtual inventory для stackables только если первые варианты хуже.

Не допускать:

- потери marker и сохранения бесплатного stack;
- переноса marker на обычный stack и удаления постоянных предметов утром;
- объединения Blood Craft stacks разных event ID/owner ID.

## 22.5. Смерть вдали от базы и dream return

Первый срез только сохраняет `CombatEntrySnapshot` и использует vanilla death под `SoftDeath`.

До релизной версии отдельно принять и реализовать UX смерти, учитывая nomap/noportals:

### Кандидат: dream collapse

- lethal blood damage фиксирует outcome `Death`;
- игрок теряет доступ к активной части события;
- нет skill loss;
- нет обязательного долгого corpse run к случайной точке боя;
- после fade игрок возвращается к валидной точке входа в Fighting либо к другому безопасному anchor;
- обычный inventory не дублируется и не теряется;
- Blood Craft items очищаются;
- DreamText объясняет удар, забытьё и возвращение.

### Открытые вопросы

- создавать ли TombStone вообще;
- перехватывать ли только blood enemy damage или любую смерть с Bloodlust;
- что делать на корабле, в данже, воде, воздухе и attached;
- как валидировать сохранённую позицию после изменения мира;
- что делать, если вход в бой произошёл в заведомо опасной точке;
- как совместить с death/respawn модами.

До решения не выдавать обычный vanilla death flow за окончательный дизайн.

## 22.6. Lifesteal

Отложить до playtest общего темпа.

Будущий вариант должен учитывать attack-instance/target dedup и cap лечения в секунду; не реализовывать сейчас преждевременную формулу.

## 22.7. Music

Позже возможно несколько собственных треков по состояниям Marked/Active/Final. Текущая visual/phase architecture должна предоставлять чистые transitions, но музыка не входит в эту задачу.

---
