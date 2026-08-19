using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Characters;
using StardewValley.Menus;

namespace NpcOutfitSelector
{
    internal sealed class AppearancePreviewGrid : IDisposable
    {
        private const int FrameWidth = 16;
        private const int FrameHeight = 32;
        private const int Scale = 4;
        private const int CellWidth = FrameWidth * Scale; // 64
        private const int CellHeight = FrameHeight * Scale; // 128
        private const int ColumnsPerRow = 6;
        private const int TicksPerFrame = 8;
        private const int RadioButtonSize = 36;
        private const int RadioButtonGap = 10;

        private sealed class Cell
        {
            public bool IsClearCell;
            public string Id;
            public string Season;
            public string SpritePath;
            public Rectangle Bounds;
            public Texture2D Texture;
            public bool TextureFailed;
            public int WalkFrameCount;
            public int HoverProgress;
        }

        private readonly IModHelper helper;
        private readonly string npcName;
        private readonly List<Cell> cells = new();
        private readonly int verticalStep;

        // Reused by the click handler so hit-testing matches exactly what Draw() last rendered.
        private float lastDrawX;
        private float lastDrawY;

        // GMCM only calls Draw() for rows currently scrolled into view. Without this flag, a grid
        // scrolled away keeps stale lastDrawX/Y forever, and a click elsewhere in the menu could
        // land inside its old checkbox area and silently change an NPC the player never touched.
        private bool isVisibleThisFrame;

        private readonly Func<string> getCurrentCategory;
        private readonly Func<string, string> getSelectedOutfitId;
        private readonly Action<string, string> setSelectedOutfitId;
        private readonly Func<string, bool> hasOverride;
        private readonly Action<string> clearOverride;

        public int Height { get; }

        public AppearancePreviewGrid(
            IModHelper helper,
            string npcName,
            List<CharacterAppearanceData> appearances,
            Func<string> getCurrentCategory,
            Func<string, string> getSelectedOutfitId,
            Action<string, string> setSelectedOutfitId,
            Func<string, bool> hasOverride,
            Action<string> clearOverride)
        {
            this.helper = helper;
            this.npcName = npcName;
            this.getCurrentCategory = getCurrentCategory;
            this.getSelectedOutfitId = getSelectedOutfitId;
            this.setSelectedOutfitId = setSelectedOutfitId;
            this.hasOverride = hasOverride;
            this.clearOverride = clearOverride;
            this.helper.Events.Input.ButtonPressed += this.OnButtonPressed;
            this.helper.Events.Display.Rendering += this.OnRendering;

            this.verticalStep = CellHeight + RadioButtonGap + RadioButtonSize;

            // Leading "clear" cell — checking it removes any saved override for the current
            // category, reverting to whatever the game would naturally pick.
            this.cells.Add(new Cell
            {
                IsClearCell = true,
                Bounds = new Rectangle(0, 0, CellWidth, CellHeight),
            });

            int index = 1;
            foreach (CharacterAppearanceData app in appearances)
            {
                if (string.IsNullOrWhiteSpace(app.Sprite))
                {
                    continue;
                }

                int col = index % ColumnsPerRow;
                int row = index / ColumnsPerRow;

                this.cells.Add(new Cell
                {
                    Id = string.IsNullOrWhiteSpace(app.Id) ? "(no id)" : app.Id,
                    Season = app.Season?.ToString() ?? "any season",
                    SpritePath = app.Sprite,
                    Bounds = new Rectangle(col * CellWidth, row * this.verticalStep, CellWidth, CellHeight),
                });
                index++;
            }

            int rows = (int)Math.Ceiling(this.cells.Count / (float)ColumnsPerRow);
            this.Height = rows * this.verticalStep;
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

        public void Draw(SpriteBatch spriteBatch, Vector2 pos)
        {
            this.isVisibleThisFrame = true;

            // GMCM passes pos.X as the row's horizontal midpoint (every control sits at
            // Table.Size.X / 2). Since the menu panel is centered on screen, that midpoint is also
            // the panel's center, so we can rebuild its left edge and hug the grid against it
            // instead of drawing outward from the midpoint.
            int panelWidth = Math.Min(1200, Game1.uiViewport.Width - 200);
            float anchorX = pos.X - (panelWidth / 2f);

            this.lastDrawX = anchorX;
            this.lastDrawY = pos.Y;

            Vector2 cursorPos = this.helper.Input.GetCursorPosition().GetScaledScreenPixels();
            Point cursor = new((int)cursorPos.X, (int)cursorPos.Y);

            string currentCategory = this.getCurrentCategory();
            string selectedOutfitId = this.getSelectedOutfitId(currentCategory);
            bool categoryHasOverride = this.hasOverride(currentCategory);

            string hoveredTooltip = null;

            foreach (Cell cell in this.cells)
            {
                Rectangle screenBounds = new(
                    (int)anchorX + cell.Bounds.X,
                    (int)pos.Y + cell.Bounds.Y,
                    cell.Bounds.Width,
                    cell.Bounds.Height);

                bool isHovered = screenBounds.Contains(cursor);

                if (cell.IsClearCell)
                {
                    spriteBatch.Draw(
                        Game1.staminaRect,
                        screenBounds,
                        null,
                        Color.White * 0.25f);

                    Rectangle clearRadioBounds = this.GetRadioButtonBounds(screenBounds);
                    Rectangle clearRadioSourceRect = !categoryHasOverride
                        ? OptionsCheckbox.sourceRectChecked
                        : OptionsCheckbox.sourceRectUnchecked;

                    spriteBatch.Draw(
                        Game1.mouseCursors,
                        new Vector2(clearRadioBounds.X, clearRadioBounds.Y),
                        clearRadioSourceRect,
                        Color.White,
                        0f,
                        Vector2.Zero,
                        Scale,
                        SpriteEffects.None,
                        0.86f);

                    if (isHovered)
                    {
                        hoveredTooltip = "No override\n(use the game's own choice)";
                    }

                    continue;
                }

                if (cell.Texture is null && !cell.TextureFailed)
                {
                    this.TryLoadTexture(cell);
                }

                if (cell.Texture is not null)
                {
                    int frame = 0;
                    if (isHovered && cell.WalkFrameCount > 1)
                    {
                        cell.HoverProgress++;
                        frame = (cell.HoverProgress / TicksPerFrame) % cell.WalkFrameCount;
                    }
                    else
                    {
                        cell.HoverProgress = 0;
                    }

                    Rectangle npcSourceRect = new(frame * FrameWidth, 0, FrameWidth, FrameHeight);

                    spriteBatch.Draw(
                        cell.Texture,
                        new Vector2(screenBounds.X, screenBounds.Y),
                        npcSourceRect,
                        Color.White,
                        0f,
                        Vector2.Zero,
                        Scale,
                        SpriteEffects.None,
                        1f);

                    Rectangle radioBounds = this.GetRadioButtonBounds(screenBounds);
                    // Only highlight when there's an actual saved override — otherwise
                    // selectedOutfitId is just the natural fallback, and the clear cell is checked.
                    bool isSelected = categoryHasOverride && cell.Id == selectedOutfitId;
                    Rectangle radioSourceRect = isSelected
                        ? OptionsCheckbox.sourceRectChecked
                        : OptionsCheckbox.sourceRectUnchecked;

                    spriteBatch.Draw(
                        Game1.mouseCursors,
                        new Vector2(radioBounds.X, radioBounds.Y),
                        radioSourceRect,
                        Color.White,
                        0f,
                        Vector2.Zero,
                        Scale,
                        SpriteEffects.None,
                        0.86f);
                }

                if (isHovered)
                {
                    hoveredTooltip = $"{cell.Id}\n{cell.Season}";
                }
            }

            if (hoveredTooltip is not null)
            {
                IClickableMenu.drawToolTip(spriteBatch, hoveredTooltip, null, null);
            }
        }

        private Rectangle GetRadioButtonBounds(Rectangle cellScreenBounds)
        {
            int x = cellScreenBounds.X + (cellScreenBounds.Width / 2) - (RadioButtonSize / 2);
            int y = cellScreenBounds.Y + cellScreenBounds.Height + RadioButtonGap;
            return new Rectangle(x, y, RadioButtonSize, RadioButtonSize);
        }

        private void TryLoadTexture(Cell cell)
        {
            try
            {
                Texture2D texture = this.helper.GameContent.Load<Texture2D>(cell.SpritePath);
                if (texture.Width < FrameWidth || texture.Height < FrameHeight || texture.Width % FrameWidth != 0)
                {
                    cell.TextureFailed = true;
                    return;
                }

                cell.Texture = texture;
                cell.WalkFrameCount = Math.Min(4, texture.Width / FrameWidth);
            }
            catch
            {
                cell.TextureFailed = true;
            }
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (e.Button != SButton.MouseLeft || !this.isVisibleThisFrame)
            {
                return;
            }

            // Same coordinate space Draw() uses — e.Cursor.ScreenPixels is unscaled and misses
            // whenever the UI scale option isn't 100%.
            Vector2 mousePos = e.Cursor.GetScaledScreenPixels();
            Point mouse = new((int)mousePos.X, (int)mousePos.Y);

            string currentCategory = this.getCurrentCategory();
            string selectedOutfitId = this.getSelectedOutfitId(currentCategory);
            bool categoryHasOverride = this.hasOverride(currentCategory);

            foreach (Cell cell in this.cells)
            {
                if (cell.TextureFailed)
                {
                    continue;
                }

                Rectangle screenBounds = new(
                    (int)this.lastDrawX + cell.Bounds.X,
                    (int)this.lastDrawY + cell.Bounds.Y,
                    cell.Bounds.Width,
                    cell.Bounds.Height);

                Rectangle radioBounds = this.GetRadioButtonBounds(screenBounds);

                if (!radioBounds.Contains(mouse))
                {
                    continue;
                }

                if (cell.IsClearCell)
                {
                    if (categoryHasOverride)
                    {
                        Game1.playSound("drumkit6");
                        this.clearOverride(currentCategory);
                    }
                    break;
                }

                // Not just "!= selectedOutfitId": that falls back to the natural pick when there's
                // no override, which can equal this cell's Id (e.g. an NPC with only the synthetic
                // "Default" entry) and would make clicking it a no-op. What matters is whether this
                // exact override is already saved.
                bool isAlreadyThisOverride = categoryHasOverride && cell.Id == selectedOutfitId;
                if (!isAlreadyThisOverride)
                {
                    Game1.playSound("drumkit6");
                    this.setSelectedOutfitId(currentCategory, cell.Id);
                    break;
                }
            }
        }
    }
}
