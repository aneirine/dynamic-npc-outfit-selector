using System;
using System.Collections.Generic;
using System.Linq;

namespace NpcOutfitSelector
{
    // The 8 vanilla calendar festivals use this stable "festival_<season><day>" key — safe to
    // prettify, unlike arbitrary IS_EVENT numbers, which are one-off scene IDs with no name data.
    internal static class VanillaFestivals
    {
        public static readonly Dictionary<string, string> NamesByEventId = new(StringComparer.OrdinalIgnoreCase)
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

        public static readonly Dictionary<string, string> EventIdsByName =
            NamesByEventId.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);
    }
}
