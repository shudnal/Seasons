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

<details>
<summary><b>Changelog History</b> (<i>click to expand</i>)</summary>

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