# Blood Moon code review status

This file records the Codex/manual review trail for draft PR #42 and the exact continuation rule after the latest Blood Moon hardening.

## Pull request

```text
PR: #42 Blood Moon first implementation slice
base: master
head: feat/blood-moon
state: draft / open / not merged
URL: https://github.com/shudnal/Seasons/pull/42
```

The PR must remain draft and unmerged until explicit owner approval.

## Authoritative game source

All game/API verification for this review sequence uses:

```text
repository: https://github.com/shudnal/assemblies_combined
commit: cf2cda3a4c5c05e62cb8052a61753e5dcaecc28e
```

No assistant-side Valheim build or runtime test was performed.

## Previous Codex chronology

Earlier full-diff Codex passes found and verified fixes for:

- private skill baselines and local-player-ready resync;
- active Blood Craft preservation during restart recovery;
- pre-combat disable without morning advance;
- random-event suppression teardown;
- boss and AI interior-context filtering;
- environmental damage remaining valid for participants;
- direct-attack Harmony target enumeration;
- mixed surface/interior spawning;
- server-observed enemy-death validation;
- world-scoped chronicle identity;
- independent durable offline outcomes;
- monotonic same-event resync;
- real-time-calendar net-time separation;
- lease clock-domain separation;
- newest-valid persistence recovery;
- hostile attribution validation;
- interaction routing through early Resolving;
- disabled Blood Craft recipe exclusion;
- exact extra-enemy prefab validation;
- resolution spawn-race prevention;
- persistence-aware outcome acknowledgement;
- late Defeated report rejection;
- dormant `EventId == -1` cleanup safety;
- frozen per-event extra-enemy identity;
- durable stale-prior-event spawn-pool history.

An earlier exact code head received a clean Codex result:

```text
0be8eb89d600a95720f08387041fb7ab15ccd5f7
Didn't find any major issues.
```

A later documentation-inclusive review found adjustable-wall-clock lease expiry. That was fixed at:

```text
9f8e24d6fdadb656aa37d067cd5b4a466224105
```

Received lease lifetime is now bounded once and tracked with Unity `Time.realtimeSinceStartup`.

## Review on head 39d12ec6361f82f930c94cd0c258f272e3a8a916

The next Codex pass produced six inline findings. The branch was not sent for another review immediately; the findings were first reconciled with the authoritative contracts and a broader static hardening pass.

### 1. Lethal hit credit was observed too late — confirmed

The previous `RPC_Damage` postfix could run after the nested `CheckDeath` path, so a lethal hit could send a death report before the new source was recorded.

Fix:

- start a target-owner damage observation in `RPC_Damage`;
- observe positive HP loss from `Character.SetHealth`, which the verified vanilla flow executes before `CheckDeath`;
- record the local advisory credit before lethal death reporting;
- send the same actual-damage confirmation into the server authority path.

### 2. Defeated marker was not immediately persisted — confirmed

The custom-data marker was written immediately but could still wait for the next ordinary profile save.

Fix:

- after local Defeated interception/synchronization, request `Game.SavePlayerProfile(false)` immediately;
- when a new authoritative `enroll` clears the old Defeated marker, request another immediate profile save;
- keep reconnect resend behavior while server routing still reports active participation.

This uses the strongest persistence request exposed by vanilla without claiming stronger cloud durability than vanilla itself guarantees.

### 3. Forbidden character collider should preserve collision handling — rejected by contract

Codex suggested allowing `Projectile.OnHit` to continue for a forbidden character collider. The authoritative combat contract explicitly requires the opposite gameplay result: the forbidden collider must not be a successful hit and must not stop the projectile.

No behavioral change was made for that path. The early projectile prefix continues to reject the hit before status/push/stagger/skill side effects and allows the projectile to pass through the forbidden character. World geometry remains a separate path and keeps vanilla collision/effect/attach/destroy lifecycle while world damage is suppressed.

### 4. Blood Craft projectile payload could escape into the world — confirmed risk, refined root cause

The review explanation assumed the temporary marker could be lost during projectile item cloning. Static game-source verification showed that `ItemData.Clone()` copies `m_customData`, so the marker is preserved.

The actual gap was that projectile respawn materializes through static `ItemDrop.DropItem`, which bypassed the earlier instance/world-consumer guards.

Fix:

- add a common `ItemDrop.DropItem(ItemData, int, Vector3, Quaternion)` guard that refuses marked temporary `ItemData`;
- preserve `m_spawnItem` for stale projectiles so ordinary permanent thrown weapons still respawn;
- on stale TTL, suppress harmful `m_spawnOnHit`/random-spawn branches while preserving the item-respawn branch.

### 5. Enemy death reporter could choose credited player — confirmed P1

The target owner previously reported both death and credited player, which let one authority choose another authority's contribution identity.

Fix:

- source owner first sends a bounded authorization keyed by event, target ZDO, source ZDO, source type and player;
- target owner separately confirms positive actual HP loss for that exact key;
- server accepts either arrival order and matches the two records;
- raw target Blood-enemy eligibility and source participant/summon identity are server-validated;
- the target's newest confirmed credit is independent from the later death-report claim;
- death is accepted only when the claimed player exactly matches server-confirmed credit;
- exactly-once accepted death consumes the confirmed credit;
- pending death validation waits for both replicated dead state and confirmed credit.

Direct Bloodlust lifesteal now uses the same matched pair, removing the previous separate unbound damage-report channel.

### 6. Outcome queue capture was best-effort — confirmed

The resolution previously populated the outcome store in memory even if disk persistence failed, then could continue presentation/release.

Fix:

- `Capture` now returns success only after durable snapshot persistence;
- the store has an explicit dirty state;
- failed writes remain dirty and retry;
- `PublishingOutcomes` does not start delivery or presentation timing until capture succeeds;
- later queue mutations also retain dirty state after write failure.

## Additional hardening found during the same pass

The review fixes exposed several adjacent issues that were corrected before requesting another review.

### Routed-RPC registration lifetime

Vanilla `ZRoutedRpc.Register` stores handlers with `Dictionary.Add`. Clearing a subsystem's cached `registeredRpc` during a world reset could therefore try to register the same name again on a still-live transport and throw.

The damage-credit authority, Bloodlust and outcome-presentation subsystems now clear only runtime/event state while retaining the cached transport reference. `BloodMoonHitAttribution` already followed this pattern. A genuinely new `ZRoutedRpc` instance registers normally by reference change.

Immutable hit-attribution handlers are also registered proactively on every peer through the controller/network startup path rather than only when that peer happens to create a projectile/AOE. This closes the target-owner-only registration gap.

### Outcome presentation versus durable acknowledgement

DreamText start/completion is now tracked only by the dedicated presentation handshake. Durable profile/queue acknowledgement no longer doubles as a presentation-completion signal. The two guarantees remain independent.

### OfferingBowl pre-Marked cutover

Static verification of vanilla `OfferingBowl` confirmed a genuine cross-owner cutover race: a request sent before 18:00 could reach the bowl owner after Marked and be rejected by the authoritative owner guard.

The Forewarning boss-spawn path now uses a server-authorized relay:

1. client `InitiateSpawnBoss` sends the request to the server;
2. server accepts only while its own event state is still `Forewarning` and validates a boss-producing bowl ZDO;
3. server relays an accepted request to the current bowl owner;
4. the owner executes vanilla `RPC_SpawnBoss` in a narrow authorized-completion scope, even if Marked was entered after server acceptance;
5. requests first received by the server after Marked are not authorized.

Item-producing bowls are not routed or blocked by this mechanism.

### Stale projectile TTL and permanent items

Static `Projectile.FixedUpdate` verification showed that `m_spawnOnTtl` calls the same `SpawnOnHit` path used for `m_spawnItem`. Completely disabling TTL spawn would therefore lose an ordinary permanent thrown weapon after event rollover.

The stale TTL guard now keeps item respawn while suppressing only harmful spawned projectile/random-spawn branches. Marked temporary item materialization is independently blocked by `ItemDrop.DropItem`.

### Production-path consolidation

Hostile attribution validation and new-enrollment Defeated reset were folded directly into their production paths and the corresponding corrective self-patches were removed.

## Pre-documentation code checkpoint

The runtime-code hardening described above was complete at:

```text
3b55e99f4006b2aca73686a5130e8b843888ee3f
```

Documentation commits after that SHA do not change the described runtime behavior unless explicitly noted in the PR timeline.

## Static source verification in this pass

The pass re-read current game source for:

- `Character.SetHealth`, `RPC_Damage`, `CheckDeath` and Player death state;
- `Projectile.OnHit`, `FixedUpdate`, `SpawnOnHit`, skill/adrenaline and item respawn;
- `ItemDrop.ItemData.Clone` and static `ItemDrop.DropItem`;
- `OfferingBowl` interaction, owner RPC, item consumption and delayed spawn;
- `ZNetView` owner RPC routing;
- `ZNetScene.FindInstance`/prefab lookup;
- `ZRoutedRpc.Register`;
- `ZPackage` float/vector/ZDOID serialization;
- Blood Craft source recipe/station APIs and existing inventory/world-sink patch points.

This is static source review only, not a build/runtime result.

## Review request rule

The review on `39d12ec6361f82f930c94cd0c258f272e3a8a916` is superseded by the fixes above. After `13_IMPLEMENTATION_REPORT.md`, this file and `15_RELEASE_READINESS.md` are frozen and the six current threads have dispositions recorded on the PR, request Codex review on the new exact documentation-inclusive head.

If Codex reports a confirmed issue:

1. fix it on `feat/blood-moon`;
2. resolve the corresponding review thread;
3. update repository context if the finding changes implementation knowledge;
4. request another exact-head review.

The final clean reviewed SHA/result should be recorded in the PR timeline without another documentation-only commit so the reviewed head remains exact.

## Runtime gate

A clean Codex review does not replace owner-side Valheim testing. Runtime acceptance remains defined by `08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md`, `13_IMPLEMENTATION_REPORT.md` and `15_RELEASE_READINESS.md`.

## Final invariants

Before owner-side playtest handoff:

- the final documentation-inclusive Codex review has no unresolved confirmed finding;
- all inline review threads are resolved;
- PR #42 remains draft/open/unmerged;
- plugin version, public README, Thunderstore changelog and packaging/release metadata remain unchanged;
- no assistant-side Valheim build/runtime result is claimed;
- runtime-only uncertainty is explicitly documented rather than presented as proven behavior.
