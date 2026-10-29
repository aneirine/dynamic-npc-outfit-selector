using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace NpcOutfitSelector
{
    // Everything that touches config.json on disk, kept apart from ModEntry so it can be tested
    // through SMAPI's own JSON reader without a running game.
    //
    // Reading never writes: SMAPI's ReadConfig writes the file straight back, which deletes any
    // field this version doesn't know. Saving merges the known fields into the file as it is on
    // disk, so those fields survive, and is refused while the file can't be read, since an empty
    // config would otherwise replace it.
    //
    // Backups sit beside config.json, each overwritten when it's next made:
    //   config.backup.json        the file before this session first wrote to it
    //   config.before-reset.json  the file right before the latest Reset
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

        // The last config read successfully. A failed read keeps the previous one in play.
        public ModConfig Config { get; private set; } = new();

        public bool HasLoaded { get; private set; }

        // Why the latest read failed, or null if it didn't.
        public string LoadError { get; private set; }

        public void Reload()
        {
            ModConfig loaded;
            try
            {
                loaded = this.data.ReadJsonFile<ModConfig>(FileName) ?? new ModConfig();
            }
            catch (Exception ex)
            {
                this.LoadError = ex.Message;
                this.monitor.Log(
                    $"Could not read {this.PathOf(FileName)}. It has not been changed, and nothing will be saved over it "
                    + $"until it can be read: fix or remove it, then reopen the menu.\n{ex.Message}",
                    LogLevel.Error);
                return;
            }

            this.LoadError = null;
            this.HasLoaded = true;
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
                if (File.Exists(path))
                {
                    if (!this.backedUpThisSession)
                    {
                        File.Copy(path, this.PathOf(BackupFileName), overwrite: true);
                        this.backedUpThisSession = true;
                    }
                    if (isReset)
                    {
                        File.Copy(path, this.PathOf(ResetBackupFileName), overwrite: true);
                        this.monitor.Log(
                            $"Reset: your previous settings were copied to {this.PathOf(ResetBackupFileName)}. "
                            + "To undo the reset, close the game and rename that file to config.json.",
                            LogLevel.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                this.monitor.Log($"Not saved: could not back up {path} first.\n{ex.Message}", LogLevel.Error);
                return false;
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
                this.monitor.Log($"Could not save {path}; {BackupFileName} beside it holds the last good copy.\n{ex.Message}", LogLevel.Error);
                return false;
            }

            this.Config = config;
            return true;
        }

        private string PathOf(string fileName) => Path.Combine(this.folder, fileName);
    }
}
