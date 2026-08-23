# Blood Moon — preimplementation decisions and parallel-layer spikes

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

> **Статус:** production-разработку Blood Moon пока не начинать. Этот документ имеет приоритет над `01`–`08` для participant entry, dream collapse, context gates, local visibility/collision, mount handling и ordinary-world simulation. Разрешены только документация и изолированные runtime spikes.

# 27. Принятые решения

## 27.1. Никаких принудительных transform-изменений

Blood Moon не меняет Player:

- position;
- rotation;
- parent/attach state;
- velocity через восстановление snapshot;
- Y через raycast к земле;
- положение корабля или mount.

Допустимы:

- status effects;
- health/stamina/eitr через штатные API;
- fade, UI, VFX, emote;
- local presentation/collision rules;
- обычное добровольное действие Player, которое само вызывает vanilla detach/dismount.

`Character.m_lastGroundPoint` — последняя contact point, а не безопасная позиция capsule. Она может быть устаревшей и относиться к движущемуся Rigidbody. Positional backup/restore удалён из дизайна.

## 27.2. Outcome поражения

Кодовое имя:

```csharp
BloodMoonParticipantOutcome.Defeated
```

Не использовать `Death` для иллюзорного поражения: настоящего `Player.OnDeath` не происходит.

## 27.3. `SoftDeath` — только знакомая индикация

Во время активного боя Player получает vanilla `SoftDeath`, чтобы сразу видеть знакомый признак безопасной «смерти».

Это только presentation:

- `Player.HardDeath()` определяется `m_timeSinceDeath`;
- наличие `SoftDeath` status само по себе не меняет `HardDeath()`;
- реальная гарантия отсутствия skill loss состоит в том, что dream collapse не вызывает `Player.OnDeath`.

## 27.4. Re-entry

В первой версии re-entry после `Defeated` отсутствует. Поражение терминально для текущего `eventId`.

## 27.5. Постоянный результат события

Единственная постоянная механическая награда — боевые навыки Player. Blood Moon не выдаёт предметы, currency, recipes, keys, декор или другие world-state rewards.

# 28. Participant entry: вторжение и первый контакт

## 28.1. Фаза `AwaitingContact`

В 23:00 eligible Player переходит в `AwaitingContact`.

До первого контакта:

- Blood Moon environment/VFX активны;
- blood enemies видимы Player и могут выбрать его целью;
- ordinary creatures, tamed и остальной real-world слой ещё видимы и интерактивны;
- Bloodlust combat contribution ещё не начат;
- transform не меняется.

То есть в 23:00 начинается **вторжение**, но полный разрыв с real world происходит только после первого принятого blood interaction.

## 28.2. Первый контакт

Рекомендуемый и принимаемый для spike контракт — **accepted attack contact**, а не только потеря health.

Контакт:

- incoming blood hit;
- outgoing melee hit по blood enemy;
- outgoing projectile hit по blood enemy;
- block;
- parry;
- полностью mitigated/resisted hit, дошедший до block/damage pipeline.

Не контакт:

- miss;
- near-projectile notification;
- aggro/обнаружение;
- trigger overlap без attack resolution.

Первый contact должен атомарно:

1. создать `FirstBloodContactRecord`;
2. locally перевести owner Player в `Fighting` без ожидания round-trip;
3. включить full blood-only layer;
4. применить Bloodlust defensive routing уже к этому же incoming hit;
5. отправить server report;
6. пройти server validation/deduplication;
7. при rejection восстановиться из authoritative snapshot.

`FirstBloodContactRecord`:

```text
eventId
stable player ID
current peer/session UID
Player ZDOID
authoritative/local report timestamp
contact kind
blood enemy ZDOID
position только для диагностики
```

Position никогда не используется как restore anchor.

# 29. Participant state и context gate

## 29.1. Participant phase

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

- `AwaitingContact` — вторжение видно, full layer ещё не включён;
- `Fighting` — первый blood contact подтверждён;
- `GoalReached` — progress 100%, full layer и buff остаются;
- `Ejected` — Player покинул blood layer и больше не участвует;
- `Resolved` — итог опубликован и временное состояние очищено.

Outcome хранится отдельно. Минимум:

```csharp
None
Success
Defeated
Disconnected
HiddenAtBase
HiddenInWild
LateWitness
Skipped
```

Для добровольного/контекстного выхода после first contact может понадобиться отдельный `Withdrawn`; это ещё не принято.

## 29.2. Context gate — отдельное измерение

Не кодировать dungeon/ship/boss как participant phases. Хранить отдельную причину невозможности engagement, например:

```csharp
internal enum BloodMoonEngagementGate
{
    None,
    Teleporting,
    Interior,
    ShipOrOcean,
    BossEncounter
}
```

Gate определяет spawn/visibility/force-environment policy, но не разрушает participant identity.

# 30. Принятые context policies

## 30.1. Outdoor ground

- full `AwaitingContact`;
- blood enemies могут появляться и атаковать;
- first contact включает full layer.

## 30.2. Dungeon/interior

- красная атмосфера и Blood Moon forced environment остаются;
- blood enemies не создаются;
- full blood-only layer не включается;
- при выходе в поддерживаемый outdoor context до 05:45 Player может перейти в нормальный `AwaitingContact`.

## 30.3. Ship/ocean

- красная атмосфера и forced environment остаются;
- blood enemies не создаются;
- full layer не включается;
- Player не снимается с корабля и корабль не перемещается;
- после выхода на поддерживаемую сушу eligibility пересчитывается.

Точное определение `ShipOrOcean` проверить runtime: `m_attachedToShip`, current doodad controller, Ocean biome, swimming/liquid state и фактическая возможность surface spawn не должны давать противоречивые результаты.

## 30.4. Generic attached, не ship и не mount

- не выполнять forced detach;
- Player может оставаться `AwaitingContact`;
- blood enemy может нанести первый hit;
- full layer включается без изменения attach state;
- Player сам прекращает attachment через vanilla input.

## 30.5. Mounted Player

Forced dismount не является базовым решением. `Player.StopDoodadControl()` вызывает `Sadle.OnUseStop()`, затем `Player.AttachStop()`, а vanilla `AttachStop()` устанавливает position в `m_attachPoint.TransformPoint(m_detachOffset)`. Это нарушает жёсткое требование не менять transform принудительно.

Рекомендуемый spike — **blood-bound mount bridge**:

- mount определяется через `Player.GetDoodadController() is Sadle`;
- first blood hit по rider либо по текущему ridden mount считается contact rider;
- damage по mount от blood source равен нулю;
- rider входит в `Fighting` без dismount;
- текущий mount остаётся локально видимым и controllable rider;
- mount временно исключается из ordinary-hidden set только для rider;
- blood enemies не выбирают mount целью, их target — rider;
- ordinary enemies не выбирают/не повреждают blood-bound mount;
- mount не наносит blood damage и не становится reward source;
- voluntary dismount освобождает bridge: mount сразу возвращается в ordinary layer и скрывается/становится неинтерактивным для продолжающего `Fighting` Player;
- nonparticipant clients продолжают видеть обычный rider+mount.

Если этот bridge окажется ненадёжным, fallback — mounted Player остаётся context-deferred до добровольного dismount. Forced dismount не использовать без отдельного отказа от no-transform правила.

## 30.6. Active boss encounter

Локальный критерий продукта — Player видит boss health bar.

- пока `EnemyHud.instance.ShowingBossHud()` true, Player остаётся context-deferred;
- blood enemies не создаются и full layer не включается;
- boss/ordinary combat остаётся обычным;
- Blood Moon не должен ломать boss bookkeeping;
- предпочтительно сохранить boss environment/music, применяя только совместимый red overlay;
- после исчезновения boss bar и короткой stability delay eligibility пересчитывается;
- если до 05:45 время остаётся, Player входит в `AwaitingContact`.

Client сообщает boss-context server. Это low-trust delay signal: ложный positive только уменьшает личную награду. При необходимости server выполняет дополнительную nearby-boss validation, но не пытается воспроизводить UI-логику полностью.

## 30.7. Edge of world

Использовать уже существующую геометрию Seasons:

```csharp
ZoneSystemVariantController.IsBeyondWorldEdge(position, offset)
```

До зоны, где vanilla tidal/edge forces становятся опасными, принудительно снять Bloodlust и вывести Player в real world. Не ждать `HitData.HitType.EdgeOfWorld`.

Если после выхода Player всё равно погибает у края мира, это обычная vanilla death. Точный terminal outcome для профилактического выхода (`Withdrawn`/другой) ещё требуется назвать.

# 31. Dream collapse — принятое поведение

## 31.1. Точка перехвата

Owner-side `Character.CheckDeath` prefix для `Player`:

```text
phase == Fighting || phase == GoalReached
GetHealth() <= 0
collapse ещё не обработан
```

Prefix восстанавливает Player и не даёт `CheckDeath()` вызвать `Player.OnDeath()`.

Не patch-ить `Player.OnDeath` как универсальный перехват:

- direct admin/scripted `OnDeath` остаётся vanilla;
- scripted removal не является dream collapse;
- admin damage, реализованный обычным снижением health до `<= 0`, естественно проходит через общие Blood Moon rules.

## 31.2. Scope lethal damage

Любой обычный lethal damage в `Fighting`/`GoalReached` вызывает dream collapse независимо от attribution:

- direct blood hit;
- fall после knockback;
- projectile/AOE;
- environmental damage;
- обычный health reduction.

Это гарантирует, что иллюзорный бой никогда не создаёт TombStone из-за потерянной attribution.

`EdgeOfWorld` предотвращается предварительным ejection. Direct forced `Player.OnDeath` не перехватывается.

## 31.3. Результат collapse

Не происходит:

- death point;
- death effects/ragdoll;
- TombStone;
- inventory/equipment transfer;
- food clear;
- respawn request;
- position/rotation/velocity restore.

Происходит:

```text
Health  = current max
Stamina = current max
Eitr    = current max
Adrenaline unchanged
Food unchanged
Outcome = Defeated
Phase   = Ejected
```

- удалить Bloodlust combat effects;
- очистить damaging DoT status effects;
- вернуть ordinary presentation/interactions;
- запретить повторный вход текущего `eventId`;
- death-specific DreamText показать при общем resolution.

Точный generic DoT classifier нужно проверить. Не вызывать `RemoveAllStatusEffects`; buffs, food и unrelated effects сохраняются.

## 31.4. Recovery grace

После `Ejected`:

### Stage 1 — full protection

100% damage immunity до первого стабильного возвращения на опору.

Основной сигнал:

```csharp
Player.IsOnGround()
```

Чтобы не получить бесконечную invulnerability в воде/на mount/attached, spike обязан отдельно проверить и утвердить «stabilized» equivalent:

```text
IsOnGround
или IsSwimming
или IsAttached
```

До утверждения не добавлять произвольный timer cap.

Stage 1 должен покрыть landing/fall damage текущего падения.

### Stage 2 — reduced damage

После stabilization:

```text
10 секунд
75% reduction incoming damage
```

То есть final incoming multiplier `0.25`.

- applies to ordinary incoming damage, включая environment;
- не меняет transform/velocity;
- lava остаётся ошибкой Player: после grace обычный lava damage полностью возвращается;
- food/adrenaline не восстанавливаются.

# 32. Layer implementation candidate

## 32.1. Не связывать видимость с ownership

Ownership определяет, кто симулирует ZDO. Он не должен определять, кто локально видит entity.

Не использовать массовый `SetOwner(0)`:

- `ZDOMan.ReleaseNearbyZDOS` снова назначает persistent ownerless ZDO active peer;
- возможен возврат тому же participant;
- возникает owner revision churn;
- owner-targeted RPC становятся неоднозначными;
- nonpersistent modded objects могут попасть под orphan cleanup.

## 32.2. Layer membership

Нужна единая классификация:

```csharp
RealWorld
SharedPlayer
BloodAwaitingContact
BloodParticipant
BloodEnemy
BloodBoundMount
```

И единая policy:

```csharp
CanSee(localPlayer, entity)
CanCollide(localPlayer, entity)
CanTarget(attacker, target)
CanDamage(source, target, hit)
CanInteract(player, object)
```

Все Harmony patches используют эту policy, а не свои списки.

## 32.3. Local presentation

Нельзя выключать root GameObject или AI: скрывающий client может быть ZDO owner и обязан симулировать entity для других peers.

Локально управлять отдельно:

- Character visual/LOD;
- audio;
- EnemyHud;
- local Player ↔ hidden Character main-collider pair;
- hitboxes для local attacks;
- projectile/AOE acceptance;
- local hover/interaction.

Для visibility spike предпочтительно перехватывать/дополнять штатный `Character.SetVisible`, а не каждый кадр хаотично переключать root object.

`EnemyHud.TestShow`/эквивалент должен использовать ту же visibility policy.

## 32.4. Collision и attacks

`Physics.IgnoreCollision(localPlayer.m_collider, hiddenCharacter.m_collider)` решает только body collision. Он не делает hidden hitboxes прозрачными для raycast/projectile.

Нужны отдельные paths:

- melee/attack target filtering;
- projectile hit filtering;
- projectile должен пропускать incompatible Character collider и продолжать к следующему ray hit;
- AoE overlap фильтруется до `Damage`;
- final `Character.Damage`/`WearNTear` guard остаётся defense-in-depth;
- blood/ordinary projectile и persistent AOE получают `eventId`/layer attribution при создании.

## 32.5. AI target filtering

- event enemy видит только `AwaitingContact`, `Fighting`, `GoalReached` current event;
- ordinary AI не видит `Fighting`/`GoalReached` Player;
- existing target очищается после layer switch;
- event enemy не выбирает static targets/buildings/tamed;
- `GoalReached` получает lower priority только если рядом есть незавершившиеся.

Сначала проверить, достаточно ли central `BaseAI.IsEnemy` + event-specific static-target gate. Не patch-ить весь AI pipeline без необходимости.

# 33. Conditional suspension ordinary world

## 33.1. Что означает «рядом нет real-world Player»

Для ordinary Character в позиции/sector `S` real-world witness существует, если:

- Player/peer ready;
- его participant phase не `Fighting`/`GoalReached`;
- его reference position/instantiated Player находится в active area либо настроенном witness radius от entity;
- он реально способен видеть ordinary world.

`AwaitingContact` и `Ejected` считаются real-world witnesses, потому что ordinary layer для них видим.

## 33.2. Owner transfer не обязателен

Если ordinary entity owned blood participant, но рядом есть nonparticipant witness, текущий owner может продолжать vanilla simulation для наблюдателя, оставаясь локально скрытым от себя.

Прямой owner transfer к nonparticipant — optional optimization, не базовое требование.

## 33.3. Policy order

### Spike baseline — background simulation

Сначала не suspend ordinary AI вообще:

- доказать local visibility/collision/damage isolation;
- проверить owner participant/nonparticipant;
- измерить, какие реальные проблемы остаются.

### Второй spike — conditional suspension

Если ordinary entity owned blood participant и real-world witness отсутствует:

- owner сохраняется;
- узкий gate останавливает BaseAI/MonsterAI movement/targeting;
- при появлении witness или ejection AI продолжается;
- current ridden `BloodBoundMount` никогда не suspend-ится;
- это не полный snapshot freeze: Character physics/status/procreation могут продолжаться.

Предпочтительный scope:

```text
interaction radius группы + enter/leave hysteresis
```

Не весь мир и не все active sectors.

Если suspension хрупкая, production fallback — background simulation.

# 34. Переходы в unsupported context после first contact

Это остаётся прямым блокером.

Если `Fighting` Player:

- входит в dungeon;
- садится на корабль/уходит в Ocean;
- начинает boss encounter;
- телепортируется;

нужно выбрать одно:

1. terminal `Withdrawn`/ejection без heal;
2. временно suspend personal event с возвратом ordinary layer;
3. запретить конкретное interaction;
4. сохранить blood layer с context-specific behavior.

Предварительная рекомендация для простоты и agency: terminal withdrawal без skill reward и без transform changes. Но имя outcome и resource behavior ещё не утверждены.

# 35. Runtime spikes до production

Все spikes выполнять в отдельной ветке от `feat/blood-moon`, без PR в `master` и без version bump.

Рекомендуемая ветка:

```text
spike/blood-moon-parallel-layer
```

## Spike 1 — local layer matrix

Минимальный debug harness с одним ordinary enemy и одним event enemy.

Два клиента; проверить четыре owner combinations:

```text
event enemy owned participant
event enemy owned observer
ordinary enemy owned participant
ordinary enemy owned observer
```

Acceptance:

- participant видит ordinary+event в `AwaitingContact`;
- после forced debug contact participant видит event enemy, но не ordinary enemy;
- observer видит ordinary enemy и participant, но не event enemy;
- hidden entity AI продолжает работать, если client owner;
- no root GameObject disable;
- EnemyHud/audio/body collision восстанавливаются без stale state.

## Spike 2 — hit transparency and routing

- melee;
- arrow/bolt;
- event projectile;
- AoE;
- hidden ordinary collider между attacker и event enemy;
- hidden event collider между observer projectile и ordinary target;
- final damage matrix.

Acceptance: incompatible hidden Character не блокирует projectile/raycast и не получает damage.

## Spike 3 — first contact

- incoming hit;
- outgoing melee;
- outgoing projectile;
- block;
- parry;
- lethal first hit;
- duplicate/stale report;
- server rejection/resync.

Acceptance: layer switches before first accepted hit is resolved.

## Spike 4 — dream collapse

- `Character.CheckDeath` prefix;
- `Player.OnDeath` not called;
- no death point/TombStone/ragdoll/respawn;
- full health/stamina/eitr;
- food/adrenaline unchanged;
- DoT cleanup;
- `Defeated` exactly once;
- no re-entry;
- airborne landing immunity then 10s 75% reduction;
- lava and fall scenarios;
- direct forced `Player.OnDeath` remains vanilla.

## Spike 5 — contexts

- dungeon/interior;
- ship/ocean;
- generic attached;
- mounted bridge;
- teleport;
- visible boss HUD → hidden boss HUD;
- world-edge preventive ejection.

## Spike 6 — ordinary simulation

После успешных spikes 1–5 сравнить:

- background simulation;
- conditional AI suspension;
- observer enters/leaves;
- owner disconnect/migration;
- tamed/flying/swimming AI;
- network/CPU impact.

# 36. Gate для production-кода

Уже закрыто:

1. entry model — switch on first accepted contact;
2. defeat outcome — `Defeated`;
3. lethal scope — любой обычный lethal в `Fighting`/`GoalReached`;
4. no `Player.OnDeath` for dream collapse;
5. full health/stamina/eitr; food/adrenaline unchanged;
6. DoT cleanup;
7. no re-entry;
8. SoftDeath only as indicator;
9. no mass owner clearing;
10. dungeon/interior and ship/ocean deferred policy;
11. boss-HUD deferral;
12. no forced dismount; mounted bridge is preferred spike.

Остаётся закрыть runtime spikes и решения:

1. exact local visibility/audio/collider implementation;
2. projectile/melee/AoE transparency;
3. exact first-contact Harmony points;
4. generic DoT classifier;
5. stabilization condition for recovery grace in water/attached states;
6. viability of blood-bound mount bridge;
7. server/client boss-context reporting;
8. outcome for preventive edge exit;
9. behavior when `Fighting` enters unsupported context;
10. background simulation versus conditional suspension;
11. real-world interaction restrictions in full layer;
12. projectile/AOE/summon attribution for production.

Пока эти spikes не завершены:

- production implementation не начинать;
- implementation PR в `master` не открывать;
- version/release files не менять;
- spike-код держать изолированным и легко удаляемым либо переводимым в production после review.
