# Blood Moon — задача на полную реализацию

## 0. Branch and source of truth

Работать только в:

```text
feat/blood-moon
```

Перед началом полностью прочитать:

```text
docs/tasks/CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md
docs/tasks/blood-moon/01_STATE_AND_SCHEDULE.md
docs/tasks/blood-moon/02_NETWORK_PERSISTENCE_AND_PARTICIPANTS.md
docs/tasks/blood-moon/03_PRESENTATION_AND_SUPPRESSION.md
docs/tasks/blood-moon/04_COMBAT_PROGRESS_AND_RESOLUTION.md
docs/tasks/blood-moon/05_SEASONS_INTEGRATION.md
docs/tasks/blood-moon/06_FUTURE_PROGRESSION_AND_REWARDS.md
docs/tasks/blood-moon/07_BLOOD_CRAFT_AND_WORLD_PRESERVATION.md
docs/tasks/blood-moon/08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md
docs/tasks/blood-moon/09_IMPLEMENTATION_DECISIONS_AND_ORDER.md
docs/tasks/blood-moon/11_VALHEIM_NETWORK_AI_AND_CCS_RESEARCH.md
docs/tasks/blood-moon/12_RELATED_MODS_RESEARCH.md
```

При чтении Valheim сначала использовать:

```text
https://github.com/shudnal/assemblies_combined
```

## 1. Goal

Реализовать Blood Moon в полном объёме принятого дизайна как production-quality подсистему Seasons:

- annual calendar;
- forewarning;
- Marked preparation;
- visual transition/forced env;
- Blood Craft;
- global existing-monster Blood behavior;
- additional marked spawns;
- hidden groups;
- combat routing;
- progress/GoalReached;
- Defeated/Withdrawn;
- persistent outdoor boss parking;
- morning resolution;
- skill rewards;
- chronicle/DreamText;
- debug/diagnostics;
- persistence/network/recovery.

Не ограничиваться архитектурным skeleton или no-op placeholders.

## 2. Constraints

Не делать:

- personal visibility/collision layers;
- `AwaitingContact`;
- vanilla `SoftDeath`;
- clone original monsters;
- persistent ZDO hunt/alert mutation existing monsters;
- temporary persistence unknown bosses;
- material/cosmetic/world-state event rewards;
- mandatory JSON enemy catalog;
- single group-wide spawn coordinator;
- new branch;
- version bump;
- README/Thunderstore changelog/package changes без отдельного запроса.

## 3. Architecture requirements

### State

- explicit event/participant/resolution state machines;
- `GoalReached` separate from ExitReason;
- deterministic eventId;
- idempotent transitions/cleanup.

### Network

- normal CCS `CustomSyncedValue<string>` for global/public snapshots;
- no SequencedCustomSyncedValue;
- targeted own RPC;
- local client authority for Defeated;
- every relevant zone owner performs spawning only in its own zones;
- server grants per-zone spawn lease/budget and enforces group/server caps;
- ownership migration invalidates the old zone revision/lease;
- boss discovery report + server parking.

### Persistence

- world UID binding;
- atomic active snapshot;
- ZDO markers for extras/bosses/items;
- restart recovery;
- safe first install.

### Interaction

- one `BloodMoonInteractionRules`;
- early attack filtering;
- final damage guard;
- immutable projectile/AOE event attribution.

## 4. Required implementation details

### Calendar and phase

Defaults:

```text
Forewarning day 6
Final day 9
Marked 18:00
Active 23:00
Auto-complete 04:15
End 05:45
Morning 06:00
```

### Visual

- Fader clone;
- all Color.r=1;
- wind 1..2;
- sun angle 70;
- linear overlay 18–23;
- force env after 23;
- Ashlands_FaderFX cloud/cloud(1);
- no external assets.

### Existing enemies

Generic predicate and runtime AI patches. Ordinary loot remains.

### Extras

- server computes groups, pool and caps;
- no single peer owns spawning for an entire group;
- every owner of a relevant active zone runs the scheduler for that zone only;
- per-zone lease contains at least `eventId`, `groupId`, zone identity, owner/session, revision and allowance;
- owner uses local surface/interior data;
- marker is written immediately;
- no loot, fast ragdoll, stale cleanup;
- server counts reports against group/server caps;
- reports from stale owner/zone revision are ignored.

### Defeated

Local CheckDeath interception, full resources, narrow DoT cleanup, two-stage protection, no re-entry.

### Bosses

OfferingBowl block; persistent outdoor far parking with ownership/revision protocol; interior/nonpersistent encounter Withdrawn.

### Blood Craft

Known recipe clones, normal recipe retained, temp upgrade only, inventory-only, no stack merge, external sinks blocked, personal cleanup.

### Skills

Implement all configured modes and budgets from file `06`.

## 5. Diagnostics

Commands minimum:

```text
seasons bloodmoon status
seasons bloodmoon start forewarning
seasons bloodmoon start marked
seasons bloodmoon start active
seasons bloodmoon setprogress <0..100>
seasons bloodmoon goalreached
seasons bloodmoon defeat
seasons bloodmoon withdraw
seasons bloodmoon spawn <prefab>
seasons bloodmoon parkboss
seasons bloodmoon restoreboss
seasons bloodmoon resolve
seasons bloodmoon cleanup
seasons bloodmoon dump-participants
seasons bloodmoon dump-groups
seasons bloodmoon dump-spawn-zones
seasons bloodmoon dump-monsters
seasons bloodmoon dump-bosses
seasons bloodmoon dump-sync
```

Commands используют production transition methods.

Structured log prefixes:

```text
[BloodMoon][event:<id>][phase]
[BloodMoon][event:<id>][player:<id>]
[BloodMoon][event:<id>][group:<id>]
[BloodMoon][event:<id>][zone:<zone>]
[BloodMoon][event:<id>][spawn]
[BloodMoon][event:<id>][boss:<zdoid>]
[BloodMoon][event:<id>][resolution]
```

Per-hit logs только diagnostic config.

## 6. Development process

1. Выполнять порядок из `09`.
2. Делать логические commits в одной ветке.
3. После каждого блока собирать проект.
4. Исправлять compile errors до продолжения.
5. Обновлять документы при отличиях фактической реализации.
6. Добавить runtime manual test checklist.
7. После полного результата открыть draft PR `feat/blood-moon → master`.
8. Запустить Codex review.
9. Исправить подтверждённые замечания.
10. PR не merge.

## 7. Expected result of executing this task

Результатом должна быть не новая постановка задачи и не исследовательский отчёт, а законченная реализация Blood Moon в ветке `feat/blood-moon`.

Обязательный конечный набор:

1. Production-код всех подсистем, перечисленных в разделе `Goal`, без no-op core flow и без откладывания уже принятых механик.
2. Логические коммиты в одной ветке, чистый worktree и сохранённая история решений.
3. Успешная сборка проекта; все compile errors устранены.
4. Реальные state machines, persistence/recovery, CCS/RPC, zone-owner spawning, combat rules, Blood Craft, rewards, boss parking и resolution, связанные в один рабочий end-to-end flow.
5. Debug/admin-команды и диагностические дампы, достаточные для дальнейшей настройки и игрового плейтеста владельцем.
6. Обновлённые документы, отражающие фактические patch points, найденные ограничения и отличия от первоначального плана.
7. Manual runtime test checklist с честным разделением:
   - что реально проверено доступными средствами;
   - что требует запуска Valheim и multiplayer-плейтеста владельцем.
8. Draft PR `feat/blood-moon → master`.
9. Отдельный Codex code review этого PR и исправление подтверждённых замечаний.
10. Финальный отчёт с точкой продолжения, если после реальной игры потребуются баланс, VFX/SFX или runtime fixes.

Не является ожидаемым результатом:

- только план;
- только skeleton;
- только несколько первых этапов;
- отдельная spike/MVP-ветка;
- утверждение, что непроверенный в игре runtime гарантированно работает;
- merge PR;
- изменение версии или release-файлов.

## 8. Final report

Указать:

- commits;
- changed files;
- architecture;
- game-code commit;
- CCS/RPC split;
- build commands/result;
- runtime scenarios actually tested;
- what still needs real-game owner playtest;
- performance assumptions;
- compatibility limitations;
- review result;
- continuation point.

## 9. Definition of clean documented result

- рабочая ветка чистая;
- code compiles;
- no TODO/no-op placeholders in core flow;
- docs match code;
- state/recovery/cleanup idempotent;
- all accepted edge policies implemented;
- debug controls exist;
- draft PR/review complete;
- version/release files untouched.
