# Ice-floe JSON settings

Base: `eec493e596d5231378ae7c07208865c62f7fa3f2`, PR #45,
`perf/snow-performance`. Date: 2026-09-26.

This is the current configuration contract. It replaces the separate-cfg
instructions and the previously introduced split between local and synchronized
fields of the floe JSON. It does not change the accepted movement model.

## Main config

The three basic controls remain in `BepInEx/config/shudnal.Seasons.cfg`:

```ini
[Season - Winter ocean]
Enable ice floes in winter = true
Fill the water with ice floes at given days from to = {"x":4.0,"y":10.0}
Health of ice floes = 20
```

They retain server control and the existing enable/day callbacks. `[Test] / Log
ice floes` is a local diagnostic switch. Amount and scale are not main-config
entries. The retired `shudnal.Seasons.IceFloes.cfg` is not read or recreated;
startup removes that exact old file without migrating any values.

## One JSON synchronization path

```text
BepInEx/config/shudnal.Seasons/Seasonal ice floes.json
BepInEx/config/shudnal.Seasons/Default settings/Seasonal ice floes.json
```

The second path is the generated reference template. Copy it one directory up
and edit that copy; reference export does not overwrite the override.

The lifecycle is identical to `seasonalSnowJSON` and the other custom values:

```text
shared JSON watcher or initial read
  -> GetSyncedValueToAssign
  -> AssignValueSafeAndNotify (initial) / AssignValueSafeIfChanged (changes)
  -> seasonalIceFloesJSON.ValueChanged
  -> SeasonalIceFloeSettings.ApplySynchronizedSettings
  -> all runtime fields
```

`ReadConfigs` and `ReadConfigFile` contain no ice-floe filename checks, parsing,
local application, early-return exceptions, or custom rename handling. The only
floe filename comparison is the ordinary filename-to-CustomSyncedValue mapping,
next to the snow mapping. File read failures, deletion, rename, initial loading
and server synchronization follow the common implementation.

There is one effective JSON configuration. The `client` object is retained as a
field group, not a synchronization exception: its fields now come from the same
effective `seasonalIceFloesJSON.Value` as all other fields. A server payload
therefore also controls prediction/background options, distant bob and fallback
participation. Local watcher events cannot independently apply this section
before the synchronized value. No parallel local snapshot or dedicated Reload
entry point remains.

Parsing and numeric normalization happen in the ValueChanged handler. Invalid
JSON is logged and leaves the previous applied settings in place, like snow
settings; it is not specially intercepted by the file watcher. Missing fields
use shipped defaults, and an empty effective payload restores defaults. Removing
an override follows the common empty-payload path. No second ConfigFile, watcher,
thread, queue, networking layer, or configuration migration is added.

`SeasonalIceFloeSettings.ApplyConfiguredRuntimeValues()` can reapply the last
effective configuration after temporary inspector edits. It does not read files,
write a pose or bypass synchronization. The existing inspector runtime fields
remain transient diagnostics, not another persistent settings source.

## Defaults and placement

```json
{
  "amountPerZone": { "min": 10, "max": 15 },
  "scale": { "min": 1.25, "max": 2.5 }
}
```

Amount specifies placement attempts per zone, not a global cap. Amount, scale
and base health apply to newly generated floes, not existing objects. To
regenerate a population, disable and then re-enable floes during eligible winter
days. Keep the existing full default example for the remaining fields.

Two placement blockers were corrected during this review:

1. After terrain, biome, altitude and ocean-depth eligibility had already passed,
   placement still called `Floating.GetLiquidLevel` solely as a readiness gate.
   Missing local water volumes returned an invalid height and deferred the same
   candidate indefinitely. This contradicts the accepted fixed Ocean sampler.
   That gate is removed; placement still validates terrain and Ocean eligibility
   and uses the world's sea level. Other objects' water sampling is unchanged.
2. A completed zone was cached both in its ZDO marker and in the local `settled`
   set. A subsequently cleared server marker could remain hidden by that local
   cache. Completed zones now use the existing marker as their sole completion
   cache; the local set remains only for the existing local stop conditions.
   Marked zones still do not spawn duplicates, and valid markers are not erased.

These are source-confirmed blocking paths, not a runtime attribution of the
maintainer's report to either one. No log or live placement state was supplied.
The existing immediate cleanup, placement budgets and biome/depth exclusions
remain in place. Frozen ocean and out-of-period winter days still intentionally
prevent spawning.

For an on-demand main-thread placement observation use:

```csharp
Seasons.SeasonalIceFloes.GetPlacementStatus()
```

It reports connection/server role, enable/season/day/frozen state, initialized
water state and world edge, prefab availability, amount/scale and placement
queue sizes. It does not run or reset placement and adds no per-frame logging.

## Verification boundary

Input source hashes were matched against the current branch, including the
previously delivered archive now committed as eec493e. Static checks cover the
small diff, balanced C# syntax structure, unified JSON routing, runtime-field
coverage, removal of the spawn water-query gate and accidental Cyrillic text.
No mod build, game execution, physics simulation or automated mod tests were
performed. Runtime spawning and network settings application still require
maintainer confirmation. No numerical physics, kinematic, worker, publication,
version, package or dependency changes are included.
