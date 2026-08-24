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

- dynamic global Blood Moon behavior on existing monsters;
- marked additional event spawning;
- target/damage isolation without personal visibility layers;
- `Defeated` without `Player.OnDeath`;
- far-sector outdoor boss parking/restore;
- ordinary interior and ship/ocean participation;
- interior spawn and interior-boss fallback.

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
seasons bloodmoon spike parkboss
seasons bloodmoon spike restoreboss
seasons bloodmoon spike cleanup
seasons bloodmoon spike dumpmonsters
seasons bloodmoon spike dumpbosses
```

Multiplayer-changing commands admin-only.

# 4. Spike A — dynamic existing monsters

Do not add conversion-origin ZDO marker to existing monsters.

While spike Active, test policy-based behavior on every loaded eligible existing MonsterAI:

- considered Blood enemy dynamically;
- Player-only target selection;
- no static/building/tamed/NPC/boss/monster targets;
- high aggression/no flee while target exists;
- event incoming/outgoing damage multipliers;
- optional temporary red VFX;
- ordinary loot and ragdoll remain;
- death removes the real monster normally;
- survivor remains and reverts when spike ends;
- no permanent max-health/shared-prefab mutation;
- owner migration/restart behavior;
- minimal cleanup of event-created target/alert/VFX state.

Build diagnostic eligibility table for:

- hostile ground monster;
- passive animal;
- tamed;
- boss;
- neutral/aggravated Dvergr;
- trader/named NPC;
- PlayerSpawned summon;
- fish/bird;
- starred creature;
- modded MonsterAI.

Record inclusion/exclusion reason.

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
- cleanup uses a copied collection and never deletes ordinary ZDO.

# 6. Spike C — target and damage matrix

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
- trap/turret;
- environmental damage to Blood enemy;
- delayed attribution;
- GoalReached target priority;
- terminal Player body blocking remains vanilla.

# 7. Spike D — Defeated and recovery

Patch owner-side `Character.CheckDeath` only for local Player in `Fighting`/`GoalReached`.

Prove:

- no `Player.OnDeath`;
- no death point/effects/ragdoll/TombStone/respawn;
- no inventory/equipment/food changes;
- no transform/parent/velocity changes;
- exactly one `Defeated`.

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

Test grounded, fall, >15s air, swimming, attached, mounted, lava and direct forced `Player.OnDeath`.

No vanilla `SoftDeath` status. Use temporary custom debug status text.

# 8. Spike E — contexts and interior spawn

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
- continue Fighting after destination loads;
- edge check still applies.

## Ordinary interior/dungeon

- Player remains `Fighting`;
- existing dungeon monsters qualify normally;
- test additional spawn without `Heightmap/GetGroundData`:
  - candidate around Player;
  - `Pathfinding.FindValidPoint` using prefab agent type;
  - require full path to Player;
  - preserve same interior/high-Y context;
  - no spawn if no valid point.

Record whether vanilla `CreatureSpawner` positions or registered dungeon spawners can improve candidate selection without mutating their state.

## Interior boss

Test Queen or another interior boss and report the minimal safe policy:

- do not far-park while `Character.InInterior()`;
- compare group/Player Boss Encounter Hold versus skipping Blood Moon for affected Player;
- ordinary boss fight must not receive free Blood Craft damage, Bloodlust advantage or safe `Defeated` promise;
- after boss encounter ends before 05:45, determine whether event participation can start/resume cleanly.

Do not invent a personal visibility layer.

# 9. Spike F — OfferingBowl block

From 18:00-equivalent debug state:

- block `OfferingBowl.UseItem` when `m_bossPrefab != null`;
- block item-stand `OfferingBowl.Interact`;
- guard `OfferingBowl.RPC_SpawnBoss` authoritatively;
- do not block item-producing bowls;
- do not consume inventory items or existing item-stand attachments;
- test a spawn queued before the block: allow it to complete and park boss if Active.

# 10. Spike G — far-sector boss parking

Far-sector parking is the primary candidate. Do not implement in-place stasis unless parking fails for a documented reason.

Test at least one vanilla persistent outdoor boss.

## Parking transaction

1. detect loaded alive outdoor boss;
2. write event marker and original position;
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
- restore original position;
- force sync;
- clear marker last;
- verify instance recreation when arena is active;
- optional short AI grace/appearance effect;
- idempotent after simulated crash at each transaction step.

## Required cases

- owner participant/other peer/server;
- old owner attempts another transform update;
- multiple bosses;
- restore with no nearby Player;
- restart while parked;
- stale marker outside active event;
- boss dies during transition;
- persistent vanilla boss;
- nonpersistent modded boss;
- boss loaded after Active begins;
- interior boss excluded;
- HUD/music/forced event clear after unload and resume after restore;
- projectiles/AOE/summons left in arena;
- boss minions becoming ordinary Blood enemies.

For nonpersistent boss compare:

- temporary `Persistent=true` with original flag;
- encounter hold/fallback.

Do not declare generic modded support without evidence that custom runtime-only state survives unload/recreate.

# 11. Evidence report

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
- damage matrix;
- Defeated/recovery results;
- mounted/ship/interior results;
- interior spawn findings;
- OfferingBowl race results;
- boss parking ownership/persistence/restart results;
- interior/nonpersistent boss recommendation;
- CPU/network observations;
- rejected approaches;
- minimal production patch set;
- required doc updates;
- whether spike code should be deleted or promoted.

Run Codex review on draft PR. Do not merge before owner runtime review.

# 12. Acceptance gate

Answer with runtime evidence:

1. Exact eligibility predicate?
2. Can existing monsters use dynamic Blood behavior without persistent mutation?
3. Can marked extras alone lose loot and be safely deleted?
4. Can target/damage routing remain centralized?
5. Can CheckDeath produce Defeated without vanilla death side effects?
6. Does 15s + 10s recovery work?
7. Do mounted/attached/ship/ocean remain functional?
8. Can additional enemies spawn in interiors through navmesh/path checks?
9. Does OfferingBowl block avoid item loss and races?
10. Can far-sector parking preserve vanilla boss state across owner migration/restart?
11. What fallback is required for interior/nonpersistent/modded boss?
12. What minimal production architecture is justified?
