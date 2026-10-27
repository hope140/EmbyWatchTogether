using System;
using System.Collections.Generic;
using System.IO;
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
        public async Task RunCheckAsync_WhenInitialConfigurationIsNullAttachesAndPersistsPendingState()
        {
            var serializer = new Mock<IXmlSerializer>();
            var plugin = CreateInitializedPlugin(serializer, initializeStorage: true);
            serializer.Setup(x => x.DeserializeFromFile(typeof(PluginConfiguration), It.IsAny<string>()))
                .Returns((object)null);
            Assert.Null(plugin.Configuration);
            var settingsEvents = 0;
            plugin.ConfigurationChanged += (_, __) => settingsEvents++;
            var releaseClient = new FakeReleaseClient(CreateRelease(2, 0, 0));
            var installation = CreateInstallationManager(out var installMock);

            await WatchTogetherUpdateTask.RunCheckAsync(
                plugin, releaseClient, installation, null, null, CancellationToken.None);
            await WatchTogetherUpdateTask.RunCheckAsync(
                plugin, releaseClient, installation, null, null, CancellationToken.None);

            Assert.Equal("2.0.0", plugin.Configuration.PendingUpdateVersion);
            Assert.Equal(0, settingsEvents);
            serializer.Verify(x => x.SerializeToFile(
                It.Is<object>(value => ((PluginConfiguration)value).PendingUpdateVersion == "2.0.0"),
                It.IsAny<string>()), Times.Once);
            installMock.Verify(x => x.InstallPackage(
                It.IsAny<PackageVersionInfo>(), true,
                It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()), Times.Once);
            using (var manager = new PluginUpdateManager(plugin, releaseClient, installation, null))
            {
                Assert.True(manager.GetStatus().RestartRequired);
            }
        }

        [Fact]
        public async Task RunCheckAsync_StaleSettingsSaveBetweenRunsDoesNotReinstallPendingRelease()
        {
            var serializer = new Mock<IXmlSerializer>();
            var savedPendingVersions = new List<string>();
            serializer.Setup(x => x.SerializeToFile(It.IsAny<object>(), It.IsAny<string>()))
                .Callback<object, string>((configuration, _) =>
                    savedPendingVersions.Add(((PluginConfiguration)configuration).PendingUpdateVersion));
            var plugin = CreateInitializedPlugin(serializer, initializeStorage: true);
            var staleSettings = new PluginConfiguration { PollIntervalSeconds = 1.5 };
            var releaseClient = new FakeReleaseClient(CreateRelease(2, 0, 0));
            var installation = CreateInstallationManager(out var installMock);

            await WatchTogetherUpdateTask.RunCheckAsync(
                plugin, releaseClient, installation, null, null, CancellationToken.None);
            plugin.UpdateConfiguration(staleSettings);
            await WatchTogetherUpdateTask.RunCheckAsync(
                plugin, releaseClient, installation, null, null, CancellationToken.None);

            installMock.Verify(x => x.InstallPackage(
                It.IsAny<PackageVersionInfo>(), true,
                It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.Equal("2.0.0", plugin.Configuration.PendingUpdateVersion);
            Assert.Equal(1.5, plugin.Configuration.PollIntervalSeconds);
            Assert.Equal(new[] { "2.0.0", "2.0.0" }, savedPendingVersions);
            using (var manager = new PluginUpdateManager(plugin, releaseClient, installation, null))
            {
                Assert.True(manager.GetStatus().RestartRequired);
                Assert.Equal("2.0.0", manager.GetStatus().PendingVersion);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RunCheckAsync_PendingWriteAndSettingsSaveShareAtomicConfigurationBoundary(bool settingsFirst)
        {
            using var firstSaveEntered = new ManualResetEventSlim();
            using var releaseFirstSave = new ManualResetEventSlim();
            using var secondStarted = new ManualResetEventSlim();
            using var secondCompleted = new ManualResetEventSlim();
            var serializer = new Mock<IXmlSerializer>();
            var saveCount = 0;
            serializer.Setup(x => x.SerializeToFile(It.IsAny<object>(), It.IsAny<string>()))
                .Callback<object, string>((_, __) =>
                {
                    if (Interlocked.Increment(ref saveCount) == 1)
                    {
                        firstSaveEntered.Set();
                        if (!releaseFirstSave.Wait(TimeSpan.FromSeconds(10)))
                        {
                            throw new TimeoutException("The test did not release the first configuration save.");
                        }
                    }
                });
            var plugin = CreateInitializedPlugin(serializer, initializeStorage: true);
            // Materialize the original configuration before either writer starts.
            Assert.Null(plugin.Configuration.PendingUpdateVersion);
            var releaseClient = new FakeReleaseClient(CreateRelease(2, 0, 0));
            var installation = CreateInstallationManager(out var installMock);
            Action install = () => WatchTogetherUpdateTask.RunCheckAsync(
                plugin, releaseClient, installation, null, null, CancellationToken.None).GetAwaiter().GetResult();
            Action saveSettings = () => plugin.UpdateConfiguration(
                new PluginConfiguration { PollIntervalSeconds = 1.5 });
            Exception firstError = null;
            Exception secondError = null;
            var first = new Thread(() =>
            {
                try { (settingsFirst ? saveSettings : install)(); }
                catch (Exception ex) { firstError = ex; }
            });
            var second = new Thread(() =>
            {
                secondStarted.Set();
                try { (settingsFirst ? install : saveSettings)(); }
                catch (Exception ex) { secondError = ex; }
                finally { secondCompleted.Set(); }
            });

            try
            {
                first.Start();
                Assert.True(firstSaveEntered.Wait(TimeSpan.FromSeconds(5)));
                second.Start();
                Assert.True(secondStarted.Wait(TimeSpan.FromSeconds(5)));
                Assert.True(SpinWait.SpinUntil(
                    () => (second.ThreadState & ThreadState.WaitSleepJoin) != 0 || secondCompleted.IsSet,
                    TimeSpan.FromSeconds(5)));
                Assert.False(secondCompleted.IsSet);
                // The blocked writer cannot replace or mutate the current
                // configuration until the first writer finishes persistence.
                Assert.Equal(settingsFirst ? null : "2.0.0", plugin.Configuration.PendingUpdateVersion);
            }
            finally
            {
                releaseFirstSave.Set();
                var firstJoined = (first.ThreadState & ThreadState.Unstarted) != 0 ||
                    first.Join(TimeSpan.FromSeconds(10));
                var secondJoined = (second.ThreadState & ThreadState.Unstarted) != 0 ||
                    second.Join(TimeSpan.FromSeconds(10));
                Assert.True(firstJoined);
                Assert.True(secondJoined);
            }

            Assert.Null(firstError);
            Assert.Null(secondError);
            Assert.Equal("2.0.0", plugin.Configuration.PendingUpdateVersion);
            Assert.Equal(1.5, plugin.Configuration.PollIntervalSeconds);
            Assert.Equal(2, saveCount);
            installMock.Verify(x => x.InstallPackage(
                It.IsAny<PackageVersionInfo>(), true,
                It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

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
        public async Task Execute_BetaReadsTokenFromPluginStoreAndUsesItForApiCheck()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "watch-together-update-task-token-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var paths = new Mock<IApplicationPaths>();
                paths.SetupGet(x => x.PluginConfigurationsPath).Returns(root);
                var plugin = new Plugin(paths.Object, Mock.Of<IXmlSerializer>());
                plugin.SetAttributes(
                    Path.Combine(root, "Emby.Plugins.WatchTogether.dll"),
                    root,
                    new Version(1, 0, 0, 0));
                plugin.SetStartupInfo(_ => { });
                plugin.UpdateConfiguration(new PluginConfiguration
                {
                    UpdateChannel = PluginConfiguration.BetaUpdateChannel,
                });
                plugin.GitHubTokens.SetToken("ghp_mock_token");
                SetPluginInstance(plugin);

                HttpRequestOptions capturedRequest = null;
                var httpClient = new Mock<IHttpClient>();
                httpClient.Setup(x => x.GetResponse(It.IsAny<HttpRequestOptions>()))
                    .Returns((HttpRequestOptions request) =>
                    {
                        capturedRequest = request;
                        return Task.FromResult(new HttpResponseInfo
                        {
                            StatusCode = System.Net.HttpStatusCode.Unauthorized,
                        });
                    });
                var task = new WatchTogetherUpdateTask(
                    httpClient.Object,
                    Mock.Of<IJsonSerializer>(),
                    Mock.Of<IInstallationManager>(),
                    Mock.Of<IServerApplicationHost>());

                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    task.Execute(CancellationToken.None, null));

                Assert.Contains("GitHub Token 无效", exception.Message);
                Assert.NotNull(capturedRequest);
                Assert.Equal("Bearer ghp_mock_token", capturedRequest.RequestHeaders["Authorization"]);
                Assert.False(capturedRequest.LogRequest);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }

                SetPluginInstance(null);
            }
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
        public async Task RunCheckAsync_WhenInstallFails_NotifiesAndFailsTask()
        {
            var plugin = CreateInitializedPlugin();
            var releaseClient = new FakeReleaseClient(CreateRelease(2, 0, 0));
            var installation = new Mock<IInstallationManager>();
            installation.Setup(x => x.InstallPackage(
                    It.IsAny<PackageVersionInfo>(),
                    true,
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("install details"));
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(x => x.SendMessageToAdminSessions(
                    "GeneralCommand",
                    It.IsAny<GeneralCommand>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                WatchTogetherUpdateTask.RunCheckAsync(
                    plugin,
                    releaseClient,
                    installation.Object,
                    null,
                    null,
                    CancellationToken.None,
                    sessionManager.Object));

            Assert.Equal("安装更新失败，请稍后重试。", exception.Message);
            sessionManager.Verify(x => x.SendMessageToAdminSessions(
                "GeneralCommand",
                It.Is<GeneralCommand>(command =>
                    command.Name == GeneralCommandType.DisplayMessage.ToString() &&
                    command.Arguments["Header"] == "Watch Together" &&
                    command.Arguments["Text"] == "安装更新失败，请稍后重试。" &&
                    command.Arguments["TimeoutMs"] == "3000"),
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

        private static Plugin CreateInitializedPlugin(Mock<IXmlSerializer> serializer = null, bool initializeStorage = false)
        {
            var paths = new Mock<IApplicationPaths>();
            paths.SetupGet(x => x.PluginConfigurationsPath).Returns("C:\\watch-together-update-task-tests");
            serializer = serializer ?? new Mock<IXmlSerializer>();
            serializer.Setup(x => x.DeserializeFromFile(typeof(PluginConfiguration), It.IsAny<string>()))
                .Returns(new PluginConfiguration());
            var plugin = new Plugin(paths.Object, serializer.Object);
            plugin.SetAttributes(
                "C:\\watch-together-update-task-tests\\Emby.Plugins.WatchTogether.dll",
                initializeStorage ? "C:\\watch-together-update-task-tests\\data" : null,
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
