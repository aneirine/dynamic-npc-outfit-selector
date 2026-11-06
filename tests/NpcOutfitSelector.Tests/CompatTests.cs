using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NpcOutfitSelector.Compat;
using StardewValley.GameData.Characters;

namespace NpcOutfitSelector.Tests
{
    // The released 1.1.1 as the reference, not a model of it. Every config in
    // fixtures/generated/old was run through the 1.1.1 sources by tests/Oracle-1.1.1, and what
    // that build handed the game is in expected-1.1.1.json. The current code has to hand the game
    // exactly the same, from the old file and from the file it saves (fixtures/generated/new),
    // and 1.1.1 reading that saved file has to as well (expected-1.1.1-on-new.json), so going
    // back to 1.1.1 after a save changes nothing either.
    internal static class CompatTests
    {
        private static string Dir => Path.Combine(AppContext.BaseDirectory, "fixtures", "generated");

        // Fixes announced in the 1.1.2 changelog: 1.1.1 silently dropped these picks. Each entry
        // is the line the current code adds that 1.1.1 didn't.
        private static readonly Dictionary<string, string[]> KnownFixes = new()
        {
            ["known-fix-101-png.json"] = new[]
            {
                "Haley: NpcOutfitSelector.Force.Haley.Winter | sprite Mods/NpcOutfitSelector/LocalAssets/assets/Characters/Winter_Snowing_Outdoor"
                + " | portrait (null) | season - | condition SEASON Winter | precedence -5000016 | weight 1 | in/out True/True | island False",
            },
        };

        public static void Run()
        {
            Assert.Section("Generated configs: the current code hands the game what the released 1.1.1 did");
            Dictionary<string, List<string>> expected = ReadExpected("expected-1.1.1.json");
            Dictionary<string, List<string>> expectedOnNew = ReadExpected("expected-1.1.1-on-new.json");
            string[] cases = Directory.GetFiles(Path.Combine(Dir, "old"), "*.json").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();

            var oldFailures = new List<string>();
            var newFailures = new List<string>();
            var staleNew = new List<string>();
            var downgradeFailures = new List<string>();
            int entries = 0;

            foreach (string name in cases)
            {
                if (!expected.TryGetValue(name, out List<string> reference))
                {
                    oldFailures.Add($"{name}: no 1.1.1 result; rerun the oracle");
                    continue;
                }
                KnownFixes.TryGetValue(name, out string[] fixes);
                List<string> want = Patch(reference, fixes);
                entries += want.Count;

                (List<string> fromOld, JObject saved) = RunCurrent(Path.Combine(Dir, "old", name));
                Compare(name + " (old file)", fromOld, want, oldFailures);

                string newPath = Path.Combine(Dir, "new", name);
                if (!File.Exists(newPath) || !JToken.DeepEquals(ReadJson(newPath), saved))
                {
                    staleNew.Add(name);
                }
                else
                {
                    Compare(name + " (saved by this version)", RunCurrent(newPath).Described, want, newFailures);
                }

                if (!expectedOnNew.TryGetValue(name, out List<string> onNew))
                {
                    downgradeFailures.Add($"{name}: no 1.1.1 result for the saved file; rerun the oracle");
                }
                else
                {
                    Compare(name + " (saved by this version, read by 1.1.1)", Patch(onNew, fixes), want, downgradeFailures);
                }
            }

            Assert.Note($"{cases.Length} configs, {entries} Data/Characters entries compared line by line");
            Assert.Equal("every case has a 1.1.1 result", cases.Length > 0 && cases.All(expected.ContainsKey), true);
            Assert.Equal("old file: same as 1.1.1", Report(oldFailures), 0);
            Assert.Equal("fixtures/generated/new is what this version saves", string.Join(", ", staleNew), "");
            Assert.Equal("saved file: same as 1.1.1", Report(newFailures), 0);
            Assert.Equal("saved file read by 1.1.1: same as before", Report(downgradeFailures), 0);
            Assert.Equal("known fixes are the only differences", KnownFixes.Keys.All(cases.Contains), true);
        }

        // Load through ConfigStore as the game does, apply, and save the result once the way the
        // menu's Save does.
        internal static (List<string> Described, JObject Saved) RunCurrent(string configPath)
        {
            using var folder = new TempFolder();
            var data = new FakeDataHelper(folder.Path);
            File.Copy(configPath, data.FullPath(ConfigStore.FileName));
            var store = new ConfigStore(data, folder.Path, new FakeMonitor());
            store.Reload();
            if (store.LoadError is not null)
            {
                return (new List<string> { $"LOAD ERROR: {store.LoadError}" }, null);
            }

            Dictionary<string, CharacterData> characters = CompatScenario.Characters();
            ForcedEntries.Apply(characters, store.Config, CompatScenario.LocalAssets());
            List<string> described = CompatScenario.Describe(characters);

            store.Save(store.Config);
            return (described, ReadJson(data.FullPath(ConfigStore.FileName)));
        }

        private static List<string> Patch(List<string> reference, string[] added)
        {
            if (added is null)
            {
                return reference;
            }
            // Each one was the last forced entry inserted for its NPC, so it heads that NPC's list.
            var result = new List<string>(reference);
            foreach (string line in added.Reverse())
            {
                string npc = line.Substring(0, line.IndexOf(':'));
                int first = result.FindIndex(l => l.StartsWith(npc + ": ", StringComparison.Ordinal));
                result.Insert(first < 0 ? result.Count : first, line);
            }
            return result;
        }

        private static void Compare(string label, List<string> actual, List<string> want, List<string> failures)
        {
            if (actual.SequenceEqual(want))
            {
                return;
            }
            failures.Add(label);
            foreach (string line in want.Except(actual).Take(5))
            {
                failures.Add($"    1.1.1 had: {line}");
            }
            foreach (string line in actual.Except(want).Take(5))
            {
                failures.Add($"    now has:   {line}");
            }
            if (!want.Except(actual).Any() && !actual.Except(want).Any())
            {
                failures.Add("    same entries, different order");
            }
        }

        private static int Report(List<string> failures)
        {
            foreach (string line in failures.Take(40))
            {
                Assert.Note(line);
            }
            return failures.Count(line => !line.StartsWith("    ", StringComparison.Ordinal));
        }

        private static Dictionary<string, List<string>> ReadExpected(string fileName)
        {
            string path = Path.Combine(Dir, fileName);
            return File.Exists(path)
                ? JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(File.ReadAllText(path))
                : new Dictionary<string, List<string>>();
        }

        private static JObject ReadJson(string path) => CompatScenario.CreateSmapiJsonHelper().ReadJsonFileIfExists(path, out JObject result) ? result : null;
    }
}
