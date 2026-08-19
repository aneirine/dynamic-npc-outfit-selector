using System;
using System.Collections.Generic;
using System.Linq;

namespace NpcOutfitSelector
{
    // Every fixed value the category system is built from, in one place: the syntax of a category
    // key, the vocabularies its segments are drawn from, and the numbers that rank them.
    //
    // Most of this is permanent. Category keys and appearance Ids are written into players'
    // config.json, so changing a spelling here silently invalidates saved overrides.
    internal static class AppearanceConstants
    {
        // ----- Key syntax -----

        // A category key is Base[::Weather][::Where], e.g. "Winter::Snowing::Outdoor".
        public const string KeySeparator = "::";

        // A dropdown left on this contributes no segment at all, so a plain base key is spelled
        // exactly as 1.0.1 wrote it.
        public const string AnyOption = "Any";

        // The coarse half of the Where slot. An exact place fills the same slot, because naming a
        // location already answers indoors-or-outdoors — every GameLocation is one or the other,
        // so "Beach::Outdoor" would be redundant.
        //
        public const string IndoorValue = "Indoor";
        public const string OutdoorValue = "Outdoor";

        // Every narrowing segment is written "<axis>_<value>". The prefix is what identifies the
        // axis, so nothing has to be guessed from a vocabulary — which matters because both sets
        // are open-ended: mods add maps, and Data/LocationContexts lets them add weather IDs too
        // (WeatherCondition.Weather is a plain string). An unfamiliar value on either axis is
        // therefore passed to the game untouched instead of being misread as the other one.
        //
        // RULE FOR ADDING AXES LATER: give the new axis its own "<axis>_" prefix and nothing else
        // changes. Never add a second unprefixed segment kind — see below for why the one that
        // exists is safe.
        //
        // A dynamic vocabulary would not do instead: weather IDs could be read from
        // Data/LocationContexts, but then a key's meaning would depend on which mods are installed,
        // and uninstalling one would silently reinterpret a saved key.
        public const string WeatherPrefix = "Weather_";
        public const string WherePrefix = "Where_";

        // The base is the one unprefixed segment. It is unambiguous precisely because it is the
        // only one: it comes first, and it stays bare so a 1.0.1 key like "Spring" keeps its exact
        // spelling. It is also optional — a key of nothing but prefixed segments ("Weather_Sunny")
        // simply applies in every season.

        // ----- Appearance Ids -----

        public const string DefaultAppearanceId = "Default";
        public const string LocalAssetIdPrefix = "LocalAsset::";

        // Ginger Island's resort attire, which the game selects through its own IsIslandAttire flag
        // rather than a condition. 1.0.1 called this "Beach", which collided with the Where_Beach
        // place — Pelican Town's map, a different beach entirely — so a key reading
        // "Beach::Where_Beach" looked sensible while being self-contradictory. Renamed, with the
        // old spelling kept readable forever: it is a shipped 1.0.1 key.
        public const string IslandCategory = "Island";
        public const string LegacyIslandCategory = "Beach";

        // Label prefix for a non-vanilla IS_EVENT id — no display name data exists for those.
        public const string CustomEventLabelPrefix = "Event: ";

        // ----- Vocabularies -----

        public static readonly string[] Seasons = { "Spring", "Summer", "Fall", "Winter" };

        // The places villagers actually go. Deliberately not exhaustive: it's the floor for the
        // title screen, where no save is loaded. In game this is merged with the locations actually
        // present, so other mods' places appear without this list knowing about them.
        public static readonly string[] KnownPlaces =
        {
            "Farm", "Town", "Beach", "Mountain", "Forest", "Desert", "Railroad", "BusStop", "Woods",
            "IslandSouth", "IslandNorth", "IslandWest",
            "Saloon", "SeedShop", "Hospital", "Blacksmith", "AnimalShop", "FishShop", "JojaMart",
            "ScienceHouse", "ManorHouse", "ArchaeologyHouse", "CommunityCenter", "Mine",
        };

        // Weather is three separate things, deliberately not collapsed into one string:
        //
        //   key       what a category key and a local-asset filename contain — kept space-free so
        //             both stay easy to type and to split
        //   constant  what the game's own WEATHER query compares against
        //   label     what the dropdown displays, via GMCM's formatChoice
        //
        // Only the key is permanent.
        public static readonly Dictionary<string, string> WeatherConstantsByKey = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Sunny"] = "Sun",
            ["Raining"] = "Rain",
            ["Lightning"] = "Storm",
            ["Snowing"] = "Snow",
            ["GreenRain"] = "GreenRain",
            ["Windy"] = "Wind",
        };

        // Only the keys that don't already read well; everything else displays as-is.
        public static readonly Dictionary<string, string> WeatherLabelsByKey = new(StringComparer.OrdinalIgnoreCase)
        {
            ["GreenRain"] = "Green Rain",
        };

        public static readonly Dictionary<string, string> WeatherKeysByConstant =
            WeatherConstantsByKey.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

        // The 8 vanilla calendar festivals use this stable "festival_<season><day>" key — safe to
        // prettify, unlike arbitrary IS_EVENT numbers, which are one-off scene IDs with no name data.
        public static readonly Dictionary<string, string> FestivalNamesByEventId = new(StringComparer.OrdinalIgnoreCase)
        {
            ["festival_spring13"] = "Egg Festival",
            ["festival_spring24"] = "Flower Dance",
            ["festival_summer11"] = "Luau",
            ["festival_summer28"] = "Dance of the Moonlight Jellies",
            ["festival_fall16"] = "Stardew Valley Fair",
            ["festival_fall27"] = "Spirit's Eve",
            ["festival_winter8"] = "Festival of Ice",
            ["festival_winter25"] = "Feast of the Winter Star",
        };

        public static readonly Dictionary<string, string> FestivalEventIdsByName =
            FestivalNamesByEventId.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

        // ----- Priority -----

        // A forced entry's Precedence is ForcedPrecedenceBase minus its score, and lower wins.
        // The base sits far below the lowest any real content pack uses (-10000, island attire).
        public const int ForcedPrecedenceBase = -5_000_000;

        // Added per narrowing segment. Powers of two, so every combination sums to a distinct
        // score. 4 > 2 + 1 also makes an exact place beat any mix of coarser conditions — "at the
        // beach" is more specific than "outdoors in the rain". Their maximum sum is 7.
        public const int CoarseWhereScore = 1;
        public const int WeatherScore = 2;
        public const int ExactPlaceScore = 4;

        // Starting score by what the base segment is. The base is a tier, not another narrowing
        // condition: it answers "which wardrobe slot is this", while the segments above answer
        // "when, within that slot". So each tier is spaced above the largest possible sum of
        // segments, and a key can never climb out of its tier by adding conditions.
        //
        // The tiers are ordered by how much of the game they claim. No base and a custom base both
        // match unconditionally, but they still need separate values — otherwise "Where_Indoor" and
        // "Casual::Where_Indoor" tie at the same score while both being true at once, and the game
        // resolves that with a coin flip rather than by order.
        public const int NoBaseScore = 0;
        public const int CustomBaseScore = 8;
        public const int SeasonBaseScore = 16;
        public const int FestivalBaseScore = 32;
    }
}
