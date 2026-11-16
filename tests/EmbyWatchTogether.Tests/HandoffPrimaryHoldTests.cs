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

        [Fact]
        public void MissingStartupRuntime_WaitsInsteadOfFailingHold()
        {
            using var f = new Replay();
            f.Switch();
            f.Provider.Items["u1"] = f.Make("u1", "next", false, 0, runtime: 0);
            for (int i = 0; i < 4; i++) f.Tick();
            Assert.Equal(RoomState.Handoff, f.Runtime.State);
            Assert.Single(f.Issuer.Plays);
            Assert.Empty(f.Issuer.Commands);
            f.Provider.Items["u1"] = f.Make("u1", "next", false, 0);
            f.Tick();
            f.AdvancePrimary();
            Assert.Equal(RoomState.Handoff, f.Runtime.State);
            Assert.Single(f.Issuer.Commands, c => c.user == "u1" && c.command == RemoteCommands.Pause);
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

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        public void ResumeAfterHold_NaturalAlignmentRequiresStableProgressWithoutControls(double gap)
        {
            using var f = new Replay();
            f.BeginResumed(gap);
            Assert.Equal(RoomState.Handoff, f.Runtime.State);
            f.AdvancePair(.999, gap);
            Assert.Equal(RoomState.Handoff, f.Runtime.State);
            f.AdvancePair(.001, gap);
            Assert.Equal(RoomState.Watching, f.Runtime.State);
            Assert.Equal(new[] { ("u1", RemoteCommands.Pause) }, f.Issuer.Commands);
            Assert.Single(f.Issuer.Plays);
            Assert.Empty(f.Runtime.Pending);
        }

        [Fact]
        public void ResumeWhilePeerStillLoading_DoesNotRetryOldPlayItemOrPause()
        {
            using var f = new Replay();
            f.Switch(); var start = f.Now;
            f.AdvancePrimary(); f.Tick();
            f.Provider.Items["u1"] = f.Make("u1", "next", false, 1.3); f.Tick();
            for (int i = 0; i < 20; i++)
            {
                f.Provider.Items["u1"] = f.Make("u1", "next", false, 1.3 + i * .25);
                f.Tick();
            }
            Assert.Equal(RoomState.Handoff, f.Runtime.State);
            Assert.Single(f.Issuer.Plays); Assert.Single(f.Issuer.Commands);
            f.Now = start.AddSeconds(10); f.Tick(0);
            Assert.Equal(RoomState.Waiting, f.Runtime.State);
            Assert.Single(f.Issuer.Plays); Assert.Single(f.Issuer.Commands);
        }

        [Fact]
        public void ResumedPrimaryChangesTarget_StartsFreshGenerationWithoutOldRestore()
        {
            using var f = new Replay(); f.BeginResumed();
            var previous = f.Runtime.Handoff;
            f.Provider.Items["u1"] = f.Make("u1", "third", false, 0); f.Tick();
            Assert.Equal(RoomState.Handoff, f.Runtime.State);
            Assert.NotSame(previous, f.Runtime.Handoff);
            Assert.Equal("third", f.Runtime.Handoff.TargetItemId);
            Assert.Equal(new[] { "next", "third" }, f.Issuer.Plays);
            Assert.Single(f.Issuer.Commands);
            // A late old-target observation never completes the fresh operation.
            f.PeerAtTarget(3.3); f.Tick();
            Assert.Equal(RoomState.Handoff, f.Runtime.State);
            Assert.DoesNotContain(f.Issuer.Commands, command => command.command == RemoteCommands.Unpause);
        }

        [Theory]
        [InlineData("pause")]
        [InlineData("seek")]
        [InlineData("peer-seek")]
        [InlineData("gap")]
        [InlineData("session")]
        [InlineData("peer-session")]
        [InlineData("media")]
        [InlineData("peer-media")]
        [InlineData("peer-item")]
        [InlineData("runtime")]
        [InlineData("permission")]
        [InlineData("raw-control")]
        [InlineData("rate")]
        [InlineData("peer-missing")]
        [InlineData("primary-missing")]
        [InlineData("older-snapshot")]
        public void ResumedHold_InvalidObservationFailsClosedWithoutOldIntent(string change)
        {
            using var f = new Replay(); f.BeginResumed();
            var original = f.Provider.Items["u1"];
            if (change == "peer-missing") f.Provider.Items.Remove("u2");
            else if (change == "primary-missing") f.Provider.Items.Remove("u1");
            else if (change.StartsWith("peer-"))
                f.Provider.Items["u2"] = f.Make("u2", change == "peer-item" ? "old" : "next", false,
                    change == "peer-seek" ? 30 : original.PositionSeconds,
                    session: change == "peer-session" ? "replacement" : "s2",
                    media: change == "peer-media" ? "replacement" : "media");
            else
                f.Provider.Items["u1"] = f.Make("u1", "next", change == "pause",
                    change == "seek" ? 30 : original.PositionSeconds + (change == "gap" ? 2.01 : 0),
                    session: change == "session" ? "replacement" : "s1",
                    media: change == "media" ? "replacement" : "media", runtime: change == "runtime" ? 0 : 100,
                    capability: change != "permission", raw: change != "raw-control", rate: change == "rate" ? 1.5 : 1,
                    activity: change == "older-snapshot" ? original.LastActivityDateUtc.AddSeconds(-1) : null);
            f.Tick();
            Assert.Equal(RoomState.Waiting, f.Runtime.State);
            f.Provider.Items["u1"] = f.Make("u1", "next", false, 4);
            f.Provider.Items["u2"] = f.Make("u2", "next", false, 4);
            for (int i = 0; i < 10; i++) f.Tick();
            Assert.Equal(RoomState.Waiting, f.Runtime.State);
            Assert.Single(f.Issuer.Plays); Assert.Single(f.Issuer.Commands);
        }

        [Fact]
        public void ResumedHold_FrozenPairCannotClaimSuccessAndDeadlineCannotBeExtended()
        {
            using var f = new Replay(); f.BeginResumed();
            for (int i = 0; i < 50; i++) f.Tick();
            Assert.Equal(RoomState.Waiting, f.Runtime.State);
            Assert.Single(f.Issuer.Plays); Assert.Single(f.Issuer.Commands);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ResumedPeer_ContinuityUsesPreviousSampleNotOnlyInitialBaseline(bool olderActivity)
        {
            using var f = new Replay(); f.BeginResumed();
            f.Now = f.Now.AddSeconds(.5);
            f.Provider.Items["u1"] = f.Make("u1", "next", false, 2.8);
            f.PeerAtTarget(4.7); f.Tick(0);
            Assert.Equal(RoomState.Handoff, f.Runtime.State);
            var peer = f.Provider.Items["u2"];
            f.Now = f.Now.AddSeconds(.25);
            f.Provider.Items["u1"] = f.Make("u1", "next", false, 3.05);
            f.Provider.Items["u2"] = f.Make("u2", "next", false, olderActivity ? 4.7 : 2.4,
                activity: olderActivity ? peer.LastActivityDateUtc.AddSeconds(-.1) : null);
            f.Tick(0);
            Assert.Equal(RoomState.Waiting, f.Runtime.State);
            Assert.Single(f.Issuer.Plays); Assert.Single(f.Issuer.Commands);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void FirstResumeWithRawControlLoss_CannotLaterRestoreOldPlayingIntent(bool primaryLostControl)
        {
            using var f = new Replay();
            f.Switch(); f.AdvancePrimary(); f.Tick();
            f.Provider.Items["u1"] = f.Make("u1", "next", false, 1.3, raw: !primaryLostControl);
            f.Provider.Items["u2"] = f.Make("u2", "next", false, 1.3, raw: primaryLostControl);
            f.Tick();
            Assert.Equal(RoomState.Waiting, f.Runtime.State);
            f.Provider.Items["u1"] = f.Make("u1", "next", true, 1.3);
            f.PeerAtTarget(1.3, true);
            for (int i = 0; i < 10; i++) f.Tick();
            Assert.Equal(RoomState.Waiting, f.Runtime.State);
            Assert.Single(f.Issuer.Plays); Assert.Single(f.Issuer.Commands);
        }

        [Theory]
        [InlineData(.1)]
        [InlineData(5)]
        public void HoldSnapshotFailure_RetiresIntentAcrossShortAndProtectedOutages(double outage)
        {
            using var f = new Replay();
            f.Switch(); f.AdvancePrimary(); f.Tick();
            f.Provider.Fail = true; f.Tick(); f.Tick(outage);
            f.Provider.Fail = false;
            f.Provider.Items["u1"] = f.Make("u1", "next", true, 1.2);
            f.PeerAtTarget(1.2, true);
            for (int i = 0; i < 20; i++) f.Tick();
            Assert.Equal(RoomState.Waiting, f.Runtime.State);
            Assert.Single(f.Issuer.Plays); Assert.Single(f.Issuer.Commands);
        }

        [Theory]
        [InlineData(9.999, RoomState.Watching)]
        [InlineData(10, RoomState.Waiting)]
        [InlineData(10.001, RoomState.Waiting)]
        public void ResumedHold_OriginalAbsoluteDeadlineWinsOverAlignedPair(double seconds, RoomState expected)
        {
            using var f = new Replay(); f.BeginResumed();
            var started = f.Runtime.Handoff.StartedAtUtc;
            var delta = (started.AddSeconds(seconds) - f.Now).TotalSeconds;
            var position = f.Provider.Items["u1"].PositionSeconds + delta;
            f.Now = started.AddSeconds(seconds);
            f.Provider.Items["u1"] = f.Make("u1", "next", false, position);
            f.PeerAtTarget(position); f.Tick(0);
            Assert.Equal(expected, f.Runtime.State);
            Assert.Single(f.Issuer.Plays); Assert.Single(f.Issuer.Commands);
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
                string session = null, bool capability = true, string media = "media", int runtime = 100,
                bool raw = true, double rate = 1, DateTimeOffset? activity = null) =>
                new SessionSnapshot(session ?? (user == "u1" ? "s1" : "s2"), user, item, media,
                    (long)(position * SessionSnapshot.TicksPerSecond), runtime * SessionSnapshot.TicksPerSecond,
                    paused, rate, false, raw, new SessionCapabilityReport(capability,
                        new[] { "Pause", "Unpause", "Seek", "DisplayMessage" }), activity ?? Now);
            public void BeginResumed(double gap = 0)
            {
                Switch(); AdvancePrimary(); Tick();
                PeerAtTarget(1.2); Tick();
                Provider.Items["u1"] = Make("u1", "next", false, 1.3);
                PeerAtTarget(1.3 + gap); Tick(.1);
                Now = Now.AddSeconds(1);
                Provider.Items["u1"] = Make("u1", "next", false, 2.3);
                PeerAtTarget(2.3 + gap); Tick(0);
            }
            public void AdvancePair(double seconds, double gap = 0)
            {
                var position = Provider.Items["u1"].PositionSeconds + seconds;
                Now = Now.AddSeconds(seconds);
                Provider.Items["u1"] = Make("u1", "next", false, position);
                PeerAtTarget(position + gap); Tick(0);
            }
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
            public bool Fail;
            public IReadOnlyList<SessionSnapshot> GetSessionSnapshots() => Fail
                ? throw new InvalidOperationException("simulated provider failure") : Items.Values.ToArray();
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
