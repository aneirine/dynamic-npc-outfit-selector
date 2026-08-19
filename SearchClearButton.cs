using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace NpcOutfitSelector
{
    // A clickable "Clear" next to the search box. GMCM has no button primitive, so this is drawn
    // into an AddComplexOption slot and hit-tested against our own cursor reading — the same
    // approach AppearancePreviewGrid already uses for its outfit cards.
    internal sealed class SearchClearButton : IDisposable
    {
        private const int ButtonWidth = 180;
        private const int ButtonHeight = 48;

        private static readonly Rectangle ButtonFrameSource = new(432, 439, 9, 9);

        private readonly IModHelper helper;
        private readonly Func<string> getQuery;
        private readonly Action clear;

        private Rectangle lastBounds;

        // GMCM only calls Draw() for rows scrolled into view; without this a scrolled-away button
        // keeps stale bounds and could swallow a click meant for something else.
        private bool isVisibleThisFrame;

        public int Height => ButtonHeight;

        public SearchClearButton(IModHelper helper, Func<string> getQuery, Action clear)
        {
            this.helper = helper;
            this.getQuery = getQuery;
            this.clear = clear;
            this.helper.Events.Input.ButtonPressed += this.OnButtonPressed;
            this.helper.Events.Display.Rendering += this.OnRendering;
        }

        public void Dispose()
        {
            this.helper.Events.Input.ButtonPressed -= this.OnButtonPressed;
            this.helper.Events.Display.Rendering -= this.OnRendering;
        }

        private void OnRendering(object sender, RenderingEventArgs e)
        {
            this.isVisibleThisFrame = false;
        }

        private bool HasQuery => !string.IsNullOrWhiteSpace(this.getQuery());

        public void Draw(SpriteBatch spriteBatch, Vector2 pos)
        {
            this.isVisibleThisFrame = true;

            // GMCM passes pos.X as the row's horizontal midpoint, which is also the centered
            // panel's centre — rebuild its left edge so the button lines up with the options above.
            int panelWidth = Math.Min(1200, Game1.uiViewport.Width - 200);
            int x = (int)pos.X - (panelWidth / 2);

            this.lastBounds = new Rectangle(x, (int)pos.Y, ButtonWidth, ButtonHeight);

            // Dimmed when there's nothing to clear, so it reads as unavailable rather than broken.
            Color tint = this.HasQuery ? Color.White : Color.White * 0.5f;

            IClickableMenu.drawTextureBox(
                spriteBatch,
                Game1.mouseCursors,
                ButtonFrameSource,
                this.lastBounds.X,
                this.lastBounds.Y,
                this.lastBounds.Width,
                this.lastBounds.Height,
                tint,
                4f,
                drawShadow: false);

            const string label = "Clear search";
            Vector2 labelSize = Game1.smallFont.MeasureString(label);
            spriteBatch.DrawString(
                Game1.smallFont,
                label,
                new Vector2(
                    this.lastBounds.X + ((ButtonWidth - labelSize.X) / 2f),
                    this.lastBounds.Y + ((ButtonHeight - labelSize.Y) / 2f)),
                Game1.textColor * (this.HasQuery ? 1f : 0.5f));
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (e.Button != SButton.MouseLeft || !this.isVisibleThisFrame || !this.HasQuery)
            {
                return;
            }

            // Same coordinate space Draw() uses — e.Cursor.ScreenPixels is unscaled and misses
            // whenever the UI scale option isn't 100%.
            Vector2 cursor = e.Cursor.GetScaledScreenPixels();
            if (!this.lastBounds.Contains((int)cursor.X, (int)cursor.Y))
            {
                return;
            }

            Game1.playSound("drumkit6");
            this.clear();
        }
    }
}
