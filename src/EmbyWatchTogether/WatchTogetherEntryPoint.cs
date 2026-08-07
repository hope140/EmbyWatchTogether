using System;
using System.IO;
using System.Threading;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Updates;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Server entry point: resolves the server id, builds the room manager,
    /// session bridge and sync engine, then starts background coordination.
    /// </summary>
    public sealed class WatchTogetherEntryPoint : IServerEntryPoint
    {
        private readonly ISessionManager _sessionManager;
        private readonly IServerApplicationHost _applicationHost;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IHttpClient _httpClient;
        private readonly IInstallationManager _installationManager;
        private readonly ILogManager _logManager;
        private SessionBridge _bridge;
        private SyncEngine _syncEngine;
        private PluginUpdateManager _updateManager;

        public WatchTogetherEntryPoint(
            ISessionManager sessionManager,
            IServerApplicationHost applicationHost,
            IJsonSerializer jsonSerializer,
            IHttpClient httpClient = null,
            IInstallationManager installationManager = null,
            ILogManager logManager = null)
        {
            _sessionManager = sessionManager;
            _applicationHost = applicationHost;
            _jsonSerializer = jsonSerializer;
            _httpClient = httpClient;
            _installationManager = installationManager;
            _logManager = logManager;
        }

        public void Run()
        {
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                return;
            }

            plugin.ApplicationHost = _applicationHost;
            plugin.ResolveServerId();

            var bridge = new SessionBridge(_sessionManager);
            var store = new RoomStore(Path.Combine(plugin.DataFolderPath, "rooms.json"), _jsonSerializer);
            var rooms = new RoomManager(store);
            var provider = new SessionBridgeSnapshotProvider(bridge);
            var issuer = new SessionBridgeCommandIssuer(bridge);

            plugin.Store = store;
            plugin.Rooms = rooms;
            plugin.Bridge = bridge;
            plugin.Issuer = issuer;

            // Some test hosts and older Emby startup paths construct the entry
            // point without update services. Keep the existing sync engine
            // usable in that case while normal DI receives all dependencies.
            if (_httpClient != null && _installationManager != null)
            {
                var releaseClient = new GitHubReleaseClient(
                    _httpClient,
                    "EmbyWatchTogether/" + (plugin.Version?.ToString() ?? "unknown") +
                    " (+" + GitHubReleaseClient.RepositoryUrl + ")");
                _updateManager = new PluginUpdateManager(
                    plugin,
                    releaseClient,
                    _installationManager,
                    _applicationHost,
                    _logManager);
                plugin.UpdateManager = _updateManager;
                _updateManager.Start();
            }

            _bridge = bridge;
            _syncEngine = new SyncEngine(
                rooms,
                provider,
                issuer,
                plugin.ResolveServerId,
                pollIntervalSeconds: plugin.Configuration.PollIntervalSeconds,
                pauseOtherOnPlaybackStop: plugin.Configuration.PauseOtherOnPlaybackStop,
                notifyOtherOnPlaybackStop: plugin.Configuration.NotifyOtherOnPlaybackStop,
                messageIssuer: issuer);
            SubscribeToSessionChanges(bridge);
            _syncEngine.Start();
        }

        public void Dispose()
        {
            _updateManager?.Dispose();
            _updateManager = null;

            if (Plugin.Instance != null)
            {
                Plugin.Instance.UpdateManager = null;
            }

            UnsubscribeFromSessionChanges(_bridge);

            _syncEngine?.Dispose();
            _syncEngine = null;

            _bridge?.Dispose();
            _bridge = null;
        }

        private void SubscribeToSessionChanges(SessionBridge bridge)
        {
            bridge.PlaybackStart += OnPlaybackStart;
            bridge.PlaybackProgress += OnPlaybackProgress;
            bridge.PlaybackStopped += OnPlaybackStopped;
            bridge.SessionStarted += OnSessionStarted;
            bridge.SessionEnded += OnSessionEnded;
            bridge.CapabilitiesChanged += OnCapabilitiesChanged;
        }

        private void UnsubscribeFromSessionChanges(SessionBridge bridge)
        {
            if (bridge == null)
            {
                return;
            }

            bridge.PlaybackStart -= OnPlaybackStart;
            bridge.PlaybackProgress -= OnPlaybackProgress;
            bridge.PlaybackStopped -= OnPlaybackStopped;
            bridge.SessionStarted -= OnSessionStarted;
            bridge.SessionEnded -= OnSessionEnded;
            bridge.CapabilitiesChanged -= OnCapabilitiesChanged;
        }

        private void OnPlaybackStart(object sender, EventArgs e) => _syncEngine?.RequestImmediatePoll();

        private void OnPlaybackProgress(object sender, EventArgs e) => _syncEngine?.RequestImmediatePoll();

        private void OnPlaybackStopped(object sender, EventArgs e) => _syncEngine?.RequestImmediatePoll();

        private void OnSessionStarted(object sender, SessionEventArgs e) => _syncEngine?.RequestImmediatePoll();

        private void OnSessionEnded(object sender, SessionEventArgs e) => _syncEngine?.RequestImmediatePoll();

        private void OnCapabilitiesChanged(object sender, SessionEventArgs e) => _syncEngine?.RequestImmediatePoll();
    }
}
