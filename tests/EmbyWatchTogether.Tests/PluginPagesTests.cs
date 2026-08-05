using System.IO;
using System.Linq;
using System.Reflection;
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

        [Fact]
        public void ConfigurationPage_UsesEmbyPluginConfigurationApi()
        {
            var assembly = typeof(Plugin).Assembly;
            var html = ReadResource(assembly, "Emby.Plugins.WatchTogether.Configuration.watchtogether.html");
            var javascript = ReadResource(assembly, "Emby.Plugins.WatchTogether.Configuration.WatchTogether.js");

            Assert.Contains("wtPauseOtherOnPlaybackStop", html);
            Assert.Contains("wtSaveConfig", html);
            Assert.Contains("PauseOtherOnPlaybackStop", javascript);
            Assert.Contains("getPluginConfiguration", javascript);
            Assert.Contains("updatePluginConfiguration", javascript);
        }

        private static string ReadResource(Assembly assembly, string resourceName)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
