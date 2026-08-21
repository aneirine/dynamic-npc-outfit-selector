using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StardewModdingAPI;
using StardewValley.GameData.Characters;

namespace NpcOutfitSelector
{
    internal sealed class LocalAssetFile
    {
        public string NpcName;
        public string RawCondition;
        public string RelativePath; // relative to this mod's own folder
    }

    // Scans this mod's own assets/Characters and assets/Portraits folders for player-dropped PNGs
    // named "{NpcName}_{Condition}.png", at any subfolder depth — for players with loose sprite
    // sheets but no Content Patcher pack of their own.
    internal static class LocalAssetScanner
    {
        public static List<LocalAssetFile> ScanFolder(
            string modFolderPath,
            string subfolder,
            IReadOnlyList<string> knownNpcNames,
            IMonitor monitor)
        {
            string root = Path.Combine(modFolderPath, "assets", subfolder);
            var results = new List<LocalAssetFile>();
            if (!Directory.Exists(root))
            {
                return results;
            }

            // Longest name first, so e.g. "Mister Qi" isn't shadowed by a shorter accidental match.
            List<string> namesByLength = knownNpcNames.OrderByDescending(n => n.Length).ToList();
            var seenBasenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string filePath in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
            {
                string basename = Path.GetFileNameWithoutExtension(filePath);

                if (!seenBasenames.Add(basename))
                {
                    monitor.Log(
                        $"Skipping duplicate local asset filename '{basename}.png' — a file with that "
                        + $"exact name was already found elsewhere under assets/{subfolder}/.",
                        LogLevel.Warn);
                    continue;
                }

                string matchedNpc = null;
                string condition = null;
                foreach (string npcName in namesByLength)
                {
                    string normalizedNpc = npcName.Replace(" ", "");
                    if (basename.StartsWith(normalizedNpc + "_", StringComparison.OrdinalIgnoreCase))
                    {
                        matchedNpc = npcName;
                        condition = basename.Substring(normalizedNpc.Length + 1);
                        break;
                    }
                }

                if (matchedNpc is null)
                {
                    monitor.Log(
                        $"Skipping '{basename}.png' under assets/{subfolder}/ — its name doesn't start "
                        + "with a known NPC name followed by '_' (expected e.g. 'Haley_Spring.png').",
                        LogLevel.Warn);
                    continue;
                }

                results.Add(new LocalAssetFile
                {
                    NpcName = matchedNpc,
                    RawCondition = condition,
                    RelativePath = Path.GetRelativePath(modFolderPath, filePath).Replace('\\', '/'),
                });
            }

            return results;
        }

        // Turns the scanned files into pickable appearance entries, pairing each sprite with the
        // portrait named for the same condition. Portraits are optional, so a missing one is null
        // rather than a skipped entry.
        public static Dictionary<string, List<CharacterAppearanceData>> BuildOptions(
            string modFolderPath, IReadOnlyList<string> knownNpcNames, IMonitor monitor)
        {
            List<LocalAssetFile> spriteFiles = ScanFolder(modFolderPath, "Characters", knownNpcNames, monitor);
            List<LocalAssetFile> portraitFiles = ScanFolder(modFolderPath, "Portraits", knownNpcNames, monitor);

            var portraitAssetByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (LocalAssetFile portraitFile in portraitFiles)
            {
                portraitAssetByKey[$"{portraitFile.NpcName}::{portraitFile.RawCondition}"] =
                    ToAssetName(portraitFile.RelativePath);
            }

            var result = new Dictionary<string, List<CharacterAppearanceData>>(StringComparer.OrdinalIgnoreCase);
            foreach (LocalAssetFile spriteFile in spriteFiles)
            {
                string category = AppearanceCategory.NormalizeLocalCondition(spriteFile.RawCondition);
                portraitAssetByKey.TryGetValue(
                    $"{spriteFile.NpcName}::{spriteFile.RawCondition}", out string portraitAsset);

                CharacterAppearanceData entry = AppearanceCategory.BuildLocalEntry(
                    id: $"{AppearanceConstants.LocalAssetIdPrefix}{spriteFile.NpcName}::{category}::{spriteFile.RawCondition}",
                    category: category,
                    sprite: ToAssetName(spriteFile.RelativePath),
                    portrait: portraitAsset);

                if (!result.TryGetValue(spriteFile.NpcName, out List<CharacterAppearanceData> list))
                {
                    list = new List<CharacterAppearanceData>();
                    result[spriteFile.NpcName] = list;
                }
                list.Add(entry);
            }

            return result;
        }

        public static string ToAssetName(string relativePath) =>
            AppearanceConstants.LocalAssetPathPrefix + Path.ChangeExtension(relativePath, null);
    }
}
