using System;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Server entry point. Later stacks wire session events, the room manager and
    /// the sync engine here; S1 only establishes the DI wiring.
    /// </summary>
    public sealed class WatchTogetherEntryPoint : IServerEntryPoint
    {
        private readonly ISessionManager _sessionManager;

        public WatchTogetherEntryPoint(ISessionManager sessionManager)
        {
            _sessionManager = sessionManager;
        }

        public void Run()
        {
            // Reserved for S2/S3: playback event subscription, room lifecycle.
        }

        public void Dispose()
        {
            // Reserved for cleanup.
        }
    }
}
