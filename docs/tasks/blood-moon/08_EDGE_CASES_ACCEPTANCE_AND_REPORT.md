# Blood Moon — runtime acceptance, edge cases and implementation report

This is the owner-side runtime acceptance checklist for the current Blood Moon implementation. It must be read together with `25_PROJECT_CONFORMANCE_MATRIX.md` and the precedence rules in `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

A successful static/Codex review does not replace these runtime checks.

## 1. Lifecycle, persistence and clock control

Verify:

- feature disabled;
- single-player;
- listen server;
- dedicated server;
- first enable before the forewarning window;
- first enable inside the current event window -> current annual event is skipped;
- restart in Forewarning;
- restart in Marked;
- restart in Active;
- restart in AutoCompleting;
- restart at every persisted Resolving step;
- empty server at 23:00;
- late join/disconnect/reconnect;
- stale CCS revision;
- stale/duplicate RPC;
- duplicate enemy death report;
- cleanup twice;
- config disable mid-event;
- world unload/reload in every phase;
- same-process switch to another world and back.

### `skiptime` / time changes

Verify independently:

- Forewarning -> Marked by forward time;
- Marked -> Active by forward time;
- Active -> AutoCompleting by forward time;
- one large forward jump from Forewarning into Active;
- one large forward jump from Forewarning past 05:45;
- one large forward jump from Forewarning past 06:00;
- required Marked/Active/AutoCompleting transition side effects occur before normal resolution after a large jump;
- jump past forced end uses the normal Resolving transaction rather than direct `Resolved` assignment;
- backward time does not move the event to an earlier gameplay phase;
- backward time does not recreate killed creatures, temporary items, terminal participation, skill XP or old annual events;
- repeated rewind across an earlier autumn does not replay an event whose ID is below the stored high-water marks;
- `seasons bloodmoon start marked` retains a preparation interval before Active;
- trying to administratively start an earlier live phase is rejected until explicit cleanup;
- debug cleanup/start does not make the same world-day event an independent fresh reward lifecycle.

## 2. CCS/RPC/session lifecycle

Verify:

- late join receives complete current public state;
- private participant detail arrives/restores correctly;
- equal current-state snapshots do not create harmful duplicate processing;
- participant transition updates public routing state;
- no `SequencedCustomSyncedValue` dependency exists;
- targeted progress/private detail is not broadcast as public state;
- Defeated notification can only terminal-exit the sender's participant identity;
- same-event resync revisions remain monotonic;
- a stale previous-event resync does not replace a newer event;
- fade/action messages are scoped to the matching event;
- session/world teardown clears event runtime state without duplicate routed-RPC registration on a still-live transport;
- reconnect restores skill-report baselines before further reporting.

## 3. Presentation and system suppression

Verify:

- forewarning has a non-text visual effect;
- Marked begins at 18:00;
- Active begins at 23:00;
- forced end begins at 05:45;
- seasonal luminance -> Blood overlay order is visually correct;
- forced-environment lease behaves correctly when another mod changes force environment during Active;
- missing `Fader` fallback does not crash;
- missing `Ashlands_FaderFX` fallback does not crash;
- cloned cloud cleanup works across environment switching;
- current RandEvent is stopped at Marked;
- new ordinary RandEvent is blocked while suppression is owned;
- RandEvent starts work again after resolution/world teardown;
- sleep is blocked for enrolled Marked/active participants and restored after terminal/resolution state;
- no vanilla `SoftDeath` behavior/icon is introduced;
- ordinary number-row hotkeys remain functional during resolution fade;
- mod-defined hotkeys reading `ZInput` remain functional during resolution fade;
- Blood Moon has no global `Player.TakeInput` suppression.

### DreamText — current authoritative policy

Per `22_DREAMTEXT_AND_INPUT_POLICY.md`:

- resolution does **not** force an immediate DreamText screen;
- resolution completion does not wait for the player to sleep;
- outcome stores a profile-backed pending Blood Moon dream;
- the next ordinary vanilla sleep uses normal `SleepText` timing;
- one pending Blood Moon dream replaces that sleep's random dream;
- with several pending Blood Moon dreams, oldest event is consumed first and only one is consumed per sleep;
- presented dream is not repeated;
- with no pending Blood Moon dream, ordinary vanilla random dream behavior is unchanged;
- disconnect/reconnect does not lose a pending durable dream.

## 4. Existing monsters

Verify:

- hostile ground `MonsterAI`;
- passive animal;
- tamed creature;
- boss;
- neutral Dvergr;
- aggravated Dvergr;
- Players/PlayerSpawned/TrainingDummy faction;
- starred/unique hostile;
- modded `MonsterAI` using ordinary faction semantics;
- newly loaded/spawned ordinary monster during Active;
- monster owner migration;
- existing survivor after event;
- killed existing monster keeps ordinary loot/ragdoll;
- no max-health/shared-prefab mutation;
- no persistent hunt/alert ZDO mutation;
- no stale event VFX/target cache after event end;
- outside Blood Moon, ordinary creature loot/ragdoll remains completely normal.

## 5. Additional spawning and zone ownership

There is **no group-wide spawn coordinator**. Every relevant loaded zone is serviced only by its current zone/`SpawnSystem` owner.

Verify:

- multiple relevant zones owned by different peers in one hidden combat group;
- each peer spawns only for its owned zones;
- event/group/role/original-zone markers exist before extra participation;
- extras have no ordinary loot;
- extra ragdoll is short-lived;
- surviving marked extra ZDO is deleted at resolution;
- stale marked extra is deleted after restart/recovery;
- deletion iterates copied collections;
- ordinary unmarked ZDO is never deleted as an event extra;
- raised cap permits future spawns;
- lowered cap does not delete already-live extras;
- zone owner disconnect/migration invalidates/reissues only the affected zone lease;
- stale old-owner/old-revision report is rejected;
- same-revision lease renewal does not replenish already-spent allowance;
- a genuinely newer lease revision can grant new allowance;
- duplicate pending report does not spend a second token;
- report arriving before ZDO/custom-marker replication waits within the bounded observation window;
- extra can cross a sector boundary before report acceptance and remains valid based on immutable spawn-zone provenance;
- surface spawn works;
- ship/ocean participant may have no valid land extra and remains Fighting;
- interior candidate comes only from a loaded authored `CreatureSpawner` position;
- no valid interior candidate -> no fallback random interior spawn;
- one map zone containing both surface and interior participants preserves both contexts.

## 6. Damage, AI, projectile and AOE

Verify:

- Blood enemy targets only Fighting/GoalReached participants;
- GoalReached is lower target priority while an unfinished Fighting target exists;
- no static target/building pressure;
- no tamed/NPC/boss/other-monster target;
- no ordinary flee/idle while a valid Blood target is available;
- NoMonsterArea/base avoidance does not cancel Blood pressure;
- ordinary physical hazard behavior is not unintentionally broken;
- participant melee/projectile/thrown/AOE only damages current Blood enemies;
- forbidden target gets no damage/status/stagger/push/skill credit/aggravation;
- traps/turrets/environment do not farm Blood enemies;
- immutable projectile/AOE `eventId` survives equipment/phase changes;
- participant exit after projectile creation: delayed damage may still land while globally valid, but no progress/skill credit is granted;
- after global event end: stale event-attributed damage is blocked.

### Projectile collision split

Verify separately:

- forbidden **Character** collider is not a successful hit and does not stop the Blood projectile;
- terrain stops/handles projectile through vanilla lifecycle;
- building/wall physically stops/handles projectile but receives no Blood damage;
- tree/rock/resource physically handles collision but receives no Blood damage;
- vanilla hit/effect/attach/destroy behavior remains for world geometry;
- stale projectile after event end still has ordinary physical collision;
- permanent thrown item can respawn through its normal stale hit/TTL lifecycle;
- temporary Blood Craft thrown item cannot become a retrievable world item;
- stale projectile cannot damage an optional third-party `IDestructible.Damage(HitData)` implementation;
- projectile/AOE nested effects restore the caller's Blood attack context exactly once.

## 7. Progress and Bloodlust

Verify:

- direct/remote lethal hit credits the player whose latest matched positive actual HP loss is confirmed;
- blocked/resisted/zero-damage hit does not steal final death credit;
- authorization and target-owner actual-damage confirmation can arrive in either network order;
- duplicate death report is exactly-once;
- server computes enemy point value rather than accepting client points;
- current progress sharing uses the credited source player's position plus current hidden-group membership and configured share radius;
- auto-complete display progress is separate from earned `CombatPoints`;
- 0% Bloodlust -> neutral Player endpoint modifiers;
- 50% -> interpolated values;
- 100% -> full configured values;
- `GoalReached` keeps full earned Bloodlust while participant remains active;
- auto display floor alone does not increase Bloodlust;
- outgoing modifier applies only participant -> Blood enemy;
- incoming reduction applies only Blood enemy -> participant;
- movement modifier works for walk/run/crouch/swim and restores original fields;
- another Harmony postfix modifying movement is not overwritten by a second Blood Moon finalizer restore;
- lifesteal uses confirmed actual HP loss, not nominal damage;
- overkill does not heal for more than actual lost HP;
- AOE/multi-hit obeys the rolling max-health-per-second cap;
- no lifesteal after participant exit/global combat end.

## 8. Defeated/recovery

Verify:

- direct Blood hit;
- fall after knockback;
- environmental HP loss;
- lava;
- drowning;
- grounded;
- airborne longer than 15 seconds;
- swimming;
- attached;
- mounted;
- accepted damaging vanilla DoT classes;
- unknown modded DoT remains;
- no `Player.OnDeath`;
- no TombStone/death point/ragdoll/respawn;
- health/stamina/eitr restored fully;
- food/adrenaline unchanged;
- no transform/velocity reset;
- Stage 1 <= 15 seconds;
- Stage 2 exactly 10 seconds at incoming multiplier `0.25`;
- morning/resolution can occur while local recovery continues;
- disconnect/reconnect during recovery;
- repeated Defeated acknowledgement does not heal/cleanse/restart protection a second time;
- direct forced `Player.OnDeath` remains vanilla;
- while Resolving freezes Blood combat, unrelated ordinary vanilla skill gain (for example Run/Jump) is not globally blocked.

## 9. GoalReached + later exit

Verify:

```text
GoalReached -> normal morning
GoalReached -> Defeated
GoalReached -> Withdrawn
GoalReached -> disconnect
```

In every case:

- `GoalReached=true` remains;
- completion entitlement/reward remains;
- `ExitReason` records the later fact separately;
- chronicle and eventual DreamText can represent both facts.

## 10. Boss offering and parking

### OfferingBowl

Verify:

- inventory boss offering blocked at/after Marked;
- item-stand boss altar blocked at/after Marked;
- item-producing bowl remains vanilla;
- blocked offering does not consume items/attachments;
- request first received by server before the Marked cutoff may complete after Marked;
- request first received by server after Marked is rejected;
- ownership migration after server acceptance can relay the accepted request to the current bowl owner;
- already accepted vanilla delayed spawn is not cancelled by later Blood Moon transition.

### Parking

Verify:

- persistent outdoor vanilla boss;
- boss owner client/server/listen host;
- owner revision propagation;
- queued old-owner higher-data-revision race;
- ForceSend/sector invalidation;
- multiple bosses/deterministic slots;
- finite far XZ position, never the `< -5000` rescue path;
- restore with no Player nearby;
- restart while parked;
- stale marker;
- crash after marker/before move;
- crash after restore/before marker clear;
- same persistent ZDO returns with health/level/ZDO data;
- exact animation/target/coroutine/HUD restoration is not required.

### Unsupported boss

Verify:

- interior boss;
- nonpersistent surface boss;
- only affected participants in the same surface/interior and same loaded interior Location context are Withdrawn;
- nearby surface player is not withdrawn by an interior boss;
- unrelated combat groups remain active;
- unsupported boss fight remains vanilla;
- no temporary `Persistent=true` mutation.

## 11. Blood Craft

Current accepted meaning:

```text
free requirements = free material requirements
source crafting/repair station and required station level remain required
```

Verify:

- only known enabled eligible recipes;
- custom tabs untouched;
- runtime clone identity;
- no shared source `Recipe` mutation;
- original permanent recipe remains available;
- crafting material requirements are free;
- source station type is still required;
- source station level/quality progression is still required;
- temporary upgrade materials are free but station/quality rules remain;
- permanent item upgrade is not free;
- temporary marker contains valid schema/world/event/owner/source-recipe identity;
- temporary/permanent stack never merges;
- different world/event/owner/source recipe never merges;
- ordinary inventory drop is blocked for temporary item;
- external inventory/world sink is blocked;
- `ItemStand`, `ArmorStand`, `Fermenter`, `CookingStation`, `Smelter`, `Turret`, `Catapult` reject temporary item;
- stale load cleanup;
- reconnect/restart preserves valid temporary inventory and removes stale markers;
- custom equipment slots are cleaned where supported;
- Defeated/Withdrawn personal cleanup;
- consumed food/mead effect may remain;
- projectile/AOE attribution remains correct;
- temporary summon cleanup;
- `m_requireOnlyOneIngredient` source recipe does not fail because the free clone has no material rows;
- multi-craft near inventory/stack limits behaves safely.

## 12. Skills and rewards

Verify:

- configured aliases parsed;
- invalid aliases logged/ignored;
- only allowed combat skills by default;
- live bonus follows actual `Player.RaiseSkill` calls;
- reconnect restores live bonus/sequence baseline;
- live x3/bonus pool stops at configured +10 level-equivalent cap;
- completion top five selected by base contribution;
- completion +25 total budget;
- per-skill +10 completion cap;
- no overflow redistribution;
- partial reward scaling excludes automatic display completion;
- Hybrid limits remain correct across level boundaries;
- Blocking/BloodMagic/ElementalMagic work when actual corresponding skill raises occur;
- unrelated ordinary skill gain remains vanilla when Blood Moon accounting is inactive/frozen.

## 13. Outcomes and durable queue

Verify:

- chronicle is world/event-scoped and idempotent;
- Rested removal occurs once for the outcome path where intended;
- completion/live reward replay is idempotent;
- offline outcome survives reconnect;
- offline outcome survives a later annual Blood Moon state replacing the current event;
- outcome queue capture failure keeps resolution in `PublishingOutcomes` until persistence succeeds;
- local profile acknowledgement removes server queue entry only after successful durable application;
- cloud profile behavior follows the documented conservative persistence policy;
- duplicate delivery does not duplicate reward/chronicle/pending dream;
- pending DreamText is not a resolution dependency.

## 14. Marketplace 9.9.4/minimap

Using the verified 9.9.4 contract from `24_MARKETPLACE_9_9_4_COMPATIBILITY.md`, verify:

- winter map generation;
- normal -> winter -> normal season changes;
- disabling/re-enabling seasonal minimap control while winter is active;
- manual/vanilla map regeneration;
- Marketplace territory overlay redraw after a Seasons terrain-color update;
- `originalMapColors` and `originalHeightColors` are both ready before Marketplace `DoMapMagic` executes;
- no `MarketplaceCompat.UpdateMap` `NullReferenceException`;
- custom/modded biome colors are retained;
- world exit during seasonal map generation does not apply old-world results into a new minimap;
- another later normal map update can retry after a transient compatibility failure.

## 15. Build/review report

Final report must contain:

- branch and exact head;
- changed files / conformance-audit fixes;
- architecture/conformance summary;
- exact `assemblies_combined` source checkpoint used;
- owner build result for the post-audit head;
- scenarios actually run by the owner;
- untested multiplayer/VFX cases explicitly marked;
- compatibility findings;
- known limitations/future-by-design items;
- final complete project-conformance Codex review result;
- exact continuation point.

## 16. Definition of ready for the next owner playtest

The implementation is statically ready for the next owner playtest when:

- complete end-to-end lifecycle remains implemented;
- server/client state and recovery paths are present;
- debug commands can exercise phases and diagnostics;
- no personal-layer code exists;
- no material event reward system exists;
- existing ordinary loot and event-extra no-loot remain distinct;
- Defeated and boss parking remain idempotent;
- current project-conformance matrix contains no unresolved confirmed code defect;
- project documentation precedence is unambiguous;
- final exact-head Codex review explicitly reviews the full PR against the project contract/matrix;
- PR remains draft/open/unmerged.

Owner-side build and runtime validation are still required after source changes made by the conformance audit.
