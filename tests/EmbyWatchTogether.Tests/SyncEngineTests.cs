using System;
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
    public class SyncEngineTests
    {
        private readonly TestClock _clock = new TestClock();
        private readonly RecordingIssuer _issuer = new RecordingIssuer();
        private readonly RecordingMessageIssuer _messageIssuer = new RecordingMessageIssuer();
        private readonly RoomManager _rooms = new RoomManager();
        private readonly FakeSnapshotProvider _provider = new FakeSnapshotProvider();

        [Fact]
        public void RequestImmediatePoll_WakesBackgroundLoop_AndDisposeIsIdempotent()
        {
            var rooms = new RoomManager();
            rooms.CreateRoom(
                "server-1", "http://emby", "room", "admin-1",
                new[] { "u1", "u2" }, "u1");
            var provider = new CountingSnapshotProvider();
            var engine = new SyncEngine(
                rooms,
                provider,
                new RecordingIssuer(),
                () => "server-1",
                pollIntervalSeconds: 5.0);

            try
            {
                engine.Start();
                Assert.True(provider.WaitForCount(1, TimeSpan.FromSeconds(2)));

                var countAfterInitialPoll = provider.Count;
                engine.RequestImmediatePoll();

                Assert.True(provider.WaitForCount(
                    countAfterInitialPoll + 1,
                    TimeSpan.FromSeconds(2)));

                engine.Stop();
                engine.Dispose();
                engine.Dispose();
                engine.RequestImmediatePoll();
            }
            finally
            {
                engine.Dispose();
            }
        }

        [Fact]
        public void PollOnce_NoRooms_ReturnsEmpty()
        {
            var engine = CreateEngine();

            var results = engine.PollOnce(_clock.Now);

            Assert.Empty(results);
        }

        [Fact]
        public void PollOnce_ServerMismatch_MarksRoomUnavailable()
        {
            var room = CreateRoom();
            var engine = CreateEngine(serverId: "other-server");

            var results = engine.PollOnce(_clock.Now);

            Assert.Equal(RoomState.Unavailable, results.Single().State);
            Assert.Equal("room server is unavailable", results.Single().Error);
        }

        [Fact]
        public void Barrier_ProgressesPauseSeekRestoreToWatching()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 50 * SessionSnapshot.TicksPerSecond));

            // t0: pair eligible -> barrier starts, pause issued to both.
            var r1 = engine.PollOnce(_clock.Now);
            Assert.Equal(RoomState.Barrier, r1.Single().State);
            Assert.Equal(2, _issuer.Issued.Count);
            Assert.All(_issuer.Issued, i => Assert.Equal(RemoteCommands.Pause, i.command));

            // t1: both paused -> pending matched, stage advances to seek (no new command yet).
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 0));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            Assert.Equal(2, _issuer.Issued.Count);

            // t2: seek stage -> seek issued to secondary.
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            Assert.Equal(3, _issuer.Issued.Count);
            var seek = _issuer.Issued.Last();
            Assert.Equal(RemoteCommands.Seek, seek.command);
            Assert.Equal("u2", seek.userId);
            Assert.Equal(50 * SessionSnapshot.TicksPerSecond, seek.positionTicks);

            // t3: secondary at target -> pending matched, stage advances to restore.
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            Assert.Equal(3, _issuer.Issued.Count);

            // t4: restore stage -> primary paused, Pause re-issued to both.
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            Assert.Equal(5, _issuer.Issued.Count);

            // t5: everyone resumed (matching the primary's pre-barrier state) -> watching.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            Assert.Equal(RoomState.Watching, _rooms.GetRuntime(room.Id).State);
            Assert.Null(_rooms.GetRuntime(room.Id).Barrier);
        }

        [Fact]
        public void Barrier_ReanchorsPrimaryAfterPauseAcknowledgement()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 50 * SessionSnapshot.TicksPerSecond));

            engine.PollOnce(_clock.Now);

            // The primary advances while the pause commands are being applied.
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 53 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 0));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            var seek = _issuer.Issued.Last();
            Assert.Equal(RemoteCommands.Seek, seek.command);
            Assert.Equal("u2", seek.userId);
            Assert.Equal(53 * SessionSnapshot.TicksPerSecond, seek.positionTicks);
        }

        [Fact]
        public void Barrier_FinalAlignsSecondaryAfterRestore()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 50 * SessionSnapshot.TicksPerSecond));
            engine.PollOnce(_clock.Now);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 53 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 0));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 53 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 53 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            // Both restore commands are acknowledged, but the secondary starts a
            // little behind because restore commands are delivered independently.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 53 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 49 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            Assert.Equal(RoomState.Barrier, _rooms.GetRuntime(room.Id).State);

            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            var finalAlign = _issuer.Issued.Last();
            Assert.Equal(RemoteCommands.Seek, finalAlign.command);
            Assert.Equal("u2", finalAlign.userId);
            Assert.Equal(53 * SessionSnapshot.TicksPerSecond, finalAlign.positionTicks);

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 53 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 53 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Equal(RoomState.Watching, _rooms.GetRuntime(room.Id).State);
        }

        [Fact]
        public void Barrier_SkipsFinalAlignWhenRestoreDriftIsWithinStartupTolerance()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 50 * SessionSnapshot.TicksPerSecond));
            engine.PollOnce(_clock.Now);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 53 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 0));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 53 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 53 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 53 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 52 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Equal(RoomState.Watching, _rooms.GetRuntime(room.Id).State);
            Assert.Equal(5, _issuer.Issued.Count);
        }

        [Fact]
        public void SoloPlayer_IsNeverPausedWhileWaitingForSecond()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(Snapshot("s1", "u1", paused: false, position: 0));

            engine.PollOnce(_clock.Now);

            Assert.Empty(_issuer.Issued);
            Assert.Equal(RoomState.Waiting, _rooms.GetRuntime(room.Id).State);
        }

        [Fact]
        public void DifferentItems_DoNotSeek_AndPauseActiveSessions()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0, itemId: "i1"),
                Snapshot("s2", "u2", paused: false, position: 0, itemId: "i2"));

            var result = engine.PollOnce(_clock.Now).Single();

            Assert.Equal(RoomState.Waiting, result.State);
            Assert.Contains("不同视频", result.Error);
            Assert.DoesNotContain(_issuer.Issued, i => i.command == RemoteCommands.Seek);
            Assert.Equal(2, _issuer.Issued.Count(i => i.command == RemoteCommands.Pause));
        }

        [Fact]
        public void ParticipantLeavingBeforePlaybackStarts_DoesNotStickStoppedError()
        {
            var room = CreateRoom();
            var engine = CreateEngine();

            // Both participants only opened the item. The coordinator has
            // started the barrier, but neither side has produced playback.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 0));
            engine.PollOnce(_clock.Now);
            Assert.Equal(RoomState.Barrier, _rooms.GetRuntime(room.Id).State);

            // The secondary closes before playback starts. SessionSelector
            // omits the stopped session, so the barrier should simply reset.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 0, stopped: true));
            _clock.Advance(1);
            var result = engine.PollOnce(_clock.Now).Single();

            Assert.Equal(RoomState.Waiting, result.State);
            Assert.Null(result.Error);
            Assert.Null(_rooms.GetRuntime(room.Id).Barrier);

            // Both reopen later at different positions. A fresh barrier must
            // start automatically without pressing the manual resync action.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 60 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            result = engine.PollOnce(_clock.Now).Single();

            Assert.Equal(RoomState.Barrier, result.State);
            Assert.Null(result.Error);
        }

        [Fact]
        public void PrimaryPositionResetToZero_PausesSecondaryByDefault()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 60 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            var result = engine.PollOnce(_clock.Now).Single();

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(RoomState.Waiting, result.State);
            Assert.Contains("播放已停止", result.Error);
            Assert.Null(runtime.Barrier);
            Assert.Empty(runtime.Pending);
            Assert.Empty(runtime.Suppressed);
            Assert.Empty(runtime.Previous);
            Assert.Contains(_issuer.Issued, i =>
                i.userId == "u2" && i.command == RemoteCommands.Pause);
            Assert.DoesNotContain(_issuer.Issued, i =>
                i.userId == "u1" && i.command == RemoteCommands.Pause);
            Assert.DoesNotContain(_issuer.Issued, i =>
                i.userId == "u2" && i.command == RemoteCommands.Seek && i.positionTicks == 0);

            // Re-opening the same item near the same position starts a new barrier;
            // the old watching snapshot is never reused.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 0));
            _clock.Advance(1);
            result = engine.PollOnce(_clock.Now).Single();

            Assert.Equal(RoomState.Barrier, result.State);
            Assert.Contains(_issuer.Issued, i => i.command == RemoteCommands.Pause);
        }

        [Fact]
        public void StoppedParticipant_PausesOnlyTheOnlineParticipant()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0, stopped: true),
                Snapshot("s2", "u2", paused: false, position: 60 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            var result = engine.PollOnce(_clock.Now).Single();

            Assert.Equal(RoomState.Waiting, result.State);
            Assert.Contains(_issuer.Issued, i =>
                i.userId == "u2" && i.command == RemoteCommands.Pause);
            Assert.DoesNotContain(_issuer.Issued, i =>
                i.userId == "u1" && i.command == RemoteCommands.Pause);
        }

        [Fact]
        public void StoppedParticipant_NotifiesOnlyTheOnlineParticipant_WhenEnabled()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0, stopped: true),
                Snapshot("s2", "u2", paused: false, position: 60 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            var message = Assert.Single(_messageIssuer.Issued);
            Assert.Equal("u2", message.userId);
            Assert.Contains("对方已停止播放", message.text);
        }

        [Fact]
        public void SupportedDisplayMessage_IsForwardedToRemoteSession()
        {
            var manager = new Mock<ISessionManager>();
            manager.Setup(m => m.SendMessageCommand(
                    It.IsAny<string>(),
                    "s2",
                    It.IsAny<MessageCommand>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            using var bridge = new SessionBridge(manager.Object);
            var issuer = new SessionBridgeCommandIssuer(bridge);
            var snapshot = new SessionSnapshot(
                "s2", "u2", "i1", "m1", 10 * SessionSnapshot.TicksPerSecond,
                100 * SessionSnapshot.TicksPerSecond, false, 1.0, stopped: false,
                supportsRemoteControl: true,
                new SessionCapabilityReport(true, new[] { RemoteCommands.DisplayMessage }),
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

            var ok = issuer.TryIssueMessage(
                "room-1", "admin-1", "u2", snapshot,
                "一起观看", "对方已停止播放，请重新打开视频", 3000,
                DateTimeOffset.UtcNow, out var error);

            Assert.True(ok);
            Assert.Null(error);
            manager.Verify(m => m.SendMessageCommand(
                It.IsAny<string>(),
                "s2",
                It.Is<MessageCommand>(c =>
                    c.Header == "一起观看" &&
                    c.Text == "对方已停止播放，请重新打开视频" &&
                    c.TimeoutMs == 3000),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void StoppedParticipant_NotifiesOnlyOnceUntilPlaybackResumes()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0, stopped: true),
                Snapshot("s2", "u2", paused: false, position: 60 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Single(_messageIssuer.Issued);
        }

        [Fact]
        public void StoppedParticipant_NotifiesAgainAfterResumingSameItem()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0, stopped: true),
                Snapshot("s2", "u2", paused: false, position: 60 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            Assert.Single(_messageIssuer.Issued);

            // Reopen the same item and complete a fresh pause/seek/restore barrier.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            Assert.Equal(RoomState.Barrier, engine.PollOnce(_clock.Now).Single().State);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // pause acknowledged, enter seek stage
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // seek issued
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // seek acknowledged, enter restore stage

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // restore issued
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // restore acknowledged, enter final alignment stage
            _clock.Advance(1);
            Assert.Equal(RoomState.Watching, engine.PollOnce(_clock.Now).Single().State);

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0, stopped: true),
                Snapshot("s2", "u2", paused: false, position: 60 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Equal(2, _messageIssuer.Issued.Count);
        }

        [Fact]
        public void StoppedParticipant_DoesNotNotifyWhenDisabled()
        {
            var room = CreateRoom();
            var engine = CreateEngine(notifyOtherOnPlaybackStop: false);
            EnterWatching(engine, room);

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0, stopped: true),
                Snapshot("s2", "u2", paused: false, position: 60 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Empty(_messageIssuer.Issued);
        }

        [Fact]
        public void UnsupportedDisplayMessage_DoesNotBlockStopHandling()
        {
            var room = CreateRoom();
            var manager = new Mock<ISessionManager>();
            using var bridge = new SessionBridge(manager.Object);
            var engine = CreateEngine(messageIssuer: new SessionBridgeCommandIssuer(bridge));
            EnterWatching(engine, room);

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0, stopped: true),
                Snapshot("s2", "u2", paused: false, position: 60 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            var result = engine.PollOnce(_clock.Now).Single();

            Assert.Equal(RoomState.Waiting, result.State);
            Assert.Contains(_issuer.Issued, i =>
                i.userId == "u2" && i.command == RemoteCommands.Pause);
        }

        [Fact]
        public void MissingParticipant_PausesOnlyTheRemainingParticipant()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);

            SetCandidates(
                Snapshot("s2", "u2", paused: false, position: 60 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            var result = engine.PollOnce(_clock.Now).Single();

            Assert.Equal(RoomState.Waiting, result.State);
            Assert.Contains(_issuer.Issued, i =>
                i.userId == "u2" && i.command == RemoteCommands.Pause);
            Assert.DoesNotContain(_issuer.Issued, i => i.userId == "u1");
        }

        [Fact]
        public void PrimaryPositionReset_DoesNotPauseSecondaryWhenDisabled()
        {
            var room = CreateRoom();
            var engine = CreateEngine(pauseOtherOnPlaybackStop: false);
            EnterWatching(engine, room);

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 60 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            var result = engine.PollOnce(_clock.Now).Single();

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(RoomState.Waiting, result.State);
            Assert.Contains("播放已停止", result.Error);
            Assert.Empty(_issuer.Issued);
            Assert.Empty(runtime.Pending);
            Assert.Empty(runtime.Previous);
        }

        [Fact]
        public void WatchingTick_PropagatesPauseFromPrimary()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);

            // Primary pauses; secondary should receive Unpause? No: primary paused -> Pause to secondary.
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Contains(_issuer.Issued, i =>
                i.userId == "u2" && i.command == RemoteCommands.Pause);
        }

        [Fact]
        public void WatchingTick_PlayPauseTransitionDoesNotLookLikeSeek()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);
            _issuer.Issued.Clear();

            // A pause acknowledgement is observed first.
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            // The users resume after a delayed poll. The ten-second movement is
            // caused by resuming playback, not by a seek, so only Unpause should
            // be propagated.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 60 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 60 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(10);
            engine.PollOnce(_clock.Now);

            Assert.Contains(_issuer.Issued, i =>
                i.userId == "u2" && i.command == RemoteCommands.Unpause);
            Assert.DoesNotContain(_issuer.Issued, i => i.command == RemoteCommands.Seek);
        }

        [Fact]
        public void WatchingTick_LongPollingIntervalUsesExpectedPosition_AndDoesNotSeek()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);
            _issuer.Issued.Clear();

            // A delayed poll reports ten seconds of normal playback in one
            // snapshot. Raw delta logic would treat this as a manual seek.
            _clock.Advance(10);
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 60 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 60 * SessionSnapshot.TicksPerSecond));
            engine.PollOnce(_clock.Now);

            Assert.DoesNotContain(_issuer.Issued, i => i.command == RemoteCommands.Seek);
        }

        [Fact]
        public void WatchingTick_DifferentPlaybackRatesFollowTheirProjection_AndDoNotSeek()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);
            _issuer.Issued.Clear();

            // Establish different, but stable playback rates before the long
            // polling interval. Their resulting position gap is intentional.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond, playbackRate: 1.0),
                Snapshot("s2", "u2", paused: false, position: 50 * SessionSnapshot.TicksPerSecond, playbackRate: 1.5));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            _clock.Advance(10);
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 60 * SessionSnapshot.TicksPerSecond, playbackRate: 1.0),
                Snapshot("s2", "u2", paused: false, position: 65 * SessionSnapshot.TicksPerSecond, playbackRate: 1.5));
            engine.PollOnce(_clock.Now);

            Assert.DoesNotContain(_issuer.Issued, i => i.command == RemoteCommands.Seek);
        }

        [Fact]
        public void WatchingTick_NaturalRateDifferenceAndSustainedDriftDoNotSeek()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);
            _issuer.Issued.Clear();

            // Each round advances normally, but the playback-rate difference
            // gradually grows the position gap beyond the seek threshold.
            for (int round = 1; round <= 6; round++)
            {
                SetCandidates(
                    Snapshot("s1", "u1", paused: false, position: (50 + round) * SessionSnapshot.TicksPerSecond),
                    Snapshot("s2", "u2", paused: false, position: (50 + 2 * round) * SessionSnapshot.TicksPerSecond));
                _clock.Advance(1);
                engine.PollOnce(_clock.Now);

                Assert.DoesNotContain(_issuer.Issued, i => i.command == RemoteCommands.Seek);
            }

            Assert.Equal(0, _issuer.Issued.Count(i => i.command == RemoteCommands.Seek));
        }

        [Fact]
        public void WatchingTick_ManualSeek_StartsAlignBarrier_AndAlignsFollower()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);

            // u1 drags from 50s to 60s: a fresh align barrier starts (pause
            // both sides) instead of a raw seek to the peer.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 60 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(RoomState.Barrier, runtime.State);
            Assert.Equal("u1", runtime.Barrier.AnchorUserId);
            Assert.DoesNotContain(_issuer.Issued, i => i.command == RemoteCommands.Seek);

            // Pause is issued to both by the first barrier tick.
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            Assert.Equal(2, _issuer.Issued.Count(i => i.command == RemoteCommands.Pause));

            // Both paused -> re-anchor at u1's position, move to seek stage.
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 60 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            Assert.Equal(2, _issuer.Issued.Count(i => i.command == RemoteCommands.Pause));

            // Seek stage: follower (u2) is seeked to the anchor position.
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            var seek = _issuer.Issued.Last(i => i.command == RemoteCommands.Seek);
            Assert.Equal("u2", seek.userId);
            Assert.Equal(60 * SessionSnapshot.TicksPerSecond, seek.positionTicks);

            // Follower in place -> restore (both were playing -> unpause).
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 60 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 60 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            Assert.Contains(_issuer.Issued, i => i.command == RemoteCommands.Unpause);

            // Both resumed at the anchor -> final align -> back to Watching.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 60 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 60 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            Assert.Equal(RoomState.Watching, _rooms.GetRuntime(room.Id).State);
        }

        [Fact]
        public void WatchingTick_ManualSeek_SecondaryDragUsesSecondaryAsAnchor()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);

            // u2 (secondary) drags from 50s to 60s.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 60 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(RoomState.Barrier, runtime.State);
            Assert.Equal("u2", runtime.Barrier.AnchorUserId);

            // Pause is issued to both by the first barrier tick.
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            // Both paused -> seek stage targets the primary with u2's position.
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 60 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            var seek = _issuer.Issued.Last(i => i.command == RemoteCommands.Seek);
            Assert.Equal("u1", seek.userId);
            Assert.Equal(60 * SessionSnapshot.TicksPerSecond, seek.positionTicks);
        }

        [Fact]
        public void WatchingTick_SmallBackwardJump_WithinSeekWindow_IsIgnored()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);

            // A remote seek was just issued to u1, so a small rewind is the
            // player's clock re-basing, not a user action.
            _rooms.GetRuntime(room.Id).LastSeekAtUtc["u1"] = _clock.Now;
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 44 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 51 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Equal(RoomState.Watching, _rooms.GetRuntime(room.Id).State);
            Assert.DoesNotContain(_issuer.Issued, i => i.command == RemoteCommands.Seek);
            Assert.DoesNotContain(_issuer.Issued, i => i.command == RemoteCommands.Pause);
        }

        [Fact]
        public void WatchingTick_SmallBackwardJump_OutsideSeekWindow_StartsAlignBarrier()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);

            // No recent remote seek: a small rewind (e.g. the user presses -5s
            // on the player) is a real manual seek.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 44 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 51 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(RoomState.Barrier, runtime.State);
            Assert.Equal("u1", runtime.Barrier.AnchorUserId);
        }

        [Fact]
        public void WatchingTick_LargeBackwardJumpStartsAlignBarrier()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);

            // A real backward drag (30s) is a manual seek and starts alignment.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 20 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 51 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(RoomState.Barrier, runtime.State);
            Assert.Equal("u1", runtime.Barrier.AnchorUserId);
        }
        [Fact]
        public void InitialBarrier_DelayedSnapshotAcknowledgementWithinGrace_DoesNotFail()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 0));

            engine.PollOnce(_clock.Now); // pause issued, pending recorded
            Assert.Equal(RoomState.Barrier, _rooms.GetRuntime(room.Id).State);

            // The first retry is issued after 3s, but the SessionInfo snapshot
            // still exposes the pre-command state at that exact poll.
            _clock.Advance(3);
            engine.PollOnce(_clock.Now);
            Assert.Equal(4, _issuer.Issued.Count);

            // At the retry timeout, keep waiting for the bounded snapshot grace
            // instead of racing BarrierTick into a false failure.
            _clock.Advance(3);
            engine.PollOnce(_clock.Now);
            Assert.Equal(RoomState.Barrier, _rooms.GetRuntime(room.Id).State);
            Assert.Null(_rooms.GetRuntime(room.Id).Error);

            // The delayed acknowledgement arrives within the one-second grace.
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 0),
                Snapshot("s2", "u2", paused: true, position: 0));
            _clock.Advance(0.5);
            engine.PollOnce(_clock.Now);

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(RoomState.Barrier, runtime.State);
            Assert.Null(runtime.Error);
            Assert.Equal(4, _issuer.Issued.Count);
        }

        [Fact]
        public void PendingCommand_NotAcknowledged_RetriesThenFailsToWaiting()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 0));

            engine.PollOnce(_clock.Now); // pause issued, pending recorded
            Assert.Equal(RoomState.Barrier, _rooms.GetRuntime(room.Id).State);

            // 3s pass without acknowledgement; first retry re-issues. The
            // bounded grace expires one second later and still fails.
            _clock.Advance(3);
            engine.PollOnce(_clock.Now);
            _clock.Advance(4);
            engine.PollOnce(_clock.Now);

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(RoomState.Waiting, runtime.State);
            Assert.Equal("playback command was not acknowledged", runtime.Error);
        }

        [Fact]
        public void PendingCommandFailure_AutomaticallyRetriesWhenBothClientsRemainReady()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 0));

            engine.PollOnce(_clock.Now); // initial pause commands
            _clock.Advance(3);
            engine.PollOnce(_clock.Now); // first retry
            _clock.Advance(4);
            var failed = engine.PollOnce(_clock.Now).Single();

            Assert.Equal(RoomState.Waiting, failed.State);
            Assert.Equal("playback command was not acknowledged", failed.Error);
            var issuedBeforeRetry = _issuer.Issued.Count;

            // Cooldown prevents an immediate command storm.
            _clock.Advance(SyncConstants.AutomaticBarrierRetryDelaySeconds - 0.1);
            var coolingDown = engine.PollOnce(_clock.Now).Single();
            Assert.Equal(RoomState.Waiting, coolingDown.State);
            Assert.Equal("playback command was not acknowledged", coolingDown.Error);
            Assert.Equal(issuedBeforeRetry, _issuer.Issued.Count);

            // Once the cooldown expires, both ready clients automatically get a
            // fresh barrier without the manual resync action.
            _clock.Advance(0.1);
            var retried = engine.PollOnce(_clock.Now).Single();
            Assert.Equal(RoomState.Barrier, retried.State);
            Assert.Null(retried.Error);
            Assert.True(_issuer.Issued.Count > issuedBeforeRetry);
            Assert.Equal(2, _messageIssuer.Issued.Count);
            Assert.Equal(new[] { "u1", "u2" }, _messageIssuer.Issued.Select(message => message.userId).OrderBy(userId => userId));
            Assert.All(_messageIssuer.Issued, message =>
                Assert.True(
                    message.text.Contains("自动") || message.text.Contains("重新同步"),
                    $"Unexpected automatic retry message: {message.text}"));

            // Later barrier polling must not repeat the automatic retry notice.
            engine.PollOnce(_clock.Now);
            Assert.Equal(2, _messageIssuer.Issued.Count);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AutomaticRetry_MessageFailure_DoesNotBlockBarrier(bool throwException)
        {
            var room = CreateRoom();
            var messageIssuer = new RecordingMessageIssuer
            {
                ReturnFalse = !throwException,
                ThrowOnIssue = throwException,
            };
            var engine = CreateEngine(messageIssuer: messageIssuer);
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 0));

            engine.PollOnce(_clock.Now);
            _clock.Advance(3);
            engine.PollOnce(_clock.Now);
            _clock.Advance(4);
            Assert.Equal(RoomState.Waiting, engine.PollOnce(_clock.Now).Single().State);

            _clock.Advance(SyncConstants.AutomaticBarrierRetryDelaySeconds);
            var retried = engine.PollOnce(_clock.Now).Single();

            Assert.Equal(RoomState.Barrier, retried.State);
            Assert.Equal(2, messageIssuer.Issued.Count);
        }

        [Fact]
        public void Resync_AfterError_AllowsBarrierAgain()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 0));

            engine.PollOnce(_clock.Now);
            _clock.Advance(3);
            engine.PollOnce(_clock.Now);
            _clock.Advance(4);
            engine.PollOnce(_clock.Now);
            Assert.Equal(RoomState.Waiting, _rooms.GetRuntime(room.Id).State);
            Assert.NotNull(_rooms.GetRuntime(room.Id).Error);

            _rooms.Action(room.Id, "resync", new Dictionary<string, SessionSnapshot>(), null, _clock.Now);
            var issuedBefore = _issuer.Issued.Count;
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 0));
            engine.PollOnce(_clock.Now);

            Assert.Equal(RoomState.Barrier, _rooms.GetRuntime(room.Id).State);
            Assert.True(_issuer.Issued.Count > issuedBefore);
        }

        private SyncEngine CreateEngine(
            string serverId = "server-1",
            bool pauseOtherOnPlaybackStop = true,
            bool notifyOtherOnPlaybackStop = true,
            IMessageIssuer messageIssuer = null)
        {
            return new SyncEngine(
                _rooms, _provider, _issuer, () => serverId, () => _clock.Now,
                pollIntervalSeconds: 1.0,
                pauseOtherOnPlaybackStop: pauseOtherOnPlaybackStop,
                notifyOtherOnPlaybackStop: notifyOtherOnPlaybackStop,
                messageIssuer: messageIssuer ?? _messageIssuer);
        }

        private Room CreateRoom()
        {
            return _rooms.CreateRoom(
                "server-1", "http://emby", "room", "admin-1",
                new[] { "u1", "u2" }, "u1");
        }

        private void EnterWatching(SyncEngine engine, Room room)
        {
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 50 * SessionSnapshot.TicksPerSecond));
            engine.PollOnce(_clock.Now);
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Equal(RoomState.Watching, _rooms.GetRuntime(room.Id).State);
            _issuer.Issued.Clear();
        }

        private void SetCandidates(params SessionSnapshot[] snapshots)
        {
            _provider.Snapshots = snapshots.ToList();
        }

        private static SessionSnapshot Snapshot(
            string sessionId,
            string userId,
            bool paused,
            long position,
            string itemId = "i1",
            bool stopped = false,
            double playbackRate = 1.0)
        {
            return new SessionSnapshot(
                sessionId, userId, itemId, "m1",
                position, 100 * SessionSnapshot.TicksPerSecond, paused, playbackRate,
                stopped: stopped, supportsRemoteControl: true,
                new SessionCapabilityReport(true, new[] { "Pause", "Unpause", "Seek" }),
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        }

        private sealed class TestClock
        {
            public DateTimeOffset Now { get; private set; } =
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            public void Advance(double seconds)
            {
                Now = Now.AddSeconds(seconds);
            }
        }

        private sealed class CountingSnapshotProvider : ISessionSnapshotProvider
        {
            private int _count;

            public int Count => Volatile.Read(ref _count);

            public IReadOnlyList<SessionSnapshot> GetSessionSnapshots()
            {
                Interlocked.Increment(ref _count);
                return Array.Empty<SessionSnapshot>();
            }

            public bool WaitForCount(int expected, TimeSpan timeout)
            {
                var deadline = DateTime.UtcNow + timeout;
                while (Count < expected && DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(10);
                }

                return Count >= expected;
            }
        }

        private sealed class FakeSnapshotProvider : ISessionSnapshotProvider
        {
            public List<SessionSnapshot> Snapshots { get; set; } = new List<SessionSnapshot>();

            public IReadOnlyList<SessionSnapshot> GetSessionSnapshots() => Snapshots;
        }

        private sealed class RecordingMessageIssuer : IMessageIssuer
        {
            public List<(string userId, string header, string text)> Issued { get; } =
                new List<(string, string, string)>();

            public bool ReturnFalse { get; set; }

            public bool ThrowOnIssue { get; set; }

            public bool TryIssueMessage(
                string roomId,
                string controllingUserId,
                string userId,
                SessionSnapshot snapshot,
                string header,
                string text,
                int? timeoutMs,
                DateTimeOffset now,
                out string error)
            {
                Issued.Add((userId, header, text));
                if (ThrowOnIssue)
                {
                    throw new InvalidOperationException("message delivery failed");
                }

                error = ReturnFalse ? "message delivery failed" : null;
                return !ReturnFalse;
            }
        }

        private sealed class RecordingIssuer : ICommandIssuer
        {
            public List<(string userId, string command, long? positionTicks)> Issued { get; } =
                new List<(string, string, long?)>();

            public bool TryIssue(
                string roomId,
                string controllingUserId,
                string userId,
                SessionSnapshot snapshot,
                string command,
                long? positionTicks,
                DateTimeOffset now,
                out string error)
            {
                Issued.Add((userId, command, positionTicks));
                error = null;
                return true;
            }
        }
    }
}
