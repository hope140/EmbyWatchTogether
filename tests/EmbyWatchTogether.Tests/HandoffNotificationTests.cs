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
    // Real SessionBridge/SDK dispatch, with deterministic server snapshots and transport ACKs.
    // These tests do not claim that a client displayed a toast.
    [Collection(TransportTimingCollection.Name)]
    public class HandoffNotificationTests
    {
        [Fact]
        public async Task StopThenNewStartTwelveMillisecondsLater_DropsObsoleteStopWithoutDelayingPause()
        {
            using var f = new Replay();
            f.EnterWatching();
            using (f.HoldRoomGate())
            {
                f.ConfirmPrimaryStop();
                Assert.Equal(new[] { "s2:Pause" }, f.Commands.ToArray());
                Assert.Empty(f.Messages);
                f.Set("u1", "43", false, 0);
                f.Tick(.012);
                Assert.Equal(RoomState.Handoff, f.Runtime.State);
                Assert.Equal(new[] { "s2:Pause", "s2:Play:43" }, f.Commands.ToArray());
            }
            await f.Idle;
            Assert.Empty(f.Messages);
        }

        [Fact]
        public async Task QueuedDifferentVideoNotice_RechecksBothCurrentItemsWithoutAnotherPoll()
        {
            using var f = new Replay();
            using (f.HoldRoomGate())
            {
                f.Set("u1", "42", true, 40);
                f.Set("u2", "43", true, 40);
                f.Tick();
                // The receiving primary is unchanged; only its peer has caught up.
                f.Set("u2", "42", true, 40);
            }
            await f.Idle;
            Assert.Empty(f.Messages);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CompletedHandoff_ReportsFollowingOnceAndPreservesPauseIntent(bool paused)
        {
            using var f = new Replay();
            f.EnterWatching();
            f.Set("u1", "43", paused, 0);
            f.Tick();
            Assert.Equal(RoomState.Handoff, f.Runtime.State);
            Assert.Empty(f.Messages);
            f.CompleteHandoff("43", paused);
            await f.Idle;
            Assert.Equal(RoomState.Watching, f.Runtime.State);
            Assert.Equal(2, f.Messages.Count);
            Assert.All(f.Messages, m => Assert.Equal("已跟随切换，同步完成", m.text));
            if (paused) Assert.DoesNotContain(f.Commands, c => c.EndsWith(":Unpause"));
            f.Tick();
            await f.Idle;
            Assert.Equal(2, f.Messages.Count);
        }

        [Fact]
        public async Task PreviousCompletion_DoesNotLeakIntoNewHandoffWhilePeerStillHasOldItem()
        {
            using var f = new Replay();
            f.EnterWatching();
            f.Set("u1", "43", true, 0);
            f.Tick();
            using (f.HoldRoomGate())
            {
                f.CompleteHandoff("43", true);
                Assert.Equal(RoomState.Watching, f.Runtime.State);
                f.Set("u1", "44", true, 0);
                f.Tick();
                Assert.Equal(RoomState.Handoff, f.Runtime.State);
            }
            await f.Idle;
            Assert.Empty(f.Messages);
        }

        [Fact]
        public async Task GenuineStop_RepeatedPollsKeepsOneNoticeAndOneImmediatePause()
        {
            using var f = new Replay();
            f.EnterWatching();
            f.ConfirmPrimaryStop();
            Assert.Equal(new[] { "s2:Pause" }, f.Commands.ToArray());
            for (int i = 0; i < 4; i++) f.Tick(.1);
            await f.Idle;
            Assert.Single(f.Messages);
            Assert.Equal("对方已停止播放，同步已暂停", f.Messages.Single().text);
            Assert.Equal(new[] { "s2:Pause" }, f.Commands.ToArray());
        }

        [Fact]
        public async Task GenuineDifferentVideo_RemainsVisibleOnceWithoutPlayOrSeek()
        {
            using var f = new Replay();
            f.Set("u1", "42", false, 40);
            f.Set("u2", "43", false, 40);
            f.Tick();
            await f.Idle;
            Assert.Equal(2, f.Messages.Count);
            Assert.All(f.Messages, m => Assert.Contains("不同视频", m.text));
            f.Tick();
            await f.Idle;
            Assert.Equal(2, f.Messages.Count);
            Assert.All(f.Commands, c => Assert.EndsWith(":Pause", c));
        }

        [Fact]
        public async Task StopNotice_HalfSecondBoundaryAndReadyWorkDoNotDelayPlayback()
        {
            using var f = new Replay();
            f.EnterWatching();
            double monotonic = 0;
            f.SetMonotonic(() => Volatile.Read(ref monotonic));
            f.ConfirmPrimaryStop();
            // Direct recorders, not an accessor that silently waits for the queue.
            Assert.Equal(new[] { "s2:Pause" }, f.Commands.ToArray());
            await f.DrainReady();
            Assert.Empty(f.Messages);
            Assert.False(f.RawIdle.IsCompleted);
            Volatile.Write(ref monotonic, .499);
            await f.DrainReady();
            Assert.Empty(f.Messages);
            Volatile.Write(ref monotonic, .5);
            await f.DrainReady();
            await f.Idle;
            Assert.Single(f.Messages);
            Assert.Equal(new[] { "s2:Pause" }, f.Commands.ToArray());
        }

        [Theory]
        [InlineData("resume")]
        [InlineData("setting")]
        [InlineData("stop")]
        [InlineData("dispose")]
        public async Task DeferredStop_RechecksSourceSettingsAndShutdown(string change)
        {
            using var f = new Replay();
            f.EnterWatching();
            double monotonic = 0;
            f.SetMonotonic(() => Volatile.Read(ref monotonic));
            f.ConfirmPrimaryStop();
            await f.DrainReady();
            switch (change)
            {
                case "resume": f.Set("u1", "42", false, 40); break; // No poll: semantic recheck alone must discard it.
                case "setting": f.Engine.UpdateOptions(new SyncEngineOptions(1, true, false, true)); break;
                case "stop": f.Engine.Stop(); break;
                case "dispose": f.Engine.Dispose(); break;
            }
            Volatile.Write(ref monotonic, .5);
            if (change == "resume" || change == "setting") await f.DrainReady();
            await f.Idle;
            Assert.Empty(f.Messages);
            Assert.Equal(new[] { "s2:Pause" }, f.Commands.ToArray());
        }

        [Fact]
        public async Task LateHandoff_AfterGenuineStopNoticeCompletesWithoutDifferentVideoNoise()
        {
            using var f = new Replay();
            f.EnterWatching();
            f.ConfirmPrimaryStop();
            await f.Idle;
            Assert.Single(f.Messages);
            f.Set("u1", "43", false, 0);
            f.Tick(2.5);
            Assert.Equal(RoomState.Handoff, f.Runtime.State);
            f.CompleteHandoff("43", false);
            await f.Idle;
            Assert.Equal(RoomState.Watching, f.Runtime.State);
            Assert.Equal(3, f.Messages.Count);
            Assert.Equal(2, f.Messages.Count(m => m.text == "已跟随切换，同步完成"));
            Assert.DoesNotContain(f.Messages, m => m.text.Contains("不同视频"));
        }

        [Fact]
        public async Task FastHandoff_BeforeStopConfirmationProducesOnlyCompletion()
        {
            using var f = new Replay();
            f.EnterWatching();
            f.RemovePrimary(); f.Tick(0);
            f.Set("u1", "43", false, 0); f.Tick(.5);
            Assert.Equal(new[] { "s2:Play:43" }, f.Commands.ToArray());
            f.CompleteHandoff("43", false);
            await f.Idle;
            Assert.Equal(2, f.Messages.Count);
            Assert.All(f.Messages, m => Assert.Equal("已跟随切换，同步完成", m.text));
        }

        [Fact]
        public async Task FailedHandoff_RetainsFailureAndDoesNotAnnounceSuccessOrResume()
        {
            using var f = new Replay();
            f.EnterWatching();
            f.Set("u1", "43", true, 0); f.Tick(); f.Tick(10);
            await f.Idle;
            Assert.Equal(RoomState.Waiting, f.Runtime.State);
            Assert.StartsWith("media handoff ", f.Runtime.Error);
            Assert.Equal(2, f.Messages.Count);
            Assert.All(f.Messages, message => Assert.Contains("手动重新同步", message.text));
            Assert.Equal(new[] { "s2:Play:43" }, f.Commands.ToArray());
            f.Set("u2", "43", true, 0); f.Tick();
            Assert.Equal(RoomState.Waiting, f.Runtime.State);
            Assert.Equal(new[] { "s2:Play:43" }, f.Commands.ToArray());
        }

        [Fact]
        public async Task PrimaryHold_NoticeRequiresPauseAckAndIsSentOnlyOnce()
        {
            using var f = new Replay(); f.EnterWatching();
            f.Set("u1", "43", false, 0); f.Tick();
            f.Set("u1", "43", false, 1.2); f.Tick(1.1);
            await f.Idle;
            Assert.Empty(f.Messages); // Accepted transport is not a paused snapshot ACK.
            f.Tick(); await f.Idle;
            var message = Assert.Single(f.Messages);
            Assert.Equal("s1", message.session);
            Assert.Equal("正在等待另一端载入，准备好后会自动继续", message.text);
            for (int i = 0; i < 4; i++) f.Tick();
            await f.Idle; Assert.Single(f.Messages);
            Assert.Equal(new[] { "s2:Play:43", "s1:Pause" }, f.Commands.ToArray());
        }

        [Theory]
        [InlineData("resume")]
        [InlineData("item")]
        [InlineData("setting")]
        [InlineData("stop")]
        [InlineData("expired")]
        [InlineData("peer-stop")]
        [InlineData("peer-session")]
        [InlineData("primary-capability")]
        public async Task QueuedHoldNotice_RechecksCurrentPauseOperationAndSettings(string change)
        {
            using var f = new Replay(); f.EnterWatching();
            f.Set("u1", "43", false, 0); f.Tick();
            f.Set("u1", "43", false, 1.2); f.Tick(1.1);
            using (f.HoldRoomGate())
            {
                f.Tick(); // Queue the ACK notice, but prevent delivery.
                if (change == "resume") f.Set("u1", "43", false, 1.2);
                if (change == "item") f.Set("u1", "44", true, 1.2);
                if (change == "setting") f.Engine.UpdateOptions(new SyncEngineOptions(1, true, true, false));
                if (change == "stop") f.Set("u1", "43", true, 1.2, stopped: true);
                if (change == "expired") f.Tick(10);
                if (change == "peer-stop") f.Set("u2", "42", false, 40, stopped: true);
                if (change == "peer-session") f.Set("u2", "42", false, 40, session: "replacement");
                if (change == "primary-capability") f.Set("u1", "43", true, 1.2, capability: false);
            }
            await f.Idle;
            Assert.DoesNotContain(f.Messages, message => message.text.Contains("准备好后"));
            Assert.DoesNotContain(f.Messages, message => message.text.Contains("同步完成"));
        }

        [Fact]
        public async Task OriginalPausedIntent_DoesNotPromiseAutomaticResume()
        {
            using var f = new Replay(); f.EnterWatching();
            f.Set("u1", "43", true, 0); f.Tick(); f.Tick(1.1);
            await f.Idle;
            Assert.Empty(f.Messages);
            Assert.Equal(new[] { "s2:Play:43" }, f.Commands.ToArray());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task NaturalRecovery_NotificationsNeverAddPlaybackControls(bool notifications)
        {
            using var f = new Replay(notifications); f.EnterWatching();
            f.BeginResumeObservation(); await f.Idle;
            f.Set("u1", "43", false, 3.4); f.Set("u2", "43", false, 3.4); f.Tick(1.1);
            Assert.Equal(RoomState.Watching, f.Runtime.State);
            await f.Idle;
            Assert.Equal(new[] { "s2:Play:43", "s1:Pause" }, f.Commands.ToArray());
            if (notifications) Assert.Equal(2, f.Messages.Count(message => message.text == "已跟随切换，同步完成"));
            else Assert.Empty(f.Messages);
        }

        [Theory]
        [InlineData("pause")]
        [InlineData("media")]
        [InlineData("capability")]
        public async Task NaturalRecovery_QueuedSuccessRechecksCurrentPlayingAlignment(string change)
        {
            using var f = new Replay(); f.EnterWatching();
            f.BeginResumeObservation(); await f.Idle; f.Messages.Clear();
            using (f.HoldRoomGate())
            {
                f.Set("u1", "43", false, 3.4); f.Set("u2", "43", false, 3.4); f.Tick(1.1);
                Assert.Equal(RoomState.Watching, f.Runtime.State);
                f.Set("u2", "43", change == "pause", 3.4,
                    media: change == "media" ? "replacement" : "source", capability: change != "capability");
            }
            await f.Idle; Assert.Empty(f.Messages);
        }

        [Fact]
        public async Task NotificationOptions_DoNotChangePlaybackCommandSequence()
        {
            string[] enabled;
            using (var f = new Replay())
            {
                f.EnterWatching(); f.ConfirmPrimaryStop();
                f.Set("u1", "43", false, 0); f.Tick(.012);
                f.CompleteHandoff("43", false); await f.Idle;
                enabled = f.Commands.ToArray();
            }
            using (var f = new Replay(false))
            {
                f.EnterWatching(); f.ConfirmPrimaryStop();
                f.Set("u1", "43", false, 0); f.Tick(.012);
                f.CompleteHandoff("43", false); await f.Idle;
                Assert.Equal(enabled, f.Commands.ToArray());
                Assert.Empty(f.Messages);
            }
            Assert.Equal(new[] { "s2:Pause", "s2:Play:43", "s1:Pause", "s2:Pause", "s1:Unpause", "s2:Unpause" }, enabled);
        }

        [Fact]
        public async Task Completion_RechecksPeerBeforePollCanObserveNewItem()
        {
            using var f = new Replay();
            f.EnterWatching();
            f.Set("u1", "43", true, 0); f.Tick();
            using (f.HoldRoomGate())
            {
                f.CompleteHandoff("43", true);
                f.Set("u1", "44", true, 0); // No poll, so the old operation token has not changed yet.
            }
            await f.Idle;
            Assert.Empty(f.Messages);
        }

        [Fact]
        public async Task NonPrimaryStop_InitialResetAndRepeatedPollsKeepItsSingleNotice()
        {
            using var f = new Replay();
            f.EnterWatching();
            double monotonic = 0;
            f.SetMonotonic(() => Volatile.Read(ref monotonic));
            object Token(string name) => typeof(RoomRuntime).GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f.Runtime);
            var before = Token("NotificationOperation");
            f.Set("u2", "42", false, 40, stopped: true);
            f.Tick(0); f.Tick(2);
            Assert.Equal(new[] { "s1:Pause" }, f.Commands.ToArray());
            Assert.NotSame(before, Token("NotificationOperation")); // Initial stop reset retires alignment notices.
            var stopToken = Token("StopNotificationOperation");
            Assert.NotNull(stopToken);
            for (int i = 0; i < 4; i++) f.Tick(.1);
            Assert.Same(stopToken, Token("StopNotificationOperation"));
            await f.DrainReady();
            Assert.Empty(f.Messages);
            Volatile.Write(ref monotonic, .5);
            await f.DrainReady(); await f.Idle;
            var message = Assert.Single(f.Messages);
            Assert.Equal("s1", message.session);
            Assert.Equal("对方已停止播放，同步已暂停", message.text);
            Assert.Equal(new[] { "s1:Pause" }, f.Commands.ToArray());
        }

        [Fact]
        public void StopNotificationIdentity_SurvivesStopResetButNotOrdinaryReset()
        {
            // Direct RoomRuntime contract test, not a claim that every stopped poll resets.
            var runtime = new RoomRuntime();
            var type = typeof(RoomRuntime);
            var stop = type.GetProperty("StopNotificationOperation", BindingFlags.NonPublic | BindingFlags.Instance);
            var alignment = type.GetProperty("NotificationOperation", BindingFlags.NonPublic | BindingFlags.Instance);
            var reset = type.GetMethod("ResetToWaitingPreservingPrimaryItemTransitionCandidate", BindingFlags.NonPublic | BindingFlags.Instance);
            var token = new object();
            stop.SetValue(runtime, token);
            for (int i = 0; i < 2; i++)
            {
                var previous = alignment.GetValue(runtime);
                reset.Invoke(runtime, null);
                Assert.NotSame(previous, alignment.GetValue(runtime));
                Assert.Same(token, stop.GetValue(runtime));
            }
            runtime.ResetToWaiting();
            Assert.Null(stop.GetValue(runtime));
        }

        private sealed class Replay : IDisposable, ISessionSnapshotProvider
        {
            private readonly object _snapshotsGate = new object();
            private readonly Dictionary<string, SessionSnapshot> _snapshots = new Dictionary<string, SessionSnapshot>();
            private readonly SessionBridge _bridge;
            internal readonly ConcurrentQueue<string> Commands = new ConcurrentQueue<string>();
            internal readonly ConcurrentQueue<(string session, string text)> Messages = new ConcurrentQueue<(string, string)>();
            internal readonly RoomManager Rooms = new RoomManager();
            internal readonly Room Room;
            internal readonly SyncEngine Engine;
            internal DateTimeOffset Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            internal RoomRuntime Runtime => Rooms.GetRuntime(Room.Id);
            internal Task RawIdle => (Task)typeof(SyncEngine).GetProperty("NotificationsIdle", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Engine);
            internal Task Idle => RawIdle.WaitAsync(TimeSpan.FromSeconds(5));
            private object Dispatcher => typeof(SyncEngine).GetField("_notifications", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Engine);
            internal void SetMonotonic(Func<double> clock) => Dispatcher.GetType()
                .GetField("_elapsedSeconds", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(Dispatcher, clock);
            internal Task DrainReady()
            {
                var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Assert.True((bool)Dispatcher.GetType().GetMethod("TryEnqueue", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(Dispatcher, new object[] { (Action<CancellationToken>)(_ => signal.TrySetResult(true)) }));
                return signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }

            internal Replay(bool notifications = true)
            {
                var manager = new Mock<ISessionManager>();
                manager.Setup(m => m.SendPlaystateCommand(It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<PlaystateRequest>(), It.IsAny<CancellationToken>()))
                    .Returns<string, string, PlaystateRequest, CancellationToken>((_, session, request, token) =>
                    {
                        Commands.Enqueue(session + ":" + request.Command);
                        var user = session == "s1" ? "u1" : "u2";
                        lock (_snapshotsGate)
                        {
                            var s = _snapshots[user];
                            Set(user, s.ItemId, request.Command == PlaystateCommand.Pause ||
                                request.Command != PlaystateCommand.Unpause && s.IsPaused,
                                request.Command == PlaystateCommand.Seek
                                    ? request.SeekPositionTicks.Value / (double)SessionSnapshot.TicksPerSecond : s.PositionSeconds);
                        }
                        return Task.CompletedTask;
                    });
                manager.Setup(m => m.SendPlayCommand(It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()))
                    .Returns<string, string, PlayRequest, CancellationToken>((_, session, request, token) =>
                    { Commands.Enqueue(session + ":Play:" + request.ItemIds.Single()); return Task.CompletedTask; });
                manager.Setup(m => m.SendMessageCommand(It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>()))
                    .Returns<string, string, MessageCommand, CancellationToken>((_, session, message, token) =>
                    { Messages.Enqueue((session, message.Text)); return Task.CompletedTask; });
                _bridge = new SessionBridge(manager.Object);
                var issuer = new SessionBridgeCommandIssuer(_bridge);
                Room = Rooms.CreateRoom("server", "http://example.invalid", "notification replay", "admin", new[] { "u1", "u2" }, "u1");
                Engine = new SyncEngine(Rooms, this, issuer, () => "server", () => Now,
                    messageIssuer: issuer, notifyOtherOnPlaybackStop: notifications, notifyOnSyncActions: notifications);
            }
            internal void Set(string user, string item, bool paused, double position, bool stopped = false,
                string media = "source", bool capability = true, string session = null)
            {
                lock (_snapshotsGate)
                    _snapshots[user] = new SessionSnapshot(session ?? (user == "u1" ? "s1" : "s2"), user, item, media,
                        (long)(position * SessionSnapshot.TicksPerSecond), 100 * SessionSnapshot.TicksPerSecond,
                        paused, 1, stopped, true,
                        new SessionCapabilityReport(capability, new[] { "Pause", "Unpause", "Seek", "DisplayMessage" }), Now);
            }
            public IReadOnlyList<SessionSnapshot> GetSessionSnapshots()
            { lock (_snapshotsGate) return _snapshots.Values.ToArray(); }
            internal void Tick(double seconds = .1) { Now = Now.AddSeconds(seconds); Engine.PollOnce(Now); }
            internal void EnterWatching()
            {
                Set("u1", "42", false, 40); Set("u2", "42", false, 40);
                for (int i = 0; i < 6; i++) Tick();
                Assert.Equal(RoomState.Watching, Runtime.State);
                Idle.GetAwaiter().GetResult(); Commands.Clear(); Messages.Clear();
            }
            internal void RemovePrimary() { lock (_snapshotsGate) _snapshots.Remove("u1"); }
            internal void BeginResumeObservation()
            {
                Set("u1", "43", false, 0); Tick();
                Set("u1", "43", false, 1.2); Tick(1.1); Tick();
                Set("u2", "43", false, 1.2); Tick();
                Set("u1", "43", false, 1.3); Set("u2", "43", false, 1.3); Tick();
                Set("u1", "43", false, 2.3); Set("u2", "43", false, 2.3); Tick(1.1);
                Assert.Equal(RoomState.Handoff, Runtime.State);
            }
            internal void ConfirmPrimaryStop()
            {
                RemovePrimary();
                Tick(0); Tick(1.999);
                Assert.Empty(Commands);
                Tick(.001);
                Assert.Equal(RoomState.Waiting, Runtime.State);
            }
            internal void CompleteHandoff(string item, bool paused)
            {
                Set("u1", item, paused, 1.2); Set("u2", item, paused, 0);
                Tick(1.1);
                Set("u2", item, paused, 1.2);
                Tick(1.1);
                for (int i = 0; i < 6; i++) Tick();
            }
            internal IDisposable HoldRoomGate() => (IDisposable)typeof(RoomManager)
                .GetMethod("TryEnterRoom", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Rooms, new object[] { Room.Id });
            public void Dispose() { Engine.Dispose(); _bridge.Dispose(); }
        }
    }
}
