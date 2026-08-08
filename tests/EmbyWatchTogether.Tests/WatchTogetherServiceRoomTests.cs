using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
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

        private static SessionInfo NewSession(
            Mock<ISessionManager> sessionManager,
            string sessionId,
            string userId)
        {
            var session = new SessionInfo
            {
                Id = sessionId,
                UserId = userId,
                Capabilities = new ClientCapabilities
                {
                    SupportsMediaControl = true,
                    SupportedCommands = new[] { "Pause", "Unpause", "Seek" },
                },
            };
            string playSessionId = "play-" + sessionId;
            var playSession = new PlaySessionInfo(sessionManager.Object, session, playSessionId, null)
            {
                NowPlayingItem = new BaseItemDto { Id = "item-1", RunTimeTicks = 100 * SessionSnapshot.TicksPerSecond },
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

        private static WatchTogetherService NewService(string userId)
        {
#pragma warning disable SYSLIB0050 // Test-only construction avoids plugin/user service initialization.
            var user = (User)FormatterServices.GetUninitializedObject(typeof(User));
#pragma warning restore SYSLIB0050
            user.Id = Guid.Parse(userId);
            user.Policy = new UserPolicy { IsAdministrator = false };
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

        private static bool GetBoolean(object response, string propertyName)
        {
            return (bool)response.GetType().GetProperty(propertyName).GetValue(response);
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
