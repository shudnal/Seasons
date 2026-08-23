# CHAT 2026-08-23 — Blood Moon: design and preimplementation work

## 0. Current status

> **Production implementation is paused.** The next work item is an isolated runtime spike for the parallel-world boundary, first contact, dream collapse, mounted context and ordinary-world simulation.

Repository:

```text
https://github.com/shudnal/Seasons
```

Design branch:

```text
feat/blood-moon
```

Game-code source of truth:

```text
https://github.com/shudnal/assemblies_combined
```

Do not use the old external `BloodMoon_Design_Document.md` or `BloodMoon_Codex_Implementation_Brief.md` as requirements.

## 0.1. Authoritative document set

1. `docs/tasks/CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`
2. `docs/tasks/blood-moon/01_STATE_AND_SCHEDULE.md`
3. `docs/tasks/blood-moon/02_NETWORK_PERSISTENCE_AND_PARTICIPANTS.md`
4. `docs/tasks/blood-moon/03_PRESENTATION_AND_SUPPRESSION.md`
5. `docs/tasks/blood-moon/04_COMBAT_PROGRESS_AND_RESOLUTION.md`
6. `docs/tasks/blood-moon/05_SEASONS_INTEGRATION.md`
7. `docs/tasks/blood-moon/06_FUTURE_PROGRESSION_AND_REWARDS.md`
8. `docs/tasks/blood-moon/07_FUTURE_PARALLEL_WORLD_AND_DEATH.md`
9. `docs/tasks/blood-moon/08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md`
10. `docs/tasks/blood-moon/09_PREIMPLEMENTATION_DECISIONS_AND_SPIKES.md`
11. `docs/tasks/blood-moon/10_PARALLEL_LAYER_RUNTIME_SPIKE.md`

For participant entry, `Defeated`, dream collapse, mounted handling, local visibility/collision, ownership and context gates, file `09` has priority. File `10` is the only implementation task currently permitted and applies only to the isolated spike branch.

## 0.2. Working process

Until the spike is reviewed and runtime-tested:

- do not implement the full event;
- do not open an implementation PR into `master`;
- do not bump the mod version;
- do not edit public README/changelog/release files;
- keep spike code isolated and removable;
- record every confirmed or rejected approach back into files `09` and `10`.

After the gate is closed by an explicit owner decision, prepare a new production task from the confirmed spike results.

---

# 1. Product vision

Blood Moon is an annual late-autumn event. With default Seasons settings it occurs once per 40 game days, approximately once per up to 20 hours of active gameplay.

It is not a raid on the base. It is a temporary blood-layer combat episode:

- the world begins to redden before combat;
- blood enemies hunt participating players;
- buildings, crops, tameables, bosses and ordinary creatures are outside the blood combat layer;
- the player can experiment with already known combat equipment through future temporary Blood Craft;
- permanent mechanical reward is limited to combat skills;
- there are no material, recipe, key, decorative or other world-state rewards;
- defeat does not create a tombstone or respawn the player;
- after the event only learned skill and an informational chronicle remain.

Core statements:

> Blood Moon does not seek the player’s walls. It seeks the player.

> Everything shaped by blood disappears; experience remains.

---

# 2. Accepted lifecycle direction

```text
Forewarning nights
→ Marked at 18:00
→ linear red overlay
→ Active at 23:00
→ context check
→ AwaitingContact
→ accepted first blood interaction
→ Fighting/full blood layer
→ GoalReached or Defeated/Disconnected
→ early or forced resolution
→ fade, cleanup, DreamText, morning
```

Accepted decisions:

- explicit server event state machine;
- explicit participant phase and separate outcome;
- deterministic annual `eventId`;
- server-authoritative progress and outcomes;
- `AwaitingContact` keeps ordinary world visible until first accepted blood interaction;
- first contact includes incoming/outgoing hit, block, parry and fully mitigated accepted hit;
- outcome name is `BloodMoonParticipantOutcome.Defeated`;
- dream collapse is intercepted in owner-side `Character.CheckDeath` before `Player.OnDeath`;
- no death point, ragdoll, TombStone, inventory transfer or respawn;
- restore health, stamina and eitr to full; keep food and adrenaline;
- clear damaging DoT status effects;
- full protection until the player is stabilized, then 10 seconds with 75% incoming-damage reduction;
- no re-entry in the first release;
- `SoftDeath` is shown only as a familiar indication and is not the protection mechanism;
- no position, rotation, parent, ship or mount manipulation by Blood Moon;
- no mass `SetOwner(0)` parking;
- dungeon/interior and ship/ocean receive atmosphere but no blood enemies/full layer;
- active boss encounter defers personal engagement while the boss HUD is visible;
- edge-of-world proximity ejects the player before vanilla edge death;
- mounted entry requires a dedicated runtime spike; forced dismount is not accepted because vanilla detach changes position.

---

# 3. Production scope after the spike gate

The future first playable vertical slice is expected to include:

- annual calendar and safe first-install behavior;
- Forewarning, Marked, Active, AutoCompleting, Resolving, Resolved/Skipped;
- CCS/RPC synchronization and recovery;
- environment overlay, forced environment and cloud VFX;
- sleep and ordinary random-event suppression;
- one blood enemy without persistent DoT;
- hidden combat groups and server caps;
- centralized interaction policy;
- server progress, GoalReached, Defeated and disconnect outcomes;
- morning resolution, DreamText and Rested removal;
- debug commands and structured logs.

It must not be started from this index alone. Use the future production task produced after the runtime spike.

---

# 4. Deferred systems

- automatically inferred production enemy pool and enemy roles;
- Momentum/StallTime;
- Blood Craft UI/items;
- skill-reward payout and limited x3 gain;
- lifesteal/attack speed;
- Odin watcher and Doppelganger;
- optional PvP;
- optional re-entry;
- final music/SFX;
- final forewarning presentation.

The parallel layer, Blood Craft attacks, projectiles/AOE and summons must share one interaction policy even when implemented in separate commits.
