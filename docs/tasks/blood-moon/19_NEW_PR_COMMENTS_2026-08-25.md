# Blood Moon new PR comment checkpoint — 2026-08-25

This checkpoint records the owner-requested pass over only the new PR #42 inline comments that appeared after `18_CODEX_REVIEW_HARDENING_2026-08-25.md`.

## Repository state

```text
branch: feat/blood-moon
PR: #42
base: master
state: draft / open / not merged
previous checkpoint head: 8f7c93751c59e96fe8684219380662d1fe9506c3
runtime-code head immediately before this checkpoint: 715c4e38ab6337cb0acebe35876e3adf04b054b6
```

Per owner instruction, this pass did **not** request another Codex review. It only handled the seven new inline comments already present on PR #42.

No assistant-side Valheim build or runtime test was performed. Game API verification continued against:

```text
shudnal/assemblies_combined@cf2cda3a4c5c05e62cb8052a61753e5dcaecc28e
```

No plugin version, public README, Thunderstore changelog, packaging or release metadata was changed.

`16_CLIENT_TRUST_BOUNDARY.md` remains authoritative: normal compatible-client timing, ownership migration, reconnect/restart/recovery and ordinary mod interaction are in scope; adversarially modified clients are not.

## Seven new PR comments

### 1. Overlapping OfferingBowl relays after ownership change

Resolved by `5d89e2cd12a5701be05364835e548061597bf0b0` and completed by `715c4e38ab6337cb0acebe35876e3adf04b054b6`.

The server does not start a second owner attempt while the previous relayed peer remains connected and can still acknowledge. Owner-side execution also carries a request-scoped bowl token consisting of the authority server session plus request id. The token is written and force-sent before vanilla `RPC_SpawnBoss`; a replacement owner seeing the same token reports success without executing vanilla again.

### 2. Requester reconnect while an inventory offering is pending

Resolved by `3145e1fdd24c993544180bcdcd25e617e663ed29`.

The implementation deliberately chooses the cancellation option from the review comment rather than rebinding vanilla interaction state. If the original requester routed session disappears before inventory consumption can safely complete, the pending inventory offering is cancelled. The owner also checks requester availability before invoking vanilla, so a reconnect cannot inherit stale `m_interactUser` / `m_usedSpawnItem` state or receive a free boss spawn.

### 3. Damage confirmation ordering across target ownership migration

Resolved by `2ca8bdb3de09ccf8365b6dce1a6d3fae8dc7cf52`.

The target owner now records whether the exact observed positive HP loss was lethal at the `Character.SetHealth` observation point, before nested `CheckDeath`. Matched non-lethal confirmations continue to drive actual-damage/lifesteal accounting but never update death credit. Only a matched lethal confirmation can populate `confirmedCredits`, so a delayed older non-lethal confirmation cannot overwrite lethal credit by arriving later at the server.

### 4. Owner loss after OfferingBowl execution but before relay result

Resolved by `3145e1fdd24c993544180bcdcd25e617e663ed29` and `715c4e38ab6337cb0acebe35876e3adf04b054b6`.

Owner execution is request-idempotent across peers through the persisted bowl execution token described above. Requester-side inventory removal is separately keyed by the same authority session/request identity, so an owner-loss retry cannot remove the inventory offering a second time.

### 5. Delayed projectile/AOE after source ZDO removal

Resolved by `a6be2e56512bee7f15acb1b9fa7f4359643cccb8`.

Immutable attribution no longer fails solely because its participant/summon/enemy source ZDO has disappeared before the target-owner attribution RPC arrives. When the source ZDO still exists, stable source identity markers are validated. When it has legitimately been removed by disconnect, cleanup or unload, captured attribution survives and current event/target policy controls physical damage. Progress remains server-gated by active participation, preserving the contract that post-exit delayed damage can land without granting progress.

### 6. Multiple queued DreamText presentations

Resolved by `8e7c82be973568e6a9b6f56ef39de22322e07289`.

A pending outcome for another event no longer destroys the active DreamText presenter. It remains pending until the current presentation completes and releases its overlay/input guards; the existing durable outcome retry then starts the next event. Multiple offline outcomes therefore serialize instead of cancelling one another.

### 7. Blood Craft use during snapshot-validation grace

Resolved by `1f6b2afc461388cb08eef96b0d6e71952522d935`.

Snapshot grace still preserves marked items in inventory, but an item that cannot yet pass `BloodCraft.IsValidFor` is quarantined:

- restored marked equipment is unequipped without deleting the item;
- marked items cannot be equipped or used until world/event/owner validation succeeds;
- invalid marked ammo is skipped by `Inventory.GetAmmoItem` in favor of a usable alternative;
- deletion remains deferred until the existing snapshot-validation grace resolves.

## Static scope check

The code diff from `8f7c93751c59e96fe8684219380662d1fe9506c3` through runtime head `715c4e38ab6337cb0acebe35876e3adf04b054b6` is limited to:

```text
BloodMoon/BloodCraftGraceGuards.cs
BloodMoon/BloodMoonDamageCreditAuthority.cs
BloodMoon/BloodMoonDreams.cs
BloodMoon/BloodMoonHitAttribution.cs
BloodMoon/BloodMoonOfferingAuthority.cs
```

All seven new inline review threads were answered and resolved.

PR #42 remains draft and must not be merged without explicit owner approval.
