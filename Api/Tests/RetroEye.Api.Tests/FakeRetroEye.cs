using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RetroEye.Api.Tests
{
    /// <summary>
    /// A stand-in for RetroEye's side of contract 1 (docs\API.md), so the client can be tested on a real pipe without
    /// RetroEye: it writes theme.json (a recorded state, as this process), sets the change event, listens on the pipe
    /// of a channel of the test's own, answers hello/get/subscribe/notify/ping/bye as the spec says, pushes events,
    /// and can drop its clients, restart and shut down. It can also play an older RetroEye (contract 1.0, without
    /// "restarting"), or one that doesn't speak contract 1 at all.
    /// </summary>
    internal sealed class FakeRetroEye : IDisposable
    {
        private readonly object _lock = new object();
        private readonly List<Client> _clients = new List<Client>();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly EventWaitHandle _changed;
        private readonly Task _listening;
        private ApiJson _state;
        private int _notifications;

        /// <summary>Starts a stand-in RetroEye showing a recorded state, on the folder and channel given.</summary>
        public FakeRetroEye(string folder, string channel, string stateName, int minor = ApiNames.ContractMinor, bool speaksContract1 = true)
        {
            Folder = folder;
            Channel = channel;
            Minor = minor;
            SpeaksContract1 = speaksContract1;
            Instance = Guid.NewGuid().ToString("N");
            _changed = new EventWaitHandle(false, EventResetMode.ManualReset, ApiNames.ChangedEventName(channel, ApiNames.UserSid()));
            _state = Stamp(TestFiles.StateJson(stateName), 1);
            WriteFile(_state);
            _listening = Task.Run(async () =>
            {
                try
                {
                    await ListenAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    lock (_lock) Errors.Add("listening: " + ex);
                }
            });
        }

        /// <summary>The folder theme.json is written to.</summary>
        public string Folder { get; }

        /// <summary>The channel the pipe and event are named with.</summary>
        public string Channel { get; }

        /// <summary>The contract minor this RetroEye speaks: 1 (with "restarting"), or 0 to play a 1.0 RetroEye.</summary>
        public int Minor { get; }

        /// <summary>Whether it speaks contract 1 on its pipe (false: it answers hello with unsupportedContract).</summary>
        public bool SpeaksContract1 { get; }

        /// <summary>This copy's instance id (new at each start).</summary>
        public string Instance { get; }

        /// <summary>The notifications queued so far: (id, title, tag), in order.</summary>
        public List<(string Id, string Title, string Tag)> Notified { get; } = new List<(string, string, string)>();

        /// <summary>What went wrong on its side, for a failing test to tell.</summary>
        public List<string> Errors { get; } = new List<string>();

        /// <summary>The clients that have said hello and subscribed.</summary>
        public int Subscribed
        {
            get
            {
                lock (_lock)
                {
                    return _clients.Count(c => c.Subscribed);
                }
            }
        }

        /// <summary>The capabilities this RetroEye names: contract 1.0's, plus events.restarting from 1.1.</summary>
        private IEnumerable<string> Capabilities => Minor >= 1 ? ApiNames.Capabilities : ApiNames.Capabilities.Where(c => c != "events.restarting");

        /// <summary>Shows another recorded state: theme.json rewritten, the event set, and versionChanged (if the version or look changed) then themeChanged pushed.</summary>
        public void Show(string stateName)
        {
            ApiJson before = _state;
            ApiJson after = Stamp(TestFiles.StateJson(stateName), before["sequence"].AsInt() + 1);
            _state = after;
            WriteFile(after);
            bool version = before["windows"]["version"].AsString() != after["windows"]["version"].AsString() || before["look"]["id"].AsString() != after["look"]["id"].AsString();
            if (version)
            {
                Push("versionChanged", after);
            }
            Push("themeChanged", after);
            Pulse();
        }

        /// <summary>Says a queued notification was clicked and closed, to the client that sent it.</summary>
        public void Click(string id)
        {
            var (_, _, tag) = Notified.Single(n => n.Id == id);
            Push("notificationClicked", null, m => m.Set("notification", id).Set("tag", tag));
            Push("notificationClosed", null, m => m.Set("notification", id).Set("tag", tag).Set("reason", "clicked"));
        }

        /// <summary>Drops every client's pipe without a word (RetroEye keeps running).</summary>
        public void DropClients()
        {
            lock (_lock)
            {
                foreach (Client client in _clients)
                {
                    client.Pipe.Dispose();
                }
                _clients.Clear();
            }
        }

        /// <summary>
        /// Closes as RetroEye does: "restarting" first when it is restarting (contract 1.1), then "shuttingDown"; theme.json
        /// says running false (and restarting); the pipe stops listening.
        /// </summary>
        public void Close(bool restarting = false)
        {
            if (_stop.IsCancellationRequested)
            {
                return;
            }
            if (restarting && Minor >= 1)
            {
                Push("restarting");
            }
            Push("shuttingDown");
            ApiJson last = Stamp(_state, _state["sequence"].AsInt() + 1).Set("running", false);
            if (restarting && Minor >= 1)
            {
                last.Set("restarting", true);
            }
            WriteFile(last);
            Pulse();
            _stop.Cancel();
            DropClients();
            try
            {
                _listening.Wait(2000);
            }
            catch (AggregateException)
            {
            }
        }

        /// <summary>Shuts down (no restart).</summary>
        public void Dispose()
        {
            Close();
            _changed.Dispose();
        }

        // ---- The pipe ----

        /// <summary>One client's pipe and what it asked for.</summary>
        private sealed class Client
        {
            public NamedPipeServerStream Pipe;
            public bool Greeted;
            public bool Subscribed;
            public readonly SemaphoreSlim Writing = new SemaphoreSlim(1, 1);
        }

        /// <summary>Waits for clients, one pipe instance at a time, until stopped.</summary>
        private async Task ListenAsync()
        {
            string name = ApiNames.PipeName(Channel, ApiNames.SessionId(), ApiNames.UserSid());
            while (!_stop.IsCancellationRequested)
            {
                // Buffers as RetroEye's own: with none, its welcome and the client's subscribe would each wait for the other to read
                var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, ApiNames.MaxClients, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 16 * 1024, 64 * 1024);
                try
                {
                    await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
                {
                    pipe.Dispose();
                    continue;
                }
                var client = new Client { Pipe = pipe };
                lock (_lock)
                {
                    _clients.Add(client);
                }
                _ = Task.Run(() => TalkAsync(client));
            }
        }

        /// <summary>Reads a client's lines and answers them, until its pipe closes.</summary>
        private async Task TalkAsync(Client client)
        {
            var reader = new ApiLineReader(client.Pipe, ApiNames.MaxClientLine);
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    string line = await reader.ReadLineAsync(_stop.Token).ConfigureAwait(false);
                    if (line == null || !await HandleAsync(client, line).ConfigureAwait(false))
                    {
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                lock (_lock) Errors.Add("talking: " + ex);
            }
            lock (_lock)
            {
                _clients.Remove(client);
            }
            client.Pipe.Dispose();
        }

        /// <summary>Answers one line; false ends the conversation.</summary>
        private async Task<bool> HandleAsync(Client client, string line)
        {
            if (!ApiJson.TryParse(line, out ApiJson message) || message.Kind != ApiJsonKind.Object)
            {
                await SendAsync(client, Reply("error", ApiJson.Missing).Set("code", "badJson")).ConfigureAwait(false);
                return true;
            }
            ApiJson id = message["id"];
            string type = message["type"].AsString();
            if (type != "hello" && !client.Greeted)
            {
                await SendAsync(client, Reply("error", id).Set("code", "helloFirst")).ConfigureAwait(false);
                return true;
            }
            switch (type)
            {
                case "hello":
                    bool asksOne = message["contracts"].Items.Any(m => m.AsInt() == 1) || message["contract"].AsInt() == 1;
                    if (!SpeaksContract1 || !asksOne)
                    {
                        await SendAsync(client, Reply("error", id).Set("code", "unsupportedContract").Set("message", "This pipe speaks contract 2.")
                            .Set("supported", ApiJson.NewArray().Add(ApiJson.From(2)))).ConfigureAwait(false);
                        return true;
                    }
                    client.Greeted = true;
                    var capabilities = ApiJson.NewArray();
                    foreach (string capability in Capabilities)
                    {
                        capabilities.Add(ApiJson.From(capability));
                    }
                    await SendAsync(client, Reply("welcome", id).Set("server", "RetroEye")
                        .Set("contract", ApiJson.NewObject().Set("major", 1).Set("minor", Minor).Set("version", "1." + Minor))
                        .Set("capabilities", capabilities).Set("deprecated", ApiJson.NewArray()).Set("retroEyeVersion", "1.0.0.8")
                        .Set("instance", Instance).Set("folder", Folder)).ConfigureAwait(false);
                    await SendAsync(client, Reply("state", id).Set("state", _state)).ConfigureAwait(false);
                    return true;
                case "get":
                    await SendAsync(client, Reply("state", id).Set("state", _state)).ConfigureAwait(false);
                    return true;
                case "subscribe":
                    client.Subscribed = true;
                    await SendAsync(client, Reply("subscribed", id)).ConfigureAwait(false);
                    string known = message["knownInstance"].AsString();
                    if (known.Length > 0 && known != Instance)
                    {
                        await SendAsync(client, Event("started").Set("instance", Instance)).ConfigureAwait(false);
                    }
                    return true;
                case "notify":
                    string queued = "n-" + Interlocked.Increment(ref _notifications);
                    lock (_lock)
                    {
                        Notified.Add((queued, message["title"].AsString(), message["tag"].AsStringOrNull()));
                    }
                    await SendAsync(client, Reply("queued", id).Set("notification", queued)).ConfigureAwait(false);
                    return true;
                case "ping":
                    await SendAsync(client, Reply("pong", id)).ConfigureAwait(false);
                    return true;
                case "bye":
                    await SendAsync(client, Reply("bye", id)).ConfigureAwait(false);
                    return false;
                default:
                    await SendAsync(client, Reply("error", id).Set("code", "unknownType")).ConfigureAwait(false);
                    return true;
            }
        }

        /// <summary>Pushes an event to every subscribed client.</summary>
        private void Push(string name, ApiJson state = null, Action<ApiJson> more = null)
        {
            List<Client> clients;
            lock (_lock)
            {
                clients = _clients.Where(c => c.Subscribed).ToList();
            }
            foreach (Client client in clients)
            {
                ApiJson message = Event(name);
                if (state != null)
                {
                    message.Set("state", state);
                }
                more?.Invoke(message);
                try
                {
                    SendAsync(client, message).Wait();
                }
                catch (AggregateException)
                {
                }
            }
        }

        /// <summary>Writes one message as a line.</summary>
        private static async Task SendAsync(Client client, ApiJson message)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message.ToString() + "\n");
            await client.Writing.WaitAsync().ConfigureAwait(false);
            try
            {
                await client.Pipe.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                await client.Pipe.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
            }
            finally
            {
                client.Writing.Release();
            }
        }

        // ---- The file and event ----

        /// <summary>A recorded state made this copy's: its instance, this process, the sequence given, the contract minor it speaks.</summary>
        private ApiJson Stamp(ApiJson state, int sequence)
        {
            ApiJson copy = ApiJson.Parse(state.ToString());
            copy.Set("instance", Instance).Set("sequence", sequence).Set("running", true).Set("written", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
            copy["retroEye"].Set("processId", Environment.ProcessId);
            copy.Set("contract", ApiJson.NewObject().Set("major", 1).Set("minor", Minor).Set("version", "1." + Minor));
            var capabilities = ApiJson.NewArray();
            foreach (string capability in Capabilities)
            {
                capabilities.Add(ApiJson.From(capability));
            }
            copy.Set("capabilities", capabilities);
            return copy;
        }

        /// <summary>Writes theme.json whole (a temporary file, then a swap), as RetroEye does.</summary>
        private void WriteFile(ApiJson state)
        {
            string path = Path.Combine(Folder, ApiNames.ThemeFile), temporary = path + ".tmp";
            File.WriteAllText(temporary, state.ToIndentedString(), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Replace(temporary, path, null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }

        /// <summary>Sets the change event for half a second, as RetroEye does.</summary>
        private void Pulse()
        {
            _changed.Set();
            Task.Delay(ApiNames.EventPulse).ContinueWith(_ =>
            {
                try
                {
                    _changed.Reset();
                }
                catch (ObjectDisposedException)
                {
                }
            });
        }

        /// <summary>An answer to a request (carrying its id back as "re").</summary>
        private static ApiJson Reply(string type, ApiJson id)
        {
            ApiJson reply = ApiJson.NewObject().Set("type", type);
            if (!id.IsNull)
            {
                reply.Set("re", id);
            }
            return reply;
        }

        /// <summary>An event message.</summary>
        private static ApiJson Event(string name) => ApiJson.NewObject().Set("type", "event").Set("event", name);
    }
}
