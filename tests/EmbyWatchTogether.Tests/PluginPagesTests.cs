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

            Assert.Contains("data-bindheader=\"true\"", html);
            Assert.Contains("wtPauseOtherOnPlaybackStop", html);
            Assert.Contains("wtNotifyOtherOnPlaybackStop", html);
            Assert.Contains("wtNotifyOnSyncActions", html);
            Assert.Contains("同步操作时向播放端发送文字提示", html);
            Assert.Contains("aria-live=\"polite\"", html);
            Assert.Contains("wtSaveConfig", html);
            Assert.Contains("wtPluginVersion", html);
            Assert.Contains("wtRepositoryLink", html);
            Assert.Contains("--wt-text: var(--theme-text-color, hsla(var(--theme-text-color-hue, 204), var(--theme-text-color-saturation, 20%), var(--theme-text-color-lightness, 20%), var(--theme-text-color-alpha, 1)));", html);
            Assert.Contains("--theme-secondary-text-color-alpha", html);
            Assert.Contains("--theme-primary-color-hue", html);
            Assert.Contains("--card-background-lightness", html);
            Assert.Contains("--wt-card: hsla(var(--card-background-hue", html);
            Assert.Contains("--card-background-alpha, 1", html);
            Assert.DoesNotContain("--wt-card: var(--theme-background", html);
            Assert.Contains("--button-background-lightness", html);
            Assert.Contains("--line-background", html);
            Assert.Contains(".wt-page input::placeholder", html);
            Assert.Contains("color: var(--wt-muted) !important;", html);
            Assert.Contains("background: var(--wt-card);", html);
            Assert.Contains("background: var(--wt-button) !important;", html);
            Assert.Contains("background: var(--wt-hover) !important;", html);
            Assert.DoesNotContain("background: #f1f4f6 !important;", html);
            Assert.DoesNotContain("background: #fff0f1 !important;", html);
            Assert.DoesNotContain("background: var(--theme-background-color, #fff)", html);
            Assert.DoesNotContain("background: #fff !important;", html);
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
            Assert.Contains("snapshot_unavailable", javascript);
            Assert.Contains("暂时无法读取播放会话，自动同步已进入保护状态；恢复后会重新对齐。", javascript);
            Assert.Contains("different_video", javascript);
            Assert.Contains("remote_control_unavailable", javascript);
            Assert.Contains("_wtRoomFeedback", javascript);
            Assert.Contains("_wtRoomBusy", javascript);
            Assert.Contains("将尝试暂停仍在房间的一方", javascript);
            Assert.Contains("已退出房间，但仍在房间的一方暂停失败，请检查客户端。", javascript);
            Assert.Contains("已退出房间，仍在房间的一方已暂停。", javascript);
            Assert.Contains("已退出房间，自动同步已停止。", javascript);
            Assert.DoesNotContain("仍在房间的一方会暂停", javascript);
            Assert.Contains("只删除同步关系，不删除媒体", javascript);
            Assert.Contains("会暂时暂停双方并重新对齐，确认继续吗", javascript);
            Assert.Contains("重新同步已开始，播放可能暂时暂停，请等待同步完成", javascript);
            Assert.Contains("clearAllRoomFeedback", javascript);
            Assert.Contains("_wtStatusTimer", javascript);
            Assert.DoesNotContain("正在处理此房间，请稍候", javascript);
            Assert.Contains("房间“' + roomName + '”已删除；只移除同步关系，媒体未删除", javascript);
            var deleteFunctionIndex = javascript.IndexOf("function deleteRoom", System.StringComparison.Ordinal);
            var deleteFeedbackIndex = javascript.IndexOf("clearRoomFeedback(page, room.RoomId)", deleteFunctionIndex, System.StringComparison.Ordinal);
            var deleteStatusIndex = javascript.IndexOf("setTransientStatus(page, '房间“'", deleteFunctionIndex, System.StringComparison.Ordinal);
            Assert.True(deleteFeedbackIndex >= 0 && deleteStatusIndex > deleteFeedbackIndex);
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
