using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    [Collection(TransportTimingCollection.Name)]
    public class StoppedPositionRecoveryTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LargeGapAfterStop_ExplainsConfirmationWithoutMovingEitherTimeline(bool paused)
        {
            using var f = new Replay(paused);
            for (var i = 0; i < 20; i++) f.Tick();
            Assert.Equal(RoomState.Waiting, f.Runtime.State);
            Assert.Contains("重新同步", f.Runtime.Error);
            Assert.Empty(f.Issuer.Commands);
            Assert.Empty(f.Issuer.PlayItems);
            Assert.Equal(0, f.Provider.Items["u1"].PositionTicks);
            Assert.Equal(16 * SessionSnapshot.TicksPerSecond, f.Provider.Items["u2"].PositionTicks);
            var notice = Assert.Single(SyncDiagnostics.Build(f.Room, f.Runtime, "server", "test", "waiting", f.Now).Events,
                e => e.Type == "stop_resume_confirmation_required");
            Assert.Equal(16d, notice.DriftSeconds);
            Assert.Null(notice.LatencySeconds);
            Assert.Equal("unknown", notice.Alias);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void ExplicitResync_AlignsToPrimaryAndPreservesPrimaryIntent(bool paused, bool admin)
        {
            using var f = new Replay(paused);
            if (admin)
                Assert.Null(f.Rooms.Action(f.Room.Id, "resync", f.Provider.Items, f.Issuer, f.Now).Error);
            else
                Assert.Equal("accepted", f.Rooms.RequestParticipantResync(f.Room.Id, "u2", () => "server", f.Now).Status);
            for (var i = 0; i < 14; i++) f.Tick();
            Assert.Equal(RoomState.Watching, f.Runtime.State);
            Assert.Equal(paused, f.Provider.Items["u1"].IsPaused);
            Assert.Equal(paused, f.Provider.Items["u2"].IsPaused);
            Assert.Contains(f.Issuer.Commands, c => c.user == "u2" && c.command == RemoteCommands.Seek && c.position == 0);
            Assert.DoesNotContain(f.Issuer.Commands, c => c.user == "u1" && c.command == RemoteCommands.Seek);
            Assert.Empty(f.Issuer.PlayItems);
        }

        [Fact]
        public void UserWithoutMembership_CannotAuthorizeResume()
        {
            using var f = new Replay(false);
            Assert.Throws<UnauthorizedAccessException>(() => f.Rooms.RequestParticipantResync(f.Room.Id, "stranger", () => "server", f.Now));
            f.Tick();
            Assert.Equal(RoomState.Waiting, f.Runtime.State);
            Assert.Empty(f.Issuer.Commands);
        }

        [Fact]
        public void DifferentParticipantVideo_DoesNotAutomaticallySeekOrPlayItem()
        {
            using var f = new Replay(false);
            f.Provider.Items["u2"] = f.Make("u2", true, 16, item: "other-item");
            for (var i = 0; i < 5; i++) f.Tick();
            Assert.DoesNotContain(f.Issuer.Commands, c => c.command == RemoteCommands.Seek || c.command == RemoteCommands.Unpause);
            Assert.Empty(f.Issuer.PlayItems);
        }

        [Theory]
        [InlineData("u1", "missing")]
        [InlineData("u2", "missing")]
        [InlineData("u1", "stopped")]
        [InlineData("u2", "stopped")]
        [InlineData("u1", "control")]
        [InlineData("u2", "control")]
        public void ConfirmationHint_IsWithdrawnWhenCurrentPairIsNoLongerEligible(string user, string change)
        {
            using var f = new Replay(false);
            Assert.Contains("重新同步", f.Runtime.Error);
            if (change == "missing") f.Provider.Items.Remove(user);
            else f.Provider.Items[user] = f.Make(user, true, user == "u1" ? 0 : 16,
                stopped: change == "stopped", rawControl: change != "control");
            for (var i = 0; i < 5; i++) f.Tick();
            Assert.DoesNotContain("重新同步", f.Runtime.Error ?? "");
            Assert.DoesNotContain(f.Issuer.Commands, c => c.command == RemoteCommands.Seek || c.command == RemoteCommands.Unpause);
            Assert.Empty(f.Issuer.PlayItems);
            f.Provider.Items[user] = f.Make(user, user == "u2", user == "u1" ? 0 : 16);
            f.Tick();
            Assert.Contains("重新同步", f.Runtime.Error);
            Assert.DoesNotContain(f.Issuer.Commands, c => c.command == RemoteCommands.Seek || c.command == RemoteCommands.Unpause);
            Assert.Empty(f.Issuer.PlayItems);
        }

        private sealed class Replay : IDisposable
        {
            public DateTimeOffset Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            public readonly RoomManager Rooms = new RoomManager();
            public readonly Provider Provider = new Provider();
            public readonly Recorder Issuer;
            public readonly Room Room;
            public readonly SyncEngine Engine;
            public RoomRuntime Runtime => Rooms.GetRuntime(Room.Id);
            public Replay(bool primaryPaused)
            {
                Room = Rooms.CreateRoom("server", "http://example.invalid", "offline replay", "admin", new[] { "u1", "u2" }, "u1");
                Issuer = new Recorder(this);
                Engine = new SyncEngine(Rooms, Provider, Issuer, () => "server", () => Now,
                    notifyOtherOnPlaybackStop: false, notifyOnSyncActions: false);
                Provider.Items["u1"] = Make("u1", false, 16);
                Provider.Items["u2"] = Make("u2", false, 16);
                for (var i = 0; i < 10; i++) Tick();
                Assert.Equal(RoomState.Watching, Runtime.State);
                Provider.Items["u1"] = Make("u1", false, 16, item: "", stopped: true);
                Tick();
                Now = Now.AddSeconds(2.1);
                Engine.PollOnce(Now);
                Assert.Equal(RoomState.Waiting, Runtime.State);
                Provider.Items["u1"] = Make("u1", primaryPaused, 0);
                Tick();
                Issuer.Commands.Clear();
            }
            public SessionSnapshot Make(string user, bool paused, double position, string item = "i1", bool stopped = false, bool rawControl = true) =>
                new SessionSnapshot(user == "u1" ? "s1" : "s2", user, item, "m1", (long)(position * SessionSnapshot.TicksPerSecond),
                    100 * SessionSnapshot.TicksPerSecond, paused, 1, stopped, rawControl,
                    new SessionCapabilityReport(true, new[] { "Pause", "Unpause", "Seek", "DisplayMessage" }), Now);
            public void Tick() { Now = Now.AddSeconds(.25); Engine.PollOnce(Now); }
            public void Dispose() => Engine.Dispose();
        }
        private sealed class Provider : ISessionSnapshotProvider
        {
            public readonly Dictionary<string, SessionSnapshot> Items = new Dictionary<string, SessionSnapshot>();
            public IReadOnlyList<SessionSnapshot> GetSessionSnapshots() => Items.Values.ToArray();
        }
        private sealed class Recorder : ICommandIssuer, IPlayItemIssuer
        {
            private readonly Replay _owner;
            public readonly List<(string user, string command, long? position)> Commands = new List<(string, string, long?)>();
            public readonly List<string> PlayItems = new List<string>();
            public Recorder(Replay owner) { _owner = owner; }
            public bool TryIssue(string roomId, string controllingUserId, string userId, SessionSnapshot snapshot,
                string command, long? positionTicks, DateTimeOffset now, out string error)
            {
                Commands.Add((userId, command, positionTicks));
                var paused = command == RemoteCommands.Pause || command != RemoteCommands.Unpause && snapshot.IsPaused;
                _owner.Provider.Items[userId] = _owner.Make(userId, paused,
                    command == RemoteCommands.Seek ? positionTicks.Value / (double)SessionSnapshot.TicksPerSecond : snapshot.PositionSeconds, snapshot.ItemId);
                error = null; return true;
            }
            public bool TryIssuePlayItem(string roomId, string controllingUserId, string userId, SessionSnapshot snapshot,
                string itemId, DateTimeOffset now, CancellationToken cancellationToken, out string error)
            { PlayItems.Add(itemId); error = null; return true; }
        }
    }
}
