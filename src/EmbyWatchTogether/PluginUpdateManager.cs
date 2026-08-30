using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Updates;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Session;
using MediaBrowser.Model.Updates;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Core update check and installation logic. Scheduling is owned by the
    /// Emby scheduled task (<see cref="WatchTogetherUpdateTask"/>); this
    /// manager only guards concurrent operations and exposes status.
    /// </summary>
    public sealed class PluginUpdateManager : IDisposable
    {
        private readonly object _stateLock = new object();
        private readonly SemaphoreSlim _operationGate = new SemaphoreSlim(1, 1);
        private readonly IPluginReleaseClient _releaseClient;
        private readonly IInstallationManager _installationManager;
        private readonly IServerApplicationHost _applicationHost;
        private readonly ISessionManager _sessionManager;
        private readonly Func<PluginConfiguration> _configurationAccessor;
        private readonly Action _saveConfiguration;
        private readonly Func<Version> _currentVersionAccessor;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();

        private readonly PluginUpdateStatus _status;
        private VerifiedPluginRelease _verifiedRelease;
        private string _pendingVersionStatusOverride;
        private string _postInstallDiagnosticOverride;
        private bool _disposed;

        private const string CurrentVersionUnavailableMessage =
            "无法读取当前插件版本，已拒绝更新检查和安装。";

        public PluginUpdateManager(
            Plugin plugin,
            IPluginReleaseClient releaseClient,
            IInstallationManager installationManager,
            IServerApplicationHost applicationHost,
            ILogManager logManager = null,
            ISessionManager sessionManager = null)
            : this(
                () => plugin?.Configuration ?? new PluginConfiguration(),
                GetSaveConfigurationAction(plugin),
                () => plugin?.Version,
                releaseClient,
                installationManager,
                applicationHost,
                logManager,
                sessionManager)
        {
        }

        public PluginUpdateManager(
            PluginConfiguration configuration,
            Version currentVersion,
            IPluginReleaseClient releaseClient,
            IInstallationManager installationManager,
            IServerApplicationHost applicationHost = null,
            ILogManager logManager = null,
            ISessionManager sessionManager = null)
            : this(
                () => configuration ?? new PluginConfiguration(),
                null,
                () => currentVersion,
                releaseClient,
                installationManager,
                applicationHost,
                logManager,
                sessionManager)
        {
        }

        private PluginUpdateManager(
            Func<PluginConfiguration> configurationAccessor,
            Action saveConfiguration,
            Func<Version> currentVersionAccessor,
            IPluginReleaseClient releaseClient,
            IInstallationManager installationManager,
            IServerApplicationHost applicationHost,
            ILogManager logManager,
            ISessionManager sessionManager)
        {
            _configurationAccessor = configurationAccessor ?? throw new ArgumentNullException(nameof(configurationAccessor));
            _saveConfiguration = saveConfiguration;
            _currentVersionAccessor = currentVersionAccessor ?? throw new ArgumentNullException(nameof(currentVersionAccessor));
            _releaseClient = releaseClient ?? throw new ArgumentNullException(nameof(releaseClient));
            _installationManager = installationManager ?? throw new ArgumentNullException(nameof(installationManager));
            _applicationHost = applicationHost;
            _sessionManager = sessionManager;
            try
            {
                _logger = logManager?.GetLogger(nameof(PluginUpdateManager));
            }
            catch
            {
                _logger = null;
            }

            var configuration = ReadConfiguration();
            Version currentVersion;
            Exception currentVersionException;
            var currentVersionAvailable = TryReadCurrentVersion(out currentVersion, out currentVersionException);
            _status = new PluginUpdateStatus
            {
                CurrentVersion = currentVersionAvailable ? FormatVersion(currentVersion) : null,
                PendingVersion = configuration.PendingUpdateVersion,
                RestartRequired = currentVersionAvailable &&
                    IsPendingRestartRequired(configuration.PendingUpdateVersion, currentVersion),
                RepositoryUrl = GitHubReleaseClient.RepositoryUrl,
            };

            if (!currentVersionAvailable)
            {
                _status.LastError = CurrentVersionUnavailableMessage;
                LogException(CurrentVersionUnavailableMessage, currentVersionException);
            }
        }

        public PluginUpdateStatus GetStatus()
        {
            Exception currentVersionException = null;
            PluginUpdateStatus snapshot;
            lock (_stateLock)
            {
                var configuration = ReadConfiguration();
                Version currentVersion;
                Exception versionException;
                if (TryReadCurrentVersion(out currentVersion, out versionException))
                {
                    _status.CurrentVersion = FormatVersion(currentVersion);
                    _status.RestartRequired = IsPendingRestartRequired(
                        _pendingVersionStatusOverride ?? configuration.PendingUpdateVersion,
                        currentVersion);
                }
                else
                {
                    _status.CurrentVersion = null;
                    _status.UpdateAvailable = false;
                    _status.RestartRequired = _pendingVersionStatusOverride != null;
                    _status.LastError = CombineDiagnostics(
                        _postInstallDiagnosticOverride,
                        CurrentVersionUnavailableMessage);
                    currentVersionException = versionException;
                }

                _status.PendingVersion = _pendingVersionStatusOverride ?? configuration.PendingUpdateVersion;
                snapshot = _status.Clone();
            }

            if (currentVersionException != null)
            {
                LogException(CurrentVersionUnavailableMessage, currentVersionException);
            }

            return snapshot;
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
                        _status.LastError = _postInstallDiagnosticOverride;
                    }

                    if (_verifiedRelease == null)
                    {
                        var message = "请先检查更新，再安装插件更新。";
                        SetError(message, null);
                        await NotifyAdminAsync(message, linked.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        Version currentVersion;
                        Exception currentVersionException;
                        if (!TryReadCurrentVersion(out currentVersion, out currentVersionException))
                        {
                            _verifiedRelease = null;
                            SetError(CurrentVersionUnavailableMessage, currentVersionException);
                            await NotifyAdminAsync(CurrentVersionUnavailableMessage, linked.Token)
                                .ConfigureAwait(false);
                        }
                        else if (!IsNewer(_verifiedRelease.Release.Version, currentVersion))
                        {
                            var message = "当前已经是最新" + GetChannelLabel(_verifiedRelease) + "。";
                            SetError(message, null);
                            await NotifyAdminAsync(message, linked.Token).ConfigureAwait(false);
                        }
                        else
                        {
                            var configuration = ReadConfiguration();
                            if (!string.IsNullOrWhiteSpace(configuration.PendingUpdateVersion) &&
                                VersionsEqual(configuration.PendingUpdateVersion, _verifiedRelease.Release.Version))
                            {
                                var message = "该版本已等待重启生效。";
                                SetError(message, null);
                                await NotifyAdminAsync(message, linked.Token).ConfigureAwait(false);
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
                    var message = "安装更新失败，请稍后重试。";
                    SetError(message, ex);
                    await NotifyAdminAsync(message, linked.Token).ConfigureAwait(false);
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
            lock (_stateLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _lifetimeCancellation.Cancel();
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
                _status.LastError = _postInstallDiagnosticOverride;
                _status.LastCheckedAtUtc = checkedAt;
                _verifiedRelease = null;
            }

            try
            {
                Version currentVersion;
                Exception currentVersionException;
                if (!TryReadCurrentVersion(out currentVersion, out currentVersionException))
                {
                    lock (_stateLock)
                    {
                        _verifiedRelease = null;
                        _status.UpdateAvailable = false;
                    }

                    SetError(CurrentVersionUnavailableMessage, currentVersionException);
                    await NotifyAdminAsync(CurrentVersionUnavailableMessage, cancellationToken)
                        .ConfigureAwait(false);
                    return GetStatus();
                }

                var verifiedRelease = await _releaseClient
                    .CheckForLatestAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (verifiedRelease == null || verifiedRelease.Release == null ||
                    verifiedRelease.Release.Version == null || verifiedRelease.Asset == null ||
                    string.IsNullOrWhiteSpace(verifiedRelease.Md5Checksum))
                {
                    throw new ReleaseValidationException("插件更新校验结果无效。");
                }

                var release = verifiedRelease.Release;
                lock (_stateLock)
                {
                    _status.LatestVersion = FormatVersion(release.Version);
                    _status.ReleaseUrl = release.HtmlUrl;
                    _status.UpdateAvailable = IsNewer(release.Version, currentVersion);
                    _verifiedRelease = verifiedRelease;
                }

                if (!IsNewer(release.Version, currentVersion))
                {
                    if (automatic)
                    {
                        await NotifyLatestVersionAsync(
                            currentVersion,
                            verifiedRelease,
                            cancellationToken).ConfigureAwait(false);
                    }

                    LogInfo("检查完成：" + BuildLatestVersionMessage(currentVersion, verifiedRelease));
                    return GetStatus();
                }

                // The Emby scheduled task always installs when a newer release
                // is found; the administrator controls this by enabling,
                // disabling or re-scheduling the task itself.
                if (automatic)
                {
                    var configuration = ReadConfiguration();
                    if (!string.IsNullOrWhiteSpace(configuration.PendingUpdateVersion) &&
                        VersionsEqual(configuration.PendingUpdateVersion, release.Version))
                    {
                        lock (_stateLock)
                        {
                            _status.UpdateAvailable = false;
                        }
                        await NotifyAdminAsync(
                            "版本 v" + FormatVersion(release.Version) + " 已等待重启生效。",
                            cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await NotifyAdminAsync(
                            "发现" + GetChannelLabel(verifiedRelease) + " v" +
                            FormatVersion(release.Version) + "，正在安装。",
                            cancellationToken).ConfigureAwait(false);
                        try
                        {
                            await InstallVerifiedReleaseAsync(verifiedRelease, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            lock (_stateLock)
                            {
                                _verifiedRelease = null;
                                _status.UpdateAvailable = false;
                            }

                            const string installFailureMessage = "安装更新失败，请稍后重试。";
                            SetError(installFailureMessage, ex);
                            await NotifyAdminAsync(installFailureMessage, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }
                }

                return GetStatus();
            }
            catch (OperationCanceledException)
            {
                lock (_stateLock)
                {
                    _verifiedRelease = null;
                }
                throw;
            }
            catch (ReleaseValidationException ex)
            {
                lock (_stateLock)
                {
                    _verifiedRelease = null;
                    _status.UpdateAvailable = false;
                }
                SetError(ex.UserMessage, ex);
                await NotifyAdminAsync(ex.UserMessage, cancellationToken).ConfigureAwait(false);
                return GetStatus();
            }
            catch (Exception ex)
            {
                lock (_stateLock)
                {
                    _verifiedRelease = null;
                    _status.UpdateAvailable = false;
                }
                SetError("检查更新失败，请稍后重试。", ex);
                await NotifyAdminAsync("检查更新失败，请稍后重试。", cancellationToken).ConfigureAwait(false);
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
                _status.LastError = _postInstallDiagnosticOverride;
            }

            try
            {
                // Keep one previous DLL before Emby's installer overwrites it.
                // The fixed file name means only the most recent backup is kept.
                try
                {
                    BackupPluginDll(
                        Plugin.Instance?.AssemblyFilePath,
                        Plugin.Instance?.DataFolderPath);
                }
                catch (Exception ex)
                {
                    LogException("更新前备份当前插件失败。", ex);
                }

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
                var configurationSaved = SaveConfigurationSafely(configuration, out var saveException);
                Exception notificationException = null;
                try
                {
                    _applicationHost?.NotifyPendingRestart();
                }
                catch (Exception ex)
                {
                    notificationException = ex;
                    LogException("更新已安装，但通知 Emby 等待重启失败。", ex);
                }
                lock (_stateLock)
                {
                    _status.PendingVersion = configuration.PendingUpdateVersion;
                    _status.RestartRequired = true;
                    _status.UpdateAvailable = false;
                    _status.ReleaseUrl = release.HtmlUrl;

                    if (!configurationSaved || notificationException != null)
                    {
                        _pendingVersionStatusOverride = configuration.PendingUpdateVersion;
                        _postInstallDiagnosticOverride = BuildPostInstallDiagnostic(
                            configurationSaved,
                            notificationException != null);
                        _status.LastError = _postInstallDiagnosticOverride;
                    }
                }

                if (!configurationSaved)
                {
                    LogException("更新已安装，但保存待重启状态失败。", saveException);
                }

                if (configurationSaved && notificationException == null)
                {
                    var message = "已安装 v" + FormatVersion(release.Version) + "，重启 Emby 后生效。";
                    LogInfo(message);
                    await NotifyAdminAsync(message, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var message = BuildPostInstallDiagnostic(configurationSaved, notificationException != null);
                    LogInfo(message);
                    await NotifyAdminAsync(message, cancellationToken).ConfigureAwait(false);
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

        private Task NotifyLatestVersionAsync(
            Version currentVersion,
            VerifiedPluginRelease verifiedRelease,
            CancellationToken cancellationToken)
        {
            return NotifyAdminAsync(
                BuildLatestVersionMessage(currentVersion, verifiedRelease),
                cancellationToken);
        }

        private async Task NotifyAdminAsync(string message, CancellationToken cancellationToken)
        {
            if (_sessionManager == null || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                var command = MessageCommandFactory.DisplayMessageCommand(
                    "Watch Together",
                    message,
                    timeoutMs: 3000);

                await _sessionManager.SendMessageToAdminSessions(
                    "GeneralCommand",
                    command,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogException("发送插件更新提示失败。", ex);
            }
        }

        public static void BackupPluginDll(string sourcePath, string backupDir)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(backupDir))
            {
                return;
            }

            if (!File.Exists(sourcePath))
            {
                return;
            }

            Directory.CreateDirectory(backupDir);
            File.Copy(sourcePath, Path.Combine(backupDir, "previous-version.dll"), true);
        }

        private static Action GetSaveConfigurationAction(Plugin plugin)
        {
            // A DI-created plugin has a data folder. The null path is only the
            // deliberately uninitialized test/task construction, which cannot
            // persist configuration and should retain the prior no-op behavior.
            if (plugin == null || string.IsNullOrWhiteSpace(plugin.DataFolderPath))
            {
                return null;
            }

            return plugin.SaveConfiguration;
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

        private bool TryReadCurrentVersion(out Version version, out Exception exception)
        {
            version = null;
            exception = null;
            try
            {
                version = _currentVersionAccessor();
                if (version == null)
                {
                    exception = new InvalidOperationException("当前插件版本为空。");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                exception = ex;
                return false;
            }
        }

        private bool SaveConfigurationSafely(PluginConfiguration configuration, out Exception exception)
        {
            exception = null;
            try
            {
                _saveConfiguration?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                exception = ex;
                return false;
            }
        }

        private static string BuildPostInstallDiagnostic(bool configurationSaved, bool notificationFailed)
        {
            if (!configurationSaved && notificationFailed)
            {
                return "更新已安装，但保存待重启状态和通知 Emby 均失败；更新已安装但待处理。";
            }

            if (!configurationSaved)
            {
                return "更新已安装，但保存待重启状态失败；更新已安装但待处理。";
            }

            return "更新已安装，但通知 Emby 等待重启失败；更新已安装但待处理。";
        }

        private void SetError(string userMessage, Exception exception)
        {
            lock (_stateLock)
            {
                _status.LastError = CombineDiagnostics(_postInstallDiagnosticOverride, userMessage);
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

        private void LogInfo(string message)
        {
            try
            {
                _logger?.Info(message);
            }
            catch
            {
                // Logging must never terminate checking or installation.
            }
        }

        private static bool IsNewer(Version candidate, Version current)
        {
            return candidate != null && current != null && CompareVersions(candidate, current) > 0;
        }

        private static string CombineDiagnostics(string persistentDiagnostic, string currentDiagnostic)
        {
            if (string.IsNullOrWhiteSpace(persistentDiagnostic))
            {
                return currentDiagnostic;
            }

            if (string.IsNullOrWhiteSpace(currentDiagnostic) ||
                persistentDiagnostic.IndexOf(currentDiagnostic, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return persistentDiagnostic;
            }

            if (currentDiagnostic.IndexOf(persistentDiagnostic, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return currentDiagnostic;
            }

            return persistentDiagnostic + "；" + currentDiagnostic;
        }

        private static bool VersionsEqual(string pending, Version release)
        {
            return Version.TryParse(pending, out var pendingVersion) &&
                CompareVersions(pendingVersion, release) == 0;
        }

        private static bool IsPendingRestartRequired(string pending, Version current)
        {
            return !string.IsNullOrWhiteSpace(pending) &&
                current != null &&
                (!Version.TryParse(pending, out var pendingVersion) || CompareVersions(pendingVersion, current) != 0);
        }

        private static int CompareVersions(Version left, Version right)
        {
            if (left == null)
            {
                return right == null ? 0 : -1;
            }

            if (right == null)
            {
                return 1;
            }

            var comparison = left.Major.CompareTo(right.Major);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Minor.CompareTo(right.Minor);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = NormalizeVersionPart(left.Build).CompareTo(NormalizeVersionPart(right.Build));
            if (comparison != 0)
            {
                return comparison;
            }

            return NormalizeVersionPart(left.Revision).CompareTo(NormalizeVersionPart(right.Revision));
        }

        private static int NormalizeVersionPart(int value)
        {
            return value < 0 ? 0 : value;
        }

        private static string FormatVersion(Version version)
        {
            return version == null ? null : version.ToString();
        }

        private static string BuildLatestVersionMessage(
            Version currentVersion,
            VerifiedPluginRelease verifiedRelease)
        {
            var channelLabel = GetChannelLabel(verifiedRelease);
            var latestVersion = verifiedRelease?.Release?.Version;
            if (latestVersion != null && CompareVersions(currentVersion, latestVersion) > 0)
            {
                return "当前版本 v" + FormatVersion(currentVersion) +
                    " 高于最新" + channelLabel + " v" + FormatVersion(latestVersion) + "，无需更新。";
            }

            return "当前已是最新" + channelLabel + " v" + FormatVersion(currentVersion) + "。";
        }

        private static string GetChannelLabel(VerifiedPluginRelease release)
        {
            return release?.Release?.Prerelease == true ? "测试版" : "正式版";
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
