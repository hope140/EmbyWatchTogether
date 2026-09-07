using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Runtime.Serialization;
using Emby.Plugins.WatchTogether;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.Users;
using Moq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public sealed class SyncDiagnosticsTests
    {
        [Fact]
        public void EventRingIsBoundedAndEvictsOldestEntry()
        {
            var runtime = new RoomRuntime();
            for (int i = 0; i < SyncDiagnostics.MaxEvents + 5; i++)
            {
                Record(runtime, "transition-" + i, "u1", null, "observed", i, null,
                    DateTimeOffset.UtcNow.AddSeconds(i));
            }

            var state = Capture(runtime);
            var events = (IList)state.GetType().GetProperty("Events").GetValue(state);
            Assert.Equal(SyncDiagnostics.MaxEvents, events.Count);
            Assert.Equal("transition-5", events[0].GetType().GetProperty("Type").GetValue(events[0]));
        }

        [Fact]
        public void ExportUsesAliasesHashesAndStableErrors()
        {
            string userA = "11111111111111111111111111111111";
            string userB = "22222222222222222222222222222222";
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-private", "", "private", userA,
                new[] { userA, userB }, userA);
            var runtime = manager.GetRuntime(room.Id);
            runtime.Error = "Pause command failed: secret exception, C:\\private\\token";
            var snapshot = new SessionSnapshot(
                "session-secret", userA, "item-secret", "source", 12_000_000, 120_000_000,
                false, 1, false, true,
                new SessionCapabilityReport(true, new[] { RemoteCommands.Pause, RemoteCommands.Seek }),
                DateTimeOffset.UtcNow.AddSeconds(-2));
            RecordSnapshots(runtime, new Dictionary<string, SessionSnapshot> { [userA] = snapshot }, DateTimeOffset.UtcNow);

            var exported = SyncDiagnostics.Build(room, runtime, "server-private", "1.4.3.2",
                "command_failed", DateTimeOffset.UtcNow);
            Assert.Equal("userA", exported.Participants[0].Alias);
            Assert.Equal("userB", exported.Participants[1].Alias);
            Assert.DoesNotContain(room.Id, exported.RoomHash);
            Assert.DoesNotContain("session-secret", exported.Sessions[0].SessionHash);
            Assert.DoesNotContain("item-secret", exported.Sessions[0].ItemHash);
            Assert.Equal("command_failed", exported.LastError);
            Assert.DoesNotContain("secret", exported.LastError, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private", exported.LastError, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EventDeduplicationDoesNotRecordIdenticalConsecutiveEvent()
        {
            var runtime = new RoomRuntime();
            var at = DateTimeOffset.UtcNow;
            Record(runtime, "barrier_started", "u1", RemoteCommands.Pause, "entered", 1, null, at);
            Record(runtime, "barrier_started", "u1", RemoteCommands.Pause, "entered", 1, null, at.AddSeconds(1));
            var state = Capture(runtime);
            var events = (IList)state.GetType().GetProperty("Events").GetValue(state);
            Assert.Single(events);
        }

        [Fact]
        public void ExportIncludesMissingParticipantAndActualRecoveryAliases()
        {
            string userA = "11111111111111111111111111111111";
            string userB = "22222222222222222222222222222222";
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "", "room", userA,
                new[] { userA, userB }, userA);
            var runtime = manager.GetRuntime(room.Id);
            runtime.AckLatencySeconds[userA] = 1.25;
            var now = DateTimeOffset.UtcNow;

            typeof(RoomRuntime).GetMethod("StartRemoteControlRecovery", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(runtime, new object[] { now.AddSeconds(-2), "signature", new[] { userA } });
            var exported = SyncDiagnostics.Build(room, runtime, "server-1", "1.4.3.2", "waiting", now);

            Assert.Equal(2, exported.Sessions.Count);
            Assert.Contains(exported.Sessions, session => session.Alias == "userB" && !session.Online);
            Assert.Equal(1.25, exported.Sessions.Single(session => session.Alias == "userA").AckLatencySeconds);
            Assert.Equal(new[] { "userA" }, exported.RecoveryWindow.AffectedAliases);

            typeof(RoomRuntime).GetMethod("ClearRemoteControlRecovery", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(runtime, null);
            exported = SyncDiagnostics.Build(room, runtime, "server-1", "1.4.3.2", "waiting", now);
            Assert.Empty(exported.RecoveryWindow.AffectedAliases);
        }

        [Fact]
        public void ExportNormalizesUnknownEventTypeAndStaleSnapshots()
        {
            string userA = "11111111111111111111111111111111";
            string userB = "22222222222222222222222222222222";
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "", "room", userA,
                new[] { userA, userB }, userA);
            var runtime = manager.GetRuntime(room.Id);
            var old = DateTimeOffset.UtcNow.AddSeconds(-30);
            Record(runtime, "private_detail_should_not_escape", userA, null, "observed", null, null, old);
            RecordSnapshots(runtime, new Dictionary<string, SessionSnapshot>(), old);

            var exported = SyncDiagnostics.Build(room, runtime, "server-1", "1.4.3.2", "waiting", DateTimeOffset.UtcNow);
            Assert.Equal("observed", exported.Events.Single().Type);
            Assert.Equal("stale", exported.SnapshotHealth);
        }

        [Fact]
        public void DiagnosticsEndpointAllowsMemberAndRejectsOutsiderWithoutIssuer()
        {
            string userA = "11111111111111111111111111111111";
            string userB = "22222222222222222222222222222222";
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "", "room", userA,
                new[] { userA, userB }, userA);
            var sessions = new Mock<ISessionManager>();
            sessions.Setup(m => m.Sessions).Returns(new List<SessionInfo>());
            using (var bridge = new SessionBridge(sessions.Object))
            {
                var issuer = new RecordingIssuer();
                var plugin = NewPlugin(manager, bridge, "server-1", issuer);
                var member = NewService(userA);
                var response = WithPlugin(plugin, () => member.Get(new GetRoomDiagnosticsRequest { Id = room.Id }));
                var diagnostics = Assert.IsType<RoomDiagnostics>(response);
                Assert.Equal("1", diagnostics.SchemaVersion);
                Assert.Equal("userA", diagnostics.Participants[0].Alias);
                Assert.Empty(issuer.Issued);

                var outsider = NewService("33333333333333333333333333333333");
                Assert.Throws<UnauthorizedAccessException>(() =>
                    WithPlugin(plugin, () => outsider.Get(new GetRoomDiagnosticsRequest { Id = room.Id })));
            }
        }

        [Fact]
        public void DiagnosticsEndpointRejectsServerMismatch()
        {
            string userA = "11111111111111111111111111111111";
            string userB = "22222222222222222222222222222222";
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "", "room", userA,
                new[] { userA, userB }, userA);
            var sessions = new Mock<ISessionManager>();
            sessions.Setup(m => m.Sessions).Returns(new List<SessionInfo>());
            using (var bridge = new SessionBridge(sessions.Object))
            {
                var plugin = NewPlugin(manager, bridge, "server-2");
                var member = NewService(userA);
                Assert.Throws<ServiceUnavailableException>(() =>
                    WithPlugin(plugin, () => member.Get(new GetRoomDiagnosticsRequest { Id = room.Id })));
            }
        }

        private static void Record(RoomRuntime runtime, string type, string userId, string command,
            string result, long? position, double? latency, DateTimeOffset at)
        {
            typeof(RoomRuntime).GetMethod("RecordDiagnosticEvent", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(runtime, new object[] { type, userId, command, result, position, latency, at });
        }

        private static void RecordSnapshots(RoomRuntime runtime, IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            DateTimeOffset at)
        {
            typeof(RoomRuntime).GetMethod("RecordDiagnosticSnapshots", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(runtime, new object[] { snapshots, at });
        }

        private static object Capture(RoomRuntime runtime)
        {
            return typeof(RoomRuntime).GetMethod("CaptureDiagnostics", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(runtime, null);
        }

        private static Plugin NewPlugin(RoomManager manager, SessionBridge bridge, string serverId,
            ICommandIssuer issuer = null)
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
#pragma warning disable SYSLIB0050
            var user = (User)FormatterServices.GetUninitializedObject(typeof(User));
#pragma warning restore SYSLIB0050
            user.Id = Guid.Parse(userId);
            user.Policy = new UserPolicy { IsAdministrator = false };
            var auth = new Mock<IAuthorizationContext>();
            auth.Setup(c => c.GetAuthorizationInfo(It.IsAny<IRequest>()))
                .Returns(new AuthorizationInfo { User = user });
            return new WatchTogetherService { AuthorizationContext = auth.Object };
        }

        private static void SetPluginProperty(Plugin plugin, string name, object value)
        {
            typeof(Plugin).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SetValue(plugin, value);
        }

        private static object WithPlugin(Plugin plugin, Func<object> action)
        {
            var property = typeof(Plugin).GetProperty(
                "Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var previous = (Plugin)property.GetValue(null);
            property.SetValue(null, plugin);
            try { return action(); }
            finally { property.SetValue(null, previous); }
        }

        private sealed class RecordingIssuer : ICommandIssuer
        {
            public List<string> Issued { get; } = new List<string>();

            public bool TryIssue(string roomId, string controllingUserId, string userId,
                SessionSnapshot snapshot, string command, long? positionTicks,
                DateTimeOffset now, out string error)
            {
                Issued.Add(command);
                error = null;
                return true;
            }
        }
    }
}
