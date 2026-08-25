# Blood Moon code review status

This file records the repository/Codex review trail for draft PR #42 and the final continuation checkpoint after implementation hardening.

## Pull request

```text
PR: #42 Blood Moon first implementation slice
base: master
head: feat/blood-moon
state: draft / open / not merged
URL: https://github.com/shudnal/Seasons/pull/42
```

The PR must remain draft and unmerged until the owner explicitly approves it.

## Authoritative game source

All game/API verification for review fixes uses:

```text
repository: https://github.com/shudnal/assemblies_combined
commit: cf2cda3a4c5c05e62cb8052a61753e5dcaecc28e
```

No assistant-side Valheim build or runtime test was performed.

## Review chronology

The branch received repeated full-diff Codex passes. Every confirmed inline finding listed below was fixed on `feat/blood-moon` and its review thread was resolved before another exact-head review was requested.

### Pass 1 — `f11ff4d4ff`

Five findings were accepted and fixed:

1. explicit resync did not preserve private skill sequence/live-bonus baselines;
2. generic world cleanup could remove valid active Blood Craft items during recovery;
3. disabling the feature before combat could follow the normal morning-advance path;
4. random-event suppression could leak across world teardown;
5. boss withdrawal used XZ proximity without sufficient interior-context discrimination.

Representative fixes include:

```text
86e7292681048a09fcd51e2f59365e98f1b2730a
831dc32b585562c4d071a3f47780f35deedb908d
ddbb65649e721742864f4da468b83f76882f386d
621429699a140a92e937aace93c6800cea5ebe10
2da9849b0144656e944becb4cae36ea9bb93ca22
565fc4890fe34c678fdca1366df0a1a4c94a2ec9
```

### Pass 2 — `ddbb65649e`

Six findings were accepted and fixed, including:

1. environmental/null-attacker hazards were incorrectly blocked against participants;
2. private resync could fire before local Player creation and never retry;
3. direct attack Harmony attributes did not reliably enumerate both attack methods;
4. mixed interior/surface zones could route the whole spawn allowance to one context;
5. a modified zone owner could report a living enemy as dead;
6. chronicle keys were not world-scoped.

The same audit established that skill interception must patch the real `Player.RaiseSkill` override rather than the base Character method.

### Pass 3 — `e870e5805b`

The next pass and associated manual freeze work hardened:

- undelivered per-player outcomes so later events cannot overwrite them;
- stale/late resync ordering with monotonic envelopes;
- immutable AI/projectile/AOE source context;
- real-time-calendar handling so calendar absolute seconds never become Valheim net time.

### Pass 4 — `f7ed0e6bdc`

Confirmed findings included:

- lease expiry compared independent client/server clock domains;
- canonical event persistence could hide a newer valid `.new` snapshot;
- claimed Blood-enemy source attribution was not sufficiently verified at RPC ingress;
- interaction routing stopped too early during `Resolving` while Blood behavior was still active;
- disabled source/upgrade recipes could remain visible through Blood Craft.

The fixes introduced client-local lease rebasing, newest-valid snapshot selection and stronger raw-ZDO attribution/recipe validation.

The durable outcome store was also brought to the same newest-valid canonical/`.new`/`.old` recovery model.

### Intermediate clean review — `daf5f5bb3b`

Codex reported no major issues on this exact head after interior spawn-context hardening.

Further manual hardening then changed runtime code, so the branch correctly continued through new exact-head reviews instead of treating this result as final.

### Pass 5 — `688b901477`

Five new findings were accepted and fixed:

1. spawn reports could accept an arbitrary client-marked ZDO instead of the configured extra-enemy prefab;
2. boss discovery still lacked the same navigation/interior context required by boss withdrawal;
3. the mixed-context spawn interceptor could race the resolution spawn freeze;
4. durable outcome acknowledgement occurred before Player-profile persistence;
5. a late defeat RPC could still rewrite participant state after resolution started.

Fixes added exact prefab/`MonsterAI` validation, navigation-context discovery, resolution-phase spawn rejection, persistence-aware outcome acknowledgement and a combat-live defeat-report guard.

Manual review after this pass also found that cleanup must include a valid extra whose report was completely lost; resolution now scans current-event markers but destroys only ZDOs that pass authoritative extra-enemy validation.

### Pass 6 — `51c6e99098`

Two findings were accepted and fixed:

1. dormant `EventId == -1` cleanup matched the default `-1` marker on ordinary world ZDOs;
2. extra-enemy validation depended on the live config value, so changing the configured prefab during an active event/restart could strand legitimate extras.

The fixes:

- short-circuit dormant cleanup without scanning ordinary world ZDOs;
- freeze the extra-enemy prefab identity once per event;
- persist that identity in event state;
- reuse the existing lease `PoolRevision` as the stable prefab hash;
- make both normal and mixed-context spawning resolve the lease's frozen prefab;
- validate reports, deferred reports and cleanup against the frozen identity rather than live config.

### Pass 7 — `0b423b0350`

One finding was accepted and fixed:

- after the current event state was replaced, the destroy guard no longer knew the frozen prefab identity for an extra marked with a prior event ID, so stale prior-event extras could survive recovery permanently.

The fix adds a server-only durable spawn-pool registry:

```text
world UID -> event ID -> frozen prefab name
```

The registry is written from authoritative frozen event state, retains complete event history, uses newest-valid canonical/`.new`/`.old` recovery, and lets stale-event cleanup retain exact prefab + `MonsterAI` validation without trusting client markers.

### Code-freeze clean review — `0be8eb89d6`

Codex result:

```text
Didn't find any major issues.
```

Exact full SHA:

```text
0be8eb89d600a95720f08387041fb7ab15ccd5f7
```

At that checkpoint all inline review threads were resolved.

## Important manual findings incorporated between Codex passes

Manual freeze audits also fixed or reconfirmed issues that were not always raised as standalone Codex comments:

- 06:00 frozen transition broadcasts the correct net time;
- restart gives persisted participants bounded reconnect grace;
- stale projectile/AOE attribution cannot revive under a later event ID;
- schedule search uses the configured total year length;
- Fader cloud clones reactivate correctly after vanilla environment switching;
- teleport spatial suspension is heartbeat/TTL based;
- rejected spawn cleanup is bound to immutable ZDOID creator identity;
- raw SpawnSystem zone ownership is independently verified;
- completion reward replay is monotonic;
- Blood Craft multi-craft respects vanilla stack limits;
- cancelled pre-combat events never replay success outcomes;
- `PlayerProfile.Save() == true` is not accepted as cloud durability proof because current vanilla cloud failure can still produce `true` after only a local recovery backup;
- valid unreported extras remain cleanable at resolution;
- frozen spawn-pool identity is persisted before any event lease can create a valid extra.

## Documentation freeze

After the clean code-freeze review, only internal task documentation is being updated:

- `13_IMPLEMENTATION_REPORT.md`;
- this file;
- `15_RELEASE_READINESS.md`.

A final Codex request must review the resulting documentation-inclusive head. If that pass reports a confirmed runtime/code/documentation issue, fix it and review again. The final clean result may be recorded in the PR timeline without modifying these files again, so the reviewed head remains exact.

## Final invariants before owner playtest

The repository checkpoint is acceptable only when all of the following remain true:

- no unresolved review thread exists;
- the final documentation-inclusive Codex pass has no confirmed finding;
- PR #42 is still draft/open/unmerged;
- no plugin version, public README, Thunderstore changelog or packaging/release metadata was changed;
- no assistant-side Valheim build/runtime execution is claimed;
- owner-side runtime acceptance remains the next gate.
