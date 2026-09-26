# Task: finish lifecycle corrections and batch nondynamic ice floes

Date: 2026-09-27.
Repository: `shudnal/Seasons`, branch `perf/snow-performance`, PR #45.
Documentation baseline: `118239a8293aefda3e49c1fcc126df7d25aa02b4`.
Reviewed implementation: `256fe447afebdc1882ae5e20dc23ea3ea88bcd45`, with
implementation documentation at `0ebb831f9b8b97d5748c80b3504941a2ae493d84`.

## 1. Current authorization and execution

**Implement the corrections and the renderer in one work session. Do not stop
after the former Stage A.** This version replaces the old staged execution gate,
the proposed calm-wind threshold/calibration, and the restriction against reusing
KG's implementation. Older planning text is historical where it conflicts with
this task. Deliver two coherent implementation commits: lifecycle/water/geometry
corrections first, instanced rendering second. Both are authorized now.

Fetch the current work branch, preserve newer changes, and record the actual
base. Read applicable AGENTS.md files, `docs/codex-seasonal-runtime-lifecycle.md`,
`docs/seasonal-runtime-lifecycle-implementation.md`, and the current floe JSON,
background, motion, authority and cleanup documentation. Do not work on master,
merge PR #45, or modify ValheimProfiler, versions, dependencies or packaging.

Do not compile the mod, run mod tests, launch Valheim, or create substitute
physics/threading test harnesses. Static source/API/IL inspection, diff checks,
project-entry checks and syntax inspection are allowed. Compilation, gameplay,
multiplayer and performance measurements belong to the maintainer. All project
content must be English except intentional localization. Return an archive with
changed/new files and a deletion manifest if repository publication fails.

Use `shudnal/assemblies_combined` first for game source. The reviewed native
reference is `5a2365409cff644d6adaccd2b308178cc4179b19`, `assembly_valheim`.
Inspect the actual current source before choosing a registration mechanism.

## 2. Confirmed inputs and scope

### Maintainer-confirmed prefab

```text
ice1                         existing root, body, view and IceFloe remain
  default                    no children
    MeshCollider
    MeshRenderer
    MeshFilter
```

The supplied transform dump has `default.localPosition = (0,0,0)`, identity local
rotation and `default.localScale = (4,4,4)`. Root scale varies between instances;
root position/rotation change with the existing motion. The maintainer confirms
that these are the only three components on `default`. There is no reason to
invent an arbitrary hierarchy/animation/LOD conversion framework for this shape.
Read actual shared mesh, submesh/material slots and renderer settings once at
preparation; the transform dump does not specify their values.

The root's shown Y=50 and inactive hierarchy belong to a prefab dump, not a
persistent runtime waterline or an instruction to disable the live root. Keep
the actual root localScale, including different scales on the three axes. Do not
quantize scale to 0.1, search for matching transforms, or bake a mesh per scale.

### Maintainer-confirmed water behavior

Disabling either MeshCollider or the entire `default` object was tested in game:
**the existing Floating entry remains in `WaterVolume.m_inWater`.** Treat this as
an established failure of implicit cleanup, not an unanswered test suggestion.
Turning off `detectCollisions`, a Collider, a Renderer or a GameObject is not a
water-unregistration protocol.

The native reference's `UpdateFloaters` removes invalid interactables/transforms;
it does not check collider enablement or remove a valid entry just because it is
above the surface. It still samples `GetWaterSurface` for a retained transform.
This source observation is separate from the maintainer's general description
of water exit; do not depend on a height-based removal that is absent there.

### Preserve accepted behavior

Keep dynamic contacts, climbing, native owner priority, soft fallback leases,
remote pose smoothing, the current ZDO publication interval, fixed Ocean depth
1 / surface offset 0, 70% nominal immersion, current wind adoption, one rolling
background worker with a gradual ten-second reserve, and immediate one-pass
seasonal deletion of loaded objects and unloaded marked ZDOs. Network behavior
still needs the maintainer's multiplayer verification.

Do not change wave formulas, curve representation, sampling spacing, force
coefficients, density, scale, day defaults, health, global fixed timestep or
publication cadence in this task. The earlier no-spawn report was the configured
day range 4-10 versus Winter day 2; do not invent another spawn fix for it.

No weak-wind threshold, exact-zero special mode, angle sampling, automatic
calibration or tilt suppression is requested. The maintainer tested the visual
case and rejected this optimization. Also defer mathematical spectrum reuse,
new forecast scheduling, lightweight-plane curve redesign, stationary physical
roots, detached moving visuals, and visibility-based simulation throttling.
Necessary cached hull preparation for an inactive child is included below.

## 3. Close the lifecycle review findings

### 3.1 Explicitly own water sampling participation

Removing `FloeWaterObservation` removed the generic Seasons helper but restored
native per-frame wave sampling for every floe still in a water volume. Removing
an object from `Floating.Instances` or `ZSyncTransform.Instances` does not remove
it from `WaterVolume.m_inWater`. Native `MonoUpdaters.Update` services the latter
independently. Do not merely make the Seasons profiler row disappear.

Implement a scoped admission/release mechanism for marked seasonal floes whose
water is already supplied by the accepted custom sampler. It must:

- explicitly retire existing native water-sampling membership when taking over;
- prevent subsequent water-entry events from reintroducing redundant sampling
  while the instance is managed, including nearby dynamic managed floes;
- keep the associated `Increment`/`Decrement` accounting consistent across all
  overlapping water volumes, later exits, volume destruction and reactivation;
- supply any still-needed Floating level/effect state from our existing surface;
- restore ordinary participation correctly if a surviving instance is genuinely
  released to native behavior, including release while still inside water.

Do not synthesize arbitrary `OnTriggerExit` calls, clear whole volume lists, reset
another object's liquid state, or repeatedly scan all water volumes per frame or
per force step. A finite admission/transition reconciliation is acceptable when
needed for already-existing membership; capture subsequent membership through
scoped lifecycle/trigger handling. Avoid multiplying a full world scan by every
instance in a simultaneous batch transition. Late trigger events must not cause
double decrements, negative liquid counts, or re-add a removed participant.

Choose the smallest source-correct implementation. An instance-specific water
adapter or sparse trigger-boundary bridge is acceptable; a new generic
`UpdateFloaters` helper invoked for every unrelated interactable is not. Never
mutate water-list iteration unsafely. If a native bridge remains, document its
real residual branch cost and activation; do not claim literally zero added
instructions. Disabling `default` is renderer/collider control, not this fix.
The correction must also work with batching switched off.

### 3.2 Finish auxiliary-state retirement

At the reviewed baseline, `SeasonalIceFloes.ReconcilePolicy` clears placement
work but retains `controllers` until a full world reset. Clear that feature-owned
cache at deactivation. Snow's `CompleteDormancy` resets the controller visual
pool, but not the instance bindings in `SeasonalSnowMeshSettings.Instances`,
`CopiedInstances`, `DisabledInstances` and `IgnoredInstances`.

Separate reusable definitions/configuration from seasonal live bindings. Restore
owned transforms/materials, remove owned copies, release instance references and
prevent their readmission from inactive callbacks. Do not erase donor/config data
needed at the next activation. Preserve permanent native Deep North/mountain
snow and explicitly configured year-round native-cap rules; document any small
separate state that genuinely belongs to such rules. Do not retain the full
seasonal registry to implement a narrow permanent rule. Extend status reporting
to include these auxiliary counts rather than reporting only the main pool.

Outside winter, or for a disabled feature in winter, no recurring seasonal
calculation, empty-list polling or per-object helper dispatch may remain after
finite teardown. Drivers must actually stop. Keep legitimate one-time
season/config/world/admission events. No Patch/Unpatch cycles, profiler changes,
or moving the same unconditional hot callback into a differently named helper.

### 3.3 Correct Unity object semantics and audit transitions

The wet/snow alias transpiler in `SeasonalSnowVisualPatches` replaced Unity object
validity with raw `brfalse` before reading `MeshRenderer.gameObject`, and equality
with `ceq`. Restore Unity validity/equality semantics: a destroyed native object
can retain a non-null managed wrapper. Inspect IL stack effects, labels and
exception boundaries without adding reflection or hierarchy scans to wear loops.

Also audit the changed native sync/force phase order, reentrant release, native
list restoration, component disable/re-enable and synchronous cleanup invoked
from `ZNetScene.AddInstance` during creation. These are review requirements, not
assertions that each path has a reproduced crash. A safe one-shot completion
boundary is acceptable; do not reintroduce gradual multi-frame floe deletion.
Do not expand this into a new phase scheduler or unrelated snow-model rewrite.

## 4. Rendering architecture

### Eligibility

The established local motion mode decides the visual path, not just `IsOwner()`
and not an arbitrary external `Body.isKinematic` override:

| Instance state | Rendering |
| --- | --- |
| Native dynamic local owner | Existing active `default` and native MeshRenderer |
| Native dynamic replica of another owner | Existing active `default` and native MeshRenderer |
| Managed ownerless kinematic simulator | Instanced visual; `default` inactive |
| Managed ownerless kinematic replica or waiting pose | Instanced visual of the current accepted pose; `default` inactive |
| Managed FarVisual | Instanced local bob pose; `default` inactive; no decorative ZDO writes |

Do not treat a different native owner as ownerlessness. Replica and simulator
both draw their own current accepted/smoothed pose. The render path does not
claim leases or change authority. Only marked seasonal floes are admitted; do
not change the shared ice1 prefab or unmarked native ice. Headless servers create
no graphics driver, material or batch registry.

### Geometry before hiding the child

Prepare hull geometry before `default.SetActive(false)`. The current
`UpdateOwnerlessMotion` rejects an inactive collider via `activeInHierarchy`,
and `ReadHullGeometry` currently consumes the collider. A renderer-only patch
would therefore stop motion/withdraw the lease.

Cache the mesh-local bounds and the shape-to-root relation, center offset,
intrinsic thickness and necessary COM relation. Keep cached shape data separate
from collider participation. Kinematic motion, FarVisual, recovery and background
inputs must work while the child/collider is inactive. Compute world centers from
cached geometry and the current root/body pose. Keep scale/mesh/child-transform
invalidation correct; a new rotation is not a new geometry revision. Do not use
empty inactive `Collider.bounds` as the shape. Active collider checks remain
required for dynamic simulation.

Preserve the dynamic mass, center of mass and inertia behavior when restoring the
collider. Do not derive a new baseline or handoff velocity from a body whose
shape was just deactivated. Keep existing localScale and transition velocities;
no new physical model or artificial impulse is part of the renderer change.

### Shared definition and matrices

Reuse one visual definition for the common mesh/material state. Store the local
visual offset once, including the child's scale of four:

```csharp
visualLocalMatrix = root.worldToLocalMatrix * visual.localToWorldMatrix;
renderMatrix = root.localToWorldMatrix * visualLocalMatrix;
```

The first expression is preparation; the second runs after the actual pose has
been updated. Do not double-apply child or root scale, import the prefab's Y=50,
read a stale predicted pose, or reuse the registration-time matrix forever.
Shared mesh and material identity, not matching position/rotation/scale, enable
instancing. Update each assigned matrix slot on the main thread after the
existing motion path. Do not move matrix production to the forecast worker.

No per-frame hierarchy search, renderer/material discovery, new material per
floe, LINQ totals or matrix-array allocation. Preserve render settings including
layer, shadows, lighting/probes and relevant material state. Use actual submesh
and material slots; do not silently draw submesh zero if that loses geometry.
Keep the implementation specific to this known prefab, not an asset-conversion
framework. Do not introduce per-instance material property blocks without an
actual requirement.

### Stable world cells, native bounds/culling

Use fixed 32x32 metre world-XZ cells. For a single definition the cell pair is
sufficient; retaining KG's `(BatchId, CellX, CellZ)` key is also fine:

```csharp
int cellX = Mathf.FloorToInt(anchor.x * (1f / 32f));
int cellZ = Mathf.FloorToInt(anchor.z * (1f / 32f));
```

Assign by the stable geometric XZ anchor when entering a batch. Current kinematics
holds the hull center, not necessarily an off-center root pivot, stationary.
Do not reshuffle cells for ordinary pivot movement during tilt. Recalculate on
return from dynamics or a real relocated anchor, including a teleport. Negative
coordinates must use floor, not truncation. No camera-relative sectors, dot
products, per-frame camera sorting, CullingGroup, or offscreen motion suspension.

Keep persistent lists of matrices and instances per cell and an instance slot
index. Update in place; remove by swapping the last entry into the free slot and
repair its handle. Empty cells are removed. Never render stale list capacity or a
matrix for an already removed object.

Submit each nonempty cell with `Graphics.RenderMeshInstanced`, splitting only as
required by the actual API/shader capacity. Let Unity compute aggregate bounds
from mesh and matrices. Do not override bounds with a 32x32 square: large tilted
floes can extend beyond it. Do not combine the entire Ocean into one draw, and do
not claim that extra group submissions or culling are free. Nonuniform XYZ scale
is valid input; do not enable `assumeuniformscaling`. The general API ceiling is
1023, but Unity documents a default limit of 511 with both transform matrices;
use a valid conservative chunk capacity rather than blindly copying 1023.

### Render order, transitions and failures

Update batch matrices after local pose/replica/FarVisual updates, then draw once
per rendered frame in the appropriate late phase. Do not duplicate draw calls
through both a frame loop and camera callbacks. Retain required camera behavior
(including reflections) without creating a new camera-selection policy. Draw
also on paused frames: hold matrices still, not invisible. Do not adopt KG's
teleport draw suppression unless the existing game scene actually requires it.

| Transition | Required ordering and result |
| --- | --- |
| Dynamic to managed kinematic/batch | Cache shape/state, establish the non-colliding mode and ready batch slot, detach redundant water sampling, then hide `default`; never hide before a usable draw path exists |
| Kinematic or FarVisual pose update | Existing movement, current matrix write, grouped draw; no extra water query or force |
| Kinematic to FarVisual or back | Keep the batch membership where the anchor is unchanged; retain existing authority/bob rules |
| Batch to native dynamic | Remove the slot, restore `default` and physics at the accepted pose/velocities before simulation; no double draw, missing contact frame or stale collider |
| Batching switch off | Restore normal visual state and remove all slots without changing kinematic/dynamic/FarVisual mode; custom water isolation remains in effect |
| Disable, destroy, season exit or world change | Retire slots before another draw; cancel old work by existing lifetimes, restore only surviving owned state, release empty groups/resources, stop the render driver |

Store and restore each live instance's original child/renderer/collider state.
Do not blindly enable objects another system had already hidden. References to
native mesh/material assets are not resources to destroy. Use a shared material
or one managed shared instancing copy per definition, never `.material` per
instance. Preserve source-material changes through the existing lifecycle, not
by cloning or reparsing every frame. Keep source attribution for reused KG code.

Validate graphics support and the template at initialization. A failed admission
or rendering setup leaves/restores the ordinary visual, without repeated warnings
or repeated failing draw exceptions every frame. Mutation/removal during a draw
pass must be safe; destruction must not become a multi-frame unregister backlog.
Do not depend on visibility callbacks from the MeshRenderer that has been hidden.

## 5. Configuration and reuse

Keep the three main controls in `shudnal.Seasons.cfg`, `Season - Winter ocean`:

```ini
Enable ice floes in winter = true
Fill the water with ice floes at given days from to = {"x":4.0,"y":10.0}
Health of ice floes = 20
```

Add only one batching toggle to `Seasonal ice floes.json`, for example
`rendering.enableInstancedRendering`, initially false for controlled comparison.
Cell size remains a code constant. Use the same `seasonalIceFloesJSON.ValueChanged`
path as all advanced floe settings, including defaults and live reload. No
filename-specific watcher branches, local shadow settings, separate cfg,
migration, custom reload pipeline or independent synchronization channel.
A render toggle changes visuals only and must not respawn/reseed floes.

The maintainer explicitly permits direct reuse of KG-BatchRenderer code rather
than an unnecessary clean-room rewrite. Read the relevant source excerpts in
[reference/kg-batchrenderer-core.md](reference/kg-batchrenderer-core.md). They
are included so this task is executable without access to the chat attachment.
Reuse BatchKey, Bucket, stable slot removal, matrix offset and draw-loop structure
where useful. Adapt moving matrices, immediate removal, reversible child state,
capacity and feature lifetime; the reference's registration-time-only matrices
and Start/OnDestroy-only lifecycle are insufficient for this task.

Do not import the unrelated plugin, UI, autoload system, Piece/TreeBase scan,
health-visual patches or a new runtime dependency. Do not copy the unused LINQ
count or a permanent empty queue service. The excerpts are decompiled reference
text, not a compilable extra source file; adapt language syntax to this project.

## 6. Deliverables and acceptance

Return both implementation commits plus a concise routing/ownership audit:
which water paths were removed, how membership/counters are balanced, how native
state is restored, how render transitions are ordered, and what dormant native
branches remain. Update the implementation documentation without claiming a
measured speedup or network acceptance. Keep an on-demand inspection route for
floes; do not add another always-running HUD patch.

Extend existing on-demand diagnostics with batching enabled/driver state,
native versus batched participants, occupied cells, current matrices and draw
submissions, plus feature-owned water membership and auxiliary snow/cache counts.
Read maintained counters; do not scan the world to produce status and do not
emit per-frame logs. No participants means no rendering loop or empty batch
service. A feature disabled/out of season must remain dormant after finite
teardown; late callbacks or worker results must not recreate it.

Static review must check registration symmetry, list mutation and swap indices,
shared asset ownership, inactive-child geometry, restored COM/inertia/velocities,
render phase order, paused drawing, negative XZ cells, large-mesh bounds, stale
native water entries and late trigger accounting, world/session cancellation,
project Compile entries, generic JSON routing and unintended Cyrillic text.
Do not run the following maintainer-side checks yourself:

| Scenario | Acceptance |
| --- | --- |
| Same loaded Ocean population, batching off/on | Unchanged positions, scales, waterline and movement; separate uninstrumented FPS and render CPU/GPU/draw measurements |
| Kinematic simulator, remote replica and FarVisual | All render at their own accepted current pose; no stale registration matrix or extra wave sampling |
| Approach/depart on foot/ship; two overlapping floes | Native dynamic contacts return with `default`, no visible gap/double draw or new impulse; far overlap remains harmless |
| Disable Collider/default while registered with water | Our lifecycle explicitly releases sampling; no reliance on Unity removing it automatically; counters remain valid |
| Batching off but seasonal floe simulation on | No duplicate native water sampling is reintroduced just to show the MeshRenderer |
| Pause, freefly rotation, cell edges and large anisotropic floes | No disappearing paused meshes, clipped cell edges or camera-dependent regrouping |
| Floe destruction, toggle, season exit and world change | Immediate visible removal, no ghost matrix/lease/job or retained feature-only registry |
| Spring or disabled winter features | Drivers stopped, auxiliary bindings cleared; no hidden unconditional helper loop |
| Native permanent snow and unmarked ice | Existing behavior untouched; wet/cap aliases remain safe with destroyed Unity wrappers |
| Second client and dedicated/headless server | Ownership transitions and visual restoration correct; headless peer creates no renderer resources |

## References and evidence boundary

Maintainer runtime observations and component confirmation are inputs above;
static code findings are not claims of reproduced gameplay failures. This task
has not been implemented by the documentation change.

- Seasons review baseline: https://github.com/shudnal/Seasons/tree/0ebb831f9b8b97d5748c80b3504941a2ae493d84
- Native water dispatch: https://github.com/shudnal/assemblies_combined/blob/5a2365409cff644d6adaccd2b308178cc4179b19/assembly_valheim/WaterVolume.cs
- Instance data, bounds and limits: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.RenderMeshInstanced.html
- Trigger exit on deactivation: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Collider.OnTriggerExit.html
- Render state: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderParams.html
