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
            Assert.Section("Released configs keep every pick's meaning through load, save and load");
            RoundTrip("config-1.0.1.json");
            RoundTrip("config-1.1.0.json", expectUnchangedKeys: true);
            RoundTrip("config-1.1.1.json", expectUnchangedKeys: true);
        }

        // load -> migrate -> save -> load -> migrate, then compare every pick with the file as written.
        private static void RoundTrip(string fixture, bool expectUnchangedKeys = false)
        {
            using var folder = new TempFolder();
            var data = new FakeDataHelper(folder.Path);
            File.Copy(FixturePath(fixture), data.FullPath(ConfigStore.FileName));
            List<Pick> written = ReadPicks(data);

            var store = new ConfigStore(data, new FakeMonitor());
            store.Save(store.Load());
            ModConfig reloaded = store.Load();
            List<Pick> after = Picks(reloaded);

            List<string> lost = written.Select(p => p.ToString()).Except(after.Select(p => p.ToString())).ToList();
            List<string> gained = after.Select(p => p.ToString()).Except(written.Select(p => p.ToString())).ToList();
            foreach (string pick in lost)
            {
                Assert.Note($"lost or changed: {pick}");
            }
            foreach (string pick in gained)
            {
                Assert.Note($"appeared: {pick}");
            }
            Assert.Equal($"{fixture}: every pick keeps its meaning", lost.Count + gained.Count, 0);
            Assert.Equal($"{fixture}: no pick lost or duplicated", after.Count, written.Count);

            if (expectUnchangedKeys)
            {
                Assert.Equal($"{fixture}: no key respelled",
                    string.Join(", ", after.Select(p => $"{p.Npc}/{p.Key}").Except(written.Select(p => $"{p.Npc}/{p.Key}"))), "");
            }

            ModConfig again = store.Load();
            again.MigrateLegacyKeys();
            Assert.Equal($"{fixture}: migrating twice changes nothing",
                string.Join("|", Picks(again).Select(p => $"{p.Npc}/{p.Key}={p.Value}")),
                string.Join("|", after.Select(p => $"{p.Npc}/{p.Key}={p.Value}")));
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

        internal static List<Pick> Picks(ModConfig config) =>
            config.SelectedOutfits
                .Where(npc => npc.Value is not null)
                .SelectMany(npc => npc.Value.Select(p => new Pick(npc.Key, p.Key, p.Value)))
                .ToList();

        internal static string FixturePath(string fileName) =>
            Path.Combine(AppContext.BaseDirectory, "fixtures", fileName);
    }
}
