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

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
        }

        public override string Name => "Watch Together";

        public override string Description =>
            "Two-person synchronized watching on the same Emby server (pause/play/seek sync).";

        public override Guid Id => PluginId;

        /// <summary>
        /// Web UI pages are registered by the S6 stack; none yet.
        /// </summary>
        public IEnumerable<PluginPageInfo> GetPages() => Array.Empty<PluginPageInfo>();
    }
}
