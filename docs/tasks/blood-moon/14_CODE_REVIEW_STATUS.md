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

Triggered by:

```text
@codex review
```

Codex review ID:

```text
5012228234
```

Reviewed commit:

```text
f11ff4d4ff
```

The first review reported five findings. All five were accepted as valid and fixed.

### P1 — Include skill baselines in resync details

Finding:

Explicit resync constructed a private participant detail without `LiveSkillBonusUsed` and `LastSkillReportSequence`. The client could therefore accept an all-zero baseline after already reporting skill gains, restart report sequencing and locally reopen the live bonus budget.

Resolution:

- explicit resync detail is normalized to the same skill baseline as normal targeted participant detail;
- `LiveSkillBonusUsed` is derived from persisted per-skill live bonus accounting;
- `LastSkillReportSequence` is copied from the persisted participant record.

Commit:

```text
86e7292681048a09fcd51e2f59365e98f1b2730a
fix: preserve Blood Moon skill baselines on resync
```

### P2 — Preserve valid Blood Craft items during active recovery

Finding:

Generic client-world cleanup ran before a single-player/listen-server state recovery could publish the matching event. `BloodCraft.CleanupLocal` therefore destroyed otherwise valid world/event/owner-bound temporary items during restart.

Resolution:

- generic world transitions now preserve marked inventory items until the loaded world/event snapshot can validate them;
- `EnsureWorldLoaded`, `CleanupClientWorldState` and ZNet teardown use a scoped preservation guard;
- explicit event boundaries such as Defeated, Withdrawn and resolution still perform destructive Blood Craft cleanup;
- stale/cross-world markers remain subject to existing world/event/owner validation after load.

Commits:

```text
831dc32b585562c4d071a3f47780f35deedb908d
fix: preserve Blood Craft items across world recovery

255ac6b7aa52cd58b54fcfbac98bba90ff261558
fix: preserve active Blood Craft inventory on world teardown

ddbb65649e721742864f4da468b83f76882f386d
fix: preserve Blood Craft through all world transitions
```

### P1 — Avoid advancing to dawn when disabling before combat

Finding:

Disabling Blood Moon during Forewarning or Marked entered the ordinary resolution pipeline, whose time step targets the scheduled 06:00 after the event night. During early forewarning this could skip several world days.

Resolution:

- pre-combat feature disable records a persisted `ResolutionCancelledBeforeCombat` fact;
- cancellation still performs fade/cleanup/world-system restoration;
- cancellation skips the frozen-morning time advance;
- cancellation skips reward, chronicle, DreamText and Rested outcome publication;
- the persisted flag keeps crash/restart behavior deterministic during cancellation resolution.

Commits:

```text
72b4d7f4ccb844473c8bd8638880e352a561f186
feat: persist Blood Moon pre-combat cancellation mode

621429699a140a92e937aace93c6800cea5ebe10
fix: cancel pre-combat Blood Moon without advancing time
```

### P1 — Release random-event suppression on world teardown

Finding:

`BloodMoonRandEventSuppression` retained its runtime ownership flag when unloading a Marked/Active/Resolving world, so the next world loaded in the same process could continue blocking random events.

Resolution:

ZNet teardown now explicitly releases Blood Moon random-event suppression ownership.

Commit:

```text
2da9849b0144656e944becb4cae36ea9bb93ca22
fix: release Blood Moon random-event suppression on unload
```

### P1 — Restrict boss withdrawal to the same interior context

Finding:

Unsupported interior boss handling selected affected participants only by XZ distance. Valheim dungeon interiors preserve nearby map XZ coordinates while being vertically separated, so a surface player near the entrance could incorrectly receive terminal Withdrawn.

Resolution:

- affected participant must match the boss `Character.InInterior` state;
- for interior bosses, a resolved vanilla `Location.GetLocation` must also match when available;
- nonpersistent surface boss encounters only withdraw surface participants within the encounter distance.

Commit:

```text
565fc4890fe34c678fdca1366df0a1a4c94a2ec9
fix: restrict Blood Moon boss withdrawal to encounter context
```

## Additional manual findings after the first review

The same freeze pass also fixed issues not listed by the first Codex review, including:

- frozen 06:00 transition now calls `ZNet.SendNetTime()` and aligns `EnvMan.m_totalSeconds`;
- dedicated-server restart gives persisted participants a bounded reconnect grace before `Disconnected` becomes terminal;
- stale Blood Moon projectile/AOE attribution is blocked after global event end and across later event IDs;
- schedule search uses the actual configured total year length rather than assuming four equal seasons;
- forewarning/Marked Fader cloud clone is explicitly reactivated after vanilla `EnvMan.SetEnv` object switching;
- teleport spatial suspension is heartbeat/TTL based and self-healing;
- rejected spawn cleanup is bound to the reporting peer's immutable ZDOID creator;
- zone ownership claims are independently verified against raw owned `SpawnSystem` ZDOs in the exact sector;
- completion reward replay is monotonic and cannot lower later skill progress;
- Blood Craft multi-craft respects vanilla maximum stack size.

## Second Codex review

A second `@codex review` request was submitted after the first-review fixes. At the time this checkpoint was written, the connector had acknowledged the request and the updated review result was still pending.

Continuation point:

1. read the second Codex review on PR #42;
2. validate each finding against the authoritative Blood Moon contracts and current `assemblies_combined` source;
3. fix confirmed findings in `feat/blood-moon` with logical commits;
4. repeat Codex review if code changes materially;
5. update this file and `13_IMPLEMENTATION_REPORT.md` with final review status;
6. leave PR #42 draft and unmerged for owner-side runtime playtest.
