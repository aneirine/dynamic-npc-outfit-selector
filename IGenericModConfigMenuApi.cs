using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;

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
}
