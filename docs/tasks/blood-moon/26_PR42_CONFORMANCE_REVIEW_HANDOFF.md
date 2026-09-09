# Blood Moon: PR #42 conformance review handoff

## 0. Scope and checkpoint

Date: 2026-09-09.

Repository: `shudnal/Seasons`.
Working branch: `feat/blood-moon`.
Target: `master`.
PR #42 must remain draft, open and unmerged.

Current review sequence:

1. project-conformance matrix audit -> `25_PROJECT_CONFORMANCE_MATRIX.md`;
2. full Codex project-conformance review of `231ae391891e330277868bf7733723333ea59521` -> six supported-runtime findings -> fixed/documented in `27_CODEX_CONFORMANCE_REVIEW_FIXES_2026-09-08.md`;
3. full Codex project-conformance review of `6449ded3e66611380f1535670286fb40fd5b2bbf` -> four supported-runtime findings -> fixed/documented in `28_CODEX_CONFORMANCE_REVIEW_ROUND2_FIXES_2026-09-08.md`;
4. full Codex project-conformance review of `f55064e5239e7abadfa75287be9afc56d4d85914` -> six supported-runtime findings -> fixed/documented in `29_CODEX_CONFORMANCE_REVIEW_ROUND3_FIXES_2026-09-08.md`;
5. full Codex project-conformance review of the later round-3 head -> seven supported-runtime findings -> fixed/documented in `30_CODEX_CONFORMANCE_REVIEW_ROUND4_FIXES_2026-09-09.md`;
6. accepted post-review presentation decision -> `31_LATE_BIOME_PRESENTATION_POLICY.md`;
7. full Codex project-conformance review of `d8f540558b28c9d341a47ee735ba12d7a42a6e5a` -> eight supported-runtime findings -> fixed/documented in `32_CODEX_CONFORMANCE_REVIEW_ROUND5_FIXES_2026-09-09.md`;
8. full Codex project-conformance review of `cd7dffaab1d70647dafb51a9ef49aa84038e2714` -> eight supported-runtime findings -> interrupted correction pass resumed and completed in `33_CODEX_CONFORMANCE_REVIEW_ROUND6_FIXES_2026-09-09.md`;
9. next gate: another full project-conformance review of the exact PR head identified by the new `@codex review` request and Codex review summary.

Do not encode the final exact review head as a self-referential value in this file: updating the file itself creates a newer head. The PR review request/timeline is authoritative for the exact submitted and reviewed SHA.

This checkpoint is continuation metadata, not a gameplay-design override. `25_PROJECT_CONFORMANCE_MATRIX.md` remains the cross-subsystem matrix; later corrective checkpoints provide implementation evidence for their explicit review findings. `31_LATE_BIOME_PRESENTATION_POLICY.md` is an accepted product decision in its explicit presentation scope.

## 1. Authoritative mechanics and precedence

The accepted mechanics are stored in `docs/tasks/CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md` and the subsystem documents it indexes. They do not depend on retrieving an old chat or rejected external drafts.

For current review work read, at minimum:

- `docs/tasks/CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`;
- `docs/tasks/blood-moon/25_PROJECT_CONFORMANCE_MATRIX.md`;
- this file;
- `docs/tasks/blood-moon/27_CODEX_CONFORMANCE_REVIEW_FIXES_2026-09-08.md`;
- `docs/tasks/blood-moon/28_CODEX_CONFORMANCE_REVIEW_ROUND2_FIXES_2026-09-08.md`;
- `docs/tasks/blood-moon/29_CODEX_CONFORMANCE_REVIEW_ROUND3_FIXES_2026-09-08.md`;
- `docs/tasks/blood-moon/30_CODEX_CONFORMANCE_REVIEW_ROUND4_FIXES_2026-09-09.md`;
- `docs/tasks/blood-moon/31_LATE_BIOME_PRESENTATION_POLICY.md`;
- `docs/tasks/blood-moon/32_CODEX_CONFORMANCE_REVIEW_ROUND5_FIXES_2026-09-09.md`;
- `docs/tasks/blood-moon/33_CODEX_CONFORMANCE_REVIEW_ROUND6_FIXES_2026-09-09.md`;
- `docs/tasks/blood-moon/08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md`;
- later accepted decisions `16`, `20`, `22`, `23`, `24`, and `31` in their explicit scope.

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
- supported-client distributed correctness is in scope; modified-client-only anti-cheat is not a release blocker;
- Ashlands and Deep North remain full Blood Moon gameplay biomes, but Blood Moon atmospheric/environmental visual overrides are suppressed locally while the player is in those biomes; `SE_BloodMoon`, Bloodlust, combat, progress, spawning and participant state remain unchanged across the boundary.

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

## 5. Corrections from the fourth full project-conformance review

The next documentation-aware full review produced seven additional supported-runtime findings. All seven were confirmed, fixed, replied to and resolved before the late-biome presentation decision:

- the contiguous skill-order adapter is part of the actually compiled classic project rather than an omitted standalone source;
- private skill baselines expose only persistence-recoverable high-water state;
- the diagnostic spawn guard is part of the actually compiled project;
- obsolete lease allowance is retired/revoked before ordinary budget clamping can erase the delivered allowance value;
- replicated-extra discovery consumes matching current/retired lease provenance instead of trusting event+prefab identity alone;
- accepted enemy-death progress remains profile/server retryable until its dedupe marker and awarded progress are recoverable from persistence;
- Active-event late join is staged as durable `Marked`, protected client-side before combat routing converges, and promoted to durable `Fighting` only after the direct prepare/ready handshake.

See document `30` for detailed fixes, sanity corrections and runtime gates.

## 6. Accepted late-biome presentation decision

`31_LATE_BIOME_PRESENTATION_POLICY.md` is authoritative for Ashlands/Deep North presentation.

The decision deliberately separates gameplay from atmosphere:

- Blood Moon remains active mechanically in both biomes;
- the Blood Moon status, Bloodlust, targeting/damage rules, progress, skills, spawning, boss rules, Defeated/recovery and outcomes do not toggle at biome borders;
- atmospheric Blood Moon overrides are suppressed while the local `EnvMan` biome is `AshLands` or `DeepNorth`;
- this includes the current forced environment, color interpolation, Fader-derived red fog/cloud layer and Forewarning/Marked tint, plus future moon-color/ray overrides;
- entering an excluded biome releases only the Blood Moon-owned force-environment lease and restores displaced/native presentation;
- leaving it reapplies the current event visual factor and reacquires `Seasons_BloodMoon` only when the current phase still requires weather suppression;
- resolution fade remains an event/UI transition and is not suppressed by this atmospheric biome gate.

Future environmental presentation code must use `BloodMoonPresentationPolicy.AllowsEnvironmentalOverridesForCurrentBiome()` instead of inventing independent biome checks.

### Later corrections: rounds 5 and 6

Checkpoint `32` records eight corrections concerning server-owned death retention, pre-outcome report draining, zone-owner lease retirement, pending-spawn token accounting, world-scoped skill records, duplicate prepare and durable personal terminal facts.

Checkpoint `33` completes the following review's eight findings and the interrupted implementation pass. It requires verified profile storage before retention ACK and replay, preserves pending pre-disconnect death transactions and unacknowledged terminal evidence, uses readiness for normal/catch-up combat starts, preserves the first personal terminal reason, isolates the drain timeout by world/state lifetime, and closes late-biome readiness/orphaned-force-environment paths. Its follow-up commit also fixes pending death-evidence consumption/order and local terminal-evidence lookup after reconnect.

Read both checkpoints rather than treating earlier implementation descriptions as proof that those edge cases were already correct. The bounded network drain does not bypass the event-state persistence gate before immutable outcome capture.

## 7. Documentation integrity repair

During earlier review preparation, an attempted partial edit accidentally replaced `25_PROJECT_CONFORMANCE_MATRIX.md` with a truncated fragment. It was immediately repaired without manual reconstruction by restoring the exact previously verified matrix blob:

```text
6efa1d5e97e71e93de571d30c0ace8aaca51d271
```

A later accidental temporary file was also created and removed; Git comparison reported no final file delta from that artifact. Production code was not changed by either documentation repair.

## 8. Review requirements

The next Codex request must again be a **complete project-conformance review**, not a latest-delta review.

Required scope:

- complete effective `master...feat/blood-moon` behavior;
- all major sections of `25_PROJECT_CONFORMANCE_MATRIX.md`;
- all internal Harmony adapters that modify apparent production behavior;
- re-review corrections in `27`, `28`, `29`, `30`, `32`, and `33` for regressions/interactions;
- accepted presentation decision `31` and its implementation;
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
- readiness-gated normal, catch-up and late-join enrollment/death protection;
- verified profile retention and its alternate periodic-replay routes;
- first terminal-reason preservation and pending evidence across outcome/restart boundaries;
- server-owned pre-disconnect kill retention without reopening terminal participation;
- world/state-scoped bounded drain lifetime and persistence-before-outcome capture;
- Ashlands/Deep North boundary crossing in Forewarning, Marked, Active, GoalReached and early Resolving, verifying presentation-only suppression without gameplay-state changes;
- ordinary mod interoperability.

For every finding require a normal supported-client/runtime reproduction path and point to the conflicting project requirement/matrix/accepted-decision row. Do not promote intentionally fabricated RPC/marker or modified-client-only attacks to release blockers.

A clean result must identify the exact reviewed commit and explicitly state that the full PR was checked against the project contract/matrix and current accepted decisions including `31`.

## 9. Evidence limits

No assistant-side Seasons build, automated mod tests or Valheim run is claimed for the post-review corrections or the late-biome presentation change.

Owner-side compilation and `08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md` remain separate acceptance gates. Visual/VFX/fade tuning, balance and localization remain owner-side work.

PR #42 stays draft/open/unmerged until owner approval.
