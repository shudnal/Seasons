# Snow trampling

Footsteps on supported building pieces reduce their seasonal snow cap. This uses
`FootStep.GetGroundMaterial`, inside the existing non-null `WearNTear` branch,
just before its material switch. The transpiler reuses the `WearNTear` already on
the evaluation stack. It does not repeat the ground query, raycast, or change the
selected footstep material. If the material read is missing or ambiguous after
other patches, it logs a warning and leaves the method unchanged.

## Configuration

Both settings are server-synchronized and declared in the main `ConfigInit()`,
in `[Season - Winter snow]`. Changes apply to subsequent footsteps without a
scene reload.

| Setting | Default | Behavior |
| --- | --- | --- |
| `Snow reduction per footstep` | `0.01` | Absolute buildup removed from the piece per accepted step. Valid range: 0 to 1. Zero disables trampling. |
| `Footsteps can clear snow completely` | `false` | Allow a zero lower limit. Otherwise use the smaller value of the piece's current normal or reduced buildup range. |

With the default ranges, footsteps stop at `0.51` on normal pieces and `0.30`
on reduced-buildup pieces. For example, a reduced cap at `0.325` becomes `0.315`,
then `0.305`, then `0.30`. Further steps leave it unchanged. If heat, construction,
or hoe clearing has already left a cap below its floor, trampling does not add
snow or reduce it further. The floor follows the configured buildup range; a
range with a zero minimum also has a zero trampling floor.

When complete clearing is enabled, the same cap can reach zero. A saved zero
remains a real snapshot, not a request to seed new snow. New snowfall can still
accumulate after the step. These settings do not change melting, hoe clearing,
native Deep North snow, terrain snow, snowless pieces, or the ocean collider.

## Multiplayer and lifetime

Only the character owner reports a step. This applies to both players and
creatures with native footstep events. Repeated queries for the same character
within one rendered frame count once. The mechanism does not generate footsteps
for distant or off-screen creatures for which the game skips them.

If the character and piece have the same owner, the change is local. Otherwise a
single scene-level routed RPC sends the actor ID, piece ID, and a sequence, not a
snow amount. The receiver checks actor ownership, target ownership, sequence,
and proximity to the piece's cached collider bounds. A small bounded burst budget
limits repeated remote requests. An ownership change in flight rejects the step;
there are no retries, ownership claims, new per-piece RPC registrations, or new
per-frame scans. A piece without an owner uses the existing ownerless-write policy.

The piece must have a confirmed current-winter state and a ready active region.
Stale snapshots or owner changes queue the existing refresh instead of applying
a change to stale data. Before reducing snow, the controller integrates the old
weather and heat interval. It then writes the exact reduced value and timestamp
through `SeasonalSnowStorage`, reclassifies the piece, and updates its visual via
the existing queue. The hover diagnostics record `Trampling` as the change cause
on the client that applies the reduction. Remote clients receive a saved snapshot.

Actor bookkeeping uses weak keys and is reset with the scene. The new RPC handler
is removed only if it is still the handler this subsystem registered.

## In-game verification

Start from the feature branch containing this change. Enable the existing snow
hover diagnostics. Use a normal and a reduced-buildup piece with confirmed snow.

1. Walk across each piece. Verify a `0.01` reduction per accepted step, without
   changing the normal footstep material. Stand still to check that there is no
   background reduction from trampling.
2. Reach the appropriate minimum with complete clearing disabled. Check a cap
   already below the minimum: stepping must not raise it to that minimum.
3. Enable complete clearing and reach zero. Unload the area or rejoin during dry
   weather and verify that the zero is preserved. Snowfall after clearing may add snow.
4. Change the reduction while in the world, including zero. Change the minimum
   ranges and verify that the next steps use the correct normal or reduced floor.
5. Use two clients with different character/piece owners, then change the piece
   owner and repeat. Observers must not multiply the reduction. Verify creature
   footsteps as well. For very small reductions, visual quantization may require
   several steps before the cap visibly changes.
6. Confirm that hoe clearing and heat can still remove snow below the trampling
   floor. Check native Deep North snow, a snowless piece, and a summer world.

No build or game tests were run while preparing this change.
