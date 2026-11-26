# Hoe snow clearing

Seasons 1.11.0. Game source: `shudnal/assemblies_combined` at
`216cb30f748e0ef9c74a8366395e9cc110f3cd8d` (Valheim 1.0.30).

## Player behavior

Select **Clear snow** in the hoe's normal build grid, aim at an existing building
piece, and use the normal placement button. One successful action clears that
piece's seasonal snow to zero, including a fully healthy piece. It does not repair
health, remove a structure, place an object, modify terrain, or require a crafting
station. Normal hoe actions and hammer repairs retain their native paths.

The tile is available only while the world is initialized and
`SeasonalSnow.WinterReady` is true: winter with seasonal snow and seasonal
environment control enabled. It is absent in other seasons or when those controls
are disabled, including in no-cost mode.

A missing target, unsupported piece, or piece without seasonal snow uses the
existing `$msg_nosnow` message. Wards retain `$msg_privatezone`; insufficient
stamina uses the native stamina-bar flash. Only a successful action consumes the
hoe's normal build stamina/eitr/durability, plays its tool animation, and creates
the native snow-shovel effect at the already captured piece position.
This does not clear native Deep North snow, terrain snow, or creature materials.
Cleared pieces can accumulate fresh snow again according to the existing rules.

## Selection and UI

`Seasons_ClearSnow` is a private copy of the hammer's repair action. Its prefab and
icon are reused; the entry is added to or removed from the hoe's `PieceTable` when
availability changes. Its `m_repairPiece` flag remains true for native targeting,
repair-mode recognition, and suppression of the placement ghost. It does not enable
`m_canRemovePieces`. The action is made known before the active table is rebuilt;
no new key binding, raycast, or per-frame target search is added.

Availability is refreshed by the existing snow lifecycle reconciliation after
world initialization, season changes, and settings changes. Only an actual change
rebuilds the equipped hoe's available list and an open BuildUi. Cloned hoe tables
are reconciled before their normal player-side list rebuild. Removing the selected
action resets selection to the first remaining action, restores its placement
ghost, and discards any buffered snow-clearing click. Unavailable entries are
removed from the source list rather than merely relying on `m_enabled`, which
native no-cost mode can bypass. The input dispatch also rejects unavailable use.

Two BuildUi transpilers treat only this action as an ordinary tile when creating
buttons and filtering search results. All other repair/remove items remain special.
The native placement input calls `Player.Repair` for the selected repair-style
action. A prefix intercepts only the snow-clearing piece, verifies that it is
still selected and available, and dispatches snow clearing instead of repairing
health. Ordinary repairs retain their native path. `Player.UpdatePlacement`
is not transpiled: its previous input transpiler caused terrain placement ghosts
to deactivate and reactivate every frame while input was enabled. UI hook
mismatches disable the snow-clearing action.

The supplied `Assets/clear_snow.png` is embedded unchanged. Dedicated servers do not
instantiate the UI action or load its icon; they still register the RPC handlers.

## Snow state and multiplayer

An owner or an ownerless piece is changed locally through the seasonal controller.
Otherwise a request is sent only to the current piece owner. No ownership claim,
client-selected snow amount, or speculative write to another owner's ZDO is used.
Both peers must have this feature installed for remote clearing to succeed.

The receiver resolves the sender's live player, verifies the replicated hoe in the
right hand, validates range and ward access for that player, and accepts only a
ready, confirmed, current-winter state it can write. Range validation uses the
piece's cached collider bounds so a large roof can be reached at its edge. Remote
range allows one meter for interpolation. Remote ward checks use vanilla default
overlapping-ward semantics, not the receiving client's local identity.

The old weather interval is consumed and an explicit zero is published with the
current epoch/time. The runtime snapshot, bucket, and visual target are updated
together. Save data is not deleted, the piece is not retired, and neighboring
geometry is not invalidated. Existing snow replication handles remote visuals.
`ManualClear` identifies the change in the optional hover diagnostics.

The requester allows one outstanding request, validates its reply by peer, sequence,
router, and world, and pays from the original tool reference only on success. The
receiver caches the last result per peer and rate-limits new requests. A handoff in
flight is rejected rather than recursively forwarded. A missing response expires
after three seconds. Late or duplicate replies cannot complete a newer action.
A timeout can still occur after a remote owner has processed a request; no local
prediction or automatic retry is used to hide that ambiguity.

RPC registration runs in the `ZNetScene.Awake` postfix and uses its `__instance`.
Native Awake already uses the routed RPC for its own SpawnObject registration;
there is no Game.Start hook or redundant scene-singleton gate. Scene teardown
removes only the handlers owned by this feature. Per-peer receipts and outstanding
requests are discarded on world teardown. There is no periodic network check or
background polling loop.

## Localization and scope

The `seasons_clear_snow` and `seasons_clear_snow_description` tokens are provided
in the existing localization files. The UpdateTranslations pre-build target
generates embedded JSON from `EmbeddedLocalizations.csv`.

## Manual verification

Check tile selection, search, supplied icon, and localized name/description. Test
mouse and gamepad placement, switching back to terrain actions, and hammer repair.
Verify that hoe and cultivator placement particles animate normally with the
inventory and console closed, and that their previews still hide when appropriate.
Clear a healthy snow-covered roof, then try empty snow, open sky, bare terrain, an
unsupported piece, low stamina, and a protected piece. Check single-piece scope,
zero persistence through area/world reload, and later accumulation from snowfall.

While holding the hoe (also with BuildUi open), switch winter to spring and back,
and toggle seasonal snow and environment control. Verify removal/reappearance,
selection and ghost recovery, no-cost behavior, and cloned hoe tables after
re-equipping. Repeat after returning to the main menu and entering another world.

With two clients, test both local and remote owners, permitted/denied ward users,
large-piece edges, rapid repeated clicks, ownership handoff, leaving the area during
a request, and returning through the main menu. Confirm that failed operations do
not repair, alter terrain, or consume tool resources.

Validation performed by the assistant is limited to static source/API, XML/CSV,
diff, text, and file-hash checks. No mod build, automated mod test, or Valheim runtime
execution was performed. Gameplay and network behavior still require manual checks.
