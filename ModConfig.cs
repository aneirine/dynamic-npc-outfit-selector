using System.Collections.Generic;
using StardewModdingAPI.Utilities;

namespace NpcOutfitSelector
{
    public class ModConfig
    {
        // npcName -> (category -> chosen appearance Id)
        public Dictionary<string, Dictionary<string, string>> SelectedOutfits { get; set; } = new();

        // Opens this mod's config page directly while playing. Set to "" to disable.
        public KeybindList MenuHotkey { get; set; } = KeybindList.Parse("F9");
    }
}
