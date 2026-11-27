# Seasons × FarLands / SteadyFrame: integration design

**Status:** Agreed design (revision 3, 2026-11-27). Implementation baseline for both sides; not a claim that the features have been implemented, tested or released.

**Goal:** Implement a small, performant, optional integration for autumn tree colors and protected grass without changing the existing draw grouping or native Seasons behavior. The GPU grass implementation is shared by FarLands and SteadyFrame; the tree integration is specific to FarLands.

**Coordination:** [Seasons issue #55](https://github.com/shudnal/Seasons/issues/55). Revision 3 consolidates the [FarLands technical review](https://github.com/shudnal/Seasons/issues/55#issuecomment-7218849170), [Seasons decisions](https://github.com/shudnal/Seasons/issues/55#issuecomment-7221072693), [FarLands final clarifications](https://github.com/shudnal/Seasons/issues/55#issuecomment-7223264121) and [Seasons acceptance](https://github.com/shudnal/Seasons/issues/55#issuecomment-7223353504). Both sides have closed the remaining contract questions. Resolve any implementation-level changes in the issue and keep this document in sync; FarLands will review revision 3 against its actual source.

## 1. Scope and verified baseline

Two independent improvements are agreed:

1. **Autumn tree colors:** distant deciduous trees should show the same spatial distribution of autumn color variants as nearby trees, with approximate colors acceptable. Preserve FarLands' GPU instancing and indirect rendering architecture.
2. **Protected grass:** allow FarLands' background grass generation to remain active with Seasons, while preserving the seasonal grass settings and shield protection behavior.

No new general-purpose rendering system, per-instance material creation, cross-mod dependency for unrelated features, or per-frame CPU scan is requested. Spring, summer and winter need no additional distant tree color variation. Existing snow, terrain, building, rock and Seasonality integrations must continue to work. Understorey color variation, FarLands persistent atlas caching, and the internal modernization of the Seasons texture cache format are outside the joint scope. Existing approximation in FarLands' placement and rendering algorithms is not a request to reproduce every detail of the game simulation.

### Source baseline

| Component | State inspected |
| --- | --- |
| Seasons | 1.11.0, [`64809f4ec3e7f735c5477497a404d47889e83984`](https://github.com/shudnal/Seasons/tree/64809f4ec3e7f735c5477497a404d47889e83984) (`master` after PR #52) |
| FarLands | Decompiled 1.5.2 assembly, informational version `1.5.2+c606f871c912653a00e4d6576a9035c832e13018`. Decompiled C# file SHA-256 `c31f70938beba74c66b67ac1efbad3eeb75f4d76789d579c72ac0d6936092a6a`. |
| Valheim | 1.0.30, [`216cb30f748e0ef9c74a8366395e9cc110f3cd8d`](https://github.com/shudnal/assemblies_combined/tree/216cb30f748e0ef9c74a8366395e9cc110f3cd8d) |

The initial Seasons-side FarLands analysis used the decompiled assembly, not its complete source repository. The FarLands developer subsequently checked the C# source and shaders against 1.5.2 and supplied the missing channel semantics, GPU reader audit and job lifecycle details in the technical review linked above. Statements attributed to that review are source evidence supplied by the FarLands side, not an independent Seasons-side shader audit. No builds, runtime tests or performance measurements have been performed for this integration. Source line references below refer to the inspected revisions, not necessarily the latest working branches.

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

This wrapper is the agreed implementation of the new API method; it does not exist in the baseline. `WorldToMapPoint` computes `(x / 12 + 1024) / 2048` and `(z / 12 + 1024) / 2048`. `GetNoise` uses the world's seed factor (`log10(abs(seed))`, or zero), two Perlin samples, frequency 10000, a divisor of 1.1, a power of 1.3, and rounding to increments of 0.05. In autumn the coordinates are `(mx, 1 - my)`:

```text
n = (Perlin(mx * 10000 + seedFactor, (1 - my) * 10000 - seedFactor)
   + 0.5 * Perlin(mx * 20000 - seedFactor, (1 - my) * 20000 + seedFactor)) / 1.1
factor = Round(Pow(n, 1.3) * 20) / 20
index = factor < 0.25 ? 0 : factor < 0.5 ? 1 : factor < 0.75 ? 2 : 3
```

Use the existing implementation for exact float/double arithmetic and rounding behavior. The result depends on position and world seed, not tree type, camera position, tile load order or time within autumn. It is not a uniform random distribution of four colors. The API should only be called after the Seasons controller and world are ready, on the main thread.

## 3. Autumn colors: current FarLands implementation

From the inspected assembly:

- `GameLooks.Materials` uses `SeasonsLink.Varies`, creates a renderer/LOD mirror of a prefab under an inactive holder at `(0, -5000, 0)`, and calls `SeasonsLink.Follow`. `SeasonsLink` uses reflection to access the Seasons controller, variant cache and related methods. It then returns the mirror renderer's shared materials.
- This supplies one seasonal appearance per prefab/renderer. Since the mirror has a fixed position, the actual positions of the distant trees are not used in the variant selection. This explains the uniform autumn colors.
- `GameLooks.Watch` / `Changed` detect changes in materials and textures; `TickForest` calls `StopForest` and recreates the far forest when `GameLooks.Version` changes. Use this existing lifecycle for the new appearance data rather than rebuilding the forest again through another independent mechanism.
- `TreeMeshes` / `MeshParts` / `ModelLayer` render the far model LODs. `ModelLayer` uses a copied material with the corresponding FarLands shader, and issues `Graphics.RenderMeshIndirect` per mesh part. The original source material is not a place to store per-tree values.
- `ImpostorBaker` uses 64 views (8 × 8). `Stage` prepares a model with its materials. A deferred camera renders each view, then command-buffer blits from the GBuffer through `ImpostorBake.shader` into `_tileAlbedo` and `_tileNormal`. `Step` copies tiles into the atlas; `EndType` generates mipmaps and reads normal atlas alpha for coverage. Some types also have a separate snow albedo atlas. `ImpostorDrawer` renders the impostor using indirect drawing.
- `InstanceBuffer.Put` writes `GpuTree` entries containing position, scale, yaw, type and two tilt values: eight floats, 32 bytes. `TreeTypes.Max` is 32; several culling/counting arrays and type masks are organized by tree type. `FarForest.Capacity` is 524288. Model and impostor paths share the tree instance data.

The FarLands source review confirmed that Put runs on the main thread (it calls ComputeBuffer.SetData), that the existing look-change lifecycle recreates the forest, and that all GPU readers of the type field use integer truncation. See section 4 for the agreed shader/layout details.

Relevant decompiled symbols/line anchors: `GameLooks` 21738+, `ImpostorBaker` 23066+, `ImpostorDrawer` 23658+, `ImpostorLayer` 23829+, `InstanceBuffer` 24006+, `ModelLayer` 25393+, `SeasonsLink` 27729+, `TreeMeshes` 28874+, `TreeTypes` 29097+, `TickForest` 31406+.

## 4. Agreed autumn integration

### 4.1 Requirements

- Use the existing Seasons autumn noise function once per relevant tree at instance-buffer upload. No noise calculation per frame or shader invocation.
- Use zero-based **variant 3 (the fourth variant)** as the generated base appearance for eligible distant forest trees in autumn. Variants 0–2 are approximate color corrections of that base; variant 3 is identity.
- Preserve the existing logical tree types, mesh parts, culling groups, draw commands and 32-byte instance layout. Do not duplicate color atlases, normal/coverage atlases or draw groups for the variants.
- Apply the new variation only to eligible autumn appearances. Outside autumn, keep the current mirror-position variant selection and the existing material path. Leave understorey, rocks, buildings and unrelated rendering paths unchanged. No extra autumn color work in other seasons.
- Match the configured overall color scheme, not individual replacement PNGs or exact source pixels. The eligible forest impostors use an existing atlas channel for the mask; no additional persistent impostor mask atlas.

### 4.2 Main-thread appearance API and existing mirrors

The final API signatures and capability binding rules are in section 8. Retain the existing GameLooks renderer/LOD mirrors and native Seasons material resolution. Both the model and bake paths already use GameLooks.Materials(prefab, renderer).

FarLands calls FollowWithVariant(controlledCopy, prefabName, 3) for the forest-tree mirror path intended for the new integration. The requested index is a persistent autumn-only override on that controlled copy:

- In autumn, indices 0–3 select the explicit variant. Outside autumn, the normal position-based variant selection is used.
- An index of -1 always uses the normal selection, including in autumn. FarLands uses -1 for understorey and other existing mirror consumers.
- Registration and repeated following must work with the existing mirror lifecycle; keep the override when an existing controller is refreshed or reinitialized. No re-registration on every season change is required. Do not change the selection for ordinary loaded game objects.
- Use the same cached renderer/material/texture binding, including LOD and material slot, as native Seasons. Do not infer the base or mask from the texture name or mirror position.
- TryGetRecolor(controlledRenderer, materialIndex, out mask, palette) returns the mask in the UV space of that slot's _MainTex, and the palette described in section 4.5. Use the actual effective palette associated with the generated texture/cache entry, including configured prefab/material overrides. A false result means no additional color correction for the slot: for example, no controlled texture, no valid mask, texture control disabled, not autumn, or data not ready. Preserve the existing appearance path for objects/types that cannot participate.

Seasons owns shared materials, generated textures and the returned source masks. FarLands owns the mirrors, copied materials, temporary bake resources, atlases and shader bindings it creates. Lookup/material construction is per appearance, not per tree. A special generated-only binding must not overwrite a shared material used by ordinary loaded trees or legacy consumers. Keep existing controller removal and legacy paths compatible; no new API methods are needed for unrelated features.

### 4.3 Source masks and CustomTextures

Seasons already selects the exact pixels that will be recolored. Build the mask from the existing pixelsToChange list during seasonal texture cache generation, before altering the original pixels:

```text
mask = byte[originalWidth * originalHeight]  // initially 0
for each pixelIndex in pixelsToChange:
    mask[pixelIndex] = 255
```

A single mask covers the four generated variants of this texture because they share the selected pixel indices. Values are 0 (unchanged) or 255 (recolor); filtering and mipmaps produce intermediate coverage. The mask is scalar linear data, not an sRGB color image. Respect source dimensions, UV orientation, wrap, filtering and the original alpha/cutout. Associate it with the resolved cache-local texture entry and material binding, not a globally unique name or a Unity instance ID from a different session. No separate per-season or per-variant mask is required.

**Agreed CustomTextures policy:** the new autumn forest path uses the generated variant 3 and its matching source mask, and the configured ColorVariant parameters for variants 0–2. Ignore PNG replacements when constructing this generated base/mask pair or choosing the new distant color corrections. In particular, the shipped Fall_3.png replacements (zero-based variant 2) for beech, birch and oak do not disable recoloring, and do not require a separate fitted palette. A replacement of Fall_4.png must not replace the generated base while retaining its old mask. Normal Seasons objects and existing appearance paths outside this integration continue using CustomTextures as before.

This deliberately accepts differences between near replacement textures and distant approximate colors. Do not analyze arbitrary user PNGs, introduce a separate color fitting system, or turn the presence of a custom texture into a reason to disable all four distant colors. If a concrete user configuration produces an undesirable discrepancy, Seasons can investigate the supplied settings and adjust the relevant configured palette separately.

Persist the source masks with the corresponding cache generation. Regenerate the cache when required mask data is absent or outdated. Provide reusable mask retrieval on the Seasons side, with lazy loading when a consumer requests a mask (not merely because FarLands is installed). Prefer a linear single-channel R8 texture with mipmaps if supported. Texture creation/upload is on the main thread. Share loaded masks for the current cache generation; release them on replacement/disposal according to section 4.6. Users who never request masks must not retain decoded mask textures or persistent mask payload arrays in RAM/VRAM.

The on-disk format, serialization, cache migration and other internal cache optimizations are outside the joint integration scope. They are implementation details owned by Seasons. FarLands depends only on the appearance API and resource lifetime, not a particular Seasons cache file format.

### 4.4 Project the mask during impostor baking

The FarLands developer verified the following shader channel layout against 1.5.2:

| Atlas | Format | RGB | Alpha |
| --- | --- | --- | --- |
| Albedo | ARGB32, sRGB | Albedo multiplied by coverage | Smoothness (GBuffer1.a) multiplied by coverage |
| AlbedoSnow | Same | Snow albedo multiplied by coverage | Smoothness multiplied by coverage |
| Normal | ARGB32, linear | Object-space normal multiplied by coverage | Coverage used for silhouettes and cutoffs |

Impostor.shader uses _Gloss >= 0 ? _Gloss : albedo.a. Forest trees use TreeMeshes.Smoothness = 0, so their Albedo.a is not consumed as smoothness. Understorey does use the stored smoothness. The agreed optimization is to put **mask × coverage in Albedo.a for eligible forest types only**. Do not change Normal.a, and do not change understorey smoothness or introduce an R8 impostor atlas for it. Albedo alpha is linear even though the RGB channels are sRGB. No extra persistent impostor atlas is needed.

The source mask is in original material UV space; the existing post-GBuffer blit has lost those UVs. Therefore render one additional mask view for each of the 64 base views, only while an eligible autumn appearance needs recoloring:

1. Reuse the same geometry, camera/view, projection, UV transforms, alpha cutout, depth test and occlusion as the base bake.
2. For each participating slot, create a temporary copy of its bake material. Pack the source mask into RGB and the base _MainTex alpha into A in a temporary texture, using the original dimensions, wrap, filtering and mip handling. Use correct linear data sampling/encoding so the sampled mask values are preserved. Set _Color.rgb to 1, keep _Color.a and _Cutoff, and disable _MossAlpha, _MossBlend, _AddSnow and _AddRain for the mask pass.
3. For non-recolorable slots of the same model, render zero RGB while preserving their alpha, depth and cutout behavior. Do not omit the geometry: opaque bark must occlude leaves behind it. The verified mask material path is Custom/Vegetation. A forest type whose slots cannot be rendered correctly by that path remains outside the new recolor path.
4. The extra pass renders the mask to GBuffer0.r. Copy that value multiplied by coverage into the matching Albedo tile alpha with ColorMask A, preserving RGB from the original base view. Retain the existing snow bake and normal/coverage atlas.
5. Assemble the same tile coordinates and generate mipmaps at EndType. Release temporary textures/materials after baking the type; they do not become an additional permanent set of color atlases.

In the impostor shader, use the existing view weights normalized by coverage to reconstruct the mask just as for premultiplied albedo; for example, the normalized weighted sum of Albedo.a from the selected views. Read the mask before the snow RGB mix. Do not use the repurposed alpha as smoothness or opacity for these types. The existing normal/coverage cutoff remains the authority for silhouette. Verify wind/animation and cutout consistency between the two renders, edge/mip filtering, color space and bake cost in-game; equal silhouettes and absence of color fringes are acceptance criteria, not measured facts yet.

For the intermediate mesh-model path, bind the original mask to the copied material and sample it in the source UV space. It needs no mask projection. Use the same palette and instance index as the impostor path. Per-material source mask textures, temporary mask bake textures and the extra bake passes have a cost; unchanged persistent impostor atlas size does not mean zero overall memory or GPU overhead.

### 4.5 GPU index and color correction

Compute the index with VegetationApi.GetAutumnVariant(worldPosition) at InstanceBuffer.Put, on the main thread, only for eligible types with autumn recoloring active. Use the actual TreeInstance X/Z position, not a tile center. FarLands' placement remains approximate relative to native trees; equal world positions and seeds produce the same index. Do not persist the index in the placement cache; the existing GameLooks.Version change rebuilds the instance buffers. Outside the eligible autumn path, no new noise calculation is needed.

The source review confirmed that GpuTree uses eight floats, including both tilt fields, and that ForestCull.compute and Canopy.compute truncate t.type with (uint)t.type. The other existing shader paths do not need a fractional type value. Use the existing float type field as follows:

```text
q = 0                    // identity, noneligible, or outside autumn
q = variant + 1          // only eligible autumn variants 0, 1, 2
packedType = treeType + q / 4.0
```

Variant 3 is identity, not q=4. For the existing type range below 32, quarter increments are exactly representable in float. Integer truncation yields the original tree type, so the existing culling groups, type masks, indirect draws and 32-byte stride remain unchanged. Preserve both tilt fields. Document this encoding in FarTree.cginc and audit all CPU/GPU consumers affected by the new code; do not extend the packing to unrelated fields.

Decode q in the vertex stage (uint)(frac(t.type) * 4 + 0.5) and pass it with nointerpolation to the fragment stage. q=0 bypasses the correction. Otherwise select palette index q-1. The caller supplies Vector4[8], and Seasons fills it with the actual configured palette for the resolved source texture:

| Entries | Components |
| --- | --- |
| palette[0..3] | (target.r, target.g, target.b, targetProportion) |
| palette[4..7] | (useColor, reduceOriginalColorToGrayscale, restoreLuminance, 0) |

Boolean fields are encoded as 0 or 1. Color components are the values used by Seasons MergeColors, not precomputed RGB multipliers. Variant 3's entries can be returned with the other variants, but no shader correction is applied for q=0.

Apply the existing MergeColors operation approximately to the generated variant 3 albedo: optional grayscale conversion, blend toward the configured target, and optional restoration of the base HSL lightness. Use the following closed-form restoration rather than a full RGB/HSL/RGB round trip:

```hlsl
float3 SetLightness(float3 c, float L0)
{
    float L = (max(c.r, max(c.g, c.b)) + min(c.r, min(c.g, c.b))) * 0.5;
    return L0 + (c - L) * (1 - abs(2 * L0 - 1))
           / max(1 - abs(2 * L - 1), 1e-4);
}
```

Conceptually, for q != 0 and a nonzero mask and useColor flag:

```text
original = base albedo in MergeColors color space
source = grayscale(original) if configured, otherwise original
corrected = lerp(source, target.rgb, targetProportion)
if restoreLuminance: corrected = SetLightness(corrected, HslLightness(original))
result = lerp(original, corrected, mask)
```

Preserve base alpha, normal, lighting and geometry. Perform the correction before lighting and convert between the shader color space and the Seasons texture-value space where needed. Check actual project color space and texture/material values during implementation; the baked albedo also contains material tint and moss, so exact numerical equivalence is not a requirement. Preserve the existing snow mix and keep new color work out of other seasons.

For the model path, the palette is shared per mesh/material part. For the impostor path, it is shared per type using the existing property block (SetVectorArray); if multiple slots have different recolor palettes, use the slot with the largest masked coverage as agreed by the FarLands side. This is an intentional approximation, not a request for more per-pixel palette indices. No per-instance palette data is needed.

Applying the original blend to an already blended base leaves a residual term, approximately (1 - t_i) * t_3 times the difference between the original and the base target before lightness restoration. For the default palette the factor is 0.05–0.06. It is not a measured final color error. **Do not introduce a threshold on this factor**, suppress variants for high values, or fit a relative transform to replacement PNGs. The configured palette is authoritative for the approximate distant colors.

### 4.6 Appearance changes and resource lifetime

Use AppearanceRevision() and IsAppearanceUpdating() from the public API rather than querying private state in the new path.

- AppearanceRevision is a cheap session-local change token. Change it when an effective seasonal appearance, palette, relevant settings or resource generation changes, including replacing a cache with another instance of the same disk revision and relevant custom texture changes. Do not calculate a settings hash or scan textures on each getter call.
- IsAppearanceUpdating exposes the existing texturesVariants.IsUpdating state. Keep the legacy member for old FarLands. The new path must not treat a partially rebuilt base/mask/palette as a valid appearance. Advance the change token before disposing previously issued resources and ensure that completion of an update publishes the coherent new resources; consumers must not lose the pending invalidation by observing a revision while IsAppearanceUpdating is true.
- FarLands checks the change token in the existing approximately 2-second GameLooks.Changed check; reuse GameLooks.Version and forest recreation rather than a separate refresh loop. Retain the existing per-frame destroyed-object check and include masks. Before baking each type, check the borrowed mask and materials for Unity-null/destroyed state; abort that bake and use the ordinary look invalidation path if required resources are gone.
- Seasons owns the borrowed objects and releases obsolete ones on cache replacement/disposal. FarLands never destroys those objects; it releases its own references, bindings and bake resources through its existing lifecycle. No separate reference-counting or event system is required. Ensure no further use of a destroyed resource, including during incremental baking; a black default mask for an unbound model mask property leaves that appearance unchanged.
- FarLands' disk cache currently stores tree placement, not baked atlases. Do not add a persistent atlas cache as part of this design. If FarLands chooses to implement one separately, it needs a deterministic key including effective appearance inputs, geometry, bake parameters and formats, rather than the session-local AppearanceRevision. Internal disk cache implementation is not part of this API.

### 4.7 Alternatives considered

| Approach | Decision and reason |
| --- | --- |
| Four render groups/materials per type | Rejected. Up to four times as many applicable draw groups; risks type limits and culling complexity. This is not a fourfold FPS prediction. |
| Four complete albedo atlas layers | Rejected. Preserves grouping only with a different sampling layout and multiplies atlas/bake cost; exact color is not needed. |
| Compute a new hash/noise in a shader | Rejected. Existing Seasons noise is available at upload and gives matching spatial distribution; no repeated GPU noise computation. |
| Detect foliage in every fragment | Rejected. Repeated classification cost, approximate bark/leaf selection after recoloring/filtering. |
| Infer mask from recolored albedo at bake time | Rejected in favor of exact source masks; existing Seasons pixel selection includes material/position rules that an albedo heuristic loses. |
| Separate R8 impostor atlas | Not needed for the agreed forest types. Albedo.a is available; separate atlases/sampling or changing understorey smoothness would add cost for no required feature. |
| Expand GpuTree or add a parallel buffer | Not needed. The verified fractional type encoding keeps the 32-byte layout and logical type grouping. |
| Fit colors to every replacement PNG or disable variants by error threshold | Rejected. Approximate configured palette is sufficient, and suppressing variants can make near/far differences more noticeable. |
| Existing mirrors + generated base + exact source mask projected into Albedo.a + existing noise and cheap shader correction | Agreed. Preserves the rendering architecture and atlas count while moving classification to cache generation and mask projection to baking. |

The performance trade-off is limited cache data, source mask resources and extra bake work versus no additional persistent impostor atlases, color groups or instance bytes. Measure actual upload/bake time, CPU/GPU cost and memory before release.

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

In the inspected FarLands `GpuGrass` (shared code also compiled into SteadyFrame, as confirmed by the FarLands developer):

- `ForeignBuild(ClutterSystem)` caches a decision for a `ClutterSystem` instance. It inspects Harmony owners on `ClutterSystem.GenerateVegPatch` and `InstanceRenderer.AddInstance(Vector3, Quaternion, float)` / `AddInstance(Matrix4x4)`. It disables background grass generation for unknown foreign patches (with existing exceptions for FarLands and known idle patches). The Seasons positional prefix is one of these foreign patches. GPU rendering/upload of native-created grass is a separate path and can still work. Do not equate this conflict with the entire FarLands grass renderer being disabled.
- `RefreshKinds` reads enabled, instanced clutter with a supported `Custom/Grass` material, extracts placement properties into `BuildKind`, and retains the original clutter list index. It reads scale, amount/quality, biome/forest/altitude/vegetation/clearing/tilt/water/random offset and other existing placement settings. These are already compatible with Seasons' normal property changes, subject to normal rebuild timing.
- `Patch_Build.Prefix` temporarily disables those supported entries so the native generator does not generate them too. Its postfix queues a `BuildJob`, and its void finalizer restores enabled entries. Unhandled clutter is still generated by the game. Keep this split and the existing index-based RNG.
- `PumpBuild` calls `Prepare` on the main thread, which snapshots terrain maps, then enqueues the job for `WorkerLoop` on a background thread. `Build` creates final positions and transforms using the copied terrain/placement data and deterministic `BuildRandom`. `Apply` uploads only a job accepted by `Current(job)` (the owner is still present in `Built` and refers to that exact job).
- `Build` consumes the random height offset and scale before writing `_buildRows`, `_buildGround`, bounds and counts. The per-kind accepted instance cap is 1023. This is the natural insertion point for a placement predicate.
- `Freed`, `BuiltCells`, `BuiltPatch`, `Current`, `ClearBuilt` and `Patch_FreePatch` manage replaced, reset and freed jobs. `ClutterSystem.ResetGrass` marks intersecting patches for replacement; it does not synchronously rebuild every patch. Preserve replacement and handoff behavior.

Relevant decompiled anchors: `BuildKind` 1331+, `BuildJob` 1396+, `Patch_Build` 1489+, `ForeignBuild` 3160+, `PumpBuild` 3218+, `Current`/`Apply` 3274+, `Freed` 3419+, `RefreshKinds` 3499+, `Prepare` 3575+, `WorkerLoop` 3659+, `Build` 3781+ (final position/scale and rows near 3960+). These anchors are for orientation; use the current actual source for changes.

## 7. Agreed grass integration

### 7.1 Main-thread API and numeric snapshot

The grass API consists of IsShieldedClutter, GetGrassProtection and ReplacesPlacementPatch (exact signatures in section 8). Do not execute live Seasons or game shield checks in the worker.

IsShieldedClutter(clutter) resolves the registered Seasons shield-copy identity; FarLands must not parse the suffix. Evaluate during RefreshKinds for each relevant BuildKind, using the existing kind lifecycle/cache keyed by the clutter reference, not per grass candidate.

GetGrassProtection(Vector3 patchCenter, float halfSize, List<Vector4> outer, List<Vector4> inner) fills the caller-owned reusable lists with relevant spheres and returns Active. Each sphere is (center.x, center.y, center.z, radiusSquared). The lists represent separate unions:

- outer: valid native shield/dome centers with current positive m_radius, when the native IsShieldedPosition test is applicable.
- inner: valid tracked Seasons shield centers with positive (trackedRadius - 2).
- Only include spheres that can intersect the XZ square of the patch. Compare the minimum squared XZ distance to the square with the squared radius; the actual position test later uses all three coordinates. Do not square a nonpositive radius. Keep both sets even where they overlap; do not require outer and inner hits to belong to the same shield.

The lists are outputs for the current call, not accumulated state. Active is the current native placement gate, isAnyShieldActive. In the normal inactive case return false and empty lists; protected clones are normally disabled, so the native prefix allows all candidates. If Active is true but the effective per-position outer predicate is disabled (e.g. SeasonState.IsActive is false), return true with empty lists: ordinary instances are allowed, shield copies are rejected. Preserve the existing protection configuration, including winter-only and ignored-center gating for active areas, rather than introducing a new per-candidate ignored-biome test.

Prepare executes on the main thread. It obtains this data into reusable lists, then gives the job its own stable numeric data for the intersecting spheres. No Unity Object, Transform, live shield list, shared mutable Seasons dictionary, ConfigEntry or calls into world generation may be captured by the worker. Use ordinary squared-distance arithmetic. Reuse the existing job/buffer lifetime; avoid adding per-candidate callbacks, locks, allocations or a spatial index.

### 7.2 Placement in the existing pipeline (G1)

```text
Main thread: RefreshKinds
  classify each relevant kind as ordinary or Seasons shield copy
Main thread: Prepare
  obtain protection snapshot for this job; store numeric data
Worker: Build
  if Active and no outer spheres: skip shield-copy kinds
  run existing placement and RNG for other kinds
  after final position and scale RNG, before the 1023 cap and output writes:
    reject placements that do not pass the agreed geometric filter
Main thread: Apply
  use existing Current(job) and patch handoff
```

The worker predicate is:

```csharp
if (!active)
    return true;
bool insideOuter = ContainsAnyStrict(outer, position); // squared XYZ
if (!insideOuter)
    return !isShieldCopy;
return isShieldCopy && ContainsAnyStrict(inner, position);
```

ContainsAnyStrict compares squared XYZ distance < radiusSquared for any sphere in the list. Use the final world position including height and random offset. For ordinary candidates only the outer test is needed; for protected candidates both are needed. With no active protection there is no per-candidate shield test. With active protection but no intersecting outer sphere, omit protected-copy kinds and keep the ordinary fast path unchanged. Do not optimize fully contained patches at the cost of vertical bounds handling.

Filter after all position/scale random draws, before rows, count, ground data, bounds and the accepted-instance limit. The current generator seeds each kind from the original clutter index. Skipping an entire impossible shield-copy kind is safe (its independent seed and zero count do not affect other kinds); never renumber kinds. Rejecting a candidate must not skip random draws that the existing algorithm would consume. Do not convert an ordinary instance into a protected copy: originals and copies can have different enabled/scale/amount settings and separate seeds. The goal is equivalent conditional placement, not identical blade positions between native and FarLands generators.

### 7.3 Shield changes and job handoff (G3)

Seasons already calls ResetGrass(position, affectedRadius + 1) when relevant shield visual radius changes. SetShieldData updates tracked radii and invalidates caches; visual refresh is throttled (first radius, change of at least 3 m from last visual radius, or target reached). UpdateShieldActiveState calls ClearAll only when the global clone enablement changes. Keep this policy, existing config/season/EWD rebuild handling and world-scoped job lifetime. No whole-world reset for every radius step.

The FarLands source review confirmed the existing replacement mechanism: ResetGrass marks patches for rebuild; the next eligible Update generates the replacement patch, installs the new job in BuiltCells, and frees the old patch. Freed transfers the old rendered patch or its replacement reference to the new job, while Current(job) rejects superseded/freed jobs. A result finishing before the marked patch is actually replaced can remain visible during the normal handoff until the new job applies; the native path also retains old grass while terrain is not ready. Accept this transient behavior. No new global generation counter, reset version check or shader filter is required for G3. Verify that there is no permanent stale or duplicate grass after handoff.

On the Seasons side, review RemoveShield when another shield remains active: it removes tracked coverage and calls UpdateShieldActiveState, but has no explicit local grass reset in the inspected code. If the normal radius-to-zero lifecycle does not already refresh the affected patch, reset only the old affected area via ClutterSystem.ResetGrass(center, radius). This is a potential existing native-path issue, not a confirmed runtime regression. The existing FarLands path already handles ResetGrass, so no additional cross-mod invalidation API is required.

### 7.4 ForeignBuild, capability loss and optional integration (G2)

Keep the Seasons native positional AddInstance prefix enabled for native/unhandled clutter, legacy versions and fallback. When the complete version-compatible grass capability is bound, ForeignBuild may exempt the exact replacement patch identified by Seasons:

- Require the patch owner shudnal.Seasons and a true result from ReplacesPlacementPatch(MethodInfo patchMethod). Seasons recognizes its own InstanceRenderer_AddInstance_PreventInProtectedArea.Prefix, not other patches. Do not hard-code its internal class name in FarLands or whitelist all Seasons patches.
- All other unknown foreign patches retain the existing conservative/native behavior. If the API is absent, incomplete, incompatible or the worker is disabled, do not enable this exception. Cache the decision on ClutterSystem as before. No separate grass readiness flag is required; the snapshot is correct once the capability is bound, including inactive/no-shield states.
- **An API failure is not an inactive protection result.** A successful GetGrassProtection returning false is a valid state. If IsShieldedClutter or GetGrassProtection throws or yields an invalid result, do not substitute a successful false return from the delegate wrapper. Disable the grass capability for the rest of the session, invalidate the cached ForeignBuild decision and return to native generation through the existing ResetGrass/worker-disable path. Do not publish a result generated without the required filter; keep the native prefix active. A failed patch-identity probe must not exempt the patch. Do not attempt repeated automatic recovery or per-frame rebinding.

GpuGrass is shared source compiled into FarLands and SteadyFrame. Each copy binds the neutral Seasons API itself. Update the shared GpuGrass version used by its existing highest-version selection, so the new compatible worker wins when both mods are installed. Do not create a second active worker. See section 8.2 for the mixed-version case.

### 7.5 Alternatives considered

| Approach | Decision and reason |
| --- | --- |
| Keep native placement for all grass whenever Seasons is installed | Existing safe fallback, but loses the CPU benefit of background generation. |
| Ignore the Seasons Harmony owner without porting the predicate | Rejected. Breaks normal/clone placement under and outside shields. |
| Call Seasons placement checks directly from the worker | Rejected. Unsafe access to Unity objects, mutable caches and native state; locks or main-thread dispatch per blade undermine performance. |
| Generate everything then filter on main thread or during GPU drawing | Rejected. Extra generation/rows/upload or per-frame GPU work; complicates disabled kinds and instance caps. |
| Replace clones with per-instance material/scale logic | Rejected. Unnecessary change of existing placement semantics and rendering architecture. |
| Numeric snapshot + worker rejection and no-overlap skip | Agreed. Small extension of the existing Prepare/BuildJob/Build dataflow, preserves clone mechanics and native fallback, does bounded arithmetic near shields only. |

## 8. Shared API v1 and ownership

### 8.1 Exact agreed public contract

Expose one public static class Seasons.VegetationApi in the Seasons plugin assembly. The following is the signature contract (declarations for documentation, not a compilable implementation):

```csharp
namespace Seasons
{
    public static class VegetationApi
    {
        public static int ApiVersion(); // 1

        // Grass; main thread.
        public static bool IsShieldedClutter(ClutterSystem.Clutter clutter);
        public static bool GetGrassProtection(
            Vector3 patchCenter, float halfSize,
            List<Vector4> outer, List<Vector4> inner);
        public static bool ReplacesPlacementPatch(MethodInfo patchMethod);

        // Autumn appearance; main thread.
        public static bool FollowWithVariant(
            GameObject controlledCopy, string prefabName, int variant);
        public static bool TryGetRecolor(
            MeshRenderer controlledRenderer, int materialIndex,
            out Texture mask, Vector4[] palette);
        public static int GetAutumnVariant(Vector3 worldPosition);
        public static int AppearanceRevision();
        public static bool IsAppearanceUpdating();
    }
}
```

All Unity types are from UnityEngine, List is System.Collections.Generic.List and MethodInfo is System.Reflection.MethodInfo. The signature contains only BCL, Unity and game types, no Seasons-specific enums, structures or interface dependencies. ApiVersion returns 1 for this contract. The method bodies, not the signature sketch above, will be implemented in the Seasons PR.

| Method | Contract |
| --- | --- |
| ApiVersion | Capability version, not the plugin release number or a resource revision. |
| IsShieldedClutter | True only for a registered Seasons shield-copy kind; false is a valid ordinary kind. |
| GetGrassProtection | Fills current intersecting outer/inner spheres in caller-owned lists, returns the Active gate. See section 7.1; false is a successful inactive state, not an error code. |
| ReplacesPlacementPatch | True only for the exact Seasons placement prefix replaced by the equivalent worker filter. |
| FollowWithVariant | Follow a controlled mirror using the existing mapping; explicit 0–3 index only in autumn, -1 normal selection in all seasons. Return false if the controller cannot follow that object. Repeated calls use the existing controller lifecycle. |
| TryGetRecolor | For the controlled renderer/material slot, return a valid borrowed Texture and fill caller-provided Vector4[8] with the configured palette. False means no new correction; do not consume its outputs in that case. No extra correction outside autumn, while updating, or without an eligible generated base/mask. |
| GetAutumnVariant | Original noise function, returns 0–3. Call only for relevant world positions once the Seasons appearance is ready; no per-frame evaluation. |
| AppearanceRevision | Cheap session-local token changed on effective look/resource changes (section 4.6). |
| IsAppearanceUpdating | Existing texturesVariants.IsUpdating state exposed as a public method. |

Optional binding follows FarLands/SteadyFrame's existing VcpApi pattern: locate the class and public static methods by name and exact signatures once, create delegates, and invoke those delegates. No compile-time reference to Seasons or a mandatory shared DLL. This avoids requiring a Seasons assembly or its net48 target when the .NET Standard 2.1 consumer is running without it. Keep independent capabilities; enable each only if ApiVersion is at least the required version and every member of that set is bound:

- Grass: IsShieldedClutter, GetGrassProtection, ReplacesPlacementPatch.
- Autumn trees: FollowWithVariant, TryGetRecolor, GetAutumnVariant, AppearanceRevision, IsAppearanceUpdating.

No repeated reflection on trees, grass candidates or frames. No blanket catch-to-default for grass API calls after enabling the replacement path; use the failure contract in section 7.4. The agreed new public API is additive. Existing legacy reflection paths for terrain, snow, buildings, materials and cleanup remain available. Any future additive API version must retain existing signatures/semantics for existing consumers.

### 8.2 Mixed-version compatibility

The agreed compatibility baseline is Seasons 1.11.0 and FarLands 1.5.2, plus the combination with the updated shared grass worker in SteadyFrame. Preserve behavior when only one mod is updated; do not backport the new features into older releases or promise compatibility with arbitrary future internal changes.

| Combination | Expected behavior |
| --- | --- |
| New Seasons + new FarLands | Both new features enabled independently when their corresponding API capabilities are available. |
| New Seasons + FarLands 1.5.2 | Existing SeasonsLink appearance (one position-selected variant per mirror). Native grass fallback through ForeignBuild and the Seasons prefix. |
| Seasons 1.11.0 + new FarLands | New API unavailable; retain the existing SeasonsLink appearance and native grass fallback. |
| FarLands 1.5.2 + new SteadyFrame + new Seasons | New shared grass integration through SteadyFrame's newer worker; distant forest colors retain the old FarLands behavior. |
| Only Seasons installed | Native Seasons appearance and grass placement unchanged. No masks loaded solely because the API exists. |
| Only FarLands and/or SteadyFrame installed | Existing behavior, no Seasons API delegates active. |

The FarLands developer listed the following legacy reflection-facing members used by SeasonsLink 1.5.2. Preserve their names, signatures, field/property types and behavior for the supported legacy paths:

| Type | Members |
| --- | --- |
| Seasons.Seasons | texturesVariants (field; its type has controllers field and IsUpdating property), seasonState and controlEnvironments (fields), IsIgnoredPosition(Vector3) |
| Seasons.SeasonState | IsActive (property), GetCurrentSeason() |
| Seasons.SeasonalSnow | Enabled (property), GetSnowBuildupRange(), GetReducedSnowBuildupRange(), ReducedSnowBuildupPrefabNames (field), GetCumulativeSnowGainAt(Heightmap.Biome, double) |
| Seasons.PrefabVariantController | instance (property), AddControllerTo(GameObject, bool, ZNetView, WearNTear, string, MeshRenderer), RemoveController(GameObject) |
| Seasons.Heightmap_GetBiomeColor_TerrainColor | overrideColor (field) |

Retain the existing old adapter rather than creating a separate set of compatibility shims. When no compatible public capability is available, the optional delegate path is not used. Continue to allow missing members in the older adapter to produce its existing safe fallback and warning. Preserve the native Seasons placement prefix in all versions. Do not change the runtime object model used by the legacy adapter as part of the mask/cache work.

The FarLands side estimates the mixed-version support to be small (one optional delegate adapter, existing mirror path selection and exact patch exemption). This remains to be verified in the mixed-version tests; the matrix describes expected, not yet tested, behavior.

### 8.3 Division of ownership

| Seasons | FarLands / shared GpuGrass in SteadyFrame |
| --- | --- |
| Public versioned API and exact position-to-index function | Optional delegate binding, feature-set selection, index packing and shader decoding |
| Existing controlled mirror mapping, autumn-only explicit variant, generated base and matching configured palette | Keep existing GameLooks lifecycle, use the explicit forest-tree path, preserve other mirrors |
| Source texture masks, persistence, lazy loading, ownership and appearance state | Project masks into forest Albedo.a, bind model masks and palettes, own copied resources and atlas lifetime |
| Shield-copy classification, current numeric geometry snapshot, exact patch identity | Capture in Prepare, filter in Build, preserve native fallback, handle API failures and worker selection |
| Preserve existing native/legacy behavior; investigate local shield removal reset | Preserve existing job handoff, handle ResetGrass, test both old/new version paths |

No requirement on either mod to change its public release version until the implementations are ready and release coordination is explicitly agreed.

## 9. Acceptance criteria and validation plan

Static review first, then runtime tests and profiling by the developers on the two implementation branches. Record the exact mod commits, game version, settings and relevant logs. The design has no measured benchmark threshold yet.

### Autumn colors

- In autumn, eligible distant forest trees show the configured four-color distribution where the existing noise selects each index. Near and far representations of equal world positions use the same index; approximate colors and differences from CustomTextures replacements are expected. Tile unload/reload and camera position do not alter the index.
- Variant 3 is identity; mask 0 or useColor false leaves base albedo unchanged. Check beech, birch and oak, including the shipped Fall_3.png replacements. Changing the configured autumn palette changes the distant correction; arbitrary base replacement PNGs do not replace the generated base/mask pair. Ordinary Seasons and old integration behavior is unchanged.
- Check model/impostor transitions, shader color space, HSL lightness, bark, alpha cutout, overlapping leaves/trunks, coverage-normalized mips and silhouette. Verify wind consistency between bake passes and preservation of existing snow/shadow/lighting behavior. No recolor of unrelated types.
- Spring/summer/winter, disabled texture controls, understorey and ineligible types retain existing appearance. No noise/recolor work for the new feature outside eligible autumn appearances.
- On season/settings/cache reload (including rebuilding with the same cache key) and world unload/rejoin, verify coherent base, mask, palette and indices. During incremental baking, invalidated/destroyed resources are not used; the normal look refresh restores the new appearance. No persistent mask payloads or textures loaded in a client that never requests them.
- Compare indirect draw counts, culling group counts, GpuTree stride, and permanent impostor atlas count/size with the baseline under the same settings: no new color groups, 32-byte stride and unchanged atlas count for eligible forest trees. Measure bake time, temporary and source mask memory, upload time and steady-state GPU cost separately. Confirm reasonable performance for the added correction; do not claim all VRAM or GPU timings are unchanged.

### Grass

- With no shield, compare placement against the existing FarLands worker on the same patch/settings; normal grass remains unchanged and shield copies do not generate. With active protection but no outer overlap, the worker skips impossible copy kinds and ordinary grass uses the existing fast path. Without Seasons the worker and native fallback behave as before.
- Check patches outside, inside and crossing shields, the inner boundary band, steep ground, overlapping spheres, normal grass disabled, differing scales, and winter-only vs all-season protection. Compare both ordinary and protected candidates with the native placement policy, not exact RNG output between different generators.
- Confirm filtering occurs after all RNG draws and before the accepted cap, rows/bounds/ground/count writes; no kind renumbering. Verify native-only clutter still passes through the native prefix. No Unity object access or mutable shared Seasons state in worker jobs.
- Activate, fuel, expand, shrink and remove shields while jobs are queued/running, including removal while another shield remains. Check the existing handoff and any needed local ResetGrass call; no permanently stale grass, duplicate draws or unnecessary global rebuilds on incremental radius changes. Check world change, season/config reload and existing EWD clutter updates where available.
- Verify ForeignBuild exempts only the exact implemented patch and retains native fallback for unknown foreign patches, absent/incomplete/incompatible API. Force an API failure separately from a successful inactive snapshot: grass capability is disabled for that session and native placement is restored. Check the shared-worker selection when both consumers are installed.
- Record main-thread and worker timings, allocations and patch throughput with no shields and with shields, compared to the existing native fallback and FarLands background generation. No extra rendering groups, repeated per-candidate shield scans or worker cross-mod callbacks outside the necessary geometric tests.

### Mixed versions

Verify every combination in section 8.2: startup/logs, seasonal appearance at distance, expected grass generation path and correct native shield protection. In the old FarLands case the existing reflection-facing members must remain available, and new FarLands with old or absent Seasons must not fail binding or startup. Include the older FarLands + newer SteadyFrame combination, verify only the selected shared worker runs, and inspect the existing grass fallback warning when applicable.

These checks are a joint test plan, not completed runtime evidence.

## 10. Implementation tasks and coordination

### Seasons

- Add Seasons.VegetationApi v1 with the exact signatures and independent feature contracts in section 8. Keep the existing legacy reflection-facing members.
- Expose the original autumn index function; add the autumn-only explicit variant for the existing controlled mirror lifecycle, keeping normal objects and other seasons unchanged.
- Generate source masks from pixelsToChange, persist them with the relevant cache generation and load them lazily. Resolve the actual generated base/mask pair and effective configured palette for TryGetRecolor; the new autumn path ignores replacement PNGs as agreed.
- Expose AppearanceRevision and IsAppearanceUpdating, keep coherent resources across refresh/rebuild and dispose of old resources safely.
- Expose shield-copy classification, numeric outer/inner snapshot and exact native patch identity. Keep the native prefix. Review RemoveShield and add local ResetGrass only if the existing path does not already refresh the affected area.
- Perform focused static checks on API signatures, palette/mask mapping, cache/resource lifetime, protection geometry and legacy members. No unrelated cache format modernization or release work is required by this joint design.

### FarLands and shared GpuGrass

- Bind VegetationApi once using the existing optional delegate pattern. Validate independent capability sets, retain legacy SeasonsLink, and implement the agreed grass failure fallback.
- In the shared grass code (GpuGrassBuild.cs), add ShieldCopy classification in RefreshKinds, collect the snapshot in Prepare, skip impossible kinds and filter before the cap in Build. Exempt the exact Seasons prefix in ForeignBuild only when the grass capability is active. Preserve job handoff and native-only kinds; update the shared worker version for FarLands/SteadyFrame selection.
- In GameLooks, use the autumn override for forest tree mirrors and normal selection for all other mirrors. Share base/mask/palette lookup between the bake and model paths. Add the public revision/updating checks to the existing lifecycle; check masks and materials before baking and in the existing destroyed-resource check.
- In ImpostorBaker / ImpostorBake.shader, bake the temporary mask pass into Albedo.a for eligible forest types only, retaining the existing atlas sizes, coverage, mipmaps, snow and smoothness behavior. Keep understorey unchanged.
- In InstanceBuffer.Put / FarTree.cginc, encode q in the fractional part of the existing type field. In Impostor.shader / ModelVegetation.shader / FarModel.cginc, decode q, read the mask and configured palette, apply the agreed approximate correction. Keep draw grouping and stride.
- Perform focused static review of GPU layout/consumers, shader channel use, resource checks, worker snapshot/fallback and legacy adapter. Validate color space, bake alignment and actual performance in the joint runtime checks.

### Coordination and release gate

The architecture and API v1 were agreed in issue #55 (T1–T5, G1–G3 and A1–A2, including the final IsAppearanceUpdating and autumn-only FollowWithVariant decisions). No additional architectural approval is required to begin the implementation. The FarLands developer will compare revision 3 with the actual source/shaders and report any discrepancies in the issue. Resolve and document implementation-level changes before making incompatible contract changes.

1. Seasons implements in a dedicated branch and opens a PR linked to #55; FarLands implements in its own branch/PR and links the corresponding work. Share exact refs/commits so the FarLands developer can build the Seasons branch and test both changes before merging.
2. Review and test both implementations against section 9, including mixed versions and SteadyFrame. Fix confirmed defects in the branches. Report actual checks and measurements rather than treating static design review as game verification.
3. Once both developers agree that the implementations work together and the compatibility matrix is acceptable, coordinate the merges and new releases. Do not raise release versions or publish either mod merely because this document or one implementation is ready.

Internal Seasons cache serialization/storage optimization, persistent FarLands atlas caching, understorey variation, and support for arbitrary custom replacement texture matching are not joint implementation tasks. If a new requirement emerges from actual tests, discuss it separately in #55.
