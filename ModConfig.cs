using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI.Utilities;

namespace NpcOutfitSelector
{
    public class ModConfig
    {
        // npcName -> (category key -> chosen appearance Id). This shape is what 1.0.1 wrote and
        // must stay loadable; see AppearanceConstants for the key syntax. Keys are kept as the
        // player wrote them; everything that acts on them goes through GetEffectivePicks.
        public Dictionary<string, Dictionary<string, string>> SelectedOutfits { get; set; } = new();

        // Opens this mod's config page directly while playing. Set to "" to disable.
        public KeybindList MenuHotkey { get; set; } = KeybindList.Parse("F9");

        // Each key read the way 1.1.1 read it (AppearanceCategory.EffectiveKey). When several
        // keys read the same, the one 1.1.1 kept applies: the key already in that form, else the
        // first in the file. The others stay in the file but do nothing. Values may be null.
        public Dictionary<string, string> GetEffectivePicks(string npcName)
        {
            var picks = new Dictionary<string, string>();
            if (!this.SelectedOutfits.TryGetValue(npcName, out Dictionary<string, string> byCategory) || byCategory is null)
            {
                return picks;
            }

            foreach ((string key, string effective) in EffectiveSources(byCategory))
            {
                picks[effective] = byCategory[key];
            }
            return picks;
        }

        public string GetSelectedOutfitId(string npcName, string category, string naturalOutfitId) =>
            this.GetEffectivePicks(npcName).TryGetValue(category, out string outfitId) && outfitId is not null
                ? outfitId
                : naturalOutfitId;

        public bool HasOverride(string npcName, string category) =>
            this.GetEffectivePicks(npcName).TryGetValue(category, out string outfitId) && outfitId is not null;

        // Every key that reads as this category goes, so a hand-written twin can't take over.
        public void ClearOverride(string npcName, string category)
        {
            if (this.SelectedOutfits.TryGetValue(npcName, out Dictionary<string, string> byCategory) && byCategory is not null)
            {
                foreach (string key in byCategory.Keys.Where(k => AppearanceCategory.EffectiveKey(k) == category).ToList())
                {
                    byCategory.Remove(key);
                }
            }
        }

        // Staged in memory only; the menu's save callback writes it to disk. A key that already
        // fills this slot keeps its spelling.
        public void SetSelectedOutfitId(string npcName, string category, string outfitId)
        {
            if (!this.SelectedOutfits.TryGetValue(npcName, out Dictionary<string, string> byCategory) || byCategory is null)
            {
                byCategory = new Dictionary<string, string>();
                this.SelectedOutfits[npcName] = byCategory;
            }

            string existing = EffectiveSources(byCategory).FirstOrDefault(pair => pair.Effective == category).Key;
            byCategory[existing ?? category] = outfitId;
        }

        public ModConfig Clone() => new()
        {
            SelectedOutfits = this.SelectedOutfits.ToDictionary(
                npc => npc.Key,
                npc => npc.Value is null ? null : new Dictionary<string, string>(npc.Value)),
            MenuHotkey = this.MenuHotkey,
        };

        // A hand-edited null crashed 1.1.1 on load. Either one means "nothing picked".
        public void RepairNulls()
        {
            this.SelectedOutfits ??= new();
            foreach (string npcName in this.SelectedOutfits.Where(npc => npc.Value is null).Select(npc => npc.Key).ToList())
            {
                this.SelectedOutfits[npcName] = new Dictionary<string, string>();
            }
        }

        // Renames a 1.0.1 "Beach" base to "Island", so the menu, which only writes Island, finds
        // it. Skipped when another key already fills that slot: both stay and 1.1.1's winner
        // keeps applying. Returns a warning for every key left doing nothing.
        public List<string> MigrateLegacyKeys()
        {
            var warnings = new List<string>();
            foreach ((string npcName, Dictionary<string, string> byCategory) in this.SelectedOutfits)
            {
                if (byCategory is null)
                {
                    continue;
                }

                foreach (string key in byCategory.Keys.ToList())
                {
                    string renamed = AppearanceCategory.RenameLegacyBase(key);
                    string effective = AppearanceCategory.EffectiveKey(key);
                    bool hasTwin = byCategory.Keys.Any(other => other != key && AppearanceCategory.EffectiveKey(other) == effective);
                    if (renamed != key && !hasTwin)
                    {
                        RenameKeepingOrder(byCategory, key, renamed);
                    }
                }

                Dictionary<string, string> applied = EffectiveSources(byCategory).ToDictionary(pair => pair.Effective, pair => pair.Key);
                foreach (string key in byCategory.Keys)
                {
                    string winner = applied[AppearanceCategory.EffectiveKey(key)];
                    if (winner != key)
                    {
                        warnings.Add($"{npcName}: \"{key}\" in config.json is ignored because \"{winner}\" sets the same "
                            + "outfit slot. Both are kept in the file; delete the one you don't want.");
                    }
                }
            }
            return warnings;
        }

        // Pairs of (key as written, key as read) for the keys that apply, in file order.
        private static IEnumerable<(string Key, string Effective)> EffectiveSources(Dictionary<string, string> byCategory)
        {
            var winners = new Dictionary<string, string>();
            foreach (string key in byCategory.Keys.Where(k => AppearanceCategory.EffectiveKey(k) == k))
            {
                winners[key] = key;
            }
            foreach (string key in byCategory.Keys)
            {
                winners.TryAdd(AppearanceCategory.EffectiveKey(key), key);
            }

            return byCategory.Keys
                .Select(key => (Key: key, Effective: AppearanceCategory.EffectiveKey(key)))
                .Where(pair => winners[pair.Effective] == pair.Key);
        }

        // Dictionary order is file order, which decides which twin applies, so a rename must not
        // move the key to the end.
        private static void RenameKeepingOrder(Dictionary<string, string> byCategory, string from, string to)
        {
            List<KeyValuePair<string, string>> entries = byCategory.ToList();
            byCategory.Clear();
            foreach ((string key, string value) in entries)
            {
                byCategory[key == from ? to : key] = value;
            }
        }
    }
}
