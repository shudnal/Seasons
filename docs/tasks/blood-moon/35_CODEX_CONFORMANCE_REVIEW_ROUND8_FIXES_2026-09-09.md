# Blood Moon: Codex project-conformance review round 8 fixes

## Status and scope

Date: 2026-09-09.

Repository: `shudnal/Seasons`. Working branch: `feat/blood-moon`. PR #42 remains draft, open and unmerged.

This checkpoint records the five supported-runtime findings returned by the complete documentation-aware Codex review of exact head `cdc7f09bc340e342e85c4498d466ff89c6949eb1` and the corrective implementation that follows it. It is implementation evidence and does not redefine accepted gameplay mechanics.

The round-8 fixes are implemented in the already compiled `BloodMoon/BloodMoonRestartReconnectGrace.cs`. No classic-project compile-list, dependency, version, README, changelog or packaging change is part of this round.

No assistant-side build, automated mod test or Valheim runtime execution is claimed.

## 1. Retry a server-owned pre-exit kill after Defeated or Withdrawn

Review comment: `3966976632`.

A dedicated-server-owned enemy death creates `BloodMoonServerOwnedDeathRetention.PendingRetention` only while the credited participant is combat-active. The previous retry path continued that already-observed fact after `Disconnected`, but stopped sending it after a later personal `Defeated` or `Withdrawn` transition.

Round 8 adds a pre-pass to the existing retention tick. While the accepted pre-outcome drain is open, pending server-owned death transactions are retried for terminal `Defeated` and `Withdrawn` participants as well. The retry invokes the existing private retention sender and therefore keeps its current-peer lookup, retry cadence, profile-persistence ACK and server-side death-durability transaction.

This does not authorize a new post-exit attack: the pending retention object itself was created before the personal terminal transition, while the participant was combat-active. Final application continues through the round-7 profile-backed terminal death replay path and never reopens the participant phase or exit reason.

## 2. Re-stage recovered Fighting/GoalReached participants until prepare/ready converges

Review comment: `3966976643`.

On an Active/AutoCompleting server restart, persisted `Fighting`/`GoalReached` participants were previously left combat-active before reconnecting clients received their new-session global/private snapshots. Restart reconnect grace prevented an immediate server `Disconnected` transition, but did not protect the client owner from the pre-snapshot vanilla-death window.

Round 8 stages every recovered combat-active participant as `Marked` after restart recovery. The original phase and `FightingAt` timestamp are retained in server runtime state. The existing direct server-to-client prepare RPC and existing Marked retry path install the local death guard before readiness is acknowledged. The existing server `OnReadyRpc` changes Marked to Fighting and saves it; a persistence prefix restores the recovered phase semantics immediately before that save:

- prior `Fighting` returns to `Fighting` with its original `FightingAt`;
- prior `GoalReached` returns to `GoalReached` with sticky `GoalReached = true` and its original `FightingAt`.

The restored phase is therefore the one persisted and published after readiness. Failed saves leave the participant staged for retry. A player that disconnects before ready can still become terminal through the existing reconnect-grace/exit path. Staging is performed before `EnsureWorldLoaded` publishes its forced recovery snapshot, so a reconnecting client sees Marked rather than an unsafe recovered Fighting route.

## 3. Enforce the 15-second Stage-1 maximum with monotonic realtime

Review comment: `3966976651`.

`BloodMoonRecovery.TickClientProtection` previously decremented recovery by the caller's fixed delta. Unity pause/time-scale can stop `FixedUpdate`, so Stage 1 could remain fully immune for more than 15 real seconds.

Round 8 replaces the effective tick delta with elapsed `Time.realtimeSinceStartup` for the same world/player session. The original recovery state machine still owns all transitions and persistence:

- if play continues normally, the delta remains approximately the ordinary fixed interval;
- if FixedUpdate pauses, the first later tick consumes the entire real elapsed interval;
- the existing Stage-1 overdue carry flows into Stage 2, so a pause longer than 15 seconds enters Stage 2 with the correct reduced remainder;
- if more than the complete 25-second recovery window elapsed, the original method ends recovery in that same resumed tick.

The realtime tracker resets on world/session teardown. Persisted cross-process recovery continues to use its existing UTC elapsed-time reconstruction; this round changes only in-process monotonic timing.

## 4. Preserve recovery records across world switches

Review comment: `3966976659`.

The original recovery implementation uses one live custom-data slot, `Seasons.BloodMoon.Recovery`. Loading another world caused `EnsureLoaded` to see the first world's record as a world mismatch and delete it, so a later profile save in world B permanently destroyed world A's still-live recovery state.

Round 8 virtualizes that live slot with durable world/event-scoped backing keys:

`Seasons.BloodMoon.Recovery.<WorldUid>.<EventId>`

Before the original loader can inspect the live slot, a valid record is archived under the identity embedded in its JSON. A record belonging to another world is removed only from the live compatibility slot, not from its scoped backing key. If the current world has a scoped record, the newest current-world record by persisted UTC save timestamp is hydrated into the live slot for the unchanged original loader. Original persistence is mirrored back to the scoped key; normal recovery completion removes current-world scoped recovery state.

This preserves the existing tested recovery serializer/state machine and avoids keeping another world's record in the active compatibility slot. Returning to the original world reconstructs remaining recovery using the record's existing `SavedUtcTicks`, so real time spent in another world is deducted rather than granting a fresh protection duration.

No pre-release configuration migration is introduced. The compatibility slot is archived opportunistically when encountered by the effective loader.

## 5. Settle a completely crossed event before its outcome boundary

Review comment: `3966976666`.

Round 7 correctly reconstructs a Blood Moon that a running server observes being crossed completely by one forward `skiptime`, but the resulting Marked participants still use the normal prepare/ready transport gate. On the next server tick the schedule is already beyond Morning; without settlement the controller could enter resolution before remote readiness ACKs, leaving those participants Marked so forced-end automatic completion skipped them.

Round 8 arms a runtime-only settlement window only for a schedule that the existing `BloodMoonObservedServerClock` proves was crossed by consecutive ticks in the same running world/state. When the next schedule-forced resolution is attempted:

- resolution is held for at least one second so the normal `UpdateConnectedParticipants` and direct prepare/ready retries can run;
- connected Marked participants may settle normally into Fighting during that interval;
- the wait is bounded at five real seconds and cannot wedge resolution;
- if a compatible connected participant is still Marked at the bound, the server promotes that participant only inside the resolution transition. `BeginResolution` immediately changes the global phase to Resolving before any exposed Fighting snapshot can be published, and its existing `CompleteAutomaticProgressAtForcedEnd` assigns the normal `AutoCompleted`/100% dawn display result;
- disconnected participants are handled by the normal participant update during the minimum settlement interval.

The round-7 observed-clock boundary remains intact: server downtime, a disabled Blood Moon window, or first load after a missed event does not arm this settlement and does not retroactively replay the event.

## Owner-side runtime gates

At minimum verify:

1. Dedicated-server-owned enemy kill -> player Defeated before retention ACK -> reconnect before outcome capture: the retained kill is accepted exactly once and Defeated remains terminal.
2. Repeat with Withdrawn; repeat with the retention ACK reordered behind the terminal transaction.
3. Restart dedicated server during Active with a persisted Fighting participant. Reconnect under delayed CCS/private snapshots: server routing is Marked until direct prepare/ready; vanilla death cannot occur in the convergence window; after ACK the original Fighting state returns.
4. Repeat restart for a GoalReached participant: GoalReached remains sticky and the published post-ready participant phase is GoalReached rather than ordinary Fighting.
5. Pause a defeated single-player/listen-host game for more than 15 real seconds during Stage 1, then resume: full immunity must already have expired into the correctly shortened Stage 2. Repeat beyond 25 seconds: recovery must end on resume.
6. Become Defeated in world A, switch to world B before recovery finishes, save there, then return to A before the original 25 real seconds have elapsed: A's record survives and resumes only for its remaining elapsed-time-adjusted duration. World B must not receive A's protection.
7. Cross a whole event by one supported forward `skiptime` while remote participants are connected. Delay one prepare/ready ACK beyond the first resolution attempt but below five seconds: resolution waits and the participant receives the normal auto-completed dawn result.
8. Delay readiness beyond the five-second settlement bound: resolution still completes and the still-connected staged participant is included in automatic completion without publishing an unsafe combat-active window.
9. Shut the server down before an event and start it after Morning, or keep Blood Moon disabled through the window: the missed event is not replayed and the round-8 settlement is not armed.

## Next review requirement

Request another complete project-conformance Codex review of the exact final PR head. Mandatory reading must include the authoritative mechanics index, matrix `25`, handoff `26`, corrective checkpoints `27`-`30`, `32`, `33`, `34`, this checkpoint `35`, accepted late-biome policy `31`, acceptance `08`, and accepted decisions `16/20/22/23/24/31` in their explicit scope.

The review must inspect the complete effective `master...feat/blood-moon`, including all internal Harmony adapters and ordering, lifecycle/reset interactions and the actual classic `.csproj` compile list. Re-review these five corrections end to end under supported latency/reordering, personal terminal races, restart snapshot convergence, pause/time-scale behavior, world switching and fully crossed forward time jumps. Modified-client-only forgery/anti-cheat scenarios are not release blockers under `16_CLIENT_TRUST_BOUNDARY.md`.
