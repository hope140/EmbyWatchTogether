using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Updates;
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

            public FakeReleaseClient(GitHubReleaseInfo release)
            {
                _release = release;
            }

            public Task<VerifiedPluginRelease> CheckForLatestAsync(
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new VerifiedPluginRelease
                {
                    Release = _release,
                    Asset = _release.Assets[0],
                    Md5Checksum = "md5-checksum",
                });
            }
        }

    }
}
