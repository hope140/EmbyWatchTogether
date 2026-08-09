using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.Session;
using MediaBrowser.Model.Users;
using Moq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    [CollectionDefinition("Plugin singleton", DisableParallelization = true)]
    public sealed class PluginSingletonCollection : ICollectionFixture<object>
    {
    }

    [Collection("Plugin singleton")]
    public class WatchTogetherServiceRoomTests
    {
        [Fact]
        public void Leave_FromWatching_PausesTheOtherOnlineParticipant()
        {
            var primaryUserId = Guid.NewGuid().ToString("N");
            var leavingUserId = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom(
                "server-1", "http://emby", "room", "admin-1",
                new[] { primaryUserId, leavingUserId }, primaryUserId);
            manager.GetRuntime(room.Id).State = RoomState.Watching;

            var issuer = new RecordingIssuer();
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(s => s.Sessions).Returns(new List<SessionInfo>
            {
                NewSession(sessionManager, "session-primary", primaryUserId),
                NewSession(sessionManager, "session-leaving", leavingUserId),
            });
            using var bridge = new SessionBridge(sessionManager.Object);
            var plugin = NewPlugin(manager, bridge, issuer, "server-1");
            var service = NewService(leavingUserId);

            object response = WithPlugin(plugin, () =>
                service.Post(new LeaveRoomRequest { Id = room.Id }));

            Assert.True(GetBoolean(response, "Changed"));
            Assert.Single(issuer.Issued);
            Assert.Equal(primaryUserId, issuer.Issued[0].userId);
            Assert.Equal(RemoteCommands.Pause, issuer.Issued[0].command);
        }

        [Fact]
        public void Leave_PrimaryFromWatching_PausesTheOtherOnlineParticipant()
        {
            var primaryUserId = Guid.NewGuid().ToString("N");
            var leavingUserId = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom(
                "server-1", "http://emby", "room", "admin-1",
                new[] { primaryUserId, leavingUserId }, primaryUserId);
            manager.GetRuntime(room.Id).State = RoomState.Watching;

            var issuer = new RecordingIssuer();
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(s => s.Sessions).Returns(new List<SessionInfo>
            {
                NewSession(sessionManager, "session-leaving", leavingUserId),
            });
            using var bridge = new SessionBridge(sessionManager.Object);
            var plugin = NewPlugin(manager, bridge, issuer, "server-1");
            var service = NewService(primaryUserId);

            object response = WithPlugin(plugin, () =>
                service.Post(new LeaveRoomRequest { Id = room.Id }));

            Assert.True(GetBoolean(response, "Changed"));
            Assert.Single(issuer.Issued);
            Assert.Equal(leavingUserId, issuer.Issued[0].userId);
            Assert.Equal(RemoteCommands.Pause, issuer.Issued[0].command);
        }

        [Fact]
        public void Leave_RepeatedRequestReturnsSuccessWithoutPausingAgain()
        {
            var primaryUserId = Guid.NewGuid().ToString("N");
            var leavingUserId = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom(
                "server-1", "http://emby", "room", "admin-1",
                new[] { primaryUserId, leavingUserId }, primaryUserId);
            manager.GetRuntime(room.Id).State = RoomState.Watching;

            var issuer = new RecordingIssuer();
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(s => s.Sessions).Returns(new List<SessionInfo>
            {
                NewSession(sessionManager, "session-primary", primaryUserId),
            });
            using var bridge = new SessionBridge(sessionManager.Object);
            var plugin = NewPlugin(manager, bridge, issuer, "server-1");
            var service = NewService(leavingUserId);

            WithPlugin(plugin, () => service.Post(new LeaveRoomRequest { Id = room.Id }));
            issuer.Issued.Clear();
            manager.GetRuntime(room.Id).State = RoomState.Watching;

            object repeatedResponse = WithPlugin(plugin, () =>
                service.Post(new LeaveRoomRequest { Id = room.Id }));

            Assert.False(GetBoolean(repeatedResponse, "Changed"));
            Assert.Empty(issuer.Issued);
        }

        [Fact]
        public void Join_ChangedNotifiesOnlyOtherJoinedParticipant()
        {
            var u1 = Guid.NewGuid().ToString("N");
            var u2 = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "", "room", "admin-1", new[] { u1, u2 }, u1);
            manager.SetParticipantJoined(room.Id, u2, false);
            var sm = new Mock<ISessionManager>();
            sm.Setup(s => s.Sessions).Returns(new[] { NewSession(sm, "s1", u1), NewSession(sm, "s2", u2) });
            sm.Setup(s => s.SendMessageCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            using var bridge = new SessionBridge(sm.Object);
            var plugin = NewPlugin(manager, bridge, new RecordingIssuer(), "server-1");
            var service = NewService(u2);
            WithPlugin(plugin, () => service.Post(new JoinRoomRequest { Id = room.Id }));
            sm.Verify(s => s.SendMessageCommand(It.IsAny<string>(), "s1", It.Is<MessageCommand>(m => m.Header == "一起观看" && m.Text == "对方已加入房间，请打开同一视频" && m.TimeoutMs == 3000), It.IsAny<CancellationToken>()), Times.Once);
            sm.Verify(s => s.SendMessageCommand(It.IsAny<string>(), "s2", It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>()), Times.Never);
            WithPlugin(plugin, () => service.Post(new JoinRoomRequest { Id = room.Id }));
            sm.Verify(s => s.SendMessageCommand(It.IsAny<string>(), "s1", It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData("pause", "管理员已暂停房间播放")]
        [InlineData("resume", "管理员已继续房间播放")]
        public void Control_PauseAndResume_NotifyOnlyIssuedCurrentSessions(string action, string text)
        {
            var u1 = Guid.NewGuid().ToString("N");
            var u2 = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "", "room", "admin-1", new[] { u1, u2 }, u1);
            var issuer = new RecordingIssuer();
            var sm = new Mock<ISessionManager>();
            sm.Setup(s => s.Sessions).Returns(new[] { NewSession(sm, "s1", u1), NewSession(sm, "s2", u2) });
            sm.Setup(s => s.SendMessageCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            using var bridge = new SessionBridge(sm.Object);
            var plugin = NewPlugin(manager, bridge, issuer, "server-1");
            var service = NewService(u1, true);
            WithPlugin(plugin, () => service.Post(new ControlRoomRequest { Id = room.Id, Action = action }));
            var expectedTargets = issuer.Issued.Select(x => x.userId == u1 ? "s1" : "s2").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actualTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            sm.Invocations.Where(i => i.Method.Name == nameof(ISessionManager.SendMessageCommand)).ToList().ForEach(i => actualTargets.Add((string)i.Arguments[1]));
            Assert.Equal(expectedTargets, actualTargets);
            sm.Verify(s => s.SendMessageCommand(It.IsAny<string>(), It.IsAny<string>(), It.Is<MessageCommand>(m => m.Header == "一起观看" && m.Text == text && m.TimeoutMs == 3000), It.IsAny<CancellationToken>()), Times.Exactly(expectedTargets.Count));
        }

        [Fact]
        public void Leave_ChangedNotifiesRemainingParticipant_AndRepeatedLeaveDoesNotNotify()
        {
            var u1 = Guid.NewGuid().ToString("N");
            var u2 = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "", "room", "admin-1", new[] { u1, u2 }, u1);
            var sm = new Mock<ISessionManager>();
            sm.Setup(s => s.Sessions).Returns(new[] { NewSession(sm, "s1", u1), NewSession(sm, "s2", u2) });
            sm.Setup(s => s.SendMessageCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            using var bridge = new SessionBridge(sm.Object);
            var plugin = NewPlugin(manager, bridge, new RecordingIssuer(), "server-1");
            var service = NewService(u2);
            WithPlugin(plugin, () => service.Post(new LeaveRoomRequest { Id = room.Id }));
            sm.Verify(s => s.SendMessageCommand(It.IsAny<string>(), "s1", It.Is<MessageCommand>(m => m.Header == "一起观看" && m.Text == "对方已退出房间，自动同步已停止" && m.TimeoutMs == 3000), It.IsAny<CancellationToken>()), Times.Once);
            sm.Verify(s => s.SendMessageCommand(It.IsAny<string>(), "s2", It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>()), Times.Never);
            WithPlugin(plugin, () => service.Post(new LeaveRoomRequest { Id = room.Id }));
            sm.Verify(s => s.SendMessageCommand(It.IsAny<string>(), "s1", It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void Control_Resync_NotifiesCurrentJoinedDisplayCapableSessions()
        {
            var u1 = Guid.NewGuid().ToString("N");
            var u2 = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "", "room", "admin-1", new[] { u1, u2 }, u1);
            var sm = new Mock<ISessionManager>();
            sm.Setup(s => s.Sessions).Returns(new[] { NewSession(sm, "s1", u1, new[] { "DisplayMessage" }), NewSession(sm, "s2", u2, Array.Empty<string>()) });
            sm.Setup(s => s.SendMessageCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            using var bridge = new SessionBridge(sm.Object);
            var plugin = NewPlugin(manager, bridge, new RecordingIssuer(), "server-1");
            var service = NewService(u1, true);
            WithPlugin(plugin, () => service.Post(new ControlRoomRequest { Id = room.Id, Action = "resync" }));
            sm.Verify(s => s.SendMessageCommand(It.IsAny<string>(), "s1", It.Is<MessageCommand>(m => m.Header == "一起观看" && m.Text == "管理员已发起重新同步，请稍候" && m.TimeoutMs == 3000), It.IsAny<CancellationToken>()), Times.Once);
            sm.Verify(s => s.SendMessageCommand(It.IsAny<string>(), "s2", It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void SyncNoticesDisabled_SuppressesMembershipAndControl_ButExplicitMessageStillSends()
        {
            var u1 = Guid.NewGuid().ToString("N");
            var u2 = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "", "room", "admin-1", new[] { u1, u2 }, u1);
            manager.SetParticipantJoined(room.Id, u2, false);
            var sm = new Mock<ISessionManager>();
            sm.Setup(s => s.Sessions).Returns(new[] { NewSession(sm, "s1", u1), NewSession(sm, "s2", u2) });
            sm.Setup(s => s.SendMessageCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            using var bridge = new SessionBridge(sm.Object);
            var plugin = NewPlugin(manager, bridge, new RecordingIssuer(), "server-1");
            SetPluginConfiguration(plugin, new PluginConfiguration { NotifyOnSyncActions = false });
            var service = NewService(u2, true);
            WithPlugin(plugin, () => service.Post(new JoinRoomRequest { Id = room.Id }));
            WithPlugin(plugin, () => service.Post(new ControlRoomRequest { Id = room.Id, Action = "resync" }));
            sm.Verify(
                s => s.SendMessageCommand(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<MessageCommand>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            sm.Invocations.Clear();
            WithPlugin(plugin, () => service.Post(new SendRoomMessageRequest { Id = room.Id, Text = "explicit" }));
            sm.Verify(s => s.SendMessageCommand(It.IsAny<string>(), It.IsAny<string>(), It.Is<MessageCommand>(m => m.Text == "explicit"), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [InlineData("offline")]
        [InlineData("unsupported")]
        [InlineData("server")]
        public void SyncNotice_SkipsOfflineDisplayUnsupportedAndServerMismatch(string mode)
        {
            var u1 = Guid.NewGuid().ToString("N");
            var u2 = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "", "room", "admin-1", new[] { u1, u2 }, u1);
            var sm = new Mock<ISessionManager>();
            var target = NewSession(sm, "s2", u2, mode == "unsupported" ? Array.Empty<string>() : new[] { "DisplayMessage" });
            sm.Setup(s => s.Sessions).Returns(mode == "offline" ? new[] { NewSession(sm, "s1", u1) } : new[] { NewSession(sm, "s1", u1), target });
            sm.Setup(s => s.SendMessageCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            using var bridge = new SessionBridge(sm.Object);
            var plugin = NewPlugin(manager, bridge, new RecordingIssuer(), mode == "server" ? "server-2" : "server-1");
            var service = NewService(u1, true);
            WithPlugin(plugin, () => service.Post(new ControlRoomRequest { Id = room.Id, Action = "resync" }));
            sm.Verify(s => s.SendMessageCommand(It.IsAny<string>(), "s2", It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void Control_SessionIdentityChangesAfterAction_DoesNotNotifyNewSession()
        {
            var u1 = Guid.NewGuid().ToString("N");
            var u2 = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "", "room", "admin-1", new[] { u1, u2 }, u1);
            var sm = new Mock<ISessionManager>();
            var reads = 0;
            sm.Setup(s => s.Sessions).Returns(() =>
            {
                reads++;
                return new[] { NewSession(sm, "s1", u1), NewSession(sm, reads <= 3 ? "old" : "new", u2) };
            });
            sm.Setup(s => s.SendMessageCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            using var bridge = new SessionBridge(sm.Object);
            var plugin = NewPlugin(manager, bridge, new RecordingIssuer(), "server-1");
            var service = NewService(u1, true);
            WithPlugin(plugin, () => service.Post(new ControlRoomRequest { Id = room.Id, Action = "pause" }));
            sm.Verify(s => s.SendMessageCommand(It.IsAny<string>(), "old", It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>()), Times.Never);
            sm.Verify(s => s.SendMessageCommand(It.IsAny<string>(), "new", It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void Control_MemberLeavesDuringNotificationSnapshot_DoesNotNotifyOldOrNewSession()
        {
            var u1 = Guid.NewGuid().ToString("N");
            var u2 = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "", "room", u1, new[] { u1, u2 }, u1);
            var reads = 0;
            var memberLeft = false;
            var sm = new Mock<ISessionManager>();
            sm.Setup(s => s.Sessions).Returns(() =>
            {
                reads++;
                // Read 1 captures action snapshots; read 2 refreshes the
                // per-target action snapshot; read 3 is the notification
                // snapshot immediately before SendDisplayMessageAsync.
                if (reads >= 3 && !memberLeft)
                {
                    memberLeft = true;
                    Assert.True(manager.SetParticipantJoined(room.Id, u1, false));
                    Assert.True(manager.SetParticipantJoined(room.Id, u2, false));
                }

                return new[] { NewSession(sm, "s1", u1), NewSession(sm, "s2", u2) };
            });
            sm.Setup(s => s.SendMessageCommand(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<MessageCommand>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            using var bridge = new SessionBridge(sm.Object);
            var plugin = NewPlugin(manager, bridge, new RecordingIssuer(), "server-1");
            var service = NewService(u1, true);

            WithPlugin(plugin, () => service.Post(new ControlRoomRequest { Id = room.Id, Action = "pause" }));

            Assert.True(reads >= 3);
            sm.Verify(
                s => s.SendMessageCommand(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<MessageCommand>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public void Control_RoomDeletedDuringNotificationSnapshot_DoesNotNotifyOldOrNewSession()
        {
            var u1 = Guid.NewGuid().ToString("N");
            var u2 = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "", "room", u1, new[] { u1, u2 }, u1);
            var reads = 0;
            var roomDeleted = false;
            var sm = new Mock<ISessionManager>();
            sm.Setup(s => s.Sessions).Returns(() =>
            {
                reads++;
                // Read 1 captures action snapshots; read 2 refreshes the
                // per-target action snapshot; read 3 is the notification
                // snapshot immediately before SendDisplayMessageAsync.
                if (reads >= 3 && !roomDeleted)
                {
                    roomDeleted = true;
                    Assert.True(manager.DeleteRoom(room.Id));
                }

                return new[] { NewSession(sm, "s1", u1), NewSession(sm, "s2", u2) };
            });
            sm.Setup(s => s.SendMessageCommand(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<MessageCommand>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            using var bridge = new SessionBridge(sm.Object);
            var plugin = NewPlugin(manager, bridge, new RecordingIssuer(), "server-1");
            var service = NewService(u1, true);

            WithPlugin(plugin, () => service.Post(new ControlRoomRequest { Id = room.Id, Action = "resume" }));

            // The room is invalidated during the first notification snapshot;
            // any additional target reads must still skip both sends.
            Assert.True(reads >= 3);
            sm.Verify(
                s => s.SendMessageCommand(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<MessageCommand>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public void SyncNoticeDeliveryFailure_DoesNotChangeActionResponse()
        {
            var u1 = Guid.NewGuid().ToString("N");
            var u2 = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "", "room", "admin-1", new[] { u1, u2 }, u1);
            manager.SetParticipantJoined(room.Id, u2, false);
            var sm = new Mock<ISessionManager>();
            sm.Setup(s => s.Sessions).Returns(new[] { NewSession(sm, "s1", u1), NewSession(sm, "s2", u2) });
            sm.Setup(s => s.SendMessageCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>())).Throws(new InvalidOperationException("send failed"));
            using var bridge = new SessionBridge(sm.Object);
            var plugin = NewPlugin(manager, bridge, new RecordingIssuer(), "server-1");
            var service = NewService(u2);
            var response = WithPlugin(plugin, () => service.Post(new JoinRoomRequest { Id = room.Id }));
            Assert.Equal(room.Id, GetString(response, "RoomId"));
            Assert.Equal("Waiting", GetString(response, "State"));
            Assert.True(manager.GetRoom(room.Id).IsJoined(u2));
        }

        [Fact]
        public void Rooms_StatusReason_MapsRuntimeAndEligibilityReasons()
        {
            var user1 = Guid.NewGuid().ToString("N");
            var user2 = Guid.NewGuid().ToString("N");
            var user3 = Guid.NewGuid().ToString("N");
            var user4 = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom("other-server", "", "room", user1, new[] { user1, user2 }, user1);
            var plugin = NewPlugin(manager, null, new RecordingIssuer(), "server-1");
            var service = NewService(user1, true);
            manager.GetRuntime(room.Id).Error = "command failed";
            var result = WithPlugin(plugin, () => service.Get(new GetRoomsRequest()));
            Assert.Equal("server_unavailable", GetString(GetRoomResponse(result, room.Id), "StatusReason"));

            room = manager.CreateRoom("server-1", "", "room2", user3, new[] { user3, user4 }, user3);
            var runtime = manager.GetRuntime(room.Id);
            runtime.Error = "两位参与者打开了不同视频，暂不发送同步指令";
            result = WithPlugin(plugin, () => service.Get(new GetRoomsRequest()));
            Assert.Equal("different_video", GetString(GetRoomResponse(result, room.Id), "StatusReason"));
            runtime.Error = "播放已停止，等待双方重新打开同一视频";
            result = WithPlugin(plugin, () => service.Get(new GetRoomsRequest()));
            Assert.Equal("playback_stopped", GetString(GetRoomResponse(result, room.Id), "StatusReason"));
            manager.SetParticipantJoined(room.Id, user4, false);
            runtime.Error = "command failed";
            result = WithPlugin(plugin, () => service.Get(new GetRoomsRequest()));
            Assert.Equal("command_failed", GetString(GetRoomResponse(result, room.Id), "StatusReason"));
            runtime.Error = null;
            result = WithPlugin(plugin, () => service.Get(new GetRoomsRequest()));
            Assert.Equal("member_left", GetString(GetRoomResponse(result, room.Id), "StatusReason"));
            manager.SetParticipantJoined(room.Id, user4, true);
            runtime.State = RoomState.Barrier;
            result = WithPlugin(plugin, () => service.Get(new GetRoomsRequest()));
            Assert.Equal("aligning", GetString(GetRoomResponse(result, room.Id), "StatusReason"));
            runtime.State = RoomState.Watching;
            result = WithPlugin(plugin, () => service.Get(new GetRoomsRequest()));
            Assert.Equal("watching", GetString(GetRoomResponse(result, room.Id), "StatusReason"));
            runtime.State = RoomState.Waiting;
            result = WithPlugin(plugin, () => service.Get(new GetRoomsRequest()));
            Assert.Equal("waiting_for_playback", GetString(GetRoomResponse(result, room.Id), "StatusReason"));
        }

        [Fact]
        public void State_StatusReason_MapsEligibilityReasons()
        {
            var user1 = Guid.NewGuid().ToString("N");
            var user2 = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "", "room", user1, new[] { user1, user2 }, user1);
            var plugin = NewPlugin(manager, null, new RecordingIssuer(), "server-1");
            var runtime = manager.GetRuntime(room.Id);
            var service = NewService(user1, true);
            SetEligibilityFailure(runtime, "RemoteControlUnsupportedOrMismatch");
            Assert.Equal(
                "remote_control_unavailable",
                GetString(
                    WithPlugin(plugin, () => service.Get(new GetRoomStateRequest { Id = room.Id })),
                    "StatusReason"));
            SetEligibilityFailure(runtime, "InvalidOrDifferentRuntime");
            Assert.Equal(
                "media_mismatch",
                GetString(
                    WithPlugin(plugin, () => service.Get(new GetRoomStateRequest { Id = room.Id })),
                    "StatusReason"));
            SetEligibilityFailure(runtime, "PlaybackRateNotOne");
            Assert.Equal(
                "unsupported_playback_rate",
                GetString(
                    WithPlugin(plugin, () => service.Get(new GetRoomStateRequest { Id = room.Id })),
                    "StatusReason"));
            SetEligibilityFailure(runtime, null);
            Assert.Equal(
                "waiting_for_playback",
                GetString(
                    WithPlugin(plugin, () => service.Get(new GetRoomStateRequest { Id = room.Id })),
                    "StatusReason"));
        }

        [Fact]
        public void Leave_FromWaitingDoesNotPauseTheOtherParticipant()
        {
            var primaryUserId = Guid.NewGuid().ToString("N");
            var leavingUserId = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom(
                "server-1", "http://emby", "room", "admin-1",
                new[] { primaryUserId, leavingUserId }, primaryUserId);

            var issuer = new RecordingIssuer();
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(s => s.Sessions).Returns(new List<SessionInfo>
            {
                NewSession(sessionManager, "session-primary", primaryUserId),
            });
            using var bridge = new SessionBridge(sessionManager.Object);
            var plugin = NewPlugin(manager, bridge, issuer, "server-1");
            var service = NewService(leavingUserId);

            object response = WithPlugin(plugin, () =>
                service.Post(new LeaveRoomRequest { Id = room.Id }));

            Assert.True(GetBoolean(response, "Changed"));
            Assert.Empty(issuer.Issued);
        }

        [Fact]
        public void Leave_FromAnotherServerDoesNotPauseTheOtherParticipant()
        {
            var primaryUserId = Guid.NewGuid().ToString("N");
            var leavingUserId = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom(
                "server-1", "http://emby", "room", "admin-1",
                new[] { primaryUserId, leavingUserId }, primaryUserId);
            manager.GetRuntime(room.Id).State = RoomState.Watching;

            var issuer = new RecordingIssuer();
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(s => s.Sessions).Returns(new List<SessionInfo>
            {
                NewSession(sessionManager, "session-primary", primaryUserId),
            });
            using var bridge = new SessionBridge(sessionManager.Object);
            var plugin = NewPlugin(manager, bridge, issuer, "server-2");
            var service = NewService(leavingUserId);

            object response = WithPlugin(plugin, () =>
                service.Post(new LeaveRoomRequest { Id = room.Id }));

            Assert.True(GetBoolean(response, "Changed"));
            Assert.Empty(issuer.Issued);
        }

        [Fact]
        public void Leave_FromUnavailableRoomDoesNotPauseTheOtherParticipant()
        {
            var primaryUserId = Guid.NewGuid().ToString("N");
            var leavingUserId = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom(
                "server-1", "http://emby", "room", "admin-1",
                new[] { primaryUserId, leavingUserId }, primaryUserId);
            manager.GetRuntime(room.Id).State = RoomState.Unavailable;

            var issuer = new RecordingIssuer();
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(s => s.Sessions).Returns(new List<SessionInfo>
            {
                NewSession(sessionManager, "session-primary", primaryUserId),
            });
            using var bridge = new SessionBridge(sessionManager.Object);
            var plugin = NewPlugin(manager, bridge, issuer, "server-1");
            var service = NewService(leavingUserId);

            object response = WithPlugin(plugin, () =>
                service.Post(new LeaveRoomRequest { Id = room.Id }));

            Assert.True(GetBoolean(response, "Changed"));
            Assert.Empty(issuer.Issued);
        }

        [Fact]
        public void Control_InGateSnapshotRevalidation_DoesNotIssueToFormerMember()
        {
            var primaryUserId = Guid.NewGuid().ToString("N");
            var leavingUserId = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom(
                "server-1", "http://emby", "room", "admin-1",
                new[] { primaryUserId, leavingUserId }, primaryUserId);

            int sessionReads = 0;
            var issuer = new RecordingIssuer();
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(s => s.Sessions).Returns(() =>
            {
                Interlocked.Increment(ref sessionReads);
                manager.LeaveParticipant(room.Id, leavingUserId);
                return new List<SessionInfo>
                {
                    NewSession(sessionManager, "session-primary", primaryUserId),
                    NewSession(sessionManager, "session-leaving", leavingUserId),
                };
            });
            using var bridge = new SessionBridge(sessionManager.Object);
            var plugin = NewPlugin(manager, bridge, issuer, "server-1");
            var service = NewService(primaryUserId, administrator: true);

            object response = WithPlugin(plugin, () =>
                service.Post(new ControlRoomRequest { Id = room.Id, Action = "pause" }));

            Assert.Single(issuer.Issued);
            Assert.Equal(primaryUserId, issuer.Issued[0].userId);
            Assert.DoesNotContain(
                leavingUserId,
                (IEnumerable<string>)response.GetType().GetProperty("Users").GetValue(response));
            Assert.Equal(2, sessionReads);
        }

        [Fact]
        public void Control_ServerMismatchDoesNotIssueCommands()
        {
            var primaryUserId = Guid.NewGuid().ToString("N");
            var otherUserId = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom(
                "server-1", "http://emby", "room", "admin-1",
                new[] { primaryUserId, otherUserId }, primaryUserId);
            manager.GetRuntime(room.Id).State = RoomState.Watching;

            var issuer = new RecordingIssuer();
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(s => s.Sessions).Returns(() => new List<SessionInfo>
            {
                NewSession(sessionManager, "session-primary", primaryUserId),
                NewSession(sessionManager, "session-other", otherUserId),
            });
            using var bridge = new SessionBridge(sessionManager.Object);
            var plugin = NewPlugin(manager, bridge, issuer, "server-2");
            var service = NewService(primaryUserId, administrator: true);

            object response = WithPlugin(plugin, () =>
                service.Post(new ControlRoomRequest { Id = room.Id, Action = "pause" }));

            Assert.Empty(issuer.Issued);
            Assert.Equal("room server is unavailable", GetString(response, "Error"));
        }

        [Fact]
        public void Message_ServerMismatchDoesNotSendMessages()
        {
            var primaryUserId = Guid.NewGuid().ToString("N");
            var otherUserId = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom(
                "server-1", "http://emby", "room", "admin-1",
                new[] { primaryUserId, otherUserId }, primaryUserId);

            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(s => s.Sessions).Returns(() => new List<SessionInfo>
            {
                NewSession(sessionManager, "session-primary", primaryUserId),
                NewSession(sessionManager, "session-other", otherUserId),
            });
            sessionManager.Setup(s => s.SendMessageCommand(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<MessageCommand>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            using var bridge = new SessionBridge(sessionManager.Object);
            var plugin = NewPlugin(manager, bridge, new RecordingIssuer(), "server-2");
            var service = NewService(primaryUserId, administrator: true);

            object response = WithPlugin(plugin, () =>
                service.Post(new SendRoomMessageRequest { Id = room.Id, Text = "hello" }));

            Assert.Equal(0, GetInt(response, "Sent"));
            sessionManager.Verify(s => s.SendMessageCommand(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<MessageCommand>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void Message_SameServerSendsToCurrentJoinedSessions()
        {
            var primaryUserId = Guid.NewGuid().ToString("N");
            var otherUserId = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom(
                "server-1", "http://emby", "room", "admin-1",
                new[] { primaryUserId, otherUserId }, primaryUserId);

            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(s => s.Sessions).Returns(() => new List<SessionInfo>
            {
                NewSession(sessionManager, "session-primary", primaryUserId),
                NewSession(sessionManager, "session-other", otherUserId),
            });
            sessionManager.Setup(s => s.SendMessageCommand(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<MessageCommand>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            using var bridge = new SessionBridge(sessionManager.Object);
            var plugin = NewPlugin(manager, bridge, new RecordingIssuer(), "server-1");
            var service = NewService(primaryUserId, administrator: true);

            object response = WithPlugin(plugin, () =>
                service.Post(new SendRoomMessageRequest { Id = room.Id, Text = "hello" }));

            Assert.Equal(2, GetInt(response, "Sent"));
            sessionManager.Verify(s => s.SendMessageCommand(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.Is<MessageCommand>(m => m.Header == "Watch Together" && m.Text == "hello"),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task Message_ParticipantLeavesWhileWaitingForGate_IsNotTargeted()
        {
            var primaryUserId = Guid.NewGuid().ToString("N");
            var leavingUserId = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom(
                "server-1", "http://emby", "room", "admin-1",
                new[] { primaryUserId, leavingUserId }, primaryUserId);

            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(s => s.Sessions).Returns(new List<SessionInfo>
            {
                NewSession(sessionManager, "session-primary", primaryUserId),
                NewSession(sessionManager, "session-leaving", leavingUserId),
            });
            sessionManager.Setup(s => s.SendMessageCommand(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<MessageCommand>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            using var bridge = new SessionBridge(sessionManager.Object);
            var plugin = NewPlugin(manager, bridge, new RecordingIssuer(), "server-1");
            var service = NewService(primaryUserId, administrator: true);

            IDisposable gate = EnterRoomGate(manager, room.Id);
            using var started = new ManualResetEventSlim(false);
            Task<object> messageTask = Task.Run(() =>
            {
                started.Set();
                return WithPlugin(plugin, () =>
                    service.Post(new SendRoomMessageRequest { Id = room.Id, Text = "hello" }));
            });

            try
            {
                Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
                Assert.True(manager.LeaveParticipant(room.Id, leavingUserId));
            }
            finally
            {
                gate.Dispose();
            }

            object response = await messageTask;

            Assert.Equal(1, GetInt(response, "Sent"));
            sessionManager.Verify(s => s.SendMessageCommand(
                It.IsAny<string>(),
                "session-primary",
                It.IsAny<MessageCommand>(),
                It.IsAny<CancellationToken>()), Times.Once);
            sessionManager.Verify(s => s.SendMessageCommand(
                It.IsAny<string>(),
                "session-leaving",
                It.IsAny<MessageCommand>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void Message_DeleteDuringInGateSnapshotRevalidationDoesNotSend()
        {
            var primaryUserId = Guid.NewGuid().ToString("N");
            var otherUserId = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom(
                "server-1", "http://emby", "room", "admin-1",
                new[] { primaryUserId, otherUserId }, primaryUserId);

            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(s => s.Sessions).Returns(() =>
            {
                manager.DeleteRoom(room.Id);
                return new List<SessionInfo>
                {
                    NewSession(sessionManager, "session-primary", primaryUserId),
                    NewSession(sessionManager, "session-other", otherUserId),
                };
            });
            sessionManager.Setup(s => s.SendMessageCommand(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<MessageCommand>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            using var bridge = new SessionBridge(sessionManager.Object);
            var plugin = NewPlugin(manager, bridge, new RecordingIssuer(), "server-1");
            var service = NewService(primaryUserId, administrator: true);

            object response = WithPlugin(plugin, () =>
                service.Post(new SendRoomMessageRequest { Id = room.Id, Text = "hello" }));

            Assert.Equal(0, GetInt(response, "Sent"));
            sessionManager.Verify(s => s.SendMessageCommand(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<MessageCommand>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void Leave_TargetSessionChangesAfterLeave_DoesNotPauseNewSession()
        {
            var primaryUserId = Guid.NewGuid().ToString("N");
            var leavingUserId = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom(
                "server-1", "http://emby", "room", "admin-1",
                new[] { primaryUserId, leavingUserId }, primaryUserId);
            manager.GetRuntime(room.Id).State = RoomState.Watching;

            int sessionReads = 0;
            var issuer = new RecordingIssuer();
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(s => s.Sessions).Returns(() =>
            {
                if (Interlocked.Increment(ref sessionReads) == 1)
                {
                    return new List<SessionInfo>
                    {
                        NewSession(sessionManager, "session-primary-old", primaryUserId, "item-1"),
                        NewSession(sessionManager, "session-leaving", leavingUserId, "item-1"),
                    };
                }

                return new List<SessionInfo>
                {
                    NewSession(sessionManager, "session-primary-new", primaryUserId, "item-2"),
                };
            });
            using var bridge = new SessionBridge(sessionManager.Object);
            var plugin = NewPlugin(manager, bridge, issuer, "server-1");
            var service = NewService(leavingUserId);

            object response = WithPlugin(plugin, () =>
                service.Post(new LeaveRoomRequest { Id = room.Id }));

            Assert.True(GetBoolean(response, "Changed"));
            Assert.Empty(issuer.Issued);
        }

        [Fact]
        public void Leave_RejoinDuringSecondSnapshotRead_CancelsOldPause()
        {
            var primaryUserId = Guid.NewGuid().ToString("N");
            var leavingUserId = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom(
                "server-1", "http://emby", "room", "admin-1",
                new[] { primaryUserId, leavingUserId }, primaryUserId);
            manager.GetRuntime(room.Id).State = RoomState.Watching;

            int sessionReads = 0;
            var issuer = new RecordingIssuer();
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(s => s.Sessions).Returns(() =>
            {
                if (Interlocked.Increment(ref sessionReads) == 1)
                {
                    return new List<SessionInfo>
                    {
                        NewSession(sessionManager, "session-primary", primaryUserId),
                        NewSession(sessionManager, "session-leaving", leavingUserId),
                    };
                }

                Assert.True(manager.SetParticipantJoined(room.Id, leavingUserId, true));
                return new List<SessionInfo>
                {
                    NewSession(sessionManager, "session-primary", primaryUserId),
                    NewSession(sessionManager, "session-leaving", leavingUserId),
                };
            });
            using var bridge = new SessionBridge(sessionManager.Object);
            var plugin = NewPlugin(manager, bridge, issuer, "server-1");
            var service = NewService(leavingUserId);

            object response = WithPlugin(plugin, () =>
                service.Post(new LeaveRoomRequest { Id = room.Id }));

            Assert.True(GetBoolean(response, "Changed"));
            Assert.Empty(issuer.Issued);
            Assert.Equal(2, sessionReads);
        }

        [Fact]
        public void Leave_ServerIdMismatch_DoesNotIssuePauseAfterPrimaryLeaves()
        {
            var primaryUserId = Guid.NewGuid().ToString("N");
            var otherUserId = Guid.NewGuid().ToString("N");
            var manager = new RoomManager();
            var room = manager.CreateRoom(
                "server-1", "http://emby", "room", "admin-1",
                new[] { primaryUserId, otherUserId }, primaryUserId);
            manager.GetRuntime(room.Id).State = RoomState.Watching;

            var issuer = new RecordingIssuer();
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(s => s.Sessions).Returns(new List<SessionInfo>
            {
                NewSession(sessionManager, "session-primary", primaryUserId),
                NewSession(sessionManager, "session-other", otherUserId),
            });
            using var bridge = new SessionBridge(sessionManager.Object);
            var plugin = NewPlugin(manager, bridge, issuer, "server-2");
            var service = NewService(primaryUserId);

            object response = WithPlugin(plugin, () =>
                service.Post(new LeaveRoomRequest { Id = room.Id }));

            Assert.True(GetBoolean(response, "Changed"));
            Assert.Empty(issuer.Issued);
        }

        private static IDisposable EnterRoomGate(RoomManager manager, string roomId)
        {
            var method = typeof(RoomManager).GetMethod(
                "TryEnterRoom",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var access = method.Invoke(manager, new object[] { roomId });
            Assert.NotNull(access);
            return (IDisposable)access;
        }

        private static SessionInfo NewSession(
            Mock<ISessionManager> sessionManager,
            string sessionId,
            string userId,
            string itemId = "item-1")
        {
            var session = new SessionInfo
            {
                Id = sessionId,
                UserId = userId,
                Capabilities = new ClientCapabilities
                {
                    SupportsMediaControl = true,
                    SupportedCommands = new[] { "Pause", "Unpause", "Seek", "DisplayMessage" },
                },
            };
            string playSessionId = "play-" + sessionId;
            var playSession = new PlaySessionInfo(sessionManager.Object, session, playSessionId, null)
            {
                NowPlayingItem = new BaseItemDto { Id = itemId, RunTimeTicks = 100 * SessionSnapshot.TicksPerSecond },
                PlayState = new PlayerStateInfo
                {
                    PositionTicks = 10 * SessionSnapshot.TicksPerSecond,
                    PlaybackRate = 1.0,
                    MediaSourceId = "source-1",
                    IsPaused = false,
                },
            };

            var playSessions = new ConcurrentDictionary<string, PlaySessionInfo>(StringComparer.OrdinalIgnoreCase)
            {
                [playSessionId] = playSession,
            };
            typeof(SessionInfo).GetField("_playSessions", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(session, playSessions);
            typeof(SessionInfo).GetField("_lastPlaySessionId", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(session, playSessionId);
            return session;
        }

        private static SessionInfo NewSession(
            Mock<ISessionManager> sessionManager,
            string sessionId,
            string userId,
            string[] supportedCommands)
        {
            var session = NewSession(sessionManager, sessionId, userId);
            session.Capabilities.SupportedCommands = supportedCommands;
            return session;
        }

        private static Plugin NewPlugin(
            RoomManager manager,
            SessionBridge bridge,
            RecordingIssuer issuer,
            string serverId)
        {
#pragma warning disable SYSLIB0050
            var plugin = (Plugin)FormatterServices.GetUninitializedObject(typeof(Plugin));
#pragma warning restore SYSLIB0050
            SetPluginProperty(plugin, "Rooms", manager);
            SetPluginProperty(plugin, "Bridge", bridge);
            SetPluginProperty(plugin, "Issuer", issuer);
            SetPluginProperty(plugin, "ServerId", serverId);
            return plugin;
        }

        private static WatchTogetherService NewService(string userId, bool administrator = false)
        {
#pragma warning disable SYSLIB0050 // Test-only construction avoids plugin/user service initialization.
            var user = (User)FormatterServices.GetUninitializedObject(typeof(User));
#pragma warning restore SYSLIB0050
            user.Id = Guid.Parse(userId);
            user.Policy = new UserPolicy { IsAdministrator = administrator };
            var auth = new Mock<IAuthorizationContext>();
            auth.Setup(c => c.GetAuthorizationInfo(It.IsAny<IRequest>()))
                .Returns(new AuthorizationInfo { User = user });

            return new WatchTogetherService
            {
                AuthorizationContext = auth.Object,
            };
        }

        private static object WithPlugin(Plugin plugin, Func<object> action)
        {
            var property = typeof(Plugin).GetProperty(
                "Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var previous = (Plugin)property.GetValue(null);
            property.SetValue(null, plugin);
            try
            {
                return action();
            }
            finally
            {
                property.SetValue(null, previous);
            }
        }

        private static void SetPluginProperty(Plugin plugin, string name, object value)
        {
            typeof(Plugin).GetProperty(
                name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SetValue(plugin, value);
        }

        private static void SetPluginConfiguration(Plugin plugin, PluginConfiguration configuration)
        {
            for (var type = typeof(Plugin); type != null; type = type.BaseType)
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (field.FieldType == typeof(PluginConfiguration))
                    {
                        field.SetValue(plugin, configuration);
                        return;
                    }
                }
            }
            throw new MissingFieldException("PluginConfiguration backing field not found");
        }

        private static bool GetBoolean(object response, string propertyName)
        {
            return (bool)response.GetType().GetProperty(propertyName).GetValue(response);
        }

        private static int GetInt(object response, string propertyName)
        {
            return (int)response.GetType().GetProperty(propertyName).GetValue(response);
        }

        private static string GetString(object response, string propertyName)
        {
            return (string)response.GetType().GetProperty(propertyName).GetValue(response);
        }

        private static object GetRoomResponse(object response, string roomId)
        {
            return ((System.Collections.IEnumerable)response).Cast<object>()
                .Single(item => string.Equals(GetString(item, "RoomId"), roomId, StringComparison.Ordinal));
        }

        private static void SetEligibilityFailure(RoomRuntime runtime, string reasonName)
        {
            var property = typeof(RoomRuntime).GetProperty(
                "LastEligibilityFailureReason",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(property);

            object value = reasonName == null
                ? null
                : Enum.Parse(property.PropertyType.GetGenericArguments()[0], reasonName);
            property.SetValue(runtime, value);
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
