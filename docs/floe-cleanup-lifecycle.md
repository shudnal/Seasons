# Floe cleanup after disable and season changes

Base: `33a53e99f1f353cf71493f3ca4a1efa5e64ca590`, branch
`perf/snow-performance`, PR #45.
Game API reference: `shudnal/assemblies_combined` at
`d1374bfd9175ac8f733ae483b0a06e5c8b75906e`.

## Maintainer observations

The maintainer reports about 70 FPS without the patch profiler. The approximately
5 FPS instrumented screenshot is not an uninstrumented performance baseline.
The supplied counters show forecast reuse and zero aggregate geometry rebuilds
since reset, despite many accepted lossyScale-noise observations. Wind changes
are the largest remaining reported forecast invalidation category. This change
neither retunes prediction nor claims a further FPS improvement.

The reported blocker is lifecycle: disabling ice floes and changing away from
the eligible winter period left the floes visible, preventing a no-floe baseline.

## Source findings

`SeasonalWorldMaintenance.Update` previously returned before ALL discovery when
`WorldGenerator.instance?.m_world?.m_biomeData?.IsReady != true`, or while the
local loading screen was active. This is a terrain-processing prerequisite, not
a prerequisite for removing a known seasonal ZDO. If that readiness chain is
missing or false, floe cleanup cannot run at all. The screenshot does not expose
that field, so it does not prove which runtime gate applied on this machine.

Even when that guard passed, loaded floes had to wait for their sectors in a
worldwide cursor limited to 128 objects per rendered frame. The screenshot has
roughly 1.46 million world ZDOs. The order of this scan can impose a long delay
before it reaches visible floes; limiting destruction itself was not the only
latency. No loaded-instance cleanup pass existed.

The placement controller requests cleanup on a wanted-to-unwanted transition.
Its initial wanted state is false, so entering an already disabled/ineligible
world is not itself such a transition. Other events could request a pass, but
cleanup should not depend on one of them happening later.

## Changes

Only `Controllers/SeasonalWorldMaintenance.cs` and this document change.

Floe cleanup now depends on a connected live world/ZDO manager and its current
policy, not biome-generation readiness or the local terrain-loading screen.
Terrain processing retains its original readiness checks in a separate predicate.
If a cleanup pass skips terrain while it is unavailable, a terrain rescan is
requested after readiness returns. Terrain rules and operation budgets are not
retuned.

The server observes the cleanup policy from its existing ZoneSystem.Update
callback. An explicit `enableIceFloes=false`, departure from the eligible season
or day range, or freezing the ocean requests cleanup. Initial known ineligibility
also requests it. An absent season during initialization is not treated as a
known season change; explicit feature disable can be processed without it.

On a cleanup transition/request, the server walks only its current
`ZNetScene.m_instances`, enqueueing marked floes and spawn markers immediately.
This is a one-off O(loaded-instance-count) pass with no world-sized snapshot and
no destruction inside dictionary enumeration. It is not performed every frame.

Visible floe sectors additionally enter a small priority discovery queue. This
finds their zone-controller spawn markers even if the distant floes have scene
instances but the controllers themselves do not. Priority discovery shares the
128-object and 1 ms discovery limits with the background scan. It does not
instantiate any zone or load terrain. Repeated zones and removals are coalesced.

The existing background world scan still cleans unloaded seasonal floes and
zone markers. A narrowly scoped AddInstance postfix schedules already marked
objects that become instantiated while cleanup is required. The existing moving
floe hook remains eligible after the global scan completes. Neither hook destroys
objects during native initialization/sector insertion.

All actual deletion still runs through the existing SeasonalIceFloes removal
queue and RemoveObject path, including native network destruction and ordinary
component teardown. Only watermarked seasonal `ice1` objects are removed; native
unmarked ice is untouched. Zone-controller objects are not deleted: only their
seasonal spawn-completion markers are reset. No damage or item-drop path is used.

Destruction retains its cap of four removal entries per service call plus the
existing time budget. Large groups disappear over several frames, not through a
single mass-destruction stall. A queued action checks the current placement policy
again; re-enabling eligible floes stops removal and lets the existing placement
controller repopulate cleared zones. For an off/on diagnostic comparison, wait
until the visible cleanup and nearby marker processing have finished before
re-enabling. Deleting and respawning does not preserve each floe's ZDO identity.

## Scope and observability

Cleanup remains server-only, including the host of a local single-player world.
A remote client cannot delete another player's world by changing an unsynchronized
local diagnostic field. `EnableFallbackSimulation`, `EnableWavePrediction` and
the Apply switches do not mean "remove floes"; use the actual enableIceFloes
configuration or a real season/day change.

Priority is based on objects instantiated on the server/host. A dedicated server
without such scene instances still has the bounded world scan. Immediate cleanup
of every client's distant view is not promised, nor was it verified in multiplayer.
Objects loaded later remain eligible for cleanup while the policy is disabled.

The runtime-inspector action
`Seasons.SeasonalWorldMaintenance.GetFloeCleanupStatus()` reports server status,
cleanup policy, a pending loaded pass, counts selected in the last loaded pass,
remaining priority zones and background scan position. It only reads state.
`loadedFloesQueued` and `loadedMarkersQueued` are historical counts for that pass,
not remaining queue lengths or proof that every deletion has completed.
Background scanning can continue after the visible ocean is clear. For the
cleanest FPS comparison, let it finish as well; ongoing terrain maintenance is
separate. No per-frame logging or profiling instrumentation was added.

Water heights, normals, forecast horizons, cache invalidation thresholds, lease
protocol, ZDO motion publication, 70-percent waterline, physics coefficients,
distant bobbing, spawn density, project Compile entries, dependencies and version
are unchanged. Existing persistence fields are reused.

## Maintainer checks

1. In the eligible winter period, disable the actual ice-floe configuration.
   Keep the game running, not paused. Loaded seasonal floes should begin being
   removed without waiting for the global cursor or biome readiness. Wait until
   the group is gone before measuring the no-floe FPS with profiling off.
2. Re-enable the setting in the same eligible period after cleanup. Nearby zones
   whose spawn markers were reset should repopulate through normal budgeted
   generation, without reloading the world.
3. With floes present, change to a non-winter season (or outside the configured
   winter-day interval). Verify cleanup again. This is a season change, not just
   an environment/weather command.
4. Load a world containing previously saved seasonal floes with the feature
   disabled. Verify initial cleanup, later streamed instances and unloaded-data
   cleanup; no additional toggle should be required.
5. Check unmarked native ice, repeated off/on transitions and server-authoritative
   removal across clients. Multiplayer acceptance remains pending.

## Static verification boundary

The reconstructed original maintenance file matches Git blob
`b9db93a67c01fccf464b29ca2c96f0704a3c484a` exactly. Validation covers source/API
review, diff inspection, lexical delimiter/string checks and an accidental
Cyrillic scan of changed files. No mod build, automated mod tests, physics
simulation, Valheim execution or performance measurement was performed.
