# Recoloring settings
Recoloring aka cache settings are located at "Cache settings" folder inside the config folder.
Default settings are located in "Defaults" subfolder. 

To make settings work you need to copy file you want to edit from "Defaults" subfolder into "Cache settings" folder.

In shorts:
* Materials.json - what material to recolor (to filter prefabs, shaders, renderers and materials)
* Colors.json - what color variants to use for recoloring
* Color ranges.json - pixels of what colors should be recolored
* Color positions.json - if you need to ignore some areas of the texture or recolor only the desired area

## Materials.json
Consists of rules how to find or filter unneeded materials.
Filtered by default (won't be recolored):
* ships (layer 0)
* items (layer 12)
* layer 16 (except Pickable and Plant, they will be tried to recolor)

What could be customized in Materials.json

### Filter by prefab name
* ignorePrefab - string collection - Prefabs from this list won't be recolored, full name, case sensitive
* ignorePrefabPartialName - string collection - Prefabs from this list won't be recolored, partial name, case sensitive
* piecePrefab - string collection - Prefabs from layer 10 will be recolored only if they are in this list, full name, case sensitive
* piecePrefabPartialName - string collection - Prefabs from layer 10 will be recolored only if they are in this list, partial name, case insensitive
* creaturePrefab - string collection - Materials of SkinnedMeshRenderer will be recolored only if they are in this list, full name, case sensitive
* effectPrefab - string collection - Prefabs from layer 8 will be recolored only if they are in this list, full name, case sensitive

### Filter by shader, renderer, material property
* shadersTypes - Dictionary (string, string collection) - renderer type to be tried to recolor and their shaders
* shaderOnlyMaterial - Dictionary (string, string collection) - shader name and materials list that only be tried to recolored. If shader is not in this dictionary its materials will be tried to recolor
* shaderIgnoreMaterial - Dictionary (string, string collection) - shader name and materials list that won't be recolored
* particleSystemStartColors - string collection - list of prefabs where ParticleSystem will be tried to found and that ParticleSystem startColor will be recolored
* shaderTextures - Dictionary (string, string collection) - shader name and material texture name properties list to be tried to recolor
* materialTextures - Dictionary (string, string collection) - material name and material texture name properties list that will be tried to recolor (overrides the shaderTextures list, if texture with set property is not found fallback to shaderTextures settings)
* shaderColors - Dictionary (string, string collection) - shader name and material color name properties list to be tried to recolor
* materialColors - Dictionary (string, string collection) - material name and material color name properties list to be tried to recolor

To get an idea of what material you want to recolor and how should it be found using this rules you should understand the prefabs structure. To see the game objects you can unpack the game files using https://github.com/Valheim-Modding/Wiki/wiki/Valheim-Unity-Project-Guide or use bepinex mod https://github.com/ManlyMarco/RuntimeUnityEditor to see the objects in game.

## Colors.json
Consists of color sets tied to group of prefabs and specific colors. Color sets consist of color variants, descriptions of how color should be merged.
* seasonal - default colors if prefab doesn't fit in other categories
* grass - materials with shader "Custom/Grass"
* moss - textures with "moss" in name (_MossTex mostly)
* creature - materials with shader "Custom/Creature"
* piece - materials with shader "Custom/Piece" or name starts with "GoblinVillage" (their roofs is actually Custom/Vegetation shader)
* conifer - materials and prefabs with "pine" in name
* bush - materials and prefabs with "bush" or "shrub" in name
* prefabOverrides - case sensitive list of prefab names and tied to them set of seasonal colors
* materialOverrides - case sensitive list of material names and tied to them set of seasonal colors

### Color variant
* useColor - bool - should that color be used or original color stays
* color - string - HEX code of what color should be used to merge with original
* targetProportion - float (0 to 1) - target proportion in what original color should be merged with set color
* preserveAlphaChannel - bool - should alpha channel (transparency) of original color be preserved after color change
* reduceOriginalColorToGrayscale - bool - should original color be reduced to grayscale before color merge (used for winter colors)
* restoreLuminance - bool - should Luminance or Lightness component of color be restored to original color value

More about color interpolation and HSL at https://docs.unity3d.com/ScriptReference/Color.Lerp.html and https://en.wikipedia.org/wiki/HSL_and_HSV

## Color ranges.json
Consists of rules to get what color should be replaced. Operates HSL color scheme. 
* seasonal - default rules to get colors to change if prefab doesn't fit in other categories
* grass - materials with shader "Custom/Grass"
* moss - textures with "moss" in name (_MossTex mostly)
* specific - rules to define a set of materials that shoud use that color rules

### Color rule default
* hue - (start 0, end 360) - range of hue value of color to fit (start and end included)
* saturation - (start 0, end 1) - range of saturation value of color to fit (start and end included)
* luminance - (start 0, end 1) - range of luminance (Luminosity, Lightness) value of color to fit (start and end included)

If several color rules set - at least one of them should be met for pixel with that color to be recolored.

To get pixel color value from image you can use https://imagecolorpicker.com/

### Specific material rule
First 3 fields used to set where to find the string to match 
* material - string - material name to apply rule
* prefab - string - prefab name to apply rule
* renderer - string - renderer name to apply rule

Next 3 fields used to define how to apply the condition
* only - bool - this condition should always be met
* partial - bool - name set in previous field could match only partially
* not - bool - this condition should NOT be met to apply the rule

In short at first step the conditions with only=true are selected, and all of them should be met.
Then the conditions with only=false are selected and at least one of them should be met.
In case of not=true the logic is inverted and that single condition should NOT be met for material to fit.

Example - Lox recoloring.
There is several lox models with the same texture but different materials.
We only want to recolor the fur and not the skin. There are renderer named "Furr1" in every Lox model tied to fur material.
The fur have special brownish color we need to set.

In "materials" collection we set the rule to only match partial name "Furr" of material renderer.
```
{
    "material": null,
    "prefab": null,
    "renderer": "Furr",
    "only": true,
    "partial": true,
    "not": false
}
```

Then we set Loxen in Hildirs camp (they have special material name)
```
{
    "material": "HildirsLox",
    "prefab": null,
    "renderer": null,
    "only": false,
    "partial": false,
    "not": false
}
```
And regular Loxen (Haldor's Halstein fits in there)
```{
    "material": null,
    "prefab": "Lox",
    "renderer": null,
    "only": false,
    "partial": false,
    "not": false
}
```
And we want to recolor Lox ragdoll spawned on death
```
{
    "material": null,
    "prefab": "lox_ragdoll",
    "renderer": null,
    "only": false,
    "partial": false,
    "not": false
}
```
That means Renderer name should always contains "Furr" string.
And then at least one of them should be met
* material could be named "HildirsLox"
* prefab could be named "Lox" or "lox_ragdoll"

If that is met then the texture colors should be from ranges set in "colors" field.

## Color positions.json
Consists of rules which part of texture should or should NOT be recolored.

By default used to exclude fir and pine trunks and to include only roof parts of pieces textures.

Material specific rules are the same as in Color ranges.json

Bounds are set from top left pixel to bottom right pixel.

start==0 and end==0 means every pixel.

If not=true then pixels in set area will be ignored.

# If you have questions
I didn't think anyone will ever read that topic. To get help with this mess and more detailed description pls reach me on discord you can get in github profile.