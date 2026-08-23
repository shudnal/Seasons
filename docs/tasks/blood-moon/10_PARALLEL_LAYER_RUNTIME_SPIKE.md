# Blood Moon — isolated parallel-layer runtime spike

This is the only implementation task currently permitted by the Blood Moon design set.

## 0. Branch and scope

Work only in:

```text
spike/blood-moon-parallel-layer
```

Base: current `feat/blood-moon`.

Open only a draft PR:

```text
spike/blood-moon-parallel-layer → feat/blood-moon
```

Do not merge into `master`, bump the version, edit release files, add permanent assets or claim production readiness.

All spike behavior is disabled by default and enabled only by an admin/debug command or explicit development config.

Before patching a game method, read its current implementation from `shudnal/assemblies_combined`.

## 1. Goal

Prove or reject a minimal parallel-world architecture without:

- moving/rotating/reparenting Player;
- disabling networked root GameObjects;
- using mass `SetOwner(0)` parking;
- allowing hidden colliders to block attacks;
- calling `Player.OnDeath` for illusory defeat;
- breaking observer, mount, boss, dungeon, ship or ownership scenarios.

A failed candidate is a useful result if the failure and fallback are documented.

## 2. Temporary model

### 2.1. Layer classification

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

### 2.2. Participant phase and outcome

```csharp
internal enum BloodMoonParticipantPhase
{
    None,
    Marked,
    AwaitingContact,
    Fighting,
    GoalReached,
    Ejected,
    Resolved
}

internal enum BloodMoonParticipantOutcome
{
    None,
    Success,
    Defeated,
    Disconnected
}
```

The spike may omit unrelated future outcomes.

### 2.3. Engagement gate

Context is independent of participant phase:

```csharp
internal enum BloodMoonEngagementGate
{
    None,
    Teleporting,
    Interior,
    ShipOrOcean,
    BossEncounter
}
```

A gate may be active before or after first contact.

While gate is active:

- ordinary world is visible/interactable;
- blood enemies do not spawn/target this Player;
- full blood-only layer is suspended;
- Bloodlust combat modifiers are suspended;
- Blood Moon `SoftDeath` indicator is absent;
- `Character.CheckDeath` dream-collapse protection is inactive;
- ordinary death is vanilla;
- event clock and existing personal progress continue;
- when gate clears before forced end, Player returns to `AwaitingContact` or resumes `Fighting`/`GoalReached` without transform changes.

This suspension is the preferred policy for interior, ship/ocean, teleport and visible-boss contexts. It avoids terminal withdrawal and preserves agency. The spike must test whether resume is clean.

### 2.4. Central policy

Use one policy object; do not duplicate allowlists across patches:

```text
CanSee(localPlayer, entity)
CanCollide(localPlayer, entity)
CanTarget(attacker, target)
CanDamage(source, target, hit)
CanInteract(player, target)
```

Temporary state is limited to:

```text
eventId
phase/outcome/gate
first-contact record
ejection/grace state
entity layer marker
projectile/AOE attribution
```

Do not build production persistence/networking. Multiplayer reports still include event ID, sender/object identity and duplicate protection.

## 3. Debug commands

Minimum:

```text
seasons bloodmoon spike status
seasons bloodmoon spike reset
seasons bloodmoon spike awaiting
seasons bloodmoon spike fighting
seasons bloodmoon spike gate <none|teleport|interior|ship|boss>
seasons bloodmoon spike eject
seasons bloodmoon spike spawn ordinary <prefab>
seasons bloodmoon spike spawn blood <prefab>
seasons bloodmoon spike setowner <ordinary|blood> <server|participant|observer>
seasons bloodmoon spike dump
```

Single-player/listen must work. Multiplayer-changing commands are admin-only.

# 4. Spike A — local visibility and ownership matrix

Set up participant, observer, one ordinary enemy and one event-marked blood enemy.

Test all owner combinations:

```text
blood owned by participant
blood owned by observer
ordinary owned by participant
ordinary owned by observer
```

### AwaitingContact

- participant sees ordinary + blood;
- observer sees ordinary + participant, not blood.

### Fighting/GoalReached with gate None

- participant sees blood + all Players, not ordinary creatures/tamed/boss;
- observer sees ordinary + participant, not blood.

Requirements:

- no root `GameObject.SetActive(false)`;
- hidden owner entity still simulates for remote peer;
- visual/audio/EnemyHud restore without stale state;
- owner migration and late join reapply local presentation.

Candidate path:

- patch/use `Character.SetVisible` or renderer state without disabling root/AI;
- suppress local audio and `EnemyHud.TestShow` through the same policy;
- keep terrain/world collision active.

# 5. Spike B — cross-layer collision and hit transparency

Test both local-Player pairs and entity-to-entity pairs.

Blood and ordinary Characters must not push or physically affect each other merely because one client owns both. Cache pairwise collision changes; do not recompute all pairs every frame.

Verify:

- body colliders;
- hitboxes;
- melee ray/overlap;
- arrows/bolts;
- thrown/event projectiles;
- AoE;
- final `Character.Damage`/`WearNTear` guards.

Critical cases:

1. Hidden ordinary enemy between participant and blood target: participant projectile passes through and hits blood target.
2. Hidden blood enemy between observer and ordinary target: observer projectile passes through.
3. Blood enemy and ordinary creature do not push each other on the simulation owner.
4. AoE affects only compatible targets.
5. Incompatible target receives no damage, stagger, status or skill credit.

Inspect current `Attack.DoMeleeAttack`, `Attack.DoAreaAttack`, `Projectile.FixedUpdate`, `Projectile.OnHit` and `Projectile.DoAOE`. Prefer filtering before damage. Final damage guard remains defense in depth.

Projectile/AOE captures on creation:

```text
eventId
source layer
source ZDOID
```

Delayed hit must not depend on later source state/owner.

# 6. Spike C — accepted first contact

Start in `AwaitingContact`, ordinary and blood worlds both visible.

Contacts:

- incoming hit;
- outgoing melee/projectile hit;
- block;
- parry;
- fully mitigated/resisted accepted hit.

Not contacts:

- miss;
- near-projectile notification;
- aggro;
- trigger overlap without attack resolution.

Acceptance:

- local `AwaitingContact → Fighting` occurs before the same first incoming hit is resolved;
- Bloodlust base defense applies to that hit;
- full layer switches once;
- server validates/deduplicates;
- rejection/resync restores renderer/audio/collision/status.

Inspect actual order in `Character.RPC_Damage`, `Humanoid.BlockAttack`, melee and projectile paths. Do not infer contact from positive health loss only.

First record:

```text
eventId
stable Player ID
peer UID
Player ZDOID
contact kind
blood enemy ZDOID
timestamp
position only for diagnostics
```

# 7. Spike D — dream collapse

Use owner-side `Character.CheckDeath` interception only when:

```text
phase == Fighting || phase == GoalReached
gate == None
full blood layer active
health <= 0
not already ejected
```

Prove:

- `Player.OnDeath` is not called;
- no death point/effects/ragdoll/TombStone;
- no inventory/equipment/food transfer;
- no respawn;
- no transform/parent/velocity restore;
- outcome exactly `Defeated` once;
- no re-entry.

Restore:

```text
Health = current max
Stamina = current max
Eitr = current max
Food unchanged
Adrenaline unchanged
```

Remove damaging DoT effects without `RemoveAllStatusEffects`.

At minimum test:

- `SE_Burning`;
- `SE_Poison`;
- `SE_Smoke`;
- `SE_Stats` where `m_tickInterval > 0` and `m_healthPerTick < 0`.

Report a safe approach for unknown modded DoT classes. Do not delete unrelated buffs.

## 7.1. Recovery protection

Stage 1:

- full incoming immunity while airborne from collapse;
- cover landing/fall damage from that trajectory.

Test a finite stabilization rule. Candidate:

```text
IsOnGround || IsSwimming || IsAttached
```

Stage 2:

```text
10 seconds
75% incoming reduction
final multiplier 0.25
```

Test fall, water, attached and lava. Lava is not permanently safe; vanilla damage returns after grace.

`SoftDeath` is added only while full blood layer is active, as familiar UI. It is removed/suspended with the layer and is not the protection mechanism.

Direct forced `Player.OnDeath` remains vanilla. HP reduction reaches collapse naturally.

# 8. Spike E — contexts

## 8.1. Interior/dungeon

- red atmosphere may remain;
- gate `Interior`;
- no blood spawn/layer/SoftDeath/collapse protection;
- clear gate and resume after supported outdoor return.

## 8.2. Ship/ocean

- red atmosphere remains;
- gate `ShipOrOcean`;
- no blood spawn/layer;
- no forced detach/ship movement;
- resume on supported land.

## 8.3. Teleport

- gate during transition;
- no spawn/contact during teleport;
- after completion recompute destination context;
- resume prior personal phase if supported.

## 8.4. Generic attached

- no forced detach;
- may remain AwaitingContact and receive first hit;
- full layer must not alter parent/attach state;
- stabilization after defeat must not wait forever solely because attached.

## 8.5. Mounted bridge

Do not force `StopDoodadControl`: vanilla saddle release calls `AttachStop`, which moves Player to detach offset.

Test `BloodBoundMount`:

- obtain current mount via `Player.GetDoodadController() is Sadle`;
- blood hit on rider or ridden mount contacts rider;
- mount takes zero blood damage;
- if mount collider receives a blood hit, either transparently continue/redirect the attack to rider so mount is not an invulnerable shield;
- rider enters Fighting without dismount;
- mount stays visible/controllable to rider and ordinary to observers;
- blood enemies target rider, not mount;
- ordinary enemies do not target/damage bridged mount;
- mount cannot damage blood enemies or grant progress;
- voluntary dismount releases bridge; still-fighting Player then sees mount as ordinary-hidden.

If unreliable, mounted context uses an engagement gate until voluntary dismount. Never silently use forced dismount.

## 8.6. Boss encounter

Use local `EnemyHud.instance.ShowingBossHud()` signal:

- gate `BossEncounter` while visible;
- preserve boss combat/bookkeeping/environment/music where possible; use compatible red overlay only;
- after HUD absent for stability delay, clear gate and resume.

Client report is low-trust delay state, never a reward/outcome claim. Optionally validate nearby boss on server.

## 8.7. Edge of world

Use `ZoneSystemVariantController.IsBeyondWorldEdge(position, positiveSafetyOffset)`.

Before vanilla edge/tidal death:

- remove full layer/Bloodlust/SoftDeath;
- eject or withdraw without transform changes;
- ordinary edge death afterwards remains vanilla.

Report a final neutral outcome name; do not label preventive exit `Defeated` automatically.

# 9. Spike F — ordinary-world simulation

Run only after layer isolation works.

## 9.1. Baseline: background simulation

Do not change owner or suspend AI. Prove hidden owner simulation works for remote observer.

## 9.2. Definition of real-world witness

For an ordinary entity, witness exists if a ready Player/peer:

- is not full-layer `Fighting`/`GoalReached`;
- is within active area or configured witness radius;
- can see ordinary world.

`AwaitingContact`, context-gated participants and `Ejected` count as witnesses.

## 9.3. Conditional suspension experiment

Only if baseline causes unacceptable world changes:

- ordinary entity is owned by full-layer participant;
- no real-world witness;
- retain owner;
- suspend only AI movement/target acquisition through narrow guard;
- exclude BloodBoundMount;
- resume when witness appears or participant layer ends;
- use interaction radius + hysteresis, not all active sectors.

This is not full freeze: physics/status/procreation may continue. Compare CPU/network and state integrity. Production fallback is background simulation if suspension is fragile.

# 10. Evidence and report

Commit a report with:

- exact Seasons and `assemblies_combined` commits;
- patches/alternatives tried;
- single/listen/dedicated results;
- ownership matrix;
- screenshots/log excerpts;
- performance observations;
- supported/unsupported contexts;
- recommended minimal production architecture;
- rejected approaches and fallbacks;
- required doc changes;
- which spike code to delete, retain as diagnostics or promote selectively.

Run Codex review on draft PR into `feat/blood-moon`. Do not merge before owner runtime review.

# 11. Acceptance gate

Answer with runtime evidence:

1. Can client hide entity while still owning/simulating it remotely?
2. Can cross-layer Character physics be isolated?
3. Can hidden hitboxes be transparent to melee/projectile/AoE?
4. Can first contact switch before same hit resolution?
5. Can CheckDeath produce Defeated without OnDeath side effects?
6. Can recovery protection terminate on ground/water/attached without infinite immunity?
7. Can mounted bridge work without forced movement?
8. Can context gates suspend/resume personal layer cleanly?
9. Is background simulation acceptable; if not, is conditional suspension safe?
10. What is the minimal production patch set, and what must be simplified/dropped?
