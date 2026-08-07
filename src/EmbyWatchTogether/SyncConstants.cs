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

        // Startup alignment is stricter than the general seek acknowledgement
        // tolerance so a short restore-order skew is corrected before Watching.
        public const long StartupAlignToleranceTicks = 1 * TicksPerSecond;

        public const long DriftThresholdTicks = 5 * TicksPerSecond;

        // After a manual seek is propagated, the peer may load faster and run
        // ahead while the seeker is still buffering at the target. Instead of
        // repeatedly pulling the peer back or letting it run away, wait for the
        // seeker: pause the peer once it leads by RealignWaitLead, then resume
        // when the seeker actually starts playing (moved RealignResume past the
        // anchor). A position within RealignStuck of the anchor means the seeker
        // has not finished loading yet.
        public const long RealignWaitLeadTicks = 3 * TicksPerSecond;

        public const long RealignResumeTicks = 2 * TicksPerSecond;

        public const long RealignStuckTicks = 2 * TicksPerSecond;

        // Advisory only: keep waiting after this, but tell both sides the seeker
        // has been stuck for a long time.
        public const double RealignTimeoutSeconds = 60.0;

        // If the user manually resumes the paused peer while the seeker is still
        // stuck, re-pause it (when leading again) after this cooldown instead of
        // spamming pause commands every poll.
        public const double RealignRepauseIntervalSeconds = 5.0;

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

    }
}
