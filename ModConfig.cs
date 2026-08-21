using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI.Utilities;

namespace NpcOutfitSelector
{
    public class ModConfig
    {
        // npcName -> (category key -> chosen appearance Id). This shape is what 1.0.1 wrote and
        // must stay loadable; see AppearanceConstants for the key syntax.
        public Dictionary<string, Dictionary<string, string>> SelectedOutfits { get; set; } = new();

        // Opens this mod's config page directly while playing. Set to "" to disable.
        public KeybindList MenuHotkey { get; set; } = KeybindList.Parse("F9");

        public string GetSelectedOutfitId(string npcName, string category, string naturalOutfitId) =>
            this.SelectedOutfits.TryGetValue(npcName, out Dictionary<string, string> byCategory)
            && byCategory.TryGetValue(category, out string outfitId)
                ? outfitId
                : naturalOutfitId;

        public bool HasOverride(string npcName, string category) =>
            this.SelectedOutfits.TryGetValue(npcName, out Dictionary<string, string> byCategory)
            && byCategory.ContainsKey(category);

        public void ClearOverride(string npcName, string category)
        {
            if (this.SelectedOutfits.TryGetValue(npcName, out Dictionary<string, string> byCategory))
            {
                byCategory.Remove(category);
            }
        }

        // Staged in memory only; the menu's save callback writes it to disk.
        public void SetSelectedOutfitId(string npcName, string category, string outfitId)
        {
            if (!this.SelectedOutfits.TryGetValue(npcName, out Dictionary<string, string> byCategory))
            {
                byCategory = new Dictionary<string, string>();
                this.SelectedOutfits[npcName] = byCategory;
            }

            byCategory[category] = outfitId;
        }

        // Rewrites a key's base onto its canonical spelling (currently only Beach -> Island). Done
        // on load rather than aliased, so re-saving can't leave both spellings on one NPC — they
        // would score identically, and an exact tie is broken by weighted random.
        public void MigrateLegacyKeys()
        {
            foreach (Dictionary<string, string> byCategory in this.SelectedOutfits.Values)
            {
                List<string> stale = byCategory.Keys
                    .Where(key => AppearanceCategory.CanonicalizeKey(key) != key)
                    .ToList();

                foreach (string key in stale)
                {
                    string canonical = AppearanceCategory.CanonicalizeKey(key);
                    string outfitId = byCategory[key];
                    byCategory.Remove(key);

                    // An already-canonical key wins if somehow both are present.
                    if (!byCategory.ContainsKey(canonical))
                    {
                        byCategory[canonical] = outfitId;
                    }
                }
            }
        }
    }
}
