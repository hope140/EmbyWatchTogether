using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
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
        public void TryIssue_TransportFailure_LogsExceptionWithoutReturningDetails()
        {
            var manager = NewManager();
            var exception = new InvalidOperationException("private transport detail");
            manager.Setup(m => m.SendPlaystateCommand(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlaystateRequest>(), It.IsAny<CancellationToken>()))
                .Throws(exception);
            var messages = new List<string>();
            var exceptions = new List<Exception>();
            var logManager = NewLogManager(messages, exceptions);
            using (var bridge = new SessionBridge(manager.Object))
            {
                var issuer = new SessionBridgeCommandIssuer(bridge, logManager.Object);
                string error;
                var result = issuer.TryIssue(
                    "room", "admin", "user", NewSnapshot(), RemoteCommands.Pause, null,
                    DateTimeOffset.UtcNow, out error);

                Assert.False(result);
                Assert.Equal("command_failed", error);
                Assert.DoesNotContain("private transport detail", error);
                Assert.Single(exceptions);
                Assert.Same(exception, exceptions[0]);
                Assert.Contains("private transport detail", exceptions[0].Message);
                Assert.Contains("room", messages[0]);
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
        public void TryIssue_CancelledTransport_LogsExceptionWithoutReturningDetails()
        {
            var manager = NewManager();
            var exception = new OperationCanceledException("private timeout detail");
            manager.Setup(m => m.SendPlaystateCommand(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlaystateRequest>(), It.IsAny<CancellationToken>()))
                .Throws(exception);
            var messages = new List<string>();
            var exceptions = new List<Exception>();
            var logManager = NewLogManager(messages, exceptions);
            using (var bridge = new SessionBridge(manager.Object))
            {
                var issuer = new SessionBridgeCommandIssuer(bridge, logManager.Object);
                string error;
                var result = issuer.TryIssue(
                    "room", "admin", "user", NewSnapshot(), RemoteCommands.Pause, null,
                    DateTimeOffset.UtcNow, out error);

                Assert.False(result);
                Assert.Equal("command_timeout", error);
                Assert.DoesNotContain("private timeout detail", error);
                Assert.Single(exceptions);
                Assert.Same(exception, exceptions[0]);
                Assert.Contains("private timeout detail", exceptions[0].Message);
                Assert.Contains("timed out", messages[0]);
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

        [Fact]
        public void TryIssuePlayItem_AllowsTargetDifferentFromCurrentSnapshotItem()
        {
            var manager = NewManager();
            using (var bridge = new SessionBridge(manager.Object))
            {
                var issuer = new SessionBridgeCommandIssuer(bridge);
                string error;
                var result = issuer.TryIssuePlayItem(
                    "room", "admin", "user", NewPlayItemSnapshot(), "43",
                    DateTimeOffset.UtcNow, out error);

                Assert.True(result);
                Assert.Null(error);
                manager.Verify(m => m.SendPlayCommand(
                    It.IsAny<string>(), "session", It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        [Theory]
        [InlineData("", "user", "42", "invalid_argument")]
        [InlineData("admin", "other", "42", "invalid_argument")]
        [InlineData("admin", "user", "other", "invalid_argument")]
        public void TryIssuePlayItem_InvalidIdentityOrItem_ReturnsStableError(
            string controllingUserId, string userId, string itemId, string expectedError)
        {
            using (var bridge = new SessionBridge(NewManager().Object))
            {
                var issuer = new SessionBridgeCommandIssuer(bridge);
                string error;
                var result = issuer.TryIssuePlayItem(
                    "room", controllingUserId, userId, NewPlayItemSnapshot(), itemId,
                    DateTimeOffset.UtcNow, out error);

                Assert.False(result);
                Assert.Equal(expectedError, error);
            }
        }

        [Fact]
        public void TryIssuePlayItem_OfflineOrUnsupported_IsRejected()
        {
            var manager = NewManager();
            using (var bridge = new SessionBridge(manager.Object))
            {
                var issuer = new SessionBridgeCommandIssuer(bridge);
                string error;
                var offline = issuer.TryIssuePlayItem("room", "admin", "user", NewPlayItemSnapshot(stopped: true), "42", DateTimeOffset.UtcNow, out error);
                Assert.False(offline);
                Assert.Equal("session_offline", error);

                var unsupported = issuer.TryIssuePlayItem("room", "admin", "user", NewUnsupportedPlayItemSnapshot(), "42", DateTimeOffset.UtcNow, out error);
                Assert.False(unsupported);
                Assert.Equal("remote_control_unsupported", error);
            }
        }

        [Fact]
        public void TryIssuePlayItem_CancelledTransport_ReturnsTimeoutWithoutDetails()
        {
            var manager = NewManager();
            manager.Setup(m => m.SendPlayCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()))
                .Throws(new OperationCanceledException("private play detail"));
            using (var bridge = new SessionBridge(manager.Object))
            {
                var issuer = new SessionBridgeCommandIssuer(bridge);
                string error;
                var result = issuer.TryIssuePlayItem(
                    "room", "admin", "user", NewPlayItemSnapshot(), "42", DateTimeOffset.UtcNow,
                    new CancellationTokenSource().Token, out error);

                Assert.False(result);
                Assert.Equal("command_timeout", error);
                Assert.DoesNotContain("private play detail", error);
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
            manager.Setup(m => m.SendPlayCommand(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            return manager;
        }

        private static Mock<ILogManager> NewLogManager(
            List<string> messages,
            List<Exception> exceptions)
        {
            var logger = new Mock<ILogger>();
            logger.Setup(l => l.ErrorException(
                    It.IsAny<string>(), It.IsAny<Exception>(), It.IsAny<object[]>()))
                .Callback<string, Exception, object[]>((message, exception, _) =>
                {
                    messages.Add(message);
                    exceptions.Add(exception);
                });
            var logManager = new Mock<ILogManager>();
            logManager.Setup(l => l.GetLogger(nameof(SessionBridgeCommandIssuer)))
                .Returns(logger.Object);
            return logManager;
        }

        private static SessionSnapshot NewSnapshot()
        {
            return new SessionSnapshot(
                "session", "user", "item", "source", 0, 1, false, 1, false, true,
                new SessionCapabilityReport(true, new[] { RemoteCommands.Pause, "DisplayMessage" }));
        }

        private static SessionSnapshot NewPlayItemSnapshot(bool stopped = false)
        {
            return new SessionSnapshot(
                "session", "user", "42", "source", 0, 1, false, 1, stopped, true,
                new SessionCapabilityReport(true, new[] { RemoteCommands.Pause }));
        }

        private static SessionSnapshot NewUnsupportedPlayItemSnapshot()
        {
            return new SessionSnapshot(
                "session", "user", "42", "source", 0, 1, false, 1, false, true,
                new SessionCapabilityReport(false, new string[0]));
        }
    }
}
