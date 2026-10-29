using System.Collections.Generic;
using System.Linq;
using StardewValley.GameData.Characters;

namespace NpcOutfitSelector
{
    // Turns the saved picks into the appearance entries injected into Data/Characters. Separate
    // from the asset event so the result can be checked without the game's content pipeline.
    internal static class ForcedEntries
    {
        // Marks entries this injects to force a choice; filtered out of the grid so a forced entry
        // doesn't show up as a duplicate of its source.
        public const string IdPrefix = "NpcOutfitSelector.Force.";

        public static void Apply(
            IDictionary<string, CharacterData> data,
            ModConfig config,
            IReadOnlyDictionary<string, List<CharacterAppearanceData>> localAssetsByNpc)
        {
            foreach (string npcName in config.SelectedOutfits.Keys)
            {
                if (!data.TryGetValue(npcName, out CharacterData charData))
                {
                    continue;
                }

                charData.Appearance ??= new List<CharacterAppearanceData>();

                foreach ((string category, string outfitId) in config.GetEffectivePicks(npcName))
                {
                    if (outfitId is null)
                    {
                        continue;
                    }

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
                            match = LocalAssetScanner.FindSaved(localOptions, outfitId);
                        }
                        if (match is null)
                        {
                            continue;
                        }
                        sprite = match.Sprite;
                        portrait = match.Portrait;
                    }

                    charData.Appearance.Insert(0, AppearanceCategory.BuildForcedEntry(
                        id: $"{IdPrefix}{npcName}.{category}",
                        category: category,
                        sprite: sprite,
                        portrait: portrait));
                }
            }
        }
    }
}
