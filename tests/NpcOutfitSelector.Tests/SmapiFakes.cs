using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewModdingAPI.Framework.Logging;
using StardewModdingAPI.Toolkit.Serialization;
using StardewModdingAPI.Toolkit.Utilities;

namespace NpcOutfitSelector.Tests
{
    // Helper.Data as SMAPI implements it, over a temporary folder: the same JsonHelper, with the
    // same converters SCore adds at startup.
    internal sealed class FakeDataHelper : IDataHelper
    {
        private readonly JsonHelper json = CreateJsonHelper();

        public FakeDataHelper(string folder)
        {
            this.Folder = folder;
        }

        public string Folder { get; }

        public static JsonHelper CreateJsonHelper() => NpcOutfitSelector.Compat.CompatScenario.CreateSmapiJsonHelper();

        public TModel ReadJsonFile<TModel>(string path) where TModel : class =>
            this.json.ReadJsonFileIfExists(this.FullPath(path), out TModel result) ? result : null;

        public void WriteJsonFile<TModel>(string path, TModel data) where TModel : class
        {
            if (data is null)
            {
                File.Delete(this.FullPath(path));
                return;
            }
            this.json.WriteJsonFile(this.FullPath(path), data);
        }

        public string FullPath(string path) => Path.Combine(this.Folder, PathUtilities.NormalizePath(path));

        public TModel ReadSaveData<TModel>(string key) where TModel : class => throw new NotSupportedException();
        public void WriteSaveData<TModel>(string key, TModel data) where TModel : class => throw new NotSupportedException();
        public TModel ReadGlobalData<TModel>(string key) where TModel : class => throw new NotSupportedException();
        public void WriteGlobalData<TModel>(string key, TModel data) where TModel : class => throw new NotSupportedException();
    }

    internal sealed class FakeMonitor : IMonitor
    {
        public List<(LogLevel Level, string Message)> Messages { get; } = new();

        public bool IsVerbose => false;

        public void Log(string message, LogLevel level = LogLevel.Trace) => this.Messages.Add((level, message));

        public void LogOnce(string message, LogLevel level = LogLevel.Trace) => this.Log(message, level);

        public void VerboseLog(string message) { }

        public void VerboseLog(ref VerboseLogStringHandler message) { }
    }

    // A fresh folder per test, so one test's file can't leak into the next.
    internal sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            this.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NpcOutfitSelector.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(this.Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
