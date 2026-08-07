using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using Moq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class GitHubReleaseClientTests
    {
        [Theory]
        [InlineData("1.2.3", 1, 2, 3)]
        [InlineData("v1.2.3.4", 1, 2, 3)]
        [InlineData("V2.0", 2, 0, -1)]
        public void TryParseReleaseVersion_AllowsOptionalVPrefix(
            string tag,
            int major,
            int minor,
            int build)
        {
            Assert.True(GitHubReleaseClient.TryParseReleaseVersion(tag, out var version));
            Assert.Equal(major, version.Major);
            Assert.Equal(minor, version.Minor);
            Assert.Equal(build, version.Build);
        }

        [Theory]
        [InlineData("https://github.com/hope140/EmbyWatchTogether/releases/latest/download/Emby.Plugins.WatchTogether.dll", true)]
        [InlineData("http://github.com/hope140/EmbyWatchTogether/releases/latest/download/Emby.Plugins.WatchTogether.dll", false)]
        [InlineData("https://github.com/other/repo/releases/latest/download/Emby.Plugins.WatchTogether.dll", false)]
        [InlineData("https://example.com/hope140/EmbyWatchTogether/releases/latest/download/Emby.Plugins.WatchTogether.dll", false)]
        [InlineData("https://github.com/hope140/EmbyWatchTogether/releases/latest/download/other.dll", false)]
        public void IsAllowedDownloadUrl_RestrictsHttpsRepositoryPath(string url, bool expected)
        {
            Assert.Equal(expected, GitHubReleaseClient.IsAllowedDownloadUrl(url));
        }

        [Fact]
        public async Task CheckForLatestAsync_ValidatesAssemblyAndCleansTempFile()
        {
            var client = CreateClient(out var httpClient);
            var tempPath = CopyPluginAssembly();
            httpClient.Setup(x => x.GetTempFileResponse(
                    It.Is<HttpRequestOptions>(o => o.Progress != null)))
                .ReturnsAsync(new HttpResponseInfo { StatusCode = HttpStatusCode.OK, TempFilePath = tempPath });

            var verified = await client.CheckForLatestAsync(CancellationToken.None);

            Assert.NotNull(verified);
            Assert.NotNull(verified.Release);
            Assert.NotNull(verified.Asset);
            Assert.Equal(typeof(Plugin).Assembly.GetName().Version, verified.Release.Version);
            Assert.Equal(GitHubReleaseClient.AssetName, verified.Asset.Name);
            Assert.Equal(GitHubReleaseClient.LatestDownloadUrl, verified.Asset.BrowserDownloadUrl);
            Assert.False(string.IsNullOrWhiteSpace(verified.Md5Checksum));
            Assert.False(File.Exists(tempPath));
        }

        [Fact]
        public async Task CheckForLatestAsync_RejectsBadResponseAndCleansTempFile()
        {
            var client = CreateClient(out var httpClient);
            var tempPath = CopyPluginAssembly();
            httpClient.Setup(x => x.GetTempFileResponse(It.IsAny<HttpRequestOptions>()))
                .ReturnsAsync(new HttpResponseInfo { StatusCode = HttpStatusCode.Forbidden, TempFilePath = tempPath });

            await Assert.ThrowsAsync<ReleaseValidationException>(() =>
                client.CheckForLatestAsync(CancellationToken.None));
            Assert.False(File.Exists(tempPath));
        }

        [Fact]
        public async Task CheckForLatestAsync_RejectsNonPluginAssembly()
        {
            var client = CreateClient(out var httpClient);
            var tempPath = Path.Combine(Path.GetTempPath(), "watchtogether-invalid-" + Guid.NewGuid().ToString("N") + ".dll");
            File.WriteAllText(tempPath, "not a real assembly");
            httpClient.Setup(x => x.GetTempFileResponse(It.IsAny<HttpRequestOptions>()))
                .ReturnsAsync(new HttpResponseInfo { StatusCode = HttpStatusCode.OK, TempFilePath = tempPath });

            await Assert.ThrowsAsync<ReleaseValidationException>(() =>
                client.CheckForLatestAsync(CancellationToken.None));
            Assert.False(File.Exists(tempPath));
        }

        private static GitHubReleaseClient CreateClient(out Mock<IHttpClient> httpClient)
        {
            httpClient = new Mock<IHttpClient>();
            return new GitHubReleaseClient(httpClient.Object);
        }

        private static string CopyPluginAssembly()
        {
            var path = Path.Combine(Path.GetTempPath(), "watchtogether-test-" + Guid.NewGuid().ToString("N") + ".dll");
            File.Copy(typeof(Plugin).Assembly.Location, path);
            return path;
        }
    }
}
