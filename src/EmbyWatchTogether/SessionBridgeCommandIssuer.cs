using System;
using System.Threading;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// ICommandIssuer backed by SessionBridge. Commands are gated on the
    /// snapshot's capability report so unsupported clients are never targeted
    /// (same gate as the Python reference session selection).
    /// </summary>
    public sealed class SessionBridgeCommandIssuer : ICommandIssuer
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

            var capabilities = snapshot.Capabilities;
            bool supported =
                (string.Equals(command, RemoteCommands.Pause, StringComparison.Ordinal) && capabilities.CanPause) ||
                (string.Equals(command, RemoteCommands.Unpause, StringComparison.Ordinal) && capabilities.CanUnpause) ||
                (string.Equals(command, RemoteCommands.Seek, StringComparison.Ordinal) && capabilities.CanSeek);

            if (!supported)
            {
                error = $"session does not support command {command}";
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
    }
}
