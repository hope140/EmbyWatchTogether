using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Moq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class SyncEnginePerformanceTests
    {
        [Theory]
        [InlineData(false, 3, 4)]
        [InlineData(true, 4, 5)]
        public void Barrier_AdvancesSatisfiedStages_AndConfirmsEachNewCommand(bool needsSeek, int polls, int commands)
        {
            using var h = new Harness();
            h.Set(false, false, 40, needsSeek ? 10 : 40);
            Assert.Equal(RoomState.Barrier, h.Poll());
            Assert.Equal(2, h.Commands.Count);
            Assert.Equal(BarrierStage.Pause, h.Runtime.Barrier.Stage);
            h.Set(true, true, 40, needsSeek ? 10 : 40);
            Assert.Equal(RoomState.Barrier, h.Poll());
            Assert.Equal(needsSeek ? BarrierStage.Seek : BarrierStage.Restore, h.Runtime.Barrier.Stage);
            Assert.Equal(needsSeek ? 3 : 4, h.Commands.Count);
            if (needsSeek)
            {
                h.Set(true, true, 40, 40);
                Assert.Equal(RoomState.Barrier, h.Poll());
                Assert.Equal(BarrierStage.Restore, h.Runtime.Barrier.Stage);
            }
            h.Set(false, false, 40, 40);
            Assert.Equal(RoomState.Watching, h.Poll());
            Assert.Equal(polls, h.Polls);
            Assert.Equal(commands, h.Commands.Count);
        }

        [Fact]
        public void Barrier_AlreadyPausedAndAligned_EntersWatchingInOnePollWithoutCommands()
        {
            using var h = new Harness();
            h.Set(true, true, 40, 40);
            Assert.Equal(RoomState.Watching, h.Poll());
            Assert.Empty(h.Commands);
        }

        [Fact]
        public void Barrier_UnacknowledgedPauseOrSeek_CannotRestore()
        {
            using var h = new Harness();
            h.Set(false, false, 40, 10);
            h.Poll();
            h.Set(true, false, 40, 10);
            h.Poll();
            Assert.Equal(BarrierStage.Pause, h.Runtime.Barrier.Stage);
            Assert.DoesNotContain(h.Commands, c => c.command == RemoteCommands.Seek);
            h.Set(true, true, 40, 10);
            h.Poll();
            Assert.Equal(BarrierStage.Seek, h.Runtime.Barrier.Stage);
            h.Poll();
            Assert.Equal(BarrierStage.Seek, h.Runtime.Barrier.Stage);
            Assert.DoesNotContain(h.Commands, c => c.command == RemoteCommands.Unpause);
            Assert.Equal(40 * SessionSnapshot.TicksPerSecond, h.Runtime.Pending["u2"].PositionTicks);
        }

        [Fact]
        public void ManualSeek_IssuesPauseInDetectionPoll_ThenWaitsForNewSnapshot()
        {
            using var h = new Harness();
            h.EnterWatching();
            h.Set(false, false, 80, 41);
            Assert.Equal(RoomState.Barrier, h.Poll());
            Assert.Equal(2, h.Commands.Count);
            Assert.All(h.Commands, c => Assert.Equal(RemoteCommands.Pause, c.command));
            Assert.Equal(BarrierStage.Pause, h.Runtime.Barrier.Stage);
            Assert.Equal(2, h.Runtime.Pending.Count);
        }

        [Fact]
        public async Task BlockedLegacyNotice_DoesNotBlockPollOrFollowingPlaybackCommand()
        {
            using var h = new Harness();
            h.EnterWatching();
            h.EnableNotifications();
            h.Enqueue("u1");
            await h.Messages.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                h.Set(true, false, 40, 40);
                Assert.Equal(RoomState.Watching, await Task.Run(h.Poll).WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Contains(h.Commands, c => c.user == "u2" && c.command == RemoteCommands.Pause);
                Assert.Single(h.Messages.Calls);
            }
            finally { h.Messages.Release.Set(); }
            await h.Idle.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, h.Messages.Calls.Count);
        }

        [Theory]
        [InlineData("session")]
        [InlineData("item")]
        [InlineData("member")]
        [InlineData("server")]
        [InlineData("snapshot_failure")]
        [InlineData("snapshot_protection")]
        [InlineData("setting")]
        [InlineData("capability")]
        [InlineData("offline")]
        [InlineData("deleted_room")]
        public async Task QueuedNotice_RevalidatesCurrentTargetBeforeSending(string invalidation)
        {
            using var h = new Harness();
            h.EnterWatching();
            h.EnableNotifications();
            h.Enqueue("u1");
            await h.Messages.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                h.Enqueue("u2");
                switch (invalidation)
                {
                    case "session": h.ReplacePeer(session: "replacement"); break;
                    case "item": h.ReplacePeer(item: "other"); break;
                    case "member": h.Rooms.LeaveParticipant(h.Room.Id, "u2"); break;
                    case "server": h.Server = "other"; break;
                    case "snapshot_failure": h.Provider.Throw = true; break;
                    case "snapshot_protection":
                        typeof(RoomRuntime).GetMethod("EnterSnapshotUnavailableProtection",
                            BindingFlags.Instance | BindingFlags.NonPublic).Invoke(h.Runtime, null); break;
                    case "setting": h.EnableNotifications(false); break;
                    case "capability": h.ReplacePeer(display: false); break;
                    case "offline": h.ReplacePeer(stopped: true); break;
                    case "deleted_room": h.Rooms.DeleteRoom(h.Room.Id); break;
                }
                h.Messages.Release.Set();
                await h.Idle.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Single(h.Messages.Calls);
            }
            finally { h.Messages.Release.Set(); }
        }

        [Fact]
        public async Task QueuedNotice_FakeWallClockJumpDoesNotExpireFreshQueueEntry()
        {
            using var h = new Harness();
            h.EnterWatching();
            h.EnableNotifications();
            h.Enqueue("u1");
            await h.Messages.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                h.Enqueue("u2");
                h.Now = h.Now.AddHours(5);
                h.Set(false, false, 40, 40);
                h.Messages.Release.Set();
                await h.Idle.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(2, h.Messages.Calls.Count);
            }
            finally { h.Messages.Release.Set(); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task DequeuedNotice_WaitingForRoomGate_RechecksMonotonicAgeAndCurrentClock(bool expireSnapshot)
        {
            using var h = new Harness();
            h.EnterWatching();
            h.EnableNotifications();
            double monotonic = 0;
            h.SetMonotonic(() => Volatile.Read(ref monotonic));
            using var sampled = new ManualResetEventSlim();
            h.Provider.Sampled = sampled.Set;
            var gate = (IDisposable)typeof(RoomManager).GetMethod("TryEnterRoom",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(h.Rooms, new object[] { h.Room.Id });
            try
            {
                h.Enqueue("u2");
                Assert.True(sampled.Wait(TimeSpan.FromSeconds(5)), "Worker did not sample candidates");
                // The worker has left the queue and is waiting for this gate.
                if (expireSnapshot) h.Now = h.Now.AddSeconds(61);
                else Volatile.Write(ref monotonic, 10.001);
            }
            finally { gate.Dispose(); }
            await h.Idle.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(h.Messages.Calls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task EngineStopOrDispose_CancelsBuiltInMessageTransport(bool dispose)
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var manager = new Mock<ISessionManager>();
            manager.Setup(m => m.SendMessageCommand(It.IsAny<string>(), "s1", It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>()))
                .Returns<string, string, MessageCommand, CancellationToken>((_, session, message, token) =>
                {
                    token.Register(() => cancelled.TrySetResult(true));
                    entered.TrySetResult(true);
                    return Task.Delay(Timeout.Infinite, token);
                });
            using var bridge = new SessionBridge(manager.Object);
            using var h = new Harness(new SessionBridgeCommandIssuer(bridge));
            h.EnterWatching();
            h.EnableNotifications();
            h.Enqueue("u1");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (dispose) h.Engine.Dispose(); else h.Engine.Stop();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await h.Idle.WaitAsync(TimeSpan.FromSeconds(5));
            manager.Verify(m => m.SendMessageCommand(It.IsAny<string>(), "s1", It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task EngineStopOrDispose_DoesNotWaitForLegacyNotice_AndClearsQueuedTargets(bool dispose)
        {
            using var h = new Harness();
            h.EnterWatching();
            h.EnableNotifications();
            h.Enqueue("u1");
            await h.Messages.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                h.Enqueue("u2");
                await Task.Run(() => { if (dispose) h.Engine.Dispose(); else h.Engine.Stop(); })
                    .WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(h.Idle.IsCompleted);
                h.Enqueue("u2");
                h.Messages.Release.Set();
                await h.Idle.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Single(h.Messages.Calls);
            }
            finally { h.Messages.Release.Set(); }
        }

        private sealed class Harness : IDisposable, ICommandIssuer
        {
            internal readonly RoomManager Rooms = new RoomManager();
            internal readonly ProviderFake Provider = new ProviderFake();
            internal readonly BlockingMessages Messages = new BlockingMessages();
            internal readonly List<(string user, string command)> Commands = new List<(string, string)>();
            internal readonly Room Room;
            internal readonly SyncEngine Engine;
            internal string Server = "server";
            internal DateTimeOffset Now = DateTimeOffset.UtcNow;
            internal int Polls;
            internal RoomRuntime Runtime => Rooms.GetRuntime(Room.Id);
            internal Task Idle => (Task)typeof(SyncEngine).GetProperty("NotificationsIdle",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Engine);
            internal Harness(IMessageIssuer messageIssuer = null)
            {
                Room = Rooms.CreateRoom("server", "http://emby", "room", "admin", new[] { "u1", "u2" }, "u1");
                Engine = new SyncEngine(Rooms, Provider, this, () => Server, () => Now,
                    messageIssuer: messageIssuer ?? Messages, notifyOnSyncActions: false);
            }
            internal void EnableNotifications(bool enabled = true) => Engine.UpdateOptions(new SyncEngineOptions(1, true, true, enabled));
            internal void SetMonotonic(Func<double> monotonic)
            {
                var dispatcher = typeof(SyncEngine).GetField("_notifications", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(Engine);
                dispatcher.GetType().GetField("_elapsedSeconds", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(dispatcher, monotonic);
            }
            internal SessionSnapshot Snapshot(string user, bool paused, int seconds, string session = null,
                string item = "item", bool display = true, bool stopped = false) =>
                new SessionSnapshot(session ?? (user == "u1" ? "s1" : "s2"), user, item, "source",
                    seconds * SessionSnapshot.TicksPerSecond, 100 * SessionSnapshot.TicksPerSecond,
                    paused, 1, stopped, true, new SessionCapabilityReport(true, display
                        ? new[] { "Pause", "Unpause", "Seek", "DisplayMessage" }
                        : new[] { "Pause", "Unpause", "Seek" }), Now);
            internal void Set(bool firstPaused, bool secondPaused, int first, int second) => Provider.Snapshots =
                new[] { Snapshot("u1", firstPaused, first), Snapshot("u2", secondPaused, second) };
            internal void ReplacePeer(string session = "s2", string item = "item", bool display = true, bool stopped = false) =>
                Provider.Snapshots = new[] { Provider.Snapshots[0], Snapshot("u2", false, 40, session, item, display, stopped) };
            internal RoomState Poll()
            {
                Polls++;
                var result = Engine.PollOnce(Now).Single().State;
                Now = Now.AddSeconds(1);
                return result;
            }
            internal void EnterWatching()
            {
                Set(false, false, 40, 40); Poll();
                Set(true, true, 40, 40); Poll();
                Set(false, false, 40, 40); Assert.Equal(RoomState.Watching, Poll());
                Commands.Clear();
            }
            internal void Enqueue(string user) => typeof(SyncEngine).GetMethod("QueueNotification",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Engine,
                    new object[] { Room, user, Provider.Snapshots.Single(s => s.UserId == user), "header", "text", false });
            public bool TryIssue(string roomId, string controllingUserId, string userId, SessionSnapshot snapshot,
                string command, long? positionTicks, DateTimeOffset now, out string error)
            { Commands.Add((userId, command)); error = null; return true; }
            public void Dispose() { Messages.Release.Set(); Engine.Dispose(); }
        }

        private sealed class ProviderFake : ISessionSnapshotProvider
        {
            internal IReadOnlyList<SessionSnapshot> Snapshots = Array.Empty<SessionSnapshot>();
            internal bool Throw;
            internal Action Sampled;
            public IReadOnlyList<SessionSnapshot> GetSessionSnapshots()
            {
                if (Throw) throw new InvalidOperationException("snapshot failed");
                var candidates = Snapshots;
                Sampled?.Invoke();
                return candidates;
            }
        }

        private sealed class BlockingMessages : IMessageIssuer
        {
            internal readonly TaskCompletionSource<bool> Entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly ManualResetEventSlim Release = new ManualResetEventSlim();
            internal readonly ConcurrentQueue<string> Calls = new ConcurrentQueue<string>();
            public bool TryIssueMessage(string roomId, string controllingUserId, string userId, SessionSnapshot snapshot,
                string header, string text, int? timeoutMs, DateTimeOffset now, out string error)
            {
                Calls.Enqueue(userId);
                Entered.TrySetResult(true);
                Release.Wait();
                error = null;
                return true;
            }
        }
    }
}
