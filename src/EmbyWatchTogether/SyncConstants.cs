namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Ported sync thresholds from the Python reference implementation.
    /// </summary>
    public static class SyncConstants
    {
        public const long TicksPerSecond = 10_000_000L;

        public const long MaxRuntimeDifferenceTicks = 3 * TicksPerSecond;

        public const long SeekToleranceTicks = 2 * TicksPerSecond;

        public const long DriftThresholdTicks = 5 * TicksPerSecond;

        // External players often report a small position rewind (a few seconds)
        // shortly after a seek lands while re-basing their clock. A backward
        // jump smaller than this is ignored only while the user is inside the
        // seek calibration window (see SeekCalibrationWindowSeconds); outside
        // that window any backward jump is a real user seek.
        public const long ManualSeekBackwardToleranceTicks = 15 * TicksPerSecond;

        public const double SeekCalibrationWindowSeconds = 15.0;

        // Manual seek detection (both directions): the position must differ
        // from the expected playback projection by at least this much to count
        // as a user seek. 4s makes the common +/-5s player buttons symmetric
        // (a 5s forward jump exceeds the projection by only 4s because playback
        // itself advances during the poll interval).
        // The floor is raised for a user whose command acknowledgement latency
        // (rolling EMA, see RoomRuntime.AckLatencySeconds) is higher, so that a
        // stale snapshot for a slow client is not mistaken for a manual seek.
        public const double SeekDetectionFloorSeconds = 4.0;

        // Upper bound for the measured per-user command acknowledgement latency
        // used to raise the seek detection threshold.
        public const double AckLatencyMaxSeconds = 10.0;

        // Pause propagation alignment: if the follower has not been aligned to
        // the paused anchor within this window, give up seeking it.
        public const double PauseAlignTimeoutSeconds = 10.0;

        public const double PlaybackRateTolerance = 0.010000001;

        public const double PendingTimeoutSeconds = 3.0;

        // Emby can acknowledge a command in the player before the next
        // SessionInfo snapshot exposes the new state. Keep this grace bounded
        // and apply it only after the one allowed retry in the initial barrier.
        public const double PendingRetryGraceSeconds = 1.0;

        public const int MaxPendingRetries = 1;

        public const double SuppressSeconds = 3.0;

        public const double BarrierTimeoutSeconds = 3.0;

        // If startup fails after both clients are available, retry the barrier
        // automatically after a short cooldown instead of requiring a manual
        // resync action.
        public const double AutomaticBarrierRetryDelaySeconds = 3.0;

        // A Barrier Seek may be retried after a delivery failure or an
        // unconfirmed snapshot, but all attempts share one absolute budget.
        // This preserves the frozen Barrier target without allowing a seek
        // failure to loop forever.
        public const double BarrierSeekRetryBudgetSeconds = 15.0;

        // Waiting-state Pause failures are retried at a bounded cadence. The
        // attempt count includes the initial issue, so a continuously failing
        // session receives at most three Pause calls for one identity/ability
        // condition.
        public const double WaitingPauseRetryDelaySeconds = 3.0;

        public const int MaxWaitingPauseAttempts = 3;

    }
}
