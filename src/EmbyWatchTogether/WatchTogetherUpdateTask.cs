using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Updates;
using MediaBrowser.Controller;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Emby scheduled task that replaces the plugin's former in-plugin update
    /// scheduler. Emby automatically registers IScheduledTask implementations
    /// from plugin assemblies, so this task appears under Dashboard →
    /// Scheduled Tasks; the default trigger runs it every 24 hours and
    /// administrators can change the schedule, disable it or run it manually
    /// from there. The task owns its update manager so it never depends on the
    /// entry point having initialized one, and it exposes exactly one public
    /// constructor so Emby's container can create it.
    /// </summary>
    public sealed class WatchTogetherUpdateTask : IScheduledTask
    {
        public static readonly string TaskKey = "WatchTogetherUpdateCheck";

        public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(24);

        private readonly IHttpClient _httpClient;
        private readonly IInstallationManager _installationManager;
        private readonly IServerApplicationHost _applicationHost;
        private readonly ILogManager _logManager;

        public WatchTogetherUpdateTask(
            IHttpClient httpClient,
            IInstallationManager installationManager,
            IServerApplicationHost applicationHost,
            ILogManager logManager = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _installationManager = installationManager ?? throw new ArgumentNullException(nameof(installationManager));
            _applicationHost = applicationHost;
            _logManager = logManager;
        }

        public string Name => "Update Plugin";

        public string Key => TaskKey;

        public string Description =>
            "检查 GitHub 上的 Watch Together 正式版；发现新版本时自动安装，重启 Emby 后生效。";

        public string Category => "Watch Together";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerInterval,
                    IntervalTicks = DefaultInterval.Ticks,
                },
            };
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            progress?.Report(0);
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                // The plugin entry class has not finished loading; skip this
                // run instead of failing the task during startup.
                progress?.Report(1);
                return;
            }

            progress?.Report(0.1);
            var releaseClient = new GitHubReleaseClient(
                _httpClient,
                "EmbyWatchTogether/" + (plugin.Version?.ToString() ?? "unknown") +
                " (+" + GitHubReleaseClient.RepositoryUrl + ")");
            await RunCheckAsync(
                plugin,
                releaseClient,
                _installationManager,
                _applicationHost,
                _logManager,
                cancellationToken).ConfigureAwait(false);

            progress?.Report(1);
        }

        /// <summary>
        /// Runs one check-and-install pass against the given release client.
        /// Kept public and free of plugin singletons so tests can drive the
        /// full flow without Emby DI or network access.
        /// </summary>
        public static async Task RunCheckAsync(
            Plugin plugin,
            IPluginReleaseClient releaseClient,
            IInstallationManager installationManager,
            IServerApplicationHost applicationHost,
            ILogManager logManager,
            CancellationToken cancellationToken)
        {
            if (plugin == null)
            {
                throw new ArgumentNullException(nameof(plugin));
            }

            if (releaseClient == null)
            {
                throw new ArgumentNullException(nameof(releaseClient));
            }

            using (var manager = new PluginUpdateManager(
                plugin,
                releaseClient,
                installationManager,
                applicationHost,
                logManager))
            {
                var status = await manager
                    .CheckForUpdatesAsync(true, cancellationToken)
                    .ConfigureAwait(false);

                // Surface check/validation failures as task failures so they
                // show up in the scheduled-task history instead of silently
                // passing.
                if (!string.IsNullOrEmpty(status?.LastError))
                {
                    throw new InvalidOperationException(status.LastError);
                }
            }
        }
    }
}
