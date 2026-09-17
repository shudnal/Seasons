# Blood Moon: Codex project-conformance review round 2 corrections

Date: 2026-09-08.

Repository: `shudnal/Seasons`.
Branch: `feat/blood-moon`.
PR: #42, draft/open/unmerged.

Reviewed baseline for this round:

```text
6449ded3e66611380f1535670286fb40fd5b2bbf
```

The review explicitly operated against the complete repository contract/matrix, not only the latest delta. It produced four supported-client/runtime findings. All four are treated as confirmed and corrected below.

This file is corrective implementation evidence. It does not replace `25_PROJECT_CONFORMANCE_MATRIX.md` or change accepted gameplay design.

## 1. Skill-report ACK only after recoverable server persistence

Finding: `BloodMoonSkillReports.Accept` acknowledged the participant sequence even when `BloodMoonPersistence.Save` failed. A disk-full/transient I/O failure could therefore make the client delete its profile-backed pending report while restart recovered a server state predating that sequence.

Corrections:

- `BloodMoonPersistence.Save` now returns whether the generation is durably recoverable;
- a fully written `.new` snapshot counts as recoverable because `Load` validates and orders `.json`, `.new` and `.old` by `PersistenceGeneration`;
- an unsuccessful write before a complete `.new` exists returns false;
- skill-report ACK is sent only after the current participant high-water is recoverable;
- if persistence fails after applying a report in memory, the client keeps its pending report;
- a retry is sequence-idempotent, attempts persistence again, and ACKs only after persistence succeeds.

Primary commit:

```text
5fc7b08d65a150354808da678816797ae261f056
```

Follow-up skill-flow commit:

```text
0da937ad2ae5c786ee80eb6aa96aaf077c80aba2
```

Runtime gates:

- make server state directory temporarily unwritable while a skill report arrives;
- verify no ACK removes the client pending report;
- restore writes and verify retry is accepted once and ACKed;
- restart after `.new` is written but canonical rotation fails and verify the accepted sequence survives.

## 2. Drain pre-resolution skill reports without creating new resolving-period reports

Finding: a skill raise produced immediately before `Resolving` could arrive after `State.IsCombatLive` became false. The server discarded it, while the client stopped retrying on the resolving snapshot, so completion contribution could omit a legitimate pre-resolution raise.

Accepted boundary:

```text
new Blood Moon skill accounting freezes when the client enters Resolving
already-produced Active/AutoCompleting reports may drain before outcome capture
```

Corrections:

- server accepts normal participant skill-report sequences during Active/AutoCompleting and during early `Resolving` before `PublishingOutcomes`;
- client retry continues through the same early-resolution drain window;
- `BloodMoonSkills.EnsureLocalEvent` is suppressed for new raises once the local global phase is `Resolving`, so ordinary Run/Jump/etc. gains remain vanilla-only and cannot create new Blood Moon contribution or x3 bonus after combat freeze;
- `PublishingOutcomes` remains the hard serialization boundary; late reports after that step are not added to the event reward payload.

Primary commit:

```text
0da937ad2ae5c786ee80eb6aa96aaf077c80aba2
```

Runtime gates:

- raise an eligible skill immediately before forced end with artificial latency;
- verify the pending report is retried during early resolution and contributes once;
- raise Run/Jump after the resolving snapshot and verify ordinary skill gain remains vanilla, with no new Blood Moon report/bonus;
- lose ACK during early resolution and verify duplicate retry does not duplicate contribution.

## 3. Revoke obsolete group leases before topology removes them

Finding: merge/split topology could make a prior group ID obsolete. The pre-pass set its server allowance to zero, but original `UpdateServerLeases` removed the non-relevant lease before the post-pass could send the zero clamp. The zone owner could keep spending its previous positive client lease until local expiry while replacement-group leases used reclaimed budget.

Correction:

- before original `UpdateServerLeases` removes an obsolete group/event lease, the server sends a same-revision lease with `Allowance = 0` to the owner;
- client same-revision handling already keeps the minimum allowance seen, so a later reordered positive renewal cannot replenish the revoked revision;
- reports from a removed group are still rejected by server group/lease validation;
- current topology accounting and post-pass clamps from review round 1 remain unchanged.

Commit:

```text
522e920584ebb9d56a8c4cc8097e5313b499ebd9
```

Runtime gates:

- create two active groups with live leases, then merge them while clients retain allowance;
- split a group while old-zone owners have allowance;
- inject ordinary latency/reordering and verify old revisions cannot produce accepted over-cap extras;
- confirm lowering/revocation never deletes already-live extras.

## 4. Recovered forward jumps use normal controller phase transitions

Finding: `BloodMoonPersistence.Load` called `BloodMoonRecoverySchedule.ReconcileLoadedState` before assigning the returned object to `BloodMoonController.State`. Direct recovery mutation could therefore jump Forewarning/Marked/Active to Resolving (or directly to another combat phase) without executing `EnterMarked`/`EnterActive` side effects such as enrollment, suppression, environment setup and group/spawn initialization.

Correction:

- `ReconcileLoadedState` no longer mutates gameplay phase forward while loading persistence;
- it may log that a later schedule target was observed, but returns the persisted state unchanged;
- after `Controller.State` is assigned, the first normal server tick evaluates the frozen schedule and routes through `AdvanceToExpectedPhase`;
- the existing large-forward-jump bridge can then see the actual controller state and traverses required `Marked -> Active -> AutoCompleting` side effects before normal forced-end resolution;
- backward time remains non-rollback by design.

Commit:

```text
58154db9a94c8fc2df8480ab2b250aeb8504a2a4
```

Runtime gates:

- persist Forewarning and restart after Active time;
- persist Forewarning and restart after 05:45 / after 06:00;
- persist Marked and restart after forced end;
- verify enrollment/suppression/Active initialization occurs before resolution;
- verify no backward-time phase rollback.

## 5. Review-thread handling

The four Codex findings from the `6449ded3...` review must be answered individually with the concrete fix and then resolved only after the corresponding code is present.

After these corrections, request another complete project-conformance Codex review on the exact new head. The request must explicitly require:

- the full `master...feat/blood-moon` effective behavior, not latest delta only;
- the authoritative index and `25_PROJECT_CONFORMANCE_MATRIX.md`;
- checkpoints `26`, `27` and this `28`;
- re-review of persistence generation/save durability, skill ACK/retry/drain, topology cap/revocation and recovered time jumps;
- supported-client latency/reordering, ownership migration, reconnect/restart and persistence failures;
- exact reviewed SHA and explicit confirmation of project-contract scope.

## 6. Evidence limits

No assistant-side Seasons build, automated mod test or Valheim run is claimed for these corrections.

Owner-side build and runtime acceptance remain required, especially the new failure/reorder scenarios listed above.
