using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StardewValley.GameData.Characters;

namespace NpcOutfitSelector.Tests
{
    // config.json as a player has it, read through the same path the mod uses in game: SMAPI's
    // JSON reader and converters, then migration. A key passing here keeps what it *means* to the
    // game, which is stricter than keeping its spelling or the number of keys.
    internal static class ConfigFileTests
    {
        public static void Run()
        {
            Assert.Section("Released configs keep every pick through load, save and load");
            RoundTrip("config-1.0.1.json");
            RoundTrip("config-1.1.0.json", expectUnchangedKeys: true);
            RoundTrip("config-1.1.1.json", expectUnchangedKeys: true);

            Assert.Section("Hand-edited configs keep every value, and what 1.1.1 did with them");
            RoundTrip("config-hand-edited.json");
            HandEditedMatchesTheReleasedBuild();

            Assert.Section("Nulls and empty values load instead of crashing");
            RoundTrip("config-null-values.json");
            RoundTrip("config-null-outfits.json");
            RoundTrip("config-empty-hotkey.json");
            Nulls();

            Drafts();
            ReadingNeverWrites();
            UnknownFieldsSurvive();
            UnreadableFilesAreNeverReplaced();
            Backups();
            SettingsToKeep();
            EmptyFilesKeepTheBackup();
            RepeatedResetKeepsTheBackup();
            ReadOnlyFiles();
        }

        // Every shape a released version wrote or a player hand-edited, old and new.
        private static readonly string[] ReleasedFixtures =
        {
            "config-1.0.1.json", "config-1.1.0.json", "config-1.1.1.json", "config-hand-edited.json",
        };

        private static readonly (string Name, byte[] Bytes)[] EmptyContents =
        {
            ("0 bytes", Array.Empty<byte>()),
            ("blanks", System.Text.Encoding.UTF8.GetBytes("  \r\n\t ")),
            ("NUL bytes", new byte[64]),
            ("null", System.Text.Encoding.UTF8.GetBytes("null")),
        };

        // What a save cut off by a crash or a full disk can leave behind. SMAPI reads each of
        // these as "no file", so they load as a fresh config; the backup must outlive them.
        private static void EmptyFilesKeepTheBackup()
        {
            Assert.Section("An emptied config.json never takes the backup with it");
            foreach (string fixture in ReleasedFixtures)
            {
                byte[] goodBackup = File.ReadAllBytes(FixturePath(fixture));
                foreach ((string contentName, byte[] bytes) in EmptyContents)
                {
                    string name = $"{contentName}, backup {fixture}";
                    using var folder = new TempFolder();
                    var data = new FakeDataHelper(folder.Path);
                    string path = data.FullPath(ConfigStore.FileName);
                    string backup = Path.Combine(folder.Path, ConfigStore.BackupFileName);
                    File.WriteAllBytes(path, bytes);
                    File.WriteAllBytes(backup, goodBackup);

                    var monitor = new FakeMonitor();
                    var store = new ConfigStore(data, folder.Path, monitor);
                    store.Reload();
                    Assert.Equal($"{name}: loads with nothing picked", $"{store.LoadError ?? "ok"} {store.Config.SelectedOutfits.Count}", "ok 0");
                    Assert.Equal($"{name}: left as it is", File.ReadAllBytes(path).SequenceEqual(bytes), true);
                    Assert.Equal($"{name}: the menu names the backup", store.EmptyFileWarning?.Contains(ConfigStore.BackupFileName), true);
                    Assert.Equal($"{name}: so does the log", monitor.Messages.Any(m => m.Level == StardewModdingAPI.LogLevel.Warn && m.Message.Contains(ConfigStore.BackupFileName)), true);

                    Assert.Equal($"{name}: Reset doesn't copy it anywhere", store.Save(new ModConfig(), isReset: true)
                        && !File.Exists(Path.Combine(folder.Path, ConfigStore.ResetBackupFileName)), true);
                    store.Reload();
                    store.Config.SetSelectedOutfitId("Haley", "Fall", "pack.New");
                    Assert.Equal($"{name}: a save still works", store.Save(store.Config), true);
                    Assert.Equal($"{name}: and leaves the backup as it was", File.ReadAllBytes(backup).SequenceEqual(goodBackup), true);
                    Assert.Equal($"{name}: the warning goes once saved", store.EmptyFileWarning, null);
                    Assert.Equal($"{name}: the save reads back", Load(data, new FakeMonitor()).GetSelectedOutfitId("Haley", "Fall", null), "pack.New");
                }
            }

            foreach ((string name, byte[] bytes) in EmptyContents)
            {
                using var folder = new TempFolder();
                var data = new FakeDataHelper(folder.Path);
                File.WriteAllBytes(data.FullPath(ConfigStore.FileName), bytes);
                var store = new ConfigStore(data, folder.Path, new FakeMonitor());
                store.Reload();
                Assert.Equal($"{name}, no backup: nothing to warn about", store.EmptyFileWarning, null);
                store.Config.SetSelectedOutfitId("Haley", "Fall", "pack.New");
                Assert.Equal($"{name}, no backup: a save makes no empty backup",
                    store.Save(store.Config) && !File.Exists(Path.Combine(folder.Path, ConfigStore.BackupFileName)), true);
            }

            // A backup with nothing in it is nothing to go back to.
            using (var folder = new TempFolder())
            {
                var data = new FakeDataHelper(folder.Path);
                File.WriteAllBytes(data.FullPath(ConfigStore.FileName), Array.Empty<byte>());
                data.WriteJsonFile(ConfigStore.BackupFileName, new ModConfig());
                var store = new ConfigStore(data, folder.Path, new FakeMonitor());
                store.Reload();
                Assert.Equal("empty, backup of the defaults: no warning", $"{store.EmptyFileWarning ?? "none"} '{store.RestoreHint}'", "none ''");
            }
        }

        // What a Reset or an emptied file would lose, so what earns a backup.
        private static void SettingsToKeep()
        {
            Assert.Section("What counts as settings worth a backup");
            var cases = new (string Json, bool Expected)[]
            {
                ("{}", false),
                ("{ \"SelectedOutfits\": {}, \"MenuHotkey\": \"F9\" }", false),
                ("{ \"SelectedOutfits\": null }", false),
                ("{ \"SelectedOutfits\": { \"Haley\": null, \"Sam\": {} } }", false),
                ("{ \"SelectedOutfits\": { \"Haley\": { \"Spring\": null } } }", false),
                ("{ \"selectedoutfits\": {}, \"menuhotkey\": \"f9\" }", false),
                ("{ \"SomeFutureSetting\": true }", false),
                ("{ \"SelectedOutfits\": { \"Haley\": { \"Spring\": \"pack.A\" } } }", true),
                ("{ \"selectedoutfits\": { \"Haley\": { \"Beach\": \"pack.A\" } } }", true),
                ("{ \"SelectedOutfits\": { \"Haley\": { \"Spring\": [ \"pack.A\" ] } } }", true),
                ("{ \"SelectedOutfits\": { \"Haley\": \"pack.A\" } }", true),
                ("{ \"SelectedOutfits\": [ \"pack.A\" ] }", true),
                ("{ \"MenuHotkey\": \"F10\" }", true),
                ("{ \"MenuHotkey\": \"LeftShift + F9\" }", true),
                ("{ \"MenuHotkey\": \"\" }", true),
                ("{ \"MenuHotkey\": null }", true),
            };
            foreach ((string json, bool expected) in cases)
            {
                Assert.Equal(json, ConfigStore.HasSettingsToKeep(JObject.Parse(json)), expected);
            }
            Assert.Equal("no file at all", ConfigStore.HasSettingsToKeep(null), false);
            foreach (string fixture in ReleasedFixtures)
            {
                (FakeDataHelper data, TempFolder folder) = Open(fixture);
                using (folder)
                {
                    Assert.Equal(fixture, ConfigStore.HasSettingsToKeep(data.ReadJsonFile<JObject>(ConfigStore.FileName)), true);
                }
            }
        }

        // GMCM's Default button resets, saves and reopens the page with no confirmation, in the
        // same spot, so a double click is two resets.
        private static void RepeatedResetKeepsTheBackup()
        {
            Assert.Section("Reset twice still leaves the picks in config.before-reset.json");
            foreach (string fixture in ReleasedFixtures)
            {
                (FakeDataHelper data, TempFolder folder) = Open(fixture);
                using (folder)
                {
                    string path = data.FullPath(ConfigStore.FileName);
                    string beforeReset = Path.Combine(folder.Path, ConfigStore.ResetBackupFileName);
                    var monitor = new FakeMonitor();
                    var store = new ConfigStore(data, folder.Path, monitor);
                    store.Reload();
                    store.Config.SetSelectedOutfitId("Haley", "Fall", "pack.ThisSession");
                    store.Save(store.Config);
                    byte[] withPicks = File.ReadAllBytes(path);

                    store.Save(new ModConfig(), isReset: true);
                    Assert.Equal($"{fixture}: the log says how to undo it", monitor.Messages.Any(m => m.Message.Contains(beforeReset)
                        && m.Message.Contains($"replace {ConfigStore.FileName}")), true);
                    store.Reload();
                    store.Save(new ModConfig(), isReset: true);
                    Assert.Equal($"{fixture}: the second Reset leaves it alone", File.ReadAllBytes(beforeReset).SequenceEqual(withPicks), true);
                    Assert.Equal($"{fixture}: and says so instead of pointing at an empty copy",
                        monitor.Messages.Count(m => m.Message.Contains("was left as it was")), 1);

                    var nextSession = new ConfigStore(data, folder.Path, new FakeMonitor());
                    nextSession.Reload();
                    byte[] backupBefore = File.ReadAllBytes(Path.Combine(folder.Path, ConfigStore.BackupFileName));
                    nextSession.Save(nextSession.Config, isReset: true);
                    Assert.Equal($"{fixture}: a later session's Reset of an empty file keeps both backups",
                        File.ReadAllBytes(beforeReset).SequenceEqual(withPicks)
                        && File.ReadAllBytes(Path.Combine(folder.Path, ConfigStore.BackupFileName)).SequenceEqual(backupBefore), true);

                    File.Copy(beforeReset, path, overwrite: true);
                    Assert.Equal($"{fixture}: and putting it back restores this session's pick",
                        Load(data, new FakeMonitor()).GetSelectedOutfitId("Haley", "Fall", null), "pack.ThisSession");
                }
            }

            // Nothing picked, but a hotkey of the player's own: a Reset would lose it.
            using (var folder = new TempFolder())
            {
                var data = new FakeDataHelper(folder.Path);
                string path = data.FullPath(ConfigStore.FileName);
                File.WriteAllText(path, "{ \"SelectedOutfits\": {}, \"MenuHotkey\": \"F10\" }");
                byte[] before = File.ReadAllBytes(path);
                var store = new ConfigStore(data, folder.Path, new FakeMonitor());
                store.Reload();
                store.Save(new ModConfig(), isReset: true);
                store.Reload();
                store.Save(new ModConfig(), isReset: true);
                Assert.Equal("only a hotkey set: Reset still backs it up, and a second one keeps it",
                    File.ReadAllBytes(Path.Combine(folder.Path, ConfigStore.ResetBackupFileName)).SequenceEqual(before), true);
            }
        }

        // Players set config.json read-only to stop SMAPI rewriting it. File.Copy would carry the
        // flag over to the backup, and a read-only backup refuses every later copy.
        private static void ReadOnlyFiles()
        {
            Assert.Section("A read-only config.json doesn't leave a read-only backup behind");
            foreach (string fixture in new[] { "config-1.0.1.json", "config-1.1.1.json" })
            {
                (FakeDataHelper data, TempFolder folder) = Open(fixture);
                using (folder)
                {
                    string path = data.FullPath(ConfigStore.FileName);
                    string backup = Path.Combine(folder.Path, ConfigStore.BackupFileName);
                    string beforeReset = Path.Combine(folder.Path, ConfigStore.ResetBackupFileName);
                    int picks = Load(data, new FakeMonitor()).SelectedOutfits.Count;
                    File.SetAttributes(path, FileAttributes.ReadOnly);

                    var store = new ConfigStore(data, folder.Path, new FakeMonitor());
                    store.Reload();
                    Assert.Equal($"{fixture}: a read-only file still loads", $"{store.LoadError ?? "ok"} {store.Config.SelectedOutfits.Count}", $"ok {picks}");
                    store.Config.SetSelectedOutfitId("Haley", "Fall", "pack.New");
                    Assert.Equal($"{fixture}: saving over it fails", store.Save(store.Config), false);
                    Assert.Equal($"{fixture}: the backup it made is writable", File.GetAttributes(backup).HasFlag(FileAttributes.ReadOnly), false);
                    Assert.Equal($"{fixture}: Reset fails too", store.Save(new ModConfig(), isReset: true), false);
                    Assert.Equal($"{fixture}: and its backup is writable", File.GetAttributes(beforeReset).HasFlag(FileAttributes.ReadOnly), false);

                    File.SetAttributes(path, FileAttributes.Normal);
                    File.SetAttributes(backup, FileAttributes.ReadOnly);
                    File.SetAttributes(beforeReset, FileAttributes.ReadOnly);
                    var nextSession = new ConfigStore(data, folder.Path, new FakeMonitor());
                    nextSession.Reload();
                    nextSession.Config.SetSelectedOutfitId("Haley", "Fall", "pack.New");
                    Assert.Equal($"{fixture}: once config.json is writable again, read-only backups don't block saving",
                        nextSession.Save(nextSession.Config) && nextSession.Save(new ModConfig(), isReset: true), true);
                }
            }
        }

        private static readonly string[] AllFixtures =
        {
            "config-1.0.1.json", "config-1.1.0.json", "config-1.1.1.json", "config-hand-edited.json",
            "config-null-values.json", "config-null-outfits.json", "config-empty-hotkey.json",
            "config-future-list.json", "config-malformed.json",
        };

        private static void Nulls()
        {
            (FakeDataHelper data, TempFolder folder) = Open("config-null-values.json");
            using (folder)
            {
                ModConfig config = Load(data, new FakeMonitor());
                Assert.Equal("a null NPC table reads as empty", config.SelectedOutfits["Sam"].Count, 0);
                Assert.Equal("a null outfit isn't an override", config.HasOverride("Emily", "Spring"), false);
                Assert.Equal("and falls back to the natural pick", config.GetSelectedOutfitId("Emily", "Spring", "natural"), "natural");
                Assert.Equal("the rest still applies", config.GetSelectedOutfitId("Emily", "Summer", "natural"), "pack.EmilySummer");
            }

            (data, folder) = Open("config-null-outfits.json");
            using (folder)
            {
                ModConfig config = Load(data, new FakeMonitor());
                Assert.Equal("null SelectedOutfits reads as empty", config.SelectedOutfits.Count, 0);
                Assert.Equal("null hotkey reads as none bound", config.MenuHotkey.Keybinds.Length, 0);
            }

            (data, folder) = Open("config-empty-hotkey.json");
            using (folder)
            {
                Assert.Equal("empty hotkey reads as none bound", Load(data, new FakeMonitor()).MenuHotkey.Keybinds.Length, 0);
            }
        }

        // The menu edits a copy and only Save hands it back (A5), so the copy must share nothing
        // the game reads.
        private static void Drafts()
        {
            Assert.Section("A menu draft is independent of the live config");
            (FakeDataHelper data, TempFolder folder) = Open("config-1.1.1.json");
            using (folder)
            {
                ModConfig live = Load(data, new FakeMonitor());
                ModConfig draft = live.Clone();
                draft.SetSelectedOutfitId("Haley", "Fall", "pack.Draft");
                draft.SetSelectedOutfitId("Krobus", "Spring", "pack.Draft");
                draft.ClearOverride("Sam", "Fall");

                Assert.Equal("an edit to an NPC with picks", live.HasOverride("Haley", "Fall"), false);
                Assert.Equal("an NPC new to the draft", live.SelectedOutfits.ContainsKey("Krobus"), false);
                Assert.Equal("a cleared pick", live.GetSelectedOutfitId("Sam", "Fall", null), "Default");
                Assert.Equal("the copy starts out equal", string.Join("|", EffectivePicks(Load(data, new FakeMonitor()).Clone())),
                    string.Join("|", EffectivePicks(live)));
                Assert.Equal("and carries the hotkey", live.Clone().MenuHotkey.ToString(), "F9");
            }
        }

        // A3: SMAPI's ReadConfig wrote the file back on every read, which the mod did on launch,
        // each day, and once per NPC when the menu opened.
        private static void ReadingNeverWrites()
        {
            Assert.Section("Reading config.json never changes it");
            foreach (string fixture in AllFixtures)
            {
                (FakeDataHelper data, TempFolder folder) = Open(fixture);
                using (folder)
                {
                    byte[] before = File.ReadAllBytes(data.FullPath(ConfigStore.FileName));
                    var store = new ConfigStore(data, folder.Path, new FakeMonitor());
                    store.Reload();
                    store.Reload();
                    Assert.Equal($"{fixture}: same bytes after reading",
                        File.ReadAllBytes(data.FullPath(ConfigStore.FileName)).SequenceEqual(before), true);
                    Assert.Equal($"{fixture}: no other file made", Directory.GetFiles(folder.Path).Length, 1);
                }
            }

            (FakeDataHelper missingData, TempFolder missingFolder) = (null, new TempFolder());
            using (missingFolder)
            {
                missingData = new FakeDataHelper(missingFolder.Path);
                var store = new ConfigStore(missingData, missingFolder.Path, new FakeMonitor());
                store.Reload();
                Assert.Equal("no file: defaults, and only config.json created", $"{store.LoadError ?? "ok"} {string.Join(",", Directory.GetFiles(missingFolder.Path).Select(Path.GetFileName))}", "ok config.json");
                Assert.Equal("no file: created exactly as 1.1.1 created it",
                    File.ReadAllText(missingData.FullPath(ConfigStore.FileName)) == FakeDataHelper.CreateJsonHelper().Serialize(new ModConfig()), true);
                byte[] created = File.ReadAllBytes(missingData.FullPath(ConfigStore.FileName));
                store.Reload();
                Assert.Equal("no file: created once, then only read", File.ReadAllBytes(missingData.FullPath(ConfigStore.FileName)).SequenceEqual(created), true);
                Assert.Equal("no file: no warning", store.EmptyFileWarning, null);
                store.Config.SetSelectedOutfitId("Haley", "Fall", "pack.New");
                Assert.Equal("no file: the first save works", store.Save(store.Config), true);
                Assert.Equal("no file: and doesn't back up the defaults", File.Exists(Path.Combine(missingFolder.Path, ConfigStore.BackupFileName)), false);
            }

            // Reading a file that's there, however it looks, never creates one.
            foreach ((string name, byte[] bytes) in EmptyContents)
            {
                using var folder = new TempFolder();
                var data = new FakeDataHelper(folder.Path);
                File.WriteAllBytes(data.FullPath(ConfigStore.FileName), bytes);
                new ConfigStore(data, folder.Path, new FakeMonitor()).Reload();
                Assert.Equal($"{name}: not replaced by the defaults", File.ReadAllBytes(data.FullPath(ConfigStore.FileName)).SequenceEqual(bytes), true);
            }

            // Saving a file that holds only the known fields writes exactly what 1.1.1's
            // Helper.WriteConfig wrote, so nothing about the file's look changes for old users.
            (FakeDataHelper released, TempFolder releasedFolder) = Open("config-1.1.1.json");
            using (releasedFolder)
            {
                var store = new ConfigStore(released, releasedFolder.Path, new FakeMonitor());
                store.Reload();
                store.Save(store.Config);
                Assert.Equal("a save writes what 1.1.1 wrote, byte for byte",
                    File.ReadAllText(released.FullPath(ConfigStore.FileName)) == FakeDataHelper.CreateJsonHelper().Serialize(store.Config), true);
            }
        }

        private static void UnknownFieldsSurvive()
        {
            Assert.Section("Fields this version doesn't know survive a save");
            (FakeDataHelper data, TempFolder folder) = Open("config-hand-edited.json");
            using (folder)
            {
                JObject before = data.ReadJsonFile<JObject>(ConfigStore.FileName);
                var store = new ConfigStore(data, folder.Path, new FakeMonitor());
                store.Reload();
                store.Config.SetSelectedOutfitId("Haley", "Fall", "pack.New");
                store.Save(store.Config);
                JObject after = data.ReadJsonFile<JObject>(ConfigStore.FileName);

                Assert.Equal("unknown field kept as it was", JToken.DeepEquals(before["SomeFutureSetting"], after["SomeFutureSetting"]), true);
                Assert.Equal("top-level order kept", string.Join(",", after.Properties().Select(p => p.Name)), "SelectedOutfits,MenuHotkey,SomeFutureSetting");
                Assert.Equal("the edit was saved", (string)after["SelectedOutfits"]["Haley"]["Fall"], "pack.New");
                Assert.Equal("the hotkey is kept as written", (string)after["MenuHotkey"], "LeftShift + F9");
            }
        }

        // A2: a failed read used to become an empty config that the next save wrote over the file.
        private static void UnreadableFilesAreNeverReplaced()
        {
            Assert.Section("A file that can't be read is never replaced");
            foreach (string fixture in new[] { "config-malformed.json", "config-future-list.json" })
            {
                (FakeDataHelper data, TempFolder folder) = Open(fixture);
                using (folder)
                {
                    byte[] before = File.ReadAllBytes(data.FullPath(ConfigStore.FileName));
                    var monitor = new FakeMonitor();
                    var store = new ConfigStore(data, folder.Path, monitor);
                    store.Reload();
                    Assert.Equal($"{fixture}: the read is flagged as failed", store.LoadError is not null, true);
                    Assert.Equal($"{fixture}: the log names the file", monitor.Messages.Any(m => m.Level == StardewModdingAPI.LogLevel.Error
                        && m.Message.Contains(data.FullPath(ConfigStore.FileName))), true);
                    Assert.Equal($"{fixture}: nothing applies meanwhile", store.Config.SelectedOutfits.Count, 0);
                    Assert.Equal($"{fixture}: save refused", store.Save(store.Config), false);
                    Assert.Equal($"{fixture}: Reset refused too", store.Save(new ModConfig(), isReset: true), false);
                    Assert.Equal($"{fixture}: file untouched", File.ReadAllBytes(data.FullPath(ConfigStore.FileName)).SequenceEqual(before), true);
                    Assert.Equal($"{fixture}: no backup either", Directory.GetFiles(folder.Path).Length, 1);
                    Assert.Equal($"{fixture}: no backup to point at", store.RestoreHint, "");
                }

                // With a backup beside it, the log and the menu notice say how to go back to it.
                foreach (string backupFixture in new[] { "config-1.0.1.json", "config-1.1.1.json" })
                {
                    (FakeDataHelper withBackup, TempFolder withBackupFolder) = Open(fixture);
                    using (withBackupFolder)
                    {
                        File.Copy(FixturePath(backupFixture), Path.Combine(withBackupFolder.Path, ConfigStore.BackupFileName));
                        var monitor = new FakeMonitor();
                        var store = new ConfigStore(withBackup, withBackupFolder.Path, monitor);
                        store.Reload();
                        Assert.Equal($"{fixture}, backup {backupFixture}: the log names the backup", monitor.Messages.Any(m => m.Level == StardewModdingAPI.LogLevel.Error
                            && m.Message.Contains(ConfigStore.BackupFileName)), true);
                        Assert.Equal($"{fixture}, backup {backupFixture}: so does the notice", store.RestoreHint.Contains(ConfigStore.BackupFileName), true);
                    }
                }
            }

            (FakeDataHelper lockedData, TempFolder lockedFolder) = Open("config-1.1.1.json");
            using (lockedFolder)
            {
                string path = lockedData.FullPath(ConfigStore.FileName);
                byte[] before = File.ReadAllBytes(path);
                var store = new ConfigStore(lockedData, lockedFolder.Path, new FakeMonitor());
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    store.Reload();
                }
                Assert.Equal("a locked file is a failed read", store.LoadError is not null, true);
                Assert.Equal("so saving is refused", store.Save(store.Config), false);
                Assert.Equal("and the file is untouched", File.ReadAllBytes(path).SequenceEqual(before), true);
                store.Reload();
                Assert.Equal("once it unlocks, it reads again", $"{store.LoadError ?? "ok"} {store.Config.SelectedOutfits.Count}", "ok 6");
            }

            (FakeDataHelper brokenLater, TempFolder brokenFolder) = Open("config-1.1.1.json");
            using (brokenFolder)
            {
                var store = new ConfigStore(brokenLater, brokenFolder.Path, new FakeMonitor());
                store.Reload();
                string path = brokenLater.FullPath(ConfigStore.FileName);
                File.Copy(FixturePath("config-malformed.json"), path, overwrite: true);
                byte[] broken = File.ReadAllBytes(path);
                store.Reload();
                Assert.Equal("broken mid-session: the last good config stays in play", store.Config.SelectedOutfits.Count, 6);
                Assert.Equal("but isn't saved over the broken file", store.Save(store.Config), false);
                Assert.Equal("which is left as it is", File.ReadAllBytes(path).SequenceEqual(broken), true);
            }
        }

        private static void Backups()
        {
            Assert.Section("Backups before the first save, and before Reset");
            (FakeDataHelper data, TempFolder folder) = Open("config-1.1.1.json");
            using (folder)
            {
                string path = data.FullPath(ConfigStore.FileName);
                string backup = Path.Combine(folder.Path, ConfigStore.BackupFileName);
                string beforeReset = Path.Combine(folder.Path, ConfigStore.ResetBackupFileName);
                byte[] original = File.ReadAllBytes(path);

                var monitor = new FakeMonitor();
                var store = new ConfigStore(data, folder.Path, monitor);
                store.Reload();
                store.Config.SetSelectedOutfitId("Haley", "Fall", "pack.First");
                store.Save(store.Config);
                Assert.Equal("first save backs up the original", File.ReadAllBytes(backup).SequenceEqual(original), true);

                byte[] afterFirst = File.ReadAllBytes(path);
                store.Config.SetSelectedOutfitId("Haley", "Fall", "pack.Second");
                store.Save(store.Config);
                Assert.Equal("later saves in the session leave it alone", File.ReadAllBytes(backup).SequenceEqual(original), true);

                byte[] beforeTheReset = File.ReadAllBytes(path);
                store.Save(new ModConfig(), isReset: true);
                Assert.Equal("Reset copies the file first", File.ReadAllBytes(beforeReset).SequenceEqual(beforeTheReset), true);
                Assert.Equal("and says where", monitor.Messages.Any(m => m.Message.Contains(beforeReset)), true);
                Assert.Equal("Reset emptied the live file", Load(data, new FakeMonitor()).SelectedOutfits.Count, 0);

                File.Copy(beforeReset, path, overwrite: true);
                Assert.Equal("renaming it back undoes the reset", Load(data, new FakeMonitor()).GetSelectedOutfitId("Haley", "Fall", null), "pack.Second");

                var nextSession = new ConfigStore(data, folder.Path, new FakeMonitor());
                nextSession.Reload();
                nextSession.Save(nextSession.Config);
                Assert.Equal("a new session backs up afresh", File.ReadAllBytes(backup).SequenceEqual(File.ReadAllBytes(beforeReset)), true);
                Assert.Equal("the first-save backup differs from mid-session state", afterFirst.SequenceEqual(original), false);
            }
        }

        private static (FakeDataHelper Data, TempFolder Folder) Open(string fixture)
        {
            var folder = new TempFolder();
            var data = new FakeDataHelper(folder.Path);
            File.Copy(FixturePath(fixture), data.FullPath(ConfigStore.FileName));
            return (data, folder);
        }

        internal static ModConfig Load(FakeDataHelper data, FakeMonitor monitor)
        {
            var store = new ConfigStore(data, data.Folder, monitor);
            store.Reload();
            return store.Config;
        }

        // load -> migrate -> save -> load -> migrate. Checked two ways: nothing in the file is lost,
        // and every pick does in game what 1.1.1 made it do.
        private static void RoundTrip(string fixture, bool expectUnchangedKeys = false)
        {
            using var folder = new TempFolder();
            var data = new FakeDataHelper(folder.Path);
            File.Copy(FixturePath(fixture), data.FullPath(ConfigStore.FileName));
            List<Pick> written = ReadPicks(data);

            var store = new ConfigStore(data, folder.Path, new FakeMonitor());
            store.Reload();
            string hotkey = store.Config.MenuHotkey?.ToString();
            Assert.Equal($"{fixture}: saved", store.Save(store.Config), true);
            List<Pick> saved = ReadPicks(data);
            store.Reload();
            ModConfig reloaded = store.Config;
            Assert.Equal($"{fixture}: hotkey unchanged ({hotkey})", reloaded.MenuHotkey?.ToString(), hotkey);

            List<Pick> missing = written
                .Where(p => !saved.Any(s => s.Npc == p.Npc && s.Value == p.Value
                    && (s.Key == p.Key || s.Key == AppearanceCategory.RenameLegacyBase(p.Key))))
                .ToList();
            foreach (Pick pick in missing)
            {
                Assert.Note($"missing from the file: {pick.Npc}/{pick.Key} = {pick.Value}");
            }
            Assert.Equal($"{fixture}: every value stays in the file", missing.Count, 0);
            Assert.Equal($"{fixture}: nothing added to the file", saved.Count, written.Count);

            List<string> expected = AsReleasedBuildApplied(written).Select(p => p.ToString()).ToList();
            List<string> applied = EffectivePicks(reloaded).Select(p => p.ToString()).ToList();
            foreach (string pick in expected.Except(applied))
            {
                Assert.Note($"1.1.1 applied, now doesn't: {pick}");
            }
            foreach (string pick in applied.Except(expected))
            {
                Assert.Note($"now applied, 1.1.1 didn't: {pick}");
            }
            Assert.Equal($"{fixture}: every pick means what it did in 1.1.1",
                expected.Except(applied).Count() + applied.Except(expected).Count(), 0);
            Assert.Equal($"{fixture}: nothing applied twice", applied.Count, expected.Count);

            if (expectUnchangedKeys)
            {
                Assert.Equal($"{fixture}: no key respelled",
                    string.Join(", ", saved.Select(p => $"{p.Npc}/{p.Key}").Except(written.Select(p => $"{p.Npc}/{p.Key}"))), "");
            }

            store.Reload();
            store.Save(store.Config);
            Assert.Equal($"{fixture}: migrating twice changes nothing",
                string.Join("|", ReadPicks(data).Select(p => $"{p.Npc}/{p.Key}={p.Value}")),
                string.Join("|", saved.Select(p => $"{p.Npc}/{p.Key}={p.Value}")));
        }

        // Captured from the 1.1.1 build reading config-hand-edited.json (its load-time migration),
        // so the test's own model of 1.1.1 below is checked against the real thing once.
        private static void HandEditedMatchesTheReleasedBuild()
        {
            using var folder = new TempFolder();
            var data = new FakeDataHelper(folder.Path);
            File.Copy(FixturePath("config-hand-edited.json"), data.FullPath(ConfigStore.FileName));
            var monitor = new FakeMonitor();
            ModConfig config = Load(data, monitor);

            Assert.Equal("Haley, as 1.1.1 applied it", string.Join(" | ", config.GetEffectivePicks("Haley").Select(p => $"{p.Key}={p.Value}")),
                "Weather_Sunny=pack.HaleySunnySpring | Weather_Sunny::Where_Beach=pack.HaleyBeachSun"
                + " | Spring::Weather_Sunny::Where_Beach=pack.HaleySpringBeachSun | winter::Weather_snowing=pack.HaleyWinterSnow"
                + " | Spring=pack.HaleyAnyWeather | Summer::Weather_Raining=pack.HaleyTwoWeathers | Casual=LocalAsset::Haley::Casual::Casual");
            Assert.Equal("Alex, as 1.1.1 applied it", string.Join(" | ", config.GetEffectivePicks("Alex").Select(p => $"{p.Key}={p.Value}")),
                "Island=pack.AlexIsland | Island::Weather_Sunny=pack.AlexLegacyIslandSun"
                + " | Island::Where_Indoor=pack.AlexLegacyIslandIndoor | Weather_Sunny=pack.AlexNotABase");

            Assert.Equal("Haley's keys stay as written", string.Join(", ", config.SelectedOutfits["Haley"].Keys),
                "Weather_Sunny::Spring, Where_Beach::Weather_Sunny, Spring::Where_Beach::Weather_Sunny, winter::weather_snowing, "
                + "Spring::Weather_Any, Any::Weather_Sunny, Summer::Weather_Sunny::Weather_Raining, Spring::Rainy, Casual");
            Assert.Equal("only a lone Beach base is renamed", string.Join(", ", config.SelectedOutfits["Alex"].Keys),
                "Beach, Island, Island::Weather_Sunny, Island::Where_Indoor, Weather_Sunny::Beach");
            Assert.Equal("each ignored key is reported", string.Join(" / ", monitor.Messages.Select(m => m.Message.Split(' ')[1])),
                "\"Any::Weather_Sunny\" / \"Spring::Rainy\" / \"Beach\"");
        }

        // 1.1.0 and 1.1.1's load-time migration, as it was: each key rewritten into
        // EffectiveKey's form, and on a collision the key already in that form wins, else the
        // first. Kept here as the reference the current code must match.
        private static List<Pick> AsReleasedBuildApplied(List<Pick> written)
        {
            var result = new List<Pick>();
            foreach (IGrouping<string, Pick> npc in written.GroupBy(p => p.Npc))
            {
                var byCategory = npc.ToDictionary(p => p.Key, p => p.Value);
                foreach (string key in byCategory.Keys.Where(k => AppearanceCategory.EffectiveKey(k) != k).ToList())
                {
                    string canonical = AppearanceCategory.EffectiveKey(key);
                    string outfitId = byCategory[key];
                    byCategory.Remove(key);
                    if (!byCategory.ContainsKey(canonical))
                    {
                        byCategory[canonical] = outfitId;
                    }
                }
                result.AddRange(byCategory.Select(p => new Pick(npc.Key, p.Key, p.Value)));
            }
            return result;
        }

        // What a key does once it reaches the game, which is what a player would notice changing.
        internal static string Meaning(string key)
        {
            CharacterAppearanceData entry = AppearanceCategory.BuildForcedEntry("test.id", key, "S", "P");
            return $"precedence {entry.Precedence}, condition {entry.Condition ?? "(none)"}, "
                + $"in/out {entry.Indoors}/{entry.Outdoors}, island {entry.IsIslandAttire}";
        }

        internal sealed record Pick(string Npc, string Key, string Value)
        {
            public override string ToString() => $"{this.Npc}: [{Meaning(this.Key)}] = {this.Value ?? "(null)"}";
        }

        // The file as it is on disk, read without the model so nothing in the reader can hide a loss.
        internal static List<Pick> ReadPicks(FakeDataHelper data)
        {
            var picks = new List<Pick>();
            JObject root = data.ReadJsonFile<JObject>(ConfigStore.FileName);
            if (root?["SelectedOutfits"] is JObject outfits)
            {
                foreach (JProperty npc in outfits.Properties())
                {
                    if (npc.Value is JObject byCategory)
                    {
                        picks.AddRange(byCategory.Properties().Select(p => new Pick(npc.Name, p.Name, p.Value.Type == JTokenType.Null ? null : p.Value.ToString())));
                    }
                }
            }
            return picks;
        }

        internal static List<Pick> EffectivePicks(ModConfig config) =>
            config.SelectedOutfits.Keys
                .SelectMany(npc => config.GetEffectivePicks(npc).Select(p => new Pick(npc, p.Key, p.Value)))
                .ToList();

        internal static string FixturePath(string fileName) =>
            Path.Combine(AppContext.BaseDirectory, "fixtures", fileName);
    }
}
