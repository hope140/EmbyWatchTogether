using System;
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
            var configuration = new PluginConfiguration { AutoUpdateEnabled = true };
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
        public async Task DisablingAutomaticUpdatesBeforeDownloadSkipsQueuedInstall()
        {
            var configuration = new PluginConfiguration { AutoUpdateEnabled = true };
            var release = CreateRelease(2, 0, 0);
            var releaseClient = new BlockingReleaseClient(release);
            var installation = CreateInstallationManager(out var installMock);
            using (var manager = new PluginUpdateManager(configuration, new Version(1, 0, 0), releaseClient, installation))
            {
                var check = manager.CheckForUpdatesAsync(true);
                await releaseClient.MetadataStarted.Task;
                configuration.AutoUpdateEnabled = false;
                manager.NotifyConfigurationChanged();
                releaseClient.ReleaseMetadata.TrySetResult(release);

                await check;
                installMock.Verify(x => x.InstallPackage(
                    It.IsAny<PackageVersionInfo>(),
                    true,
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()), Times.Never);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(721)]
        public void PluginRejectsInvalidUpdateIntervals(int interval)
        {
#pragma warning disable SYSLIB0050 // Formatter-based serialization is obsolete; this deliberately bypasses the constructor.
            var plugin = (Plugin)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Plugin));
#pragma warning restore SYSLIB0050
            Assert.Throws<ArgumentOutOfRangeException>(() => plugin.UpdateConfiguration(new PluginConfiguration
            {
                UpdateCheckIntervalHours = interval,
            }));
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

            public Task<GitHubReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(_release);
            }

            public Task<VerifiedPluginRelease> DownloadAndVerifyAsync(
                GitHubReleaseInfo release,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new VerifiedPluginRelease
                {
                    Release = release,
                    Asset = release.Assets[0],
                    Md5Checksum = "md5-checksum",
                });
            }
        }

        private sealed class BlockingReleaseClient : IPluginReleaseClient
        {
            private readonly GitHubReleaseInfo _release;

            public BlockingReleaseClient(GitHubReleaseInfo release)
            {
                _release = release;
            }

            public TaskCompletionSource<bool> MetadataStarted { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<GitHubReleaseInfo> ReleaseMetadata { get; } =
                new TaskCompletionSource<GitHubReleaseInfo>(TaskCreationOptions.RunContinuationsAsynchronously);

            public async Task<GitHubReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken)
            {
                MetadataStarted.TrySetResult(true);
                return await ReleaseMetadata.Task.ConfigureAwait(false);
            }

            public Task<VerifiedPluginRelease> DownloadAndVerifyAsync(
                GitHubReleaseInfo release,
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
