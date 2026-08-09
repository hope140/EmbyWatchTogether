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
            Assert.Contains("wtNotifyOtherOnPlaybackStop", html);
            Assert.Contains("wtNotifyOnSyncActions", html);
            Assert.Contains("同步操作时向播放端发送文字提示", html);
            Assert.Contains("aria-live=\"polite\"", html);
            Assert.Contains("wtSaveConfig", html);
            Assert.Contains("wtPluginVersion", html);
            Assert.Contains("wtRepositoryLink", html);
            Assert.Contains("--wt-text: var(--theme-text-color, #26313a);", html);
            Assert.Contains("--wt-muted: var(--theme-secondary-text-color, #68737d);", html);
            Assert.DoesNotContain("@media (prefers-color-scheme: dark)", html);
            Assert.DoesNotContain("wtUpdateSection", html);
            Assert.DoesNotContain("wtCheckUpdate", html);
            Assert.DoesNotContain("wtAutoUpdateEnabled", html);
            Assert.DoesNotContain("wtInstallUpdate", html);
            Assert.DoesNotContain("wtSaveUpdateConfig", html);
            Assert.Contains("PauseOtherOnPlaybackStop", javascript);
            Assert.Contains("NotifyOtherOnPlaybackStop", javascript);
            Assert.Contains("NotifyOnSyncActions", javascript);
            Assert.Contains("statusReasonMessages", javascript);
            Assert.Contains("server_unavailable", javascript);
            Assert.Contains("different_video", javascript);
            Assert.Contains("remote_control_unavailable", javascript);
            Assert.Contains("_wtRoomFeedback", javascript);
            Assert.Contains("_wtRoomBusy", javascript);
            Assert.Contains("仍在房间的一方会暂停", javascript);
            Assert.Contains("只删除同步关系，不删除媒体", javascript);
            Assert.DoesNotContain("room.Error", javascript);
            Assert.Contains("getPluginConfiguration", javascript);
            Assert.Contains("updatePluginConfiguration", javascript);
            Assert.Contains("WatchTogether/Info", javascript);
            Assert.Contains("wtPluginVersion", javascript);
            Assert.DoesNotContain("loadUpdateStatus", javascript);
            Assert.DoesNotContain("saveUpdateConfiguration", javascript);
            Assert.DoesNotContain("_wtUpdateBusy", javascript);
            Assert.Contains("dataType: 'json'", javascript);

            var roomsIndex = html.IndexOf("id=\"wtRooms\"", System.StringComparison.Ordinal);
            var configIndex = html.IndexOf("id=\"wtConfigSection\"", System.StringComparison.Ordinal);
            var helpIndex = html.IndexOf("class=\"verticalSection wt-section wt-help\"", System.StringComparison.Ordinal);
            var settingsIndex = html.IndexOf("id=\"wtSettingsSection\"", System.StringComparison.Ordinal);

            Assert.True(roomsIndex >= 0);
            Assert.True(settingsIndex >= 0);
            Assert.True(configIndex > roomsIndex);
            Assert.True(helpIndex < settingsIndex);
            Assert.True(configIndex > helpIndex);
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
