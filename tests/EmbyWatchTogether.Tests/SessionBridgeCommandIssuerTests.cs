using System;
using System.Collections.Generic;
using System.Linq;
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

        [Fact]
        public void TryIssuePlayItem_LogsOneDispatchAndReturnedResult_WithRequestHash()
        {
            var manager = NewManager();
            var messages = new List<string>();
            var exceptions = new List<Exception>();
            var infos = new List<string>();
            var logManager = NewLogManager(messages, exceptions, infos);
            using (var bridge = new SessionBridge(manager.Object))
            {
                var issuer = new SessionBridgeCommandIssuer(bridge, logManager.Object);
                string error;
                var result = issuer.TryIssuePlayItem(
                    "room-secret", "admin-secret", "user", NewPlayItemSnapshot(), "42",
                    DateTimeOffset.UtcNow, out error);

                Assert.True(result);
                Assert.Null(error);
                Assert.Single(infos, message => message.Contains("event=dispatch "));
                Assert.Single(infos, message => message.Contains("event=dispatch_result ") &&
                    message.Contains("result=returned"));
                manager.Verify(m => m.SendPlayCommand(
                    It.IsAny<string>(), "session", It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()),
                    Times.Once);
                Assert.Contains(infos, message => message.Contains("requestHash=") &&
                    !message.Contains("room-secret") && !message.Contains("admin-secret"));
            }
        }

        [Fact]
        public void TryIssuePlayItem_ValidationRejectionDoesNotLogDispatch()
        {
            var messages = new List<string>();
            var exceptions = new List<Exception>();
            var infos = new List<string>();
            var logManager = NewLogManager(messages, exceptions, infos);
            using (var bridge = new SessionBridge(NewManager().Object))
            {
                var issuer = new SessionBridgeCommandIssuer(bridge, logManager.Object);
                string error;
                var result = issuer.TryIssuePlayItem(
                    "room-secret", "admin-secret", "user-secret", NewPlayItemSnapshot(),
                    "not-numeric", DateTimeOffset.UtcNow, out error);

                Assert.False(result);
                Assert.Equal("invalid_argument", error);
                Assert.Empty(infos);
            }
        }

        [Theory]
        [InlineData(true, "timeout", "command_timeout")]
        [InlineData(false, "failed", "command_failed")]
        public void TryIssuePlayItem_TransportFailureLogsOneStableDispatchResult(
            bool timeout,
            string expectedDispatchResult,
            string expectedError)
        {
            var manager = NewManager();
            manager.Setup(m => m.SendPlayCommand(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()))
                .Throws(timeout
                    ? (Exception)new OperationCanceledException("secret transport detail")
                    : new InvalidOperationException("secret transport detail"));
            var messages = new List<string>();
            var exceptions = new List<Exception>();
            var infos = new List<string>();
            var logManager = NewLogManager(messages, exceptions, infos);
            using (var bridge = new SessionBridge(manager.Object))
            {
                var issuer = new SessionBridgeCommandIssuer(bridge, logManager.Object);
                string error;
                var result = issuer.TryIssuePlayItem(
                    "room-secret", "admin-secret", "user", NewPlayItemSnapshot(), "42",
                    DateTimeOffset.UtcNow, new CancellationTokenSource().Token, out error);

                Assert.False(result);
                Assert.Equal(expectedError, error);
                Assert.Single(infos, message => message.Contains("event=dispatch "));
                Assert.Single(infos, message => message.Contains("event=dispatch_result ") &&
                    message.Contains("result=" + expectedDispatchResult));
                manager.Verify(m => m.SendPlayCommand(
                    It.IsAny<string>(), "session", It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()),
                    Times.Once);
                Assert.DoesNotContain(infos, message => message.Contains("secret transport detail"));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TryIssuePlayItem_LoggerFailureDoesNotChangeCommandResult(bool timeout)
        {
            var manager = NewManager();
            if (timeout)
            {
                manager.Setup(m => m.SendPlayCommand(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()))
                    .Throws(new OperationCanceledException("logger timeout detail"));
            }
            var logger = new Mock<ILogger>();
            logger.Setup(l => l.Info(It.IsAny<string>(), It.IsAny<object[]>()))
                .Throws(new InvalidOperationException("logger secret"));
            var logManager = new Mock<ILogManager>();
            logManager.Setup(l => l.GetLogger(nameof(SessionBridgeCommandIssuer)))
                .Returns(logger.Object);
            using (var bridge = new SessionBridge(manager.Object))
            {
                var issuer = new SessionBridgeCommandIssuer(bridge, logManager.Object);
                string error;
                var result = issuer.TryIssuePlayItem(
                    "room", "admin", "user", NewPlayItemSnapshot(), "42",
                    DateTimeOffset.UtcNow, out error);

                Assert.Equal(!timeout, result);
                Assert.Equal(timeout ? "command_timeout" : null, error);
                manager.Verify(m => m.SendPlayCommand(
                    It.IsAny<string>(), "session", It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()),
                    Times.Once);
            }
        }

        [Fact]
        public void TryIssuePlayItem_RequestHashUsesUtcLengthPrefixesAndRedactsInputs()
        {
            var manager = NewManager();
            var messages = new List<string>();
            var exceptions = new List<Exception>();
            var infos = new List<string>();
            var logManager = NewLogManager(messages, exceptions, infos);
            var utc = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
            var sameInstantDifferentOffset = utc.ToOffset(TimeSpan.FromHours(8));
            using (var bridge = new SessionBridge(manager.Object))
            {
                var issuer = new SessionBridgeCommandIssuer(bridge, logManager.Object);
                string error;
                Assert.True(issuer.TryIssuePlayItem(
                    "room", "admin", "user", NewPlayItemSnapshot(sessionId: "session"), "42",
                    utc, out error));
                Assert.True(issuer.TryIssuePlayItem(
                    "room", "admin", "user", NewPlayItemSnapshot(sessionId: "session"), "42",
                    sameInstantDifferentOffset, out error));
                Assert.True(issuer.TryIssuePlayItem(
                    "a|b", "admin", "user", NewPlayItemSnapshot(sessionId: "c"), "42",
                    utc, out error));
                Assert.True(issuer.TryIssuePlayItem(
                    "a", "admin", "user", NewPlayItemSnapshot(sessionId: "b|c"), "42",
                    utc, out error));
                Assert.True(issuer.TryIssuePlayItem(
                    "room\r\nSECRET https://internal.example/x", "admin", "user",
                    NewPlayItemSnapshot(sessionId: "session\r\nSESSION_SECRET"), "42",
                    utc, out error));
            }

            var dispatches = infos.Where(message => message.Contains("event=dispatch ")).ToList();
            var requests = dispatches.Select(message => message.Split(
                new[] { "requestHash=" }, StringSplitOptions.None)[1].Split(' ')[0]).ToList();
            Assert.Equal(5, requests.Count);
            Assert.Equal(requests[0], requests[1]);
            Assert.NotEqual(requests[2], requests[3]);
            Assert.DoesNotContain(infos, message => message.Contains("SECRET") ||
                message.Contains("internal.example") || message.Contains("room\r") ||
                message.Contains("session\r"));
            manager.Verify(m => m.SendPlayCommand(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()),
                Times.Exactly(5));
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
            List<Exception> exceptions,
            List<string> infos = null)
        {
            var logger = new Mock<ILogger>();
            logger.Setup(l => l.ErrorException(
                    It.IsAny<string>(), It.IsAny<Exception>(), It.IsAny<object[]>()))
                .Callback<string, Exception, object[]>((message, exception, _) =>
                {
                    messages.Add(message);
                    exceptions.Add(exception);
                });
            logger.Setup(l => l.Info(It.IsAny<string>(), It.IsAny<object[]>()))
                .Callback<string, object[]>((message, _) => infos?.Add(message));
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

        private static SessionSnapshot NewPlayItemSnapshot(
            bool stopped = false,
            string sessionId = "session",
            string userId = "user",
            string itemId = "42")
        {
            return new SessionSnapshot(
                sessionId, userId, itemId, "source", 0, 1, false, 1, stopped, true,
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
