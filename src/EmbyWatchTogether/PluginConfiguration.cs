using MediaBrowser.Model.Plugins;
using System;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Plugin configuration persisted by Emby. Defaults match the current
    /// Watch Together synchronization policy.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        public bool Enabled { get; set; } = true;

        public double PollIntervalSeconds { get; set; } = 0.5;

        public bool PauseOtherOnPlaybackStop { get; set; } = true;

        public bool NotifyOtherOnPlaybackStop { get; set; } = true;

        public int MaxRuntimeDifferenceSeconds { get; set; } = 3;

        public int SeekToleranceSeconds { get; set; } = 2;

        public int BarrierSeekTimeoutSeconds { get; set; } = 10;

        public int StaleSessionTimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// Automatic checks are opt-in so a newly installed plugin never
        /// contacts GitHub until an administrator enables the feature.
        /// </summary>
        public bool AutoUpdateEnabled { get; set; } = false;

        /// <summary>
        /// Automatic check interval in hours. Plugin.UpdateConfiguration
        /// enforces the server-side 1..720 range.
        /// </summary>
        public int UpdateCheckIntervalHours { get; set; } = 24;

        /// <summary>
        /// Persisted UTC timestamp used to schedule the next background check.
        /// </summary>
        public DateTimeOffset? LastUpdateCheckAtUtc { get; set; }

        /// <summary>
        /// Emby installs plugin DLLs on the next restart. Keeping the pending
        /// version in configuration prevents duplicate installs before then.
        /// </summary>
        public string PendingUpdateVersion { get; set; }
    }
}
