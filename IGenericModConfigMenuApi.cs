using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace NpcOutfitSelector
{
    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);

        void Unregister(IManifest mod);

        void AddSectionTitle(IManifest mod, Func<string> text, Func<string> tooltip = null);

        void AddParagraph(IManifest mod, Func<string> text);

        void AddBoolOption(
            IManifest mod,
            Func<bool> getValue,
            Action<bool> setValue,
            Func<string> name,
            Func<string> tooltip = null,
            string fieldId = null);

        void AddKeybindList(
            IManifest mod,
            Func<KeybindList> getValue,
            Action<KeybindList> setValue,
            Func<string> name = null,
            Func<string> tooltip = null,
            string fieldId = null);

        void AddTextOption(
            IManifest mod,
            Func<string> getValue,
            Action<string> setValue,
            Func<string> name,
            Func<string> tooltip = null,
            string[] choices = null,
            Func<string, string> formatChoice = null,
            string fieldId = null);

        // Fires live on any field change — unlike AddBoolOption/AddTextOption's setValue, which
        // GMCM only invokes once on Save.
        void OnFieldChanged(IManifest mod, Action<string, object> onChange);

        // Replaces the currently-open menu with a freshly built one reading the mod's current
        // option list — GMCM's own menu Table is otherwise immutable once constructed.
        void OpenModMenu(IManifest mod);

        void AddComplexOption(
            IManifest mod,
            Func<string> name,
            Action<SpriteBatch, Vector2> draw,
            Func<string> tooltip = null,
            Action beforeMenuOpened = null,
            Action beforeSave = null,
            Action afterSave = null,
            Action beforeReset = null,
            Action afterReset = null,
            Action beforeMenuClosed = null,
            Func<int> height = null,
            string fieldId = null);
    }

    // Asked for on its own, so a GMCM without it leaves the rest of the API working. When no
    // mod's page is open it returns false, or true with mod null: GMCM 1.16 does the latter on
    // its keybinds page, so check mod, not just the result.
    public interface IGenericModConfigMenuCurrentMenuApi
    {
        bool TryGetCurrentMenu(out IManifest mod, out string page);
    }
}
