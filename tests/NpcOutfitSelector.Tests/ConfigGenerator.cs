using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NpcOutfitSelector.Compat;

namespace NpcOutfitSelector.Tests
{
    // Writes fixtures/generated: old/ holds configs in the shapes released versions wrote or a
    // player could hand-edit, new/ the same configs after a Save by the current code. Run with
    // `--generate <fixtures/generated>`, then run tests/Oracle-1.1.1 over both folders.
    // Keys are spelled out here rather than built with the mod's helpers, so a change to those
    // helpers can't quietly change the inputs too.
    internal static class ConfigGenerator
    {
        private const int Seed = 11_2;
        private const int CasesPerShape = 40;

        private static readonly string[] Seasons = { "Spring", "Summer", "Fall", "Winter" };
        private static readonly string[] Festivals =
        {
            "Egg Festival", "Flower Dance", "Luau", "Dance of the Moonlight Jellies", "Stardew Valley Fair",
            "Spirit's Eve", "Festival of Ice", "Feast of the Winter Star",
        };
        private static readonly string[] CustomBases = { "Casual", "Wedding" };
        private static readonly string[] Weathers = { "Sunny", "Raining", "Lightning", "Snowing", "GreenRain", "Windy", "Foggy" };
        private static readonly string[] Wheres = { "Indoor", "Outdoor", "Town", "Saloon", "Beach", "IslandSouth", "MyModPlace" };

        public static void Generate(string dir)
        {
            string oldDir = Path.Combine(dir, "old");
            string newDir = Path.Combine(dir, "new");
            foreach (string folder in new[] { oldDir, newDir })
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
                Directory.CreateDirectory(folder);
            }

            var random = new Random(Seed);
            var shapes = new (string Name, string About, Func<Random, string> Key)[]
            {
                ("101", "1.0.1 shape: a bare base per key, Beach for island attire", OldKey),
                ("111", "1.1.0/1.1.1 shape: base, weather and place as the menu writes them", MenuKey),
                ("hand", "hand-edited: case, segment order, Any, repeated axes, Beach and Island, twins", HandKey),
            };
            foreach ((string shape, string about, Func<Random, string> makeKey) in shapes)
            {
                for (int i = 1; i <= CasesPerShape; i++)
                {
                    Write(Path.Combine(oldDir, $"gen-{shape}-{i:000}.json"), $"Generated (seed {Seed}), {about}.", Config(random, makeKey, shape));
                }
            }

            // The real fixtures too, as they are.
            foreach (string fixture in new[] { "config-1.0.1.json", "config-1.1.0.json", "config-1.1.1.json", "config-hand-edited.json" })
            {
                File.Copy(ConfigFileTests.FixturePath(fixture), Path.Combine(oldDir, "real-" + fixture.Substring("config-".Length)));
            }

            // A 1.0.1 pick of a custom PNG with a weather in its name: the Id 1.0.1 saved no
            // longer matches the file's, which 1.1.1 dropped and 1.1.2 finds by file name.
            Write(Path.Combine(oldDir, "known-fix-101-png.json"), "1.0.1 Id for Haley_Winter_Snowing_Outdoor.png (fixed in 1.1.2).",
                new JObject { ["SelectedOutfits"] = new JObject { ["Haley"] = new JObject
                {
                    // Last, so its forced entry is the one at the head of Haley's list.
                    ["Summer"] = "pack.Haley.A",
                    ["Winter"] = "LocalAsset::Haley::Winter::Winter_Snowing_Outdoor",
                } } });

            foreach (string oldPath in Directory.GetFiles(oldDir, "*.json"))
            {
                (_, JObject saved) = CompatTests.RunCurrent(oldPath);
                if (saved is null)
                {
                    throw new InvalidOperationException($"{oldPath} did not load or save");
                }
                File.WriteAllText(Path.Combine(newDir, Path.GetFileName(oldPath)), saved.ToString(Formatting.Indented));
            }
            Console.WriteLine($"Wrote {Directory.GetFiles(oldDir).Length} configs to {oldDir} and their saved form to {newDir}.");
        }

        private static JObject Config(Random random, Func<Random, string> makeKey, string shape)
        {
            var outfits = new JObject();
            foreach (string npc in CompatScenario.Npcs.OrderBy(_ => random.Next()).Take(random.Next(0, CompatScenario.Npcs.Length + 1)))
            {
                var byCategory = new JObject();
                int count = random.Next(0, 7);
                for (int k = 0; k < count; k++)
                {
                    string key = makeKey(random);
                    if (byCategory.Property(key, StringComparison.Ordinal) is null)
                    {
                        byCategory[key] = Value(random, npc);
                    }
                    // A second spelling of a key already there: both slots are filled.
                    if (shape == "hand" && random.Next(4) == 0)
                    {
                        string twin = Twin(random, key);
                        if (byCategory.Property(twin, StringComparison.Ordinal) is null)
                        {
                            byCategory[twin] = Value(random, npc);
                        }
                    }
                }
                outfits[npc] = byCategory;
            }

            var root = new JObject { ["SelectedOutfits"] = outfits };
            switch (shape == "101" ? 0 : random.Next(4))
            {
                case 1: root["MenuHotkey"] = "F9"; break;
                case 2: root["MenuHotkey"] = "LeftShift + F9"; break;
                case 3: root["MenuHotkey"] = ""; break;
            }
            if (shape == "hand" && random.Next(3) == 0)
            {
                root["SomeFutureSetting"] = new JObject { ["Enabled"] = true };
            }
            return root;
        }

        private static string Value(Random random, string npc)
        {
            string[] pool = CompatScenario.PackIds(npc)
                .Concat(CompatScenario.LocalIds(npc))
                .Append("Default")
                .ToArray();
            return Pick(random, pool);
        }

        private static string OldKey(Random random) => random.Next(5) switch
        {
            0 or 1 => Pick(random, Seasons),
            2 => Pick(random, Festivals),
            3 => "Beach",
            _ => Pick(random, CustomBases),
        };

        private static string MenuKey(Random random)
        {
            string baseCategory = random.Next(6) switch
            {
                0 or 1 => Pick(random, Seasons),
                2 => Pick(random, Festivals),
                3 => "Island",
                4 => Pick(random, CustomBases),
                _ => "",
            };
            var segments = new List<string>();
            if (baseCategory != "")
            {
                segments.Add(baseCategory);
            }
            if (random.Next(2) == 0)
            {
                segments.Add("Weather_" + Pick(random, Weathers));
            }
            if (random.Next(2) == 0 || segments.Count == 0)
            {
                segments.Add("Where_" + Pick(random, Wheres));
            }
            return string.Join("::", segments);
        }

        private static string HandKey(Random random)
        {
            List<string> segments = MenuKey(random).Split("::").ToList();
            if (segments[0] == "Island" && random.Next(2) == 0)
            {
                segments[0] = random.Next(2) == 0 ? "Beach" : "beach";
            }
            switch (random.Next(6))
            {
                case 0: segments.Add("Weather_Any"); break;
                case 1: segments.Insert(0, "Any"); break;
                case 2: segments.Add("Weather_" + Pick(random, Weathers)); break;
                case 3: segments.Add("Rainy"); break;
            }
            if (random.Next(3) == 0)
            {
                segments = segments.OrderBy(_ => random.Next()).ToList();
            }
            if (random.Next(3) == 0)
            {
                segments = segments.Select(s => random.Next(2) == 0 ? s.ToLowerInvariant() : s).ToList();
            }
            return string.Join("::", segments);
        }

        // Another spelling a player might use for the same slot.
        private static string Twin(Random random, string key)
        {
            string[] segments = key.Split("::");
            return random.Next(3) switch
            {
                0 => key.ToUpperInvariant(),
                1 => string.Join("::", segments.Reverse()),
                _ => key + "::Where_Any",
            };
        }

        private static string Pick(Random random, string[] options) => options[random.Next(options.Length)];

        private static void Write(string path, string comment, JObject config) =>
            File.WriteAllText(path, $"// {comment}\n{config.ToString(Formatting.Indented)}\n");
    }
}
