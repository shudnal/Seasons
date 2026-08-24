# Blood Moon — accepted simplifications and remaining preimplementation decisions

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

> **Статус:** production-код пока не начинать. Основные продуктовые решения закрыты. Выполнить global-event spike для точных Harmony/network/parking points и подтвердить один оставшийся вопрос post-goal outcome.

# 39. Окончательно принятые решения

## 39.1. Общий мир

Нет:

- персональной visibility;
- hidden ordinary monsters;
- cross-layer colliders;
- layer-aware ownership;
- first-contact transition;
- `AwaitingContact`;
- clone original + blood copy.

Все клиенты видят общий набор Blood Moon противников.

## 39.2. Active

В 23:00 Player сразу получает `Fighting`, если он не находится в encounter с boss, который нельзя park.

Ship/ocean, mount, attached и normal interior поддерживаются.

## 39.3. Existing monsters — dynamic direct conversion

Во время Active любой loaded Character считается Blood enemy, если:

```text
alive/valid Character
has MonsterAI
not boss
not tamed
faction != Players
faction != PlayerSpawned
faction != TrainingDummy
BaseAI.IsEnemy(monster, at least one Fighting/GoalReached Player)
```

Не использовать prefab allowlist/hardcoded exclusions как основной механизм.

Следствия:

- passive animals и neutral Dvergr не проходят штатную enemy semantics;
- aggravated Dvergr проходит только когда vanilla считает его врагом;
- hostile unique/modded MonsterAI проходит автоматически;
- existing monster не получает conversion marker;
- ordinary loot/ragdoll сохраняются;
- killed existing monster не восстанавливается;
- survivor не удаляется;
- permanent max health/shared prefab/ZDO hunt state не меняются;
- Blood behavior реализуется условными runtime-патчами;
- cleanup удаляет только event VFX/transient caches.

Diagnostic dump обязан показывать inclusion/exclusion reason.

## 39.4. Extra spawns

Только custom-spawned enemy получает:

```text
SpawnedEventId
GroupId
Role
```

Для marked extra:

- no ordinary loot;
- fast ragdoll;
- delete surviving/stale marked ZDO;
- ordinary existing ZDO никогда не удалять этим cleanup.

## 39.5. Defeated

```csharp
BloodMoonParticipantOutcome.Defeated
```

- no `Player.OnDeath`;
- intercept `Character.CheckDeath` at health <= 0;
- no grave/death point/ragdoll/respawn;
- full health/stamina/eitr;
- food/adrenaline unchanged;
- no position/rotation/parent/velocity changes;
- terminal, no re-entry.

Vanilla `SoftDeath` status не добавляется. Собственный status effect объясняет безопасное поражение.

## 39.6. DoT и recovery

Очищать только:

- `SE_Burning`;
- `SE_Poison`;
- `SE_Smoke`;
- negative-tick `SE_Stats`.

Recovery:

```text
Stage 1: full immunity until IsOnGround || IsSwimming || IsAttached, max 15 sec
Stage 2: 10 sec, incoming multiplier 0.25
```

Recovery имеет собственный конечный таймер и не обрывается только из-за morning resolution.

## 39.7. Withdrawn

Terminal `Withdrawn` применяется:

- до edge/tidal danger через `IsBeyondWorldEdge(..., positiveOffset)`;
- affected Player в encounter с interior boss;
- affected Player в encounter с nonpersistent/unparkable boss.

Re-entry отсутствует. Body blocking остаётся vanilla. Personal Blood Craft cleanup выполняется сразу.

## 39.8. Boss summon block

С 18:00 boss-producing `OfferingBowl` не принимает новые sacrifices. Guard:

- `UseItem`;
- item-stand `Interact`;
- authoritative `RPC_SpawnBoss`.

Queued до 18:00 spawn не отменять после item consumption.

## 39.9. Persistent outdoor boss parking

Far-sector server ZDO parking принят как production direction.

Park только:

- alive boss;
- outdoor/not interior;
- `ZDO.Persistent == true`.

Nonpersistent boss не park и не переводить временно в persistent. Совместимость с конкретным модовым boss добавляется только по обращениям.

Parking contract:

1. marker/original position first;
2. server takes ownership;
3. live transform/rigidbody и ZDO переводятся в deterministic far XZ slot;
4. velocities zeroed;
5. sector invalidation/force sync;
6. restore original position;
7. clear marker last;
8. old peer owner не восстанавливается.

Не использовать `y < -5000`.

Требуется восстановить тот же ZDO на исходную позицию. Не требуется сохранять exact animation, target, velocity, coroutine, HUD или runtime-only mod fields.

## 39.10. Boss residual objects

Projectiles, AOE, summons/minions и delayed effects специально не удаляются. Они завершают собственный lifecycle.

Если source всё ещё определяется как ordinary boss, общая damage policy не делает его Blood source. Boss minion, проходящий ordinary predicate, может стать Blood enemy.

## 39.11. Interior extra spawn

Использовать только позиции уже загруженных `CreatureSpawner`:

- same interior/location context;
- full path до Player;
- optional small navmesh snap around authored point;
- не вызывать `CreatureSpawner.Spawn()`;
- не генерировать случайные navmesh points fallback;
- no valid spawner point → no extra spawn.

Появление из другой комнаты допустимо и желательно при существующем пути.

## 39.12. AI mutation

Не менять persistent AI settings и shared prefab:

- не вызывать `SetHuntPlayer(true)` для existing monster;
- не писать event hunt/alert state в ZDO;
- не пытаться восстановить pre-event target history.

Вмешиваться conditionally в вызовы target selection, flee/idle, damage и speed. После Active policy становится false; остаются только обычные последствия реального боя.

## 39.13. Live balance configs

Применяются на лету:

- damage multipliers — next hit;
- speed/aggression — next AI update;
- interval/radii — next scheduler tick;
- group distances — next recompute;
- raised cap allows new spawn;
- lowered cap не удаляет live extras, а блокирует новые;
- changed pool/weights влияет на future spawns.

Event calendar и absolute timestamps текущей ночи не hot-reloadятся.

# 40. Единственный оставшийся продуктовый вопрос

## GoalReached, затем personal exit

`GoalReached` Player остаётся в бою, пока помогает другим, поэтому он ещё может получить `Defeated`, `Withdrawn` или disconnect.

Рекомендация:

- `GoalReached=true` фиксируется необратимо для текущего eventId;
- completion reward и Success не отнимаются;
- последующая причина выхода хранится отдельно (`Defeated`, `Withdrawn`, `Disconnected`);
- DreamText/statistics могут учитывать оба факта;
- state/outcome model не должна пытаться уместить эти два независимых факта в один взаимоисключающий enum.

Нужно явное подтверждение владельца перед production state model.

# 41. Остались только технические runtime-вопросы

## 41.1. AI patch surface

Нужно определить минимальные точки:

- target acquisition/validation;
- static target exclusion;
- flee/idle override;
- outgoing/incoming multipliers;
- speed/aggression;
- projectile/AOE attribution;
- exactly-once progress.

Критерий: no persistent mutation и одна central policy.

## 41.2. Boss discovery на dedicated server

Server может не иметь live `Character` instance для client-owned boss.

Spike должен проверить:

- owner/observer report ZDOID + observed `InInterior()`;
- server prefab/`IsBoss`/`Persistent`/position validation;
- duplicate/stale report;
- boss created after Active begins;
- transaction only on server.

## 41.3. Parking transaction

Нужны runtime evidence:

- ownership handoff;
- old-owner transform race;
- live instance vs raw ZDO path;
- far-sector unload;
- force-send/sector invalidation;
- multiple bosses/slots;
- restart/stale marker/idempotent restore;
- no Player nearby during restore.

## 41.4. Defeated and network

Проверить provisional local `CheckDeath` interception до round-trip, server validation, duplicate report и reconnect/recovery behavior.

## 41.5. Interior spawner positions

Проверить, доступны ли нужные `CreatureSpawner` instances и full path в типовых crypt/cave/mine/dungeon layouts. Failure просто означает отсутствие extras в конкретном interior.

## 41.6. CCS/RPC split

Нужно подтвердить фактические queue guarantees CCS и зафиксировать:

- global current snapshot;
- редкие ordered transitions;
- targeted participant updates;
- owner reports;
- fade ACK.

# 42. Production gate

До production получить runtime evidence для:

1. generic eligibility predicate;
2. dynamic Blood behavior без persistent mutation;
3. marked extra loot/cleanup;
4. centralized target/damage/projectile routing;
5. `Defeated` + recovery;
6. OfferingBowl block/race;
7. boss discovery and persistent outdoor parking;
8. restart/idempotent restore;
9. interior/nonpersistent boss → affected `Withdrawn`;
10. CreatureSpawner-based interior extras;
11. live config semantics;
12. final Success + later exit model.

Только `10_GLOBAL_EVENT_RUNTIME_SPIKE.md` может создавать debug code до снятия gate.
