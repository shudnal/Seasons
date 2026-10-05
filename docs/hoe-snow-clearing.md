# Hoe snow clearing

Base: `2796400dfd3406f699370c1484e9b8a138d1fb6f` (Seasons 1.10.4).
Game source: `shudnal/assemblies_combined` at
`5a2365409cff644d6adaccd2b308178cc4179b19` (Valheim 1.0.16).

## Player behavior

Select **Clear snow** in the hoe's normal build grid, aim at an existing building
piece, and use the normal placement button. One successful action clears that
piece's seasonal snow to zero, including a fully healthy piece. It does not repair
health, remove a structure, place an object, modify terrain, or require a crafting
station. Normal hoe actions and hammer repairs retain their native paths.

A missing target, unsupported piece, or piece without seasonal snow uses the
existing `$msg_nosnow` message. Wards retain `$msg_privatezone`; insufficient
stamina uses the native stamina-bar flash. Only a successful action consumes the
hoe's normal build stamina/eitr/durability and plays its tool animation/effect.
This does not clear native Deep North snow, terrain snow, or creature materials.
Cleared pieces can accumulate fresh snow again according to the existing rules.

## Selection and UI

`Seasons_ClearSnow` is a private copy of the hammer's repair action, appended once
to the hoe's `PieceTable`. Its `m_repairPiece` flag remains true for native targeting,
repair-mode recognition, and suppression of the placement ghost. It does not enable
`m_canRemovePieces`. The action is made known before the active table is rebuilt;
no new key binding, raycast, or per-frame target search is added.

Two BuildUi transpilers treat only this action as an ordinary tile when creating
buttons and filtering search results. All other repair/remove items remain special.
The input transpiler routes the existing Player.Repair call to the snow action or
the real, patched repair method as appropriate. A direct-call guard prevents our
action from accidentally repairing health. Required hook mismatches disable the
action rather than silently falling back to terrain placement or repair.

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

RPC registration uses Game.Start; scene teardown removes only the handlers owned
by this feature. Per-peer receipts and outstanding requests are discarded on world
teardown. There is no periodic network check or background polling loop.

## Localization and scope

Only `EmbeddedLocalizations.csv` is edited for localization, adding
`seasons_clear_snow` and `seasons_clear_snow_description` in all 15 existing language
columns. The existing UpdateTranslations pre-build target generates embedded JSON
locally. No JSON files, version fields, release notes, or unrelated runtime logic
are changed in this branch.

## Manual verification

Check tile selection, search, supplied icon, and localized name/description. Test
mouse and gamepad placement, switching back to terrain actions, and hammer repair.
Clear a healthy snow-covered roof, then try empty snow, open sky, bare terrain, an
unsupported piece, low stamina, and a protected piece. Check single-piece scope,
zero persistence through area/world reload, and later accumulation from snowfall.

With two clients, test both local and remote owners, permitted/denied ward users,
large-piece edges, rapid repeated clicks, ownership handoff, leaving the area during
a request, and returning through the main menu. Confirm that failed operations do
not repair, alter terrain, or consume tool resources.

Validation performed by the assistant is limited to static source/API, XML/CSV,
diff, text, and file-hash checks. No mod build, automated mod test, or Valheim runtime
execution was performed. Gameplay and network behavior still require manual checks.
