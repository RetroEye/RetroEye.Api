// RetroEye app API, contract v1: the typed view of theme.json. Every class keeps its raw JSON (Raw), so a member a
// later contract adds can be read before this library knows it.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RetroEye.Api
{
    /// <summary>The Windows versions contract 1.0 names; a later minor may add more, read as Unknown here.</summary>
    public enum RetroEyeWindowsVersion
    {
        /// <summary>A version this library doesn't know (added by a later RetroEye).</summary>
        Unknown,
        /// <summary>Windows 95.</summary>
        Windows95,
        /// <summary>Windows 98.</summary>
        Windows98,
        /// <summary>Windows Me.</summary>
        WindowsMe,
        /// <summary>Windows 2000.</summary>
        Windows2000,
        /// <summary>Windows XP.</summary>
        WindowsXP,
        /// <summary>Windows Vista.</summary>
        WindowsVista,
        /// <summary>Windows 7.</summary>
        Windows7,
        /// <summary>RetroEye's System themes, which follow the Windows it runs on.</summary>
        System,
    }

    /// <summary>The families of looks: what to draw for a look id this library doesn't know.</summary>
    public enum RetroEyeLookFamily
    {
        /// <summary>A family this library doesn't know.</summary>
        Unknown,
        /// <summary>Classic: raised 3D frames and buttons (95 to 2000, and the later versions' Classic).</summary>
        Classic,
        /// <summary>XP's Luna and Royale styles.</summary>
        Luna,
        /// <summary>Vista's and 7's Aero.</summary>
        Aero,
        /// <summary>Vista Basic: Aero's shapes without glass.</summary>
        Basic,
    }

    /// <summary>A colour as RetroEye publishes it: "#RRGGBB".</summary>
    public readonly struct ApiColor : IEquatable<ApiColor>
    {
        /// <summary>The red part, 0 to 255.</summary>
        public byte R { get; }

        /// <summary>The green part, 0 to 255.</summary>
        public byte G { get; }

        /// <summary>The blue part, 0 to 255.</summary>
        public byte B { get; }

        /// <summary>Makes a colour from its three parts.</summary>
        public ApiColor(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        /// <summary>Reads "#RRGGBB" (or "RRGGBB"); gives the fallback for anything else.</summary>
        public static ApiColor Parse(string? text, ApiColor fallback)
        {
            if (text == null)
            {
                return fallback;
            }
            string hex = text.TrimStart('#');
            if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            {
                return fallback;
            }
            return new ApiColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        /// <summary>Returns the colour as Windows' COLORREF (red in the low byte), as GetSysColor gives it.</summary>
        public int ToColorRef() => R | (G << 8) | (B << 16);

        /// <summary>Returns "#RRGGBB".</summary>
        public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";

        /// <inheritdoc />
        public bool Equals(ApiColor other) => R == other.R && G == other.G && B == other.B;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is ApiColor other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => (R << 16) | (G << 8) | B;
    }

    /// <summary>
    /// The 31 classic colour slots, by the names contract v1 gives Windows' COLOR_* values (COLOR_SCROLLBAR is
    /// "scrollBar", COLOR_BTNFACE "buttonFace" and so on), each with the index GetSysColor takes.
    /// </summary>
    public static class ApiColorNames
    {
        /// <summary>Every slot's name in GetSysColor's order: the index in this list is the COLOR_* number.</summary>
        public static readonly IReadOnlyList<string> All = new[]
        {
            "scrollBar", "desktop", "activeCaption", "inactiveCaption", "menu", "window", "windowFrame", "menuText",
            "windowText", "captionText", "activeBorder", "inactiveBorder", "appWorkspace", "highlight", "highlightText",
            "buttonFace", "buttonShadow", "grayText", "buttonText", "inactiveCaptionText", "buttonHighlight",
            "buttonDarkShadow", "buttonLight", "infoText", "infoBackground", "buttonAlternateFace", "hotLight",
            "gradientActiveCaption", "gradientInactiveCaption", "menuHighlight", "menuBar",
        };

        /// <summary>Returns the COLOR_* number of a slot's name, or -1 for a name that isn't one.</summary>
        public static int IndexOf(string name)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i] == name)
                {
                    return i;
                }
            }
            return -1;
        }
    }

    /// <summary>One of the six fonts: caption, smallCaption, menu, status, message and icon.</summary>
    public sealed class ApiFont
    {
        /// <summary>Reads a font from its JSON.</summary>
        public ApiFont(ApiJson raw)
        {
            Raw = raw;
            Face = raw["face"].AsString("Tahoma");
            DrawnFace = raw["drawnFace"].AsString(Face);
            SizePoints = raw["sizePt"].AsNumber(8);
            Weight = raw["weight"].AsInt(400);
            Italic = raw["italic"].AsBool();
            Bitmap = raw["bitmap"].AsBool();
        }

        /// <summary>The font as published.</summary>
        public ApiJson Raw { get; }

        /// <summary>The face the version named, such as "MS Sans Serif".</summary>
        public string Face { get; }

        /// <summary>
        /// The face to draw with today: Face itself, or its outline twin where Face was a bitmap font modern
        /// toolkits can't draw ("Microsoft Sans Serif" for "MS Sans Serif").
        /// </summary>
        public string DrawnFace { get; }

        /// <summary>The size in points.</summary>
        public double SizePoints { get; }

        /// <summary>The size in pixels at 96 DPI (points × 96 / 72).</summary>
        public double SizePixels => SizePoints * 96.0 / 72.0;

        /// <summary>The weight: 400 regular, 700 bold.</summary>
        public int Weight { get; }

        /// <summary>Whether the font is bold (Weight 600 or more).</summary>
        public bool Bold => Weight >= 600;

        /// <summary>Whether it is italic.</summary>
        public bool Italic { get; }

        /// <summary>Whether the version drew it from a bitmap (pixel) font, with no smoothing: draw it aliased for the true look.</summary>
        public bool Bitmap { get; }
    }

    /// <summary>A colour along a fill, at a place from 0 (start) to 1 (end).</summary>
    public sealed class ApiGradientStop
    {
        /// <summary>Makes a stop.</summary>
        public ApiGradientStop(double offset, ApiColor color)
        {
            Offset = offset;
            Color = color;
        }

        /// <summary>Where the colour is, 0 to 1.</summary>
        public double Offset { get; }

        /// <summary>The colour there.</summary>
        public ApiColor Color { get; }
    }

    /// <summary>How a caption is filled: one colour, or a gradient left to right or top to bottom.</summary>
    public sealed class ApiFill
    {
        /// <summary>Reads a fill from its JSON.</summary>
        public ApiFill(ApiJson raw)
        {
            Raw = raw;
            Kind = raw["kind"].AsString("solid");
            Stops = raw["stops"].Items.Select(s => new ApiGradientStop(s["offset"].AsNumber(), ApiColor.Parse(s["color"].AsStringOrNull(), default))).ToList();
        }

        /// <summary>The fill as published.</summary>
        public ApiJson Raw { get; }

        /// <summary>"solid", "horizontal" (left to right) or "vertical" (top to bottom).</summary>
        public string Kind { get; }

        /// <summary>The colours along it, at least one; a solid fill has one.</summary>
        public IReadOnlyList<ApiGradientStop> Stops { get; }
    }

    /// <summary>A caption's look in one state, active or inactive.</summary>
    public sealed class ApiCaptionState
    {
        /// <summary>Reads a caption state from its JSON.</summary>
        public ApiCaptionState(ApiJson raw)
        {
            Raw = raw;
            Fill = new ApiFill(raw["fill"]);
            Text = ApiColor.Parse(raw["text"].AsStringOrNull(), new ApiColor(255, 255, 255));
            TextShadow = raw["textShadow"].AsStringOrNull() is string shadow ? ApiColor.Parse(shadow, default) : (ApiColor?)null;
            TextGlow = raw["textGlow"].AsBool();
        }

        /// <summary>The state as published.</summary>
        public ApiJson Raw { get; }

        /// <summary>The caption's fill.</summary>
        public ApiFill Fill { get; }

        /// <summary>The title's colour.</summary>
        public ApiColor Text { get; }

        /// <summary>The colour of the title's one-pixel shadow down and right (XP's), or null for none.</summary>
        public ApiColor? TextShadow { get; }

        /// <summary>Whether the title stands in a soft white glow (Vista's and 7's glass).</summary>
        public bool TextGlow { get; }
    }

    /// <summary>One picture part RetroEye drew: its file, size and 9-slice insets.</summary>
    public sealed class ApiPart
    {
        /// <summary>Reads a part from its JSON.</summary>
        public ApiPart(string name, ApiJson raw, string folder)
        {
            Name = name;
            Raw = raw;
            File = raw["file"].AsString();
            FullPath = System.IO.Path.Combine(folder, File);
            Width = raw["width"].AsInt();
            Height = raw["height"].AsInt();
            SliceLeft = raw["slice"]["left"].AsInt();
            SliceTop = raw["slice"]["top"].AsInt();
            SliceRight = raw["slice"]["right"].AsInt();
            SliceBottom = raw["slice"]["bottom"].AsInt();
        }

        /// <summary>The part's name, such as "captionActive" or "button.close.hot".</summary>
        public string Name { get; }

        /// <summary>The part as published.</summary>
        public ApiJson Raw { get; }

        /// <summary>The PNG's file name in the parts folder.</summary>
        public string File { get; }

        /// <summary>The PNG's full path.</summary>
        public string FullPath { get; }

        /// <summary>The picture's width, in screen pixels.</summary>
        public int Width { get; }

        /// <summary>The picture's height, in screen pixels.</summary>
        public int Height { get; }

        /// <summary>How many pixels at the left stay as they are when the picture is stretched (the 9-slice inset).</summary>
        public int SliceLeft { get; }

        /// <summary>How many pixels at the top stay as they are.</summary>
        public int SliceTop { get; }

        /// <summary>How many pixels at the right stay as they are.</summary>
        public int SliceRight { get; }

        /// <summary>How many pixels at the bottom stay as they are.</summary>
        public int SliceBottom { get; }
    }

    /// <summary>
    /// Everything RetroEye publishes about the Windows version it shows, as read from theme.json (or a state sent
    /// on the pipe). Each part is typed; <see cref="Raw"/> keeps the whole JSON for anything a later contract adds.
    /// </summary>
    public sealed class ThemeState
    {
        /// <summary>Reads a state from its JSON, with the parts folder its picture files are in.</summary>
        public ThemeState(ApiJson raw, string? partsFolder = null)
        {
            Raw = raw;
            // "contract": {"major":1,"minor":0} (a bare number is read as the major, minor 0)
            ApiJson contract = raw["contract"];
            ContractMajor = contract.Kind == ApiJsonKind.Number ? contract.AsInt() : contract["major"].AsInt(ApiNames.ContractMajor);
            ContractMinor = contract.Kind == ApiJsonKind.Number ? 0 : contract["minor"].AsInt(0);
            Capabilities = raw["capabilities"].Items.Select(c => c.AsString()).Where(c => c.Length > 0).ToList();
            Deprecated = raw["deprecated"].Items.Select(d => d["name"].AsString()).Where(d => d.Length > 0).ToList();
            Sequence = (long)raw["sequence"].AsNumber();
            Instance = raw["instance"].AsString();
            Running = raw["running"].AsBool();
            Restarting = raw["restarting"].AsBool();
            Written = DateTime.TryParse(raw["written"].AsString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime written) ? written : DateTime.MinValue;
            RetroEyeVersion = raw["retroEye"]["version"].AsString();
            WindowsVersion = raw["windows"]["version"].AsString("98");
            WindowsName = raw["windows"]["name"].AsString("Windows 98");
            LookId = raw["look"]["id"].AsString("classic");
            LookName = raw["look"]["name"].AsString("Classic");
            IsClassic = raw["look"]["classic"].AsBool(true);
            SchemeName = raw["look"]["scheme"].AsString();
            ThemeName = raw["look"]["theme"].AsString();
            VisualStyle = raw["look"]["visualStyle"].AsString("Classic");
            var colors = new Dictionary<string, ApiColor>();
            foreach (var member in raw["colors"].Members)
            {
                colors[member.Key] = ApiColor.Parse(member.Value.AsStringOrNull(), default);
            }
            Colors = colors;
            var fonts = new Dictionary<string, ApiFont>();
            foreach (var member in raw["fonts"].Members)
            {
                fonts[member.Key] = new ApiFont(member.Value);
            }
            Fonts = fonts;
            var metrics = new Dictionary<string, double>();
            foreach (var member in raw["metrics"].Members)
            {
                metrics[member.Key] = member.Value.AsNumber();
            }
            Metrics = metrics;
            Scale = raw["scale"]["factor"].AsNumber(1);
            Dpi = raw["scale"]["dpi"].AsInt(96);
            WindowsDpi = raw["scale"]["windowsDpi"].AsInt(96);
            ApiJson chrome = raw["chrome"];
            FrameKind = chrome["frameKind"].AsString("raised3d");
            CornerShape = chrome["corners"]["shape"].AsString("square");
            CornerRadius = chrome["corners"]["radius"].AsNumber();
            CaptionActive = new ApiCaptionState(chrome["caption"]["active"]);
            CaptionInactive = new ApiCaptionState(chrome["caption"]["inactive"]);
            CaptionTextAlign = chrome["caption"]["textAlign"].AsString("left");
            CaptionButtonOrder = chrome["buttons"]["order"].Items.Select(i => i.AsString()).ToList();
            CaptionButtonStyle = chrome["buttons"]["style"].AsString("classic3d");
            GlassEnabled = chrome["glass"]["enabled"].AsBool();
            GlassColor = chrome["glass"]["color"].AsStringOrNull() is string glass ? ApiColor.Parse(glass, default) : (ApiColor?)null;
            GlassOpacity = chrome["glass"]["opacity"].AsNumber();
            TaskbarEdge = raw["taskbar"]["edge"].AsString("bottom");
            TaskbarHeight = raw["taskbar"]["height"].AsNumber(28);
            PartsFolder = partsFolder;
            PartsGeneration = raw["parts"]["generation"].AsInt();
            var parts = new Dictionary<string, ApiPart>();
            if (partsFolder != null)
            {
                foreach (var member in raw["parts"]["items"].Members)
                {
                    parts[member.Key] = new ApiPart(member.Key, member.Value, partsFolder);
                }
            }
            Parts = parts;
        }

        /// <summary>The whole state as published.</summary>
        public ApiJson Raw { get; }

        /// <summary>The contract major RetroEye wrote: 1 in this folder (a v2 would be written to api\v2\ beside it).</summary>
        public int ContractMajor { get; }

        /// <summary>The contract minor RetroEye wrote: 0 or later; a later minor only adds what this library ignores.</summary>
        public int ContractMinor { get; }

        /// <summary>The features RetroEye names (see ApiNames.Capabilities); check one with <see cref="Has"/>.</summary>
        public IReadOnlyList<string> Capabilities { get; }

        /// <summary>The names of members, events, messages or parts RetroEye says are deprecated (empty in 1.0).</summary>
        public IReadOnlyList<string> Deprecated { get; }

        /// <summary>Whether RetroEye names a capability, such as "notify" or "parts.buttons".</summary>
        public bool Has(string capability) => Capabilities.Contains(capability);

        /// <summary>
        /// The Windows version as a value this library knows, or <see cref="RetroEyeWindowsVersion.Unknown"/> for one a
        /// later RetroEye adds (use <see cref="WindowsVersion"/>, <see cref="VisualStyle"/> and the colours then).
        /// </summary>
        public RetroEyeWindowsVersion KnownWindowsVersion => WindowsVersion switch
        {
            "95" => RetroEyeWindowsVersion.Windows95,
            "98" => RetroEyeWindowsVersion.Windows98,
            "me" => RetroEyeWindowsVersion.WindowsMe,
            "2000" => RetroEyeWindowsVersion.Windows2000,
            "xp" => RetroEyeWindowsVersion.WindowsXP,
            "vista" => RetroEyeWindowsVersion.WindowsVista,
            "7" => RetroEyeWindowsVersion.Windows7,
            "system" => RetroEyeWindowsVersion.System,
            _ => RetroEyeWindowsVersion.Unknown,
        };

        /// <summary>
        /// The look's family as a value this library knows, falling back on the WPF theme it matches for a look id a
        /// later RetroEye adds (an unknown look whose visualStyle is Luna.* is <see cref="RetroEyeLookFamily.Luna"/>).
        /// </summary>
        public RetroEyeLookFamily LookFamily =>
            LookId == "classic" || VisualStyle == "Classic" ? RetroEyeLookFamily.Classic
            : VisualStyle.StartsWith("Luna", StringComparison.Ordinal) || VisualStyle.StartsWith("Royale", StringComparison.Ordinal) ? RetroEyeLookFamily.Luna
            : LookId == "vistaBasic" ? RetroEyeLookFamily.Basic
            : VisualStyle.StartsWith("Aero", StringComparison.Ordinal) ? RetroEyeLookFamily.Aero
            : RetroEyeLookFamily.Unknown;

        /// <summary>The write's number, one more at each change since RetroEye started.</summary>
        public long Sequence { get; }

        /// <summary>A name new each time RetroEye starts, so a client can tell a restart from a change.</summary>
        public string Instance { get; }

        /// <summary>Whether RetroEye was running when it wrote this (false once it has shut down).</summary>
        public bool Running { get; }

        /// <summary>
        /// Contract 1.1: whether RetroEye wrote this as it closed to start again at once (its Restart), so a reader
        /// waits for it to come back rather than taking <see cref="Running"/> false as gone. False in a 1.0 file.
        /// </summary>
        public bool Restarting { get; }

        /// <summary>When it was written, in UTC.</summary>
        public DateTime Written { get; }

        /// <summary>RetroEye's own version, such as "1.0.0.8".</summary>
        public string RetroEyeVersion { get; }

        /// <summary>The Windows version shown: "95", "98", "me", "2000", "xp", "vista", "7" (or "system", "unknown").</summary>
        public string WindowsVersion { get; }

        /// <summary>The version's name, such as "Windows 98".</summary>
        public string WindowsName { get; }

        /// <summary>
        /// The look: "classic", "lunaBlue", "lunaOliveGreen", "lunaSilver", "royale", "aero", "vistaBasic" or "system".
        /// </summary>
        public string LookId { get; }

        /// <summary>The look's name, such as "Luna Blue" or "Classic".</summary>
        public string LookName { get; }

        /// <summary>Whether the look is Classic (95 to 2000, and the later versions' Classic).</summary>
        public bool IsClassic { get; }

        /// <summary>The Appearance scheme's name, such as "Windows Standard".</summary>
        public string SchemeName { get; }

        /// <summary>RetroEye's theme name, such as "Windows 95-98" or "Windows XP Silver".</summary>
        public string ThemeName { get; }

        /// <summary>The WPF theme that matches the look: "Classic", "Luna.NormalColor", "Luna.Homestead", "Luna.Metallic", "Royale.NormalColor" or "Aero.NormalColor".</summary>
        public string VisualStyle { get; }

        /// <summary>The colour slots by name (see <see cref="ApiColorNames"/>).</summary>
        public IReadOnlyDictionary<string, ApiColor> Colors { get; }

        /// <summary>The six fonts by name: caption, smallCaption, menu, status, message and icon.</summary>
        public IReadOnlyDictionary<string, ApiFont> Fonts { get; }

        /// <summary>The metrics by name, in the version's own pixels (at scale 1).</summary>
        public IReadOnlyDictionary<string, double> Metrics { get; }

        /// <summary>How big RetroEye draws the version's pixels on the main screen: 1, 1.25, 2...</summary>
        public double Scale { get; }

        /// <summary>Windows' own DPI on the main screen when RetroEye wrote this (96 at 100%).</summary>
        public int WindowsDpi { get; }

        /// <summary>The DPI the picture parts were drawn at (96 × Scale).</summary>
        public int Dpi { get; }

        /// <summary>The frame's kind: "raised3d" (95 to 2000 and Classic), "luna" (XP), "aero" (Vista and 7 glass) or "basic" (Vista Basic).</summary>
        public string FrameKind { get; }

        /// <summary>"square" or "rounded" (the top corners).</summary>
        public string CornerShape { get; }

        /// <summary>The top corners' radius in the version's pixels, 0 when square.</summary>
        public double CornerRadius { get; }

        /// <summary>The active caption's look.</summary>
        public ApiCaptionState CaptionActive { get; }

        /// <summary>The inactive caption's look.</summary>
        public ApiCaptionState CaptionInactive { get; }

        /// <summary>Where the title sits: "left" or "center".</summary>
        public string CaptionTextAlign { get; }

        /// <summary>The caption buttons of a sizable window, left to right, such as minimize, maximize, close.</summary>
        public IReadOnlyList<string> CaptionButtonOrder { get; }

        /// <summary>The caption buttons' style: "classic3d", "luna" or "aero".</summary>
        public string CaptionButtonStyle { get; }

        /// <summary>Whether the frame is glass (Vista's and 7's Aero with transparency).</summary>
        public bool GlassEnabled { get; }

        /// <summary>The glass's colour (Window Color), or null where there is no glass colour.</summary>
        public ApiColor? GlassColor { get; }

        /// <summary>How strongly the glass colour is laid on, 0 to 1.</summary>
        public double GlassOpacity { get; }

        /// <summary>The main taskbar's edge: "bottom", "top", "left" or "right".</summary>
        public string TaskbarEdge { get; }

        /// <summary>The main taskbar's height (or width, at the left or right), in the version's pixels.</summary>
        public double TaskbarHeight { get; }

        /// <summary>The folder the picture parts are in, or null for a state without them.</summary>
        public string? PartsFolder { get; }

        /// <summary>The parts' number, one more each time RetroEye draws them again.</summary>
        public int PartsGeneration { get; }

        /// <summary>The picture parts by name (empty when RetroEye drew none, or for a state from the pipe without a folder).</summary>
        public IReadOnlyDictionary<string, ApiPart> Parts { get; }

        /// <summary>Returns a colour slot, or the fallback if the state hasn't it.</summary>
        public ApiColor Color(string name, ApiColor fallback) => Colors.TryGetValue(name, out ApiColor color) ? color : fallback;

        /// <summary>Returns a metric, or the fallback if the state hasn't it.</summary>
        public double Metric(string name, double fallback) => Metrics.TryGetValue(name, out double value) ? value : fallback;

        /// <summary>Returns a font, or null if the state hasn't it.</summary>
        public ApiFont? Font(string name) => Fonts.TryGetValue(name, out ApiFont? font) ? font : null;

        /// <summary>Returns a picture part, or null if there is none of that name.</summary>
        public ApiPart? Part(string name) => Parts.TryGetValue(name, out ApiPart? part) ? part : null;

        /// <summary>
        /// The state used when RetroEye isn't running and has never written one: Windows 98's Windows Standard, so a
        /// client still looks like something RetroEye would show. <see cref="Running"/> is false.
        /// </summary>
        public static ThemeState Default => new ThemeState(ApiJson.Parse(DefaultJson));

        /// <summary>Windows 98's Windows Standard, as contract v1 writes it, for <see cref="Default"/>.</summary>
        internal const string DefaultJson = @"{
""contract"":{""major"":1,""minor"":0,""version"":""1.0""},""capabilities"":[],""deprecated"":[],""sequence"":0,""instance"":"""",""running"":false,""written"":"""",
""retroEye"":{""version"":"""",""processId"":0},
""windows"":{""version"":""98"",""name"":""Windows 98""},
""look"":{""id"":""classic"",""name"":""Classic"",""classic"":true,""theme"":""Windows 95-98"",""visualStyle"":""Classic"",""scheme"":""Windows Standard""},
""colors"":{""scrollBar"":""#C0C0C0"",""desktop"":""#008080"",""activeCaption"":""#000080"",""inactiveCaption"":""#808080"",""menu"":""#C0C0C0"",""window"":""#FFFFFF"",""windowFrame"":""#000000"",""menuText"":""#000000"",""windowText"":""#000000"",""captionText"":""#FFFFFF"",""activeBorder"":""#C0C0C0"",""inactiveBorder"":""#C0C0C0"",""appWorkspace"":""#808080"",""highlight"":""#000080"",""highlightText"":""#FFFFFF"",""buttonFace"":""#C0C0C0"",""buttonShadow"":""#808080"",""grayText"":""#808080"",""buttonText"":""#000000"",""inactiveCaptionText"":""#C0C0C0"",""buttonHighlight"":""#FFFFFF"",""buttonDarkShadow"":""#000000"",""buttonLight"":""#C0C0C0"",""infoText"":""#000000"",""infoBackground"":""#FFFFE1"",""buttonAlternateFace"":""#C0C0C0"",""hotLight"":""#000080"",""gradientActiveCaption"":""#1084D0"",""gradientInactiveCaption"":""#C0C0C0"",""menuHighlight"":""#000080"",""menuBar"":""#C0C0C0""},
""fonts"":{
 ""caption"":{""face"":""MS Sans Serif"",""drawnFace"":""Microsoft Sans Serif"",""sizePt"":8,""weight"":700,""italic"":false,""bitmap"":true},
 ""smallCaption"":{""face"":""MS Sans Serif"",""drawnFace"":""Microsoft Sans Serif"",""sizePt"":8,""weight"":700,""italic"":false,""bitmap"":true},
 ""menu"":{""face"":""MS Sans Serif"",""drawnFace"":""Microsoft Sans Serif"",""sizePt"":8,""weight"":400,""italic"":false,""bitmap"":true},
 ""status"":{""face"":""MS Sans Serif"",""drawnFace"":""Microsoft Sans Serif"",""sizePt"":8,""weight"":400,""italic"":false,""bitmap"":true},
 ""message"":{""face"":""MS Sans Serif"",""drawnFace"":""Microsoft Sans Serif"",""sizePt"":8,""weight"":400,""italic"":false,""bitmap"":true},
 ""icon"":{""face"":""MS Sans Serif"",""drawnFace"":""Microsoft Sans Serif"",""sizePt"":8,""weight"":400,""italic"":false,""bitmap"":true}},
""metrics"":{""captionHeight"":19,""captionButtonWidth"":18,""captionButtonHeight"":18,""smallCaptionHeight"":16,""borderWidth"":1,""sizingBorderWidth"":1,""paddedBorderWidth"":0,""frameWidth"":4,""fixedFrameWidth"":3,""edge"":2,""menuHeight"":19,""scrollBarWidth"":16,""scrollBarHeight"":16,""iconSize"":32,""iconSpacingX"":75,""iconSpacingY"":75,""captionTextInset"":2},
""scale"":{""factor"":1,""dpi"":96,""windowsDpi"":96},
""chrome"":{""frameKind"":""raised3d"",""corners"":{""shape"":""square"",""radius"":0},
 ""caption"":{""active"":{""fill"":{""kind"":""horizontal"",""stops"":[{""offset"":0,""color"":""#000080""},{""offset"":1,""color"":""#1084D0""}]},""text"":""#FFFFFF"",""textShadow"":null,""textGlow"":false},
  ""inactive"":{""fill"":{""kind"":""horizontal"",""stops"":[{""offset"":0,""color"":""#808080""},{""offset"":1,""color"":""#C0C0C0""}]},""text"":""#C0C0C0"",""textShadow"":null,""textGlow"":false},
  ""textAlign"":""left"",""font"":""caption"",""iconShown"":true,""iconSize"":16,""height"":18},
 ""buttons"":{""style"":""classic3d"",""order"":[""minimize"",""maximize"",""close""],""dialogOrder"":[""help"",""close""],""width"":16,""height"":14,""closeWidth"":16,""gap"":0,""gapBeforeClose"":2,""glyph"":""#000000""},
 ""frame"":{""active"":""#C0C0C0"",""inactive"":""#C0C0C0"",""thickness"":4,""lines"":{""active"":{""topLeft"":[""#C0C0C0"",""#FFFFFF""],""bottomRight"":[""#000000"",""#808080""]},""inactive"":{""topLeft"":[""#C0C0C0"",""#FFFFFF""],""bottomRight"":[""#000000"",""#808080""]}}},
 ""glass"":{""enabled"":false,""color"":null,""intensity"":0,""transparency"":false,""opacity"":0}},
""taskbar"":{""found"":false,""edge"":""bottom"",""height"":28,""autoHide"":false,""bounds"":null},
""parts"":{""folder"":""parts"",""scale"":1,""dpi"":96,""generation"":0,""items"":{}}
}";
    }
}
