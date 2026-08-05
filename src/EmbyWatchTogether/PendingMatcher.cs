using System;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Decides whether a pending command has been acknowledged by the client
    /// (ported from the Python _pending_matches).
    /// </summary>
    public static class PendingMatcher
    {
        public static bool Matches(PendingCommand pending, SessionSnapshot snapshot)
        {
            if (pending == null || snapshot == null)
            {
                return false;
            }

            return Matches(pending.Command, pending.PositionTicks, snapshot);
        }

        public static bool Matches(string command, long? positionTicks, SessionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }

            if (string.Equals(command, RemoteCommands.Pause, StringComparison.Ordinal))
            {
                return snapshot.IsPaused;
            }

            if (string.Equals(command, RemoteCommands.Unpause, StringComparison.Ordinal))
            {
                return !snapshot.IsPaused;
            }

            if (string.Equals(command, RemoteCommands.Seek, StringComparison.Ordinal))
            {
                long target = positionTicks ?? 0;
                return Math.Abs(snapshot.PositionTicks - target) <= SyncConstants.SeekToleranceTicks;
            }

            return false;
        }
    }
}
