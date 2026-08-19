using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StardewValley.GameData.Characters;

namespace NpcOutfitSelector
{
    // Buckets an appearance entry by what actually makes it apply — season, beach, or a
    // festival/event — read straight off the entry's own data instead of a hardcoded calendar.
    internal static class AppearanceCategory
    {
        private static readonly string[] Seasons = { "Spring", "Summer", "Fall", "Winter" };

        private static readonly Regex EventIdPattern = new(@"IS_EVENT\s+(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SeasonKeywordPattern = new(
            $@"\bSEASON\s+({string.Join("|", Seasons)})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The 8 vanilla calendar festivals use this stable "festival_<season><day>" key — safe to
        // prettify, unlike arbitrary IS_EVENT numbers, which are one-off scene IDs with no name data.
        private static readonly Dictionary<string, string> VanillaFestivalNames = new(StringComparer.OrdinalIgnoreCase)
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

        private static readonly Dictionary<string, string> VanillaFestivalKeysByName =
            VanillaFestivalNames.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

        // Shared by BuildForcedEntry and BuildLocalEntry: turns a category name into the game
        // Condition that reconstructs it (SEASON/IS_EVENT), or null if there's no real equivalent
        // (Beach is handled separately by each caller; a fully custom category also returns null).
        private static string BuildConditionForCategory(string category)
        {
            if (Seasons.Contains(category, StringComparer.OrdinalIgnoreCase))
            {
                return $"SEASON {category}";
            }

            string eventKey = VanillaFestivalKeysByName.TryGetValue(category, out string key)
                ? key
                : category.StartsWith("Event: ", StringComparison.Ordinal) ? category.Substring("Event: ".Length) : null;

            return eventKey is not null ? $"IS_EVENT {eventKey}" : null;
        }

        // Builds the synthetic entry used to force a specific outfit for a category, with a
        // Precedence low enough to beat any real entry (the lowest seen from real content packs is
        // -10000, for island Beach attire).
        public static CharacterAppearanceData BuildForcedEntry(string id, string category, string sprite, string portrait)
        {
            var entry = new CharacterAppearanceData
            {
                Id = id,
                Sprite = sprite,
                Portrait = portrait,
                Precedence = -5_000_000,
                Condition = BuildConditionForCategory(category),
            };

            if (category.Equals("Beach", StringComparison.OrdinalIgnoreCase))
            {
                // Ginger Island's beach resort only — the mainland Beach map swaps textures by
                // filename convention, not through this Condition system.
                entry.IsIslandAttire = true;
            }

            return entry;
        }

        // Same Condition reconstruction as BuildForcedEntry, but at neutral Precedence — a normal
        // pickable option for a local PNG drop, not an automatic override. Beach is left for
        // Classify() to detect from this entry's Id; a fully custom category (e.g. "Wedding") is
        // left unconditional, so forcing it later means "always".
        public static CharacterAppearanceData BuildLocalEntry(string id, string category, string sprite, string portrait)
        {
            return new CharacterAppearanceData
            {
                Id = id,
                Sprite = sprite,
                Portrait = portrait,
                Condition = BuildConditionForCategory(category),
            };
        }

        // Turns a raw filename condition token (e.g. "FlowerDance", "Winter_Indoor") into one of
        // our normal category names, matching known seasons/festivals so these group with whatever
        // Content Patcher provides instead of creating look-alike duplicates.
        public static string NormalizeLocalCondition(string rawCondition)
        {
            string token = rawCondition.Trim();

            foreach (string season in Seasons)
            {
                if (token.StartsWith(season, StringComparison.OrdinalIgnoreCase))
                {
                    return season;
                }
            }

            // StartsWith (not Equals): lets "Beach_v1.png", "Beach_v2.png" etc. group under one
            // "Beach" category instead of each becoming its own single-option category.
            if (token.StartsWith("Beach", StringComparison.OrdinalIgnoreCase))
            {
                return "Beach";
            }

            string normalizedToken = StripToLetters(token);
            foreach (string festivalName in VanillaFestivalNames.Values)
            {
                string normalizedFestival = StripToLetters(festivalName);
                if (normalizedFestival.Contains(normalizedToken, StringComparison.OrdinalIgnoreCase)
                    || normalizedToken.Contains(normalizedFestival, StringComparison.OrdinalIgnoreCase))
                {
                    return festivalName;
                }
            }

            return token;
        }

        private static string StripToLetters(string s) => new(s.Where(char.IsLetter).ToArray());

        public static string Classify(CharacterAppearanceData app)
        {
            if (app.Id == "Default")
            {
                return "Default";
            }

            if (app.IsIslandAttire)
            {
                return "Beach";
            }

            // The mainland beach look isn't flagged by any structured field — the game swaps to it
            // by filename convention, bypassing the Condition system entirely.
            if ((app.Id?.Contains("Beach", StringComparison.OrdinalIgnoreCase) ?? false)
                || (app.Sprite?.Contains("_Beach", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                return "Beach";
            }

            if (app.Season.HasValue)
            {
                return app.Season.Value.ToString();
            }

            string condition = app.Condition ?? string.Empty;

            // Checked before the season keyword below: an event key like "festival_spring13"
            // contains "spring", so a loose season search would misclassify festivals as seasons.
            Match eventMatch = EventIdPattern.Match(condition);
            if (eventMatch.Success)
            {
                // Conditions inside an ANY(...) query come quoted ("IS_EVENT 58"), hence the trim.
                string eventId = eventMatch.Groups[1].Value.Trim(',', '"', '\'', ' ');
                return VanillaFestivalNames.TryGetValue(eventId, out string festivalName)
                    ? festivalName
                    : $"Event: {eventId}";
            }

            Match seasonMatch = SeasonKeywordPattern.Match(condition);
            if (seasonMatch.Success)
            {
                string season = seasonMatch.Groups[1].Value;
                return char.ToUpperInvariant(season[0]) + season.Substring(1).ToLowerInvariant();
            }

            // Local entries (see BuildLocalEntry) with a custom category embed it in their Id as
            // "LocalAsset::{npc}::{category}" — recover it instead of dumping into "Other".
            if (app.Id?.StartsWith("LocalAsset::") == true)
            {
                string[] parts = app.Id.Split("::");
                if (parts.Length >= 3)
                {
                    return parts[2];
                }
            }

            return "Other";
        }

        public static int SortRank(string category) => category switch
        {
            "Spring" => 0,
            "Summer" => 1,
            "Fall" => 2,
            "Winter" => 3,
            "Beach" => 4,
            _ => 5,
        };

        // The full set of conditions a player can assign an outfit to, regardless of NPC: the four
        // seasons, Beach, and the 8 vanilla festivals always, unioned with every custom event/category
        // actually found — not just what one NPC already has, since the point is forcing e.g. a summer
        // look onto winter, or a look you made for the Luau onto the Egg Festival even if nothing has
        // ever defined an Egg Festival appearance for that NPC.
        public static string[] CollectGlobalCategories(IEnumerable<CharacterAppearanceData> allAppearances)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Spring", "Summer", "Fall", "Winter", "Beach",
            };
            found.UnionWith(VanillaFestivalNames.Values);

            foreach (CharacterAppearanceData app in allAppearances)
            {
                string category = Classify(app);
                if (category != "Default")
                {
                    found.Add(category);
                }
            }

            return found
                .OrderBy(SortRank)
                .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
