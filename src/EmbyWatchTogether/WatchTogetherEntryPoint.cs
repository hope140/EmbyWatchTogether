using System;
using System.IO;
using System.Threading;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
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
        private SessionBridge _bridge;
        private SyncEngine _syncEngine;

        public WatchTogetherEntryPoint(
            ISessionManager sessionManager,
            IServerApplicationHost applicationHost,
            IJsonSerializer jsonSerializer)
        {
            _sessionManager = sessionManager;
            _applicationHost = applicationHost;
            _jsonSerializer = jsonSerializer;
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
