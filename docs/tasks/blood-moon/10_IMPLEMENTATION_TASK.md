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
- zone-owner spawn coordinator;
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

Coordinator client spawn, marker immediately, no loot, fast ragdoll, stale cleanup.

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

## 7. Final report

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

## 8. Definition of clean documented result

- рабочая ветка чистая;
- code compiles;
- no TODO/no-op placeholders in core flow;
- docs match code;
- state/recovery/cleanup idempotent;
- all accepted edge policies implemented;
- debug controls exist;
- draft PR/review complete;
- version/release files untouched.
