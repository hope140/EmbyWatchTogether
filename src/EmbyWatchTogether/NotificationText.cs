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

        private static readonly IReadOnlyDictionary<string, string> TraditionalChinese = new Dictionary<string, string>
        {
            ["一起观看"] = "一起觀看",
            ["对方已停止播放，同步已暂停"] = "對方已停止播放，同步已暫停",
            ["正在自动重新同步，请稍候"] = "正在自動重新同步，請稍候",
            ["双方打开了不同视频，已暂停同步；请打开同一视频"] = "雙方開啟了不同影片，已暫停同步；請開啟相同影片",
            ["正在等待另一端载入，准备好后会自动继续"] = "正在等待另一端載入，準備好後會自動繼續",
            ["本次跟随未完成，请在房间中手动重新同步"] = "本次跟隨未完成，請在房間中手動重新同步",
            ["已跟随切换，同步完成"] = "已跟隨切換，同步完成",
            ["同步已完成，可以继续观看"] = "同步已完成，可以繼續觀看",
            ["对方已加入房间，请打开同一视频"] = "對方已加入房間，請開啟相同影片",
            ["对方已退出房间，自动同步已停止"] = "對方已退出房間，自動同步已停止",
            ["管理员已暂停房间播放"] = "管理員已暫停房間播放",
            ["管理员已继续房间播放"] = "管理員已繼續房間播放",
            ["管理员已发起重新同步，请稍候"] = "管理員已發起重新同步，請稍候",
            ["参与者已请求重新同步，请稍候"] = "參與者已請求重新同步，請稍候"
        };

        private static readonly IReadOnlyDictionary<string, string> Japanese = new Dictionary<string, string>
        {
            ["一起观看"] = "一緒に視聴",
            ["对方已停止播放，同步已暂停"] = "相手が再生を停止したため、同期を一時停止しました。",
            ["正在自动重新同步，请稍候"] = "自動で再同期しています。しばらくお待ちください。",
            ["双方打开了不同视频，已暂停同步；请打开同一视频"] = "別々の動画が開かれているため、同期を一時停止しました。同じ動画を開いてください。",
            ["正在等待另一端载入，准备好后会自动继续"] = "相手側の読み込みを待っています。準備ができ次第、再生を再開します。",
            ["本次跟随未完成，请在房间中手动重新同步"] = "動画の切り替えに追従できませんでした。ルームから再同期してください。",
            ["已跟随切换，同步完成"] = "動画が切り替わり、同期が完了しました。",
            ["同步已完成，可以继续观看"] = "同期が完了しました。視聴を続けられます。",
            ["对方已加入房间，请打开同一视频"] = "相手がルームに参加しました。同じ動画を開いてください。",
            ["对方已退出房间，自动同步已停止"] = "相手がルームを退出したため、自動同期を停止しました。",
            ["管理员已暂停房间播放"] = "管理者がルームの再生を一時停止しました。",
            ["管理员已继续房间播放"] = "管理者がルームの再生を再開しました。",
            ["管理员已发起重新同步，请稍候"] = "管理者が再同期をリクエストしました。しばらくお待ちください。",
            ["参与者已请求重新同步，请稍候"] = "参加者が再同期をリクエストしました。しばらくお待ちください。"
        };

        private static readonly IReadOnlyDictionary<string, string> Korean = new Dictionary<string, string>
        {
            ["一起观看"] = "함께 보기",
            ["对方已停止播放，同步已暂停"] = "상대방이 재생을 중지하여 동기화를 일시 정지했습니다.",
            ["正在自动重新同步，请稍候"] = "자동으로 다시 동기화하는 중입니다. 잠시 기다려 주세요.",
            ["双方打开了不同视频，已暂停同步；请打开同一视频"] = "서로 다른 동영상이 열려 있어 동기화를 일시 정지했습니다. 같은 동영상을 열어 주세요.",
            ["正在等待另一端载入，准备好后会自动继续"] = "상대방의 동영상이 로드되기를 기다리는 중입니다. 준비되면 재생을 계속합니다.",
            ["本次跟随未完成，请在房间中手动重新同步"] = "동영상 전환을 따라가지 못했습니다. 방에서 다시 동기화해 주세요.",
            ["已跟随切换，同步完成"] = "동영상 전환과 동기화를 완료했습니다.",
            ["同步已完成，可以继续观看"] = "동기화가 완료되었습니다. 시청을 계속할 수 있습니다.",
            ["对方已加入房间，请打开同一视频"] = "상대방이 방에 참가했습니다. 같은 동영상을 열어 주세요.",
            ["对方已退出房间，自动同步已停止"] = "상대방이 방에서 나가 자동 동기화가 중지되었습니다.",
            ["管理员已暂停房间播放"] = "관리자가 방의 재생을 일시 정지했습니다.",
            ["管理员已继续房间播放"] = "관리자가 방의 재생을 계속했습니다.",
            ["管理员已发起重新同步，请稍候"] = "관리자가 다시 동기화를 요청했습니다. 잠시 기다려 주세요.",
            ["参与者已请求重新同步，请稍候"] = "참가자가 다시 동기화를 요청했습니다. 잠시 기다려 주세요."
        };

        private static readonly IReadOnlyDictionary<string, string> Russian = new Dictionary<string, string>
        {
            ["一起观看"] = "Совместный просмотр",
            ["对方已停止播放，同步已暂停"] = "Другой участник остановил воспроизведение. Синхронизация приостановлена.",
            ["正在自动重新同步，请稍候"] = "Идёт автоматическая синхронизация. Подождите немного.",
            ["双方打开了不同视频，已暂停同步；请打开同一视频"] = "Открыты разные видео. Синхронизация приостановлена. Откройте одно и то же видео.",
            ["正在等待另一端载入，准备好后会自动继续"] = "Ожидаем загрузки у другого участника. Воспроизведение продолжится, когда видео будет готово.",
            ["本次跟随未完成，请在房间中手动重新同步"] = "Не удалось завершить переход на другое видео. Запустите повторную синхронизацию в комнате.",
            ["已跟随切换，同步完成"] = "Переход на другое видео выполнен. Синхронизация завершена.",
            ["同步已完成，可以继续观看"] = "Синхронизация завершена. Можно продолжить просмотр.",
            ["对方已加入房间，请打开同一视频"] = "Другой участник вошёл в комнату. Откройте одно и то же видео.",
            ["对方已退出房间，自动同步已停止"] = "Другой участник вышел из комнаты. Автоматическая синхронизация остановлена.",
            ["管理员已暂停房间播放"] = "Администратор приостановил воспроизведение в комнате.",
            ["管理员已继续房间播放"] = "Администратор возобновил воспроизведение в комнате.",
            ["管理员已发起重新同步，请稍候"] = "Администратор запросил повторную синхронизацию. Подождите немного.",
            ["参与者已请求重新同步，请稍候"] = "Участник запросил повторную синхронизацию. Подождите немного."
        };

        private static readonly IReadOnlyDictionary<string, string> French = new Dictionary<string, string>
        {
            ["一起观看"] = "Regarder ensemble",
            ["对方已停止播放，同步已暂停"] = "L’autre participant a arrêté la lecture. La synchronisation est suspendue.",
            ["正在自动重新同步，请稍候"] = "Resynchronisation automatique en cours. Patientez un instant.",
            ["双方打开了不同视频，已暂停同步；请打开同一视频"] = "Les participants ont ouvert des vidéos différentes. La synchronisation est suspendue. Ouvrez la même vidéo.",
            ["正在等待另一端载入，准备好后会自动继续"] = "En attente du chargement chez l’autre participant. La lecture reprendra dès que possible.",
            ["本次跟随未完成，请在房间中手动重新同步"] = "Le changement de vidéo n’a pas abouti. Relancez la synchronisation depuis le salon.",
            ["已跟随切换，同步完成"] = "Le changement de vidéo a été suivi. La synchronisation est terminée.",
            ["同步已完成，可以继续观看"] = "La synchronisation est terminée. Vous pouvez reprendre le visionnage.",
            ["对方已加入房间，请打开同一视频"] = "L’autre participant a rejoint le salon. Ouvrez la même vidéo.",
            ["对方已退出房间，自动同步已停止"] = "L’autre participant a quitté le salon. La synchronisation automatique est arrêtée.",
            ["管理员已暂停房间播放"] = "Un administrateur a mis la lecture du salon en pause.",
            ["管理员已继续房间播放"] = "Un administrateur a repris la lecture du salon.",
            ["管理员已发起重新同步，请稍候"] = "Un administrateur a demandé une resynchronisation. Patientez un instant.",
            ["参与者已请求重新同步，请稍候"] = "Un participant a demandé une resynchronisation. Patientez un instant."
        };

        private static readonly IReadOnlyDictionary<string, string> German = new Dictionary<string, string>
        {
            ["一起观看"] = "Gemeinsam schauen",
            ["对方已停止播放，同步已暂停"] = "Die andere Person hat die Wiedergabe gestoppt. Die Synchronisierung ist unterbrochen.",
            ["正在自动重新同步，请稍候"] = "Die Wiedergabe wird automatisch neu synchronisiert. Bitte kurz warten.",
            ["双方打开了不同视频，已暂停同步；请打开同一视频"] = "Es wurden verschiedene Videos geöffnet. Die Synchronisierung ist unterbrochen. Öffnet dasselbe Video.",
            ["正在等待另一端载入，准备好后会自动继续"] = "Das Video der anderen Person wird noch geladen. Sobald es bereit ist, geht die Wiedergabe weiter.",
            ["本次跟随未完成，请在房间中手动重新同步"] = "Der Videowechsel konnte nicht abgeschlossen werden. Startet die Synchronisierung im Raum erneut.",
            ["已跟随切换，同步完成"] = "Der Videowechsel wurde übernommen. Die Synchronisierung ist abgeschlossen.",
            ["同步已完成，可以继续观看"] = "Die Synchronisierung ist abgeschlossen. Ihr könnt weiterschauen.",
            ["对方已加入房间，请打开同一视频"] = "Die andere Person ist dem Raum beigetreten. Öffnet dasselbe Video.",
            ["对方已退出房间，自动同步已停止"] = "Die andere Person hat den Raum verlassen. Die automatische Synchronisierung wurde beendet.",
            ["管理员已暂停房间播放"] = "Die Administration hat die Wiedergabe im Raum pausiert.",
            ["管理员已继续房间播放"] = "Die Administration hat die Wiedergabe im Raum fortgesetzt.",
            ["管理员已发起重新同步，请稍候"] = "Die Administration hat eine erneute Synchronisierung angefordert. Bitte kurz warten.",
            ["参与者已请求重新同步，请稍候"] = "Ein Teilnehmer hat eine erneute Synchronisierung angefordert. Bitte kurz warten."
        };

        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Translations =
            new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["en"] = English,
                ["zh-Hant"] = TraditionalChinese,
                ["ja"] = Japanese,
                ["ko"] = Korean,
                ["ru"] = Russian,
                ["fr"] = French,
                ["de"] = German,
            };

        internal static bool IsLanguage(string language) =>
            language == "zh" || (language != null && Translations.ContainsKey(language));

        public static string LanguageFor(string userId, Func<string, string> languageForUser)
        {
            try { var language = languageForUser?.Invoke(userId); return IsLanguage(language) ? language : "zh"; }
            catch { return "zh"; }
        }

        public static string Translate(string text, string userId, Func<string, string> languageForUser)
        {
            try
            {
                var language = languageForUser?.Invoke(userId);
                if (text != null && language != null && Translations.TryGetValue(language, out var dictionary) &&
                    dictionary.TryGetValue(text, out var translated)) return translated;
            }
            catch
            {
                // Advisory notices must remain independent of preferences and playback.
            }
            return text;
        }
    }
}
