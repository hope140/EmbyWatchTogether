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
        Handoff = 4,
        Recovering = 5,
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

        public bool AnchorPositionCandidatePaused { get; set; }

        public bool RestoreSent { get; set; }

        public bool FromMediaHandoff { get; set; }

        public bool FromAutomaticDriftRepair { get; set; }

        public double? AutomaticDriftSeconds { get; set; }
    }

    /// <summary>
    /// Ephemeral state for moving the participant to the primary's newly
    /// selected item. This is deliberately separate from Barrier pending
    /// commands and is never persisted with room metadata.
    /// </summary>
    public sealed class MediaHandoffState
    {
        public string TargetItemId { get; set; }

        public string SourceItemId { get; set; }

        public DateTimeOffset StartedAtUtc { get; set; }

        public string PrimaryUserId { get; set; }

        public string PrimarySessionId { get; set; }

        public string ParticipantUserId { get; set; }

        public string ParticipantSessionId { get; set; }

        public bool PlayItemPending { get; set; }

        public DateTimeOffset? PlayItemIssuedAtUtc { get; set; }

        public int RetryCount { get; set; }

        public long Generation { get; set; }

        public DateTimeOffset? NextRetryAtUtc { get; set; }

        public string LastError { get; set; }

        public bool IsParticipantResync { get; set; }

        public ISet<string> SupersededTargetItemIds { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ephemeral state for a short disappearance of one or both already-bound
    /// sessions while a room is Watching. The expected identity is copied from
    /// the last confirmed Watching snapshot so a later device session cannot
    /// be mistaken for a recovery.
    /// </summary>
    public sealed class TransientSessionRecoveryState
    {
        public string MissingUserId { get; set; }

        public string ExpectedSessionId { get; set; }

        public string ExpectedItemId { get; set; }

        public DateTimeOffset StartedAtUtc { get; set; }

        public long LastKnownPositionTicks { get; set; }

        public bool LastKnownPaused { get; set; }

        public List<string> MissingUserIds { get; } = new List<string>();

        public Dictionary<string, TransientSessionRecoveryParticipant> Participants { get; } =
            new Dictionary<string, TransientSessionRecoveryParticipant>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class TransientSessionRecoveryParticipant
    {
        public string UserId { get; set; }

        public string ExpectedSessionId { get; set; }

        public string ExpectedItemId { get; set; }

        public long LastKnownPositionTicks { get; set; }

        public bool LastKnownPaused { get; set; }
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
    /// Bounded attempt state for Pauses issued while a room is Waiting. The
    /// identity and capability key keep a later session or changed
    /// command-capability condition from inheriting the previous limit.
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
