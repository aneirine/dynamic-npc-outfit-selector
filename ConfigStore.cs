using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace NpcOutfitSelector
{
    // Everything that touches config.json on disk, kept apart from ModEntry so it can be tested
    // through SMAPI's own JSON reader without a running game.
    //
    // Reading an existing file never writes it: SMAPI's ReadConfig writes the file straight back,
    // which deletes any field this version doesn't know. Only a missing file is created, with the
    // defaults, as 1.1.1 did. Saving merges the known fields into the file as it is on disk, so
    // those fields survive, and is refused while the file can't be read, since an empty config
    // would otherwise replace it.
    //
    // Backups sit beside config.json:
    //   config.backup.json        the file before this session first wrote to it
    //   config.before-reset.json  the file right before the latest Reset that had settings to lose
    // Neither is overwritten by a file with nothing to keep in it (see HasSettingsToKeep), so an
    // emptied config.json (a crash mid-write, a full disk, two Resets in a row) can't take the
    // last good copy with it.
    //
    // Writes stay in place (SMAPI truncates and rewrites the file). Don't switch to a temp file
    // plus rename or File.Replace: Vortex keeps a hard link on config.json, and a rename breaks it.
    internal sealed class ConfigStore
    {
        public const string FileName = "config.json";
        public const string BackupFileName = "config.backup.json";
        public const string ResetBackupFileName = "config.before-reset.json";

        private readonly IDataHelper data;
        private readonly string folder;
        private readonly IMonitor monitor;
        private bool backedUpThisSession;

        public ConfigStore(IDataHelper data, string folder, IMonitor monitor)
        {
            this.data = data;
            this.folder = folder;
            this.monitor = monitor;
        }

        // The last config read or saved successfully. A failed read keeps the previous one in play.
        public ModConfig Config { get; private set; } = new();

        // Why the latest read failed, or null if it didn't.
        public string LoadError { get; private set; }

        // Set when config.json exists but holds nothing (0 bytes, blanks, NUL bytes or null) while
        // config.backup.json has settings in it: most likely a write that was cut off.
        public string EmptyFileWarning { get; private set; }

        // A sentence pointing at config.backup.json when it holds settings worth going back to,
        // else "". Cached, since the menu asks for it on every frame it draws the notice.
        public string RestoreHint { get; private set; } = "";

        public void Reload()
        {
            string path = this.PathOf(FileName);
            this.RefreshRestoreHint();

            ModConfig loaded;
            try
            {
                loaded = this.data.ReadJsonFile<ModConfig>(FileName);
            }
            catch (Exception ex)
            {
                this.LoadError = ex.Message;
                this.monitor.Log(
                    $"Could not read {path}. It has not been changed, and nothing will be saved over it "
                    + $"until it can be read: fix or remove it, then reopen the menu.{this.RestoreHint}\n{ex.Message}",
                    LogLevel.Error);
                return;
            }

            this.LoadError = null;
            this.EmptyFileWarning = null;

            // SMAPI's reader returns null both for a missing file and for one with no content, so
            // only File.Exists can tell them apart. A missing file is safe to create; an empty one
            // is left alone, since the backup may be the only copy of what it held.
            if (loaded is null)
            {
                if (!File.Exists(path))
                {
                    this.CreateDefaultFile(path);
                }
                else if (this.RestoreHint != "")
                {
                    this.EmptyFileWarning = $"{FileName} is empty, so no outfits are picked.{this.RestoreHint}";
                    this.monitor.LogOnce($"{path} is empty.{this.RestoreHint}", LogLevel.Warn);
                }
                loaded = new ModConfig();
            }

            loaded.RepairNulls();
            foreach (string warning in loaded.MigrateLegacyKeys())
            {
                this.monitor.LogOnce(warning, LogLevel.Warn);
            }
            this.Config = loaded;
        }

        public bool Save(ModConfig config, bool isReset = false)
        {
            string path = this.PathOf(FileName);
            if (this.LoadError is not null)
            {
                this.monitor.Log($"Not saved: {path} could not be read, and saving would replace it.\n{this.LoadError}", LogLevel.Error);
                return false;
            }

            JObject file;
            try
            {
                file = this.data.ReadJsonFile<JObject>(FileName) ?? new JObject();
            }
            catch (Exception ex)
            {
                this.monitor.Log($"Not saved: {path} was changed since it was read and can no longer be read.\n{ex.Message}", LogLevel.Error);
                return false;
            }

            try
            {
                bool worthKeeping = File.Exists(path) && HasSettingsToKeep(file);
                if (!this.backedUpThisSession)
                {
                    if (worthKeeping)
                    {
                        CopyBackup(path, this.PathOf(BackupFileName));
                    }
                    this.backedUpThisSession = true;
                }
                if (isReset)
                {
                    if (worthKeeping)
                    {
                        CopyBackup(path, this.PathOf(ResetBackupFileName));
                        this.monitor.Log(
                            $"Reset: your previous settings were copied to {this.PathOf(ResetBackupFileName)}. "
                            + $"To undo the reset, close the game and replace {FileName} with that file.",
                            LogLevel.Info);
                    }
                    else if (File.Exists(this.PathOf(ResetBackupFileName)))
                    {
                        this.monitor.Log(
                            $"Reset: nothing was set, so {this.PathOf(ResetBackupFileName)} was left as it was. "
                            + "It still holds the settings from before the last reset that had any.",
                            LogLevel.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                this.monitor.Log($"Not saved: could not back up {path} first.\n{ex.Message}", LogLevel.Error);
                return false;
            }
            finally
            {
                this.RefreshRestoreHint();
            }

            // A plain serializer, so another mod's global Newtonsoft defaults can't reshape the
            // output. The hotkey is written as its string, as SMAPI's own converter does.
            file["SelectedOutfits"] = JObject.FromObject(config.SelectedOutfits, new JsonSerializer());
            file["MenuHotkey"] = config.MenuHotkey is null ? JValue.CreateNull() : new JValue(config.MenuHotkey.ToString());

            try
            {
                this.data.WriteJsonFile(FileName, file);
            }
            catch (Exception ex)
            {
                this.monitor.Log($"Could not save {path}.{this.RestoreHint}\n{ex.Message}", LogLevel.Error);
                return false;
            }

            this.Config = config;
            this.EmptyFileWarning = null;
            return true;
        }

        // Something a Reset, or a file emptied by a cut-off write, would lose: a picked outfit or
        // a hotkey other than the default. A shape this version can't read counts as something.
        internal static bool HasSettingsToKeep(JObject file)
        {
            if (file is null)
            {
                return false;
            }

            JToken outfits = file.GetValue(nameof(ModConfig.SelectedOutfits), StringComparison.OrdinalIgnoreCase);
            bool hasPicks = outfits switch
            {
                null => false,
                JObject npcs => npcs.Properties().Any(npc => npc.Value is JObject slots
                    ? slots.Properties().Any(slot => slot.Value.Type != JTokenType.Null)
                    : npc.Value.Type != JTokenType.Null),
                _ => outfits.Type != JTokenType.Null,
            };
            return hasPicks || HasCustomHotkey(file);
        }

        // Missing means the default applies; null or "" turn the hotkey off, which a Reset undoes.
        private static bool HasCustomHotkey(JObject file)
        {
            JToken hotkey = file.GetValue(nameof(ModConfig.MenuHotkey), StringComparison.OrdinalIgnoreCase);
            if (hotkey is null)
            {
                return false;
            }
            if (hotkey.Type != JTokenType.String)
            {
                return true;
            }
            return !KeybindList.TryParse((string)hotkey, out KeybindList parsed, out _)
                || parsed.ToString() != new ModConfig().MenuHotkey.ToString();
        }

        private void RefreshRestoreHint()
        {
            this.RestoreHint = HasSettingsToKeep(this.TryRead(BackupFileName))
                ? $" {BackupFileName} beside it has an earlier copy: to go back to it, close the game "
                    + $"and replace {FileName} with it."
                : "";
        }

        // File.Copy carries the read-only flag over, and a read-only backup would then refuse
        // every later copy, which blocks saving for good.
        private static void CopyBackup(string from, string to)
        {
            ClearReadOnly(to);
            File.Copy(from, to, overwrite: true);
            ClearReadOnly(to);
        }

        private static void ClearReadOnly(string path)
        {
            if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
            }
        }

        // What 1.1.1 did on first launch through ReadConfig, so a player without GMCM has a file
        // to edit. Only ever for a missing file.
        private void CreateDefaultFile(string path)
        {
            try
            {
                this.data.WriteJsonFile(FileName, new ModConfig());
            }
            catch (Exception ex)
            {
                this.monitor.Log($"Could not create {path} with the default settings.\n{ex.Message}", LogLevel.Warn);
            }
        }

        private JObject TryRead(string fileName)
        {
            try
            {
                return this.data.ReadJsonFile<JObject>(fileName);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private string PathOf(string fileName) => Path.Combine(this.folder, fileName);
    }
}
