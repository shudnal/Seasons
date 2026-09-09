# Blood Moon: Codex project-conformance review round 6 fixes

## Status and scope

Date: 2026-09-09.

Repository: `shudnal/Seasons`. Working branch: `feat/blood-moon`. PR #42 remains draft, open and unmerged.

This checkpoint completes the interrupted correction pass for the eight findings from the full documentation-aware review of `cd7dffaab1d70647dafb51a9ef49aa84038e2714` (review ID `5149615580`). It records implementation evidence, not new authorization to change gameplay.

The resumed pass verified the already-published head `4e632b0563153bf1468d62a3098e4f67d02964a7`, inspected the prepared Git tree left by the interrupted pass, and committed its remaining corrections as `0bc9d88cec0431ebf7374b450330b30e7ed8a8a2`. The latter changes only `BloodMoonEnemyDeathPending.cs` and `BloodMoonRecovery.cs`.

The exact next submitted/reviewed head belongs in the PR request and Codex response, not in a self-referential field in this document.

## Authoritative context

Read the mechanics index `../CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`, matrix `25_PROJECT_CONFORMANCE_MATRIX.md`, handoff `26_PR42_CONFORMANCE_REVIEW_HANDOFF.md`, corrective checkpoints `27`-`30` and `32`, accepted presentation policy `31_LATE_BIOME_PRESENTATION_POLICY.md`, and acceptance document `08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md`.

Apply accepted decisions `16`, `20`, `22`, `23`, `24`, and `31` in their explicit scope. In particular, document `22` still requires the next ordinary vanilla sleep for DreamText and forbids a new global Player input lock. Document `31` suppresses atmospheric presentation, not gameplay, in Ashlands and Deep North. Historical corrective reports do not override these decisions.

## Findings and corrections

### 1. Retention ACK must mean recoverable player-profile storage

Review comment: `3964534627`.

`BloodMoonRound5Runtime.SaveProfile` now returns a success result. It captures current character data, uses the ordinary game save path, reloads a separate `PlayerProfile` through the vanilla local/cloud loader, and checks player identity and the serialized player-data bytes. The temporary loaded profile is never installed as the live profile and does not create or load a Player object.

Both terminal-retention and dedicated-server-owned-death retention handlers withhold their retention ACK unless this verification succeeds. A repeated request retries persistence even when the desired custom-data value already exists in memory. A reentrancy guard and throttled warning prevent recursive profile-save attempts and warning spam.

The follow-up correction closes an alternate route: periodic pending-enemy-death replay also verifies the profile before sending a batch. An in-memory record left by a failed retention save cannot bypass the withheld retention ACK through the retry loop. Verification happens once per retry batch, not separately for every queued kill.

An ordinary immediate local Defeated notification is distinct from a retention ACK: safe defeat is not undone by a local disk failure, and the server may still persist that notification. The code does not claim that an unsuccessful local save is durable.

### 2. Preserve a server-owned death observed before disconnect

Review comment: `3964534631`.

The dedicated-server retention queue no longer discards an observed kill merely because the credited participant becomes `Disconnected`. Within the pre-outcome drain, the same player's reconnect can resume the retention handshake and profile-backed replay.

Death validation distinguishes a fresh combat report from an already-observed profile-backed transaction. The latter may be accepted for a participant whose exit reason is `Disconnected`, before outcome capture, without requiring that participant to become combat-active again. Applying the retained points preserves terminal phase and exit reason; genuine GoalReached can become sticky without changing an Exited participant back to GoalReached/Fighting.

The existing ready-sender, world/event identity, positive deterministic replay value, exactly-once enemy ID and immutable outcome-boundary rules remain. This is not authorization to award progress for a new post-exit attack. A peer missing beyond the accepted bounded drain is not promised unlimited retroactive outcome mutation.

### 3. Resolution completion is not a terminal-storage acknowledgement

Review comment: `3964534633`.

Terminal persistence now distinguishes the pending transaction from retained first-reason evidence. `resolution-complete` removes only acknowledged evidence; it leaves an unacknowledged `PendingTerminal` record intact. A matching terminal ACK removes the pending record while preserving enough evidence to reject a delayed same-event prepare/enroll during the active lifetime.

Pending terminal reports continue requesting an ACK after combat. After the outcome-capture boundary, the server may acknowledge an identical already-recorded terminal fact, but may not replace the immutable outcome with a different reason.

`PublishOutcomes` also requires successful event-state persistence before capturing the independent outcome queue. A successful outcome-store write must not conceal a failed save of the participant facts used to construct that outcome. The bounded network-drain timeout is not a bypass for this persistence requirement.

The existing debug cleanup/restart semantics remain a separate administrative path; pending evidence is not discarded merely to make an ordinary resolution look complete.

### 4. Catch-up starts must use the same combat-ready handshake

Review comment: `3964534636`.

`EnterActive` no longer promotes every Marked participant directly to Fighting. Marked also serves as transport staging until the direct prepare/ready exchange has installed local death protection. The handshake now covers ordinary starts and same-tick Forewarning -> Marked -> Active/AutoCompleting catch-up as well as late joins.

`JoinedLate` remains descriptive metadata, not a prerequisite for readiness. Prepare and readiness retries work for every staged Marked participant during combat. The server persists Fighting before publication; failure restores the staged state for retry. Duplicate prepare does not reset an already prepared or Fighting participant, and terminal evidence still takes precedence.

Forward-only scheduling remains unchanged: readiness is not permission to rewind gameplay, to reopen terminal participation, or to bypass the normal forced-end resolution path.

### 5. Fall back when an existing EnvMan still reports None

Review comment: `3964534637`.

The presentation policy consults the local Player when `EnvMan.GetCurrentBiome()` returns `None`, not only when EnvMan itself is absent. If both sources are uninitialized, environmental overrides remain disabled until a concrete permitted biome becomes available.

This affects only Blood Moon atmosphere. Status, Bloodlust, combat, enrollment, progress, crafting and resolution remain independent of the late-biome gate.

### 6. Isolate the drain timeout by world and state lifetime

Review comment: `3964534641`.

`BloodMoonRound5DrainTimeout` now binds the elapsed-time budget to the event-state object, world UID and event ID. It exposes an explicit reset used by `BloodMoonOutcomeDrainGate.Reset`, which is called during world teardown, controller-world cleanup, restart setup and debug cleanup.

World B receives its own drain budget even if world A previously exhausted a timeout with the same numeric event ID. The timeout still bounds network waiting; it does not certify storage or authorize outcome changes after capture.

### 7. Keep the first retained personal terminal reason

Review comment: `3964534646`.

Local terminal storage preserves the first existing same-world/event/player Defeated or Withdrawn evidence instead of replacing it with a later request. A server withdrawal request racing an earlier local defeat receives the actually retained reason in the retention ACK, not the requested conflicting reason.

Server pending retention is keyed by participant scope rather than by the requested reason. It can therefore consume the first-reason response without manufacturing a separate competing withdrawal transaction. Only the Disconnected fallback can be corrected before outcome capture; a committed personal terminal reason is not overwritten by later reordered messages.

The follow-up correction makes `BloodMoonRecovery.IsLocallyExited` consult retained terminal evidence as well as its runtime set. A reconnect with pending Withdrawn evidence cannot temporarily resume participation while retrying the server transition.

### 8. Clear an orphaned Blood Moon force environment

Review comment: `3964534649`.

Another weather mod may temporarily replace Blood Moon, then later restore `Seasons_BloodMoon` after our lease has been released. `ReleaseForcedEnvironment` now recognizes that exact returned Blood Moon value even when the local ownership flag has already been cleared. An orphan with no valid restore target returns to native weather rather than reviving an expired foreign override or restoring Blood Moon to itself.

The presentation tick reconciles disallowed/no-longer-required force environments continuously, not only when the biome gate changes. It does not overwrite another mod's currently active different environment and does not continuously reacquire Blood Moon against a live foreign owner.

## Additional effective-flow correction

The pending death-evidence path now retains its validated dead-ZDO evidence until the controller actually accepts the transaction. Removing it before invoking the controller made that controller's second validation fail after ordinary ZDO destruction.

The existing death-durability prefix also establishes the profile-replay scope before the pending-evidence guard runs. Otherwise an earlier guard could defer the call before the profile-backed replay context existed. Postfix/finalizer cleanup remains idempotent. These are direct edits to the existing paths, not a new review-round Harmony layer.

## Code commits in this correction pass

- `3ae9cc7bda7f7fb15c3db33df7b7a0c8c56a084d`: biome readiness and displaced force-environment cleanup.
- `43bf8385f95b2667860634b35690b1ae1690514f`: readiness-gated catch-up, retained disconnected death credit and persistence before outcome capture.
- `4e632b0563153bf1468d62a3098e4f67d02964a7`: verified profile retention, first terminal reason, pending/evidence separation and session-bound drain reset.
- `0bc9d88cec0431ebf7374b450330b30e7ed8a8a2`: persistence-gated enemy-death replay batch, pending-evidence lifetime/order and local terminal-evidence lookup.

## Static verification and evidence boundary

The resumed pass read the actual remote source at the identified heads, inspected the recovered tree's complete two-file diff, and checked the effective retention/retry/ACK paths and existing Harmony adapters. Every changed runtime file is already explicitly included in the classic `Seasons.csproj`; no project/reference/version/package change is part of round 6. New code/comments/documentation use English.

Game save-path reasoning is based on `shudnal/assemblies_combined`, including `Game.SavePlayerProfile`, `PlayerProfile.SavePlayerData`, `PlayerProfile.SavePlayerToDisk` and the profile loader. A save request, absence of an exception, or the cloud save method's boolean alone is not treated as evidence of successful persistence.

No assistant-side compilation, automated mod test or Valheim execution was performed. Static inspection and a requested Codex review are not runtime validation.

## Owner-side acceptance additions

1. Force a local/cloud profile write failure during each retention handshake. Neither retention ACK nor the periodic death-replay route may claim durable evidence; restoring storage permits retry.
2. Disconnect the credited participant after a server-owned kill and before retention ACK. Reconnect within the accepted drain: the kill is applied once and Disconnected remains terminal.
3. Fail event-state persistence while outcome-queue storage still works. Outcome publication must not bypass the failed state save; unacknowledged terminal evidence survives resolution notifications and restart.
4. Jump from Forewarning directly into Active/AutoCompleting with delayed snapshots. A remote player becomes Fighting only through prepare/ready; the protection and terminal-evidence paths remain consistent.
5. Reconnect in Ashlands/Deep North with EnvMan present but biome None. Use the ready Player biome; with neither source ready, do not force Blood Moon weather.
6. Exhaust a drain in world A, then load world B with the same event ID. World B gets a new bounded drain interval.
7. Persist Defeated, then deliver a reordered withdrawal-retention request and duplicate prepare. The first reason remains Defeated and participation does not reopen.
8. Let another weather mod displace Blood Moon, enter an excluded biome, then release the foreign override. Returned Blood Moon weather is cleared without overwriting a different live foreign environment.
9. Remove a dead enemy ZDO before delayed authorization arrives. Pending evidence remains available for the controller's repeated validation and accepted progress is persisted/ACKed once.

## Next review

Request a new complete project-conformance review on the exact final PR head. Require the mechanics/index, matrix `25`, handoff `26`, checkpoints `27`-`30`, accepted policy `31`, corrections `32` and this document, and acceptance `08` before inspecting implementation.

Review the full effective `master...feat/blood-moon`, not only this round's delta. Include existing internal Harmony adapters and the actual classic compile list. Re-check the eight findings above, their alternate RPC/retry paths and all preceding corrections under supported-client latency, reordering, ownership migration, disconnect/reconnect, restart/world switch, storage failures and ordinary mod interoperability. Modified-client-only anti-cheat scenarios remain outside release-blocking scope per document `16`.

A clean response must state the exact reviewed SHA and explicitly confirm full project-contract/matrix coverage. Do not infer a completed review from the request or an eyes reaction. Keep the PR draft/open/unmerged.
