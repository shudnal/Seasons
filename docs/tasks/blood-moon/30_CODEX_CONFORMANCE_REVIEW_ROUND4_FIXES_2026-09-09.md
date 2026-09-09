# PR #42 project-conformance review — round 4 fixes

Date: 2026-09-09

This checkpoint records the supported-runtime findings from the fourth documentation-aware Codex review after the round-3 checkpoint (`29_CODEX_CONFORMANCE_REVIEW_ROUND3_FIXES_2026-09-08.md`) and the corrections applied before requesting the next complete project-conformance review.

This file is implementation/review evidence. It does not replace the accepted gameplay contract in the authoritative index and `25_PROJECT_CONFORMANCE_MATRIX.md`.

## Review scope

The review was evaluated under `16_CLIENT_TRUST_BOUNDARY.md`: compatible/unmodified clients, normal latency/reordering, ZDO ownership migration, reconnect/restart/world teardown, recoverable persistence failures and ordinary interoperability are in scope. Modified-client-only forgery is not a release blocker.

All seven findings were accepted as supported-runtime correctness issues.

## 1. Classic project omitted the contiguous skill-order adapter

**Finding:** `BloodMoonSkillReportOrder.cs` contained required sequencing behavior but was not included by the classic explicit `<Compile>` list.

**Correction:** the contiguous-sequence implementation was moved into the already compiled `BloodMoonResyncSkillBaseline.cs`; the standalone source was removed. The classic project now ships the adapter without introducing a second compile-list surface.

The server accepts only `LastSkillReportSequence + 1`. Out-of-order reports remain pending at the normal client retry layer. If synchronized eligibility changes while an already-produced report is pending, that exact contiguous sequence may be consumed without contribution so later reports do not remain permanently blocked.

## 2. Private skill baselines could advertise non-durable state

**Finding:** a failed server persistence write could leave the in-memory participant skill high-water ahead of the recoverable snapshot while targeted participant details/resync exposed that in-memory value as an ACK-equivalent baseline.

**Correction:** `BloodMoonSkillDurableBaseline` captures `(LastSkillReportSequence, LiveSkillBonusUsed)` only after a successful `BloodMoonPersistence.Save`, or from a loaded persisted snapshot. Both the regular private detail serializer and explicit resync detail are clamped to that durable baseline. If no durable baseline has been observed for the event/player, zero is published rather than the newer in-memory value.

A compile-oriented sanity pass also found and corrected the missing `Newtonsoft.Json` import after this adapter was consolidated into `BloodMoonResyncSkillBaseline.cs`.

## 3. Classic project omitted the diagnostic spawn guard

**Finding:** `BloodMoonDiagnosticSpawnGuard.cs` was not included by the classic explicit `<Compile>` list, so the shipped assembly could omit the protection that keeps diagnostic marked extras inside production cleanup provenance.

**Correction:** the guard was moved into the already compiled `BloodMoonGameplayGuards.cs`; the standalone source was removed. Diagnostic `spawn <prefab>` during a live Blood Moon accepts only the event-frozen production extra-enemy prefab.

## 4. Obsolete lease allowance could be reclaimed before client revocation

**Finding:** topology reconciliation could clamp an obsolete lease to zero before the later revocation branch inspected it, erasing evidence that the client had already received positive allowance. Server capacity could then be reused while the old client still possessed spendable tokens.

**Correction:** `BloodMoonRetiredLeaseReservations.RetireObsolete` runs before normal lease reconciliation. For every already-delivered allowance unit on a topology-obsolete lease it creates a short-lived server reservation token, then sends a same-revision `Allowance = 0` revocation before the old lease is removed/reclaimed.

The reservation uses the same realtime domain as production pending-spawn reports. `ProcessPendingReports` leaves the synthetic missing-ZDO record pending until its realtime expiry; the reservation helper removes it when consumed or expired. Thus ordinary lease expiry/`skiptime` cannot silently return capacity early.

## 5. Lost-report discovery lacked lease provenance

**Finding:** discovering any current-event marked ZDO with the frozen prefab was insufficient. A spawn created from an old delivered lease revision could survive after that allowance had already been returned and then be admitted without consuming capacity.

**Correction:** replicated-extra discovery now requires one real accounting token:

- a matching retained obsolete-lease reservation; or
- a matching current lease with positive allowance and current group revision.

The match includes event, original group/zone, creator/owner peer and frozen pool identity. Recovery consumes exactly one matching token before adding the ZDO to `ExtraEnemyZdos`. A marked ZDO without provenance is not promoted into server cap accounting merely because its report RPC was lost.

## 6. Death progress was not crash-durable before lethal evidence consumption

**Finding:** a valid death report could mutate progress/dedup state and consume the in-memory matched lethal credit before a persistence write succeeded. A server crash in that window could reload the pre-kill snapshot after both the dead ZDO and matched credit had disappeared.

**Correction:** enemy-death progress now has a profile-backed ACK/retry transaction:

1. the unmodified enemy owner persists a pending death record before sending the normal report;
2. the first report still carries no replay points and follows the ordinary dead-ZDO + matched lethal-credit validation path;
3. confirmed lethal credit is not finally consumed and the client is not ACKed until `ReportedEnemyDeaths` plus the awarded progress are recoverable from `BloodMoonPersistence`;
4. failed server writes retain an in-memory pending transaction, block publication of the undurable mutation and prevent resolution from completing past it;
5. after a server restart, the client profile can replay the same death with the deterministic `10 * replicated level` point value that was stored before the first send; under the accepted trust boundary this compatible-client-owned replay fact is a recovery source when the server's ephemeral dead-ZDO/credit evidence was lost;
6. duplicate/reconnect delivery after a successful persisted snapshot is ACKed idempotently and does not award the death twice.

The persistence-load adapter was also corrected so loading an already durable snapshot only captures its durable markers; it no longer performs an unnecessary write merely to classify the loaded state as durable.

## 7. Late join could enter vanilla death before combat state converged

**Finding:** an Active-event late joiner could be made `Fighting` server-side before the client had received the matching participant/combat state. A lethal event in that transport window could therefore reach the ordinary tombstone/respawn/skill-loss path.

**Correction:** late-join enrollment is now staged as a two-phase transition without adding a new public participant enum:

- the server first persists the late joiner as `Marked`;
- a dedicated server-to-client `EnrollmentPrepare` RPC is independent of the CCS/client-action queue and installs the local death guard;
- the client ACKs readiness;
- only after that ACK does the server persist and publish `Fighting`;
- the prepare RPC is retried while the durable participant remains late-join `Marked`;
- the local guard is also proactive when an `Active`/`AutoCompleting` global snapshot has arrived but the local participant record has not yet arrived, closing the pre-prepare ordering window;
- protection has no arbitrary wall-clock timeout. It is released by matching `Fighting`/terminal state, event resolution, a genuinely newer event id, or network/world teardown.

The guard only prevents the forbidden vanilla death transition. It does not grant Blood Moon reward/progress eligibility before the server has durably entered `Fighting`.

## Static sanity checks performed before the next review

- `Seasons.csproj` explicitly compiles `BloodMoonResyncSkillBaseline.cs`, `BloodMoonGameplayGuards.cs`, `BloodMoonLeaseClientTime.cs`, `BloodMoonEnemyDeathPending.cs`, and `BloodMoonNetworkSessionLifecycle.cs`.
- The removed standalone skill-order and diagnostic-guard files are no longer relied upon for shipped behavior.
- `Character.CheckDeath` behavior was checked against `shudnal/assemblies_combined` before extending the late-join guard.
- Production `BloodMoonSpawner.ProcessPendingReports` was inspected to confirm pending-report expiry is already `Time.realtimeSinceStartup`, matching retained lease reservation expiry.
- No assistant-side Valheim build or runtime execution is claimed for this checkpoint.

## Required runtime gates

The next owner-side multiplayer/runtime pass should include at minimum:

1. out-of-order skill reports across a failed server save and reconnect;
2. topology merge/split with a delivered positive lease, delayed/lost spawn report and replacement lease pressure at both group/server caps;
3. server persistence failure immediately after an enemy-death award, followed separately by retry without restart and by server restart/reconnect;
4. late join during Active with delayed/reordered global/routing/prepare traffic, including lethal environmental damage before the participant snapshot and lethal Blood damage immediately after `Fighting` arrives;
5. diagnostic extra spawn and dawn cleanup from the shipped classic project build.

## Next review requirement

Request a complete project-conformance review of the exact new PR head. Codex must read the authoritative index, `25_PROJECT_CONFORMANCE_MATRIX.md`, checkpoints `26`-`30`, the acceptance checklist, and later decisions `16`, `20`, `22`, `23`, and `24`, then inspect the full effective `master...feat/blood-moon` implementation including internal Harmony adapters and the classic project compile list. The review must not be limited to this round's delta.
