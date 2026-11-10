using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Session;
using Moq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    [Collection(TransportTimingCollection.Name)]
    public class StoppedPlaybackTests
    {
        private sealed class Issuer : ICommandIssuer
        {
            internal readonly List<(string user, string command, long? position)> Calls = new();
            internal Action<string> OnIssue;
            public bool TryIssue(string roomId, string controllingUserId, string userId,
                SessionSnapshot snapshot, string command, long? positionTicks, DateTimeOffset now, out string error)
            { error = null; Calls.Add((userId, command, positionTicks)); OnIssue?.Invoke(command); return true; }
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly Mock<ISessionManager> Manager = new();
            internal readonly SessionInfo A, B;
            internal readonly SessionBridge Bridge;
            internal readonly SessionBridgeSnapshotProvider Provider;
            internal readonly RoomManager Rooms = new();
            internal readonly Room Room;
            internal readonly Issuer Issuer = new();
            internal readonly SyncEngine Engine;
            internal readonly List<string> Errors = new();
            internal readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
            internal readonly ConcurrentQueue<string> Messages = new();
            internal DateTimeOffset NotificationNow;
            internal Fixture(bool useBridgeIssuer = false, bool theaterCapabilities = false, bool notifications = false)
            {
                A = Session("a", "u1", theaterCapabilities); B = Session("b", "u2", theaterCapabilities);
                Manager.Setup(m => m.Sessions).Returns(new[] { A, B });
                Bridge = new SessionBridge(Manager.Object);
                Provider = new SessionBridgeSnapshotProvider(Bridge);
                Play(A, "p1"); Play(B, "p2");
                Room = Rooms.CreateSelfServiceRoom("server", "", "test", "u1", "u2", Now);
                var runtime = Rooms.GetRuntime(Room.Id);
                runtime.State = RoomState.Watching; runtime.SyncItemId = "42";
                runtime.PreviousAtUtc = Now.AddSeconds(-1);
                foreach(var s in Provider.GetSessionSnapshots()) runtime.Previous[s.UserId] = s;
                Assert.True(RoomEligibility.IsPairEligible(runtime.Previous), string.Join(";",runtime.Previous.Values.Select(s=>$"{s.UserId} {s.ItemId} raw={s.SupportsRemoteControl} stopped={s.Stopped} runtime={s.RunTimeTicks} rate={s.PlaybackRate}")));
                var logger=new Mock<ILogger>();
                logger.Setup(l=>l.ErrorException(It.IsAny<string>(),It.IsAny<Exception>(),It.IsAny<object[]>()))
                    .Callback<string,Exception,object[]>((message,error,args)=>Errors.Add(error.ToString()));
                var logs=new Mock<ILogManager>();logs.Setup(l=>l.GetLogger(It.IsAny<string>())).Returns(logger.Object);
                Manager.Setup(m=>m.SendPlayCommand(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<PlayRequest>(),It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
                Manager.Setup(m=>m.SendPlaystateCommand(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<PlaystateRequest>(),It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
                Manager.Setup(m => m.SendMessageCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>()))
                    .Callback<string, string, MessageCommand, CancellationToken>((_, session, message, token) => Messages.Enqueue(message.Text))
                    .Returns(Task.CompletedTask);
                ICommandIssuer issuer = useBridgeIssuer ? new SessionBridgeCommandIssuer(Bridge) : Issuer;
                NotificationNow = Now;
                Engine = new SyncEngine(Rooms, Provider, issuer, () => "server",
                    clock: notifications ? () => NotificationNow : null, notifyOnSyncActions: notifications,
                    messageIssuer: notifications ? issuer as IMessageIssuer : null, logManager: logs.Object);
            }
            internal Task NoticesIdle => ((Task)typeof(SyncEngine).GetProperty("NotificationsIdle", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(Engine)).WaitAsync(TimeSpan.FromSeconds(5));
            internal void Poll(DateTimeOffset now) { NotificationNow = now; Engine.PollOnce(now); Assert.True(Errors.Count==0,string.Join("\n",Errors)); }
            internal void Polls(int first, int count)
            { for(var i=first;i<first+count;i++) Poll(Now.AddMilliseconds(i*100)); }
            private SessionInfo Session(string id, string user, bool theaterCapabilities)
            {
                var controller = new Mock<ISessionController>();
                controller.SetupGet(c => c.SupportsMediaControl).Returns(true);
                return new SessionInfo(Manager.Object, Mock.Of<ILogger>()) {
                    Id=id, UserId=user, LastActivityDate=Now.UtcDateTime,
                    Capabilities=new ClientCapabilities { SupportsMediaControl=true, SupportedCommands=theaterCapabilities ? TheaterCommands : new[]{"Pause","Unpause","Seek"} },
                    SessionControllers=new[]{controller.Object}
                };
            }
            internal PlaySessionInfo Play(SessionInfo s, string id, double position=300, string item="42", bool paused=true)
            {
                return s.GetOrAddPlaySessionInfo(new PlaybackProgressInfo {
                    PlaySessionId=id, Item=new BaseItemDto { Id=item, RunTimeTicks=1500L*10000000 },
                    PositionTicks=(long)(position*10000000), IsPaused=paused, MediaSourceId="source", PlaybackRate=1
                });
            }
            internal void Stop(SessionInfo s, PlaySessionInfo p) => Manager.Raise(m=>m.PlaybackStopped+=null,
                new PlaybackStopEventArgs { Session=s, PlaySession=p, PlaySessionId=p.Id });
            internal void Start(SessionInfo s, PlaySessionInfo p) => Manager.Raise(m=>m.PlaybackStart+=null,
                new PlaybackProgressEventArgs { Session=s, PlaySession=p, PlaySessionId=p.Id });
            internal void Progress(SessionInfo s, PlaySessionInfo p) => Manager.Raise(m=>m.PlaybackProgress+=null,
                new PlaybackProgressEventArgs { Session=s, PlaySession=p, PlaySessionId=p.Id });
            internal void ServerStop(SessionInfo s, PlaySessionInfo p)
            {
                // Emby 4.10 replaces the state before queuing PlaybackStopped.
                p.NowPlayingItem = null;
                p.PlayState = new PlayerStateInfo();
                Stop(s,p);
            }
            public void Dispose() { Engine.Dispose(); Bridge.Dispose(); }
        }

        // Actual Theater capability shape: general commands do not enumerate Playstate commands.
        private static readonly string[] TheaterCommands = {
            "MoveUp", "MoveDown", "MoveLeft", "MoveRight", "PageUp", "PageDown",
            "PreviousLetter", "NextLetter", "ToggleOsd", "ToggleContextMenu", "Select", "Back",
            "SendKey", "SendString", "GoHome", "GoToSettings", "VolumeUp", "VolumeDown", "Mute",
            "Unmute", "ToggleMute", "SetVolume", "SetAudioStreamIndex", "SetSubtitleStreamIndex",
            "RefreshMediaSource", "DisplayContent", "GoToSearch", "DisplayMessage", "SetRepeatMode",
            "SetSubtitleOffset", "SetPlaybackRate", "ChannelUp", "ChannelDown", "PlayMediaSource", "PlayTrailers"
        };

        [Theory]
        [InlineData("late_register", 3.736)]
        [InlineData("late_register", 4.587)]
        [InlineData("late_register", 5.339)]
        [InlineData("early_register", 3.736)]
        [InlineData("early_register", 4.587)]
        [InlineData("early_register", 5.339)]
        [InlineData("queued_stop", 3.736)]
        [InlineData("queued_stop", 4.587)]
        [InlineData("queued_stop", 5.339)]
        public void TheaterCapabilities_LateHandoffReachesWatching(string scenario, double delay)
        {
            using var replay = new TheaterReplay();
            replay.Begin(scenario, delay);
            Assert.Single(replay.Sent, c => c.command == "PlayItem");
            for (int i = 1; i <= 24 && replay.Runtime.State != RoomState.Watching; i++)
                replay.Step(delay + i * .25);
            Assert.Equal(RoomState.Watching, replay.Runtime.State);
            Assert.All(replay.F.Provider.GetSessionSnapshots(), s => {
                Assert.Equal("43", s.ItemId); Assert.False(s.IsPaused);
            });
            Assert.Single(replay.Sent, c => c.command == "PlayItem" && c.session == "b" && c.item == "43");
            Assert.DoesNotContain(replay.Sent, c => c.command == "Seek");
            Assert.DoesNotContain(replay.Sent, c => c.command == "Seek" && c.item != "43");
            Assert.Single(replay.Sent, c => c.command == "Pause" && c.session == "a");
            Assert.Equal(2, replay.Sent.Count(c => c.command == "Pause" && c.session == "b"));
            Assert.Equal(2, replay.Sent.Count(c => c.command == "Unpause"));
            Assert.Equal(6, replay.Sent.Count);
        }

        [Fact]
        public void TheaterCapabilities_OffsetPeerSeeksOnceAndRestores()
        {
            using var replay = new TheaterReplay(peerStartPosition: 20);
            replay.Begin("late_register", 4.587);
            for (int i = 1; i <= 24 && replay.Runtime.State != RoomState.Watching; i++)
                replay.Step(4.587 + i * .25);
            Assert.Equal(RoomState.Watching, replay.Runtime.State);
            Assert.Single(replay.Sent, c => c.command == "PlayItem");
            Assert.Single(replay.Sent, c => c.command == "Seek" && c.session == "b" && c.item == "43");
            Assert.Equal(7, replay.Sent.Count);
            var pair = replay.F.Provider.GetSessionSnapshots();
            Assert.All(pair, s => { Assert.Equal("43", s.ItemId); Assert.False(s.IsPaused); });
            Assert.InRange(Math.Abs(pair[0].PositionTicks - pair[1].PositionTicks), 0, 10000000L);
        }

        [Theory]
        [InlineData("participant_no_control")]
        [InlineData("primary_no_control")]
        [InlineData("participant_raw_denied")]
        [InlineData("primary_raw_denied")]
        [InlineData("membership")]
        [InlineData("participant_identity")]
        [InlineData("participant_stop_restart")]
        [InlineData("no_start")]
        [InlineData("old_item")]
        [InlineData("expired")]
        public void TheaterCapabilities_LateHandoffPreservesSafetyGates(string restriction)
        {
            using var replay = new TheaterReplay();
            var delay = restriction == "expired" ? 10.0 : 4.587;
            replay.Begin("early_register", delay, restriction);
            for (int i = 1; i <= 16; i++) replay.Step(delay + i * .25);
            Assert.DoesNotContain(replay.Sent, c => c.command == "PlayItem");
            Assert.DoesNotContain(replay.Sent, c => c.command == "Seek" || c.command == "Unpause");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TheaterCapabilities_ControlLossDuringHandoffNeverRestores(bool primary)
        {
            using var replay = new TheaterReplay();
            replay.Begin("late_register", 4.587);
            Assert.Single(replay.Sent, c => c.command == "PlayItem");
            replay.DenyControl(primary ? replay.F.A : replay.F.B, cachedEvidence: false);
            var count = replay.Sent.Count;
            for (int i = 1; i <= 20; i++) replay.Step(4.587 + i * .25);
            Assert.Equal(count, replay.Sent.Count);
            Assert.Equal(RoomState.Waiting, replay.Runtime.State);
        }

        [Theory]
        [InlineData("late_register", .5)]
        [InlineData("late_register", 2.012)]
        [InlineData("late_register", 4.587)]
        [InlineData("early_register", 2.012)]
        [InlineData("queued_stop", 2.012)]
        public async Task TheaterNotifications_RealStopStartSnapshotsKeepCommandTraceAndOnlyCurrentCompletion(string scenario, double delay)
        {
            (string session, string command, string item, long? position)[] expected = null;
            foreach (bool notifications in new[] { false, true })
            {
                using var replay = new TheaterReplay(notifications: notifications);
                // Hold delivery while replaying the event sequence; this is queue staleness
                // coverage, not a claim about how long a client actually displayed anything.
                using ((IDisposable)typeof(RoomManager).GetMethod("TryEnterRoom", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(replay.F.Rooms, new object[] { replay.F.Room.Id }))
                {
                    replay.Begin(scenario, delay);
                    for (int i = 1; i <= 24 && replay.Runtime.State != RoomState.Watching; i++)
                        replay.Step(delay + i * .25);
                    Assert.Equal(RoomState.Watching, replay.Runtime.State);
                }
                await replay.F.NoticesIdle;
                if (!notifications) expected = replay.Sent.ToArray();
                else
                {
                    Assert.Equal(expected, replay.Sent.ToArray());
                    Assert.Equal(2, replay.F.Messages.Count);
                    Assert.All(replay.F.Messages, message => Assert.Equal("已跟随切换，同步完成", message));
                }
                Assert.Single(replay.Sent, c => c.command == "PlayItem");
                Assert.Equal(2, replay.Sent.Count(c => c.command == "Unpause"));
                Assert.DoesNotContain(replay.Sent, c => c.command == "Seek");
                Assert.Equal(delay < 2 ? 5 : 6, replay.Sent.Count);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task TheaterHoldResume_RealSdkStateReplacementAndSameIdRestartCannotReviveOldHold(bool primary)
        {
            using var replay = new HoldResumeReplay();
            replay.Begin();
            var session = primary ? replay.F.A : replay.F.B;
            var id = primary ? "next" : "peer-next";
            var playback = session.GetPlaySessionInfo(id);
            // The real SDK replaces PlayState before Stop, then reuses the ID.
            replay.F.ServerStop(session, playback);
            var restarted = replay.F.Play(session, id, 2.3, "43", false);
            replay.F.Start(session, restarted);
            replay.F.Poll(replay.F.Now.AddSeconds(2.5));
            Assert.Equal(RoomState.Waiting, replay.Runtime.State);
            replay.SetPair(3.4, 3.3);
            Assert.Equal(RoomState.Waiting, replay.Runtime.State);
            Assert.Equal(new[] { "b:PlayItem", "a:Pause" }, replay.Commands);
            await replay.F.NoticesIdle;
            Assert.DoesNotContain(replay.F.Messages, message => message.Contains("同步完成"));
            Assert.Contains(replay.F.Messages, message => message.Contains("手动重新同步"));
        }

        [Fact]
        public async Task TheaterHoldResume_NaturalAlignmentRestoresWatchingAndOrdinaryControls()
        {
            using var replay = new HoldResumeReplay();
            replay.Begin();
            replay.SetPair(3.399, 3.299);
            Assert.Equal(RoomState.Handoff, replay.Runtime.State);
            replay.SetPair(3.4, 3.3);
            Assert.Equal(RoomState.Watching, replay.Runtime.State);
            Assert.Equal(new[] { "b:PlayItem", "a:Pause" }, replay.Commands);
            await replay.F.NoticesIdle;
            Assert.Equal(2, replay.F.Messages.Count(message => message == "已跟随切换，同步完成"));

            replay.Set("u1", 3.3, true); replay.F.Poll(replay.F.Now.AddSeconds(3.5));
            Assert.Equal("b:Pause", replay.Commands.Last());
            replay.Set("u2", 3.3, true); replay.F.Poll(replay.F.Now.AddSeconds(3.6));
            replay.Set("u1", 3.3, false); replay.F.Poll(replay.F.Now.AddSeconds(7.1));
            Assert.Equal("b:Unpause", replay.Commands.Last());
            replay.Set("u2", 3.3, false); replay.F.Poll(replay.F.Now.AddSeconds(7.2));
            replay.Set("u1", 30, false); replay.F.Poll(replay.F.Now.AddSeconds(7.3));
            Assert.Equal(RoomState.Barrier, replay.Runtime.State);
            Assert.Equal("a:Pause", replay.Commands[^2]);
            Assert.Equal("b:Pause", replay.Commands[^1]);
        }

        private sealed class HoldResumeReplay : IDisposable
        {
            internal readonly Fixture F = new(useBridgeIssuer: true, theaterCapabilities: true, notifications: true);
            internal readonly List<string> Commands = new();
            internal RoomRuntime Runtime => F.Rooms.GetRuntime(F.Room.Id);
            internal HoldResumeReplay()
            {
                F.Manager.Setup(m => m.SendPlaystateCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlaystateRequest>(), It.IsAny<CancellationToken>()))
                    .Callback<string, string, PlaystateRequest, CancellationToken>((_, session, request, token) => Commands.Add(session + ":" + request.Command))
                    .Returns(Task.CompletedTask);
                F.Manager.Setup(m => m.SendPlayCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()))
                    .Callback<string, string, PlayRequest, CancellationToken>((_, session, request, token) => Commands.Add(session + ":PlayItem"))
                    .Returns(Task.CompletedTask);
                foreach (var session in new[] { F.A, F.B })
                    F.Start(session, F.Play(session, session == F.A ? "p1" : "p2", 40, "42", false));
                Runtime.Previous.Clear();
                foreach (var snapshot in F.Provider.GetSessionSnapshots()) Runtime.Previous[snapshot.UserId] = snapshot;
            }
            internal void Begin()
            {
                var old = F.A.GetPlaySessionInfo("p1"); F.ServerStop(F.A, old); F.A.RemovePlaySession(old);
                F.Start(F.A, F.Play(F.A, "next", 0, "43", false)); F.Poll(F.Now);
                Set("u1", 1.2, false); F.Poll(F.Now.AddSeconds(1.1));
                Set("u1", 1.2, true); F.Poll(F.Now.AddSeconds(1.2));
                old = F.B.GetPlaySessionInfo("p2"); F.ServerStop(F.B, old); F.B.RemovePlaySession(old);
                F.Start(F.B, F.Play(F.B, "peer-next", 1.2, "43", false)); F.Poll(F.Now.AddSeconds(1.3));
                SetPair(1.4, 1.3); SetPair(2.4, 2.3);
                Assert.Equal(RoomState.Handoff, Runtime.State);
                Assert.Equal(new[] { "b:PlayItem", "a:Pause" }, Commands);
            }
            internal void Set(string user, double position, bool paused)
            {
                var session = user == "u1" ? F.A : F.B;
                F.Progress(session, F.Play(session, user == "u1" ? "next" : "peer-next", position, "43", paused));
            }
            internal void SetPair(double elapsed, double position)
            { Set("u1", position, false); Set("u2", position, false); F.Poll(F.Now.AddSeconds(elapsed)); }
            public void Dispose() => F.Dispose();
        }

        private sealed class TheaterReplay : IDisposable
        {
            internal readonly Fixture F;
            internal RoomRuntime Runtime => F.Rooms.GetRuntime(F.Room.Id);
            internal readonly List<(string session, string command, string item, long? position)> Sent = new();
            private readonly Queue<(string session, string command, string item, long? position)> _pending = new();
            private double _last;
            private readonly double _peerStartPosition;
            internal TheaterReplay(double peerStartPosition = 0, bool notifications = false)
            {
                F = new Fixture(useBridgeIssuer: true, theaterCapabilities: true, notifications: notifications);
                _peerStartPosition = peerStartPosition;
                Assert.Equal(35, TheaterCommands.Length);
                F.Manager.Setup(m => m.SendPlaystateCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlaystateRequest>(), It.IsAny<CancellationToken>()))
                    .Callback<string, string, PlaystateRequest, CancellationToken>((_, session, command, token) => {
                        var target = session == "a" ? F.A : F.B;
                        var item = target.NowPlayingItem?.Id;
                        var sent = (session, command.Command.ToString(), item, command.SeekPositionTicks);
                        Sent.Add(sent); _pending.Enqueue(sent);
                    }).Returns(Task.CompletedTask);
                F.Manager.Setup(m => m.SendPlayCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()))
                    .Callback<string, string, PlayRequest, CancellationToken>((_, session, command, token) => {
                        var sent = (session, "PlayItem", command.ItemIds.Single().ToString(), (long?)null);
                        Sent.Add(sent); _pending.Enqueue(sent);
                    }).Returns(Task.CompletedTask);
                foreach (var session in new[] { F.A, F.B }) {
                    var playback = F.Play(session, session == F.A ? "p1" : "p2", 526.793, "42", false);
                    F.Start(session, playback);
                }
                Runtime.Previous.Clear();
                foreach (var snapshot in F.Provider.GetSessionSnapshots()) Runtime.Previous[snapshot.UserId] = snapshot;
                Runtime.PreviousAtUtc = F.Now.AddMilliseconds(-250);
            }
            internal void DenyControl(SessionInfo session, bool cachedEvidence)
            {
                session.SessionControllers = Array.Empty<ISessionController>();
                session.Capabilities = new ClientCapabilities { SupportsMediaControl = false,
                    SupportedCommands = cachedEvidence ? TheaterCommands : Array.Empty<string>() };
                Assert.False(SessionSnapshot.FromSessionInfo(session).SupportsRemoteControl);
            }
            internal void Begin(string scenario, double delay, string restriction = null)
            {
                if (restriction?.EndsWith("no_control") == true || restriction?.EndsWith("raw_denied") == true)
                    DenyControl(restriction.StartsWith("primary") ? F.A : F.B, restriction.EndsWith("raw_denied"));
                if (restriction == "membership") Assert.True(F.Rooms.LeaveParticipant(F.Room.Id, "u2"));
                var old = F.A.GetPlaySessionInfo("p1");
                if (scenario == "queued_stop") {
                    old.NowPlayingItem = null; old.PlayState = new PlayerStateInfo();
                    F.A.RemovePlaySession(old); F.Stop(F.A, old);
                } else { F.ServerStop(F.A, old); F.A.RemovePlaySession(old); }
                Step(0);
                for (int n = 1; n < Math.Ceiling(delay * 10); n++) {
                    if (scenario != "late_register" && n == 1) F.Play(F.A, "next", 0, "", false);
                    Step(n / 10.0);
                }
                if (restriction == "participant_identity") F.B.Id = "replacement";
                if (restriction == "participant_stop_restart") {
                    var peer = F.B.GetPlaySessionInfo("p2"); var item = peer.NowPlayingItem;
                    F.ServerStop(F.B, peer); peer.NowPlayingItem = item; F.Start(F.B, peer);
                }
                var nextItem = restriction == "old_item" ? "42" : "43";
                var next = F.Play(F.A, "next", 0, nextItem, false);
                next.NowPlayingItem = new BaseItemDto { Id = nextItem, RunTimeTicks = 1500L * 10000000 };
                if (restriction == "no_start") F.Progress(F.A, next); else F.Start(F.A, next);
                Step(delay);
            }
            internal void Step(double elapsed)
            {
                while (_pending.Count > 0) {
                    var command = _pending.Dequeue(); var session = command.session == "a" ? F.A : F.B;
                    var current = new[] { "p1", "p2", "next", "peer-next" }.Select(session.GetPlaySessionInfo)
                        .SingleOrDefault(p => p?.NowPlayingItem != null && !string.IsNullOrEmpty(p.NowPlayingItem.Id));
                    if (current == null) continue;
                    if (command.command == "PlayItem") {
                        F.ServerStop(session, current); session.RemovePlaySession(current);
                        var next = F.Play(session, "peer-next", _peerStartPosition, command.item, false); F.Start(session, next);
                    } else {
                        var state = current.PlayState;
                        var updated = F.Play(session, current.Id,
                            command.command == "Seek" ? command.position.Value / 10000000.0 : state.PositionTicks.GetValueOrDefault() / 10000000.0,
                            current.NowPlayingItem.Id,
                            command.command == "Pause" || command.command != "Unpause" && state.IsPaused);
                        F.Progress(session, updated);
                    }
                }
                foreach (var session in new[] { F.A, F.B }) {
                    var current = new[] { "p1", "p2", "next", "peer-next" }.Select(session.GetPlaySessionInfo)
                        .SingleOrDefault(p => p?.NowPlayingItem != null && !string.IsNullOrEmpty(p.NowPlayingItem.Id));
                    if (current == null || current.PlayState.IsPaused) continue;
                    var advanced = F.Play(session, current.Id,
                        current.PlayState.PositionTicks.GetValueOrDefault() / 10000000.0 + elapsed - _last,
                        current.NowPlayingItem.Id, false);
                    F.Progress(session, advanced);
                }
                _last = elapsed;
                F.Poll(F.Now.AddSeconds(elapsed));
            }
            public void Dispose() => F.Dispose();
        }

        [Theory]
        [InlineData("Seek", false, false)]
        [InlineData("Seek", false, true)]
        [InlineData("Seek", true, false)]
        [InlineData("Seek", true, true)]
        [InlineData("Unpause", false, false)]
        [InlineData("Unpause", false, true)]
        [InlineData("Unpause", true, false)]
        [InlineData("Unpause", true, true)]
        [InlineData("PlayItem", false, false)]
        [InlineData("PlayItem", false, true)]
        [InlineData("PlayItem", true, false)]
        [InlineData("PlayItem", true, true)]
        [InlineData("Pause", false, false)]
        [InlineData("Pause", false, true)]
        [InlineData("Pause", true, false)]
        [InlineData("Pause", true, true)]
        public void SourceBoundDispatch_StopBoundary(string command, bool targetStop, bool afterEntry)
        {
            using var f = new Fixture();
            f.Progress(f.A, f.A.GetPlaySessionInfo("p1"));
            f.Progress(f.B, f.B.GetPlaySessionInfo("p2"));
            var sources = f.Provider.GetSessionSnapshots().ToArray();
            var target = sources.Single(s => s.UserId == "u2");
            var session = targetStop ? f.B : f.A;
            var playback = session.GetPlaySessionInfo(targetStop ? "p2" : "p1");
            int sends = 0;
            void EnterTransport()
            {
                sends++;
                if (afterEntry) f.ServerStop(session, playback);
            }
            f.Manager.Setup(m => m.SendPlaystateCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlaystateRequest>(), It.IsAny<CancellationToken>()))
                .Callback(EnterTransport).Returns(Task.CompletedTask);
            f.Manager.Setup(m => m.SendPlayCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()))
                .Callback(EnterTransport).Returns(Task.CompletedTask);
            if (!afterEntry) f.ServerStop(session, playback);
            var issuer = new SessionBridgeCommandIssuer(f.Bridge);
            var method = typeof(SessionBridgeCommandIssuer).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(m => m.Name.EndsWith(command == "PlayItem" ? "ISourceBoundPlayItemIssuer.TryIssuePlayItem" : "ICancellableCommandIssuer.TryIssue"));
            object[] arguments = command == "PlayItem"
                ? new object[] { "room", "u1", "u2", target, "43", f.Now, CancellationToken.None, sources, null }
                : new object[] { "room", "u1", "u2", target, command, (long?)6000000000, f.Now, CancellationToken.None, sources, null };
            var accepted = (bool)method.Invoke(issuer, arguments);
            var expected = afterEntry || (!targetStop && command == "Pause");
            Assert.Equal(expected, accepted);
            Assert.Equal(expected ? 1 : 0, sends);
            playback.NowPlayingItem = new BaseItemDto { Id = "42", RunTimeTicks = 1500L * 10000000 };
            f.Start(session, playback);
            var fresh = f.Provider.GetSessionSnapshots().ToArray();
            arguments[3] = fresh.Single(s => s.UserId == "u2");
            arguments[arguments.Length - 2] = fresh;
            Assert.True((bool)method.Invoke(issuer, arguments));
            Assert.Equal(expected ? 2 : 1, sends);
        }

        [Theory]
        [InlineData("Seek", false, false)]
        [InlineData("Seek", false, true)]
        [InlineData("Seek", true, false)]
        [InlineData("Seek", true, true)]
        [InlineData("Unpause", false, false)]
        [InlineData("Unpause", false, true)]
        [InlineData("Unpause", true, false)]
        [InlineData("Unpause", true, true)]
        [InlineData("PlayItem", false, false)]
        [InlineData("PlayItem", false, true)]
        [InlineData("PlayItem", true, false)]
        [InlineData("PlayItem", true, true)]
        [InlineData("Seek", null, false)]
        [InlineData("Unpause", null, false)]
        [InlineData("PlayItem", null, false)]
        public void QueuedSourceStop_PreventsTransport(string command, bool? targetStop, bool restart)
        {
            using var f = new Fixture(useBridgeIssuer: true);
            var old = f.A.GetPlaySessionInfo("p1"); f.Progress(f.A, old);
            f.Progress(f.B, f.B.GetPlaySessionInfo("p2"));
            f.Play(f.A, "p1", command == "Seek" ? 600 : 300, "42", command == "Seek");
            if (command == "PlayItem")
            {
                f.A.RemovePlaySession(old);
                old = f.Play(f.A, "next", 0, "43"); f.Start(f.A, old);
            }
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var issuer = typeof(SyncEngine).GetField("_issuer", flags).GetValue(f.Engine);
            var dispatch = typeof(SessionBridgeCommandIssuer).GetField("_dispatch", flags).GetValue(issuer);
            var gate = dispatch.GetType().GetField("_lock", flags).GetValue(dispatch);
            Exception fault = null;
            var thread = new Thread(() => { try { f.Poll(f.Now); } catch (Exception e) { fault = e; } }) { IsBackground = true };
            lock (gate)
            {
                thread.Start();
                Assert.True(SpinWait.SpinUntil(() => (thread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, 2000));
                if (targetStop.HasValue)
                {
                    var stoppedSession = targetStop.Value ? f.B : f.A;
                    var stoppedPlayback = targetStop.Value ? f.B.GetPlaySessionInfo("p2") : old;
                    var item = stoppedPlayback.NowPlayingItem;
                    f.ServerStop(stoppedSession, stoppedPlayback);
                    if (restart)
                    {
                        stoppedPlayback.NowPlayingItem = item;
                        f.Start(stoppedSession, stoppedPlayback);
                    }
                }
            }
            Assert.True(thread.Join(6000)); Assert.Null(fault);
            if (!targetStop.HasValue)
            {
                var sent = Assert.Single(f.Manager.Invocations, i =>
                    i.Method.Name == "SendPlayCommand" || i.Method.Name == "SendPlaystateCommand");
                Assert.Equal("b", sent.Arguments[1]);
                if (command == "PlayItem")
                {
                    Assert.Equal("SendPlayCommand", sent.Method.Name);
                    Assert.Equal(43L, Assert.Single(((PlayRequest)sent.Arguments[2]).ItemIds));
                }
                else
                {
                    Assert.Equal("SendPlaystateCommand", sent.Method.Name);
                    var request = (PlaystateRequest)sent.Arguments[2];
                    Assert.Equal(command == "Seek" ? PlaystateCommand.Seek : PlaystateCommand.Unpause, request.Command);
                    if (command == "Seek") Assert.Equal(600L * 10000000, request.SeekPositionTicks);
                }
                return;
            }
            f.Manager.Verify(m => m.SendPlaystateCommand(It.IsAny<string>(), "b",
                It.Is<PlaystateRequest>(r => r.Command == PlaystateCommand.Seek || r.Command == PlaystateCommand.Unpause),
                It.IsAny<CancellationToken>()), Times.Never);
            f.Manager.Verify(m => m.SendPlayCommand(It.IsAny<string>(), "b", It.IsAny<PlayRequest>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(true, false, 2.635)]
        [InlineData(true, true, 2.635)]
        [InlineData(true, false, 3.3)]
        [InlineData(true, true, 3.3)]
        [InlineData(false, false, 2.635)]
        [InlineData(false, true, 2.635)]
        [InlineData(false, false, 3.3)]
        [InlineData(false, true, 3.3)]
        [InlineData(true, false, 0.3)]
        [InlineData(true, true, 0.3)]
        [InlineData(false, false, 0.3)]
        [InlineData(false, true, 0.3)]
        [InlineData(true, true, 0.0)]
        [InlineData(false, false, 0.0)]
        public void BridgeTransition_ExplicitStartSendsExactlyOnePlayItem(bool replacement, bool sameId, double delay)
        {
            using var f = new Fixture(useBridgeIssuer:true);
            var old = f.A.GetPlaySessionInfo("p1");
            f.Progress(f.A, old);
            var captured = f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1");
            if (replacement) f.ServerStop(f.A,old); else f.Stop(f.A,old);
            f.A.RemovePlaySession(old);
            if (delay > 0) f.Poll(f.Now);
            if (delay > 2) f.Poll(f.Now.AddSeconds(2.1));
            var next = f.Play(f.A,sameId ? "p1" : "next",0,"43");
            f.Start(f.A,next);
            f.Poll(f.Now.AddSeconds(delay));
            f.Poll(f.Now.AddSeconds(delay+0.1));
            Assert.True(captured.Stopped);
            f.Manager.Verify(m=>m.SendPlayCommand(It.IsAny<string>(),"b",It.Is<PlayRequest>(r=>r.ItemIds.Single()==43L),It.IsAny<CancellationToken>()),Times.Once);
            f.Manager.Verify(m=>m.SendPlaystateCommand(It.IsAny<string>(),It.IsAny<string>(),It.Is<PlaystateRequest>(r=>r.Command==PlaystateCommand.Seek || r.Command==PlaystateCommand.Unpause),It.IsAny<CancellationToken>()),Times.Never);
        }

        [Theory]
        [InlineData("exit")]
        [InlineData("old_progress")]
        [InlineData("new_progress")]
        [InlineData("quick_progress")]
        [InlineData("expired")]
        [InlineData("participant_stop")]
        [InlineData("participant_restart")]
        [InlineData("quick_participant_restart")]
        [InlineData("quick_participant_identity")]
        [InlineData("participant_identity")]
        public void BridgeTransition_RejectsUnprovenOrInvalidatedRestart(string scenario)
        {
            using var f = new Fixture(useBridgeIssuer:true);
            var old = f.A.GetPlaySessionInfo("p1"); f.Progress(f.A,old);
            f.ServerStop(f.A,old); f.A.RemovePlaySession(old);
            if (!scenario.StartsWith("quick_")) { f.Poll(f.Now); f.Poll(f.Now.AddSeconds(2.1)); }
            if (scenario=="participant_stop") f.Stop(f.B,f.B.GetPlaySessionInfo("p2"));
            if (scenario.EndsWith("participant_restart"))
            {
                var participant=f.B.GetPlaySessionInfo("p2");
                f.Stop(f.B,participant); f.Start(f.B,participant);
            }
            if (scenario.EndsWith("participant_identity")) f.B.Id="replacement";
            if (scenario!="exit")
            {
                var next=f.Play(f.A,scenario=="old_progress" ? "p1" : "next",0,"43");
                if (scenario.EndsWith("progress")) f.Progress(f.A,next); else f.Start(f.A,next);
            }
            var delay=scenario=="expired" ? 10.1 : scenario.StartsWith("quick_") ? 0.3 : 3.3;
            f.Poll(f.Now.AddSeconds(delay)); f.Poll(f.Now.AddSeconds(delay+0.1));
            f.Manager.Verify(m=>m.SendPlayCommand(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<PlayRequest>(),It.IsAny<CancellationToken>()),Times.Never);
            f.Manager.Verify(m=>m.SendPlaystateCommand(It.IsAny<string>(),It.IsAny<string>(),It.Is<PlaystateRequest>(r=>r.Command==PlaystateCommand.Seek || r.Command==PlaystateCommand.Unpause),It.IsAny<CancellationToken>()),Times.Never);
        }

        [Fact]
        public void BridgeTransition_NormalPauseResumeAndSeekStillReachServer()
        {
            using var f = new Fixture(useBridgeIssuer:true);
            f.Play(f.A,"p1",300,paused:false); f.Polls(0,3);
            f.Manager.Verify(m=>m.SendPlaystateCommand(It.IsAny<string>(),"b",It.Is<PlaystateRequest>(r=>r.Command==PlaystateCommand.Unpause),It.IsAny<CancellationToken>()),Times.AtLeastOnce);
            f.Play(f.B,"p2",300,paused:false); f.Polls(3,2);
            f.Play(f.A,"p1",300,paused:true); f.Polls(5,3);
            f.Manager.Verify(m=>m.SendPlaystateCommand(It.IsAny<string>(),"b",It.Is<PlaystateRequest>(r=>r.Command==PlaystateCommand.Pause),It.IsAny<CancellationToken>()),Times.AtLeastOnce);
            f.Play(f.B,"p2",300,paused:true); f.Polls(8,3);
            f.Play(f.A,"p1",600,paused:true); f.Polls(11,4);
            f.Manager.Verify(m=>m.SendPlaystateCommand(It.IsAny<string>(),"b",It.Is<PlaystateRequest>(r=>r.Command==PlaystateCommand.Seek && r.SeekPositionTicks==600L*10000000),It.IsAny<CancellationToken>()),Times.AtLeastOnce);
            f.Manager.Verify(m=>m.SendPlayCommand(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<PlayRequest>(),It.IsAny<CancellationToken>()),Times.Never);
        }

        [Fact]
        public void StopRemoveAndLateProgressRecreation_CannotSeekFollower()
        {
            using var f=new Fixture();
            var old=f.A.GetPlaySessionInfo("p1");
            f.Stop(f.A,old); f.A.RemovePlaySession(old);
            var recreated=f.Play(f.A,"p1",1499);
            Assert.NotSame(old,recreated);
            Assert.True(f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1").Stopped);
            f.Polls(0,8);
            Assert.DoesNotContain(f.Issuer.Calls,c=>c.command=="Seek" || c.command=="Unpause");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ServerStateReplacement_RevokesCapturedSnapshotsWithoutRevivingThemOnStart(bool explicitStart)
        {
            using var f=new Fixture();var p=f.A.GetPlaySessionInfo("p1");
            if(explicitStart) f.Start(f.A,p); else f.Progress(f.A,p);
            var captured=f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1");
            f.ServerStop(f.A,p);f.A.RemovePlaySession(p);
            Assert.True(captured.Stopped);
            var reopened=f.Play(f.A,"p1",600);
            f.Progress(f.A,reopened); // Missing Start must not clear a stopped ID.
            var late=f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1");
            Assert.True(late.Stopped);
            f.Start(f.A,reopened);
            Assert.True(captured.Stopped);Assert.True(late.Stopped);
            Assert.False(f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1").Stopped);
        }

        [Fact]
        public void ServerStateReplacement_DelayedOldStopRevokesOnlyItsOwnSnapshots()
        {
            using var f=new Fixture();var old=f.A.GetPlaySessionInfo("p1");f.Start(f.A,old);
            var captured=f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1");
            f.A.RemovePlaySession(old);
            var next=f.Play(f.A,"p1",500);f.Start(f.A,next);
            var current=f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1");
            f.Progress(f.A,old); // Queued old callback must not replace the new owner.
            f.ServerStop(f.A,old);
            Assert.True(captured.Stopped);Assert.False(current.Stopped);
            Assert.False(f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1").Stopped);
        }

        [Fact]
        public void ServerStateReplacement_RevokesEveryObservedStateOfThePlayback()
        {
            using var f=new Fixture();var p=f.A.GetPlaySessionInfo("p1");f.Progress(f.A,p);
            var first=f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1");
            p.PlayState=new PlayerStateInfo();f.Play(f.A,"p1",500);
            // Bind must adopt this replacement even before its Progress callback.
            var second=f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1");
            f.Start(f.A,p); // Duplicate live Start must retain all captured markers.
            f.ServerStop(f.A,p);
            Assert.True(first.Stopped);Assert.True(second.Stopped);
        }

        [Fact]
        public void ServerStateReplacement_StopBetweenRestoreCommandsCancelsSecondUnpause()
        {
            using var f=new Fixture();f.Start(f.A,f.A.GetPlaySessionInfo("p1"));
            f.Play(f.A,"p1",600,paused:false);f.Play(f.B,"p2",300,paused:false);f.Polls(0,2);
            f.Play(f.A,"p1",600);f.Play(f.B,"p2",300);f.Polls(2,2);
            f.Issuer.OnIssue=command=> { if(command=="Unpause") f.ServerStop(f.A,f.A.GetPlaySessionInfo("p1")); };
            f.Play(f.B,"p2",600);f.Polls(4,2);
            Assert.Single(f.Issuer.Calls,c=>c.command=="Unpause");
        }

        [Theory]
        [InlineData("Seek")]
        [InlineData("Unpause")]
        [InlineData("Pause")]
        [InlineData("PlayItem")]
        public async Task ServerStateReplacement_QueuedTargetCannotSendAfterStopAndSameIdStart(string command)
        {
            using var f=new Fixture();var p=f.A.GetPlaySessionInfo("p1");f.Start(f.A,p);
            var snapshot=f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1");
            int sends=0;
            f.Manager.Setup(m=>m.SendPlaystateCommand(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<PlaystateRequest>(),It.IsAny<CancellationToken>()))
                .Callback(()=>Interlocked.Increment(ref sends)).Returns(Task.CompletedTask);
            f.Manager.Setup(m=>m.SendPlayCommand(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<PlayRequest>(),It.IsAny<CancellationToken>()))
                .Callback(()=>Interlocked.Increment(ref sends)).Returns(Task.CompletedTask);
            var issuer=new SessionBridgeCommandIssuer(f.Bridge);
            bool Issue(SessionSnapshot target,out string error) => command=="PlayItem"
                ? issuer.TryIssuePlayItem("room","u1","u1",target,"43",f.Now,out error)
                : issuer.TryIssue("room","u1","u1",target,command,600L*10000000,f.Now,out error);
            var dispatch=typeof(SessionBridgeCommandIssuer).GetField("_dispatch",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(issuer);
            var gate=dispatch.GetType().GetField("_lock",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dispatch);
            var result=new TaskCompletionSource<(bool ok,string error)>(TaskCreationOptions.RunContinuationsAsynchronously);
            var caller=new Thread(()=> {
                try {var ok=Issue(snapshot,out var error);result.TrySetResult((ok,error));}
                catch(Exception error){result.TrySetException(error);}
            }){IsBackground=true};
            lock(gate)
            {
                caller.Start();
                Assert.True(SpinWait.SpinUntil(()=>(caller.ThreadState&System.Threading.ThreadState.WaitSleepJoin)!=0,2000));
                f.ServerStop(f.A,p);f.Play(f.A,"p1",500);
                // SessionManager.OnPlaybackStart also restores NowPlayingItem;
                // SDK GetOrAdd alone only updates PlayState on an existing object.
                p.NowPlayingItem=new BaseItemDto{Id="42",RunTimeTicks=1500L*10000000};
                f.Start(f.A,p);
            }
            var completed=await result.Task.WaitAsync(TimeSpan.FromSeconds(6));
            Assert.True(caller.Join(2000));Assert.False(completed.ok);Assert.Equal("session_offline",completed.error);
            Assert.Equal(0,Volatile.Read(ref sends));
            Assert.True(Issue(f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1"),out var nextError),nextError);
            Assert.Equal(1,Volatile.Read(ref sends));
        }

        [Fact]
        public void ServerStateReplacement_FirstProgressAdoptsPreviouslyCapturedMarker()
        {
            using var f=new Fixture();
            var captured=f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1");
            var p=f.A.GetPlaySessionInfo("p1");f.Progress(f.A,p);
            f.ServerStop(f.A,p);
            Assert.True(captured.Stopped);
        }

        [Fact]
        public void ServerStateReplacement_ConcurrentCapturesRemainStoppedAcrossSameObjectStart()
        {
            using var f=new Fixture();var p=f.A.GetPlaySessionInfo("p1");f.Start(f.A,p);
            var captured=new SessionSnapshot[32];
            Parallel.For(0,captured.Length,i=>captured[i]=f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1"));
            f.ServerStop(f.A,p);
            f.Play(f.A,"p1",600);p.NowPlayingItem=new BaseItemDto{Id="42",RunTimeTicks=1500L*10000000};f.Start(f.A,p);
            var next=f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1");
            Assert.False(next.Stopped);Assert.All(captured,s=>Assert.True(s.Stopped));
            f.ServerStop(f.A,p);
            Assert.True(next.Stopped);Assert.All(captured,s=>Assert.True(s.Stopped));
        }

        [Fact]
        public void ServerStateReplacement_WithoutPriorIdentityDoesNotGuessCapturedOwnership()
        {
            using var f=new Fixture();
            var captured=f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1");
            var p=f.A.GetPlaySessionInfo("p1");f.ServerStop(f.A,p);f.A.RemovePlaySession(p);
            // No public SDK identity links this old state to the Stop after replacement.
            Assert.False(captured.Stopped);
            f.Play(f.A,"p1",1499);
            Assert.True(f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1").Stopped);
        }

        [Fact]
        public void StopRevokesCapturedSnapshot_BeforeWakeAndAfterNewStart()
        {
            using var f=new Fixture();
            var captured=f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1");
            f.Bridge.PlaybackStopped += (_,__)=>Assert.True(captured.Stopped);
            var p=f.A.GetPlaySessionInfo("p1"); f.Stop(f.A,p);
            f.Start(f.A,p); // Explicit same-ID Start, even if Emby reuses the object.
            Assert.True(captured.Stopped);
            Assert.False(f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1").Stopped);
        }

        [Fact]
        public void DuplicateLiveStart_DoesNotOrphanCapturedMarker()
        {
            using var f=new Fixture();
            var captured=f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1");
            var p=f.A.GetPlaySessionInfo("p1");f.Start(f.A,p);f.Stop(f.A,p);
            Assert.True(captured.Stopped);
        }

        [Theory]
        [InlineData("42")]
        [InlineData("43")]
        public void ReopenOrSwitch_NewIdNotPoisonedByDelayedOldStop(string item)
        {
            using var f=new Fixture(); var old=f.A.GetPlaySessionInfo("p1");
            f.Stop(f.A,old); f.A.RemovePlaySession(old);
            var next=f.Play(f.A,"next",200,item); f.Start(f.A,next);
            f.Stop(f.A,old);
            var current=f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1");
            Assert.False(current.Stopped); Assert.Equal(item,current.ItemId);
        }

        [Fact]
        public void SameIdNewObjectStart_DelayedOldStopCannotPoisonIt()
        {
            using var f=new Fixture();var old=f.A.GetPlaySessionInfo("p1");
            f.Stop(f.A,old);f.A.RemovePlaySession(old);
            var next=f.Play(f.A,"p1",100);f.Start(f.A,next);
            f.Stop(f.A,old);
            Assert.False(f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1").Stopped);
        }

        [Fact]
        public void IdOnlyStopAfterRemoval_RevokesPreviouslyObservedPlayback()
        {
            using var f=new Fixture();var p=f.A.GetPlaySessionInfo("p1");f.Start(f.A,p);
            var captured=f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1");
            f.A.RemovePlaySession(p);
            f.Manager.Raise(m=>m.PlaybackStopped+=null,new PlaybackStopEventArgs{Session=f.A,PlaySessionId="p1"});
            Assert.True(captured.Stopped);
        }

        [Theory]
        [InlineData(600)]
        [InlineData(1440)]
        public void PausedManualSeekAndContinuousForward_AreStillSynchronized(double position)
        {
            using var f=new Fixture();
            f.Play(f.A,"p1",position); f.Polls(0,4);
            Assert.Contains(f.Issuer.Calls,c=>c.user=="u2" && c.command=="Seek" && c.position==(long)(position*10000000));
            f.Play(f.B,"p2",position); f.Polls(4,3);
            Assert.Equal(RoomState.Watching,f.Rooms.GetRuntime(f.Room.Id).State);
            f.Play(f.A,"p1",position+20); f.Polls(7,4);
            Assert.Contains(f.Issuer.Calls,c=>c.user=="u2" && c.command=="Seek" && c.position==(long)((position+20)*10000000));
        }

        [Fact]
        public void PlayingSeek_StillUsesPauseBarrier()
        {
            using var f=new Fixture();
            f.Play(f.A,"p1",1440,paused:false); f.Play(f.B,"p2",300,paused:false);
            f.Polls(0,2);
            Assert.Contains(f.Issuer.Calls,c=>c.command=="Pause");
            f.Play(f.A,"p1",1440); f.Play(f.B,"p2",300);
            f.Polls(2,2);
            Assert.Contains(f.Issuer.Calls,c=>c.command=="Seek" && c.position==1440L*10000000);
            f.Play(f.B,"p2",1440); f.Polls(4,2);
            Assert.Contains(f.Issuer.Calls,c=>c.command=="Unpause");
        }

        [Fact]
        public void StopDuringBarrierPause_DisallowsSubsequentSeekOrUnpause()
        {
            using var f=new Fixture();
            f.Play(f.A,"p1",1440,paused:false);f.Play(f.B,"p2",300,paused:false);
            f.Issuer.OnIssue=command=> { if(command=="Pause") f.Stop(f.A,f.A.GetPlaySessionInfo("p1")); };
            f.Polls(0,2);
            Assert.Contains(f.Issuer.Calls,c=>c.command=="Pause");
            f.Play(f.A,"p1",1440);f.Play(f.B,"p2",300);
            f.Polls(2,8);
            Assert.DoesNotContain(f.Issuer.Calls,c=>c.command=="Seek" || c.command=="Unpause");
        }

        [Fact]
        public void UnresolvableStartCannotClearStopTombstone()
        {
            using var f=new Fixture();var old=f.A.GetPlaySessionInfo("p1");
            f.Stop(f.A,old);f.A.RemovePlaySession(old);
            f.Manager.Raise(m=>m.PlaybackStart+=null,new PlaybackProgressEventArgs{Session=f.A,PlaySessionId="p1"});
            f.Play(f.A,"p1",1499);
            Assert.True(f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1").Stopped);
        }

        [Fact]
        public void StopBetweenRestoreCommands_RevokesAlreadySelectedSnapshots()
        {
            using var f=new Fixture();
            f.Play(f.A,"p1",600,paused:false);f.Play(f.B,"p2",300,paused:false);
            f.Polls(0,2);
            f.Play(f.A,"p1",600);f.Play(f.B,"p2",300);f.Polls(2,2);
            f.Issuer.OnIssue=command=> { if(command=="Unpause") f.Stop(f.A,f.A.GetPlaySessionInfo("p1")); };
            f.Play(f.B,"p2",600);f.Polls(4,2);
            Assert.Single(f.Issuer.Calls,c=>c.command=="Unpause");
        }

        [Fact]
        public void ReplacementServerSession_DoesNotInheritOldStop()
        {
            using var f=new Fixture();f.Stop(f.A,f.A.GetPlaySessionInfo("p1"));
            var replacement=new SessionInfo(f.Manager.Object,Mock.Of<ILogger>()) {Id=f.A.Id,UserId="u1"};
            f.Play(replacement,"p1",500);
            f.Manager.Setup(m=>m.Sessions).Returns(new[]{replacement,f.B});
            Assert.False(f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1").Stopped);
        }

        [Fact]
        public void MissingServerSessionId_DoesNotCreateSpeculativeStop()
        {
            using var f=new Fixture();f.A.Id=null;
            f.Stop(f.A,f.A.GetPlaySessionInfo("p1"));f.A.Id="a";
            Assert.False(f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1").Stopped);
        }

        [Fact]
        public void MissingOrContradictoryStopIdentity_DoesNotGuessCurrentPlayback()
        {
            using var f=new Fixture();
            f.Manager.Raise(m=>m.PlaybackStopped+=null,new PlaybackStopEventArgs {Session=f.A});
            f.Manager.Raise(m=>m.PlaybackStopped+=null,new PlaybackStopEventArgs {Session=f.A, PlaySession=f.A.GetPlaySessionInfo("p1"), PlaySessionId="other"});
            f.Manager.Raise(m=>m.PlaybackStopped+=null,new PlaybackStopEventArgs {PlaySessionId="p1"});
            Assert.False(f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1").Stopped);
        }

        [Fact]
        public void MissingStringId_UsesMatchingEventPlaybackObject()
        {
            using var f=new Fixture();
            f.Manager.Raise(m=>m.PlaybackStopped+=null,new PlaybackStopEventArgs {Session=f.A,PlaySession=f.A.GetPlaySessionInfo("p1")});
            Assert.True(f.Provider.GetSessionSnapshots().Single(s=>s.UserId=="u1").Stopped);
        }

        [Fact]
        public void NoStopBeforeTailJump_RemainsOutsideProtection()
        {
            using var f=new Fixture();f.Play(f.A,"p1",1500);f.Polls(0,4);
            Assert.Contains(f.Issuer.Calls,c=>c.command=="Seek");
        }
    }
}
