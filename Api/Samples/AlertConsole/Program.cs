// RetroEye app API sample: a windowless program that follows RetroEye and sends alerts as the era's tray balloon.
#nullable enable
using RetroEye.Api;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

// Named events and a console of its own are Windows things, as is RetroEye
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

namespace RetroEye.Samples.AlertConsole
{
    /// <summary>
    /// A program with no window at all: it follows RetroEye, and everything it has to say comes up as RetroEye's own
    /// tray balloons (the shown Windows version's), and goes into RetroEye.AlertConsole.log beside it. It greets you when
    /// it starts and tells of every Windows version change, of RetroEye stopping, restarting and coming back. Click one of
    /// its balloons and it offers to quit; click that one and it does. Started again while it runs, it tells the running
    /// one (which says so in a balloon) and exits.
    /// Options: --console shows its lines as they come, in RetroEye's own Command Prompt (the era's console, not Windows
    /// Terminal; in the console it was started from, when it has one); --quit tells a running one to quit; --seconds N
    /// quits after N seconds; --log FILE writes the log elsewhere; --folder and --channel find a RetroEye other than the
    /// real one; --retroeye PATH (or RETROEYE_EXE) names the RetroEye.exe whose Command Prompt to use.
    /// </summary>
    public static class Program
    {
        /// <summary>The sample's name, as it introduces itself to RetroEye and titles its balloons.</summary>
        private const string AppName = "Alert console sample";

        private static readonly object LogLock = new object();
        private static StreamWriter? _log;
        private static bool _toConsole;

        /// <summary>The sample's entry point.</summary>
        public static async Task<int> Main(string[] args)
        {
            string? Arg(string name)
            {
                int at = Array.IndexOf(args, name);
                return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
            }
            bool Has(string name) => args.Contains(name);

            bool console = Has("--console");
            bool hosted = Has("--hosted");
            string channel = ApiNames.CleanChannel(Arg("--channel") ?? ApiNames.DefaultChannel());
            // One per user, session and RetroEye channel
            string key = $"RetroEye.AlertConsole.{(channel.Length > 0 ? channel : "main")}.{ApiNames.UserSid()}";

            if (Has("--quit"))
            {
                return Signal(key + ".Quit") ? 0 : 1;
            }

            string logPath = Arg("--log") ?? Path.Combine(AppContext.BaseDirectory, "RetroEye.AlertConsole.log");
            OpenLog(logPath);
            string? noPrompt = null;

            // Already running: tell that one (it says so in a balloon), and go
            if (Mutex.TryOpenExisting(@"Local\" + key, out Mutex? existing))
            {
                existing.Dispose();
                Say($"Started again (process {Environment.ProcessId}): told the running one, and exiting");
                Signal(key + ".Again");
                if (console && HasConsole())
                {
                    Console.WriteLine("Alert console sample is already running; it shows a balloon to say so.");
                }
                return 0;
            }

            // --console with no console of its own (a Windows app started from Explorer, Run or a shortcut): its lines
            // are shown in RetroEye's own Command Prompt, which hosts this program's console
            if (console && !hosted && !HasConsole())
            {
                string? why = OpenInRetroEyeCommandPrompt(args, Arg("--folder"), Arg("--retroeye"));
                if (why == null)
                {
                    return 0;
                }
                // Couldn't: run without a window after all, and say why in the first balloon
                noPrompt = why;
                console = false;
            }

            using var mutex = new Mutex(true, @"Local\" + key, out bool first);
            if (!first)
            {
                Signal(key + ".Again");
                return 0;
            }
            _toConsole = console && HasConsole();
            if (_toConsole)
            {
                try
                {
                    Console.Title = AppName;
                }
                catch (IOException)
                {
                }
            }

            using var quit = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + key + ".Quit");
            using var again = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + key + ".Again");
            using var stop = new CancellationTokenSource();
            if (_toConsole)
            {
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    stop.Cancel();
                };
            }
            if (Arg("--seconds") is string seconds)
            {
                stop.CancelAfter(TimeSpan.FromSeconds(double.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture)));
            }

            using var client = new RetroEyeClient(AppName, new RetroEyeClientOptions { Folder = Arg("--folder"), Channel = Arg("--channel") });
            Say($"{AppName} started (process {Environment.ProcessId}, {(_toConsole ? "with a console" : "no window")}); log {logPath}");
            Say($"Looking for RetroEye in {client.Folder}{(client.Channel.Length > 0 ? $" (channel {client.Channel})" : "")}");
            Say(client.IsRunning ? $"RetroEye is running: {Describe(client.State)}" : $"RetroEye isn't running; using {Describe(client.State)}");

            string lastVersion = client.State.WindowsVersion;
            int greeted = 0;
            async Task Balloon(string title, string text, string tag)
            {
                NotifyResult result = await client.TryNotifyAsync(new Notification { Title = title, Text = text, Icon = NotificationIcon.Info, Tag = tag });
                Say(result.Queued ? $"Balloon {result.Id} \"{title}\"" : $"Balloon \"{title}\" not shown: {result}");
            }
            async Task Greet()
            {
                if (Interlocked.Exchange(ref greeted, 1) == 1)
                {
                    return;
                }
                Say($"Connected: {Describe(await client.GetStateAsync())}");
                lastVersion = client.State.WindowsVersion;
                string how = noPrompt != null ? $"RetroEye's Command Prompt couldn't be opened ({noPrompt}), so it runs with no window. "
                    : _toConsole ? "Its lines show in its console too. " : "It has no window: what it has to say comes here. ";
                await Balloon($"{AppName} is running", how + "Click this balloon to quit it.", "hello");
            }

            client.EventReceived += (s, e) => Say($"Event: {e.Name}{(e.State != null ? $" -> {Describe(e.State)}" : "")}");
            client.Started += (s, e) =>
            {
                Say("RetroEye started");
                if (Volatile.Read(ref greeted) == 0)
                {
                    _ = Task.Run(async () =>
                    {
                        if (await client.WaitForConnectionAsync(10000))
                        {
                            await Greet();
                        }
                    });
                }
            };
            client.Stopped += (s, e) => Say("RetroEye stopped; waiting for it to come back");
            client.Restarting += (s, e) => Say("RetroEye is restarting");
            client.Disconnected += (s, e) => Say("Lost RetroEye's pipe; reconnecting");
            client.Reconnected += (s, e) =>
            {
                Say($"RetroEye is back: {Describe(client.State)}");
                if (Volatile.Read(ref greeted) == 1)
                {
                    _ = Balloon("RetroEye is back", $"{AppName} follows it again ({client.State.WindowsName}).", "back");
                }
            };
            client.NotificationClicked += (s, e) =>
            {
                Say($"Balloon {e.Id} clicked ({e.Tag})");
                if (e.Tag == "quit")
                {
                    stop.Cancel();
                }
                else
                {
                    _ = Balloon($"Quit {AppName}?", "Click here to quit it. It keeps running if you don't.", "quit");
                }
            };
            client.NotificationClosed += (s, e) => Say($"Balloon {e.Id} closed ({e.Reason})");
            client.StateChanged += (s, e) =>
            {
                if (e.Name == "versionChanged" && client.State.WindowsVersion != lastVersion)
                {
                    lastVersion = client.State.WindowsVersion;
                    _ = Balloon("Windows changed", $"RetroEye now shows {client.State.WindowsName} ({client.State.LookName}).", "version");
                }
            };
            client.Start();
            if (await client.WaitForConnectionAsync(client.IsRunning ? 5000 : 1000))
            {
                await Greet();
            }
            else
            {
                Say("RetroEye's pipe isn't there yet; the balloons start when RetroEye does");
            }

            var handles = new WaitHandle[] { quit, again, stop.Token.WaitHandle };
            while (!stop.IsCancellationRequested)
            {
                int which = WaitHandle.WaitAny(handles);
                if (which == 0)
                {
                    Say("Asked to quit (--quit)");
                    break;
                }
                if (which == 1)
                {
                    Say("Started again: telling you it's already running");
                    await Balloon($"{AppName} is already running", "Click this balloon to quit it.", "again");
                }
            }
            if (client.IsConnected)
            {
                await Balloon($"{AppName} has quit", "Start it again to have its alerts back.", "bye");
            }
            Say("Bye");
            _log?.Dispose();
            return 0;
        }

        /// <summary>
        /// Asks RetroEye to show this program's console in its own Command Prompt (RetroEye.exe --command-prompt --run),
        /// running a copy of it there with --console --hosted. The copy is run by dotnet.exe, a console program, so the
        /// Command Prompt has its console; this Windows app has none. Returns null when it's open, or why not.
        /// </summary>
        private static string? OpenInRetroEyeCommandPrompt(string[] args, string? folder, string? named)
        {
            string? retroEye = named ?? Environment.GetEnvironmentVariable("RETROEYE_EXE") ?? RunningRetroEye(folder);
            if (retroEye == null || !File.Exists(retroEye))
            {
                Say("Can't find RetroEye.exe for its Command Prompt");
                return "RetroEye.exe wasn't found";
            }
            string? dotnet = DotnetHost();
            string self = typeof(Program).Assembly.Location;
            if (dotnet == null || !File.Exists(self))
            {
                Say("Can't find dotnet.exe to run the console copy");
                return "dotnet.exe wasn't found";
            }
            // This program again, with the same options, as the console's program
            var command = new List<string> { Quote(dotnet), Quote(self) };
            command.AddRange(args.Where(a => a != "--hosted").Select(Quote));
            command.Add("--hosted");
            var start = new ProcessStartInfo { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
            if (retroEye.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                // A RetroEye run by dotnet (a test copy)
                start.FileName = dotnet;
                start.ArgumentList.Add(retroEye);
            }
            else
            {
                start.FileName = retroEye;
            }
            start.ArgumentList.Add("--command-prompt");
            start.ArgumentList.Add("--run");
            start.ArgumentList.Add(string.Join(" ", command));
            start.ArgumentList.Add("--title");
            start.ArgumentList.Add(AppName);
            try
            {
                using Process? process = Process.Start(start);
                Say($"Opened RetroEye's Command Prompt ({retroEye}, process {process?.Id}) to show the console copy");
                return null;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                Say($"Can't open RetroEye's Command Prompt: {ex.Message}");
                return ex.Message;
            }
        }

        /// <summary>Returns the RetroEye.exe that's running (the one that wrote theme.json), or the installed one, or null.</summary>
        private static string? RunningRetroEye(string? folder)
        {
            ThemeState? state = RetroEyeClient.ReadFile(folder ?? ApiNames.DefaultFolder());
            int id = state?.Raw["retroEye"]["processId"].AsInt() ?? 0;
            if (id > 0 && state!.Running)
            {
                try
                {
                    using Process process = Process.GetProcessById(id);
                    string? path = process.MainModule?.FileName;
                    if (path != null && Path.GetFileName(path).Equals("RetroEye.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        return path;
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                }
            }
            string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "RetroEye", "RetroEye.exe");
            return File.Exists(installed) ? installed : null;
        }

        /// <summary>Returns dotnet.exe, the .NET host this program's runtime came with, or null.</summary>
        private static string? DotnetHost()
        {
            string? process = Environment.ProcessPath;
            if (process != null && Path.GetFileName(process).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                return process;
            }
            // ...\dotnet\shared\Microsoft.NETCore.App\10.0.x\ -> ...\dotnet\dotnet.exe
            string runtime = RuntimeEnvironment.GetRuntimeDirectory();
            string fromRuntime = Path.GetFullPath(Path.Combine(runtime, "..", "..", "..", "dotnet.exe"));
            if (File.Exists(fromRuntime))
            {
                return fromRuntime;
            }
            string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            return root != null && File.Exists(Path.Combine(root, "dotnet.exe")) ? Path.Combine(root, "dotnet.exe") : null;
        }

        /// <summary>Returns an argument quoted for a command line when it needs to be.</summary>
        private static string Quote(string argument) =>
            argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0 ? argument : "\"" + argument.Replace("\"", "\\\"") + "\"";

        /// <summary>Sets a named event of the running copy's, if there is one; returns whether there was.</summary>
        private static bool Signal(string name)
        {
            if (EventWaitHandle.TryOpenExisting(@"Local\" + name, out EventWaitHandle? handle))
            {
                using (handle)
                {
                    handle.Set();
                }
                return true;
            }
            return false;
        }

        /// <summary>Whether this process has a console to write to (a Windows app started by Explorer has none).</summary>
        private static bool HasConsole() => GetConsoleWindow() != IntPtr.Zero;

        /// <summary>Opens the log, adding to what's there.</summary>
        private static void OpenLog(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                _log = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { AutoFlush = true };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // No log: the balloons still come
            }
        }

        /// <summary>Writes a line, with the time, to the log (and the console, when it has one).</summary>
        private static void Say(string text)
        {
            string line = $"{DateTime.Now:HH:mm:ss.fff}  {text}";
            lock (LogLock)
            {
                try
                {
                    _log?.WriteLine(line);
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
                if (_toConsole)
                {
                    Console.WriteLine(line);
                }
            }
        }

        /// <summary>Returns a state in a few words.</summary>
        private static string Describe(ThemeState state) =>
            $"{state.WindowsName}, {state.LookName}, caption {state.CaptionActive.Fill.Kind} {state.CaptionActive.Fill.Stops[0].Color}, " +
            $"face {state.Color("buttonFace", default)}, {state.Font("caption")?.Face} {state.Font("caption")?.SizePoints}pt, scale {state.Scale}, {state.Parts.Count} parts";

        /// <summary>Returns the console window this process is attached to, or zero.</summary>
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();
    }
}
