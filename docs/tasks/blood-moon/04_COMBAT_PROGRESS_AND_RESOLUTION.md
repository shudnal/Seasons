# Blood Moon — combat, progress, defeat and resolution

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

# 18. Hidden combat groups

Server-only grouping:

- recompute 3–5 seconds;
- connected components by distance;
- merge default 120 m;
- split default 160 m;
- stable ID by maximum member overlap;
- cap from participant count;
- server hard cap;
- spawn anchor = real member, not geometric center.

В группы входят `Fighting` и `GoalReached`.

`Exited`, `Resolved` и disconnected не входят.

Map markers отсутствуют.

# 19. Blood Moon enemies

В 23:00 используются два источника:

1. все загруженные eligible небоссовые `MonsterAI`;
2. дополнительные custom-spawned enemies до group/server cap.

## 19.1. Прямая динамическая конверсия — принято

Suspend+clone отвергнут.

Существующий monster считается Blood enemy по текущему event state и общим игровым признакам:

```text
Blood Moon Active
AND Character alive/valid
AND has MonsterAI
AND not boss
AND not tamed
AND faction not Players/PlayerSpawned/TrainingDummy
AND BaseAI.IsEnemy(monster, at least one Fighting/GoalReached Player)
```

Это generic predicate без hardcoded prefab names:

- passive animal исключается штатной faction semantics;
- neutral Dvergr исключается, aggravated Dvergr допускается, если vanilla считает его врагом;
- named/modded hostile MonsterAI допускается;
- Player summon исключается faction `PlayerSpawned`;
- fish/bird/ambient objects обычно не имеют подходящего hostile `MonsterAI` и не проходят predicate.

Нужен diagnostic dump inclusion/exclusion reason, но продуктовая граница считается принятой.

Поведение существующего monster реализуется conditionally через policy/patches:

- Player-only target selection;
- no flee/idle при наличии допустимой цели;
- event damage multipliers;
- event visual treatment;
- no damage ordinary world.

Не вызывать persistent setters вроде `SetHuntPlayer(true)`, не менять ZDO alert/hunt fields и shared prefab. Vanilla AI может естественно оставить survivor рядом/alerted после события — точное восстановление pre-event AI history не требуется.

Если existing monster погиб во время события:

- это обычная смерть реального существа;
- ordinary loot разрешён;
- ordinary ragdoll/cleanup остаются;
- объект не восстанавливается.

Если existing monster пережил событие:

- его ZDO не удаляется;
- global Blood behavior прекращается вместе с Active;
- cleanup удаляет только Blood Moon VFX и собственные transient caches.

## 19.2. Дополнительные event spawns

Только custom-spawned противники получают marker:

```text
Seasons.BloodMoon.SpawnedEventId
Seasons.BloodMoon.GroupId
Seasons.BloodMoon.Role
```

Marker должен быть записан до полноценного combat participation.

Для marked extras:

- ordinary loot подавляется;
- ragdoll быстро очищается, default 2 seconds;
- выжившие ZDO удаляются server-side при resolution;
- stale marker другого/завершённого event удаляется при recovery/load.

Server итерирует copy коллекции ZDO и никогда не удаляет unmarked ordinary ZDO.

## 19.3. Dynamic discovery

Не требуется помечать все существующие ZDO мира.

- любой loaded eligible Character автоматически квалифицируется через policy;
- newly loaded/spawned ordinary monster во время Active автоматически становится Blood enemy;
- periodic scan нужен только для VFX, diagnostics и cached runtime helpers;
- не сканировать весь world каждый frame.

## 19.4. Aggression

Blood enemy:

- всегда ищет active participant;
- не flee/idle при наличии допустимой цели;
- игнорирует PlayerBase/NoMonsters только для additional spawn;
- не выбирает static targets;
- не выбирает tamed/NPC/boss/другого monster;
- GoalReached target имеет меньший score, пока есть Fighting;
- ordinary event-creature despawn rules не должны удалять его из-за остановленного RandEventSystem.

## 19.5. Health/damage

Меньшая живучесть задаётся event-specific incoming damage multiplier.

Меньший исходящий урон — event-specific outgoing multiplier.

Existing health/max health не переписываются.

# 20. Damage routing

Разрешено:

```text
Blood enemy → Fighting/GoalReached Player
Fighting/GoalReached Player attack → Blood enemy
```

Запрещены:

- buildings;
- crops;
- trees/ores/resource objects;
- tamed;
- ordinary NPC;
- bosses;
- nonparticipants/Exited;
- traps/turrets/environment как способ убивать Blood enemies по умолчанию.

Фильтровать candidate hit до status/stagger/skill credit; final damage guard оставить.

Projectile/AOE/summon должен сохранять event/source attribution при создании, если исходного `HitData.m_attacker` недостаточно после delayed hit или owner migration.

Lingering projectile/AOE/summon запаркованного boss специально не удаляется. Он завершает собственный lifecycle; если source всё ещё определяется как ordinary boss, общая damage policy не должна превращать его в Blood source.

# 21. Progress

Разделить:

```text
CombatBloodlustPoints
DisplayedBloodlustProgress
CombatContribution
GoalReached
ExitReason
```

Kill/share:

- server validates enemy identity и exactly-once death;
- points получают active members группы в configured radius;
- не только last hit;
- GoalReached actions могут идти в stats, но не progress;
- kills existing и marked extra enemies учитываются одинаково, стоимость задаёт server.

At 100%:

- phase → `GoalReached`;
- `GoalReached=true` фиксируется необратимо для reward текущего eventId;
- full buff remains;
- lower aggro only while unfinished targets exist.

Рекомендованная ещё подлежащая явному подтверждению семантика: последующий `Defeated`, `Withdrawn` или disconnect завершает участие и влияет на DreamText/statistics, но не снимает уже заработанный Success/reward.

Auto-complete:

```text
Displayed = max(combatProgress, automaticFloor)
```

Не даёт points/contribution/reward/Success.

# 22. Defeated dream collapse

Owner-side `Character.CheckDeath` intercept:

```text
Player
phase Fighting/GoalReached
health <= 0
not already Exited
```

No `Player.OnDeath`.

Restore:

```text
Health = max
Stamina = max
Eitr = max
Food unchanged
Adrenaline unchanged
```

Outcome:

```text
phase = Exited
exit reason = Defeated
```

## DoT cleanup

Удалять только вычислимо damaging effects:

- `SE_Burning`;
- `SE_Poison`;
- `SE_Smoke`;
- `SE_Stats` с `m_tickInterval > 0 && m_healthPerTick < 0`.

Не использовать `RemoveAllStatusEffects`. Unknown modded DoT не удалять автоматически.

## Recovery protection

Stage 1:

```text
100% incoming protection
until IsOnGround || IsSwimming || IsAttached
hard cap 15 seconds
```

Stage 2:

```text
10 seconds
75% incoming reduction
final multiplier 0.25
```

Recovery protection не зависит от global event phase и не обязана исчезать при morning resolution раньше собственного конечного срока.

Direct `Player.OnDeath`/scripted removal не перехватывать. Admin HP reduction естественно приводит к `Defeated`.

# 23. Edge withdrawal

До vanilla edge death:

```csharp
ZoneSystemVariantController.IsBeyondWorldEdge(position, positiveSafetyOffset)
```

→ phase `Exited`, exit reason `Withdrawn`.

No transform changes. Re-entry отсутствует.

# 24. Contexts

- outdoor ground: `Fighting`;
- mounted/attached: `Fighting`, no forced detach;
- ship/ocean: `Fighting`; land additional spawner может не найти поверхность, existing sea monsters продолжают работать;
- ordinary interior/dungeon: `Fighting`; existing monsters становятся Blood enemies;
- additional interior spawn использует только позиции загруженных `CreatureSpawner`, относящихся к тому же interior/location, с полным path validation; если точки нет, spawn пропускается;
- teleport: временно pause spawn-anchor use, затем продолжить в destination;
- encounter с interior boss или nonpersistent/unparkable boss: affected Player получает terminal `Withdrawn`, personal Blood Craft cleanup и no re-entry.

Body blocking Exited Player-ом остаётся vanilla.

# 25. Live balance configs

Runtime tuning должно применяться без перезапуска и без пересоздания event state:

- incoming/outgoing multipliers — со следующего hit;
- speed/aggression parameters — со следующего AI update;
- spawn interval/radii — со следующего scheduler tick;
- group merge/split distance — с немедленным или ближайшим group recompute;
- увеличение cap разрешает новые spawns сразу;
- уменьшение cap не удаляет уже живых marked extras, а блокирует новые до снижения count;
- pool/weight change влияет только на новые extra spawns;
- ragdoll cleanup delay влияет на последующие deaths.

Calendar/schedule timestamps текущего event остаются замороженными и меняются только со следующего года.

# 26. Early/morning resolution

Early end:

- хотя бы один Player был enrolled;
- все enrolled имеют terminal Success/Defeated/Withdrawn/Disconnected.

05:45 forced resolution.

Prepare:

1. freeze enrollment;
2. stop additional spawn;
3. freeze outcomes;
4. fade request + timeout.

Resolve under fade:

1. disable global Blood Moon monster behavior;
2. delete marked extra enemy ZDO;
3. remove event VFX/transient caches from existing monsters;
4. restore parked bosses;
5. clean Blood Craft;
6. remove force environment/VFX;
7. restore RandEventSystem;
8. time → 06:00;
9. remove Rested;
10. publish DreamText;
11. release input.

No Player transform changes. Boss residual projectiles/AOE/summons специально не итерируются и не удаляются.
