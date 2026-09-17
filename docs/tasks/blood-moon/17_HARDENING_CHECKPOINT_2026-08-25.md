# Blood Moon hardening checkpoint — 2026-08-25

This is the latest recovery checkpoint for draft PR #42 after the post-review hardening pass. It supplements `13_IMPLEMENTATION_REPORT.md`, `14_CODE_REVIEW_STATUS.md`, `15_RELEASE_READINESS.md` and the owner trust-boundary addendum in `16_CLIENT_TRUST_BOUNDARY.md`.

## Repository state

```text
branch: feat/blood-moon
PR: #42
base: master
state: draft / open / not merged
runtime-code head immediately before this checkpoint: 9ef4769036fdbcc0bd5d4897b0c7df4340c66226
```

The exact documentation-inclusive review head must be taken from the PR after this file is committed and used verbatim in the next Codex review request.

No plugin version, public README, Thunderstore changelog or packaging/release metadata was changed.

No assistant-side Valheim build or runtime test was performed. Game API checks continue to use:

```text
shudnal/assemblies_combined@cf2cda3a4c5c05e62cb8052a61753e5dcaecc28e
```

## Owner trust boundary

`16_CLIENT_TRUST_BOUNDARY.md` is now authoritative for development and review.

Blood Moon must be correct under normal Valheim networking, ownership migration, latency/reordering, reconnect/restart, stale state and ordinary mod interoperability. Intentionally modified game/mod clients, fabricated Blood Moon traffic/state and other adversarial-client behavior are outside scope.

A future review finding is actionable only when it has a supported reproduction path that does not require an intentionally dishonest or modified client. Correctness checks may remain when they also protect supported runtime behavior, but they are not an anti-cheat guarantee.

## Latest hardening changes

### Lethal hit and actual-damage ordering

Positive target-owner HP loss is observed from `Character.SetHealth`, before nested `CheckDeath`, so a lethal hit records its local credited source before the death report path runs.

The current cross-owner damage-correlation path accepts normal RPC arrival reordering and ties actual positive HP loss to the permitted combat source before server-side death credit and direct-participant lifesteal accounting. It is retained for distributed correctness; it must not be presented as protection against an adversarial client.

### Defeated durability

The local world/event Defeated marker is persisted immediately through the normal player-profile save request and is retried to the server while public routing still reports the participant combat-active. A new authoritative `enroll` clears the old marker and requests persistence again.

### Outcome queue durability and presentation ordering

`BloodMoonOutcomeQueue.Capture` now blocks resolution progression until the captured outcome store has been durably written. Failed writes remain dirty and retry.

The presentation handshake is separate from durable profile acknowledgement. `Begin(eventId)` is now idempotent for the same event, so presentation start/completion acknowledgements that arrive after a successful store retry but before the controller repeats `PublishingOutcomes` are not erased.

### Blood Craft projectile/world-drop invariant

Vanilla `ItemData.Clone()` preserves `m_customData`. The missing world sink was static `ItemDrop.DropItem`, used by projectile item respawn. Temporary Blood Craft `ItemData` is rejected at that boundary, preventing temporary thrown/ammo items from materializing as world pickups.

Stale projectile TTL handling preserves ordinary permanent `m_spawnItem` recovery while suppressing harmful spawned-projectile/random-spawn branches. Temporary item respawn remains rejected by the central world-drop invariant.

### Forbidden projectile targets

No code change was made for the review suggestion to consume a projectile on a forbidden character collider. The authoritative combat contract explicitly requires a forbidden collider to produce no successful hit and not stop the projectile. World geometry remains a separate path that preserves vanilla collision/destruction while Blood-source world damage is suppressed.

### OfferingBowl pre-Marked cutover

Boss-producing `OfferingBowl` initiation during `Forewarning` now uses a server-authorized relay.

The server decides the cutover at request receipt:

- request received while the authoritative event is still `Forewarning` is relayed to the current bowl owner and may complete after `Marked`;
- request received after the authoritative transition to `Marked` is not authorized;
- item-producing bowls remain outside this path;
- items/attachments are still consumed only by vanilla `RPC_SpawnBoss` acceptance.

Relay RPC handlers are registered with the normal Blood Moon network composition on every peer before gameplay use. This removes the previously documented owner-RPC-arrival race from the known-limitations list; real multiplayer timing still requires owner playtest.

### RPC registration lifetime

Subsystem cached `ZRoutedRpc` references remain tied to the actual transport object and are not cleared merely by world-state reset where duplicate registration on the same method table would be possible.

## Current review disposition

All inline review threads present before this checkpoint are resolved.

For future Codex passes:

1. include the trust boundary from `16_CLIENT_TRUST_BOUNDARY.md` in the review request;
2. fix findings reachable by supported clients/runtime behavior;
3. mark adversarial-client-only findings out of scope rather than adding anti-cheat complexity;
4. if code changes after review, request another review for the new exact head.

## Priority owner-side runtime acceptance

The full matrix remains `08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md`. Highest-priority scenarios after this checkpoint are:

- single-player, listen and dedicated annual lifecycle;
- restart/reconnect in Active and each resolution step;
- direct/projectile/AOE/summon lethal attribution and zero-damage hits;
- lifesteal and rolling HPS cap under normal multiplayer ownership;
- Defeated followed by immediate process termination/reconnect;
- outcome-store I/O retry followed by DreamText presentation;
- outcome delivery under latency/disconnect/reconnect;
- pre-18:00 boss offering whose relay completes after Marked;
- post-18:00 boss offering rejection without item/attachment consumption;
- bowl ownership migration around the offering relay;
- temporary thrown/ammo item respawn on hit and TTL;
- stale projectile hit/TTL behavior;
- mixed interior/surface spawn ownership and migration.

PR #42 remains draft and must not be merged without explicit owner approval.
