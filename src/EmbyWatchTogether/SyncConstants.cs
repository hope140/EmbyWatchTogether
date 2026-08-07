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

        // External players often report a small position rewind (a few seconds)
        // shortly after a seek lands while re-basing their clock. Only backward
        // jumps beyond this are treated as user seeks.
        public const long ManualSeekBackwardToleranceTicks = 15 * TicksPerSecond;

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
