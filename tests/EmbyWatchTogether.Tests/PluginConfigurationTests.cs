using System.Linq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class PluginConfigurationTests
    {
        [Fact]
        public void NotifyOtherOnPlaybackStop_DefaultsToEnabledAndIsIndependent()
        {
            var configuration = new PluginConfiguration();

            Assert.True(configuration.NotifyOtherOnPlaybackStop);

            configuration.PauseOtherOnPlaybackStop = false;
            Assert.True(configuration.NotifyOtherOnPlaybackStop);

            configuration.NotifyOtherOnPlaybackStop = false;
            Assert.False(configuration.NotifyOtherOnPlaybackStop);
        }

        [Fact]
        public void PollIntervalSeconds_DefaultsToHalfSecond()
        {
            var configuration = new PluginConfiguration();

            Assert.Equal(0.5, configuration.PollIntervalSeconds);
        }

        [Fact]
        public void UpdateSettings_AreNotPartOfPluginConfiguration()
        {
            var properties = typeof(PluginConfiguration).GetProperties();

            Assert.DoesNotContain(properties, p => p.Name == "AutoUpdateEnabled");
            Assert.DoesNotContain(properties, p => p.Name == "UpdateCheckIntervalHours");
            Assert.DoesNotContain(properties, p => p.Name == "LastUpdateCheckAtUtc");
        }

        [Fact]
        public void PendingUpdateVersion_RemainsInternalStateWithNullDefault()
        {
            var configuration = new PluginConfiguration();

            Assert.Null(configuration.PendingUpdateVersion);
        }
    }
}
