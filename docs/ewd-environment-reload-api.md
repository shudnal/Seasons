# Expand World Data environment reload API

The old hook assumed `EnvironmentManager.Set(string)` and an `Initialized` field
on the same type. Current EWD source has a private
`bool Set(Dictionary<string, string> files)` and shares its private static
`Initialized` gate through `ExpandWorldData.SyncedDataManager`.

Reviewed upstream source: `JereKuusela/valheim-expand_world_data` at
`32005ab84d769699e568fccacbf605b89232424b`:

- `ExpandWorldData/data/EnvironmentManager.cs`
- `ExpandWorldData/data/SyncedDataManager.cs`
- `ExpandWorldData/data/DataManager.cs`

The compatibility resolver probes the exact dictionary and legacy string
signatures with ordinary reflection. Optional-member probing does not emit the
misleading HarmonyX warning for the absent legacy signature. It accepts only
static, non-generic methods returning `bool` or `void`, and resolves the boolean
initialization field in the legacy or shared manager.

Exactly one of two return-type-specific Harmony classes is enabled. Boolean
setters only request seasonal reapplication after a successful result. Parsing
failures and migration retries therefore do not reapply seasonal data. Legacy
void setters retain their existing completion hook. Original arguments, return
values, EWD synchronization, world reset and biome-list hooks are not replaced.

The existing Seasons world-readiness and active-state checks still apply. No
reapplication is introduced inside EWD initialization. Unsupported API shapes
produce one explicit Seasons warning and omit only the reload hook instead of
patching an arbitrary method with the same name or suppressing Harmony logging.

Manual checks: initial world entry, successful environment-file reload, malformed
YAML, migration-triggered reload, data synchronization to a second client and
leaving/re-entering a world. Check both a current dictionary API and a legacy
string API where available. The reporting user's exact EWD version was not given;
the change is selected by runtime member shape, not a guessed version number.

Source inspection only; no mod build or runtime tests were performed.
