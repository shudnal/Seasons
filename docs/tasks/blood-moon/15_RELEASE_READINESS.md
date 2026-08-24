# Blood Moon release-readiness checkpoint

This file is the final repository checkpoint for draft PR #42 before owner-side runtime playtest. It complements `13_IMPLEMENTATION_REPORT.md` and `14_CODE_REVIEW_STATUS.md`.

## Pull request state

```text
PR: #42 Blood Moon first implementation slice
base: master
head: feat/blood-moon
state: draft / open / not merged
URL: https://github.com/shudnal/Seasons/pull/42
```

The pull request must remain unmerged until explicit owner approval.

The implementation still intentionally leaves the following release metadata unchanged:

- plugin version;
- public README;
- Thunderstore changelog;
- packaging/release metadata.

## Static review status

Multiple full-diff Codex review passes have been run. Confirmed findings from the earlier passes were fixed and their inline threads were resolved.

Reviewed commits include:

```text
f11ff4d4ff
  first Codex pass

ddbb65649e
  second Codex pass

e870e5805b
  third Codex pass

f7ed0e6bdc
  fourth Codex pass
```

The fourth pass identified and the branch fixed the following additional issues:

- client spawn-lease expiry used independent real-time-calendar clocks;
- canonical Blood Moon persistence could hide a newer valid `.new` recovery snapshot;
- the durable outcome queue had the same recovery-order weakness;
- a peer could claim an owned non-enemy ZDO as `BloodEnemy` hit attribution;
- the interaction matrix stopped at `Resolving` before the authoritative `BloodBehaviorEnabled` disable step;
- disabled source/upgrade recipes could still appear through Blood Craft.

The fixes now:

- rebase received lease lifetime into the client time domain;
- validate all canonical/`.new`/`.old` event-state candidates and recover the newest valid state;
- persist outcome-queue revision/write time and recover its newest valid candidate;
- validate `BloodEnemy` attribution at RPC ingress against the shared raw-ZDO enemy predicate and sender ownership;
- keep target/damage routing active through the fade window while the server still owns Blood behavior;
- freeze new extra spawning and skill credit as soon as resolution starts;
- require Blood Craft source and temporary-upgrade recipes to remain enabled.

Additional manual hardening after those reviews includes:

- file-write timestamp tie breaking for state snapshots whose subsystem-only save does not advance the event revision;
- immutable attribution lookup now returns the recorded source fact independently of the current event so stale-event guards can reject old local projectiles across event rollover;
- participant-summon attribution validates the current sender ownership of the source ZDO;
- pre-combat cancellation cannot later replay normal success outcomes after the feature is re-enabled;
- direct attack skill-credit guarding patches the real `Player.RaiseSkill` override;
- environmental/unattributed damage remains vanilla-valid for active participants while it cannot damage Blood enemies;
- server death reports require observed `s_health <= 0` and use bounded pending reconciliation for replication order;
- explicit resync retries after `Player.SetLocalPlayer` until current public/private state is available;
- chronicle identity is world-scoped;
- real-time-calendar worlds never feed calendar absolute seconds into Valheim net time.

A final Codex pass must target the exact final code head after this checkpoint. If that pass reports confirmed findings, they must be fixed and reviewed again before this checkpoint can be considered clean.

## Authoritative API source

All game API and Harmony target verification uses:

```text
repository: https://github.com/shudnal/assemblies_combined
commit: cf2cda3a4c5c05e62cb8052a61753e5dcaecc28e
```

Recent freeze checks specifically reconfirmed:

- `ZPackage.m_stream` is a seekable `MemoryStream`, so attribution pre-validation can restore the package cursor safely;
- `SeasonState.GetTotalSeconds()` uses ZNet time for ordinary worlds and UTC calendar elapsed seconds for world-settings real-time calendar mode;
- `Player.SetLocalPlayer()` is the reliable local-player-ready hook used by resync retry;
- `Player` overrides `RaiseSkill`, and projectile/AOE skill raises dispatch through that override;
- `Character.GetHealth()` is backed by ZDO `ZDOVars.s_health`;
- the current `EnvSetup` has 14 `Color` fields and the Blood Moon environment red-channel rule covers all 14.

## Runtime verification gate

No local Valheim build or runtime execution was performed by the assistant, per owner workflow.

Release readiness therefore still requires owner-side runtime validation, with the acceptance matrix in `08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md`, at minimum for:

- single-player annual lifecycle;
- listen-host lifecycle;
- dedicated server with multiple clients;
- late join and reconnect during Marked/Active/resolution;
- dedicated-server restart during Active and during persisted resolution steps;
- Defeated from enemy damage and environmental damage;
- recovery stage transition on ground, in water and while falling;
- GoalReached followed by continued helper combat, Defeated and Withdrawn;
- zone ownership migration with in-flight lease/report replication;
- mixed interior/surface participants in the same map zone;
- interior authored CreatureSpawner pathing;
- boss encounter parking/restoration and unsupported interior/nonpersistent withdrawal context;
- stale Blood Craft inventory across reconnect/restart and all guarded vanilla sinks;
- permanent and temporary combat summons;
- live x3 skill cap, reconnect baseline and completion reward replay;
- real-time calendar mode;
- early feature-disable cancellation;
- final 04:15/05:45/06:00 resolution behavior and client fade/input release.

## Known non-blocking content limitations

- Blood Moon music assets are not supplied yet; music production is intentionally paused.
- Blood Moon-specific runtime wording is English-first until playtest wording stabilizes.
- Automatic inferred enemy-pool progression remains future work documented in `06_FUTURE_PROGRESSION_AND_REWARDS.md`; the current implementation uses the explicit configured bootstrap prefab.
- Third-party custom world sinks or custom inventory implementations that bypass vanilla boundaries may require compatibility patches after runtime discovery.

## Continuation rule

If runtime testing finds a defect, record the reproduction, decision and fix in this repository before continuing. Do not merge PR #42 or alter release metadata until the owner explicitly requests the release step.
