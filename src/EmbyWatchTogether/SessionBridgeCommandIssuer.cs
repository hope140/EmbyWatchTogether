using System;
using System.Threading;
using MediaBrowser.Model.Logging;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// ICommandIssuer backed by SessionBridge. Commands are gated on the
    /// snapshot's capability report so unsupported clients are never targeted
    /// (same gate as the Python reference session selection).
    /// </summary>
    public sealed class SessionBridgeCommandIssuer :
        ICommandIssuer,
        IMessageIssuer,
        ICancellableCommandIssuer,
        ICancellableMessageIssuer
    {
        private static readonly TimeSpan ExternalCallTimeout = TimeSpan.FromSeconds(5);
        private readonly SessionBridge _bridge;
        private readonly ILogger _logger;

        public SessionBridgeCommandIssuer(SessionBridge bridge, ILogManager logManager = null)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            try
            {
                _logger = logManager?.GetLogger(nameof(SessionBridgeCommandIssuer));
            }
            catch
            {
                _logger = null;
            }
        }

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
            using (var timeout = new CancellationTokenSource(ExternalCallTimeout))
            {
                return TryIssueWithCancellation(
                    roomId,
                    controllingUserId,
                    userId,
                    snapshot,
                    command,
                    positionTicks,
                    now,
                    timeout.Token,
                    out error);
            }
        }

        bool ICancellableCommandIssuer.TryIssue(
            string roomId,
            string controllingUserId,
            string userId,
            SessionSnapshot snapshot,
            string command,
            long? positionTicks,
            DateTimeOffset now,
            CancellationToken cancellationToken,
            out string error)
        {
            return TryIssueWithCancellation(
                roomId,
                controllingUserId,
                userId,
                snapshot,
                command,
                positionTicks,
                now,
                cancellationToken,
                out error);
        }

        private bool TryIssueWithCancellation(
            string roomId,
            string controllingUserId,
            string userId,
            SessionSnapshot snapshot,
            string command,
            long? positionTicks,
            DateTimeOffset now,
            CancellationToken cancellationToken,
            out string error)
        {
            if (snapshot == null || !snapshot.Online)
            {
                error = "session_offline";
                return false;
            }

            if (!IsCommandSupported(snapshot.Capabilities, command))
            {
                error = "remote_control_unsupported";
                return false;
            }

            try
            {
                switch (command)
                {
                    case RemoteCommands.Pause:
                        _bridge.SendPauseAsync(controllingUserId, snapshot.SessionId, cancellationToken)
                            .GetAwaiter().GetResult();
                        break;
                    case RemoteCommands.Unpause:
                        _bridge.SendUnpauseAsync(controllingUserId, snapshot.SessionId, cancellationToken)
                            .GetAwaiter().GetResult();
                        break;
                    case RemoteCommands.Seek:
                        _bridge.SendSeekAsync(controllingUserId, snapshot.SessionId, positionTicks ?? 0, cancellationToken)
                            .GetAwaiter().GetResult();
                        break;
                    default:
                        error = "remote_control_unsupported";
                        return false;
                }

                error = null;
                return true;
            }
            catch (OperationCanceledException exception)
            {
                LogFailure(
                    "Watch Together remote command timed out",
                    roomId,
                    userId,
                    command,
                    exception);
                error = "command_timeout";
                return false;
            }
            catch (Exception exception)
            {
                LogFailure(
                    "Watch Together remote command failed",
                    roomId,
                    userId,
                    command,
                    exception);
                error = "command_failed";
                return false;
            }
        }

        public bool TryIssueMessage(
            string roomId,
            string controllingUserId,
            string userId,
            SessionSnapshot snapshot,
            string header,
            string text,
            int? timeoutMs,
            DateTimeOffset now,
            out string error)
        {
            using (var timeout = new CancellationTokenSource(ExternalCallTimeout))
            {
                return TryIssueMessageWithCancellation(
                    roomId,
                    controllingUserId,
                    userId,
                    snapshot,
                    header,
                    text,
                    timeoutMs,
                    now,
                    timeout.Token,
                    out error);
            }
        }

        bool ICancellableMessageIssuer.TryIssueMessage(
            string roomId,
            string controllingUserId,
            string userId,
            SessionSnapshot snapshot,
            string header,
            string text,
            int? timeoutMs,
            DateTimeOffset now,
            CancellationToken cancellationToken,
            out string error)
        {
            return TryIssueMessageWithCancellation(
                roomId,
                controllingUserId,
                userId,
                snapshot,
                header,
                text,
                timeoutMs,
                now,
                cancellationToken,
                out error);
        }

        private bool TryIssueMessageWithCancellation(
            string roomId,
            string controllingUserId,
            string userId,
            SessionSnapshot snapshot,
            string header,
            string text,
            int? timeoutMs,
            DateTimeOffset now,
            CancellationToken cancellationToken,
            out string error)
        {
            if (snapshot == null || !snapshot.Online)
            {
                error = "session_offline";
                return false;
            }

            if (snapshot.Capabilities == null || !snapshot.Capabilities.CanDisplayMessage)
            {
                error = "remote_control_unsupported";
                return false;
            }

            try
            {
                _bridge.SendDisplayMessageAsync(
                    controllingUserId,
                    snapshot.SessionId,
                    header,
                    text,
                    timeoutMs,
                    cancellationToken)
                    .GetAwaiter().GetResult();
                error = null;
                return true;
            }
            catch (OperationCanceledException exception)
            {
                LogFailure(
                    "Watch Together display message timed out",
                    roomId,
                    userId,
                    RemoteCommands.DisplayMessage,
                    exception);
                error = "command_timeout";
                return false;
            }
            catch (Exception exception)
            {
                LogFailure(
                    "Watch Together display message failed",
                    roomId,
                    userId,
                    RemoteCommands.DisplayMessage,
                    exception);
                error = "command_failed";
                return false;
            }
        }

        private void LogFailure(
            string message,
            string roomId,
            string userId,
            string operation,
            Exception exception)
        {
            try
            {
                _logger?.ErrorException(
                    $"{message} (room={roomId}, user={userId}, operation={operation})",
                    exception);
            }
            catch
            {
                // Logging must never change the stable issuer result.
            }
        }

        /// <summary>
        /// Command gate ported from the Python reference: a session is targetable
        /// when it is remotely controllable (SupportsRemoteControl or a non-empty
        /// command list). Emby clients commonly omit Pause/Unpause/Seek from the
        /// declared command list (e.g. Emby Theater) while still honouring them
        /// through the session playback controller, so specific command names are
        /// not required here.
        /// </summary>
        public static bool IsCommandSupported(SessionCapabilityReport capabilities, string command)
        {
            if (capabilities == null ||
                (!capabilities.SupportsRemoteControl && capabilities.SupportedCommands.Count == 0))
            {
                return false;
            }

            switch (command)
            {
                case RemoteCommands.Pause:
                case RemoteCommands.Unpause:
                case RemoteCommands.PlayPause:
                case RemoteCommands.Seek:
                case RemoteCommands.Stop:
                    return true;
                default:
                    return false;
            }
        }
    }
}
