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
        private readonly object _lifecycleLock = new object();
        private Plugin _plugin;
        private RoomStore _store;
        private RoomManager _rooms;
        private SessionBridge _bridge;
        private ICommandIssuer _issuer;
        private SyncEngine _syncEngine;
        private bool _disposed;

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
            lock (_lifecycleLock)
            {
                if (_disposed || _syncEngine != null)
                {
                    return;
                }

                var plugin = Plugin.Instance;
                if (plugin == null)
                {
                    return;
                }

                var previousApplicationHost = plugin.ApplicationHost;
                var previousServerId = plugin.ServerId;
                SessionBridge bridge = null;
                SyncEngine syncEngine = null;
                bool configurationSubscribed = false;
                bool sessionEventsSubscribed = false;

                try
                {
                    bridge = new SessionBridge(_sessionManager);
                    var store = new RoomStore(Path.Combine(plugin.DataFolderPath, "rooms.json"), _jsonSerializer);
                    var rooms = new RoomManager(store);
                    var provider = new SessionBridgeSnapshotProvider(bridge);
                    var issuer = new SessionBridgeCommandIssuer(bridge, _logManager);
                    var options = SyncEngineOptions.From(plugin.Configuration);

                    syncEngine = new SyncEngine(
                        rooms,
                        provider,
                        issuer,
                        plugin.ResolveServerId,
                        pollIntervalSeconds: options.PollIntervalSeconds,
                        pauseOtherOnPlaybackStop: options.PauseOtherOnPlaybackStop,
                        notifyOtherOnPlaybackStop: options.NotifyOtherOnPlaybackStop,
                        notifyOnSyncActions: options.NotifyOnSyncActions,
                        messageIssuer: issuer,
                        logManager: _logManager);

                    // Keep the internal ownership visible to event handlers while
                    // withholding all runtime references from Plugin until Start
                    // has completed successfully.
                    _plugin = plugin;
                    _store = store;
                    _rooms = rooms;
                    _bridge = bridge;
                    _issuer = issuer;
                    _syncEngine = syncEngine;

                    plugin.ConfigurationChanged += OnConfigurationChanged;
                    configurationSubscribed = true;
                    SubscribeToSessionChanges(bridge);
                    sessionEventsSubscribed = true;

                    // Close the small race between taking the startup snapshot
                    // and subscribing to the plugin event.
                    syncEngine.UpdateOptions(SyncEngineOptions.From(plugin.Configuration));
                    plugin.ApplicationHost = _applicationHost;
                    plugin.ResolveServerId();
                    syncEngine.Start();

                    if (!ReferenceEquals(Plugin.Instance, plugin))
                    {
                        throw new InvalidOperationException("插件实例在 Watch Together 启动期间发生变化。");
                    }

                    // Publish the runtime graph only after every object has been
                    // constructed and the background engine has started.
                    plugin.Store = store;
                    plugin.Rooms = rooms;
                    plugin.Bridge = bridge;
                    plugin.Issuer = issuer;
                }
                catch (Exception ex)
                {
                    if (configurationSubscribed)
                    {
                        UnsubscribeFromConfigurationChanges(plugin);
                    }

                    if (sessionEventsSubscribed)
                    {
                        UnsubscribeFromSessionChanges(bridge);
                    }

                    ClearOwnedPluginReferences(plugin);

                    if (ReferenceEquals(plugin.ApplicationHost, _applicationHost))
                    {
                        plugin.ApplicationHost = previousApplicationHost;
                    }

                    if (string.Equals(plugin.ServerId, previousServerId, StringComparison.Ordinal) == false)
                    {
                        plugin.ServerId = previousServerId;
                    }

                    if (ReferenceEquals(_syncEngine, syncEngine))
                    {
                        _syncEngine = null;
                    }

                    if (ReferenceEquals(_bridge, bridge))
                    {
                        _bridge = null;
                    }

                    _plugin = null;
                    _store = null;
                    _rooms = null;
                    _issuer = null;

                    try
                    {
                        syncEngine?.Dispose();
                    }
                    catch (Exception disposeException)
                    {
                        LogStartupException("Watch Together 启动失败后的同步引擎清理失败。", disposeException);
                    }

                    try
                    {
                        bridge?.Dispose();
                    }
                    catch (Exception disposeException)
                    {
                        LogStartupException("Watch Together 启动失败后的会话桥接清理失败。", disposeException);
                    }

                    LogStartupException("Watch Together 启动失败。", ex);
                }
            }
        }

        public void Dispose()
        {
            lock (_lifecycleLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                var plugin = _plugin;
                var bridge = _bridge;
                var syncEngine = _syncEngine;

                UnsubscribeFromConfigurationChanges(plugin);
                UnsubscribeFromSessionChanges(bridge);
                ClearOwnedPluginReferences(plugin);

                _plugin = null;
                _store = null;
                _rooms = null;
                _issuer = null;
                _syncEngine = null;
                _bridge = null;

                try
                {
                    syncEngine?.Dispose();
                }
                catch (Exception ex)
                {
                    LogStartupException("Watch Together 停止同步引擎失败。", ex);
                }

                try
                {
                    bridge?.Dispose();
                }
                catch (Exception ex)
                {
                    LogStartupException("Watch Together 停止会话桥接失败。", ex);
                }
            }
        }

        private void ClearOwnedPluginReferences(Plugin plugin)
        {
            if (plugin == null || !ReferenceEquals(Plugin.Instance, plugin))
            {
                return;
            }

            if (ReferenceEquals(plugin.Store, _store))
            {
                plugin.Store = null;
            }

            if (ReferenceEquals(plugin.Rooms, _rooms))
            {
                plugin.Rooms = null;
            }

            if (ReferenceEquals(plugin.Bridge, _bridge))
            {
                plugin.Bridge = null;
            }

            if (ReferenceEquals(plugin.Issuer, _issuer))
            {
                plugin.Issuer = null;
            }
        }

        private void LogStartupException(string message, Exception exception)
        {
            try
            {
                _logManager?.GetLogger(nameof(WatchTogetherEntryPoint))?.ErrorException(message, exception);
            }
            catch
            {
                // Startup cleanup and diagnostics must not throw into Emby.
            }
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
