# Blood Moon — isolated parallel-layer runtime spike

This is the only implementation task currently permitted by the Blood Moon design set.

## 0. Branch and scope

Create and work only in:

```text
spike/blood-moon-parallel-layer
```

Base it on the current head of:

```text
feat/blood-moon
```

The spike must not be merged into `master`. Open a draft PR from the spike branch into `feat/blood-moon` for review only.

Do not:

- implement the annual calendar or complete event;
- add release configuration or migrations;
- change the plugin version;
- update public README/changelog/package files;
- add permanent assets;
- claim production readiness;
- turn experimental hooks into broad always-on Harmony patches.

All spike behavior must be disabled by default and reachable only through an admin/debug command or an explicit local development config.

Before patching any game method, read its current code from:

```text
https://github.com/shudnal/assemblies_combined
```

## 1. Goal

Determine whether the intended parallel-world experience can be implemented safely without:

- moving Player;
- disabling networked GameObjects;
- breaking ZDO ownership;
- making hidden colliders block attacks;
- producing a real death/TombStone;
- breaking mounted, boss, dungeon, ship or observer scenarios.

The spike is successful only if it produces evidence for a minimal production architecture. It is acceptable and useful for a candidate approach to fail.

## 2. Temporary test model

Use a minimal isolated controller with these local classifications:

```csharp
internal enum BloodMoonSpikeLayer
{
    RealWorld,
    SharedPlayer,
    BloodAwaitingContact,
    BloodParticipant,
    BloodEnemy,
    BloodBoundMount
}
```

Use one centralized policy object. Exact signatures may change, but the spike must not duplicate rules across patches:

```csharp
CanSee(localPlayer, entity)
CanCollide(localPlayer, entity)
CanTarget(attacker, target)
CanDamage(source, target, hit)
CanInteract(player, target)
```

Create only enough temporary state to test:

```text
eventId
participant phase
outcome
first-contact record
ejection/grace state
entity layer marker
projectile/AOE layer attribution
```

Do not build the production persistence/network protocol yet. RPC messages used in multiplayer tests still require event ID, sender validation and duplicate protection.

## 3. Debug commands

Prefer the existing Seasons command style. Minimum test controls:

```text
seasons bloodmoon spike status
seasons bloodmoon spike reset
seasons bloodmoon spike awaiting
seasons bloodmoon spike fighting
seasons bloodmoon spike eject
seasons bloodmoon spike spawn ordinary <prefab>
seasons bloodmoon spike spawn blood <prefab>
seasons bloodmoon spike setowner <ordinary|blood> <server|participant|observer>
seasons bloodmoon spike dump
```

Commands must work in single-player/listen server. Multiplayer-changing commands are admin-only.

## 4. Spike A — local visibility and ownership matrix

Set up:

- one participant client;
- one observer client;
- one ordinary enemy;
- one event-marked blood enemy;
- all four ownership combinations:
  - blood enemy owned participant;
  - blood enemy owned observer;
  - ordinary enemy owned participant;
  - ordinary enemy owned observer.

Required states:

### `AwaitingContact`

Participant sees ordinary enemy and blood enemy. Observer sees ordinary enemy and participant but not blood enemy.

### `Fighting`

Participant sees blood enemy and other players but not ordinary enemy/tamed/boss. Observer sees ordinary enemy and participant but not blood enemy.

Requirements:

- never call `GameObject.SetActive(false)` on a networked entity as the layer mechanism;
- hidden owner entity must still be able to simulate for remote peers;
- local visual/audio/EnemyHud suppression must restore without stale state;
- owner migration and late join must reapply presentation correctly.

Candidate presentation path:

- use or patch `Character.SetVisible`/equivalent for the visual root without disabling AI/root;
- explicitly suppress local audio and `EnemyHud.TestShow`;
- keep world/terrain collision of the hidden entity active.

Record exact methods and state that had to be touched.

## 5. Spike B — collision and attack transparency

`Physics.IgnoreCollision` between local Player main collider and hidden Character main collider is only one part of the test.

Verify:

- body collision;
- hitbox collision;
- melee overlap/raycast;
- arrow/bolt raycast;
- thrown projectile;
- event projectile;
- AoE overlap;
- final `Character.Damage`/`WearNTear` safeguards.

Critical scenarios:

1. A hidden ordinary enemy stands between participant and blood enemy. Participant projectile must pass through the hidden ordinary collider and hit the blood enemy.
2. A hidden blood enemy stands between observer and ordinary target. Observer projectile must pass through the hidden blood collider.
3. An AoE only damages compatible targets.
4. An incompatible object never receives damage, stagger, status or skill credit.

Inspect `Attack.DoMeleeAttack`, `Attack.DoAreaAttack`, `Projectile.FixedUpdate`/`OnHit`/`DoAOE` and related current game code. Prefer filtering candidate hits before damage. Keep final damage guards as defense in depth.

Projectile/AOE spawned during the spike must capture:

```text
eventId
source layer
source ZDOID
```

at creation, so delayed hit behavior does not depend on the owner’s later state.

## 6. Spike C — accepted first contact

Participant starts in `AwaitingContact` with ordinary and blood entities visible.

Test contact kinds:

- incoming direct hit;
- outgoing melee hit;
- outgoing projectile hit;
- block;
- parry;
- fully mitigated/resisted accepted hit;
- lethal first hit;
- duplicate report;
- stale event ID;
- server rejection/resync.

Not contacts:

- miss;
- near-projectile notification;
- aggro;
- trigger overlap without attack resolution.

Acceptance:

- local transition to `Fighting` occurs before the first accepted incoming hit is resolved;
- first Bloodlust defense applies to that same hit;
- layer switch happens once;
- server validates and publishes the transition;
- rejection restores authoritative state without leaving hidden renderers/colliders.

Determine the narrowest reliable Harmony points. In particular inspect the current order in `Character.RPC_Damage` and `Humanoid.BlockAttack` rather than inferring contact from health delta alone.

## 7. Spike D — dream collapse

Use owner-side `Character.CheckDeath` interception for local Player when:

```text
phase == Fighting || phase == GoalReached
health <= 0
not already ejected
```

The spike must prove:

- `Player.OnDeath` is not called;
- no death point;
- no death effects/ragdoll;
- no TombStone;
- no inventory/equipment transfer;
- no food clear;
- no respawn request;
- no position/rotation/parent/velocity restore;
- outcome is exactly `BloodMoonParticipantOutcome.Defeated` once;
- no re-entry.

On collapse:

```text
Health  = current maximum
Stamina = current maximum
Eitr    = current maximum
Food    = unchanged
Adrenaline = unchanged
```

Remove damaging DoT effects without `RemoveAllStatusEffects`. At minimum inspect and test current vanilla `SE_Burning`, `SE_Poison` and `SE_Smoke`; report how modded damaging status effects could be handled without deleting buffs.

### Recovery protection

Stage 1:

- full incoming-damage immunity while the Player is still airborne from the collapse;
- must suppress the landing/fall damage belonging to that fall.

Test and choose an explicit stabilization condition. Candidate:

```text
IsOnGround || IsSwimming || IsAttached
```

This prevents indefinite immunity in water or on an attached object while preserving the intended airborne protection.

Stage 2:

```text
10 seconds
75% incoming-damage reduction
final incoming multiplier = 0.25
```

Test fall, water and lava. Lava is not made permanently safe: after the grace period vanilla lava behavior returns.

Direct forced `Player.OnDeath` must remain vanilla. An admin command implemented by ordinary HP reduction naturally reaches the spike collapse; an admin/scripted direct death call does not.

## 8. Spike E — contexts

### Interior/dungeon

- red atmosphere/forced-environment presentation may remain;
- no blood enemy spawn;
- no full layer/contact;
- eligibility resumes after returning to a supported outdoor context.

### Ship/ocean

- red atmosphere remains;
- no blood enemy spawn/full layer;
- no forced detach or ship movement;
- eligibility resumes on supported land.

### Generic attached

- no forced detach;
- Player may remain `AwaitingContact` and receive a first blood hit;
- full layer must not change parent/attach state.

### Mounted

Do not use forced `StopDoodadControl` as the primary design: vanilla `Sadle.OnUseStop` calls `Player.AttachStop`, and `AttachStop` moves Player to the detach offset.

Test a **blood-bound mount bridge**:

- current mount is obtained through `Player.GetDoodadController() is Sadle`;
- blood hit on rider or current mount enters rider into `Fighting`;
- blood source damage to mount is zero;
- mount stays visible and controllable for rider;
- mount remains ordinary for observers;
- blood enemies target rider, not mount;
- ordinary enemies cannot target/damage the blood-bound mount while rider is in full layer;
- mount cannot damage blood enemies or produce progress;
- voluntary dismount releases bridge and returns mount to ordinary hidden behavior for the still-fighting Player.

If the bridge cannot be made reliable without broad invasive patches, record mounted context as deferred until voluntary dismount. Do not silently fall back to forced dismount.

### Boss encounter

Use `EnemyHud.instance.ShowingBossHud()` as the local product signal:

- while visible, do not spawn blood enemies or enter full layer;
- preserve boss combat/bookkeeping;
- prefer boss environment/music plus compatible red overlay, not Blood Moon force that destroys readability;
- after boss HUD has been absent for a short stability delay, re-evaluate eligibility.

Test a client report to server. Treat it only as a delay signal, not a trusted reward claim.

### Edge of world

Use `ZoneSystemVariantController.IsBeyondWorldEdge(position, offset)` with a positive safety offset. Eject/withdraw Player before vanilla tidal/edge death becomes relevant. Do not wait for `HitData.HitType.EdgeOfWorld`.

The final non-defeat outcome name for this preventive exit remains open; report a recommendation.

## 9. Spike F — ordinary-world simulation

Run this only after visibility/collision isolation works.

### Baseline: background simulation

Do not alter ownership or suspend AI. Prove that participant can be locally isolated while owner simulation continues for observer.

### Conditional suspension experiment

Definition of a real-world witness for an ordinary entity:

- ready Player/peer;
- participant phase is not `Fighting` or `GoalReached`;
- reference/Player position is within active area or configured witness radius;
- ordinary world is visible to that Player.

`AwaitingContact` and `Ejected` count as real-world witnesses.

If an ordinary entity is owned by a full-layer participant and no real-world witness exists:

- retain owner;
- suspend only AI movement/target acquisition through a narrow guard;
- do not claim a full simulation freeze;
- exclude current `BloodBoundMount`;
- resume when witness appears or participant is ejected/resolved.

Use interaction radius plus enter/leave hysteresis, not every active sector.

Compare:

- ordinary ground/flying/swimming enemies;
- tameables;
- observer entering/leaving;
- owner disconnect/migration;
- CPU/network behavior;
- state after resume.

Production recommendation must prefer background simulation if conditional suspension introduces ownership or component inconsistency.

## 10. Evidence and result report

Commit a report to the spike branch containing:

- exact game commit from `assemblies_combined` used;
- exact Seasons base commit;
- patches tried;
- which approaches worked, failed or were abandoned;
- single-player/listen/dedicated results;
- owner matrix results;
- screenshots/log excerpts where useful;
- known unsupported interactions;
- recommended production architecture;
- changes required in authoritative design files `01`–`09`;
- whether the spike code should be deleted, retained as tests, or promoted selectively.

Open a draft PR:

```text
spike/blood-moon-parallel-layer → feat/blood-moon
```

Run Codex code review on that PR. Do not merge before owner runtime review.

## 11. Spike acceptance gate

The spike is complete when it can answer, with runtime evidence:

1. Can a client hide an entity while still owning/simulating it for another client?
2. Can incompatible Character colliders become transparent to melee/projectiles/AoE without changing global layers?
3. Can first contact switch the layer before the same hit is resolved?
4. Can `Character.CheckDeath` produce `Defeated` without any `Player.OnDeath` side effect?
5. Can recovery protection handle airborne/water/attached cases without indefinite immunity?
6. Can the mounted bridge work without forced transform changes?
7. Can boss/dungeon/ship contexts defer engagement cleanly?
8. Is background simulation acceptable, or is conditional AI suspension both necessary and safe?
9. Which minimal set of production Harmony points is required?
10. Which parts remain too fragile and must be simplified or dropped?
