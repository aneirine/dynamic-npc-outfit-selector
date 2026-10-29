using System;
using StardewModdingAPI;

namespace NpcOutfitSelector
{
    // Everything that touches config.json on disk, kept apart from ModEntry so it can be tested
    // through SMAPI's own JSON reader without a running game.
    internal sealed class ConfigStore
    {
        public const string FileName = "config.json";

        private readonly IDataHelper data;
        private readonly IMonitor monitor;

        public ConfigStore(IDataHelper data, IMonitor monitor)
        {
            this.data = data;
            this.monitor = monitor;
        }

        // Same as SMAPI's Helper.ReadConfig: read, fall back to a new config, write it back.
        public ModConfig Load()
        {
            ModConfig loaded;
            try
            {
                loaded = this.data.ReadJsonFile<ModConfig>(FileName) ?? new ModConfig();
                this.data.WriteJsonFile(FileName, loaded);
            }
            catch (Exception ex)
            {
                this.monitor.Log($"Could not read config.json, starting fresh: {ex.Message}", LogLevel.Warn);
                loaded = new ModConfig();
            }

            foreach (string warning in loaded.MigrateLegacyKeys())
            {
                this.monitor.LogOnce(warning, LogLevel.Warn);
            }
            return loaded;
        }

        public void Save(ModConfig config)
        {
            this.data.WriteJsonFile(FileName, config);
        }
    }
}
