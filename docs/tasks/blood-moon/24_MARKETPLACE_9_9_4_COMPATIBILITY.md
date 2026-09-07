# Marketplace 9.9.4 compatibility checkpoint

## Scope

This checkpoint supersedes the Marketplace-version evidence in `23_PR42_RUNTIME_HARDENING_AND_CLOCK_POLICY.md` where only Marketplace 9.8.9 decompilation was available.

The owner supplied the Hexium consolidated decompilation for:

```text
KG-Marketplace_And_Server_NPCs_Revamped 9.9.4
```

The relevant readable type is:

```text
Marketplace.Modules.TerritorySystem.TerritorySystem_Main_Client
```

## Confirmed 9.9.4 contract

Marketplace 9.9.4 contains:

```csharp
private static Color[] originalMapColors = null;
private static Color[] originalHeightColors = null;
private static async void DoMapMagic();
```

Its own Harmony postfixes for both `Minimap.LoadMapData` and `Minimap.GenerateWorldMap` do this before calling `DoMapMagic()`:

```csharp
originalMapColors = __instance.m_mapTexture.GetPixels();
originalHeightColors = __instance.m_heightTexture.GetPixels();
DoMapMagic();
```

`DoMapMagic()` only checks `originalMapColors` for null before entering the body, but then immediately allocates from both arrays, including:

```csharp
Color[] heightColors = new Color[originalHeightColors.Length];
```

Therefore setting only `originalMapColors` and invoking `DoMapMagic()` can produce a `NullReferenceException` when `originalHeightColors` has not yet been initialized.

## Seasons correction

`Compatibility/MarketplaceCompat.cs` now mirrors the external contract:

- require the current generated/readable minimap;
- resolve a writable static `originalMapColors` field;
- when `originalHeightColors` exists, require it to be a writable supported color-array field too;
- read the current map texture into the expected array type;
- read the current height texture into the expected array type;
- set **both** Marketplace baselines before invoking `DoMapMagic()`;
- preserve the legacy compatibility path when an older Marketplace API has no `originalHeightColors` field;
- keep transient failures retryable instead of permanently disabling compatibility.

The correction commit is:

```text
ed83ec2814f6d1575757add4cac36c680a340500
```

## Validation boundary

This is source-level compatibility verification against the supplied 9.9.4 decompilation. It is not a claim of runtime validation. Owner-side test should cover:

1. load a world with Marketplace 9.9.4 and territory map drawing enabled;
2. allow Marketplace to generate its normal territory overlay;
3. cross a season boundary or otherwise force Seasons minimap recoloring;
4. verify no `MarketplaceCompat.UpdateMap` exception;
5. verify territory overlay remains visible on the recolored map;
6. repeat after `Minimap.GenerateWorldMap` / map regeneration;
7. repeat entering and leaving winter and with seasonal map control disabled/re-enabled.
