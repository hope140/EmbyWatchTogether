using System;
using System.Collections.Generic;
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
            internal Fixture(bool useBridgeIssuer = false)
            {
                A = Session("a", "u1"); B = Session("b", "u2");
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
                Engine = new SyncEngine(Rooms, Provider, useBridgeIssuer ? new SessionBridgeCommandIssuer(Bridge) : Issuer, () => "server", notifyOnSyncActions:false,logManager:logs.Object);
            }
            internal void Poll(DateTimeOffset now) { Engine.PollOnce(now); Assert.True(Errors.Count==0,string.Join("\n",Errors)); }
            internal void Polls(int first, int count)
            { for(var i=first;i<first+count;i++) Poll(Now.AddMilliseconds(i*100)); }
            private SessionInfo Session(string id, string user)
            {
                var controller = new Mock<ISessionController>();
                controller.SetupGet(c => c.SupportsMediaControl).Returns(true);
                return new SessionInfo(Manager.Object, Mock.Of<ILogger>()) {
                    Id=id, UserId=user, LastActivityDate=Now.UtcDateTime,
                    Capabilities=new ClientCapabilities { SupportsMediaControl=true, SupportedCommands=new[]{"Pause","Unpause","Seek"} },
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
