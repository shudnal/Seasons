# Blood Moon: Codex project-conformance review round 7 fixes

## Status and scope

Date: 2026-09-09.

Repository: `shudnal/Seasons`. Working branch: `feat/blood-moon`. PR #42 remains draft, open and unmerged.

This checkpoint records the four supported-runtime findings returned by the complete documentation-aware Codex review of exact head `5fa284568b8ca97170dd1b42cb226bbb114789b6` (review submitted 2026-09-09 07:14 UTC) and their corrective implementation. It is implementation/review evidence; it does not redefine gameplay outside the accepted project contract.

The four corrections are implemented in already compiled Blood Moon source files, so this round adds no classic-project compile entry and no version/release/package change.

No assistant-side build, automated mod test or Valheim runtime execution is claimed.

## 1. Drain already-produced skill reports after personal terminal exit

Review comment: `3965660192`.

A skill report produced while the participant was Active/AutoCompleting is profile-backed before send. Ordinary message reordering can deliver a Defeated/Withdrawn/Disconnected transition before that queued report or its retry. The old `BloodMoonSkillReports.Accept` rejected the report because `participant.IsCombatActive` had become false.

Round 7 adds a terminal-only pre-outcome drain path. During combat or early `Resolving` before `PublishingOutcomes`, a correctly bound terminal participant may advance only the existing contiguous skill-report sequence. The normal `BloodMoonSkills.AcceptServerReport` implementation and its contiguous-sequence Harmony guard remain authoritative; state persistence must succeed before ACK. This can update skill contribution/live-bonus accounting but never changes the participant phase or exit reason and cannot create a new post-exit report on a compatible client.

The existing scoped client retry already drains persisted pending reports according to the event phase rather than participant combat state, so the server-side terminal path is reachable after Defeated/Withdrawn/Disconnected.

## 2. Drain retained pre-exit enemy kills after Defeated/Withdrawn

Review comment: `3965660200`.

The prior retained-death recovery admitted a profile-backed pre-exit kill after `Disconnected`, but not after personal terminal `Defeated` or `Withdrawn`. Under ordinary reordering, a valid killing blow can therefore be retained while the personal terminal transaction reaches the server first.

Round 7 recognizes a profile-backed death transaction as a fact created before the personal exit whenever the accepted pre-outcome drain remains open. It accepts the deterministic retained replay for a terminal participant from a ready event sender without reopening participation. `AwardPoints` already preserves terminal phase/reason; genuine `GoalReached` remains sticky if the retained points cross the goal, but the participant stays Exited with the original terminal reason.

This exception applies only to the profile-backed replay scope created by the existing death-durability transaction. Fresh post-exit attacks are still rejected by the ordinary combat/activity path.

## 3. Separate OfferingBowl execution attempt from completion

Review comment: `3965660204`.

The previous cross-owner idempotency marker was written before `OfferingBowl.RPC_SpawnBoss`. A bowl owner could therefore write/replicate that marker and crash before vanilla actually called `SpawnBoss`; a replacement owner interpreted the marker as completed and the accepted pre-cutoff offering disappeared without a boss spawn.

Current Valheim source was rechecked first in `shudnal/assemblies_combined@cf2cda3a4c5c05e62cb8052a61753e5dcaecc28e`, `OfferingBowl.cs`. Vanilla `RPC_SpawnBoss` calls `SpawnBoss(spawnPoint)` only after ownership, queue and `CanSpawnBoss` checks pass. `SpawnBoss` synchronously schedules `DelayedSpawnBoss` via `Invoke` before returning.

Round 7 therefore treats the existing `OfferingExecuted*` fields as attempt markers and adds separate request/authority completion markers. A postfix on the actual vanilla `OfferingBowl.SpawnBoss` writes and force-sends completion only for an authorized Blood Moon relay. `SendRelayResult(completed:true)` is accepted only when that completion fact exists. If only the old attempt marker exists, the result is downgraded to failure, the stale attempt marker is cleared and the server keeps the pending request for the current-owner retry loop.

Thus an owner crash before `SpawnBoss` no longer produces a phantom success, while a normally completed owner still leaves a request-scoped fact that prevents duplicate execution after ownership handoff.

## 4. Reconcile an event crossed completely while state was Dormant

Review comment: `3965660214`.

`FindCurrentOrNext` discarded every schedule whose `MorningAt` was already in the past. If an already-enabled world remained Dormant and `skiptime` jumped from before Forewarning to after that event's morning, the event was never created, so none of the required Marked/Active/resolution side effects ran.

Round 7 adds selection of the most recently crossed eligible Blood Moon schedule when the event high-water allows it and then exposes that crossed schedule as `AutoCompleting` for the creation/catch-up decision. `TryCreateScheduledEvent` creates the normal Forewarning state, after which the existing controller forward-jump bridge traverses `EnterMarked -> EnterActive -> EnterAutoCompleting`; the next normal server tick enters the standard resolution transaction. Backward time remains non-rollback.

A follow-up sanity correction narrows this behavior to an **observed continuous-runtime clock crossing**. `BloodMoonController.TickServer` records the previous server-observed event time for the same world/state instance. The crossed schedule is eligible only if that previous tick was before its Forewarning boundary and the current tick is at/after its Morning boundary. This distinguishes an actual `skiptime`/forward clock jump from:

- server downtime spanning the event;
- Blood Moon being disabled for the event window and enabled afterward;
- the first tick after loading a world whose event already passed.

Those cases do not retroactively replay a missed Blood Moon. World/state replacement and `ZNet.OnDestroy` reset the observation scope. A currently-active schedule still takes precedence over an older crossed schedule, and high-water marks prevent replay of already created/resolved events.

## Owner-side runtime gates

At minimum verify:

1. Raise an eligible skill immediately before Defeated/Withdrawn and delay/reorder the skill RPC behind the terminal transaction: the queued sequence is persisted/ACKed before outcome capture, while the participant remains terminal.
2. Deliver a lethal enemy hit immediately before Defeated and separately before Withdrawn; delay the death replay until after the terminal reason is committed: points/GoalReached are applied exactly once without changing the terminal reason.
3. OfferingBowl owner receives an accepted pre-cutoff relay, writes the attempt marker, then disappears before vanilla `SpawnBoss`: replacement ownership must retry rather than report completion. Repeat after successful `SpawnBoss`: replacement must not schedule a duplicate boss.
4. Force `CanSpawnBoss`/queued-state no-op on a relay: no completion fact is written and the request is not falsely removed as successful.
5. In an already-enabled running Dormant world, observe a tick before Forewarning, then `skiptime` to after event morning: the event is created and traverses Marked/Active/AutoCompleting side effects before normal resolution.
6. Shut the server down before Forewarning and restart it after the event morning: the missed event is not replayed retroactively.
7. Keep Blood Moon disabled throughout an event window and re-enable it afterward: the missed event is not replayed retroactively.
8. Enable Blood Moon only after an event window has already passed: that crossed event is not retroactively replayed.
9. Jump across a completed event when an active current event window is also present: the current active schedule takes precedence over an older crossed schedule.

## Next review requirement

Request another complete project-conformance Codex review of the exact final PR head. Mandatory reading must include the authoritative mechanics index, matrix `25`, handoff `26`, corrective checkpoints `27`-`30`, `32`, `33`, this checkpoint `34`, accepted late-biome policy `31`, acceptance `08`, and accepted decisions `16/20/22/23/24/31` in their explicit scope.

The review must inspect the complete effective `master...feat/blood-moon`, including internal Harmony ordering and the actual classic `.csproj` compile list, not only this round's delta. Supported latency/reordering, terminal-state races, ownership migration, reconnect/restart/world switch, persistence failures, clock jumps and ordinary mod interoperability remain in scope. Modified-client-only attacks are not release blockers under `16_CLIENT_TRUST_BOUNDARY.md`.
