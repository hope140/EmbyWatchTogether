using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Updates;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Session;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Model.Updates;
using Moq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    [Collection("Plugin singleton")]
    public class WatchTogetherUpdateTaskTests
    {
        [Fact]
        public void HasSinglePublicConstructor_ForContainerDiscovery()
        {
            // Emby's container requires exactly one public constructor; two
            // would make scheduled-task registration fail at startup.
            var constructors = typeof(WatchTogetherUpdateTask).GetConstructors();

            Assert.Single(constructors);
        }

        [Fact]
        public void DefaultTriggers_ReturnsSingle24HourIntervalTrigger()
        {
            var task = CreateTask();

            var triggers = task.GetDefaultTriggers().ToList();

            var trigger = Assert.Single(triggers);
            Assert.Equal(TaskTriggerInfo.TriggerInterval, trigger.Type);
            Assert.Equal(TimeSpan.FromHours(24).Ticks, trigger.IntervalTicks);
        }

        [Fact]
        public async Task Execute_WhenPluginNotInitialized_CompletesWithoutError()
        {
            var task = CreateTask();
            var progress = new RecordingProgress();

            Assert.Null(Plugin.Instance);
            await task.Execute(CancellationToken.None, progress);

            Assert.Equal(1, progress.Values.Last());
        }

        [Fact]
        public async Task RunCheckAsync_InstallsNewerReleaseOnce()
        {
            var plugin = CreateInitializedPlugin();
            var releaseClient = new FakeReleaseClient(CreateRelease(2, 0, 0));
            var installation = CreateInstallationManager(out var installMock);

            await WatchTogetherUpdateTask.RunCheckAsync(
                plugin,
                releaseClient,
                installation,
                null,
                null,
                CancellationToken.None);

            installMock.Verify(x => x.InstallPackage(
                It.Is<PackageVersionInfo>(p =>
                    p.name == "Watch Together" &&
                    p.guid == Plugin.PluginId.ToString("D") &&
                    p.versionStr == "2.0.0" &&
                    p.classification == PackageVersionClass.Release &&
                    p.targetFilename == GitHubReleaseClient.AssetName &&
                    p.checksum == "md5-checksum"),
                true,
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RunCheckAsync_WhenAlreadyLatest_NotifiesAdminSessions()
        {
            var plugin = CreateInitializedPlugin();
            var releaseClient = new FakeReleaseClient(CreateRelease(1, 0, 0));
            var installation = CreateInstallationManager(out var installMock);
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(x => x.SendMessageToAdminSessions(
                    "GeneralCommand",
                    It.IsAny<GeneralCommand>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            await WatchTogetherUpdateTask.RunCheckAsync(
                plugin,
                releaseClient,
                installation,
                null,
                null,
                CancellationToken.None,
                sessionManager.Object);

            installMock.Verify(x => x.InstallPackage(
                It.IsAny<PackageVersionInfo>(),
                true,
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            sessionManager.Verify(x => x.SendMessageToAdminSessions(
                "GeneralCommand",
                It.Is<GeneralCommand>(command =>
                    command.Name == GeneralCommandType.DisplayMessage.ToString() &&
                    command.Arguments["Header"] == "Watch Together" &&
                    command.Arguments["Text"] == "当前已是最新正式版 v1.0.0.0。" &&
                    command.Arguments["TimeoutMs"] == "3000"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RunCheckAsync_ThrowsWhenCheckReportsError()
        {
            var plugin = CreateInitializedPlugin();
            var releaseClient = new ThrowingReleaseClient("无法读取 GitHub 正式版信息。");
            var installation = CreateInstallationManager(out _);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                WatchTogetherUpdateTask.RunCheckAsync(
                    plugin,
                    releaseClient,
                    installation,
                    null,
                    null,
                    CancellationToken.None));

            Assert.Contains("无法读取 GitHub 正式版信息", exception.Message);
        }

        [Fact]
        public async Task RunCheckAsync_WithCancelledToken_ThrowsOperationCanceledException()
        {
            var plugin = CreateInitializedPlugin();
            var releaseClient = new FakeReleaseClient(CreateRelease(2, 0, 0));
            var installation = CreateInstallationManager(out _);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                WatchTogetherUpdateTask.RunCheckAsync(
                    plugin,
                    releaseClient,
                    installation,
                    null,
                    null,
                    cancellation.Token));
        }

        private static WatchTogetherUpdateTask CreateTask()
        {
            return new WatchTogetherUpdateTask(
                Mock.Of<IHttpClient>(),
                Mock.Of<IJsonSerializer>(),
                Mock.Of<IInstallationManager>(),
                Mock.Of<IServerApplicationHost>(),
                sessionManager: Mock.Of<ISessionManager>());
        }

        private static Plugin CreateInitializedPlugin()
        {
            var paths = new Mock<IApplicationPaths>();
            paths.SetupGet(x => x.PluginConfigurationsPath).Returns("C:\\watch-together-update-task-tests");
            var serializer = new Mock<IXmlSerializer>();
            var plugin = new Plugin(paths.Object, serializer.Object);
            plugin.SetAttributes(
                "C:\\watch-together-update-task-tests\\Emby.Plugins.WatchTogether.dll",
                null,
                new Version(1, 0, 0, 0));
            plugin.SetStartupInfo(_ => { });
            SetPluginInstance(null);
            return plugin;
        }

        private static void SetPluginInstance(Plugin plugin)
        {
            typeof(Plugin)
                .GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .GetSetMethod(true)
                .Invoke(null, new object[] { plugin });
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

        private sealed class ThrowingReleaseClient : IPluginReleaseClient
        {
            private readonly string _message;

            public ThrowingReleaseClient(string message)
            {
                _message = message;
            }

            public Task<VerifiedPluginRelease> CheckForLatestAsync(
                CancellationToken cancellationToken)
            {
                throw new ReleaseValidationException(_message);
            }
        }

        private sealed class RecordingProgress : IProgress<double>
        {
            public List<double> Values { get; } = new List<double>();

            public void Report(double value)
            {
                Values.Add(value);
            }
        }
    }
}
