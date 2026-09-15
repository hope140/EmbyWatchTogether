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
            Assert.Equal(500, SyncDiagnostics.MaxEvents);
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
        public void ExportIncludesHandoffStateWithHashesAliasesAndStableError()
        {
            string userA = "11111111111111111111111111111111";
            string userB = "22222222222222222222222222222222";
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "", "room", userA,
                new[] { userA, userB }, userA);
            var runtime = manager.GetRuntime(room.Id);
            var handoff = (MediaHandoffState)typeof(RoomRuntime).GetMethod(
                "BeginMediaHandoff", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(runtime, new object[] {
                    "target-item", "source-item", DateTimeOffset.UtcNow.AddSeconds(-2),
                    userA, "primary-session", userB, "participant-session" });
            handoff.PlayItemPending = true;
            handoff.RetryCount = 1;
            handoff.Generation = 7;
            handoff.LastError = "play item acknowledgement timed out";
            handoff.IsParticipantResync = true;
            runtime.State = RoomState.Handoff;

            var now = DateTimeOffset.UtcNow;
            var exported = SyncDiagnostics.Build(room, runtime, "server-1", "1.5.0.3", "media_handoff", now);

            Assert.NotNull(exported.Handoff);
            Assert.Equal(SyncDiagnostics.Hash("target-item"), exported.Handoff.TargetItemHash);
            Assert.Equal(SyncDiagnostics.Hash("source-item"), exported.Handoff.SourceItemHash);
            Assert.Equal("userA", exported.Handoff.PrimaryAlias);
            Assert.Equal("userB", exported.Handoff.ParticipantAlias);
            Assert.True(exported.Handoff.PlayItemPending);
            Assert.Equal(1, exported.Handoff.RetryCount);
            Assert.Equal(7, exported.Handoff.Generation);
            Assert.True(exported.Handoff.IsParticipantResync);
            Assert.Equal("play_item_timeout", exported.Handoff.LastError);
            Assert.DoesNotContain("target-item", exported.Handoff.TargetItemHash);
        }

        [Fact]
        public void ExportAllowsPlayItemAndHandoffEventResults()
        {
            string userA = "11111111111111111111111111111111";
            string userB = "22222222222222222222222222222222";
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "", "room", userA,
                new[] { userA, userB }, userA);
            var runtime = manager.GetRuntime(room.Id);
            var now = DateTimeOffset.UtcNow;
            Record(runtime, "handoff_started", null, RemoteCommands.PlayItem, "started", null, null, now);
            Record(runtime, "handoff_target_confirmed", null, RemoteCommands.PlayItem, "confirmed", null, null, now.AddSeconds(1));
            Record(runtime, "handoff_superseded", null, RemoteCommands.PlayItem, "superseded", null, null, now.AddSeconds(2));

            var exported = SyncDiagnostics.Build(room, runtime, "server-1", "1.5.0.3", "waiting_for_playback", now.AddSeconds(2));

            Assert.Contains(exported.Events, e => e.Type == "handoff_started" && e.Command == RemoteCommands.PlayItem && e.Result == "entered");
            Assert.Contains(exported.Events, e => e.Type == "handoff_target_confirmed" && e.Result == "success");
            Assert.Contains(exported.Events, e => e.Type == "handoff_superseded" && e.Result == "changed");
        }

        [Fact]
        public void ExportIncludesTransientRecoveryAndDriftTelemetryWithoutRawIdentity()
        {
            string userA = "11111111111111111111111111111111";
            string userB = "22222222222222222222222222222222";
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "", "room", userA,
                new[] { userA, userB }, userA);
            var runtime = manager.GetRuntime(room.Id);
            var started = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var previous = new Dictionary<string, SessionSnapshot>
            {
                [userA] = DiagnosticSnapshot("session-a", userA, "item-a", started),
                [userB] = DiagnosticSnapshot("session-b", userB, "item-a", started),
            };
            typeof(RoomRuntime).GetMethod(
                "BeginTransientSessionRecovery", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(runtime, new object[] { started, new[] { userB }, previous });
            runtime.State = RoomState.Recovering;
            SetRuntimeProperty(runtime, "CurrentDriftSeconds", 4.0);
            SetRuntimeProperty(runtime, "MaxObservedAbsoluteDriftSeconds", 4.0);
            SetRuntimeProperty(runtime, "DriftAboveRepairThresholdSinceUtc", started.AddSeconds(-2));
            SetRuntimeProperty(runtime, "LastAutoRepairAtUtc", started.AddSeconds(-10));
            SetRuntimeProperty(runtime, "AutoRepairCount", 1);
            SetRuntimeProperty(runtime, "LastAutoRepairDriftSeconds", 4.0);

            var exported = SyncDiagnostics.Build(
                room,
                runtime,
                "server-1",
                "1.6.0.1",
                "transient_recovery",
                started.AddSeconds(2));

            Assert.Equal("Recovering", exported.RoomState);
            Assert.True(exported.TransientRecovery.Active);
            Assert.Contains("userB", exported.TransientRecovery.MissingAliases);
            var recoveringParticipant = Assert.Single(exported.TransientRecovery.Participants,
                item => item.Alias == "userB");
            Assert.Equal(SyncDiagnostics.Hash("session-b"), recoveringParticipant.ExpectedSessionHash);
            Assert.Equal(SyncDiagnostics.Hash("item-a"), recoveringParticipant.ExpectedItemHash);
            Assert.DoesNotContain("session-b", recoveringParticipant.ExpectedSessionHash);
            Assert.True(exported.Drift.CurrentDriftSeconds.HasValue);
            Assert.Equal(4.0, exported.Drift.CurrentDriftSeconds.Value, precision: 3);
            Assert.Equal(1, exported.Drift.AutoRepairCount);
            Assert.Equal(4.0, exported.Drift.LastAutoRepairDriftSeconds.Value, precision: 3);
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

        private static SessionSnapshot DiagnosticSnapshot(
            string sessionId,
            string userId,
            string itemId,
            DateTimeOffset at)
        {
            return new SessionSnapshot(
                sessionId,
                userId,
                itemId,
                "media",
                50 * SessionSnapshot.TicksPerSecond,
                100 * SessionSnapshot.TicksPerSecond,
                false,
                1.0,
                stopped: false,
                supportsRemoteControl: true,
                new SessionCapabilityReport(true, new[] { RemoteCommands.Pause, RemoteCommands.Unpause, RemoteCommands.Seek }),
                at);
        }

        private static void SetRuntimeProperty(RoomRuntime runtime, string name, object value)
        {
            typeof(RoomRuntime).GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SetValue(runtime, value);
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
