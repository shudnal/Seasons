# Snow runtime follow-up: summer loading and release migration

Updated: 2026-09-22. Branch: `perf/snow-performance`.

This follow-up supersedes the door, snapshot layout and non-winter activity descriptions in the original integration progress document. Historical gameplay acceptance of the visual layer is unchanged. The changes below have not been built or tested in the game by the assistant.

## Revisions

- Public/master migration reference: `988e98c514ce49369a92cbaf934a7aca384fe6e1`.
- Connected simulation baseline: `2ffa4ad7eea1c083711c4eb538285b0d97cd23b0`.
- `d12a7e7`: compact persisted snapshots and one revision update per changed snapshot.
- `6b44976`: remove door tracking, suspend non-winter runtime work, filter geometry notifications, preserve release migration semantics and checkpoint departing ownership.
- `07de449`: restrict incoming interaction signals to actual station or attachment targets.
- `436c527`: honor failed-readiness retry intervals, bound readiness work separately, and gate visual preparation before queue insertion.
- `5e5b3ef`: reject unmarked summer pieces before eligibility/biome work and hide loaded legacy cap roots directly.
- `04f41b0`: reuse registrations, avoid a whole-region refresh for each new piece, and reject unrelated incoming ZDO changes before queueing snow work.
- `9f2a1c9`: preserve hide priority across target coalescing and prevent a retired runtime's queued target from restoring summer snow.
- `a0c11e3`: bypass rule lookup for already neutral native fields and avoid non-winter geometry-position reads on destruction.

The screenshots from the public-build report contain the old nested `Seasons.SeasonalSnow+...` patches. They must not be attributed to the newly connected controller just because both versions use the same package version.

## Current acceptance boundary

The implementation is connected. These follow-up commits tighten the existing runtime rather than add another producer, a new snow format or a new configuration option. There are no deliberately unconnected files in this slice. No further broad rewrite is required before maintainer gameplay verification; measured regressions or independent review findings may require targeted fixes.

The latest reduced-mod screenshot still shows Summer and the old nested snow handlers. It strengthens the public-build regression report, but does not measure this branch. The high call count of per-piece handlers makes repeated work important even when a single invocation is much shorter than the recorded multi-second freeze. Individual-call maxima must not be added as though they were one frame.

### Streaming guards added after e70bca8

- A failed region readiness check retries no sooner than 0.5 game seconds later. Repeated creation notifications no longer bypass that deadline. A previously ready region dirtied by an actual event still gets prompt validation. Native `IsAreaReady` semantics are retained.
- Readiness has its own limit of two native checks per frame and a 1 ms elapsed-time guard. A deferred candidate retains its dirty state and retry time. One native check is indivisible and can itself exceed the scheduling guard.
- Publishing a readiness result to pieces no longer immediately dirties the same readiness result again.
- Registration queues the new piece and marks region readiness, instead of requesting a full region pass for every new piece. Actual geometry notifications still invalidate neighbors.
- Existing valid registrations return before eligibility work. Ordinary native visual requests do not call registration again.
- The existing 0.01 remapped visual-level gate is checked before adding a prepared visual to the runtime queue. Sub-threshold arithmetic remains exact. Existing renderer-queue entries consume the newest runtime value without another preparation entry. Root/health-variant changes and forced terminal states remain eligible.
- A hide stays high priority until the previously applied root has actually been hidden. A queued visual whose runtime was retired during non-winter cleanup cannot resurrect its old positive target.
- Incoming ZDO changes with the same owner and snow snapshot do not wake snow buckets. Health geometry and heat activity retain their separate notification paths. The persistence schema, publication thresholds and ownership checkpoint semantics are unchanged by this slice.
- Summer metadata checks happen before prefab eligibility and biome lookup. Clean native fields return before rule lookup in the wear boundary hooks. Marked legacy cleanup can hide the three existing cap roots without discovering renderers or allocating material pools.

## User-supplied video evidence

The recordings are `removed-nps-vcp.mp4`, `without Seasons.mp4`, and `with-low-settings.mp4`. They are not repository assets. The reporter was in summer without snow caps. NetworkPerformanceSystem and ValheimCommunityPatch had been removed in the first recording.

In the first recording, house geometry begins appearing around 00:15 and more structures appear around 00:19. The first conspicuous interrupted movement begins around 00:21, rather than at the first visible object. Later interruptions are longer; near-static game-image intervals include approximately 00:39.3-00:40.9 and 00:45.3-00:47.4. These are video observations, not sampled CPU call durations. A static-image interval alone cannot identify the responsible method.

The network HUD shows an incoming rate of 75.0 KB/s around 00:20-00:25, but 97.5 KB/s around 00:32. Thus 75 KB/s is not a demonstrated fixed ceiling for this recording. The displayed FPS is averaged and must not be treated as the duration of an individual frozen frame.

The recording without Seasons does not show comparable repeated multi-second interruptions during the inspected approach. Lower graphics settings do not remove the visible interruption pattern in the third recording. These are useful comparative observations, not a controlled benchmark: removing Seasons changes more than snow and the recordings have different timing and settings.

## Confirmed public-build CPU work outside winter

In master, `QueueSnowInitialization` does not check the season. `ProcessPendingSnowInitializations` clones and walks the entire pending set on each scene creation pass. It calls `IsAreaReady` before `TryInitializeLoadedSnow` reaches its non-winter exit. Readiness is cached per zone only for that pass, not across passes.

The native `ZNetScene.IsAreaReady` is not a flag read: it gathers nearby sector ZDOs and checks prefab validity and instance existence. As loading progresses, more entries can be checked before the first missing instance terminates the scan. The old `UpdateWear` and `UpdateCover` prefixes also enter activity/ownership bookkeeping before checking winter.

Additionally, master's `TryInitializeSeasonalSnowOnStart` initializes the highest-collider snow probe origin outside winter, before queuing initialization. The regular seasonal upward cover cast has winter guards. Therefore delayed freezes do not by themselves prove that the winter cover cast is running in summer; startup probe preparation and repeated readiness bookkeeping are distinct confirmed costs.

The new branch no longer contains the old producer. The follow-up closes remaining non-winter paths in the new runtime instead of patching the removed implementation.

## Non-winter boundary

When `WinterReady` is false:

- Do not initialize a snow simulation scene, register pieces or heat sources, discover instances, poll heaters, prepare cover geometry, expand region jobs or integrate snow.
- Do not record winter placement on unrelated pieces or allocate interaction target caches.
- Global ZDO transform callbacks return without reading rotations or inspecting prefabs.
- Retain necessary instance/save cleanup, scene teardown and completion of an already-started end-of-winter hide/cleanup pass.
- Check legacy metadata on Awake/Start; ordinary clean summer visual callbacks bypass that work. A positive native value arriving later may trigger cleanup again.

Disabled-cap enforcement, one-time cap configuration, and native Deep North behavior remain separate from seasonal simulation. This is not a claim that every Harmony callback or every other Seasons feature has zero cost outside winter.

## Doors and geometry

There is no Door patch, Animator access, moving-door registry, completion polling or animation-driven cover invalidation. Static snow on a door remains an ordinary piece cap. Building or removing the piece still uses ordinary geometry notifications.

Network-object arrival/removal separates area readiness from changed cover geometry. A per-prefab cache recognizes stationary colliders in the existing snow cast mask. Dynamic characters, drops, vehicles and projectiles do not invalidate neighboring roofs just by loading. Readiness-only notifications mark region metadata without queuing every piece.

A neighboring geometry change reruns the cover cast but does not rediscover the receiver's own collider hierarchy. Placement, health-variant changes, transform changes and snow-mesh changes still invalidate the receiver's own cached geometry when necessary. The existing cover cast, radius, origin rule and self-filter are preserved.

## Release migration and network footprint

Migration reads the public/master native snow float together with its Seasons winter marker, from timestamp and baseline. It is not a version ladder for experimental builds. Explicit zero remains a saved value. Master's pre-winter baseline could be zero because the initial minimum was implicit in `GetSnowTarget`; first confirmation retains that implicit contribution for a positive legacy value instead of losing it when importing to a full-value snapshot. Later accumulation never refills the minimum automatically.

A normal new snapshot contains:

| Field | Type | Key plus value bytes |
| --- | --- | ---: |
| `Seasons_SeasonalSnowValue` | float | 8 |
| `Seasons_SeasonalSnowEpoch` | long | 12 |
| `Seasons_SeasonalSnowFrom` | long | 12 |

This is 32 bytes of key/value payload, not the complete ZDO or network packet. An ordinary positive master state uses about 44 bytes for native snow, baseline, from, winter flag and watermark; optional flags add more. Winter construction adds two placement longs, another 24 bytes. Group counts, ZDO identity/revisions, other object fields and transport framing are excluded from these figures.

New publication removes redundant legacy fields and native snow/pre-snow data, then increments DataRevision once if something changed. Native default reads for the absent fields remain zero/false. ZDO replication serializes the complete selected object, not just the one changed snow field: publication frequency can matter more than the 32-byte snapshot. No transport-rate limit is changed.

Before leaving an owned active area, checkpoint the working value and consumed-weather cursor while authority is still local. A direct local `ZDO.SetOwner` transition checkpoints before changing ownership too. Never publish from the incoming `SetOwnerInternal` callback: the network reader may already have installed the incoming revision and still be about to deserialize its payload. Forced ownership races and multiplayer delivery order still require gameplay verification; this is not an atomic cross-peer handoff protocol.

## Interaction signals

Incoming activity still carries no client-selected snow level. An attachment signal requires the player's replicated sync-transform connection to point to the target. A station signal requires an actual active CraftingStation on the cached target hierarchy and proximity to its use point/range, with a small position margin. Proximity to an arbitrary snow-covered wall is no longer sufficient.

Vanilla does not replicate the exact currently open crafting station through Player.GetCurrentCraftingStation. Do not validate a remote player with owner-only local state or reintroduce Animator dependencies. The station signal remains an activity assertion constrained by target type and distance, not authoritative proof that the crafting UI is open. Target component lists are cached and released with the piece/world.

## Verification still required

Source checks for this follow-up cover changed-file diffs, call paths and native API signatures. They are not compilation, runtime tests or profiler measurements. No version, snow JSON schema, packaging, master branch or blood-moon branch changes are included. The latest slice does not change material pools, remap, LOD bindings, weather integration or snapshot layout.

Rebuild locally and restart every participating peer with the same branch revision. Keep the old public-build profile as the baseline, not as an acceptance result for this implementation.

| Check | Expected boundary |
| --- | --- |
| Fresh summer approach to the reported base, then a second approach | No seasonal registration, readiness scans, heat polling or cover casts; only applicable legacy cleanup. Old nested producer patch names are absent. |
| Winter, then switch to summer with pending work | Accumulation/melting stops; all cap roots hide without a later queued positive target; subsequent streaming remains idle. |
| Winter approach during gradual object streaming | Saved-first distant appearance remains; ready-area confirmation completes; failed readiness probes respect the retry interval rather than object count. |
| Snowfall, one/two heaters, added/removed roof, normal/worn/broken and child/LOD caps | Exact simulation with coalesced visual changes; no lost final hide, health-variant change or terminal level. |
| Import master positive/zero states, construction, save/reload and owner changes | Release-format import and weather cursor rules remain intact; unrelated ZDO changes do not reset or wake snow. |
| Dedicated server and multiple clients, including remote-owned stations/chairs and ice floes | Validate interaction delivery, ownership transitions, floe uniqueness/distance/cleanup and network queues separately. |

For performance comparison, keep route, weather, camera, mod list and profiler settings comparable. Separate first-time legacy cleanup from repeated steady-state loading. Compare frame-time spikes and per-frame totals as well as individual-call percentiles. The fixed retry/queue guards do not establish a measured FPS gain or prove that every reported freeze was caused by snow.

Independent Codex review is still pending. No PR was opened. The available connected review actions require a PR; no authorized local Codex CLI is available in the assistant environment. Review the branch in the maintainer's local Codex worktree, prioritizing lifecycle, release-format import, summer idle behavior, ownership cursor handoff and interaction compatibility. Do not build the mod or run tests during that review.
