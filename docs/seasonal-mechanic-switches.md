# Independent seasonal mechanic switches

Base: `feature/winter-ocean-sliding` at `9d028f5f48ca0b7d82cc8c1650bd4ae95cf98bfa`.
This change includes the previously unapplied winter Pickable perishing switch.
The release version remains 1.10.6.

## Configuration

All switches are declared in the common `Seasons.ConfigInit()` and synchronized
with the server. All default to `true`, retaining the configured seasonal behavior.
The switches do not overwrite seasonal JSON, resource lists, tuning values or
existing configuration keys. Re-enabling uses those saved settings again.

| Section | Key | Disabled behavior |
| --- | --- | --- |
| Season | Pickables can perish in winter | Stop winter freezing/perishing of controlled mature Pickables. Growing Plants, crop growth, respawn rates and cultivation remain separate. |
| Season - Control | Enable tree regrowth | Do not schedule new regrowth; queued replanting checks this switch again before creating a sapling. Existing saplings and trees remain. |
| Season - Control | Control wood drops | Do not apply the seasonal wood-drop multiplier; keep the resource list. |
| Season - Control | Control meat drops | Do not apply the seasonal meat-drop multiplier; keep the resource list. |
| Season - Control | Control food consumption | Do not apply the separate seasonal food-duration drain multiplier. |
| Season - Control | Control stamina consumption | Do not apply the separate seasonal stamina-consumption multiplier. Other status effects, Summer Heat and other mods remain independent. |
| Season - Control | Control rested duration | Do not scale newly calculated Rested duration or comfort contribution. Already granted time is not rewritten. |
| Season - Control | Control fuel consumption | Leave fireplace, fueled cooking-station and hot-tub updates unscaled. Does not control torch durability or general smelter production. |
| Season - Control | Control beehive production | Leave honey production and bee activity/messages to the normal game path. Stored honey and production progress are not cleared. |
| Season - Control | Control sap collection | Leave sap-collector updates unscaled without clearing storage or progress. |
| Season - Control | Control livestock procreation | Leave the seasonal probability, pregnancy-duration and population-distance adjustments unapplied. Do not reset animals or pregnancies. |
| Season - Control | Control night length | Do not override day-fraction rescaling, morning times or the sleep destination. Day length in seconds is a separate setting. |
| Season - Control | Control wind intensity | Do not scale weather-derived wind strength. Seasonal weather selection is unaffected. |
| Season - Control | Enable torch warmth | Remove the seasonal torch heat and durability-drain overrides while preserving unrelated properties. |
| Season - Summer heat - Damage in red zone | Enable heat damage | Stop the heat damage timer and omit its health warning. Keep heat accumulation, zones, visuals and other modifiers. |

`Control stats` continues to control the season status effect, not all independent
mechanics above. Its description now makes that distinction explicit. No new
plant-growth or rain-protection switch is included in this change.

## Runtime and compatibility

Disabled means that Seasons does not apply the selected contribution. It does not
force another mod's values to vanilla constants. Effective seasonal multipliers
return their neutral value for existing consumers, including duration estimates;
patches that otherwise mutate temporary fields also bypass their work while off.
Finalizers still restore state captured by an invocation that began while enabled.

Configuration changes take effect through existing update/event paths. There are
no new frame loops, recurring jobs, scene-wide object searches or network messages.
Torch refreshes visit the game's existing character registry to include already
equipped remote-player and creature visuals before restoring the source prefab.
Weak-key records preserve the pre-Seasons durability and Heat values; unrelated
EffectArea flags are untouched, and different values written later are not forced
back to a hard-coded default. Independent writes of an identical value cannot be
distinguished from an inherited seasonal value.

Switching off Pickable perishing prevents the existing delayed coroutine from
removing a harvest while the switch is off. Saved freezing timestamps are cleared
on the next existing owner-side check or load. This is not a mass rewrite of
unloaded ZDOs, and very fast off/on changes are not a guaranteed timer reset.
See `winter-pickable-perishing.md` for the exact crop lifecycle boundaries.

Already lost harvests, consumed resources, granted Rested time and created trees
are not rolled back. Changing night control does not restart a sleep time-skip
that is already underway. Disabling a contribution does not disable a weather,
status effect or another mod that produces a similar outcome independently.

## Manual checks

- Disable each switch with deliberately non-neutral seasonal tuning; change the
  season and re-enable. Confirm that tuning and resource lists were preserved.
- Start freezing a mature Pickable and turn perishing off before its delayed
  removal. Its harvest must remain and its winter warning must disappear. Check
  that an immature Plant still follows the existing growth and health rules.
- Destroy a regrowth-eligible stump and turn regrowth off during the existing
  delay. No sapling should appear while disabled; existing saplings must remain.
- Check honey, sap and fuel operation plus duration displays, food/stamina use,
  new Rested calculations, wood/meat drops and animal breeding independently.
- Disable night control before sleeping. Check time rescaling, morning and sleep
  destination without changing the separate day-length setting. Wind control must
  not turn off wind or replace the selected weather.
- Disable heat damage while overheated: health ticks stop, but heat and other
  modifiers remain. The damage-health warning must no longer be shown.
- Toggle torch warmth with a torch equipped on both clients and with a GoblinTorch
  visual loaded. Check warmth, durability, re-equipping, season changes and world
  reload. Repeat with non-default torch durability and unrelated EffectArea flags.

Validation is limited to source/API review, diff inspection, configuration-key
checks, project XML and archive integrity. No mod build or runtime tests were run.
