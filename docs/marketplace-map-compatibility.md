# Marketplace territory map compatibility

## Supported rendering contracts

The compatibility adapter recognizes the legacy `void DoMapMagic()` entry point
with managed `Color[]` / `Color32[]` map snapshots, and the `Awaitable DoMapMagic()`
entry point present in the supplied Marketplace 10.0.6 decompiled source.

Marketplace 10.0.6 stores `originalMapColors` as
`NativeArray<TerritorySystem_Main_Client.RGB24Pixel>` and `originalHeightColors`
as `NativeArray<TerritorySystem_Main_Client.RHalfPixel>`. The adapter identifies
these exact field shapes by reflection. It does not require a compile-time
reference to Marketplace, Unity.Collections, or the private pixel structs.

The original compatibility check accepted only a void-returning method and
managed arrays. Both parts of that check rejected Marketplace 10.0.6.

## Rendering and buffer ownership

Both entry points route redraw requests to the existing bounded, main-thread
`MarketplaceMapOverlay` renderer. Terrain colors and floating-point heights come
from Seasons' existing native-map capture, not a rendered territory overlay.
Territory visibility, colors, gradients, priorities, `RevealOnMap`, and
`ShowExternalWater` retain the existing renderer and Marketplace method bindings.

Legacy managed snapshots are published and released as before. The native-array
path deliberately skips that publication and release: Marketplace retains
ownership of its persistent buffers. Seasons neither writes managed arrays into
those fields nor disposes or replaces their native storage. Marketplace's own
texture snapshot capture is left intact. No new native buffers or background
rendering tasks are created.

## Awaitable completion

The Awaitable-returning method has its own Harmony prefix with a typed result.
Returning false without supplying that result would leave an awaiting Marketplace
caller with null. `OnTerritoryUpdate()` in 10.0.6 awaits the draw before calling
`ZoneVisualizer.OnMapChange()`.

Every invocation receives a separate `AwaitableCompletionSource`. Requests for
the same map coalesce into the existing redraw, and their completions are raised
after the current overlay has been uploaded and the renderer state is committed.
Completion batches are detached before callbacks run, because Unity Awaitable
continuations can run synchronously and request another redraw.

Before a map has been captured, the result completes as a no-op; native capture
will queue the latest territories. Replacing or unloading a map also completes
its obsolete requests without writing to another world's textures. Rendering
exceptions complete existing callers normally, matching Marketplace's own
catch-and-return behavior, while the existing Seasons retry remains queued.
There is no additional polling component or coroutine.

## Release and verification

Seasons 1.10.6 updates the plugin version and Thunderstore manifest together.
The changelog contains a single compatibility entry. Snow clearing, ice floes,
snow simulation, localization, and the minimap rasterizer are unchanged.

Source basis: the supplied Seasons 1.10.5 project archive and Marketplace 10.0.6
decompiled source. Static checks cover reflection names/signatures, return-type
patch selection, completion and buffer-ownership paths, version consistency,
diff scope, and accidental Cyrillic outside localization. No mod build, automated
mod tests, or Valheim runtime execution was performed.

Manual verification: load a map with territories, switch winter/spring, toggle
seasonal minimap coloring and Marketplace map drawing, update/remove territories,
and return through the main menu to another world. Check map colors, gradients,
revealed areas, water display, and absence of unsupported-API or await errors.
