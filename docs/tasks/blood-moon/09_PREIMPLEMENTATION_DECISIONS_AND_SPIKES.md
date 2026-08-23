# Blood Moon — preimplementation decisions and technical spikes

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

> **Статус:** разработку production-кода Blood Moon пока не начинать. Общая архитектура понятна, но несколько решений ниже напрямую определяют participant state machine, death flow, visibility/collision layer и ownership. Сначала закрыть эти вопросы и провести узкие технические spikes в реальной игре.

# 27. Новое базовое направление

## 27.1. Никакого восстановления позиции или ротации

Blood Moon не должен перемещать или разворачивать модель Player ни при входе, ни при поражении, ни при утреннем завершении.

Допустимо:

- изменить status effects;
- изменить health/stamina/eitr через штатные API, если это принято отдельным решением;
- включить emote/pose;
- локально изменить presentation/collision rules blood layer;
- сделать fade.

Недопустимо:

- телепортировать к `m_lastGroundPoint`;
- raycast-ом подбирать «безопасную» Y-позицию;
- снимать Player с корабля, mount или attached object;
- менять rotation;
- восстанавливать transform из snapshot.

Причина: `Character.m_lastGroundPoint` является последней контактной точкой, а не гарантированным безопасным положением capsule. Она может быть устаревшей, относиться к движущемуся collider/rigidbody и по умолчанию не является валидированным spawn anchor.

## 27.2. Active начинается с `AwaitingContact`

В 23:00 enrolled Player входит в blood presentation/interaction layer, но ещё не считается реально вступившим в бой.

Требуемая participant-модель:

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

- `AwaitingContact`: Player видит blood enemies и может быть ими выбран целью, но первый подтверждённый контакт ещё не зарегистрирован.
- `Fighting`: произошёл первый допустимый blood interaction.
- `GoalReached`: боевой progress достиг 100%; полный buff остаётся.
- `Ejected`: Player пережил dream collapse, вернулся в real-world layer и больше не является целью/участником боя текущего event ID.

`Disconnected` остаётся outcome/metadata, а не отдельной физической фазой.

## 27.3. Первый контакт вместо positional backup

Не хранить `CombatEntrySnapshot` как точку будущего teleport/restore.

Вместо него хранить `FirstBloodContactRecord`:

```text
eventId
stable player ID
current peer/session UID
authoritative timestamp
contact kind: incoming / outgoing / block / parry
blood enemy ZDOID
player position только для диагностики/статистики
```

Позиция в записи не является anchor и никогда автоматически не применяется к transform.

Первый контакт должен пройти через центральный `BloodMoonInteractionRules` и серверную валидацию. Клиент-владелец Player может применить необходимое локальное состояние немедленно, но сервер остаётся источником истины.

## 27.4. Предпочтительный death flow: dream collapse без настоящей смерти

Основной кандидат на финальное решение:

1. Owner Player обнаруживает lethal condition в blood layer до вызова `Player.OnDeath`.
2. Обычный death flow не запускается.
3. Не создаются death point, ragdoll, TombStone и respawn request.
4. Обычный inventory, equipment, food и position не меняются.
5. Player восстанавливает health до принятого значения; стартовый кандидат — 100% max health.
6. Participant outcome фиксируется как `Death`, phase — `Ejected`.
7. Blood presentation, targeting и collisions для этого Player немедленно отключаются.
8. Player остаётся в той же position/rotation и снова видит real-world layer.
9. При необходимости включается короткая transition grace без изменения transform.
10. При глобальном завершении Player получает death-specific DreamText.

В текущем коде Valheim `Character.CheckDeath()` вызывает `OnDeath()` только после того, как health уже стал `<= 0`. Поэтому `Character.CheckDeath` является предпочтительной точкой spike: проверить, можно ли owner-side prefix безопасно заменить смерть на collapse, восстановить health и сообщить серверу outcome до создания TombStone.

## 27.5. `SoftDeath` сам по себе не является гарантией

Ванильный `SoftDeath` status можно оставить как понятную игроку визуальную индикацию, но нельзя считать его механизмом защиты навыков.

`Player.HardDeath()` определяется `m_timeSinceDeath`, а ванильный код лишь добавляет `SoftDeath`, когда `HardDeath()` уже false. Простое `SEMan.AddStatusEffect(SEMan.s_statusEffectSoftDeath)` не меняет результат `HardDeath()`.

Если в blood layer остаются сценарии настоящей vanilla death, необходимо отдельно гарантировать `HardDeath == false` узким patch-ом. Для dream collapse skill loss отсутствует потому, что `Player.OnDeath` вообще не вызывается.

# 28. Определение первого контакта — решение ещё требуется

Нужно выбрать один контракт и затем использовать его в сети, state machine и reward tracking.

## Вариант A — только фактическая потеря health

Контакт считается состоявшимся, если final accepted damage уменьшил health.

Минусы:

- успешный block/parry не считается боем;
- полностью resisted hit не считается;
- первый защищённый игрок может долго оставаться `AwaitingContact`, хотя активно сражается.

## Вариант B — принятый attack contact

Контакт считается состоявшимся, если допустимый blood-layer hit дошёл до damage/block pipeline, даже если block/parry/resistance свели health damage к нулю.

Промах, near-projectile notification и простое попадание в trigger без attack resolution не считаются.

**Рекомендация:** вариант B. Он лучше поддерживает shields, parry, tank/support play и не привязывает state machine к конкретной формуле damage mitigation.

До явного утверждения не кодировать окончательное условие.

# 29. Scope dream collapse — решение ещё требуется

## Вариант A — только прямой lethal hit от blood enemy

Плюсы: минимальный exploit surface.

Минусы:

- fall после blood knockback создаст настоящую могилу;
- delayed poison/burning/AOE может создать настоящую могилу;
- attribution легко потерять между projectile, DOT и final death.

## Вариант B — любой lethal damage в `Fighting`/`GoalReached`

Плюсы:

- единая понятная гарантия: смерть внутри иллюзорного боя всегда выбрасывает в real world;
- не нужно угадывать происхождение fall/DOT;
- никогда не возникает TombStone из иллюзорной фазы.

Минусы:

- один раз за событие Player может использовать collapse как защиту от world hazard;
- нужно определить исключения вроде `EdgeOfWorld`, admin kill и специальных scripted deaths.

## Вариант C — окно blood attribution

Collapse применяется для прямого blood damage и environmental death в течение N секунд после blood hit/knockback.

Минус: сложная и неизбежно спорная эвристика.

**Предварительная рекомендация:** вариант B с небольшим явным deny-list (`EdgeOfWorld`, административное/скриптовое убийство, если такое требуется). Ejection терминальна для текущего event ID, поэтому exploit ограничен одним выходом из редкого ежегодного события.

# 30. Transition grace после ejection

Так как transform не меняется, ejection может произойти:

- в воздухе после knockback;
- внутри collider скрытого ordinary creature;
- над водой/лавой;
- на корабле или mount;
- в узком проходе, где сразу возобновляется real-world combat.

Нужен отдельный короткий transition state, не являющийся новым участием в Blood Moon.

Кандидат:

```text
минимум 2 секунды invulnerability
и далее до первого устойчивого IsOnGround(),
но не дольше 8–10 секунд
```

Во время grace:

- blood enemies уже не видят и не повреждают Player;
- ordinary world presentation возвращается;
- Player не перемещается и сохраняет velocity;
- fall damage, связанный с текущим падением, должен быть подавлен либо grace должна покрыть landing;
- после cap обычные правила полностью возвращаются.

Нужно отдельно решить поведение в воде/лаве и при `EdgeOfWorld`.

# 31. Re-entry после ejection

Для первой релизной версии допустимо не давать повторный вход: поражение должно сохранять смысл.

Если re-entry будет добавлен позже, наиболее совместимый вариант — один специальный временный Blood Craft consumable, который можно подготовить в Marked и использовать из inventory после ejection:

- не требует campfire/base/world object;
- подходит игроку в поле, nomap/noportals и на корабле;
- исчезает утром;
- имеет owner/event ID;
- может быть ограничен одним использованием;
- возвращает в `AwaitingContact` или `Fighting` по отдельно принятому правилу.

Не делать automatic re-entry и не привязывать основной вариант к жертве на базе.

# 32. Ownership ordinary world: `SetOwner(0)` не использовать как базовый механизм

## 32.1. Почему ownerless parking опасен

Ванильный `ZDOMan.ReleaseZDOS` примерно раз в две секунды вызывает `ReleaseNearbyZDOS` для server reference position и peer positions. Persistent ZDO без допустимого owner в active area снова получает owner по vanilla proximity logic.

Следовательно, простой `SetOwner(0)`:

- не является устойчивым состоянием pause;
- потребует patch центрального owner-selection path;
- может немедленно вернуть ordinary creature тому же blood participant;
- создаёт дополнительные owner-revision churn и сетевые края;
- для нестандартных nonpersistent объектов повышает риск orphan cleanup;
- делает обычный owner-targeted RPC неоднозначным, пока owner равен нулю.

Ownership — механизм сетевой симуляции, а не штатный pause API.

## 32.2. Предпочтительная совместимая схема

Не менять ownership без необходимости.

1. Presentation/collision layer отделяется от симуляции.
2. Event enemy может оставаться owned любым nearby peer, включая nonparticipant, если его AI продолжает работать и central interaction rules разрешают ему видеть только blood participants.
3. Ordinary creature, owned blood participant, либо продолжает background real-world simulation, либо локально suspend-ится через узкий AI gate — это отдельное продуктовое решение ниже.
4. Если рядом есть eligible nonparticipant peer, server может **напрямую** передать ordinary creature этому peer, не оставляя ZDO ownerless.
5. Если eligible peer нет, сохранить текущего owner и применить выбранную simulation policy.
6. Не patch-ить глобальный `ZDO.SetOwner` без доказанной необходимости.
7. Если всё же потребуется owner-selection patch, он должен быть узким, prefab/character-cached и покрыт dedicated/listen/disconnect tests.

Для ownership policy participant record должен различать:

```text
stable profile/player ID
current ZNet peer/session UID
Player ZDOID
```

Peer UID может измениться после reconnect.

# 33. Должен ли real world продолжать симуляцию

Нужно выбрать политику.

## Policy A — background simulation

Ordinary enemies/tamed продолжают vanilla AI на текущем owner, но:

- не видят blood-layer Player;
- не наносят ему damage;
- локально скрыты и не сталкиваются с ним.

Плюсы: минимальное вмешательство в ownership/AI, nonparticipants всегда видят живой мир.

Минусы: пока solo Player находится в иллюзии, ordinary enemies могут переместиться, напасть на tamed/base и изменить мир в его отсутствие.

## Policy B — conditional suspension

Если ordinary creature owned blood participant и рядом нет real-world nonparticipant:

- owner сохраняется;
- BaseAI/MonsterAI simulation локально suspend-ится;
- объект не уничтожается и не становится ownerless;
- после ejection/окончания simulation продолжается;
- если появляется nonparticipant, server по возможности передаёт owner ему и suspension снимается.

Плюсы: ближе к образу «мир застыл там, где его оставили».

Минусы: нужно проверить movement/physics/status/procreation paths помимо `BaseAI.UpdateAI`.

## Policy C — layer-aware owner pool

Patch vanilla owner selection и разрешить ordinary objects только real-world peers, blood enemies — только blood peers/server.

Это самый инвазивный вариант и пока **не рекомендуется**.

**Предварительная рекомендация:** Policy B после отдельного spike. Если она окажется хрупкой, fallback — Policy A. Не переходить к Policy C без измеримой причины.

# 34. Presentation, collisions и owner simulation

Нельзя отключать root GameObject или весь AI только потому, что локальный Player не должен видеть entity: этот же client может быть owner и обязан симулировать объект для других peers.

Нужен локальный layer controller, который раздельно управляет:

- renderers/LOD;
- blood/ordinary VFX;
- audio emitters;
- EnemyHud/name presentation;
- Character/hitbox colliders относительно local Player;
- projectile collision masks;
- AoE overlap acceptance;
- target selection;
- final damage routing.

Обязательные ownership test cases:

1. blood enemy owned participant;
2. blood enemy owned nonparticipant;
3. ordinary enemy owned participant;
4. ordinary enemy owned nonparticipant;
5. owner migration во время контакта;
6. owner disconnect;
7. late join рядом с уже существующими entities.

# 35. Наследование blood layer для созданных объектов

Для поддержки реальных боевых билдов одной маркировки weapon/projectile недостаточно.

Будущее правило:

- projectile и persistent AOE, созданные blood-layer participant, получают `eventId`/layer attribution при создании;
- combat summon, созданный participant во время Active, становится blood entity текущего event ID;
- такой summon атакует только blood enemies и исчезает при ejection/resolve;
- summon/tamed, существовавший до входа в layer, остаётся ordinary real-world entity и локально скрывается от participant;
- ordinary turret/trap не становится blood entity автоматически;
- blood enemy projectile/AOE также сохраняет marker после смерти/смены owner источника.

Иначе magic/BloodMagic builds не будут полноценно работать в событии.

# 36. DOT и status effects

Persistent vanilla effects создают проблему attribution:

- poison/burning могут сработать после ejection;
- один и тот же status hash может существовать до Blood Moon и быть reset blood hit-ом;
- без source marker нельзя безопасно удалить только blood-origin instance.

До реализации расширенного enemy pool выбрать один путь:

1. первый playable slice использует enemy без persistent DOT/status attacks;
2. blood attacks применяют отдельные blood-specific status clones с event ID;
3. внедрить source-aware tracking для SE application и аккуратный cleanup.

**Рекомендация для первого combat prototype:** вариант 1. Не маскировать нерешённый attribution глобальным `RemoveStatusEffect(Poison/Burning)`.

# 37. Контексты, которые нельзя оставить на потом

До production implementation определить поведение минимум для:

- outdoor ground;
- dungeon/interior;
- ship/ocean;
- mounted/attached;
- swimming;
- flying/falling;
- active boss encounter;
- portal/teleport transition;
- player entering/leaving instance/dungeon;
- late join во время Active.

Для каждого контекста нужна одна из политик:

```text
полноценный spawn/participation
ограниченный context-specific enemy pool
AwaitingContact без forced engagement
безопасный skip текущего Player
```

Нельзя считать surface spawn достаточным для игрока на корабле или в dungeon.

# 38. World interactions в blood layer

Нужно явно решить, какие real-world действия остаются допустимыми.

Уже принято:

- geometry/terrain/buildings остаются видимыми и коллизионными;
- blood-layer attacks не повреждают buildings, crops, ores, trees, ordinary entities;
- doors и crafting stations должны оставаться usable;
- Blood Craft работает через реальные stations/UI;
- Blood Craft items нельзя вынести в world/external inventory.

Открыто:

- видимость и pickup уже лежащих ordinary ItemDrop;
- использование containers во время Active;
- управление ship/mount;
- placement/building/terrain tools;
- взаимодействие с trader/NPC;
- ordinary traps/turrets.

Эти правила должны быть согласованы с принципом: постоянный результат события — навыки, но Player не теряет агентность и остаётся в той же физической точке мира.

# 39. Обязательные technical spikes до начала разработки

Spikes выполняются отдельными минимальными экспериментами и не превращаются автоматически в production implementation.

## Spike A — first contact и dream collapse

Проверить в single-player, listen server и dedicated server:

- incoming first blood hit;
- outgoing first blood hit;
- block/parry;
- lethal first hit;
- `Character.CheckDeath` interception до `Player.OnDeath`;
- no TombStone/death point/respawn;
- full heal/ejection без transform changes;
- server validation и повторный report;
- stale event ID.

## Spike B — local parallel presentation

На двух клиентах проверить обе ownership-комбинации:

- один client в blood layer, второй в real world;
- event enemy owner participant/nonparticipant;
- ordinary enemy owner participant/nonparticipant;
- renderer/audio/hitbox/collision/projectile isolation;
- observers видят participant, сражающегося с воздухом.

## Spike C — ordinary simulation policy

Сравнить Policy A и Policy B:

- AI/movement после suspend/resume;
- flying/swimming/tamed;
- nonparticipant entering range;
- direct owner transfer без ownerless interval;
- owner disconnect;
- CPU/network profile.

## Spike D — spawned combat objects

Проверить melee, arrow/bolt, thrown weapon, bomb, persistent AOE и summon attribution через owner migration и delayed hit.

## Spike E — context spawn

Проверить land, dungeon, ship/ocean, attached/mount и portal transition. Зафиксировать поддерживаемую политику для первого релиза.

# 40. Gate для начала production-кода

До явного решения владельца мода должны быть закрыты минимум:

1. first-contact contract;
2. dream-collapse lethal scope;
3. ejection grace;
4. ordinary-world simulation policy;
5. visibility/collision mechanism;
6. context policy для dungeon и ocean/ship;
7. DOT/status policy;
8. re-entry policy первой версии;
9. boss-overlap policy;
10. допустимые world interactions.

Пока gate не закрыт:

- не начинать реализацию первого вертикального среза;
- не открывать implementation PR;
- не делать version bump;
- разрешены только документация, чтение `assemblies_combined` и отдельно согласованные technical spikes.
