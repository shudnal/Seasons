# Seasonal materials before native creature levels

Base: `8309530916e9e0e2a7dacb14ed6731522a180726`, PR #50.
Game source: `shudnal/assemblies_combined` at
`5a2365409cff644d6adaccd2b308178cc4179b19`, `assembly_valheim/LevelEffects.cs`.

## Scope

Coordinate the existing seasonal recolor with `LevelEffects.SetupLevelVisualization`
for living, non-player creatures whose main renderer's first material slot is
already controlled by Seasons. Snow-cap simulation, diagnostics, sliding, release
version, and the separate ragdoll coloring path are unchanged.

The reported invisible starred creature has not been reproduced in-game. This
change addresses the source/cache lifetime mismatch found during source review;
it is not proof that every invisible-creature report has this cause.

## Ordering

1. Select and update the normal seasonal source material, or the original material
   when reverting seasonal coloring.
2. Assign that source before running native level visualization. Do not use the
   previously generated level material as the next seasonal source.
3. Run the actual `SetupLevelVisualization`, including its ordinary Harmony patches.
   The native method still selects the level setup, creates the material, sets its
   hue/saturation/value/emission, and manages scale and enabled objects.
4. Retire obsolete owned copies after the complete patched invocation, including
   exception exits. Exceptions are not suppressed.

Registration before `LevelEffects.Start` only assigns the source. It does not set
`m_character`, subscribe to level changes, or invoke an uninitialized component.
The subsequent native Start/level-change call observes the prepared source through
the same bridge. Repaints after initialization invoke the native method again.

The existing public no-argument `RemoveFromPrefabList` API is preserved. Level
bindings are released when a creature unloads, its controller is reinitialized or
removed, or the world controller is destroyed. Reinitialization detaches to the
clean source before capturing material bindings again. A live creature leaving
seasonal control gets native level visualization over its original material.
Dying/unloading visuals do not receive an unnecessary native regeneration.

## Cache isolation and ownership

Vanilla keys its global material cache by prefab name plus level. That key cannot
represent two creatures with the same prefab and level but different seasonal
color variants, or a seasonal creature and an unmodified creature.

For a registered invocation only, native loads of `LevelEffects.m_materials` are
routed to that binding's private dictionary. It is cleared before regeneration
and after the invocation. Neither the global dictionary nor its entries are
cleared, swapped, overwritten, or destroyed by the bridge. Unregistered creatures
continue to use the ordinary native cache.

The existing `Material(Material)` constructor call is observed separately to
identify precisely which copies this binding created. Only those owned copies
can be retired, and only after they are no longer assigned to the bound renderer.
Unrelated replacement materials and the vanilla cache are not owned by Seasons.
A native copy of a seasonal source is recognized during normal reversion, unlike
the previous check that recognized only a direct seasonal-material assignment.

This deliberately generates a fresh level material per affected creature at
repaint/level events. It does not add an Update loop, polling, per-frame creature
scans, or new settings. Copies are not shared across distinct creature bindings,
so one creature's seasonal variant cannot determine another creature's texture.
Bulk recolor/revert iteration uses snapshots because level callbacks can activate
or retire controlled objects. A same-binding repaint requested recursively from
a level callback does not recursively run that native calculation again.

## Compatibility boundary

The level formula is not reimplemented, and the native method is not skipped.
Normal patches to level parameters, scale, activation, and the native result still
execute. This is not a guarantee for arbitrary other transpilers or mods that
assume every level material must appear in the global cache. Such integrations
require their actual source or a reproduction.

Cache redirection and constructor ownership observation are installed together.
The expected source has two cache loads and one material-copy constructor. If
that structure cannot be found, a warning is emitted and the bridge is not
registered; partial cache/ownership rewriting is not applied.

## Manual verification

- In spring, compare ordinary, one-star, and two-star Draugr, including existing
  and newly spawned creatures. Body visibility and level appearance must persist.
- Keep two same-level creatures in different seasonal color variants. Repaint,
  toggle custom textures, and revert seasonal coloring; their variants must not
  become whichever creature was processed first.
- Repeat custom-texture reloads and season changes on the same living creature.
  Inspect both body visibility and star hue/emission. Do not rely only on a new spawn.
- Leave and reload the area, then leave and re-enter the world without exiting the
  game. Native cached materials must not retain released seasonal textures.
- Repeat with the reporting user's creature/level/visual mods. Check the log for
  the bridge warning or exceptions, and distinguish the same creature disappearing
  again from a newly loaded creature being invisible.

Validation for this change is static source and diff inspection only. No build,
automated mod tests, or Valheim runtime execution was performed.
