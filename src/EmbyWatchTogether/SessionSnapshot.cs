using System;
using MediaBrowser.Controller.Session;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Immutable view of one Emby session used by room coordination. Field set
    /// mirrors the Python reference snapshot (watch_together_coordinator.py).
    /// </summary>
    public sealed class SessionSnapshot
    {
        public const long TicksPerSecond = 10_000_000L;

        public SessionSnapshot(
            string sessionId,
            string userId,
            string itemId,
            string mediaSourceId,
            long positionTicks,
            long runTimeTicks,
            bool isPaused,
            double playbackRate,
            bool stopped,
            bool supportsRemoteControl,
            SessionCapabilityReport capabilities)
        {
            SessionId = sessionId ?? string.Empty;
            UserId = userId ?? string.Empty;
            ItemId = itemId ?? string.Empty;
            MediaSourceId = mediaSourceId ?? string.Empty;
            PositionTicks = Math.Max(0, positionTicks);
            RunTimeTicks = Math.Max(0, runTimeTicks);
            IsPaused = isPaused;
            PlaybackRate = playbackRate > 0 ? playbackRate : 1.0;
            Stopped = stopped;
            Capabilities = capabilities ?? new SessionCapabilityReport(false, Array.Empty<string>());
            SupportsRemoteControl = supportsRemoteControl;
        }

        public string SessionId { get; }

        public string UserId { get; }

        public string ItemId { get; }

        public string MediaSourceId { get; }

        public long PositionTicks { get; }

        public long RunTimeTicks { get; }

        public bool IsPaused { get; }

        public double PlaybackRate { get; }

        public bool Stopped { get; }

        public bool SupportsRemoteControl { get; }

        public SessionCapabilityReport Capabilities { get; }

        public bool Online => !string.IsNullOrEmpty(SessionId) && !Stopped;

        public double PositionSeconds => PositionTicks / (double)TicksPerSecond;

        public static SessionSnapshot FromSessionInfo(SessionInfo session, string userId = null)
        {
            if (session == null)
            {
                return null;
            }

            var playState = session.PlayState;
            var item = session.NowPlayingItem;

            string itemId = string.Empty;
            long? runTimeTicks = null;
            if (item != null)
            {
                itemId = item.Id ?? string.Empty;
                runTimeTicks = item.RunTimeTicks;
            }

            // The C# PlayerStateInfo has no IsStopped flag; a missing
            // NowPlayingItem is the server-side equivalent of stopped.
            bool stopped = string.IsNullOrEmpty(itemId);

            double rate = playState?.PlaybackRate ?? 0;
            long position = playState?.PositionTicks ?? 0;
            bool isPaused = playState?.IsPaused ?? false;
            string mediaSourceId = playState?.MediaSourceId ?? string.Empty;

            var capabilities = CapabilityProbe.Probe(session);

            return new SessionSnapshot(
                session.Id,
                userId ?? session.UserId,
                itemId,
                mediaSourceId,
                position,
                runTimeTicks ?? 0,
                isPaused,
                rate,
                stopped,
                session.SupportsRemoteControl,
                capabilities);
        }
    }
}
