using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StardewValley.GameData.Characters;

namespace NpcOutfitSelector
{
    // Buckets an appearance entry by what makes it apply (season/beach/festival), read off its own data.
    internal static class AppearanceCategory
    {
        // Shared with ModEntry — both must agree on these exact values since they're
        // embedded in synthetic appearance Ids and in players' saved config.json.
        internal const string DefaultAppearanceId = "Default";
        internal const string LocalAssetIdPrefix = "LocalAsset::";

        private static readonly string[] Seasons = { "Spring", "Summer", "Fall", "Winter" };

        private static readonly Regex EventIdPattern = new(@"IS_EVENT\s+(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SeasonKeywordPattern = new(
            $@"\bSEASON\s+({string.Join("|", Seasons)})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Label prefix for a non-vanilla IS_EVENT id — no display name data exists for those.
        private const string CustomEventLabelPrefix = "Event: ";

        // Appended to a base category (e.g. "Winter") when an entry is restricted to one context
        // via the Indoors/Outdoors fields — the real mechanism some packs use for e.g. a coat worn
        // outside in winter vs. regular clothes inside, independent of Condition/Season.
        private const string IndoorSuffix = " (Indoor)";
        private const string OutdoorSuffix = " (Outdoor)";

        private static string StripIndoorOutdoorSuffix(string category)
        {
            if (category.EndsWith(IndoorSuffix, StringComparison.Ordinal))
            {
                return category.Substring(0, category.Length - IndoorSuffix.Length);
            }
            if (category.EndsWith(OutdoorSuffix, StringComparison.Ordinal))
            {
                return category.Substring(0, category.Length - OutdoorSuffix.Length);
            }
            return category;
        }

        // Turns a category name back into a game Condition (SEASON/IS_EVENT), or null if there's
        // no real equivalent — Beach is handled separately by each caller, custom categories stay unconditional.
        private static string BuildConditionForCategory(string category)
        {
            string baseCategory = StripIndoorOutdoorSuffix(category);

            if (Seasons.Contains(baseCategory, StringComparer.OrdinalIgnoreCase))
            {
                return $"SEASON {baseCategory}";
            }

            string eventKey = VanillaFestivals.EventIdsByName.TryGetValue(baseCategory, out string key)
                ? key
                : baseCategory.StartsWith(CustomEventLabelPrefix, StringComparison.Ordinal)
                    ? baseCategory.Substring(CustomEventLabelPrefix.Length)
                    : null;

            return eventKey is not null ? $"IS_EVENT {eventKey}" : null;
        }

        // Mirrors a "<category> (Indoor)"/"(Outdoor)" choice onto the synthetic entry's actual
        // Indoors/Outdoors fields — both default to true on CharacterAppearanceData, so without this
        // the restriction the category name promises would silently never take effect in-game.
        private static void ApplyIndoorOutdoorRestriction(CharacterAppearanceData entry, string category)
        {
            if (category.EndsWith(IndoorSuffix, StringComparison.Ordinal))
            {
                entry.Outdoors = false;
            }
            else if (category.EndsWith(OutdoorSuffix, StringComparison.Ordinal))
            {
                entry.Indoors = false;
            }
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

            ApplyIndoorOutdoorRestriction(entry, category);

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

            ApplyIndoorOutdoorRestriction(entry, category);

            return entry;
        }

        // Turns a raw filename condition token (e.g. "FlowerDance", "Winter_Indoor") into one of
        // our normal category names, matching known seasons/festivals so these group with whatever
        // Content Patcher provides instead of creating look-alike duplicates.
        public static string NormalizeLocalCondition(string rawCondition)
        {
            string token = rawCondition.Trim();

            foreach (string season in Seasons)
            {
                if (!token.StartsWith(season, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string remainder = token.Substring(season.Length);
                if (remainder.Contains("Indoor", StringComparison.OrdinalIgnoreCase))
                {
                    return season + IndoorSuffix;
                }
                if (remainder.Contains("Outdoor", StringComparison.OrdinalIgnoreCase))
                {
                    return season + OutdoorSuffix;
                }
                return season;
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

            return ApplyIndoorOutdoorSuffix(ClassifyBase(app), app);
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

            // Local entries (see BuildLocalEntry) with a custom category embed it in their Id as
            // "LocalAsset::{npc}::{category}" — recover it instead of dumping into "Other".
            if (app.Id?.StartsWith(LocalAssetIdPrefix) == true)
            {
                string[] parts = app.Id.Split("::");
                if (parts.Length >= 3)
                {
                    return parts[2];
                }
            }

            return "Other";
        }

        // Appends the Indoor/Outdoor suffix when an entry is restricted to one context (see
        // ApplyIndoorOutdoorRestriction) — e.g. a Winter_Indoor/Winter_Outdoor pair shows up as two
        // separate pickable categories instead of merging into one "Winter".
        private static string ApplyIndoorOutdoorSuffix(string category, CharacterAppearanceData app)
        {
            if (!app.Indoors && app.Outdoors)
            {
                return category + OutdoorSuffix;
            }
            if (app.Indoors && !app.Outdoors)
            {
                return category + IndoorSuffix;
            }
            return category;
        }

        public static int SortRank(string category)
        {
            string baseCategory = StripIndoorOutdoorSuffix(category);
            return baseCategory switch
            {
                "Spring" => 0,
                "Summer" => 1,
                "Fall" => 2,
                "Winter" => 3,
                "Beach" => 4,
                _ => 5,
            };
        }

        // Every category a player can assign an outfit to, regardless of NPC: seasons + Beach +
        // vanilla festivals always, plus whatever custom categories are actually found — so you can
        // force e.g. a summer look onto winter even if no NPC has ever defined one for winter.
        public static string[] CollectGlobalCategories(IEnumerable<CharacterAppearanceData> allAppearances)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Spring", "Summer", "Fall", "Winter", "Beach",
                "Winter" + IndoorSuffix, "Winter" + OutdoorSuffix,
            };
            found.UnionWith(VanillaFestivals.NamesByEventId.Values);

            foreach (CharacterAppearanceData app in allAppearances)
            {
                string category = Classify(app);
                if (category != DefaultAppearanceId)
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
