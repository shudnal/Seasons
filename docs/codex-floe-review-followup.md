# Floe lifecycle review and next optimization plan

Date: 2026-09-27.
Repository: `shudnal/Seasons`, branch `perf/snow-performance`, PR #45.
Reviewed implementation: `256fe447afebdc1882ae5e20dc23ea3ea88bcd45`.
Reviewed HEAD: `0ebb831f9b8b97d5748c80b3504941a2ae493d84` (documentation follow-up).
Comparison base: `b5ce33e8d391359961fa71101c8a890225433ff4`.

## Execution gate

**Execute Stage A only in the next corrective pass.** Stages B-D are the planned
follow-up, not authorization to combine another optimization rewrite with the
corrections. Stop after Stage A and return reviewable commits and a dispatch/
cleanup audit. The maintainer will request the next stage after reviewing it.
Fetch the current work branch before editing; preserve newer changes and record
the actual implementation base. Do not merge PR #45 or work on master.

Read applicable AGENTS.md files and `docs/codex-seasonal-runtime-lifecycle.md`.
Use `shudnal/assemblies_combined` first for native game source. This review used
`5a2365409cff644d6adaccd2b308178cc4179b19`; the inspected native water and update
paths agree with the earlier reference.

Do not compile, run mod tests, launch the game, or build a surrogate physics or
threading test harness. Static source/API/IL review and textual consistency
checks are appropriate here. The maintainer performs builds and runtime checks.
Do not modify ValheimProfiler, numerical physics, spawning density, scale,
network publication cadence, dependency versions or packaging. Keep project
content in English except intentional localization. Do not add migrations or a
second configuration pipeline. Return an archive if GitHub writes fail.

## Accepted boundaries

Preserve native-owned dynamic contacts, ownerless kinematic following, the soft
lease protocol, native ownership priority, current remote pose smoothing,
fixed Ocean depth 1 / offset 0, 70% nominal collider immersion, smooth wind
adoption, the single rolling-forecast worker and immediate seasonal deletion.
Network behavior still needs the maintainer's multiplayer verification.

The maintainer has explicitly deferred:

- separating the visual hierarchy from the physical root;
- stopping pose updates based on camera visibility, including a new CullingGroup
  policy or offscreen simulation throttling.

GPU instancing in Stage D must consume the existing actual pose; it must not
silently implement either deferred change. Normal graphics batch bounds/culling
are necessary renderer behavior, not a new simulation visibility policy.

The earlier non-spawning report was a day-range mismatch: Winter day 2 versus
configured days 4-10. Do not introduce more speculative spawn repairs for it.

## Stage A: close the lifecycle refactor findings

### A1. Removing the water helper restored redundant native wave sampling

Evidence:

- The new `Controllers/SeasonalIceFloePrediction.cs` deletes
  `FloeWaterObservation` and the UpdateFloaters transpiler.
- `SeasonalIceFloeWaves.Track` removes marked instances from
  `Floating.Instances` and `ZSyncTransform.Instances`, but leaves the native
  Floating component enabled and its water-interactable participation intact.
- Native `WaterVolume.UpdateFloaters` iterates its own `m_inWater`, obtains the
  interactable's transform, computes `GetWaterSurface`, and calls SetLiquidLevel.
  Native `MonoUpdaters.Update` invokes it independently of Floating.Instances.

Therefore every floe still registered with a WaterVolume gets a native
main-thread water-height calculation as well as the accepted custom sampler or
background forecast. This is a source-confirmed redundant path for registered
floes, not a claim that all 900 instances are currently registered or that its
FPS cost has been measured. The implementation document acknowledges restored
native sampling; deleting the Seasons profiler row does not remove that work.

Remove redundant native water sampling for owned seasonal floe instances through
a properly scoped participation/lifecycle solution. Do not restore the generic
per-interactable helper or intercept every unrelated floating item. Audit entry,
exit, multiple water colliders, feature disable, world unload and handoff to the
native instance state. Preserve impact/surface effects only where actually used
and source their levels from the accepted sampler. Do not assume changing a
Rigidbody collision flag removes existing `m_inWater` entries; do not use
repeated global water scans as cleanup. Do not disable ship/character/item water
logic or mutate the shared ice1 prefab to achieve this.

Show exactly how registered floes cease to request native water height and how
normal objects retain their original path. If a minimal native dispatch bridge
is technically necessary, document its instruction/branch cost and ensure
unrelated objects do not call a Seasons helper. Do not claim zero extra native
instructions while retaining a bridge.

### A2. Dormancy is not complete across the auxiliary registries

The main snow driver and visual pool are now retired, but these lifetimes were
not closed by that change:

- `SeasonalIceFloes.ReconcilePolicy` clears work/settled/request/exclusion state
  on deactivation, but not the `controllers` map; that map is cleared by world
  Reset, not by the feature's inactive transition.
- `SeasonalSnowController.CompleteDormancy` resets controller visuals, not
  `SeasonalSnowMeshSettings.Instances`, `CopiedInstances`, `DisabledInstances`
  or `IgnoredInstances`.
- `SeasonalSnowMeshSettings` still admits instance records from Awake, applies
  configured transforms/copies and performs its rules without a winter-session
  lifetime. `SeasonalSnow.InitializePrefabs` can run the loaded-instance pass
  outside winter. These paths are unchanged, not newly introduced leaks, but
  they are part of the original requested audit and the new status omits them.

Separate immutable parsed rules/donor definitions from live seasonal instance
bindings. Release seasonal bindings and restore owned resources on stop; do not
recreate them on ordinary inactive object callbacks. Clear the placement cache
when its owning feature session ends. Include the auxiliary counts in the
on-demand status so it cannot report an apparently empty system while omitting
these collections.

Preserve native Deep North/mountain snow and explicitly configured native-cap
rules. Do NOT simply call the current all-purpose mesh-settings Reset and lose
configuration/donor data required for later activation. Classify any genuinely
year-round native rule separately and document its minimal residual state and
notifications. Never retain a whole seasonal scene registry just for such a rule.

### A3. The new wet-visual IL bridge lost Unity object-lifetime semantics

In `WinterSnow/SeasonalSnowVisualPatches.cs`,
`WearNTear_UpdateWear_ExcludeSnowFromWetVisuals.Transpiler` loads a
`MeshRenderer` into `cap`, uses raw `brfalse`, and then calls `Component.gameObject`.
The former C# expression used Unity's implicit bool (`piece.m_snow && ...`).
A destroyed Unity object may retain a non-null managed wrapper: raw `brfalse`
does not reject it, so the property access can throw MissingReferenceException.
The bridge also substitutes raw `ceq` for the original Unity object equality.

Preserve the original Unity validity/equality semantics or move the alias
classification into a valid, scoped binding with a defined invalidation path.
Do not introduce per-wear allocations, reflection, or repeated hierarchy scans.
Inspect stack effects, labels and exception boundaries of the resulting IL.
This is a source-level correctness finding, not an exception reproduced in game.

### A4. Explicitly review the remaining dispatch and diagnostics boundaries

These are audit requirements, not additional confirmed crashes:

- The floe driver still copies/checks every participant in FixedStep, including
  ownerless kinematics and FarVisual, then does another late pass. Do not describe
  it as a phase-specific active list yet. Stage B addresses this redundant work.
- Native force/client-sync phase ordering changed from the MonoUpdaters sequence
  to a separate DefaultExecutionOrder(1000) driver. Preserve moving-contact and
  ownership-acquisition ordering; document the actual ordering rather than
  claiming the old intra-phase order is identical.
- Removing the HUD postfix removes diagnostics on child targets with another
  Hoverable. Preserve an on-demand way to inspect the same floe without adding
  a new always-running global hover router.
- Cleanup now runs synchronously from RequestCleanup, including AddInstance.
  Audit destruction while native scene creation still holds ZDO references.
  Preserve immediate explicit cleanup; a safe one-shot end-of-operation boundary
  is not the old multi-frame deletion queue. Do not claim a reproduced failure
  here without additional evidence.

Return Stage A with a concrete old/new routing table, restored native state,
auxiliary registry counts, and a maintainer check sequence. Do not advance to
Stage B-D in that corrective pass.

## Stage B: lightweight data and scheduling (planned, gated)

1. Give kinematics a result containing only what it consumes in the ordinary
   movement path. Do not construct a dynamic SurfaceFrame with a second sampled
   time/normal, point velocity and force-only fields for every visual update.
   Diagnostic-only detail is sampled on demand.
2. Convert four probe values into world-space slope X/Z in the worker. Keep
   central full-spectrum height separate from filtered large-wave tilt. Store
   height, two slopes and their temporal derivatives. Linear plane reconstruction
   commutes with Hermite interpolation for fixed probe geometry; normalize and
   clamp tilt after interpolation. Preserve smooth revision-to-revision seams.
3. Reuse prepared hull geometry until its actual inputs change; no repeated
   structural collider/COM/mass work solely because a rendered frame occurred.
   This is not permission to ignore scale, collider replacement or real drift.
4. Use due-time scheduling for publication and forecast refill and a completion
   path for finished jobs. Avoid N per-frame timer checks and ticket polls.
   Maintain immediate native-owner priority and revalidate authority before writes.
5. Use actual phase participants: only dynamic bodies enter the force pass.
   Kinematic/FarVisual ownership and range transitions still receive the required
   frame/lifecycle observations; do not miss a transition merely because the body
   was removed from the force list. Keep one shared coordinator and one worker.
6. Compute expensive angular velocity from the most recent pose pair when needed
   for publication/handoff/diagnostics, not an average over an entire publication
   interval. Do not degrade transition velocities.

## Stage C: wave reuse and calm tilt (planned, gated)

### Exact reuse first

For full-spectrum central height, wave terms 1..9 have fixed directions; only
term 0 depends on the wind direction. During blending, compute the common nine
once and combine their amplitude with the weighted intensities. Compute the two
wind-directed terms separately. This reduces that two-wind height sample from
20 CreateWave calls to 11 (10 when only one wind contributes), not the entire
controller by 45%. Floating-point summation can differ slightly.

Keep using the trusted CreateWave/TrochSin math. Do not add third-party patch
inspection. Do not introduce another worker or per-floe tasks.

### Distinguish zero waves, weak tilt and full motion

At exactly zero contributing amplitudes the Ocean surface is constant. Skip
wave evaluation/refill until inputs change. Do not infer zero from a console
command alone or from one endpoint during a wind blend.

For weak but nonzero wind, the intended approximation is **kinematic tilt
suppression**, not suppression of central full-spectrum height. Keep vertical
tracking and 70% immersion; otherwise the previous hovering-above-water problem
returns. Do not use FarVisual bob inside rendered waves. Never disable nearby
dynamic righting, buoyancy or collision responses because the wind is weak.

Use a maintainer-tunable intensity threshold with hysteresis and a smooth
flatten/restore transition. Evaluate the threshold from prepared wind state,
not by calculating a full discarded normal each frame. A default of zero can
retain exact current behavior until the maintainer chooses a visual threshold.
Use the existing single synchronized JSON path for any persistent options.

A few adjacent normals with less than five degrees difference do not establish
that tilt is negligible: a slow or momentarily stationary slope can be large.
Calibration should record the maximum angle to world up (and the range of tilt)
over existing forecast windows and multiple positions/phases. Use the current
worker results while full tilt is running; do not add an always-on calibration
sampler that spends the work being saved. A sampled maximum is an empirical
quality estimate, not a global mathematical bound.

For fixed direction/probe geometry the slope scales linearly with intensity:
`theta(I) = atan(I * |slope_at_unit_intensity|)` before any tilt clamp. This can
help infer an empirical threshold, but a short temporal/spatial sample cannot
prove a worst-case bound for the whole Ocean. An illustrative 5-degree flattening
error at 8 m from the center changes edge height by about 0.70 m; five degrees
must be treated as an experimental tolerance, not a shipped guarantee.

When leaving calm mode, prioritize a short valid normal forecast and blend back.
Do not reset every ten-second curve on each small wind change. Keep hysteresis,
wind revisions, queue limits, cancellation and current heave coverage coherent.

## Stage D: floe-only GPU instancing (planned, gated)

### What the attached KG source actually demonstrates

Reference: user-supplied `KG-BatchRenderer-1.4.0-decompiled.cs`, a best-effort
decompiled source listing, not a tested drop-in implementation for Seasons.
Use the ideas, not copied implementation code or a new mandatory dependency.

- BatchKey is prefab batch ID plus 32 m X/Z cell; scale is NOT in the key.
- Registration stores `localToWorldMatrix * LocalOffset` for each instance.
- Draw calls use `Graphics.RenderMeshInstanced` and submesh 0.
- The instance matrix is captured at registration and is never refreshed by an
  Update/LateUpdate path. This implementation cannot follow moving floes as-is.
- Admission is for Piece/TreeBase and excludes Animator. IncludedPrefabs is not
  a general bypass of that initial filter. Do not assume native ice1 is admitted.
- It picks one renderer/material (often LOD0), disables all original renderers,
  and does not implement the original LOD transitions in the shown draw loop.
- BatchedInstance handles Start/OnDestroy, not OnDisable/reactivation.
- It uses a hard maximum of 1023 and computes an unused total count with LINQ
  in ProcessPending. Neither is a pattern to copy blindly.

Different per-axis scales are supported through per-instance transform data.
Do NOT quantize spawn scale to 0.1 for batching. It does not improve the key of
this method and would change geometry/health/collision distribution for no such
benefit. Do not use assumeuniformscaling for anisotropic XYZ-scaled floes.

### Seasons integration plan

Retain the actual GameObject, Rigidbody, collider, existing pose application and
network lifecycle. Render from their accepted final transform. No detached
visual root and no offscreen motion policy are included.

Create one optional floe renderer using only marked active seasonal instances.
Build shared definitions by actual mesh/submesh/material/render-state and LOD
compatibility; one prefab is promising but does not prove one mesh/pass/material
at runtime. The attached C# does not provide the actual ice1 shader or asset
layout. Establish these from the game's prefab/source assets or maintainer-side
inspection; report uncertainty rather than assuming it.

Refresh each rendered matrix after the existing movement phase. Use cached
renderer-local offsets when valid; do not scan hierarchies, reconstruct batches,
or allocate matrix arrays per frame. Move a slot between spatial buckets when
its X/Z bucket actually changes. Preserve removal indices and bound lifetime.

Keep spatial batches for sane bounds. Do not put the whole ocean in one bound.
Use shader/platform-appropriate batch capacity: Unity documents an upper bound
of 1023 and a common default of 511 when both transform matrices are required.
A conservative capacity is acceptable; exact capacity must match the shader.

Preserve materials, submeshes, LOD, shadow casting/receiving, lighting/probes,
layers, render queues, supported material-property overrides and camera behavior.
Do not hide all original renderers after collecting only one arbitrary renderer.
For unsupported assets/states leave the original path intact. Restore exact
original renderer state on opt-out, disable, destruction, season exit or failure;
no invisible floes, double draws or ghost batches after deletion. Native unmarked
ice, ships, structures and other mods' renderers are outside this renderer.

The batching switch is for A/B diagnosis and must use the existing configuration
mechanism. It starts disabled until runtime material/LOD/camera equivalence is
verified. Do not promise a performance gain from fewer draw submissions alone.
Do not alter prefab count or scale for the comparison.

## Maintainer verification order

After Stage A, verify inactive/spring and disabled-winter states, loaded/unloaded
transitions and native permanent snow. Inspect auxiliary registry counts and
native WaterVolume work, not only disappearance of Seasons patch rows. Compare
the same floe population without profiler first; then inspect short instrumented
captures. Builds/gameplay/network verification remain with the maintainer.

For Stage B, compare main-thread pose preparation and worker/refill counts with
identical movement/settings. For Stage C, compare full tilt and calm tilt with
unchanged heave at multiple wind strengths and during transitions. For Stage D,
compare native and instanced rendering at identical population, camera, weather,
scale and quality; inspect draw calls, render CPU/GPU time, LOD/shadows and motion.

Do not mix all stages into one performance claim. No expected FPS multiplier is
established by this source review.

## Primary references

- Reviewed Seasons source: https://github.com/shudnal/Seasons/tree/0ebb831f9b8b97d5748c80b3504941a2ae493d84
- Native WaterVolume and MonoUpdaters: https://github.com/shudnal/assemblies_combined/tree/5a2365409cff644d6adaccd2b308178cc4179b19/assembly_valheim
- GPU instance data, bounds and limits: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.RenderMeshInstanced.html
- Unity destroyed-object semantics: https://docs.unity3d.com/cn/6000.0/ScriptReference/MonoBehaviour.html
- Trigger lifetime caveat: https://docs.unity3d.com/ja/6000.0/ScriptReference/Collider.OnTriggerExit.html
