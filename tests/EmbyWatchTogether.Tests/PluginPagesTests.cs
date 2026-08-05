using System.Linq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class PluginPagesTests
    {
        [Fact]
        public void PluginAssembly_EmbedsWatchTogetherPageAndController()
        {
            var names = typeof(Plugin).Assembly.GetManifestResourceNames();

            Assert.Contains("Emby.Plugins.WatchTogether.Configuration.watchtogether.html", names);
            Assert.Contains("Emby.Plugins.WatchTogether.Configuration.WatchTogether.js", names);
        }
    }
}
