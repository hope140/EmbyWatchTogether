using System;
using System.Collections.Generic;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Mutable per-room runtime state held in memory (persisted room metadata is
    /// the Room entity; runtime is rebuilt on restart).
    /// </summary>
    public sealed class RoomRuntime
    {
        public RoomState State { get; set; } = RoomState.Waiting;

        public string Error { get; set; }

        internal RoomEligibilityFailureReason? LastEligibilityFailureReason { get; set; }

        internal string LastMultipleSessionDiagnosticSignature { get; set; }

        public Dictionary<string, PendingCommand> Pending { get; } = new Dictionary<string, PendingCommand>();

        public Dictionary<string, WaitingPauseRetryState> WaitingPauseRetries { get; } =
            new Dictionary<string, WaitingPauseRetryState>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, SuppressedCommand> Suppressed { get; } = new Dictionary<string, SuppressedCommand>();

        /// <summary>
        /// Rolling exponential moving average (seconds) of the time between a
        /// remote command being issued and its acknowledgement appearing in a
        /// SessionInfo snapshot, per user. Used to raise the manual-seek
        /// detection threshold for clients whose snapshots lag. Retained across
        /// resets because it describes the client, not the room state.
        /// </summary>
        public Dictionary<string, double> AckLatencySeconds { get; } =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Pending pause-alignment targets per user (see <see cref="PauseAlignState"/>).
        /// </summary>
        public Dictionary<string, PauseAlignState> PauseAlign { get; } =
            new Dictionary<string, PauseAlignState>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Last time a Seek command was issued to each user. Used to ignore the
        /// small position rewind some players report shortly after a remote seek
        /// lands (clock re-basing) without ignoring real user seeks.
        /// </summary>
        public Dictionary<string, DateTimeOffset> LastSeekAtUtc { get; } =
            new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, SessionSnapshot> Previous { get; } = new Dictionary<string, SessionSnapshot>();

        public DateTimeOffset? PreviousAtUtc { get; set; }

        public DateTimeOffset? MissingSessionSinceUtc { get; set; }

        public int DriftRounds { get; set; }

        public string SyncItemId { get; set; }

        public BarrierState Barrier { get; set; }

        public DateTimeOffset? BarrierRetryAtUtc { get; set; }

        public void ResetToWaiting()
        {
            bool preserveStopIdentity = MissingSessionSinceUtc.HasValue;
            State = RoomState.Waiting;
            Error = null;
            Barrier = null;
            Pending.Clear();
            WaitingPauseRetries.Clear();
            Suppressed.Clear();
            PauseAlign.Clear();
            LastSeekAtUtc.Clear();
            if (!preserveStopIdentity)
            {
                Previous.Clear();
                PreviousAtUtc = null;
            }
            DriftRounds = 0;
            if (!preserveStopIdentity)
            {
                SyncItemId = null;
            }
            BarrierRetryAtUtc = null;
        }
    }
}
