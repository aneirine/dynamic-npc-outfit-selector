using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Characters;

namespace NpcOutfitSelector
{
    public class ModEntry : Mod
    {
        private const string CategoryFieldIdPrefix = "SelectedCategory_";
        private const string SearchQueryFieldId = "Search_Query";
        private const string SearchTriggerFieldId = "Search_Trigger";

        // Marks entries OnAssetRequested injects to force a choice; filtered out of the grid so a
        // forced entry doesn't show up as a duplicate of its source.
        private const string ForcedEntryIdPrefix = "NpcOutfitSelector.Force.";

        // Asset namespace local PNGs (see LocalAssetScanner) are exposed under, so a forced entry
        // can reference them like any other texture.
        private const string LocalAssetPrefix = "Mods/NpcOutfitSelector/LocalAssets/";

        // Vanilla's key for an as-yet-unrevealed NPC slot — not a real character, skip it everywhere.
        private const string UnrevealedNpcKey = "???";

        private readonly Dictionary<string, string> selectedCategoryByNpc = new();
        private readonly List<AppearancePreviewGrid> activeGrids = new();
        private ModConfig config;
        private Dictionary<string, List<CharacterAppearanceData>> localAssetsByNpc;

        // Typed live via OnFieldChanged; only actually applied to the visible list when the
        // adjacent Search toggle fires (see ApplySearchFilter) — not on every keystroke, since GMCM's
        // own Textbox only takes keyboard focus back via a mouse click, so rebuilding the menu (which
        // replaces it with a fresh Textbox) on every character would force a re-click after each letter.
        private string searchQuery = "";
        private bool searchTrigger;

        public override void Entry(IModHelper helper)
        {
            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            helper.Events.GameLoop.DayStarted += this.OnDayStarted;
            helper.Events.Display.MenuChanged += this.OnMenuChanged;
            helper.Events.Content.AssetRequested += this.OnAssetRequested;

            helper.ConsoleCommands.Add(
                "outfit_dump",
                "Dumps the Appearance entries this mod currently reads from Data/Characters.\n\n"
                + "Usage: outfit_dump [npc name]\n"
                + "- outfit_dump           : lists every NPC with its appearance-entry count\n"
                + "- outfit_dump Marnie    : lists full detail (Id, Condition, Season, Sprite, Portrait) for that NPC",
                this.OnDumpCommand);

            helper.ConsoleCommands.Add(
                "outfit_categories",
                "Shows, for one NPC, what this mod resolves as the selected outfit for every "
                + "category (dropdown option) — the saved override if any, otherwise the computed "
                + "natural pick. Usage: outfit_categories <npc name>",
                this.OnCategoriesCommand);
        }

        private void OnCategoriesCommand(string command, string[] args)
        {
            if (args.Length == 0)
            {
                this.Monitor.Log("Usage: outfit_categories <npc name>", LogLevel.Warn);
                return;
            }

            Dictionary<string, CharacterData> characters;
            try
            {
                characters = this.LoadCharacters();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Could not load Data/Characters: {ex.Message}", LogLevel.Error);
                return;
            }

            string npcName = args[0];
            if (!characters.TryGetValue(npcName, out CharacterData data))
            {
                this.Monitor.Log($"No character named '{npcName}' in Data/Characters.", LogLevel.Warn);
                return;
            }

            this.config ??= this.LoadConfig();
            this.localAssetsByNpc ??= this.BuildLocalAssetOptions(characters.Keys.ToList());

            List<CharacterAppearanceData> displayAppearances = this.BuildDisplayAppearances(npcName, data);

            string[] categories = AppearanceCategory.CollectGlobalCategories(
                characters.Values.SelectMany(d => d.Appearance ?? Enumerable.Empty<CharacterAppearanceData>())
                    .Concat(this.localAssetsByNpc.Values.SelectMany(list => list)));
            bool hasOverrides = this.config.SelectedOutfits.TryGetValue(npcName, out Dictionary<string, string> byCategory);

            this.Monitor.Log($"{npcName} — resolved outfit per category:", LogLevel.Info);
            foreach (string category in categories)
            {
                string natural = GetNaturalOutfitId(displayAppearances, category);
                string overrideId = null;
                bool hasOverride = hasOverrides && byCategory.TryGetValue(category, out overrideId);
                string resolved = this.GetSelectedOutfitId(npcName, category, natural);

                this.Monitor.Log(
                    $"  {category,-20} natural={natural,-55} override={(hasOverride ? overrideId : "(none)"),-55} resolved={resolved}",
                    LogLevel.Info);
            }
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            // Deferred one tick so every mod's GameLaunched handler (including Content Patcher's)
            // has already run — otherwise Data/Characters can still be missing other packs' edits.
            this.Helper.Events.GameLoop.UpdateTicked += this.OnFirstUpdateTicked;
        }

        private void OnFirstUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            this.Helper.Events.GameLoop.UpdateTicked -= this.OnFirstUpdateTicked;
            this.BuildMenu();
        }

        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            this.BuildMenu();
        }

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            this.BuildMenu();
        }

        private void OnMenuChanged(object sender, MenuChangedEventArgs e)
        {
            // Rebuild whenever GMCM's menu (mod list or our page) opens, so it always reflects the
            // latest Data/Characters instead of whatever was loaded at GameLaunched/SaveLoaded.
            if (e.NewMenu is not null && e.NewMenu.GetType().Name.Contains("ModConfigMenu"))
            {
                this.BuildMenu();
            }
        }

        private void OnAssetRequested(object sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.BaseName.StartsWith(LocalAssetPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = e.NameWithoutLocale.BaseName.Substring(LocalAssetPrefix.Length) + ".png";
                e.LoadFromModFile<Texture2D>(relativePath, AssetLoadPriority.Medium);
                return;
            }

            if (!e.NameWithoutLocale.IsEquivalentTo("Data/Characters"))
            {
                return;
            }

            e.Edit(asset =>
            {
                this.config ??= this.LoadConfig();

                IDictionary<string, CharacterData> data = asset.AsDictionary<string, CharacterData>().Data;
                this.localAssetsByNpc ??= this.BuildLocalAssetOptions(data.Keys.ToList());

                foreach (KeyValuePair<string, Dictionary<string, string>> npcOverrides in this.config.SelectedOutfits)
                {
                    string npcName = npcOverrides.Key;
                    if (!data.TryGetValue(npcName, out CharacterData charData))
                    {
                        continue;
                    }

                    charData.Appearance ??= new List<CharacterAppearanceData>();

                    foreach (KeyValuePair<string, string> categoryChoice in npcOverrides.Value)
                    {
                        string category = categoryChoice.Key;
                        string outfitId = categoryChoice.Value;

                        string sprite;
                        string portrait;
                        if (outfitId == AppearanceCategory.DefaultAppearanceId)
                        {
                            sprite = $"Characters/{npcName}";
                            portrait = $"Portraits/{npcName}";
                        }
                        else
                        {
                            CharacterAppearanceData match = charData.Appearance.FirstOrDefault(a => a.Id == outfitId);
                            if (match is null && this.localAssetsByNpc.TryGetValue(npcName, out List<CharacterAppearanceData> localOptions))
                            {
                                match = localOptions.FirstOrDefault(a => a.Id == outfitId);
                            }
                            if (match is null)
                            {
                                continue;
                            }
                            sprite = match.Sprite;
                            portrait = match.Portrait;
                        }

                        charData.Appearance.Insert(0, AppearanceCategory.BuildForcedEntry(
                            id: $"{ForcedEntryIdPrefix}{npcName}.{category}",
                            category: category,
                            sprite: sprite,
                            portrait: portrait));
                    }
                }
            }, AssetEditPriority.Late);
        }

        private void OnGmcmFieldChanged(string fieldId, object value)
        {
            if (fieldId is null)
            {
                return;
            }

            if (fieldId == SearchQueryFieldId && value is string query)
            {
                // Tracked live so the box's own text persists across a rebuild, but this alone
                // doesn't touch the visible NPC list — see ApplySearchFilter.
                this.searchQuery = query;
                return;
            }

            if (fieldId == SearchTriggerFieldId && value is bool triggered)
            {
                this.searchTrigger = triggered;
                this.ApplySearchFilter();
                return;
            }

            if (fieldId.StartsWith(CategoryFieldIdPrefix) && value is string category)
            {
                string npcName = fieldId.Substring(CategoryFieldIdPrefix.Length);
                this.selectedCategoryByNpc[npcName] = category;
            }
        }

        // Rebuilds the (now-filtered) option list, then asks GMCM to replace the already-open menu
        // with a fresh one reading it — GMCM's own Table is built once and can't be swapped in place,
        // so this is the only way to make the filter visible without the player leaving the page.
        private void ApplySearchFilter()
        {
            this.BuildMenu();

            var gmcm = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            gmcm?.OpenModMenu(this.ModManifest);
        }

        private string GetSelectedOutfitId(string npcName, string category, string naturalOutfitId)
        {
            if (this.config.SelectedOutfits.TryGetValue(npcName, out Dictionary<string, string> byCategory)
                && byCategory.TryGetValue(category, out string outfitId))
            {
                return outfitId;
            }
            return naturalOutfitId;
        }

        private bool HasOverride(string npcName, string category)
        {
            return this.config.SelectedOutfits.TryGetValue(npcName, out Dictionary<string, string> byCategory)
                && byCategory.ContainsKey(category);
        }

        private void ClearOverride(string npcName, string category)
        {
            if (this.config.SelectedOutfits.TryGetValue(npcName, out Dictionary<string, string> byCategory))
            {
                byCategory.Remove(category);
            }
        }

        // Falls back to this NPC's own highest-priority (lowest Precedence) entry for the category,
        // instead of a blind "Default", when the player hasn't picked anything yet.
        private static string GetNaturalOutfitId(List<CharacterAppearanceData> displayAppearances, string category)
        {
            CharacterAppearanceData best = null;
            foreach (CharacterAppearanceData app in displayAppearances)
            {
                if (AppearanceCategory.Classify(app) != category)
                {
                    continue;
                }
                if (best is null || app.Precedence < best.Precedence)
                {
                    best = app;
                }
            }

            return best?.Id ?? AppearanceCategory.DefaultAppearanceId;
        }

        private void SetSelectedOutfitId(string npcName, string category, string outfitId)
        {
            // Staged in memory only — BuildMenu()'s save callback writes it to disk on Save.
            if (!this.config.SelectedOutfits.TryGetValue(npcName, out Dictionary<string, string> byCategory))
            {
                byCategory = new Dictionary<string, string>();
                this.config.SelectedOutfits[npcName] = byCategory;
            }
            byCategory[category] = outfitId;
        }

        private ModConfig LoadConfig()
        {
            try
            {
                return this.Helper.ReadConfig<ModConfig>();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Could not read config.json, starting fresh: {ex.Message}", LogLevel.Warn);
                return new ModConfig();
            }
        }

        private Dictionary<string, CharacterData> LoadCharacters()
        {
            // Force a fresh read — otherwise a stale cached copy can miss another mod's edits.
            this.Helper.GameContent.InvalidateCache("Data/Characters");
            return this.Helper.GameContent.Load<Dictionary<string, CharacterData>>("Data/Characters");
        }

        private Dictionary<string, List<CharacterAppearanceData>> BuildLocalAssetOptions(IReadOnlyList<string> knownNpcNames)
        {
            string modFolder = this.Helper.DirectoryPath;
            List<LocalAssetFile> spriteFiles = LocalAssetScanner.ScanFolder(modFolder, "Characters", knownNpcNames, this.Monitor);
            List<LocalAssetFile> portraitFiles = LocalAssetScanner.ScanFolder(modFolder, "Portraits", knownNpcNames, this.Monitor);

            var portraitAssetByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (LocalAssetFile portraitFile in portraitFiles)
            {
                portraitAssetByKey[$"{portraitFile.NpcName}::{portraitFile.RawCondition}"] = ToLocalAssetName(portraitFile.RelativePath);
            }

            var result = new Dictionary<string, List<CharacterAppearanceData>>(StringComparer.OrdinalIgnoreCase);
            foreach (LocalAssetFile spriteFile in spriteFiles)
            {
                string category = AppearanceCategory.NormalizeLocalCondition(spriteFile.RawCondition);
                string spriteAsset = ToLocalAssetName(spriteFile.RelativePath);
                portraitAssetByKey.TryGetValue($"{spriteFile.NpcName}::{spriteFile.RawCondition}", out string portraitAsset);

                CharacterAppearanceData entry = AppearanceCategory.BuildLocalEntry(
                    id: $"{AppearanceCategory.LocalAssetIdPrefix}{spriteFile.NpcName}::{category}::{spriteFile.RawCondition}",
                    category: category,
                    sprite: spriteAsset,
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

        private static string ToLocalAssetName(string relativePath) =>
            LocalAssetPrefix + Path.ChangeExtension(relativePath, null);

        private List<CharacterAppearanceData> BuildDisplayAppearances(string npcName, CharacterData data)
        {
            var displayAppearances = new List<CharacterAppearanceData>
            {
                new CharacterAppearanceData
                {
                    Id = AppearanceCategory.DefaultAppearanceId,
                    Sprite = $"Characters/{npcName}",
                    Portrait = $"Portraits/{npcName}",
                },
            };
            if (data.Appearance is not null)
            {
                displayAppearances.AddRange(data.Appearance.Where(
                    a => a.Id is null || !a.Id.StartsWith(ForcedEntryIdPrefix, StringComparison.Ordinal)));
            }
            if (this.localAssetsByNpc.TryGetValue(npcName, out List<CharacterAppearanceData> localOptions))
            {
                displayAppearances.AddRange(localOptions);
            }

            return displayAppearances;
        }

        private void OnDumpCommand(string command, string[] args)
        {
            Dictionary<string, CharacterData> characters;
            try
            {
                characters = this.LoadCharacters();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Could not load Data/Characters: {ex.Message}", LogLevel.Error);
                return;
            }

            if (args.Length == 0)
            {
                foreach (var pair in characters.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                {
                    int count = pair.Value.Appearance?.Count ?? 0;
                    this.Monitor.Log($"{pair.Key}: {count} appearance entries", LogLevel.Info);
                }
                this.Monitor.Log($"Total: {characters.Count} characters. Run 'outfit_dump <name>' for full detail on one.", LogLevel.Info);
                return;
            }

            string npcName = args[0];
            if (!characters.TryGetValue(npcName, out CharacterData data))
            {
                this.Monitor.Log($"No character named '{npcName}' in Data/Characters.", LogLevel.Warn);
                return;
            }

            List<CharacterAppearanceData> appearances = data.Appearance;
            if (appearances is null || appearances.Count == 0)
            {
                this.Monitor.Log($"{npcName}: no appearance entries.", LogLevel.Info);
                return;
            }

            this.Monitor.Log($"{npcName}: {appearances.Count} appearance entries", LogLevel.Info);
            foreach (CharacterAppearanceData app in appearances)
            {
                this.Monitor.Log(
                    $"  Id={app.Id}\n"
                    + $"    Condition:  {app.Condition}\n"
                    + $"    Season:     {app.Season?.ToString() ?? "(none)"}\n"
                    + $"    Sprite:     {app.Sprite}\n"
                    + $"    Portrait:   {app.Portrait}\n"
                    + $"    Precedence: {app.Precedence}",
                    LogLevel.Info);
            }
        }

        private void BuildMenu()
        {
            var gmcm = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (gmcm is null)
            {
                this.Monitor.Log("Generic Mod Config Menu not installed; nothing to show.", LogLevel.Info);
                return;
            }

            foreach (AppearancePreviewGrid grid in this.activeGrids)
            {
                grid.Dispose();
            }
            this.activeGrids.Clear();

            // Reload from disk on every rebuild so a Cancel from a previous visit is discarded here
            // instead of lingering as an unsaved in-memory choice.
            this.config = this.LoadConfig();

            try { gmcm.Unregister(this.ModManifest); }
            catch { }

            gmcm.Register(
                mod: this.ModManifest,
                // GMCM's "Default" button calls this then automatically calls save() right after.
                reset: () => this.config = new ModConfig(),
                save: () =>
                {
                    this.Helper.WriteConfig(this.config);
                    this.Helper.GameContent.InvalidateCache("Data/Characters");

                    // The game only re-picks an NPC's appearance when it warps between locations —
                    // reload explicitly so a save-in-progress updates immediately.
                    if (Context.IsWorldReady)
                    {
                        foreach (string npcName in this.config.SelectedOutfits.Keys)
                        {
                            Game1.getCharacterFromName(npcName)?.reloadSprite();
                        }
                    }
                },
                titleScreenOnly: false
            );

            // setValue on AddBoolOption/AddTextOption only fires on Save; OnFieldChanged fires live,
            // which the grid below needs to update its checked outfit as soon as the category changes.
            gmcm.OnFieldChanged(this.ModManifest, this.OnGmcmFieldChanged);

            Dictionary<string, CharacterData> characters;
            try
            {
                characters = this.LoadCharacters();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Could not load Data/Characters: {ex.Message}", LogLevel.Warn);
                gmcm.AddParagraph(this.ModManifest, () =>
                    "Could not read Data/Characters yet. Load a save and reopen this menu.");
                return;
            }

            this.localAssetsByNpc = this.BuildLocalAssetOptions(characters.Keys.ToList());

            gmcm.AddTextOption(
                mod: this.ModManifest,
                name: () => "Search NPCs",
                tooltip: () => "Type a name, then toggle Search below to filter the list.",
                getValue: () => this.searchQuery,
                setValue: value => this.searchQuery = value,
                fieldId: SearchQueryFieldId
            );
            gmcm.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Search",
                tooltip: () => "Apply the name filter above. Leave the search box empty and toggle to show everyone again.",
                getValue: () => this.searchTrigger,
                setValue: value => this.searchTrigger = value,
                fieldId: SearchTriggerFieldId
            );

            int npcCount = 0;
            int appearanceCount = 0;

            var displayAppearancesByNpc = new Dictionary<string, List<CharacterAppearanceData>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in characters)
            {
                if (pair.Key == UnrevealedNpcKey)
                {
                    continue;
                }
                displayAppearancesByNpc[pair.Key] = this.BuildDisplayAppearances(pair.Key, pair.Value);
            }

            // Every NPC gets the same full set of categories, not just what it already has — the
            // whole point is being able to force e.g. a summer look onto winter.
            string[] categories = AppearanceCategory.CollectGlobalCategories(
                displayAppearancesByNpc.Values.SelectMany(list => list));

            foreach (var pair in characters.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                string npcName = pair.Key;
                if (npcName == UnrevealedNpcKey)
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(this.searchQuery)
                    && npcName.IndexOf(this.searchQuery, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                npcCount++;

                gmcm.AddSectionTitle(this.ModManifest, () => npcName);

                List<CharacterAppearanceData> displayAppearances = displayAppearancesByNpc[npcName];
                appearanceCount += displayAppearances.Count;

                // Reset to the actual current season on every menu (re)open — otherwise the dropdown
                // keeps whatever was picked last time, and it's easy to edit the wrong season by accident.
                string currentSeasonCategory = Context.IsWorldReady
                    ? char.ToUpperInvariant(Game1.currentSeason[0]) + Game1.currentSeason.Substring(1)
                    : null;
                this.selectedCategoryByNpc[npcName] = currentSeasonCategory is not null && categories.Contains(currentSeasonCategory)
                    ? currentSeasonCategory
                    : categories[0];

                gmcm.AddTextOption(
                    mod: this.ModManifest,
                    name: () => "Select option",
                    tooltip: () => "Choose which condition to assign an outfit for",
                    getValue: () => this.selectedCategoryByNpc[npcName],
                    setValue: value => this.selectedCategoryByNpc[npcName] = value,
                    choices: categories,
                    fieldId: $"{CategoryFieldIdPrefix}{npcName}"
                );

                var previewGrid = new AppearancePreviewGrid(
                    this.Helper,
                    npcName,
                    displayAppearances,
                    getCurrentCategory: () => this.selectedCategoryByNpc[npcName],
                    getSelectedOutfitId: category => this.GetSelectedOutfitId(
                        npcName, category, GetNaturalOutfitId(displayAppearances, category)),
                    setSelectedOutfitId: (category, outfitId) => this.SetSelectedOutfitId(npcName, category, outfitId),
                    hasOverride: category => this.HasOverride(npcName, category),
                    clearOverride: category => this.ClearOverride(npcName, category));
                this.activeGrids.Add(previewGrid);

                gmcm.AddComplexOption(
                    mod: this.ModManifest,
                    name: () => "",
                    draw: previewGrid.Draw,
                    height: () => previewGrid.Height,
                    beforeMenuOpened: () => this.config = this.LoadConfig(),
                    fieldId: $"AppearanceGrid_{npcName}"
                );
            }

            this.Monitor.Log(
                $"Built GMCM list: {npcCount} characters, {appearanceCount} appearance entries "
                + $"({this.localAssetsByNpc.Values.Sum(list => list.Count)} from local assets).",
                LogLevel.Info);
        }
    }
}
