# Ice-floe JSON settings

Date: 2026-09-26. Base: `76c4f9ae6cedca515c064fa24cc0fcfcb25a81c2`,
PR #45, `perf/snow-performance`.

This is the current configuration contract. It supersedes the separate-cfg
instructions in `floe-runtime-config-and-overhead.md` and the configuration
section of `floe-background-forecast.md`. It does not change the motion model.

## Main config

The three basic controls remain in `BepInEx/config/shudnal.Seasons.cfg` under
their original section and key names, with these defaults:

```ini
[Season - Winter ocean]
Enable ice floes in winter = true
Fill the water with ice floes at given days from to = {"x":4.0,"y":10.0}
Health of ice floes = 20
```

They retain server control and the existing enable/day change callbacks.
`[Test] / Log ice floes` also returns to the main cfg as a local diagnostic
switch. Amount and scale are no longer bound in the main config.

`shudnal.Seasons.IceFloes.cfg` is no longer created, loaded, synchronized or
watched. Startup deletes that exact obsolete file without reading its values.
Failure to delete it is logged, but it remains unused. Only obsolete amount and
scale keys are removed from the main cfg; unrelated keys are not removed.
There is no migration from the retired file to either the main cfg or JSON.
Existing values already stored under the restored main-config keys remain valid.

## JSON override and default template

This follows the other Seasons JSON settings:

```text
Editable override:
BepInEx/config/shudnal.Seasons/Seasonal ice floes.json

Generated reference template:
BepInEx/config/shudnal.Seasons/Default settings/Seasonal ice floes.json
```

World initialization exports the complete template with the shipped defaults.
Do not edit the reference copy: it is regenerated. Copy it one directory up to
create an override, or create a smaller JSON object containing only the fields
you need. The editable override is never overwritten by default export.
An absent or empty override uses defaults. Missing properties and null sections
also retain defaults. Deleting or renaming the override away restores defaults.

For example:

```json
{
  "amountPerZone": { "min": 10, "max": 15 },
  "scale": { "min": 1.25, "max": 2.5 }
}
```

Amount is the random number of placement attempts per zone, not a global cap.
Existing exclusions can reduce the resulting count. Amount, scale and base
health affect subsequent spawning only. To regenerate the same ocean, disable
floes with the main-config switch, then re-enable during eligible winter days.
The immediate cleanup algorithm is unchanged.

The complete example is in `examples/Seasonal ice floes.json`. JSON property
names use lower camel case. Unknown properties or invalid JSON are rejected
with a warning; previously applied settings are retained. Numeric inputs are
normalized to the same finite ranges as the prior cfg controls. Each successful
load starts from defaults, not from the previously loaded override, so removing
a property restores its default rather than retaining a hidden old value.

## Shared and local settings

The existing JSON watcher, main-thread dispatch and ConditionalConfigSync
custom-value path are reused. There is no second ConfigFile, dedicated file
watcher, per-frame config polling or new synchronization protocol.

Top-level `amountPerZone`, `scale`, `surface`, `buoyancy`, `tilt`, `motion` and
`authority` are shared settings. Their effective payload follows the same
server synchronization as the other Seasons JSON files.

The `client` section is applied only from this machine's local JSON file.
Receiving a server JSON does not replace it. It preserves the previous local
prediction quality, background enable/reserve, distant bob and fallback
participation settings. The server's copy of this section is not an instruction
for other clients. It is valid to supply only `client` locally while using the
server's shared settings.

New JSON values update the shared runtime fields on the main thread, without
resetting object poses or accessing live objects from the forecast worker.
Config reload does not directly restart that worker; its existing input and
mode checks continue to govern the next forecast/motion call.

Manual main-thread inspector actions remain available:

```csharp
Seasons.SeasonalIceFloeSettings.Reload();
Seasons.SeasonalIceFloeSettings.ApplyConfiguredRuntimeValues();
```

The first reads the override through the shared JSON loader. The second
reapplies the last effective shared JSON plus local client settings after
transient inspector experiments. Neither writes a cfg or resets a body pose.
`IceFloeClimb.ResetSurfacePhysicsSettings()` remains a separate diagnostic action
restoring its compiled baseline, not a persistent JSON editor.

## Scope and verification

The accepted defaults are unchanged: amount 10..15, scale 1.25..2.5, 70-percent
collider waterline, 0.2-second kinematic publication interval and ten-second
background reserve. All former tuning fields are represented in the JSON model.

Wave formulas, physics/kinematics, ownership election, network pose handling,
forecast threading and cleanup were not changed. Worker shutdown was moved from
the deleted cfg-watcher cleanup to the plugin's existing OnDestroy path.

Validation is limited to exact source/blob checks, source/diff review, syntax
structure, JSON example structure, runtime-field/default mapping and an
accidental-Cyrillic scan. No mod build, automated mod tests, Valheim run or
performance measurement was performed. Runtime JSON reload and multiplayer
synchronization still need maintainer verification.
