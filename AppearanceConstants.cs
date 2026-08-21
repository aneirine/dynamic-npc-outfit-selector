using System;
using System.Collections.Generic;
using System.Linq;

namespace NpcOutfitSelector
{
    // Fixed values of the category system: key syntax, the vocabularies segments draw from, and
    // the numbers that rank them. Rationale is in planning/1.1.0-category-keys.md.
    //
    // Keys and appearance Ids are written into players' config.json, so respelling anything here
    // invalidates saved overrides.
    internal static class AppearanceConstants
    {
        // Key syntax: Base[::Weather_x][::Where_x], e.g. "Winter::Weather_Snowing::Where_Outdoor".
        // The base is the only unprefixed segment, which is what makes it unambiguous; a new axis
        // must therefore bring its own prefix.
        public const string KeySeparator = "::";
        public const string AnyOption = "Any";
        public const string WeatherPrefix = "Weather_";
        public const string WherePrefix = "Where_";

        // Coarse half of the Where slot; an exact place fills the same slot.
        public const string IndoorValue = "Indoor";
        public const string OutdoorValue = "Outdoor";

        public const string DefaultAppearanceId = "Default";
        public const string LocalAssetIdPrefix = "LocalAsset::";

        // Asset namespace the mod's own PNGs are served under, so a forced entry can point at one
        // like any other texture.
        public const string LocalAssetPathPrefix = "Mods/NpcOutfitSelector/LocalAssets/";

        // Ginger Island resort attire. 1.0.1 spelled it "Beach", which must stay readable forever.
        public const string IslandCategory = "Island";
        public const string LegacyIslandCategory = "Beach";

        // For an IS_EVENT id with no display name of its own.
        public const string CustomEventLabelPrefix = "Event: ";

        public static readonly string[] Seasons = { "Spring", "Summer", "Fall", "Winter" };

        // Only a floor, for the title screen where no save is loaded; merged with the locations
        // actually present once one is.
        public static readonly string[] KnownPlaces =
        {
            "Farm", "Town", "Beach", "Mountain", "Forest", "Desert", "Railroad", "BusStop", "Woods",
            "IslandSouth", "IslandNorth", "IslandWest",
            "Saloon", "SeedShop", "Hospital", "Blacksmith", "AnimalShop", "FishShop", "JojaMart",
            "ScienceHouse", "ManorHouse", "ArchaeologyHouse", "CommunityCenter", "Mine",
        };

        // Weather has three forms: the key (config.json and filenames), the constant the game's
        // WEATHER query compares against, and the dropdown label. Only the key is permanent.
        public static readonly Dictionary<string, string> WeatherConstantsByKey = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Sunny"] = "Sun",
            ["Raining"] = "Rain",
            ["Lightning"] = "Storm",
            ["Snowing"] = "Snow",
            ["GreenRain"] = "GreenRain",
            ["Windy"] = "Wind",
        };

        // Only keys that don't already read well; the rest display as-is.
        public static readonly Dictionary<string, string> WeatherLabelsByKey = new(StringComparer.OrdinalIgnoreCase)
        {
            ["GreenRain"] = "Green Rain",
        };

        public static readonly Dictionary<string, string> WeatherKeysByConstant =
            WeatherConstantsByKey.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

        // Only the 8 calendar festivals have stable ids worth naming; other IS_EVENT numbers are
        // one-off scenes.
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

        // Precedence = ForcedPrecedenceBase - score; lower wins. Well below the -10000 real content
        // packs use.
        public const int ForcedPrecedenceBase = -5_000_000;

        // Powers of two, so no two keys can reach the same score — the game breaks an exact tie by
        // weighted random, not by order.
        public const int CoarseWhereScore = 1;
        public const int WeatherScore = 2;
        public const int ExactPlaceScore = 4;

        // Tiers, spaced above the segments' maximum sum of 7 so a key can't climb out of its own.
        public const int NoBaseScore = 0;
        public const int CustomBaseScore = 8;
        public const int SeasonBaseScore = 16;
        public const int FestivalBaseScore = 32;
    }
}
