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
  GreenRain, Windy) and **Location** (Indoor, Outdoor). Both default to "Any". So you can give
  someone a plain winter look, and a heavier coat that only shows up outdoors while it's snowing.
- Applies live: no restart, no reload, just hit Save and NPCs already in the world update
  immediately.
- Works with any Content Patcher appearance pack automatically, and also with your own loose PNG
  files if you don't have (or want to make) a Content Patcher pack of your own.

## How the mod decides which outfit wins

The three dropdowns combine into a single key, which is what you'll see in `config.json`:

```
Spring                      just spring
Spring::Raining             spring, but only when it's raining
Spring::Indoor              spring, but only indoors
Winter::Snowing::Outdoor    winter, snowing, and outdoors
```

You can set as many of these as you like for the same character, and they can all be true at once.
**The more specific one wins** — the key with more parts beats the key with fewer.

Each key gets a score, and the highest score is the outfit you see:

| Part of the key | Points |
|---|---|
| A custom category of your own (no game condition attached) | 0 |
| A season — Spring, Summer, Fall, Winter | 10 |
| A festival — Egg Festival, Flower Dance, ... | 20 |
| `::Indoor` or `::Outdoor` | +1 |
| A weather, e.g. `::Snowing` | +2 |

So for one character:

| Key | Score | When it wins |
|---|---|---|
| `Casual` | 0 | Whenever nothing below applies — a baseline look |
| `Winter` | 10 | Any winter day |
| `Winter::Outdoor` | 11 | Outdoors in winter, beating plain `Winter` |
| `Winter::Snowing` | 12 | Snowing in winter, beating both above |
| `Winter::Snowing::Outdoor` | 13 | Outdoors while it snows, beating everything above |
| `Winter Star` | 20 | During the festival, beating every winter key |

Two keys only ever score the same when they can't both be true anyway — you can't be indoors and
outdoors at once, and there's only one weather at a time. That matters, because when two outfits
genuinely tie, the game picks between them at random rather than taking the first one. The scoring
exists specifically so that never happens.

A few notes:

- Festivals outrank seasons on purpose: on Egg Festival day, your festival outfit wins over your
  spring one.
- Weather counts for more than indoor/outdoor, so `Winter::Snowing` beats `Winter::Indoor`. Both
  have two parts, and something had to break the tie.
- Impossible combinations are allowed and simply never trigger — `Winter::GreenRain` is harmless,
  it just never comes up in vanilla. Nothing is filtered out, in case a weather mod makes it real.
- `Beach` is separate from all of this. Ginger Island's beach attire is chosen by its own mechanism
  in the game, so it takes no weather or location parts and never competes with the keys above.

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
- "Beach" currently covers Ginger Island's beach resort. The regular Pelican Town beach look is
  swapped by a different, filename-based method and isn't force-applied by this mod yet.
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
