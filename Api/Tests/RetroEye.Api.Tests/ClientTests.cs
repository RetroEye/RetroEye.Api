using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace RetroEye.Api.Tests
{
    /// <summary>
    /// RetroEyeClient on a real pipe against a stand-in RetroEye (FakeRetroEye, speaking contract 1 as docs\API.md
    /// says): it connects and reads the state, hears a version change, sends a notification and hears its click,
    /// comes back by itself after a drop with what changed meanwhile, tells a restart from a stop, works with a 1.0
    /// RetroEye, keeps to the file when the pipe speaks another major, and without RetroEye keeps Windows 98's defaults
    /// and says so. They wait on pipes and timers, so they run on their own.
    /// </summary>
    [Collection(ClientTests.RunAlone)]
    public class ClientTests
    {
        /// <summary>The collection these tests run in, by themselves.</summary>
        public const string RunAlone = "Client on a pipe, run alone";

        /// <summary>A client for a stand-in RetroEye's folder and channel.</summary>
        private static RetroEyeClient ClientFor(FakeRetroEye retroEye, string name, int retryMs = 300) =>
            new RetroEyeClient(name, new RetroEyeClientOptions { Folder = retroEye.Folder, Channel = retroEye.Channel, RetryMs = retryMs });

        /// <summary>
        /// The client finds RetroEye from theme.json before connecting, connects, reads the state, hears a version change
        /// pushed, sends a notification that is queued and hears its click, and is told when RetroEye shuts down.
        /// </summary>
        [Fact]
        public void TheClientTalksToRetroEye()
        {
            using var retroEye = new FakeRetroEye(TestFiles.ScratchFolder(), TestFiles.Channel(), "98");
            using var client = ClientFor(retroEye, "Pipe test");
            var changed = new ManualResetEventSlim();
            var clicked = new ManualResetEventSlim();
            var stopped = new ManualResetEventSlim();
            string clickedTag = null;
            client.StateChanged += (s, e) =>
            {
                if (e.Name == "versionChanged" && client.State.WindowsVersion == "xp") changed.Set();
            };
            client.NotificationClicked += (s, e) => { clickedTag = e.Tag; clicked.Set(); };
            client.Stopped += (s, e) => stopped.Set();
            Assert.True(client.IsRunning);
            Assert.False(client.IsConnected);
            client.Start();
            Assert.True(client.WaitForConnectionAsync(5000).Result, "never connected " + string.Join("; ", retroEye.Errors));
            Assert.Equal("98", client.State.WindowsVersion);
            Assert.True(client.Supports("notify"));
            Assert.Contains("events.restarting", client.ServerCapabilities);
            Assert.True(SpinWait.SpinUntil(() => retroEye.Subscribed == 1, 5000), "never subscribed");

            retroEye.Show("xp-blue");
            Assert.True(changed.Wait(5000), "no versionChanged");
            Assert.Equal("xp", client.State.WindowsVersion);
            Assert.Equal("xp", client.GetStateAsync().Result.WindowsVersion);

            NotifyResult result = client.TryNotifyAsync(new Notification { Title = "Backup done", Text = "All files copied.", Tag = "b1" }).Result;
            Assert.True(result.Queued, result.ToString());
            Assert.Equal("n-1", result.Id);
            Assert.Equal("Backup done", retroEye.Notified[0].Title);
            retroEye.Click(result.Id);
            Assert.True(clicked.Wait(5000), "no click");
            Assert.Equal("b1", clickedTag);

            retroEye.Close();
            Assert.True(stopped.Wait(5000), "never told of the shutdown");
            Assert.False(client.IsRunning);
            Assert.False(RetroEyeClient.ReadFile(retroEye.Folder).Running);
            Assert.Equal(NotifyStatus.NotRunning, client.TryNotifyAsync(new Notification { Title = "Gone" }).Result.Status);
        }

        /// <summary>A notification sent the moment the client starts waits for RetroEye's welcome rather than failing.</summary>
        [Fact]
        public void ANotificationSentAtOnceWaitsForTheWelcome()
        {
            using var retroEye = new FakeRetroEye(TestFiles.ScratchFolder(), TestFiles.Channel(), "7");
            using var client = ClientFor(retroEye, "At once");
            client.Start();
            NotifyResult result = client.TryNotifyAsync(new Notification { Title = "Hello", Text = "Straight away." }).Result;
            Assert.True(result.Queued, result.ToString());
            Assert.True(client.IsConnected);
            Assert.Single(retroEye.Notified);
        }

        /// <summary>
        /// The pipe drops without a word while RetroEye keeps running, and the version is switched meanwhile: the client
        /// isn't told RetroEye stopped, comes back by itself, hears the change, and can send again.
        /// </summary>
        [Fact]
        public void ADroppedPipeComesBackWithWhatChanged()
        {
            using var retroEye = new FakeRetroEye(TestFiles.ScratchFolder(), TestFiles.Channel(), "95");
            using var client = ClientFor(retroEye, "Dropped", 400);
            var stopped = new ManualResetEventSlim();
            var back = new ManualResetEventSlim();
            var lost = new ManualResetEventSlim();
            var version = new ManualResetEventSlim();
            client.Stopped += (s, e) => stopped.Set();
            client.Reconnected += (s, e) => back.Set();
            client.Disconnected += (s, e) => lost.Set();
            client.StateChanged += (s, e) =>
            {
                if (e.Name == "versionChanged" && client.State.WindowsVersion == "2000") version.Set();
            };
            client.Start();
            Assert.True(client.WaitForConnectionAsync(5000).Result, "never connected " + string.Join("; ", retroEye.Errors));

            retroEye.DropClients();
            Assert.True(lost.Wait(5000), "the drop wasn't noticed");
            retroEye.Show("2000");
            Assert.True(back.Wait(10000), "never came back");
            Assert.True(version.Wait(5000), "the version change made while away was never told");
            Assert.False(stopped.IsSet, "a drop was taken for RetroEye stopping");
            Assert.True(client.IsRunning);
            Assert.True(client.TryNotifyAsync(new Notification { Title = "Still here" }).Result.Queued);
        }

        /// <summary>
        /// RetroEye's Restart (contract 1.1): "restarting" then "shuttingDown", theme.json saying restarting; the client
        /// waits rather than calling it stopped, then hears the new copy start and is back on it.
        /// </summary>
        [Fact]
        public void ARestartIsNotAStop()
        {
            string folder = TestFiles.ScratchFolder(), channel = TestFiles.Channel();
            var first = new FakeRetroEye(folder, channel, "98");
            using var client = new RetroEyeClient("Restart", new RetroEyeClientOptions { Folder = folder, Channel = channel, RetryMs = 300 });
            var restarting = new ManualResetEventSlim();
            var started = new ManualResetEventSlim();
            var back = new ManualResetEventSlim();
            var stopped = new ManualResetEventSlim();
            client.Restarting += (s, e) => restarting.Set();
            client.Started += (s, e) => started.Set();
            client.Reconnected += (s, e) => back.Set();
            client.Stopped += (s, e) => stopped.Set();
            client.Start();
            Assert.True(client.WaitForConnectionAsync(5000).Result, "never connected " + string.Join("; ", first.Errors));

            first.Close(restarting: true);
            Assert.True(restarting.Wait(5000), "no restarting");
            Assert.True(RetroEyeClient.ReadFile(folder).Restarting);
            Thread.Sleep(700);
            Assert.True(client.IsRestarting);
            Assert.True(client.IsRunning);

            using var second = new FakeRetroEye(folder, channel, "98");
            Assert.True(started.Wait(10000), "the new copy wasn't found");
            Assert.True(back.Wait(10000), "never came back");
            Assert.False(stopped.IsSet, "a restart was taken for a stop");
            Assert.False(client.IsRestarting);
            Assert.Equal(second.Instance, client.State.Instance);
            Assert.True(client.TryNotifyAsync(new Notification { Title = "Welcome back" }).Result.Queued);
            first.Dispose();
        }

        /// <summary>
        /// A 1.1 client with a 1.0 RetroEye (no "restarting", minor 0): it connects, reads, notifies, and a shutdown is
        /// a stop, as it always was.
        /// </summary>
        [Fact]
        public void ANewerClientWorksWithAnOlderRetroEye()
        {
            using var retroEye = new FakeRetroEye(TestFiles.ScratchFolder(), TestFiles.Channel(), "xp-olive", minor: 0);
            using var client = ClientFor(retroEye, "Older server");
            var stopped = new ManualResetEventSlim();
            var restarting = new ManualResetEventSlim();
            client.Stopped += (s, e) => stopped.Set();
            client.Restarting += (s, e) => restarting.Set();
            client.Start();
            Assert.True(client.WaitForConnectionAsync(5000).Result, "never connected " + string.Join("; ", retroEye.Errors));
            Assert.Equal(0, client.State.ContractMinor);
            Assert.False(client.Supports("events.restarting"));
            Assert.True(client.Supports("notify"));
            Assert.Equal("lunaOliveGreen", client.State.LookId);
            Assert.True(client.TryNotifyAsync(new Notification { Title = "Hi" }).Result.Queued);
            retroEye.Close(restarting: true);
            Assert.True(stopped.Wait(5000), "never told of the shutdown");
            Assert.False(restarting.IsSet);
        }

        /// <summary>A RetroEye whose pipe doesn't speak contract 1 refuses the client plainly; the client keeps to theme.json and its event.</summary>
        [Fact]
        public void AnotherMajorOnThePipeFallsBackOnTheFile()
        {
            using var retroEye = new FakeRetroEye(TestFiles.ScratchFolder(), TestFiles.Channel(), "me", speaksContract1: false);
            using var client = ClientFor(retroEye, "Major one", 200);
            var changed = new ManualResetEventSlim();
            client.StateChanged += (s, e) =>
            {
                if (client.State.WindowsVersion == "vista") changed.Set();
            };
            client.Start();
            Assert.True(SpinWait.SpinUntil(() => client.Refused != null, 5000), "never refused");
            Assert.False(client.IsConnected);
            Assert.True(client.IsRunning);
            Assert.Equal("me", client.State.WindowsVersion);
            retroEye.Show("vista-aero");
            Assert.True(changed.Wait(5000), "the file's change was never read");
        }

        /// <summary>Without RetroEye the client says it isn't running, keeps Windows 98's defaults, and a notification says NotRunning at once.</summary>
        [Fact]
        public void WithoutRetroEyeTheClientKeepsDefaults()
        {
            using var client = new RetroEyeClient("Lonely", new RetroEyeClientOptions { Folder = TestFiles.ScratchFolder(), Channel = TestFiles.Channel() });
            client.Start();
            Thread.Sleep(300);
            Assert.False(client.IsRunning);
            Assert.False(client.IsConnected);
            Assert.Equal("98", client.State.WindowsVersion);
            var started = DateTime.UtcNow;
            Assert.Equal(NotifyStatus.NotRunning, client.TryNotifyAsync(new Notification { Title = "Anyone?" }).Result.Status);
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
            Assert.Null(client.NotifyAsync(new Notification { Title = "Hi" }).Result);
        }
    }

    /// <summary>The client tests run by themselves, never beside other test classes.</summary>
    [CollectionDefinition(ClientTests.RunAlone, DisableParallelization = true)]
    public sealed class ClientTestsCollection
    {
    }
}
