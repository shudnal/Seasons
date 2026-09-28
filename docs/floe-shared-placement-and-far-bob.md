# Shared placement exclusions and minimal distant bob

Base: `fde3815532714cffe71d83e5afc71e70a20a8c4d`, `perf/snow-performance`, PR #45.
Date: 2026-09-27.

## Scope and accepted baseline

The maintainer reports correct visuals and imperceptible near/ownerless/distant
transitions, with the latest unchanged-scene measurement improving from
105-109 to 111-114 FPS. Those are maintainer observations, not measurements from
this change. Network behavior still needs multiplayer verification.

This pass changes shared placement bookkeeping, simplifies the existing local
FarVisual oscillator, and closes the previously reviewed disabled-water-volume
admission gap. It does not change dynamic forces, wave formulas, background
forecast curves, leases, ZDO publication cadence, instancing or its 96 m cells.
No wind threshold, automatic angle calibration, additional config file or
special JSON loading path is introduced.

## Placement: share the existing exclusion collection first

`ZoneWork.Exclusions` is replaced by one static `placementExclusions` list. All
placement jobs check that list, and each successful placement immediately adds
its existing center/radius record to it. Static location clear areas also use
the shared list. Per-zone `Created` IDs and completion markers remain separate.

The list survives job completion and zone unload. It is cleared on world reset
and when the floe eligibility policy actually changes. A repeated notification
with unchanged eligibility does not erase it. No frame callback iterates it when
placement is idle. The on-demand placement status includes `exclusions`.

This restores sharing between neighboring local placement jobs without adding a
spatial-index framework. It is a session placement reservation list, not a new
world database or a live collision system. It does not reseed all pre-existing
floes from ZDO, synchronize concurrent placements by different clients, move
existing overlaps apart, or continuously update reservations after physical
movement/destruction. Those are not silently claimed as solved. The existing
zone markers still control whether a zone is eligible for another placement
pass. Inspect fresh generation first before extending the model.

### Preserve empirically calibrated dimensions

The maintainer deliberately chose the scale-1 constants and margins in game.
This pass preserves the exact candidate radius calculation, root-scale/depth
multipliers, and the post-spawn `GetFloeSize(instance) + 0.5f` reservation. The
candidate margin remains 0.2 m. No factor is doubled and the default child's
local scale of 4 is not multiplied into those calibrated numbers again.

The existing post-spawn measurement is intentionally unchanged for this
comparison, including its collider/renderer fallback implementation. Removing
that measurement or unifying radius formulas belongs to a later, separately
measured change if the shared list is insufficient. Changing both the exclusion
scope and empirical dimensions now would obscure the result.

The earlier audit's proposed mesh-derived footprint and broad ZDO occupancy
index are therefore deferred, not part of this implementation.

## FarVisual: one shared scalar oscillator

The old bob did not call CalcWave for its displacement, but it asked for a
SurfaceContext that prepared the full common wind snapshot and required the
water-prefab path. It also read camera/clipping state, rebuilt world-space hull
extents, limited the same settings and evaluated an exponential and sine for
each floe on each displayed frame.

The new path has three boundaries:

1. On entry, preserve the existing authoritative pose/velocity restoration
   state, choose the flat heading, and cache the individual phase sine/cosine
   and fixed world-edge offset.
2. Once per displayed frame, and only when a far floe needs an update, read the
   existing server clock and sea level, validate the shared bob settings, and
   compute one smoothing factor plus sine/cosine of the common phase.
3. Per far floe, combine those values with its cached individual phase, update
   the target height and apply the existing smooth vertical movement. Use
   `sin(t + phase) = sin(t)cos(phase) + cos(t)sin(phase)`; do not sample a wave.

The first return from a tilted wave pose still flattens smoothly. Once that
transition reaches the flat heading, rotation is set exactly and is not written
again during normal bob. The center-to-pivot correction and intrinsic thickness
are cached; explicit geometry/scale revisions update the correction. A normal
bob step does not compute hull AABB extents or inspect collider activity.

Amplitude 0.08 m, period 6 s, response 0.2 s and the configured submergence/height
offset remain the same. All floes retain their individual phases. Algebraic
reordering can produce insignificant floating-point differences; the one-time
flattening transition now has a small completion tolerance instead of running
Slerp forever.

There is no far-bob dependency on WaterVolume, wind, surface normals, forecasts,
contacts, Rigidbody forces or per-floe server-time reads. The redundant bob-only
far-clip check is removed; loaded FarVisual objects can keep their cheap visual
pose even outside that clip distance. Existing mode selection and renderer
culling remain responsible for their respective boundaries.

This does not remove the outer lifetime/native-owner/distance checks needed to
return a floe to kinematics or physics. It does not alter the driver's phase
routing or claim that every operation associated with a distant instance is now
zero-cost. It removes the unnecessary surface/geometry work inside the bob.

No decorative pose is written to ZDO. Pause still holds the pose while the
existing rendering driver continues drawing it. The shared bob cache resets
with the floe world/session runtime. Hover reports `Far bob=shared oscillator`
and whether the flattening transition has finished.

## Water-volume re-enable correction

An inactive WaterVolume is absent from the game's Instances list but can retain
its m_inWater entries. Admission could therefore miss a floe in that volume.
The existing OnEnable postfix now retires already-managed members before its
pending-restoration lookup. This reuses RetireMembers/RetireMember, including
the existing liquid counter balancing. It does not wait for OnTriggerExit and
does not introduce an UpdateFloaters hook or a recurring volume scan.

## Cover and inactive-season follow-up: analysis, not code changes here

`WearNTear_SetHealthVisual_UpdateCoverStatus` serves seasonal textures in all
seasons, not only winter snow. Its custom HaveRoof handles vines, protected
positions and a native roof result followed by a non-leaky/self-filtered sphere
cast. These semantics are not equivalent to reading m_haveRoof alone. The
expensive check is already limited to once per five seconds per bound variant;
material updates happen only when the effective covered state changes.

The waste is routing every SetHealthVisual call through a controller lookup and
timer check. A follow-up can service due registered variants directly, keeping
that five-second cadence and the existing cover semantics. Merely triggering on
changes of the native roof bool is insufficient: protected-position or custom
leaky/self filtering can change while the native bool remains true.

The concrete inactive-season routing option is a small native IL gate for pure
policy overrides such as frozen-wave suppression and active snow geometry
notifications. The false path executes native code without a managed Seasons
helper/callback, but still has the added branch instructions. Do not claim zero
instructions or use runtime Patch/Unpatch. Frozen-fish periodic checks can be
owned by an enabled frozen-water session instead of a callback on every fish's
fixed update. Sparse visibility/lifetime notifications must retain ownership
and restoration semantics. These changes need their own focused audit; none is
implemented in this pass. Native UpdateSnowVisual/UpdateCover calls must remain
for native game behavior, including permanent snow outside seasonal regions.

## Verification boundary and maintainer checks

The three edited source baselines were verified against their Git blob hashes.
Static checks cover the diff, lexical C# structure, placement formula
preservation, unchanged near-force/wave methods, shared-state reset, and absence
of accidental Cyrillic in changed repository files. No mod compilation, tests,
Unity/physics harness, game run or measured performance claim is made here.

For placement, intentionally regenerate eligible floes by disabling/re-enabling
them in the current configured winter interval. Observe adjacent newly filled
zones and the shared exclusions count. Existing overlaps are not a valid test
of a placement-only fix. Do not alter amount, dimensions or gaps simultaneously.

For bob, keep the existing population and camera/settings; move out beyond
visible waves, wait for flattening, then approach again. Check waterline,
vertical motion, pause/resume, batching off/on and immediate removal. A distant
hover should report the shared oscillator without acquiring a new wave sample.
Optional water check: disable/re-enable a WaterVolume around an already managed
floe; its native m_inWater list must not resume duplicate sampling of that floe.
