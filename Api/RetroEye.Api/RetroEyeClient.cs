// RetroEye app API, contract v1: the client. Talks to RetroEye on its pipe, falls back to theme.json and the change
// event when the pipe can't be opened, and reconnects by itself.
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RetroEye.Api
{
    /// <summary>Where a client finds RetroEye: the contract's folder and the channel. The defaults find the real RetroEye.</summary>
    public sealed class RetroEyeClientOptions
    {
        /// <summary>The folder holding theme.json; RETROEYE_API_FOLDER or %LOCALAPPDATA%\RetroEye\api\v1 when null.</summary>
        public string? Folder { get; set; }

        /// <summary>The channel (see ApiNames.ChannelVariable); RETROEYE_API_CHANNEL when null.</summary>
        public string? Channel { get; set; }

        /// <summary>Whether to keep RetroEye's pipe open for pushed events and notifications (true), or only watch the file and event.</summary>
        public bool UsePipe { get; set; } = true;

        /// <summary>
        /// How long to wait between tries to reach RetroEye, in milliseconds: 2 seconds. After the pipe drops, the client
        /// tries again sooner (a quarter of a second) and backs off, doubling each time, up to this.
        /// </summary>
        public int RetryMs { get; set; } = 2000;

        /// <summary>How long a request waits for its answer, in milliseconds: 5 seconds.</summary>
        public int RequestTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// How long a request (a notification, <see cref="RetroEyeClient.GetStateAsync"/>) made while RetroEye runs but
        /// the pipe isn't ready yet (the client has only just started, or is reconnecting after a drop or a restart) waits
        /// for it to be ready before giving up, in milliseconds: 5 seconds. 0 doesn't wait.
        /// </summary>
        public int ConnectWaitMs { get; set; } = 5000;

        /// <summary>
        /// How long RetroEye may take to come back after saying it is restarting before the client calls it stopped, in
        /// milliseconds: 30 seconds.
        /// </summary>
        public int RestartWaitMs { get; set; } = 30000;
    }

    /// <summary>What became of a notification sent with <see cref="RetroEyeClient.TryNotifyAsync"/>.</summary>
    public enum NotifyStatus
    {
        /// <summary>RetroEye queued it; <see cref="NotifyResult.Id"/> is its id, and it is shown in turn.</summary>
        Queued,

        /// <summary>RetroEye isn't running (or the client hasn't been started), so it wasn't sent.</summary>
        NotRunning,

        /// <summary>RetroEye runs but the pipe wasn't ready within <see cref="RetroEyeClientOptions.ConnectWaitMs"/>, or dropped as it was sent.</summary>
        NotConnected,

        /// <summary>RetroEye refused it; <see cref="NotifyResult.Code"/> says why (queueFull, rateLimited, disabled...).</summary>
        Refused,

        /// <summary>It was sent, but RetroEye didn't answer within <see cref="RetroEyeClientOptions.RequestTimeoutMs"/>.</summary>
        TimedOut,
    }

    /// <summary>The result of <see cref="RetroEyeClient.TryNotifyAsync"/>: what became of the notification, and why.</summary>
    public sealed class NotifyResult
    {
        /// <summary>Makes a result.</summary>
        public NotifyResult(NotifyStatus status, string? id = null, string? code = null, string? message = null)
        {
            Status = status;
            Id = id;
            Code = code;
            Message = message;
        }

        /// <summary>What became of it.</summary>
        public NotifyStatus Status { get; }

        /// <summary>Whether RetroEye queued it.</summary>
        public bool Queued => Status == NotifyStatus.Queued;

        /// <summary>The id RetroEye gave it ("n-17"), when queued.</summary>
        public string? Id { get; }

        /// <summary>RetroEye's error code, when refused.</summary>
        public string? Code { get; }

        /// <summary>RetroEye's words, when refused.</summary>
        public string? Message { get; }

        /// <summary>Returns the result in a few words.</summary>
        public override string ToString() => Status switch
        {
            NotifyStatus.Queued => $"queued as {Id}",
            NotifyStatus.NotRunning => "RetroEye isn't running",
            NotifyStatus.NotConnected => "RetroEye's pipe wasn't ready",
            NotifyStatus.Refused => $"refused: {Code}",
            _ => "RetroEye didn't answer in time",
        };
    }

    /// <summary>
    /// A program's line to RetroEye: what Windows version and look it shows (<see cref="State"/>), word of every
    /// change, and notifications shown as the era's own tray balloons. Works with no window at all. When RetroEye
    /// isn't running it says so (<see cref="IsRunning"/> false) and keeps the last state it saw, or Windows 98's
    /// defaults; it reconnects by itself, backing off, whenever the pipe drops, subscribes again, and says so
    /// (<see cref="Reconnected"/>). Events are raised on the SynchronizationContext the client was made on (a WPF or
    /// WinForms window's thread), or on a background thread when there is none.
    /// </summary>
    public sealed class RetroEyeClient : IDisposable
    {
        /// <summary>The first wait before trying the pipe again after it dropped, in milliseconds.</summary>
        private const int FirstBackOffMs = 250;

        private readonly string _name;
        private readonly RetroEyeClientOptions _options;
        private readonly SynchronizationContext? _context;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly ConcurrentDictionary<long, TaskCompletionSource<ApiJson>> _waiting = new ConcurrentDictionary<long, TaskCompletionSource<ApiJson>>();
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private readonly object _stateLock = new object();
        private Task? _loop;
        private NamedPipeClientStream? _pipe;
        private long _nextId;
        private long _helloId;
        private ThemeState _state;
        private ThemeState? _beforeConnect;
        private volatile bool _running;
        private volatile bool _connected;
        private volatile bool _hadConnection;
        private volatile string _startedFor = "";
        private TaskCompletionSource<bool> _ready = NewReady();
        private long _restartingUntil;

        /// <summary>Makes a client that introduces itself by the name given (at most 64 characters); call <see cref="Start"/> to begin.</summary>
        public RetroEyeClient(string clientName, RetroEyeClientOptions? options = null)
        {
            _name = string.IsNullOrWhiteSpace(clientName) ? "Unnamed app" : clientName;
            _options = options ?? new RetroEyeClientOptions();
            Folder = _options.Folder ?? ApiNames.DefaultFolder();
            Channel = ApiNames.CleanChannel(_options.Channel ?? ApiNames.DefaultChannel());
            _context = SynchronizationContext.Current;
            _state = ReadFile(Folder) ?? ThemeState.Default;
            _running = _state.Running && IsWriterAlive(_state);
            if (_running)
            {
                _startedFor = _state.Instance;
            }
        }

        /// <summary>The folder holding theme.json and the parts.</summary>
        public string Folder { get; private set; }

        /// <summary>The channel the client looks on (empty for the real RetroEye).</summary>
        public string Channel { get; }

        /// <summary>The last state seen: RetroEye's now, its last before it closed, or Windows 98's defaults.</summary>
        public ThemeState State
        {
            get
            {
                lock (_stateLock)
                {
                    return _state;
                }
            }
        }

        /// <summary>Whether RetroEye is running (its pipe answered, or theme.json says so and its process is alive).</summary>
        public bool IsRunning => _running;

        /// <summary>
        /// Whether RetroEye has said it is restarting (contract 1.1) and hasn't come back yet; it is still counted as
        /// running meanwhile, for up to <see cref="RetroEyeClientOptions.RestartWaitMs"/>.
        /// </summary>
        public bool IsRestarting => Interlocked.Read(ref _restartingUntil) > DateTime.UtcNow.Ticks;

        /// <summary>The capabilities RetroEye named in its welcome on the pipe (empty before it); see <see cref="Supports"/>.</summary>
        public IReadOnlyList<string> ServerCapabilities { get; private set; } = Array.Empty<string>();

        /// <summary>Why RetroEye refused the pipe (it doesn't speak this library's contract major), or null.</summary>
        public string? Refused { get; private set; }

        /// <summary>
        /// Whether RetroEye has a feature, by capability name ("notify", "parts.buttons"...): from its welcome when
        /// connected, else from theme.json. Check for features this way rather than by version number.
        /// </summary>
        public bool Supports(string capability) =>
            (ServerCapabilities.Count > 0 ? ServerCapabilities : State.Capabilities).Contains(capability);

        /// <summary>
        /// Whether the pipe to RetroEye is open and RetroEye has welcomed the client, so events come at once and
        /// notifications can be sent.
        /// </summary>
        public bool IsConnected => _connected;

        /// <summary>Raised when anything in the state changes (any of themeChanged, versionChanged, dpiChanged, taskbarMoved).</summary>
        public event EventHandler<RetroEyeEventArgs>? StateChanged;

        /// <summary>Raised for every event RetroEye pushes, by name, including those StateChanged covers.</summary>
        public event EventHandler<RetroEyeEventArgs>? EventReceived;

        /// <summary>Raised when RetroEye is found running after it wasn't (it started, or the client found it), or a new copy of it took over after a restart.</summary>
        public event EventHandler? Started;

        /// <summary>Raised when RetroEye says it is shutting down (and not restarting), or is found gone.</summary>
        public event EventHandler? Stopped;

        /// <summary>
        /// Raised when the pipe is ready again after it dropped ("RetroEye is back"): the client has said hello again and
        /// subscribed again. Any change missed meanwhile has been raised as its event (versionChanged...) just before.
        /// </summary>
        public event EventHandler? Reconnected;

        /// <summary>Raised when a ready pipe drops while RetroEye is still running or restarting; the client keeps trying again by itself.</summary>
        public event EventHandler? Disconnected;

        /// <summary>Raised when RetroEye says it is closing to start again at once (contract 1.1's "restarting"); <see cref="Stopped"/> is not raised for it.</summary>
        public event EventHandler? Restarting;

        /// <summary>Raised when a notification this client sent is clicked.</summary>
        public event EventHandler<NotificationEventArgs>? NotificationClicked;

        /// <summary>Raised when a notification this client sent closes, with why.</summary>
        public event EventHandler<NotificationEventArgs>? NotificationClosed;

        /// <summary>Starts looking for RetroEye in the background; returns at once.</summary>
        public void Start()
        {
            if (_loop == null)
            {
                _loop = Task.Run(() => RunAsync(_stop.Token));
            }
        }

        /// <summary>
        /// Waits until the pipe is ready (RetroEye has welcomed the client), for at most the time given; returns at once
        /// when it already is. Returns whether it is.
        /// </summary>
        public async Task<bool> WaitForConnectionAsync(int timeoutMs, CancellationToken cancel = default)
        {
            if (_connected)
            {
                return true;
            }
            if (_loop == null || timeoutMs <= 0)
            {
                return false;
            }
            Task ready = Volatile.Read(ref _ready).Task;
            Task done = await Task.WhenAny(ready, Task.Delay(timeoutMs, cancel)).ConfigureAwait(false);
            cancel.ThrowIfCancellationRequested();
            return done == ready && _connected;
        }

        /// <summary>
        /// Asks RetroEye to show a notification as the era's own tray balloon; returns the id it was queued as, or null
        /// if RetroEye isn't connected. If RetroEye runs but the pipe isn't ready yet (just started, reconnecting), it
        /// waits for it for up to <see cref="RetroEyeClientOptions.ConnectWaitMs"/>. Throws <see cref="RetroEyeApiException"/>
        /// if RetroEye refuses it (queue full, rate limit, API turned off...). <see cref="TryNotifyAsync"/> says why instead.
        /// </summary>
        public async Task<string?> NotifyAsync(Notification notification, CancellationToken cancel = default)
        {
            if (notification == null)
            {
                throw new ArgumentNullException(nameof(notification));
            }
            ApiJson? reply = await RequestAsync(notification.ToJson(), cancel).ConfigureAwait(false);
            return reply?["notification"].AsStringOrNull();
        }

        /// <summary>
        /// Asks RetroEye to show a notification as the era's own tray balloon, as <see cref="NotifyAsync"/> does (waiting
        /// for the pipe while it gets ready), and says plainly what became of it: queued with its id, RetroEye not
        /// running, the pipe not ready in time, refused with RetroEye's code, or no answer. Never throws for those.
        /// </summary>
        public async Task<NotifyResult> TryNotifyAsync(Notification notification, CancellationToken cancel = default)
        {
            if (notification == null)
            {
                throw new ArgumentNullException(nameof(notification));
            }
            if (_loop == null || (!_connected && !_running && !IsRestarting))
            {
                return new NotifyResult(NotifyStatus.NotRunning);
            }
            try
            {
                ApiJson? reply = await RequestAsync(notification.ToJson(), cancel).ConfigureAwait(false);
                string? id = reply?["notification"].AsStringOrNull();
                if (id != null)
                {
                    return new NotifyResult(NotifyStatus.Queued, id);
                }
                return new NotifyResult(_running || IsRestarting ? NotifyStatus.NotConnected : NotifyStatus.NotRunning);
            }
            catch (RetroEyeApiException ex)
            {
                return new NotifyResult(NotifyStatus.Refused, null, ex.Code, ex.Message);
            }
            catch (TimeoutException)
            {
                return new NotifyResult(NotifyStatus.TimedOut);
            }
        }

        /// <summary>Asks RetroEye for its state now (waiting for the pipe while it gets ready); gives the last state seen if it isn't connected.</summary>
        public async Task<ThemeState> GetStateAsync(CancellationToken cancel = default)
        {
            ApiJson? reply = await RequestAsync(ApiJson.NewObject().Set("type", "get"), cancel).ConfigureAwait(false);
            if (reply != null && !reply["state"].IsNull)
            {
                var state = new ThemeState(reply["state"], PartsFolderOf(reply["state"]));
                SetState(state);
                return state;
            }
            return State;
        }

        /// <summary>Reads theme.json from a folder, or returns null if there is none or it can't be read.</summary>
        public static ThemeState? ReadFile(string folder) => ReadFile(folder, out _);

        /// <summary>Reads theme.json from a folder, or returns null, saying whether that's because there is none (rather than it being unreadable just now).</summary>
        private static ThemeState? ReadFile(string folder, out bool missing)
        {
            missing = false;
            string path = Path.Combine(folder, ApiNames.ThemeFile);
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        // Gone for a moment while it is replaced, or never written: look once more before saying so
                        if (attempt < 1)
                        {
                            Thread.Sleep(30);
                            continue;
                        }
                        missing = true;
                        return null;
                    }
                    string text;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        text = reader.ReadToEnd();
                    }
                    ApiJson json = ApiJson.Parse(text);
                    return new ThemeState(json, Path.Combine(folder, json["parts"]["folder"].AsString(ApiNames.PartsFolder)));
                }
                catch (IOException)
                {
                    // Being replaced this moment: try again shortly
                    Thread.Sleep(30);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(30);
                }
                catch (FormatException)
                {
                    Thread.Sleep(30);
                }
            }
            return null;
        }

        /// <summary>Stops looking for RetroEye and closes the pipe.</summary>
        public void Dispose()
        {
            _stop.Cancel();
            try
            {
                _pipe?.Dispose();
            }
            catch (IOException)
            {
            }
            try
            {
                _loop?.Wait(1000);
            }
            catch (AggregateException)
            {
            }
            foreach (var waiting in _waiting.Values)
            {
                waiting.TrySetCanceled();
            }
            Volatile.Read(ref _ready).TrySetResult(false);
        }

        // ---- The loop ----

        /// <summary>
        /// Keeps trying the pipe, and between tries watches the file and its event, until stopped. After the pipe drops it
        /// tries again after a quarter of a second, then backs off (doubling up to RetryMs) until RetroEye welcomes it again;
        /// the file and its event are watched meanwhile, so no change is missed. Nothing that goes wrong ends the loop.
        /// </summary>
        private async Task RunAsync(CancellationToken stop)
        {
            int backOff = 0;
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    int wait = Math.Max(50, _options.RetryMs);
                    if (_options.UsePipe && Refused == null)
                    {
                        PipeOutcome outcome = await TryPipeAsync(stop).ConfigureAwait(false);
                        if (outcome == PipeOutcome.Welcomed)
                        {
                            // It was up, and has dropped: back soon
                            backOff = FirstBackOffMs;
                        }
                        else if (outcome == PipeOutcome.Dropped || backOff > 0)
                        {
                            // Dropped before RetroEye said welcome, or still can't be opened after a drop: back off
                            backOff = backOff == 0 ? FirstBackOffMs : Math.Min(backOff * 2, wait);
                        }
                        if (backOff > 0)
                        {
                            wait = Math.Min(backOff, wait);
                        }
                    }
                    WatchFileOnce(stop, wait);
                    CheckRestartOver();
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception)
                {
                    // Whatever it was, keep going: a moment's pause, then look again
                    stop.WaitHandle.WaitOne(Math.Max(50, _options.RetryMs));
                }
            }
        }

        /// <summary>How a try at the pipe ended.</summary>
        private enum PipeOutcome
        {
            /// <summary>It couldn't be opened (RetroEye isn't listening, or is busy).</summary>
            NotOpened,

            /// <summary>It opened, but closed before RetroEye welcomed the client.</summary>
            Dropped,

            /// <summary>RetroEye welcomed the client, and the pipe has since closed.</summary>
            Welcomed,
        }

        /// <summary>
        /// Opens the pipe and talks on it until it closes: hello, then subscribe to everything (again, after a drop).
        /// The client counts as connected only once RetroEye's welcome has come.
        /// </summary>
        private async Task<PipeOutcome> TryPipeAsync(CancellationToken stop)
        {
            string name = ApiNames.PipeName(Channel, ApiNames.SessionId(), ApiNames.UserSid());
            var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                pipe.Connect(300);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                pipe.Dispose();
                return PipeOutcome.NotOpened;
            }
            _pipe = pipe;
            bool welcomed = false;
            try
            {
                var reader = new ApiLineReader(pipe, ApiNames.MaxServerLine);
                _beforeConnect = State;
                long hello = Interlocked.Increment(ref _nextId);
                Interlocked.Exchange(ref _helloId, hello);
                await SendAsync(ApiJson.NewObject().Set("type", "hello").Set("client", _name).Set("contracts", ApiJson.NewArray().Add(ApiJson.From(ApiNames.ContractMajor))).Set("minor", ApiNames.ContractMinor).Set("id", hello), stop).ConfigureAwait(false);
                await SendAsync(ApiJson.NewObject().Set("type", "subscribe").Set("knownInstance", State.Instance).Set("id", Interlocked.Increment(ref _nextId)), stop).ConfigureAwait(false);
                while (!stop.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(stop).ConfigureAwait(false);
                    if (line == null)
                    {
                        break;
                    }
                    if (ApiJson.TryParse(line, out ApiJson? message) && message != null)
                    {
                        Handle(message);
                        welcomed |= _connected;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
            {
            }
            finally
            {
                bool wasConnected = _connected;
                _connected = false;
                _pipe = null;
                pipe.Dispose();
                // Requests made from now wait for the next welcome
                TaskCompletionSource<bool> ready = Volatile.Read(ref _ready);
                if (ready.Task.IsCompleted)
                {
                    Interlocked.CompareExchange(ref _ready, NewReady(), ready);
                }
                foreach (var waiting in _waiting)
                {
                    waiting.Value.TrySetResult(ApiJson.Missing);
                }
                _waiting.Clear();
                if (!stop.IsCancellationRequested)
                {
                    JudgeAfterDrop(wasConnected);
                }
            }
            return welcomed ? PipeOutcome.Welcomed : PipeOutcome.Dropped;
        }

        /// <summary>
        /// After the pipe closed without RetroEye saying it is shutting down: it has stopped only if theme.json says so
        /// (and not that it's restarting), or its process is gone. A file that can't be read just now (it is being
        /// replaced) proves nothing; the loop keeps looking.
        /// </summary>
        private void JudgeAfterDrop(bool wasConnected)
        {
            if (!_running)
            {
                return;
            }
            if (!IsRestarting)
            {
                ThemeState? file = ReadFile(Folder, out bool missing);
                if (file != null && file.Restarting)
                {
                    BeginRestart(raiseEvent: true);
                }
                else if (missing || (file != null && (!file.Running || !IsWriterAlive(file))))
                {
                    _running = false;
                    Raise(() => Stopped?.Invoke(this, EventArgs.Empty));
                    return;
                }
            }
            if (wasConnected)
            {
                Raise(() => Disconnected?.Invoke(this, EventArgs.Empty));
            }
        }

        /// <summary>Handles one message from RetroEye: an answer to a request, or an event.</summary>
        private void Handle(ApiJson message)
        {
            string type = message["type"].AsString();
            if (type == "welcome")
            {
                Welcome(message);
            }
            if (type == "error" && message["code"].AsString() == "unsupportedContract")
            {
                // This RetroEye doesn't speak the library's major on this pipe: keep to theme.json and its event
                Refused = message["message"].AsString("unsupported contract");
                _pipe?.Dispose();
            }
            if (type == "state" && !message["state"].IsNull)
            {
                var state = new ThemeState(message["state"], PartsFolderOf(message["state"]));
                ThemeState before = State;
                SetState(state);
                if (message["re"].Kind == ApiJsonKind.Number && (long)message["re"].AsNumber() == Interlocked.Read(ref _helloId))
                {
                    // The state with the welcome: anything that changed while the client was away is told now, then that it's back
                    RaiseChange(_beforeConnect ?? before, state);
                    if (_hadConnection)
                    {
                        Raise(() => Reconnected?.Invoke(this, EventArgs.Empty));
                    }
                    _hadConnection = true;
                    // Requests waiting for the pipe go now
                    Volatile.Read(ref _ready).TrySetResult(true);
                }
            }
            if (message.Has("re") && message["re"].Kind == ApiJsonKind.Number && _waiting.TryRemove((long)message["re"].AsNumber(), out var waiting))
            {
                waiting.TrySetResult(message);
            }
            if (type != "event")
            {
                return;
            }
            string name = message["event"].AsString();
            ThemeState? pushed = message["state"].IsNull ? null : new ThemeState(message["state"], PartsFolderOf(message["state"]));
            if (pushed != null)
            {
                SetState(pushed);
            }
            var args = new RetroEyeEventArgs(name, message, pushed);
            Raise(() => EventReceived?.Invoke(this, args));
            switch (name)
            {
                case "themeChanged":
                case "versionChanged":
                case "dpiChanged":
                case "taskbarMoved":
                    Raise(() => StateChanged?.Invoke(this, args));
                    break;
                case "started":
                    StartedFor(message["instance"].AsString(State.Instance));
                    break;
                case "restarting":
                    BeginRestart(raiseEvent: true);
                    break;
                case "shuttingDown":
                    if (!IsRestarting)
                    {
                        _running = false;
                        Raise(() => Stopped?.Invoke(this, EventArgs.Empty));
                    }
                    break;
                case "notificationClicked":
                    var clicked = new NotificationEventArgs(message["notification"].AsString(), message["tag"].AsStringOrNull(), "clicked");
                    Raise(() => NotificationClicked?.Invoke(this, clicked));
                    break;
                case "notificationClosed":
                    var closed = new NotificationEventArgs(message["notification"].AsString(), message["tag"].AsStringOrNull(), message["reason"].AsString("timeout"));
                    Raise(() => NotificationClosed?.Invoke(this, closed));
                    break;
            }
        }

        /// <summary>Takes RetroEye's welcome: the folder, its capabilities, and that it runs (a new copy of it, perhaps).</summary>
        private void Welcome(ApiJson message)
        {
            string folder = message["folder"].AsString();
            if (folder.Length > 0)
            {
                Folder = folder;
            }
            var capabilities = new List<string>();
            foreach (ApiJson capability in message["capabilities"].Items)
            {
                capabilities.Add(capability.AsString());
            }
            ServerCapabilities = capabilities;
            Interlocked.Exchange(ref _restartingUntil, 0);
            StartedFor(message["instance"].AsString());
            _connected = true;
        }

        /// <summary>Raises Started once for each copy of RetroEye found running after none was, or after a restart.</summary>
        private void StartedFor(string instance)
        {
            if (_running && (instance.Length == 0 || instance == _startedFor))
            {
                return;
            }
            _running = true;
            if (instance.Length > 0)
            {
                _startedFor = instance;
            }
            // A new copy is up: a restart it announced is over
            Interlocked.Exchange(ref _restartingUntil, 0);
            Raise(() => Started?.Invoke(this, EventArgs.Empty));
        }

        /// <summary>Notes that RetroEye is restarting: it is still counted as running for a while, and Restarting is raised.</summary>
        private void BeginRestart(bool raiseEvent)
        {
            bool already = IsRestarting;
            Interlocked.Exchange(ref _restartingUntil, DateTime.UtcNow.AddMilliseconds(Math.Max(1000, _options.RestartWaitMs)).Ticks);
            if (raiseEvent && !already)
            {
                Raise(() => Restarting?.Invoke(this, EventArgs.Empty));
            }
        }

        /// <summary>Calls RetroEye stopped once a restart it announced has taken too long.</summary>
        private void CheckRestartOver()
        {
            long until = Interlocked.Read(ref _restartingUntil);
            if (until != 0 && until <= DateTime.UtcNow.Ticks && !_connected)
            {
                Interlocked.Exchange(ref _restartingUntil, 0);
                if (_running)
                {
                    ThemeState? file = ReadFile(Folder);
                    if (file == null || !file.Running || !IsWriterAlive(file))
                    {
                        _running = false;
                        Raise(() => Stopped?.Invoke(this, EventArgs.Empty));
                    }
                }
            }
        }

        /// <summary>Raises what changed between two states as the events RetroEye would have pushed: versionChanged and themeChanged, or themeChanged.</summary>
        private void RaiseChange(ThemeState before, ThemeState after)
        {
            if (after.Sequence == before.Sequence && after.Instance == before.Instance)
            {
                return;
            }
            bool version = before.WindowsVersion != after.WindowsVersion || before.LookId != after.LookId;
            foreach (string name in version ? new[] { "versionChanged", "themeChanged" } : new[] { "themeChanged" })
            {
                var args = new RetroEyeEventArgs(name, ApiJson.NewObject().Set("type", "event").Set("event", name), after);
                Raise(() => EventReceived?.Invoke(this, args));
                Raise(() => StateChanged?.Invoke(this, args));
            }
        }

        /// <summary>
        /// Waits for the change event (or the time given), then reads theme.json and raises what changed; for clients
        /// without the pipe, and between tries to open it.
        /// </summary>
        private void WatchFileOnce(CancellationToken stop, int waitMs)
        {
            EventWaitHandle? changed = null;
            try
            {
                changed = EventWaitHandle.OpenExisting(ApiNames.ChangedEventName(Channel, ApiNames.UserSid()));
            }
            catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
            {
            }
            try
            {
                if (changed != null)
                {
                    int signalled = WaitHandle.WaitAny(new[] { changed, stop.WaitHandle }, waitMs);
                    if (signalled == 0)
                    {
                        // Let the event fall again (RetroEye resets it after a moment), so one change isn't read twice
                        Thread.Sleep(ApiNames.EventPulse + 50);
                    }
                }
                else
                {
                    stop.WaitHandle.WaitOne(waitMs);
                }
            }
            finally
            {
                changed?.Dispose();
            }
            if (stop.IsCancellationRequested || _connected)
            {
                return;
            }
            ThemeState? file = ReadFile(Folder, out bool missing);
            if (file == null && !missing)
            {
                // Can't be read just now: nothing is known, so nothing changes
                return;
            }
            if (file != null)
            {
                ThemeState before = State;
                if (file.Sequence != before.Sequence || file.Instance != before.Instance)
                {
                    SetState(file);
                    RaiseChange(before, file);
                }
                if (file.Restarting && !file.Running && _running)
                {
                    BeginRestart(raiseEvent: true);
                    return;
                }
            }
            bool running = file != null && file.Running && IsWriterAlive(file);
            if (running)
            {
                StartedFor(file!.Instance);
            }
            else if (_running && !IsRestarting)
            {
                _running = false;
                Raise(() => Stopped?.Invoke(this, EventArgs.Empty));
            }
        }

        // ---- Sending ----

        /// <summary>
        /// Sends a request with a new id and waits for its answer; waits first for the pipe to be ready when RetroEye runs
        /// but it isn't yet. Null if not connected; throws for an error answer, or TimeoutException when no answer comes.
        /// </summary>
        private async Task<ApiJson?> RequestAsync(ApiJson request, CancellationToken cancel)
        {
            if (!_connected && (_running || IsRestarting) && _options.UsePipe && Refused == null)
            {
                await WaitForConnectionAsync(_options.ConnectWaitMs, cancel).ConfigureAwait(false);
            }
            if (!_connected || _pipe == null)
            {
                return null;
            }
            long id = Interlocked.Increment(ref _nextId);
            request.Set("id", id);
            var answer = new TaskCompletionSource<ApiJson>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiting[id] = answer;
            try
            {
                await SendAsync(request, cancel).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                _waiting.TryRemove(id, out _);
                return null;
            }
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                timeout.CancelAfter(_options.RequestTimeoutMs);
                using (timeout.Token.Register(() => answer.TrySetCanceled()))
                {
                    ApiJson reply;
                    try
                    {
                        reply = await answer.Task.ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        _waiting.TryRemove(id, out _);
                        cancel.ThrowIfCancellationRequested();
                        throw new TimeoutException("RetroEye didn't answer in time.");
                    }
                    if (reply.IsNull)
                    {
                        return null;
                    }
                    if (reply["type"].AsString() == "error")
                    {
                        throw new RetroEyeApiException(reply["code"].AsString("error"), reply["message"].AsString());
                    }
                    return reply;
                }
            }
        }

        /// <summary>Writes one message as a line of UTF-8 JSON on the pipe.</summary>
        private async Task SendAsync(ApiJson message, CancellationToken cancel)
        {
            NamedPipeClientStream pipe = _pipe ?? throw new InvalidOperationException("Not connected.");
            byte[] bytes = Encoding.UTF8.GetBytes(message.ToString() + "\n");
            await _writeLock.WaitAsync(cancel).ConfigureAwait(false);
            try
            {
                await pipe.WriteAsync(bytes, 0, bytes.Length, cancel).ConfigureAwait(false);
                await pipe.FlushAsync(cancel).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        // ---- Helpers ----

        /// <summary>Returns a new, unfinished signal for the pipe being ready, whose waiters carry on on their own threads.</summary>
        private static TaskCompletionSource<bool> NewReady() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Returns the parts folder of a state sent on the pipe, which lives beside the folder's theme.json.</summary>
        private string PartsFolderOf(ApiJson state) => Path.Combine(Folder, state["parts"]["folder"].AsString(ApiNames.PartsFolder));

        /// <summary>Keeps a state as the last one seen.</summary>
        private void SetState(ThemeState state)
        {
            lock (_stateLock)
            {
                _state = state;
            }
        }

        /// <summary>Whether the RetroEye that wrote a state is still running (its process is alive).</summary>
        private static bool IsWriterAlive(ThemeState state)
        {
            int process = state.Raw["retroEye"]["processId"].AsInt();
            if (process <= 0)
            {
                return false;
            }
            try
            {
                using (var found = System.Diagnostics.Process.GetProcessById(process))
                {
                    return !found.HasExited;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Alive, though not ours to look into
                return true;
            }
        }

        /// <summary>
        /// Raises an event on the client's SynchronizationContext, or here when it has none; a handler that throws there
        /// can't stop the client's loop.
        /// </summary>
        private void Raise(Action raise)
        {
            if (_context != null)
            {
                _context.Post(_ => raise(), null);
                return;
            }
            try
            {
                raise();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"RetroEye.Api: an event handler threw: {ex}");
            }
        }
    }
}
