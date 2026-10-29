# Tests

Covers the category-key format, the priority that resolves between keys, and — separately —
that everything a released version could have written to `config.json` still means exactly what
it did.

Those keys and outfit Ids land in players' `config.json` and can never be respelled after
release, so this is the part of the mod worth pinning down before shipping.

## Running them

```
dotnet build tests/NpcOutfitSelector.Tests -c Release -p:EnableModDeploy=false
dotnet tests/NpcOutfitSelector.Tests/bin/Release/net6.0/NpcOutfitSelector.Tests.dll
```

Exit code is 0 when everything passes, 1 otherwise. `EnableModDeploy=false` stops the build of
the mod itself from overwriting the copy installed in the game's Mods folder.

**Not `dotnet run`.** ModBuildConfig repoints the run command at SMAPI so that pressing F5
launches the game, and it imports after the project file, so that can't be overridden from
there — `dotnet run` on this project starts Stardew Valley instead of the tests.

The game has to be installed, same as for building the mod itself. Its assemblies are
referenced but never copied beside a mod (a mod runs inside the game), so the runner resolves
them at startup from the path baked in at build time — see `Program.cs`.

## What's in here

| File | |
|---|---|
| `Program.cs` | Entry point and the assembly resolver, kept separate so it is registered before any game type is touched. |
| `CategoryKeyTests.cs` | Key format, scores, compiled conditions, and the ties that exist today. |
| `ConfigFileTests.cs` | Each fixture through the real load path: SMAPI's JSON reader, migration, save, and back. |
| `LocalAssetIdTests.cs` | The Ids of custom PNGs, exactly as each release produced them. |
| `ForcedEntryTests.cs` | A saved pick turned into the entry injected into `Data/Characters`. |
| `SmapiFakes.cs` | `Helper.Data` and a monitor, backed by SMAPI's own `JsonHelper` and converters. |
| `Assert.cs` | Small harness — prints expected next to actual and collects failures. |

## Fixtures

Copied to the output folder on build. Each new one starts with a `//` comment saying what it
is; SMAPI's reader accepts comments, so they're read exactly like a player's file.

| File | |
|---|---|
| `config-1.0.1.json` | A real config in the 1.0.1 shape (legacy `Beach`, no hotkey). |
| `config-1.1.0.json` | A real config in the 1.1.0 shape, every axis combination for one NPC. |
| `config-1.1.1.json` | Written by the 1.1.1 build's own serializer. |
| `config-hand-edited.json` | Reordered segments, case, `Any`, a repeated axis, both island spellings, an unknown field. |
| `config-null-values.json` | A `null` NPC table and a `null` outfit. |
| `config-null-outfits.json` | `SelectedOutfits` and `MenuHotkey` both `null`. |
| `config-empty-hotkey.json` | `MenuHotkey` as an empty string. |
| `config-future-list.json` | A list where an outfit Id is expected, standing in for a future format. |
| `config-malformed.json` | Cut off mid-file. |
