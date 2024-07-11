# Seasons
![](https://staticdelivery.nexusmods.com/mods/3667/images/headers/2654_1705107480.jpg)

Four customizable seasons.

## If you feel impact on fps.

If you do then probably you GPU can do better. Try setting launch options
`-gfx-enable-gfx-jobs -gfx-enable-native-gfx-jobs`
in Valheim general settings at Steam.

It unlocks more GPU power available to the game which could help.
It's more handy than editing **boot.config** file. It won't harm at least.

If you use RenderLimits mod and have "Distance area" setting more than 10 it may cause noticable fps loss at tree heavy areas. It couldn't be optimized further using only tools available for mods.

## What can be customized in different seasons
* environments. Add new weathers, replace currents weathers properties, remove weather.
* random events. Make some events appear more often or be removed completely, change biome restrictions.
* lightings. Control luminance, fog density on every time of day and indoors.
* stats. Control different stats and multipliers like regeneration modifiers, stamina usage, skill levels or raise skill speed, damage modifiers and so.
* seasonal settings like days in season, night length, related production and other multipliers
* trader items. Adds some items targeting potential lack of gatherable items in corresponding (prices may be high but he's an entrepreneur after all)

## Configurating the mod
The mod have general settings done by usual config through bepinex.

### Custom settings
Custom seasonal settings are done by creating/changing JSON files.

The mod creates a directory "shudnal.Seasons" at bepinex/config folder. There is the storage of cache, default and custom settings.

### Default settings
On every launch the mod is generating "Default settings" folder at config folder. There is storage of files with mod's default values to be applied.

Mod creates **\BepInEx\config\shudnal.Seasons\Default settings** directory on every world load. So you need to launch a world at least once after mod was installed.

## Texture recoloring
The mods comes without built-in textures and generate them on the first launch.
You can change cache settings as you pleased if you don't like the defaults.
Cache settings will be synced from the server on log in.

The textures and rules for applying them to objects are stored at \BepInEx\cache\shudnal.Seasons folder.
By default cache is saved as nonhumanreadable binary file for further faster loads. It can also be saved as JSON and PNG files.

The rules for cache forming are stored at "Cache settings" folder in config folder.
The list of the objects comes from prefab, clutter and locations list after ZoneSystem.Start. Due to procedural generation of objects textures if the game have custom assets loaded at that moment they will be tried to recolored the same way as the default game's assets.

You can replace generated textures with your own ones.

The main idea of the mod is only to change vanilla colors without changing other parts of object appearance.

[Recoloring settings](https://valheim.thunderstore.io/package/shudnal/Seasons/wiki/1483-recoloring-settings/)

## Seasonal settings
Basic seasonal settings are located in JSON files: Spring.json, Summer.json, Fall.json, Winter.json.
Files with default settings are located in "Default settings" folder. Mod creates **\BepInEx\config\shudnal.Seasons\Default settings** directory on every world load. So you need to launch a world at least once after mod was installed.

To start making custom changes you should copy the corresponding file to config folder "shudnal.Seasons". When you change the file there should be "[Info   :   Seasons] Settings updated: Season_Name" line in the bepinex console and LogOutput.log file.

Every property not represented in the custom file will fall to default value from "Default settings" seasonal file.
It means you may left only changed values at custom file to make it more meaningful.

### Settings descriptions

* daysInSeason (integer, 10) - season length in days, change of this value will recalculate current day and season with immediate effect
* nightLength (integer, 30) - the fraction of the day which is gonna be night. Default 30 is vanilla which means 30% of day length will be night time. Change of the value will immediately take effect on sun/moon position and corresponding values
* torchAsFiresource (bool) - torch become a warmth source with a price of extra durability drain
* torchDurabilityDrain (float) - vanilla drain is 0.0333 and warm torch are worn at x3 speed of 0.1f
* plantsGrowthMultiplier (float) - multiplier of speed which plants and pickables(consumable ones) are growing
* beehiveProductionMultiplier (float) - multiplier or beehive production speed
* foodDrainMultiplier (float) - multiplier of food drain speed
* staminaDrainMultiplier (float) - works on every action that uses stamina
* fireplaceDrainMultiplier (float) - multiplier of how much woods are used in fireplaces, oven and bath
* sapCollectingSpeedMultiplier (float) - multiplier of sap collection speed (sap level restoration is intact)
* rainProtection (bool) - makes building pieces not damaged by weather
* woodFromTreesMultiplier (float) - multiplier of the maximum count of possible wood drops from trees and bushes
* windIntensityMultiplier (float) - multiplier of wind force and intensity (affect boats and windmill)
* restedBuffDurationMultiplier (float) - multiplier of rested buff duration
* livestockProcreationMultiplier (float) - complex multiplier of creatures breeding speed (affects pregnancy chance, speed, distance to partner, distance to other creatures)
* overheatIn2WarmClothes (bool) - if you wear two armor pieces that have ResistantVSFrost modifiers you will have Warm status effect reducing stamina and eitr regen by 20% (change your cape to less warm one if you need)
* meatFromAnimalsMultiplier (float) - multiplier of the maximum count of possible meet drops from boars, deers and other living creatures except bugs
* treesRegrowthChance (float) - chance to left sapling when destroying tree stumps. If tree has no sapling it won't be regrown. Supports all custom saplings if they configured right.

### Some explanations and ideas behind default settings
* default season length of 10 days should take enough time both to struggle and make profit of season effects
* short nights in summer and long nights in winter corresponds with similar cycle in Scandinavia. Also white snow in winter even at night makes environment look pretty bright
* you could face freezing at some moments of fall and winter hence it's handy to use torch to get warm
* plants doesn't grow in winter, slow growth in fall, and grow fast at spring and summer to balance overall value
* bees sleep in winter and do most of the work in summer and fall
* you don't need much firewood at spring and summer but much more to make warmth in fall and winter
* snow in winter doesn't damage the buildings
* summer and fall have more winds
* creatures breed mostly in spring and summer
* it's harder to stay rested in winter
* you will get overheat in summer if you're wearing 2 warm clothes at once
* creatures ususally gets more weight to live through winter so fall and winter means more meaty enemies

## Seasonal environments

There are files with unloaded vanilla environments "Default environments.json" (basically vanilla weather list) and biome environments "Default biome environments.json" (distribution between biomes). This files are for informational purposes only. To get an idea behind vanilla weather and to further copy that weather to customize.

### Environment

The structure of the environment reflects adapted ingame environment settings. The most useful to change vanilla environment properties:
* m_default (bool) - this is the default valheim weather to be set in default situations (it is set to true for "Clear" by default and it should stays that way)
* m_name (string) - name of the environment, it should be unique
* m_isWet (bool) - should exposed to environment characters get Wet debuff
* m_isFreezing (bool) - should exposed to environment characters get Freezing debuff (despite time of day)
* m_isFreezingAtNight (bool) - should exposed to environment characters get Freezing debuff (at night time)
* m_isCold (bool) - should exposed to environment characters get Cold debuff (at all time)
* m_isColdAtNight (bool) - should exposed to environment characters get Cold debuff (at night time)
* m_alwaysDark (bool) - if set to true then environment will darken all colors (like in the vanilla Swamps)
* m_psystems (string) - particle system names separated by commas (mist, rain, snow effects, etc)
* m_ambientLoop (string) - name of audio clip being played on the loop (sounds of wind, snow, rain, etc)

Custom properties:
* m_cloneFrom (string) - which vanilla environment properties should be copied to custom environment

### Biome environments

The structure of the biome environment reflects adapted ingame biome environment settings.
Customized biome environment settings:
* m_name (string) - name of the biome settings, in most cases the biome name
* m_environments - contains an array of environments represented by name and weight. More weight the environment have means the weather will be like this more often.

Weather are chosed from environments list based on pseudorandom value common to all clients.

### Custom environments

File "Custom environments.json" contains default custom environments being added to environment list. Basically it's the list of new weather.

Properties in that file are similar to Environment while only changed one are presented.

There are seasonal variants of vanilla weather mostly.

### Custom music

Place music files into "**...\BepInEx\config\shudnal.Seasons\Custom music**" folder.

Any media track that Unity Engine can read will be loaded into music and will be available to use in custom environments.

For example track "runichills.mp3" will be loaded as "runichills" music track.

To set custom music track settings create file with **.json** extension and the same name as music track.

For example track "runichills.mp3" can have "runichills.json" settings file otherwise default settings is used.

Paste code in *.json file you created (this is default settings)
```
{
  "m_enabled": true,
  "m_volume": 1.0,
  "m_fadeInTime": 3.0,
  "m_alwaysFadeout": false,
  "m_loop": true,
  "m_resume": true,
  "m_ambientMusic": true
}
```
* m_enabled (bool) - music is enabled
* m_volume (float) - volume level of that track
* m_fadeInTime (float) - time in seconds where track volume will be gradually increased on track start
* m_alwaysFadeout (bool) - track will always have fade out effect no matter if it crossed with another track or not
* m_loop (bool) - track will be played continuously
* m_resume (bool) - resume track playback from the moment it stopped previously
* m_ambientMusic (bool) - if set to true then track will be played on loop if game setting "Continuous music" is enabled

Both music tracks and json settings file should be shared with all clients via modpack or manually. Otherwise the music just will not be played.

### Custom biome environments

File "Custom Biome Environments.json" contains default custom environments and their distribution between seasons.

Environments set in property "add" will be added to m_environments list in vanilla Biome environments.
Environments set in property "replace" will be replace set "m_environment" with the same weight in m_environments list in vanilla Biome environments.
Environments set in property "remove" will be completely removed from biome environment.

Use Add to add completely new weather to the biome environment.
Use Replace if you made some seasonal variant of the weather (like changing particle system from "Rain" to "Snow" in "LightRain" Meadows weather means what used to be rainy weather becomes snowy weather without other parameters to be changed)
Use Remove to get rid of some weather.

### Some explanations and ideas behind default settings
* Rain component of weather was replaced by snow one for winter variants
* Added summer swamp weather without wet status
* Added summer swamp darklands weather (a bit different color scheme)
* Winter weather variants have combination of freezing status (freezing at night or in the wet rain/storm)
* Added summer weather variation without cold at night
* Added more clear weather at summer
* Added more rainy weather at fall
* Spring is default weather
* Added dark variants of winter weathers (to represent cloudy days IRL)

## Random events

Default vanilla randon events are saved in "Default events.json".
They represent the name of the event and biome distribution. Weight value can be ignored. This file is for informational purposes only to copy event name and biomes.

### Custom events

File "Custom events.json" contains default custom events and their distribution between seasons. Every other settings have no meaningful purpose in seasonal logic hence were omitted.

Event structure:
* m_name (string) - event with that name will be modified
* m_biomes (string) - comma separated biome list in which the event can be started
* m_weight (integer) - how much more likely that event will be started. 0 to never, 1 to usual probability

That parameters only counts when possible events are being selected. 

### Some explanations and ideas behind default settings
* in winter there could be wolves not only in mountains and plains
* there are no skeletons, blobs, trolls and surtlings in winter but increased chance of dragons and wolves raid
* there are more draugrs, blobs and skeletons in fall due to rainy weather
* in summer there are no wolves but more surtlings, bats and cackling goblins
* in spring there are more trolls, meaties and greydwarves

That way overall raids should be balanced and immersive.

## Custom lightings

File "Custom lightings.json" contains default custom lighting settings and their distribution between seasons.

There are distinct luminance and fog density settings for morning, day, evening, night and indoors.
"luminanceMultiplier" controls the luminance component of HSL color model of environment lightings.
"fogDensityMultiplier" control how much the "air" itself will block lights.

You better test it yourself to find out how it actually looks.

Settings "lightIntensityDayMultiplier" and "lightIntensityNightMultiplier" controls how much light will come from sun and moon. Less luminanceMultiplier and more lightIntensityNightMultiplier makes nights more realistic, moonlit swamp looks terrific.

### Some explanations and ideas behind default settings
* Spring is default 
* Summer has more light at night
* Fall have more darker lightings
* Winter have dark nights which compensated by more moonlight and overall white surroundings reflecting more light

## Custom stats

File "Custom stats.json" contains default custom stats settings and their distribution between seasons.

Current stats tooltip will be shown in almanac to see actual effect. Reopen the almanac after background stats config was changed.

Stats file structure reflects vanilla SE_Stats status effect which has properties:
* m_tickInterval (float) - time in seconds in which m_healthPerTick will apply damage or heal
* m_healthPerTickMinHealthPercentage (float) - percent of max hp you will be affected by m_healthPerTick heal/damage
* m_healthPerTick (float) - actual number HP. If m_healthPerTick > 0 you will be healed otherwise damaged
* m_healthHitType (string) - type of damage m_healthPerTick damage
* m_staminaDrainPerSec (float) - how much stamina will be drained per second
* m_runStaminaDrainModifier (float) - 1.0 for 100%, 0.1 for 10%, -0.5 for -50% means less stamina used
* m_jumpStaminaUseModifier (float) - 1.0 for 100%, 0.1 for 10%, -0.5 for -50% means less stamina used
* m_healthRegenMultiplier (float) - 1.2 for +20% regeneration, 0.8 for -20% regeneration
* m_staminaRegenMultiplier (float) - 1.2 for +20% regeneration, 0.8 for -20% regeneration
* m_eitrRegenMultiplier (float) - 1.2 for +20% regeneration, 0.8 for -20% regeneration
* m_raiseSkills (string, float) - set pairs of skill and its raise multiplier, means more/less skill point gained on action
* m_skillLevels (string, float) - set pairs of common skill and its addition, i.e. "Jump": 15.0 to get +15 to jump skill
* m_modifyAttackSkills (string, float) - set pairs of weapon skill and its addition, i.e. "Swords": 15.0 to get +15 to swords skill
* m_damageModifiers (string, string) - set pairs of damage type and damage modifiers listed below
* m_noiseModifier (float) - 0.2 for 20% more noise generated, -0.2 for 20% less noise generated
* m_stealthModifier (float) - 0.2 for 20% more effective stealth, -0.2 for 20% less effective stealth
* m_speedModifier (float) - 0.05 for 5% more movement speed, -0.05 for 5% less movement speed
* m_maxMaxFallSpeed (float) - 5 is the default value of Feather SlowFall cape
* m_fallDamageModifier (float) - fall damage modifier, 0.7 for 30% less damage from falling

### Damage types 
* Blunt
* Slash
* Pierce
* Chop
* Pickaxe
* Fire
* Frost
* Lightning
* Poison
* Spirit
* Physical
* Elemental

### Damage modifiers
* Normal
* Resistant
* Weak
* Immune
* Ignore
* VeryResistant
* VeryWeak

### Some explanations and ideas behind default settings
* there are poison resistance and slow healing in spring
* spring and summer are easier to move and get more skill from that actions
* fall and winter are focused at resource gathering and fishing
* in summer you move faster, use less stamina, make less noise and better hiding
* good weather in summer makes your health restore faster
* pessimistic thoughts in fall make your eitr restore faster
* fresh winter air makes your stamina regen faster
* you are more resistant to fire in winter
* you make more noise on the snow and worse at hiding
* snow traversal takes 5% of your movement speed
* but you take less damage from falling in the snow

## Custom trader items

File "Custom trader items.json" contains default custom trader items and their distribution between seasons.

Items can be customized distinctly between seasons and traders. You can use custom traders names.

You can adapt items list from https://valheim.thunderstore.io/package/shudnal/TradersExtended/ as it shares the item structure.

### Trader names
You can use different names of the same trader. Case insensitive
* original prefab name (internal ID, haldor has "Haldor")
* unlocalized name (m_name value, haldor has "$npc_haldor")
* localized name (result of translation of unlocalized name to current language, in english "$npc_haldor" becomes "Haldor")
* for vanilla Haldor and Hildir you are just fine using "haldor" and "hildir"

### Trader item
The structure of the trader item reflects adapted ingame tradeable item description:
* prefab (string) - game object prefab name. Current list of items [here](https://valheim-modding.github.io/Jotunn/data/objects/item-list.html). Wrongly set prefab names will be ignored. 
* stack (int) - how much of item will be sold
* price (int) - price for stack
* requiredGlobalKey (string) - global key used to track progress in game worlds. Current list of keys [here](https://valheim.fandom.com/wiki/Global_Keys)

### Some explanations and ideas behind default settings
* Hildir hats are there merely to get an example of file structure. Yet it's seasonal hats why wouldn't she had it.
* Haldor stacks meat through winter to sell it to you in spring
* Haldor has some surpluses from the current harvest in fall (you may get some to prepare for winter)
* Haldor has some potentially unavailable items in winter
* Haldor has some seeds in winter because he knows you will need some in spring

## Grass control

If you ever used grass tweaks mods you should be familiar with that settings.

You can set default grass settings in "Season - Grass" section in general mod config.
* Patch size (float) - size of terrain square populated with grass (sparseness or how wide a single grass "node" is across the ground). Increase to make grass more sparse and decrease to make grass more tight
* Amount scale (float) - grass density or how many grass patches created around you at once
* List of affected grass prefabs - string, comma separated - in case you have custom grass prefabs you should add it to that list

Settings change will be applied on the fly to see effect immediately.

File "Custom grass settings.json" contains distribution of grass settings between days and seasons.

Grass settings depends on day and grass settings will be interpolated between two settings. 

For example if scaleMax is set 1.1 in day 1 and 1.3 in day 5 it means intermediate values will be 
* day 1 - 1.1
* day 2 - 1.15
* day 3 - 1.2
* day 4 - 1.25
* day 5 - 1.3

The same logic works for other values. If value is not set it takes default value set in general settings.

### Grass settings
* m_day (int)
* m_grassPatchSize (float)
* m_amountScale (float)
* m_scaleMin (float) - multiplier of minimum size of grass
* m_scaleMax (float) - multiplier of maximum size of grass. If set to 0 the grass will be completely disabled.

### Some explanations and ideas behind default settings
* main goal was completely disabled grass in winter after set day. That should help greatly to performance in snow storms.
* grass size will be gradually reduced to zero in winter to make it looks like more and more snow
* grass will return in spring gradually
* in spring grass size will be decreased a little in comparison to default grass state
* in summer grass size will be increased but it will be a bit sparser to not hit performance
* in fall grass size will be a bit increase and gradually decreased and made sparser on the last day

## Seasonal clutter

There is 3 new clutter prefabs:
* Meadows flowers (red and blue)
* Black forest flowers (pink)
* Swamp flowers (white)

By default that prefabs are only enabled in Spring adding some heavily needed flavor.

File "Custom clutter settings.json" contains seasonal clutter distribution settings between seasons.

You can add your own clutter using other mods and control its seasonal state through that file.

### Seasonal clutter settings
* clutterName - string - clutter name as it set in ClutterSystem.m_clutter
* spring - bool - should it be enabled in spring
* summer - bool - should it be enabled in summer
* fall - bool - should it be enabled in fall
* winter - bool - should it be enabled in winter

## Custom textures

Custom textures could be placed in **\BepInEx\config\shudnal.Seasons\Custom textures** folder.

Mod comes with some predefined default textures. It will be placed in **\BepInEx\config\shudnal.Seasons\Custom textures\Defaults** folder. Default textures will be overwritten after mod version change.

If you want to make changes to default textures then copy needed folder into **\BepInEx\config\shudnal.Seasons\Custom textures**.

Folders inside of **\BepInEx\config\shudnal.Seasons\Custom textures** folder should be named as texture name. If different objects use the same texture it will be replaced for all of them.

To find what texture name is you should set general config option "Test - Cache format" to "SaveBothLoadBinary" and rebuild the cache (by running console command or just deleting old one in **\BepInEx\cache\shudnal.Seasons**).

After that new cache folder will form with files **cache.json** and **cache.bin** and **textures** folder. Cache.json consists of all objects and their materials and color that will be replaced. 

In that file you can find prefab and texture number which corresponds with folder in "texture\" directory. 

In that texture directory you can find file with name ending on ".orig.png". That is the name of texture you want to replace.

Texture numbers could be generated differently on every cache build.

Files inside of texture folder corresponds with season and variant. Naming convention is Season_Number.png. Your textures should be named accordingly.

### Example

You want to replace winter texture of Beech.

Find your current cache revision folder. It's located in **\BepInEx\cache\shudnal.Seasons** folder and named as cache revision number.
To get your current cache revision number you can enable logging and look for it in log file. Or simply delete every other folder and rebuild cache. 

Most recent folder will be your current cache folder. In that example it's 2054891382. Go inside.

Open **cache.json** file and look for beech prefab name. You can find its name on [Jotunn's prefab list](https://valheim-modding.github.io/Jotunn/data/prefabs/prefab-list.html) or using [RuntimeUnityEditor](https://github.com/ManlyMarco/RuntimeUnityEditor).

In that case there are three beech prefabs in **cache.json**.
* Beech_Sapling - small plantable version of beech
* Beech_small1 - small variant
* Beech_small2 - small variant
* Beech1 - actual big meadows tree and our current target to change texture

Beech_Sapling, Beech_small1 and Beech_small2 shares the same texture with number *112892* (your number might be different).

Beech1 have material **beech_leaf** and its **_MainTex** (main texture) with number *171264*.
There are also bark material and _MossTex. It should be ignored.

So we take number *171264* and go into **\BepInEx\cache\shudnal.Seasons\2054891382\textures\171264** folder.
There are set of seasonal files, **properties.json** file and **beech_leaf.orig.png** file. Last file is original texture used in vanilla game. Original texture file name convention is *{Texture name}.orig.png* so our texture name will be **beech_leaf**.

Now we create new folder in **\BepInEx\config\shudnal.Seasons\Custom textures** and name it **beech_leaf**.

For that example we take file Winter_1.png from **\BepInEx\cache\shudnal.Seasons\2054891382\textures\171264** folder to **\BepInEx\config\shudnal.Seasons\Custom textures\beech_leaf**.

You can now edit that file as you wish. It will be loaded as Winter variant number 1 for Beech1 prefab in game.

Any changes done to that file will be applied on the fly.

Only textures appeared in **cache.json** file could be replaced.

## Custom biome settings

File "Custom biome settings.json" contains settings for biome terrain color seasonal override and winter colors for minimap.

Changes made to terrain colors requires change of current season to get effect. You can simply override and change season in general config section "Season - Override".

Changes made to winter minimap colors requires world restart.

### Seasonal ground colors
* biome - string -  biome name, case insensitive, custom biomes supported (you can set biome name if it added properly like in [Expand World Data](https://thunderstore.io/c/valheim/p/JereKuusela/Expand_World_Data/) or you can set biome numeric ID)
* spring - string - biome terrain that should be used for that biome in Spring
* summer - string - biome terrain that should be used for that biome in Summer
* fall - string - biome terrain that should be used for that biome in Fall
* winter - string - biome terrain that should be used for that biome in Winter

### Winter colors

Being set as pairs "Biome": "Color Hex Code".

Vanilla winter colors made by interpolating original biome color to "#FAFAFF" preserving original alpha channel. #FAFAFF also called Ghost White and it's not just white color but has a bit snowy tint.

### Some explanations and ideas behind default settings
* Ashlands, Mountain and Deep north do not change its terrain.
* All controlled biomes become mountain in Winter to get snow effect on the ground
* Meadows have Plains color in Fall to get yellowish ground color
* Black forest has Swamp color in Fall to get effect of wet dirt ground
* Plains has Meadows color in Spring to get effect of blooming surroundings

## General settings
* minimap will be recolored using the seasonal colors setting
* seasonal items will be enabled in the corresponding season
* you won't die from freezing debuff if you're not in the mountains or deep north. You will stay at 1 hp.
* seasonal stats will be applied only outdoors. In dungeons you won't have advantage.
* current seasonal buff can be hidden and timer format can bet set to current day or the time left to next season
* seasons are changed in the morning of first day
* seasons are changed with small fade effect which can be disabled
* seasons are changed instantly and current season can be overriden
* you can set localization strings for season names and tooltips
* due to seasonal change of honey production and plants growth there are settings to show estimates of plants and beehive production (like in BetterUI)
* seasons can be set changeable only when sleeping
* ice floes will spawn in ocean in set period of days in winter
* water will freeze in set period of days in winter
* ships can be pushed out of water when the surface is frozen

## Custom world settings for realtime seasons calculations

File "Custom world settings.json" contains example entry of custom world settings.

You can set start time in UTC timezone and the day length in seconds.
Your world name should be equal the set world name in settings for them to activate.

If the world setting is set then seasons will be calculated from set datetime using current UTC time, set daylength in seconds and days in season from season settings.

That way you will not have an option to change season on the sleep. Season will change when time is come.

For example you can 
* set start time as 0:00 of Monday
* set day length 86400 seconds (1 day)
* set days in season = 7

It will mean your world has week long seasons starting from set Monday.

## Seasonal buff icon replacement

You can place files in the config folder to replace current buff icon (restart required):
* season_fall.png
* season_spring.png
* season_summer.png
* season_winter.png

## Seasonal global key

You can enable setting of season related server wide global key. It's disabled by default. You can customize the key in case you need it.

Default seasonal keys:
* season_fall
* season_spring
* season_summer
* season_winter

## Installation (manual)
extract Seasons folder to your BepInEx\Plugins\ folder

## Configurating
The best way to handle configs is [Configuration Manager](https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/).

Or [Official BepInEx Configuration Manager](https://valheim.thunderstore.io/package/Azumatt/Official_BepInEx_ConfigurationManager/).

## Mirrors
[Nexus](https://www.nexusmods.com/valheim/mods/2654)

[Thunderstore](https://valheim.thunderstore.io/package/shudnal/Seasons/)