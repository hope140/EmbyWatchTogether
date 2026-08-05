using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class PluginConfigurationTests
    {
        [Fact]
        public void PollIntervalSeconds_DefaultsToHalfSecond()
        {
            var configuration = new PluginConfiguration();

            Assert.Equal(0.5, configuration.PollIntervalSeconds);
        }
    }
}
