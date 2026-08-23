# Blood Moon — authoritative foundation and first playable vertical slice

## Task status

- **Repository:** `shudnal/Seasons`
- **Working branch:** `feat/blood-moon`
- **Base branch:** `master`
- **Branch base at task creation:** `eff54feeb573813cb8f41844f563823d609cb71b`
- **Current plugin version at task creation:** `1.8.2`
- **Implementation status:** not started
- **Primary source of truth for this work:** this document
- **Game-code source of truth:** `https://github.com/shudnal/assemblies_combined`, branch `master`
- **CCS source of truth:** `https://github.com/shudnal/ConditionalConfigSync`, branch `master`

The previous Blood Moon documents generated outside the repository were exploratory and are obsolete. Do not use them as implementation requirements. Preserve existing Seasons behavior and architecture unless this task explicitly requires a change.

---

## 1. Goal of this task

Implement the first complete, playable Blood Moon slice in Seasons. This task intentionally combines the former foundation, visual/status-flow, and one-enemy prototype stages so the result can be exercised in the real game end to end.

The completed slice must include:

1. annual scheduling near the end of autumn;
2. explicit server-authoritative event and participant state machines;
3. persistence and recovery sufficient to prevent duplicate or surprise events;
4. a Marked preparation phase beginning at 18:00;
5. a combat phase beginning at 23:00;
6. forced resolution at 05:45, or earlier when every participant has resolved their personal outcome;
7. a synchronized Blood Moon status effect and vanilla SoftDeath protection;
8. sleep blocking;
9. complete suppression of ordinary `RandEventSystem` random/standalone events during the Blood Moon window, with the current ordinary random event stopped at 18:00;
10. gradual environmental reddening from 18:00 to 23:00, followed by a forced Blood Moon environment;
11. one configurable event-enemy prefab spawned through a dedicated Blood Moon spawner;
12. event-enemy isolation from buildings, tameables, crops, loot economy, and ordinary world combat;
13. authoritative progress accounting and exactly-once enemy-death credit;
14. early or forced cleanup, fade, DreamText, morning time advance, and Rested reset;
15. administrative debug commands and structured logs sufficient for iterative game testing.

The result does **not** need final balance, final VFX/SFX polish, final music, multiple enemy roles, Blood Craft, skill rewards, personal visibility, or production README text. It must, however, leave deliberate extension points for those later phases.

---

## 2. Product vision and non-negotiable behavior

Blood Moon is a rare annual autumn event, not a conventional raid. At default season lengths it occurs approximately once every 40 game days. Its intended feeling is a short “micro-diabloid” episode: a large number of temporary enemies, a fast player power curve, low permanent cost, and a deliberately unsettling morning resolution.

### 2.1 The player, not the base, is the target

Event enemies may ignore `PlayerBase`, `NoMonsters`, and similar spawn suppression. This is intentional. Players often build on islands, behind earth walls, or inside completely spawn-suppressed bases; Blood Moon must still reach them.

At the same time, event enemies must not:

- select buildings as targets;
- damage buildings;
- select tameables as targets;
- damage tameables;
- select or damage crops and other protected world objects;
- attack ordinary monsters or NPCs in this first slice;
- produce ordinary loot;
- leave persistent enemies or long-lived ragdolls after the event.

The atmospheric rule is:

> The moon does not seek the house. It seeks the player.

### 2.2 Death is a valid exit

At active combat start, every participating player receives the vanilla SoftDeath status effect. The mechanic must make it clear that dying during the event does not cause the normal skill-loss penalty.

A player may deliberately fight, succeed, hide, disconnect, or die. Death before the personal goal is reached resolves that player with a death outcome and removes them from active participation for the remainder of that Blood Moon. They must receive the death-specific DreamText at resolution.

A player who already reached the personal goal may continue fighting at full Bloodlust strength to help others. If that player dies afterwards, preserve the success outcome while also recording the post-goal death in statistics.

### 2.3 Blood Moon is not a `RandEventSystem` event

Do not create a `RandomEvent` for Blood Moon. Implement an independent controller, spawner, state model, and synchronization layer.

From Marked start at 18:00 until resolution is complete:

- stop the currently active ordinary random event at Marked entry;
- prevent ordinary random events and standalone random events from starting or advancing;
- prevent ordinary random-event synchronization from reactivating one;
- resume vanilla behavior only after Blood Moon has fully resolved.

Do not draw raid circles, exclamation marks, or any other Blood Moon markers on the map. Future player grouping is deliberately hidden and should be perceived only through enemy pressure.

Boss/event-zone forced-event bookkeeping is not the target of this task. Inspect `RandEventSystem` in `assemblies_combined` and block the normal `m_randomEvent`/standalone path without corrupting boss fights or event-zone state. Blood Moon environment and gameplay must still take visual priority during active combat.

### 2.4 No final music in this task

The owner will later add one or more custom combat tracks, possibly selected by phase/state. Keep a clean integration hook, but do not add placeholder audio files or redesign `CustomMusic.cs` now.

---

## 3. Repository and implementation rules

1. Work only in `feat/blood-moon`.
2. Do not commit to `master`.
3. Do not bump the plugin version.
4. Do not update Thunderstore/GitHub changelogs or public README in this task.
5. Do not add config migrations. Blood Moon has not been released.
6. Preserve .NET Framework 4.8, C# 10, the existing non-SDK project format, and explicit `<Compile Include>` entries in `Seasons.csproj`.
7. Do not add third-party runtime dependencies.
8. Use the current ConditionalConfigSync dependency already present in the repository.
9. Follow `.editorconfig`, existing naming, logging, config, localization, and Harmony conventions.
10. Before patching a Valheim method, read its current implementation from `shudnal/assemblies_combined`. Do not rely on remembered signatures or old decompilation.
11. Prefer narrow patches guarded by Blood Moon markers/state. Do not globally alter unrelated combat, AI, inventory, environment, or time behavior.
12. Keep dedicated-server/headless execution safe. Graphics, HUD, VFX, and local-player code must be guarded.
13. Use explicit invariants and idempotent cleanup. Repeated cleanup or repeated state application must be safe.
14. Keep the first implementation reviewable. Use cohesive commits; do not produce one giant unstructured commit.

Suggested code location:

```text
BloodMoon/
    BloodMoonController.cs
    BloodMoonState.cs
    BloodMoonSchedule.cs
    BloodMoonParticipant.cs
    BloodMoonNetwork.cs
    BloodMoonPersistence.cs
    SE_BloodMoon.cs
    BloodMoonEnvironment.cs
    BloodMoonVisuals.cs
    BloodMoonSpawner.cs
    BloodMoonEnemy.cs
    BloodMoonResolution.cs
    BloodMoonPatches.cs
    BloodMoonCommands.cs
```

This is a suggested decomposition, not a requirement to create one class per line. Avoid god classes, but also avoid empty abstractions.

---

## 4. Required state model

Do not model this feature as a collection of unrelated booleans. Use explicit enums, transition validation, and one authoritative server transition path.

### 4.1 Event phase

Use an event phase equivalent to:

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

`Enabled` is a feature gate, not an event phase.

### 4.2 Resolution reason

Keep the terminal reason separate from phase:

```csharp
internal enum BloodMoonResolutionReason
{
    None,
    AllParticipantsResolved,
    ForcedMorning,
    DisabledByConfig,
    TimeSkippedPastEvent,
    InvalidRecoveredState,
    WorldShutdown,
    NoWitnesses,
}
```

### 4.3 Participant phase

Use a participant phase equivalent to:

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

Keep outcome separate:

```csharp
internal enum BloodMoonParticipantOutcome
{
    None,
    Success,
    Death,
    Disconnected,
    Survived,
    HiddenAtBase,
    HiddenInWild,
    LateWitness,
    Skipped,
}
```

Participant metadata must be able to represent at least:

```text
persistent player ID
current peer/routing ID, if connected
event ID
phase
outcome
joined late
auto-completed display progress
was at a protected player base at resolution
was disconnected
reached goal before death
combat progress
displayed progress
kills credited
damage/contribution counters needed by this slice
post-goal deaths
last known position
last state revision
```

Do not use peer UID as persistent player identity. Use the stable character/player ID (`Player.GetPlayerID()` or the current equivalent verified in `assemblies_combined`) and use peer UID only for network routing.

### 4.4 Morning-resolution steps

Resolution must be explicit and idempotent. Use a sequence equivalent to:

```csharp
internal enum BloodMoonResolutionStep
{
    None,
    FreezingEnrollment,
    StoppingSpawns,
    AwaitingClientFade,
    CleaningEnemies,
    CleaningItems,
    ApplyingRewards,
    AdvancingTime,
    PublishingOutcomes,
    ReleasingClients,
    Complete,
}
```

`CleaningItems` and `ApplyingRewards` are no-ops in this first slice, but retaining explicit steps makes later Blood Craft and skill-reward work predictable. Do not implement Blood Craft or rewards now.

### 4.5 Combat groups

Combat groups are derived data, not another state machine. In this first slice, implement the abstraction but allow each active player to function as a singleton group. Do not implement proximity merge/split yet and do not show groups on the map.

A future group must be able to own:

```text
stable group ID
member persistent player IDs
current spawn anchors
spawn budget and alive count
future Momentum and StallTime
future global/player-key progression context
```

Do not bake per-player-only assumptions into the spawner API.

---

## 5. Annual calendar and exact clock schedule

### 5.1 Default autumn range

Add synchronized server configs for at least:

```text
Enable Blood Moon                         default true
Forewarning start autumn day              default 6
Final Blood Moon autumn day               default 9
Marked start clock hour                   default 18.0
Active combat start clock hour            default 23.0
Forced combat end clock hour              default 5.75
Automatic completion duration, game hours default 2.4
```

The final night is the night beginning on the configured final autumn day and ending the following morning.

If the configured final day exceeds the actual autumn length, use the last valid autumn day. Clamp the forewarning start to a valid day and never allow it to be after the effective final day.

Forewarning spans the configured start day through the day before the final Blood Moon night. In this first slice, Forewarning may be limited to state, logging, and localization hooks; final multi-day atmospheric escalation is deferred.

### 5.2 Clock semantics and Seasons day rescaling

The requested times refer to the visible/rescaled Valheim clock, not blindly to raw `ZNet` day fractions.

Current Seasons patches `EnvMan.RescaleDayFraction`. Therefore, do not calculate 18:00, 23:00, or 05:45 by simply multiplying `0.75`, `23/24`, and `5.75/24` by `m_dayLengthSec`.

At event creation:

1. capture the authoritative world day;
2. capture `m_dayLengthSec`;
3. capture the applicable `seasonState.DayStartFraction()` for the final autumn night;
4. convert visible clock fractions back to raw day fractions by inverting the current Seasons rescale formula;
5. convert those raw fractions to absolute `ZNet` seconds;
6. store the resulting schedule in event state and persistence.

For the existing piecewise rescale logic, the inverse is conceptually:

```text
visible 00:00–06:00:
    raw = visible / 0.25 * dayStart

visible 06:00–18:00:
    raw = dayStart
        + (visible - 0.25) / 0.5
        * (nightStart - dayStart)

visible 18:00–24:00:
    raw = nightStart
        + (visible - 0.75) / 0.25
        * dayStart
```

where `nightStart = 1 - dayStart`.

Use double precision for absolute schedule values. `EnvMan.m_smoothDayFraction` may drive local visual interpolation, but it must not be the server source of truth for state transitions.

Timing-config or season-length changes after the event schedule has been captured apply to the next annual event. Do not move an active event’s boundaries in real time.

### 5.3 Event ID

Use a deterministic event ID. The preferred ID is the world day on which the final night begins at 18:00:

```csharp
long eventId = finalNightWorldDay;
```

Include `eventId` in:

- authoritative event state;
- participant records;
- network messages;
- persisted runtime state;
- event-enemy ZDO markers;
- future Blood Craft markers;
- logs and diagnostics.

Reject stale data from a different event ID.

### 5.4 Transition rules

The server is the only side allowed to transition authoritative phase.

Required normal transitions:

```text
Dormant      -> Forewarning
Forewarning  -> Marked       at 18:00 on the effective final day
Marked       -> Active       at 23:00
Active       -> AutoCompleting at configured auto-complete start
Active/AutoCompleting -> Resolving when all participants resolve or at 05:45
Resolving    -> Resolved
```

A time skip may cross more than one boundary. Implement catch-up processing that applies all required durable side effects exactly once or resolves safely. Never leave the event stuck because no frame observed an exact fraction crossing.

### 5.5 Enrollment behavior

- At 18:00, online local players receive Marked presentation and preparation access.
- The authoritative combat roster is finalized/created at 23:00 from eligible online players.
- A player who disconnects during Marked but returns before 23:00 may still participate.
- A player joining after 23:00 and before enrollment closes is a late joiner and begins with zero combat progress.
- A player joining during AutoCompleting receives the current automatic display floor but zero combat contribution.
- A player joining after Resolving begins receives `LateWitness` presentation only and must not hold resolution open.
- A participant who disconnects after Active begins resolves as `Disconnected` and may not re-enter that same event in this first slice.
- A participant who dies before reaching the goal resolves as `Death` and may not re-enter that same event in this first slice.
- GoalReached participants remain physically active helpers until the whole event resolves.

If nobody is online at 23:00, keep enrollment open until 05:45. The first player who joins before forced end may participate. If no player participates at all, resolve as `NoWitnesses` without forcing client fade/DreamText on unrelated later connections.

---

## 6. Progress, completion, and outcomes

Keep these concepts distinct:

```text
CombatProgress       progress earned from actual Blood Moon combat
DisplayedProgress    max(CombatProgress, automatic completion floor)
CombatContribution   real activity used later for outcome/reward/statistics
```

### 6.1 First-slice progress source

For this first slice, progress is awarded when the configured event enemy dies near active participants. Do not depend on last hit.

For every validated event-enemy death:

- credit every `Fighting` or `GoalReached` participant within the configured radius;
- increment that participant’s combat points/kills-nearby contribution;
- update `CombatProgress` up to 100%;
- transition `Fighting -> GoalReached` when combat progress reaches 100%;
- preserve the full Bloodlust effect and allow continued combat after GoalReached.

Use configurable values rather than hard-wiring a required kill count:

```text
Progress per validated enemy death
Progress credit radius
```

### 6.2 Automatic completion

Automatic completion begins a configurable number of visible game hours before 05:45. The default 2.4 hours preserves the original “last 10% of a day” concept, producing a default start around 03:21.

During this window, calculate a linear automatic floor from 0% to 100% and use:

```csharp
DisplayedProgress = Mathf.Max(CombatProgress, automaticFloor);
```

Automatic completion must not:

- add combat contribution;
- add kills;
- turn a hidden/AFK participant into a combat success;
- grant future skill rewards;
- change CombatProgress.

`AutoCompleted` must be recorded when the display reaches 100% without combat progress reaching 100%.

### 6.3 Early event end

Maintain the original active/completed-list behavior through explicit states:

- `Fighting` participants keep the event open;
- `GoalReached`, `Eliminated`, and disconnected participants do not keep it open;
- GoalReached participants may still help players who remain Fighting;
- once at least one participant has enrolled and no participant remains Fighting, begin resolution immediately;
- if all participants simply die or disconnect, that is still a valid early resolution.

### 6.4 Final outcomes

At resolution, preserve already fixed success/death/disconnect outcomes. For surviving players who did not reach 100% CombatProgress:

- `HiddenAtBase` if contribution is effectively zero and the player is inside a protected PlayerBase/spawn-protected area;
- `HiddenInWild` if contribution is effectively zero and the player is outside such an area;
- `Survived` if the player made meaningful combat contribution but did not reach the personal goal;
- `LateWitness` if the player joined only after resolution enrollment closed.

The exact “effectively zero” threshold should be a small named constant or config suitable for playtesting, not an opaque magic expression.

---

## 7. Server authority and networking decision

Server authority is mandatory. A client may report observable events that occur on an object it owns, but it must never decide phase, progress, participant outcome, spawn budget, or event completion.

### 7.1 Evaluate CCS before coding the transport

Read current implementations in:

```text
shudnal/ConditionalConfigSync/ConditionalConfigSync/CustomSyncedValue.cs
shudnal/ConditionalConfigSync/ConditionalConfigSync/Parts/Transport.cs
shudnal/Seasons/Utils/CustomSyncedValuesSynchronizer.cs
```

Compare:

1. own RPCs only;
2. `CustomSyncedValue<T>` latest-state synchronization;
3. `SequencedCustomSyncedValue<T>` ordered event synchronization;
4. a hybrid.

The preferred starting design is a hybrid:

- a low-frequency CCS `CustomSyncedValue<string>` (or another proven CCS-serializable DTO) for the latest public authoritative event snapshot and automatic late-join initial state;
- targeted own RPCs for participant-specific snapshots/progress, client-to-server reports, and resolution fade acknowledgements;
- `SequencedCustomSyncedValue<T>` only if a real broadcast event stream benefits from CCS queue semantics and cannot be represented safely as revisioned state.

Do not use a sequenced value merely because an event is “one-shot.” A revisioned current state plus targeted RPC may be safer for reconnects and duplicate suppression.

If implementation evidence supports a different design, document the reason in this task file or a short decision note before proceeding. Do not modify CCS itself in this task.

### 7.2 Public state snapshot

The synchronized public snapshot must be versioned and revisioned. It should contain at least:

```text
protocol/schema version
monotonically increasing revision
event ID
event phase
resolution reason
absolute Marked/Active/Auto/ForcedEnd schedule
enrollment-open flag
whether active combat/environment is expected
```

Clients ignore stale revisions or a mismatching event ID.

### 7.3 Participant-specific snapshot

A client must be able to request/recover its own participant state after local Player creation or reconnect. Include at least:

```text
event ID
persistent player ID
participant phase
outcome
CombatProgress
DisplayedProgress
joined-late/auto-completed flags
local resolution state
```

Do not expose hidden group internals in UI. Sending all participant records in a server snapshot is acceptable only if the payload/update frequency remains small and the choice is documented.

### 7.4 Required RPC/event categories

Use stable, plugin-prefixed RPC names and a protocol version. The exact split may change after the CCS evaluation, but support these semantics:

```text
request current participant snapshot
server participant snapshot/progress delta
enemy death/combat report to server
prepare resolution/fade
client fade-ready acknowledgement
resolution outcome/release
admin/debug command routing when required
```

### 7.5 Validation and deduplication

Every client report must validate:

- sender peer is ready and maps to an expected player where applicable;
- protocol version;
- current event ID;
- current server phase;
- participant is eligible;
- referenced ZDO exists or was known recently;
- referenced enemy has this event’s Blood Moon marker;
- report has not already been accepted;
- position/range is plausible;
- payload sizes and counts are bounded.

Event-enemy death may run on the peer that owns that enemy rather than the dedicated server. The owner may report:

```text
event ID
enemy ZDOID
position
optional killer persistent player ID
```

The server performs authoritative nearby-participant credit and stores the enemy ZDOID in an exactly-once deduplication set. Duplicate/replayed reports must not add progress twice.

Throttle participant progress deltas. The HUD may interpolate between authoritative values; do not broadcast a full state every frame.

---

## 8. Persistence, initialization, and recovery

### 8.1 Persistent data

Create a versioned server-side Blood Moon persistence file keyed by world UID under the Seasons config/data directory. Use atomic write semantics (`temp` plus replace/move) and tolerate a missing file.

Persist at least:

```text
schema version
world UID
initialized marker
last started event ID
last resolved event ID
last skipped event ID
active event schedule and phase, if any
participant records and progress, if active
resolution reason/step, if resolving
```

Also place a minimal world-bound initialization marker in the world’s global keys, using a unique Seasons prefix, so moving a world without the sidecar file does not look like a brand-new pre-Blood-Moon world. Do not create one permanent global key per annual event unless there is no cleaner supported option.

### 8.2 First install/update safety

When Blood Moon support is first initialized:

- if the current annual event window has not begun, allow the future event normally;
- if the current world is already inside Forewarning, Marked, Active, AutoCompleting, or pre-resolution morning for that annual event, mark the current event/year `Skipped`;
- do not surprise an existing server with an immediate Blood Moon after installing/updating the mod;
- allow an admin debug command to reset/force the current year for testing.

No config migration is required because the feature is unreleased.

### 8.3 Restart during an event

On a normal server restart with a valid runtime snapshot:

1. restore schedule and event ID;
2. validate current world time against the persisted phase;
3. rebuild connected participant routing without changing persistent outcomes;
4. rebuild singleton combat groups;
5. adopt or purge event entities by event ID as appropriate;
6. continue from the correct current phase;
7. send fresh snapshots after clients reconnect.

If active state is missing, corrupt, or inconsistent:

- purge event enemies for the stale/current event ID;
- clear local visual/forced-environment state;
- resolve/skip the current annual event without reward;
- never start the same annual event a second time.

A world moved to another server with only the world initialization key but no active sidecar state must fail safe: if currently inside that event window, skip the current event rather than reconstructing a potentially duplicate live event.

### 8.4 Configuration changes while active

- timing, day-range, and schedule-related config changes apply to the next annual event once the current schedule is captured;
- safe balance values may apply immediately if explicitly designed that way;
- disabling Blood Moon during Marked/Active/AutoCompleting must enter safe `Resolving`, not simply flip a boolean and abandon state;
- persistence and synchronized state must reflect the transition.

---

## 9. Marked and Bloodlust status effects

### 9.1 Custom status effect

Register a dedicated `SE_BloodMoon` using the established Seasons/ObjectDB status-effect pattern.

The same custom status-effect class may present different name/tooltip text based on participant/event phase:

- **Marked (18:00–23:00):** countdown/preparation, sleep blocked, explanation that combat is coming; future Blood Craft text hook may be present but do not implement Blood Craft;
- **Active/AutoCompleting:** authoritative displayed progress, current Bloodlust modifiers, and goal state;
- **GoalReached:** full buff remains, progress is complete, enemies prefer unfinished players when available.

The status effect is presentation and modifier delivery, not the authoritative event state.

### 9.2 Vanilla SoftDeath

At `Marked -> Active`, add or refresh the vanilla `SEMan.s_statusEffectSoftDeath` for every participant.

Requirements:

- verify actual vanilla death/skill-loss order in `assemblies_combined/assembly_valheim/Player.cs` and `SEMan.cs`;
- ensure SoftDeath remains active long enough for custom day lengths and the full event;
- add a narrow fallback guard preventing skill loss for a valid Blood Moon participant if vanilla status lifetime/removal order is insufficient;
- do not accidentally shorten a pre-existing SoftDeath;
- track whether SoftDeath existed before Blood Moon if cleanup requires removal;
- prefer leaving vanilla lifetime intact over removing someone else’s protection.

### 9.3 Sleep blocking

From Marked entry until the participant resolves:

- block normal bed interaction/sleep initiation with localized feedback;
- also guard the underlying sleep state so other mods or alternate call paths cannot bypass it trivially;
- do not globally prevent unrelated nonparticipants from using beds unless the server-wide time-skip behavior requires it;
- Blood Moon resolution controls its own synchronized morning advance.

### 9.4 First-slice Bloodlust modifiers

Implement configurable interpolation based on **CombatProgress**, not automatic display floor:

```text
Outgoing damage bonus at 0%      default +15%
Outgoing damage bonus at 100%    default +40%
Incoming damage multiplier at 0% default 0.50
Incoming damage multiplier at 100% default 0.75
Movement speed bonus at 0%       default 0%
Movement speed bonus at 100%     default +15%
```

Interpretation:

- the player begins highly protected so entering the fight is safe;
- as Bloodlust rises, outgoing damage and speed increase;
- protection decreases from 50% reduction to 25% reduction, producing a more reckless berserker state;
- the full 100% buff remains after GoalReached.

For this slice, apply outgoing/incoming combat modifiers only when the other side is a valid current Blood Moon enemy. Do not allow the event buff to become a boss/ordinary-world combat exploit. Movement speed may remain a general player modifier while the effect is active.

Attack-speed modification and lifesteal are explicitly deferred until combat tempo is observed in real playtests.

---

## 10. Environment and visual implementation

The owner will tune final visuals in game. Implement the requested functional pipeline without importing external assets.

### 10.1 Blood Moon environment

After environments are available, create a unique clone of the vanilla environment named `Fader`, for example:

```text
Seasons_BloodMoon
```

On the cloned `EnvSetup`:

- for every instance field of type `Color`, set only `r = 1.0f`, preserving `g`, `b`, and `a` from the Fader source;
- set `m_windMin = 1.0f`;
- set `m_windMax = 2.0f`;
- set `m_sunAngle = 70.0f`.

The owner has already verified in game that these wind and sun-angle values work. Do not add a speculative alternative path unless the current game code requires it.

Register/rebuild the clone through the current environment lifecycle so it survives Seasons custom-environment refresh and Expand World Data compatibility refresh. Never add it as a weighted ordinary weather candidate.

### 10.2 Fader cloud effect

Use Fader’s `Ashlands_FaderFX` environment object as the source.

Create a private clone that retains only the `cloud` and `cloud (1)` effects. The owner inspected the source: changing `ParticleSystem.main.startColor` is sufficient; no gradient/ColorOverLifetime rewrite is required for this prefab.

For each retained cloud particle system:

- set the start color’s red channel to `1.0f` while preserving other channels;
- cache original emission values;
- drive prelude intensity smoothly with the Blood Moon visual coefficient;
- ensure no duplicate cloud systems exist when switching from prelude overlay to the forced environment;
- stop and clear particles during cleanup;
- destroy/reset clones on world unload.

It is acceptable to manage one manual follow-player cloud clone throughout Marked/Active instead of letting the forced environment instantiate a second copy, provided the result is leak-free and visually continuous.

### 10.3 18:00–23:00 overlay

From visible 18:00 to 23:00, retain the current weather, including rain/snow/etc., and linearly blend its environment settings toward the Blood Moon target.

Integrate with the existing `EnvMan.SetEnv` transient modification pipeline in `SeasonState/EnvManPatches.cs`:

```text
save original EnvSetup fields
apply normal Seasons luminance, if enabled
apply Blood Moon overlay second
allow vanilla SetEnv to consume the temporary values
restore every modified source field in Postfix
```

Requirements:

- Blood Moon overlay must work even when `controlLightings` or texture controllers are disabled;
- at coefficient 1, environment color fields must equal the Blood Moon/Fader target, preventing a visual pop at 23:00;
- preserve existing Harmony ordering with GammaOfNightLights;
- extend save/restore to every additionally modified field, including wind/sun values where required;
- avoid reflection in a per-frame hot path when explicit assignments are practical;
- use a linear coefficient for now; expose the calculation cleanly so an easing curve can be added later.

### 10.4 Forced environment at 23:00

At Active start, stop relying on the current weather overlay and force `Seasons_BloodMoon` on each client. This intentionally removes rain, snow, and other weather effects that could reduce combat visibility/effectiveness.

Before forcing:

- remember the client’s previous `EnvMan.m_forceEnv` value;
- call the verified current `SetForceEnvironment` path;
- ensure the Blood Moon environment itself is not reprocessed by ordinary seasonal luminance in a way that causes a 23:00 color jump;
- ensure prelude cloud handling does not duplicate the forced environment cloud effect.

During resolution under black fade:

- clear/restore the previous force environment only if the current force is still the Blood Moon environment;
- do not overwrite a force-environment change another system made after Blood Moon took control;
- restore ordinary environment flow cleanly for late join, disconnect, world unload, and invalid recovery.

### 10.5 Headless safety

The dedicated server may own phase/timing but must not instantiate VFX, access camera/HUD, or assume a graphics device. Environment presentation is local-client behavior derived from authoritative synchronized state.

### 10.6 Music hook only

Provide an internal phase-change hook suitable for future Blood Moon music selection. Do not add tracks, file loading, or final music-priority patches in this task.

---

## 11. Independent random-event suppression

Inspect the current `RandEventSystem` implementation before patching.

At Marked entry on the server:

1. terminate the current ordinary `m_randomEvent` through its proper stop/reset path;
2. synchronize the cleared state to clients;
3. begin suppressing ordinary random/standalone event update/start paths.

While Blood Moon is Marked, Active, AutoCompleting, or Resolving:

- no new ordinary random event may start;
- standalone-event timers must not trigger events;
- the current ordinary event must remain null;
- do not let periodic `SendCurrentRandomEvent` restore stale state;
- event timers may either pause or be reset deliberately, but behavior after Blood Moon must be documented and deterministic.

Do not stop boss AI or corrupt `m_forcedEvent`. If a boss/event-zone forced event exists, Blood Moon still controls its own forced environment and combat layer, but ordinary boss mechanics remain intact.

---

## 12. First playable event enemy

### 12.1 Scope

Implement one configurable enemy prefab end to end. Use a safe vanilla default such as `Greydwarf`, but keep the prefab name in synchronized server config for testing.

Required configs should include at least:

```text
Test/event enemy prefab name
Maximum alive per singleton group/player
Server-wide hard maximum alive
Spawn interval
Minimum spawn distance
Maximum spawn distance
Progress per death
Progress credit radius
Ragdoll cleanup delay, default about 2 seconds
```

Use conservative defaults and make balance values easy to change. Do not implement stars/level changes in this slice.

### 12.2 Spawn ownership and markers

The server controls spawn budget and instantiation requests. Each event enemy must receive persistent ZDO markers equivalent to:

```text
Blood Moon marker/version
event ID
source group ID or participant ID
role (single prototype role for now)
future progression-source field
```

Do not identify an event enemy only by prefab name.

Spawn validation must:

- ignore player-base/no-monster suppression intentionally;
- require a loaded/valid zone;
- find valid terrain/ground placement;
- avoid spawning visibly inside solid geometry;
- avoid invalid water/air placement for the configured prefab;
- avoid spawning directly inside the player camera where practical;
- stop when per-group or server-wide caps are reached.

Use a participant as a concrete spawn anchor even though the budget belongs to the group abstraction. Do not spawn around a geometric group center that may later be far from every member.

### 12.3 AI target eligibility

An event enemy may target only current Blood Moon participants who are still physically participating:

```text
Fighting
GoalReached
```

It must not target:

- buildings/destructibles;
- tameables;
- crops;
- ordinary monsters;
- NPCs;
- dead/eliminated/disconnected participants;
- unrelated players.

Implement target filtering at AI-selection level, not only as a final damage cancellation. Otherwise event enemies will waste time attacking invulnerable world objects.

Keep a final damage guard as defense in depth.

A GoalReached player remains targetable. If unfinished Fighting players are available in the same future group, reduce the GoalReached player’s target priority rather than making them completely invisible. If the GoalReached player is the only available participant, event enemies may target them normally.

### 12.4 Damage isolation

For this first slice:

- event enemies damage only valid Blood Moon participants;
- event enemies do not damage ordinary world actors or world objects;
- nonparticipant players cannot meaningfully interfere with event enemies;
- ordinary creatures should not consume/kill event enemies in a way that grants progress;
- Bloodlust damage bonuses apply only in the Blood Moon combat layer.

Use narrow checks based on event ID and ZDO marker.

### 12.5 Loot and ragdoll cleanup

On each spawned instance, remove/disable `CharacterDrop` output without mutating the shared prefab.

Ensure:

- no normal item drops;
- no trophies/resources enter the world economy;
- spawned ragdolls inherit enough marker context to be recognized;
- ragdolls disappear after the configured short delay;
- all surviving enemies/ragdolls for the event ID are cleaned during resolution or invalid recovery.

Read the current `Character`/`Ragdoll` creation path in `assemblies_combined` and patch the narrowest reliable propagation point.

### 12.6 Death report and authoritative credit

Enemy ownership may transfer to a client. Therefore:

1. the owner observes the event enemy death;
2. the owner reports event ID, ZDOID, and death position;
3. the server validates marker/state and deduplicates by ZDOID;
4. the server awards nearby participant progress;
5. the server updates participant snapshots/revision;
6. the enemy is removed from alive accounting exactly once.

Do not trust a client-provided progress amount or participant list.

### 12.7 Spawn lifecycle

- Start spawning only in Active/AutoCompleting.
- Stop new spawns as soon as Resolving starts.
- If a singleton group participant resolves by death/disconnect, remove or reassign its remaining enemies safely.
- If the participant reaches the goal but others still need help in a future merged group, allow the helper to continue; singleton behavior may simply keep that player’s enemies until global resolution for now.
- Cleanup must be idempotent and keyed by event ID.

---

## 13. Morning resolution protocol

Resolution must be synchronized in two phases so time does not jump before clients can fade.

### 13.1 Prepare/fade phase

Server:

1. closes enrollment;
2. stops spawns;
3. freezes participant outcomes/statistics;
4. publishes Resolving state and target morning time;
5. sends prepare-resolution to relevant clients.

Clients:

1. disable/guard player input as appropriate;
2. begin black fade using existing Seasons/Hud patterns;
3. send a ready acknowledgement when black or after a safe local timeout.

The server waits for connected participant acknowledgements with a short global timeout. One broken client must not stall the world indefinitely.

### 13.2 Resolve phase

Under fade, the server/controller sequence must:

1. clean event enemies/ragdolls;
2. run the future item-cleanup step as a no-op for now;
3. run the future reward step as a no-op for now;
4. advance time to the next valid morning using current `EnvMan`/Seasons day-length behavior;
5. clear the Blood Moon forced environment and custom status effect;
6. reset Rested;
7. publish the participant outcome and DreamText selection;
8. release clients and fade back in;
9. persist Resolved exactly once.

The combat deadline is 05:45; the morning target may be the proper next `EnvMan.GetMorningStartSec` rather than exactly 06:00, so it remains compatible with Seasons day-length logic.

### 13.3 Player pose

Do not blindly raycast and change player Y.

- Do not move a player on a ship, in water, in air, in a dungeon/interior edge case, teleporting, or attached.
- If a player is safely grounded, a lying/resting emote may be applied for the wake-up presentation.
- Otherwise use fade plus DreamText without repositioning.
- The emote should end on player input, but input recovery must never become stuck if the emote fails.

### 13.4 DreamText outcomes

Add localization keys and functional placeholder text for at least:

- Success;
- Death;
- Survived but incomplete;
- Hidden at base;
- Hidden in the wild;
- Late witness, if shown.

Use the existing Valheim DreamTexts/SleepText presentation path where practical. Queue or defer presentation if the local player is loading, respawning, or not yet valid.

Tone should remain ominous and ambiguous. Existing accepted examples may be adapted:

**Success**

> You come to on the cold ground. Only the red moon, the ring of weapons, and a joy you are now ashamed of remain in your memory. Your body aches, but the blood no longer sings.

**Death**

> You remember the blow. Then earth, darkness, and a distant howl. When dawn touches your face, you are no longer certain any of it was a dream.

**Hidden at base**

> You wake exhausted. In the nightmare the walls were thinner than paper, and something beyond the door called you by name. Outside, it is quiet. Too quiet.

**Hidden in the wild**

> You come to far from where the night began. Your legs carried you through the dark until the moon was satisfied. You do not know what you saw. You do not want to know.

Do not mass-translate every language in this task. Add correct localization tokens and at least English source text; leave final localization polishing for a later pass.

---

## 14. Administrative commands and diagnostics

Implement server/admin-only commands using the current Terminal conventions:

```text
bloodmoon status
bloodmoon forewarning
bloodmoon marked
bloodmoon active
bloodmoon progress <0..100>
bloodmoon spawn [prefab]
bloodmoon resolve
bloodmoon cleanup
bloodmoon skip_current
bloodmoon reset_current_year
bloodmoon dump_participants
bloodmoon network
```

Behavior:

- commands issued on a client must route to the server and validate admin status;
- forced phase commands must still use normal transition functions and side effects;
- `progress` targets the issuing player unless an explicit safe target syntax is added;
- `cleanup` is idempotent and does not falsely mark success;
- `status` reports schedule, world/event IDs, phase, resolution step, participants, alive enemies, and synchronization revision;
- `network` reports selected CCS/RPC mode, last public revision, pending acknowledgements, and report deduplication counts.

Use structured log prefixes:

```text
[BloodMoon][event:<id>][phase]
[BloodMoon][event:<id>][player:<id>]
[BloodMoon][event:<id>][group:<id>]
[BloodMoon][event:<id>][spawn]
[BloodMoon][event:<id>][network]
[BloodMoon][event:<id>][resolution]
```

Normal expected state transitions should respect the existing logging toggle where appropriate. Warnings/errors required to diagnose invalid recovery, rejected network reports, or failed cleanup must not disappear behind verbose logging.

---

## 15. Required edge-case behavior

Implement and manually reason through at least the following:

### Calendar/time

- autumn shorter than configured final day;
- config changed before and during the event;
- time skips from before 18:00 to after 23:00;
- time skips directly past 05:45;
- season changes while resolution is running;
- season override/day override does not cause repeated daily events;
- day-length rescaling still produces visible 18:00/23:00/05:45 boundaries;
- no annual event runs twice.

### Networking

- dedicated server with no local player;
- listen server/host;
- one client;
- several clients;
- late join before Active;
- late join during Active;
- late join during AutoCompleting;
- reconnect after Active disconnect does not re-enroll;
- stale event/revision packets are ignored;
- duplicate enemy-death reports credit once;
- owner transfer does not leak alive count;
- a client missing fade ACK cannot stall forever.

### Player lifecycle

- death immediately after Active starts;
- death at the same moment as reaching 100%;
- death after GoalReached;
- death during Resolving;
- disconnect during Marked;
- disconnect during Active;
- local player loading/respawning when outcome arrives;
- sleep attempt through vanilla bed;
- alternate sleep call path from another mod;
- SoftDeath already existed before Blood Moon;
- custom day length makes the active phase longer than vanilla SoftDeath duration.

### Environment

- current weather is clear/rain/snow/fog at 18:00;
- linear blend reaches exact target at 23:00;
- forced environment is restored on normal end, early end, disconnect, invalid recovery, and world unload;
- no duplicate `Ashlands_FaderFX` clouds;
- no seasonal-luminance pop at 23:00;
- EWD/environment reload does not lose or duplicate the custom environment;
- headless server does not touch graphics objects.

### Enemy/world

- spawn inside a fully spawn-suppressed island base;
- no building/tamed/crop damage;
- no normal loot;
- short ragdoll lifetime;
- nonparticipant cannot farm/interfere;
- enemy cleanup after death, disconnect, early success, forced morning, and restart;
- maxAlive and server hard cap are enforced;
- progress is credited to all eligible nearby participants, not only killer;
- no progress from stale/ordinary enemies.

### Resolution

- solo success ends early and skips to morning;
- solo death ends early and shows death outcome;
- all players disconnect;
- nobody ever joins;
- one player GoalReached helps another;
- all participants reach GoalReached simultaneously;
- player is on a ship/in water/attached/in air during fade;
- Rested is removed;
- input and fade always recover.

---

## 16. Acceptance criteria for this task

The task is complete only when all of the following are true:

### Architecture

- explicit server event, participant, and resolution states exist;
- transitions are centralized, validated, logged, and idempotent;
- deterministic event IDs and absolute schedules are used;
- grouping is abstracted without implementing visible markers or full merge/split;
- networking choice is documented and server-authoritative.

### Playable flow

- final autumn day enters Marked at visible 18:00;
- current weather reddens linearly until 23:00;
- ordinary random event is stopped and remains blocked;
- at 23:00 participants enter combat, receive custom Bloodlust and vanilla SoftDeath, and the dedicated environment is forced;
- one event-enemy type spawns independently of player-base suppression;
- enemies attack only eligible participants and cannot damage protected world entities;
- enemies drop no ordinary loot and clean up quickly;
- nearby deaths advance authoritative progress exactly once;
- 100% preserves full buff and personal success while allowing help;
- death/disconnect resolves the participant;
- event resolves early when no Fighting participants remain or forcibly at 05:45;
- clients fade, time advances, enemies clean up, environment restores, Rested clears, and appropriate DreamText is shown.

### Safety

- first install during the active annual window skips that year;
- restart/recovery never duplicates the annual event;
- dedicated server runs without client-only exceptions;
- stale/malformed reports are rejected;
- cleanup can be called repeatedly;
- existing Seasons/Summer Heat/environment functionality still compiles and behaves as before outside Blood Moon.

### Repository quality

- `Seasons.csproj` includes all new source files;
- project builds in Debug and Release when expected local references are present;
- no new compiler warnings introduced by this feature;
- no version bump, release notes, package changes, or config migrations;
- implementation and test notes are added to the bottom of this task file before opening review.

---

## 17. Explicitly deferred work

Do not implement the following in this task. Preserve the decisions for later phases.

### 17.1 Hidden multiplayer grouping and enemy progression

Future groups will merge/split by player proximity with hysteresis and remain invisible to players. No map markers.

Enemy selection will combine global and personal progression keys:

- global keys establish the common world baseline;
- for every group member, read Player keys and union any additional enemy candidates they unlock;
- if an advanced player leaves the group, future spawn-pool calculation becomes easier for the remaining group;
- existing enemies do not need to be retroactively replaced;
- an enemy candidate unlocked by Player key but not Global key records which members unlocked it;
- such an enemy may prefer those members as attack targets;
- once the corresponding Global key exists, the enemy no longer needs player-specific target preference.

This needs a future JSON pool format containing prefab, role, global conditions, player-key conditions, weight, per-group limit, biome modifiers, progress value, and target-preference source.

### 17.2 Momentum and anti-hide pressure

Future code should separate:

```text
Momentum  — recent successful combat, used to increase the amount of weak “meat” and tempo
StallTime — enemies alive but little meaningful damage, used to add ranged/Howler/door pressure
```

Do not implement either now.

### 17.3 Multiple roles and advanced AI

Deferred:

- Swarm/Bruiser/Ranged role pools;
- Howler support aura;
- Doppelganger;
- door opening/jamming pressure;
- emote easter eggs;
- Odin observer;
- personal event-enemy visibility and ordinary-enemy hiding;
- optional friendly fire/PvP;
- return-to-event ritual after death.

### 17.4 Skill rewards — accepted future formula

Future reward modes will be configurable:

```text
None
Completion
Multiplier
Hybrid
```

The eligible combat-skill list is server configurable and resolves case-insensitive aliases to `Skills.SkillType`; do not use raw numeric IDs in user-facing config.

**Completion mode:**

1. accumulate eligible per-skill base experience/contribution during the event;
2. select at most the five skills with the highest accumulated value;
3. distribute a total budget of **+25 skill levels** proportionally across those selected skills;
4. cap the actual completion award at **+10 levels per skill**;
5. do **not** redistribute overflow removed by the cap.

Example:

```text
raw proportional allocation: +15 / +7 / +3
actual completion award:      +10 / +7 / +3
```

**Multiplier mode:**

- eligible skill gain is x3 while a separate bonus budget remains;
- track only the extra amount above normal x1 gain;
- cap that extra multiplier-derived gain at **+10 levels** for the event;
- when the extra budget is exhausted, further gain continues at x1.

**Hybrid mode:** applies both. A single skill may therefore receive at most +10 completion and +10 multiplier-derived bonus, i.e. **+20 bonus levels for that skill**. Aggregate bonus across several skills may exceed +20 because the completion pool is distributed across up to five skills.

Support contribution should later be inferred from actual eligible `Skills.RaiseSkill` activity (Blocking, BloodMagic, ElementalMagic, etc.), not guessed solely from killing blows.

### 17.5 Blood Craft — accepted future invariants

Blood Craft is available throughout Marked/Active while the player has the event status. It removes material requirements only for recipes already known to that player and applies to allowed combat categories.

Temporary items will carry versioned `m_customData` with event ID and owner ID and may exist only in their owner’s player inventory.

Future requirements:

- temporary and normal stacks must never merge;
- current Valheim stacking ignores `m_customData`, so patch the central stack-compatibility/merge path after inspecting `Inventory.cs`;
- prefer a central compatibility guard/transpiler over a virtual inventory or temporary remove/reinsert workaround unless code evidence proves otherwise;
- `ItemDrop.Awake` destroys a temporary item that reaches the world;
- patch concrete `Interactable.UseItem(Humanoid, ItemDrop.ItemData)` implementations or their reliable common paths so temporary items cannot be inserted into world objects;
- remove temporary items before tombstone inventory is created;
- remove stale temporary items during inventory load/recovery;
- free food/mead effects may remain after the event; only remaining temporary item stacks must be removed;
- free upgrades are allowed only for already-temporary items, never permanent equipment;
- projectiles/AoE created from temporary weapons must carry an event marker through ZDO or the actual current networked source path;
- temporary weapon/projectile damage to bosses is blocked;
- Blood Craft UI/VFX, station red fog, red recipes, and item tint are later work.

### 17.6 Combat tuning

Deferred until real playtests establish basic tempo:

- lifesteal;
- attack-speed modification;
- level/star changes;
- scaling `maxAlive` by elapsed time/Momentum;
- final spawn counts and damage values;
- final VFX/SFX/music selection.

---

## 18. Implementation sequence and commits

The first slice is one task, but implement it in reviewable vertical commits. A reasonable sequence is:

1. **Blood Moon state, calendar, persistence, networking decision, and debug status**
2. **Marked/Active status flow, SoftDeath, sleep blocking, random-event suppression**
3. **Environment clone, 18:00 overlay, 23:00 forced environment, cleanup**
4. **One-enemy spawner, marker, AI/damage isolation, no loot/ragdoll cleanup**
5. **Progress/death reports, early resolution, fade/morning/DreamText**
6. **Recovery hardening, commands, build cleanup, test notes**

The exact commit boundaries may differ, but each commit should build or clearly document a temporary compile-safe stub.

---

## 19. Codex review workflow

After implementation:

1. build Debug and Release;
2. inspect the full branch diff against `master`;
3. update the implementation/test notes below;
4. push `feat/blood-moon`;
5. open a **draft pull request** to `master`;
6. run Codex code review on the PR;
7. address every valid correctness, networking, lifecycle, cleanup, compatibility, and maintainability finding;
8. document any intentionally rejected finding with technical reasoning;
9. run Codex review again until no unresolved substantive findings remain;
10. leave the PR draft and unmerged for owner game testing.

Review must pay particular attention to:

- server/client authority violations;
- state transitions that can happen twice or not at all;
- client ownership of Character deaths;
- stale event IDs/revisions;
- dedicated-server null paths;
- force-environment restoration;
- random-event suppression leaks;
- enemy AI targeting world objects;
- cleanup after restart/disconnect/time skip;
- SoftDeath lifetime and skill-loss ordering;
- compatibility with current Seasons environment and Summer Heat code.

---

## 20. Implementation notes to complete before review

Codex must replace this section with concrete results before opening the draft PR.

### Networking decision

- Chosen CCS/RPC architecture:
- Why it was chosen:
- Custom values/RPC names added:
- Revision/deduplication strategy:

### Persistence

- File/global-key schema:
- Atomic-write behavior:
- Restart/recovery behavior verified in code:

### Environment

- Exact initialization/reload hook:
- Force-environment restore behavior:
- Cloud-object lifecycle:

### Enemy prototype

- Default prefab:
- Spawn validation path:
- AI target filtering methods patched:
- Damage safety methods patched:
- Death/ragdoll marker propagation:

### Builds

- Debug build:
- Release build:
- Warnings:

### Manual test checklist status

- Solo:
- Host/client:
- Dedicated server:
- Death/SoftDeath:
- Early resolution:
- Forced 05:45 resolution:
- Environment transition:
- Random-event suppression:
- Cleanup/restart:

### Remaining known limitations for owner playtest

-
