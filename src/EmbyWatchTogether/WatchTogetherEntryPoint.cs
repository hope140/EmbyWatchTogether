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

            try
            {
                var systemInfo = _applicationHost
                    .GetSystemInfo(null, null, CancellationToken.None)
                    .GetAwaiter().GetResult();
                plugin.ServerId = systemInfo?.Id;
            }
            catch
            {
                // Server id stays empty; rooms are marked unavailable until it resolves.
            }

            var bridge = new SessionBridge(_sessionManager);
            var store = new RoomStore(Path.Combine(plugin.DataFolderPath, "rooms.json"), _jsonSerializer);
            var rooms = new RoomManager(store);
            var provider = new SessionBridgeSnapshotProvider(bridge);
            var issuer = new SessionBridgeCommandIssuer(bridge);

            plugin.Store = store;
            plugin.Rooms = rooms;
            plugin.Bridge = bridge;
            plugin.Issuer = issuer;

            _syncEngine = new SyncEngine(
                rooms,
                provider,
                issuer,
                () => plugin.ServerId,
                pollIntervalSeconds: plugin.Configuration.PollIntervalSeconds);
            _syncEngine.Start();
        }

        public void Dispose()
        {
            _syncEngine?.Dispose();
        }
    }
}
