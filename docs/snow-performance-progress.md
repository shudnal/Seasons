# Snow performance implementation progress

Design reference: [snow-performance.md](snow-performance.md).
Baseline: `f77249d625dadf5e5262cb969a9a8455e3c7ccbc` on `perf/snow-performance`.

## Current boundary

Cap rendering, Seasons-owned float persistence, and saved-first ready-area confirmation are connected. The four-list simulation replacement is **not** connected yet. This branch is an incremental implementation, not the completed optimization or a release candidate.

The existing `SeasonalSnow` calculation remains the producer. Its geometry, active-area heat/interaction rules, and wear-driven calculation cadence remain in place. Native `WearNTear.Awake`, `UpdateWear`, and `RPC_SetSnow` select the private snow key only for eligible seasonal pieces. This is a transitional storage adapter, not the final simulation scheduler. Neither global ZDO methods nor Seasons' own methods are patched.

The visual controller reads `SeasonalSnow.GetVisualSnow()` explicitly. The previous prefix/finalizer substitution of `m_snowBuildup` for provisional drawing has been removed. The runtime field still carries the old producer's real value until the next calculation stage; do not confuse that remaining dependency with positive writes to the vanilla save key.

## Landed slices

| Commit | Scope |
| --- | --- |
| `69d848c` | Immutable material pools, one original and up to 100 lazy variants per source material. |
| `4a65dde` | Singleton-owned renderer bindings, latest-target visual queue, 0.01 visual change gate, and at most 50 applications per frame. |
| `ff9f85d` | Disabled and copied caps use the same ownership/cleanup path. Remove MaterialMan calls from `SeasonalSnowMeshSettings`. |
| `3d3a21a` | Scope the queue to its scene, reject stopped-scene work, prioritize hides, restore native Ignore visuals once, and bound unsupported-material warnings. |
| `4bb9775` | Prevent native wet visuals from toggling an aliased managed snow cap. |
| `bf1880f` | Pause ordinary visual queue processing with the game. |
| `376d000` | Record maintainer visual-parity and qualitative frame-stability feedback. |
| `605ef4c` | Disable seasonal stat modifiers in Ashlands and Deep North; maintainer confirmed this separate fix works. |
| `3a9bddd` | Add private float/epoch keys and storage primitives without connecting them yet. |
| `fad274c` | Connect private persistence, explicit visual selection, and one complete ready-area confirmation; remove the automatic minimum top-up and whole-world ZDO cleanup. |

No version, gameplay configuration default, snow JSON schema, or release changelog has changed.

## Implemented rendering behavior

- Capture each distinct normal/worn/broken renderer once, preserving source materials and non-snow material slots.
- Preserve the native 0.25 visibility threshold and damaged-cap fallback order.
- Clear renderer-wide and per-slot property blocks when taking ownership. Exclude managed caps from existing MaterialMan assignments and future renderer refreshes, without filtering unrelated piece renderers.
- Reuse `sharedMaterial` variants; do not mutate originals, use `renderer.material`, or involve `PrefabVariantController`.
- Deduplicate pending visuals. A later value replaces the target rather than appending another percentage to replay.
- Restore original materials before destroying pooled clones. Release copied renderers before removing their objects. No additional MonoBehaviour is attached to a piece or heater.
- Drive visual application from the existing scene Update callback, once per frame for the current scene. Reject shutdown callbacks from a different scene.
- Do not create material variants on a dedicated server.

## Private persistence and single confirmation

`Seasons_SeasonalSnowValue` holds a float, including an explicit zero. `Seasons_SeasonalSnowEpoch` identifies the winter in the existing season calendar. The existing from/baseline fields remain the weather-history cursor and now store the full confirmed buildup. Legacy pre-winter baselines are interpreted under their old convention only before receiving the epoch.

On activation, a current saved value takes priority over prediction, including saved zero. If there is no applicable saved state, the existing distant prediction remains available before `IsAreaReady`. No new LOD/distance cutoff or owner requirement is imposed on that visual path. `owner == 0` remains eligible for the existing initialization policy; a foreign owner is never claimed or overwritten for this purpose.

When the area is ready, calculate the complete cover/shield/history result before clearing the provisional flag or requesting a new visual. Publish the final private value and its baseline context, then request one visual target. The queue retains only the latest target, not an intermediate zero or minimum. Ownership transitions without a complete current epoch return to pending confirmation rather than silently accepting missing state.

A discovered eligible exposed piece without saved state receives the configured minimum plus timeline snowfall. A piece placed during winter records a current zero snapshot before reconciliation and receives only later gain. The native growth postfix no longer lifts a new or melted piece back to the minimum; new confirmed baselines never add that minimum again.

Legacy migration is idempotent and limited to loaded managed pieces with existing Seasons metadata. Copy the native level only when the private value is absent, then clear native snow/pre-snow; also finish cleanup when a private value was already copied. Do not migrate ordinary Deep North snow or an arbitrary unmarked third-party value. A non-owner waits for the owner's migration while retaining a read-only legacy view. Ownerless initialization follows the existing policy without claiming ownership.

`ClearServerSeasonalSnowZDOs` and its whole-world scans have been removed. Loaded pieces are cleared on season exit, and an epoch mismatch prevents a previously unloaded winter value from becoming current. After a piece has been processed by this version and the world has been saved, removing Seasons leaves the vanilla snow key at zero. This does not clean an untouched legacy area, promise recovery of unsaved changes after a crash, or remove native Deep North snow.

### Deliberately still transitional

- The old native producer and `m_snowBuildup` remain, as does the existing interactive `RPC_SetSnow` route with its private-key adapter. Remove these dependencies when the four-list replacement is connected, not by running a second simulation alongside them.
- The maximum remains 0.99 and configuration defaults remain unchanged while native calculation is still present. A full 1.0 maximum belongs to the next producer replacement.
- The 0.01 gate currently limits visuals, not all ZDO publication. Publication throttling requires a separate precise runtime value and coherent history cursor; it is not implemented by merely rounding the save value.
- Current heat still uses the old lookup, active-area conditions, and rates. During saved-state reconciliation, an active heater keeps the saved value and rebases under the old rule instead of reconstructing past fuel usage. Additive static links, inactive-area heat reconciliation, and the final missed-weather bookkeeping remain for the heat/list stage.
- The custom roof algorithm is unchanged, including collider-derived origin and self exclusion. Real snow under newly added cover is still cleared by the old path; gradual covered melting and sector-triggered invalidation are not connected yet.

## Still required by the approved plan

1. Four region-partitioned simulation buckets and region-filtered state-refresh work, replacing seasonal calculations in UpdateWear and the transitional native runtime field.
2. Cached stationary heat links, additive independent sources, preserved live interactions, and the covered-piece melt setting.
3. Event-driven custom roof invalidation and separate geometry/publication budgets, with precise simulation independent of saved/visual steps.
4. Complete weather-cursor bookkeeping across unload, ownership changes, and dormant heated pieces; finalize network wake-up paths without reducing distant visualization.
5. Full 1.0 maximum after removing the native snow producer, and gradual melting of real snow under newly added cover.
6. Maintainer-side functional and performance acceptance. No quantitative FPS improvement is claimed from static inspection or qualitative feedback.

## Verification performed

Static review of native target signatures, project XML/source inclusion, duplicate includes, changed-file scope, lexical delimiter balance, and accidental Cyrillic outside localization. Remote content hashes are compared with the reviewed local files. The custom cover probe/cast methods were compared with the pre-change source and are unchanged.

No compilation, mod execution, automated gameplay tests, or in-game profiling was performed by the assistant.

### Next maintainer checks

Use a copy of the world and restart the game after rebuilding. In multiplayer, all participating peers need this branch build: the package version alone does not distinguish its private storage format from earlier incremental builds.

| Scenario | Check |
| --- | --- |
| Existing snowy base | Caps retain the verified appearance, native `s_snow` becomes zero on processed seasonal pieces, and `Seasons_SeasonalSnowValue` contains the level. |
| Saved zero, then reload in clear winter weather | Saved zero is used instead of the minimum; test a naturally melted/new piece so its history cursor is coherent. |
| Approach from distant rendering into a ready and then active area | Saved/predicted visual changes directly to the final confirmation; no intermediate hide, minimum, or duplicate weather gain. |
| New construction during winter | Starts clean; later snowfall grows from zero without jumping to the configured minimum. |
| Legacy save and ownership transition | Migration retains existing snow and clears native storage without taking ownership. |
| Season override, normal season change, reload in a later winter | Old winter data is not reused as a current snapshot; season exit removes added caps. |
| Processed world copy saved, then loaded without Seasons | Added seasonal caps do not return; native Deep North remains native. |
| Existing interactive/heat behavior, Ignore/Disabled, copied caps | No regression from storage routing; future heat/cover mechanics are not expected yet. |

## Roof wet-visual alias regression

The maintainer reported disappearing sloped-roof caps after enabling Winter with Season override. A `wood_roof_45` dump showed saved buildup `0.7144539`, a positive controller target, an applied snow material, empty property blocks, and an inactive cap object. A second dump confirmed `m_wet == m_snow.gameObject`, with no alias in the health-visual objects.

Native `WearNTear.UpdateWear` toggles `m_wet` from `m_rainWet` before its snow-visual refresh. The former unconditional snow drawing could undo that toggle. The new 0.01 gate correctly skips unchanged material levels but exposed this second writer of the same object's activity. This evidence identifies a visibility conflict, not a failed custom roof cast or an incorrect remap.

Commit `4bb9775` filters only `UpdateWear`'s `m_wet` field reads through the controller's cached cap bindings. A managed cap is not returned as a wet visual, so the conflicting `SetActive` call is skipped. The actual field, all ordinary wet effects, rain wear, and native behavior after release remain unchanged. This prevents the conflicting write instead of adding a repeated scan or reactivation loop. No snow amount, threshold, timeline, cover query, or initialization predicate changed in that fix.

The scene-update driver also defers ordinary visual queue processing while `Game.IsPaused()` or `Time.timeScale <= 0`. Pending targets remain coalesced and resume without replaying intermediate levels. Explicit scene teardown and visual release still run; this is not a freeze of network state, configuration changes, or every vanilla callback.

## Maintainer follow-up: visual parity and frame stability

Reported on 2026-09-21 after the wet-visual fix and pause guard in `bf1880f`. These are maintainer-run observations, not assistant-executed tests.

The maintainer confirmed that caps no longer flicker in the previously reported scenario. A subsequent concern that minimum caps were approximately 30-40% lower was not reproduced when comparing the branch with the master build on winter day 1, with default settings and no snowfall. In that comparison the new visual matched the old visual.

The REPL height probe reported:

```text
buildup=0.51; materialLevel=0.34; exactLevel=0.3466667
```

Applying the exact unquantized value produced only a barely noticeable visual difference. Do not compensate by raising the minimum, removing the native remap, changing cap transforms, or changing the 0.01 material step. The suspected large height regression is closed for the tested scenario. Prior snowfall before an earlier day-2 comparison is a possible explanation for the initial impression, not a verified weather-history finding.

The maintainer also reported noticeably steadier FPS with the new visuals, before the calculation refactor. Record this as qualitative frame-stability feedback only: no frame-time trace, FPS gain, low-percentile statistic, or CPU/GPU attribution was supplied. It does not establish that the original weak-PC 40-to-5 FPS report has been resolved.

These results resolve the reported flicker and minimum-height concerns for the rendering stage; they do not mark every lifecycle, multiplayer, distance, material, or configuration scenario in the acceptance matrix as tested. The storage/confirmation changes in `fad274c` still require their own game verification.
