# Dynamic NPC Outfit Selector

A Stardew Valley (1.6+) SMAPI mod. [Nexus page](https://www.nexusmods.com/stardewvalley/mods/50375)

Ever wished you could just pick what each villager wears? Now you can.

This mod exists because I got tired of manually swapping files between folders to get the look
I wanted for a season or a festival. I wanted something dynamic instead, something I could just
click through in-game.

**IMPORTANT: This mod doesn't add any outfits on its own.** It's a picker, not an art pack. It
reads whatever's already loaded in your game (from Content Patcher appearance packs, or your own
PNGs, see Requirements below) and lets you choose between them.

**Note:** festival and beach outfits sometimes have fewer animation frames than a regular
seasonal outfit's spritesheet. Forcing one onto a different season can cause visual glitches
(mismatched or invisible frames) if the source art doesn't have a full set. Use at your own
discretion — if something looks off, just switch that slot back to "no override."

## Installation instructions

1. Install [SMAPI](https://smapi.io/).
2. Install [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098). It is
   required for the menu to be displayed.
3. Unzip this mod into your `Stardew Valley/Mods` folder, so you end up with
   `Mods/Dynamic NPC Outfit Selector/manifest.json`.
4. Launch the game through SMAPI. Open the mod's page from the GMCM cog on the title screen or
   the in-game pause menu.

## Main features

- Lists every NPC and every outfit variant currently loaded in your game as a clickable picture,
  no matter which mod (or mods) it came from.
- Pick a condition from a dropdown — Spring, Summer, Fall, Winter, Beach, or a specific festival
  — then check the outfit you want for it. Leave it on the blank "no override" card to let the
  game (or whatever mod normally handles that NPC) keep deciding on its own.
- Narrow it further with two more dropdowns: **Weather** (Sunny, Raining, Lightning, Snowing,
  Green Rain, Windy) and **Where** (Indoor, Outdoor, or one exact place like the Beach or the
  Saloon). Both default to "Any". So you can give someone a plain winter look, and a heavier coat
  that only shows up outdoors while it's snowing.
- The Where list is built from the locations actually loaded in your save, so places added by
  other mods (Stardew Valley Expanded and friends) show up on their own.
- Applies live: no restart, no reload, just hit Save and NPCs already in the world update
  immediately.
- Works with any Content Patcher appearance pack automatically, and also with your own loose PNG
  files if you don't have (or want to make) a Content Patcher pack of your own.

## How the mod decides which outfit wins

The three dropdowns combine into a single key, which is what you'll see in `config.json`:

```
Spring                                  just spring
Spring::Weather_Raining                 spring, but only when it's raining
Spring::Where_Indoor                    spring, but only indoors
Spring::Where_Beach                     spring, but only while they're at the beach
Winter::Weather_Snowing::Where_Outdoor  winter, snowing, and outdoors
Weather_Sunny                           any season, as long as it's sunny
```

The first part is the category. Every part after it says which setting it belongs to, written
`Setting_Value`, and narrows when the category applies. Order doesn't matter when you read them,
and the mod always saves them as category, weather, place.

Naming the setting is what lets a value the mod has never seen still work. Other mods can add both
new locations *and* new weather types; because the part already says which it is, an unfamiliar
value is handed to the game untouched instead of being guessed at.

The category itself is the one part with no name in front, so it's also the one part you can leave
out entirely — `Weather_Sunny` on its own applies in every season.

You can set as many of these as you like for the same character, and they can all be true at once.
**The more specific one wins** — the key with more parts beats the key with fewer.

Each key gets a score, and the highest score is the outfit you see. The category sets the tier; the
parts after it move you up **within** that tier:

| Category (the tier) | Points |
|---|---|
| No category at all | 0 |
| A custom one of your own, e.g. `Casual` | 8 |
| A season — Spring, Summer, Fall, Winter | 16 |
| A festival — Egg Festival, Flower Dance, ... | 32 |

| Added on top | Points |
|---|---|
| `Where_Indoor` or `Where_Outdoor` | +1 |
| A weather, e.g. `Weather_Snowing` | +2 |
| An exact place, e.g. `Where_Beach` | +4 |

So for one character:

| Key | Score | When it wins |
|---|---|---|
| `Casual` | 8 | Whenever nothing below applies — a baseline look |
| `Winter` | 16 | Any winter day |
| `Winter::Where_Outdoor` | 17 | Outdoors in winter, beating plain `Winter` |
| `Winter::Weather_Snowing` | 18 | Snowing in winter, beating both above |
| `Winter::Weather_Snowing::Where_Outdoor` | 19 | Outdoors while it snows, beating everything above |
| `Winter::Where_Town` | 20 | In town, beating every plain-weather key above |
| `Winter Star` | 32 | During the festival, beating every winter key |

The add-ons are 1, 2 and 4, so they sum to at most 7 — less than the gap between tiers. Every
possible key therefore lands on its own number, and **no two keys can ever tie**. That matters:
when two outfits genuinely tie, the game picks between them at random rather than taking the first
one. The scoring exists so that never happens.

It also means **a category always beats a rule without one**, no matter how many conditions that
rule piles on. `Weather_Sunny::Where_Beach` (6) loses to a plain `Winter` (16). That's deliberate:
a category names a wardrobe slot you set up, while a category-less rule is a background default —
the same role `Casual` plays.

**`Indoor` and `Outdoor` act as fallbacks for exact places.** They're the same setting, just less
specific, and you don't have to do anything to make this work: at the beach, a `::Where_Beach` key wins
if you set one, otherwise `::Where_Outdoor` applies (the beach *is* outdoors), otherwise the plain
season, otherwise the game's own choice.

A few notes:

- Festivals outrank seasons on purpose: on Egg Festival day, your festival outfit wins over your
  spring one.
- Weather counts for more than indoor/outdoor, so `Winter::Weather_Snowing` beats `Winter::Where_Indoor`. Both
  have two parts, and something had to break the tie.
- "Where" means where the **NPC** is, not where you are. `Spring::Where_Beach` dresses Alex for the
  beach when *he* goes there.
- Impossible combinations are allowed and simply never trigger — `Winter::Weather_GreenRain` is harmless,
  it just never comes up in vanilla. Nothing is filtered out, in case a weather mod makes it real.
- Anything the dropdowns don't offer can be typed into `config.json` by hand and still works:
  `Spring::Weather_RedRain` for a weather mod's own weather, or `Spring::Where_SomeModdedMap` for
  a location this mod hasn't heard of. The value after the underscore goes to the game unchanged.
- The `Beach` **category** is a different thing from the `Where_Beach` **place**: the category is
  Ginger Island's resort attire, which the game picks by its own mechanism, while `Where_Beach` is
  Pelican Town's beach map.

## Requirements

- Stardew Valley 1.6+
- SMAPI 4.0.0+
- [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) is required; the
  mod does nothing without it.
- Content Patcher — optional, only needed if you want outfits from other authors' appearance
  packs. Your own local PNGs work without it.
- Outfits themselves aren't included. You'll need at least one of:
  1. A Content Patcher appearance pack (most "seasonal outfit" mods on Nexus already work this
     way — just install it normally and its outfits show up here automatically), or
  2. Your own PNGs dropped straight into `Mods/Dynamic NPC Outfit Selector/assets/Characters/`
     and `assets/Portraits/`, named `<NpcName>_<Condition>.png` (e.g. `Haley_Spring.png`,
     `Haley_Beach.png`, `Haley_Winter_Snowing_Outdoor.png`). Subfolders don't matter; portraits
     are optional. Full instructions are in `assets/README.txt` once the mod is installed.

## Known limitations

- Only NPCs with proper appearance data show real alternatives. Older mods that just overwrite a
  texture file directly aren't represented as separate options — there's nothing to list.
- The "Beach" category covers Ginger Island's beach resort, which the game selects by its own
  island-attire mechanism. Pelican Town's beach isn't that — the game swaps that look by a
  filename convention this mod can't hook into. You can still dress someone for it with an exact
  place, e.g. `Summer::Where_Beach`.
- Some assets may be replaced with default ones in case it doesn't have the required animation
  in their assets.
- Forcing a festival or beach outfit onto a regular season slot (or vice versa) can cause visual
  glitches: mismatched frames or invisible sprites, if that outfit's spritesheet doesn't have the
  full set of animation frames a regular season needs. This is a limitation of the source art,
  not something this mod can fix.

## Credits

None of the outfits shown in the screenshots/video is included with this mod. They belong to the
appearance packs used to demonstrate it:

- [Seasonal Outfits - Slightly Cuter Aesthetic](https://www.nexusmods.com/stardewvalley/mods/5450) by Poltergeister
- [Seasonal Haley](https://www.nexusmods.com/stardewvalley/mods/11389)

Full credit to their authors.

## For the author (how to build the release)

You need the game installed plus the .NET 6 SDK.

```
dotnet build -c Release
```

ModBuildConfig locates your game automatically, compiles the mod, copies it into your `Mods`
folder, and produces an upload-ready zip in `bin/Release`. That zip is what you upload to Nexus.

If the game isn't auto-detected, create a `stardewvalley.targets` or set the `GamePath`
property — see the [ModBuildConfig docs](https://github.com/Pathoschild/SMAPI/blob/develop/docs/technical/mod-package.md).
