using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace NpcOutfitSelector.Tests
{
    internal static class Program
    {
        // The game's assemblies are referenced but deliberately not copied next to a mod, since a
        // mod runs inside the game. A standalone runner has to find them itself, and the handler
        // must be in place before any game type is touched — hence the split from CategoryKeyTests,
        // whose method body is only JITted once it is called.
        private static int Main()
        {
            string gamePath = Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "GamePath")
                ?.Value;

            if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
            {
                Console.WriteLine($"Could not locate the game folder (got '{gamePath}'). Build with the game installed.");
                return 1;
            }

            // SMAPI's own dependencies live in smapi-internal rather than beside the game.
            string[] probePaths = { gamePath, Path.Combine(gamePath, "smapi-internal") };

            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                string name = new AssemblyName(args.Name).Name + ".dll";
                string file = probePaths
                    .Select(path => Path.Combine(path, name))
                    .FirstOrDefault(File.Exists);

                return file is null ? null : Assembly.LoadFrom(file);
            };

            return CategoryKeyTests.Run();
        }
    }
}
