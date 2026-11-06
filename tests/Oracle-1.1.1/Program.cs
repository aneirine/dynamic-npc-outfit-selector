using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NpcOutfitSelector.Compat;
using StardewModdingAPI.Toolkit.Serialization;
using StardewValley.GameData.Characters;

namespace NpcOutfitSelector.Oracle
{
    // Runs config files through the released 1.1.1 code and records what it handed the game.
    // ModConfig, AppearanceCategory and AppearanceConstants are compiled from the 1.1.1 sources
    // (see the .csproj). The two methods below are 1.1.1's ModEntry.LoadConfig and the
    // Data/Characters edit in ModEntry.OnAssetRequested, copied from commit 6ba64a2 with only
    // Helper and Monitor swapped for what SMAPI does behind them. Don't "fix" them: being 1.1.1
    // is the point.
    internal static class Program
    {
        private const string ForcedEntryIdPrefix = "NpcOutfitSelector.Force.";

        // dotnet NpcOutfitSelector.Oracle111.dll <folder of configs> <output.json>
        private static int Main(string[] args)
        {
            string gamePath = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "GamePath")?.Value;
            string[] probePaths = { gamePath, Path.Combine(gamePath, "smapi-internal") };
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
            {
                string file = probePaths.Select(p => Path.Combine(p, new AssemblyName(e.Name).Name + ".dll")).FirstOrDefault(File.Exists);
                return file is null ? null : Assembly.LoadFrom(file);
            };
            return Run(args[0], args[1]);
        }

        private static int Run(string configsDir, string outputPath)
        {
            var results = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string configPath in Directory.GetFiles(configsDir, "*.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                string folder = Path.Combine(Path.GetTempPath(), "NpcOutfitSelector.Oracle111", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                try
                {
                    string path = Path.Combine(folder, "config.json");
                    File.Copy(configPath, path);
                    results[Path.GetFileName(configPath)] = Describe111(path);
                }
                finally
                {
                    Directory.Delete(folder, recursive: true);
                }
            }

            File.WriteAllText(outputPath, JsonConvert.SerializeObject(results, Formatting.Indented) + "\n");
            Console.WriteLine($"1.1.1: {results.Count} configs -> {outputPath}");
            return 0;
        }

        private static List<string> Describe111(string configPath)
        {
            ModConfig config;
            try
            {
                config = LoadConfig(configPath);
            }
            catch (Exception ex)
            {
                return new List<string> { $"1.1.1 CRASHED: {ex.GetType().Name}" };
            }

            Dictionary<string, CharacterData> data = CompatScenario.Characters();
            EditCharacters(data, config, CompatScenario.LocalAssets());
            return CompatScenario.Describe(data);
        }

        // 1.1.1 ModEntry.LoadConfig. Helper.ReadConfig is SMAPI's ReadJsonFile ?? new, then
        // WriteConfig straight back.
        private static ModConfig LoadConfig(string configPath)
        {
            JsonHelper json = CompatScenario.CreateSmapiJsonHelper();
            ModConfig loaded;
            try
            {
                loaded = json.ReadJsonFileIfExists(configPath, out ModConfig read) ? read : null;
                loaded ??= new ModConfig();
                json.WriteJsonFile(configPath, loaded);
            }
            catch (Exception)
            {
                loaded = new ModConfig();
            }

            loaded.MigrateLegacyKeys();
            return loaded;
        }

        // 1.1.1 ModEntry.OnAssetRequested, the Data/Characters edit.
        private static void EditCharacters(IDictionary<string, CharacterData> data, ModConfig config, Dictionary<string, List<CharacterAppearanceData>> localAssetsByNpc)
        {
            foreach (KeyValuePair<string, Dictionary<string, string>> npcOverrides in config.SelectedOutfits)
            {
                string npcName = npcOverrides.Key;
                if (!data.TryGetValue(npcName, out CharacterData charData))
                {
                    continue;
                }

                charData.Appearance ??= new List<CharacterAppearanceData>();

                foreach (KeyValuePair<string, string> categoryChoice in npcOverrides.Value)
                {
                    string category = categoryChoice.Key;
                    string outfitId = categoryChoice.Value;

                    string sprite;
                    string portrait;
                    if (outfitId == AppearanceConstants.DefaultAppearanceId)
                    {
                        sprite = $"Characters/{npcName}";
                        portrait = $"Portraits/{npcName}";
                    }
                    else
                    {
                        CharacterAppearanceData match = charData.Appearance.FirstOrDefault(a => a.Id == outfitId);
                        if (match is null && localAssetsByNpc.TryGetValue(npcName, out List<CharacterAppearanceData> localOptions))
                        {
                            match = localOptions.FirstOrDefault(a => a.Id == outfitId);
                        }
                        if (match is null)
                        {
                            continue;
                        }
                        sprite = match.Sprite;
                        portrait = match.Portrait;
                    }

                    charData.Appearance.Insert(0, AppearanceCategory.BuildForcedEntry(
                        id: $"{ForcedEntryIdPrefix}{npcName}.{category}",
                        category: category,
                        sprite: sprite,
                        portrait: portrait));
                }
            }
        }
    }
}
