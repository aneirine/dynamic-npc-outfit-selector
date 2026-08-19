using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StardewValley.GameData.Characters;

namespace NpcOutfitSelector.Tests
{
    // Category keys go into players' config.json and can never be respelled after release, so these
    // cover the format itself, the priority that resolves between keys, and — separately — that
    // everything 1.0.1 could have written still means exactly what it did.
    internal static class CategoryKeyTests
    {
        public static int Run()
        {
            KeyFormat();
            UnknownValues();
            Priority();
            CompiledConditions();
            ReadingDataBack();
            IslandRename();
            BackwardCompatibility();

            return Assert.Report();
        }

        private static void KeyFormat()
        {
            Assert.Section("Compose writes base, then weather, then where");
            Assert.Equal("nothing narrowed", Compose("Spring", "Any", "Any"), "Spring");
            Assert.Equal("weather only", Compose("Spring", "Raining", "Any"), "Spring::Weather_Raining");
            Assert.Equal("where only", Compose("Spring", "Any", "Indoor"), "Spring::Where_Indoor");
            Assert.Equal("both", Compose("Winter", "Snowing", "Outdoor"), "Winter::Weather_Snowing::Where_Outdoor");
            Assert.Equal("exact place", Compose("Spring", "Any", "Beach"), "Spring::Where_Beach");

            Assert.Section("Decompose is the exact inverse");
            foreach (string key in new[]
            {
                "Spring", "Casual", "Spring::Weather_Raining", "Spring::Where_Indoor", "Spring::Where_Beach",
                "Winter::Weather_Snowing::Where_Outdoor", "Egg Festival::Where_Indoor", "Weather_Sunny",
            })
            {
                Assert.Equal($"round-trip {key}", RoundTrip(key), key);
            }

            Assert.Section("Reading ignores order; writing is canonical");
            Assert.Equal("reversed segments", RoundTrip("Spring::Where_Beach::Weather_Sunny"), "Spring::Weather_Sunny::Where_Beach");

            Assert.Section("A key needs no base at all");
            Assert.Equal("weather alone", Compose("", "Sunny", "Any"), "Weather_Sunny");
            Assert.Equal("weather and place", Compose("", "Sunny", "Beach"), "Weather_Sunny::Where_Beach");
            Assert.Equal("no SEASON clause", Forced("Weather_Sunny").Condition, "WEATHER Here Sun");
        }

        // Both axes are open-ended: other mods add maps, and WeatherCondition.Weather in
        // Data/LocationContexts is a plain string, so they can add weather IDs too. The axis prefix
        // is what lets an unfamiliar value through instead of being read as the other axis.
        private static void UnknownValues()
        {
            Assert.Section("Unknown values pass through to the game");
            Assert.Equal("modded weather", Forced("Spring::Weather_RedRain").Condition, "SEASON Spring, WEATHER Here RedRain");
            Assert.True("not mistaken for a place", !Forced("Spring::Weather_RedRain").Condition.Contains("LOCATION_NAME"));
            Assert.Equal("modded place", Forced("Spring::Where_ModdedTown").Condition, "SEASON Spring, LOCATION_NAME Here ModdedTown");
            Assert.Equal("both at once", Forced("Spring::Weather_RedRain::Where_ModdedTown").Condition,
                "SEASON Spring, WEATHER Here RedRain, LOCATION_NAME Here ModdedTown");
            Assert.Equal("modded weather still scores as weather", Score("Spring::Weather_RedRain"), 18);
        }

        // A tie is not survivable: ChooseAppearance gathers every entry at the lowest Precedence and
        // picks among them by day-seeded weighted random, so two keys on one score is a coin flip.
        private static void Priority()
        {
            Assert.Section("Every reachable key scores uniquely");

            var scored = new List<(string Key, int Score)>();
            foreach (string baseCategory in new[] { "", "Casual", "Spring", "Egg Festival" })
            {
                foreach (string weather in new[] { "Any", "Sunny" })
                {
                    foreach (string where in new[] { "Any", "Indoor", "Beach" })
                    {
                        string key = Compose(baseCategory, weather, where);
                        if (key.Length > 0)
                        {
                            scored.Add((key, Score(key)));
                        }
                    }
                }
            }

            foreach ((string key, int score) in scored.OrderBy(row => row.Score))
            {
                Assert.Note($"{score,3}  {key}");
            }

            List<IGrouping<int, (string Key, int Score)>> collisions = scored
                .GroupBy(row => row.Score)
                .Where(group => group.Count() > 1)
                .ToList();

            foreach (IGrouping<int, (string Key, int Score)> group in collisions)
            {
                Assert.Note($"score {group.Key} shared by: {string.Join(" | ", group.Select(row => row.Key))}");
            }

            Assert.Equal("no two keys share a score", collisions.Count, 0);

            Assert.Section("Ordering follows the documented tiers");
            Assert.True("festival beats season", Score("Egg Festival") > Score("Spring"));
            Assert.True("season beats custom", Score("Spring") > Score("Casual"));
            Assert.True("custom beats no base", Score("Casual") > Score("Weather_Sunny"));
            Assert.True("weather beats indoor/outdoor", Score("Spring::Weather_Sunny") > Score("Spring::Where_Indoor"));
            Assert.True("exact place beats weather and indoor combined",
                Score("Spring::Where_Beach") > Score("Spring::Weather_Sunny::Where_Indoor"));
            Assert.True("a category always beats one without",
                Score("Spring") > Score("Weather_Sunny::Where_Beach"));
        }

        private static void CompiledConditions()
        {
            Assert.Section("Where maps to fields, weather and place to queries");

            CharacterAppearanceData indoor = Forced("Spring::Where_Indoor");
            Assert.Equal("indoor keeps the season query", indoor.Condition, "SEASON Spring");
            Assert.Equal("indoor clears Outdoors", $"{indoor.Indoors}/{indoor.Outdoors}", "True/False");

            CharacterAppearanceData place = Forced("Spring::Where_Beach");
            Assert.Equal("a place is a query", place.Condition, "SEASON Spring, LOCATION_NAME Here Beach");
            Assert.Equal("a place leaves the fields alone", $"{place.Indoors}/{place.Outdoors}", "True/True");

            Assert.Equal("festival", Forced("Egg Festival").Condition, "IS_EVENT festival_spring13");
            Assert.Equal("custom category is unconditional", Forced("Casual").Condition, null);
        }

        private static void ReadingDataBack()
        {
            Assert.Section("Pack entries classify into the same keys");
            Assert.Equal("outdoor-only winter",
                Classify(condition: "SEASON Winter", indoors: false, outdoors: true), "Winter::Where_Outdoor");
            Assert.Equal("indoor-only spring rain",
                Classify(condition: "SEASON Spring, WEATHER Here Rain", indoors: true, outdoors: false),
                "Spring::Weather_Raining::Where_Indoor");
            Assert.Equal("a pack naming a location",
                Classify(condition: "SEASON Summer, LOCATION_NAME Here Town"), "Summer::Where_Town");

            Assert.Section("Local asset filenames");
            Assert.Equal("Winter_Indoor", Normalize("Winter_Indoor"), "Winter::Where_Indoor");
            Assert.Equal("Winter_Snowing_Outdoor", Normalize("Winter_Snowing_Outdoor"), "Winter::Weather_Snowing::Where_Outdoor");
            Assert.Equal("lowercase", Normalize("winter_snowing"), "Winter::Weather_Snowing");
            Assert.Equal("plain season", Normalize("Spring"), "Spring");
            Assert.Equal("Spring_Beach", Normalize("Spring_Beach"), "Spring::Where_Beach");
            Assert.Equal("festival abbreviation", Normalize("FlowerDance"), "Flower Dance");
        }

        private static void IslandRename()
        {
            Assert.Section("Island replaces Beach, which is still read");
            Assert.True("Beach reads as island", AppearanceCategory.IsIslandCategory("Beach"));
            Assert.True("Island reads as island", AppearanceCategory.IsIslandCategory("Island"));
            Assert.True("a season does not", !AppearanceCategory.IsIslandCategory("Spring"));
            Assert.True("Island sets island attire", Forced("Island").IsIslandAttire);
            Assert.True("legacy Beach still does", Forced("Beach").IsIslandAttire);
            Assert.Equal("canonicalize Beach", AppearanceCategory.CanonicalizeKey("Beach"), "Island");
            Assert.Equal("canonicalize a narrowed one",
                AppearanceCategory.CanonicalizeKey("Beach::Weather_Sunny"), "Island::Weather_Sunny");
            Assert.Equal("seasons are untouched", AppearanceCategory.CanonicalizeKey("Spring"), "Spring");

            Assert.Section("Legacy keys migrate once, and can't end up duplicated");
            var config = new ModConfig();
            config.SelectedOutfits["Haley"] = new Dictionary<string, string> { ["Beach"] = "outfitA" };
            config.SelectedOutfits["Alex"] = new Dictionary<string, string>
            {
                ["Beach"] = "legacy",
                ["Island"] = "current",
            };

            ModEntry.MigrateLegacyKeys(config);

            Assert.Equal("Beach became Island", string.Join(",", config.SelectedOutfits["Haley"].Keys), "Island");
            Assert.Equal("value survived", config.SelectedOutfits["Haley"]["Island"], "outfitA");
            Assert.Equal("both spellings collapse to one", string.Join(",", config.SelectedOutfits["Alex"].Keys), "Island");
            Assert.Equal("the canonical one wins", config.SelectedOutfits["Alex"]["Island"], "current");
        }

        // Everything below is a key or value 1.0.1 could have written. None of it may change meaning.
        private static void BackwardCompatibility()
        {
            Assert.Section("1.0.1 keys keep their exact spelling and meaning");
            foreach (string legacy in new[]
            {
                "Spring", "Summer", "Fall", "Winter",
                "Egg Festival", "Flower Dance", "Luau", "Dance of the Moonlight Jellies",
                "Stardew Valley Fair", "Spirit's Eve", "Festival of Ice", "Feast of the Winter Star",
            })
            {
                Assert.Equal($"{legacy} round-trips", RoundTrip(legacy), legacy);
            }

            Assert.Equal("Spring condition", Forced("Spring").Condition, "SEASON Spring");
            Assert.Equal("Spring is not narrowed", $"{Forced("Spring").Indoors}/{Forced("Spring").Outdoors}", "True/True");
            Assert.Equal("Egg Festival condition", Forced("Egg Festival").Condition, "IS_EVENT festival_spring13");
            Assert.True("legacy Beach still island attire", Forced("Beach").IsIslandAttire);
            Assert.Equal("legacy Beach has no condition", Forced("Beach").Condition, null);

            // Local-asset Ids are stored as *values* in shipped configs, so their spelling is frozen.
            Assert.Section("1.0.1 local-asset Ids still resolve");
            Assert.Equal("beach asset", Classify(id: "LocalAsset::Haley::Beach::Beach"), "Island");
            Assert.Equal("custom category asset", Classify(id: "LocalAsset::Abigail::Casual::Casual"), "Casual");
            Assert.Equal("a key inside the Id",
                Classify(id: "LocalAsset::Alex::Winter::Weather_Snowing::Winter_Snowing"), "Winter::Weather_Snowing");

            Assert.Section("The real 1.0.1 config on disk");
            string fixture = FindRepoFile(Path.Combine("planning", "test-config-1.0.1.json"));
            if (fixture is null)
            {
                Assert.Equal("fixture found", "missing", "planning/test-config-1.0.1.json");
                return;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(fixture));
            foreach (JsonProperty npc in document.RootElement.GetProperty("SelectedOutfits").EnumerateObject())
            {
                foreach (JsonProperty entry in npc.Value.EnumerateObject())
                {
                    // A legacy spelling is allowed to migrate; after that it must survive verbatim.
                    string canonical = AppearanceCategory.CanonicalizeKey(entry.Name);
                    Assert.Equal($"{npc.Name}/{entry.Name}", RoundTrip(canonical), canonical);
                }
            }
        }

        private static string Compose(string baseCategory, string weather, string where) =>
            AppearanceCategory.Compose(baseCategory, weather, where);

        private static int Score(string key) => AppearanceCategory.ScoreForCategory(key);

        private static string Normalize(string rawCondition) => AppearanceCategory.NormalizeLocalCondition(rawCondition);

        private static CharacterAppearanceData Forced(string key) =>
            AppearanceCategory.BuildForcedEntry("test.id", key, "Characters/X", "Portraits/X");

        private static string RoundTrip(string key)
        {
            (string baseCategory, string weather, string where) = AppearanceCategory.Decompose(key);
            return AppearanceCategory.Compose(baseCategory, weather ?? "Any", where ?? "Any");
        }

        private static string Classify(string condition = null, bool indoors = true, bool outdoors = true, string id = "test.id") =>
            AppearanceCategory.Classify(new CharacterAppearanceData
            {
                Id = id,
                Condition = condition,
                Indoors = indoors,
                Outdoors = outdoors,
                Sprite = "Characters/X",
            });

        // Walks up from the test binary so the fixture is found however the project is run.
        private static string FindRepoFile(string relativePath)
        {
            for (DirectoryInfo dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }
    }
}
