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

        public const double PlaybackRateTolerance = 0.010000001;

        public const double PendingTimeoutSeconds = 3.0;

        public const double SuppressSeconds = 3.0;

        public const double BarrierTimeoutSeconds = 3.0;

        public const int DriftRoundsBeforeSeek = 2;
    }
}
