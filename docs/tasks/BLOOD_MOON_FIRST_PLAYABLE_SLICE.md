# Blood Moon — first playable vertical slice

> **Status:** authoritative implementation task for `feat/blood-moon`  
> **Repository:** `shudnal/Seasons`  
> **Base:** current `master` (`1.8.2` at task creation)  
> **Working branch:** `feat/blood-moon`  
> **Scope:** combine the former foundation, presentation/lifecycle, and first combat prototype stages into one complete, reviewable vertical slice.

This file is the source of truth for the first Blood Moon implementation. Do not use the old `BloodMoon_Design_Document.md` or `BloodMoon_Codex_Implementation_Brief.md`; they predate the decisions below and contain obsolete timings and architecture.

## 1. Working rules

1. Work only on `feat/blood-moon`. Do not modify or merge `master` directly.
2. Before editing game-facing code, inspect the current decompiled game sources in `shudnal/assemblies_combined`, especially:
   - `assembly_valheim/EnvMan.cs`
   - `assembly_valheim/RandEventSystem.cs`
   - `assembly_valheim/Player.cs`
   - `assembly_valheim/ZNet.cs` and `ZNetPeer.cs`
   - `assembly_valheim/ZoneSystem.cs`
   - `assembly_valheim/SEMan.cs`
   - `assembly_valheim/Character.cs`, `MonsterAI.cs`, `BaseAI.cs`
   - `assembly_valheim/SpawnSystem.cs`
   - `assembly_valheim/CharacterDrop.cs`, `Ragdoll.cs`
   - `assembly_valheim/DreamTexts.cs`, `SleepText.cs`
   - `assembly_valheim/Skills.cs`
3. Inspect the actual CCS version referenced by Seasons, including `CustomSyncedValue<T>`, `SequencedCustomSyncedValue<T>`, its initial/full sync, pending queues, and ownership rules. Do not reproduce a network mechanism already supplied reliably by CCS.
4. Preserve the current Seasons architecture and style. Blood Moon belongs in its own `BloodMoon/` folder and must not turn `Seasons.cs`, `SeasonState.cs`, or `EnvManPatches.cs` into monolithic controllers.
5. `Seasons.csproj` uses explicit `<Compile Include=...>` entries. Add every new source file explicitly.
6. Do not bump the plugin version, update release notes, or publish packages in this task.
7. Build the project in Debug and Release after implementation. Report any environment-only build limitation precisely; do not silently skip validation.
8. Make logical commits. Open a pull request from `feat/blood-moon` to `master`, leave it unmerged, then run Codex code review on the complete PR and address clear correctness findings in follow-up commits.

---

## 2. Feature vision

Blood Moon is a rare annual autumn event, not a normal raid. With default season lengths it happens once per 40 game days, roughly once per up to 20 hours of active gameplay.

The final autumn night turns into a short hack-and-slash episode:

- the world begins to redden at 18:00;
- the actual combat begins at 23:00;
- the event ends at 05:45 or earlier when every enrolled participant has resolved their personal outcome;
- players receive Bloodlust and vanilla SoftDeath protection;
- temporary enemies hunt participating players but cannot damage their base, tamed creatures, crops, or unrelated world actors;
- event enemies have no normal loot and are fully cleaned up;
- death is a legitimate way to leave the event;
- the morning is resolved through a controlled fade, time skip, outcome-specific DreamText, and Rested removal.

The event must feel dangerous but its long-term cost is deliberately limited. It targets the player, not the player’s home.

Key narrative line:

> The Moon does not seek your walls, roof, or doors. It seeks you.

---

## 3. Scope of this task

Implement one end-to-end playable Blood Moon cycle containing all items below.

### 3.1 Required in this first slice

- Blood Moon configuration and annual schedule.
- Safe first-install behavior for existing worlds.
- Deterministic event ID and explicit server event state machine.
- Explicit participant phase/outcome model.
- Server-authoritative state and synchronization.
- Persistence/recovery for server restart during an event.
- Forewarning state support, even if the first version uses only minimal messages/logging.
- Marked phase from 18:00 to 23:00.
- Linear red environment overlay from 18:00 to 23:00.
- Custom forced Blood Moon environment from 23:00 until resolution.
- Basic red cloud VFX from vanilla `Ashlands_FaderFX` assets.
- Dynamic Blood Moon status effect presentation.
- Sleep blocking during Marked and Active phases.
- Vanilla SoftDeath status during the combat phase.
- Full suppression of ordinary random events during the Blood Moon window and termination of an event already in progress at 18:00.
- Hidden proximity grouping of players; no map marker or visible group UI.
- Combined world-global and per-player progression context for each hidden group.
- A configurable first enemy pool and one functional role (`Swarm`) sufficient to test at least a baseline and a progression-gated enemy.
- Server-controlled spawn budgets and hard caps.
- Event enemy ZDO metadata.
- Event enemy targeting restrictions and final damage safeguards.
- No normal event-enemy loot.
- Fast event-enemy ragdoll cleanup.
- Server-authoritative Bloodlust combat progress.
- Goal reached at 100% without removing the Bloodlust combat buff.
- Death and disconnect outcomes.
- Linear automatic display completion near morning without converting inactivity into combat success.
- Early resolution when all enrolled participants have resolved.
- Forced resolution at 05:45.
- Two-phase morning resolution with client fade acknowledgement and timeout.
- Outcome-specific DreamText for at least success, death, hiding at a protected base, and hiding in the wild.
- Rested removal after resolution.
- Complete cleanup on disable, shutdown, recovery failure, time skip, or world unload.
- Admin/debug commands and structured diagnostics sufficient to test the feature without waiting one year.

### 3.2 Explicitly deferred

Do not implement these in this PR:

- Blood Craft/free temporary crafting.
- Skill reward payout and x3 skill gain budget.
- Lifesteal tuning and attack-speed modification.
- `Momentum`/`StallTime` pressure escalation beyond minimal counters/logging.
- Bruiser, Ranged, Howler, Doppelganger, or Blood Shade roles.
- Door opening/jamming anti-hide behavior.
- Personal visibility filtering for event and normal enemies.
- Odin watcher changes.
- Dvergr presentation changes.
- Blood Moon music and SFX beyond placeholders/hooks.
- Optional Blood Moon PvP.
- Rituals or returning to the event after death.
- Persistent chronicle in `knownTexts` and final statistical titles.
- Material rewards.

However, the first implementation must preserve the future constraints in section 18 so these features can be added without replacing the event core.

---

## 4. Time model and deterministic event identity

Do not drive authoritative transitions with `EnvMan.m_smoothDayFraction`. It is a client-side smoothed presentation value and is unsuitable as the source of truth.

Use authoritative absolute world time from `ZNet.instance.GetTimeSeconds()` on the server. Freeze the effective schedule when the current annual event is created.

### 4.1 Default schedule

- Visual/Marked start: `18:00` (`18 / 24 = 0.75`).
- Combat/Active start: `23:00` (`23 / 24`).
- Forced end: `05:45` next day (`5.75 / 24`).
- Auto-complete start: configurable duration before 05:45; default `1.5` in-game hours, therefore 04:15.

For event world day `D` and frozen day length `L`:

```text
dayStart          = D * L
visualStart       = dayStart + L * 18.0 / 24.0
combatStart       = dayStart + L * 23.0 / 24.0
forcedEnd         = dayStart + L * (24.0 + 5.75) / 24.0
autoCompleteStart = forcedEnd - L * autoCompleteDurationHours / 24.0
```

Use `double` for schedule seconds.

### 4.2 Event ID

The event ID is the world day on which the 18:00 Marked phase begins:

```text
eventId = eventWorldDay
```

Include `eventId` in synchronized snapshots, participant records, runtime persistence, event enemy ZDOs, death reports, resolution messages, and future Blood Craft items/projectiles. Reject or ignore data carrying an event ID other than the currently active event.

### 4.3 Time jumps

The state machine must tolerate time moving across multiple boundaries in one update: `skiptime`, sleep/time mods, debug commands, server restart after a boundary, and configuration reload. Advance through required transitions in order, or enter safe resolution if the active window was skipped entirely. Never replay an already resolved annual event.

---

## 5. Explicit state model

Do not implement the event as independent booleans.

### 5.1 Global event phase

```csharp
internal enum BloodMoonEventPhase
{
    Dormant,
    Forewarning,
    Marked,
    Active,
    AutoCompleting,
    Resolving,
    Resolved,
    Skipped,
}
```

`Enabled` is a feature gate, not another phase.

### 5.2 Participant phase

```csharp
internal enum BloodMoonParticipantPhase
{
    None,
    Marked,
    Fighting,
    GoalReached,
    Eliminated,
    Resolved,
}
```

### 5.3 Participant outcome

Keep outcome separate from phase:

```csharp
internal enum BloodMoonParticipantOutcome
{
    None,
    Success,
    Death,
    Disconnected,
    HiddenAtBase,
    HiddenInWild,
    LateWitness,
    Skipped,
}
```

A participant record must retain at least stable player/profile ID, current peer UID while connected, display name for logs only, phase/outcome, enrollment time, late-join flag, raw combat Bloodlust points, displayed percent, whether auto-complete contributed, minimal kill/assist counters, last group ID/position, protected-base state at resolution, death/disconnect flags, and a revision if needed for reconciliation.

### 5.4 Resolution steps

```csharp
internal enum BloodMoonResolutionStep
{
    None,
    FreezingEnrollment,
    StoppingSpawns,
    AwaitingClientFade,
    CleaningEnemies,
    FinalizingParticipants,
    AdvancingTime,
    PublishingOutcomes,
    ReleasingClients,
    Complete,
}
```

### 5.5 Resolution reason

```csharp
internal enum BloodMoonResolutionReason
{
    AllParticipantsResolved,
    ForcedMorning,
    DisabledByConfig,
    TimeSkippedPastEvent,
    InvalidRecoveredState,
    WorldShutdown,
    DebugCommand,
}
```

### 5.6 Combat groups are derived data

Do not create a group state machine. A group is recalculated derived data with stable identity where possible. Keep participant IDs, representative center, `Momentum`, `StallSeconds`, alive count, spawn budget, and a resolved progression context. Split/merge are recomputation results, not long-lived phases.

---

## 6. Server authority and networking decision

Server authority is non-negotiable. Clients render local effects and report facts that the dedicated server cannot observe reliably because of ZDO ownership; clients do not decide progress, outcomes, phase transitions, spawn budgets, or rewards.

### 6.1 Evaluate CCS before writing transport

Before implementing the network layer, inspect the exact referenced CCS implementation and document the chosen split near the transport entry point.

Preferred design to validate:

1. **CCS `CustomSyncedValue<string>` for the latest authoritative Blood Moon snapshot.** Server publishes compact versioned JSON; equal states are suppressed; CCS initial/full sync gives late joiners current state. Give it a priority lower than `currentSeasonDay`, publish only on changes, and throttle progress-only snapshots.
2. **CCS `SequencedCustomSyncedValue<T>` only for a low-volume server-to-all pulse that genuinely benefits from ordered duplicate-preserving delivery.** Do not use it for hit/death spam and respect the bounded queue.
3. **Namespaced custom routed RPCs for client-to-server reports, targeted prepare/ACK messages, and messages that should not broadcast to every client.**

If CCS cannot express a required guarantee, use a custom RPC for that path, but do not duplicate CCS initial state synchronization unnecessarily.

### 6.2 Snapshot

Use a schema-versioned DTO containing schema version, event ID, global revision, phase, resolution step/reason, frozen schedule, enrollment-open flag, participant state sufficient for local reconstruction, forced-environment state, and cleanup state. Client handlers are idempotent and older revisions cannot roll state backward.

### 6.3 RPCs

Use a versioned namespace such as:

```text
shudnal.Seasons.BloodMoon.ReportEnemyDeath
shudnal.Seasons.BloodMoon.PrepareResolution
shudnal.Seasons.BloodMoon.ResolutionAck
shudnal.Seasons.BloodMoon.AdminCommand
```

An owner-side enemy-death report contains protocol version, event ID, enemy `ZDOID`, group ID from ZDO, position, and optionally last attacker only as a hint. The server validates event/phase/ID, marker, deduplication, plausible location/group, sender/ownership, and never accepts reward values from the client. Deduplicate by `eventId + ZDOID` for the event lifetime.

### 6.4 Progress update rate

Do not publish per hit/frame. Aggregate changes and publish progress at a bounded rate, initially no more than four times per second and only when changed. Clients may interpolate HUD display.

---

## 7. Persistence and recovery

### 7.1 World-bound markers

Use uniquely prefixed persistent world markers for schema initialized, last started event ID, last resolved event ID, and last skipped event ID. Inspect current `ZoneSystem` global-key APIs first. If IDs are key suffixes, remove the previous marker of the same type to avoid accumulating one key per year.

Suggested prefixes:

```text
seasons_internal_bloodmoon_initialized
seasons_internal_bloodmoon_last_started_<eventId>
seasons_internal_bloodmoon_last_resolved_<eventId>
seasons_internal_bloodmoon_last_skipped_<eventId>
```

Never treat these as progression keys.

### 7.2 Transient runtime snapshot

While Marked/Active/AutoCompleting/Resolving, atomically persist server-only JSON keyed by world UID under the Seasons runtime/config directory. Persist schema/world/event/schedule, phase/resolution, participants/progress/outcomes, previous force environment, and enough data to resume safely without Unity references. Use temp-file plus atomic replace/move, throttle progress-only writes, and delete after successful resolution.

### 7.3 First installation or first enable

Protect existing worlds:

- before the configured forewarning range, allow the upcoming event;
- inside forewarning/Marked/Active/resolution, mark the current annual event `Skipped` and wait until next year;
- after the annual window, initialize so the passed event is not replayed;
- provide an admin command to reset/force it for tests.

### 7.4 Restart during an active event

With a valid runtime snapshot, restore event/participants, compare current time to frozen schedule, resume or resolve, rebuild groups, reconcile/purge stale enemies by event ID, and sync clients after ready. With invalid/missing/mismatched state, purge enemies/presentation, restore/clear forced environment safely, mark current event resolved/skipped without reward, log the reason, and never restart that annual event from zero.

---

## 8. Annual scheduling and enrollment

Use a configured autumn range default `6..9`: start day controls forewarning, final day controls 18:00/final night, and values beyond actual autumn length fall back to the last valid day. Freeze effective days for a created event. The event happens once per complete Seasons year.

At Marked start, enroll every connected eligible player by stable profile/player ID.

Late join rules:

- before enrollment freezes: enroll if no completed record exists for this event;
- during Active: `Fighting`, zero combat points;
- during AutoCompleting: current automatic display floor, zero contribution;
- during Resolving: `LateWitness`, does not block completion;
- after resolution: no re-entry.

Disconnecting ends participation for this annual event; reconnecting does not grant a second attempt. If nobody is online, keep the event available until 05:45. Do not early-resolve an event that never enrolled anyone; resolve it unwitnessed at the end without client UI/reward.

---

## 9. Event lifecycle

### 9.1 Forewarning

Implement phase/hooks. Minimal localized warnings/logs are enough in this slice.

### 9.2 Marked — 18:00 to 23:00

At Marked transition:

- freeze schedule/event ID;
- enroll players;
- show/update `Marked by the Moon` status;
- block sleep;
- begin linear red environment and cloud ramp;
- suppress RandEventSystem;
- stop current random and forced event presentation;
- preserve environment state needed for restoration;
- publish snapshot.

The first tooltip must explain approaching combat, blocked sleep, and 23:00 start. Do not promise Blood Craft in this first implementation.

### 9.3 Active — starts 23:00

- force custom Blood Moon environment;
- keep clouds full;
- change status to Bloodlust;
- add vanilla `SoftDeath` if absent;
- start spawns/progress;
- keep sleep blocked and random events suppressed.

Track whether Blood Moon added SoftDeath. On normal resolution remove it only when Blood Moon added it and the player did not die. After death, relinquish ownership so normal post-death SoftDeath is not removed accidentally.

### 9.4 Goal reached

Only real combat reaches combat 100%. At 100% set `GoalReached`/`Success`, keep the full buff, keep the player in battle, and lower enemy priority only while unfinished players are available. If they are the only valid target, fight them normally.

### 9.5 Death/disconnect

Death is a valid exit: mark `Eliminated/Death`, remove from blocking/active groups, retain progress/statistics, do not re-enroll, preserve vanilla death/tombstone flow, and use death DreamText. Disconnect similarly produces `Disconnected` and no second attempt.

### 9.6 Automatic completion

From configured start to 05:45, linearly raise only the displayed floor. Keep `CombatBloodlustPoints`, `DisplayedBloodlustPercent`, and `CombatContribution` separate. Auto-complete grants no combat points, success, contribution, kills, or future skill reward. At resolution, unresolved survivors become `HiddenAtBase` or `HiddenInWild`.

### 9.7 Early/forced completion

Resolve early only after at least one enrollment and every enrolled participant is GoalReached, Eliminated, disconnected, or resolved. Do not add an indefinite “Moon is sated” phase. At 05:45 resolve regardless.

---

## 10. RandEventSystem ownership

Blood Moon is not a `RandomEvent`.

From Marked start until resolution completes:

- stop current ordinary random event;
- stop/clear current forced event presentation as needed so its environment/UI cannot compete;
- prevent `UpdateRandomEvent`, standalone starts, `StartRandomEvent`, and forced-event selection from starting/restarting presentation;
- freeze rather than advance timers where practical;
- restore normal behavior after cleanup.

Do not destroy bosses or alter quest state. A surviving boss/EventZone may be rediscovered after suppression. Blood Moon forced environment wins during Active.

---

## 11. Environment and VFX

### 11.1 Custom environment

After environments are available:

1. find vanilla `Fader`;
2. clone it, never mutate vanilla;
3. name it `Seasons_BloodMoon` or another unique internal name;
4. set `.r = 1.0f` for every `Color` field, preserving other channels/alpha;
5. set `m_windMin = 1f`, `m_windMax = 2f`, `m_sunAngle = 70f`;
6. register it for `SetForceEnvironment` but not normal weather pools;
7. fail visually with one warning, not feature crash, if missing.

The user has already verified wind/sun settings in the current game.

### 11.2 Linear overlay 18:00–23:00

Use:

```text
factor = clamp01((now - visualStart) / (combatStart - visualStart))
```

Integrate with the existing transient `EnvMan.SetEnv` patch in this order:

```text
original env → seasonal luminance → Blood Moon interpolation → vanilla SetEnv → restore original fields
```

Blood Moon visuals are independent of `controlLightings` and `UseTextureControllers`. Preserve Harmony ordering around `shudnal.GammaOfNightLights`. Save/restore every modified field. Avoid reflection in the hot path.

### 11.3 Forced environment at 23:00

Remember previous force env, call `SetForceEnvironment(Seasons_BloodMoon)` on graphical clients, maintain ownership during Active, and restore previous value only if Blood Moon still owns the force slot. Invalid recovery clears only Blood Moon’s force. At factor 1 the Blood Moon target dominates seasonal luminance so weather such as rain/snow no longer affects combat readability.

### 11.4 Red cloud overlay

Following `SummerHeatVisuals` lifecycle:

1. clone `Ashlands_FaderFX` or Fader’s environment object;
2. never mutate vanilla;
3. retain only content under `cloud` and `cloud (1)` while preserving required parents;
4. disable/remove unrelated particles/renderers;
5. set only `ParticleSystem.main.startColor.r = 1.0f` for retained systems; the prefab has been inspected and no complex color modules need handling;
6. cache original emission;
7. ramp emission with Marked factor and keep full through Active/AutoCompleting/Resolving until fade covers cleanup;
8. stop/clear/destroy on end/unload;
9. no-op headless.

Use a separately controlled overlay object so it can exist before forced env and duplicate activation is avoided.

### 11.5 Music

Add clean phase hooks/placeholders only. No tracks or final priority logic in this PR; future music may vary by phase.

---

## 12. Status effect, SoftDeath, and sleep

Implement dedicated `SE_BloodMoon` through current Seasons registration. It must present Marked/Bloodlust text from synchronized state, expose displayed percent, update/remove idempotently, and remain client-presentation only.

Block sleep while local participant is Marked/Fighting/GoalReached.

Implement configurable linear modifiers needed for playtest:

- outgoing damage bonus;
- incoming damage reduction;
- movement speed bonus.

Accepted direction/default starting point:

```text
0%:   incoming damage -50%, outgoing damage +15%
100%: incoming damage -25%, outgoing damage +40%
movement: 0% to +15%
```

Do not implement lifesteal or attack-speed changes yet.

---

## 13. Hidden combat groups

No raid circles, pins, icons, or group UI.

Recalculate on configurable interval (initially 5 s) and immediately after join/death/disconnect/teleport. Use proximity connected components with hysteresis defaults merge 120 m, split 160 m. Preserve IDs/counters by maximum participant overlap.

A group owns shared budget/cap, but each spawn attempt chooses a real participant anchor, not a potentially empty geometric center.

Initial formula:

```text
groupCap = baseAlive + perPlayerAlive * activeParticipantCount
```

Enforce server-wide hard cap. GoalReached players remain members, but unfinished participants are preferred anchors/targets. Keep/log `Momentum` and `StallSeconds` fields without advanced escalation.

---

## 14. Combined global and player-key progression

Do not use only global keys or current biome.

For each group calculate current world global keys, per-player keys, union, and ownership by player. Use biome only as optional flavor, not minimum tier.

Use canonical vanilla server-synchronized player-data path. Do not assume dedicated server can read every remote `Player.m_uniques`. Inspect `RandEventSystem.RefreshPlayerEventData`, `ZNetPeer.m_serverSyncedPlayerData`, and current player-key serialization. If required keys are not exposed, add a low-frequency versioned client report tied to authenticated sender; never accept another player’s keys.

Enemy definitions can require all/any global keys, all/any player keys, at least one group member’s key, and excluded keys. Global keys define baseline. Group player keys can unlock harder enemies while the advanced player remains; after split, subsequent pool becomes easier.

Exercise the resolver with at least one baseline and one actual vanilla key-gated entry. Do not guess obsolete strings; derive from current definitions.

Store enough spawn metadata to later prioritize an enemy toward players whose personal keys made it eligible, without redesigning spawn records.

---

## 15. First playable enemy system

### 15.1 Definitions

Create schema-versioned human-editable JSON under Seasons config with embedded/default fallback. Entry fields: prefab, role (`Swarm` now), weight, required global/player keys, exclusions, optional biome weights, per-group limit, Bloodlust death points, and only necessary difficulty fields. Skip invalid entries with diagnostics; one bad prefab cannot disable event.

### 15.2 Spawn

Server chooses definition/group/anchor/position/budget. Spawn despite PlayerBase/NoMonsters/wards/islands/trenches, never directly in camera or invalid solid geometry, use configurable distance, prefer viable ground/path, enforce caps, and leave star level unchanged.

### 15.3 ZDO metadata

Store marker/version, event ID, group ID, role, relevant progression source/key-owner data, and original prefab ID. Verify marker plus current event ID everywhere.

### 15.4 Target and damage restrictions

Event enemies target only current-event participants in Fighting/GoalReached. They do not target buildings, tamed creatures, crops, neutral NPCs/Dvergr, ordinary monsters, dead/disconnected/eliminated/late-witness/nonparticipant players. Patch target selection and add final direct/projectile/AoE damage filter. Monster-on-monster default off.

GoalReached players have lower score only while unfinished valid targets exist.

### 15.5 Loot/ragdolls

Suppress normal drops/trophies/resources. Remove ragdolls after configurable ~2 s and purge all living/ragdoll event objects on resolution/recovery.

### 15.6 Ownership/death reports

Assume ownership migrates. Hook owner-side death, report once, server validates/dedupes. First slice may credit configured points to every nearby unfinished group participant rather than only last hit, avoiding last-hit competition and client reward authority.

---

## 16. Bloodlust progress and modifiers

Maintain separate `CombatBloodlustPoints`, `DisplayedBloodlustPercent`, and `CombatContribution`. Server converts points using configurable target. Deaths are required source now; model allows later damage/assist/support.

At 100 combat percent keep full status/buffs, set GoalReached, reduce relative aggro, count optional stats, cap display at 100, and allow help until global resolution.

Centralize/pure-test interpolation. Lifesteal/attack speed remain off.

---

## 17. Morning resolution

Use two phases.

### 17.1 Prepare

Server closes enrollment, stops spawns, freezes outcomes, sends targeted prepare, waits for idempotent ACKs with timeout. Clients validate event/revision, block relevant input, fade black using existing Seasons/Hud patterns, and ACK once.

### 17.2 Resolve under fade

Server purges enemies/ragdolls, finalizes outcomes, restores/clears force env through synchronized state, advances to next valid morning using Seasons `SkipToMorning`, publishes final state, persists resolved marker, deletes runtime snapshot.

Clients remove Blood Moon status/owned SoftDeath, remove Rested, show outcome DreamText, optionally play lying/rest pose only when safely grounded and not swimming/attached/on ship/airborne/teleporting/incompatible interior, never blindly move Y, then release input/fade once.

Minimum localized texts:

**Success** — “You come to on cold ground. The red Moon, ringing steel, and a joy you are now ashamed of are all that remain in memory. Your body aches, but the blood no longer sings.”

**Death** — “You remember the blow. Then earth, darkness, and a distant howl. When dawn touches your face, you are no longer certain any of it was a dream.”

**Hidden at base** — “You wake broken. In the nightmare the walls were thinner than paper, and something beyond the door called you by name. Outside, it is quiet. Too quiet.”

**Hidden in wild** — “You come to far from where the night began. Your legs carried you through the dark until the Moon was sated. You do not know what you saw. You do not want to know.”

Wording may be polished but outcomes stay distinct.

---

## 18. Future constraints the core must preserve

### 18.1 Skill rewards (not implemented now)

Allowed skill list is server-configurable via aliases for `Skills.SkillType`.

Completion mode:

- total direct budget +25 levels;
- first five combat skills sorted by event experience/contribution;
- proportional distribution;
- final actual cap +10 per skill;
- unused cap overflow is not redistributed unless later changed;
- example +15,+7,+3 → +10,+7,+3.

Multiplier mode:

- x3 gain for allowed combat skills;
- bonus portion total cap +10 levels;
- after consumed, gain returns to x1.

Maximum combined bonus = +20. Future tracking must support weapons, Blocking, ElementalMagic, BloodMagic, configured support skills, and exclude Run/Jump/Sneak by default.

### 18.2 Blood Craft (not implemented now)

Known recipes only; available throughout Marked/Active status. Temporary items carry schema/event/owner in `m_customData`, exist only in owner player inventory, are destroyed in `ItemDrop.Awake`, cannot be supplied to world `Interactable.UseItem` sinks, remain normally usable/equippable/consumable, cannot merge with permanent stacks because current stack merge ignores customData, are removed from inventory/tombstone on end/recovery, may leave consumed food/mead effects active, remaining ammo/consumables are removed, free upgrade only for temporary items, no boss damage, and projectiles/AoE carry event marker.

### 18.3 Advanced systems

Future `Momentum` raises meat/swarm tempo; separate `StallTime` drives anti-hide. Personal visibility keeps global state, target eligibility, and local visibility separate. Optional PvP is experimental, off by default, trusted servers only, nonlethal, grief-protected.

---

## 19. Configuration for this slice

Follow current server-config/CCS conventions in dedicated Blood Moon sections.

Schedule: enabled true; days 6,9; hours 18,23,5.75; auto-complete 1.5 h. Validate order/fallback. Schedule changes mid-event apply next year; disabling enters controlled resolution.

Groups/spawns: update interval; merge/split; spawn min/max; interval; base/per-player/world caps; ragdoll lifetime; Bloodlust target points; progress radius/credit; position attempt limits.

Combat: outgoing 0/100; incoming reduction 0/100; movement 0/100. Do not expose speculative role settings.

Diagnostics: separate Blood Moon logging and optional verbose group/spawn/progression logging.

---

## 20. Admin/debug commands

Implement admin-checked current Terminal patterns:

```text
bloodmoon status
bloodmoon start marked
bloodmoon start active
bloodmoon progress <0..100> [player]
bloodmoon spawn <prefab-or-definition> [count]
bloodmoon resolve
bloodmoon cleanup
bloodmoon skip_current
bloodmoon reset_current_year
bloodmoon dump_groups
bloodmoon dump_progression
bloodmoon dump_enemies
```

Remote commands authenticate current admin API. Commands are idempotent. `status` reports ID/phase/schedule/participants/groups/enemies/snapshot revision/persistence/force-env ownership. Debug starts outside autumn must not corrupt annual markers unless explicit. Cleanup awards nothing.

Structured logs:

```text
[BloodMoon][event:<id>][phase:<phase>]
[BloodMoon][event:<id>][player:<id>]
[BloodMoon][event:<id>][group:<id>]
[BloodMoon][event:<id>][spawn]
[BloodMoon][event:<id>][resolution]
```

No per-frame/unchanged-state spam.

---

## 21. Suggested code structure

```text
BloodMoon/
  BloodMoonConfig.cs
  BloodMoonModels.cs
  BloodMoonSchedule.cs
  BloodMoonController.cs
  BloodMoonParticipantController.cs
  BloodMoonNetworking.cs
  BloodMoonPersistence.cs
  BloodMoonGroups.cs
  BloodMoonProgression.cs
  BloodMoonEnemyDefinitions.cs
  BloodMoonSpawner.cs
  BloodMoonEnemy.cs
  BloodMoonCombatPatches.cs
  BloodMoonRandEventPatches.cs
  BloodMoonEnvironment.cs
  BloodMoonVisuals.cs
  SE_BloodMoon.cs
  BloodMoonResolution.cs
  BloodMoonCommands.cs
```

Adjust names only for a clearly better fit. Avoid global mutable state scattered across unrelated files. Centralize hashes/RPC IDs. Use pure helpers for schedule/progress/grouping/progression eligibility.

---

## 22. Required edge cases

Test/reason and document in PR:

- disabled before/during each phase;
- first enable before/inside/after annual window;
- short autumn and day-length changes;
- skip across 18:00/23:00/05:45;
- restart in Marked/Active/AutoCompleting/Resolving;
- corrupt/missing snapshot;
- no players online, late joins in each phase;
- 100%, all 100%, death during normal play/resolution, disconnect/reconnect;
- host and dedicated server;
- group split/merge and advanced-player departure;
- ownership migration and duplicate/stale death reports;
- attempted target/damage to every forbidden category;
- active random/forced event and surviving boss;
- missing visual assets;
- another mod changing force env;
- player in dungeon/interior/ship/attached/swimming/airborne/teleport/dead during resolution;
- shutdown/unload each phase;
- stale old-event enemy;
- CCS sync before/after local Player creation;
- out-of-order/duplicate revisions/RPCs.

---

## 23. Acceptance criteria

### Architecture/build

- Debug and Release build, or exact external missing-reference error documented.
- All files explicitly included in csproj.
- State transitions explicit/idempotent/logged.
- Server owns phase/progress/success/spawns.
- CCS/RPC split documented and current API used correctly.

### Two-client dedicated-server cycle

1. Start Marked by command.
2. Environment reddens smoothly.
3. existing RandEvent stops and cannot restart.
4. Active forces environment, red clouds full, sleep blocked, SoftDeath visible.
5. hidden groups form without map UI.
6. enemies spawn despite base suppression.
7. enemies pursue/damage only eligible players.
8. no damage to structures/tamed/crops/NPCs/ordinary monsters/nonparticipants.
9. no loot; fast ragdolls.
10. death reports dedupe and server progress increases.
11. 100% player retains full Bloodlust and helps other.
12. death exits without skill loss and gets death outcome.
13. split/merge updates cap/progression.
14. early all-resolved executes explicit morning flow.
15. fade/cleanup/time/DreamText/env restore/Rested removal/input release happen once.
16. restart resumes valid state or safely resolves invalid state without replay/orphans.

### Safety

- no stale force env, enemies, status, input lock, runtime snapshot after cleanup/unload;
- stale event packets do nothing;
- first enable in current window skips by default;
- RandEventSystem resumes afterward.

---

## 24. Deliverables and PR workflow

1. Implementation on `feat/blood-moon`.
2. Keep this task updated if actual current game code requires an architectural correction; record reason rather than silently diverging.
3. PR technical note: CCS/RPC decision; state/persistence; patched methods; test commands; acceptance results; limitations/deferred work.
4. Logical commits.
5. PR to `master`, unmerged.
6. Run Codex code review on complete PR, evaluate every finding, fix correctness/regression issues, dismiss only findings conflicting with accepted design/current game code.

Do not begin Blood Craft, skill rewards, advanced roles, personal visibility, music, or optional PvP in this PR. Finish a clean, recoverable, server-authoritative first playable Blood Moon cycle first.
