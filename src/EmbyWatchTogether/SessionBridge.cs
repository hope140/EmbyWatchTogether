using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Server-side bridge over ISessionManager: issues remote-control commands,
    /// forwards playback/session events, and exposes session lookup. Semantics
    /// ported from the Python reference emby_session_api.py / coordinator.
    /// </summary>
    public sealed class SessionBridge : IDisposable
    {
        private readonly ISessionManager _sessionManager;
        private bool _disposed;

        public SessionBridge(ISessionManager sessionManager)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));

            _sessionManager.PlaybackStart += OnPlaybackStart;
            _sessionManager.PlaybackProgress += OnPlaybackProgress;
            _sessionManager.PlaybackStopped += OnPlaybackStopped;
            _sessionManager.SessionStarted += OnSessionStarted;
            _sessionManager.SessionEnded += OnSessionEnded;
            _sessionManager.CapabilitiesChanged += OnCapabilitiesChanged;
        }

        public event EventHandler<PlaybackProgressEventArgs> PlaybackStart;

        public event EventHandler<PlaybackProgressEventArgs> PlaybackProgress;

        public event EventHandler<PlaybackStopEventArgs> PlaybackStopped;

        public event EventHandler<SessionEventArgs> SessionStarted;

        public event EventHandler<SessionEventArgs> SessionEnded;

        public event EventHandler<SessionEventArgs> CapabilitiesChanged;

        public IReadOnlyList<SessionInfo> GetSessions()
        {
            return (_sessionManager.Sessions ?? Enumerable.Empty<SessionInfo>()).ToList();
        }

        public SessionInfo FindSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return null;
            }

            return GetSessions().FirstOrDefault(s =>
                string.Equals(s.Id, sessionId, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<SessionInfo> FindSessionsForUsers(IEnumerable<string> userIds)
        {
            if (userIds == null)
            {
                return Array.Empty<SessionInfo>();
            }

            var wanted = new HashSet<string>(userIds, StringComparer.OrdinalIgnoreCase);
            return GetSessions()
                .Where(s => s.UserId != null && wanted.Contains(s.UserId))
                .ToList();
        }

        public Task SendPauseAsync(string controllingUserId, string sessionId, CancellationToken cancellationToken = default)
        {
            return SendPlaystateAsync(controllingUserId, sessionId, PlaystateRequestFactory.Pause(controllingUserId), cancellationToken);
        }

        public Task SendUnpauseAsync(string controllingUserId, string sessionId, CancellationToken cancellationToken = default)
        {
            return SendPlaystateAsync(controllingUserId, sessionId, PlaystateRequestFactory.Unpause(controllingUserId), cancellationToken);
        }

        public Task SendPlayPauseAsync(string controllingUserId, string sessionId, CancellationToken cancellationToken = default)
        {
            return SendPlaystateAsync(controllingUserId, sessionId, PlaystateRequestFactory.PlayPause(controllingUserId), cancellationToken);
        }

        public Task SendStopAsync(string controllingUserId, string sessionId, CancellationToken cancellationToken = default)
        {
            return SendPlaystateAsync(controllingUserId, sessionId, PlaystateRequestFactory.Stop(controllingUserId), cancellationToken);
        }

        public Task SendSeekAsync(string controllingUserId, string sessionId, long positionTicks, CancellationToken cancellationToken = default)
        {
            return SendPlaystateAsync(controllingUserId, sessionId, PlaystateRequestFactory.Seek(controllingUserId, positionTicks), cancellationToken);
        }

        public Task SendDisplayMessageAsync(
            string controllingUserId,
            string sessionId,
            string header,
            string text,
            int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            if (sessionId == null)
            {
                throw new ArgumentNullException(nameof(sessionId));
            }

            var command = MessageCommandFactory.DisplayMessage(header, text, timeoutMs);
            return _sessionManager.SendMessageCommand(
                controllingSessionId: string.Empty,
                sessionId: sessionId,
                command: command,
                cancellationToken: cancellationToken);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _sessionManager.PlaybackStart -= OnPlaybackStart;
            _sessionManager.PlaybackProgress -= OnPlaybackProgress;
            _sessionManager.PlaybackStopped -= OnPlaybackStopped;
            _sessionManager.SessionStarted -= OnSessionStarted;
            _sessionManager.SessionEnded -= OnSessionEnded;
            _sessionManager.CapabilitiesChanged -= OnCapabilitiesChanged;
        }

        private async Task SendPlaystateAsync(
            string controllingUserId,
            string sessionId,
            PlaystateRequest request,
            CancellationToken cancellationToken)
        {
            if (sessionId == null)
            {
                throw new ArgumentNullException(nameof(sessionId));
            }

            await _sessionManager.SendPlaystateCommand(
                controllingSessionId: string.Empty,
                sessionId: sessionId,
                command: request,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        private void OnPlaybackStart(object sender, PlaybackProgressEventArgs e) => PlaybackStart?.Invoke(sender, e);

        private void OnPlaybackProgress(object sender, PlaybackProgressEventArgs e) => PlaybackProgress?.Invoke(sender, e);

        private void OnPlaybackStopped(object sender, PlaybackStopEventArgs e) => PlaybackStopped?.Invoke(sender, e);

        private void OnSessionStarted(object sender, SessionEventArgs e) => SessionStarted?.Invoke(sender, e);

        private void OnSessionEnded(object sender, SessionEventArgs e) => SessionEnded?.Invoke(sender, e);

        private void OnCapabilitiesChanged(object sender, SessionEventArgs e) => CapabilitiesChanged?.Invoke(sender, e);
    }
}
