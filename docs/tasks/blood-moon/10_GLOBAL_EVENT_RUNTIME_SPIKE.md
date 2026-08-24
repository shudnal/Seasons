# Blood Moon — isolated global-event runtime spike

This is the only implementation task currently permitted by the Blood Moon design set.

# 0. Branch and scope

Work only in:

```text
spike/blood-moon-global-event
```

Base: current `feat/blood-moon`.

Draft PR only:

```text
spike/blood-moon-global-event → feat/blood-moon
```

Do not implement calendar/rewards/Blood Craft/full production networking. Do not change version, README, changelog or package. All behavior disabled by default and enabled only through debug/admin commands.

Read current game methods from `shudnal/assemblies_combined` before patching.

# 1. Goal

Determine the minimal reliable architecture for:

- dynamic global Blood Moon behavior on existing monsters without persistent mutation;
- marked additional event spawning;
- centralized target/damage/projectile routing;
- `Defeated` without `Player.OnDeath`;
- server-authoritative far-sector parking/restore of persistent outdoor bosses;
- boss discovery when server lacks a live instance;
- ordinary interior/ship/ocean/mounted participation;
- optional interior extras from loaded `CreatureSpawner` points;
- terminal `Withdrawn` for interior/nonpersistent/unparkable boss encounter.

# 2. Temporary model

```csharp
internal enum BloodMoonSpikeParticipantPhase
{
    None,
    Fighting,
    GoalReached,
    Exited
}

internal enum BloodMoonSpikeOutcome
{
    None,
    Success,
    Defeated,
    Withdrawn
}
```

Keep an independent `GoalReached` flag/record so the spike can test Success followed by later personal exit.

One central policy:

```text
IsEligibleExistingMonster
IsBloodEnemy
IsBloodMoonSpawned
IsActiveParticipant
CanTarget
CanDamage
CanReceiveProgress
```

Markers:

```text
Seasons.BloodMoon.SpawnedEventId
Seasons.BloodMoon.GroupId
Seasons.BloodMoon.ParkedEventId
Seasons.BloodMoon.ParkingSchema
Seasons.BloodMoon.OriginalPosition
```

# 3. Debug controls

Minimum:

```text
seasons bloodmoon spike status
seasons bloodmoon spike reset
seasons bloodmoon spike activate
seasons bloodmoon spike spawn <prefab>
seasons bloodmoon spike defeat
seasons bloodmoon spike withdraw
seasons bloodmoon spike goalreached
seasons bloodmoon spike parkboss
seasons bloodmoon spike restoreboss
seasons bloodmoon spike cleanup
seasons bloodmoon spike dumpmonsters
seasons bloodmoon spike dumpbosses
seasons bloodmoon spike set <config> <value>
```

Multiplayer-changing commands admin-only.

# 4. Spike A — generic dynamic existing monsters

Do not add conversion-origin ZDO marker to existing monsters.

Expected predicate:

```text
alive/valid Character
has MonsterAI
not boss
not tamed
faction not Players/PlayerSpawned/TrainingDummy
BaseAI.IsEnemy(monster, at least one Fighting/GoalReached Player)
```

While spike Active, prove policy-based behavior:

- Player-only target selection;
- no static/building/tamed/NPC/boss/monster targets;
- high aggression/no flee while valid target exists;
- event incoming/outgoing damage multipliers;
- optional temporary red VFX;
- ordinary loot and ragdoll remain;
- death removes the real monster normally;
- survivor remains and reverts when spike ends;
- no permanent max-health/shared-prefab mutation;
- no `SetHuntPlayer`, event alert/hunt ZDO writes or restore ledger;
- owner migration/restart behavior;
- cleanup limited to VFX/transient caches.

Diagnostic table must include:

- hostile ground monster;
- passive animal;
- tamed;
- boss;
- neutral/aggravated Dvergr;
- `Players`/`PlayerSpawned`/`TrainingDummy`;
- unique/starred creature;
- hostile modded MonsterAI;
- newly loaded ordinary monster during Active.

Record exact inclusion/exclusion reason and any case where vanilla `BaseAI.IsEnemy` is insufficient.

# 5. Spike B — marked additional spawns

Spawn one configured prefab through a custom spike spawner.

Write marker before combat participation:

```text
SpawnedEventId
GroupId
```

Prove:

- marked enemy uses same Blood behavior;
- ordinary loot is suppressed only for marked extra;
- existing unmarked Blood enemy still drops ordinary loot;
- marked ragdoll cleanup works;
- resolution deletes all surviving marked ZDO server-side;
- stale marked ZDO is deleted on simulated restart/recovery;
- cleanup uses a copied collection and never deletes ordinary ZDO;
- lowering live cap does not delete existing marked extras, only blocks new spawn.

# 6. Spike C — target, damage and attribution

Blood enemy:

- targets only `Fighting`/`GoalReached` Player;
- never static target/building/tamed/NPC/boss/other monster;
- ignores `Exited`;
- no flee while valid target exists.

Participant attack:

- damages only current-event Blood enemy;
- no building/tree/ore/crop/tamed/boss/ordinary object damage;
- forbidden target receives no status/stagger/skill credit.

Test:

- melee;
- projectile;
- thrown;
- AoE;
- combat summon;
- trap/turret;
- environmental damage to Blood enemy;
- delayed attribution after equipment/phase/owner change;
- GoalReached target priority;
- terminal Player body blocking remains vanilla.

Determine the minimum attribution needed beyond `HitData.m_attacker`. Do not implement personal visibility/collision code.

# 7. Spike D — Defeated and recovery

Patch owner-side `Character.CheckDeath` only for local Player in `Fighting`/`GoalReached`.

Prove:

- no `Player.OnDeath`;
- no death point/effects/ragdoll/TombStone/respawn;
- no inventory/equipment/food changes;
- no transform/parent/velocity changes;
- exactly one `Defeated` report/transition.

Restore:

```text
Health max
Stamina max
Eitr max
Food unchanged
Adrenaline unchanged
```

Remove only:

- `SE_Burning`;
- `SE_Poison`;
- `SE_Smoke`;
- negative-tick `SE_Stats`.

Recovery:

```text
Stage 1: immunity until IsOnGround || IsSwimming || IsAttached, max 15s
Stage 2: 10s, incoming multiplier 0.25
```

Test grounded, fall, >15s air, swimming, attached, mounted, lava, morning resolution during protection, disconnect/reconnect and direct forced `Player.OnDeath`.

No vanilla `SoftDeath` status. Use temporary custom debug status text.

Also test:

```text
GoalReached → Defeated
GoalReached → Withdrawn
GoalReached → disconnect
```

Report whether `GoalReached=true` can remain authoritative while later exit reason is stored separately.

# 8. Spike E — contexts and interior extras

## Mounted/attached

- no forced detach;
- Blood enemy targets Player, not tamed mount;
- tamed mount takes no damage;
- Player can become `Defeated` in place.

## Ship/ocean

- Player remains `Fighting`;
- land spawner may fail naturally;
- existing sea MonsterAI can qualify;
- no forced movement or withdrawal merely for being at sea.

## Teleport

- temporarily pause spawn-anchor use;
- continue `Fighting` after destination loads;
- edge check still applies.

## Ordinary interior/dungeon

- Player remains `Fighting`;
- existing dungeon monsters qualify normally;
- extras use only loaded `CreatureSpawner` authored positions;
- filter to same interior/location context;
- do not call `CreatureSpawner.Spawn()` or change its ZDO connections;
- require a full path from candidate to Player;
- a small navmesh snap around candidate is allowed;
- do not create random navmesh-point fallback;
- no valid candidate means no extra spawn.

Record which crypt/cave/mine/dungeon layouts provide useful candidates and whether enemies can naturally approach from another room.

## Interior or unparkable boss

Accepted product policy:

- do not park interior boss;
- do not park nonpersistent boss;
- affected Player gets terminal `Withdrawn`;
- remove Bloodlust/Blood Craft personal state;
- ordinary boss fight remains vanilla;
- no re-entry that night;
- other Players/groups continue Blood Moon.

Test minimal detection:

- client/owner observes boss HUD/live Character and reports ZDOID/`InInterior`;
- server validates prefab/`IsBoss`/`Persistent`/position;
- false/stale/duplicate report does not withdraw unrelated Player;
- Player approaching an unparkable boss during Active is withdrawn before Blood Craft/Bloodlust can affect boss combat.

# 9. Spike F — OfferingBowl block

From 18:00-equivalent debug state:

- block `OfferingBowl.UseItem` when `m_bossPrefab != null`;
- block item-stand `OfferingBowl.Interact`;
- guard `OfferingBowl.RPC_SpawnBoss` authoritatively;
- do not block item-producing bowls;
- do not consume inventory items or existing item-stand attachments;
- queued pre-18 spawn completes;
- if resulting boss is persistent/outdoor during Active, park it;
- otherwise affected encounter Player is withdrawn when the boss becomes relevant.

# 10. Spike G — far-sector persistent outdoor boss parking

Far-sector parking is the accepted production direction. Do not implement in-place stasis or temporary persistence for nonpersistent bosses.

Test at least one vanilla persistent outdoor boss.

## Discovery

Test both:

- server has live boss instance;
- boss is client-owned and server only receives validated ZDOID report.

Server alone decides eligibility and parking slot.

## Parking transaction

1. detect alive persistent outdoor boss;
2. write `ParkedEventId`, schema and `OriginalPosition` first;
3. take server ZDO ownership;
4. if server has live instance, move transform/rigidbody consistently and zero velocities;
5. move ZDO to deterministic reserved far XZ sector with normal finite Y;
6. force/observe sector invalidation and client unload;
7. do not change rotation;
8. do not restore old peer owner.

Never use `y < -5000` because `ZSyncTransform` rescues such objects.

## Restore

- scan all ZDO with marker, including unloaded boss;
- take server ownership;
- restore `OriginalPosition`;
- force sync/sector invalidation;
- clear marker last;
- verify valid instance recreation when arena becomes active;
- idempotent after simulated crash at each transaction step.

Correctness does not require exact restoration of animation, target, velocity, coroutine, HUD or runtime-only component state.

## Required cases

- owner participant/other peer/server;
- old owner attempts another transform update;
- multiple bosses/separate slots;
- restore with no nearby Player;
- restart while parked;
- stale marker outside active event;
- boss dies during transition;
- persistent vanilla boss;
- boss loaded after Active begins;
- interior boss excluded;
- nonpersistent boss excluded without changing `Persistent`;
- HUD/music/forced event clear after unload and resume naturally after restore;
- lingering projectile/AOE/summon remains and completes own lifecycle;
- boss minion qualifying as ordinary Blood enemy.

# 11. Spike H — live balance config

Without implementing the final public config surface, prove runtime application semantics:

- incoming/outgoing multiplier changes affect next hit;
- speed/aggression changes affect next AI update;
- spawn interval/radius changes affect next tick;
- group distance changes trigger recompute;
- raised cap allows new spawn;
- lowered cap does not delete live extras;
- changed spawn prefab/weight affects future spawns only;
- current event schedule does not change.

# 12. Evidence report

Commit:

```text
docs/tasks/blood-moon/SPIKE_GLOBAL_EVENT_RESULTS.md
```

Include:

- Seasons base commit;
- `assemblies_combined` commit;
- exact patches tried;
- single/listen/dedicated results;
- eligibility table;
- existing-vs-extra loot/cleanup evidence;
- damage/attribution matrix;
- Defeated/recovery/post-goal results;
- mounted/ship/interior results;
- CreatureSpawner interior findings;
- OfferingBowl race results;
- boss discovery/parking ownership/persistence/restart results;
- nonpersistent/interior boss Withdrawn results;
- live config behavior;
- CPU/network observations;
- rejected approaches;
- minimal production patch set;
- CCS/RPC recommendation;
- required doc updates;
- whether spike code should be deleted or promoted.

Run Codex review on draft PR. Do not merge before owner runtime review.

# 13. Acceptance gate

Answer with runtime evidence:

1. Does generic eligibility work without prefab allowlists?
2. Can existing monsters use dynamic Blood behavior without persistent mutation?
3. Can marked extras alone lose loot and be safely deleted?
4. Can target/damage/projectile routing remain centralized?
5. Can `CheckDeath` produce `Defeated` without vanilla death side effects?
6. Does 15s + 10s recovery work across morning/reconnect?
7. Do mounted/attached/ship/ocean/interior remain functional?
8. Are loaded `CreatureSpawner` positions sufficient for optional interior extras?
9. Does OfferingBowl block avoid item loss and queued-spawn races?
10. Can server discover and far-park persistent outdoor boss across owner migration/restart?
11. Are interior/nonpersistent boss encounters withdrawn cleanly without affecting other groups?
12. Do live config changes have deterministic semantics?
13. Can Success and later exit reason coexist in the participant model?
14. What minimal production architecture and CCS/RPC split are justified?
