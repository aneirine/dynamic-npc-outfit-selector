Drop your own outfit PNGs here — no Content Patcher pack needed.

Characters/  -> the sprite sheet (walking/idle character), same format as vanilla Characters/<Npc>.png
Portraits/   -> the matching portrait (optional), same format as vanilla Portraits/<Npc>.png

File name format (subfolder structure inside doesn't matter — nest them however you like):

    <NpcName>_<Condition>.png

Examples:
    Haley_Spring.png
    Haley_Summer.png
    Haley_Winter_Indoor.png
    Haley_Winter_Snowing.png
    Haley_Winter_Snowing_Outdoor.png
    Haley_FlowerDance.png
    Haley_Beach.png

<Condition> is matched against known seasons (Spring/Summer/Fall/Winter), "Beach", and the vanilla
festival names (Egg Festival, Flower Dance, Luau, etc. — abbreviations like "FlowerDance" or
"EggFest" are recognized too). Anything else becomes its own custom category in the mod's menu.

After a season you can also add a weather and/or a place, in any order, separated by underscores:

    weather: Sunny, Raining, Lightning, Snowing, GreenRain, Windy
    where:   Indoor, Outdoor, or a location name such as Beach, Town, Saloon, Mountain

So Haley_Winter_Snowing_Outdoor.png lands in the "Winter::Snowing::Outdoor" slot, which beats plain
"Winter" whenever it's actually snowing and she's outside. Haley_Summer_Beach.png lands in
"Summer::@Beach" and beats both. You don't have to name files this way — every sprite here can be
assigned to any slot from the menu's dropdowns regardless of its name.

These show up as extra pickable outfits in the NPC Outfit Selector menu (GMCM) alongside anything
provided by installed Content Patcher packs.

IMPORTANT — file names must be unique across this whole folder (subfolders included):
If you're combining outfits from two different sources and they happen to use the same file name
(e.g. both call a file "Haley_Beach.png"), only the first one found is kept — the other is silently
skipped (a warning is written to the SMAPI log, but the mod won't guess which one you meant). Rename
one of them to something distinct — e.g. "Haley_Beach_2.png" — before dropping it in.
