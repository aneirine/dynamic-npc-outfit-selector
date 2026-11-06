using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewModdingAPI.Toolkit.Serialization;
using StardewValley;
using StardewValley.GameData.Characters;

namespace NpcOutfitSelector.Compat
{
    // Compiled into both the test runner (current code) and tests/Oracle-1.1.1 (the released
    // 1.1.1 sources), so both sides start from the same game data and describe their result the
    // same way. Nothing in here may depend on the mod's own code, since the two sides build
    // different versions of it.
    internal static class CompatScenario
    {
        // NPCs a generated config may name. Sam's Appearance is null and Abigail's is empty, as
        // in vanilla; Nobody isn't in the game at all.
        public static readonly string[] Npcs = { "Haley", "Alex", "Emily", "Sam", "Abigail", "Nobody" };

        // Pack outfits a config may pick. The last of each NPC's is not installed.
        public static string[] PackIds(string npc) =>
            new[] { "A", "B", "C", "D", "Missing" }.Select(letter => $"pack.{npc}.{letter}").ToArray();

        // Custom PNG Ids exactly as 1.1.0 and 1.1.1 built them, plus one that no file matches.
        public static string[] LocalIds(string npc) => npc switch
        {
            "Haley" => new[]
            {
                "LocalAsset::Haley::Beach::Beach",
                "LocalAsset::Haley::Casual::Casual",
                "LocalAsset::Haley::Winter::Weather_Snowing::Where_Outdoor::Winter_Snowing_Outdoor",
                "LocalAsset::Haley::Spring::NoSuchFile",
            },
            "Alex" => new[] { "LocalAsset::Alex::Beach::BeachFull_remake", "LocalAsset::Alex::Fall::NoSuchFile" },
            "Abigail" => new[] { "LocalAsset::Abigail::Casual::Casual" },
            _ => Array.Empty<string>(),
        };

        // Data/Characters as another mod left it, before this mod's edit.
        public static Dictionary<string, CharacterData> Characters()
        {
            var data = new Dictionary<string, CharacterData>();
            foreach (string npc in new[] { "Haley", "Alex", "Emily" })
            {
                string[] packs = PackIds(npc);
                data[npc] = new CharacterData
                {
                    Appearance = new List<CharacterAppearanceData>
                    {
                        new() { Id = packs[0], Season = Season.Summer, Sprite = $"Characters/{npc}_A", Portrait = $"Portraits/{npc}_A", Precedence = -100 },
                        new() { Id = packs[1], Condition = "WEATHER Here Rain", Sprite = $"Characters/{npc}_B", Portrait = $"Portraits/{npc}_B" },
                        new() { Id = packs[2], Sprite = $"Characters/{npc}_C", Portrait = null },
                        new() { Id = packs[3], IsIslandAttire = true, Sprite = $"Characters/{npc}_D", Portrait = $"Portraits/{npc}_D", Weight = 3 },
                    },
                };
            }
            data["Sam"] = new CharacterData { Appearance = null };
            data["Abigail"] = new CharacterData { Appearance = new List<CharacterAppearanceData>() };
            return data;
        }

        // The custom PNGs found in the mod's assets folder. Only Id, Sprite and Portrait are read
        // when a pick is applied.
        public static Dictionary<string, List<CharacterAppearanceData>> LocalAssets()
        {
            var result = new Dictionary<string, List<CharacterAppearanceData>>();
            foreach (string npc in new[] { "Haley", "Alex", "Abigail" })
            {
                result[npc] = LocalIds(npc)
                    .Where(id => !id.EndsWith("::NoSuchFile", StringComparison.Ordinal))
                    .Select(id => new CharacterAppearanceData
                    {
                        Id = id,
                        Sprite = $"Mods/NpcOutfitSelector/LocalAssets/assets/Characters/{id.Split("::")[^1]}",
                        Portrait = null,
                    })
                    .ToList();
            }
            // 1.0.1 spelled a file with a weather or place differently in its Id; the files are
            // the same, so the current form is what the scan produces.
            return result;
        }

        // Everything the game will read from the edited Data/Characters, entry by entry, in list
        // order. Two results that describe the same are the same input to the game.
        public static List<string> Describe(IDictionary<string, CharacterData> data) =>
            data.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SelectMany(pair => pair.Value.Appearance is null
                    ? new[] { $"{pair.Key}: (Appearance null)" }
                    : pair.Value.Appearance.Select(e => $"{pair.Key}: {Describe(e)}"))
                .ToList();

        public static string Describe(CharacterAppearanceData e) =>
            $"{e.Id} | sprite {e.Sprite ?? "(null)"} | portrait {e.Portrait ?? "(null)"} | season {e.Season?.ToString() ?? "-"}"
            + $" | condition {e.Condition ?? "-"} | precedence {e.Precedence} | weight {e.Weight}"
            + $" | in/out {e.Indoors}/{e.Outdoors} | island {e.IsIslandAttire}";

        // SMAPI's JsonHelper with the converters SCore adds at startup (one of them is what parses
        // MenuHotkey). Those are internal to SMAPI, so they're created by name.
        public static JsonHelper CreateSmapiJsonHelper()
        {
            var helper = new JsonHelper();
            foreach (string name in new[] { "ColorConverter", "KeybindConverter", "PointConverter", "Vector2Converter", "RectangleConverter" })
            {
                Type type = typeof(IMonitor).Assembly.GetType($"StardewModdingAPI.Framework.Serialization.{name}", throwOnError: true);
                helper.JsonSettings.Converters.Add((JsonConverter)Activator.CreateInstance(type, nonPublic: true));
            }
            return helper;
        }
    }
}
