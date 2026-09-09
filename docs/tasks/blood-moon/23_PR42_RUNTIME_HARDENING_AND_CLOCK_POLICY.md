# PR #42 runtime hardening and clock-control policy

## Scope and evidence

Work remains in `feat/blood-moon`, in the existing draft PR #42. Do not merge, change the plugin version, or prepare a release without the owner.

This review started at `da668ec6719ef15d1345505c2b944ea17c023704`. Runtime changes in this pass extend through `2b55c8181d50e39069d652bf024c6c3b0189c722`; subsequent documentation commits and the exact review target are recorded in the PR timeline.

The owner reported that the baseline runs without obvious Blood Moon exceptions, requested the Marketplace compatibility fix, and retained responsibility for visual tuning. Quiet runtime logs are not proof that multiplayer lifecycle, loot, or retry behavior is correct.

Game source was checked in `shudnal/assemblies_combined` at `cf2cda3a4c5c05e62cb8052a61753e5dcaecc28e`. This pass used source and diff inspection only. No Seasons build, automated mod tests, or Valheim runtime execution was performed. Owner-side validation is still required; an external code-review request is not a completed review.

The Balrond loading-indicator report is closed for this task. No Balrond patch, dependency, or loading-screen reorder was added.

## 1. Accepted clock policy

### Natural time and `skiptime`

The server's frozen event schedule controls **forward-only** phase progression:

```text
Forewarning -> Marked -> Active -> AutoCompleting -> Resolving -> Resolved
```

A forward jump reconciles to the later scheduled phase through the normal transition methods in the next server tick. Intermediate transition side effects still run when necessary; there is no requirement to spend a full tick in each intermediate combat phase. A jump past the forced end enters the normal resolution pipeline, rather than assigning `Resolved` and bypassing cleanup.

A backward clock change does not reverse gameplay. In particular, it does not:

- recreate dead ordinary creatures;
- restore consumed items or undo Blood Craft cleanup;
- unpark or repark bosses by assigning a phase enum;
- remove skill experience already acquired;
- revoke `GoalReached`, clear a terminal exit, or permit ordinary re-entry;
- replay a previously created/resolved event from an earlier year.

`LastCreatedEventId` and `LastResolvedEventId` are high-water marks. Normal calendar creation only accepts a later event ID. This prevents replay after rewinding into an earlier autumn, not just duplication of the most recent event.

Displayed automatic progress does not decrease when time is rewound. Combat progress, contribution, and reward accounting remain separate from that display floor. Reaching the target through automatic display completion still does not grant combat success.

This is an explicit accepted behavior, not an unfinished bidirectional time-travel implementation. Calendar-based appearance may still interpolate from the current time within the retained phase; gameplay is not rolled back.

### Explicit administrator controls

Use the existing server/admin command path:

```text
seasons bloodmoon status
seasons bloodmoon start forewarning
seasons bloodmoon start marked
seasons bloodmoon start active
seasons bloodmoon resolve
seasons bloodmoon cleanup
```

`start` anchors the requested phase to the current time and retains normal relative durations. In particular, `start marked` no longer sets `MarkedAt` and `ActiveAt` to the same timestamp and immediately skips preparation.

A backward `start` while an event is live is rejected with an actionable message. An administrator must explicitly clean up the current event before starting a diagnostic scenario again. Cleanup stops leases, removes extras and temporary items, restores parked bosses, releases fade/input, and retains event-history high-water marks. It is not a rollback of acquired skills or durable outcome receipts.

Do not treat repeated debug starts on the same world day as independent reward-bearing production events. They can reuse the day's event ID; already-applied durable receipts must not be cleared to make a test repeat its rewards. Use an appropriate separate world day or a disposable test world when checking a fresh reward lifecycle.

Progress commands reject non-finite values, non-combat states, and terminal participants. `GoalReached` remains sticky after a later debug progress reduction.

### Technical timeout domains

Restart reconnect grace, pending spawn-report observation, and delayed rejected-spawn cleanup use `Time.realtimeSinceStartup`. Advancing game time must not instantly expire these networking grace periods, and rewinding must not extend them indefinitely.

The existing client lease time adapter remains in use. Serialized server leases still use the server schedule's time domain; do not claim that every existing networking timer has been converted in this pass.

## 2. Marketplace and minimap lifecycle

### Failure path

The old adapter attempted to read `Minimap.instance.m_mapTexture` and invoke reflected Marketplace fields without establishing minimap readiness. Its catch path could permanently discard compatibility handles after a transient error. `MinimapVariantController.OnDestroy` also called the normal restore path, which could invoke Marketplace while the world/UI was being destroyed.

### Corrected contract

- Resolve the Marketplace plugin instance, territory client type, parameterless static `DoMapMagic`, and writable static `originalMapColors` before use.
- Accept the inspected `Color[]` shape and the compatible `Color32[]` variant without a hard DLL reference.
- Require the current minimap, generated/readable map texture, and height/fog/exploration data.
- If Marketplace exposes `originalHeightColors`, wait until its own initialization populated that baseline. Do not overwrite it from an already modified texture.
- Skip redraw during teardown. Do not start Marketplace's asynchronous map work from the minimap controller's destructor.
- A transient update failure does not permanently disable the adapter; a later normal map update may retry. Log the underlying exception without repeating it continuously.
- Unsupported reflection metadata is diagnosed separately from an unready minimap.

Reference inspected during this pass: the decompiled Marketplace 9.8.9 territory client at `https://valheim.hexium.gg/mods/KG/Marketplace_And_Server_NPCs_Revamped/versions/9.8.9/decompile/download`. This is evidence for that API shape, not proof that the owner's exact installed binary has been executed or that every later version is compatible.

### Related minimap corrections

`RevertTextures` now selects the normal terrain palette before applying it; previously disabling seasonal map colors in winter could reapply winter colors.

Map generation captures its world generator, dimensions, and vanilla palette on the main thread. Its worker no longer reads changing `Minimap.instance` or `WorldGenerator.instance` singletons while another world is loading. Destruction cancels further rows; worker exceptions are contained and reported; results are applied only to the same surviving minimap.

Unknown modded biome IDs are not silently converted to white. Their palette lookup is deferred to the main thread and still goes through the game's/modded `GetPixelColor` path.

The controller's owned forest texture is released on teardown. Regeneration reapplies seasonal colors, and hooks tolerate the variant controller not existing yet.

## 3. Confirmed Blood Moon corrections

| Area | Baseline defect | Correction |
| --- | --- | --- |
| Remote-client world lifecycle | `EnsureWorldLoaded` required server `State != null` even on clients, where authoritative `State` is intentionally absent. Repeated fixed ticks could clear leases, effects, temporary items, recovery, and attribution. | Distinguish client initialization from server state ownership. Initialize a world before consuming its client runtime state. |
| Environment startup ordering | A world-change reset could run after recovery acquired the event environment. | Run presentation world initialization before recovery/publication and reapply an already delivered client snapshot. |
| Snapshot session reset | Cached publication signatures and pending actions could survive a session reset. | Provide an explicit network reset; clear snapshots, private details, pending actions, and signatures. Keep publisher revisions increasing within the process. |
| Resync and fade ordering | A delayed different-event resync or unscoped fade could affect a newer event. | Compare cross-event publication revisions; queue event-scoped fade until the matching snapshot; ignore terminal stale fade starts. Preserve the existing same-event revision normalizer. |
| Listen-host resync routing | A remote request claiming the host's player ID could choose the local apply branch. | Local application requires both the validated local player and a server-originated request. This is routing integrity, not a new combat anti-cheat policy. |
| Local defeat acknowledgement | The owner already healed and cleansed before reporting defeat; the returned action could do it again, including after recovery had expired. | Use the local/durable terminal marker to make the acknowledgement idempotent. It does not heal, cleanse, or create a second protection window. |
| Defeat during shutdown | Routing could still say `Fighting` after global blood behavior had been disabled during resolution. | `CheckDeath` interception also requires live blood behavior. Existing recovery protection remains a separate lifecycle. |
| Extra identity | `-1 == -1` classified unmarked monsters as event-created when there was no current event. Unconditional loot/ragdoll hooks could affect normal enemies. | Current-event classification requires a nonnegative event ID. Lifetime no-loot/ragdoll behavior requires an actual nonnegative spawn marker. |
| Stale extras | An extra from another event could fall through to ordinary-monster conversion. | A marked extra only belongs to its own event; it is not reclassified as an ordinary converted creature. |
| Lease renewal | A same-revision renewal could restore allowance already spent before reports reached the server. | Retain per-key revision/remaining-token history; same-revision renewal may only reduce allowance. A new grant requires a new revision. |
| Spawn validation | A moving extra could cross a sector boundary before its report arrived. A partially replicated ZDO was also treated differently from an absent one. | Write immutable spawn-zone markers, validate the origin rather than current sector, and wait for complete custom metadata. |
| Duplicate pending reports | A duplicate could reach cap/rejection logic before the original reservation was completed. | Recognize already-pending ZDO IDs before spending another token or queuing deletion. |
| Delayed cleanup | An unmarked initial ZDO could prematurely remove its cleanup watch. | Wait for the event marker within a bounded realtime window; never delete an ordinary unmarked object. |
| Nested attack context | A nested proc/projectile/AOE could clear its caller's thread-local combat context. Both postfix and finalizer also called cleanup. | Capture invocation-scoped state and restore it idempotently. Preserve the caller's attacker, immutable attribution, and credit flag. |

Existing base-game target methods were retained. No new personal visibility layer, forced player relocation, boss policy, reward formula, or visual design was introduced.

The resync detail projection continues to use the durable per-skill bonus ledger. `LiveSkillBonusUsed` is a computed, non-persisted total rather than a second stored counter. Existing skill baseline and other hardening adapters remain part of the patch stack and must be considered when reviewing the effective behavior.

## 4. Compatibility boundaries retained

- Existing monsters keep ordinary loot and are never deleted by event cleanup.
- Event-created extras remain no-loot even if their current global snapshot is temporarily unavailable or stale cleanup is pending.
- Damage permission and skill credit remain separate. This pass does not change the accepted lifetime of already-created projectiles after their player exits.
- A local Player still owns defeat detection; the server receives an idempotent notification, not an independently proven health event.
- A zone's owner remains responsible for actual spawning. No group-wide substitute coordinator was added.
- Do not reintroduce a custom DreamText overlay or mandatory input lock based on superseded early design text. Read `22_DREAMTEXT_AND_INPUT_POLICY.md` before changing presentation.
- No additional dependency on Marketplace, Balrond, or a specific biome mod was added.

## 5. Owner-side validation scenarios

These are outstanding runtime checks, not claims of tests performed by the assistant.

1. Start a normal world without an active Blood Moon. Kill an ordinary creature and confirm normal loot/ragdoll behavior. Repeat with Blood Moon disabled.
2. Join a dedicated server during Marked and Active. Confirm state/effects persist across fixed ticks and that temporary items are not repeatedly cleaned.
3. Leave and rejoin the same world in one process, then join another world. Check current snapshots, participant details, leases, and no stale fade.
4. Exercise `start marked`, forward time jumps, backward time jumps, forced end, explicit cleanup, and administrative restart. Check phase/status and preserved history.
5. Trigger Defeated, then observe delayed/repeated notifications and reconnect during protection. Confirm one heal/cleanse and no extra protection grant.
6. Renew a zone lease while reports are delayed; spend it completely, renew the same revision, and later issue a new revision. Confirm no replenishment from the old revision and normal spawning from the new one.
7. Spawn near a sector boundary and allow the extra to move before the report is accepted. It must remain valid based on its spawn origin.
8. Simulate report-before-ZDO and report-before-custom-fields ordering, repeated pending reports, and resolution during pending replication. Check caps and bounded cleanup without deleting ordinary objects.
9. Use an attack that triggers nested AOE/projectile effects. Check damage routing, no world damage, and skill credit before and after the nested invocation.
10. With Marketplace installed, load/reload a winter world, change seasons, disable/re-enable seasonal minimap colors, regenerate the map, and exit during generation. Check ordinary terrain colors, territory overlays, and logs.
11. Include registered or custom biome IDs when validating map generation; their configured colors must remain intact.
12. Confirm unchanged boss restoration, inventory cleanup, and the current vanilla-sleep DreamText policy around resolution.

## 6. Continuation and review

The branch contains the implementation, this checkpoint, and the earlier subsystem documents. Continue from the current PR head, not from the historical baseline SHA above.

Inspect the final delta from the baseline, request Codex review in PR #42 after all edits, and record the exact target SHA in the request. Do not treat older clean reviews as covering this pass. The PR must remain draft and unmerged.

If a new review identifies a confirmed regression, fix the actual production path and its existing adapters together; avoid adding another self-Harmony wrapper solely to override a previous wrapper. Do not build or run Valheim on the assistant side unless the owner changes that instruction explicitly.
