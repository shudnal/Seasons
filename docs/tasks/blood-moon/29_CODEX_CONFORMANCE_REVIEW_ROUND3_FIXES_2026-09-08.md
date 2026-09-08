# Blood Moon: Codex project-conformance review round 3 fixes

## 0. Scope

Date: 2026-09-08.

Review target that produced these findings: `f55064e5239e7abadfa75287be9afc56d4d85914`.

This is a corrective evidence checkpoint for draft PR #42. It does not replace the product contract or `25_PROJECT_CONFORMANCE_MATRIX.md`. The review explicitly covered the complete PR against the repository mechanics/matrix and returned six additional supported-runtime findings.

No owner-side build or Valheim runtime validation is claimed by this document.

## 1. Explicit JSON contracts for durable live-skill reports

Finding: the profile-backed `LiveSkillReportRecord` and `PendingSkillGainReport` introduced by round 1 were routed through the Blood Moon controlled serializer but were not registered with `BloodMoonContractResolver`. Serialization could therefore throw before the report was persisted or sent.

Correction:

- both private nested types are now explicit entries in `BloodMoonContractResolver.FieldContracts`;
- the record contract contains world/event/player identity, ACK/sequence high-water values, local live-bonus high-water and the pending report list;
- the report contract contains sequence, skill and the base/live deltas.

Invariant: every Blood Moon-owned JSON type remains explicit; no fallback serialization of arbitrary Unity/runtime object graphs was enabled.

Runtime evidence:

- perform an eligible skill raise during Active;
- verify no JSON contract exception;
- disconnect/reconnect with a pending report and verify the record reloads.

## 2. Contiguous skill-report ACK high-water

Finding: the server previously accepted any sequence greater than `LastSkillReportSequence`. If sequence 2 arrived before still-pending sequence 1, ACK 2 caused the client to delete both profile-backed reports and permanently lose sequence 1 contribution.

Correction:

- `BloodMoonSkills.AcceptServerReport` is guarded so a new report may advance the server high-water only when `sequence == LastSkillReportSequence + 1`;
- duplicate/older and gap reports do not mutate contribution;
- `BloodMoonSkillReports.Accept` still persists the current server state before returning its ACK, so a gap receives only the current durable contiguous high-water;
- once the missing earlier report is accepted, later durable reports remain pending and retry normally;
- if synchronized skill eligibility changes while an already-created supported-client report is pending, the exact expected sequence is consumed without contribution so the sequence stream cannot remain permanently wedged.

Invariant: `SkillGainAck` means "all sequences through N are durably accepted/consumed", never "N is the largest report observed".

Runtime evidence:

- force delivery order 2 -> 1 -> 2;
- verify ACK sequence 0 -> 1 -> 2 and both eligible contributions are represented once;
- repeat across reconnect.

## 3. Revision-aware obsolete lease revocation

Finding: the round-2 zero-allowance revoke handled removed group IDs, but a merge/split commonly retains the same group ID and increments `GroupRevision`. The old delivered lease could therefore survive client-side while the server removed it and granted replacement revision capacity.

Correction:

- a lease is considered current only when event ID, group ID and `GroupRevision` all match current topology;
- before the original lease update can remove an obsolete revision, the owner receives the old lease revision with `Allowance = 0`;
- post-pass topology clamp uses the same revision-aware predicate;
- same-revision client minimum-allowance behavior keeps the revoke order-safe against delayed older renewals.

Invariant: server budget is not reclaimed from an obsolete topology revision before the client has been told that old revision has zero spendable allowance.

Runtime evidence:

- split and merge a group while the owner still has positive allowance;
- delay/reorder old renewal, zero revoke and replacement lease;
- verify old revision cannot spawn after revoke and live extras remain counted.

## 4. Temporary summon cleanup uses monotonic realtime

Finding: personal/global temporary-summon cleanup watches used mutable SeasonState time. Normal resolution subsequently advances world/net time toward morning, which could prematurely expire the pending-replication cleanup watch.

Correction:

- cleanup watch deadlines are based on `Time.realtimeSinceStartup`;
- cleanup scan cadence is also based on realtime;
- the existing public method parameters remain for call-site compatibility but no longer determine watch lifetime.

Invariant: `skiptime`, final morning advance, realtime-calendar adjustments and backward clock movement cannot shorten or extend the five-second temporary-summon observation window.

Runtime evidence:

- create a temporary summon immediately before personal/global cleanup;
- delay its marker/ZDO observation while advancing event/world time;
- verify the realtime watch continues scanning for the full intended interval.

## 5. Diagnostic spawn must use the frozen event prefab

Finding: `seasons bloodmoon spawn <prefab>` allowed any `MonsterAI`, but production cleanup intentionally verifies extras against the event-frozen single prefab. A diagnostic Troll in an event frozen to Draugr could survive cleanup with a permanent Blood Moon no-loot marker.

Correction:

- diagnostic spawn is now rejected unless the resolved prefab is exactly the event-frozen extra-enemy prefab;
- if the pool has not yet been frozen, the diagnostic path freezes/persists it first;
- production extra validation and cleanup rules are unchanged.

Invariant: every diagnostic marked extra uses the same cleanup-verifiable prefab identity as production extras.

Runtime evidence:

- during an event frozen to prefab A, verify `spawn B` is rejected;
- verify `spawn A` is accepted and removed by ordinary resolution cleanup.

## 6. Lost spawn-report recovery before capacity reclaim

Finding: an extra ZDO could be successfully instantiated and force-replicated while its single routed spawn-report RPC was lost. When the lease later expired/invalidated, the server could reclaim the reservation without ever adding that still-live marked creature to `ExtraEnemyZdos`, allowing replacement capacity and a temporary cap overshoot.

Correction:

- before lease pruning/reconciliation and new capacity allocation, the server scans its already-replicated ZDO registry for current-event marked extras;
- discovery accepts only the event's frozen prefab through the existing `BloodMoonSpawnReportValidation.IsAllowedExtraEnemyZdo` contract;
- newly discovered IDs are inserted into `ExtraEnemyZdos` and persisted before server/group budgets are calculated;
- normal report/pending-report paths remain valid and idempotent; discovery is a recovery path, not a replacement for normal reporting.

Invariant: once a valid marked extra is replicated to the server, loss of its report RPC cannot make that live creature disappear from later server/group cap accounting.

Runtime evidence:

- suppress/drop the spawn-report while allowing the marked ZDO to replicate;
- expire/invalidate the originating lease;
- verify discovery adds the live extra before replacement allowance is calculated;
- repeat across owner migration/disconnect.

## 7. Files changed by this review round

- `BloodMoon/BloodMoonJsonContracts.cs`
- `BloodMoon/BloodMoonSkillReportOrder.cs`
- `BloodMoon/BloodMoonLeaseBudgetReconciliation.cs`
- `BloodMoon/BloodMoonSummons.cs`
- `BloodMoon/BloodMoonDiagnosticSpawnGuard.cs`
- this checkpoint

## 8. Next review gate

The next Codex request must again be a full project-conformance review of the exact current PR head, not a latest-delta-only review.

Required reading before code conclusions:

1. `docs/tasks/CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`;
2. `25_PROJECT_CONFORMANCE_MATRIX.md`;
3. `26_PR42_CONFORMANCE_REVIEW_HANDOFF.md`;
4. correction checkpoints `27`, `28`, and this `29`;
5. `08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md`;
6. later authoritative decisions `16`, `20`, `22`, `23`, `24` in their explicit scopes.

Re-test all six round-3 corrections for ordinary latency/reordering, reconnect, ownership/topology migration and persistence failure. Also challenge them for new regressions against the rest of the matrix.

Keep PR #42 draft, open and unmerged. Do not claim owner-side build/runtime evidence that has not actually been performed.
