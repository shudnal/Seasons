# Blood Moon implementation report

This document is the implementation and recovery checkpoint for the complete Blood Moon first gameplay slice on `feat/blood-moon`. The authoritative design remains `docs/tasks/CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md` plus `docs/tasks/blood-moon/01` through `12`.

## Repository status

```text
base branch: master
implementation branch: feat/blood-moon
pull request: #42
pull request state: draft / open / not merged
code-freeze reviewed head: 0be8eb89d600a95720f08387041fb7ab15ccd5f7
```

The plugin version, public README, Thunderstore changelog and packaging/release metadata are intentionally unchanged.

No local Valheim build or runtime execution was performed by the assistant, per owner workflow. Static verification used the current decompiled game source first:

```text
repository: https://github.com/shudnal/assemblies_combined
commit: cf2cda3a4c5c05e62cb8052a61753e5dcaecc28e
```

## Implemented architecture

### Explicit state machines and frozen schedule

The server owns the event lifecycle:

```text
Dormant
Forewarning
Marked
Active
AutoCompleting
Resolving
Resolved
Skipped
```

Participant state is independent:

```text
None
Marked
Fighting
GoalReached
Exited
Resolved
```

Participant exit reason is stored separately as `Defeated`, `Withdrawn` or `Disconnected`, and `GoalReached` remains an independent fact.

The annual schedule is frozen and persisted per event. With default settings it provides forewarning, Marked, Active, auto-completion, forced-end and 06:00 morning boundaries. Pre-combat feature disable uses a persisted cancellation path and never advances the world to the event morning or publishes normal success outcomes.

### Durable event persistence

The current world event state is stored under the Seasons Blood Moon config directory. Persistence validates schema and world UID. Recovery examines canonical, `.new` and `.old` candidates and chooses the newest valid state by revision, update time and file timestamp instead of blindly preferring the canonical file. A recovered fallback is rewritten as canonical.

State-changing boundaries persist before dependent work continues. Restart reconciliation advances the recovered state to the correct frozen phase before normal publication.

Boss parking also stores durable markers directly on the boss ZDO, so recovery does not depend exclusively on the JSON sidecar.

### Network authority and privacy

CCS publishes only global routing state and public participant routing state. Personal progress, contribution, report sequence and live-skill budget remain private and are delivered only to the owning player.

Routed RPC handlers validate the appropriate authority boundary:

- server sender for server-to-client event traffic;
- peer/player identity for participant reports;
- immutable ZDOID creator and current ownership where object authority matters;
- event, group, group revision, zone, lease revision and frozen spawn-pool identity for extra-enemy reports;
- server-observed ZDO state for credited enemy deaths.

Explicit resync is monotonic: an older response cannot overwrite a newer same-event CCS snapshot. Resync retries after the local Player exists until current global, public and private state align, and skill report sequence/live-bonus baselines are preserved.

### Presentation, environment and suppression

Blood Moon presentation is derived from vanilla runtime objects instead of mutating shared source definitions.

- `Seasons_BloodMoon` is cloned from vanilla `Fader`.
- All `EnvSetup` color fields receive the required red-channel treatment.
- Forewarning/Marked presentation is applied around vanilla environment selection with exception-safe restoration.
- Active uses a force-environment lease and releases only the lease owned by Blood Moon.
- Fader cloud presentation is cloned/filtered rather than modifying the vanilla source object.
- Random-event suppression is owned explicitly and is released on resolution and world teardown.
- Sleep and boss-producing offering interactions are guarded during their required phases.
- Resolution fade waits for acknowledgements with a timeout and blocks local Player input until release.
- The supplied `blood_moon.png` sprite is used for Blood Moon and recovery status presentation.

Real-time-calendar worlds never feed UTC/calendar absolute seconds into Valheim net time. The frozen 06:00 transition uses the correct net-time domain and broadcasts the resulting net time.

### Hidden combat groups and spatial state

The server rebuilds connected combat groups with merge/split hysteresis. Group anchors are positions of actual members. GoalReached participants remain valid helpers while Fighting members remain.

Teleporting players remain enrolled but are temporarily excluded from spawn-anchor/zone calculations by heartbeat/TTL spatial suspension so interrupted teleport state self-recovers.

AI targeting, boss encounter selection and mixed spawn contexts distinguish surface from interior navigation context. Interior comparisons use the resolved vanilla `Location` when available and otherwise fall back to the relevant zone context.

### Zone-owner extra-enemy spawning

The dedicated server does not pretend to own vanilla client-side loaded-zone spawning. A client owning a loaded `SpawnSystem` reports the relevant sector, and the server independently verifies the claim against raw owned SpawnSystem ZDO state.

Leases carry:

```text
event ID
group ID
group revision
zone
owner peer/session
lease revision
anchor
allowance
group cap
server hard cap
frozen spawn-pool identity
expiry
```

Client lease lifetime is rebased into the receiving client's monotonic time domain rather than comparing independent real-time-calendar clocks.

The event's extra-enemy prefab is frozen once before the first lease is issued. The frozen prefab name is persisted in the event state, and its stable prefab hash is carried in the existing `PoolRevision` field. Live config changes therefore cannot change an already-running event's spawn identity.

A separate server-only durable spawn-pool registry stores:

```text
world UID -> event ID -> frozen prefab name
```

The registry uses canonical/`.new`/`.old` recovery and preserves complete event history. This allows stale extras from prior events to be validated and removed after the current event state has already been replaced, without falling back to trusting arbitrary client markers.

Spawn reports are accepted only when all authoritative facts align. The reported ZDO must:

- be created by the reporting peer;
- belong to the current event/group/leased sector;
- match the current lease revision and frozen pool identity;
- resolve to the exact frozen prefab;
- contain `MonsterAI`.

Deferred report validation repeats the prefab/enemy check after ZDO replication. Rejected reports use bounded delayed cleanup, and cleanup refuses to destroy unrelated marked ZDOs.

Surface spawning uses distance, terrain, visibility and path checks. Interior spawning uses authored loaded `CreatureSpawner` positions without invoking the spawner's own respawn bookkeeping. Mixed zones partition surface/interior targets and preserve both contexts. Resolution explicitly blocks the replacement mixed-context spawner so an outstanding lease cannot race the spawn freeze.

### Extra-enemy lifecycle and cleanup

Blood Moon extras carry event/group/role markers and have no ordinary loot. Ragdoll drops are disabled and event ragdoll lifetime is shortened safely after vanilla setup ordering.

Resolution and recovery clean all validated extras for the event, including extras whose report was lost entirely, while preserving unrelated structures, players or other prefabs even if a modified client forged event markers on them.

Dormant state with `EventId == -1` never scans ordinary unmarked world ZDOs as event extras.

Stale prior-event cleanup uses the durable server-side spawn-pool registry and therefore retains the same exact-prefab/`MonsterAI` validation instead of weakening the security rule.

### Existing-monster behavior and damage routing

Existing Blood enemies are selected dynamically from eligible loaded hostile `MonsterAI` characters. They are not permanently converted and no persistent HuntPlayer/event-conversion mutation is written to ordinary monsters.

The interaction matrix remains active through early `Resolving` while the authoritative `BloodBehaviorEnabled` flag is still set. Final owner-side `Character.RPC_Damage` guarding plus earlier direct/projectile/AOE filtering enforce the allowed event combat routes.

Environmental/unattributed hazards remain able to damage participants. Participant-to-participant bypasses are not opened by claimed Blood-enemy attribution: claimed sources are validated against the raw ZDO enemy predicate and sender authority.

Projectile, AOE and summon attribution records immutable event/source facts. Stale attribution from an earlier event cannot become valid again after rollover.

### Enemy-death credit and progress

A death report never trusts client-provided points. The server validates sender authority, enemy eligibility and server-observed `ZDOVars.s_health <= 0`. Otherwise-valid reports can wait briefly for delayed health replication before expiring.

Enemy IDs are persisted and deduplicated exactly once. Combat points, display progress and contribution are distinct. GoalReached uses real combat progress only; auto-completion affects display progress without manufacturing combat credit or rewards.

Late defeat reports are rejected once global combat resolution has started, so the final participant outcome cannot be rewritten during fade/cleanup.

### Defeated, Withdrawn and recovery

Defeated intercepts the local Player death before vanilla grave/respawn handling for active participants. It preserves inventory/food/adrenaline, restores current resource maxima, removes only computed damaging vanilla DoTs, avoids teleporting or resetting velocity and enters terminal personal recovery without vanilla SoftDeath.

Recovery state is world/event bound in Player custom data and survives reconnect/global morning resolution. Withdrawal is a distinct terminal exit used for unsupported boss/world-edge cases and does not move the Player.

Boss discovery and boss withdrawal both require the same navigation context as the reporting/affected participant. A surface boss is therefore not parked because a player happens to be in a vertically separated dungeon at similar XZ coordinates.

### Boss parking

Persistent outdoor bosses use marker-first durable parking. The server takes ownership, zeros serialized movement, relocates the same ZDO to a deterministic far slot, forces replication and briefly reasserts the parked transform to cover old-owner revision races.

Original position has explicit marker presence, so world origin is valid. Recovery can reconstruct the parking transaction from ZDO markers even if the sidecar transaction is absent. Interior/nonpersistent bosses are not parked; affected participants in the same encounter context are withdrawn instead.

### Blood Craft

Blood Craft derives temporary recipe clones from already-known, currently enabled combat recipes without mutating the source recipe. Temporary item identity is bound to schema, world UID, event ID and owner player ID.

Implemented invariants include:

- no Blood Craft resource/station cost;
- permanent recipe remains available;
- temporary upgrade only targets the matching temporary item;
- invalid world/event/owner stacks cannot merge;
- multi-craft respects vanilla maximum stack size;
- stale world `ItemDrop` and stale container items are cleaned safely;
- transfer to external inventories/world sinks is rejected at vanilla boundaries;
- item/armor stands, fermenter, cooking station, smelter, turret and catapult are guarded;
- automatic consumers prefer permanent equivalents where possible;
- disabled source or upgrade recipes are excluded;
- temporary combat summons are attributable and cleaned at personal/global boundaries;
- unsafe `TriggerSpawnAbility` world mutation is excluded from Blood Craft.

World-transition cleanup preserves valid active-event Blood Craft inventory until the recovered world/event/owner context is known, then removes stale markers.

### Skill accounting and rewards

Live skill accounting observes the real `Player.RaiseSkill` override, not the base Character method. It tracks nonlinear level-equivalent progress, separates base contribution from the x3 live bonus and enforces the server-provided sequence/budget baseline after reconnect.

Completion rewards use persisted contribution, configured eligible skills, total budget and per-skill cap. Application stores monotonic absolute targets so an interrupted replay cannot reduce later legitimate skill progress.

### Durable outcomes

Final outcomes are captured independently of the single current event state before resolution publication. They are stored in a per-world durable outcome queue with canonical/`.new`/`.old` newest-valid recovery.

The queue contains per-player event ID, serialized completion reward and chronicle. Offline players therefore keep their undelivered outcome even after a later annual event replaces the current event state.

Client application is idempotent and writes a full-outcome marker only after reward, chronicle/DreamText state and one-time Rested removal complete successfully.

Acknowledgement is persistence-aware:

- local profiles acknowledge only after the Player data was captured and `PlayerProfile.Save()` completed successfully;
- cloud profiles do **not** treat vanilla `Save() == true` as durable proof, because current Valheim can return true after a cloud `FileWriter` failure while only writing a local recovery backup;
- for cloud profiles the server queue is retained until a fresh process observes the outcome marker loaded back from character persistence;
- listen-host delivery follows the same durable rule.

This closes the crash window between mutating the local Player and removing the server-side durable outcome.

### Resolution transaction

Resolution is persisted as ordered replayable steps:

1. freeze enrollment;
2. stop new spawn leases;
3. request client fade and wait for acknowledgements/timeout;
4. disable Blood behavior;
5. clean validated extras;
6. restore parked bosses;
7. clean temporary Blood Craft state and temporary summons;
8. restore environment/random-event systems;
9. advance ordinary-world net time to the frozen 06:00 target when applicable;
10. capture/publish durable outcomes;
11. release clients and input;
12. finalize Resolved state.

Pre-combat cancellation follows the cleanup transaction but skips morning advance and normal outcome publication.

## Review hardening incorporated

Multiple full-diff Codex passes plus manual freeze audits produced and verified fixes for, among other issues:

- private resync skill baselines and local-player-ready retries;
- active Blood Craft recovery across world transitions;
- pre-combat cancellation time skip;
- random-event suppression leakage across world unload;
- boss and AI surface/interior context;
- environmental hazard damage;
- direct attack Harmony target enumeration and the real `Player.RaiseSkill` override;
- mixed-context spawning;
- server-observed enemy death validation;
- world-scoped chronicle identity;
- independent durable offline outcome queue;
- monotonic resync envelopes;
- real-time-calendar net-time and lease-time domains;
- newest-valid persistence recovery;
- hostile attribution validation;
- interaction routing through early resolution;
- disabled Blood Craft recipes;
- arbitrary marked-ZDO spawn-report injection;
- resolution spawn race;
- persistence-aware outcome acknowledgement;
- late defeat reports;
- dormant `EventId == -1` cleanup;
- frozen per-event extra prefab identity;
- stale prior-event extra cleanup through durable spawn-pool history.

The exact code-freeze head `0be8eb89d600a95720f08387041fb7ab15ccd5f7` received a Codex result with no major issues. Review chronology and exact findings are recorded in `14_CODE_REVIEW_STATUS.md`.

## Static game-source verification

Critical game/API behavior was checked against `shudnal/assemblies_combined@cf2cda3a4c5c05e62cb8052a61753e5dcaecc28e`, including:

- `ZRoutedRpc`, `ZNetPeer`, Player ZDO identity and sender semantics;
- `ZDOID` creator identity, `ZDOMan` ownership and sectors;
- `ZNetScene.GetPrefab(string/int)` and stable prefab hashes;
- `SpawnSystem`, `CreatureSpawner`, `BaseAI`, `MonsterAI` and `AnimalAI`;
- `Location.GetLocation` and interior context;
- `Character.CheckDeath`, damage/RPC flow and ZDO health;
- `Projectile`, `Aoe`, `SpawnAbility` and `TriggerSpawnAbility`;
- `Ragdoll` setup/destruction behavior;
- `OfferingBowl`, `Bed`, `RandEventSystem`, `EnvMan` and `EnvSetup`;
- vanilla inventory/container/world-consumer boundaries;
- `Skills` and the `Player.RaiseSkill` override;
- `PlayerProfile.SavePlayerData`, `PlayerProfile.Save`, `FileWriter` and `FileHelpers.ReplaceOldFile`, including cloud-save failure semantics.

## Diagnostics

`seasons bloodmoon` includes diagnostic paths for event status, phase forcing, progress/GoalReached, Defeated/Withdrawn, marked debug spawning, boss park/restore, resolution/cleanup and participant/group/zone/monster/boss/sync dumps. Diagnostics reuse production transitions/cleanup rather than maintaining a separate state machine.

## Runtime verification gate

No assistant-side build or runtime execution was performed.

Owner-side playtest must still cover the acceptance matrix in `08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md`, including at minimum:

- single-player, listen-host and dedicated multiplayer annual lifecycle;
- late join/reconnect and dedicated-server restart during Active/Resolving;
- Defeated from combat and environmental hazards;
- recovery on ground, in water and while falling;
- GoalReached helper behavior, Defeated and Withdrawn;
- real owner migration with in-flight leases/reports;
- mixed surface/interior participants and authored interior spawners;
- boss parking/restoration and unsupported encounter withdrawal;
- Blood Craft inventory/world-sink/temporary-summon boundaries;
- live x3 budget, reconnect baseline and completion reward replay;
- offline outcome delivery and local/cloud persistence acknowledgement;
- config change of extra-enemy prefab during an active event and across restart;
- lost spawn report followed by resolution cleanup;
- dormant world recovery with no event;
- stale extra from a prior event after a later event state exists;
- real-time-calendar mode;
- pre-combat feature-disable cancellation;
- 04:15/05:45/06:00 final-night behavior and fade/input release.

## Known limitations and deferred work

- Blood Moon music assets are not supplied; music work remains paused.
- Blood Moon-specific runtime wording is English-first until playtest wording stabilizes.
- Automatic raid/trophy/key/achievement-derived enemy-pool progression remains the future contract in `06_FUTURE_PROGRESSION_AND_REWARDS.md`; this implementation uses the explicit configured bootstrap prefab and freezes that identity per event.
- Third-party world sinks or inventory implementations that bypass vanilla guarded boundaries may require compatibility patches after runtime discovery.
- Exact parked-boss animation/target/coroutine/HUD runtime state is intentionally not reconstructed; the same persistent ZDO is restored and vanilla runtime state resumes.

## Continuation rule

PR #42 must remain draft and unmerged until explicit owner approval. Runtime defects, decisions and fixes must be recorded in the repository before continuing. Release metadata remains untouched until the owner explicitly requests the release step.
