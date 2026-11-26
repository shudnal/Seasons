# Expand World Data environment reload API

Reviewed upstream source: EWD 1.83.0, `JereKuusela/valheim-expand_world_data`
at `38fd723739075727861b2cf65b5bb0bb7d39c694`.

- `ExpandWorldData/environment/EnvironmentManager.cs`
- `ExpandWorldData/biome/BiomeManager.cs`
- `ExpandWorldData/data/SyncedDataManager.cs`
- `ExpandWorldData/data/YamlDataManager.cs`
- `ExpandWorldData/data/DataManager.cs`

The current manager types are `ExpandWorldData.Environment.EnvironmentManager`,
`ExpandWorldData.Biome.BiomeManager` and
`ExpandWorldData.Data.SyncedDataManager`. The environment setter now returns
`SetResult` and may rewrite files before retrying. Seasons hooks the private
static `void Apply(List<EnvSetup>)` instead. This method publishes environment
data only after parsing and backfill have succeeded, or when restoring defaults.

The existing reflection resolver also retains the supported older
`Set(Dictionary<string, string>)` and `Set(string)` signatures returning bool or
void, and their legacy type names and initialization gates. Exactly one reload
postfix is selected. Missing optional members do not produce AccessTools lookup
warnings. Unsupported publication methods or initialization gates produce a
single explicit compatibility warning.

The initialization and world-reset guards remain unchanged. During initial world
loading, Seasons waits for EWD to initialize its managers, apply pending client
sync, and load the server data. Subsequent successful environment publications
reapply seasonal environments. Failed parsing and intermediate file rewrites do
not run the current publication hook.

`BiomeManager.SetupBiomeEnvs(List<BiomeEnvSetup>)` is still the point at which EWD
publishes authoritative biome environment entries. Preserve these entries so EWD
can filter them by its global-key rules using the original EnvEntry identities.
Seasons applies its seasonal rules after EWD's filter. Custom biome names are
resolved from the current BiomeToDisplayName dictionary.

EWD's clutter manager also replaces the live clutter entries after a successful
load. Seasons retains the unscaled defaults by entry identity and restores removed
entries before forgetting them. New entries receive their own defaults and the
current seasonal settings. Failed loads leave the tracked entries unchanged.
EWD already requests a clutter rebuild; Seasons does not clear patches a second
time for the same publication. EWD's default entries share the game's mutable
clutter objects. The ToData postfix writes the original enabled flag and scales
to the YAML data object, so backfill cannot save seasonal multipliers as defaults.
The live clutter entries are not changed during serialization.

Manual checks: initial world entry, successful environment-file reload, malformed
YAML, migration/backfill-triggered reload, restoring defaults, biome environment
rules and custom biome names, data synchronization to a second client, and
leaving/re-entering a world. Reload custom clutter size/enabled values during winter,
restore defaults, and reload malformed clutter YAML. Check a supported older EWD
version as well.

Source inspection only; no mod build or runtime tests were performed.
