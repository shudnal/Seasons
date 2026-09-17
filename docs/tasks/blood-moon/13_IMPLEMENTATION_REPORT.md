# Blood Moon implementation report

This document is the implementation and recovery checkpoint for the Blood Moon gameplay slice on `feat/blood-moon`. The authoritative product and architecture contract remains `docs/tasks/CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md` plus `docs/tasks/blood-moon/01` through `12`.

## Repository checkpoint

```text
base branch: master
implementation branch: feat/blood-moon
pull request: #42
pull request state: draft / open / not merged
pre-documentation hardening head: 3b55e99f4006b2aca73686a5130e8b843888ee3f
```

The final documentation-inclusive review head is intentionally recorded in the PR timeline after this documentation freeze so the reviewed SHA can remain exact without another documentation-only commit.

The plugin version, public README, Thunderstore changelog and packaging/release metadata remain unchanged.

Per owner workflow, the assistant did not build or run the Valheim mod. Static verification used the current decompiled game source first:

```text
repository: https://github.com/shudnal/assemblies_combined
commit: cf2cda3a4c5c05e62cb8052a61753e5dcaecc28e
```

## Implemented architecture

### Explicit state machines and frozen annual schedule

The server owns explicit event and resolution state machines. Participant phase and exit reason are independent, with `GoalReached` preserved after later `Defeated`, `Withdrawn` or disconnect.

The event schedule is frozen and persisted from the event world day and event-time day length. Default boundaries remain:

```text
forewarning start: autumn day 6
final autumn day: 9
Marked: 18:00
Active: 23:00
AutoCompleting: 04:15
forced end: 05:45
morning target: 06:00
```

Pre-combat feature disable is handled directly by the production resolution path: it runs cleanup but does not advance time or publish normal outcomes.

### Persistence and restart recovery

Current event state uses world-UID-bound JSON persistence with canonical, `.new` and `.old` candidates. Recovery chooses the newest valid snapshot instead of blindly preferring the canonical file.

Extra-enemy spawn identity and parked-boss recovery also have durable server-side/ZDO evidence. Matching active state is reconstructed; stale extras and parked bosses are reconciled without depending on live `Character` instances on a dedicated server.

Persisted participants receive bounded reconnect grace after a server restart before disconnect becomes terminal.

The independent per-player outcome queue is now an explicit durable resolution dependency. `PublishingOutcomes` does not begin delivery or presentation timing until `Capture` has atomically persisted the queue snapshot. A failed write leaves the store dirty and is retried; resolution cannot release clients on a best-effort in-memory capture.

### CCS and targeted RPC split

Normal CCS `CustomSyncedValue<string>` carries recoverable global/public routing snapshots. It is not used as an event queue.

Targeted/custom RPC is used for:

- Defeated notification;
- enemy death reports;
- private participant detail and explicit resync;
- boss discovery;
- per-zone spawn claims, leases and reports;
- immutable projectile/AOE attribution transfer;
- source-owner damage authorization and target-owner actual-damage confirmation;
- Bloodlust healing grants;
- pre-Marked boss-offering authorization/relay;
- fade acknowledgement;
- durable outcome delivery/acknowledgement;
- outcome-presentation start/completion acknowledgement.

Resync is monotonic for same-event revisions, and private skill baselines are restored before new live-bonus reporting.

All Blood Moon routed-RPC handlers are registered once per `ZRoutedRpc` object. Runtime world teardown clears subsystem state without clearing the cached transport reference, because vanilla `ZRoutedRpc.Register` stores handlers with `Dictionary.Add`; a genuinely new transport is detected by object-reference change and registers normally.

### Presentation and environment

`Seasons_BloodMoon` is cloned from vanilla `Fader` rather than mutating the source environment. The required red-channel, wind and sun-angle changes are applied to the clone.

Forewarning/Marked overlays are transient around vanilla `EnvMan.SetEnv`; Active uses a force-environment lease. The cloned `Ashlands_FaderFX` keeps only the intended cloud objects and scales particle emission without altering the vanilla source object.

Random-event suppression, sleep blocking and boss-offering blocking are phase-scoped and released by resolution/world teardown.

Real-time-calendar worlds never feed calendar absolute seconds into Valheim net time. Ordinary worlds advance to the frozen morning target and explicitly broadcast the resulting net time.

### Groups and per-zone spawning

The server owns hidden combat groups, hysteresis, caps, group revisions and frozen spawn-pool identity. Actual extra-enemy spawning is performed only by the client that owns each relevant loaded zone/`SpawnSystem`.

Server validation covers:

- event ID;
- group and group revision;
- zone;
- owner/session;
- lease revision;
- lease expiry;
- group/server cap;
- immutable ZDO creator identity;
- exact frozen prefab identity;
- `MonsterAI` presence.

Received lease lifetime is rebased once to Unity `Time.realtimeSinceStartup`; OS wall-clock changes cannot extend or prematurely expire a client lease.

Surface and interior spawning remain separate. Interior extras use authored loaded `CreatureSpawner` positions without invoking their spawn bookkeeping. Mixed surface/interior zones preserve both candidate sets.

### Existing-monster behavior

Existing eligible hostile `MonsterAI` are identified dynamically. They receive runtime Blood behavior only; no persistent HuntPlayer/alert conversion marker, level mutation, max-health rewrite or shared-prefab mutation is applied.

AI target selection distinguishes surface/interior navigation context. Static-target pressure, flee/idle behavior and no-monster-area suppression are overridden only inside the Blood Moon runtime policy.

Existing monsters retain ordinary loot and survive event end normally if not killed.

### Combat routing and projectile lifecycle

Owner-side `Character.RPC_Damage` remains the final permission guard. Early direct-attack, projectile and AOE filters prevent forbidden hits from producing status, push, stagger or skill credit.

The projectile policy intentionally distinguishes forbidden characters from world geometry:

- a forbidden character collider is not a successful hit and does not stop the projectile, as required by the combat contract;
- world geometry still executes the vanilla hit/effect/attach/destroy lifecycle while Blood-source world damage and unrelated health-return side effects are suppressed.

World-object damage protection covers the current vanilla `IDestructible` implementations with `Damage(HitData)` verified in the game source, including build pieces, destructibles, rocks, trees, hit areas and Raven targets.

Stale event-attributed projectiles retain vanilla collision/destruction and normal permanent thrown-item respawn on hit or TTL. Damage, skill/adrenaline credit, callbacks and harmful spawned projectile/random-spawn branches are suppressed. A temporary Blood Craft `m_spawnItem` reaches the common `ItemDrop.DropItem` guard and cannot materialize in the world. Stale AOE is rejected.

### Actual-damage credit and Bloodlust

Combat progress and Bloodlust use earned `CombatPoints`, never auto-complete display progress. `GoalReached` pins earned factor to full strength while the participant remains combat-active.

Current provisional full-factor endpoints are server-controlled configs:

```text
outgoing damage: 1.25x
incoming damage: 0.75x
movement speed: 1.10x
lifesteal: 10% of actual Blood-enemy HP loss
lifesteal cap: 10% current max HP per rolling second
```

Movement is applied only around local walking/swimming calculations and original character fields are restored in postfix/finalizer paths.

Death credit is committed locally at the target owner when `Character.SetHealth` proves a positive HP loss, which occurs before the nested `CheckDeath` call on a lethal hit. This ensures the lethal source is available to the death-report path instead of being recorded after `RPC_Damage` has already returned.

The server does not trust the death reporter to choose the credited player. A permitted source-owner hit first creates a bounded authorization keyed by event, target ZDO, source ZDO, source type and player. The target owner separately confirms positive actual HP loss for the same key. The server accepts either network arrival order, matches the two records, and stores only the newest confirmed credit for that target. Enemy death is accepted only when the reporting target owner, raw Blood-enemy eligibility, observed dead state and exact server-confirmed credited player all agree. Exactly-once death acceptance consumes that confirmed credit.

Direct participant lifesteal uses the same matched authorization/confirmation path; there is no separate unbound actual-damage report RPC. The target owner still supplies the measured damage magnitude, but it cannot select an unrelated source/player. The server applies the rolling max-health-per-second cap before sending a monotonic targeted healing grant. Participant summons can contribute progress through immutable attribution, but direct lifesteal is limited to direct participant damage.

### Defeated and recovery

Local `Character.CheckDeath` remains the interception point before `Player.OnDeath`. Static verification confirms Player death state uses the ZDO `s_dead` flag, so `health <= 0` can be intercepted before vanilla death processing.

Defeated behavior:

- no grave, death point, ragdoll, respawn or vanilla skill loss;
- health/stamina/eitr restored to current maxima;
- food/adrenaline retained;
- only the explicitly allowed vanilla damaging DoT types are removed;
- stage 1 gives full incoming protection until stabilization, maximum 15 seconds;
- stage 2 lasts 10 seconds at 0.25 incoming damage;
- no transform/velocity reset;
- no re-entry.

A world/event-bound local Defeated marker survives process loss independently of the short recovery timer. The marker is written before recovery cleanup and an immediate vanilla `Game.SavePlayerProfile(false)` is requested after interception/synchronization. On reconnect it restores the local terminal state and retries the Defeated notification while the server still reports that participant as combat-active. A new authoritative `enroll` clears the old marker and requests another immediate profile save. Cloud durability is still subject to vanilla profile/cloud persistence semantics and is not overstated as stronger than the game API provides.

### Boss offerings and bosses

Boss-producing `OfferingBowl` interactions are blocked from Marked onward; item-producing bowls remain untouched.

The pre-18:00 in-flight boundary is now server-authoritative rather than owner-RPC-authoritative. During Forewarning, `InitiateSpawnBoss` sends a Blood Moon request to the server instead of directly invoking vanilla owner RPC. The server accepts it only while its own event state is still `Forewarning`, validates that the target ZDO is a boss-producing offering bowl, and relays the accepted request to the current bowl owner. The owner executes `RPC_SpawnBoss` inside a narrow authorized-completion scope, so a request accepted before Marked may complete after the Marked transition. A request first received by the server after Marked is not authorized and the normal guard remains closed.

This preserves the vanilla consumption ordering: inventory items or altar attachments are removed only after owner-side `RPC_SpawnBoss` accepts the spawn. An already accepted `DelayedSpawnBoss` is not cancelled by the Blood Moon transition.

Persistent outdoor bosses use marker-first far-sector parking on the same ZDO. The server takes ownership, writes a deterministic far position, zeros serialized velocity, force-sends and briefly reasserts authority. Restore returns the same persistent ZDO to its durable original position and clears markers last.

Interior/nonpersistent encounters withdraw only affected participants in the same navigation context.

### Blood Craft

Blood Craft uses runtime clones of already-known, enabled combat recipes without mutating source recipes. The source recipe name is stored in the temporary item marker, preventing output-identity ambiguity between recipes that produce the same prefab.

Temporary item identity is bound to schema, world UID, event ID, owner player ID and source recipe. Stack, transfer, container and world-consumer boundaries preserve the inventory-only invariant.

Source station semantics are retained for both creation and upgrade:

- resources are free;
- the original crafting/repair station type is required when the source recipe requires one;
- normal vanilla quality-to-station-level progression is preserved;
- permanent recipes/items do not become free upgrades.

Vanilla preflight is hardened for `m_requireOnlyOneIngredient`: `Recipe.GetAmount` is overridden only for the selected Blood Craft path so empty free-clone resources cannot dereference a missing ingredient before `DoCrafting`. Temporary-upgrade material requirement rows are hidden while station/quality requirements remain visible and enforced.

World materialization now has a common fail-safe at static `ItemDrop.DropItem`: marked temporary `ItemData` cannot become a retrievable world object. This complements `ItemDrop.Load` cleanup and the explicit world-consumer guards while still allowing normal combat use, including projectile/thrown-weapon behavior.

Temporary summons retain event/owner attribution and are removed by personal/global cleanup. Known persistent-world-spawner ability paths remain excluded.

### Skills, chronicle and durable outcomes

Live skill tracking observes actual `Player.RaiseSkill` calls and uses nonlinear level-equivalent progress. Server-side sequence/budget baselines prevent reconnect from resetting the +10 live-bonus budget.

Completion rewards preserve the configured +25 total budget, top-five contribution selection and +10 per-skill cap with no overflow redistribution.

Per-player outcomes are stored independently from the single current event state, so offline delivery survives later annual events. The outcome queue must be durably captured before current resolution starts delivery.

Dream/chronicle delivery does not intercept the next ordinary sleep. The current resolution outcome creates a dedicated presentation from the native `SleepText` UI, with its own black background and local input guard. Current timing is approximately 1 second fade-in, 3 seconds hold and 1 second fade-out.

Resolution has a separate outcome-presentation handshake:

- connected clients report presentation start/completion;
- the server keeps the original minimum presentation hold;
- late-starting presentation delays `ReleasingClients` until completion;
- missing start/completion uses bounded real-time fail-safe timeouts;
- offline participants do not block resolution;
- presentation acknowledgement is separate from durable profile-persistence acknowledgement.

The final outcome marker is written only after DreamText presentation completes. Server queue removal still waits for proven character persistence; cloud profile save success is not treated as durable proof when vanilla may have produced only a local recovery backup.

### Resolution transaction

The production controller performs:

1. enrollment freeze;
2. spawn lease freeze;
3. fade request and real-time ACK timeout;
4. Blood behavior disable;
5. validated extra cleanup;
6. boss restore;
7. temporary item/summon cleanup;
8. environment/random-event restore;
9. ordinary-world morning net-time advance when applicable;
10. durable outcome-queue capture;
11. outcome delivery and presentation start/completion wait with bounded fail-safe;
12. client/input release;
13. Resolved finalization.

Corrective self-patches for pre-combat cancellation, calendar time, net-time broadcast, defeat-report phase guard, recovery DoT policy, hostile attribution validation and enrollment-local reset were removed after their behavior was integrated into production paths. A stale patch targeting the no-longer-existing `ReplayResolvedOutcomes` method was removed with that consolidation.

## Static verification performed in this hardening pass

The latest manual pass re-read the relevant current game classes before changing their patches, including:

- `Character` death/movement/damage/`SetHealth` flow;
- `Player` death semantics and player ZDO identity;
- `Projectile` collision, damage, skill/adrenaline, spawn-item and TTL behavior;
- `ItemDrop.ItemData.Clone` and static `ItemDrop.DropItem` world materialization;
- current `IDestructible` implementations;
- `Recipe.GetAmount` and source station APIs;
- `InventoryGui.OnCraftPressed`, `SetupRequirementList`, `HideRequirement` and `DoCrafting`;
- `OfferingBowl` inventory/item-stand/RPC/delayed-spawn flow;
- `ZNetView` owner RPC routing;
- `ZNetScene.FindInstance` and prefab lookup;
- `ZRoutedRpc.Register` handler-table behavior;
- Player-profile persistence paths already documented by earlier review work.

A compile-oriented source audit also removed invalid relational comparisons between C# enum values, checked current Harmony target names/signatures against the implementation, and verified the new `ZPackage` float/vector/ZDOID read/write APIs against the same game-source commit. This is static source verification only; it is not a successful build claim.

## Runtime acceptance still required

Owner-side Valheim testing remains the release gate. The full matrix is in `08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md`; priority scenarios after this hardening are:

- annual single-player/listen/dedicated lifecycle;
- restart/reconnect in Active and every resolution step;
- Defeated followed by immediate process loss around profile persistence/server notification;
- recovery through protection expiry and later reconnect;
- direct/melee/projectile/AOE/summon combat with zero-damage and blocked hits;
- cross-peer lethal hits where source owner and target owner differ;
- Bloodlust endpoint behavior, matched actual-damage lifesteal and rolling HPS cap;
- active and stale projectile collision against terrain/build pieces/characters;
- permanent thrown-item respawn after event end and temporary thrown-item world rejection;
- mixed surface/interior groups and zone-owner migration;
- configured extra prefab changes across active-event restart;
- persistent boss parking and unsupported boss withdrawal;
- OfferingBowl immediately around 18:00, including server-accepted pre-Marked relay completing after Marked;
- Blood Craft `m_requireOnlyOneIngredient`, temporary upgrades, station levels, multi-craft and third-party inventories;
- DreamText immediate morning presentation under latency, disconnect and reconnect;
- local and cloud durable outcome acknowledgement;
- forced outcome-store write failure retaining `PublishingOutcomes` until persistence recovers;
- real-time-calendar mode and OS clock changes;
- 04:15 / 05:45 / 06:00 boundaries and fade/input release.

## Known limitations and deferred content

- Blood Moon music assets are not supplied; music work remains paused.
- Runtime wording is English-first until owner playtest stabilizes it.
- Automatic raid/trophy/key/achievement-derived enemy-pool progression remains the future contract in `06_FUTURE_PROGRESSION_AND_REWARDS.md`; the current event freezes the explicit configured bootstrap prefab.
- Third-party inventory/world-consumer systems that bypass vanilla guarded boundaries may require compatibility work after a concrete runtime report.
- Exact parked-boss animation/target/coroutine/HUD state is intentionally not reconstructed.
- Server-authorized OfferingBowl requests are intentionally defined by server receipt before the Marked cutover; a client click that has not reached the server before Marked is not considered queued.

## Continuation rule

PR #42 must remain draft and unmerged until explicit owner approval. Any runtime defect, accepted compatibility issue or changed design decision must be recorded in the repository before the next implementation step. Release metadata remains untouched until the owner explicitly requests release work.
