# Tests

Covers the category-key format, the priority that resolves between keys, and — separately —
that everything version 1.0.1 could have written still means exactly what it did.

Those keys land in players' `config.json` and can never be respelled after release, so this
is the part of the mod worth pinning down before shipping.

## Running them

```
dotnet build tests/NpcOutfitSelector.Tests -c Release
dotnet tests/NpcOutfitSelector.Tests/bin/Release/net6.0/NpcOutfitSelector.Tests.dll
```

Exit code is 0 when everything passes, 1 otherwise.

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
| `CategoryKeyTests.cs` | The tests themselves, one method per area. |
| `Assert.cs` | Small harness — prints expected next to actual and collects failures. |

`CategoryKeyTests` also reads `fixtures/config-1.0.1.json`, a real config in the 1.0.1
shape, and checks every key in it individually rather than trusting a hardcoded list. It is
copied to the output folder on build.
