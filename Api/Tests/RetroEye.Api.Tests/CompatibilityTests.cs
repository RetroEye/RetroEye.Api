using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;

namespace RetroEye.Api.Tests
{
    /// <summary>
    /// The libraries' promise not to break programs built on them (docs\API.md, "Versioning and compatibility"): their
    /// public surface only grows within 1.x (the baselines in ApiBaseline\), their assembly version stays 1.0.0.0, an
    /// older client reads a newer theme.json and a newer client an older one. Set RETROEYE_WRITE_API_BASELINE=1 to write
    /// the baselines afresh after a deliberate addition (never to take something out).
    /// </summary>
    public class CompatibilityTests
    {
        /// <summary>Returns a public surface as lines: each public type and each of its public members, with their signatures.</summary>
        private static IEnumerable<string> Surface(Assembly assembly)
        {
            const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (Type type in assembly.GetExportedTypes())
            {
                string kind = type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct" : "class";
                yield return $"{kind} {type.FullName}";
                foreach (MemberInfo member in type.GetMembers(Public))
                {
                    yield return $"{type.FullName} :: {member.MemberType} {member}";
                }
            }
        }

        /// <summary>RetroEye.Api's public surface only grows within 1.x.</summary>
        [Fact]
        public void TheApiLibraryOnlyGrows() =>
            TestFiles.KeepsEverything(Path.Combine(TestFiles.Project(), "ApiBaseline", "RetroEye.Api.txt"), Surface(typeof(RetroEyeClient).Assembly));

        /// <summary>RetroEye.Theming.Wpf's public surface only grows within 1.x.</summary>
        [Fact]
        public void TheWpfLibraryOnlyGrows() =>
            TestFiles.KeepsEverything(Path.Combine(TestFiles.Project(), "ApiBaseline", "RetroEye.Theming.Wpf.txt"), Surface(typeof(RetroEye.Theming.Wpf.RetroWindow).Assembly));

        /// <summary>The libraries are 1.x, tied to contract major 1, and their assembly version stays 1.0.0.0 for all of 1.x.</summary>
        [Fact]
        public void TheLibrariesAreVersionOne()
        {
            foreach (Assembly assembly in new[] { typeof(RetroEyeClient).Assembly, typeof(RetroEye.Theming.Wpf.RetroWindow).Assembly })
            {
                Assert.Equal(new Version(1, 0, 0, 0), assembly.GetName().Version);
            }
            Assert.Equal(1, ApiNames.ContractMajor);
        }

        /// <summary>
        /// A client built on 1.0 reads a theme.json from a later 1.x: members, events, enumeration values and parts it
        /// doesn't know are passed over, an unknown Windows version reads as Unknown and an unknown look falls back on
        /// its visualStyle's family, and everything it does know reads as before.
        /// </summary>
        [Fact]
        public void AnOlderClientReadsANewerFile()
        {
            ApiJson newer = TestFiles.StateJson("xp-silver");
            newer.Set("contract", ApiJson.NewObject().Set("major", 1).Set("minor", 7).Set("version", "1.7"));
            newer.Set("somethingNew", ApiJson.NewObject().Set("deep", ApiJson.NewArray().Add(ApiJson.From(1))));
            newer["windows"].Set("version", "11").Set("name", "Windows 11").Set("build", 22631);
            newer["look"].Set("id", "lunaZune").Set("visualStyle", "Luna.NormalColor");
            newer["colors"].Set("accent", "#0078D4");
            newer["fonts"].Set("tooltip", ApiJson.NewObject().Set("face", "Segoe UI").Set("sizePt", 9));
            newer["chrome"].Set("frameKind", "mica").Set("snapLayouts", true);
            newer["chrome"]["caption"].Set("textAlign", "center");
            newer["capabilities"].Add(ApiJson.From("notify.actions"));
            var read = new ThemeState(newer);
            Assert.Equal(1, read.ContractMajor);
            Assert.Equal(7, read.ContractMinor);
            Assert.Equal(RetroEyeWindowsVersion.Unknown, read.KnownWindowsVersion);
            Assert.Equal("Windows 11", read.WindowsName);
            Assert.Equal(RetroEyeLookFamily.Luna, read.LookFamily);
            Assert.Equal("#E0DFE3", read.Colors["buttonFace"].ToString());
            Assert.Equal("Trebuchet MS", read.Font("caption").Face);
            Assert.True(read.Has("notify"));
            Assert.True(read.Has("notify.actions"));
            Assert.Equal("mica", read.FrameKind);
        }

        /// <summary>
        /// A newer client reads a bare 1.0 file (one with only the oldest members, a bare contract number, no
        /// capabilities, chrome or parts): what's missing falls back on the defaults, nothing throws.
        /// </summary>
        [Fact]
        public void ANewerClientReadsAnOlderFile()
        {
            ApiJson old = ApiJson.Parse("{\"contract\":1,\"sequence\":3,\"running\":true,\"windows\":{\"version\":\"98\",\"name\":\"Windows 98\"},"
                + "\"colors\":{\"buttonFace\":\"#C0C0C0\"}}");
            var read = new ThemeState(old);
            Assert.Equal(1, read.ContractMajor);
            Assert.Equal(0, read.ContractMinor);
            Assert.Equal(RetroEyeWindowsVersion.Windows98, read.KnownWindowsVersion);
            Assert.Equal("#C0C0C0", read.Color("buttonFace", default).ToString());
            Assert.Equal("#FFFFFF", read.Color("window", new ApiColor(255, 255, 255)).ToString());
            Assert.Empty(read.Capabilities);
            Assert.Empty(read.Parts);
            Assert.Equal("raised3d", read.FrameKind);
            Assert.Equal(19, read.Metric("captionHeight", 19));
        }

        /// <summary>RetroWindow is drawn at RetroEye's published scale over the screen's: 2 on the 125% screen when RetroEye publishes 2.5.</summary>
        [Fact]
        public void LayoutScaleIsRetroEyesScaleOverTheScreens()
        {
            var state = new ThemeState(ApiJson.Parse("{\"scale\":{\"factor\":2.5,\"dpi\":240,\"windowsDpi\":120},\"parts\":{\"items\":{\"frameActive\":{\"file\":\"f.png\",\"width\":1,\"height\":1,\"slice\":{}}}}}"), "parts");
            Assert.Equal(2.0, RetroEye.Theming.Wpf.RetroEyeTheme.LayoutScale(state, 1.25), 3);
            Assert.Equal(2.5, RetroEye.Theming.Wpf.RetroEyeTheme.LayoutScale(state, 1.0), 3);
            Assert.Equal(1.0, RetroEye.Theming.Wpf.RetroEyeTheme.LayoutScale(ThemeState.Default, 1.25), 3);
        }

        /// <summary>The pixel-text rule: whole scales draw sharp pixel fonts, others don't.</summary>
        [Fact]
        public void PixelScaleKnowsWholeScales()
        {
            Assert.True(PixelScale.IsWhole(1));
            Assert.True(PixelScale.IsWhole(2));
            Assert.False(PixelScale.IsWhole(2.5));
            Assert.False(PixelScale.IsWhole(1.25));
        }
    }
}
