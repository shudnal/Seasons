# 1.6.6
* fixed an ice floes being left after winter is ended
* ice floes will no longer spawns again during one season (you can clear the water and it will stay for a whole winter)
* there should be no occasional double spawns of ice floes
* ice floes position, size and rotation will be different between different seasons
* ice floes will more differentiate in sizes and should overlap less

# 1.6.5
* improved compatibility with EWD and EWS
* added direct incompatibility with LongerDays
* tweaks for ice floes: generation, interaction and mass

# 1.6.4
* current day and season recalculation on day length change

# 1.6.3
* proper current day calculation on initialization when custom day length used

# 1.6.2
* proper world edge handling (no more walking on the air)
* KG Marketplace territories compatibility (no more map resetting)
* mod description updated

# 1.6.1
* fixed bogwitch custom trader item list

# 1.6.0
* internal structural clean up
* seasonal state calculation made more sensitive to time skips and jumps
* fixed rare collision of season and day initialization on start up

# 1.5.3
* fixed issue with white grass inside shield dome

# 1.5.2
* fix of minor issue with occasional NRE possibly preventing season day change
* new config value to override season day
* improvements to grass in dome feature

# 1.5.1
* grass inside shield dome at winter
* supported plants from Herbalist and CookingAdditions

# 1.5.0
* major FPS optimization
* fish should now stays below ice
* floating containers (e.g. cargo crates) can now be placed above ice surface (config disabled by default)
* torch as fire source now should be properly disabled in mountains

# 1.4.5
* ServerSync updated

# 1.4.4
* patch 0.220.3
* little fixes here and there

# 1.4.3
* terrain recoloring throttled a bit when shield generator radius is changing rapidly
* vines on a wall will be more consistent in its seasonal color
* grass will change colors every 2 days instead of every day
* new config option (Getting Wet in winter causes Cold) to get Cold status along Wet status in winter if not protected with mead
* freezing while swimming option in winter will ignore cloth but respect frost resistance mead
* more precise Pickable respawn calculations
* new config option for hover text with respawn time for Pickable
* new visual hover type for plants, pickables, beehives: Bar
* more clear hover info on pickables which is winter resistant or protected by fire or vulnerable to cold

# 1.4.2
* EpicLoot Serpent bounty made no longer obtainable when water is frozen to prevent infinite loop
* changes to cache settings to ignore certain redundant objects
* season calculation throttled to improve performance a bit

# 1.4.1
* Custom Textures fixed
* Eyescream can be used to get rid of overheat debuff in Summer
* Torch will no longer protects from mountains and deep north freezing
* You will not get overheat debuff in cold environment in Summer

# 1.4.0
* bog witch

# 1.3.16
* option to set current day as global key

# 1.3.15
* heat from fire protects crops and pickables from winter perish
* fixed visuals for ship frozen under water
* new test option to make frozen Karve inventory available after water freeze
* fish positioning in frozen ocean finally fixed

# 1.3.14
* fix for season timer blinking text
* fix for clutter error on destroy

# 1.3.13
* bloom properly disabled in Winter every time after applying graphic settings

# 1.3.12
* clutter control compatibility with Expand World Data
* option to reduce snow storm particles amount in Winter

# 1.3.11
* proper protection from freezing death in Winter
* minimal hp of freezing protection is 5
* minor bulletproof for incorrect prefab variant initialization
* option in status effect text to see both current day and time to season end

# 1.3.10
* time to season end calculation refined

# 1.3.9
* fixed timer string for last portion of last day of a season

# 1.3.8
* shield generator affects terrain color

# 1.3.7
* fix for undying winter crops and crops to control growth

<details>
<summary><b>Changelog History</b> (<i>click to expand</i>)</summary>

# 1.3.6
* potential fix for environments (weather) synchronization

# 1.3.5
* better support for HD variants of Custom Textures

# 1.3.4
* option to automatically disable Bloom in Winter (it will not change Graphics setting, only disables posteffect)

# 1.3.3
* a little bit of bulletproof for custom clutters

# 1.3.2
* a little bit of bulletproof for textures

# 1.3.1
* a little bit of bulletproof for clutter system
* control trader config now works properly
* season and day change stability improved

# 1.3.0
* custom music support for use in seasonal environments
* environments sync fix

# 1.2.8
* shield generator protects from Winter only by default
* fixed terminal issue for server_devcommands "Auto exec" feature

# 1.2.7
* shield generator as green house is disableable feature now

# 1.2.6
* season day sync fix
* shield generator works as green house negating seasonal effects

# 1.2.5
* more stable and responsive synchronization of files/values
* fix for season change visual day 1 issue

# 1.2.4
* more stable season state synchronization
* full support for skiptime command in both directions

# 1.2.3
* map compatibility with Expand World Size

# 1.2.2
* better compatibility with Expand World Data, Expand World Size and Structure Tweaks

# 1.2.1
* custom biome settings (minimap winter colors and terrain seasonal ground colors)
* seasonal items fix
* Swamp-Mistlands border fix

# 1.2.0
* new seasonal clutters (flowers for spring)
* seasonal clutter control
* custom textures support

# 1.1.13
* environment related console spam on season change fixed
* Plains-Swamp border temporary fix (could be disabled)

# 1.1.12
* grass control will be applied immediately after config change

# 1.1.11
* water state update on Ashlands enter/exit
* fish placed under the ice correctly

# 1.1.10
* Ashlands
* grass control extended and moved to distinct JSON file
* tree regrowth chance

# 1.1.9
* patch 0.217.46

# 1.1.8
* fix for day luminance control

# 1.1.7
* default colors adjustment
* default support for roof pieces from MissingPieces, FineWoodBuildPieces, Balrond ElvenRoof, Balrond Shipyard, OdinArchitect, MoreGates

# 1.1.6
* fix for wood and meat drop
* added configurable wood and meat list to control drop

# 1.1.5
* minor fix for pickable spawn on dedicated server

# 1.1.4
* Expand World Data compatibility
* minor refinements here and there

# 1.1.3
* Ice floes climb fix for large scale floes
* Resetseasonscache command returned

# 1.1.2
* plants to control growth configurable list
* configurable ice floes scale
* freezing death with exactly 1 hp fix

# 1.1.1
* Cache settings force synced from server to clients
* Cache moved to \BepInEx\cache
* Cache rebuild caused by console command will be done in background (but in main thread which cause noticeable lag, that's ok)
* Cache settings extended with bushes colors category
* Cache will be stored by revisions (which depends on cache settings)
* Default recoloring refinements

# 1.1.0
* Recoloring settings (you can now control the way mod generates textures)
* rebuild cache console command ("resetseasonscache")
* default recoloring refinements
* setting seasonal key at day change
* fix for control stats couldn't be disabled completely
* fix for ice floes floating in the air

# 1.0.13
* you can climb on ice floe

# 1.0.12
* option to hide buff info in Raven menu 
* option to set amount of ice floes in winter
* option for crops and pickables to perish in winter 
* option to make selected crops not to perish in winter
* proper coloring of raspberry bushes (delete Cache folder to recreate the cache)
* proper pickable and plants growth control 
* proper recoloring freshly planted pickables and crops
* seasonal changes properly ignores objects located in interior, Mountains, DeepNorth and Ashlands


# 1.0.11
* hide grass in winter enabled by default (now you can set period in days)
* day length in seconds config option
* GammaOfNightLights better compatibility
* minor frozen ship refinements

# 1.0.9
* dedicated server ice floes fix

# 1.0.8
* remade config setting for frozen water surface (now you can set period)
* added ice floes spawning in ocean in set period of days
* you can now build on ice (frozen water surface)
* added option to hide grass in winter
* fixed unmatched spawn biomes in random event
* added custom skills support for custom buff settings (added by Jotunn or SkillsManager)
* added localization for "All" skills in Raven menu

# 1.0.7
* major optimization

# 1.0.6
* water and terrain control rework
* refinements and optimizations
* time skip refinements
* removed incompatibility with LongerDays

# 1.0.5
* optional global key setting
* timer to the end of current season in Raven menu
* total day in season counter in Raven menu
* fish pushed above frozen surface fix
* fixed season sync in multiplayer
* fixed occasional bug leading to long sleep skip between seasons
* fixed rescaled time calculation

# 1.0.4
* optional placing ships above the ice surface
* more stable freezing ships in the ice surface
* season change on sleep when loading screen is up
* status effect info always shown in Raven menu
* day number in status effect info in Raven menu
* optional replacement of seasonal status effect icons
* optional world dependent realtime season calculation settings
* placing new objects while frozen water surface fix

# 1.0.3
* fixed typo in traders list synchronization from server

# 1.0.2
* water surface will be frozen after set day of winter (and become slippery)
* beehive, maypole, xmastree recolor (delete Cache folder to get new textures)
* traders now have customizable seasonal items to sell
* option to change seasons only when sleeping
* added incompatibiliy TastyChickenLegs.LongerDays (leads to unpredictable behaviour, have similar option)
* more stable season change
* refinements and fixes

# 1.0.1
* Running on dedicated server fixed
* Freezing while smimming in winter
* Prevent using bed with torch as a firesource
* thunderstore package restructured

# 1.0.0
 * Initial Release

</details>