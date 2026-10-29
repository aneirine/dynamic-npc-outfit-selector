using System.Collections.Generic;
using System.IO;
using System.Linq;
using StardewValley;
using StardewValley.GameData.Characters;

namespace NpcOutfitSelector.Tests
{
    // From a saved pick to the entry the game actually evaluates: which sprite and portrait, at
    // what Precedence, under what Condition. This is the edit OnAssetRequested makes to
    // Data/Characters, run against a hand-built copy of it.
    internal static class ForcedEntryTests
    {
        private const string LocalBeachId = "LocalAsset::Haley::Beach::Beach";
        private const string LocalBeachSprite = "Mods/NpcOutfitSelector/LocalAssets/assets/Characters/Haley_Beach";

        public static void Run()
        {
            OnePickOfEachKind();
            OldAndHandEditedPicks();
            EveryFixture();
        }

        private static void OldAndHandEditedPicks()
        {
            const string snowSprite = "Mods/NpcOutfitSelector/LocalAssets/assets/Characters/Haley_Winter_Snowing_Outdoor";
            var local = new Dictionary<string, List<CharacterAppearanceData>>
            {
                ["Haley"] = new List<CharacterAppearanceData>
                {
                    AppearanceCategory.BuildLocalEntry(
                        "LocalAsset::Haley::Winter::Weather_Snowing::Where_Outdoor::Winter_Snowing_Outdoor",
                        "Winter::Weather_Snowing::Where_Outdoor", snowSprite, null),
                },
            };

            var config = new ModConfig();
            config.SelectedOutfits["Haley"] = new Dictionary<string, string>
            {
                ["Winter"] = "LocalAsset::Haley::Winter::Winter_Snowing_Outdoor",
                ["Spring"] = null,
                ["Island"] = "pack.HaleySummer",
                ["Beach"] = "pack.Legacy",
            };

            Dictionary<string, CharacterData> data = Characters();
            ForcedEntries.Apply(data, config, local);
            List<CharacterAppearanceData> forced = data["Haley"].Appearance.Where(e => e.Id.StartsWith(ForcedEntries.IdPrefix)).ToList();

            Assert.Section("Picks saved by older versions, or by hand");
            Assert.Equal("a 1.0.1 custom PNG Id finds its file", forced.FirstOrDefault(e => e.Id.EndsWith(".Winter"))?.Sprite, snowSprite);
            Assert.Equal("a null outfit forces nothing", forced.Any(e => e.Id.EndsWith(".Spring")), false);
            Assert.Equal("two keys for one slot force one entry", string.Join(", ", forced.Select(e => $"{e.Id}={e.Sprite}")),
                "NpcOutfitSelector.Force.Haley.Island=Characters/Haley_PackSummer, NpcOutfitSelector.Force.Haley.Winter=" + snowSprite);
        }

        private static void OnePickOfEachKind()
        {
            var config = new ModConfig();
            config.SetSelectedOutfitId("Sam", "Fall", AppearanceConstants.DefaultAppearanceId);
            config.SetSelectedOutfitId("Haley", "Summer::Weather_Sunny", "pack.HaleySummer");
            config.SetSelectedOutfitId("Haley", "Island", LocalBeachId);
            config.SetSelectedOutfitId("Haley", "Winter", "pack.RemovedSinceSaved");
            config.SetSelectedOutfitId("Nobody", "Spring", AppearanceConstants.DefaultAppearanceId);

            Dictionary<string, CharacterData> data = Characters();
            ForcedEntries.Apply(data, config, LocalOptions());

            Assert.Section("Default forces the NPC's own base textures");
            Assert.Equal("Sam", Describe(data["Sam"].Appearance[0]),
                "NpcOutfitSelector.Force.Sam.Fall | Characters/Sam | Portraits/Sam | -5000016 | SEASON Fall | True/True | island False");
            Assert.Equal("Sam gets nothing else", data["Sam"].Appearance.Count, 1);

            Assert.Section("A pack outfit and a custom PNG, newest pick first");
            List<CharacterAppearanceData> haley = data["Haley"].Appearance;
            Assert.Equal("custom PNG for Island", Describe(haley[0]),
                $"NpcOutfitSelector.Force.Haley.Island | {LocalBeachSprite} | (null) | -5000016 | (none) | True/True | island True");
            Assert.Equal("pack outfit for sunny summer", Describe(haley[1]),
                "NpcOutfitSelector.Force.Haley.Summer::Weather_Sunny | Characters/Haley_PackSummer | Portraits/Haley_PackSummer | -5000018 | SEASON Summer, WEATHER Here Sun | True/True | island False");
            Assert.Equal("the pack's own entry stays, after them", haley[2].Id, "pack.HaleySummer");

            Assert.Section("An outfit that no longer exists, or an NPC that doesn't");
            Assert.Equal("is skipped, not forced", haley.Count(e => e.Id.EndsWith(".Winter")), 0);
            Assert.Equal("Haley has exactly the two real ones", haley.Count, 3);
            Assert.Equal("an unknown NPC isn't added", data.ContainsKey("Nobody"), false);
        }

        // Each fixture against a copy of the game data that has every outfit it names, then one
        // that has none of them: only Default and custom PNGs can still apply.
        private static void EveryFixture()
        {
            Assert.Section("Every pick in every loadable fixture becomes one forced entry");
            foreach (string fixture in new[] { "config-1.0.1.json", "config-1.1.0.json", "config-1.1.1.json" })
            {
                ModConfig config = Load(fixture);
                var everyOutfit = new Dictionary<string, CharacterData>();
                var local = new Dictionary<string, List<CharacterAppearanceData>>();
                foreach ((string npc, Dictionary<string, string> picks) in config.SelectedOutfits)
                {
                    var appearances = new List<CharacterAppearanceData>();
                    var localList = new List<CharacterAppearanceData>();
                    foreach (string outfitId in picks.Values.Distinct())
                    {
                        var source = new CharacterAppearanceData { Id = outfitId, Sprite = $"src/{outfitId}", Portrait = $"portrait/{outfitId}" };
                        if (outfitId.StartsWith(AppearanceConstants.LocalAssetIdPrefix))
                        {
                            localList.Add(source);
                        }
                        else if (outfitId != AppearanceConstants.DefaultAppearanceId)
                        {
                            appearances.Add(source);
                        }
                    }
                    everyOutfit[npc] = new CharacterData { Appearance = appearances };
                    local[npc] = localList;
                }

                ForcedEntries.Apply(everyOutfit, config, local);
                var problems = new List<string>();
                foreach ((string npc, Dictionary<string, string> picks) in config.SelectedOutfits)
                {
                    foreach ((string key, string outfitId) in picks)
                    {
                        List<CharacterAppearanceData> forced = everyOutfit[npc].Appearance
                            .Where(e => e.Id == $"{ForcedEntries.IdPrefix}{npc}.{key}").ToList();
                        CharacterAppearanceData expected = AppearanceCategory.BuildForcedEntry("x", key, null, null);
                        string sprite = outfitId == AppearanceConstants.DefaultAppearanceId ? $"Characters/{npc}" : $"src/{outfitId}";
                        if (forced.Count != 1
                            || forced[0].Sprite != sprite
                            || forced[0].Precedence != expected.Precedence
                            || forced[0].Condition != expected.Condition
                            || forced[0].Indoors != expected.Indoors
                            || forced[0].Outdoors != expected.Outdoors
                            || forced[0].IsIslandAttire != expected.IsIslandAttire)
                        {
                            problems.Add($"{npc}/{key}");
                        }
                    }
                }
                Assert.Equal($"{fixture}: all present", string.Join(", ", problems), "");

                ModConfig again = Load(fixture);
                var nothing = again.SelectedOutfits.Keys.ToDictionary(npc => npc, _ => new CharacterData());
                ForcedEntries.Apply(nothing, again, new Dictionary<string, List<CharacterAppearanceData>>());
                int expectedCount = again.SelectedOutfits.Values.Sum(p => p.Values.Count(id => id == AppearanceConstants.DefaultAppearanceId));
                Assert.Equal($"{fixture}: with every outfit gone, only Default applies",
                    nothing.Values.Sum(c => c.Appearance.Count), expectedCount);
            }
        }

        private static ModConfig Load(string fixture)
        {
            using var folder = new TempFolder();
            var data = new FakeDataHelper(folder.Path);
            File.Copy(ConfigFileTests.FixturePath(fixture), data.FullPath(ConfigStore.FileName));
            return ConfigFileTests.Load(data, new FakeMonitor());
        }

        private static Dictionary<string, CharacterData> Characters() => new()
        {
            ["Haley"] = new CharacterData
            {
                Appearance = new List<CharacterAppearanceData>
                {
                    new() { Id = "pack.HaleySummer", Season = Season.Summer, Sprite = "Characters/Haley_PackSummer", Portrait = "Portraits/Haley_PackSummer" },
                },
            },
            ["Sam"] = new CharacterData { Appearance = null },
        };

        private static Dictionary<string, List<CharacterAppearanceData>> LocalOptions() => new()
        {
            ["Haley"] = new List<CharacterAppearanceData>
            {
                AppearanceCategory.BuildLocalEntry(LocalBeachId, "Beach", LocalBeachSprite, null),
            },
        };

        private static string Describe(CharacterAppearanceData e) =>
            $"{e.Id} | {e.Sprite} | {e.Portrait ?? "(null)"} | {e.Precedence} | {e.Condition ?? "(none)"} | {e.Indoors}/{e.Outdoors} | island {e.IsIslandAttire}";
    }
}
