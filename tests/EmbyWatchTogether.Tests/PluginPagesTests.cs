using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Drawing;
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
            Assert.Contains("Emby.Plugins.WatchTogether.Resources.watch-together-thumb.png", names);
#pragma warning disable SYSLIB0050
            var pages = ((Plugin)FormatterServices.GetUninitializedObject(typeof(Plugin))).GetPages().ToList();
#pragma warning restore SYSLIB0050
            Assert.Contains(pages, page => page.Name == "WatchTogetherDiagnostics");
            Assert.DoesNotContain(pages, page => page.Name == "WatchTogether");
            Assert.Contains(pages, page => page.Name == "WatchTogetherDiagnostics.js");
        }

        [Fact]
        public void Plugin_ExposesValidPngThumbnailAndSyncMenuIcon()
        {
#pragma warning disable SYSLIB0050
            var plugin = (Plugin)FormatterServices.GetUninitializedObject(typeof(Plugin));
#pragma warning restore SYSLIB0050
            var thumbImage = (IHasThumbImage)plugin;

            Assert.Equal(ImageFormat.Png, thumbImage.ThumbImageFormat);
            using (var stream = thumbImage.GetThumbImage())
            {
                Assert.NotNull(stream);
                Assert.True(stream.Length >= 24);
                using (var reader = new BinaryReader(stream))
                {
                    Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, reader.ReadBytes(8));
                    Assert.Equal(13, ReadBigEndianInt32(reader.ReadBytes(4)));
                    Assert.Equal(new byte[] { 73, 72, 68, 82 }, reader.ReadBytes(4));
                    Assert.Equal(1280, ReadBigEndianInt32(reader.ReadBytes(4)));
                    Assert.Equal(720, ReadBigEndianInt32(reader.ReadBytes(4)));
                }
            }

            var page = plugin.GetPages().Single(item => item.Name == "WatchTogetherDiagnostics");
            Assert.Equal("sync", page.MenuIcon);
            Assert.True(page.EnableInUserMenu);
        }

        private static int ReadBigEndianInt32(byte[] bytes)
        {
            Assert.Equal(4, bytes.Length);
            return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        }

        [Fact]
        public void ConfigurationPage_UsesEmbyPluginConfigurationApi()
        {
            var assembly = typeof(Plugin).Assembly;
            var html = ReadResource(assembly, "Emby.Plugins.WatchTogether.Configuration.watchtogether.html");
            var javascript = ReadResource(assembly, "Emby.Plugins.WatchTogether.Configuration.WatchTogether.js");

            Assert.Contains("data-bindheader=\"true\"", html);
            Assert.Contains("data-controller=\"__plugin/WatchTogetherDiagnostics.js\"", html);
            Assert.DoesNotContain("<h1>一起看</h1>", html);
            Assert.Contains("wtPauseOtherOnPlaybackStop", html);
            Assert.Contains("wtNotifyOtherOnPlaybackStop", html);
            Assert.Contains("wtNotifyOnSyncActions", html);
            Assert.Contains("同步操作时向播放端发送文字提示", html);
            Assert.Contains("wtUpdateChannel", html);
            Assert.Contains("value=\"stable\"", html);
            Assert.Contains("value=\"beta\"", html);
            Assert.Contains("beta 是预发布版", html);
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
            Assert.Contains("UpdateChannel", javascript);
            Assert.Contains("statusReasonMessages", javascript);
            Assert.Contains("server_unavailable", javascript);
            Assert.Contains("snapshot_unavailable", javascript);
            Assert.Contains("暂时无法读取播放会话，自动同步已进入保护状态；恢复后会重新对齐。", javascript);
            Assert.Contains("different_video", javascript);
            Assert.Contains("remote_control_unavailable", javascript);
            Assert.Contains("_wtRoomFeedback", javascript);
            Assert.Contains("_wtRoomBusy", javascript);
            Assert.Contains("暂离不会解除成员关系，并将尝试暂停仍在房间的一方。", javascript);
            Assert.Contains("已暂离房间，但仍在房间的一方暂停失败，请检查客户端；成员关系仍保留。", javascript);
            Assert.Contains("已暂离房间，仍在房间的一方已暂停；成员关系仍保留。", javascript);
            Assert.Contains("已暂离房间，自动同步已停止；成员关系仍保留。", javascript);
            Assert.Contains("暂离房间", javascript);
            Assert.Contains("暂离不会解除成员关系", javascript);
            Assert.Contains("结束房间会解除双方成员关系", javascript);
            Assert.DoesNotContain("仍在房间的一方会暂停", javascript);
            Assert.Contains("只删除同步关系，不删除媒体", javascript);
            Assert.Contains("会暂时暂停双方并重新对齐，确认继续吗", javascript);
            Assert.Contains("重新同步已开始，播放可能暂时暂停，请等待同步完成", javascript);
            Assert.Contains("clearAllRoomFeedback", javascript);
            Assert.Contains("_wtStatusTimer", javascript);
            Assert.DoesNotContain("正在处理此房间，请稍候", javascript);
            Assert.Contains("房间“' + roomName + '”已结束；双方成员关系已解除，媒体未删除", javascript);
            var deleteFunctionIndex = javascript.IndexOf("function deleteRoom", System.StringComparison.Ordinal);
            var deleteFeedbackIndex = javascript.IndexOf("clearRoomFeedback(page, room.RoomId)", deleteFunctionIndex, System.StringComparison.Ordinal);
            var deleteStatusIndex = javascript.IndexOf("setTransientStatus(page, ending", deleteFunctionIndex, System.StringComparison.Ordinal);
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
            Assert.Contains("setAdminVisibility", javascript);
            Assert.Contains("wtRoomsHeading", html);
            Assert.Contains(">创建房间</h2>", html);
            Assert.DoesNotContain(">1. 创建房间</h2>", html);
            Assert.DoesNotContain("'2. 房间'", javascript);
            Assert.Contains("我的房间", javascript);
            Assert.Contains("暂无参与的房间", javascript);
            Assert.Contains("请求重新同步", javascript);
            Assert.Contains("roomUserName", javascript);
            Assert.Contains("查看诊断", javascript);
            Assert.Contains("participantResync", javascript);
            Assert.Contains("请求重新同步", javascript);
            Assert.Contains("WatchTogether/Rooms/' + encodeURIComponent(roomId) + '/Resync", javascript);
            Assert.Contains("status === 'accepted'", javascript);
            Assert.Contains("status === 'busy'", javascript);
            Assert.Contains("status === 'unavailable'", javascript);
            Assert.Contains("room.CurrentUserJoined && !room.IsAdmin", javascript);
            Assert.Contains("WatchTogether/Rooms/' + encodeURIComponent(roomId) + '/Diagnostics", javascript);
            Assert.Contains("function sanitizeDiagnostic", javascript);
            Assert.Contains("SnapshotHealth", javascript);
            Assert.Contains("AckLatencySeconds", javascript);
            Assert.Contains("barrier_retry_exhausted", javascript);
            Assert.Contains("waiting_pause_retry_limit", javascript);
            Assert.Contains("reportedRemoteControl", javascript);
            Assert.Contains("session.ReportedSupportsRemoteControl", javascript);
            Assert.Contains("session.online ? (session.paused ? '已暂停' : '播放中') : '状态未知'", javascript);
            Assert.Contains("上报远控", javascript);
            Assert.Contains("raw.Pending", javascript);
            Assert.Contains("raw.Barrier", javascript);
            Assert.Contains("raw.RecoveryWindow", javascript);
            Assert.Contains("raw.LastAction", javascript);
            Assert.Contains("raw.Events", javascript);
            Assert.Contains("导出诊断 JSON", javascript);
            Assert.Contains("maxDiagnosticEvents = 50", javascript);
            Assert.Contains("slice(-maxDiagnosticEvents)", javascript);
            Assert.Contains(".reverse()", javascript);
            Assert.Contains("rememberDiagnosticPanelState", javascript);
            Assert.Contains("page._wtDiagnosticOpen", javascript);
            Assert.Contains("details.addEventListener('toggle'", javascript);
            Assert.Contains("var currentPanel = page._wtDiagnosticPanels[roomId]", javascript);
            Assert.Contains("Blob", javascript);
            Assert.Contains("createObjectURL", javascript);
            Assert.Contains("textContent", javascript);
            Assert.DoesNotContain("raw.RoomHash", javascript);
            Assert.DoesNotContain("raw.ServerHash", javascript);
            Assert.DoesNotContain("session.SessionHash", javascript);
            Assert.DoesNotContain("session.ItemHash", javascript);
            Assert.DoesNotContain("innerHTML", javascript);
            Assert.Contains("wtInvitationSection", html);
            Assert.Contains("wtInvitationName", html);
            Assert.Contains("wtInvitationAcceptCode", html);
            Assert.Contains("wtInvitationCodePanel", html);
            Assert.Contains("wtCopyInvitationCode", html);
            Assert.Contains("wtInvitations", html);
            Assert.Contains("WatchTogether/Invitations", javascript);
            Assert.Contains("WatchTogether/Invitations/' + encodeURIComponent(code) + '/Accept", javascript);
            Assert.Contains("WatchTogether/Invitations/' + encodeURIComponent(invitation.InvitationId)", javascript);
            Assert.Contains("accepted", javascript);
            Assert.Contains("invalid_or_expired", javascript);
            Assert.Contains("creator_cannot_accept", javascript);
            Assert.Contains("rate_limited", javascript);
            Assert.Contains("room_unavailable", javascript);
            Assert.Contains("复制失败，请手动选择并复制邀请码。", javascript);
            Assert.DoesNotContain("room.IsSelfService && room.CanEnd", javascript);
            Assert.Contains("else if (room.CanEnd)", javascript);
            Assert.Contains("CanEnd", javascript);
            Assert.Contains("creator_already_in_room", javascript);
            Assert.Contains("你已属于一个房间，请先结束当前房间后再创建邀请码。", javascript);
            Assert.Contains("当前邀请码数量已达上限，请稍后再试。", javascript);
            Assert.Contains("result.Created !== true", javascript);
            Assert.Contains("result.Status !== 'created'", javascript);
            Assert.DoesNotContain("邀请码创建失败：' + errorMessage(error)", javascript);
            Assert.DoesNotContain("AdminUserId", javascript);

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
