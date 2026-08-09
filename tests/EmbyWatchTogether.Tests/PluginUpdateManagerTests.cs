using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Updates;
using MediaBrowser.Controller;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Updates;
using Moq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class PluginUpdateManagerTests
    {
        [Fact]
        public async Task ManualCheckDoesNotInstall_AndPendingVersionPreventsDuplicateInstall()
        {
            var configuration = new PluginConfiguration();
            var releaseClient = new FakeReleaseClient(CreateRelease(2, 0, 0));
            var installation = CreateInstallationManager(out var installMock);
            using (var manager = new PluginUpdateManager(configuration, new Version(1, 0, 0), releaseClient, installation))
            {
                var checkedStatus = await manager.CheckForUpdatesAsync(false);

                Assert.True(checkedStatus.UpdateAvailable);
                Assert.False(checkedStatus.IsChecking);
                installMock.Verify(x => x.InstallPackage(
                    It.IsAny<PackageVersionInfo>(),
                    true,
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()), Times.Never);

                var installedStatus = await manager.InstallAsync();
                Assert.True(installedStatus.RestartRequired);
                Assert.False(installedStatus.IsInstalling);
                Assert.Equal("2.0.0", configuration.PendingUpdateVersion);
                installMock.Verify(x => x.InstallPackage(
                    It.Is<PackageVersionInfo>(p =>
                        p.name == "Watch Together" &&
                        p.guid == Plugin.PluginId.ToString("D") &&
                        p.versionStr == "2.0.0" &&
                        p.classification == PackageVersionClass.Release &&
                        p.targetFilename == GitHubReleaseClient.AssetName &&
                        p.checksum == "md5-checksum" &&
                        p.sourceUrl == "https://github.com/hope140/EmbyWatchTogether/releases/download/v2.0.0/Emby.Plugins.WatchTogether.dll" &&
                        p.infoUrl == "https://github.com/hope140/EmbyWatchTogether/releases/tag/v2.0.0"),
                    true,
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()), Times.Once);

                await manager.InstallAsync();
                installMock.Verify(x => x.InstallPackage(
                    It.IsAny<PackageVersionInfo>(),
                    true,
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        [Fact]
        public async Task AutomaticCheckInstallsExactlyOnce()
        {
            var configuration = new PluginConfiguration();
            var releaseClient = new FakeReleaseClient(CreateRelease(2, 0, 0));
            var installation = CreateInstallationManager(out var installMock);
            using (var manager = new PluginUpdateManager(configuration, new Version(1, 0, 0), releaseClient, installation))
            {
                var status = await manager.CheckForUpdatesAsync(true);

                Assert.True(status.RestartRequired);
                Assert.False(status.IsChecking);
                Assert.False(status.IsInstalling);
                Assert.Equal("2.0.0", status.PendingVersion);
                installMock.Verify(x => x.InstallPackage(
                    It.IsAny<PackageVersionInfo>(),
                    true,
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        [Fact]
        public async Task EquivalentThreeAndFourPartVersionsDoNotOfferAnUpdate()
        {
            var configuration = new PluginConfiguration();
            var release = CreateRelease(1, 2, 0);
            release.Version = new Version(1, 2, 0, 0);
            var releaseClient = new FakeReleaseClient(release);
            var installation = CreateInstallationManager(out var installMock);
            using (var manager = new PluginUpdateManager(
                configuration,
                new Version(1, 2, 0),
                releaseClient,
                installation))
            {
                var status = await manager.CheckForUpdatesAsync(false);

                Assert.False(status.UpdateAvailable);
                Assert.Null(status.LastError);
                installMock.Verify(x => x.InstallPackage(
                    It.IsAny<PackageVersionInfo>(),
                    true,
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()), Times.Never);
            }
        }

        [Fact]
        public async Task PluginConstructor_NullVersionFailsClosed()
        {
#pragma warning disable SYSLIB0050
            var plugin = (Plugin)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Plugin));
#pragma warning restore SYSLIB0050
            var releaseClient = new FakeReleaseClient(CreateRelease(2, 0, 0));
            var installation = CreateInstallationManager(out var installMock);
            using (var manager = new PluginUpdateManager(
                plugin,
                releaseClient,
                installation,
                applicationHost: null))
            {
                var status = await manager.CheckForUpdatesAsync(false);

                Assert.Contains("无法读取当前插件版本", status.LastError);
                Assert.False(status.UpdateAvailable);
                Assert.Equal(0, releaseClient.CheckCount);
                installMock.Verify(x => x.InstallPackage(
                    It.IsAny<PackageVersionInfo>(),
                    true,
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()), Times.Never);
            }
        }

        [Fact]
        public async Task FailedSecondCheckInvalidatesThePreviousVerifiedRelease()
        {
            var releaseClient = new FirstSuccessThenFailureReleaseClient(CreateRelease(2, 0, 0));
            var installation = CreateInstallationManager(out var installMock);
            using (var manager = new PluginUpdateManager(
                new PluginConfiguration(),
                new Version(1, 0, 0),
                releaseClient,
                installation))
            {
                var firstStatus = await manager.CheckForUpdatesAsync(false);
                var secondStatus = await manager.CheckForUpdatesAsync(false);
                var installStatus = await manager.InstallAsync();

                Assert.True(firstStatus.UpdateAvailable);
                Assert.Contains("检查更新失败", secondStatus.LastError);
                Assert.Contains("请先检查更新", installStatus.LastError);
                Assert.Equal(2, releaseClient.CheckCount);
                installMock.Verify(x => x.InstallPackage(
                    It.IsAny<PackageVersionInfo>(),
                    true,
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()), Times.Never);
            }
        }

        [Fact]
        public async Task NullCurrentVersion_RejectsCheckAndInstall()
        {
            var releaseClient = new FakeReleaseClient(CreateRelease(2, 0, 0));
            var installation = CreateInstallationManager(out var installMock);
            using (var manager = CreateManager(
                new PluginConfiguration(),
                () => null,
                null,
                releaseClient,
                installation))
            {
                var checkedStatus = await manager.CheckForUpdatesAsync(false);
                var installedStatus = await manager.InstallAsync();

                Assert.Contains("无法读取当前插件版本", checkedStatus.LastError);
                Assert.Contains("无法读取当前插件版本", installedStatus.LastError);
                Assert.False(checkedStatus.UpdateAvailable);
                Assert.Equal(0, releaseClient.CheckCount);
                installMock.Verify(x => x.InstallPackage(
                    It.IsAny<PackageVersionInfo>(),
                    true,
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()), Times.Never);
            }
        }

        [Fact]
        public async Task ThrowingCurrentVersion_RejectsCheckAndInstall()
        {
            var releaseClient = new FakeReleaseClient(CreateRelease(2, 0, 0));
            var installation = CreateInstallationManager(out var installMock);
            using (var manager = CreateManager(
                new PluginConfiguration(),
                () => throw new InvalidOperationException("version accessor failed"),
                null,
                releaseClient,
                installation))
            {
                var checkedStatus = await manager.CheckForUpdatesAsync(false);
                var installedStatus = await manager.InstallAsync();

                Assert.Contains("无法读取当前插件版本", checkedStatus.LastError);
                Assert.Contains("无法读取当前插件版本", installedStatus.LastError);
                Assert.False(checkedStatus.UpdateAvailable);
                Assert.Equal(0, releaseClient.CheckCount);
                installMock.Verify(x => x.InstallPackage(
                    It.IsAny<PackageVersionInfo>(),
                    true,
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()), Times.Never);
            }
        }

        [Fact]
        public void GetStatus_WhenCurrentVersionAccessorThrows_LogsSafely()
        {
            var shouldThrow = false;
            var logger = new Mock<ILogger>();
            logger.Setup(x => x.ErrorException(It.IsAny<string>(), It.IsAny<Exception>()))
                .Throws(new InvalidOperationException("logger failed"));
            var logManager = new Mock<ILogManager>();
            logManager.Setup(x => x.GetLogger(nameof(PluginUpdateManager)))
                .Returns(logger.Object);
            var releaseClient = new FakeReleaseClient(CreateRelease(2, 0, 0));
            var installation = CreateInstallationManager(out _);
            using (var manager = CreateManager(
                new PluginConfiguration(),
                () =>
                {
                    if (shouldThrow)
                    {
                        throw new InvalidOperationException("version accessor failed");
                    }

                    return new Version(1, 0, 0);
                },
                null,
                releaseClient,
                installation,
                logManager: logManager.Object))
            {
                shouldThrow = true;

                var status = manager.GetStatus();

                Assert.Contains("无法读取当前插件版本", status.LastError);
                logger.Verify(x => x.ErrorException(
                    It.IsAny<string>(),
                    It.IsAny<Exception>()), Times.Once);
            }
        }

        [Fact]
        public async Task InstallationSuccess_SaveFailureExposesInstalledPendingDiagnostic()
        {
            var configuration = new PluginConfiguration();
            var releaseClient = new FakeReleaseClient(CreateRelease(2, 0, 0));
            var installation = CreateInstallationManager(out var installMock);
            Action save = () => throw new InvalidOperationException("save failed");
            using (var manager = CreateManager(
                configuration,
                () => new Version(1, 0, 0),
                save,
                releaseClient,
                installation))
            {
                var status = await manager.CheckForUpdatesAsync(true);

                Assert.True(status.RestartRequired);
                Assert.Contains("更新已安装", status.LastError);
                Assert.Contains("保存", status.LastError);
                Assert.Equal("2.0.0", status.PendingVersion);
                var afterStatus = manager.GetStatus();
                Assert.Equal(status.LastError, afterStatus.LastError);
                var afterCheck = await manager.CheckForUpdatesAsync(false);
                Assert.Equal(status.LastError, afterCheck.LastError);
                installMock.Verify(x => x.InstallPackage(
                    It.IsAny<PackageVersionInfo>(),
                    true,
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        [Fact]
        public async Task PostInstallDiagnostic_IsCombinedWithLaterCheckFailure()
        {
            var configuration = new PluginConfiguration();
            var releaseClient = new FirstSuccessThenFailureReleaseClient(CreateRelease(2, 0, 0));
            var installation = CreateInstallationManager(out var installMock);
            Action save = () => throw new InvalidOperationException("save failed");
            using (var manager = CreateManager(
                configuration,
                () => new Version(1, 0, 0),
                save,
                releaseClient,
                installation))
            {
                var installedStatus = await manager.CheckForUpdatesAsync(true);
                var failedCheckStatus = await manager.CheckForUpdatesAsync(false);

                Assert.Contains("更新已安装", installedStatus.LastError);
                Assert.Contains("保存", installedStatus.LastError);
                Assert.Contains("更新已安装", failedCheckStatus.LastError);
                Assert.Contains("检查更新失败", failedCheckStatus.LastError);
                installMock.Verify(x => x.InstallPackage(
                    It.IsAny<PackageVersionInfo>(),
                    true,
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        [Fact]
        public async Task InstallationSuccess_NotificationFailureExposesInstalledPendingDiagnostic()
        {
            var configuration = new PluginConfiguration();
            var releaseClient = new FakeReleaseClient(CreateRelease(2, 0, 0));
            var installation = CreateInstallationManager(out var installMock);
            var applicationHost = new Mock<IServerApplicationHost>();
            applicationHost.Setup(x => x.NotifyPendingRestart())
                .Throws(new InvalidOperationException("notification failed"));
            using (var manager = CreateManager(
                configuration,
                () => new Version(1, 0, 0),
                null,
                releaseClient,
                installation,
                applicationHost.Object))
            {
                var status = await manager.CheckForUpdatesAsync(true);

                Assert.True(status.RestartRequired);
                Assert.Contains("更新已安装", status.LastError);
                Assert.Contains("通知", status.LastError);
                Assert.Equal("2.0.0", status.PendingVersion);
                var afterStatus = manager.GetStatus();
                Assert.Equal(status.LastError, afterStatus.LastError);
                var afterCheck = await manager.CheckForUpdatesAsync(false);
                Assert.Equal(status.LastError, afterCheck.LastError);
                installMock.Verify(x => x.InstallPackage(
                    It.IsAny<PackageVersionInfo>(),
                    true,
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()), Times.Once);
                applicationHost.Verify(x => x.NotifyPendingRestart(), Times.Once);
            }
        }

        [Fact]
        public async Task InstallationFailureDoesNotPersistPendingVersion()
        {
            var configuration = new PluginConfiguration();
            var releaseClient = new FakeReleaseClient(CreateRelease(2, 0, 0));
            var installation = new Mock<IInstallationManager>();
            installation.Setup(x => x.InstallPackage(
                    It.IsAny<PackageVersionInfo>(),
                    true,
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("install failed"));
            using (var manager = new PluginUpdateManager(configuration, new Version(1, 0, 0), releaseClient, installation.Object))
            {
                await manager.CheckForUpdatesAsync(false);
                var status = await manager.InstallAsync();

                Assert.False(status.RestartRequired);
                Assert.Null(configuration.PendingUpdateVersion);
                Assert.Contains("安装更新失败", status.LastError);
            }
        }

        [Fact]
        public async Task AutomaticCheck_NotifiesPendingRestart_AfterInstall()
        {
            var configuration = new PluginConfiguration();
            var releaseClient = new FakeReleaseClient(CreateRelease(2, 0, 0));
            var installation = CreateInstallationManager(out var installMock);
            var applicationHost = new Mock<MediaBrowser.Controller.IServerApplicationHost>();
            using (var manager = new PluginUpdateManager(
                configuration,
                new Version(1, 0, 0),
                releaseClient,
                installation,
                applicationHost.Object))
            {
                var status = await manager.CheckForUpdatesAsync(true);

                Assert.True(status.RestartRequired);
                applicationHost.Verify(x => x.NotifyPendingRestart(), Times.Once);
            }
        }

        [Fact]
        public void BackupPluginDll_KeepsOnlyMostRecentBackup()
        {
            var dir = Path.Combine(Path.GetTempPath(), "watchtogether-backup-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var source = Path.Combine(dir, "source.dll");
                File.WriteAllText(source, "version one");

                PluginUpdateManager.BackupPluginDll(source, dir);

                var backup = Path.Combine(dir, "previous-version.dll");
                Assert.True(File.Exists(backup));
                Assert.Equal("version one", File.ReadAllText(backup));

                File.WriteAllText(source, "version two");
                PluginUpdateManager.BackupPluginDll(source, dir);

                Assert.True(File.Exists(backup));
                Assert.Equal("version two", File.ReadAllText(backup));
                Assert.Single(Directory.GetFiles(dir, "previous-version.dll"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static IInstallationManager CreateInstallationManager(out Mock<IInstallationManager> mock)
        {
            mock = new Mock<IInstallationManager>();
            mock.Setup(x => x.InstallPackage(
                    It.IsAny<PackageVersionInfo>(),
                    true,
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            return mock.Object;
        }

        private static PluginUpdateManager CreateManager(
            PluginConfiguration configuration,
            Func<Version> currentVersionAccessor,
            Action saveConfiguration,
            IPluginReleaseClient releaseClient,
            IInstallationManager installationManager,
            IServerApplicationHost applicationHost = null,
            ILogManager logManager = null)
        {
            var constructor = typeof(PluginUpdateManager).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[]
                {
                    typeof(Func<PluginConfiguration>),
                    typeof(Action),
                    typeof(Func<Version>),
                    typeof(IPluginReleaseClient),
                    typeof(IInstallationManager),
                    typeof(IServerApplicationHost),
                    typeof(ILogManager),
                },
                modifiers: null);
            return (PluginUpdateManager)constructor.Invoke(new object[]
            {
                new Func<PluginConfiguration>(() => configuration),
                saveConfiguration,
                currentVersionAccessor,
                releaseClient,
                installationManager,
                applicationHost,
                logManager,
            });
        }

        private static GitHubReleaseInfo CreateRelease(int major, int minor, int build)
        {
            return new GitHubReleaseInfo
            {
                TagName = "v" + major + "." + minor + "." + build,
                Version = new Version(major, minor, build),
                HtmlUrl = "https://github.com/hope140/EmbyWatchTogether/releases/tag/v" + major + "." + minor + "." + build,
                Assets = new System.Collections.Generic.List<GitHubReleaseAsset>
                {
                    new GitHubReleaseAsset
                    {
                        Name = GitHubReleaseClient.AssetName,
                        BrowserDownloadUrl = "https://github.com/hope140/EmbyWatchTogether/releases/download/v" + major + "." + minor + "." + build + "/Emby.Plugins.WatchTogether.dll",
                        Digest = "sha256:" + new string('0', 64),
                        Size = 1,
                    },
                },
            };
        }

        private sealed class FakeReleaseClient : IPluginReleaseClient
        {
            private readonly GitHubReleaseInfo _release;

            public int CheckCount { get; private set; }

            public FakeReleaseClient(GitHubReleaseInfo release)
            {
                _release = release;
            }

            public Task<VerifiedPluginRelease> CheckForLatestAsync(
                CancellationToken cancellationToken)
            {
                CheckCount++;
                return Task.FromResult(new VerifiedPluginRelease
                {
                    Release = _release,
                    Asset = _release.Assets[0],
                    Md5Checksum = "md5-checksum",
                });
            }
        }

        private sealed class FirstSuccessThenFailureReleaseClient : IPluginReleaseClient
        {
            private readonly VerifiedPluginRelease _firstRelease;

            public FirstSuccessThenFailureReleaseClient(GitHubReleaseInfo release)
            {
                _firstRelease = new VerifiedPluginRelease
                {
                    Release = release,
                    Asset = release.Assets[0],
                    Md5Checksum = "md5-checksum",
                };
            }

            public int CheckCount { get; private set; }

            public Task<VerifiedPluginRelease> CheckForLatestAsync(
                CancellationToken cancellationToken)
            {
                CheckCount++;
                if (CheckCount == 1)
                {
                    return Task.FromResult(_firstRelease);
                }

                throw new InvalidOperationException("second check failed");
            }
        }

    }
}
