using MediaBrowser.Model.Plugins;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Plugin configuration persisted by Emby. Values are the porting defaults
    /// taken from the Python reference implementation (see reference/README.md).
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
    }
}
