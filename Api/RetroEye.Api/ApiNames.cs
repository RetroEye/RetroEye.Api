// RetroEye app API, contract v1. RetroEye itself is built from this same file, so the library and RetroEye always
// agree on the names and limits below.
// Contract v1 is frozen: names and limits here may only be added to, never changed.
#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace RetroEye.Api
{
    /// <summary>
    /// The fixed names of contract v1: where RetroEye publishes theme.json and its picture parts, the named event it
    /// sets on every change, the named pipe it listens on, and the protocol's limits. See docs\API.md.
    /// </summary>
    public static class ApiNames
    {
        /// <summary>
        /// The contract's major version: 1. It goes up only for a change that could break a client (a member removed,
        /// renamed, retyped or given another meaning), and a new major is published beside the old one (api\v2\ next to
        /// api\v1\), never in its place. The folder, event and pipe names carry the major alone.
        /// </summary>
        public const int ContractMajor = 1;

        /// <summary>
        /// The contract's minor version: 1. It goes up for additions only (new members, events, messages, parts,
        /// enumeration values or capabilities), which a client built against an earlier minor ignores. 1.1 added the
        /// "restarting" event (and capability "events.restarting") and theme.json's "restarting" member.
        /// </summary>
        public const int ContractMinor = 1;

        /// <summary>The contract version as text, "major.minor": "1.1".</summary>
        public static string ContractVersion => ContractMajor + "." + ContractMinor;

        /// <summary>The contract majors this side speaks. RetroEye serves each on its own pipe and in its own folder.</summary>
        public static readonly int[] SupportedMajors = { 1 };

        /// <summary>
        /// The features the contract has, by name, as the server lists them in its welcome and theme.json: a client checks
        /// for the feature it needs rather than for a version number. Later minors only add names (1.1: events.restarting).
        /// </summary>
        public static readonly string[] Capabilities =
        {
            "state", "state.file", "state.event", "subscribe", "events.themeChanged", "events.versionChanged", "events.dpiChanged",
            "events.taskbarMoved", "events.started", "events.shuttingDown", "notify", "notify.icons", "notify.click", "notify.close",
            "parts", "parts.frame", "parts.caption", "parts.buttons", "parts.controls", "glass", "events.restarting",
        };

        /// <summary>Returns the contract's version as written in theme.json and the welcome: {"major":1,"minor":1,"version":"1.1"}.</summary>
        public static ApiJson ContractJson() => ApiJson.NewObject().Set("major", ContractMajor).Set("minor", ContractMinor).Set("version", ContractVersion);

        /// <summary>Returns the capabilities as a JSON array of names.</summary>
        public static ApiJson CapabilitiesJson()
        {
            var array = ApiJson.NewArray();
            foreach (string capability in Capabilities)
            {
                array.Add(ApiJson.From(capability));
            }
            return array;
        }

        /// <summary>
        /// Returns the members, events, messages and parts deprecated in this contract major, as written in theme.json and
        /// the welcome: each {"name","since","removedInMajor","use"}. Empty in 1.0. A deprecation is listed at least one minor
        /// before the major that drops it, and stays listed for as long as the old major is served.
        /// </summary>
        public static ApiJson DeprecatedJson() => ApiJson.NewArray();

        /// <summary>The published state file's name, in the contract's folder.</summary>
        public const string ThemeFile = "theme.json";

        /// <summary>The folder of picture parts, under the contract's folder.</summary>
        public const string PartsFolder = "parts";

        /// <summary>The environment variable that moves the contract's folder (the folder holding theme.json), for tests and side-by-side copies.</summary>
        public const string FolderVariable = "RETROEYE_API_FOLDER";

        /// <summary>
        /// The environment variable naming a channel: a word added to the event's and pipe's names, so a test copy of
        /// RetroEye and its test clients never meet the real one. Empty or unset for the real RetroEye.
        /// </summary>
        public const string ChannelVariable = "RETROEYE_API_CHANNEL";

        /// <summary>The longest line, in bytes of UTF-8 without its newline, RetroEye reads from a client: 64 KiB.</summary>
        public const int MaxClientLine = 64 * 1024;

        /// <summary>The longest line RetroEye writes to a client: 1 MiB (a state is far smaller).</summary>
        public const int MaxServerLine = 1024 * 1024;

        /// <summary>How many clients may be connected at once: 16.</summary>
        public const int MaxClients = 16;

        /// <summary>How many messages a client may send each second on average: 20, with bursts of up to RateBurst.</summary>
        public const int RatePerSecond = 20;

        /// <summary>How many messages a client may send in one burst before the rate limit applies: 40.</summary>
        public const int RateBurst = 40;

        /// <summary>How many notifications one client may have waiting to be shown: 5.</summary>
        public const int MaxQueuedPerClient = 5;

        /// <summary>How many notifications may be waiting from all clients together: 32.</summary>
        public const int MaxQueued = 32;

        /// <summary>The longest notification title, in characters: 63, as Windows' own balloons allowed.</summary>
        public const int MaxTitle = 63;

        /// <summary>The longest notification text, in characters: 255, as Windows' own balloons allowed.</summary>
        public const int MaxText = 255;

        /// <summary>The longest client or app name, in characters: 64.</summary>
        public const int MaxName = 64;

        /// <summary>The shortest time a notification stays up, in milliseconds: 2 seconds.</summary>
        public const int MinTimeout = 2000;

        /// <summary>The longest time a notification stays up, in milliseconds: 30 seconds.</summary>
        public const int MaxTimeout = 30000;

        /// <summary>How long a notification stays up when the client gives no timeout, in milliseconds: 10 seconds.</summary>
        public const int DefaultTimeout = 10000;

        /// <summary>How long RetroEye keeps the change event set before resetting it, in milliseconds: 500.</summary>
        public const int EventPulse = 500;

        /// <summary>
        /// Returns the contract's folder: RETROEYE_API_FOLDER if set, otherwise %LOCALAPPDATA%\RetroEye\api\v1.
        /// </summary>
        public static string DefaultFolder()
        {
            string? moved = Environment.GetEnvironmentVariable(FolderVariable);
            if (!string.IsNullOrWhiteSpace(moved))
            {
                return moved!;
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetroEye", "api", "v1");
        }

        /// <summary>Returns the channel from RETROEYE_API_CHANNEL: letters, digits, '-' and '_' only, at most 32; empty for none.</summary>
        public static string DefaultChannel() => CleanChannel(Environment.GetEnvironmentVariable(ChannelVariable));

        /// <summary>Returns a channel name with everything but letters, digits, '-' and '_' taken out, at most 32 characters.</summary>
        public static string CleanChannel(string? channel)
        {
            if (string.IsNullOrEmpty(channel))
            {
                return "";
            }
            var kept = new System.Text.StringBuilder();
            foreach (char c in channel!)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_')
                {
                    kept.Append(c);
                    if (kept.Length == 32)
                    {
                        break;
                    }
                }
            }
            return kept.ToString();
        }

        /// <summary>The current user's security identifier as text (S-1-5-21-...), which the event's and pipe's names carry.</summary>
        public static string UserSid()
        {
            IntPtr token = IntPtr.Zero, buffer = IntPtr.Zero, text = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), 0x0008 /* TOKEN_QUERY */, out token))
                {
                    return "unknown";
                }
                GetTokenInformation(token, 1 /* TokenUser */, IntPtr.Zero, 0, out int size);
                if (size <= 0)
                {
                    return "unknown";
                }
                buffer = Marshal.AllocHGlobal(size);
                if (!GetTokenInformation(token, 1, buffer, size, out _))
                {
                    return "unknown";
                }
                // TOKEN_USER starts with SID_AND_ATTRIBUTES, whose first field points at the SID
                IntPtr sid = Marshal.ReadIntPtr(buffer);
                return ConvertSidToStringSid(sid, out text) ? Marshal.PtrToStringUni(text) ?? "unknown" : "unknown";
            }
            finally
            {
                if (text != IntPtr.Zero) LocalFree(text);
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                if (token != IntPtr.Zero) CloseHandle(token);
            }
        }

        /// <summary>Opens a process's access token.</summary>
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);
        /// <summary>Reads a part of an access token (its user).</summary>
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int kind, IntPtr buffer, int length, out int needed);
        /// <summary>Writes a SID as text.</summary>
        [DllImport("advapi32.dll", EntryPoint = "ConvertSidToStringSidW", SetLastError = true)] private static extern bool ConvertSidToStringSid(IntPtr sid, out IntPtr text);
        /// <summary>This process's pseudo handle.</summary>
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        /// <summary>Closes a handle.</summary>
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        /// <summary>Frees memory Windows gave.</summary>
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

        /// <summary>The Windows session this process runs in (0 for services, 1 and up for people signed in).</summary>
        public static int SessionId() => System.Diagnostics.Process.GetCurrentProcess().SessionId;

        /// <summary>The " .channel" part of a name: empty for no channel.</summary>
        private static string ChannelPart(string channel) => string.IsNullOrEmpty(channel) ? "" : "." + channel;

        /// <summary>
        /// The name of the manual-reset event RetroEye sets for EventPulse milliseconds whenever theme.json changes:
        /// Local\RetroEye.Api.v1[.channel].Changed.&lt;user SID&gt;. Local\ already keeps it to the session.
        /// </summary>
        public static string ChangedEventName(string channel, string sid) => $@"Local\RetroEye.Api.v1{ChannelPart(channel)}.Changed.{sid}";

        /// <summary>
        /// The name of RetroEye's pipe (without \\.\pipe\): RetroEye.Api.v1[.channel].&lt;session&gt;.&lt;user SID&gt;. Pipe names are
        /// machine-wide, so the session and user are in the name, and only that user may open it.
        /// </summary>
        public static string PipeName(string channel, int session, string sid) => $"RetroEye.Api.v1{ChannelPart(channel)}.{session}.{sid}";
    }
}
