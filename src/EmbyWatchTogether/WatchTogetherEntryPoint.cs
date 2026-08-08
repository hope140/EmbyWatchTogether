using System;
using System.IO;
using System.Threading;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
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
        private readonly ILogManager _logManager;
        private Plugin _plugin;
        private SessionBridge _bridge;
        private SyncEngine _syncEngine;

        public WatchTogetherEntryPoint(
            ISessionManager sessionManager,
            IServerApplicationHost applicationHost,
            IJsonSerializer jsonSerializer,
            ILogManager logManager = null)
        {
            _sessionManager = sessionManager;
            _applicationHost = applicationHost;
            _jsonSerializer = jsonSerializer;
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
            var options = SyncEngineOptions.From(plugin.Configuration);

            plugin.Store = store;
            plugin.Rooms = rooms;
            plugin.Bridge = bridge;
            plugin.Issuer = issuer;

            _bridge = bridge;
            _plugin = plugin;
            _syncEngine = new SyncEngine(
                rooms,
                provider,
                issuer,
                plugin.ResolveServerId,
                pollIntervalSeconds: options.PollIntervalSeconds,
                pauseOtherOnPlaybackStop: options.PauseOtherOnPlaybackStop,
                notifyOtherOnPlaybackStop: options.NotifyOtherOnPlaybackStop,
                messageIssuer: issuer,
                logManager: _logManager);
            plugin.ConfigurationChanged += OnConfigurationChanged;
            // Close the small race between taking the startup snapshot and
            // subscribing to the plugin event.
            _syncEngine.UpdateOptions(SyncEngineOptions.From(plugin.Configuration));
            SubscribeToSessionChanges(bridge);
            _syncEngine.Start();
        }

        public void Dispose()
        {
            UnsubscribeFromConfigurationChanges(_plugin);
            _plugin = null;
            UnsubscribeFromSessionChanges(_bridge);

            _syncEngine?.Dispose();
            _syncEngine = null;

            _bridge?.Dispose();
            _bridge = null;
        }

        private void OnConfigurationChanged(
            object sender,
            PluginConfigurationChangedEventArgs e)
        {
            if (e?.Options != null)
            {
                _syncEngine?.UpdateOptions(e.Options);
            }
        }

        private void UnsubscribeFromConfigurationChanges(Plugin plugin)
        {
            if (plugin != null)
            {
                plugin.ConfigurationChanged -= OnConfigurationChanged;
            }
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

        private void OnPlaybackStopped(object sender, PlaybackStopEventArgs e) => _syncEngine?.RequestImmediatePoll();

        private void OnSessionStarted(object sender, SessionEventArgs e) => _syncEngine?.RequestImmediatePoll();

        private void OnSessionEnded(object sender, SessionEventArgs e) => _syncEngine?.RequestImmediatePoll();

        private void OnCapabilitiesChanged(object sender, SessionEventArgs e) => _syncEngine?.RequestImmediatePoll();
    }
}
