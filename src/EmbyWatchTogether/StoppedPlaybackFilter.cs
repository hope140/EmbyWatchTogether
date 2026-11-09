using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;

namespace Emby.Plugins.WatchTogether
{
    // Values are weakly owned by the actual server SessionInfo/PlayerStateInfo.
    // A Stop is evidence about one playback, never about every item on a device.
    internal sealed class PlaybackTermination
    {
        private int _stopped;
        internal bool IsStopped => Volatile.Read(ref _stopped) != 0;
        internal void Stop() => Interlocked.Exchange(ref _stopped, 1);
    }

    internal sealed class StoppedPlaybackFilter
    {
        private sealed class SessionStops
        {
            internal readonly object Gate = new object();
            internal readonly HashSet<string> Ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal readonly Dictionary<string, WeakReference<PlayerStateInfo>> Observed =
                new Dictionary<string, WeakReference<PlayerStateInfo>>(StringComparer.OrdinalIgnoreCase);
            internal readonly Dictionary<string, WeakReference<PlayerStateInfo>> Starts =
                new Dictionary<string, WeakReference<PlayerStateInfo>>(StringComparer.OrdinalIgnoreCase);
            internal readonly ConditionalWeakTable<PlayerStateInfo, PlaybackTermination> Samples =
                new ConditionalWeakTable<PlayerStateInfo, PlaybackTermination>();
        }

        private readonly ConditionalWeakTable<SessionInfo, SessionStops> _sessions =
            new ConditionalWeakTable<SessionInfo, SessionStops>();

        private static string EventId(PlaybackProgressEventArgs e)
        {
            if (e?.Session == null || string.IsNullOrEmpty(e.Session.Id)) return null;
            var id = e.PlaySessionId;
            if (string.IsNullOrEmpty(id)) id = e.PlaySession?.Id;
            if (string.IsNullOrEmpty(id) || (e.PlaySession != null &&
                !string.Equals(id, e.PlaySession.Id, StringComparison.OrdinalIgnoreCase))) return null;
            return id;
        }

        internal void Stopped(PlaybackStopEventArgs e)
        {
            var id = EventId(e);
            if (id == null) return; // No speculative item/session-wide tombstone.
            var state = _sessions.GetValue(e.Session, _ => new SessionStops());
            lock (state.Gate)
            {
                var playback = e.PlaySession ?? e.Session.GetPlaySessionInfo(id);
                var sample = playback?.PlayState;
                if (sample == null && state.Observed.TryGetValue(id, out var observed)) observed.TryGetTarget(out sample);
                if (sample != null) state.Samples.GetValue(sample, _ => new PlaybackTermination()).Stop();
                // A distinguishable delayed Stop must not poison a newer explicit Start,
                // even when that client reuses its PlaySessionId.
                if (sample != null && state.Starts.TryGetValue(id, out var started) &&
                    started.TryGetTarget(out var current) && !ReferenceEquals(current, sample) &&
                    ReferenceEquals(e.Session.GetPlaySessionInfo(id)?.PlayState, current)) return;
                state.Ids.Add(id);
                state.Observed.Remove(id);
            }
        }

        internal void Started(PlaybackProgressEventArgs e)
        {
            var id = EventId(e);
            if (id == null) return;
            var playback = e.PlaySession ?? e.Session.GetPlaySessionInfo(id);
            var state = _sessions.GetValue(e.Session, _ => new SessionStops());
            lock (state.Gate)
            {
                if (playback?.PlayState == null || !ReferenceEquals(e.Session.GetPlaySessionInfo(id), playback)) return;
                // An explicit server Start is the only way to reuse a terminated ID.
                // Keep previously captured markers terminal; only new snapshots rebind.
                state.Ids.Remove(id);
                if (state.Samples.TryGetValue(playback.PlayState, out var marker) && marker.IsStopped)
                    state.Samples.Remove(playback.PlayState);
                state.Starts[id] = new WeakReference<PlayerStateInfo>(playback.PlayState);
                state.Observed[id] = new WeakReference<PlayerStateInfo>(playback.PlayState);
            }
        }

        internal void Progress(PlaybackProgressEventArgs e)
        {
            var id = EventId(e);
            if (id == null) return;
            var playback = e.PlaySession ?? e.Session.GetPlaySessionInfo(id);
            if (playback?.PlayState == null) return;
            var state = _sessions.GetValue(e.Session, _ => new SessionStops());
            lock (state.Gate)
            {
                if (!state.Ids.Contains(id)) state.Observed[id] = new WeakReference<PlayerStateInfo>(playback.PlayState);
            }
        }

        internal PlaybackTermination Bind(SessionInfo session, PlayerStateInfo sample)
        {
            if (sample == null) return null;
            var state = _sessions.GetValue(session, _ => new SessionStops());
            lock (state.Gate)
            {
                var marker = state.Samples.GetValue(sample, _ => new PlaybackTermination());
                // Emby may recreate PlaySessionInfo for late progress after removing it
                // on Stop. Object identity alone would miss that new object with old ID.
                foreach (var id in state.Ids)
                {
                    if (ReferenceEquals(session.GetPlaySessionInfo(id)?.PlayState, sample))
                    {
                        marker.Stop();
                        break;
                    }
                }
                return marker;
            }
        }
    }
}
