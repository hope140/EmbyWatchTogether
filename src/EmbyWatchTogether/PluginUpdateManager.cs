using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Updates;
using MediaBrowser.Controller;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Updates;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Coordinates update checks, scheduling and installation. The manager is
    /// deliberately independent from the REST service so automatic work and
    /// administrator actions share the same operation gate.
    /// </summary>
    public sealed class PluginUpdateManager : IDisposable
    {
        private readonly object _stateLock = new object();
        private readonly SemaphoreSlim _operationGate = new SemaphoreSlim(1, 1);
        private readonly IPluginReleaseClient _releaseClient;
        private readonly IInstallationManager _installationManager;
        private readonly IServerApplicationHost _applicationHost;
        private readonly Func<PluginConfiguration> _configurationAccessor;
        private readonly Action _saveConfiguration;
        private readonly Func<Version> _currentVersionAccessor;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();

        private readonly PluginUpdateStatus _status;
        private Task _schedulerTask;
        private TaskCompletionSource<bool> _scheduleSignal = NewSignal();
        private VerifiedPluginRelease _verifiedRelease;
        private bool _started;
        private bool _disposed;

        public PluginUpdateManager(
            Plugin plugin,
            IPluginReleaseClient releaseClient,
            IInstallationManager installationManager,
            IServerApplicationHost applicationHost,
            ILogManager logManager = null)
            : this(
                () => plugin?.Configuration ?? new PluginConfiguration(),
                () => plugin?.SaveConfiguration(),
                () => plugin?.Version ?? typeof(PluginUpdateManager).Assembly.GetName().Version,
                releaseClient,
                installationManager,
                applicationHost,
                logManager)
        {
        }

        public PluginUpdateManager(
            PluginConfiguration configuration,
            Version currentVersion,
            IPluginReleaseClient releaseClient,
            IInstallationManager installationManager,
            IServerApplicationHost applicationHost = null,
            ILogManager logManager = null)
            : this(
                () => configuration ?? new PluginConfiguration(),
                null,
                () => currentVersion ?? typeof(PluginUpdateManager).Assembly.GetName().Version,
                releaseClient,
                installationManager,
                applicationHost,
                logManager)
        {
        }

        private PluginUpdateManager(
            Func<PluginConfiguration> configurationAccessor,
            Action saveConfiguration,
            Func<Version> currentVersionAccessor,
            IPluginReleaseClient releaseClient,
            IInstallationManager installationManager,
            IServerApplicationHost applicationHost,
            ILogManager logManager)
        {
            _configurationAccessor = configurationAccessor ?? throw new ArgumentNullException(nameof(configurationAccessor));
            _saveConfiguration = saveConfiguration;
            _currentVersionAccessor = currentVersionAccessor ?? throw new ArgumentNullException(nameof(currentVersionAccessor));
            _releaseClient = releaseClient ?? throw new ArgumentNullException(nameof(releaseClient));
            _installationManager = installationManager ?? throw new ArgumentNullException(nameof(installationManager));
            _applicationHost = applicationHost;
            try
            {
                _logger = logManager?.GetLogger(nameof(PluginUpdateManager));
            }
            catch
            {
                _logger = null;
            }

            var configuration = ReadConfiguration();
            var currentVersion = ReadCurrentVersion();
            _status = new PluginUpdateStatus
            {
                CurrentVersion = FormatVersion(currentVersion),
                PendingVersion = configuration.PendingUpdateVersion,
                LastCheckedAtUtc = configuration.LastUpdateCheckAtUtc,
                RestartRequired = IsPendingRestartRequired(configuration.PendingUpdateVersion, currentVersion),
                RepositoryUrl = GitHubReleaseClient.RepositoryUrl,
            };
        }

        public void Start()
        {
            lock (_stateLock)
            {
                ThrowIfDisposed();
                if (_started)
                {
                    return;
                }

                _started = true;
                _schedulerTask = Task.Run(() => SchedulerLoopAsync(_lifetimeCancellation.Token));
            }
        }

        /// <summary>
        /// Called after Plugin.UpdateConfiguration has persisted a new config.
        /// The scheduler is woken up so enable/disable and interval changes do
        /// not wait for the old timer.
        /// </summary>
        public void NotifyConfigurationChanged()
        {
            lock (_stateLock)
            {
                if (_disposed)
                {
                    return;
                }

                SignalSchedule();
            }
        }

        public PluginUpdateStatus GetStatus()
        {
            lock (_stateLock)
            {
                RefreshCurrentVersionLocked();
                var configuration = ReadConfiguration();
                _status.PendingVersion = configuration.PendingUpdateVersion;
                _status.RestartRequired = IsPendingRestartRequired(configuration.PendingUpdateVersion, ReadCurrentVersion());
                return _status.Clone();
            }
        }

        public Task<PluginUpdateStatus> CheckAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return CheckForUpdatesAsync(false, cancellationToken);
        }

        public async Task<PluginUpdateStatus> CheckForUpdatesAsync(
            bool automatic,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCancellation.Token,
                cancellationToken))
            {
                await _operationGate.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    // Resolve the check result first, then clear the in-flight
                    // flag and snapshot again. Returning GetStatus() from inside
                    // CheckCoreAsync would clone IsChecking=true because its
                    // finally block runs after the value is captured.
                    await CheckCoreAsync(automatic, linked.Token).ConfigureAwait(false);
                    lock (_stateLock)
                    {
                        _status.IsChecking = false;
                    }

                    return GetStatus();
                }
                finally
                {
                    lock (_stateLock)
                    {
                        _status.IsChecking = false;
                    }

                    _operationGate.Release();
                }
            }
        }

        public Task<PluginUpdateStatus> InstallUpdateAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return InstallAsync(cancellationToken);
        }

        public async Task<PluginUpdateStatus> InstallAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCancellation.Token,
                cancellationToken))
            {
                await _operationGate.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    lock (_stateLock)
                    {
                        _status.IsInstalling = true;
                        _status.LastError = null;
                    }

                    if (_verifiedRelease == null)
                    {
                        SetError("请先检查更新，再安装正式版插件。", null);
                    }
                    else
                    {
                        var currentVersion = ReadCurrentVersion();
                        if (!IsNewer(_verifiedRelease.Release.Version, currentVersion))
                        {
                            SetError("当前已经是最新正式版。", null);
                        }
                        else
                        {
                            var configuration = ReadConfiguration();
                            if (!string.IsNullOrWhiteSpace(configuration.PendingUpdateVersion) &&
                                VersionsEqual(configuration.PendingUpdateVersion, _verifiedRelease.Release.Version))
                            {
                                SetError("该版本已等待重启生效。", null);
                            }
                            else
                            {
                                await InstallVerifiedReleaseAsync(_verifiedRelease, linked.Token).ConfigureAwait(false);
                            }
                        }
                    }

                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    SetError("安装更新失败，请稍后重试。", ex);
                }
                finally
                {
                    lock (_stateLock)
                    {
                        _status.IsInstalling = false;
                    }

                    _operationGate.Release();
                }

                // Snapshot again so the returned status never reports
                // IsInstalling=true; the finally block runs after the value
                // captured above was cloned.
                return GetStatus();
            }
        }

        public void Dispose()
        {
            Task scheduler;
            lock (_stateLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _lifetimeCancellation.Cancel();
                SignalSchedule();
                scheduler = _schedulerTask;
            }

            if (scheduler != null)
            {
                try
                {
                    scheduler.Wait(TimeSpan.FromSeconds(5));
                }
                catch (AggregateException)
                {
                    // Scheduler exceptions are logged in the loop and never
                    // allowed to escape plugin disposal.
                }
            }

            _operationGate.Dispose();
            _lifetimeCancellation.Dispose();
        }

        private async Task<PluginUpdateStatus> CheckCoreAsync(bool automatic, CancellationToken cancellationToken)
        {
            var checkedAt = DateTimeOffset.UtcNow;
            lock (_stateLock)
            {
                _status.IsChecking = true;
                _status.LastError = null;
                _status.LastCheckedAtUtc = checkedAt;
                PersistLastCheckedLocked(checkedAt);
            }

            try
            {
                var release = await _releaseClient.GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
                if (release == null || release.Version == null)
                {
                    throw new ReleaseValidationException("GitHub 正式版信息无效。");
                }

                var currentVersion = ReadCurrentVersion();
                lock (_stateLock)
                {
                    _status.LatestVersion = FormatVersion(release.Version);
                    _status.ReleaseUrl = release.HtmlUrl;
                    _status.UpdateAvailable = IsNewer(release.Version, currentVersion);
                    _verifiedRelease = null;
                }

                if (!IsNewer(release.Version, currentVersion))
                {
                    return GetStatus();
                }

                var verifiedRelease = await _releaseClient
                    .DownloadAndVerifyAsync(release, cancellationToken)
                    .ConfigureAwait(false);
                if (verifiedRelease == null || verifiedRelease.Release == null ||
                    verifiedRelease.Asset == null || string.IsNullOrWhiteSpace(verifiedRelease.Md5Checksum))
                {
                    throw new ReleaseValidationException("正式版插件校验结果无效。");
                }

                lock (_stateLock)
                {
                    _verifiedRelease = verifiedRelease;
                    _status.UpdateAvailable = true;
                }

                // A configuration save may disable automatic updates while a
                // background metadata/download request is in flight. Re-read
                // the flag immediately before installing so that queued work
                // cannot install after the administrator turned it off.
                if (automatic && ReadConfiguration().AutoUpdateEnabled)
                {
                    var configuration = ReadConfiguration();
                    if (!string.IsNullOrWhiteSpace(configuration.PendingUpdateVersion) &&
                        VersionsEqual(configuration.PendingUpdateVersion, release.Version))
                    {
                        lock (_stateLock)
                        {
                            _status.UpdateAvailable = false;
                        }
                    }
                    else
                    {
                        await InstallVerifiedReleaseAsync(verifiedRelease, cancellationToken).ConfigureAwait(false);
                    }
                }

                return GetStatus();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ReleaseValidationException ex)
            {
                lock (_stateLock)
                {
                    _status.UpdateAvailable = false;
                }
                SetError(ex.UserMessage, ex);
                return GetStatus();
            }
            catch (Exception ex)
            {
                lock (_stateLock)
                {
                    _status.UpdateAvailable = false;
                }
                SetError("检查更新失败，请稍后重试。", ex);
                return GetStatus();
            }
            finally
            {
                lock (_stateLock)
                {
                    _status.IsChecking = false;
                }
            }
        }

        private async Task InstallVerifiedReleaseAsync(
            VerifiedPluginRelease verifiedRelease,
            CancellationToken cancellationToken)
        {
            lock (_stateLock)
            {
                _status.IsInstalling = true;
                _status.LastError = null;
            }

            try
            {
                var release = verifiedRelease.Release;
                var package = new PackageVersionInfo
                {
                    name = "Watch Together",
                    guid = Plugin.PluginId.ToString("D"),
                    versionStr = FormatVersion(release.Version),
                    classification = PackageVersionClass.Release,
                    sourceUrl = verifiedRelease.Asset.BrowserDownloadUrl,
                    checksum = verifiedRelease.Md5Checksum,
                    targetFilename = GitHubReleaseClient.AssetName,
                    infoUrl = release.HtmlUrl,
                };

                await _installationManager.InstallPackage(
                    package,
                    true,
                    new Progress<double>(),
                    cancellationToken).ConfigureAwait(false);

                var configuration = ReadConfiguration();
                configuration.PendingUpdateVersion = FormatVersion(release.Version);
                SaveConfigurationSafely(configuration);
                lock (_stateLock)
                {
                    _status.PendingVersion = configuration.PendingUpdateVersion;
                    _status.RestartRequired = true;
                    _status.UpdateAvailable = false;
                    _status.ReleaseUrl = release.HtmlUrl;
                }
            }
            catch
            {
                throw;
            }
            finally
            {
                lock (_stateLock)
                {
                    _status.IsInstalling = false;
                }
            }
        }

        private async Task SchedulerLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var configuration = ReadConfiguration();
                    if (!configuration.AutoUpdateEnabled)
                    {
                        await WaitForScheduleAsync(null, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    var lastChecked = GetStatus().LastCheckedAtUtc ?? configuration.LastUpdateCheckAtUtc;
                    var interval = GetInterval(configuration.UpdateCheckIntervalHours);
                    var dueAt = lastChecked.HasValue
                        ? lastChecked.Value.AddHours(interval)
                        : DateTimeOffset.UtcNow;
                    var delay = dueAt - DateTimeOffset.UtcNow;
                    if (delay > TimeSpan.Zero)
                    {
                        await WaitForScheduleAsync(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    try
                    {
                        await CheckForUpdatesAsync(true, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        SetError("自动检查更新失败，请稍后重试。", ex);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                SetError("自动更新任务已暂停，请稍后手动检查。", ex);
            }
        }

        private Task WaitForScheduleAsync(TimeSpan? delay, CancellationToken cancellationToken)
        {
            TaskCompletionSource<bool> signal;
            lock (_stateLock)
            {
                signal = _scheduleSignal;
            }

            var delayTask = delay.HasValue
                ? Task.Delay(delay.Value, cancellationToken)
                : Task.Delay(Timeout.Infinite, cancellationToken);
            return WaitForEitherAsync(signal.Task, delayTask, cancellationToken);
        }

        private static async Task WaitForEitherAsync(
            Task signalTask,
            Task delayTask,
            CancellationToken cancellationToken)
        {
            await Task.WhenAny(signalTask, delayTask).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        private void SignalSchedule()
        {
            var previous = _scheduleSignal;
            _scheduleSignal = NewSignal();
            previous.TrySetResult(true);
        }

        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private PluginConfiguration ReadConfiguration()
        {
            try
            {
                return _configurationAccessor() ?? new PluginConfiguration();
            }
            catch
            {
                return new PluginConfiguration();
            }
        }

        private Version ReadCurrentVersion()
        {
            try
            {
                return _currentVersionAccessor() ?? new Version(0, 0, 0, 0);
            }
            catch
            {
                return new Version(0, 0, 0, 0);
            }
        }

        private void RefreshCurrentVersionLocked()
        {
            _status.CurrentVersion = FormatVersion(ReadCurrentVersion());
        }

        private void PersistLastCheckedLocked(DateTimeOffset checkedAt)
        {
            var configuration = ReadConfiguration();
            configuration.LastUpdateCheckAtUtc = checkedAt;
            SaveConfigurationSafely(configuration);
        }

        private void SaveConfigurationSafely(PluginConfiguration configuration)
        {
            try
            {
                _saveConfiguration?.Invoke();
            }
            catch (Exception ex)
            {
                LogException("保存插件更新状态失败。", ex);
            }
        }

        private void SetError(string userMessage, Exception exception)
        {
            lock (_stateLock)
            {
                _status.LastError = userMessage;
            }

            if (exception != null)
            {
                LogException(userMessage, exception);
            }
        }

        private void LogException(string message, Exception exception)
        {
            try
            {
                _logger?.ErrorException(message, exception);
            }
            catch
            {
                // Logging must never terminate checking or installation.
            }
        }

        private static int GetInterval(int intervalHours)
        {
            return intervalHours < 1 || intervalHours > 720 ? 24 : intervalHours;
        }

        private static bool IsNewer(Version candidate, Version current)
        {
            return candidate != null && current != null && candidate > current;
        }

        private static bool VersionsEqual(string pending, Version release)
        {
            return Version.TryParse(pending, out var pendingVersion) &&
                GitHubReleaseClient.VersionsEqual(pendingVersion, release);
        }

        private static bool IsPendingRestartRequired(string pending, Version current)
        {
            return !string.IsNullOrWhiteSpace(pending) &&
                (!Version.TryParse(pending, out var pendingVersion) || !GitHubReleaseClient.VersionsEqual(pendingVersion, current));
        }

        private static string FormatVersion(Version version)
        {
            return version == null ? null : version.ToString();
        }

        private void ThrowIfDisposedInstance()
        {
            lock (_stateLock)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(PluginUpdateManager));
                }
            }
        }

        private void ThrowIfDisposed()
        {
            ThrowIfDisposedInstance();
        }
    }
}
