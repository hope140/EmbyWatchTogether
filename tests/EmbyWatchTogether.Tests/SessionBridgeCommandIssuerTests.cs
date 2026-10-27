using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        [Theory]
        [InlineData("Pause", false)]
        [InlineData("Pause", true)]
        [InlineData("PlayItem", false)]
        [InlineData("PlayItem", true)]
        [InlineData("Message", false)]
        [InlineData("Message", true)]
        public async Task BoundedDispatch_UncooperativeTaskRetainsChannelSlotUntilLateCompletion(
            string operation, bool lateFault)
        {
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int dispatches = 0;
            CancellationToken transportToken = default;
            var manager = ManagerWithTransport((_, token) =>
            {
                if (Interlocked.Increment(ref dispatches) == 1)
                {
                    transportToken = token;
                    entered.TrySetResult(true);
                    return release.Task;
                }
                return Task.CompletedTask;
            });
            using var bridge = new SessionBridge(manager.Object);
            var exceptions = new List<Exception>();
            var issuer = NewBoundedIssuer(bridge, TimeSpan.FromMilliseconds(100),
                NewLogManager(new List<string>(), exceptions).Object);
            try
            {
                var pending = Task.Run(() => Dispatch(issuer, operation));
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.False(result.accepted);
                Assert.Equal("command_timeout", result.error);
                Assert.True(SpinWait.SpinUntil(() => transportToken.IsCancellationRequested, 1000));
                foreach (var otherOperation in operation == "Message"
                    ? new[] { "Message" } : new[] { "Pause", "PlayItem" })
                {
                    var blocked = Dispatch(issuer, otherOperation);
                    Assert.False(blocked.accepted);
                    Assert.Equal("command_timeout", blocked.error);
                }
                Assert.Equal(1, Volatile.Read(ref dispatches));
                int loggedBeforeRelease = exceptions.Count;
                if (lateFault) release.TrySetException(new InvalidOperationException("late transport detail"));
                else release.TrySetResult(true);

                // Completion releases the slot; no late result logs or automatic resend.
                await Task.Delay(30);
                Assert.Equal(1, Volatile.Read(ref dispatches));
                Assert.Equal(loggedBeforeRelease, exceptions.Count);
                (bool accepted, string error) recovered = default;
                for (int attempt = 0; attempt < 100; attempt++)
                {
                    recovered = Dispatch(issuer, operation);
                    if (recovered.accepted) break;
                    await Task.Delay(10);
                }
                Assert.True(recovered.accepted, recovered.error);
                Assert.Equal(2, Volatile.Read(ref dispatches));
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        [Theory]
        [InlineData("Pause")]
        [InlineData("PlayItem")]
        [InlineData("Message")]
        public async Task BoundedDispatch_SynchronouslyBlockedFactoryDoesNotBlockCaller(string operation)
        {
            using var release = new ManualResetEventSlim(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var returned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var manager = ManagerWithTransport((_, token) =>
            {
                entered.TrySetResult(true);
                release.Wait();
                returned.TrySetResult(true);
                return Task.CompletedTask;
            });
            using var bridge = new SessionBridge(manager.Object);
            var issuer = NewBoundedIssuer(bridge, TimeSpan.FromMilliseconds(100));
            try
            {
                var pending = Task.Run(() => Dispatch(issuer, operation));
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.False(result.accepted);
                Assert.Equal("command_timeout", result.error);
                Assert.False(returned.Task.IsCompleted);
            }
            finally
            {
                release.Set();
                await returned.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BoundedDispatch_CancellationAndPreCancellationReturnStableTimeout(bool preCancelled)
        {
            int dispatches = 0;
            var manager = ManagerWithTransport((_, token) =>
            {
                Interlocked.Increment(ref dispatches);
                return Task.Delay(Timeout.Infinite, token);
            });
            using var bridge = new SessionBridge(manager.Object);
            var issuer = NewBoundedIssuer(bridge, TimeSpan.FromMilliseconds(100));
            using var cancellation = new CancellationTokenSource();
            if (preCancelled) cancellation.Cancel();
            var accepted = issuer.TryIssuePlayItem("room", "admin", "user", NewPlayItemSnapshot(),
                "42", DateTimeOffset.UtcNow, cancellation.Token, out var error);
            Assert.False(accepted);
            Assert.Equal("command_timeout", error);
            Assert.Equal(preCancelled ? 0 : 1, Volatile.Read(ref dispatches));
        }

        [Fact]
        public void BoundedDispatch_TotalUnfinishedFlightsAreCappedAndRecoverAfterCompletion()
        {
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int dispatches = 0;
            var manager = ManagerWithTransport((_, token) =>
            {
                Interlocked.Increment(ref dispatches);
                return release.Task;
            });
            using var bridge = new SessionBridge(manager.Object);
            var issuer = NewBoundedIssuer(bridge, TimeSpan.FromMilliseconds(100));
            try
            {
                for (int i = 0; i < 64; i++)
                {
                    var result = Dispatch(issuer, "PlayItem", "session-" + i);
                    Assert.False(result.accepted);
                    Assert.Equal("command_timeout", result.error);
                }
                Assert.Equal(64, Volatile.Read(ref dispatches));
                var saturated = Dispatch(issuer, "PlayItem", "session-overflow");
                Assert.False(saturated.accepted);
                Assert.Equal("command_timeout", saturated.error);
                var messageOverflow = Dispatch(issuer, "Message", "session-0");
                Assert.False(messageOverflow.accepted);
                Assert.Equal("command_timeout", messageOverflow.error);
                Assert.Equal(64, Volatile.Read(ref dispatches));
            }
            finally
            {
                release.TrySetResult(true);
            }
            Assert.True(SpinWait.SpinUntil(() => Dispatch(issuer, "PlayItem", "session-overflow").accepted, 2000));
            Assert.Equal(65, Volatile.Read(ref dispatches));
        }

        [Fact]
        public async Task BoundedDispatch_ExpiredFactoryNeverDispatchesAfterQueueContention()
        {
            int dispatches = 0;
            var manager = ManagerWithTransport((_, token) =>
            {
                Interlocked.Increment(ref dispatches);
                return Task.CompletedTask;
            });
            using var bridge = new SessionBridge(manager.Object);
            var issuer = NewBoundedIssuer(bridge, TimeSpan.FromMilliseconds(100));
            var dispatch = typeof(SessionBridgeCommandIssuer).GetField("_dispatch", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(issuer);
            var admissionGate = dispatch.GetType().GetField("_lock", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(dispatch);
            var result = new TaskCompletionSource<(bool accepted, string error)>(TaskCreationOptions.RunContinuationsAsynchronously);
            var caller = new Thread(() =>
            {
                try { result.TrySetResult(Dispatch(issuer, "PlayItem")); }
                catch (Exception exception) { result.TrySetException(exception); }
            }) { IsBackground = true };
            // Delay admission past the deadline, deterministically producing an
            // already-expired queued factory without changing global pool limits.
            lock (admissionGate)
            {
                caller.Start();
                Assert.True(SpinWait.SpinUntil(() =>
                    (caller.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, 2000));
                Thread.Sleep(150);
            }
            var expired = await result.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(expired.accepted);
            Assert.Equal("command_timeout", expired.error);
            Assert.True(caller.Join(2000));
            Assert.Equal(0, Volatile.Read(ref dispatches));
            Assert.True(SpinWait.SpinUntil(() => Dispatch(issuer, "PlayItem").accepted, 2000));
            Assert.Equal(1, Volatile.Read(ref dispatches));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task BoundedDispatch_CompletedTransportDoesNotBlockSuccessorWhileCleanupWaits(bool successorPending)
        {
            var firstRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var successorRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int dispatches = 0;
            var manager = ManagerWithTransport((_, token) =>
            {
                int attempt = Interlocked.Increment(ref dispatches);
                if (attempt == 1)
                {
                    entered.TrySetResult(true);
                    return firstRelease.Task;
                }
                return attempt == 2 && successorPending ? successorRelease.Task : Task.CompletedTask;
            });
            using var bridge = new SessionBridge(manager.Object);
            var issuer = NewBoundedIssuer(bridge, TimeSpan.FromSeconds(1));
            var dispatch = typeof(SessionBridgeCommandIssuer).GetField("_dispatch", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(issuer);
            var admissionGate = dispatch.GetType().GetField("_lock", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(dispatch);
            using var firstReturned = new ManualResetEventSlim(false);
            (bool accepted, string error) firstResult = default;
            Exception firstException = null;
            var caller = new Thread(() =>
            {
                try { firstResult = Dispatch(issuer, "PlayItem"); }
                catch (Exception exception) { firstException = exception; }
                finally { firstReturned.Set(); }
            }) { IsBackground = true };
            try
            {
                caller.Start();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.True(SpinWait.SpinUntil(() =>
                    (caller.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, 2000));
                lock (admissionGate)
                {
                    // Hold cleanup after the transport publishes completion.
                    // This thread can reenter admission while that callback waits.
                    firstRelease.TrySetResult(true);
                    Assert.True(firstReturned.Wait(2000));
                    Assert.Null(firstException);
                    Assert.True(firstResult.accepted, firstResult.error);
                    var successor = Dispatch(issuer, "Pause");
                    Assert.Equal(!successorPending, successor.accepted);
                    Assert.Equal(successorPending ? "command_timeout" : null, successor.error);
                    Assert.Equal(2, Volatile.Read(ref dispatches));
                }
                if (successorPending)
                {
                    // The old continuation must not delete the successor's slot.
                    await Task.Delay(30);
                    var blocked = Dispatch(issuer, "Pause");
                    Assert.False(blocked.accepted);
                    Assert.Equal("command_timeout", blocked.error);
                    Assert.Equal(2, Volatile.Read(ref dispatches));
                }
                else
                {
                    Assert.True(Dispatch(issuer, "Pause").accepted);
                    Assert.Equal(3, Volatile.Read(ref dispatches));
                }
            }
            finally
            {
                firstRelease.TrySetResult(true);
                successorRelease.TrySetResult(true);
                Assert.True(caller.Join(2000));
            }
        }

        [Theory]
        [InlineData("Message", "Pause")]
        [InlineData("Pause", "Message")]
        public async Task BoundedDispatch_MessageAndPlaybackUseIndependentSlots(string blockedOperation, string allowedOperation)
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            var manager = ManagerWithTransport((session, _) =>
            {
                Assert.Equal("session", session); // channel key never reaches Emby
                if (Interlocked.Increment(ref calls) == 1)
                {
                    entered.TrySetResult(true);
                    return release.Task;
                }
                return Task.CompletedTask;
            });
            using var bridge = new SessionBridge(manager.Object);
            var issuer = NewBoundedIssuer(bridge, TimeSpan.FromMilliseconds(100));
            try
            {
                var first = Task.Run(() => Dispatch(issuer, blockedOperation));
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(Dispatch(issuer, allowedOperation).accepted);
                Assert.False(Dispatch(issuer, blockedOperation).accepted);
                Assert.False((await first.WaitAsync(TimeSpan.FromSeconds(5))).accepted);
                Assert.Equal(2, Volatile.Read(ref calls));
            }
            finally { release.TrySetResult(true); }
        }

        private static SessionBridgeCommandIssuer NewBoundedIssuer(
            SessionBridge bridge, TimeSpan timeout, ILogManager logManager = null)
        {
            return (SessionBridgeCommandIssuer)Activator.CreateInstance(
                typeof(SessionBridgeCommandIssuer), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { bridge, logManager, timeout }, null);
        }

        private static (bool accepted, string error) Dispatch(
            SessionBridgeCommandIssuer issuer, string operation, string sessionId = "session")
        {
            var snapshot = new SessionSnapshot(sessionId, "user", "42", "source", 0, 1,
                false, 1, false, true,
                new SessionCapabilityReport(true, new[] { RemoteCommands.Pause, RemoteCommands.DisplayMessage }));
            string error;
            bool accepted;
            switch (operation)
            {
                case "PlayItem":
                    accepted = issuer.TryIssuePlayItem("room", "admin", "user", snapshot, "42", DateTimeOffset.UtcNow, out error);
                    break;
                case "Message":
                    accepted = issuer.TryIssueMessage("room", "admin", "user", snapshot, "header", "text", 3000, DateTimeOffset.UtcNow, out error);
                    break;
                default:
                    accepted = issuer.TryIssue("room", "admin", "user", snapshot, RemoteCommands.Pause, null, DateTimeOffset.UtcNow, out error);
                    break;
            }
            return (accepted, error);
        }

        private static Mock<ISessionManager> ManagerWithTransport(Func<string, CancellationToken, Task> transport)
        {
            var manager = NewManager();
            manager.Setup(m => m.SendPlaystateCommand(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<PlaystateRequest>(), It.IsAny<CancellationToken>()))
                .Returns<string, string, PlaystateRequest, CancellationToken>((_, sessionId, request, token) => transport(sessionId, token));
            manager.Setup(m => m.SendPlayCommand(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<PlayRequest>(), It.IsAny<CancellationToken>()))
                .Returns<string, string, PlayRequest, CancellationToken>((_, sessionId, request, token) => transport(sessionId, token));
            manager.Setup(m => m.SendMessageCommand(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<MessageCommand>(), It.IsAny<CancellationToken>()))
                .Returns<string, string, MessageCommand, CancellationToken>((_, sessionId, request, token) => transport(sessionId, token));
            return manager;
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
