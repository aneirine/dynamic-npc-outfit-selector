using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StardewValley.GameData.Characters;

namespace NpcOutfitSelector.Tests
{
    // config.json as a player has it, read through the same path the mod uses in game: SMAPI's
    // JSON reader and converters, then migration. A key passing here keeps what it *means* to the
    // game, which is stricter than keeping its spelling or the number of keys.
    internal static class ConfigFileTests
    {
        public static void Run()
        {
            Assert.Section("Released configs keep every pick through load, save and load");
            RoundTrip("config-1.0.1.json");
            RoundTrip("config-1.1.0.json", expectUnchangedKeys: true);
            RoundTrip("config-1.1.1.json", expectUnchangedKeys: true);

            Assert.Section("Hand-edited configs keep every value, and what 1.1.1 did with them");
            RoundTrip("config-hand-edited.json");
            HandEditedMatchesTheReleasedBuild();
        }

        // load -> migrate -> save -> load -> migrate. Checked two ways: nothing in the file is lost,
        // and every pick does in game what 1.1.1 made it do.
        private static void RoundTrip(string fixture, bool expectUnchangedKeys = false)
        {
            using var folder = new TempFolder();
            var data = new FakeDataHelper(folder.Path);
            File.Copy(FixturePath(fixture), data.FullPath(ConfigStore.FileName));
            List<Pick> written = ReadPicks(data);

            var store = new ConfigStore(data, new FakeMonitor());
            store.Save(store.Load());
            List<Pick> saved = ReadPicks(data);
            ModConfig reloaded = store.Load();

            List<Pick> missing = written
                .Where(p => !saved.Any(s => s.Npc == p.Npc && s.Value == p.Value
                    && (s.Key == p.Key || s.Key == AppearanceCategory.RenameLegacyBase(p.Key))))
                .ToList();
            foreach (Pick pick in missing)
            {
                Assert.Note($"missing from the file: {pick.Npc}/{pick.Key} = {pick.Value}");
            }
            Assert.Equal($"{fixture}: every value stays in the file", missing.Count, 0);
            Assert.Equal($"{fixture}: nothing added to the file", saved.Count, written.Count);

            List<string> expected = AsReleasedBuildApplied(written).Select(p => p.ToString()).ToList();
            List<string> applied = EffectivePicks(reloaded).Select(p => p.ToString()).ToList();
            foreach (string pick in expected.Except(applied))
            {
                Assert.Note($"1.1.1 applied, now doesn't: {pick}");
            }
            foreach (string pick in applied.Except(expected))
            {
                Assert.Note($"now applied, 1.1.1 didn't: {pick}");
            }
            Assert.Equal($"{fixture}: every pick means what it did in 1.1.1",
                expected.Except(applied).Count() + applied.Except(expected).Count(), 0);
            Assert.Equal($"{fixture}: nothing applied twice", applied.Count, expected.Count);

            if (expectUnchangedKeys)
            {
                Assert.Equal($"{fixture}: no key respelled",
                    string.Join(", ", saved.Select(p => $"{p.Npc}/{p.Key}").Except(written.Select(p => $"{p.Npc}/{p.Key}"))), "");
            }

            store.Save(store.Load());
            Assert.Equal($"{fixture}: migrating twice changes nothing",
                string.Join("|", ReadPicks(data).Select(p => $"{p.Npc}/{p.Key}={p.Value}")),
                string.Join("|", saved.Select(p => $"{p.Npc}/{p.Key}={p.Value}")));
        }

        // Captured from the 1.1.1 build reading config-hand-edited.json (its load-time migration),
        // so the test's own model of 1.1.1 below is checked against the real thing once.
        private static void HandEditedMatchesTheReleasedBuild()
        {
            using var folder = new TempFolder();
            var data = new FakeDataHelper(folder.Path);
            File.Copy(FixturePath("config-hand-edited.json"), data.FullPath(ConfigStore.FileName));
            var monitor = new FakeMonitor();
            ModConfig config = new ConfigStore(data, monitor).Load();

            Assert.Equal("Haley, as 1.1.1 applied it", string.Join(" | ", config.GetEffectivePicks("Haley").Select(p => $"{p.Key}={p.Value}")),
                "Weather_Sunny=pack.HaleySunnySpring | Weather_Sunny::Where_Beach=pack.HaleyBeachSun"
                + " | Spring::Weather_Sunny::Where_Beach=pack.HaleySpringBeachSun | winter::Weather_snowing=pack.HaleyWinterSnow"
                + " | Spring=pack.HaleyAnyWeather | Summer::Weather_Raining=pack.HaleyTwoWeathers | Casual=LocalAsset::Haley::Casual::Casual");
            Assert.Equal("Alex, as 1.1.1 applied it", string.Join(" | ", config.GetEffectivePicks("Alex").Select(p => $"{p.Key}={p.Value}")),
                "Island=pack.AlexIsland | Island::Weather_Sunny=pack.AlexLegacyIslandSun"
                + " | Island::Where_Indoor=pack.AlexLegacyIslandIndoor | Weather_Sunny=pack.AlexNotABase");

            Assert.Equal("Haley's keys stay as written", string.Join(", ", config.SelectedOutfits["Haley"].Keys),
                "Weather_Sunny::Spring, Where_Beach::Weather_Sunny, Spring::Where_Beach::Weather_Sunny, winter::weather_snowing, "
                + "Spring::Weather_Any, Any::Weather_Sunny, Summer::Weather_Sunny::Weather_Raining, Spring::Rainy, Casual");
            Assert.Equal("only a lone Beach base is renamed", string.Join(", ", config.SelectedOutfits["Alex"].Keys),
                "Beach, Island, Island::Weather_Sunny, Island::Where_Indoor, Weather_Sunny::Beach");
            Assert.Equal("each ignored key is reported", string.Join(" / ", monitor.Messages.Select(m => m.Message.Split(' ')[1])),
                "\"Any::Weather_Sunny\" / \"Spring::Rainy\" / \"Beach\"");
        }

        // 1.1.0 and 1.1.1's load-time migration, as it was: each key rewritten into
        // EffectiveKey's form, and on a collision the key already in that form wins, else the
        // first. Kept here as the reference the current code must match.
        private static List<Pick> AsReleasedBuildApplied(List<Pick> written)
        {
            var result = new List<Pick>();
            foreach (IGrouping<string, Pick> npc in written.GroupBy(p => p.Npc))
            {
                var byCategory = npc.ToDictionary(p => p.Key, p => p.Value);
                foreach (string key in byCategory.Keys.Where(k => AppearanceCategory.EffectiveKey(k) != k).ToList())
                {
                    string canonical = AppearanceCategory.EffectiveKey(key);
                    string outfitId = byCategory[key];
                    byCategory.Remove(key);
                    if (!byCategory.ContainsKey(canonical))
                    {
                        byCategory[canonical] = outfitId;
                    }
                }
                result.AddRange(byCategory.Select(p => new Pick(npc.Key, p.Key, p.Value)));
            }
            return result;
        }

        // What a key does once it reaches the game, which is what a player would notice changing.
        internal static string Meaning(string key)
        {
            CharacterAppearanceData entry = AppearanceCategory.BuildForcedEntry("test.id", key, "S", "P");
            return $"precedence {entry.Precedence}, condition {entry.Condition ?? "(none)"}, "
                + $"in/out {entry.Indoors}/{entry.Outdoors}, island {entry.IsIslandAttire}";
        }

        internal sealed record Pick(string Npc, string Key, string Value)
        {
            public override string ToString() => $"{this.Npc}: [{Meaning(this.Key)}] = {this.Value ?? "(null)"}";
        }

        // The file as it is on disk, read without the model so nothing in the reader can hide a loss.
        internal static List<Pick> ReadPicks(FakeDataHelper data)
        {
            var picks = new List<Pick>();
            JObject root = data.ReadJsonFile<JObject>(ConfigStore.FileName);
            if (root?["SelectedOutfits"] is JObject outfits)
            {
                foreach (JProperty npc in outfits.Properties())
                {
                    if (npc.Value is JObject byCategory)
                    {
                        picks.AddRange(byCategory.Properties().Select(p => new Pick(npc.Name, p.Name, p.Value.Type == JTokenType.Null ? null : p.Value.ToString())));
                    }
                }
            }
            return picks;
        }

        internal static List<Pick> EffectivePicks(ModConfig config) =>
            config.SelectedOutfits.Keys
                .SelectMany(npc => config.GetEffectivePicks(npc).Select(p => new Pick(npc, p.Key, p.Value)))
                .ToList();

        internal static string FixturePath(string fileName) =>
            Path.Combine(AppContext.BaseDirectory, "fixtures", fileName);
    }
}
