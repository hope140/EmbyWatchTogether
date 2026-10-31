using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    [Collection(TransportTimingCollection.Name)]
    public class HandoffPrimaryHoldTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(20)]
        public void ItemAloneOrFrozenStart_DoesNotPauseWhileParticipantLoads(double start)
        {
            using var f = new Replay();
            f.Switch(start);
            for (int i = 0; i < 8; i++) f.Tick(.25);
            Assert.Single(f.Issuer.Plays);
            Assert.Empty(f.Issuer.Commands);
            Assert.Equal(RoomState.Handoff, f.Runtime.State);
        }

        [Fact]
        public void ProgressThenPauseAck_HoldsOnceUntilParticipantIsReadyAndRestoresPlaying()
        {
            using var f = new Replay();
            f.Switch();
            Assert.Single(f.Issuer.Plays);
            Assert.Empty(f.Issuer.Commands);
            f.AdvancePrimary();
            Assert.Equal(new[] { ("u1", RemoteCommands.Pause) }, f.Issuer.Commands.Select(c => (c.user, c.command)));
            Assert.Empty(f.Runtime.Pending);
            f.Tick();
            for (int i = 0; i < 3; i++) f.Tick();
            Assert.Single(f.Issuer.Commands);
            Assert.Equal(RoomState.Handoff, f.Runtime.State);
            f.PeerAtTarget(0);
            f.Tick();
            Assert.Single(f.Issuer.Commands); // Item alone must not pause a loading peer.
            f.Now = f.Now.AddSeconds(1.1);
            f.PeerAtTarget(1.1);
            f.Tick();
            Assert.Equal(RoomState.Barrier, f.Runtime.State);
            Assert.DoesNotContain(f.Issuer.Commands, c => c.command == RemoteCommands.Unpause);
            for (int i = 0; i < 8; i++) f.Tick();
            Assert.Equal(RoomState.Watching, f.Runtime.State);
            Assert.False(f.Provider.Items["u1"].IsPaused);
            Assert.False(f.Provider.Items["u2"].IsPaused);
            Assert.Single(f.Issuer.Commands, c => c.user == "u1" && c.command == RemoteCommands.Pause);
            Assert.Single(f.Issuer.Plays);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AlreadyPausedOrManualPauseBeforeRequest_NeverAutoResumes(bool initiallyPaused)
        {
            using var f = new Replay();
            f.Switch(paused: initiallyPaused);
            if (!initiallyPaused) f.Provider.Items["u1"] = f.Make("u1", "next", true, 0);
            f.Tick();
            f.PeerAtTarget(0);
            f.Tick();
            f.Now = f.Now.AddSeconds(1.1);
            f.PeerAtTarget(1.1);
            for (int i = 0; i < 10; i++) f.Tick();
            Assert.Equal(RoomState.Watching, f.Runtime.State);
            Assert.True(f.Provider.Items["u1"].IsPaused);
            Assert.True(f.Provider.Items["u2"].IsPaused);
            Assert.DoesNotContain(f.Issuer.Commands, c => c.command == RemoteCommands.Unpause);
            Assert.DoesNotContain(f.Issuer.Commands, c => c.user == "u1");
        }

        [Fact]
        public void AcceptedPauseWithoutAck_CannotRestoreAndHasFiniteAttempts()
        {
            using var f = new Replay();
            f.Switch();
            f.Issuer.Acknowledge = false;
            f.AdvancePrimary();
            f.PeerAtTarget(0);
            f.Tick();
            f.Now = f.Now.AddSeconds(1.1);
            f.PeerAtTarget(1.1);
            f.Tick();
            Assert.Equal(RoomState.Handoff, f.Runtime.State);
            for (int i = 0; i < 50; i++) f.Tick(.25);
            Assert.Equal(RoomState.Waiting, f.Runtime.State);
            Assert.Equal(2, f.Issuer.Commands.Count(c => c.user == "u1" && c.command == RemoteCommands.Pause));
            Assert.DoesNotContain(f.Issuer.Commands, c => c.command == RemoteCommands.Seek || c.command == RemoteCommands.Unpause);
            Assert.Null(f.Runtime.Handoff);
        }

        [Theory]
        [InlineData("resume")]
        [InlineData("seek")]
        [InlineData("session")]
        [InlineData("session-and-item")]
        [InlineData("stop")]
        [InlineData("permission")]
        [InlineData("media")]
        [InlineData("runtime")]
        public void HeldPrimaryChange_CannotRestoreOldIntent(string change)
        {
            using var f = new Replay();
            f.Switch();
            f.AdvancePrimary();
            f.Tick();
            var old = f.Provider.Items["u1"];
            if (change == "stop") f.Provider.Items.Remove("u1");
            else f.Provider.Items["u1"] = f.Make("u1", change == "session-and-item" ? "third" : "next", change != "resume",
                change == "seek" ? 30 : old.PositionSeconds,
                session: change.StartsWith("session", StringComparison.Ordinal) ? "replacement" : "s1", capability: change != "permission",
                media: change == "media" ? "replacement" : "media", runtime: change == "runtime" ? 50 : 100);
            f.Tick();
            Assert.Equal(RoomState.Waiting, f.Runtime.State);
            Assert.Single(f.Issuer.Plays);
            var issued = f.Issuer.Commands.Count;
            f.Provider.Items["u1"] = f.Make("u1", "next", true, 1.2);
            f.PeerAtTarget(1.2, true);
            for (int i = 0; i < 20; i++) f.Tick();
            Assert.Equal(RoomState.Waiting, f.Runtime.State);
            Assert.Equal(issued, f.Issuer.Commands.Count);
            Assert.DoesNotContain(f.Issuer.Commands, c => c.command == RemoteCommands.Unpause);
        }

        [Fact]
        public void FrozenPeerTarget_TimesOutWithoutPauseOrUnpauseAndExplicitResyncCanRecover()
        {
            using var f = new Replay();
            f.Switch();
            f.AdvancePrimary();
            f.Tick();
            f.PeerAtTarget(0);
            for (int i = 0; i < 50; i++) f.Tick(.25);
            Assert.Equal(RoomState.Waiting, f.Runtime.State);
            Assert.Single(f.Issuer.Commands);
            Assert.True(f.Provider.Items["u1"].IsPaused);
            Assert.Null(f.Runtime.Handoff);
            Assert.Equal("accepted", f.Rooms.RequestParticipantResync(f.Room.Id, "u2", () => "server", f.Now).Status);
            for (int i = 0; i < 10; i++) f.Tick();
            Assert.Equal(RoomState.Watching, f.Runtime.State);
            Assert.True(f.Provider.Items["u1"].IsPaused);
            Assert.DoesNotContain(f.Issuer.Commands, c => c.command == RemoteCommands.Unpause);
        }

        [Fact]
        public void PauseDeliveryFailure_NeverUnpausesOrStartsBarrierAfterLateTargetAck()
        {
            using var f = new Replay();
            f.Switch();
            f.Issuer.FailPause = true;
            f.AdvancePrimary();
            Assert.Equal(RoomState.Waiting, f.Runtime.State);
            f.PeerAtTarget(1.2, true);
            for (int i = 0; i < 40; i++) f.Tick();
            Assert.Equal(RoomState.Waiting, f.Runtime.State);
            Assert.Single(f.Issuer.Commands);
            Assert.DoesNotContain(f.Issuer.Commands, c => c.command == RemoteCommands.Unpause);
        }

        [Fact]
        public void LateTargetAtDeadline_CannotExtendStartupObservationOrRestore()
        {
            using var f = new Replay();
            f.Switch();
            var began = f.Now;
            f.AdvancePrimary();
            f.Tick();
            f.Now = began.AddSeconds(9.5);
            f.PeerAtTarget(1.2, true);
            f.Tick(0);
            Assert.Equal(RoomState.Handoff, f.Runtime.State);
            f.Now = began.AddSeconds(10);
            f.PeerAtTarget(1.2, true);
            f.Tick(0);
            Assert.Equal(RoomState.Waiting, f.Runtime.State);
            Assert.DoesNotContain(f.Issuer.Commands, c => c.command == RemoteCommands.Unpause);
            f.Runtime.ResetToWaiting(); // Generic recovery cannot authorize a new cycle.
            f.Tick();
            Assert.Equal(RoomState.Waiting, f.Runtime.State);
            Assert.Single(f.Issuer.Commands);
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
            public Replay()
            {
                Room = Rooms.CreateRoom("server", "http://example.invalid", "offline handoff", "admin", new[] { "u1", "u2" }, "u1");
                Issuer = new Recorder(this);
                Engine = new SyncEngine(Rooms, Provider, Issuer, () => "server", () => Now,
                    notifyOtherOnPlaybackStop: false, notifyOnSyncActions: false);
                Provider.Items["u1"] = Make("u1", "old", false, 40);
                Provider.Items["u2"] = Make("u2", "old", false, 40);
                for (int i = 0; i < 10; i++) Tick();
                Assert.Equal(RoomState.Watching, Runtime.State);
                Issuer.Commands.Clear(); Issuer.Plays.Clear();
            }
            public SessionSnapshot Make(string user, string item, bool paused, double position,
                string session = null, bool capability = true, string media = "media", int runtime = 100) =>
                new SessionSnapshot(session ?? (user == "u1" ? "s1" : "s2"), user, item, media,
                    (long)(position * SessionSnapshot.TicksPerSecond), runtime * SessionSnapshot.TicksPerSecond,
                    paused, 1, false, true, new SessionCapabilityReport(capability,
                        new[] { "Pause", "Unpause", "Seek", "DisplayMessage" }), Now);
            public void Switch(double position = 0, bool paused = false)
            { Provider.Items["u1"] = Make("u1", "next", paused, position); Tick(); }
            public void AdvancePrimary()
            { Now = Now.AddSeconds(1.1); Provider.Items["u1"] = Make("u1", "next", false, 1.2); Tick(); }
            public void PeerAtTarget(double position, bool paused = false)
            { Provider.Items["u2"] = Make("u2", "next", paused, position); }
            public void Tick(double seconds = .25) { Now = Now.AddSeconds(seconds); Engine.PollOnce(Now); }
            public void Dispose() => Engine.Dispose();
        }
        private sealed class Provider : ISessionSnapshotProvider
        {
            public readonly Dictionary<string, SessionSnapshot> Items = new Dictionary<string, SessionSnapshot>();
            public IReadOnlyList<SessionSnapshot> GetSessionSnapshots() => Items.Values.ToArray();
        }
        private sealed class Recorder : ICommandIssuer, IPlayItemIssuer
        {
            private readonly Replay _f;
            public readonly List<(string user, string command)> Commands = new List<(string, string)>();
            public readonly List<string> Plays = new List<string>();
            public bool Acknowledge = true;
            public bool FailPause;
            public Recorder(Replay f) { _f = f; }
            public bool TryIssue(string roomId, string controller, string userId, SessionSnapshot snapshot,
                string command, long? positionTicks, DateTimeOffset now, out string error)
            {
                Commands.Add((userId, command));
                error = command == RemoteCommands.Pause && FailPause ? "offline simulated failure" : null;
                if (error != null) return false;
                if (Acknowledge)
                    _f.Provider.Items[userId] = _f.Make(userId, snapshot.ItemId,
                        command == RemoteCommands.Pause || command != RemoteCommands.Unpause && snapshot.IsPaused,
                        command == RemoteCommands.Seek ? positionTicks.Value / (double)SessionSnapshot.TicksPerSecond : snapshot.PositionSeconds);
                return true;
            }
            public bool TryIssuePlayItem(string roomId, string controller, string userId, SessionSnapshot snapshot,
                string itemId, DateTimeOffset now, CancellationToken cancellationToken, out string error)
            { Plays.Add(itemId); error = null; return true; }
        }
    }
}
