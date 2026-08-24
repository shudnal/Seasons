# Blood Moon code review status

This file records the review trail for draft PR #42 and is the current continuation checkpoint after `13_IMPLEMENTATION_REPORT.md`.

## Pull request

```text
PR: #42 Blood Moon first implementation slice
base: master
head: feat/blood-moon
state: draft / open / not merged
URL: https://github.com/shudnal/Seasons/pull/42
```

The PR must remain draft and unmerged until the owner explicitly approves it.

## First Codex review

Triggered by `@codex review`.

```text
review ID: 5012228234
reviewed commit: f11ff4d4ff
findings: 5
status: all accepted, fixed and resolved
```

### P1 — Include skill baselines in resync details

Explicit resync constructed a private participant detail without `LiveSkillBonusUsed` and `LastSkillReportSequence`, which could reopen the local live-skill budget and restart report sequencing.

Resolution:

- explicit resync detail is normalized to the same skill baseline as normal targeted participant detail;
- `LiveSkillBonusUsed` is derived from persisted per-skill live bonus accounting;
- `LastSkillReportSequence` is copied from the persisted participant record.

```text
86e7292681048a09fcd51e2f59365e98f1b2730a
fix: preserve Blood Moon skill baselines on resync
```

### P2 — Preserve valid Blood Craft items during active recovery

Generic client-world cleanup could remove valid world/event/owner-bound temporary items before a recovered single-player/listen-server state was published.

Resolution:

- generic world transitions preserve marked inventory items until the loaded world/event snapshot validates them;
- `EnsureWorldLoaded`, `CleanupClientWorldState` and ZNet teardown use scoped preservation;
- explicit event boundaries such as Defeated, Withdrawn and resolution still perform destructive cleanup;
- stale/cross-world markers remain subject to world/event/owner validation after load.

```text
831dc32b585562c4d071a3f47780f35deedb908d
fix: preserve Blood Craft items across world recovery

255ac6b7aa52cd58b54fcfbac98bba90ff261558
fix: preserve active Blood Craft inventory on world teardown

ddbb65649e721742864f4da468b83f76882f386d
fix: preserve Blood Craft through all world transitions
```

### P1 — Avoid advancing to dawn when disabling before combat

Disabling Blood Moon during Forewarning or Marked entered the normal resolution time-advance path and could skip several world days.

Resolution:

- pre-combat feature disable persists `ResolutionCancelledBeforeCombat`;
- cancellation still performs fade/cleanup/world-system restoration;
- frozen-morning advance and outcomes are skipped;
- resolved-outcome replay is also suppressed for cancelled events.

```text
72b4d7f4ccb844473c8bd8638880e352a561f186
feat: persist Blood Moon pre-combat cancellation mode

621429699a140a92e937aace93c6800cea5ebe10
fix: cancel pre-combat Blood Moon without advancing time

22829501b8725134356964181023f7dde8efe6d1
fix: suppress outcome replay for cancelled Blood Moon events
```

### P1 — Release random-event suppression on world teardown

`BloodMoonRandEventSuppression` could retain ownership across world unload and suppress random events in the next world.

```text
2da9849b0144656e944becb4cae36ea9bb93ca22
fix: release Blood Moon random-event suppression on unload
```

### P1 — Restrict boss withdrawal to the same interior context

Interior boss handling selected affected participants by XZ distance alone, allowing surface players near a dungeon entrance to be withdrawn.

Resolution:

- participant and boss must match `Character.InInterior` state;
- a resolved vanilla `Location.GetLocation` must also match for interior encounters when available;
- nonpersistent surface encounters only withdraw surface participants in encounter range.

```text
565fc4890fe34c678fdca1366df0a1a4c94a2ec9
fix: restrict Blood Moon boss withdrawal to encounter context
```

## Additional manual findings after the first review

The same freeze pass fixed issues not listed by the first review, including:

- frozen 06:00 transition broadcasts `ZNet.SendNetTime()` and aligns `EnvMan.m_totalSeconds`;
- dedicated-server restart gives persisted participants bounded reconnect grace before `Disconnected` becomes terminal;
- stale projectile/AOE attribution is blocked after global event end and across later event IDs;
- schedule search uses actual configured total year length rather than four equal seasons;
- forewarning/Marked Fader cloud clone is reactivated after vanilla environment object switching;
- teleport spatial suspension is heartbeat/TTL based and self-healing;
- rejected spawn cleanup is bound to the reporting peer's immutable ZDOID creator;
- zone ownership claims are independently verified against raw owned `SpawnSystem` ZDOs in the exact sector;
- completion reward replay is monotonic;
- Blood Craft multi-craft respects vanilla maximum stack size.

## Second Codex review

Triggered after the first-review fixes.

```text
review ID: 5012393467
reviewed commit: ddbb65649e
findings: 6
status: all accepted, fixed and resolved
```

### P1 — Allow unattributed hazards to damage participants

Null-attacker hits such as fall, lava and drowning were rejected by the central interaction matrix, making active participants immune to environmental hazards.

Resolution: unattributed damage remains vanilla-valid for active participants while remaining invalid against Blood enemies.

```text
2cb33311dcf07248aa772fd6889cb8024139244d
fix: allow environmental damage to Blood Moon participants
```

### P2 — Retry resync after the local player is created

The early `Game.Awake` resync could run with player ID zero and therefore omit private participant detail.

Resolution:

- `Player.SetLocalPlayer` requests a fresh resync;
- local fixed update retries while global/public/private snapshots are incomplete or mismatched;
- retries stop once the current participant detail is present.

```text
623efa951aef16f67e39feb23486e56371a7c4d0
fix: retry Blood Moon private resync after local player creation

e870e5805bd133f5fae60dbfb84eb54df4ba4066
fix: retry Blood Moon routing resync until snapshots align
```

### P2 — Enumerate both direct-attack patch targets

Two `HarmonyPatch` method-name attributes on one patch class did not independently patch both `Attack.DoMeleeAttack` and `Attack.DoAreaAttack`.

Resolution: the production patch now uses one `TargetMethods()` iterator that yields both exact methods. The temporary duplicate patch used during review was removed.

```text
9fc0c25e6f703b0749182193e72142ada113d603
refactor: integrate Blood Moon direct attack target enumeration

318dd017921c819f4a2bb2bc05a120b211df3c8f
refactor: remove redundant Blood Moon direct attack patch
```

A subsequent manual audit also confirmed that `Player` overrides `RaiseSkill`; the attack-context skill guard was therefore moved from the base `Character.RaiseSkill` method to the real `Player.RaiseSkill` override.

```text
065246b910a65180c37e8dce8602c8ba8f7086bc
fix: guard Blood Moon attack skill credit on Player override
```

### P2 — Keep surface targets eligible in mixed interior zones

A leased zone containing any interior participant previously routed the whole allowance to the interior path.

Resolution: the current mixed-context interceptor partitions real targets into interior/surface sets, chooses a real target context and falls back to the other valid context when the first path cannot produce a spawn.

```text
20b335652946012ebc75f8261b5b60b54d8842e9
fix: support mixed Blood Moon spawn contexts per zone
```

### P1 — Verify reported enemies actually died

Ownership and prefab eligibility alone allowed a modified zone owner to report a living enemy as dead.

Resolution:

- server requires the reported ZDO to have `ZDOVars.s_health <= 0` before credit;
- reports that are otherwise valid can wait briefly for delayed health replication;
- pending reports expire, are event-bound and are removed when invalid/deduplicated.

```text
d22bdf42af8625f2e33c1a5bdb5990d8b70e3632
fix: require server-observed death before Blood Moon credit

1f4be38cf1914bfdedb78c88f06188f29701a7fe
fix: defer Blood Moon death credit until health replication
```

### P2 — Include world identity in chronicle keys

`eventId` is world-local, so `m_knownTexts` could overwrite a same-day Blood Moon chronicle from another world.

Resolution: chronicle keys now include the stable world UID plus event ID.

```text
eeb2ebed25928ec926516ff5e9b06673b396529a
fix: scope Blood Moon chronicle keys by world
```

## Third Codex review

A third `@codex review` was requested after resolving the second-review findings. The bot acknowledged the request with `eyes`. Runtime-affecting commits continued during that review, including the corrected `Player.RaiseSkill` target and cancelled-outcome replay guard, so one final review must still be requested after the third result is processed.

## Current continuation point

1. read and validate the third Codex review on PR #42;
2. fix any confirmed findings in `feat/blood-moon`;
3. request one final Codex review against the then-current HEAD;
4. require all review threads to be resolved;
5. run final `master...feat/blood-moon` scope/freeze audit;
6. update `13_IMPLEMENTATION_REPORT.md` and this file with final review status;
7. leave PR #42 draft and unmerged for owner-side runtime playtest unless the owner explicitly changes that instruction.
