using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Moq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class SessionBridgeCommandIssuerTests
    {
        [Fact]
        public void IsCommandSupported_RemoteControllableWithoutDeclaredPlaybackCommands_AllowsPause()
        {
            // Emby Theater style: SupportsRemoteControl without Pause/Unpause/Seek
            // in the declared command list (only OSD/navigation commands).
            var report = new SessionCapabilityReport(
                true,
                new[] { "MoveUp", "MoveDown", "Select", "Back", "DisplayMessage" });

            Assert.True(SessionBridgeCommandIssuer.IsCommandSupported(report, RemoteCommands.Pause));
            Assert.True(SessionBridgeCommandIssuer.IsCommandSupported(report, RemoteCommands.Unpause));
            Assert.True(SessionBridgeCommandIssuer.IsCommandSupported(report, RemoteCommands.Seek));
        }

        [Fact]
        public void IsCommandSupported_NonEmptyCommandListImpliesRemoteControl()
        {
            var report = new SessionCapabilityReport(false, new[] { "MoveUp", "Select" });

            Assert.True(SessionBridgeCommandIssuer.IsCommandSupported(report, RemoteCommands.Pause));
        }

        [Fact]
        public void IsCommandSupported_NotRemoteControllable_Rejects()
        {
            var report = new SessionCapabilityReport(false, new string[0]);

            Assert.False(SessionBridgeCommandIssuer.IsCommandSupported(report, RemoteCommands.Pause));
            Assert.False(SessionBridgeCommandIssuer.IsCommandSupported(report, RemoteCommands.Seek));
        }

        [Fact]
        public void IsCommandSupported_NullReport_Rejects()
        {
            Assert.False(SessionBridgeCommandIssuer.IsCommandSupported(null, RemoteCommands.Pause));
        }

        [Fact]
        public void IsCommandSupported_UnknownCommand_Rejects()
        {
            var report = new SessionCapabilityReport(true, new string[0]);

            Assert.False(SessionBridgeCommandIssuer.IsCommandSupported(report, "SomethingElse"));
        }

        [Fact]
        public void TryIssue_TransportFailure_ReturnsStableErrorWithoutExceptionDetails()
        {
            var manager = NewManager();
            manager.Setup(m => m.SendPlaystateCommand(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlaystateRequest>(), It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("private transport detail"));
            using (var bridge = new SessionBridge(manager.Object))
            {
                var issuer = new SessionBridgeCommandIssuer(bridge);
                string error;
                var result = issuer.TryIssue(
                    "room", "admin", "user", NewSnapshot(), RemoteCommands.Pause, null,
                    DateTimeOffset.UtcNow, out error);

                Assert.False(result);
                Assert.Equal("command_failed", error);
                Assert.DoesNotContain("private transport detail", error);
            }
        }

        [Fact]
        public void TryIssue_CancelledTransport_ReturnsTimeoutCode()
        {
            var manager = NewManager();
            manager.Setup(m => m.SendPlaystateCommand(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlaystateRequest>(), It.IsAny<CancellationToken>()))
                .Throws(new OperationCanceledException("private timeout detail"));
            using (var bridge = new SessionBridge(manager.Object))
            {
                var issuer = new SessionBridgeCommandIssuer(bridge);
                string error;
                var result = issuer.TryIssue(
                    "room", "admin", "user", NewSnapshot(), RemoteCommands.Pause, null,
                    DateTimeOffset.UtcNow, out error);

                Assert.False(result);
                Assert.Equal("command_timeout", error);
            }
        }

        [Fact]
        public void TryIssueMessage_TransportFailure_ReturnsStableErrorWithoutExceptionDetails()
        {
            var manager = NewManager();
            manager.Setup(m => m.SendMessageCommand(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("private message detail"));
            using (var bridge = new SessionBridge(manager.Object))
            {
                var issuer = new SessionBridgeCommandIssuer(bridge);
                string error;
                var result = issuer.TryIssueMessage(
                    "room", "admin", "user", NewSnapshot(), "header", "text", 3000,
                    DateTimeOffset.UtcNow, out error);

                Assert.False(result);
                Assert.Equal("command_failed", error);
                Assert.DoesNotContain("private message detail", error);
            }
        }

        private static Mock<ISessionManager> NewManager()
        {
            var manager = new Mock<ISessionManager>();
            manager.Setup(m => m.SendPlaystateCommand(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlaystateRequest>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            manager.Setup(m => m.SendMessageCommand(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            return manager;
        }

        private static SessionSnapshot NewSnapshot()
        {
            return new SessionSnapshot(
                "session", "user", "item", "source", 0, 1, false, 1, false, true,
                new SessionCapabilityReport(true, new[] { RemoteCommands.Pause, "DisplayMessage" }));
        }
    }
}
