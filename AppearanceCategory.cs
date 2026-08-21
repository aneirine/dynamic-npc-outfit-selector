using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StardewValley.GameData.Characters;

namespace NpcOutfitSelector
{
    // Reads what makes an appearance entry apply, and turns a player's picked category back into
    // the game data that enforces it. Key syntax and constants live in AppearanceConstants.
    internal static class AppearanceCategory
    {
        private static readonly Regex EventIdPattern = new(@"IS_EVENT\s+(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SeasonKeywordPattern = new(
            $@"\bSEASON\s+({string.Join("|", AppearanceConstants.Seasons)})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WeatherPattern = new(
            @"\bWEATHER\s+\S+\s+([A-Za-z]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LocationNamePattern = new(
            @"\bLOCATION_NAME\s+\S+\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Splits a run-together place name for display only: "BusStop" reads as "Bus Stop" while
        // the stored key keeps the exact name, since that's what LOCATION_NAME has to match.
        private static readonly Regex CamelCaseBoundary = new("(?<=[a-z])(?=[A-Z])", RegexOptions.Compiled);

        public static readonly string[] WeatherChoices =
            new[] { AppearanceConstants.AnyOption }.Concat(AppearanceConstants.WeatherConstantsByKey.Keys).ToArray();

        public static string FormatWeatherChoice(string key) =>
            AppearanceConstants.WeatherLabelsByKey.TryGetValue(key ?? string.Empty, out string label) ? label : key;

        // Null for weathers with no key of their own (Festival, Wedding) — the game sets those
        // itself and they aren't offered as choices.
        public static string WeatherKeyForConstant(string weatherConstant) =>
            AppearanceConstants.WeatherKeysByConstant.TryGetValue(weatherConstant ?? string.Empty, out string key)
                ? key
                : null;

        public static string LocationKeyFor(bool isOutdoors) =>
            isOutdoors ? AppearanceConstants.OutdoorValue : AppearanceConstants.IndoorValue;

        // Coarse values first, then places. Merging what the save actually has is what makes other
        // mods' locations show up without this mod knowing them.
        public static string[] BuildWhereChoices(IEnumerable<string> discoveredPlaces)
        {
            var places = new SortedSet<string>(AppearanceConstants.KnownPlaces, StringComparer.OrdinalIgnoreCase);
            if (discoveredPlaces is not null)
            {
                places.UnionWith(discoveredPlaces.Where(p => !string.IsNullOrWhiteSpace(p)));
            }

            return new[] { AppearanceConstants.AnyOption, AppearanceConstants.IndoorValue, AppearanceConstants.OutdoorValue }
                .Concat(places)
                .ToArray();
        }

        // Dropdown values are bare ("Snowing", "Beach"); the axis prefix belongs to the key and is
        // added by Compose, so nothing in the UI has to know about it.
        public static string FormatWhereChoice(string value) =>
            value is null ? null : CamelCaseBoundary.Replace(value, " ");

        private static bool IsCoarseWhere(string where) =>
            AppearanceConstants.IndoorValue.Equals(where, StringComparison.OrdinalIgnoreCase)
            || AppearanceConstants.OutdoorValue.Equals(where, StringComparison.OrdinalIgnoreCase);

        // Anything in the Where slot that isn't the closed Indoor/Outdoor pair names a real map.
        private static bool IsExactPlace(string where) => where is not null && !IsCoarseWhere(where);

        // Both spellings mean island attire; only the canonical one is ever written.
        public static bool IsIslandCategory(string baseCategory) =>
            AppearanceConstants.IslandCategory.Equals(baseCategory, StringComparison.OrdinalIgnoreCase)
            || AppearanceConstants.LegacyIslandCategory.Equals(baseCategory, StringComparison.OrdinalIgnoreCase);

        private static string CanonicalBase(string baseCategory) =>
            IsIslandCategory(baseCategory) ? AppearanceConstants.IslandCategory : baseCategory;

        // Rewrites a key onto the canonical spelling of its base, leaving everything else alone.
        // Used to migrate a config once rather than carrying two equivalent keys, which would tie
        // and be resolved by the game's weighted random.
        public static string CanonicalizeKey(string key)
        {
            (string baseCategory, string weather, string where) = Decompose(key);
            return Compose(CanonicalBase(baseCategory), weather ?? AppearanceConstants.AnyOption,
                where ?? AppearanceConstants.AnyOption);
        }

        private static bool IsAny(string value) =>
            string.IsNullOrWhiteSpace(value)
            || value.Equals(AppearanceConstants.AnyOption, StringComparison.OrdinalIgnoreCase);

        // Always written base-first, then weather, then where — though Decompose accepts any order.
        // A blank base is allowed: the result applies in every season.
        public static string Compose(string baseCategory, string weather, string where)
        {
            var segments = new List<string>();

            if (!IsAny(baseCategory))
            {
                segments.Add(baseCategory);
            }
            if (!IsAny(weather))
            {
                segments.Add(AppearanceConstants.WeatherPrefix + weather);
            }
            if (!IsAny(where))
            {
                segments.Add(AppearanceConstants.WherePrefix + where);
            }

            return string.Join(AppearanceConstants.KeySeparator, segments);
        }

        // Order doesn't matter: each segment names its axis. An unrecognised value is kept rather
        // than dropped, since its axis is known from the prefix even when the value isn't.
        public static (string Base, string Weather, string Where) Decompose(string category)
        {
            string[] parts = (category ?? string.Empty).Split(AppearanceConstants.KeySeparator);
            string baseCategory = string.Empty;
            string weather = null;
            string where = null;

            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].StartsWith(AppearanceConstants.WeatherPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    weather = parts[i].Substring(AppearanceConstants.WeatherPrefix.Length);
                }
                else if (parts[i].StartsWith(AppearanceConstants.WherePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    where = parts[i].Substring(AppearanceConstants.WherePrefix.Length);
                }
                else if (i == 0)
                {
                    baseCategory = parts[i];
                }
            }

            return (baseCategory, weather, where);
        }

        // How specific a category is; higher wins. See AppearanceConstants for the weights and why
        // they're powers of two.
        public static int ScoreForCategory(string category)
        {
            (string baseCategory, string weather, string where) = Decompose(category);

            int score = string.IsNullOrWhiteSpace(baseCategory) ? AppearanceConstants.NoBaseScore
                : AppearanceConstants.Seasons.Contains(baseCategory, StringComparer.OrdinalIgnoreCase)
                    ? AppearanceConstants.SeasonBaseScore
                : IsEventCategory(baseCategory) ? AppearanceConstants.FestivalBaseScore
                : IsIslandCategory(baseCategory)
                    ? AppearanceConstants.SeasonBaseScore
                : AppearanceConstants.CustomBaseScore;

            if (weather is not null)
            {
                score += AppearanceConstants.WeatherScore;
            }
            if (IsExactPlace(where))
            {
                score += AppearanceConstants.ExactPlaceScore;
            }
            else if (where is not null)
            {
                score += AppearanceConstants.CoarseWhereScore;
            }

            return score;
        }

        private static bool IsEventCategory(string baseCategory) =>
            AppearanceConstants.FestivalEventIdsByName.ContainsKey(baseCategory)
            || baseCategory.StartsWith(AppearanceConstants.CustomEventLabelPrefix, StringComparison.Ordinal);

        // Turns a category key back into a game Condition, or null when there's no equivalent —
        // Beach is handled separately by each caller, custom categories stay unconditional.
        // Multiple queries are comma-joined, which the game ANDs.
        private static string BuildConditionForCategory(string category)
        {
            (string baseCategory, string weather, string where) = Decompose(category);
            var queries = new List<string>();

            if (AppearanceConstants.Seasons.Contains(baseCategory, StringComparer.OrdinalIgnoreCase))
            {
                queries.Add($"SEASON {baseCategory}");
            }
            else
            {
                string eventKey = AppearanceConstants.FestivalEventIdsByName.TryGetValue(baseCategory, out string key)
                    ? key
                    : baseCategory.StartsWith(AppearanceConstants.CustomEventLabelPrefix, StringComparison.Ordinal)
                        ? baseCategory.Substring(AppearanceConstants.CustomEventLabelPrefix.Length)
                        : null;

                if (eventKey is not null)
                {
                    queries.Add($"IS_EVENT {eventKey}");
                }
            }

            if (weather is not null)
            {
                // "Here" is the NPC's own location — that's the context ChooseAppearance passes.
                string weatherConstant =
                    AppearanceConstants.WeatherConstantsByKey.TryGetValue(weather, out string known)
                        ? known
                        : weather;
                queries.Add($"WEATHER Here {weatherConstant}");
            }

            if (IsExactPlace(where))
            {
                // A real query, unlike Indoor/Outdoor — so the entry's own Indoors/Outdoors fields
                // stay untouched; the place name already constrains both.
                queries.Add($"LOCATION_NAME Here {where}");
            }

            return queries.Count > 0 ? string.Join(", ", queries) : null;
        }

        // Indoor/Outdoor is not a game state query: ChooseAppearance checks these two fields
        // separately, and both default to true, so the restriction has to be written into them.
        private static void ApplyLocationRestriction(CharacterAppearanceData entry, string category)
        {
            (_, _, string where) = Decompose(category);

            if (AppearanceConstants.IndoorValue.Equals(where, StringComparison.OrdinalIgnoreCase))
            {
                entry.Outdoors = false;
            }
            else if (AppearanceConstants.OutdoorValue.Equals(where, StringComparison.OrdinalIgnoreCase))
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
                Precedence = AppearanceConstants.ForcedPrecedenceBase - ScoreForCategory(category),
                Condition = BuildConditionForCategory(category),
            };

            if (IsIslandCategory(Decompose(category).Base))
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

        // Turns a raw filename condition token (e.g. "FlowerDance", "Winter_Snowing_Outdoor") into a
        // category key, matching known seasons/festivals so these group with whatever Content
        // Patcher provides instead of creating look-alike duplicates.
        public static string NormalizeLocalCondition(string rawCondition)
        {
            string token = rawCondition.Trim();

            foreach (string season in AppearanceConstants.Seasons)
            {
                if (!token.StartsWith(season, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string[] extras = token.Substring(season.Length).Split('_', StringSplitOptions.RemoveEmptyEntries);

                string weather = AppearanceConstants.WeatherConstantsByKey.Keys.FirstOrDefault(
                    k => extras.Any(e => e.Equals(k, StringComparison.OrdinalIgnoreCase)));

                // A filename carries no axis prefixes, so unlike a hand-written key it can only
                // name a place already known — a bare word is otherwise indistinguishable.
                string place = AppearanceConstants.KnownPlaces.FirstOrDefault(
                    p => extras.Any(e => e.Equals(p, StringComparison.OrdinalIgnoreCase)));

                string where = place
                    ?? (extras.Any(e => e.StartsWith(AppearanceConstants.IndoorValue, StringComparison.OrdinalIgnoreCase))
                        ? AppearanceConstants.IndoorValue
                        : extras.Any(e => e.StartsWith(AppearanceConstants.OutdoorValue, StringComparison.OrdinalIgnoreCase))
                            ? AppearanceConstants.OutdoorValue
                            : null);

                return Compose(season, weather, where);
            }

            // StartsWith (not Equals): lets "Beach_v1.png", "Beach_v2.png" etc. group under one
            // "Beach" category instead of each becoming its own single-option category.
            if (token.StartsWith("Beach", StringComparison.OrdinalIgnoreCase))
            {
                return "Beach";
            }

            string normalizedToken = StripToLetters(token);
            foreach (string festivalName in AppearanceConstants.FestivalNamesByEventId.Values)
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
            if (app.Id == AppearanceConstants.DefaultAppearanceId)
            {
                return AppearanceConstants.DefaultAppearanceId;
            }

            if (app.IsIslandAttire)
            {
                return AppearanceConstants.IslandCategory;
            }

            // No structured field marks the beach look; the game swaps to a "_Beach" texture by
            // filename convention. Matched as a suffix, not a substring, so an outfit merely named
            // something like "BeachPartyDress" isn't swept in.
            if (HasBeachTextureSuffix(app.Sprite) || HasBeachTextureSuffix(app.Portrait))
            {
                return AppearanceConstants.IslandCategory;
            }

            return Compose(ClassifyBase(app), ClassifyWeather(app), ClassifyLocation(app));
        }

        // True for "Characters/Haley_Beach", false for "Characters/Haley_BeachPartyDress".
        private static bool HasBeachTextureSuffix(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            int lastSeparator = assetPath.LastIndexOfAny(new[] { '/', '\\' });
            string fileName = assetPath.Substring(lastSeparator + 1);
            return fileName.EndsWith("_Beach", StringComparison.OrdinalIgnoreCase);
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
                return AppearanceConstants.FestivalNamesByEventId.TryGetValue(eventId, out string festivalName)
                    ? festivalName
                    : $"{AppearanceConstants.CustomEventLabelPrefix}{eventId}";
            }

            Match seasonMatch = SeasonKeywordPattern.Match(condition);
            if (seasonMatch.Success)
            {
                string season = seasonMatch.Groups[1].Value;
                return char.ToUpperInvariant(season[0]) + season.Substring(1).ToLowerInvariant();
            }

            // A local entry's Id is "LocalAsset::{npc}::{category}::{rawCondition}". Everything
            // between npc and rawCondition, since the category can itself contain the separator.
            if (app.Id?.StartsWith(AppearanceConstants.LocalAssetIdPrefix) == true)
            {
                string[] parts = app.Id.Split(AppearanceConstants.KeySeparator);
                if (parts.Length >= 4)
                {
                    return CanonicalBase(string.Join(AppearanceConstants.KeySeparator, parts[2..^1]));
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
                return placeMatch.Groups[1].Value.Trim(',', '"', '\'', ' ');
            }

            if (!app.Indoors && app.Outdoors)
            {
                return AppearanceConstants.OutdoorValue;
            }
            if (app.Indoors && !app.Outdoors)
            {
                return AppearanceConstants.IndoorValue;
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

            // Falls back to the raw value so a pack using another mod's weather still classifies
            // into its own bucket instead of vanishing into the plain season.
            string constant = match.Groups[1].Value.Trim(',', '"', '\'', ' ');
            return WeatherKeyForConstant(constant) ?? constant;
        }

        public static int SortRank(string category) =>
            Decompose(category).Base switch
            {
                "Spring" => 0,
                "Summer" => 1,
                "Fall" => 2,
                "Winter" => 3,
                "Island" => 4,
                _ => 5,
            };

        // Seeded with the full set rather than only what each NPC has, so a summer look can be
        // forced onto winter. Bases only — the other axes have their own dropdowns, and listing
        // every combination here would multiply this list.
        public static string[] CollectGlobalCategories(IEnumerable<CharacterAppearanceData> allAppearances)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Spring", "Summer", "Fall", "Winter", AppearanceConstants.IslandCategory,
            };
            found.UnionWith(AppearanceConstants.FestivalNamesByEventId.Values);

            foreach (CharacterAppearanceData app in allAppearances)
            {
                string category = Classify(app);
                if (category != AppearanceConstants.DefaultAppearanceId)
                {
                    found.Add(CanonicalBase(Decompose(category).Base));
                }
            }

            return found
                .OrderBy(SortRank)
                .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
