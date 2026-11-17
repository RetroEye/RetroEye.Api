using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace RetroEye.Api.Tests
{
    /// <summary>
    /// Contract 1 (docs\API.md) as the library sees it: every recorded theme.json (States\, as RetroEye writes each
    /// version) matches the schema in docs\api and reads as that version looked; the golden lists of everything contract
    /// 1.0 and 1.1 promise are all still there (in the states, the library's names and the spec); and 1.1 only added to
    /// 1.0.
    /// </summary>
    public class ContractTests
    {
        /// <summary>Every recorded state matches the schema.</summary>
        [Fact]
        public void EveryRecordedStateMatchesTheSchema()
        {
            ApiJson schema = TestFiles.Schema();
            foreach (string name in TestFiles.StateNames())
            {
                var problems = SchemaCheck.Problems(schema, TestFiles.StateJson(name));
                Assert.True(problems.Count == 0, name + ": " + string.Join("; ", problems));
            }
        }

        /// <summary>The library's built-in Windows 98 defaults match the schema too, and say RetroEye isn't running.</summary>
        [Fact]
        public void TheLibrarysDefaultsMatchTheSchema()
        {
            Assert.Empty(SchemaCheck.Problems(TestFiles.Schema(), ThemeState.Default.Raw));
            Assert.False(ThemeState.Default.Running);
            Assert.Equal("98", ThemeState.Default.WindowsVersion);
        }

        /// <summary>
        /// The versions read as they looked: 95's caption one solid navy, 98's navy blending to blue, XP's Luna a top to
        /// bottom gradient with round corners and Trebuchet MS, 7's Aero glass in Segoe UI.
        /// </summary>
        [Fact]
        public void EachVersionReadsAsItLooked()
        {
            ThemeState w95 = TestFiles.State("95"), w98 = TestFiles.State("98"), xp = TestFiles.State("xp-blue"), seven = TestFiles.State("7");

            Assert.Equal("95", w95.WindowsVersion);
            Assert.Equal(RetroEyeWindowsVersion.Windows95, w95.KnownWindowsVersion);
            Assert.Equal("solid", w95.CaptionActive.Fill.Kind);
            Assert.Equal("#000080", w95.CaptionActive.Fill.Stops[0].Color.ToString());
            Assert.Equal("raised3d", w95.FrameKind);
            Assert.Equal("MS Sans Serif", w95.Font("caption").Face);
            Assert.True(w95.Font("caption").Bitmap);
            Assert.Equal("Microsoft Sans Serif", w95.Font("caption").DrawnFace);

            Assert.Equal("98", w98.WindowsVersion);
            Assert.Equal("horizontal", w98.CaptionActive.Fill.Kind);
            Assert.Equal("#1084D0", w98.CaptionActive.Fill.Stops[^1].Color.ToString());
            Assert.Equal("#C0C0C0", w98.Colors["buttonFace"].ToString());
            Assert.Equal(19, w98.Metric("captionHeight", 0));
            Assert.Equal(new[] { "minimize", "maximize", "close" }, w98.CaptionButtonOrder);
            Assert.Equal("square", w98.CornerShape);
            Assert.True(w98.IsClassic);

            Assert.Equal("xp", xp.WindowsVersion);
            Assert.Equal("lunaBlue", xp.LookId);
            Assert.Equal(RetroEyeLookFamily.Luna, xp.LookFamily);
            Assert.Equal("Luna.NormalColor", xp.VisualStyle);
            Assert.Equal("vertical", xp.CaptionActive.Fill.Kind);
            Assert.True(xp.CaptionActive.Fill.Stops.Count > 2);
            Assert.NotNull(xp.CaptionActive.TextShadow);
            Assert.Equal("rounded", xp.CornerShape);
            Assert.Equal(7, xp.CornerRadius);
            Assert.Equal("Trebuchet MS", xp.Font("caption").Face);
            Assert.Equal("#ECE9D8", xp.Colors["buttonFace"].ToString());

            Assert.Equal("7", seven.WindowsVersion);
            Assert.Equal("aero", seven.FrameKind);
            Assert.True(seven.GlassEnabled);
            Assert.True(seven.CaptionActive.TextGlow);
            Assert.Equal("Segoe UI", seven.Font("caption").Face);

            Assert.Equal("raised3d", TestFiles.State("xp-classic").FrameKind);
            Assert.Equal("basic", TestFiles.State("vista-basic").FrameKind);
            Assert.False(TestFiles.State("vista-basic").GlassEnabled);
        }

        /// <summary>The 31 colour slots are in GetSysColor's order, and every recorded state has every one.</summary>
        [Fact]
        public void EveryColourSlotIsThere()
        {
            Assert.Equal(31, ApiColorNames.All.Count);
            Assert.Equal(15, ApiColorNames.IndexOf("buttonFace"));
            Assert.Equal(30, ApiColorNames.IndexOf("menuBar"));
            foreach (string name in TestFiles.StateNames())
            {
                ThemeState state = TestFiles.State(name);
                foreach (string color in ApiColorNames.All)
                {
                    Assert.True(state.Colors.ContainsKey(color), $"{name}: {color}");
                }
            }
        }

        /// <summary>The recorded state with picture parts lists every part with its size and 9-slice insets.</summary>
        [Fact]
        public void PartsAreListedWithTheirSizes()
        {
            var state = new ThemeState(TestFiles.StateJson("98-parts"), "parts");
            Assert.True(state.Parts.Count > 20);
            foreach (string name in new[] { "frameActive", "frameInactive", "captionActive", "button.close.normal", "button.help.normal", "pushButton.normal", "textBox.normal" })
            {
                Assert.True(state.Parts.ContainsKey(name), name);
                Assert.True(state.Parts[name].Width > 0 && state.Parts[name].Height > 0, name);
            }
        }

        // ---- The golden lists ----

        /// <summary>Adds a line for every member path and the kind of value at it ("field chrome.caption.active.text string").</summary>
        private static void Fields(ApiJson value, string path, ISet<string> lines)
        {
            string kind = value.Kind switch
            {
                ApiJsonKind.Object => "object", ApiJsonKind.Array => "array", ApiJsonKind.String => "string",
                ApiJsonKind.Number => "number", ApiJsonKind.Boolean => "boolean", _ => "null",
            };
            lines.Add($"field {path} {kind}");
            if (value.Kind == ApiJsonKind.Object)
            {
                foreach (var member in value.Members)
                {
                    string at = path == "parts.items" ? "parts.items.*" : path.Length == 0 ? member.Key : path + "." + member.Key;
                    Fields(member.Value, at, lines);
                }
            }
            else if (value.Kind == ApiJsonKind.Array)
            {
                foreach (ApiJson item in value.Items)
                {
                    Fields(item, path + "[]", lines);
                }
            }
        }

        /// <summary>
        /// What the library side can show of contract 1 now: every field of every recorded state (parts' fields from the
        /// one with parts, which RetroEye writes into every state alike), every part, colour slot, capability and name.
        /// </summary>
        private static SortedSet<string> ContractNow()
        {
            var lines = new SortedSet<string>(StringComparer.Ordinal);
            ApiJson parts = TestFiles.StateJson("98-parts")["parts"];
            foreach (string name in TestFiles.StateNames())
            {
                ApiJson state = TestFiles.StateJson(name);
                if (state["parts"].IsNull)
                {
                    state.Set("parts", parts);
                }
                Fields(state, "", lines);
                foreach (var part in state["parts"]["items"].Members)
                {
                    lines.Add("part " + part.Key);
                }
            }
            foreach (string name in ApiColorNames.All) lines.Add("color " + name);
            foreach (string name in ApiNames.Capabilities) lines.Add("capability " + name);
            lines.Add($"name folder api\\v{ApiNames.ContractMajor}");
            lines.Add("name event " + ApiNames.ChangedEventName("", "SID"));
            lines.Add("name pipe " + ApiNames.PipeName("", 0, "SID"));
            return lines;
        }

        /// <summary>The golden lines about the protocol (event, request, reply, error), which the spec must still name.</summary>
        private static readonly string[] ProtocolKinds = { "event", "request", "reply", "error" };

        /// <summary>
        /// Nothing contract 1.0 or 1.1 promised is gone: every field, part, colour, capability and name of each golden
        /// list is in the recorded states and the library, and every event, request, reply and error code is still in the
        /// spec (docs\API.md). The lists are never rewritten here: RetroEye's own tests write the latest from its code.
        /// </summary>
        [Theory]
        [InlineData("contract-1.0.golden.txt")]
        [InlineData("contract-1.1.golden.txt")]
        public void TheContractKeepsEverythingItHad(string golden)
        {
            SortedSet<string> now = ContractNow();
            string spec = File.ReadAllText(Path.Combine(TestFiles.Repo(), "docs", "API.md"));
            var gone = new List<string>();
            foreach (string line in TestFiles.Lines(TestFiles.Spec(golden)))
            {
                string kind = line.Substring(0, line.IndexOf(' '));
                string name = line.Substring(kind.Length + 1);
                bool there = ProtocolKinds.Contains(kind) ? spec.Contains("`" + name + "`") || spec.Contains("\"" + name + "\"") : now.Contains(line);
                if (!there)
                {
                    gone.Add(line);
                }
            }
            Assert.True(gone.Count == 0, $"{golden}: removed, renamed or retyped (breaks contract 1): " + string.Join("; ", gone));
        }

        /// <summary>Contract minors only add: everything 1.0 had is in 1.1's list, and the library speaks 1.1.</summary>
        [Fact]
        public void OneOneOnlyAddsToOneZero()
        {
            var oneOne = new HashSet<string>(TestFiles.Lines(TestFiles.Spec("contract-1.1.golden.txt")));
            Assert.All(TestFiles.Lines(TestFiles.Spec("contract-1.0.golden.txt")), l => Assert.Contains(l, oneOne));
            Assert.Contains("event restarting", oneOne);
            Assert.Contains("capability events.restarting", oneOne);
            Assert.Equal(1, ApiNames.ContractMajor);
            Assert.Equal(1, ApiNames.ContractMinor);
            Assert.Equal("1.1", ApiNames.ContractVersion);
            Assert.Contains("events.restarting", ApiNames.Capabilities);
            Assert.Equal(new[] { 1 }, ApiNames.SupportedMajors);
        }

        /// <summary>The names carry the session and user, and a channel keeps a test copy apart from the real RetroEye.</summary>
        [Fact]
        public void NamesCarryUserSessionAndChannel()
        {
            string sid = ApiNames.UserSid();
            Assert.StartsWith("S-1-", sid);
            Assert.Equal($"RetroEye.Api.v1.{ApiNames.SessionId()}.{sid}", ApiNames.PipeName("", ApiNames.SessionId(), sid));
            Assert.Equal($@"Local\RetroEye.Api.v1.copy1.Changed.{sid}", ApiNames.ChangedEventName("copy1", sid));
            Assert.Equal("abc-_1", ApiNames.CleanChannel("a b/c\\-_1"));
            Assert.Equal("theme.json", ApiNames.ThemeFile);
        }
    }
}
