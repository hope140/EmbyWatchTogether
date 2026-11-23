using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Moq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    [Collection(TransportTimingCollection.Name)]
    public sealed class RoomPollSchedulingTests
    {
        [Fact]
        public void BackgroundPoll_HealthyRoomCompletesAlignmentWhileOtherRoomSendIsBlocked()
        {
            var rooms = new RoomManager();
            var slow = rooms.CreateRoom("server", "", "slow", "admin", new[] { "a1", "a2" }, "a1");
            using var sending = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var player = new PlaybackFixture
            {
                ApplyCommand = command =>
                {
                    if (command.RoomId != slow.Id) return true;
                    sending.Set();
                    Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                    return true;
                }
            };
            player.SetPlaying("a1", 40);
            player.SetPlaying("a2", 10);
            using var engine = new SyncEngine(rooms, player, player, () => "server",
                pollIntervalSeconds: 0.05, notifyOnSyncActions: false);

            try
            {
                engine.Start();
                Assert.True(sending.Wait(TimeSpan.FromSeconds(3)), "The slow room did not enter the sender.");

                player.SetPlaying("b1", 40);
                player.SetPlaying("b2", 10);
                var healthy = rooms.CreateRoom("server", "", "healthy", "admin", new[] { "b1", "b2" }, "b1");
                var healthyRuntime = rooms.GetRuntime(healthy.Id);
                engine.RequestImmediatePoll();

                Assert.True(SpinWait.SpinUntil(() => healthyRuntime.State == RoomState.Watching,
                    TimeSpan.FromSeconds(4)), "The healthy room did not finish while the slow send was held.");
                Assert.False(release.IsSet);
                Assert.Single(player.Commands, command => command.RoomId == slow.Id);

                var healthyCommands = player.Commands.Where(command => command.RoomId == healthy.Id).ToArray();
                Assert.Equal(new[] { RemoteCommands.Pause, RemoteCommands.Pause, RemoteCommands.Seek,
                    RemoteCommands.Unpause, RemoteCommands.Unpause }, healthyCommands.Select(command => command.Name));
                var playback = player.GetSessionSnapshots().Where(snapshot => snapshot.UserId.StartsWith("b")).ToArray();
                Assert.Equal(2, playback.Length);
                Assert.All(playback, snapshot =>
                {
                    Assert.False(snapshot.IsPaused);
                    Assert.Equal(40 * SessionSnapshot.TicksPerSecond, snapshot.PositionTicks);
                });
            }
            finally
            {
                release.Set();
                engine.Stop();
            }
        }

        [Fact]
        public void ConfirmationTimeout_StartsAtEachDispatchWithinTheSameRoom()
        {
            var rooms = new RoomManager();
            var room = rooms.CreateRoom("server", "", "room", "admin", new[] { "a1", "a2" }, "a1");
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var pollStartedAt = now;
            string firstUser = null;
            var player = new PlaybackFixture
            {
                ApplyCommand = command =>
                {
                    if (firstUser != null) return false;
                    firstUser = command.UserId;
                    now = now.AddSeconds(4);
                    return true;
                }
            };
            player.SetPlaying("a1", 40);
            player.SetPlaying("a2", 40);
            using var engine = new SyncEngine(rooms, player, player, () => "server", () => now,
                notifyOnSyncActions: false);

            engine.PollOnce(now);
            var commands = player.Commands.ToArray();
            Assert.Equal(2, commands.Length);
            Assert.Equal(pollStartedAt, commands[0].IssuedAt);
            Assert.Equal(pollStartedAt.AddSeconds(4), commands[1].IssuedAt);
            var pending = rooms.GetRuntime(room.Id).Pending[commands[1].UserId];
            Assert.Equal(commands[1].IssuedAt, pending.IssuedAtUtc);

            now = commands[1].IssuedAt.AddSeconds(SyncConstants.PendingTimeoutSeconds).AddTicks(-1);
            engine.PollOnce(now);
            Assert.Equal(2, player.Commands.Count);

            now = now.AddTicks(1);
            engine.PollOnce(now);
            var retry = Assert.Single(player.Commands.Skip(2));
            Assert.Equal(commands[1].UserId, retry.UserId);
            Assert.Equal(RemoteCommands.Pause, retry.Name);
            Assert.Equal(now, retry.IssuedAt);
            Assert.Equal(now, rooms.GetRuntime(room.Id).Pending[retry.UserId].IssuedAtUtc);
        }

        [Fact]
        public void BuiltInSender_ConfirmationClockStartsWhenEmbyReceivesTheSendCall()
        {
            var rooms = new RoomManager();
            var room = rooms.CreateRoom("server", "", "room", "admin", new[] { "a1", "a2" }, "a1");
            var pollTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var transportTime = pollTime.AddSeconds(4);
            int callerThread = Environment.CurrentManagedThreadId;
            var player = new PlaybackFixture();
            player.SetPlaying("a1", 40);
            player.SetPlaying("a2", 40);

            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(manager => manager.SendPlaystateCommand(It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<PlaystateRequest>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            using var bridge = new SessionBridge(sessionManager.Object);
            var issuer = new SessionBridgeCommandIssuer(bridge);
            using var engine = new SyncEngine(rooms, player, issuer, () => "server",
                () => Environment.CurrentManagedThreadId == callerThread ? pollTime : transportTime,
                notifyOnSyncActions: false);

            engine.PollOnce(pollTime);

            sessionManager.Verify(manager => manager.SendPlaystateCommand(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<PlaystateRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            var pending = rooms.GetRuntime(room.Id).Pending.Values.ToArray();
            Assert.Equal(2, pending.Length);
            Assert.All(pending, command => Assert.Equal(transportTime, command.IssuedAtUtc));
        }

        [Fact]
        public void ProviderFailure_ProtectsHealthyRoomAndRevokesSlowRoomsOldObservation()
        {
            var rooms = new RoomManager();
            var slow = rooms.CreateRoom("server", "", "slow", "admin", new[] { "a1", "a2" }, "a1");
            var healthy = rooms.CreateRoom("server", "", "healthy", "admin", new[] { "b1", "b2" }, "b1");
            using var sending = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            long nowTicks = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).Ticks;
            var player = new PlaybackFixture
            {
                ApplyCommand = command =>
                {
                    if (command.RoomId == slow.Id)
                    {
                        sending.Set();
                        Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                    }
                    return true;
                }
            };
            player.SetPlaying("a1", 40);
            player.SetPlaying("a2", 10);
            player.SetPlaying("b1", 40, paused: true);
            player.SetPlaying("b2", 40, paused: true);
            using var engine = new SyncEngine(rooms, player, player, () => "server",
                () => new DateTimeOffset(Interlocked.Read(ref nowTicks), TimeSpan.Zero),
                pollIntervalSeconds: 0.05, notifyOnSyncActions: false);
            try
            {
                engine.Start();
                Assert.True(sending.Wait(TimeSpan.FromSeconds(3)));
                var healthyRuntime = rooms.GetRuntime(healthy.Id);
                Assert.True(SpinWait.SpinUntil(() => healthyRuntime.State == RoomState.Watching, TimeSpan.FromSeconds(3)));

                player.FailSnapshots = true;
                Assert.True(SpinWait.SpinUntil(() => player.FailedReads > 0, TimeSpan.FromSeconds(3)));
                Interlocked.Add(ref nowTicks, 3 * TimeSpan.TicksPerSecond);
                engine.RequestImmediatePoll();
                Assert.True(SpinWait.SpinUntil(() => healthyRuntime.SnapshotUnavailable, TimeSpan.FromSeconds(3)));
                Assert.False(release.IsSet);

                release.Set();
                var slowRuntime = rooms.GetRuntime(slow.Id);
                Assert.True(SpinWait.SpinUntil(() => slowRuntime.SnapshotUnavailable, TimeSpan.FromSeconds(3)));
                Assert.Single(player.Commands, command => command.RoomId == slow.Id);
                Assert.Empty(slowRuntime.Pending);
            }
            finally
            {
                release.Set();
                engine.Stop();
            }
        }

        [Fact]
        public void NewEngine_CanObserveAnExistingRoomOnItsFirstPoll()
        {
            var rooms = new RoomManager();
            var room = rooms.CreateRoom("server", "", "room", "admin", new[] { "a1", "a2" }, "a1");
            var player = new PlaybackFixture();
            player.SetPlaying("a1", 40, paused: true);
            player.SetPlaying("a2", 40, paused: true);
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using (var original = new SyncEngine(rooms, player, player, () => "server", () => now))
            {
                original.PollOnce(now);
                original.PollOnce(now.AddSeconds(1));
            }

            using var replacement = new SyncEngine(rooms, player, player, () => "server", () => now);
            Assert.Equal(room.Id, Assert.Single(replacement.PollOnce(now.AddSeconds(2))).RoomId);
        }

        private sealed class PlaybackFixture : ISessionSnapshotProvider, ICommandIssuer
        {
            private readonly ConcurrentDictionary<string, SessionSnapshot> _players = new();
            private static readonly SessionCapabilityReport Capabilities =
                new SessionCapabilityReport(true, new[] { RemoteCommands.Pause, RemoteCommands.Seek, RemoteCommands.Unpause });

            internal ConcurrentQueue<(string RoomId, string UserId, string Name, DateTimeOffset IssuedAt)> Commands { get; } = new();

            internal Func<(string RoomId, string UserId, string Name, DateTimeOffset IssuedAt), bool> ApplyCommand { get; set; }

            internal volatile bool FailSnapshots;
            private int _failedReads;
            internal int FailedReads => Volatile.Read(ref _failedReads);

            internal void SetPlaying(string userId, int positionSeconds, bool paused = false)
            {
                _players[userId] = Snapshot(userId, positionSeconds * SessionSnapshot.TicksPerSecond, paused);
            }

            public IReadOnlyList<SessionSnapshot> GetSessionSnapshots()
            {
                if (FailSnapshots)
                {
                    Interlocked.Increment(ref _failedReads);
                    throw new InvalidOperationException("No session observations.");
                }
                return _players.Values.ToArray();
            }

            public bool TryIssue(string roomId, string controllingUserId, string userId, SessionSnapshot snapshot,
                string command, long? positionTicks, DateTimeOffset now, out string error)
            {
                var entry = (roomId, userId, command, now);
                Commands.Enqueue(entry);
                if (ApplyCommand(entry))
                {
                    var current = _players[userId];
                    _players[userId] = Snapshot(userId, positionTicks ?? current.PositionTicks,
                        command == RemoteCommands.Pause || (command == RemoteCommands.Seek && current.IsPaused));
                }
                error = null;
                return true;
            }

            private static SessionSnapshot Snapshot(string userId, long position, bool paused) =>
                new SessionSnapshot(userId + "-session", userId, "item", "source", position,
                    100 * SessionSnapshot.TicksPerSecond, paused, 1, false, true, Capabilities);
        }
    }
}
