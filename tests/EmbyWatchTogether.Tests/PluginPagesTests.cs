using System.Linq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class PluginPagesTests
    {
        [Fact]
        public void PluginAssembly_EmbedsWatchTogetherPage()
        {
            var resource = "Emby.Plugins.WatchTogether.Configuration.watchtogether.html";

            var names = typeof(Plugin).Assembly.GetManifestResourceNames();

            Assert.Contains(resource, names);
        }
    }
}
