# Blood Moon — combat, progress, defeat and resolution

Part of `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

> Production implementation is blocked by files `09` and `10`.

# 14. Hidden combat grouping

Server-only groups prevent full spawn budgets from multiplying around nearby Players.

Planned rules:

- recompute every 3–5 seconds;
- connected components by distance;
- merge default 120 m;
- split hysteresis default 160 m;
- stable ID by largest member overlap;
- per-group cap based on combat-capable participants;
- server hard cap;
- spawn anchor chosen from actual eligible members, never chain centroid;
- no map/UI marker.

`AwaitingContact`, `Fighting` and `GoalReached` may remain group members, but context-gated participants do not anchor spawns. `Ejected`, `Resolved` and terminal disconnected participants are excluded.

Momentum, StallTime and production enemy roles are deferred.

---

# 15. First blood enemy prototype

Use one conservative vanilla melee enemy without persistent damaging status attacks.

Custom spawner:

- independent from `RandEventSystem` event control;
- ignores ordinary NoMonsters/PlayerBase spawn suppression;
- still finds a valid supported surface point;
- respects min/max distance, group cap and server cap;
- does not spawn for interior/dungeon, ship/ocean or visible-boss context;
- stops immediately during resolution;
- marks ZDO before normal simulation.

Markers:

```text
Seasons.BloodMoon.EventId
Seasons.BloodMoon.GroupId
Seasons.BloodMoon.Role
```

## 15.1. Targeting

Blood enemy may target only current-event:

```text
AwaitingContact
Fighting
GoalReached
```

`GoalReached` receives lower priority only when unresolved targets are available.

Never target:

- nonparticipants;
- Ejected/Resolved;
- ordinary creatures/NPC;
- tamed/mount as a target;
- buildings, crops or static targets;
- stale blood entities.

AI filtering and final damage guards both delegate to centralized interaction policy.

## 15.2. Ownership reports

Do not assume blood-enemy death callback runs on dedicated server. Owner reports:

```text
eventId
enemy ZDOID
group ID
reported killer identity
position
```

Server validates marker/event/group/range/phase and deduplicates ZDOID. Server defines point value.

## 15.3. Loot and ragdoll

- no ordinary loot;
- no economy contribution;
- short ragdoll cleanup, initial default 2 seconds;
- full cleanup on resolution/ejection/world unload;
- never mutate shared prefab drop table globally.

---

# 16. First contact and progress

## 16.1. Accepted contact

`AwaitingContact → Fighting` on accepted blood interaction:

- incoming or outgoing melee/projectile hit;
- block;
- parry;
- fully mitigated accepted hit.

Not on miss, aggro or near-projectile notification.

The local owner must switch before resolving the same first incoming hit, so base Bloodlust defense and dream-collapse protection apply immediately. Server validates/deduplicates the transition.

## 16.2. Separate values

```text
CombatBloodlustPoints
DisplayedBloodlustProgress
CombatContribution
```

Auto-complete only raises displayed floor.

## 16.3. Kill progress

First prototype uses server-configured `pointsPerKill`.

A validated kill:

- grants combat points to eligible group participants within share radius;
- does not depend only on last hit;
- records killer separately;
- grants nothing to Ejected/Resolved;
- GoalReached actions may be recorded but no longer increase personal progress.

## 16.4. GoalReached

At real combat progress 100%:

- phase → `GoalReached`;
- outcome → `Success`;
- full combat buff/layer remain;
- Player can help unresolved participants;
- enemies still target this Player if no unresolved alternative exists.

No weakened `Sated` state.

## 16.5. Auto-complete

Near morning:

```text
DisplayedBloodlustProgress = max(combatProgress, automaticFloor)
```

Automatic floor:

- does not add combat points/contribution;
- does not produce `Success`;
- does not generate future skill reward.

At forced end, unresolved participant receives `HiddenAtBase` or `HiddenInWild` using the game’s normal base/protection semantics.

---

# 17. Combat modifiers

Initial linear direction:

```text
incoming reduction at 0%: 50%
incoming reduction at 100%: 25%
outgoing bonus at 0%: 15%
outgoing bonus at 100%: 40%
movement bonus at 0%: 0/small
movement bonus at 100%: 10–15%
```

Defensive base modifier must already apply to accepted first incoming hit. Contribution/reward begins only after contact.

Defer lifesteal, attack speed, stagger immunity and weapon-specific tuning until combat playtest.

---

# 18. Dream collapse and `Defeated`

## 18.1. Interception

Candidate production/spike point:

```csharp
Character.CheckDeath prefix
```

Only local owner `Player` where:

```text
phase == Fighting || phase == GoalReached
health <= 0
not already ejected
```

The prefix restores state and suppresses `Player.OnDeath`.

Direct forced `Player.OnDeath` remains vanilla. Scripted removal is not treated as an ordinary lethal health check.

## 18.2. Lethal scope

Any ordinary lethal health result during `Fighting`/`GoalReached` triggers collapse, regardless of source:

- blood hit;
- fall;
- delayed projectile/AoE;
- environment;
- ordinary HP reduction.

This prevents a blood-caused fall from producing a real TombStone due to lost attribution.

Edge-of-world is handled before lethal threshold through preventive ejection/withdrawal.

## 18.3. Collapse result

Do not call/do:

- `Player.OnDeath`;
- death point;
- death effects/ragdoll;
- TombStone;
- inventory/equipment move;
- food clear;
- respawn;
- transform/velocity restore.

Set:

```text
Health = current maximum
Stamina = current maximum
Eitr = current maximum
Food unchanged
Adrenaline unchanged
Outcome = Defeated
Phase = Ejected
```

Then:

- remove Bloodlust combat state;
- clear damaging DoT status effects only;
- restore real-world presentation/interactions;
- prevent re-entry current event;
- report exactly once to server;
- show defeat DreamText at common resolution.

At minimum inspect/test vanilla `SE_Burning`, `SE_Poison` and `SE_Smoke`; never use `RemoveAllStatusEffects`.

## 18.4. Recovery protection

### Stage 1

Full incoming-damage immunity while still airborne from collapse. It must cover landing/fall damage from that trajectory.

Runtime spike chooses a finite stabilization rule. Current candidate:

```text
IsOnGround || IsSwimming || IsAttached
```

This honors ground-touch intent without indefinite immunity in water/attached state.

### Stage 2

After stabilization:

```text
10 seconds
75% incoming-damage reduction
incoming multiplier = 0.25
```

Lava is not made permanently safe. After grace, vanilla lava damage resumes; entering lava was the player’s mistake and Blood Moon already granted a second chance.

---

# 19. Unsupported context after Fighting

Entering dungeon, ship/ocean, boss encounter or teleport after full layer begins is still a production blocker.

Candidates:

1. terminal withdrawal/ejection without heal;
2. personal event suspension and later return;
3. block the interaction;
4. context-specific blood behavior.

Preferred direction for simplicity/agency is terminal withdrawal without transform changes and without success reward, but outcome name/resource handling are not yet accepted.

---

# 20. Early and morning resolution

Early resolution when at least one Player was enrolled and every enrolled participant has terminal result:

```text
Success/GoalReached
Defeated/Ejected
Disconnected
```

GoalReached remains active while AwaitingContact/Fighting participants exist.

Forced resolution at 05:45.

Two-phase protocol:

### Prepare

- freeze enrollment;
- stop spawn;
- freeze outcomes/statistics;
- publish prepare;
- clients fade and ACK;
- timeout prevents one client blocking server.

### Resolve under fade

- destroy blood enemies/summons/projectiles;
- clear layer/status/recovery state as appropriate;
- clear cloud VFX;
- release own forced environment;
- restore RandEventSystem;
- move time to 06:00;
- remove Rested;
- publish DreamText/outcome;
- release input.

Never move or rotate Player. A safe grounded Player may receive an emote only; uncertain contexts use fade/DreamText only.

---

# 21. Debugging after spike gate

Planned commands include:

```text
seasons bloodmoon status
seasons bloodmoon start marked
seasons bloodmoon start active
seasons bloodmoon firstcontact
seasons bloodmoon defeat
seasons bloodmoon setprogress <0..100>
seasons bloodmoon spawn [prefab]
seasons bloodmoon resolve
seasons bloodmoon cleanup
seasons bloodmoon dump-participants
seasons bloodmoon dump-groups
```

Commands call normal transition methods. Per-hit/per-spawn logs are debug-only.
