using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Emby.Plugins.WatchTogether.Tests
{
    public partial class SyncEnginePerformanceTests
    {
        [Fact]
        public async Task Toast_PauseDragResume_ReportsOnlyCompletion()
        {
            var messages = new ToastRecorder();
            using var h = new Harness(messages);
            h.EnterWatching(); await h.Idle; h.EnableNotifications();
            h.Set(true, false, 40, 41); h.Poll(); await h.Idle;
            h.Set(true, true, 40, 40); h.Poll(); await h.Idle;
            h.Set(true, true, 80, 40); h.Poll(); await h.Idle;
            h.Set(true, true, 80, 80); h.Poll(); await h.Idle;
            Assert.Equal(RoomState.Watching, h.Runtime.State);
            h.Set(false, true, 80, 80); h.Poll(); await h.Idle;
            h.Set(false, false, 81, 81); h.Poll(); await h.Idle;
            Console.WriteLine("TOAST_PROBE pause-drag-resume " + string.Join(" | ",
                messages.Calls.Select(c => c.user + ":" + c.text)));
            Assert.Equal(1, messages.Calls.Count(c => c.user == "u2"));
            Assert.Equal(2, messages.Calls.Count);
        }
        private sealed class ToastRecorder : IMessageIssuer
        {
            internal readonly ConcurrentQueue<(string user, string text)> Calls = new();
            public bool TryIssueMessage(string roomId, string controllingUserId, string userId,
                SessionSnapshot snapshot, string header, string text, int? timeoutMs,
                DateTimeOffset now, out string error)
            { Calls.Enqueue((userId, text)); error = null; return true; }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Toast_OneSeek_FreshTimestampInputRetainsControlSemantics(bool replay)
        {
            var messages = new ToastRecorder();
            using var h = new Harness(messages);
            h.EnterWatching();
            await h.Idle;
            h.EnableNotifications();
            h.Set(false, false, 80, 41); h.Poll(); await h.Idle;
            // Duplicate poll wakeups with unchanged snapshots and time.
            for (int n = 0; n < 5; n++) h.Engine.PollOnce(h.Now);
            await h.Idle;
            Assert.Empty(messages.Calls);
            h.Set(true, true, 80, 42); h.Poll(); await h.Idle;
            h.Set(true, true, 80, 80); h.Poll(); await h.Idle;
            h.Set(false, false, 80, 80); h.Poll(); await h.Idle;
            Assert.Equal(RoomState.Watching, h.Runtime.State);
            Assert.Equal(2, messages.Calls.Count);
            Assert.Equal(1, messages.Calls.Count(c => c.user == "u2"));
            if (replay)
            {
                // Fresh timestamps cannot identify a replay: preserve real user controls.
                int commandsBeforeReplay = h.Commands.Count;
                h.Set(true, true, 80, 80); h.Poll(); await h.Idle;
                h.Set(false, false, 81, 81); h.Poll(); await h.Idle;
                Assert.Equal(2, messages.Calls.Count);
                Assert.Equal(commandsBeforeReplay + 2, h.Commands.Count);
                Assert.Equal(new[] { RemoteCommands.Pause, RemoteCommands.Unpause }, h.Commands.Skip(commandsBeforeReplay).Select(c => c.command));
                Assert.Equal(1, messages.Calls.Count(c => c.user == "u2"));
            }
            Console.WriteLine("TOAST_PROBE replay=" + replay + " " +
                string.Join(" | ", messages.Calls.Select(c => c.user + ":" + c.text)));
        }

        [Fact]
        public async Task ToastProbe_DelayedSeekAcknowledgement_AddsOneRetryNoticePerRecipient()
        {
            var messages = new ToastRecorder();
            using var h = new Harness(messages);
            h.EnterWatching(); await h.Idle; h.EnableNotifications();
            h.Set(false, false, 80, 41); h.Poll(); await h.Idle;
            h.Set(true, true, 80, 42); h.Poll(); await h.Idle;
            for (int n = 0; n < 12; n++)
            {
                h.Set(true, true, 80, 42); h.Poll(); await h.Idle;
            }
            Console.WriteLine("TOAST_PROBE delayedSeek " +
                string.Join(" | ", messages.Calls.Select(c => c.user + ":" + c.text)));
            Assert.Equal(2, messages.Calls.Count(c => c.text.Contains("自动重新同步")));
            Assert.Equal(1, messages.Calls.Count(c => c.user == "u2" && c.text.Contains("自动重新同步")));
        }
    }
}
