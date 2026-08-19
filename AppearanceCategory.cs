using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StardewValley.GameData.Characters;

namespace NpcOutfitSelector
{
    // Buckets an appearance entry by what makes it apply, read off its own data, and turns a
    // player's picked category back into the game data that enforces it.
    //
    // A category is a "::"-joined key: Base[::Weather][::Location], e.g. "Winter::Snowing::Outdoor".
    // A bare base is exactly what 1.0.1 wrote, so old configs keep working untouched.
    internal static class AppearanceCategory
    {
        // Shared with ModEntry — both must agree on these exact values since they're
        // embedded in synthetic appearance Ids and in players' saved config.json.
        internal const string DefaultAppearanceId = "Default";
        internal const string LocalAssetIdPrefix = "LocalAsset::";

        internal const string KeySeparator = "::";
        internal const string AnyOption = "Any";

        private const string IndoorValue = "Indoor";
        private const string OutdoorValue = "Outdoor";

        // Marks a segment as an exact place rather than the coarse Indoor/Outdoor. Both share one
        // slot in the key, because naming a location already answers indoors-or-outdoors — every
        // GameLocation is one or the other, so "Beach::Outdoor" would be redundant.
        //
        // The prefix is what lets an unlisted name still work: anything after "@" is passed to
        // LOCATION_NAME as-is, so a modded location typed into config.json by hand parses fine
        // even though the dropdown never offered it.
        internal const string ExactPlacePrefix = "@";

        // The places villagers actually go. Not exhaustive on purpose — see ExactPlacePrefix.
        private static readonly string[] KnownPlaces =
        {
            "Farm", "Town", "Beach", "Mountain", "Forest", "Desert", "Railroad", "BusStop", "Woods",
            "IslandSouth", "IslandNorth", "IslandWest",
            "Saloon", "SeedShop", "Hospital", "Blacksmith", "AnimalShop", "FishShop", "JojaMart",
            "ScienceHouse", "ManorHouse", "ArchaeologyHouse", "CommunityCenter", "Mine",
        };

        private static readonly string[] Seasons = { "Spring", "Summer", "Fall", "Winter" };

        // Three separate things, deliberately not collapsed into one string:
        //
        //   key       what a category key and a local-asset filename contain — kept space-free so
        //             both stay easy to type and to split
        //   constant  what the game's own WEATHER query compares against
        //   label     what the dropdown displays, via GMCM's formatChoice
        //
        // Only the key is permanent: it goes into players' config.json.
        private static readonly Dictionary<string, string> WeatherConstantsByKey = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Sunny"] = "Sun",
            ["Raining"] = "Rain",
            ["Lightning"] = "Storm",
            ["Snowing"] = "Snow",
            ["GreenRain"] = "GreenRain",
            ["Windy"] = "Wind",
        };

        // Only the keys that don't already read well; everything else displays as-is.
        private static readonly Dictionary<string, string> WeatherLabelsByKey = new(StringComparer.OrdinalIgnoreCase)
        {
            ["GreenRain"] = "Green Rain",
        };

        private static readonly Dictionary<string, string> WeatherKeysByConstant =
            WeatherConstantsByKey.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);

        public static readonly string[] WeatherChoices =
            new[] { AnyOption }.Concat(WeatherConstantsByKey.Keys).ToArray();

        public static string FormatWeatherChoice(string key) =>
            WeatherLabelsByKey.TryGetValue(key ?? string.Empty, out string label) ? label : key;

        // Null for weathers with no key of their own (Festival, Wedding) — the game sets those
        // itself and they aren't offered as choices.
        public static string WeatherKeyForConstant(string weatherConstant) =>
            WeatherKeysByConstant.TryGetValue(weatherConstant ?? string.Empty, out string key) ? key : null;

        public static string LocationKeyFor(bool isOutdoors) => isOutdoors ? OutdoorValue : IndoorValue;

        // One axis at two granularities: coarse first, then the exact places.
        //
        // Places found in the loaded save are merged in, so locations added by other mods (Stardew
        // Valley Expanded and the like) appear on their own. KnownPlaces is the floor, not the
        // ceiling — it's what's offered on the title screen, where no save is loaded yet.
        public static string[] BuildWhereChoices(IEnumerable<string> discoveredPlaces)
        {
            var places = new SortedSet<string>(KnownPlaces, StringComparer.OrdinalIgnoreCase);
            if (discoveredPlaces is not null)
            {
                places.UnionWith(discoveredPlaces.Where(p => !string.IsNullOrWhiteSpace(p)));
            }

            return new[] { AnyOption, IndoorValue, OutdoorValue }
                .Concat(places.Select(p => ExactPlacePrefix + p))
                .ToArray();
        }

        // "@BusStop" reads as "Bus Stop" in the dropdown; the stored key keeps the exact location
        // name, since that's what LOCATION_NAME has to match.
        public static string FormatWhereChoice(string key)
        {
            if (key is null || !key.StartsWith(ExactPlacePrefix, StringComparison.Ordinal))
            {
                return key;
            }

            string place = key.Substring(ExactPlacePrefix.Length);
            return Regex.Replace(place, "(?<=[a-z])(?=[A-Z])", " ");
        }

        private static bool IsExactPlace(string where) =>
            where is not null && where.StartsWith(ExactPlacePrefix, StringComparison.Ordinal);

        private static bool IsCoarseWhere(string where) =>
            IndoorValue.Equals(where, StringComparison.OrdinalIgnoreCase)
            || OutdoorValue.Equals(where, StringComparison.OrdinalIgnoreCase);

        private static readonly Regex EventIdPattern = new(@"IS_EVENT\s+(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SeasonKeywordPattern = new(
            $@"\bSEASON\s+({string.Join("|", Seasons)})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WeatherPattern = new(
            @"\bWEATHER\s+\S+\s+([A-Za-z]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LocationNamePattern = new(
            @"\bLOCATION_NAME\s+\S+\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Label prefix for a non-vanilla IS_EVENT id — no display name data exists for those.
        private const string CustomEventLabelPrefix = "Event: ";

        // Base for every forced entry, offset by how specific the category is (see ScoreForCategory).
        // Far below the lowest Precedence real content packs use (-10000, for island beach attire).
        private const int ForcedPrecedenceBase = -5_000_000;

        public static string Compose(string baseCategory, string weather, string location)
        {
            string key = baseCategory;
            if (!IsAny(weather))
            {
                key += KeySeparator + weather;
            }
            if (!IsAny(location))
            {
                key += KeySeparator + location;
            }
            return key;
        }

        private static bool IsAny(string value) =>
            string.IsNullOrWhiteSpace(value) || value.Equals(AnyOption, StringComparison.OrdinalIgnoreCase);

        // Segments are identified by what they are, not where they sit — the weather and location
        // vocabularies share no words, so "Spring::Indoor" parses without a placeholder for the
        // skipped weather.
        public static (string Base, string Weather, string Where) Decompose(string category)
        {
            string[] parts = (category ?? string.Empty).Split(KeySeparator);
            string weather = null;
            string where = null;

            for (int i = 1; i < parts.Length; i++)
            {
                if (IsCoarseWhere(parts[i]) || IsExactPlace(parts[i]))
                {
                    where = parts[i];
                }
                else if (WeatherConstantsByKey.ContainsKey(parts[i]))
                {
                    weather = parts[i];
                }
            }

            return (parts[0], weather, where);
        }

        // How specific a category is. Higher wins, because a longer key describes a narrower
        // situation. The base tier keeps a festival above any season regardless of segments —
        // without it "Egg Festival" and "Spring" would tie, and a tie is decided by the game's
        // weighted random, not by order.
        //
        // The segment weights are a bitmask (1/2/4), so every combination scores uniquely and no
        // two keys can tie by construction. An exact place outranks any mix of coarser conditions
        // because 4 > 2 + 1 — "at the beach" is more specific than "outdoors in the rain".
        public static int ScoreForCategory(string category)
        {
            (string baseCategory, string weather, string where) = Decompose(category);

            int score = Seasons.Contains(baseCategory, StringComparer.OrdinalIgnoreCase) ? 10
                : IsEventCategory(baseCategory) ? 20
                : baseCategory.Equals("Beach", StringComparison.OrdinalIgnoreCase) ? 10
                : 0;

            if (weather is not null)
            {
                score += 2;
            }
            if (IsExactPlace(where))
            {
                score += 4;
            }
            else if (where is not null)
            {
                score += 1;
            }

            return score;
        }

        private static bool IsEventCategory(string baseCategory) =>
            VanillaFestivals.EventIdsByName.ContainsKey(baseCategory)
            || baseCategory.StartsWith(CustomEventLabelPrefix, StringComparison.Ordinal);

        // Turns a category key back into a game Condition, or null when there's no equivalent —
        // Beach is handled separately by each caller, custom categories stay unconditional.
        // Multiple queries are comma-joined, which the game ANDs.
        private static string BuildConditionForCategory(string category)
        {
            (string baseCategory, string weather, string where) = Decompose(category);
            var queries = new List<string>();

            if (Seasons.Contains(baseCategory, StringComparer.OrdinalIgnoreCase))
            {
                queries.Add($"SEASON {baseCategory}");
            }
            else
            {
                string eventKey = VanillaFestivals.EventIdsByName.TryGetValue(baseCategory, out string key)
                    ? key
                    : baseCategory.StartsWith(CustomEventLabelPrefix, StringComparison.Ordinal)
                        ? baseCategory.Substring(CustomEventLabelPrefix.Length)
                        : null;

                if (eventKey is not null)
                {
                    queries.Add($"IS_EVENT {eventKey}");
                }
            }

            if (weather is not null && WeatherConstantsByKey.TryGetValue(weather, out string weatherConstant))
            {
                // "Here" resolves to the NPC's own current location, which is what
                // NPC.ChooseAppearance passes as the query context.
                queries.Add($"WEATHER Here {weatherConstant}");
            }

            if (IsExactPlace(where))
            {
                // A real query, unlike Indoor/Outdoor — so the entry's own Indoors/Outdoors fields
                // stay untouched; the place name already constrains both.
                queries.Add($"LOCATION_NAME Here {where.Substring(ExactPlacePrefix.Length)}");
            }

            return queries.Count > 0 ? string.Join(", ", queries) : null;
        }

        // Mirrors an "::Indoor"/"::Outdoor" choice onto the entry's actual Indoors/Outdoors fields —
        // both default to true on CharacterAppearanceData, so without this the restriction the
        // category promises would silently never take effect in-game. This is not a game state
        // query; ChooseAppearance checks these fields separately.
        private static void ApplyLocationRestriction(CharacterAppearanceData entry, string category)
        {
            (_, _, string where) = Decompose(category);

            if (IndoorValue.Equals(where, StringComparison.OrdinalIgnoreCase))
            {
                entry.Outdoors = false;
            }
            else if (OutdoorValue.Equals(where, StringComparison.OrdinalIgnoreCase))
            {
                entry.Indoors = false;
            }
        }

        // Builds the synthetic entry used to force a specific outfit for a category.
        public static CharacterAppearanceData BuildForcedEntry(string id, string category, string sprite, string portrait)
        {
            var entry = new CharacterAppearanceData
            {
                Id = id,
                Sprite = sprite,
                Portrait = portrait,
                Precedence = ForcedPrecedenceBase - ScoreForCategory(category),
                Condition = BuildConditionForCategory(category),
            };

            if (Decompose(category).Base.Equals("Beach", StringComparison.OrdinalIgnoreCase))
            {
                // Ginger Island's beach resort only — the mainland Beach map swaps textures by
                // filename convention, not through this Condition system.
                entry.IsIslandAttire = true;
            }

            ApplyLocationRestriction(entry, category);

            return entry;
        }

        // Same as BuildForcedEntry but at neutral Precedence — a normal pickable option for a
        // local PNG drop, not an automatic override.
        public static CharacterAppearanceData BuildLocalEntry(string id, string category, string sprite, string portrait)
        {
            var entry = new CharacterAppearanceData
            {
                Id = id,
                Sprite = sprite,
                Portrait = portrait,
                Condition = BuildConditionForCategory(category),
            };

            ApplyLocationRestriction(entry, category);

            return entry;
        }

        // Turns a raw filename condition token (e.g. "FlowerDance", "Winter_Snowing_Indoor") into a
        // category key, matching known seasons/festivals so these group with whatever Content
        // Patcher provides instead of creating look-alike duplicates.
        public static string NormalizeLocalCondition(string rawCondition)
        {
            string token = rawCondition.Trim();

            foreach (string season in Seasons)
            {
                if (!token.StartsWith(season, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string[] extras = token.Substring(season.Length).Split('_', StringSplitOptions.RemoveEmptyEntries);

                string weather = extras
                    .FirstOrDefault(e => WeatherConstantsByKey.ContainsKey(e));

                // A named place beats Indoor/Outdoor for the same reason it does elsewhere: it's
                // the more specific of the two, and they share one slot.
                string place = KnownPlaces.FirstOrDefault(
                    p => extras.Any(e => e.Equals(p, StringComparison.OrdinalIgnoreCase)));
                string location = place is not null ? ExactPlacePrefix + place
                    : extras.Any(e => e.StartsWith(IndoorValue, StringComparison.OrdinalIgnoreCase)) ? IndoorValue
                    : extras.Any(e => e.StartsWith(OutdoorValue, StringComparison.OrdinalIgnoreCase)) ? OutdoorValue
                    : null;

                // Normalize casing so "winter_snowing" and "Winter_Snowing" are one category.
                if (weather is not null)
                {
                    weather = WeatherConstantsByKey.Keys.First(k => k.Equals(weather, StringComparison.OrdinalIgnoreCase));
                }

                return Compose(season, weather, location);
            }

            // StartsWith (not Equals): lets "Beach_v1.png", "Beach_v2.png" etc. group under one
            // "Beach" category instead of each becoming its own single-option category.
            if (token.StartsWith("Beach", StringComparison.OrdinalIgnoreCase))
            {
                return "Beach";
            }

            string normalizedToken = StripToLetters(token);
            foreach (string festivalName in VanillaFestivals.NamesByEventId.Values)
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
            if (app.Id == DefaultAppearanceId)
            {
                return DefaultAppearanceId;
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

            return Compose(ClassifyBase(app), ClassifyWeather(app), ClassifyLocation(app));
        }

        private static string ClassifyBase(CharacterAppearanceData app)
        {
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
                return VanillaFestivals.NamesByEventId.TryGetValue(eventId, out string festivalName)
                    ? festivalName
                    : $"{CustomEventLabelPrefix}{eventId}";
            }

            Match seasonMatch = SeasonKeywordPattern.Match(condition);
            if (seasonMatch.Success)
            {
                string season = seasonMatch.Groups[1].Value;
                return char.ToUpperInvariant(season[0]) + season.Substring(1).ToLowerInvariant();
            }

            // Local entries (see BuildLocalEntry) embed their category in their Id as
            // "LocalAsset::{npc}::{category}::{rawCondition}" — recover it instead of dumping into
            // "Other". Everything between the npc and the raw condition, since the category can
            // itself contain the separator now.
            if (app.Id?.StartsWith(LocalAssetIdPrefix) == true)
            {
                string[] parts = app.Id.Split(KeySeparator);
                if (parts.Length >= 4)
                {
                    return string.Join(KeySeparator, parts[2..^1]);
                }
            }

            return "Other";
        }

        private static string ClassifyLocation(CharacterAppearanceData app)
        {
            // An exact place wins the slot: it's the more specific of the two, and a pack that
            // names one has already decided indoors-or-outdoors by naming it.
            Match placeMatch = LocationNamePattern.Match(app.Condition ?? string.Empty);
            if (placeMatch.Success)
            {
                return ExactPlacePrefix + placeMatch.Groups[1].Value.Trim(',', '"', '\'', ' ');
            }

            if (!app.Indoors && app.Outdoors)
            {
                return OutdoorValue;
            }
            if (app.Indoors && !app.Outdoors)
            {
                return IndoorValue;
            }
            return null;
        }

        private static string ClassifyWeather(CharacterAppearanceData app)
        {
            Match match = WeatherPattern.Match(app.Condition ?? string.Empty);
            if (!match.Success)
            {
                return null;
            }

            string weatherConstant = match.Groups[1].Value.Trim(',', '"', '\'', ' ');
            return WeatherKeysByConstant.TryGetValue(weatherConstant, out string label) ? label : null;
        }

        public static int SortRank(string category) =>
            Decompose(category).Base switch
            {
                "Spring" => 0,
                "Summer" => 1,
                "Fall" => 2,
                "Winter" => 3,
                "Beach" => 4,
                _ => 5,
            };

        // Every base category a player can assign an outfit to, regardless of NPC: seasons + Beach +
        // vanilla festivals always, plus whatever custom ones are actually found — so you can force
        // e.g. a summer look onto winter even if no NPC has ever defined one for winter.
        //
        // Bases only: weather and location are picked from their own dropdowns and combined into
        // the key at selection time, so listing every combination here would multiply this list.
        public static string[] CollectGlobalCategories(IEnumerable<CharacterAppearanceData> allAppearances)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Spring", "Summer", "Fall", "Winter", "Beach",
            };
            found.UnionWith(VanillaFestivals.NamesByEventId.Values);

            foreach (CharacterAppearanceData app in allAppearances)
            {
                string category = Classify(app);
                if (category != DefaultAppearanceId)
                {
                    found.Add(Decompose(category).Base);
                }
            }

            return found
                .OrderBy(SortRank)
                .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
