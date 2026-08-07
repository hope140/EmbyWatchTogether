using System;
using System.Collections.Generic;
using System.Threading;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
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

        public IServerApplicationHost ApplicationHost { get; internal set; }

        /// <summary>
        /// Shared updater owned by the server entry point. It is intentionally
        /// internal to the plugin so REST handlers can expose a read-only
        /// snapshot without constructing a second scheduler.
        /// </summary>
        public PluginUpdateManager UpdateManager { get; internal set; }

        public override void UpdateConfiguration(BasePluginConfiguration configuration)
        {
            var pluginConfiguration = configuration as PluginConfiguration;
            if (pluginConfiguration == null)
            {
                throw new ArgumentException("invalid Watch Together plugin configuration", nameof(configuration));
            }

            if (pluginConfiguration.UpdateCheckIntervalHours < 1 ||
                pluginConfiguration.UpdateCheckIntervalHours > 720)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pluginConfiguration.UpdateCheckIntervalHours),
                    "更新检测间隔必须是 1 到 720 小时之间的整数。");
            }

            base.UpdateConfiguration(pluginConfiguration);
            UpdateManager?.NotifyConfigurationChanged();
        }

        /// <summary>
        /// Lazily resolves and caches the server id. Called at startup and
        /// retried by room creation / the sync engine until it succeeds.
        /// </summary>
        public string ResolveServerId()
        {
            if (!string.IsNullOrEmpty(ServerId))
            {
                return ServerId;
            }

            try
            {
                var info = ApplicationHost?
                    .GetPublicSystemInfo(CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(info?.Id))
                {
                    ServerId = info.Id;
                }
            }
            catch
            {
                // Kept empty; callers retry lazily.
            }

            return ServerId;
        }

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
                // Controller module for the page above. Emby serves embedded
                // resources by page Name, so the .js resource must be declared
                // as its own page entry (same convention as ChapterApi pages).
                new PluginPageInfo
                {
                    Name = "WatchTogether.js",
                    EmbeddedResourcePath = "Emby.Plugins.WatchTogether.Configuration.WatchTogether.js",
                },
            };
        }
    }
}
