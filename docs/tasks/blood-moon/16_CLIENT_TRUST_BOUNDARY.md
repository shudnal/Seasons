# Blood Moon client trust boundary

Owner decision recorded on 2026-08-25 for development and code review of draft PR #42.

This document is an authoritative addendum to the Blood Moon task set. If an earlier document can be read as requiring protection against an intentionally modified or dishonest client, this document takes precedence for that aspect.

## Supported trust model

Blood Moon is designed for the normal Valheim ownership and networking model with clients running compatible, unmodified game/mod code.

The implementation must remain robust against ordinary distributed-system conditions, including:

- normal ZDO ownership migration;
- RPC/network latency and reordering that can occur in supported play;
- duplicate or stale messages produced by reconnect/retry/recovery paths;
- late join, disconnect and reconnect;
- server/client restart and world teardown;
- stale persisted state and recoverable partial writes;
- normal mod interoperability where another mod uses the supported game APIs without intentionally falsifying Blood Moon state.

These are correctness and recovery requirements, not anti-cheat requirements.

## Explicitly out of scope

Development and code review do not need to defend against a client that intentionally violates the supported runtime contract, including:

- modified Valheim binaries;
- modified Seasons/Blood Moon code;
- hostile mods deliberately forging Blood Moon RPC payloads or markers;
- packet injection or deliberate RPC spoofing outside the normal client code path;
- client-side memory/state editing intended to falsify event results;
- intentionally false reports for client-owned facts;
- other adversarial behavior that requires an untrusted or deliberately modified client.

Preventing or detecting those behaviors is outside the authority and scope of this mod.

## Development rule

Do not add protocol, persistence, validation or gameplay complexity solely to make an intentionally modified/dishonest client unable to cheat.

Identity, event, revision, ownership, deduplication and schema checks are still appropriate when they are needed for correctness with normal clients, stale state, retries, ownership migration or crash recovery.

Existing checks may remain when they also serve those supported correctness goals. They must not be described as providing an anti-cheat or adversarial-client security guarantee.

## Code review rule

A review finding is not a Blood Moon defect merely because an intentionally modified or dishonest client can fabricate data or bypass client-side logic.

Treat a finding as actionable when at least one supported path exists, for example:

- an unmodified client can reach the failure;
- normal network timing/reordering can reach it;
- ownership migration can reach it;
- restart/reconnect/recovery can reach it;
- stale/corrupt persisted state can reach it;
- an ordinary compatibility interaction can reach it.

If the only reproduction requires deliberately modified client/mod code or fabricated traffic, record it as out of scope rather than adding anti-cheat complexity.

## Review request wording

Future Codex review requests for PR #42 must explicitly include this trust boundary so adversarial-client-only findings are not treated as release blockers.
