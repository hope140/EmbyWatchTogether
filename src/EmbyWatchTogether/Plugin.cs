using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Emby plugin entry class. Emby discovers it through automatic type discovery
    /// (IPlugin) and constructs it with dependency injection.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public static readonly Guid PluginId = Guid.Parse("0f8d1c2e-3b4a-4c5d-8e6f-7a8b9c0d1e2f");

        /// <summary>
        /// Plugin singleton used by API services and the entry point to reach
        /// shared components. Set in the constructor after DI instantiation.
        /// </summary>
        public static Plugin Instance { get; private set; }

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public RoomStore Store { get; internal set; }

        public RoomManager Rooms { get; internal set; }

        public SessionBridge Bridge { get; internal set; }

        public ICommandIssuer Issuer { get; internal set; }

        public string ServerId { get; internal set; }

        public override string Name => "Watch Together";

        public override string Description =>
            "Two-person synchronized watching on the same Emby server (pause/play/seek sync).";

        public override Guid Id => PluginId;

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "WatchTogether",
                    DisplayName = "Watch Together",
                    EmbeddedResourcePath = "Emby.Plugins.WatchTogether.Configuration.watchtogether.html",
                    EnableInMainMenu = true,
                    MenuSection = "server",
                    MenuIcon = "videocam",
                },
            };
        }
    }
}
