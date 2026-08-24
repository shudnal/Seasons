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

- global Blood Moon monster activation;
- additional event spawning;
- target/damage isolation without personal visibility layers;
- `Defeated` without `Player.OnDeath`;
- ordinary-monster world preservation;
- active boss suspension/restore;
- mounted/attached and unsupported contexts.

A failed candidate is valid evidence.

# 2. Temporary model

```csharp
internal enum BloodMoonSpikeParticipantPhase
{
    None,
    Deferred,
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

internal enum BloodMoonSpikeOrigin
{
    ConvertedExisting,
    CloneOfSuspendedOriginal,
    Spawned,
    SuspendedOriginal,
    SuspendedBoss
}
```

One central policy:

```text
IsBloodEnemy
IsActiveParticipant
CanTarget
CanDamage
CanReceiveProgress
```

Temporary ZDO markers include eventId/origin/originalZDOID.

# 3. Debug controls

Minimum:

```text
seasons bloodmoon spike status
seasons bloodmoon spike reset
seasons bloodmoon spike activate
seasons bloodmoon spike convert <radius>
seasons bloodmoon spike mode <direct|clone>
seasons bloodmoon spike spawn <prefab>
seasons bloodmoon spike defeat
seasons bloodmoon spike withdraw
seasons bloodmoon spike suspendboss
seasons bloodmoon spike restoreboss
seasons bloodmoon spike cleanup
seasons bloodmoon spike dump
```

Multiplayer-changing commands admin-only.

# 4. Spike A — direct conversion

Test an existing ordinary hostile MonsterAI:

- mark as Blood enemy;
- force aggressive Player-only target selection;
- lower effective health through incoming damage multiplier;
- lower outgoing damage through multiplier;
- no building/tamed/NPC/boss/ordinary damage;
- no loot;
- short ragdoll;
- survivor cleanup;
- owner migration;
- restart marker recovery.

Record consequences:

- death removes real entity;
- position/health changed;
- starred/modded behavior;
- whether this tradeoff is acceptable.

# 5. Spike B — suspend original + blood clone

For the same existing creature:

1. mark original suspended;
2. globally hide/suspend it identically for all clients;
3. do not change owner;
4. disable AI/combat/collision/physics only as needed;
5. spawn blood clone at same position/rotation/level;
6. kill/delete clone;
7. restore original exactly;
8. test unload/reload/restart/nonpersistent.

No per-client visibility or hidden remote simulation is needed: suspension is global.

Compare:

- implementation complexity;
- component restoration;
- performance;
- modded prefabs;
- ZDO persistence;
- visual flash at swap;
- world preservation.

# 6. Spike C — eligibility and dynamic scan

Build a diagnostic scanner only.

Classify nearby:

- hostile MonsterAI;
- passive animal;
- tamed;
- boss;
- Dvergr neutral/aggravated;
- trader/NPC;
- PlayerSpawned summon;
- fish/bird;
- modded Character.

Print inclusion reason/exclusion reason.

Test periodic scan and ordinary spawn appearing after activation. No whole-world per-frame scan.

# 7. Spike D — target and damage matrix

Blood enemy:

- targets only Fighting/GoalReached Player;
- never static target/building/tamed/NPC/boss/ordinary creature;
- ignores Deferred/Exited;
- high aggression/no flee while target exists.

Participant attack:

- damages only current-event Blood enemy;
- no building/tree/ore/crop/tamed/boss/ordinary damage;
- forbidden target receives no status/stagger/skill credit.

Test:

- melee;
- projectile;
- thrown;
- AoE;
- trap/turret;
- environmental damage to Blood enemy;
- GoalReached target priority;
- Defeated/Withdrawn body-blocking.

Determine whether terminal Player↔Blood enemy collision needs pairwise ignore.

# 8. Spike E — Defeated and recovery

Patch owner-side `Character.CheckDeath` only for local Player in Fighting/GoalReached.

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

No vanilla SoftDeath status. Use temporary custom debug status text.

# 9. Spike F — edge and contexts

## Edge

Use `ZoneSystemVariantController.IsBeyondWorldEdge` with positive offset:

- outcome Withdrawn;
- no transform changes;
- Blood event rules removed before edge death.

## Interior/dungeon and ship/ocean

At activation:

- Player Deferred;
- red presentation may remain;
- no spawn anchor/target/safe defeat;
- leaving to supported land → Fighting.

Test Fighting→unsupported:

- candidate terminal Withdrawn;
- compare against suspend/resume only enough to confirm exploit/complexity tradeoff.

## Mounted/attached

- no forced detach;
- Blood enemy targets Player, not mount;
- tamed mount takes no damage;
- Player can Defeated in place;
- no special visibility/ownership bridge.

## Teleport

Pause spawn during transition; re-evaluate destination.

# 10. Spike G — boss handling

Test two candidates on at least one vanilla boss.

## A. In-place global stasis

- ZDO event marker;
- stop AI/attacks;
- hide visual/audio/HUD globally;
- disable combat colliders/physics safely;
- preserve owner/position/rotation/health/level;
- stop conflicting boss event/environment;
- restore under fade/debug command;
- restart recovery.

## B. ZDO parking

- save position/rotation/owner;
- mark eventId;
- authoritative move to reserved inactive far sector;
- prevent old owner transform overwrite;
- restore;
- restart recovery.

For both:

- multiple bosses;
- owner disconnect;
- boss projectile/AOE;
- summons/minions;
- new altar summon attempt;
- no defeat key/loot;
- restore without nearby Player;
- optional appearance animation.

Recommend production mechanism from evidence. If neither reliable, propose fallback: postpone global Blood Moon while active boss exists, but do not silently choose it.

# 11. Evidence report

Commit:

```text
docs/tasks/blood-moon/SPIKE_GLOBAL_EVENT_RESULTS.md
```

Include:

- Seasons base commit;
- `assemblies_combined` commit;
- patches tried;
- single/listen/dedicated tests;
- direct vs clone decision;
- eligibility table;
- damage matrix;
- Defeated/recovery results;
- mounted/context results;
- boss A/B results;
- restart/owner results;
- CPU/network observations;
- rejected approaches;
- recommended production patch set;
- required doc updates;
- whether spike code should be deleted or promoted.

Run Codex review on draft PR. Do not merge before owner runtime review.

# 12. Acceptance gate

Answer with runtime evidence:

1. Direct conversion or clone preservation?
2. Exact eligibility predicate?
3. Can global suspension restore original reliably?
4. Can Blood target/damage routing stay centralized?
5. Can CheckDeath create Defeated without vanilla death side effects?
6. Does 15s + 10s recovery behave correctly?
7. Do mounted/attached work without forced movement?
8. Is terminal Withdrawn correct for unsupported context?
9. Can boss stasis or parking preserve state/restart?
10. What minimal production architecture is justified?
