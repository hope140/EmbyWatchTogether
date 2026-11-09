using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Session;
using Moq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
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
            internal Fixture()
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
                Engine = new SyncEngine(Rooms, Provider, Issuer, () => "server", notifyOnSyncActions:false,logManager:logs.Object);
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
            public void Dispose() { Engine.Dispose(); Bridge.Dispose(); }
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
            f.Play(f.B,"p2",600);f.Polls(4,1);
            f.Issuer.OnIssue=command=> { if(command=="Unpause") f.Stop(f.A,f.A.GetPlaySessionInfo("p1")); };
            f.Polls(5,1);
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
