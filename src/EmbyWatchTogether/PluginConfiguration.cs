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
        /// Internal state, not a user-facing setting: Emby installs plugin
        /// DLLs on the next restart, and keeping the pending version here
        /// prevents the scheduled task from installing the same release again
        /// before that restart happens.
        /// </summary>
        public string PendingUpdateVersion { get; set; }
    }
}
