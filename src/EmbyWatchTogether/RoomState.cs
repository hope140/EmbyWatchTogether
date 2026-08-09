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

        public DateTimeOffset? SeekRetryAtUtc { get; set; }

        /// <summary>
        /// Absolute deadline shared by the initial Barrier Seek and all of its
        /// retries. A retry must never create a new budget for the same barrier.
        /// </summary>
        public DateTimeOffset? SeekRetryDeadlineAtUtc { get; set; }

        /// <summary>
        /// A candidate anchor position observed across a pause-state change
        /// during Seek. It is promoted only after the plugin's Pause is
        /// acknowledged at the same identity and position; the candidate is
        /// cleared when the barrier is rebuilt or leaves Seek.
        /// </summary>
        public long? AnchorPositionCandidateTicks { get; set; }

        public string AnchorPositionCandidateSessionId { get; set; }

        public string AnchorPositionCandidateItemId { get; set; }

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

    /// <summary>
    /// Bounded retry state for a Pause issued while a room is Waiting. The
    /// identity and capability key prevent a failed client from suppressing a
    /// later session or a changed command-capability condition.
    /// </summary>
    public sealed class WaitingPauseRetryState
    {
        public string SessionId { get; set; }

        public string ItemId { get; set; }

        public string CapabilityKey { get; set; }

        public int Attempts { get; set; }

        public DateTimeOffset NextAttemptAtUtc { get; set; }

        public bool Exhausted { get; set; }
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
