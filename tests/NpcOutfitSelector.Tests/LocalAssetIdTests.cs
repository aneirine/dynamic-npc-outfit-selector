using System.Collections.Generic;
using System.IO;
using System.Linq;
using StardewValley.GameData.Characters;

namespace NpcOutfitSelector.Tests
{
    // A custom PNG's Id is what config.json stores when it's picked, and the Id embeds a guess
    // at the file's condition. Change the guess and every saved pick of that file stops matching,
    // silently. These pin the Ids exactly as each release produced them.
    internal static class LocalAssetIdTests
    {
        private static readonly string[] Npcs = { "Haley", "Elliott", "Abigail", "Alex", "Mister Qi", "Emily" };

        // Captured by building the 1.0.1, 1.1.0 and 1.1.1 tags and running each one's own scanner
        // over these file names (assets/README.txt, README.md, Nexus posts, and the known misfires).
        // 1.1.0 and 1.1.1 agree on every file; 1.0.1 differs only where a season has extras.
        private static readonly (string File, string V101, string V11x)[] Golden =
        {
            ("Haley_Spring", "LocalAsset::Haley::Spring::Spring", "LocalAsset::Haley::Spring::Spring"),
            ("Haley_Summer", "LocalAsset::Haley::Summer::Summer", "LocalAsset::Haley::Summer::Summer"),
            ("Haley_Winter_Indoor", "LocalAsset::Haley::Winter::Winter_Indoor", "LocalAsset::Haley::Winter::Where_Indoor::Winter_Indoor"),
            ("Haley_Winter_Snowing", "LocalAsset::Haley::Winter::Winter_Snowing", "LocalAsset::Haley::Winter::Weather_Snowing::Winter_Snowing"),
            ("Haley_Winter_Snowing_Outdoor", "LocalAsset::Haley::Winter::Winter_Snowing_Outdoor", "LocalAsset::Haley::Winter::Weather_Snowing::Where_Outdoor::Winter_Snowing_Outdoor"),
            ("Haley_Winter_Outdoor_Snowing", "LocalAsset::Haley::Winter::Winter_Outdoor_Snowing", "LocalAsset::Haley::Winter::Weather_Snowing::Where_Outdoor::Winter_Outdoor_Snowing"),
            ("Haley_Summer_Beach", "LocalAsset::Haley::Summer::Summer_Beach", "LocalAsset::Haley::Summer::Where_Beach::Summer_Beach"),
            ("Haley_Spring_Town", "LocalAsset::Haley::Spring::Spring_Town", "LocalAsset::Haley::Spring::Where_Town::Spring_Town"),
            ("Haley_Fall_Raining_Saloon", "LocalAsset::Haley::Fall::Fall_Raining_Saloon", "LocalAsset::Haley::Fall::Weather_Raining::Where_Saloon::Fall_Raining_Saloon"),
            ("Emily_Fall_Indoor", "LocalAsset::Emily::Fall::Fall_Indoor", "LocalAsset::Emily::Fall::Where_Indoor::Fall_Indoor"),
            ("Alex_Winter_Snowing", "LocalAsset::Alex::Winter::Winter_Snowing", "LocalAsset::Alex::Winter::Weather_Snowing::Winter_Snowing"),
            ("Abigail_winter_snowing", "LocalAsset::Abigail::Winter::winter_snowing", "LocalAsset::Abigail::Winter::Weather_Snowing::winter_snowing"),
            ("Haley_FlowerDance", "LocalAsset::Haley::Flower Dance::FlowerDance", "LocalAsset::Haley::Flower Dance::FlowerDance"),
            ("Haley_EggFest", "LocalAsset::Haley::Egg Festival::EggFest", "LocalAsset::Haley::Egg Festival::EggFest"),
            ("Emily_Luau", "LocalAsset::Emily::Luau::Luau", "LocalAsset::Emily::Luau::Luau"),
            ("Emily_Spirits_Eve", "LocalAsset::Emily::Spirit's Eve::Spirits_Eve", "LocalAsset::Emily::Spirit's Eve::Spirits_Eve"),
            ("Haley_Beach", "LocalAsset::Haley::Beach::Beach", "LocalAsset::Haley::Beach::Beach"),
            ("Haley_Beach_2", "LocalAsset::Haley::Beach::Beach_2", "LocalAsset::Haley::Beach::Beach_2"),
            ("Alex_BeachFull_remake", "LocalAsset::Alex::Beach::BeachFull_remake", "LocalAsset::Alex::Beach::BeachFull_remake"),
            ("Abigail_Casual", "LocalAsset::Abigail::Casual::Casual", "LocalAsset::Abigail::Casual::Casual"),
            ("Elliott_Wedding", "LocalAsset::Elliott::Wedding::Wedding", "LocalAsset::Elliott::Wedding::Wedding"),
            ("Elliott_Wedding_v1", "LocalAsset::Elliott::Wedding_v1::Wedding_v1", "LocalAsset::Elliott::Wedding_v1::Wedding_v1"),
            ("MisterQi_Spring", "LocalAsset::Mister Qi::Spring::Spring", "LocalAsset::Mister Qi::Spring::Spring"),

            // Misfires of the condition guess (A4). Wrong, but frozen: picks of these are stored.
            ("Haley_2", "LocalAsset::Haley::Egg Festival::2", "LocalAsset::Haley::Egg Festival::2"),
            ("Haley_v2", "LocalAsset::Haley::Egg Festival::v2", "LocalAsset::Haley::Egg Festival::v2"),
            ("Haley_Star", "LocalAsset::Haley::Stardew Valley Fair::Star", "LocalAsset::Haley::Stardew Valley Fair::Star"),
            ("Haley_Dance", "LocalAsset::Haley::Flower Dance::Dance", "LocalAsset::Haley::Flower Dance::Dance"),
            ("Haley_Springtime", "LocalAsset::Haley::Spring::Springtime", "LocalAsset::Haley::Spring::Springtime"),
            ("Haley_Fallout", "LocalAsset::Haley::Fall::Fallout", "LocalAsset::Haley::Fall::Fallout"),
            ("Haley_Winterstar", "LocalAsset::Haley::Winter::Winterstar", "LocalAsset::Haley::Winter::Winterstar"),
        };

        public static void Run()
        {
            using var folder = new TempFolder();
            Dictionary<string, List<CharacterAppearanceData>> options = Scan(folder.Path);
            Dictionary<string, CharacterAppearanceData> byFile = options.Values
                .SelectMany(list => list)
                .ToDictionary(entry => Path.GetFileName(entry.Sprite));

            Assert.Section("Custom PNG Ids are exactly what 1.1.0 and 1.1.1 produced");
            Assert.Equal("every file found", byFile.Count, Golden.Length);
            foreach ((string file, _, string v11x) in Golden)
            {
                Assert.Equal(file, byFile.TryGetValue(file, out CharacterAppearanceData entry) ? entry.Id : "(not found)", v11x);
            }

            Assert.Section("Ids 1.0.1 stored for season files with extras, orphaned since 1.1.0");
            foreach ((string file, string v101, string v11x) in Golden.Where(row => row.V101 != row.V11x))
            {
                Assert.Equal($"{file} (1.0.1 Id)", Resolve(options, v101)?.Sprite, null);
            }

            Assert.Section("Paths and portrait pairing");
            CharacterAppearanceData spring = byFile["Haley_Spring"];
            Assert.Equal("sprite asset", spring.Sprite, "Mods/NpcOutfitSelector/LocalAssets/assets/Characters/Haley_Spring");
            Assert.Equal("matching portrait", spring.Portrait, "Mods/NpcOutfitSelector/LocalAssets/assets/Portraits/Haley_Spring");
            Assert.Equal("nested subfolders", byFile["Elliott_Wedding_v1"].Sprite,
                "Mods/NpcOutfitSelector/LocalAssets/assets/Characters/sub/deeper/Elliott_Wedding_v1");
            Assert.Equal("no portrait is null, not skipped", byFile["Haley_Summer"].Portrait, null);
        }

        // Same lookup ForcedEntries uses for a saved local-asset pick.
        private static CharacterAppearanceData Resolve(Dictionary<string, List<CharacterAppearanceData>> options, string savedId) =>
            options.Values.SelectMany(list => list).FirstOrDefault(entry => entry.Id == savedId);

        // The scanner only looks at names, so empty files stand in for real sprite sheets.
        internal static Dictionary<string, List<CharacterAppearanceData>> Scan(string modFolder)
        {
            string characters = Path.Combine(modFolder, "assets", "Characters");
            string portraits = Path.Combine(modFolder, "assets", "Portraits");
            Directory.CreateDirectory(Path.Combine(characters, "sub", "deeper"));
            Directory.CreateDirectory(portraits);

            foreach ((string file, _, _) in Golden)
            {
                string subfolder = file == "Elliott_Wedding_v1" ? Path.Combine("sub", "deeper") : file.StartsWith("Emily") ? "sub" : "";
                File.WriteAllBytes(Path.Combine(characters, subfolder, file + ".png"), new byte[0]);
            }
            File.WriteAllBytes(Path.Combine(portraits, "Haley_Spring.png"), new byte[0]);

            return LocalAssetScanner.BuildOptions(modFolder, Npcs, new FakeMonitor());
        }
    }
}
