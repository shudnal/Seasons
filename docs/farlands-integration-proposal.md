# Seasons × FarLands: integration proposal

**Status:** Draft for joint technical review (revision 1). Not an implementation specification or approval to change either repository.

**Goal:** Agree on a small, performant contract between the two mods, then implement both sides against the agreed contract. This document is intended for the FarLands developer and their coding assistant. Please review it against the actual FarLands source and shaders, correct any inaccurate assumptions, and return a revised document or a focused response to the open questions. Preserve the agreed requirements unless proposing an explicit change with a reason.

## 1. Scope and verified baseline

Two independent improvements are proposed:

1. **Autumn tree colors:** distant deciduous trees should show the same spatial distribution of autumn color variants as nearby trees, with approximate colors acceptable. Preserve FarLands' GPU instancing and indirect rendering architecture.
2. **Protected grass:** allow FarLands' background grass generation to remain active with Seasons, while preserving the seasonal grass settings and shield protection behavior.

No new general-purpose rendering system, per-instance material creation, cross-mod dependency for unrelated features, or per-frame CPU scan is requested. Spring, summer and winter need no additional distant tree color variation. Existing snow, terrain, building, rock and Seasonality integrations must continue to work. Existing approximation in FarLands' placement and rendering algorithms is not a request to reproduce every detail of the game simulation.

### Source baseline

| Component | State inspected |
| --- | --- |
| Seasons | 1.11.0, [`64809f4ec3e7f735c5477497a404d47889e83984`](https://github.com/shudnal/Seasons/tree/64809f4ec3e7f735c5477497a404d47889e83984) (`master` after PR #52) |
| FarLands | Decompiled 1.5.2 assembly, informational version `1.5.2+c606f871c912653a00e4d6576a9035c832e13018`. Decompiled C# file SHA-256 `c31f70938beba74c66b67ac1efbad3eeb75f4d76789d579c72ac0d6936092a6a`. |
| Valheim | 1.0.30, [`216cb30f748e0ef9c74a8366395e9cc110f3cd8d`](https://github.com/shudnal/assemblies_combined/tree/216cb30f748e0ef9c74a8366395e9cc110f3cd8d) |

The FarLands analysis is based on the decompiled assembly, not its complete source repository. Shader source and all shader channel semantics were not available. Statements about the proposed shader changes are design suggestions, not claims about existing shader capabilities. No builds, runtime tests or performance measurements have been performed. Source line references below refer to the inspected revisions, not necessarily the latest working branches.

## 2. Autumn colors: current Seasons implementation

### 2.1 Material and texture variants

Relevant Seasons files:

- [`Controllers/PrefabVariantController.cs`](https://github.com/shudnal/Seasons/blob/64809f4ec3e7f735c5477497a404d47889e83984/Controllers/PrefabVariantController.cs): `MaterialVariants`, `PrefabVariant.Initialize`, `UpdateFactors`, `GetCurrentVariant`, `UpdateColors`, `GetNoise`, `GetVariant`, `WorldToMapPoint`.
- [`SeasonTextures/SeasonalTexturePrefabCache.cs`](https://github.com/shudnal/Seasons/blob/64809f4ec3e7f735c5477497a404d47889e83984/SeasonTextures/SeasonalTexturePrefabCache.cs): `ColorsCacheSettings`, `ColorReplacementSpecifications`, `GetTextureVariants`, `GetColorVariants`, `GenerateTextureVariants`.
- `SeasonTextures/TextureVariants.cs`, `SeasonTextures/CachedData.cs`, `SeasonTextures/SeasonalTextureVariants.cs`, `CustomTextures.cs`.

Seasons generates four variants per season. `MaterialVariants` holds four shared materials for a particular original material and cached material context. On a season change, their texture and color properties are updated to the selected season. A nearby loaded object receives one of those materials, based on its world position. Seasons does **not** create four unique materials for each individual tree. It also has support for material color properties, prefab/material-specific overrides and custom seasonal textures.

`GetTextureVariants` reads the original texture pixels, resolves material/renderer/prefab-specific color and position rules, and constructs `pixelsToChange`. It then resolves the applicable palette (prefab override, material override, conifer, plant/grass, moss, creature, bush, piece or default seasonal palette). `GenerateTextureVariants` copies the original pixels and applies `ColorVariant.MergeColors` only to the selected pixels, for each variant of each season.

The default `seasonal` autumn palette is:

| Zero-based index | Intended appearance | Target RGB (0–1) | Blend proportion |
| --- | --- | --- | --- |
| 0 | Golden / yellow-orange | (0.8, 0.5, 0.0) | 0.75 |
| 1 | Orange-red | (0.8, 0.3, 0.0) | 0.75 |
| 2 | More red | (0.8, 0.2, 0.0) | 0.70 |
| 3 | Close to the original green / yellow-green | (0.7, 0.7, 0.0) | 0.20 |

These are blend targets, **not final pixel colors or RGB multipliers**. For this palette `MergeColors` interpolates the original RGB toward the target, converts to HSL, restores the original HSL lightness, and converts back to RGB. The alpha channel is preserved. Other palettes can have different proportions and luminance/grayscale settings. For example, conifers receive substantially weaker autumn recoloring. Do not apply the default deciduous palette to every vegetation material.

Default `ColorReplacementSpecifications.seasonal` accepts the union of the following HSL ranges (other constraints are at their default ranges):

| Hue (degrees) | Saturation minimum | Lightness minimum |
| --- | --- | --- |
| 80–160 | 0.15 | 0.00 |
| 55–91 | 0.20 | 0.18 |
| 33–57 | 0.28 | 0.26 |

The actual selection also includes material-specific and position-specific rules. Therefore the exact `pixelsToChange` list is a better source of a recolor mask than reproducing those three ranges on already recolored albedo. A tree may contain bark and leaves in the same texture; a renderer/material name alone is not always sufficient to distinguish them.

### 2.2 Deterministic spatial variant selection

`PrefabVariant.Initialize` calls `WorldToMapPoint` and `UpdateFactors` once. Separate noise factors are computed for all four seasons; `GetCurrentVariant` selects the appropriate factor. The existing autumn calculation is equivalent to the following (call the existing functions, do not implement a second approximation):

```csharp
public static int GetAutumnVariant(Vector3 worldPosition)
{
    PrefabVariantController.WorldToMapPoint(worldPosition, out float mx, out float my);
    return PrefabVariantController.GetVariant(
        PrefabVariantController.GetNoise(mx, 1f - my));
}
```

This wrapper is **proposed**, not a method that already exists. `WorldToMapPoint` computes `(x / 12 + 1024) / 2048` and `(z / 12 + 1024) / 2048`. `GetNoise` uses the world's seed factor (`log10(abs(seed))`, or zero), two Perlin samples, frequency 10000, a divisor of 1.1, a power of 1.3, and rounding to increments of 0.05. In autumn the coordinates are `(mx, 1 - my)`:

```text
n = (Perlin(mx * 10000 + seedFactor, (1 - my) * 10000 - seedFactor)
   + 0.5 * Perlin(mx * 20000 - seedFactor, (1 - my) * 20000 + seedFactor)) / 1.1
factor = Round(Pow(n, 1.3) * 20) / 20
index = factor < 0.25 ? 0 : factor < 0.5 ? 1 : factor < 0.75 ? 2 : 3
```

Use the existing implementation for exact float/double arithmetic and rounding behavior. The result depends on position and world seed, not tree type, camera position, tile load order or time within autumn. It is not a uniform random distribution of four colors. The API should only be called after the Seasons controller and world are ready, on the main thread unless thread safety is explicitly established.

## 3. Autumn colors: current FarLands implementation

From the inspected assembly:

- `GameLooks.Materials` uses `SeasonsLink.Varies`, creates a renderer/LOD mirror of a prefab under an inactive holder at `(0, -5000, 0)`, and calls `SeasonsLink.Follow`. `SeasonsLink` uses reflection to access the Seasons controller, variant cache and related methods. It then returns the mirror renderer's shared materials.
- This supplies one seasonal appearance per prefab/renderer. Since the mirror has a fixed position, the actual positions of the distant trees are not used in the variant selection. This explains the uniform autumn colors.
- `GameLooks.Watch` / `Changed` detect changes in materials and textures; `TickForest` calls `StopForest` and recreates the far forest when `GameLooks.Version` changes. Use this existing lifecycle for the new appearance data rather than rebuilding the forest again through another independent mechanism.
- `TreeMeshes` / `MeshParts` / `ModelLayer` render the far model LODs. `ModelLayer` uses a copied material with the corresponding FarLands shader, and issues `Graphics.RenderMeshIndirect` per mesh part. The original source material is not a place to store per-tree values.
- `ImpostorBaker` uses 64 views (8 × 8). `Stage` prepares a model with its materials. A deferred camera renders each view, then command-buffer blits from the GBuffer through `ImpostorBake.shader` into `_tileAlbedo` and `_tileNormal`. `Step` copies tiles into the atlas; `EndType` generates mipmaps and reads normal atlas alpha for coverage. Some types also have a separate snow albedo atlas. `ImpostorDrawer` renders the impostor using indirect drawing.
- `InstanceBuffer.Put` writes `GpuTree` entries containing position, scale, yaw, type and two tilt values: eight floats, 32 bytes. `TreeTypes.Max` is 32; several culling/counting arrays and type masks are organized by tree type. `FarForest.Capacity` is 524288. Model and impostor paths share the tree instance data.

Relevant decompiled symbols/line anchors: `GameLooks` 21738+, `ImpostorBaker` 23066+, `ImpostorDrawer` 23658+, `ImpostorLayer` 23829+, `InstanceBuffer` 24006+, `ModelLayer` 25393+, `SeasonsLink` 27729+, `TreeMeshes` 28874+, `TreeTypes` 29097+, `TickForest` 31406+.

## 4. Proposed autumn integration

### 4.1 Requirements

- Use the existing Seasons autumn noise function, evaluated once per relevant tree when filling the GPU instance buffer, not every frame or in the pixel shader.
- Use **variant 3 (the fourth variant)** as the base appearance for eligible deciduous trees in autumn. Variants 0–2 are approximate color corrections of that base; variant 3 is identity.
- Preserve the existing tree type, mesh parts, culling groups, indirect draw command count and single set of geometry/normal/coverage data. Do not duplicate every tree type or create a draw group per color variant.
- No additional color-variant atlas sets. One optional compact mask atlas is acceptable if the existing atlas channels cannot safely hold the mask.
- Apply correction only to eligible autumn materials/prefabs. No extra autumn color work in other seasons; no forced recoloring of noncontrolled trees, trunks, rocks, or other unrelated materials. Existing seasonal rendering remains the default for unsupported materials.

### 4.2 Main-thread appearance API (Seasons responsibility)

Provide a small public integration module in Seasons with directly callable methods. Exact signatures, assembly placement and lifetime contract are to be agreed; examples below describe the required data, not a finalized API:

```csharp
// Proposed contract, not existing methods/types.
public static bool IsAutumnVariationActive { get; }
public static int GetAutumnVariant(Vector3 worldPosition);
public static bool TryGetAutumnAppearance(
    GameObject prefab, MeshRenderer renderer, int materialIndex,
    out AutumnAppearance appearance);
```

The appearance result should identify the correct base shared material (or supply the required texture/color bindings for that exact renderer slot), the recolor mask for the relevant albedo texture, and four shader correction parameters. It must use the **same cached renderer/material/texture mapping** as native Seasons, including LOD context, rather than a texture name heuristic. A public method to get the mask for a resolved cache entry is useful independently of FarLands.

Seasons owns the returned shared resources; FarLands must not mutate or destroy them. FarLands owns copies, baked atlases and shader bindings it creates. Lookup/material construction is per appearance, not per tree. A false result means use the existing material path without additional autumn variation. The base variant must be explicit, not obtained by moving a mirror to a guessed coordinate.

Reuse existing material variants and texture cache where possible. If it is simpler for the initial integration to keep the mirror for material resolution, expose an explicit variant override in that controlled path. Do not change the normal near-tree variant selection or the shared original prefab to accommodate FarLands. The new API should remove the need for reflection in the new integration. Existing unrelated reflection-based features are outside this task.

### 4.3 Generate and cache source masks in Seasons

Seasons already knows the exact pixels that will be recolored. Build the mask from `pixelsToChange` while generating the seasonal texture cache, before modifying the original pixels:

```text
mask = byte[originalWidth * originalHeight]  // initially 0
for each pixelIndex in pixelsToChange:
    mask[pixelIndex] = 255
```

A single mask covers the four variants of this generated texture because all variants use the same selected pixel indices. Values are 0 (unchanged) or 255 (recolor); filtering/mipmaps produce intermediate coverage. The mask is scalar, linear data, not an sRGB color image. Respect source texture dimensions, UV orientation, wrap and filtering. Preserve existing source alpha/cutout separately. The mask must be associated with the actual cached texture entry (and relevant material binding), not a globally unique name or a Unity instance ID taken from a different game session. The current cache records texture IDs in `CachedRenderer`; reuse those resolved cache-local keys. Do not add a separate per-season or per-variant mask unless a real requirement emerges.

A mask can be absent for an uncontrolled texture or one whose custom replacement no longer corresponds to the generated texture. `CustomTextures.HaveCustomTexture` can replace a generated variant with arbitrary user content. For this integration, do not recolor a custom replacement using an unverified mask. Retain the existing appearance instead. No attempt to infer or generate masks for arbitrary user-provided textures is required.

**Agreed direction for the Seasons cache work:** integrate mask generation with modernization of the existing on-disk cache. The existing binary format uses `BinaryFormatter`; this serializer is obsolete, not binary storage itself. `CachedData` currently supports Binary, Json and both; `TextureData` stores the original PNG and sixteen variant PNG byte arrays. `SeasonalTextureVariants.Initialize` deserializes the cache and creates the textures. `SaveCacheOnDisk` collects encoded data, deletes the revision directory and writes the replacement under `s_diskLock` on a worker. Thus simply writing new mask files into the revision directory during generation would lose them when the existing save path deletes that directory.

Replace `BinaryFormatter` with an explicit versioned cache format or existing per-entry storage where it is sufficient. Avoid loading every mask byte array into RAM during normal cache initialization. A simple first design is a cache-local manifest/metadata plus separate payload files or an indexed binary file with independently addressable entries. Mask payloads must be independently accessible in every supported cache mode. Use the existing revision directory and save/rebuild lifecycle; increment `globalRevision` (currently `1.6.0`) so clients regenerate compatible data. Avoid writing mask files that can be removed by the final save; stage/publish them with the same cache generation. Preserve the meaning of the existing storage configuration unless its migration is explicitly agreed.

Load masks lazily through a public getter when requested (not at startup just because FarLands is installed). Prefer a linear single-channel `R8` GPU texture with mipmaps if the format is supported. A PNG or compact raw payload is a disk-storage choice, distinct from the GPU format; do not assume `LoadImage` preserves `R8`. Unity texture creation/upload belongs on the main thread. Share the loaded mask for the current cache generation, release it on cache replacement/disposal after consumers have been invalidated, and do not retain all decoded masks or raw pixel arrays indefinitely. When no mod requests masks, no mask textures or persistent mask payload arrays should remain resident. Existing generated seasonal textures are unchanged by this proposal.

The serializer/cache migration is owned by Seasons and can be designed independently of FarLands' renderer. Do not make FarLands wait for a particular internal serialization format; its contract is the returned appearance/mask and its lifetime.

### 4.4 Bake the source mask into the impostor (FarLands responsibility)

The mask supplied by Seasons is in source-texture UV space. It cannot be directly sampled using the impostor atlas UVs. FarLands must project it onto the **same** 64 views as the base albedo. The rendered mask must respect the same geometry, material UV transforms, view/projection, alpha cutout, depth test and occlusion as the base image. A leaf hidden behind a trunk must not contribute to the mask of the visible trunk. Render opaque/non-recolorable fragments with zero mask, not simply as missing geometry. Filtering at leaf edges and mipmaps should remain consistent with coverage so there is no colored fringe or change in silhouette.

Recommended implementation order:

1. Inspect the actual baking shader(s) and GBuffer usage. If a valid existing render target/channel or bake path can carry source mask samples, output the mask alongside base albedo during baking. Prefer writing the mask in an already-read atlas channel **only if** it has no existing meaning and sufficient precision, including mipmaps, compression and all rendering/shadow paths.
2. Otherwise use a dedicated linear single-channel atlas for eligible trees in autumn. Render/bake the source mask using the same view and alpha/depth logic; a separate mask render pass at bake time is acceptable. Do not render a second geometry pass at runtime for color variation. Reuse tile textures across frames/types, copy matching tile coordinates, generate mipmaps at `EndType`, and dispose of atlas/tile resources with the existing baker. A bake-time pass is preferable to a costly per-pixel classification during every frame.

The inspected `ImpostorBaker` copies post-GBuffer data: its full-screen blit no longer has the original material UV. Simply adding the original mask texture to `ImpostorBake.shader` is not sufficient. The exact pass/MRT/replacement-material implementation needs review with the source shaders. **Normal atlas alpha is not free**: `ReadCoverage` reads it to determine silhouette/coverage cutoffs. The meaning of albedo alpha and other channels must be verified before packing. Do not sacrifice normal precision, coverage or existing snow support to store the mask. If no channel is safely available, an R8 atlas is a small, explicit cost: nominally 1 byte per texel vs 8 bytes for two RGBA8 atlases, or +12.5% for those two same-sized mipmapped atlases (not +12.5% of all FarLands VRAM). At 1024², including a full mip chain, a separate R8 atlas is about 1.33 MiB per applicable tree type. Actual allocation depends on format/platform and should be measured.

For the intermediate mesh models, bind the original mask to the copied material or reuse an appropriate existing mask channel. No projection into an impostor atlas is needed there. The model and impostor paths should use the same palette and instance index to avoid a new color discontinuity at the model/impostor transition.

### 4.5 Instance index and shader correction

Compute the existing autumn variant index in `InstanceBuffer.Put`, after filtering to owned tree instances and only for eligible types with autumn variation enabled. Use each `TreeInstance`'s world coordinates, not its tile center or prefab mirror position. Verify that this path runs on the main thread. Existing `TickForest`/appearance invalidation already rebuilds the buffers on look changes; do not add another global per-frame index-update pass. This index need not be written to persisted FarLands world/placement caches; it is derived visual data. A noneligible instance or nonautumn season uses the identity index or a disabled per-type/material flag.

The current `GpuTree` struct is 32 bytes and all eight fields are in use. The authors should agree how to store the extra 2-bit value (a small explicit field, an existing proven spare representation, or a parallel buffer). A 4-byte field for 524288 instances is 2 MiB of raw additional buffer storage before alignment/other consumers; a packed representation can avoid that cost but must not break type-based culling, the forest mask, shade/shadows, indirect args, understorey, rocks or any other consumer. Do not silently overload fractional `Type` or tilt bits without checking every C#/HLSL consumer and layout. The invariant is unchanged **logical tree type and batching**, not necessarily unchanged physical stride. Prefer the simplest verified layout; if a safe packing is already available in FarLands, use it.

The shader reads the variant once per instance (e.g. pass as a `nointerpolation` varying where appropriate). The palette is shared data per appearance/material/type, not four colors per instance. Use a constant buffer, material properties or another existing per-type data path, whichever fits FarLands. Apply the correction to albedo before lighting, in the correct linear/sRGB domain. For a mask of 0 or the identity variant, leave the base pixel unchanged; never change alpha, normals or geometry.

A simple candidate correction, intentionally not an exact reproduction of the HSL pipeline:

```hlsl
// Pseudocode. tint is normalized to unit luminance in the chosen color space.
float y = dot(baseAlbedo.rgb, luminanceWeights);
float3 recolored = y * tint[variant];
float3 rgb = lerp(baseAlbedo.rgb, recolored, mask * strength[variant]);
// strength[3] = 0; preserve baseAlbedo.a
```

Seasons can supply target tint/strength or a small precomputed color transform derived from the actual palette and base variant. The original `ColorVariant.color` and `targetProportion` cannot be used blindly as multiplicative values: the base has already been recolored by variant 3. Use a relative correction, not a second application of the original blend as if the base were the unmodified texture. The objective is visible green, gold, orange and red groups with preserved local detail, not numerical equality to the four generated textures. Select the transform and tune constants after looking at real tree albedo and screenshots. Do not require per-fragment RGB/HSL conversions to reproduce the native pipeline. A simple linear transform with an identity entry may also be suitable if it handles the red/yellow range well; avoid saturating all detail in a channel. Conifers or materials whose palette is not the agreed deciduous one can remain unchanged initially.

### 4.6 Alternatives considered

| Approach | Decision and reason |
| --- | --- |
| Four render groups/materials per type | Rejected. Up to four times as many applicable indirect draws/groups, more culling/group management; risks the 32-type structure. Not a fourfold FPS prediction. |
| Four albedo layers / full variant atlases | Rejected for this scope. Can preserve draw count but multiplies the color atlas storage and bake work for a small visual improvement; exact colors are not required. |
| Compute noise/hash in shader | Rejected. The original position-based function is already available; compute once at upload, not repeatedly in vertex/fragment stages. A new hash would not match Seasons' spatial distribution. |
| RGB/HSL foliage detection every fragment | Rejected as the preferred solution. Repeats mask classification every frame and can recolor bark, especially after filtering/lighting/color changes. |
| Infer a mask from base albedo during baking | Acceptable simpler alternative if exact mask baking is disproportionately expensive. No extra runtime classification, but loses material/position rules and may select bark or miss foliage. Discuss if the real bake shaders make exact-mask projection impractical. |
| Exact mask from Seasons cache + bake-time projection | Preferred. Source mask is already known during cache generation, mask work is amortized, runtime only needs mask sampling and cheap color correction. Dedicated R8 atlas is allowed if packing cannot be verified. |

The preferred solution trades limited cache disk space and bake-time work for minimal steady-state cost. Actual GPU timing and visual quality still require verification. A separate mask sample is not literally free.

## 5. Grass: current Seasons implementation

Relevant files: [`Controllers/ClutterVariantController.cs`](https://github.com/shudnal/Seasons/blob/64809f4ec3e7f735c5477497a404d47889e83984/Controllers/ClutterVariantController.cs) (`AddShieldedGrass`, `ApplyGrassSettings`, `UpdateShieldActiveState`, `InstanceRenderer_AddInstance_PreventInProtectedArea`), `Seasons.cs` (`IsShieldProtectionActive`, `IsShieldedPosition`, `IsIgnoredPosition`), `Controllers/PrefabVariantController.cs` (`ShieldDomeImageEffect_SetShieldData_ProtectedStateChange`, `ShieldDomeImageEffect_RemoveShield_ProtectedStateChange`). Game: `assembly_valheim/ClutterSystem.cs`, `InstanceRenderer.cs`, `ShieldGenerator.cs` at the pinned Valheim revision.

Seasons uses the normal `ClutterSystem` configuration. It changes shared materials, grass patch size, amount scale, per-clutter scale range and enabled state as the season/settings change. These properties already feed the normal game generator. `ApplyGrassSettings` only requests `ClearAll` when relevant final state changes. The existing seasonal flower/bloom additions and EWD clutter handling should remain intact.

To keep original grass beneath active shields, Seasons clones seven clutter entries with independent original materials:

```text
instanced_meadows_grass
instanced_meadows_grass_short
instanced_forest_groundcover
instanced_forest_groundcover_brown
instanced_heathgrass
instanced_swamp_grass
instanced_mistlands_grass_short
```

The clone name and prefab name end in `_inshield_seasons`. `AddShieldedGrass` copies the clutter definition, clones the prefab and `InstanceRenderer.m_material`, and initially disables the clone. Seasonal changes can disable or shrink the ordinary grass; the protected clones can remain enabled with original appearance/scale. The shared amount scale and other actual placement settings still apply. The two clutter entries are separate generators with their own list index/seed: this is **not** a one-to-one material swap of identical grass instances.

`isAnyShieldActive` is updated from `IsThereAnyActiveShieldedArea` and current seasonal/texture settings. Shield protection follows configuration (`shieldGeneratorProtection`, optionally winter-only); it is not inherently restricted to winter. The active-area check requires a valid positive tracked shield whose center is not ignored. The clones are enabled only while there is an active protected area. The existing placement prefix targets both positional overloads of `InstanceRenderer.AddInstance`. It classifies the renderer as a registered shield copy (cached by renderer name) and effectively implements:

```csharp
// Existing behavior, simplified from the actual prefix.
if (!isAnyShieldActive)
    return true; // shield copies are normally disabled in this state

if (SeasonState.IsActive && IsShieldedPosition(pos))
    return isShieldedGrass && IsCoveredByShield(pos);

return !isShieldedGrass;
```

Important details:

- The ordinary instanced placement path is filtered under a shield, not only the seven ordinary originals that have protected copies. Preserve this distinction rather than restricting the normal rejection to a name suffix or a hard-coded list of seven source prefabs. Keep the existing Harmony prefix for native instancing, including non-GPU kinds.
- `IsShieldedPosition` calls the game's `ShieldGenerator.IsInsideShieldCached` with mutable caching. The underlying native test uses the live dome center and current `m_radius`, with strict 3D distance `< radius`. `CheckShield` validates the shield/dome references. `IsCoveredByShield` uses Seasons' tracked `shieldRadius` dictionary and `GetShieldPosition()` with strict 3D distance `< trackedRadius - 2`. The smaller radius prevents protected grass from sticking out near the shield border. These are two predicates, not necessarily identical sets of radii. With overlapping shields, retain the unions of the outer and inner predicates; do not assume both hits must be from the same shield if the sources differ.
- In a simple single-shield case, ordinary grass is allowed outside the outer radius, the protected clone is allowed inside the inner radius, and neither is allowed in the inner 2 m boundary band. This intentionally preserves the existing effect.
- `IsIgnoredPosition` is used when deciding whether there is an active protected area; the existing per-instance prefix does not independently exclude all ignored biomes. Do not add a new per-instance ignored-biome gate as part of this integration. The native placement settings still determine which clutter types can generate in a given biome.
- The current `IsShieldedPosition` and `IsCoveredByShield` use dictionaries, native shield lists and Unity object/transform access. **Neither may be called directly from FarLands' grass worker.** The proposed worker logic should implement the intended geometric tests from a main-thread snapshot, not reproduce quirks of the native/shared mutable position caches.

The native `ClutterSystem.GenerateVegPatch` first determines all placement conditions, random offset and scale, then calls `InstanceRenderer.AddInstance(position, rotation, scale)` for accepted instanced clutter. The prefix rejects the final position; it does not change RNG consumption or the clutter seed.

## 6. Grass: current FarLands implementation and conflict

In the inspected FarLands `GpuGrass`:

- `ForeignBuild(ClutterSystem)` caches a decision for a `ClutterSystem` instance. It inspects Harmony owners on `ClutterSystem.GenerateVegPatch` and `InstanceRenderer.AddInstance(Vector3, Quaternion, float)` / `AddInstance(Matrix4x4)`. It disables background grass generation for unknown foreign patches (with existing exceptions for FarLands and known idle patches). The Seasons positional prefix is one of these foreign patches. GPU rendering/upload of native-created grass is a separate path and can still work. Do not equate this conflict with the entire FarLands grass renderer being disabled.
- `RefreshKinds` reads enabled, instanced clutter with a supported `Custom/Grass` material, extracts placement properties into `BuildKind`, and retains the original clutter list index. It reads scale, amount/quality, biome/forest/altitude/vegetation/clearing/tilt/water/random offset and other existing placement settings. These are already compatible with Seasons' normal property changes, subject to normal rebuild timing.
- `Patch_Build.Prefix` temporarily disables those supported entries so the native generator does not generate them too. Its postfix queues a `BuildJob`, and its void finalizer restores enabled entries. Unhandled clutter is still generated by the game. Keep this split and the existing index-based RNG.
- `PumpBuild` calls `Prepare` on the main thread, which snapshots terrain maps, then enqueues the job for `WorkerLoop` on a background thread. `Build` creates final positions and transforms using the copied terrain/placement data and deterministic `BuildRandom`. `Apply` uploads only a job accepted by `Current(job)` (the owner is still present in `Built` and refers to that exact job).
- `Build` consumes the random height offset and scale before writing `_buildRows`, `_buildGround`, bounds and counts. The per-kind accepted instance cap is 1023. This is the natural insertion point for a placement predicate.
- `Freed`, `BuiltCells`, `BuiltPatch`, `Current`, `ClearBuilt` and `Patch_FreePatch` manage replaced, reset and freed jobs. `ClutterSystem.ResetGrass` marks intersecting patches for replacement; it does not synchronously rebuild every patch. Preserve replacement and handoff behavior.

Relevant decompiled anchors: `BuildKind` 1331+, `BuildJob` 1396+, `Patch_Build` 1489+, `ForeignBuild` 3160+, `PumpBuild` 3218+, `Current`/`Apply` 3274+, `Freed` 3419+, `RefreshKinds` 3499+, `Prepare` 3575+, `WorkerLoop` 3659+, `Build` 3781+ (final position/scale and rows near 3960+). These anchors are for orientation; use the current actual source for changes.

## 7. Proposed grass integration

### 7.1 Minimal public API and snapshot

The goal is to preserve the existing filter without executing main-thread Seasons code from the grass worker. Expose a direct API in the Seasons integration module, conceptually:

```csharp
// Proposed main-thread calls; names and types to be agreed.
public static bool IsShieldedClutter(ClutterSystem.Clutter clutter);
public static GrassProtectionSnapshot GetGrassProtectionSnapshot(
    Vector2Int patchId, float patchSize);
```

`IsShieldedClutter` resolves the actual registered clone identity; do not require FarLands to parse the suffix. Evaluate it when building `BuildKind`, once per relevant kind, not for each candidate. The snapshot contains an active-state flag and immutable plain numeric data for the relevant shield geometry (centers and squared radii for outer and inner tests). It may expose a pure predicate, or FarLands may implement the small agreed predicate on the data. The choice should avoid allocations and cross-mod callback/delegate overhead in the inner loop. No Unity `Object`, `Transform`, mutable dictionaries, `ShieldGenerator` references, config entries or calls into world generation in a worker snapshot. Value types such as position coordinates are fine; only use pure numeric operations on the worker.

Prefer capturing a shared immutable geometry snapshot when relevant shield state changes (or collecting relevant entries in `Prepare` if simpler and cheaper), then give each job the subset intersecting its patch or reference a shared immutable array. Do not enumerate the live game shield list per grass blade. A patch overlap test can use the minimum squared XZ distance from each sphere center to the patch square; this safely rejects nonintersecting spheres independently of terrain height. The final per-instance test must use XYZ, including actual ground height and random offset. Only include positive inner radii (do not square a negative radius). Outer geometry uses the valid native dome/current radius, inner geometry uses the tracked center/radius minus 2, as described above. Snapshot construction may consolidate them when verified equivalent, but the API must preserve both semantics.

### 7.2 Placement in the existing pipeline

```text
Main thread: RefreshKinds
  classify each eligible BuildKind as ordinary or Seasons shield copy
Main thread: Prepare
  acquire stable protection state/geometry for this job; determine intersection
Worker: Build
  skip protected-copy kinds in cells with no possible shield overlap
  run existing placement and RNG for other kinds
  after final position and scale RNG, before adding rows/counts/bounds:
    reject placements that do not pass the agreed geometric filter
Main thread: Apply
  use existing Current(job) / patch replacement lifecycle
```

Pseudocode (per-job, pure numeric state):

```csharp
bool Allows(in GrassProtectionSnapshot s, bool isShieldCopy, Vector3 pos)
{
    if (!s.Active)
        return true; // same prefix semantics; clones are disabled in normal state

    bool insideOuter = s.ContainsOuter(pos); // union of strict radius tests
    if (!insideOuter)
        return !isShieldCopy;

    return isShieldCopy && s.ContainsInner(pos);
}
```

`Active` should represent the full effective placement gate, including season/texture/protection state; if the API captures an unusual transition where `isAnyShieldActive` is true but `SeasonState.IsActive` is false, preserve the native result (ordinary allowed, copies rejected). The API should define this invariant explicitly and ensure the enabled clone state agrees with it. For each sphere, compare squared 3D distance with squared positive radius. No square root or Unity object access is necessary. For ordinary candidates, only the outer test is needed; for protected candidates, test both outer and inner when relevant. With protection active but no intersecting outer sphere, skip the shield-copy kind and use the exact current ordinary path, with no per-candidate mask lookup. With no protection active, do not add worker work beyond a job/kind-level gate. No new kind, buffer, shader, or draw group is required beyond the clones that Seasons already creates.

**RNG and counts:** filter only after the generator consumed the random offset and scale, before writing matrix rows, ground data, count and bounds. Rejecting a candidate must not skip random draws that the existing generator would consume. Accepted instances alone count toward the 1023 cap, matching the native prefix behavior. It is safe to skip a whole protected-copy kind in a nonintersecting patch because its RNG is seeded independently using its original `BuildKind.Index`; never renumber kinds or derive seeds from a compacted list. Do not attempt to convert ordinary instances to copies in place: the two generators can have different amount/scale/biome settings and independent placement sequences, and ordinary clutter can be disabled. The intent is equivalent **conditional placement**, not identical placement between the existing game generator and FarLands' already approximate generator.

Do not implement full-contained-patch optimization for the first implementation. It needs reliable vertical bounds, snap-to-water and random-offset handling and provides little benefit over the safe no-overlap path. Do not add a spatial index unless real shield counts/profile data justify it.

### 7.3 Shield changes and invalidation

Seasons already calls `ResetGrass(position, affectedRadius + 1)` when relevant shield visual radius changes. `SetShieldData` updates its tracked radius and caches on changes and throttles visual refresh (first radius, change of at least 3 m from last visual radius, or target reached); `UpdateShieldActiveState` also calls `ClearAll` when the global clone enablement changes. Keep this policy; do not force a full rebuild on every animated radius step or every frame.

Ensure the snapshot is taken from current authoritative geometry at job preparation. When an affected patch is reset, superseded or freed, its obsolete job must not be uploaded as the current result. The existing `Current(job)` check is a useful basis but needs review for the interval between a patch being marked `m_reset` and replacement/freeing; a job that finishes in that interval should follow the agreed existing reset/handoff policy. If necessary use a targeted reset generation/state check, not a global rejection of all jobs whenever any radius changes. Old rendered patches can remain during normal handoff; avoid permanent stale grass, duplicate draws or disappearance of unrelated patches. A shader-based runtime filter is not needed to solve this lifecycle issue.

One existing edge case to address in the integration review: `RemoveShield` removes tracked coverage and calls `UpdateShieldActiveState`, but does not itself call local `ResetGrass` when another active shield remains. If the normal radius-to-zero path does not already reset the affected area for a given removal, add a targeted reset of the old affected radius (not a blanket clear). Verify this with actual game lifecycle and overlapping/disjoint shields. This is a relevant potential stale-grass issue, not a confirmed runtime regression. Similarly, retain current config/season change and EWD rebuild behavior. Snapshot lifetime must be world-scoped; no old world references/data should be reused after world change.

### 7.4 Foreign patch detection and optional integration

The native Seasons `AddInstance` prefix must stay enabled. FarLands' `Patch_Build` bypasses it only for its own generated kinds; all native/unhandled clutter still requires it. Do not unpatch it globally just because FarLands is installed.

Modify `ForeignBuild` only when the agreed Seasons grass API is available and the equivalent filtering path is active. The current code evaluates patch owners. Prefer recognizing the exact known placement prefix (owner `shudnal.Seasons`, known method/target and compatible contract), not blanket-whitelisting every patch owned by Seasons or all future patches. All other unknown foreign patches must keep the original safe behavior. If the API is absent, incompatible, not ready or the background builder is disabled, retain the existing safe/native path. Account for the cached `_foreignChecked` decision: select integration state before the first check, or invalidate that decision if integration readiness actually changes. Do not re-enumerate Harmony patches on every frame or call.

### 7.5 Alternatives considered

| Approach | Decision and reason |
| --- | --- |
| Keep native placement for all grass whenever Seasons is installed | Existing safe fallback, but loses FarLands background generation and its CPU benefit. |
| Ignore the Seasons Harmony owner without porting the predicate | Rejected. Would allow ordinary seasonal grass under shields and protected copies elsewhere. |
| Call the current Seasons placement prefix from the worker | Rejected. Unsafe access to Unity objects, mutable caches and shared native state; locks or main-thread dispatch per blade would undermine performance. |
| Generate everything then filter on main thread or during GPU drawing | Rejected for this scope. Extra rows/upload/CPU or per-frame GPU work; would also have to handle disabled ordinary clutter, separate protected kinds and instance caps. |
| Replace all clones with a new per-instance material/scale system | Rejected. Changes existing placement semantics and rendering architecture unnecessarily. |
| Immutable geometry snapshot + worker-side rejection | Preferred. Small extension of existing Prepare/BuildJob/Build dataflow, preserves existing clone mechanics and native path, performs only bounded arithmetic near shields, skips impossible clone kinds in most cells. |

## 8. Shared API and ownership decisions

Proposed division of work:

| Seasons | FarLands |
| --- | --- |
| Public opt-in compatibility API and stable version/capability contract | Direct optional integration adapter; no new reflection for these two features |
| Exact autumn position-to-index function | Compute/store index at upload; preserve culling and draw grouping |
| Explicit base material and palette/transform data | Bind base and transform; update model/impostor shaders |
| Source texture mask generation, versioned storage and lazy loading | Project mask during bake, own atlas format/channel and rendering resources |
| Shield-copy classification, effective protection state, numeric snapshot | Capture at Prepare, filter in worker, handle job replacement and ForeignBuild |
| Cache and visual state invalidation for borrowed data | Hook into existing appearance/grass rebuild lifecycle |

Keep both mods independently usable. A direct compile-time API reference must not force Seasons to be installed for FarLands to start. Agree on a soft-dependency adapter or another established optional binding pattern that does not eagerly load/JIT the Seasons-dependent type when the plugin is absent. Seasons must not require FarLands or reference FarLands' internal classes. The precise target framework/assembly distribution needs verification (`FarLands` inspected assembly targets .NET Standard 2.1; the Seasons project targets .NET Framework 4.8). No mandatory new shared library is assumed. The API examples here are conceptual; agree on a small stable versioned contract and signatures before either side implements against them. Old versions without the contract retain the existing behavior; do not add broad compatibility layers for hypothetical future versions.

For appearance resources, explicitly agree how readiness/invalidation is signaled (existing `GameLooks` polling/version mechanism or one direct revision value/event). A cache rebuild or custom texture change must invalidate the base, mask and correction data together, even if a material reference happens to be unchanged. Prefer integrating with the existing refresh rather than adding an independent polling loop. Main-thread creation/disposal and cache-generation ownership must prevent use of destroyed textures. Shader and mask format changes should remain an implementation detail of FarLands; cache format remains an implementation detail of Seasons.

## 9. Acceptance criteria and validation plan

Static review first; runtime checks and profiling by the developers after implementation. No benchmark thresholds are claimed before measuring a baseline.

### Autumn colors

- In autumn, a forest containing eligible deciduous trees shows all four appearances where the existing noise selects them. Near and far representations of the same tree use the same index, with approximate color differences acceptable. Tile unloading/reloading and different camera positions do not change that index.
- Variant 3 is the baseline; no correction for mask 0. Bark, silhouette, opacity, lighting, shadows, normal/coverage and snow behavior remain valid. Check both the mesh-model and impostor paths and their transition. Verify alpha-tested overlapping leaves/trunks and distant mips for color fringes.
- Spring/summer/winter, disabled Seasons texture controls, noneligible vegetation, arbitrary custom texture overrides, absence of Seasons and the existing older integration behave as before. No new autumn variation work outside eligible autumn appearances.
- Season/config/cache reload and world unload/rejoin do not retain stale masks, palettes, materials or index buffers. New cache revision regenerates correctly, and users who never request masks do not load them into RAM/VRAM.
- Compare model/impostor draw counts and culling group counts with current FarLands, using the same tree types and settings. No additional color groups or color atlas sets. Record actual extra mask VRAM, buffer memory, bake time, CPU upload time and GPU time; compare steady state separately from baking and cache generation.

### Grass

- With no shield, compare placement against the existing FarLands worker on the same patch/settings; normal grass should be unchanged and protected clones should not generate. With Seasons absent, the worker and native fallback behave as before.
- With a shield, inspect a patch outside, inside, and crossing the shield; include the inner boundary band, steep ground, overlapping shields, seasonal normal grass disabled, different scale settings, and winter-only vs all-season protection. Compare against Seasons' native placement policy, not exact RNG output of the two independent generators.
- Confirm ordinary/copy filtering for every relevant GPU kind and native-only kinds. Confirm no worker-side Unity object access, shared mutable Seasons state, random-stream changes, or cap/bounds/count inconsistencies.
- Activate, fuel, expand, shrink and remove a shield while jobs are queued/running; check the case where another shield remains active. Verify patch resets and replacement, no persistent stale grass and no unnecessary global rebuilds on incremental radius changes. Check world change and season/config reload, and existing EWD clutter updates if available.
- Verify foreign patch detection is relaxed only for the implemented Seasons prefix and still falls back for unrelated unknown patches or unavailable integration. Record CPU main-thread and worker timings, patch throughput and allocations with no shields and with shields; outside shield areas should use the current fast path, with no repeated per-candidate shield scan or new rendering groups.

## 10. Questions for the FarLands developer / coding assistant

Please review these specific decisions against the real source and reply with evidence (file/symbol and, where useful, a short code excerpt or commit reference). A proposed change with a concrete reason is preferred over a generic approval.

| ID | Question / expected decision |
| --- | --- |
| T1 | Can the actual `GameLooks`/model/bake paths accept an explicit per-renderer-slot base appearance from Seasons without creating extra per-instance materials? What is the smallest data shape that fits your current material pipeline? |
| T2 | What channels are actually occupied in albedo, normal/coverage, snow or other existing atlas textures? Is there a safe existing channel and way to project the source mask in the bake, or should we agree on a separate R8 atlas and bake pass? Describe UV/alpha/depth and mip handling. |
| T3 | Which `GpuTree` representation is simplest for the 0–3 variant index, considering every GPU consumer and existing culling/layout? Can it stay at 32 bytes safely, or is an explicit field/buffer preferable? Confirm `InstanceBuffer.Put` is a suitable main-thread insertion point. |
| T4 | Which color transform parameter layout is easiest to share across model and impostor shaders? Is a tint/strength pair sufficient in your color space, or would a compact relative color transform be more suitable? |
| T5 | Can the new appearance API use the current `GameLooks.Version` invalidation and normal forest recreation for season/cache changes? How will masks and palette changes be included? |
| G1 | Confirm the `RefreshKinds` / `Prepare` / `Build` insertion points and the most natural immutable snapshot representation. Can we skip shield-copy kinds for nonintersecting patches without altering the existing per-kind index/seed? |
| G2 | Confirm the exact supported Seasons Harmony prefix exception in `ForeignBuild`. How should the optional API readiness and cached decision be handled without a per-frame scan? |
| G3 | Does the current `Current(job)`/`FreePatch` replacement path adequately handle jobs finishing after `ResetGrass` marks a patch, but before replacement? Identify the smallest needed adjustment, if any. |
| A1 | Agree on an optional, direct public API reference mechanism, version/capability checks, ownership and invalidation. Verify framework/assembly constraints. No hard dependency between the mods. |

### Review workflow

1. FarLands side: verify the source facts and answer T1–T5, G1–G3 and A1. Identify any blocked assumptions. Propose concrete revisions where necessary, preferably with actual signatures/data layouts and exact shader channel choices.
2. Seasons side: confirm or revise the API, mask cache ownership and geometry contract based on that feedback. Keep the agreed performance and visual scope; do not turn an open question into a requirement without agreement.
3. When both sides agree, consolidate the accepted decisions into the final design document with exact signatures, resource lifetimes, fallback behavior and the two implementation task lists. Mark the unresolved items closed or explicitly deferred. Use that document as the implementation baseline for both repositories.

**Suggested response format:** for each question, provide `Accept / Change / Blocked`, the source evidence, the proposed decision, and any required change on the other side. End with a proposed contract diff or updated Markdown and a list of remaining decisions. No need to implement code, change release versions, or run game tests until the contract is agreed.
