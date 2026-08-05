using System;
using System.Threading;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// ICommandIssuer backed by SessionBridge. Commands are gated on the
    /// snapshot's capability report so unsupported clients are never targeted
    /// (same gate as the Python reference session selection).
    /// </summary>
    public sealed class SessionBridgeCommandIssuer : ICommandIssuer, IMessageIssuer
    {
        private readonly SessionBridge _bridge;

        public SessionBridgeCommandIssuer(SessionBridge bridge)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
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
            if (snapshot == null || !snapshot.Online)
            {
                error = "session is not online";
                return false;
            }

            if (!IsCommandSupported(snapshot.Capabilities, command))
            {
                error = "session does not support remote control";
                return false;
            }

            try
            {
                switch (command)
                {
                    case RemoteCommands.Pause:
                        _bridge.SendPauseAsync(controllingUserId, snapshot.SessionId, CancellationToken.None)
                            .GetAwaiter().GetResult();
                        break;
                    case RemoteCommands.Unpause:
                        _bridge.SendUnpauseAsync(controllingUserId, snapshot.SessionId, CancellationToken.None)
                            .GetAwaiter().GetResult();
                        break;
                    case RemoteCommands.Seek:
                        _bridge.SendSeekAsync(controllingUserId, snapshot.SessionId, positionTicks ?? 0, CancellationToken.None)
                            .GetAwaiter().GetResult();
                        break;
                    default:
                        error = $"unsupported session command: {command}";
                        return false;
                }

                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
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
            if (snapshot == null || !snapshot.Online)
            {
                error = "session is not online";
                return false;
            }

            if (snapshot.Capabilities == null || !snapshot.Capabilities.CanDisplayMessage)
            {
                error = "session does not support display messages";
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
                    CancellationToken.None)
                    .GetAwaiter().GetResult();
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
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
