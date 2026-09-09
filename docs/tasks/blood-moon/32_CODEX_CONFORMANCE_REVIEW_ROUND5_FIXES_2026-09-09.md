# Blood Moon: Codex project-conformance review round 5 fixes

## Status

Date: 2026-09-09.

This checkpoint records the supported-runtime findings returned by the complete project-conformance review requested after round 4, plus their corrective implementation. It is implementation/review evidence and does not redefine gameplay outside the already accepted project contract.

The same work period also accepted `31_LATE_BIOME_PRESENTATION_POLICY.md`; that document is authoritative for Ashlands/Deep North presentation and is reviewed separately from the distributed-durability findings below.

No assistant-side Valheim build or runtime execution is claimed.

## Findings and corrections

### 1. Server-owned enemy deaths lacked an independent replay source

**Finding.** On a dedicated server the dying enemy may be server-owned, so `Player.m_localPlayer` does not exist where `SendEnemyDeath` originates. If accepted progress could not be persisted and the server then crashed, the existing player-profile retry source did not exist.

**Correction.** Dedicated-server-owned enemy death reporting is now staged through a participant-profile retention handshake before the server processes the death transaction. The server sends the credited participant a deterministic replay record `(world,event,player,enemy,10*replicated-level)`. The compatible client saves the same profile-key format used by ordinary owner reports and ACKs retention. Only then does the server process the death report. If the subsequent state save fails, the client already owns the recoverable replay record and the existing exactly-once death ACK removes it only after server persistence becomes recoverable.

The first ordinary validation still requires the actual dead enemy and matched lethal credit. The profile value is a restart-recovery fact under `16_CLIENT_TRUST_BOUNDARY.md`, not hostile-client anti-cheat evidence.

### 2. Persisted death replays could miss the outcome boundary after restart

**Finding.** A valid profile-backed death retry was rejected as soon as the server moved from Active/AutoCompleting into Resolving, and a restarted server could reach outcome capture before the participant reconnected and replayed the pending kill.

**Correction.** Profile-backed death replay is now accepted through the same pre-outcome drain boundary already used for skill-report draining: early `Resolving` remains open until `PublishingOutcomes`. Resolution pauses at `AdvancingTime` before outcome capture for a bounded drain window. After restart, participants that were Marked/combat-active receive up to the existing reconnect-scale 30-second window, followed by a five-second settle/drain interval after reconnection. Ordinary resolution also provides a five-second minimum drain window. The hold is bounded (35 seconds maximum) so a permanently missing peer cannot wedge resolution.

The drain occurs **before** `PublishingOutcomes`; no report is allowed to mutate an outcome after the serialization boundary.

A normal client-owned enemy death report is retained by the peer that owned the dying enemy, which is not necessarily the credited participant. A post-review sanity correction therefore allows a profile-backed death replay during this bounded drain from any still-ready ordinary event peer (or the listen-host server peer), while the replay payload remains bound to the credited participant and event. Terminal-state RPCs retain their stricter player-peer binding. This is a correctness rule for normal ZDO ownership, not an anti-cheat relaxation beyond the accepted trust model.

### 3. Zone-owner migration did not retire a same-group/same-revision lease

**Finding.** A lease whose group and group revision remained current was considered current even after the zone's actual spawn-system ownership migrated to another peer. Its delivered allowance could therefore race newly issued allowance for the replacement owner.

**Correction.** Before topology budgeting/clamping, round-5 lease maintenance compares every positive current-group lease with the zone's actual current spawn-system owner using the existing production ownership validator. If ownership moved, each already-delivered allowance unit becomes a short-lived retained server reservation token and the exact old lease revision is sent back with `Allowance = 0` before normal reconciliation can reclaim the capacity.

This runs inside the existing higher-priority `RetireObsolete` path, before `BloodMoonLeaseBudgetReconciliationPatch` clamps allowances.

### 4. Replicated-extra discovery could spend a second token for a pending report

**Finding.** A normal report spends one lease token before waiting for custom ZDO fields. Discovery could see that same ZDO after its fields appeared and consume another current/retired token before the production pending-report path finalized it.

**Correction.** Replicated-extra discovery now skips every real ZDO ID already present in `pendingSpawnReports`. Only an unreported replicated ZDO can consume a current/retired provenance token. The ordinary pending-report validator remains solely responsible for ZDOs whose routed report already spent a token.

### 5. Live-skill pending records were not isolated by world/event

**Finding.** A single player custom-data key held the active live-skill report record. Entering another world/event could overwrite an unacknowledged record from the previous world.

**Correction.** The effective client reliability layer now stores independent explicit-JSON records under keys scoped by `WorldUid + EventId + PlayerId`. Tracking, retry, ACK reconciliation, persisted live-bonus lookup and runtime restore all address only the exact current scope. Switching worlds/events cannot overwrite an unmatched older scope.

The earlier single-key implementation remains historical/pre-release data and is no longer used by effective runtime paths; no automatic migration was introduced.

### 6. Duplicate late-join prepare could erase a terminal Defeated marker

**Finding.** A delayed/retried `EnrollmentPrepare` called `BloodMoonRecovery.ResetEvent(eventId)` again. If the player had already become Defeated, that reset could delete the durable local terminal marker and stop its retry.

**Correction.** Enrollment/prepare is now terminal-aware. Before `PrepareLocal` or an `enroll` client action can reset the event, the client checks the world/event-scoped pending terminal record and the existing legacy Defeated marker. If terminal evidence exists for that same event, enrollment reset is suppressed and the terminal report is re-sent instead. `resolution-complete` clears completed-event terminal markers so an explicit same-event diagnostic cleanup/restart can enroll again.

### 7. Withdrawn had no durable client-side replay transaction

**Finding.** `ApplyWithdrawal` only updated process-memory `locallyExited`. If the server-side terminal save failed and the server crashed, there was no profile-backed fact that could restore Withdrawn after reconnect.

**Correction.** `Defeated` and `Withdrawn` now share a world/event/player-scoped pending-terminal transaction. A remote server-initiated withdrawal first asks the participant client to persist the Withdrawn marker; the server does not commit the irreversible withdrawal until that retention ACK arrives. A listen-host participant stores the marker synchronously before the normal transition. The client retries a retained terminal report while the pre-outcome drain remains open, and the server removes the pending marker only after the terminal participant state is recoverable from Blood Moon persistence.

If a previously active participant was temporarily recorded as `Disconnected` because the terminal RPC was delayed, a trusted pending Defeated/Withdrawn report delivered before outcome capture replaces that fallback exit reason.

### 8. Pre-resolution Defeated reports were rejected during early Resolving

**Finding.** A local lethal event just before the forced-end transition could be persisted by the client but arrive at the server after the phase became Resolving. `OnDefeatedReport` then rejected it, and the old retry loop stopped outside Active/AutoCompleting.

**Correction.** The new pending-terminal retry continues through early Resolving until the pre-outcome boundary. `OnDefeatedReport` accepts a correctly bound participant report during that drain window, persists the resulting terminal state and ACKs only after recovery is possible. The ordinary Active/AutoCompleting path remains unchanged.

## Classic project / compile-list gate

Round-5 hardening is intentionally in two explicit source files:

- `BloodMoon/BloodMoonRound5Durability.cs`
- `BloodMoon/BloodMoonRound5LeaseFixes.cs`

Both are explicitly included in the classic `Seasons.csproj`. A manual `.csproj` edit temporarily corrupted the `netstandard` reference during preparation; it was immediately restored. Comparing the pre-project-edit head with the repaired head shows the cumulative `.csproj` delta is **exactly two added `<Compile>` entries and no other project-file changes**.

## Relation to the Ashlands / Deep North decision

`31_LATE_BIOME_PRESENTATION_POLICY.md` was accepted after the reviewed head and implemented in the same later PR state. It deliberately leaves all gameplay/status/Bloodlust/durability behavior above unchanged while suppressing only Blood Moon atmospheric presentation in `AshLands` and `DeepNorth`.

The next Codex pass must therefore review both:

1. the eight distributed-runtime corrections in this document; and
2. the presentation-only biome boundary contract in document `31`.

## Owner-side runtime gates

At minimum verify:

1. Dedicated server owns a Blood enemy; player delivers lethal hit; client retention record is created before server death processing. Simulate/force state-save failure, restart server, reconnect client: death progress is replayed exactly once.
2. Repeat with forced-end reached before reconnect: outcome capture waits for the bounded drain and includes a replay delivered within that window.
3. Change a zone's owner while a positive same-group/same-revision lease is still delivered; old owner spends before revoke arrives and report is lost: retained token accounts the spawn, replacement capacity does not exceed caps.
4. Normal spawn report enters pending metadata state, then ZDO fields replicate: discovery does not spend a second token.
5. Produce an unacknowledged live-skill report in world A, switch to world B and produce another, then return to A: both scoped records remain independent and A resumes its original sequence/live-bonus high-water.
6. Become Defeated, delay a duplicate `EnrollmentPrepare`: terminal evidence is retained/reported and no new Fighting enrollment occurs.
7. Trigger server-side Withdrawn with persistence failure and restart: the client profile marker reasserts Withdrawn before outcome capture.
8. Trigger Defeated immediately before forced end with delayed/lost first notification: retry during early Resolving still produces Defeated and no vanilla death semantics.
9. Keep a participant disconnected past the drain bound: resolution eventually continues rather than remaining permanently stuck.
10. For a client-owned enemy, let a different peer own the enemy than the player credited for the lethal hit; delay profile replay until early Resolving and confirm the ready enemy-owner peer can deliver it without requiring `sender == creditedPlayer`.
11. Run the Ashlands/Deep North boundary scenarios from `31_LATE_BIOME_PRESENTATION_POLICY.md` in the same build.

## Next review requirement

Request another **complete project-conformance review** of the exact final PR head. Codex must read the authoritative mechanics/index, matrix `25`, handoff `26`, corrective checkpoints `27`-`30`, this checkpoint `32`, accepted presentation decision `31`, and later accepted decisions `16/20/22/23/24`.

The review must cover the full effective `master...feat/blood-moon`, including Harmony adapter interactions and the actual classic `.csproj` compile list. Supported latency/reordering, ownership migration, reconnect/restart, persistence failure and ordinary mod interoperability are in scope. Modified-client-only attacks remain outside release-blocking scope per document `16`.
