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

            if (string.Equals(pending.Command, RemoteCommands.Pause, StringComparison.Ordinal))
            {
                return snapshot.IsPaused;
            }

            if (string.Equals(pending.Command, RemoteCommands.Unpause, StringComparison.Ordinal))
            {
                return !snapshot.IsPaused;
            }

            if (string.Equals(pending.Command, RemoteCommands.Seek, StringComparison.Ordinal))
            {
                long target = pending.PositionTicks ?? 0;
                return Math.Abs(snapshot.PositionTicks - target) <= SyncConstants.SeekToleranceTicks;
            }

            return false;
        }
    }
}
