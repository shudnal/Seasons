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
4. full Codex project-conformance review of `f55064e5239e7abadfa75287be9afc56d4d85914` -> six supported-runtime findings -> fixed/documented in `29_CODEX_CONFORMANCE_REVIEW_ROUND3_FIXES_2026-09-08.md`;
5. next gate: another full project-conformance review of the exact PR head identified by the new `@codex review` request and Codex review summary.

Do not encode the final exact review head as a self-referential value in this file: updating the file itself creates a newer head. The PR review request/timeline is authoritative for the exact submitted and reviewed SHA.

This checkpoint is continuation metadata, not a gameplay-design override. `25_PROJECT_CONFORMANCE_MATRIX.md` remains the cross-subsystem matrix; later corrective checkpoints provide implementation evidence for their explicit review findings.

## 1. Authoritative mechanics and precedence

The accepted mechanics are stored in `docs/tasks/CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md` and the subsystem documents it indexes. They do not depend on retrieving an old chat or rejected external drafts.

For current review work read, at minimum:

- `docs/tasks/CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`;
- `docs/tasks/blood-moon/25_PROJECT_CONFORMANCE_MATRIX.md`;
- this file;
- `docs/tasks/blood-moon/27_CODEX_CONFORMANCE_REVIEW_FIXES_2026-09-08.md`;
- `docs/tasks/blood-moon/28_CODEX_CONFORMANCE_REVIEW_ROUND2_FIXES_2026-09-08.md`;
- `docs/tasks/blood-moon/29_CODEX_CONFORMANCE_REVIEW_ROUND3_FIXES_2026-09-08.md`;
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

The review of `6449ded3...` produced four additional findings, all confirmed, fixed, replied to and resolved:

- `SkillGainAck` is emitted only after the participant sequence is recoverable from server state persistence;
- Active/AutoCompleting skill reports already produced before resolution continue draining until `PublishingOutcomes`, while new resolving-period `RaiseSkill` remains vanilla-only;
- topology-obsolete leases are explicitly revoked with same-revision zero allowance before their reservation is reclaimed;
- persistence recovery defers forward phase catch-up until `BloodMoonController.State` is assigned and the normal controller transition path can execute required side effects.

See document `28` for exact commits and runtime gates.

## 4. Corrections from the third full project-conformance review

The review of `f55064e...` produced six additional supported-runtime findings. All six were confirmed, fixed, replied to and resolved:

- durable live-skill report record types are explicit controlled-JSON contracts;
- server skill-report ACK is a contiguous durable high-water and cannot skip a missing sequence;
- lease revocation also treats a surviving group ID with a changed `GroupRevision` as obsolete;
- temporary-summon cleanup watches use monotonic Unity realtime instead of mutable SeasonState/net/calendar time;
- diagnostic marked extras are restricted to the event-frozen production prefab so ordinary cleanup can verify them;
- already-replicated valid marked extra ZDOs are discovered before lease reclaim/new allocation, so loss of the report RPC cannot make a live extra disappear from cap accounting.

See document `29` for exact commits, invariants and runtime evidence gates.

## 5. Documentation integrity repair

During earlier review preparation, an attempted partial edit accidentally replaced `25_PROJECT_CONFORMANCE_MATRIX.md` with a truncated fragment. It was immediately repaired without manual reconstruction by restoring the exact previously verified matrix blob:

```text
6efa1d5e97e71e93de571d30c0ace8aaca51d271
```

A later accidental temporary file was also created and removed; Git comparison reported no final file delta from that artifact. Production code was not changed by either documentation repair.

## 6. Review requirements

The next Codex request must again be a **complete project-conformance review**, not a latest-delta review.

Required scope:

- complete effective `master...feat/blood-moon` behavior;
- all major sections of `25_PROJECT_CONFORMANCE_MATRIX.md`;
- all internal Harmony adapters that modify apparent production behavior;
- re-review corrections in `27`, `28`, and `29` for regressions/interactions;
- supported-client latency/reordering;
- ZDO/zone ownership and combat-group topology migration;
- reconnect/restart/world switch;
- persistence failures and `.new/.old` recovery;
- durable live-skill record serialization, contiguous sequence ACK, reconnect retry and outcome-boundary drain;
- group merge/split topology accounting and revision-aware obsolete-lease revocation;
- replicated-extra discovery under lost spawn-report delivery;
- temporary summon cleanup under final morning advancement and clock changes;
- recovered forward time jumps and required phase side effects;
- diagnostic extra cleanup semantics;
- ordinary mod interoperability.

For every finding require a normal supported-client/runtime reproduction path and point to the conflicting project requirement/matrix row. Do not promote intentionally fabricated RPC/marker or modified-client-only attacks to release blockers.

A clean result must identify the exact reviewed commit and explicitly state that the full PR was checked against the project contract/matrix.

## 7. Evidence limits

No assistant-side Seasons build, automated mod tests or Valheim run is claimed for the post-review corrections.

Owner-side compilation and `08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md` remain separate acceptance gates. Visual/VFX/fade tuning, balance and localization remain owner-side work.

PR #42 stays draft/open/unmerged until owner approval.
