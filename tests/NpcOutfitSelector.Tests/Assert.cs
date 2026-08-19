using System;
using System.Collections.Generic;

namespace NpcOutfitSelector.Tests
{
    // Minimal harness. Everything it reports is a plain value comparison, so a failure prints what
    // was expected next to what happened rather than just a stack trace.
    internal static class Assert
    {
        private static readonly List<string> Failures = new();

        public static int FailureCount => Failures.Count;

        public static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine($"--- {title} ---");
        }

        public static void Equal(string label, object actual, object expected)
        {
            string actualText = Describe(actual);
            string expectedText = Describe(expected);
            bool ok = actualText == expectedText;

            if (!ok)
            {
                Failures.Add($"{label}: expected {expectedText}, got {actualText}");
            }

            Console.WriteLine(
                $"{(ok ? "ok  " : "FAIL")} {label,-52} = {actualText}"
                + (ok ? "" : $"   (expected {expectedText})"));
        }

        public static void True(string label, bool actual) => Equal(label, actual, true);

        public static void Note(string text) => Console.WriteLine($"     {text}");

        private static string Describe(object value) => value switch
        {
            null => "(null)",
            bool b => b ? "true" : "false",
            _ => value.ToString(),
        };

        public static int Report()
        {
            Console.WriteLine();
            if (Failures.Count == 0)
            {
                Console.WriteLine("ALL PASS");
                return 0;
            }

            Console.WriteLine($"{Failures.Count} FAILURE(S):");
            foreach (string failure in Failures)
            {
                Console.WriteLine($"  - {failure}");
            }
            return 1;
        }
    }
}
