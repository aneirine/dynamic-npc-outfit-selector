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

        private static readonly string[] Seasons = { "Spring", "Summer", "Fall", "Winter" };

        // Player-facing weather names (what the dropdown shows and what lands in config.json)
        // mapped to the game's own weather constants used inside a WEATHER query.
        private static readonly Dictionary<string, string> WeatherConstantsByLabel = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Sunny"] = "Sun",
            ["Raining"] = "Rain",
            ["Lightning"] = "Storm",
            ["Snowing"] = "Snow",
            ["GreenRain"] = "GreenRain",
            ["Windy"] = "Wind",
        };

        private static readonly Dictionary<string, string> WeatherLabelsByConstant =
            WeatherConstantsByLabel.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);

        public static readonly string[] WeatherChoices =
            new[] { AnyOption }.Concat(WeatherConstantsByLabel.Keys).ToArray();

        public static readonly string[] LocationChoices = { AnyOption, IndoorValue, OutdoorValue };

        private static readonly Regex EventIdPattern = new(@"IS_EVENT\s+(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SeasonKeywordPattern = new(
            $@"\bSEASON\s+({string.Join("|", Seasons)})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WeatherPattern = new(
            @"\bWEATHER\s+\S+\s+([A-Za-z]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
        public static (string Base, string Weather, string Location) Decompose(string category)
        {
            string[] parts = (category ?? string.Empty).Split(KeySeparator);
            string weather = null;
            string location = null;

            for (int i = 1; i < parts.Length; i++)
            {
                if (parts[i].Equals(IndoorValue, StringComparison.OrdinalIgnoreCase)
                    || parts[i].Equals(OutdoorValue, StringComparison.OrdinalIgnoreCase))
                {
                    location = parts[i];
                }
                else if (WeatherConstantsByLabel.ContainsKey(parts[i]))
                {
                    weather = parts[i];
                }
            }

            return (parts[0], weather, location);
        }

        // How specific a category is. Higher wins, because a longer key describes a narrower
        // situation. The base tier keeps a festival above any season regardless of segments —
        // without it "Egg Festival" and "Spring" would tie, and a tie is decided by the game's
        // weighted random, not by order.
        //
        // Weather outranks location so two-segment keys can't tie either.
        public static int ScoreForCategory(string category)
        {
            (string baseCategory, string weather, string location) = Decompose(category);

            int score = Seasons.Contains(baseCategory, StringComparer.OrdinalIgnoreCase) ? 10
                : IsEventCategory(baseCategory) ? 20
                : baseCategory.Equals("Beach", StringComparison.OrdinalIgnoreCase) ? 10
                : 0;

            if (weather is not null)
            {
                score += 2;
            }
            if (location is not null)
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
            (string baseCategory, string weather, _) = Decompose(category);
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

            if (weather is not null && WeatherConstantsByLabel.TryGetValue(weather, out string weatherConstant))
            {
                // "Here" resolves to the NPC's own current location, which is what
                // NPC.ChooseAppearance passes as the query context.
                queries.Add($"WEATHER Here {weatherConstant}");
            }

            return queries.Count > 0 ? string.Join(", ", queries) : null;
        }

        // Mirrors an "::Indoor"/"::Outdoor" choice onto the entry's actual Indoors/Outdoors fields —
        // both default to true on CharacterAppearanceData, so without this the restriction the
        // category promises would silently never take effect in-game. This is not a game state
        // query; ChooseAppearance checks these fields separately.
        private static void ApplyLocationRestriction(CharacterAppearanceData entry, string category)
        {
            (_, _, string location) = Decompose(category);

            if (IndoorValue.Equals(location, StringComparison.OrdinalIgnoreCase))
            {
                entry.Outdoors = false;
            }
            else if (OutdoorValue.Equals(location, StringComparison.OrdinalIgnoreCase))
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
                    .FirstOrDefault(e => WeatherConstantsByLabel.ContainsKey(e));
                string location = extras.Any(e => e.StartsWith(IndoorValue, StringComparison.OrdinalIgnoreCase)) ? IndoorValue
                    : extras.Any(e => e.StartsWith(OutdoorValue, StringComparison.OrdinalIgnoreCase)) ? OutdoorValue
                    : null;

                // Normalize casing so "winter_snowing" and "Winter_Snowing" are one category.
                if (weather is not null)
                {
                    weather = WeatherConstantsByLabel.Keys.First(k => k.Equals(weather, StringComparison.OrdinalIgnoreCase));
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
            return WeatherLabelsByConstant.TryGetValue(weatherConstant, out string label) ? label : null;
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
