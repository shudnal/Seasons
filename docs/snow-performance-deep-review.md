# Snow initialization deep review

Updated: 2026-09-22. Branch: `perf/snow-performance`.

Review baseline: `fd01c2a941463c76b55d86d4009b67ec041a41b4`.
Reviewed implementation head before this document: `4a38dd74cd518304d52a44f9ae93b1236d0e80b1`.

This review concentrates on burst work while a large built area is streamed or seasonal snow is initialized. It does not replace gameplay acceptance. The assistant did not build the mod, run the game, or execute automated tests.

## Result

The steady-state model remains arithmetic over the four regional buckets. The expensive work is isolated to readiness, receiver geometry, heat topology, visual binding, and persistence boundaries. The review removed repeated initialization paths and gave every repeatable expensive category an independent scheduling boundary.

No second snow producer, per-piece MonoBehaviour, new configuration format, or new network message was introduced.

## Initialization scheduling

| Work | Current scheduling boundary |
| --- | --- |
| Region readiness | At most two native `IsAreaReady` calls per frame, with a 1 ms scheduling guard and a 0.5 second retry deadline after failure. |
| Region-to-piece expansion | Up to 128 pieces per frame. Repeated regional causes coalesce. |
| Cheap piece refresh | Up to 256 pieces per frame. |
| Receiver geometry and heat-link rebuild | Up to 20 pieces per frame with a 1 ms scheduling guard. |
| Heat-source geometry/topology | Up to four sources per frame with a 0.5 ms scheduling guard. |
| Visual target preparation | Up to 50 pieces per frame. |
| First renderer/material binding | Up to ten caps per frame with a 1 ms scheduling guard. |
| ZDO publication | Up to 50 changed snapshots per frame. |

A single Unity call cannot be interrupted after it starts. The elapsed-time guards therefore prevent additional work from starting; they are not hard upper bounds on one unusually expensive collider query, material creation, or native readiness check.

## Review changes

### Geometry and readiness

- Geometry revision increments are coalesced per current or pending regional pass instead of once per streaming event.
- A streaming object invalidates cover only for a region that was already ready. An unready region performs one complete confirmation after native area readiness succeeds.
- `CreateDestroyObjects` no longer walks all regions when the reference zone and synchronized simulation distance are unchanged.
- Initial network deserialization does not classify or compare transforms for ZDOs that do not have an instantiated `ZNetView`; `AddInstance` supplies the required creation notification.
- Cover-capable prefab classification is cached by prefab hash. Dynamic characters, drops, vehicles, and projectiles do not become roof invalidation sources.
- A snow receiver reuses `WearNTear.m_colliders` or performs one inactive-inclusive collider discovery. Neighbor changes repeat the upward cover cast without rediscovering that receiver hierarchy.
- Reference-position checkpoints inspect only regions that can overlap the departing active area.
- Destruction no longer sends equivalent invalidations from both `ZNetView.ResetZDO` and `WearNTear.OnDestroy`.
- A snow-mesh transform/copy change refreshes snow rules and visuals, but does not rebuild unrelated receiver roof geometry or static heat links.

### Visual initialization

- Renderer/LOD/child discovery and material ownership are deferred until a cap must actually become visible.
- A never-visible zero or disabled cap hides its three known roots directly and does not allocate a visual state, enumerate descendant renderers, detach from `MaterialMan`, or create pooled materials.
- First bindings have a separate frame/time budget from ordinary visual applications.
- Single-material renderers use a dedicated path without per-instance material arrays. Multi-material renderers preserve unrelated slots.
- Descendant renderer collections are reused during binding instead of allocating a new array for every composite cap.
- An unchanged remapped 0.01 visual level is rejected before entering the runtime visual queue.
- The recurring `UpdateSnowVisual` finalizer no longer reapplies snow mesh settings. Mesh rules are applied at lifecycle/configuration boundaries.
- Ordinary summer visual callbacks avoid prefab-name and snow-rule lookup. Disabled direct wet/cap aliases remain suppressed.

Composite caps and LODs remain supported. Their dependency scan still happens once per visible instance because component references are instance-specific.

### Heat topology

- Pieces allocate a heat-link list only if they actually link to a source.
- One immutable area snapshot is stored per logical heat source and shared by all linked pieces; it is rebuilt only when source topology changes.
- Distance configuration values are read once per link rebuild, and squared distance rejects out-of-range areas before `sqrt`.
- Independent sources still add. Multiple areas belonging to one source still contribute only that source's strongest applicable weight.

### Arithmetic and rules

- Registration performs one seasonal eligibility/biome decision and reuses the resulting biome.
- Existing valid registrations return before repeated eligibility work from Awake, Start, discovery, or native visual callbacks.
- Per-frame world time is read once and passed through queued piece refreshes.
- Cumulative weather gain is resolved once per biome group rather than looked up for every piece in the group.
- Native wear-field boundaries return before rule lookup when the native snow fields are already neutral.
- Unrelated ZDO revisions do not wake snow state when owner and snow snapshot are unchanged.

### Persistence

- A new snapshot contains one float and two longs in the ordinary case. Legacy/native fields are removed during migration rather than duplicated indefinitely.
- Multiple field changes in one snow snapshot increase `DataRevision` once.
- Confirmation, ownership, and rule refreshes do not publish an unchanged snapshot merely because the refresh was important.
- A sleeping zero/full piece advances its persisted weather cursor only when snowfall actually occurred during an interval that must be recorded as consumed.
- Save, unload, and ownership checkpoints skip exact writes when both value and required consumed-weather cursor are already represented.

The game still replicates the selected ZDO as a complete object. Avoiding a revision is therefore more valuable than reducing a few bytes in an already changed snapshot.

## Deliberately unchanged after review

- Per-piece interaction RPC registration remains. Replacing it with one global routed RPC would change the network protocol and validation boundary; no profile supplied evidence that delegate registration is the initialization bottleneck.
- `SeasonalSnowMeshSettings.ApplyToLoadedInstances` may use `Resources.FindObjectsOfTypeAll` on a snow-rule configuration rebuild so inactive placement previews are updated. This is a rare configuration path, not ordinary base streaming. It should be changed only with a measured reload problem.
- Save preparation still inspects registered pieces. That work occurs at an explicit persistence boundary and preserves exact final state.
- The custom cover cast, center-plus-eight-neighbor invalidation scope, ownerless distant prediction, and active-area authority rules remain unchanged.

## Static verification boundary

The review checked changed-file control flow, queue coalescing, native method/property availability, Harmony target names, and `WearNTear` collider lifecycle against the current game-source mirror. Project files, version, packaging, configuration names, JSON schema, cap remap, and the 50-per-frame ordinary visual limit were not changed.

Static inspection is not compilation or runtime proof. The maintainer should rebuild locally and restart the game before comparing profiles.

## Gameplay and profiling checks

1. Repeat the reported large-base approach in summer. Snow simulation, readiness, geometry, heat, visual binding, and snapshot publication should remain inactive except for one-time legacy cleanup.
2. Repeat the same approach in winter with an existing saved base. Watch readiness, receiver geometry, heat-source topology, initial renderer binding, and ZDO publication as separate rows.
3. Compare first approach and second approach. Persistent initialization work should collapse after the first confirmation; ordinary runtime should be arithmetic plus actual weather/heat changes.
4. Verify composite caps such as fences and thrones, all damage variants, LOD switching, wet-roof aliases, and copied caps.
5. Verify saved positive and explicit-zero states, new construction, roof add/remove, one and multiple heaters, station/attachment interaction, unload/reload, ownership transfer, winter exit, and native Deep North.
6. In multiplayer, compare ZDO revisions and network traffic before and after initial confirmation. No repeated unchanged snapshot publication should remain.

Any remaining frame spike should be attributed to one of the separated queues or to an indivisible Unity/native operation before changing the architecture again.
