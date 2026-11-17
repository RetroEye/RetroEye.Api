using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace RetroEye.Api.Tests
{
    /// <summary>Where the tests find the spec files, the recorded states and the baselines, and scratch folders.</summary>
    internal static class TestFiles
    {
        /// <summary>The repository's folder (the one holding docs\api), walking up from where the tests run.</summary>
        public static string Repo()
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder != null && !Directory.Exists(Path.Combine(folder.FullName, "docs", "api")))
            {
                folder = folder.Parent;
            }
            Assert.NotNull(folder);
            return folder.FullName;
        }

        /// <summary>This project's own folder, with States\ and ApiBaseline\.</summary>
        public static string Project() => Path.Combine(Repo(), "Api", "Tests", "RetroEye.Api.Tests");

        /// <summary>A file in docs\api.</summary>
        public static string Spec(string name) => Path.Combine(Repo(), "docs", "api", name);

        /// <summary>The schema of theme.json.</summary>
        public static ApiJson Schema() => ApiJson.Parse(File.ReadAllText(Spec("theme.v1.schema.json")));

        /// <summary>The names of the recorded states (States\*.json, as RetroEye writes them), without ".json".</summary>
        public static IReadOnlyList<string> StateNames() =>
            Directory.GetFiles(Path.Combine(Project(), "States"), "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.Ordinal).ToList();

        /// <summary>A recorded state's text.</summary>
        public static string StateText(string name) => File.ReadAllText(Path.Combine(Project(), "States", name + ".json"));

        /// <summary>A recorded state, as JSON.</summary>
        public static ApiJson StateJson(string name) => ApiJson.Parse(StateText(name));

        /// <summary>A recorded state, read through the library.</summary>
        public static ThemeState State(string name) => new ThemeState(StateJson(name));

        /// <summary>Returns a fresh scratch folder.</summary>
        public static string ScratchFolder()
        {
            string folder = Path.Combine(Path.GetTempPath(), "RetroEye.Api.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        /// <summary>Returns a channel of the test's own, so a real RetroEye running beside the tests is never in the way.</summary>
        public static string Channel() => "test" + Guid.NewGuid().ToString("N").Substring(0, 12);

        /// <summary>Whether the baselines are to be written afresh rather than checked (RETROEYE_WRITE_API_BASELINE=1).</summary>
        public static bool Writing => Environment.GetEnvironmentVariable("RETROEYE_WRITE_API_BASELINE") == "1";

        /// <summary>The lines of a list file, without blank lines and # comments.</summary>
        public static IEnumerable<string> Lines(string file) => File.ReadAllLines(file).Where(l => l.Length > 0 && !l.StartsWith("#"));

        /// <summary>
        /// Checks that every line of a checked-in list is still in what is there now (additions are fine). With
        /// RETROEYE_WRITE_API_BASELINE=1 the list is written afresh instead, unless it is frozen.
        /// </summary>
        public static void KeepsEverything(string file, IEnumerable<string> now, bool frozen = false)
        {
            var current = new SortedSet<string>(now, StringComparer.Ordinal);
            if (Writing && !frozen)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllLines(file, current);
                return;
            }
            Assert.True(File.Exists(file), $"No baseline at {file}");
            var gone = Lines(file).Where(l => !current.Contains(l)).ToList();
            Assert.True(gone.Count == 0, "Removed, renamed or retyped (breaks contract 1 / library 1.x): " + string.Join("; ", gone));
        }
    }
}
