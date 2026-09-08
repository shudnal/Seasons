# Blood Moon: PR #42 conformance review handoff

## 0. Scope and checkpoint

Date: 2026-09-08.

Repository: `shudnal/Seasons`.
Working branch: `feat/blood-moon`.
Target: `master`.
PR #42 must remain draft, open and unmerged.

Current review sequence:

1. project-conformance matrix audit -> `25_PROJECT_CONFORMANCE_MATRIX.md`;
2. full Codex project-conformance review of `231ae391891e330277868bf7733723333ea59521` -> six supported-runtime findings -> fixed/documented in `27_CODEX_CONFORMANCE_REVIEW_FIXES_2026-09-08.md`;
3. full Codex project-conformance review of `6449ded3e66611380f1535670286fb40fd5b2bbf` -> four supported-runtime findings -> fixed/documented in `28_CODEX_CONFORMANCE_REVIEW_ROUND2_FIXES_2026-09-08.md`;
4. next gate: another full project-conformance review of the exact PR head identified by the new `@codex review` request and Codex review summary.

Do not encode the final exact review head as a self-referential value in this file: updating the file itself creates a newer head. The PR review request/timeline is authoritative for the exact submitted and reviewed SHA. The last implementation/index checkpoint before this handoff finalization is `c9fca42adb16d365d7d8f590b8ac0aa8c44800bc`.

This checkpoint is continuation metadata, not a gameplay-design override. `25_PROJECT_CONFORMANCE_MATRIX.md` remains the cross-subsystem matrix; later corrective checkpoints provide implementation evidence for their explicit review findings.

## 1. Authoritative mechanics and precedence

The accepted mechanics are stored in `docs/tasks/CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md` and the subsystem documents it indexes. They do not depend on retrieving an old chat or rejected external drafts.

For current review work read, at minimum:

- `docs/tasks/CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`;
- `docs/tasks/blood-moon/25_PROJECT_CONFORMANCE_MATRIX.md`;
- this file;
- `docs/tasks/blood-moon/27_CODEX_CONFORMANCE_REVIEW_FIXES_2026-09-08.md`;
- `docs/tasks/blood-moon/28_CODEX_CONFORMANCE_REVIEW_ROUND2_FIXES_2026-09-08.md`;
- `docs/tasks/blood-moon/08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md`;
- later accepted decisions `16`, `20`, `22`, `23`, `24` in their explicit scope.

Historical reports `13`-`15` are evidence for the commits they describe, not authority over later accepted corrections.

Current decisions that must not regress include:

- next ordinary-sleep DreamText, not forced resolution DreamText;
- no global Player input suppression;
- genuine GoalReached remains sticky across later terminal exit;
- automatic display completion is not genuine combat success/reward;
- per-zone-owner spawning, no group-wide coordinator;
- existing ordinary monsters keep ordinary loot; marked extras are no-loot and cleanup-scoped;
- Blood Craft waives materials but keeps source station/level requirements;
- established source-centred progress sharing;
- forward-only clock reconciliation without gameplay rollback;
- supported-client distributed correctness is in scope; modified-client-only anti-cheat is not a release blocker.

## 2. Corrections from the first full project-conformance review

The review of `231ae391...` identified six issues, all corrected before review round 2:

- bounded validated death evidence survives dead-ZDO cleanup long enough for reordered source authorization;
- unsupported interior boss withdrawal requires matching navigation context;
- state snapshot recovery uses monotonic `PersistenceGeneration` rather than game time;
- client lease revision history resets on resolution/debug cleanup;
- group caps account live extras/pending reports against current topology across merge/split;
- live-skill deltas are profile-backed and retried/ACKed instead of fire-and-forget.

See document `27` for exact commits and runtime gates.

## 3. Corrections from the second full project-conformance review

The review of `6449ded3...` explicitly stated it was reviewing the complete repository contract/matrix and produced four additional findings. All four were confirmed, fixed, replied to in their inline threads, and resolved.

### Durable skill ACK

`BloodMoonPersistence.Save` now reports whether the current generation is recoverable from `.json/.new/.old`. `SkillGainAck` is sent only after the participant sequence is recoverable. A failed save leaves the client pending report intact for retry.

Primary commits:

```text
5fc7b08d65a150354808da678816797ae261f056
0da937ad2ae5c786ee80eb6aa96aaf077c80aba2
```

### Pre-resolution skill-report drain

Already-produced Active/AutoCompleting reports retry and are accepted through early `Resolving` before `PublishingOutcomes`. New RaiseSkill calls after the local Resolving snapshot remain vanilla-only and cannot create new Blood Moon contribution/x3 bonus.

Commit:

```text
0da937ad2ae5c786ee80eb6aa96aaf077c80aba2
```

### Obsolete lease revocation

A group/event lease that is about to become non-relevant is first sent to its owner with the same revision and `Allowance = 0`, before original lease cleanup removes it. Existing same-revision minimum semantics make this reorder-safe.

Commit:

```text
522e920584ebb9d56a8c4cc8097e5313b499ebd9
```

### Recovery after forward clock jump

Persistence load no longer directly advances gameplay phases before assigning `BloodMoonController.State`. The first normal server tick traverses the standard `EnterMarked`/`EnterActive`/AutoCompleting bridge and preserves required side effects before resolution.

Commit:

```text
58154db9a94c8fc2df8480ab2b250aeb8504a2a4
```

See document `28` for detailed invariants and runtime gates.

## 4. Documentation integrity repair

During final review preparation, an attempted partial edit accidentally replaced `25_PROJECT_CONFORMANCE_MATRIX.md` with a truncated fragment in commit `ce06aa987cd549a917cdd08e986deeacfae2d375`.

This was immediately repaired without manual reconstruction. Commit:

```text
e301daf6eda6a4db147143f8dc96263daa5ebd4b
```

restored the exact previously verified matrix blob:

```text
6efa1d5e97e71e93de571d30c0ace8aaca51d271
```

The authoritative index was then updated to explicitly list review checkpoints `26`-`28`. No production code was changed by this documentation repair.

## 5. Review requirements

The next Codex request must again be a **complete project-conformance review**, not a latest-delta review.

Required scope:

- complete effective `master...feat/blood-moon` behavior;
- all major sections of `25_PROJECT_CONFORMANCE_MATRIX.md`;
- all internal Harmony adapters that modify apparent production behavior;
- re-review the corrections in `27` and `28` for regressions/interactions;
- supported-client latency/reordering;
- ZDO/zone ownership migration;
- reconnect/restart/world switch;
- persistence failures and `.new/.old` recovery;
- live-skill pending report persistence, ACK only after recoverable save, and outcome-boundary drain;
- group merge/split topology accounting and obsolete-lease revocation;
- recovered forward time jumps and required phase side effects;
- ordinary mod interoperability.

For every finding require a normal supported-client/runtime reproduction path and point to the conflicting project requirement/matrix row. Do not promote intentionally fabricated RPC/marker or modified-client-only attacks to release blockers.

A clean result must identify the exact reviewed commit and explicitly state that the full PR was checked against the project contract/matrix.

## 6. Evidence limits

No assistant-side Seasons build, automated mod tests or Valheim run is claimed for the post-review corrections.

Owner-side compilation and `08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md` remain separate acceptance gates. Visual/VFX/fade tuning, balance and localization remain owner-side work.

PR #42 stays draft/open/unmerged until owner approval.
