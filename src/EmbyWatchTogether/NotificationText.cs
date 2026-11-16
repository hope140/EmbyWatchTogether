using System;
using System.Collections.Generic;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>Fixed system notices only. Never pass user-authored messages here.</summary>
    internal static class NotificationText
    {
        private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
        {
            ["一起观看"] = "Watch Together",
            ["对方已停止播放，同步已暂停"] = "The other participant stopped playback. Synchronization is paused.",
            ["正在自动重新同步，请稍候"] = "Automatically resynchronizing. Please wait.",
            ["双方打开了不同视频，已暂停同步；请打开同一视频"] = "Different videos are open. Synchronization is paused; please open the same video.",
            ["正在等待另一端载入，准备好后会自动继续"] = "Waiting for the other participant to load. Playback will resume when ready.",
            ["本次跟随未完成，请在房间中手动重新同步"] = "Following the media change did not complete. Please resync from the room.",
            ["已跟随切换，同步完成"] = "Media change followed. Synchronization complete.",
            ["同步已完成，可以继续观看"] = "Synchronization complete. You can continue watching.",
            ["对方已加入房间，请打开同一视频"] = "The other participant joined the room. Please open the same video.",
            ["对方已退出房间，自动同步已停止"] = "The other participant left the room. Automatic synchronization has stopped.",
            ["管理员已暂停房间播放"] = "An administrator paused room playback.",
            ["管理员已继续房间播放"] = "An administrator resumed room playback.",
            ["管理员已发起重新同步，请稍候"] = "An administrator requested resynchronization. Please wait.",
            ["参与者已请求重新同步，请稍候"] = "A participant requested resynchronization. Please wait."
        };

        public static string LanguageFor(string userId, Func<string, string> languageForUser)
        {
            try { return languageForUser?.Invoke(userId) == "en" ? "en" : "zh"; }
            catch { return "zh"; }
        }

        public static string Translate(string text, string userId, Func<string, string> languageForUser)
        {
            try
            {
                if (languageForUser != null && languageForUser(userId) == "en" &&
                    text != null && English.TryGetValue(text, out var translated)) return translated;
            }
            catch
            {
                // Advisory notices must remain independent of preferences and playback.
            }
            return text;
        }
    }
}
