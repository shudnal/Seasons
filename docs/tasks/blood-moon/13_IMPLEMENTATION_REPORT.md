# Blood Moon implementation report

This report is the implementation/recovery checkpoint for `feat/blood-moon` and complements the authoritative design set in `docs/tasks/blood-moon/01` through `12`.

## Status

The first Blood Moon implementation slice is assembled end-to-end on `feat/blood-moon` and is being frozen for the first repository-wide review and runtime playtest.

Base branch:

```text
master
```

Authoritative Valheim source used for game API and patch-point verification:

```text
repository: https://github.com/shudnal/assemblies_combined
commit: cf2cda3a4c5c05e62cb8052a61753e5dcaecc28e
```

The Seasons version, public README, Thunderstore changelog, packaging and release metadata were intentionally not changed.

## Architecture implemented

### Event state and schedule

- Explicit server event state machine: Dormant, Forewarning, Marked, Active, AutoCompleting, Resolving, Resolved and Skipped.
- Explicit participant state: Marked, Fighting, GoalReached, Exited and Resolved with independent `GoalReached` and `ExitReason` facts.
- Frozen absolute schedule persisted per world with deterministic `eventId = eventWorldDay`.
- Default annual timing: forewarning from autumn day 6 at 12:00, Marked at 18:00 on event day, Active at 23:00, automatic displayed-progress floor from 04:15, forced resolution at 05:45 and frozen morning at 06:00.
- Safe first-enable observation prevents starting an already-in-progress first annual event after installing/enabling the feature inside its window.
- Restart reconciliation advances persisted state to the expected frozen phase before the first client snapshot is published.

### Persistence

- Per-world JSON state under the Seasons config directory.
- Schema and world UID validation.
- Crash-recovery read order: canonical snapshot, `.new`, then `.old`.
- Recovered fallback snapshots rewrite the canonical file.
- Event state is persisted at state-changing boundaries and on world save.
- Boss parking also uses durable ZDO markers so recovery does not depend only on the sidecar.

### CCS and routed RPC split

CCS normal `CustomSyncedValue` snapshots contain:

- global event routing state;
- public participant routing state.

Personal progress and bookkeeping are not broadcast. Targeted participant detail contains only the owning player's combat/display progress, contribution, skill report baseline and live bonus usage.

Own routed RPCs cover:

- Defeated notification;
- enemy-death reports;
- per-zone ownership claims and spawn leases/reports;
- fade and fade acknowledgements;
- explicit resync;
- personal client actions;
- skill reports;
- boss discovery;
- immutable hit attribution transport;
- teleport/spatial anchor suspension.

Server-to-client event RPCs validate the server sender. Client-to-server operations validate event identity and the peer/player or peer/ZDO authority appropriate to the operation.

Explicit resync returns current global routing, public participant routing and only the requesting player's private detail without mutating the event revision solely to force transport.

### Forewarning, Marked and system suppression

- Forewarning presentation derives from the current environment.
- Marked enrolls current players and late join remains supported until enrollment freezes.
- Sleep is blocked for affected participants while the Blood Moon restriction is active.
- Boss-producing `OfferingBowl` interactions are blocked without changing item-producing bowls.
- Active random events are stopped and new random-event progression is suppressed through resolution.
- The suppression is released only after the Blood Moon world systems are restored.

### Environment and presentation

- A runtime environment is cloned from vanilla `Fader` and registered once as `Seasons_BloodMoon`.
- Every `Color` field on the cloned `EnvSetup` has its red component set to 1.
- Wind and sun-angle overrides follow the authoritative design.
- Forewarning/Marked overlay is applied around vanilla `EnvMan.SetEnv` after Seasons luminance modification and restored after the call.
- Temporary `EnvSetup` mutation is exception-safe through a Harmony finalizer.
- Active uses a force-environment lease and restores the previous force only while the lease still belongs to Blood Moon.
- `Ashlands_FaderFX` cloud presentation is cloned/filtered and cleaned up without mutating the vanilla source object.
- Resolution fade has an acknowledgement timeout and blocks Player input until clients are released.
- The supplied `blood_moon.png` sprite is embedded for the main Blood Moon and recovery status effects.
- Recovery uses the same sprite with vanilla `StatusEffect.m_cooldownIcon = true` overlay behavior.

### Combat groups

- Server-only connected-component groups are recomputed on a 4 second cadence.
- Default merge/split hysteresis is 120/160 metres.
- Group IDs are retained by maximum member overlap.
- Group anchors are always positions of real members, never a geometric synthetic point.
- Teleporting participants remain enrolled but are temporarily suspended from spawn-anchor and relevant-zone calculations.
- GoalReached remains an active helper while Fighting participants remain.

### Zone-owner extra spawning

- Dedicated server does not attempt to execute vanilla `SpawnSystem` spawning.
- A client that owns a loaded `SpawnSystem` reports its zone.
- The server validates the peer/player identity and independently validates the claim against an owned raw `SpawnSystem` ZDO in that exact sector.
- Separate leases carry event, group, group revision, zone, owner/session, lease revision, budget and caps.
- Ownership/group-revision changes invalidate only the affected zone lease.
- Reservations, pending reports and live extras contribute to group/server cap accounting.
- A rejected/stale spawn report cannot delete an arbitrary ZDO. Rejected objects are watched for delayed replication and are destroyed only after the expected Blood Moon event marker is observed; the immutable ZDOID creator must match the reporting peer.
- Surface extras use distance, visibility, terrain and path checks and intentionally ignore ordinary PlayerBase/NoMonsters suppression.
- Interior extras use loaded authored `CreatureSpawner` positions and require a full path; `CreatureSpawner.Spawn()` and its respawn bookkeeping are not used.
- Current bootstrap uses the explicit configured test prefab as required by the first slice. The automatic enemy-pool contract remains the separately documented future progression work.

### Extra enemy lifecycle

Extra enemies carry only current-event markers for event ID, group ID and role.

- No ordinary loot.
- Ragdoll loot disabled.
- Ragdoll destruction is explicitly rescheduled to 2 seconds because vanilla `Ragdoll.Awake` schedules `DestroyNow` before `Ragdoll.Setup` executes.
- Surviving and stale marked ZDOs are deleted during resolution/recovery.
- Ordinary existing monsters are never deleted by extra cleanup.

### Existing monster AI

Existing Blood enemies are selected dynamically from loaded valid `MonsterAI` characters:

- alive;
- not a Player;
- not boss;
- not tamed;
- not Players/PlayerSpawned/TrainingDummy faction;
- vanilla hostile to at least one active participant.

`AnimalAI` is a separate vanilla class, so ordinary passive animals are not converted by the MonsterAI predicate. Neutral Dvergr remain neutral unless vanilla aggravated hostility applies.

Runtime-only AI behavior includes:

- Blood target acquisition within configured hunt range;
- Fighting target priority with GoalReached fallback among valid in-range candidates;
- runtime HuntPlayer/alert semantics;
- static-target suppression;
- flee/consume/fire/no-monster-area suppression while Blood behavior is active;
- configurable run-speed and target-update modifiers.

Existing monsters are not given Blood conversion ZDO markers and no Blood code calls persistent `SetHuntPlayer(true)` or writes a permanent event conversion state.

### Damage routing and immutable attribution

Owner-side `Character.RPC_Damage` is the final Character damage guard before vanilla side effects. Earlier candidate filtering covers direct attack context, projectile, AOE and common world destructibles.

Allowed event combat is restricted to:

```text
participant source -> current Blood enemy
Blood enemy -> Fighting/GoalReached participant
```

Participant/Blood sources cannot use the event combat path to damage ordinary characters or persistent world targets. Trap/turret/environment sources do not gain Blood enemy farming behavior.

Projectile and AOE attribution captures immutable event/source identity at primary setup. It can be routed to the current target owner before damage. A delayed hit can remain legal after the source player's personal exit while the global event is still live, but progress credit requires the player to still be eligible when credit is evaluated.

### Combat summons

`SpawnAbility` receives immutable Blood Moon owner/event context. Spawned Characters receive event/owner markers at the vanilla `SetupAoe` transfer point.

- Permanent summons remain ordinary persistent summons after the event but their old event marker stops affecting behavior.
- Participant summons target only Blood enemies while the event context is active.
- Direct/projectile/AOE damage is routed through the same central event policy.
- Progress credit maps back to the owner while the owner remains eligible.
- Temporary Blood Craft summons are deleted at personal exit, global resolution and stale recovery, with short cleanup watches for replication races.
- `TriggerSpawnAbility` is explicitly excluded/blocked for Blood Craft because its vanilla `Setup` mutates world state through `TriggerSpawner.TriggerAllInRange` rather than creating an attributable combat summon.

### Enemy death and progress

- The owner of the dying enemy ZDO reports the death.
- The server separately validates the credited participant.
- The server validates the enemy ZDO/prefab eligibility and calculates points; the client-provided point field is ignored.
- Enemy ZDO IDs are deduplicated exactly once in persisted event state.
- Points are shared with active same-group members inside the configured share radius.
- Combat points, displayed progress and contribution are distinct values.
- GoalReached is based only on real combat progress.
- Auto-completion raises only displayed progress and does not create combat points, contribution, success or reward.

### Defeated, Withdrawn and recovery

Local Player owner intercepts `Character.CheckDeath` before `Player.OnDeath` for a Fighting/GoalReached participant.

Defeated behavior:

- no grave, death point, ragdoll or respawn;
- health/stamina/eitr restored to current maxima;
- food and adrenaline unchanged;
- only computed damaging vanilla DoTs are removed;
- no transform/velocity reset;
- no vanilla SoftDeath;
- terminal personal exit with no re-entry.

Recovery is world-bound and persisted in Player custom data:

- stage 1: full incoming protection until ground/swimming/attached, maximum 15 seconds;
- stage 2: 10 seconds at incoming multiplier 0.25;
- continues through global morning resolution and reconnect.

Withdrawn is a separate terminal exit used for world-edge safety and unsupported boss encounters, preserves prior GoalReached, and does not move the Player.

### Boss parking

Clients report only actually loaded bosses near active participants. The server validates reporter identity, raw boss ZDO, prefab hash, distance, boss state and persistence.

- persistent outdoor boss: durable marker-first parking transaction;
- interior or nonpersistent boss: no parking, affected participant(s) become Withdrawn;
- parking ownership is transferred to the server before relocation;
- parked position is a deterministic far XZ slot;
- serialized transform/body velocity is zeroed during parking/reassertion;
- `ForceSendZDO` and a short reassertion window cover queued old-owner revision races;
- original position is stored with explicit marker presence, so world origin is a valid position;
- stale markers restore after restart even if the sidecar record is unavailable;
- marker cleanup is completed before the parking transaction is forgotten;
- exact runtime animation/AI target/coroutine state is intentionally not restored, matching the accepted contract.

### Blood Craft

Blood Craft derives temporary recipe clones from already-known combat recipes without mutating the registered source recipes.

Temporary item identity contains:

```text
schema
world UID
event ID
owner player ID
```

Invariants include:

- no resource or station cost for Blood recipe clones;
- permanent recipe remains available;
- temporary upgrade only upgrades the marked temporary item;
- permanent and temporary stacks cannot merge;
- different world/event/owner temporary stacks cannot merge;
- multi-craft output is chunked to vanilla maximum stack size;
- drop/world `ItemDrop` creation is blocked and stale world items are destroyed;
- transfer to external inventories is rejected at Inventory boundaries;
- vanilla Container stale marker recovery removes and resaves invalid temporary contents;
- world sinks such as item/armor stands, fermenter, cooking station, smelter, turret and catapult are guarded;
- auto-selected consumer inputs skip temporary items and prefer a permanent equivalent where possible;
- trader selling ignores temporary valuables;
- equipped items are allowed while valid and are removed/unequipped during personal/global cleanup;
- consumed food/mead effects are not retroactively removed;
- configured food/mead allowlist remains server controlled.

### Skill rewards

Configured combat skills are parsed as case-insensitive `Skills.SkillType` names; invalid names are logged and ignored.

Live gain:

- observes real `Player.RaiseSkill` calls;
- calculates level-equivalent on the nonlinear vanilla skill curve;
- keeps the normal gain as the base contribution;
- adds up to x2 extra for effective x3 while the global live bonus budget remains;
- total Blood Moon live bonus is capped by configured level-equivalent budget;
- reconnect requires the targeted server sequence/budget baseline before bonus reporting resumes, otherwise vanilla x1 remains safe.

Completion reward:

- uses the top five configured skills by accumulated base contribution;
- configured total budget defaults to +25 level-equivalent at 100% earned combat progress;
- scales by real combat progress only;
- configured per-skill cap defaults to +10;
- capped overflow is not redistributed;
- application is idempotent and world/event-bound through Player custom data;
- an interrupted replay can never lower a skill that has since progressed above the stored absolute target.

### Resolution and outcomes

Early resolution requires at least one enrolled participant and no unfinished participant. GoalReached counts as complete for this condition while remaining combat-active until resolution begins.

Resolution steps are persisted and replayable:

1. freeze enrollment;
2. stop new spawn leases;
3. request fade and wait for acknowledgements with timeout;
4. disable Blood behavior;
5. clean event extras;
6. restore parked bosses;
7. clean temporary Blood Craft items/summons;
8. release forced environment and random-event suppression;
9. move net time to the frozen 06:00 target;
10. publish skill reward, chronicle/DreamText state and one-time Rested removal;
11. release clients and input;
12. finalize Resolved state.

Chronicle/reward/Rested application is idempotent across outcome replay and reconnect. DreamText is persisted as a one-shot outcome and is consumed through vanilla sleep dream selection.

## Diagnostics implemented

`seasons bloodmoon` exposes diagnostic commands for:

- status;
- phase start;
- progress/GoalReached;
- Defeated/Withdrawn;
- marked debug spawning;
- boss parking/restoration;
- resolution/cleanup;
- participant/group/spawn-zone/monster/boss/sync dumps.

Debug commands reuse production transition/cleanup methods rather than maintaining a separate diagnostic state machine.

## Static verification performed

No local Valheim build or runtime was executed as part of this work. The owner explicitly requires Valheim mod implementation work not to be built/tested on the assistant side.

Instead, patch targets and critical behavior were checked directly against `assemblies_combined` commit `cf2cda3a4c5c05e62cb8052a61753e5dcaecc28e`, including:

- `ZRoutedRpc` sender semantics;
- `ZNetPeer` and Player ZDO identity;
- `OfferingBowl` boss spawn flow;
- `Bed` sleep flow;
- `RandEventSystem` update/start/reset behavior;
- `EnvMan`, `EnvSetup` and forced environment behavior;
- `SpawnSystem`, `CreatureSpawner`, `ZDOMan`, `ZNetScene` and `ZDOID` ownership/sector behavior;
- `BaseAI`, `MonsterAI` and `AnimalAI` target semantics;
- `Character.CheckDeath`, `Character.Damage` and `Character.RPC_Damage`;
- `Projectile`, `Aoe`, `SpawnAbility` and `TriggerSpawnAbility`;
- `Ragdoll` scheduling/loot behavior;
- `Inventory`, `InventoryGui`, `Container`, world consumer objects and `StoreGui`;
- `Skills` and `Player.RaiseSkill`;
- `Hud` cooldown icon behavior;
- `Player.IsTeleporting` and the absence of a vanilla ZDO teleport flag.

The final static pass also corrected multiple concrete issues discovered during implementation, including stale ownership spawn cleanup, private progress privacy, reconnect skill sequence/budget recovery, restart phase publication, delayed projectile attribution, Ragdoll lifetime scheduling, GoalReached fallback targeting, resolution input blocking, monotonic completion reward replay and Blood Craft maximum-stack output.

## Runtime playtest status

Not executed by the assistant.

The following remain explicitly unverified until owner-side runtime playtest:

- single-player full annual lifecycle;
- listen-host full lifecycle;
- dedicated server with multiple clients;
- real zone ownership migration while extra spawns are in flight;
- packet/order races under real latency;
- boss parking with every vanilla boss and owner-revision timing;
- surface/interior spawn feel and path quality;
- combat AI behavior for the full vanilla/modded MonsterAI set;
- Blood Craft interaction with third-party custom inventory/equipment implementations;
- all visual/VFX presentation in every biome/interior/weather combination;
- resolution fade/input feel;
- balance values and skill-reward pacing.

## Known limitations and intentionally deferred work

- Music hooks exist but no Blood Moon music assets are currently supplied; music production is intentionally paused.
- Blood Moon-specific runtime text is currently English-first. Full localization across the Seasons language set is a polish task after wording stabilizes in playtest.
- The first slice intentionally uses one explicit extra-enemy bootstrap prefab. Automatic raid/trophy/key/achievement-derived enemy-pool inference is the future contract documented in `06_FUTURE_PROGRESSION_AND_REWARDS.md` and is not part of this implementation slice.
- Generic third-party world sinks that bypass both vanilla `Inventory` transfer and known vanilla consumer methods may require compatibility work if discovered in runtime testing.
- Exact parked boss animation/target/coroutine/HUD state is not restored by design; the same persistent ZDO is returned to its original world position and vanilla runtime state resumes naturally.

## Review and continuation

Before merge, this branch must still receive:

1. draft PR to `master`;
2. repository/Codex review of the complete diff;
3. fixes for confirmed review findings;
4. owner-side runtime playtest using the acceptance matrix in `08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md`.

The PR must remain unmerged until the owner explicitly approves it.
