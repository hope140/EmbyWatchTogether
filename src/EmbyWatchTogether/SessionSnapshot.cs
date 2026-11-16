using System;
using MediaBrowser.Controller.Session;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Frozen scalar observations of one Emby session used by room coordination.
    /// A received Stop can revoke a snapshot's playback without changing its scalars.
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
            SessionCapabilityReport capabilities,
            DateTimeOffset lastActivityDateUtc = default)
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
            LastActivityDateUtc = lastActivityDateUtc;
            WasOnlineWhenObserved = Online;
        }

        public string SessionId { get; }

        public string UserId { get; }

        public string ItemId { get; }

        public string MediaSourceId { get; }

        public long PositionTicks { get; }

        public long RunTimeTicks { get; }

        public bool IsPaused { get; }

        public double PlaybackRate { get; }

        private bool _stopped;
        // Scalar observations remain frozen. A received Stop may revoke their use.
        internal PlaybackTermination Termination { get; set; }
        internal bool PlaybackTerminated => Termination?.IsStopped ?? false;
        public bool Stopped { get => _stopped || PlaybackTerminated; private set => _stopped = value; }

        public bool SupportsRemoteControl { get; }

        public SessionCapabilityReport Capabilities { get; }

        public DateTimeOffset LastActivityDateUtc { get; }

        public bool Online => !string.IsNullOrEmpty(SessionId) && !Stopped;

        // Historical evidence only; never use this to authorize a command.
        // Revoking the playback must not erase a previously observed item.
        internal bool WasOnlineWhenObserved { get; private set; }
        internal bool HasExplicitPlaybackStart => Termination == null || Termination.HasStarted;

        public double PositionSeconds => PositionTicks / (double)TicksPerSecond;

        public static SessionSnapshot FromSessionInfo(SessionInfo session, string userId = null)
            => FromSessionInfo(session, userId, null);

        internal static SessionSnapshot FromSessionInfo(SessionInfo session, string userId, StoppedPlaybackFilter filter)
        {
            if (session == null)
            {
                return null;
            }

            var playState = session.PlayState;
            var termination = filter?.Bind(session, playState);
            var item = session.NowPlayingItem;
            if (!ReferenceEquals(playState, session.PlayState)) return null;

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
            if (!ReferenceEquals(playState, session.PlayState)) return null;

            var snapshot = new SessionSnapshot(
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
                capabilities,
                session.LastActivityDate);
            snapshot.Termination = termination;
            snapshot.WasOnlineWhenObserved = snapshot.Online;
            return snapshot;
        }
    }
}
