using System;
using System.Collections.Generic;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Room lifecycle states ported from the Python reference coordinator:
    /// waiting (idle/error recovery), barrier (pause-&gt;seek-&gt;restore),
    /// watching (active sync), unavailable (server mismatch).
    /// </summary>
    public enum RoomState
    {
        Waiting = 0,
        Barrier = 1,
        Watching = 2,
        Unavailable = 3,
    }

    public enum BarrierStage
    {
        Pause = 0,
        Seek = 1,
        Restore = 2,
    }

    public sealed class BarrierState
    {
        public BarrierStage Stage { get; set; }

        public DateTimeOffset StartedAtUtc { get; set; }

        /// <summary>
        /// The side whose position is the alignment target. Defaults to the
        /// room primary; a manual seek sets it to the user who dragged.
        /// </summary>
        public string AnchorUserId { get; set; }

        public long PrimaryPositionTicks { get; set; }

        public bool PrimaryPaused { get; set; }

        public string ItemId { get; set; }

        /// <summary>
        /// Session identity captured when the barrier starts. A matching ItemId
        /// is not enough to reuse a barrier after a participant reconnects with
        /// a different Emby session.
        /// </summary>
        public Dictionary<string, string> SessionIds { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool PauseSent { get; set; }

        public bool SeekSent { get; set; }

        public bool RestoreSent { get; set; }
    }

    public sealed class PendingCommand
    {
        public string UserId { get; set; }

        public string SessionId { get; set; }

        public string ItemId { get; set; }

        public string Command { get; set; }

        public long? PositionTicks { get; set; }

        public DateTimeOffset IssuedAtUtc { get; set; }

        public int Retries { get; set; }
    }

    public sealed class SuppressedCommand
    {
        public string SessionId { get; set; }

        public string ItemId { get; set; }

        public string Command { get; set; }

        public long? PositionTicks { get; set; }

        public DateTimeOffset UntilUtc { get; set; }
    }

    /// <summary>
    /// Deferred target for aligning a follower that confirmed a propagated
    /// pause: seek it to the paused anchor's position before anyone resumes.
    /// </summary>
    public sealed class PauseAlignState
    {
        public string AnchorUserId { get; set; }

        public string AnchorSessionId { get; set; }

        public string AnchorItemId { get; set; }

        public string SessionId { get; set; }

        public string ItemId { get; set; }

        public long TargetPositionTicks { get; set; }

        public DateTimeOffset CreatedAtUtc { get; set; }
    }
}
