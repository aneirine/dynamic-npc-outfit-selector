using System.Collections.Generic;

namespace NpcOutfitSelector
{
    public class ModConfig
    {
        // npcName -> (category -> chosen appearance Id)
        public Dictionary<string, Dictionary<string, string>> SelectedOutfits { get; set; } = new();
    }
}
