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
| `CompatTests.cs` | Every config in `fixtures/generated` must hand the game exactly what the released 1.1.1 did, before and after a save. |
| `ConfigGenerator.cs` | Writes `fixtures/generated` (`--generate`, see below). |
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

## Compared with the released 1.1.1 (`fixtures/generated`)

`CompatTests` checks against the real 1.1.1, not a model of it. `tests/Oracle-1.1.1` compiles the
1.1.1 sources (commit 6ba64a2) with a copy of 1.1.1's config load and `Data/Characters` edit, and
records, for each config, every appearance entry it hands the game, in order. The game data both
sides start from is `tests/Shared/CompatScenario.cs`.

| Path | |
|---|---|
| `old/` | Configs as released versions wrote them or a player could hand-edit them: 40 each of the 1.0.1 shape, the 1.1.x menu shape and hand edits (seeded, so the same every time), plus the real fixtures and one known fix. |
| `new/` | The same configs after a Save by the current code. |
| `expected-1.1.1.json` | What 1.1.1 hands the game for each file in `old/`. |
| `expected-1.1.1-on-new.json` | What 1.1.1 hands the game for each file in `new/`: going back to 1.1.1 after a save. |

The current code must match `expected-1.1.1.json` from both `old/` and `new/`, and
`expected-1.1.1-on-new.json` must match it too. The only allowed differences are the fixes listed in
`CompatTests.KnownFixes`.

To regenerate, after a change to the generator or the scenario, from the repo root:

```
dotnet tests/NpcOutfitSelector.Tests/bin/Release/net6.0/NpcOutfitSelector.Tests.dll --generate tests/NpcOutfitSelector.Tests/fixtures/generated
git archive 6ba64a2 | tar -x -C <empty folder>
dotnet build tests/Oracle-1.1.1 -c Release -p:ReleasedSource=<that folder>
dotnet tests/Oracle-1.1.1/bin/Release/net6.0/NpcOutfitSelector.Oracle111.dll tests/NpcOutfitSelector.Tests/fixtures/generated/old tests/NpcOutfitSelector.Tests/fixtures/generated/expected-1.1.1.json
dotnet tests/Oracle-1.1.1/bin/Release/net6.0/NpcOutfitSelector.Oracle111.dll tests/NpcOutfitSelector.Tests/fixtures/generated/new tests/NpcOutfitSelector.Tests/fixtures/generated/expected-1.1.1-on-new.json
```

Never regenerate `new/` to make a failing test pass: it failing means a save now writes something
different, which is exactly what this is here to catch.
