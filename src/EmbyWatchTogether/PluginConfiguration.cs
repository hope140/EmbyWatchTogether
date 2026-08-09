using MediaBrowser.Model.Plugins;
using System;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Immutable, normalized options consumed by the live synchronization
    /// engine. This intentionally contains only settings that the engine can
    /// apply without rebuilding its room state.
    /// </summary>
    public sealed class SyncEngineOptions
    {
        public const double DefaultPollIntervalSeconds = 0.5;
        public const double MinPollIntervalSeconds = 0.05;
        public const double MaxPollIntervalSeconds = 60.0;

        public SyncEngineOptions(
            double pollIntervalSeconds,
            bool pauseOtherOnPlaybackStop,
            bool notifyOtherOnPlaybackStop)
            : this(pollIntervalSeconds, pauseOtherOnPlaybackStop, notifyOtherOnPlaybackStop, true)
        {
            LegacyAutomaticRetryNotifications = true;
            IsNotifyOnSyncActionsExplicit = false;
        }

        public SyncEngineOptions(
            double pollIntervalSeconds,
            bool pauseOtherOnPlaybackStop,
            bool notifyOtherOnPlaybackStop,
            bool notifyOnSyncActions)
        {
            PollIntervalSeconds = NormalizePollIntervalSeconds(pollIntervalSeconds);
            PauseOtherOnPlaybackStop = pauseOtherOnPlaybackStop;
            NotifyOtherOnPlaybackStop = notifyOtherOnPlaybackStop;
            NotifyOnSyncActions = notifyOnSyncActions;
            LegacyAutomaticRetryNotifications = false;
            IsNotifyOnSyncActionsExplicit = true;
        }

        public double PollIntervalSeconds { get; }

        public bool PauseOtherOnPlaybackStop { get; }

        public bool NotifyOtherOnPlaybackStop { get; }

        public bool NotifyOnSyncActions { get; }

        internal bool LegacyAutomaticRetryNotifications { get; }

        internal bool IsNotifyOnSyncActionsExplicit { get; }

        public static SyncEngineOptions From(PluginConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            return new SyncEngineOptions(
                configuration.PollIntervalSeconds,
                configuration.PauseOtherOnPlaybackStop,
                configuration.NotifyOtherOnPlaybackStop,
                configuration.NotifyOnSyncActions);
        }

        public static double NormalizePollIntervalSeconds(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            {
                return DefaultPollIntervalSeconds;
            }

            return Math.Min(
                MaxPollIntervalSeconds,
                Math.Max(MinPollIntervalSeconds, value));
        }
    }

    /// <summary>
    /// Raised after Emby has accepted a new plugin configuration. The event
    /// carries an immutable normalized snapshot instead of the mutable
    /// persisted configuration object.
    /// </summary>
    public sealed class PluginConfigurationChangedEventArgs : EventArgs
    {
        public PluginConfigurationChangedEventArgs(SyncEngineOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public SyncEngineOptions Options { get; }
    }

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

        public bool NotifyOnSyncActions { get; set; } = true;

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
