using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Characters;
using StardewValley.Locations;
using StardewValley.Menus;

namespace NpcOutfitSelector
{
    public class ModEntry : Mod
    {
        private const string CategoryFieldIdPrefix = "SelectedCategory_";
        private const string WeatherFieldIdPrefix = "SelectedWeather_";
        private const string LocationFieldIdPrefix = "SelectedLocation_";
        private const string SearchQueryFieldId = "Search_Query";
        private const string SearchClearFieldId = "Search_Clear";
        private const string MenuHotkeyFieldId = "Menu_Hotkey";

        // Marks entries OnAssetRequested injects to force a choice; filtered out of the grid so a
        // forced entry doesn't show up as a duplicate of its source.
        private const string ForcedEntryIdPrefix = "NpcOutfitSelector.Force.";

        // Asset namespace local PNGs (see LocalAssetScanner) are exposed under, so a forced entry
        // can reference them like any other texture.
        private const string LocalAssetPrefix = "Mods/NpcOutfitSelector/LocalAssets/";

        // Vanilla's key for an as-yet-unrevealed NPC slot — not a real character, skip it everywhere.
        private const string UnrevealedNpcKey = "???";

        // The three axes the player picks per NPC; combined into one category key (see
        // AppearanceCategory.Compose) at the moment an outfit is checked.
        private readonly Dictionary<string, string> selectedCategoryByNpc = new();
        private readonly Dictionary<string, string> selectedWeatherByNpc = new();
        private readonly Dictionary<string, string> selectedLocationByNpc = new();

        private readonly List<AppearancePreviewGrid> activeGrids = new();
        private SearchClearButton searchClearButton;

        // Every NPC this session has ever had an override for, kept because a save that *removes*
        // overrides still has to repaint the characters those overrides used to apply to. Reloading
        // one that didn't actually change is harmless — it just re-picks the same appearance.
        private readonly HashSet<string> npcsTouchedByConfig = new(StringComparer.OrdinalIgnoreCase);
        private ModConfig config;
        private Dictionary<string, List<CharacterAppearanceData>> localAssetsByNpc;

        // Typed live via OnFieldChanged, but only applied to the visible list on Enter — not on
        // every keystroke, since GMCM's own Textbox only takes keyboard focus back via a mouse
        // click, so rebuilding the menu (which replaces it with a fresh Textbox) on every character
        // would force a re-click after each letter.
        //
        // Deliberately survives closing the menu: the Clear search button is the way out, so a
        // filter can't strand the player with no visible means of undoing it.
        private string searchQuery = "";

        // Set right before our own RebuildAndShowMenu() calls OpenModMenu(), so the MenuChanged
        // event that reopen fires doesn't trigger a second BuildMenu() — a second build would
        // create a fresh set of AppearancePreviewGrid instances subscribed to click events, while
        // the menu already on screen (built by OpenModMenu() from the first, now-orphaned set)
        // keeps drawing them but can never route clicks to them again.
        private bool suppressNextMenuChangedRebuild;

        public override void Entry(IModHelper helper)
        {
            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            helper.Events.GameLoop.DayStarted += this.OnDayStarted;
            helper.Events.Display.MenuChanged += this.OnMenuChanged;
            helper.Events.Content.AssetRequested += this.OnAssetRequested;
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;

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
                if (this.suppressNextMenuChangedRebuild)
                {
                    this.suppressNextMenuChangedRebuild = false;
                    return;
                }

                this.BuildMenu();
            }
        }

        // Mirrors GMCM's own internal "ActiveConfigMenu" lookup: on the title screen it nests its
        // menu under TitleMenu.subMenu instead of replacing Game1.activeClickableMenu, and in-game
        // it's chained as a child menu of the pause menu via SetChildMenu rather than becoming
        // Game1.activeClickableMenu directly — so both have to be walked, not just read once.
        private static IClickableMenu GetActiveGmcmMenu()
        {
            IClickableMenu menu = Game1.activeClickableMenu is TitleMenu
                ? TitleMenu.subMenu
                : Game1.activeClickableMenu;

            while (menu?.GetChildMenu() is not null)
            {
                menu = menu.GetChildMenu();
            }

            return menu is not null && menu.GetType().Name.Contains("ModConfigMenu") ? menu : null;
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (GetActiveGmcmMenu() is not null)
            {
                if (e.Button == SButton.Enter)
                {
                    this.ScheduleSearchApply();
                }
                return;
            }

            // Free-roam only, so the shortcut can't fire mid-cutscene or while another menu has
            // the keyboard — and can't collide with Enter above, which needs our page already open.
            if (Context.IsPlayerFree && this.config?.MenuHotkey?.JustPressed() == true)
            {
                this.searchQuery = "";
                this.RebuildAndShowMenu();
            }
        }

        // Deferred a tick so the reopen never runs while the game is still handling the click or
        // keypress that asked for it — doing it inline caused a reentrant
        // MenuChanged -> BuildMenu -> OpenModMenu loop that crashed the game.
        private void ScheduleSearchApply()
        {
            this.Helper.Events.GameLoop.UpdateTicked -= this.OnSearchApplyTicked;
            this.Helper.Events.GameLoop.UpdateTicked += this.OnSearchApplyTicked;
        }

        private void OnSearchApplyTicked(object sender, UpdateTickedEventArgs e)
        {
            this.Helper.Events.GameLoop.UpdateTicked -= this.OnSearchApplyTicked;
            this.RebuildAndShowMenu();
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
                        if (outfitId == AppearanceConstants.DefaultAppearanceId)
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

            if (value is not string choice)
            {
                return;
            }

            if (fieldId.StartsWith(CategoryFieldIdPrefix))
            {
                this.selectedCategoryByNpc[fieldId.Substring(CategoryFieldIdPrefix.Length)] = choice;
            }
            else if (fieldId.StartsWith(WeatherFieldIdPrefix))
            {
                this.selectedWeatherByNpc[fieldId.Substring(WeatherFieldIdPrefix.Length)] = choice;
            }
            else if (fieldId.StartsWith(LocationFieldIdPrefix))
            {
                this.selectedLocationByNpc[fieldId.Substring(LocationFieldIdPrefix.Length)] = choice;
            }
        }

        // Rebuilds the option list, then has GMCM build a menu around it — GMCM's own Table is
        // constructed once per menu and can't be swapped in place, so replacing the whole menu is
        // the only way to show a changed option list. Never call this from a GMCM callback: the
        // reopen re-enters our own MenuChanged handler.
        private void RebuildAndShowMenu()
        {
            var gmcm = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (gmcm is null)
            {
                return;
            }

            this.BuildMenu();
            this.suppressNextMenuChangedRebuild = true;
            gmcm.OpenModMenu(this.ModManifest);
        }

        // Real place names from the loaded save, so locations added by other mods show up in the
        // Where dropdown without this mod knowing anything about them. Skips the ones that would
        // bury the list without ever being useful for an outfit: one entry per mine level, and
        // every player-built cabin, shed and barn.
        private IEnumerable<string> DiscoverPlaces()
        {
            if (!Context.IsWorldReady)
            {
                yield break;
            }

            foreach (GameLocation location in Game1.locations)
            {
                if (string.IsNullOrWhiteSpace(location?.Name)
                    || location is MineShaft
                    || location.ParentBuilding is not null
                    || location.Name.StartsWith("UndergroundMine", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return location.Name;
            }
        }

        // Which weather/location the two narrowing dropdowns should open on: the segments of the
        // most specific saved override that actually applies to this NPC right now. Opening on
        // Any/Any instead would show the plain-season pick as checked while the character in the
        // world is visibly wearing a narrower one.
        private (string Weather, string Location) ResolveActiveAxes(
            string npcName, string baseCategory, IReadOnlyDictionary<string, GameLocation> npcLocations)
        {
            string any = AppearanceConstants.AnyOption;

            if (!this.config.SelectedOutfits.TryGetValue(npcName, out Dictionary<string, string> byCategory)
                || !npcLocations.TryGetValue(npcName, out GameLocation location)
                || location is null)
            {
                return (any, any);
            }

            string currentWeather = AppearanceCategory.WeatherKeyForConstant(location.GetWeather()?.Weather);
            // Both granularities of the same axis are true at once — the Beach *is* outdoors — so
            // a key naming either one applies, and the score decides which the menu opens on.
            string currentCoarse = AppearanceCategory.LocationKeyFor(location.IsOutdoors);
            string currentPlace = location.Name;

            string bestWeather = any;
            string bestWhere = any;
            int bestScore = -1;

            foreach (string key in byCategory.Keys)
            {
                (string keyBase, string keyWeather, string keyWhere) = AppearanceCategory.Decompose(key);

                if (!keyBase.Equals(baseCategory, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                // A null segment means "any", which always applies; a set one has to match reality.
                if (keyWeather is not null && !keyWeather.Equals(currentWeather, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (keyWhere is not null
                    && !keyWhere.Equals(currentCoarse, StringComparison.OrdinalIgnoreCase)
                    && !keyWhere.Equals(currentPlace, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int score = AppearanceCategory.ScoreForCategory(key);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestWeather = keyWeather ?? any;
                    bestWhere = keyWhere ?? any;
                }
            }

            return (bestWeather, bestWhere);
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

            return best?.Id ?? AppearanceConstants.DefaultAppearanceId;
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
            ModConfig loaded;
            try
            {
                loaded = this.Helper.ReadConfig<ModConfig>();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Could not read config.json, starting fresh: {ex.Message}", LogLevel.Warn);
                loaded = new ModConfig();
            }

            this.npcsTouchedByConfig.UnionWith(loaded.SelectedOutfits.Keys);
            MigrateLegacyKeys(loaded);
            return loaded;
        }

        // Rewrites keys onto the canonical spelling of their base — currently only Beach -> Island.
        // Done on load rather than left as an alias so a player who re-saves doesn't end up with
        // both spellings for the same thing: they'd score identically, and the game breaks an exact
        // Precedence tie with weighted random rather than by order.
        private static void MigrateLegacyKeys(ModConfig config)
        {
            foreach (Dictionary<string, string> byCategory in config.SelectedOutfits.Values)
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
                    id: $"{AppearanceConstants.LocalAssetIdPrefix}{spriteFile.NpcName}::{category}::{spriteFile.RawCondition}",
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
                    Id = AppearanceConstants.DefaultAppearanceId,
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

            this.searchClearButton?.Dispose();
            this.searchClearButton = null;

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
                    if (!Context.IsWorldReady)
                    {
                        return;
                    }

                    // Deliberately not just the current keys: "Default" empties the whole config
                    // before this runs, so the characters that need repainting are exactly the ones
                    // no longer listed here.
                    this.npcsTouchedByConfig.UnionWith(this.config.SelectedOutfits.Keys);
                    foreach (string npcName in this.npcsTouchedByConfig)
                    {
                        Game1.getCharacterFromName(npcName)?.reloadSprite();
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

            gmcm.AddKeybindList(
                mod: this.ModManifest,
                name: () => "Open this menu",
                tooltip: () => "Opens this page directly while playing. Clear it to disable the shortcut.",
                getValue: () => this.config.MenuHotkey,
                setValue: value => this.config.MenuHotkey = value,
                fieldId: MenuHotkeyFieldId
            );

            gmcm.AddTextOption(
                mod: this.ModManifest,
                name: () => "Search NPCs",
                tooltip: () => "Type a name and press Enter to filter the list. The filter is kept when "
                    + "you leave the menu — use Clear search below to drop it.",
                getValue: () => this.searchQuery,
                setValue: value => this.searchQuery = value,
                fieldId: SearchQueryFieldId
            );

            this.searchClearButton = new SearchClearButton(
                this.Helper,
                getQuery: () => this.searchQuery,
                clear: () =>
                {
                    this.searchQuery = "";
                    this.ScheduleSearchApply();
                });

            gmcm.AddComplexOption(
                mod: this.ModManifest,
                name: () => "",
                draw: this.searchClearButton.Draw,
                height: () => this.searchClearButton.Height,
                fieldId: SearchClearFieldId
            );

            int npcCount = 0;
            int appearanceCount = 0;

            // Gathered once rather than per NPC: getCharacterFromName searches every loaded
            // location, and this menu is built for the whole cast at a time.
            var npcLocations = new Dictionary<string, GameLocation>(StringComparer.OrdinalIgnoreCase);
            if (Context.IsWorldReady)
            {
                Utility.ForEachVillager(npc =>
                {
                    npcLocations[npc.Name] = npc.currentLocation;
                    return true;
                });
            }

            string[] whereChoices = AppearanceCategory.BuildWhereChoices(this.DiscoverPlaces());

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

                (string activeWeather, string activeLocation) = this.ResolveActiveAxes(
                    npcName, this.selectedCategoryByNpc[npcName], npcLocations);
                this.selectedWeatherByNpc[npcName] = activeWeather;
                this.selectedLocationByNpc[npcName] = activeLocation;

                gmcm.AddTextOption(
                    mod: this.ModManifest,
                    name: () => "Select option",
                    tooltip: () => "Choose which condition to assign an outfit for",
                    getValue: () => this.selectedCategoryByNpc[npcName],
                    setValue: value => this.selectedCategoryByNpc[npcName] = value,
                    choices: categories,
                    fieldId: $"{CategoryFieldIdPrefix}{npcName}"
                );

                gmcm.AddTextOption(
                    mod: this.ModManifest,
                    name: () => "Weather",
                    tooltip: () => "Narrow the choice above to one weather. Leave on Any to cover every weather.",
                    getValue: () => this.selectedWeatherByNpc[npcName],
                    setValue: value => this.selectedWeatherByNpc[npcName] = value,
                    choices: AppearanceCategory.WeatherChoices,
                    formatChoice: AppearanceCategory.FormatWeatherChoice,
                    fieldId: $"{WeatherFieldIdPrefix}{npcName}"
                );

                gmcm.AddTextOption(
                    mod: this.ModManifest,
                    name: () => "Where",
                    tooltip: () => "Narrow the choice above to indoors, outdoors, or one exact place. "
                        + "This is where the NPC is, not where you are. A named place wins over plain "
                        + "Indoor/Outdoor, which stays as the fallback when no exact place is set.",
                    getValue: () => this.selectedLocationByNpc[npcName],
                    setValue: value => this.selectedLocationByNpc[npcName] = value,
                    choices: whereChoices,
                    formatChoice: AppearanceCategory.FormatWhereChoice,
                    fieldId: $"{LocationFieldIdPrefix}{npcName}"
                );

                var previewGrid = new AppearancePreviewGrid(
                    this.Helper,
                    npcName,
                    displayAppearances,
                    getCurrentCategory: () => AppearanceCategory.Compose(
                        this.selectedCategoryByNpc[npcName],
                        this.selectedWeatherByNpc[npcName],
                        this.selectedLocationByNpc[npcName]),
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
