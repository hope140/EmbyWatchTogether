using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
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
        public void UpdateOptions_WakesLoopImmediately_WithNormalizedInterval()
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
                pollIntervalSeconds: 60.0);

            try
            {
                engine.Start();
                Assert.True(provider.WaitForCount(1, TimeSpan.FromSeconds(2)));
                var countBeforeUpdate = provider.Count;

                engine.UpdateOptions(new SyncEngineOptions(0.01, true, true));

                Assert.True(provider.WaitForCount(
                    countBeforeUpdate + 1,
                    TimeSpan.FromSeconds(2)));
            }
            finally
            {
                engine.Dispose();
            }
        }

        [Fact]
        public void UpdateOptions_ChangesStopBehaviorOnTheNextPoll()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            try
            {
                EnterWatching(engine, room);
                engine.UpdateOptions(new SyncEngineOptions(1.0, false, false));
                SetCandidates(
                    Snapshot("s1", "u1", paused: false, position: 0, stopped: true),
                    Snapshot("s2", "u2", paused: false, position: 60 * SessionSnapshot.TicksPerSecond));

                _clock.Advance(1);
                var result = engine.PollOnce(_clock.Now).Single();

                Assert.Equal(RoomState.Watching, result.State);
                Assert.Null(result.Error);
                Assert.DoesNotContain(_issuer.Issued, i => i.command == RemoteCommands.Pause);
                Assert.Empty(_messageIssuer.Issued);

                _clock.Advance(2);
                result = engine.PollOnce(_clock.Now).Single();
                Assert.Equal(RoomState.Waiting, result.State);
                Assert.Equal("播放已停止，等待双方重新打开同一视频", result.Error);
            }
            finally
            {
                engine.Dispose();
            }
        }

        [Fact]
        public void PollOnce_IsolatesRoomExceptionAndContinuesWithOtherRooms()
        {
            var rooms = new RoomManager();
            var first = rooms.CreateRoom(
                "server-1", "http://emby", "first", "admin-1",
                new[] { "u1", "u2" }, "u1");
            var second = rooms.CreateRoom(
                "server-1", "http://emby", "second", "admin-2",
                new[] { "u3", "u4" }, "u3");
            var provider = new FakeSnapshotProvider
            {
                Snapshots = new List<SessionSnapshot>
                {
                    Snapshot("s1", "u1", paused: false, position: 0),
                    Snapshot("s2", "u2", paused: false, position: 0),
                    Snapshot("s3", "u3", paused: false, position: 0),
                    Snapshot("s4", "u4", paused: false, position: 0),
                },
            };
            var issuer = new ThrowOnceIssuer();
            var engine = new SyncEngine(
                rooms,
                provider,
                issuer,
                () => "server-1",
                () => _clock.Now);

            try
            {
                var results = engine.PollOnce(_clock.Now);

                Assert.NotNull(issuer.FailedRoomId);
                Assert.Contains(results, result =>
                    result.RoomId != issuer.FailedRoomId &&
                    (result.RoomId == first.Id || result.RoomId == second.Id));
                Assert.Contains(issuer.Issued, issue => issue.roomId != issuer.FailedRoomId);
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
        public void EligibilityDiagnostics_LogOnReasonChangeOnly_AndResumeAfterRecovery()
        {
            var room = CreateRoom();
            var warnings = new List<string>();
            var logManager = CreateLogManager(warnings);
            var engine = CreateEngine(logManager: logManager.Object);

            SetCandidates(Snapshot("session-one", "u1", paused: false, position: 0));
            engine.PollOnce(_clock.Now);
            engine.PollOnce(_clock.Now);
            Assert.Single(warnings);
            Assert.Contains("reason=MissingSnapshot", warnings[0]);
            Assert.Contains("missing=u2", warnings[0]);

            SetCandidates(
                Snapshot("session-one", "u1", paused: false, position: 0, itemId: "item-a"),
                Snapshot("session-two", "u2", paused: false, position: 0, itemId: "item-b"));
            engine.PollOnce(_clock.Now);
            Assert.Equal(2, warnings.Count);
            Assert.Contains("reason=EmptyOrDifferentItem", warnings[1]);

            SetCandidates(
                Snapshot("session-one", "u1", paused: false, position: 0),
                Snapshot("session-two", "u2", paused: false, position: 0));
            engine.PollOnce(_clock.Now);
            SetCandidates(
                Snapshot("session-one", "u1", paused: false, position: 0, itemId: "item-a"),
                Snapshot("session-two", "u2", paused: false, position: 0, itemId: "item-b"));
            engine.PollOnce(_clock.Now);

            Assert.Equal(3, warnings.Count);
            Assert.Contains("reason=EmptyOrDifferentItem", warnings[2]);
            Assert.DoesNotContain("http://emby", string.Join("\n", warnings));
        }

        [Fact]
        public void EligibilityDiagnostics_ResetToWaitingDoesNotForgetSameReason()
        {
            var room = CreateRoom();
            var warnings = new List<string>();
            var logManager = CreateLogManager(warnings);
            var engine = CreateEngine(logManager: logManager.Object);
            EnterWatching(engine, room);

            SetCandidates(
                Snapshot("session-one", "u1", paused: false, position: 0, itemId: "item-a"),
                Snapshot("session-two", "u2", paused: false, position: 0, itemId: "item-b"));
            engine.PollOnce(_clock.Now);
            engine.PollOnce(_clock.Now);

            Assert.Single(warnings);
            Assert.Contains("reason=EmptyOrDifferentItem", warnings[0]);
            Assert.Equal(RoomState.Waiting, _rooms.GetRuntime(room.Id).State);

            SetCandidates(
                Snapshot("session-one", "u1", paused: false, position: 0),
                Snapshot("session-two", "u2", paused: false, position: 0));
            engine.PollOnce(_clock.Now);
            SetCandidates(
                Snapshot("session-one", "u1", paused: false, position: 0, itemId: "item-a"),
                Snapshot("session-two", "u2", paused: false, position: 0, itemId: "item-b"));
            engine.PollOnce(_clock.Now);

            Assert.Equal(2, warnings.Count);
            Assert.Contains("reason=EmptyOrDifferentItem", warnings[1]);
        }

        [Fact]
        public void CommandDiagnostics_DistinguishImmediateFailureFromAcceptedUnacknowledged()
        {
            var room = CreateRoom();
            var warnings = new List<string>();
            var infos = new List<string>();
            var logManager = CreateLogManager(warnings, infos);
            var engine = CreateEngine(logManager: logManager.Object);
            SetCandidates(
                Snapshot("session-one", "u1", paused: false, position: 0),
                Snapshot("session-two", "u2", paused: false, position: 0));

            _issuer.FailuresRemaining = 1;
            _issuer.FailureMessage = "X-Emby-Token=secret&api_key=secret";
            engine.PollOnce(_clock.Now);
            Assert.Contains(warnings, warning => warning.Contains("immediate-issue-failure") &&
                warning.Contains("command=Pause") && warning.Contains("targetUser=u1"));
            Assert.DoesNotContain(warnings, warning => warning.Contains("accepted-but-unacknowledged"));
            Assert.DoesNotContain("X-Emby-Token=secret", string.Join("\n", warnings.Concat(infos)));
            Assert.DoesNotContain("api_key=secret", string.Join("\n", warnings.Concat(infos)));

            warnings.Clear();
            _issuer.FailuresRemaining = 0;
            engine.PollOnce(_clock.Now);
            _clock.Advance(3);
            engine.PollOnce(_clock.Now);
            _clock.Advance(4);
            engine.PollOnce(_clock.Now);
            _clock.Advance(4);
            engine.PollOnce(_clock.Now);

            Assert.Contains(warnings, warning => warning.Contains("accepted-but-unacknowledged") &&
                warning.Contains("command=Pause") && warning.Contains("targetUser=u2") &&
                warning.Contains("paused=") && warning.Contains("position="));
            Assert.DoesNotContain(warnings, warning => warning.Contains("http://emby"));
        }

        [Fact]
        public void PollExceptionDiagnostics_DoNotLogExceptionMessage()
        {
            var room = CreateRoom();
            var warnings = new List<string>();
            var logManager = CreateLogManager(warnings);
            var engine = CreateEngine(logManager: logManager.Object);
            _issuer.ThrowOnIssue = true;
            _issuer.ExceptionMessage = "X-Emby-Token=secret&api_key=secret";
            SetCandidates(
                Snapshot("session-one", "u1", paused: false, position: 0),
                Snapshot("session-two", "u2", paused: false, position: 0));

            engine.PollOnce(_clock.Now);

            Assert.Contains(warnings, warning => warning.Contains("poll failed"));
            Assert.DoesNotContain("X-Emby-Token=secret", string.Join("\n", warnings));
            Assert.DoesNotContain("api_key=secret", string.Join("\n", warnings));
        }

        [Fact]
        public async Task PollOnce_RoomDeletedAfterRuntimeLookup_DoesNotIssueCommands()
        {
            var room = CreateRoom();
            var provider = new BlockingSnapshotProvider(new[]
            {
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 0),
            });
            var engine = new SyncEngine(
                _rooms,
                provider,
                _issuer,
                () => "server-1",
                () => _clock.Now,
                messageIssuer: _messageIssuer);

            var pollTask = Task.Run(() => engine.PollOnce(_clock.Now));
            try
            {
                await provider.EnteredTask.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.True(_rooms.DeleteRoom(room.Id));
            }
            finally
            {
                provider.Release.Set();
            }

            try
            {
                Assert.Empty(await pollTask);
                Assert.Empty(_issuer.Issued);
                Assert.Null(_rooms.GetRuntime(room.Id));
            }
            finally
            {
                engine.Dispose();
            }
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
        public void Barrier_EntersWatchingAfterRestore_WithoutSecondSeek()
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
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Equal(RoomState.Watching, _rooms.GetRuntime(room.Id).State);
            Assert.Equal(5, _issuer.Issued.Count);
            Assert.Equal(1, _issuer.Issued.Count(i => i.command == RemoteCommands.Seek));
        }

        [Fact]
        public void PendingSeek_DifferentItem_DropsWithoutRetry()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 0));

            engine.PollOnce(_clock.Now);
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 0));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.True(runtime.Pending.TryGetValue("u2", out var pending));
            Assert.Equal(RemoteCommands.Seek, pending.Command);
            Assert.Equal("s2", pending.SessionId);
            Assert.Equal("i1", pending.ItemId);
            _issuer.Issued.Clear();

            // Both users changed items before the old seek timed out. The
            // safety pause for a mismatched pair is allowed, but the old seek
            // must not be retried against the new item.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond, itemId: "i2"),
                Snapshot("s2", "u2", paused: false, position: 0, itemId: "i3"));
            _clock.Advance(3);
            var result = engine.PollOnce(_clock.Now).Single();

            Assert.Equal(RoomState.Waiting, result.State);
            Assert.Contains("不同视频", result.Error);
            Assert.DoesNotContain(_issuer.Issued, i => i.command == RemoteCommands.Seek);
            Assert.DoesNotContain(runtime.Pending.Values, pending => pending.Command == RemoteCommands.Seek);
            Assert.Null(runtime.Barrier);
        }

        [Fact]
        public void PendingAndSuppressed_CaptureSessionAndItemIdentity()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 0));

            engine.PollOnce(_clock.Now);
            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal("s1", runtime.Pending["u1"].SessionId);
            Assert.Equal("i1", runtime.Pending["u1"].ItemId);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 0),
                Snapshot("s2", "u2", paused: true, position: 0));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Equal("s1", runtime.Suppressed["u1"].SessionId);
            Assert.Equal("i1", runtime.Suppressed["u1"].ItemId);
        }

        [Fact]
        public void Barrier_ImmediateIssueFailure_SchedulesRetryBeforeAdvancingStage()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            _issuer.FailuresRemaining = 1;
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 0));

            var failed = engine.PollOnce(_clock.Now).Single();
            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(RoomState.Waiting, failed.State);
            Assert.NotNull(failed.Error);
            Assert.NotNull(runtime.BarrierRetryAtUtc);
            Assert.Null(runtime.Barrier);

            int issuedBeforeRetry = _issuer.Issued.Count;
            _clock.Advance(SyncConstants.AutomaticBarrierRetryDelaySeconds - 0.1);
            engine.PollOnce(_clock.Now);
            Assert.Equal(issuedBeforeRetry, _issuer.Issued.Count);

            _clock.Advance(0.1);
            var retried = engine.PollOnce(_clock.Now).Single();
            Assert.Equal(RoomState.Barrier, retried.State);
            Assert.True(runtime.Barrier.PauseSent);
            Assert.All(runtime.Pending.Values, pending => Assert.Equal(0, pending.Retries));
            Assert.Contains(_issuer.Issued, i =>
                i.userId == "u1" && i.sessionId == "s1" && i.itemId == "i1");
        }

        [Fact]
        public void SeekFailure_PreservesBarrier_FreezesAndRetriesOriginalTarget()
        {
            var room = CreateRoom();
            var engine = CreateEngine(notifyOnSyncActions: true);
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 0));
            engine.PollOnce(_clock.Now);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 0));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            var runtime = _rooms.GetRuntime(room.Id);
            var originalBarrier = runtime.Barrier;
            Assert.Equal(RemoteCommands.Seek, runtime.Pending["u2"].Command);

            _clock.Advance(SyncConstants.PendingTimeoutSeconds + 0.1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(SyncConstants.PendingTimeoutSeconds + SyncConstants.PendingRetryGraceSeconds + 1.1);
            engine.PollOnce(_clock.Now);

            Assert.Equal(RoomState.Barrier, runtime.State);
            Assert.Same(originalBarrier, runtime.Barrier);
            Assert.Equal(50 * SessionSnapshot.TicksPerSecond, runtime.Barrier.PrimaryPositionTicks);
            Assert.NotNull(runtime.Barrier.SeekRetryAtUtc);
            var issuedBeforeCooldown = _issuer.Issued.Count;

            // The anchor resumes and moves during cooldown. It is paused again,
            // but the failed seek target remains the original locked position.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 51 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 0));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            Assert.Equal(issuedBeforeCooldown + 1, _issuer.Issued.Count);
            Assert.Equal(RemoteCommands.Pause, _issuer.Issued.Last().command);
            Assert.Equal("u1", _issuer.Issued.Last().userId);
            Assert.Equal(50 * SessionSnapshot.TicksPerSecond, runtime.Barrier.PrimaryPositionTicks);

            _clock.Advance(SyncConstants.AutomaticBarrierRetryDelaySeconds - 1.1);
            engine.PollOnce(_clock.Now);
            Assert.DoesNotContain(_issuer.Issued.Skip(issuedBeforeCooldown + 1), i => i.command == RemoteCommands.Seek);
            Assert.Empty(_messageIssuer.Issued);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 51 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 0));
            _clock.Advance(0.1);
            engine.PollOnce(_clock.Now);
            Assert.Equal(issuedBeforeCooldown + 2, _issuer.Issued.Count);
            Assert.Equal(RemoteCommands.Pause, _issuer.Issued.Last().command);
            Assert.Equal("u2", _issuer.Issued.Last().userId);
            Assert.Empty(_messageIssuer.Issued);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 51 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 0));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            var retrySeek = _issuer.Issued.Last(i => i.command == RemoteCommands.Seek);
            Assert.Equal(50 * SessionSnapshot.TicksPerSecond, retrySeek.positionTicks);
            Assert.Equal(2, _messageIssuer.Issued.Count);
            Assert.Equal(new[] { "u1", "u2" }, _messageIssuer.Issued.Select(message => message.userId).OrderBy(userId => userId));

            engine.PollOnce(_clock.Now);
            Assert.Equal(2, _messageIssuer.Issued.Count);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            Assert.Equal(2, _issuer.Issued.Count(i => i.command == RemoteCommands.Unpause));

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 60 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            Assert.Equal(RoomState.Watching, runtime.State);
        }

        [Fact]
        public void SeekImmediateIssueFailure_PreservesBarrierUntilCooldownRetry()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 0));
            engine.PollOnce(_clock.Now);
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 0));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _issuer.FailuresRemaining = 1;

            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(RoomState.Barrier, runtime.State);
            Assert.Equal(50 * SessionSnapshot.TicksPerSecond, runtime.Barrier.PrimaryPositionTicks);
            Assert.NotNull(runtime.Barrier.SeekRetryAtUtc);
            Assert.False(runtime.Pending.ContainsKey("u2"));
        }

        [Fact]
        public void BarrierSeek_AnchorJumpDuringRetry_RebuildsInsteadOfRestoringOldTarget()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 0));
            engine.PollOnce(_clock.Now);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 0),
                Snapshot("s2", "u2", paused: true, position: 10 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // enter Seek

            _issuer.FailuresRemaining = 1;
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // Seek fails and enters cooldown

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 1729 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 0));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(RoomState.Barrier, runtime.State);
            Assert.Equal(BarrierStage.Pause, runtime.Barrier.Stage);
            Assert.Equal(1729 * SessionSnapshot.TicksPerSecond, runtime.Barrier.PrimaryPositionTicks);
            Assert.DoesNotContain(_issuer.Issued, issued => issued.command == RemoteCommands.Unpause);

            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            Assert.DoesNotContain(_issuer.Issued, issued => issued.command == RemoteCommands.Unpause);
            Assert.Contains(_issuer.Issued, issued => issued.command == RemoteCommands.Pause);
        }

        [Fact]
        public void BarrierSeek_AnchorUnpauseJump_PauseAcknowledgementPromotesCandidate()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 0));
            engine.PollOnce(_clock.Now);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 0),
                Snapshot("s2", "u2", paused: true, position: 10 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // enter Seek

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 1729 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 10 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // record candidate and issue Pause

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(0, runtime.Barrier.PrimaryPositionTicks);
            Assert.Equal(
                1729 * SessionSnapshot.TicksPerSecond,
                runtime.Barrier.AnchorPositionCandidateTicks);
            Assert.Contains(_issuer.Issued, issued =>
                issued.userId == "u1" && issued.command == RemoteCommands.Pause);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 1729 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 10 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Equal(RoomState.Barrier, runtime.State);
            Assert.Equal(BarrierStage.Pause, runtime.Barrier.Stage);
            Assert.Equal(1729 * SessionSnapshot.TicksPerSecond, runtime.Barrier.PrimaryPositionTicks);
            Assert.Null(runtime.Barrier.AnchorPositionCandidateTicks);
            Assert.DoesNotContain(_issuer.Issued, issued => issued.command == RemoteCommands.Unpause);

            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // new barrier issues Pause as its hold
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // both Pause commands acknowledge; enter Seek
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // seek follower to the promoted target

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 1729 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 1729 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // Seek acknowledges; enter Restore
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // intended playing state issues Unpause
            Assert.Contains(_issuer.Issued, issued => issued.command == RemoteCommands.Unpause);

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 1729 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 1729 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            Assert.Equal(RoomState.Watching, runtime.State);
            Assert.Null(runtime.Barrier);
        }

        [Fact]
        public void BarrierSeek_AnchorCandidateUpdatesWhileStillPlaying()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 0));
            engine.PollOnce(_clock.Now);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 0),
                Snapshot("s2", "u2", paused: true, position: 10 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // enter Seek

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 1729 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 10 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // create candidate and issue Pause

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 1732 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 10 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // update candidate before Pause acknowledgement

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(1732 * SessionSnapshot.TicksPerSecond, runtime.Barrier.AnchorPositionCandidateTicks);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 1732 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 10 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Equal(BarrierStage.Pause, runtime.Barrier.Stage);
            Assert.Equal(1732 * SessionSnapshot.TicksPerSecond, runtime.Barrier.PrimaryPositionTicks);
        }

        [Fact]
        public void BarrierSeek_PausedAnchorSeek_PreservesPausedIntentAfterRebuild()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 0));
            engine.PollOnce(_clock.Now);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 0),
                Snapshot("s2", "u2", paused: true, position: 10 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // enter Seek

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 1729 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 10 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // rebuild from paused anchor seek

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(BarrierStage.Pause, runtime.Barrier.Stage);
            Assert.True(runtime.Barrier.PrimaryPaused);
            Assert.Equal(1729 * SessionSnapshot.TicksPerSecond, runtime.Barrier.PrimaryPositionTicks);

            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // new barrier issues Pause hold
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // Pause acknowledgement enters Seek
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // seek follower

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 1729 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 1729 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // Seek acknowledgement enters Restore
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // Restore keeps both paused
            Assert.DoesNotContain(_issuer.Issued, issued => issued.command == RemoteCommands.Unpause);

            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            var result = engine.PollOnce(_clock.Now).Single();
            Assert.Equal(RoomState.Watching, result.State);
        }

        [Fact]
        public void BarrierSeek_AnchorPauseLandingSmallMovement_KeepsOriginalTarget()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 0));
            engine.PollOnce(_clock.Now);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 0),
                Snapshot("s2", "u2", paused: true, position: 10 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // enter Seek

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 1 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 10 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // Pause is issued for the small movement

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 1 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 10 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // Pause lands; original target remains locked

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(RoomState.Barrier, runtime.State);
            Assert.Equal(BarrierStage.Seek, runtime.Barrier.Stage);
            Assert.Equal(0, runtime.Barrier.PrimaryPositionTicks);
            Assert.Null(runtime.Barrier.AnchorPositionCandidateTicks);
            Assert.Contains(_issuer.Issued, issued =>
                issued.command == RemoteCommands.Seek &&
                issued.positionTicks == 0);
            Assert.DoesNotContain(_issuer.Issued, issued => issued.command == RemoteCommands.Unpause);
        }

        [Fact]
        public void BarrierSeek_SmallPausedAnchorAdvance_KeepsOriginalTarget()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 0));
            engine.PollOnce(_clock.Now);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 0),
                Snapshot("s2", "u2", paused: true, position: 10 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // enter Seek

            _issuer.FailuresRemaining = 1;
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // fail the original target Seek

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 1 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 10 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // small anchor movement is not a re-anchor

            _clock.Advance(SyncConstants.AutomaticBarrierRetryDelaySeconds - 1);
            engine.PollOnce(_clock.Now);
            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(0, runtime.Barrier.PrimaryPositionTicks);

            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            Assert.Equal(0, _issuer.Issued.Last(issued => issued.command == RemoteCommands.Seek).positionTicks);
            Assert.Equal(0, runtime.Barrier.PrimaryPositionTicks);
        }

        [Fact]
        public void BarrierSeek_RestoreRequiresAnchorAndFollowerWithinTolerance()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 0));
            engine.PollOnce(_clock.Now);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 0));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // enter Seek
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // issue Seek

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 53 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(RoomState.Barrier, runtime.State);
            Assert.Equal(BarrierStage.Seek, runtime.Barrier.Stage);
            Assert.Equal(50 * SessionSnapshot.TicksPerSecond, runtime.Barrier.PrimaryPositionTicks);
            Assert.DoesNotContain(_issuer.Issued, issued => issued.command == RemoteCommands.Unpause);
        }

        [Fact]
        public void BarrierSeek_IdentityChangeResetsInsteadOfReusingAnchor()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0),
                Snapshot("s2", "u2", paused: false, position: 0));
            engine.PollOnce(_clock.Now);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 0),
                Snapshot("s2", "u2", paused: true, position: 10 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // enter Seek
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // issue Seek

            _issuer.Issued.Clear();
            SetCandidates(
                Snapshot("s1-reconnected", "u1", paused: true, position: 1729 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 0));
            _clock.Advance(1);
            var result = engine.PollOnce(_clock.Now).Single();

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(RoomState.Barrier, result.State);
            Assert.Equal("s1-reconnected", runtime.Barrier.SessionIds["u1"]);
            Assert.Equal("s1-reconnected", runtime.Pending["u1"].SessionId);
            Assert.DoesNotContain(_issuer.Issued, issued => issued.command == RemoteCommands.Unpause);
        }

        [Fact]
        public void BarrierSeek_MixedSeekAndPauseFailure_UsesFullBarrierRetry()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 0));
            engine.PollOnce(_clock.Now); // initial barrier pause commands

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 0));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // pause acknowledgement enters Seek
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // Seek is pending for u2

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(BarrierStage.Seek, runtime.Barrier.Stage);
            Assert.Equal(RemoteCommands.Seek, runtime.Pending["u2"].Command);

            // Reproduce a same-round Seek + Pause failure. The Pause is the
            // hold request issued while the anchor resumes during Barrier Seek.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 0));
            DateTimeOffset expiredAt = _clock.Now.AddSeconds(
                -(SyncConstants.PendingTimeoutSeconds + 1));
            runtime.Pending["u2"].IssuedAtUtc = expiredAt;
            runtime.Pending["u1"] = new PendingCommand
            {
                UserId = "u1",
                SessionId = "s1",
                ItemId = "i1",
                Command = RemoteCommands.Pause,
                IssuedAtUtc = expiredAt,
            };
            _issuer.FailuresRemaining = 2;

            _clock.Advance(1);
            var failed = engine.PollOnce(_clock.Now).Single();

            Assert.Equal(RoomState.Waiting, failed.State);
            Assert.Equal("playback command was not acknowledged", failed.Error);
            Assert.Null(runtime.Barrier);
            Assert.NotNull(runtime.BarrierRetryAtUtc);
        }

        [Fact]
        public void BarrierSeek_ImmediateFailures_StopAtSharedRetryBudget()
        {
            var room = CreateRoom();
            var engine = CreateEngine(notifyOnSyncActions: true);
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 0));
            engine.PollOnce(_clock.Now);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 0));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // enter Seek after Pause acknowledgement

            _issuer.FailuresRemaining = 1000;
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // first immediate Seek failure

            var runtime = _rooms.GetRuntime(room.Id);
            int seekCountAtFailure = _issuer.Issued.Count(i => i.command == RemoteCommands.Seek);
            int messageCountAtFailure = _messageIssuer.Issued.Count;
            for (int attempt = 0; attempt < 20 && runtime.State == RoomState.Barrier; attempt++)
            {
                _clock.Advance(SyncConstants.AutomaticBarrierRetryDelaySeconds);
                engine.PollOnce(_clock.Now);
            }

            Assert.Equal(RoomState.Waiting, runtime.State);
            Assert.Contains("seek", runtime.Error.ToLowerInvariant());
            Assert.Contains("budget", runtime.Error.ToLowerInvariant());
            Assert.Equal(seekCountAtFailure + 4, _issuer.Issued.Count(i => i.command == RemoteCommands.Seek));

            _clock.Advance(100);
            engine.PollOnce(_clock.Now);
            Assert.Equal(seekCountAtFailure + 4, _issuer.Issued.Count(i => i.command == RemoteCommands.Seek));
            Assert.Equal(messageCountAtFailure + 8, _messageIssuer.Issued.Count);
        }

        [Fact]
        public void BarrierSeek_UnconfirmedAttempts_StopAtSharedRetryBudget()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 0));
            engine.PollOnce(_clock.Now);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 0));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // enter Seek
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // initial Seek remains unconfirmed

            var runtime = _rooms.GetRuntime(room.Id);
            for (int attempt = 0; attempt < 40 && runtime.State == RoomState.Barrier; attempt++)
            {
                _clock.Advance(1);
                engine.PollOnce(_clock.Now);
            }

            Assert.Equal(RoomState.Waiting, runtime.State);
            Assert.Contains("seek", runtime.Error.ToLowerInvariant());
            Assert.Contains("budget", runtime.Error.ToLowerInvariant());
            int seekCountAtFailure = _issuer.Issued.Count(i => i.command == RemoteCommands.Seek);
            int messageCountAtFailure = _messageIssuer.Issued.Count;

            _clock.Advance(100);
            engine.PollOnce(_clock.Now);
            Assert.Equal(seekCountAtFailure, _issuer.Issued.Count(i => i.command == RemoteCommands.Seek));
            Assert.Equal(messageCountAtFailure, _messageIssuer.Issued.Count);
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
        public void PrimaryPositionResetToZero_IsNotTreatedAsStop()
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
            Assert.Equal(RoomState.Barrier, result.State);
            Assert.DoesNotContain("播放已停止", result.Error ?? string.Empty);
            Assert.NotNull(runtime.Barrier);

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

            Assert.Equal(RoomState.Watching, result.State);
            Assert.Empty(_issuer.Issued);

            _clock.Advance(1);
            result = engine.PollOnce(_clock.Now).Single();
            _clock.Advance(1);
            result = engine.PollOnce(_clock.Now).Single();
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
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            var message = Assert.Single(_messageIssuer.Issued);
            Assert.Equal("u2", message.userId);
            Assert.Contains("对方已停止播放", message.text);
            Assert.Equal(3000, message.timeoutMs);
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
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Single(_messageIssuer.Issued);
        }

        [Fact]
        public void TransientStoppedSnapshot_RecoversBeforeDebounceWithoutSideEffects()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond, stopped: true),
                Snapshot("s2", "u2", paused: false, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            var result = engine.PollOnce(_clock.Now).Single();

            Assert.Equal(RoomState.Watching, result.State);
            Assert.Empty(_issuer.Issued);
            Assert.Empty(_messageIssuer.Issued);

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            result = engine.PollOnce(_clock.Now).Single();

            Assert.Equal(RoomState.Watching, result.State);
            Assert.Null(result.Error);
            Assert.Empty(_issuer.Issued);
            Assert.Empty(_messageIssuer.Issued);
        }

        [Fact]
        public void OldStoppedCandidate_IsIgnoredWhenCurrentCommonItemIsSelected()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);
            var baseActivity = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            // The old stopped session is newer than u1's current snapshot and
            // has the same item, but its stale session lacks remote control.
            // SessionSelector must still retain the current s1 session.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond,
                    itemId: "i1", lastActivityDateUtc: baseActivity),
                Snapshot("old-s1", "u1", paused: false, position: 0,
                    itemId: "i1", stopped: true, supportsRemoteControl: false,
                    lastActivityDateUtc: baseActivity.AddSeconds(10)),
                Snapshot("s2", "u2", paused: false, position: 50 * SessionSnapshot.TicksPerSecond,
                    itemId: "i1", lastActivityDateUtc: baseActivity));
            _clock.Advance(1);
            var result = engine.PollOnce(_clock.Now).Single();

            Assert.Equal(RoomState.Watching, result.State);
            Assert.Empty(_issuer.Issued);
            Assert.Empty(_messageIssuer.Issued);

            _clock.Advance(3);
            result = engine.PollOnce(_clock.Now).Single();
            Assert.Equal(RoomState.Watching, result.State);
            Assert.Empty(_issuer.Issued);
            Assert.Empty(_messageIssuer.Issued);
        }

        [Fact]
        public void TransientMissingParticipant_RecoversBeforeDebounceWithoutBarrier()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);

            SetCandidates(Snapshot("s2", "u2", paused: false, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            var result = engine.PollOnce(_clock.Now).Single();

            Assert.Equal(RoomState.Watching, result.State);
            Assert.Empty(_issuer.Issued);

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            result = engine.PollOnce(_clock.Now).Single();

            Assert.Equal(RoomState.Watching, result.State);
            Assert.Null(result.Error);
            Assert.Empty(_issuer.Issued);
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
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
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
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
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

            Assert.Equal(RoomState.Watching, result.State);
            Assert.Empty(_issuer.Issued);
            _clock.Advance(1);
            result = engine.PollOnce(_clock.Now).Single();
            _clock.Advance(1);
            result = engine.PollOnce(_clock.Now).Single();
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

            Assert.Equal(RoomState.Watching, result.State);
            Assert.Empty(_issuer.Issued);

            _clock.Advance(2);
            result = engine.PollOnce(_clock.Now).Single();

            Assert.Equal(RoomState.Waiting, result.State);
            Assert.Contains(_issuer.Issued, i =>
                i.userId == "u2" && i.command == RemoteCommands.Pause);
            Assert.DoesNotContain(_issuer.Issued, i => i.userId == "u1");
        }

        [Fact]
        public void PrimaryPositionReset_DoesNotTriggerStopWhenDisabled()
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
            Assert.Equal(RoomState.Barrier, result.State);
            Assert.DoesNotContain("播放已停止", result.Error ?? string.Empty);
            Assert.NotNull(runtime.Barrier);
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
        public void WatchingTick_SessionIdChange_ResetsAndRebarriers()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);
            _issuer.Issued.Clear();

            SetCandidates(
                Snapshot("s1-reconnected", "u1", paused: false, position: 51 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 51 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            var reset = engine.PollOnce(_clock.Now).Single();

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(RoomState.Waiting, reset.State);
            Assert.Null(runtime.Barrier);
            Assert.Empty(runtime.Pending);
            Assert.Empty(_issuer.Issued);

            // The next eligible poll starts a fresh barrier using the new
            // session identity instead of reusing Watching state.
            _clock.Advance(1);
            var rebarrier = engine.PollOnce(_clock.Now).Single();
            Assert.Equal(RoomState.Barrier, rebarrier.State);
            Assert.Contains(_issuer.Issued, i =>
                i.userId == "u1" && i.sessionId == "s1-reconnected" && i.itemId == "i1");
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
        public void WatchingTick_PlayingTenSecondsThenPauseAtNaturalPosition_OnlyPropagatesPause()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room, 50 * SessionSnapshot.TicksPerSecond);

            // The pause may have happened at any point during the ten-second
            // interval. 51s is inside the natural [50s, 60s] interval.
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 51 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 60 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(10);
            engine.PollOnce(_clock.Now);

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(RoomState.Watching, runtime.State);
            Assert.Null(runtime.Barrier);
            Assert.Contains(_issuer.Issued, issued =>
                issued.userId == "u2" && issued.command == RemoteCommands.Pause);
            Assert.DoesNotContain(_issuer.Issued, issued => issued.command == RemoteCommands.Seek);
        }

        [Fact]
        public void WatchingTick_PausedTenSecondsThenUnpauseAtNaturalPosition_OnlyPropagatesUnpause()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatchingPaused(engine, room, 50);

            // The resume may have happened at any point during the ten-second
            // interval. 51s is inside the natural [50s, 60s] interval.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 51 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(10);
            engine.PollOnce(_clock.Now);

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(RoomState.Watching, runtime.State);
            Assert.Null(runtime.Barrier);
            Assert.Contains(_issuer.Issued, issued =>
                issued.userId == "u2" && issued.command == RemoteCommands.Unpause);
            Assert.DoesNotContain(_issuer.Issued, issued => issued.command == RemoteCommands.Seek);
        }

        [Fact]
        public void WatchingTick_PauseTransitionsAtNaturalIntervalBoundaries_DoNotSeek()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room, 50 * SessionSnapshot.TicksPerSecond);

            // Lower boundary: playing -> paused at the previous position.
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(10);
            engine.PollOnce(_clock.Now);
            Assert.Equal(RoomState.Watching, _rooms.GetRuntime(room.Id).State);
            Assert.DoesNotContain(_issuer.Issued, issued => issued.command == RemoteCommands.Seek);

            // Acknowledge the propagated pause before testing the upper
            // boundary of the reverse transition.
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            // Upper boundary: paused -> playing at the fully elapsed natural
            // position after ten seconds.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 60 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 50 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(10);
            engine.PollOnce(_clock.Now);
            Assert.Equal(RoomState.Watching, _rooms.GetRuntime(room.Id).State);
            Assert.DoesNotContain(_issuer.Issued, issued => issued.command == RemoteCommands.Seek);
        }

        [Fact]
        public void WatchingTick_SeekAndUnpause_StartsBarrierWithPlayingIntent()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatchingPaused(engine, room, 1761);

            // The anchor resumes and seeks in one client update. The seek must
            // win over the pause transition, while the barrier remembers that
            // the final state should be playing.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 1771 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 1761 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(RoomState.Barrier, runtime.State);
            Assert.Equal("u1", runtime.Barrier.AnchorUserId);
            Assert.Equal(1771 * SessionSnapshot.TicksPerSecond, runtime.Barrier.PrimaryPositionTicks);
            Assert.False(runtime.Barrier.PrimaryPaused);
            Assert.DoesNotContain(_issuer.Issued, issued => issued.command == RemoteCommands.Unpause);

            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // issue the barrier Pause hold
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 1771 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 1761 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // enter Seek
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // seek follower to 1771

            Assert.Contains(_issuer.Issued, issued =>
                issued.userId == "u2" &&
                issued.command == RemoteCommands.Seek &&
                issued.positionTicks == 1771 * SessionSnapshot.TicksPerSecond);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 1771 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 1771 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // Seek acknowledgement enters Restore
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // restore issues Unpause
            Assert.Contains(_issuer.Issued, issued => issued.command == RemoteCommands.Unpause);

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 1771 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 1771 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            Assert.Equal(RoomState.Watching, runtime.State);
        }

        [Fact]
        public void WatchingTick_SeekAndPause_StartsBarrierWithPausedIntent()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room, 1761 * SessionSnapshot.TicksPerSecond);

            // The anchor seeks and pauses in one client update. The barrier
            // target is the new position and Restore must keep both paused.
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 1771 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 1761 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(RoomState.Barrier, runtime.State);
            Assert.Equal("u1", runtime.Barrier.AnchorUserId);
            Assert.Equal(1771 * SessionSnapshot.TicksPerSecond, runtime.Barrier.PrimaryPositionTicks);
            Assert.True(runtime.Barrier.PrimaryPaused);

            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // issue the barrier Pause hold
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 1771 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 1761 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // enter Seek
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // seek follower to 1771

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 1771 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 1771 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // Seek acknowledgement enters Restore
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // restore issues Pause
            Assert.DoesNotContain(_issuer.Issued, issued => issued.command == RemoteCommands.Unpause);

            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            Assert.Equal(RoomState.Watching, runtime.State);
        }

        [Fact]
        public void WatchingTick_NormalPauseAndUnpauseMovement_PropagatesOnlyState()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);
            _issuer.Issued.Clear();

            // Normal pause movement follows the previous playing projection.
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 51 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 51 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Equal(RoomState.Watching, _rooms.GetRuntime(room.Id).State);
            Assert.Contains(_issuer.Issued, issued =>
                issued.userId == "u2" && issued.command == RemoteCommands.Pause);
            Assert.DoesNotContain(_issuer.Issued, issued => issued.command == RemoteCommands.Seek);

            // Acknowledging that pause and resuming with normal movement also
            // stays in Watching and only propagates Unpause.
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 51 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 51 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 53 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 51 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Equal(RoomState.Watching, _rooms.GetRuntime(room.Id).State);
            Assert.Contains(_issuer.Issued, issued =>
                issued.userId == "u2" && issued.command == RemoteCommands.Unpause);
            Assert.DoesNotContain(_issuer.Issued, issued => issued.command == RemoteCommands.Seek);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void WatchingTick_RemotePauseOrUnpauseAckWithPositionMovement_DoesNotEnterSeekBarrier(bool unpause)
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            if (unpause)
            {
                EnterWatchingPaused(engine, room, 50);
                SetCandidates(
                    Snapshot("s1", "u1", paused: false, position: 50 * SessionSnapshot.TicksPerSecond),
                    Snapshot("s2", "u2", paused: true, position: 50 * SessionSnapshot.TicksPerSecond));
            }
            else
            {
                EnterWatching(engine, room);
                SetCandidates(
                    Snapshot("s1", "u1", paused: true, position: 50 * SessionSnapshot.TicksPerSecond),
                    Snapshot("s2", "u2", paused: false, position: 50 * SessionSnapshot.TicksPerSecond));
            }

            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // issue the propagated state command
            int issuedBeforeAck = _issuer.Issued.Count;

            // The remote acknowledgement lands with a deliberately large
            // position movement in the same snapshot.
            SetCandidates(
                Snapshot("s1", "u1", paused: unpause ? false : true,
                    position: (unpause ? 51 : 50) * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: unpause ? false : true,
                    position: 60 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Equal(RoomState.Watching, _rooms.GetRuntime(room.Id).State);
            Assert.Null(_rooms.GetRuntime(room.Id).Barrier);
            Assert.True(_issuer.Issued.Count >= issuedBeforeAck);
        }

        [Fact]
        public void WatchingTick_CombinedPauseAndSmallPositionChangeBelowThreshold_DoesNotSeek()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room, 1761 * SessionSnapshot.TicksPerSecond);
            _issuer.Issued.Clear();

            // The pause and two-second position movement are below the four-
            // second seek floor, so only Pause is propagated.
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 1764 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 1762 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Equal(RoomState.Watching, _rooms.GetRuntime(room.Id).State);
            Assert.Contains(_issuer.Issued, issued =>
                issued.userId == "u2" && issued.command == RemoteCommands.Pause);
            Assert.DoesNotContain(_issuer.Issued, issued => issued.command == RemoteCommands.Seek);
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
        public void WatchingTick_ForwardFiveSecondJump_StartsAlignBarrier()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);

            // Pressing the +5s button is a real user seek and must trigger the
            // same align barrier as dragging.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 55 * SessionSnapshot.TicksPerSecond),
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
            var engine = CreateEngine(notifyOnSyncActions: true);
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
            {
                Assert.True(
                    message.text.Contains("自动") || message.text.Contains("重新同步"),
                    $"Unexpected automatic retry message: {message.text}");
                Assert.Equal(3000, message.timeoutMs);
            });

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
            var engine = CreateEngine(messageIssuer: messageIssuer, notifyOnSyncActions: true);
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

        [Fact]
        public void AckLatency_IsRecordedWhenPendingCommandIsAcknowledged()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.True(runtime.AckLatencySeconds["u1"] > 0);
            Assert.True(runtime.AckLatencySeconds["u2"] > 0);
        }

        [Fact]
        public void WatchingTick_SlowAckLatency_RaisesSeekDetectionThreshold()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);
            _rooms.GetRuntime(room.Id).AckLatencySeconds["u2"] = 6.0;

            // A 4s out-of-band jump (55 vs expected 51) is below the raised 6s
            // threshold, so the slow client's stale snapshot is not treated as
            // a manual seek.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 51 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 55 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Equal(RoomState.Watching, _rooms.GetRuntime(room.Id).State);
            Assert.DoesNotContain(_issuer.Issued, i => i.command == RemoteCommands.Seek);

            // A jump at or above the raised threshold is still a manual seek.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 52 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 63 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(RoomState.Barrier, runtime.State);
            Assert.Equal("u2", runtime.Barrier.AnchorUserId);
        }

        [Fact]
        public void WatchingTick_PausePropagation_AlignsFollower()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);
            _rooms.GetRuntime(room.Id).AckLatencySeconds["u2"] = 10.0;

            // Primary pauses at 51s while the secondary is 8s ahead; the gap is
            // below the raised threshold so it is not mistaken for a seek.
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 51 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 59 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Contains(_issuer.Issued, i => i.userId == "u2" && i.command == RemoteCommands.Pause);

            // Once the secondary confirms the pause, it is seeked back to the
            // paused primary's position while the room stays Watching.
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 51 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 59 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            var seek = _issuer.Issued.Single(i => i.command == RemoteCommands.Seek);
            Assert.Equal("u2", seek.userId);
            Assert.Equal(51 * SessionSnapshot.TicksPerSecond, seek.positionTicks);
            Assert.Equal(RoomState.Watching, _rooms.GetRuntime(room.Id).State);

            // The seek lands without being reinterpreted as a manual seek.
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 51 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 51 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Equal(1, _issuer.Issued.Count(i => i.command == RemoteCommands.Seek));
            Assert.Equal(RoomState.Watching, _rooms.GetRuntime(room.Id).State);
        }

        [Fact]
        public void WatchingTick_PausePropagation_SmallDifference_DoesNotSeek()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);

            // Both pause; the follower is within the 2s seek tolerance of the
            // paused anchor, so no alignment seek is needed.
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 51 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Contains(_issuer.Issued, i => i.userId == "u2" && i.command == RemoteCommands.Pause);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.DoesNotContain(_issuer.Issued, i => i.command == RemoteCommands.Seek);
        }

        [Fact]
        public void WatchingTick_PauseAlign_AbortsWhenAnchorResumes()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);
            _rooms.GetRuntime(room.Id).AckLatencySeconds["u2"] = 10.0;

            // Primary pauses; alignment is deferred until the secondary
            // confirms its pause.
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 50 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 55 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Contains(_issuer.Issued, i => i.userId == "u2" && i.command == RemoteCommands.Pause);
            Assert.Single(_rooms.GetRuntime(room.Id).PauseAlign);

            // The anchor resumes before the secondary pauses: the stale target
            // must be dropped and no seek may be issued.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 51 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 56 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.DoesNotContain(_issuer.Issued, i => i.command == RemoteCommands.Seek);
            Assert.Empty(_rooms.GetRuntime(room.Id).PauseAlign);
        }

        [Fact]
        public void PauseAlignSeekPending_AnchorResume_RebuildsBarrierBeforeUnpause()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            EnterWatching(engine, room);
            _rooms.GetRuntime(room.Id).AckLatencySeconds["u2"] = 10.0;

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 51 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 59 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 51 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 59 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal(RemoteCommands.Seek, runtime.Pending["u2"].Command);
            int issuedBeforeAnchorResume = _issuer.Issued.Count;

            // The anchor resumes while the follower's Seek is still pending.
            // The resume must not overwrite that Pending Seek with Unpause.
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 52 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 59 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            var rebuilt = engine.PollOnce(_clock.Now).Single();

            Assert.Equal(RoomState.Barrier, rebuilt.State);
            Assert.Equal("u1", runtime.Barrier.AnchorUserId);
            Assert.DoesNotContain(
                _issuer.Issued.Skip(issuedBeforeAnchorResume),
                issued => issued.command == RemoteCommands.Unpause);

            // Complete the rebuilt Pause -> Seek -> acknowledgement -> Restore
            // path; only now is Unpause allowed to be issued.
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 52 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 59 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // rebuilt barrier issues Pause
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // Pause acknowledgement enters Seek
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // Seek is issued
            Assert.Equal(RemoteCommands.Seek, runtime.Pending["u2"].Command);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: 52 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: 52 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // Seek acknowledgement enters Restore
            _clock.Advance(1);
            engine.PollOnce(_clock.Now); // Restore issues Unpause
            Assert.Contains(_issuer.Issued, issued => issued.command == RemoteCommands.Unpause);

            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 52 * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: 52 * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            Assert.Equal(RoomState.Watching, runtime.State);
        }

        [Fact]
        public void WaitingPauseImmediateFailure_IsCooledBoundedAndRecoversOnIdentityOrCapabilityChange()
        {
            var room = CreateRoom();
            var engine = CreateEngine();
            _issuer.FailuresRemaining = 1000;
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: 0, itemId: "i1"),
                Snapshot("s2", "u2", paused: false, position: 0, itemId: "i2"));

            engine.PollOnce(_clock.Now);
            Assert.Equal(2, _issuer.Issued.Count(i => i.command == RemoteCommands.Pause));

            // A short polling interval must not resend an immediately failed
            // Pause before the retry cooldown.
            _clock.Advance(0.5);
            engine.PollOnce(_clock.Now);
            Assert.Equal(2, _issuer.Issued.Count(i => i.command == RemoteCommands.Pause));

            _clock.Advance(SyncConstants.WaitingPauseRetryDelaySeconds - 0.5);
            engine.PollOnce(_clock.Now);
            Assert.Equal(4, _issuer.Issued.Count(i => i.command == RemoteCommands.Pause));
            _clock.Advance(SyncConstants.WaitingPauseRetryDelaySeconds);
            engine.PollOnce(_clock.Now);
            Assert.Equal(6, _issuer.Issued.Count(i => i.command == RemoteCommands.Pause));

            var runtime = _rooms.GetRuntime(room.Id);
            Assert.Equal("waiting pause retry limit reached", runtime.Error);
            int issuedAtLimit = _issuer.Issued.Count(i => i.command == RemoteCommands.Pause);
            _clock.Advance(100);
            engine.PollOnce(_clock.Now);
            Assert.Equal(issuedAtLimit, _issuer.Issued.Count(i => i.command == RemoteCommands.Pause));

            Assert.True(runtime.WaitingPauseRetries["u1"].Exhausted);
            Assert.True(runtime.WaitingPauseRetries["u2"].Exhausted);

            // Changing only one identity permits that user to retry. The other
            // exhausted user must remain suppressed and must keep the limit
            // error visible.
            SetCandidates(
                Snapshot("s1-reconnected", "u1", paused: false, position: 0, itemId: "i1"),
                Snapshot("s2", "u2", paused: false, position: 0, itemId: "i2"));
            _clock.Advance(0.1);
            engine.PollOnce(_clock.Now);
            Assert.Equal(issuedAtLimit + 1, _issuer.Issued.Count(i => i.command == RemoteCommands.Pause));
            Assert.False(runtime.WaitingPauseRetries["u1"].Exhausted);
            Assert.Equal(1, runtime.WaitingPauseRetries["u1"].Attempts);
            Assert.True(runtime.WaitingPauseRetries["u2"].Exhausted);
            Assert.Equal("waiting pause retry limit reached", runtime.Error);

            // Only after the second exhausted user's identity changes may the
            // limit error clear and a new retry cycle begin for that user.
            SetCandidates(
                Snapshot("s1-reconnected", "u1", paused: false, position: 0, itemId: "i1"),
                Snapshot("s2-reconnected", "u2", paused: false, position: 0, itemId: "i2"));
            _clock.Advance(0.1);
            engine.PollOnce(_clock.Now);
            Assert.Equal(issuedAtLimit + 2, _issuer.Issued.Count(i => i.command == RemoteCommands.Pause));
            Assert.False(runtime.WaitingPauseRetries["u1"].Exhausted);
            Assert.False(runtime.WaitingPauseRetries["u2"].Exhausted);
            Assert.NotEqual("waiting pause retry limit reached", runtime.Error);
        }

        private SyncEngine CreateEngine(
            string serverId = "server-1",
            bool pauseOtherOnPlaybackStop = true,
            bool notifyOtherOnPlaybackStop = true,
            bool notifyOnSyncActions = false,
            IMessageIssuer messageIssuer = null,
            ILogManager logManager = null)
        {
            return new SyncEngine(
                _rooms, _provider, _issuer, () => serverId, () => _clock.Now,
                pollIntervalSeconds: 1.0,
                pauseOtherOnPlaybackStop: pauseOtherOnPlaybackStop,
                notifyOtherOnPlaybackStop: notifyOtherOnPlaybackStop,
                notifyOnSyncActions: notifyOnSyncActions,
                messageIssuer: messageIssuer ?? _messageIssuer,
                logManager: logManager);
        }

        private static Mock<ILogManager> CreateLogManager(
            List<string> warnings,
            List<string> infos = null)
        {
            var logger = new Mock<ILogger>();
            logger.Setup(x => x.Warn(It.IsAny<string>(), It.IsAny<object[]>()))
                .Callback<string, object[]>((message, _) => warnings.Add(message));
            logger.Setup(x => x.Info(It.IsAny<string>(), It.IsAny<object[]>()))
                .Callback<string, object[]>((message, _) => infos?.Add(message));
            var logManager = new Mock<ILogManager>();
            logManager.Setup(x => x.GetLogger("WatchTogether.SyncEngine"))
                .Returns(logger.Object);
            return logManager;
        }

        private Room CreateRoom()
        {
            return _rooms.CreateRoom(
                "server-1", "http://emby", "room", "admin-1",
                new[] { "u1", "u2" }, "u1");
        }

        private void EnterWatching(
            SyncEngine engine,
            Room room,
            long positionTicks = 50 * SessionSnapshot.TicksPerSecond)
        {
            SetCandidates(
                Snapshot("s1", "u1", paused: false, position: positionTicks),
                Snapshot("s2", "u2", paused: false, position: positionTicks));
            engine.PollOnce(_clock.Now);
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: positionTicks),
                Snapshot("s2", "u2", paused: true, position: positionTicks));
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
                Snapshot("s1", "u1", paused: false, position: positionTicks),
                Snapshot("s2", "u2", paused: false, position: positionTicks));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Equal(RoomState.Watching, _rooms.GetRuntime(room.Id).State);
            _issuer.Issued.Clear();
        }

        private void EnterWatchingPaused(SyncEngine engine, Room room, long positionSeconds)
        {
            EnterWatching(engine, room, positionSeconds * SessionSnapshot.TicksPerSecond);
            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: positionSeconds * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: false, position: positionSeconds * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            SetCandidates(
                Snapshot("s1", "u1", paused: true, position: positionSeconds * SessionSnapshot.TicksPerSecond),
                Snapshot("s2", "u2", paused: true, position: positionSeconds * SessionSnapshot.TicksPerSecond));
            _clock.Advance(1);
            engine.PollOnce(_clock.Now);

            Assert.Equal(RoomState.Watching, _rooms.GetRuntime(room.Id).State);
            _issuer.Issued.Clear();
        }

        [Fact]
        public void WatchingTick_UserPause_NotifiesOnlyPeerOnce_WithThreeSecondTimeout()
        {
            var room = CreateRoom(); var engine = CreateEngine(notifyOnSyncActions: true); EnterWatching(engine, room); _messageIssuer.Issued.Clear();
            SetCandidates(Snapshot("s1", "u1", true, 50 * SessionSnapshot.TicksPerSecond), Snapshot("s2", "u2", false, 50 * SessionSnapshot.TicksPerSecond)); _clock.Advance(1); engine.PollOnce(_clock.Now);
            Assert.Single(_messageIssuer.Issued); Assert.Equal("u2", _messageIssuer.Issued[0].userId); Assert.Equal("对方已暂停播放，已同步暂停", _messageIssuer.Issued[0].text); Assert.Equal(3000, _messageIssuer.Issued[0].timeoutMs);
            _clock.Advance(1); engine.PollOnce(_clock.Now); Assert.Single(_messageIssuer.Issued);
        }

        [Fact]
        public void WatchingTick_UserResume_NotifiesOnlyPeerOnce_WithThreeSecondTimeout()
        {
            var room = CreateRoom(); var engine = CreateEngine(notifyOnSyncActions: true); EnterWatchingPaused(engine, room, 50); _messageIssuer.Issued.Clear();
            SetCandidates(Snapshot("s1", "u1", false, 50 * SessionSnapshot.TicksPerSecond), Snapshot("s2", "u2", true, 50 * SessionSnapshot.TicksPerSecond)); _clock.Advance(1); engine.PollOnce(_clock.Now);
            Assert.Single(_messageIssuer.Issued); Assert.Equal("u2", _messageIssuer.Issued[0].userId); Assert.Equal("对方已继续播放，已同步继续", _messageIssuer.Issued[0].text); Assert.Equal(3000, _messageIssuer.Issued[0].timeoutMs);
            _clock.Advance(1); engine.PollOnce(_clock.Now); Assert.Single(_messageIssuer.Issued);
        }

        [Fact]
        public void WatchingTick_ManualSeek_NotifiesOnlyPeer_AndCompletionNotifiesBoth()
        {
            var room = CreateRoom(); var engine = CreateEngine(notifyOnSyncActions: true); EnterWatching(engine, room); _messageIssuer.Issued.Clear();
            SetCandidates(Snapshot("s1", "u1", false, 80 * SessionSnapshot.TicksPerSecond), Snapshot("s2", "u2", false, 50 * SessionSnapshot.TicksPerSecond)); _clock.Advance(1); engine.PollOnce(_clock.Now);
            Assert.Contains(_messageIssuer.Issued, m => m.userId == "u2" && m.text == "对方调整了播放进度，正在重新同步" && m.timeoutMs == 3000);
            Assert.DoesNotContain(_messageIssuer.Issued, m => m.userId == "u1" && m.text == "对方调整了播放进度，正在重新同步");
        }

        [Fact]
        public void DifferentVideoNotice_IsDeduplicated_RearmedAfterRecovery_AndRequiresNonEmptyItems()
        {
            var room = CreateRoom(); var engine = CreateEngine(notifyOnSyncActions: true); SetCandidates(Snapshot("s1", "u1", false, 0, "a"), Snapshot("s2", "u2", false, 0, "b")); engine.PollOnce(_clock.Now); Assert.Equal(2, _messageIssuer.Issued.Count); engine.PollOnce(_clock.Now); Assert.Equal(2, _messageIssuer.Issued.Count); SetCandidates(Snapshot("s1", "u1", false, 0, "a"), Snapshot("s2", "u2", false, 0, "a")); engine.PollOnce(_clock.Now); SetCandidates(Snapshot("s1", "u1", false, 0, "a"), Snapshot("s2", "u2", false, 0, "b")); engine.PollOnce(_clock.Now); Assert.Equal(4, _messageIssuer.Issued.Count); _messageIssuer.Issued.Clear(); SetCandidates(Snapshot("s1", "u1", false, 0, ""), Snapshot("s2", "u2", false, 0, "b")); engine.PollOnce(_clock.Now); Assert.Empty(_messageIssuer.Issued);
        }

        [Fact]
        public void DifferentVideoNotice_DisabledDoesNotConsumeEvent_EnablingNotifiesNextPoll()
        {
            var room = CreateRoom(); var engine = CreateEngine(notifyOnSyncActions: false); SetCandidates(Snapshot("s1", "u1", false, 0, "a"), Snapshot("s2", "u2", false, 0, "b")); engine.PollOnce(_clock.Now); Assert.Empty(_messageIssuer.Issued); engine.UpdateOptions(new SyncEngineOptions(1, true, true, true)); engine.PollOnce(_clock.Now); Assert.Equal(2, _messageIssuer.Issued.Count);
        }

        [Fact]
        public void NotifyOnSyncActionsDisabled_SuppressesAutomaticRetry_ButStopNoticeRemainsIndependent()
        {
            var room = CreateRoom(); var engine = CreateEngine(notifyOnSyncActions: false, notifyOtherOnPlaybackStop: true); EnterWatching(engine, room); _messageIssuer.Issued.Clear(); SetCandidates(Snapshot("s1", "u1", false, 0, stopped: true), Snapshot("s2", "u2", false, 0)); _clock.Advance(1); engine.PollOnce(_clock.Now); _clock.Advance(3); engine.PollOnce(_clock.Now); Assert.Single(_messageIssuer.Issued); Assert.Equal("对方已停止播放，请重新打开视频", _messageIssuer.Issued[0].text);
        }

        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void SyncActionMessageFailure_DoesNotChangePlaybackState(bool throwOnIssue)
        {
            var room = CreateRoom(); _messageIssuer.ThrowOnIssue = throwOnIssue; _messageIssuer.ReturnFalse = !throwOnIssue; var engine = CreateEngine(notifyOnSyncActions: true); EnterWatching(engine, room); _messageIssuer.Issued.Clear(); SetCandidates(Snapshot("s1", "u1", true, 50 * SessionSnapshot.TicksPerSecond), Snapshot("s2", "u2", false, 50 * SessionSnapshot.TicksPerSecond)); _clock.Advance(1); engine.PollOnce(_clock.Now); Assert.Equal(RoomState.Watching, _rooms.GetRuntime(room.Id).State);
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
            double playbackRate = 1.0,
            DateTimeOffset? lastActivityDateUtc = null,
            bool supportsRemoteControl = true)
        {
            return new SessionSnapshot(
                sessionId, userId, itemId, "m1",
                position, 100 * SessionSnapshot.TicksPerSecond, paused, playbackRate,
                stopped: stopped, supportsRemoteControl: supportsRemoteControl,
                new SessionCapabilityReport(
                    supportsRemoteControl,
                    supportsRemoteControl
                        ? new[] { "Pause", "Unpause", "Seek" }
                        : Array.Empty<string>()),
                lastActivityDateUtc ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
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

        private sealed class BlockingSnapshotProvider : ISessionSnapshotProvider
        {
            private readonly IReadOnlyList<SessionSnapshot> _snapshots;

            public BlockingSnapshotProvider(IReadOnlyList<SessionSnapshot> snapshots)
            {
                _snapshots = snapshots;
            }

            private readonly TaskCompletionSource<bool> _entered =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task EnteredTask => _entered.Task;

            public ManualResetEventSlim Release { get; } = new ManualResetEventSlim(false);

            public IReadOnlyList<SessionSnapshot> GetSessionSnapshots()
            {
                _entered.TrySetResult(true);
                Release.Wait(TimeSpan.FromSeconds(5));
                return _snapshots;
            }
        }

        private sealed class RecordingMessageIssuer : IMessageIssuer
        {
            public List<(string userId, string header, string text, int? timeoutMs)> Issued { get; } =
                new List<(string, string, string, int?)>();

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
                Issued.Add((userId, header, text, timeoutMs));
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
            public List<(string userId, string command, long? positionTicks, string sessionId, string itemId)> Issued { get; } =
                new List<(string, string, long?, string, string)>();

            public int FailuresRemaining { get; set; }

            public string FailureMessage { get; set; } = "command delivery failed";

            public bool ThrowOnIssue { get; set; }

            public string ExceptionMessage { get; set; } = "command delivery failed";

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
                Issued.Add((userId, command, positionTicks, snapshot?.SessionId, snapshot?.ItemId));
                if (ThrowOnIssue)
                {
                    throw new InvalidOperationException(ExceptionMessage);
                }

                if (FailuresRemaining > 0)
                {
                    FailuresRemaining--;
                    error = FailureMessage;
                    return false;
                }

                error = null;
                return true;
            }
        }

        private sealed class ThrowOnceIssuer : ICommandIssuer
        {
            public List<(string roomId, string userId, string command)> Issued { get; } =
                new List<(string, string, string)>();

            public string FailedRoomId { get; private set; }

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
                Issued.Add((roomId, userId, command));
                if (FailedRoomId == null)
                {
                    FailedRoomId = roomId;
                    throw new InvalidOperationException("room command failed");
                }

                error = null;
                return true;
            }
        }
    }
}
