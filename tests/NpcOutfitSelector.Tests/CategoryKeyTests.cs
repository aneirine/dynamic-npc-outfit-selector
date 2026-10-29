using System.Collections.Generic;
using System.Linq;
using StardewValley.GameData.Characters;

namespace NpcOutfitSelector.Tests
{
    // Category keys go into players' config.json and can never be respelled after release, so these
    // cover the format itself, the priority that resolves between keys, and — separately — that
    // everything 1.0.1 could have written still means exactly what it did.
    internal static class CategoryKeyTests
    {
        public static void Run()
        {
            KeyFormat();
            UnknownValues();
            Priority();
            CurrentTies();
            CompiledConditions();
            ReadingDataBack();
            IslandRename();
            BackwardCompatibility();
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

        // Pins today's behaviour where keys DO tie or differ only by case (B4, B5), so the tie fix
        // planned for 1.1.3 shows up here as a deliberate change rather than slipping in.
        private static void CurrentTies()
        {
            Assert.Section("Known ties, pinned until 1.1.3 fixes them");
            Assert.Equal("two custom categories score alike", $"{Score("Casual")}/{Score("Other")}", "8/8");
            Assert.Equal("and neither has a condition", $"{Forced("Casual").Condition ?? "(none)"}/{Forced("Other").Condition ?? "(none)"}", "(none)/(none)");
            Assert.Equal("so their entries tie", Forced("Casual").Precedence == Forced("Other").Precedence, true);
            Assert.Equal("Island and a season share a tier", $"{Score("Island")}/{Score("Spring")}", "16/16");
            Assert.Equal("an unrecognised pack entry files under Other",
                Classify(condition: "PLAYER_HEARTS Current Haley 8"), "Other");

            Assert.Section("Case variants");
            Assert.Equal("lower-case key scores like the canonical one",
                $"{Score("winter::weather_snowing")}/{Score("Winter::Weather_Snowing")}", "18/18");
            Assert.Equal("and keeps its own spelling in the condition",
                Forced("winter::weather_snowing").Condition, "SEASON winter, WEATHER Here Snow");
            var config = new ModConfig();
            config.SetSelectedOutfitId("Haley", "winter::weather_snowing", "x");
            Assert.Equal("the menu doesn't see it as set", config.HasOverride("Haley", "Winter::Weather_Snowing"), false);

            Assert.Section("Keys with no base");
            Assert.Equal("weather only", Score("Weather_Sunny"), 2);
            Assert.Equal("indoor only", Score("Where_Indoor"), 1);
            Assert.Equal("a place only", Score("Where_Beach"), 4);
            CharacterAppearanceData empty = Forced("");
            Assert.Equal("an empty key is unconditional",
                $"{Score("")} {empty.Condition ?? "(none)"} {empty.Indoors}/{empty.Outdoors} {empty.IsIslandAttire}", "0 (none) True/True False");
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

            // The beach look is recognised by the game's "_Beach" texture convention. Matching it
            // as a substring used to sweep in any outfit with "beach" in its name.
            Assert.Section("The beach texture suffix, not any mention of beach");
            Assert.Equal("a beach sprite", ClassifySprite("Characters/Haley_Beach"), "Island");
            Assert.Equal("case insensitive", ClassifySprite("Characters/Haley_beach"), "Island");
            Assert.Equal("a beach portrait", AppearanceCategory.Classify(new CharacterAppearanceData
            {
                Id = "x", Sprite = "Characters/Haley", Portrait = "Portraits/Haley_Beach",
            }), "Island");
            Assert.Equal("an outfit merely named after one",
                ClassifySprite("Characters/Haley_BeachPartyDress", condition: "SEASON Summer"), "Summer");
            Assert.Equal("a beach in the middle of a name",
                ClassifySprite("Characters/Haley_BeachFull_remake", condition: "SEASON Summer"), "Summer");
            Assert.Equal("an Id mentioning beach doesn't count",
                AppearanceCategory.Classify(new CharacterAppearanceData
                {
                    Id = "somepack.BeachDayOutfit", Sprite = "Characters/Haley", Condition = "SEASON Summer",
                }), "Summer");

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
            Assert.Equal("rename Beach", AppearanceCategory.RenameLegacyBase("Beach"), "Island");
            Assert.Equal("rename a narrowed one", AppearanceCategory.RenameLegacyBase("Beach::Weather_Sunny"), "Island::Weather_Sunny");
            Assert.Equal("only the base changes", AppearanceCategory.RenameLegacyBase("beach::weather_sunny::Where_x"), "Island::weather_sunny::Where_x");
            Assert.Equal("seasons are untouched", AppearanceCategory.RenameLegacyBase("Spring"), "Spring");
            Assert.Equal("a place called Beach is not a base", AppearanceCategory.RenameLegacyBase("Spring::Where_Beach"), "Spring::Where_Beach");
            Assert.Equal("nor is a word after the base", AppearanceCategory.RenameLegacyBase("Weather_Sunny::Beach"), "Weather_Sunny::Beach");
            Assert.Equal("nothing else is respelled", AppearanceCategory.RenameLegacyBase("Weather_Sunny::Spring"), "Weather_Sunny::Spring");

            Assert.Section("Legacy keys migrate once, and a collision keeps both values");
            var config = new ModConfig();
            config.SelectedOutfits["Haley"] = new Dictionary<string, string> { ["Beach"] = "outfitA" };
            config.SelectedOutfits["Alex"] = new Dictionary<string, string>
            {
                ["Beach"] = "legacy",
                ["Island"] = "current",
            };

            List<string> warnings = config.MigrateLegacyKeys();

            Assert.Equal("Beach became Island", string.Join(",", config.SelectedOutfits["Haley"].Keys), "Island");
            Assert.Equal("value survived", config.SelectedOutfits["Haley"]["Island"], "outfitA");
            Assert.Equal("both spellings stay in the file", string.Join(",", config.SelectedOutfits["Alex"].Keys), "Beach,Island");
            Assert.Equal("the legacy value is kept", config.SelectedOutfits["Alex"]["Beach"], "legacy");
            Assert.Equal("the canonical one applies, as in 1.1.1", string.Join(",", config.GetEffectivePicks("Alex")), "[Island, current]");
            Assert.Equal("and the ignored one is reported", warnings.Count, 1);
            Assert.Equal("migrating again changes nothing", $"{config.MigrateLegacyKeys().Count} {string.Join(",", config.SelectedOutfits["Alex"].Keys)}", "1 Beach,Island");

            Assert.Section("The menu acts on the slot a key fills, however it's spelled");
            var handEdited = new ModConfig();
            handEdited.SelectedOutfits["Haley"] = new Dictionary<string, string>
            {
                ["Spring::Weather_Any"] = "first",
                ["Spring::Rainy"] = "second",
            };
            Assert.Equal("reads as Spring", handEdited.GetSelectedOutfitId("Haley", "Spring", "natural"), "first");
            handEdited.SetSelectedOutfitId("Haley", "Spring", "picked");
            Assert.Equal("a pick keeps the written key", string.Join(",", handEdited.SelectedOutfits["Haley"]), "[Spring::Weather_Any, picked],[Spring::Rainy, second]");
            handEdited.ClearOverride("Haley", "Spring");
            Assert.Equal("clearing removes every twin", handEdited.SelectedOutfits["Haley"].Count, 0);
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
            // Its sprite has no "_Beach" suffix, so the category has to come from the Id instead.
            Assert.Equal("beach asset with a longer name",
                AppearanceCategory.Classify(new CharacterAppearanceData
                {
                    Id = "LocalAsset::Alex::Beach::BeachFull_remake",
                    Sprite = "Mods/NpcOutfitSelector/LocalAssets/Characters/Alex_BeachFull_remake",
                }), "Island");
            Assert.Equal("custom category asset", Classify(id: "LocalAsset::Abigail::Casual::Casual"), "Casual");
            Assert.Equal("a key inside the Id",
                Classify(id: "LocalAsset::Alex::Winter::Weather_Snowing::Winter_Snowing"), "Winter::Weather_Snowing");

            // The fixture files themselves are read through the real load path in ConfigFileTests.
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

        private static string ClassifySprite(string sprite, string condition = null) =>
            AppearanceCategory.Classify(new CharacterAppearanceData
            {
                Id = "test.id",
                Condition = condition,
                Sprite = sprite,
            });

        private static string Classify(string condition = null, bool indoors = true, bool outdoors = true, string id = "test.id") =>
            AppearanceCategory.Classify(new CharacterAppearanceData
            {
                Id = id,
                Condition = condition,
                Indoors = indoors,
                Outdoors = outdoors,
                Sprite = "Characters/X",
            });

    }
}
